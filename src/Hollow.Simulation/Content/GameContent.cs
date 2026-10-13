using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation.Nature;
using BuildingData = Hollow.Simulation.Building.BuildingData;

namespace Hollow.Simulation.Content;

/// <summary>The content files have problems; the message lists every one.</summary>
public sealed class ContentException(string message) : Exception(message);

/// <summary>
/// The ground game's numbers (GROUND.md §7, ARCHITECTURE.md P8): items, recipes, natural objects, deployables, loot,
/// survival constants and crafting tabs, read from <c>game/data/ground/*.json</c>. The server and every client load
/// the same files; <see cref="Hash"/> is checked at connect, so both run the same rules. Read once at start, then
/// read-only (safe to share between threads).
/// </summary>
public sealed partial class GameContent
{
    /// <summary>The files, in the order their text goes into <see cref="Hash"/>.</summary>
    public static readonly string[] Files =
    {
        "items.json", "recipes.json", "gather.json", "deployables.json", "loot.json", "survival.json", "categories.json",
        "weapons.json",
        "building.json", "explosives.json",
        "ships.json", "world.json", "animals.json",
        "surfaces.json",
        "capabilities.json",
        "placement.json",
    };

    /// <summary>Files read with the rest but not part of <see cref="Hash"/>: the player's lines (a translation may differ).</summary>
    public static readonly string[] Unhashed = { Content.Texts.File };

    private readonly ItemDef?[] _byId;
    private readonly Dictionary<string, ItemDef> _byKey;
    private readonly Dictionary<ushort, Recipe> _recipes;
    private readonly Dictionary<string, Recipe> _recipesByKey;
    private readonly Dictionary<string, NaturalKind> _natural;
    private readonly Dictionary<string, DeployableDef> _deployables;
    private readonly Dictionary<string, LootTable> _loot;
    private readonly Dictionary<string, Ships.ShipDef> _ships;

    private GameContent(Loaded loaded)
    {
        Items = loaded.Items;
        Recipes = loaded.Recipes;
        NaturalKinds = loaded.Natural;
        Deployables = loaded.Deployables;
        LootTables = loaded.Loot;
        Categories = loaded.Categories;
        Firearms = loaded.Firearms;
        Bullets = loaded.Bullets;
        Attachments = loaded.Attachments;
        Ships = loaded.Ships;
        Blasts = loaded.Blasts;
        Throwables = loaded.Throwables;
        RaidAlerts = loaded.RaidAlerts;
        World = loaded.World ?? new WorldData();
        Animals = loaded.Animals;
        AnimalSenses = loaded.AnimalSenses;
        AnimalSpawning = loaded.AnimalSpawning;
        HarvestRates = loaded.HarvestRates;
        Survival = loaded.Survival;
        Hash = loaded.Hash;
        Problems = loaded.Problems;
        Texts = loaded.Texts!;
        Capabilities = loaded.Capabilities ?? new CapabilityData(Array.Empty<string>());
        Placement = loaded.Placement ?? Simulation.Placement.PlacementData.Empty;
        Building = loaded.Building ?? BuildingData.Parse("{}");
        ClaimDeployable = loaded.ClaimDeployable ?? new DeployableDef { Key = "claim-device", Item = new ItemDef { Key = "claim-device" }, Health = 200, Slots = 24 };
        _byId = new ItemDef?[ushort.MaxValue + 1];
        _byKey = new Dictionary<string, ItemDef>(StringComparer.Ordinal);
        foreach (ItemDef item in Items)
        {
            _byId[item.Id] ??= item;
            _byKey.TryAdd(item.Key, item);
        }
        _recipes = new Dictionary<ushort, Recipe>();
        _recipesByKey = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        foreach (Recipe recipe in Recipes)
        {
            _recipes.TryAdd(recipe.Id, recipe);
            _recipesByKey.TryAdd(recipe.Key, recipe);
        }
        _natural = NaturalKinds.GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _deployables = Deployables.GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _loot = LootTables.GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _ships = Ships.GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public IReadOnlyList<ItemDef> Items { get; }

    public IReadOnlyList<Recipe> Recipes { get; }

    public IReadOnlyList<NaturalKind> NaturalKinds { get; }

    public IReadOnlyList<DeployableDef> Deployables { get; }

    public IReadOnlyList<LootTable> LootTables { get; }

    public IReadOnlyList<Category> Categories { get; }

    /// <summary>The firearms, kinds of rounds and attachments (weapons.json, G4); each also hangs off its item.</summary>
    public IReadOnlyList<FirearmStats> Firearms { get; }

    public IReadOnlyList<BulletStats> Bullets { get; }

    public IReadOnlyList<AttachmentStats> Attachments { get; }

    /// <summary>The kinds of ship (ships.json, G6); each also hangs off its item.</summary>
    public IReadOnlyList<Ships.ShipDef> Ships { get; }
    /// <summary>G5 (explosives.json): every blast, the explosives thrown or placed by hand, and how raid alerts go out.</summary>
    public IReadOnlyList<BlastStats> Blasts { get; }

    public IReadOnlyList<ThrowableStats> Throwables { get; }

    public RaidAlertSettings RaidAlerts { get; }

    /// <summary>G7 (world.json): salvage, world props and their loot, the recycler, research, tech trees, teams and the map.</summary>
    public WorldData World { get; }

    /// <summary>A world prop by its number (what clients see a thing of kind Prop as), or null.</summary>
    public PropDef? Prop(int id) => World.Prop(id);

    /// <summary>G7 (animals.json, GROUND.md §6.9): the species, how noises carry to them, where they live, how tools harvest them.</summary>
    public IReadOnlyList<AnimalSpecies> Animals { get; }

    public AnimalSenses AnimalSenses { get; }

    public AnimalSpawning AnimalSpawning { get; }

    public HarvestRates HarvestRates { get; }

    /// <summary>A species by its number (from outside: untrusted, null when unknown) or key.</summary>
    public AnimalSpecies? Animal(int id)
    {
        foreach (AnimalSpecies s in Animals)
        {
            if (s.Id == id)
            {
                return s;
            }
        }
        return null;
    }

    public AnimalSpecies? Animal(string key)
    {
        foreach (AnimalSpecies s in Animals)
        {
            if (s.Key == key)
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>A blast by key, or null.</summary>
    public BlastStats? Blast(string key)
    {
        foreach (BlastStats b in Blasts)
        {
            if (b.Key == key)
            {
                return b;
            }
        }
        return null;
    }

    public SurvivalTuning Survival { get; }

    /// <summary>A hash of every file's text (line ends normalised): the server and client must have the same.</summary>
    public ulong Hash { get; }

    /// <summary>The building rules' numbers (building.json, GROUND.md §6.3).</summary>
    public BuildingData Building { get; }

    /// <summary>The claim device as the zone keeps it: a deployable whose storage is its upkeep store (never placed on the ground).</summary>
    public DeployableDef ClaimDeployable { get; }

    /// <summary>The player's lines for every answer (texts.json, GROUND_SYSTEMS.md §3.2).</summary>
    public Texts Texts { get; }

    /// <summary>The kinds of behaviour world things have, and which have which (capabilities.json, GROUND_SYSTEMS.md §2.1).</summary>
    public CapabilityData Capabilities { get; }

    /// <summary>Everything that can be placed and its rule, by the wire's numbers, and the pipeline's handlers (placement.json, GROUND_SYSTEMS.md §6).</summary>
    public Simulation.Placement.PlacementData Placement { get; }

    /// <summary>Everything wrong with the files: empty when they are good. <see cref="Load"/> refuses any.</summary>
    public IReadOnlyList<string> Problems { get; }

    // ---------------------------------------------------------------- lookups (IDs from outside are untrusted: null when unknown)

    public ItemDef? Item(int id) => id is > 0 and <= ushort.MaxValue ? _byId[id] : null;

    public ItemDef? Item(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>An item that must exist (a key written in code).</summary>
    public ItemDef ItemByKey(string key) => Item(key) ?? throw new KeyNotFoundException($"No item \"{key}\" in items.json.");

    /// <summary>
    /// A recipe by its number: the recipe making an item has the item's ID; a second way to make it has a number of its
    /// own (G7, <see cref="Content.Recipe.Id"/>).
    /// </summary>
    public Recipe? Recipe(int recipeId) => AsId(recipeId) is { } k && _recipes.TryGetValue(k, out Recipe? r) ? r : null;

    /// <summary>A recipe by its key (an output's key, or a second way's own key).</summary>
    public Recipe? RecipeByKey(string key) => _recipesByKey.GetValueOrDefault(key);

    public NaturalKind? Natural(int index) => index >= 0 && index < NaturalKinds.Count ? NaturalKinds[index] : null;

    public NaturalKind? Natural(string key) => _natural.GetValueOrDefault(key);

    public DeployableDef? Deployable(int itemId) =>
        Item(itemId) is { } item ? item.Deploys ?? (item == ClaimDeployable.Item ? ClaimDeployable : null) : null;

    public DeployableDef? Deployable(string key) => _deployables.GetValueOrDefault(key);

    public LootTable? Loot(string key) => _loot.GetValueOrDefault(key);

    public Ships.ShipDef? Ship(string key) => _ships.GetValueOrDefault(key);

    /// <summary>The ship kind an item ID names (untrusted: null when it isn't one).</summary>
    public Ships.ShipDef? Ship(int itemId) => Item(itemId)?.Ship;

    private static ushort? AsId(int value) => value is > 0 and <= ushort.MaxValue ? (ushort)value : null;

    // ---------------------------------------------------------------- loading

    /// <summary>Reads the files in a folder; throws <see cref="ContentException"/> listing every problem.</summary>
    public static GameContent Load(string folder)
    {
        GameContent content = Read(folder);
        if (content.Problems.Count > 0)
        {
            throw new ContentException($"{content.Problems.Count} problem(s) in {folder}:\n  " + string.Join("\n  ", content.Problems));
        }
        return content;
    }

    /// <summary>Reads the files in a folder and keeps whatever problems they have (for checks and tests).</summary>
    public static GameContent Read(string folder)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Files.Concat(Unhashed))
        {
            string path = Path.Combine(folder, file);
            texts[file] = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        return FromTexts(texts);
    }

    /// <summary>Content from the files' texts (a missing file is a problem, never a different game).</summary>
    public static GameContent FromTexts(IReadOnlyDictionary<string, string> texts)
    {
        var problems = new List<string>();
        var loaded = new Loaded { Problems = problems };
        var documents = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        ulong hash = Hash64.Of(0x5EED_C0DE);
        foreach (string file in Files)
        {
            string text = texts.GetValueOrDefault(file) ?? "";
            hash = Hash64.Combine(hash, Hash64.Of(text.Replace("\r\n", "\n", StringComparison.Ordinal)));
            if (text.Length == 0)
            {
                problems.Add($"{file}: missing or empty");
                documents[file] = default;
                continue;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                documents[file] = document.RootElement.Clone();
            }
            catch (JsonException e)
            {
                problems.Add($"{file}: not valid JSON ({e.Message})");
                documents[file] = default;
            }
        }
        loaded.Hash = hash;
        var reader = new ContentReader(new JsonFields(problems), documents, loaded);
        reader.ReadAll();
        loaded.Texts = ReadTexts(texts.GetValueOrDefault(Content.Texts.File) ?? "", loaded, problems);
        LinkBuilding(texts.GetValueOrDefault("building.json") ?? "", documents["building.json"], loaded, problems);
        PlacementReader.Read(new JsonFields(problems), documents, loaded);
        Validation.Check(loaded, problems);
        ExplosivesReader.Check(loaded, loaded.Building, problems);
        WorldReader.Check(loaded, problems);
        AnimalsReader.Check(loaded, problems);
        return new GameContent(loaded);
    }

    // The player's lines (texts.json): read like the rest, outside the hash.
    private static Texts ReadTexts(string text, Loaded loaded, List<string> problems)
    {
        var names = new Dictionary<int, string>();
        foreach (ItemDef item in loaded.Items)
        {
            names.TryAdd(item.Id, item.Name);
        }
        string? NameOf(int id) => names.GetValueOrDefault(id);
        if (text.Length == 0)
        {
            problems.Add($"{Content.Texts.File}: missing or empty");
            return Content.Texts.Read(new JsonFields(problems), default, NameOf);
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return Content.Texts.Read(new JsonFields(problems), document.RootElement.Clone(), NameOf);
        }
        catch (JsonException e)
        {
            problems.Add($"{Content.Texts.File}: not valid JSON ({e.Message})");
            return Content.Texts.Read(new JsonFields(problems), default, NameOf);
        }
    }

    // building.json (GROUND.md §6.3) has its own reader; its items are linked here: a door item to its door, and the
    // building's own item keys (claim device, locks, key, materials) to items that say what they are.
    private static void LinkBuilding(string text, JsonElement document, Loaded loaded, List<string> problems)
    {
        BuildingData data;
        try
        {
            data = BuildingData.Parse(document.ValueKind == JsonValueKind.Object ? text : "{}");
        }
        catch (JsonException)
        {
            data = BuildingData.Parse("{}");
        }
        if (document.ValueKind == JsonValueKind.Object)
        {
            problems.AddRange(data.Problems.Select(p => "building.json: " + p));
        }
        loaded.Building = data;
        var byKey = new Dictionary<string, ItemDef>(StringComparer.Ordinal);
        foreach (ItemDef item in loaded.Items)
        {
            byKey.TryAdd(item.Key, item);
        }
        foreach (ItemDef item in loaded.Items)
        {
            string where = $"items.json: {JsonFields.Quote(item.Key)}";
            if (item.Building == BuildingRole.Door)
            {
                item.Door = item.DoorKey != null ? data.Door(item.DoorKey) : null;
                if (item.Door == null)
                {
                    problems.Add($"{where}.door: no door \"{item.DoorKey}\" in building.json");
                }
            }
            else if (item.DoorKey != null)
            {
                problems.Add($"{where}.door: only a door item names a door");
            }
        }
        void Expect(string key, BuildingRole role, string what)
        {
            if (!byKey.TryGetValue(key, out ItemDef? item) || item.Building != role)
            {
                problems.Add($"building.json: {what} \"{key}\" must be an item with \"building\": \"{role}\"");
            }
        }
        Expect(data.ClaimItem, BuildingRole.Claim, "the claim device");
        Expect(data.KeyLockItem, BuildingRole.KeyLock, "the key lock");
        Expect(data.CodeLockItem, BuildingRole.CodeLock, "the code lock");
        Expect(data.KeyItem, BuildingRole.Key, "the key");
        foreach (string material in data.Materials)
        {
            if (byKey.TryGetValue(material, out ItemDef? item))
            {
                data.LinkMaterial(material, item.Id);
            }
            else
            {
                problems.Add($"building.json: material \"{material}\" is not an item");
            }
        }
        if (byKey.TryGetValue(data.ClaimItem, out ItemDef? claim))
        {
            loaded.ClaimDeployable = new DeployableDef
            {
                Key = data.ClaimItem,
                Item = claim,
                Health = (float)data.ClaimHealth,
                HalfExtents = data.ClaimSize / 2,
                // The building's own body blocks; the device is placed by the building rules, never on the ground.
                Solid = false,
                Slots = data.ClaimSlots,
            };
        }
    }

    /// <summary>
    /// The data folder: <c>HOLLOW_GROUND_DATA</c> if set, else <c>game/data/ground</c> found walking up from the
    /// program (a checkout), else <c>data/ground</c> beside it. Null if none has the files.
    /// </summary>
    public static string? FindFolder()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("HOLLOW_GROUND_DATA");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(Path.Combine(fromEnvironment, Files[0])))
        {
            return fromEnvironment;
        }
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            foreach (string candidate in new[] { Path.Combine(dir, "game", "data", "ground"), Path.Combine(dir, "data", "ground") })
            {
                if (File.Exists(Path.Combine(candidate, Files[0])))
                {
                    return candidate;
                }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>The data folder found by <see cref="FindFolder"/>, loaded; throws if there is none.</summary>
    public static GameContent LoadDefault() =>
        Load(FindFolder() ?? throw new ContentException("game/data/ground not found (set HOLLOW_GROUND_DATA to the folder)."));

    internal sealed class Loaded
    {
        public List<ItemDef> Items = new();
        public List<Recipe> Recipes = new();
        public List<NaturalKind> Natural = new();
        public List<DeployableDef> Deployables = new();
        public List<LootTable> Loot = new();
        public List<Category> Categories = new();
        public List<FirearmStats> Firearms = new();
        public List<BulletStats> Bullets = new();
        public List<AttachmentStats> Attachments = new();
        public List<Ships.ShipDef> Ships = new();
        public List<BlastStats> Blasts = new();
        public List<ThrowableStats> Throwables = new();
        public RaidAlertSettings RaidAlerts = new();
        public WorldData? World;
        public List<AnimalSpecies> Animals = new();
        public AnimalSenses AnimalSenses = new();
        public AnimalSpawning AnimalSpawning = new();
        public HarvestRates HarvestRates = new();
        public SurvivalTuning Survival = new();
        public BuildingData? Building;
        public DeployableDef? ClaimDeployable;
        public Texts? Texts;
        public CapabilityData? Capabilities;
        public Simulation.Placement.PlacementData? Placement;
        public ulong Hash;
        public List<string> Problems = new();
    }

    // ---------------------------------------------------------------- reading each file

    private sealed class ContentReader(JsonFields f, Dictionary<string, JsonElement> files, Loaded loaded)
    {
        private readonly Dictionary<string, ItemDef> _items = new(StringComparer.Ordinal);

        public void ReadAll()
        {
            ReadCapabilities();
            ReadCategories();
            ReadItems();
            ReadRecipes();
            ReadGather();
            ReadDeployables();
            ReadLoot();
            ReadSurvival();
            ReadWeapons();
            ExplosivesReader.Read(f, files[ExplosivesReader.File], loaded, Resolve);
            ReadShips();
            WorldReader.Read(f, files[WorldReader.File], loaded, Resolve);
            AnimalsReader.Read(f, files[AnimalsReader.File], loaded, Resolve);
        }

        private JsonElement Root(string file, params string[] fields)
        {
            JsonElement root = files[file];
            if (root.ValueKind == JsonValueKind.Object)
            {
                f.OnlyKnown(root, file, fields.Append("source").Append("note").ToArray());
            }
            return root;
        }

        private ItemDef? Resolve(string key, string where)
        {
            if (_items.TryGetValue(key, out ItemDef? item))
            {
                return item;
            }
            f.Problem(where, $"no item \"{key}\"");
            return null;
        }

        // capabilities.json: the capabilities' keys, in their append-only order (at most 64: a thing's set is one word).
        private void ReadCapabilities()
        {
            const string file = CapabilityData.File;
            JsonElement root = Root(file, "capabilities");
            var keys = new List<string>();
            foreach ((JsonElement e, int i) in f.List(root, "capabilities", file).Select((e, i) => (e, i)))
            {
                string where = $"{file}: capabilities[{i}]";
                f.OnlyKnown(e, where, "key", "note");
                string key = f.Text(e, "key", where);
                if (keys.Contains(key))
                {
                    f.Problem(where, $"\"{key}\" is in the list twice");
                }
                keys.Add(key);
            }
            if (keys.Count > Hollow.Simulation.Capabilities.CapabilitySet.Most)
            {
                f.Problem(file, $"at most {Hollow.Simulation.Capabilities.CapabilitySet.Most} capabilities");
            }
            loaded.Capabilities = new CapabilityData(keys);
        }

        private void ReadCategories()
        {
            JsonElement root = Root("categories.json", "categories");
            foreach ((JsonElement e, int i) in f.List(root, "categories", "categories.json").Select((e, i) => (e, i)))
            {
                string where = $"categories.json: categories[{i}]";
                f.OnlyKnown(e, where, "key", "name");
                loaded.Categories.Add(new Category(f.Text(e, "key", where), f.Text(e, "name", where)));
            }
        }

        private void ReadItems()
        {
            JsonElement root = Root("items.json", "items");
            JsonElement[] list = f.List(root, "items", "items.json");
            // Keys first, so references between items (fuel byproducts, cooking) resolve in any order.
            var drafts = new List<(ItemDef Item, JsonElement Element, string Where)>();
            for (int i = 0; i < list.Length; i++)
            {
                JsonElement e = list[i];
                string key = f.Text(e, "key", $"items.json: items[{i}]");
                string where = $"items.json: {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "id", "key", "name", "description", "category", "stack", "condition", "melee", "bow", "ammo",
                    "consume", "wear", "fuel", "cook", "deploys", "building", "door", "ship");
                int id = f.Integer(e, "id", where);
                if (id is < 1 or > ushort.MaxValue)
                {
                    f.Problem(where, $"id {id} must be 1 … 65535");
                }
                var item = new ItemDef
                {
                    Id = (ushort)Math.Clamp(id, 0, ushort.MaxValue),
                    Key = key,
                    Name = f.Text(e, "name", where),
                    Description = f.Text(e, "description", where, ""),
                    Category = f.Text(e, "category", where),
                    StackSize = f.Integer(e, "stack", where, 1),
                    MaxCondition = f.Integer(e, "condition", where, 0),
                    Melee = f.TryChild(e, "melee", out JsonElement melee) ? Melee(melee, where + ".melee") : null,
                    Bow = f.TryChild(e, "bow", out JsonElement bow) ? Bow(bow, where + ".bow") : null,
                    Ammo = f.TryChild(e, "ammo", out JsonElement ammo) ? Ammo(ammo, where + ".ammo") : null,
                    Consume = f.TryChild(e, "consume", out JsonElement consume) ? Consume(consume, where + ".consume") : null,
                    Wear = f.TryChild(e, "wear", out JsonElement wear) ? Wear(wear, where + ".wear") : null,
                    Fuel = f.TryChild(e, "fuel", out JsonElement fuel) ? Fuel(fuel, where + ".fuel") : null,
                    Cook = f.TryChild(e, "cook", out JsonElement cook) ? Cook(cook, where + ".cook") : null,
                    DeploysKey = f.Has(e, "deploys") ? f.Text(e, "deploys", where) : null,
                    Building = f.Has(e, "building") ? Role(f.Text(e, "building", where), where) : BuildingRole.None,
                    DoorKey = f.Has(e, "door") ? f.Text(e, "door", where) : null,
                    ShipKey = f.Has(e, "ship") ? f.Text(e, "ship", where) : null,
                };
                if (!_items.TryAdd(key, item))
                {
                    f.Problem(where, "the key is used twice");
                }
                drafts.Add((item, e, where));
                loaded.Items.Add(item);
            }
            foreach ((ItemDef item, JsonElement _, string where) in drafts)
            {
                if (item.Fuel?.ByproductKey is { } byproduct)
                {
                    item.Fuel.Byproduct = Resolve(byproduct, where + ".fuel.byproduct");
                }
                if (item.Cook != null)
                {
                    item.Cook.Into = Resolve(item.Cook.IntoKey, where + ".cook.into");
                }
                if (item.Bow != null)
                {
                    item.Bow.Ammo = item.Bow.AmmoKeys.Select(k => Resolve(k, where + ".bow.ammo")).OfType<ItemDef>().ToArray();
                }
            }
        }

        private BuildingRole Role(string text, string where)
        {
            switch (text)
            {
                case "plan": return BuildingRole.Plan;
                case "hammer": return BuildingRole.Hammer;
                case "claim": return BuildingRole.Claim;
                case "key-lock": return BuildingRole.KeyLock;
                case "code-lock": return BuildingRole.CodeLock;
                case "key": return BuildingRole.Key;
                case "door": return BuildingRole.Door;
            }
            f.Problem(where + ".building", $"\"{text}\" is not plan, hammer, claim, key-lock, code-lock, key or door");
            return BuildingRole.None;
        }

        private MeleeStats Melee(JsonElement e, string where)
        {
            f.OnlyKnown(e, where, "damage", "reach", "swing", "strike", "tree", "ore", "structure", "bleed");
            return new MeleeStats
            {
                Damage = f.Number(e, "damage", where),
                Reach = f.Number(e, "reach", where),
                SwingSeconds = f.Number(e, "swing", where),
                StrikeSeconds = f.Number(e, "strike", where),
                Tree = f.Number(e, "tree", where, 0),
                Ore = f.Number(e, "ore", where, 0),
                Structure = f.Number(e, "structure", where, 0),
                Bleed = f.Number(e, "bleed", where, 0),
            };
        }

        private BowStats Bow(JsonElement e, string where)
        {
            f.OnlyKnown(e, where, "ammo", "draw", "speed", "damage");
            return new BowStats
            {
                AmmoKeys = f.Texts(e, "ammo", where),
                DrawSeconds = f.Number(e, "draw", where),
                SpeedScale = f.Number(e, "speed", where, 1),
                DamageScale = f.Number(e, "damage", where, 1),
            };
        }

        private AmmoStats Ammo(JsonElement e, string where)
        {
            f.OnlyKnown(e, where, "damage", "speed", "gravity", "bleed", "structure", "recover", "lifetime");
            return new AmmoStats
            {
                Damage = f.Number(e, "damage", where),
                Speed = f.Number(e, "speed", where),
                Gravity = f.Number(e, "gravity", where, 1),
                Bleed = f.Number(e, "bleed", where, 0),
                Structure = f.Number(e, "structure", where, 0),
                Recover = f.Number(e, "recover", where, 0),
                LifetimeSeconds = f.Number(e, "lifetime", where, 4),
            };
        }

        private ConsumeStats Consume(JsonElement e, string where)
        {
            f.OnlyKnown(e, where, "calories", "hydration", "health", "heal", "stopsBleeding", "poison");
            return new ConsumeStats
            {
                Calories = f.Number(e, "calories", where, 0),
                Hydration = f.Number(e, "hydration", where, 0),
                Health = f.Number(e, "health", where, 0),
                Heal = f.Number(e, "heal", where, 0),
                StopsBleeding = f.Flag(e, "stopsBleeding", where, false),
                Poison = f.Number(e, "poison", where, 0),
            };
        }

        private WearStats Wear(JsonElement e, string where)
        {
            f.OnlyKnown(e, where, "layers", "covers", "melee", "projectile", "insulation");
            WearLayer layers = WearLayer.None;
            foreach (string name in f.Texts(e, "layers", where))
            {
                WearLayer? layer = name switch
                {
                    "head" => WearLayer.Head,
                    "face" => WearLayer.Face,
                    "torso" => WearLayer.Torso,
                    "torso-armour" => WearLayer.TorsoArmour,
                    "legs" => WearLayer.Legs,
                    "legs-armour" => WearLayer.LegsArmour,
                    "hands" => WearLayer.Hands,
                    "feet" => WearLayer.Feet,
                    _ => null,
                };
                if (layer == null)
                {
                    f.Problem(where, $"unknown layer \"{name}\"");
                }
                layers |= layer ?? WearLayer.None;
            }
            BodyRegions covers = BodyRegions.None;
            foreach (string name in f.Texts(e, "covers", where))
            {
                int index = Array.IndexOf(BodyRegionNames, name);
                if (index < 0)
                {
                    f.Problem(where, $"unknown body region \"{name}\" (art/lib/hollow_art.py REGIONS)");
                    continue;
                }
                covers |= (BodyRegions)(1 << index);
            }
            return new WearStats
            {
                Layers = layers,
                Covers = covers,
                Melee = f.Number(e, "melee", where, 0),
                Projectile = f.Number(e, "projectile", where, 0),
                Insulation = f.Number(e, "insulation", where, 0),
            };
        }

        private FuelStats Fuel(JsonElement e, string where)
        {
            f.OnlyKnown(e, where, "seconds", "byproduct");
            return new FuelStats
            {
                Seconds = f.Number(e, "seconds", where),
                ByproductKey = f.Has(e, "byproduct") ? f.Text(e, "byproduct", where) : null,
            };
        }

        private CookStats Cook(JsonElement e, string where)
        {
            f.OnlyKnown(e, where, "into", "seconds", "in");
            return new CookStats { IntoKey = f.Text(e, "into", where), Seconds = f.Number(e, "seconds", where), In = f.Text(e, "in", where) };
        }

        private void ReadRecipes()
        {
            JsonElement root = Root("recipes.json", "recipes");
            JsonElement[] list = f.List(root, "recipes", "recipes.json");
            for (int i = 0; i < list.Length; i++)
            {
                JsonElement e = list[i];
                string output = f.Text(e, "output", $"recipes.json: recipes[{i}]");
                string where = $"recipes.json: {JsonFields.Quote(output)}";
                f.OnlyKnown(e, where, "output", "amount", "seconds", "workbench", "ingredients", "known", "station", "id", "key");
                ItemDef? item = Resolve(output, where);
                var ingredients = new List<ItemAmount>();
                foreach (JsonElement ingredient in f.List(e, "ingredients", where))
                {
                    if (f.Tuple(ingredient, where + ".ingredients", 1, out string key, out double[] values) &&
                        Resolve(key, where + ".ingredients") is { } input)
                    {
                        ingredients.Add(new ItemAmount(input, (int)values[0]));
                    }
                }
                if (item == null)
                {
                    continue;
                }
                // A second way to make an item (G7) has a number and a key of its own; the first is its output's.
                bool own = f.Has(e, "id") || f.Has(e, "key");
                int id = own ? f.Integer(e, "id", where) : item.Id;
                if (id is < 1 or > ushort.MaxValue)
                {
                    f.Problem(where, $"id {id} must be 1 … 65535");
                }
                loaded.Recipes.Add(new Recipe
                {
                    Output = item,
                    Amount = f.Integer(e, "amount", where, 1),
                    Seconds = f.Number(e, "seconds", where),
                    Workbench = f.Integer(e, "workbench", where, 0),
                    Ingredients = ingredients,
                    Known = f.Flag(e, "known", where, true),
                    Id = (ushort)Math.Clamp(id, 1, ushort.MaxValue),
                    Key = own ? f.Text(e, "key", where) : item.Key,
                    StationKey = f.Has(e, "station") ? f.Text(e, "station", where) : null,
                });
            }
        }

        private void ReadGather()
        {
            static string Hyphenated(Surface.ScatterKind k) =>
                string.Concat(k.ToString().Select((ch, i) => char.IsUpper(ch) && i > 0 ? "-" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
            static IEnumerable<string> ScatterKindNames() => Enum.GetValues<Surface.ScatterKind>().Select(Hyphenated);
            static Surface.ScatterKind? ScatterKindNamed(string name) =>
                Enum.GetValues<Surface.ScatterKind>().Where(k => Hyphenated(k) == name).Select(k => (Surface.ScatterKind?)k).FirstOrDefault();
            JsonElement root = Root("gather.json", "classes", "kinds");
            // How each class is gathered, and the drawn surfaces the weak spots sit on (GROUND.md §6.1).
            Dictionary<string, (GatherRules Rules, JsonElement Spot)> classes = GatherReader.Classes(f, root, Resolve);
            JsonElement surfaces = Root(GatherReader.SurfacesFile, "rings", "directions", "kinds");
            JsonElement[] list = f.List(root, "kinds", "gather.json");
            for (int i = 0; i < list.Length; i++)
            {
                JsonElement e = list[i];
                string key = f.Text(e, "key", $"gather.json: kinds[{i}]");
                string where = $"gather.json: {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "key", "name", "class", "health", "yield", "stages", "respawn", "radius", "height", "standIn", "scatter", "look");
                string className = f.Text(e, "class", where);
                NaturalClass kind = className switch
                {
                    "tree" => NaturalClass.Tree,
                    "node" => NaturalClass.Node,
                    "pickup" => NaturalClass.Pickup,
                    "bush" => NaturalClass.Bush,
                    "boulder" => NaturalClass.Boulder,
                    _ => NaturalClass.Pickup,
                };
                if (className is not ("tree" or "node" or "pickup" or "bush" or "boulder"))
                {
                    f.Problem(where, $"class \"{className}\" is not tree, node, pickup, bush or boulder");
                }
                var yields = new List<ItemAmount>();
                foreach (JsonElement y in f.List(e, "yield", where))
                {
                    if (f.Tuple(y, where + ".yield", 1, out string item, out double[] values) && Resolve(item, where + ".yield") is { } def)
                    {
                        yields.Add(new ItemAmount(def, (int)values[0]));
                    }
                }
                float perTile = 0;
                if (f.TryChild(e, "standIn", out JsonElement standIn))
                {
                    f.OnlyKnown(standIn, where + ".standIn", "perTile");
                    perTile = f.Number(standIn, "perTile", where + ".standIn");
                }
                var scatterKinds = new List<Surface.ScatterKind>();
                float minScale = 0;
                if (f.TryChild(e, "scatter", out JsonElement scatter))
                {
                    f.OnlyKnown(scatter, where + ".scatter", "kinds", "minScale");
                    foreach (string name in f.Texts(scatter, "kinds", where + ".scatter"))
                    {
                        if (ScatterKindNamed(name) is { } sk)
                        {
                            scatterKinds.Add(sk);
                        }
                        else
                        {
                            f.Problem(where + ".scatter", $"\"{name}\" is not a scatter kind ({string.Join(", ", ScatterKindNames())})");
                        }
                    }
                    minScale = f.Number(scatter, "minScale", where + ".scatter", 0f);
                }
                bool harvested = kind is NaturalClass.Tree or NaturalClass.Node;
                var natural = new NaturalKind
                {
                    Index = (ushort)i,
                    Key = key,
                    Name = f.Text(e, "name", where),
                    Class = kind,
                    Health = harvested ? f.Number(e, "health", where) : 0,
                    Yield = yields,
                    Stages = f.Integer(e, "stages", where, 1),
                    RespawnSeconds = f.Number(e, "respawn", where),
                    Radius = f.Number(e, "radius", where),
                    Height = f.Number(e, "height", where),
                    StandInPerTile = perTile,
                    ScatterKinds = scatterKinds,
                    ScatterMinScale = minScale,
                };
                if (harvested && classes.TryGetValue(className, out var rules))
                {
                    // The class's rules with this kind's own surface, and any parts of the look it replaces.
                    ISpotSurface? surface = GatherReader.Surface(f, rules.Spot, $"gather.json: classes.{className}.spot", natural, surfaces);
                    GatherLook look = f.TryChild(e, "look", out JsonElement own) ? GatherReader.Look(f, own, where + ".look", rules.Rules.Look) : rules.Rules.Look;
                    natural.Gather = rules.Rules with { Spot = rules.Rules.Spot with { Surface = surface! }, Look = look };
                    if (surface == null)
                    {
                        natural.Gather = null;
                    }
                }
                else if (harvested)
                {
                    f.Problem(where, $"gather.json has no \"classes\" entry for \"{className}\"");
                }
                else if (f.Has(e, "look"))
                {
                    f.Problem(where, "only trees and outcrops have a look");
                }
                loaded.Natural.Add(natural);
            }
        }

        private void ReadDeployables()
        {
            JsonElement root = Root("deployables.json", "deployables");
            JsonElement[] list = f.List(root, "deployables", "deployables.json");
            for (int i = 0; i < list.Length; i++)
            {
                JsonElement e = list[i];
                string key = f.Text(e, "key", $"deployables.json: deployables[{i}]");
                string where = $"deployables.json: {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "key", "item", "health", "size", "placement", "solid", "respawn", "slots", "device", "workbench", "bay");
                ItemDef? item = Resolve(f.Text(e, "item", where), where + ".item");
                JsonElement[] size = f.List(e, "size", where);
                Vector3 half = Vector3.Zero;
                if (size.Length == 3 && size.All(s => s.ValueKind == JsonValueKind.Number))
                {
                    half = new Vector3((float)size[0].GetDouble(), (float)size[1].GetDouble(), (float)size[2].GetDouble());
                }
                else
                {
                    f.Problem(where, "\"size\" must be three numbers (half extents x, y, z)");
                }
                DeviceStats? device = null;
                if (f.TryChild(e, "device", out JsonElement d))
                {
                    string dw = where + ".device";
                    f.OnlyKnown(d, dw, "fuel", "input", "output", "cooks", "warmth");
                    float radius = 0, celsius = 0;
                    if (f.TryChild(d, "warmth", out JsonElement warmth))
                    {
                        f.OnlyKnown(warmth, dw + ".warmth", "radius", "celsius");
                        radius = f.Number(warmth, "radius", dw + ".warmth");
                        celsius = f.Number(warmth, "celsius", dw + ".warmth");
                    }
                    device = new DeviceStats
                    {
                        FuelSlots = f.Integers(d, "fuel", dw),
                        InputSlots = f.Integers(d, "input", dw),
                        OutputSlots = f.Integers(d, "output", dw),
                        Cooks = f.Text(d, "cooks", dw),
                        WarmthRadius = radius,
                        WarmthCelsius = celsius,
                    };
                }
                BayStats? bay = null;
                if (f.TryChild(e, "bay", out JsonElement b))
                {
                    string bw = where + ".bay";
                    f.OnlyKnown(b, bw, "at", "clear", "maxSlope");
                    bay = new BayStats
                    {
                        At = Vector(b, "at", bw),
                        Clear = Vector(b, "clear", bw),
                        MaxSlopeDegrees = f.Number(b, "maxSlope", bw, 20),
                    };
                }
                if (item == null)
                {
                    continue;
                }
                var def = new DeployableDef
                {
                    Key = key,
                    Item = item,
                    Health = f.Number(e, "health", where),
                    HalfExtents = half,
                    Solid = f.Flag(e, "solid", where, true),
                    RespawnSeconds = f.Number(e, "respawn", where, 0),
                    Slots = f.Integer(e, "slots", where, 0),
                    Device = device,
                    Workbench = f.Integer(e, "workbench", where, 0),
                    Bay = bay,
                };
                loaded.Deployables.Add(def);
            }
            foreach (Recipe recipe in loaded.Recipes)
            {
                if (recipe.StationKey == null)
                {
                    continue;
                }
                recipe.Station = loaded.Deployables.FirstOrDefault(d => d.Key == recipe.StationKey);
                if (recipe.Station == null)
                {
                    f.Problem($"recipes.json: {JsonFields.Quote(recipe.Output.Key)}.station", $"no deployable \"{recipe.StationKey}\"");
                }
            }
            foreach (ItemDef item in loaded.Items)
            {
                if (item.DeploysKey == null)
                {
                    continue;
                }
                DeployableDef? def = loaded.Deployables.FirstOrDefault(d => d.Key == item.DeploysKey);
                if (def == null)
                {
                    f.Problem($"items.json: {JsonFields.Quote(item.Key)}.deploys", $"no deployable \"{item.DeploysKey}\"");
                }
                else if (def.Item != item)
                {
                    f.Problem($"deployables.json: {JsonFields.Quote(def.Key)}", $"its item is \"{def.Item.Key}\", but \"{item.Key}\" deploys it");
                }
                item.Deploys = def;
            }
        }

        private void ReadLoot()
        {
            JsonElement root = Root("loot.json", "tables");
            JsonElement[] list = f.List(root, "tables", "loot.json");
            for (int i = 0; i < list.Length; i++)
            {
                JsonElement e = list[i];
                string key = f.Text(e, "key", $"loot.json: tables[{i}]");
                string where = $"loot.json: {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "key", "guaranteed", "rolls", "entries");
                var guaranteed = new List<LootTable.Entry>();
                foreach (JsonElement g in f.List(e, "guaranteed", where, required: false))
                {
                    if (f.Tuple(g, where + ".guaranteed", 2, out string item, out double[] v) && Resolve(item, where + ".guaranteed") is { } def)
                    {
                        guaranteed.Add(new LootTable.Entry(def, (int)v[0], (int)v[1], 1));
                    }
                }
                var entries = new List<LootTable.Entry>();
                foreach (JsonElement g in f.List(e, "entries", where, required: false))
                {
                    // A fifth number makes it a blueprint of that workbench level (G7, world.json).
                    if (f.Tuple(g, where + ".entries", 3, out string item, out double[] v, optional: 1) && Resolve(item, where + ".entries") is { } def)
                    {
                        entries.Add(new LootTable.Entry(def, (int)v[0], (int)v[1], v[2], g.GetArrayLength() == 5 ? (int)v[3] : -1));
                    }
                }
                int[] rolls = f.Integers(e, "rolls", where);
                if (entries.Count > 0 && rolls.Length != 2)
                {
                    f.Problem(where, "\"rolls\" must be [least, most]");
                }
                loaded.Loot.Add(new LootTable
                {
                    Key = key,
                    Guaranteed = guaranteed,
                    Entries = entries,
                    LeastRolls = rolls.Length == 2 ? rolls[0] : 0,
                    MostRolls = rolls.Length == 2 ? rolls[1] : 0,
                });
            }
        }

        private void ReadSurvival()
        {
            JsonElement root = Root("survival.json", "vitals", "metabolism", "bleeding", "burning", "wounded", "temperature",
                "climateStandIn", "combat", "world", "bag", "rateLimits", "reach", "lifetimes");
            string w = "survival.json";
            JsonElement Section(string name, params string[] fields)
            {
                if (!f.TryChild(root, name, out JsonElement child))
                {
                    if (files["survival.json"].ValueKind == JsonValueKind.Object)
                    {
                        f.Problem(w, $"\"{name}\" is missing");
                    }
                    return default;
                }
                f.OnlyKnown(child, $"{w}: {name}", fields.Append("note").ToArray());
                return child;
            }
            JsonElement vitals = Section("vitals", "maxHealth", "maxCalories", "maxHydration", "spawn");
            JsonElement spawn = f.TryChild(vitals, "spawn", out JsonElement s) ? s : default;
            JsonElement m = Section("metabolism", "caloriesPerSecond", "hydrationPerSecond", "sprintFactor", "coldFactor",
                "starvationDamagePerSecond", "thirstDamagePerSecond", "regenMinCalories", "regenMinHydration", "regenPerSecond",
                "comfortRegenFactor", "healPerSecond");
            JsonElement b = Section("bleeding", "damagePerSecond", "clotPerSecond");
            JsonElement t = Section("temperature", "comfortable", "coldScale", "coldDamageFrom", "coldDamagePerSecond", "wetCooling", "wetSeconds", "fireWarmsTo");
            JsonElement c = Section("climateStandIn", "meanCelsius", "daySwing", "lapsePerKilometre", "dayLengthSeconds");
            JsonElement k = Section("combat", "head", "body", "limb", "maxRewindSeconds", "equipSeconds");
            JsonElement o = Section("world", "beachCooldownSeconds", "respawnKit");
            JsonElement lifetimes = Section("lifetimes", "bag", "body", "dud");
            LifetimeRule Lifetime(string name)
            {
                string where = $"{w}: lifetimes.{name}";
                if (!f.TryChild(lifetimes, name, out JsonElement e))
                {
                    f.Problem(where, "is missing");
                    return new LifetimeRule(300, LifetimeRestart.None, true);
                }
                f.OnlyKnown(e, where, "seconds", "restart", "emptied", "note");
                string restart = f.Text(e, "restart", where, "none");
                if (restart is not ("none" or "deposit"))
                {
                    f.Problem(where, $"restart \"{restart}\" (known: none, deposit)");
                }
                float seconds = f.Number(e, "seconds", where);
                if (seconds is <= 0 or > 86400)
                {
                    f.Problem(where, "seconds 0 … 86400");
                }
                return new LifetimeRule(seconds, restart == "deposit" ? LifetimeRestart.Deposit : LifetimeRestart.None, f.Flag(e, "emptied", where, false));
            }
            JsonElement reach = Section("reach", "interact", "open", "keepOpen", "help", "invite", "station");
            JsonElement bag = Section("bag", "name", "slots", "mergeRadius");
            JsonElement r = Section("rateLimits", "intentBurst", "intentsPerSecond", "gunBurst", "gunActionsPerSecond");
            JsonElement burning = Section("burning", "damagePerSecond");
            JsonElement wounded = Section("wounded", "maxDamage", "health", "seconds", "recoverChance", "fedBonus");
            (float Least, float Most) Pair(JsonElement e, string name, string where, float least, float most)
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
                {
                    if (e.ValueKind == JsonValueKind.Object)
                    {
                        f.Problem(where, $"\"{name}\" is missing");
                    }
                    return (least, most);
                }
                if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 && v[0].ValueKind == JsonValueKind.Number &&
                    v[1].ValueKind == JsonValueKind.Number)
                {
                    return ((float)v[0].GetDouble(), (float)v[1].GetDouble());
                }
                f.Problem(where, $"\"{name}\" must be [least, most]");
                return (least, most);
            }
            (float woundedHealthLeast, float woundedHealthMost) = Pair(wounded, "health", w + ": wounded", 7, 12);
            (float woundedSecondsLeast, float woundedSecondsMost) = Pair(wounded, "seconds", w + ": wounded", 40, 50);
            loaded.Survival = new SurvivalTuning
            {
                MaxHealth = f.Number(vitals, "maxHealth", w + ": vitals", 100),
                MaxCalories = f.Number(vitals, "maxCalories", w + ": vitals", 500),
                MaxHydration = f.Number(vitals, "maxHydration", w + ": vitals", 250),
                SpawnHealth = f.Number(spawn, "health", w + ": vitals.spawn", 60),
                SpawnCalories = f.Number(spawn, "calories", w + ": vitals.spawn", 260),
                SpawnHydration = f.Number(spawn, "hydration", w + ": vitals.spawn", 160),
                CaloriesPerSecond = f.Number(m, "caloriesPerSecond", w + ": metabolism"),
                HydrationPerSecond = f.Number(m, "hydrationPerSecond", w + ": metabolism"),
                SprintFactor = f.Number(m, "sprintFactor", w + ": metabolism", 1),
                ColdFactor = f.Number(m, "coldFactor", w + ": metabolism", 1),
                StarvationDamagePerSecond = f.Number(m, "starvationDamagePerSecond", w + ": metabolism"),
                ThirstDamagePerSecond = f.Number(m, "thirstDamagePerSecond", w + ": metabolism"),
                RegenMinCalories = f.Number(m, "regenMinCalories", w + ": metabolism"),
                RegenMinHydration = f.Number(m, "regenMinHydration", w + ": metabolism"),
                RegenPerSecond = f.Number(m, "regenPerSecond", w + ": metabolism"),
                ComfortRegenFactor = f.Number(m, "comfortRegenFactor", w + ": metabolism", 1),
                HealPerSecond = f.Number(m, "healPerSecond", w + ": metabolism", 1),
                BleedDamagePerSecond = f.Number(b, "damagePerSecond", w + ": bleeding"),
                BleedClotPerSecond = f.Number(b, "clotPerSecond", w + ": bleeding"),
                ComfortableCelsius = f.Number(t, "comfortable", w + ": temperature"),
                ColdScale = f.Number(t, "coldScale", w + ": temperature"),
                ColdDamageFrom = f.Number(t, "coldDamageFrom", w + ": temperature"),
                ColdDamagePerSecond = f.Number(t, "coldDamagePerSecond", w + ": temperature"),
                WetCooling = f.Number(t, "wetCooling", w + ": temperature", 0),
                WetSeconds = f.Number(t, "wetSeconds", w + ": temperature", 0),
                FireWarmsTo = f.Number(t, "fireWarmsTo", w + ": temperature", 25),
                ClimateMeanCelsius = f.Number(c, "meanCelsius", w + ": climateStandIn"),
                ClimateDaySwing = f.Number(c, "daySwing", w + ": climateStandIn"),
                ClimateLapsePerKilometre = f.Number(c, "lapsePerKilometre", w + ": climateStandIn"),
                DayLengthSeconds = f.Number(c, "dayLengthSeconds", w + ": climateStandIn"),
                HeadMultiplier = f.Number(k, "head", w + ": combat"),
                BodyMultiplier = f.Number(k, "body", w + ": combat"),
                LimbMultiplier = f.Number(k, "limb", w + ": combat"),
                MaxRewindSeconds = f.Number(k, "maxRewindSeconds", w + ": combat"),
                EquipSeconds = f.Number(k, "equipSeconds", w + ": combat", 0.3f),
                BagLifetime = Lifetime("bag"),
                BodyLifetime = Lifetime("body"),
                DudLifetime = Lifetime("dud"),
                BagName = f.Text(bag, "name", w + ": bag"),
                BagSlots = Math.Clamp(f.Integer(bag, "slots", w + ": bag"), 1, 255),
                BagMergeRadius = f.Number(bag, "mergeRadius", w + ": bag"),
                InteractReach = f.Number(reach, "interact", w + ": reach"),
                OpenReach = f.Number(reach, "open", w + ": reach"),
                KeepOpenReach = f.Number(reach, "keepOpen", w + ": reach"),
                HelpReach = f.Number(reach, "help", w + ": reach"),
                InviteReach = f.Number(reach, "invite", w + ": reach"),
                StationReach = f.Number(reach, "station", w + ": reach"),
                BeachCooldownSeconds = f.Number(o, "beachCooldownSeconds", w + ": world", 0),
                RespawnKitKey = f.Text(o, "respawnKit", w + ": world"),
                IntentBurst = f.Integer(r, "intentBurst", w + ": rateLimits", 16),
                IntentsPerSecond = f.Number(r, "intentsPerSecond", w + ": rateLimits", 20),
                GunBurst = f.Integer(r, "gunBurst", w + ": rateLimits", 20),
                GunActionsPerSecond = f.Number(r, "gunActionsPerSecond", w + ": rateLimits", 30),
                BurnDamagePerSecond = f.Number(burning, "damagePerSecond", w + ": burning"),
                WoundMaxDamage = f.Number(wounded, "maxDamage", w + ": wounded"),
                WoundedHealthLeast = woundedHealthLeast,
                WoundedHealthMost = woundedHealthMost,
                WoundedSecondsLeast = woundedSecondsLeast,
                WoundedSecondsMost = woundedSecondsMost,
                WoundedRecoverChance = f.Number(wounded, "recoverChance", w + ": wounded"),
                WoundedFedBonus = f.Number(wounded, "fedBonus", w + ": wounded", 0),
            };
        }

        // ---------------------------------------------------------------- weapons.json (G4)

        // Attachments and rounds first: the guns name them.
        private void ReadWeapons()
        {
            const string file = "weapons.json";
            JsonElement root = Root(file, "weapons", "ammo", "attachments");
            JsonElement[] attachments = f.List(root, "attachments", file);
            for (int i = 0; i < attachments.Length; i++)
            {
                JsonElement e = attachments[i];
                string key = f.Text(e, "item", $"{file}: attachments[{i}]");
                string where = $"{file}: attachment {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "item", "slot", "zoom", "hipSpread", "aimedSpread", "recoil", "speed", "damage", "silenced", "light", "note");
                ItemDef? item = Resolve(key, where);
                string slotName = f.Text(e, "slot", where);
                AttachmentSlot? slot = slotName switch
                {
                    "sight" => AttachmentSlot.Sight,
                    "muzzle" => AttachmentSlot.Muzzle,
                    "rail" => AttachmentSlot.Rail,
                    _ => null,
                };
                if (slot == null)
                {
                    f.Problem(where, $"slot \"{slotName}\" is not sight, muzzle or rail");
                }
                if (item == null)
                {
                    continue;
                }
                var stats = new AttachmentStats
                {
                    Item = item,
                    Index = loaded.Attachments.Count,
                    Slot = slot ?? AttachmentSlot.Sight,
                    Zoom = f.Double(e, "zoom", where, 0),
                    HipSpread = f.Double(e, "hipSpread", where, 1),
                    AimedSpread = f.Double(e, "aimedSpread", where, 1),
                    Recoil = f.Double(e, "recoil", where, 1),
                    Speed = f.Double(e, "speed", where, 1),
                    Damage = f.Double(e, "damage", where, 1),
                    Silenced = f.Flag(e, "silenced", where, false),
                    Light = f.Flag(e, "light", where, false),
                };
                if (item.Attachment != null)
                {
                    f.Problem(where, "is listed twice");
                    continue;
                }
                item.Attachment = stats;
                loaded.Attachments.Add(stats);
            }

            JsonElement[] ammo = f.List(root, "ammo", file);
            for (int i = 0; i < ammo.Length; i++)
            {
                JsonElement e = ammo[i];
                string key = f.Text(e, "item", $"{file}: ammo[{i}]");
                string where = $"{file}: ammo {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "item", "calibre", "damage", "speed", "bleed", "burn", "structure", "blast", "note");
                ItemDef? item = Resolve(key, where);
                if (item == null)
                {
                    continue;
                }
                var stats = new BulletStats
                {
                    Item = item,
                    Calibre = f.Text(e, "calibre", where),
                    Damage = f.Double(e, "damage", where, 1),
                    Speed = f.Double(e, "speed", where, 1),
                    Bleed = f.Double(e, "bleed", where, 0),
                    Burn = f.Double(e, "burn", where, 0),
                    Structure = f.Double(e, "structure", where, 0),
                    BlastKey = f.Has(e, "blast") ? f.Text(e, "blast", where) : null,
                };
                if (item.Bullet != null)
                {
                    f.Problem(where, "is listed twice");
                    continue;
                }
                item.Bullet = stats;
                loaded.Bullets.Add(stats);
            }

            JsonElement[] weapons = f.List(root, "weapons", file);
            for (int i = 0; i < weapons.Length; i++)
            {
                JsonElement e = weapons[i];
                string key = f.Text(e, "item", $"{file}: weapons[{i}]");
                string where = $"{file}: weapon {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "item", "calibre", "magazine", "fire", "interval", "reload", "equip", "speed", "drag", "gravity",
                    "range", "lifetime", "damage", "falloff", "head", "body", "limb", "spread", "recoil", "zoom", "attachments", "note");
                ItemDef? item = Resolve(key, where);
                string mode = f.Text(e, "fire", where);
                if (mode is not ("semi" or "auto"))
                {
                    f.Problem(where, $"fire \"{mode}\" is not semi or auto");
                }
                double[] falloff = Numbers(e, "falloff", where, 3) ?? new double[] { 0, 0, 1 };
                var stats = new FirearmStats
                {
                    Item = item!,
                    Calibre = f.Text(e, "calibre", where),
                    Magazine = f.Integer(e, "magazine", where),
                    Mode = mode == "auto" ? FireMode.Auto : FireMode.Semi,
                    IntervalSeconds = f.Double(e, "interval", where),
                    ReloadSeconds = f.Double(e, "reload", where),
                    EquipSeconds = f.Double(e, "equip", where),
                    Speed = f.Double(e, "speed", where),
                    Drag = f.Double(e, "drag", where, 0),
                    Gravity = f.Double(e, "gravity", where, 1),
                    Range = f.Double(e, "range", where),
                    LifetimeSeconds = f.Double(e, "lifetime", where),
                    Damage = f.Double(e, "damage", where),
                    FalloffStart = falloff[0],
                    FalloffEnd = falloff[1],
                    FalloffLeast = falloff[2],
                    Head = f.Double(e, "head", where),
                    Body = f.Double(e, "body", where),
                    Limb = f.Double(e, "limb", where),
                    Spread = Spread(e, where + ".spread"),
                    Recoil = Recoil(e, where + ".recoil"),
                    Zoom = f.Double(e, "zoom", where, 1),
                    AttachmentKeys = f.Texts(e, "attachments", where),
                };
                if (item == null)
                {
                    continue;
                }
                if (item.Firearm != null)
                {
                    f.Problem(where, "is listed twice");
                    continue;
                }
                stats.Attachments = stats.AttachmentKeys.Select(k => Resolve(k, where + ".attachments")).OfType<ItemDef>().ToArray();
                stats.Ammo = loaded.Bullets.Where(b => b.Calibre == stats.Calibre).Select(b => b.Item).ToArray();
                item.Firearm = stats;
                loaded.Firearms.Add(stats);
            }
        }

        private double[]? Numbers(JsonElement e, string name, string where, int count)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
            {
                f.Problem(where, $"\"{name}\" is missing");
                return null;
            }
            if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != count || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number))
            {
                f.Problem(where, $"\"{name}\" must be {count} numbers");
                return null;
            }
            return v.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        }

        private SpreadStats Spread(JsonElement weapon, string where)
        {
            if (!f.TryChild(weapon, "spread", out JsonElement e))
            {
                f.Problem(where, "is missing");
                return new SpreadStats();
            }
            f.OnlyKnown(e, where, "hip", "aimed", "bloom", "maxBloom", "bloomRecovery", "moving", "movingAimed", "airborne", "crouched", "note");
            return new SpreadStats
            {
                Hip = f.Double(e, "hip", where),
                Aimed = f.Double(e, "aimed", where),
                BloomPerShot = f.Double(e, "bloom", where, 0),
                MaxBloom = f.Double(e, "maxBloom", where, 0),
                BloomRecovery = f.Double(e, "bloomRecovery", where, 0),
                Moving = f.Double(e, "moving", where, 0),
                MovingAimed = f.Double(e, "movingAimed", where, 0),
                Airborne = f.Double(e, "airborne", where, 1),
                Crouched = f.Double(e, "crouched", where, 1),
            };
        }

        private RecoilStats Recoil(JsonElement weapon, string where)
        {
            if (!f.TryChild(weapon, "recoil", out JsonElement e))
            {
                f.Problem(where, "is missing");
                return new RecoilStats();
            }
            f.OnlyKnown(e, where, "mode", "pitch", "pattern", "pitchLimit", "yawLimit", "reset", "kick", "recoveryDelay", "recovery", "note");
            string mode = f.Text(e, "mode", where);
            if (mode is not ("vertical" or "pattern"))
            {
                f.Problem(where, $"mode \"{mode}\" is not vertical or pattern");
            }
            bool pattern = mode == "pattern";
            var steps = new List<RecoilStep>();
            if (pattern)
            {
                foreach (JsonElement step in f.List(e, "pattern", where))
                {
                    if (step.ValueKind == JsonValueKind.Array && step.GetArrayLength() == 2 &&
                        step[0].ValueKind == JsonValueKind.Number && step[1].ValueKind == JsonValueKind.Number)
                    {
                        steps.Add(new RecoilStep(step[0].GetDouble(), step[1].GetDouble()));
                    }
                    else
                    {
                        f.Problem(where, "each pattern step is [pitch, yaw]");
                    }
                }
            }
            return new RecoilStats
            {
                Mode = pattern ? RecoilMode.Pattern : RecoilMode.Vertical,
                PitchPerShot = pattern ? 0 : f.Double(e, "pitch", where),
                Pattern = steps,
                PitchLimit = f.Double(e, "pitchLimit", where),
                YawLimit = f.Double(e, "yawLimit", where),
                ResetSeconds = f.Double(e, "reset", where, pattern ? null : 0.4),
                KickSeconds = f.Double(e, "kick", where),
                RecoveryDelaySeconds = f.Double(e, "recoveryDelay", where),
                RecoverySeconds = f.Double(e, "recovery", where),
            };
        }

        // ---------------------------------------------------------------- ships (G6)

        private Vector3 Vector(JsonElement o, string name, string where)
        {
            JsonElement[] v = f.List(o, name, where);
            if (v.Length == 3 && v.All(x => x.ValueKind == JsonValueKind.Number))
            {
                return new Vector3((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble());
            }
            if (f.Has(o, name))
            {
                f.Problem(where, $"\"{name}\" must be three numbers");
            }
            return Vector3.Zero;
        }

        private Vector3[] Vectors(JsonElement o, string name, string where, bool required = true)
        {
            var result = new List<Vector3>();
            foreach (JsonElement v in f.List(o, name, where, required))
            {
                if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
                {
                    result.Add(new Vector3((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble()));
                }
                else
                {
                    f.Problem(where, $"each of \"{name}\" is three numbers");
                }
            }
            return result.ToArray();
        }

        private Ships.TurnAxis Turn(JsonElement o, string name, string where)
        {
            JsonElement[] v = f.List(o, name, where);
            if (v.Length == 2 && v.All(x => x.ValueKind == JsonValueKind.Number))
            {
                const float degrees = MathF.PI / 180;
                return new Ships.TurnAxis((float)v[0].GetDouble() * degrees, (float)v[1].GetDouble() * degrees);
            }
            f.Problem(where, $"\"{name}\" is [most rate °/s, acceleration °/s²]");
            return default;
        }

        private void ReadShips()
        {
            JsonElement root = Root("ships.json", "ships");
            JsonElement[] list = f.List(root, "ships", "ships.json");
            for (int i = 0; i < list.Length; i++)
            {
                JsonElement e = list[i];
                string key = f.Text(e, "key", $"ships.json: ships[{i}]");
                string where = $"ships.json: {JsonFields.Quote(key)}";
                f.OnlyKnown(e, where, "key", "item", "mass", "inertia", "centreOfMass", "health", "damage", "thrust", "speed", "boost",
                    "rates", "drag", "landing", "skids", "crash", "fuel", "cargo", "hull", "sockets", "nozzles", "reach", "ceiling", "wreck", "placement");
                ItemDef? item = Resolve(f.Text(e, "item", where), where + ".item");
                var blocks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (string name in new[] { "damage", "thrust", "speed", "boost", "rates", "drag", "landing", "skids", "crash", "fuel", "sockets", "nozzles" })
                {
                    if (f.TryChild(e, name, out JsonElement child) && child.ValueKind == JsonValueKind.Object)
                    {
                        blocks[name] = child;
                    }
                    else
                    {
                        f.Problem(where, $"needs \"{name}\" (an object)");
                    }
                }
                if (blocks.Count < 12)
                {
                    continue;
                }
                JsonElement damage = blocks["damage"], thrust = blocks["thrust"], speed = blocks["speed"], boost = blocks["boost"];
                JsonElement rates = blocks["rates"], drag = blocks["drag"], landing = blocks["landing"], skids = blocks["skids"];
                JsonElement crash = blocks["crash"], fuel = blocks["fuel"], sockets = blocks["sockets"], nozzles = blocks["nozzles"];
                f.OnlyKnown(damage, where + ".damage", "melee", "arrow", "bullet", "explosive");
                f.OnlyKnown(thrust, where + ".thrust", "main", "retro", "lateral", "lift", "down");
                f.OnlyKnown(speed, where + ".speed", "forward", "reverse", "lateral", "up", "down");
                f.OnlyKnown(boost, where + ".boost", "thrust", "speed", "seconds", "cooldown");
                f.OnlyKnown(rates, where + ".rates", "pitch", "yaw", "roll");
                f.OnlyKnown(drag, where + ".drag", "forward", "side", "top", "turn");
                f.OnlyKnown(landing, where + ".landing", "maxSlope", "assist");
                f.OnlyKnown(skids, where + ".skids", "points", "stiffness", "damping", "travel", "friction");
                f.OnlyKnown(crash, where + ".crash", "safe", "scale", "pilot");
                f.OnlyKnown(fuel, where + ".fuel", "item", "capacity", "idle", "full", "boost", "placed");
                f.OnlyKnown(sockets, where + ".sockets", "seat", "eye", "hatch", "fuel", "exits");
                f.OnlyKnown(nozzles, where + ".nozzles", "main", "aux", "lift", "rcs");
                var hull = new List<Ships.HullBox>();
                foreach (JsonElement h in f.List(e, "hull", where))
                {
                    f.OnlyKnown(h, where + ".hull", "centre", "size", "skid");
                    hull.Add(new Ships.HullBox(Vector(h, "centre", where + ".hull"), Vector(h, "size", where + ".hull"), f.Flag(h, "skid", where + ".hull", false)));
                }
                var wreck = new List<ItemAmount>();
                foreach (JsonElement w in f.List(e, "wreck", where, required: false))
                {
                    if (f.Tuple(w, where + ".wreck", 1, out string wreckKey, out double[] values) && Resolve(wreckKey, where + ".wreck") is { } wreckItem)
                    {
                        wreck.Add(new ItemAmount(wreckItem, (int)values[0]));
                    }
                }
                float[] assist = f.List(landing, "assist", where + ".landing").Where(x => x.ValueKind == JsonValueKind.Number)
                    .Select(x => (float)x.GetDouble()).ToArray();
                if (assist.Length != 2)
                {
                    f.Problem(where + ".landing", "\"assist\" is [least descent m/s, descent per metre of height]");
                }
                ItemDef? fuelItem = Resolve(f.Text(fuel, "item", where + ".fuel"), where + ".fuel.item");
                if (item == null || fuelItem == null)
                {
                    continue;
                }
                var def = new Ships.ShipDef
                {
                    Key = key,
                    Item = item,
                    Mass = f.Number(e, "mass", where),
                    Inertia = Vector(e, "inertia", where),
                    CentreOfMass = Vector(e, "centreOfMass", where),
                    Health = f.Number(e, "health", where),
                    MeleeDamage = f.Number(damage, "melee", where + ".damage"),
                    ArrowDamage = f.Number(damage, "arrow", where + ".damage"),
                    BulletDamage = f.Number(damage, "bullet", where + ".damage"),
                    ExplosiveDamage = f.Number(damage, "explosive", where + ".damage"),
                    MainThrust = f.Number(thrust, "main", where + ".thrust"),
                    RetroThrust = f.Number(thrust, "retro", where + ".thrust"),
                    LateralThrust = f.Number(thrust, "lateral", where + ".thrust"),
                    LiftThrust = f.Number(thrust, "lift", where + ".thrust"),
                    DownThrust = f.Number(thrust, "down", where + ".thrust"),
                    ForwardSpeed = f.Number(speed, "forward", where + ".speed"),
                    ReverseSpeed = f.Number(speed, "reverse", where + ".speed"),
                    LateralSpeed = f.Number(speed, "lateral", where + ".speed"),
                    UpSpeed = f.Number(speed, "up", where + ".speed"),
                    DownSpeed = f.Number(speed, "down", where + ".speed"),
                    BoostThrust = f.Number(boost, "thrust", where + ".boost"),
                    BoostSpeed = f.Number(boost, "speed", where + ".boost"),
                    BoostSeconds = f.Number(boost, "seconds", where + ".boost"),
                    BoostCooldownSeconds = f.Number(boost, "cooldown", where + ".boost"),
                    Pitch = Turn(rates, "pitch", where + ".rates"),
                    Yaw = Turn(rates, "yaw", where + ".rates"),
                    Roll = Turn(rates, "roll", where + ".rates"),
                    DragForward = f.Number(drag, "forward", where + ".drag"),
                    DragSide = f.Number(drag, "side", where + ".drag"),
                    DragTop = f.Number(drag, "top", where + ".drag"),
                    TurnDrag = f.Number(drag, "turn", where + ".drag"),
                    MaxLandingSlope = f.Number(landing, "maxSlope", where + ".landing") * MathF.PI / 180,
                    LandingDescent = assist.Length == 2 ? assist[0] : 1.5f,
                    LandingDescentPerMetre = assist.Length == 2 ? assist[1] : 0.25f,
                    SkidPoints = Vectors(skids, "points", where + ".skids"),
                    SkidStiffness = f.Number(skids, "stiffness", where + ".skids"),
                    SkidDamping = f.Number(skids, "damping", where + ".skids"),
                    SkidTravel = f.Number(skids, "travel", where + ".skids"),
                    SkidFriction = f.Number(skids, "friction", where + ".skids"),
                    CrashSafeSpeed = f.Number(crash, "safe", where + ".crash"),
                    CrashScale = f.Number(crash, "scale", where + ".crash"),
                    CrashPilotShare = f.Number(crash, "pilot", where + ".crash", 0),
                    Fuel = fuelItem,
                    FuelCapacity = f.Number(fuel, "capacity", where + ".fuel"),
                    FuelIdle = f.Number(fuel, "idle", where + ".fuel"),
                    FuelFull = f.Number(fuel, "full", where + ".fuel"),
                    FuelBoost = f.Number(fuel, "boost", where + ".fuel", 1),
                    PlacedFuel = f.Number(fuel, "placed", where + ".fuel", 0),
                    CargoSlots = f.Integer(e, "cargo", where, 0),
                    Hull = hull,
                    Seat = Vector(sockets, "seat", where + ".sockets"),
                    Eye = Vector(sockets, "eye", where + ".sockets"),
                    Hatch = Vector(sockets, "hatch", where + ".sockets"),
                    FuelCap = Vector(sockets, "fuel", where + ".sockets"),
                    Exits = Vectors(sockets, "exits", where + ".sockets"),
                    MainNozzles = Vectors(nozzles, "main", where + ".nozzles", required: false),
                    AuxNozzles = Vectors(nozzles, "aux", where + ".nozzles", required: false),
                    LiftNozzles = Vectors(nozzles, "lift", where + ".nozzles", required: false),
                    RcsNozzles = Vectors(nozzles, "rcs", where + ".nozzles", required: false),
                    Reach = f.Number(e, "reach", where, 2.4f),
                    Ceiling = f.Number(e, "ceiling", where),
                    Wreck = wreck,
                };
                def.ComputeBounds();
                loaded.Ships.Add(def);
            }
            foreach (ItemDef item in loaded.Items)
            {
                if (item.ShipKey == null)
                {
                    continue;
                }
                Ships.ShipDef? def = loaded.Ships.FirstOrDefault(s => s.Key == item.ShipKey);
                if (def == null)
                {
                    f.Problem($"items.json: {JsonFields.Quote(item.Key)}.ship", $"no ship \"{item.ShipKey}\" in ships.json");
                }
                else if (def.Item != item)
                {
                    f.Problem($"ships.json: {JsonFields.Quote(def.Key)}", $"its item is \"{def.Item.Key}\", but \"{item.Key}\" says it is that ship");
                }
                item.Ship = def;
            }
        }
    }

    /// <summary>Region names in the character work's order (BodyRegions bit i is name i).</summary>
    public static readonly string[] BodyRegionNames =
    {
        "face", "scalp", "neck", "chest", "belly", "pelvis", "upper_arm", "forearm", "hand", "thigh", "calf", "ankle", "foot",
    };
}
