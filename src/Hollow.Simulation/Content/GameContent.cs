using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation.Content.Readers;
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
        Content.InteractionData.File,
        "placement.json",
        "batteries.json",
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
        Batteries = loaded.Batteries;
        Blasts = loaded.Blasts;
        Throwables = loaded.Throwables;
        Fires = loaded.Fires;
        FireRules = loaded.FireRules;
        RaidAlerts = loaded.RaidAlerts;
        World = loaded.World ?? new WorldData();
        Animals = loaded.Animals;
        AnimalSenses = loaded.AnimalSenses;
        AnimalSpawning = loaded.AnimalSpawning;
        HarvestRates = loaded.HarvestRates;
        Survival = loaded.Survival;
        WorkbenchSpeed = loaded.WorkbenchSpeed;
        Hash = loaded.Hash;
        Problems = loaded.Problems;
        Texts = loaded.Texts!;
        Capabilities = loaded.Capabilities ?? new CapabilityData(Array.Empty<string>());
        Interactions = loaded.Interactions ?? new InteractionData(Array.Empty<VerbDef>(), 0.35f);
        Placement = loaded.Placement ?? Simulation.Placement.PlacementData.Empty;
        ItemActions = loaded.Actions ?? global::Hollow.Simulation.Items.ItemActions.Bind(Array.Empty<global::Hollow.Simulation.Items.ItemActionDef>(), new List<string>());
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

    /// <summary>The crewed batteries' kinds (batteries.json, IRON_WAKE.md §6, IW3): a ship's stations name one.</summary>
    public IReadOnlyList<BatteryDef> Batteries { get; }

    /// <summary>A battery kind by its number (the wire's), or null.</summary>
    public BatteryDef? Battery(int index) => index >= 0 && index < Batteries.Count ? Batteries[index] : null;

    /// <summary>G5 (explosives.json): every blast, the explosives thrown or placed by hand, and how raid alerts go out.</summary>
    public IReadOnlyList<BlastStats> Blasts { get; }

    public IReadOnlyList<ThrowableStats> Throwables { get; }

    /// <summary>IW4 (explosives.json fires, IRON_WAKE.md §7): the kinds of fire blasts leave, and the rules every fire follows.</summary>
    public IReadOnlyList<FireDef> Fires { get; }

    public FireRules FireRules { get; }

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

    /// <summary>A kind of fire by key, or null.</summary>
    public FireDef? Fire(string key)
    {
        foreach (FireDef f in Fires)
        {
            if (f.Key == key)
            {
                return f;
            }
        }
        return null;
    }

    public SurvivalTuning Survival { get; }

    /// <summary>
    /// How much of a recipe's time a job takes by how many levels the best workbench in reach is above the recipe's
    /// (recipes.json <c>workbenchSpeed</c>, GROUND_SYSTEMS.md §7: Rust's 1, ½, ¼); the last holds for more.
    /// </summary>
    public IReadOnlyList<double> WorkbenchSpeed { get; }

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

    /// <summary>The interaction verbs (interactions.json, GROUND_SYSTEMS.md §5): what the E key can do, by number and key.</summary>
    public InteractionData Interactions { get; }

    /// <summary>Everything that can be placed and its rule, by the wire's numbers, and the pipeline's handlers (placement.json, GROUND_SYSTEMS.md §6).</summary>
    public Simulation.Placement.PlacementData Placement { get; }

    /// <summary>
    /// The inventory's buttons (items.json <c>actions</c>, GROUND_SYSTEMS.md §7) and their handlers: what a stack offers
    /// and doing one, on the server and in the client's prediction.
    /// </summary>
    public global::Hollow.Simulation.Items.ItemActions ItemActions { get; }

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

    /// <summary>
    /// The folders whose JSON files are data files the listed ones name (a ships.json entry's interior names its ship's
    /// data file, IRON_WAKE.md §9.1): read and hashed with the rest, after them, by name.
    /// </summary>
    public static readonly string[] DataFolders = { "ships" };

    /// <summary>Reads the files in a folder and keeps whatever problems they have (for checks and tests).</summary>
    public static GameContent Read(string folder) => FromTexts(ReadTexts(folder));

    /// <summary>Every file's text in a folder, by its name (a data file by its path under the folder: "ships/iron-wake.json").</summary>
    public static Dictionary<string, string> ReadTexts(string folder)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Files.Concat(Unhashed))
        {
            string path = Path.Combine(folder, file);
            texts[file] = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        foreach (string sub in DataFolders)
        {
            string path = Path.Combine(folder, sub);
            if (!Directory.Exists(path))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(path, "*.json").Order(StringComparer.Ordinal))
            {
                texts[sub + "/" + Path.GetFileName(file)] = File.ReadAllText(file);
            }
        }
        return texts;
    }

    /// <summary>The data files among some texts (their names have a folder), in the order they are hashed.</summary>
    private static IEnumerable<string> DataFiles(IReadOnlyDictionary<string, string> texts) =>
        texts.Keys.Where(k => k.Contains('/', StringComparison.Ordinal) && k.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

    /// <summary>Content from the files' texts (a missing file is a problem, never a different game).</summary>
    public static GameContent FromTexts(IReadOnlyDictionary<string, string> texts)
    {
        var problems = new List<string>();
        var loaded = new Loaded { Problems = problems };
        var documents = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        ulong hash = Hash64.Of(0x5EED_C0DE);
        foreach (string file in Files.Concat(DataFiles(texts)))
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
        // One reader per file, in the order the files refer to each other (ContentReaders.All, GROUND_SYSTEMS.md §12.6):
        // every file read, then every file checked, the checks across them last.
        var reading = new ContentReading(new JsonFields(problems), documents, texts, loaded);
        foreach (IContentReader reader in ContentReaders.All)
        {
            reader.Read(reading);
        }
        var checking = new ContentChecking(loaded, problems);
        foreach (IContentReader reader in ContentReaders.All)
        {
            reader.Check(checking);
        }
        // Every file linked: each item's parts and its one held behaviour, and the inventory's actions bound to their
        // handlers (GROUND_SYSTEMS.md §7).
        ItemPartTable.Assign(loaded, problems);
        loaded.Actions = global::Hollow.Simulation.Items.ItemActions.Bind(loaded.ItemActions, problems);
        return new GameContent(loaded);
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
        public List<BatteryDef> Batteries = new();
        public List<BlastStats> Blasts = new();
        public List<ThrowableStats> Throwables = new();
        public List<FireDef> Fires = new();
        public FireRules FireRules = new();
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
        public InteractionData? Interactions;
        public Simulation.Placement.PlacementData? Placement;
        public List<global::Hollow.Simulation.Items.ItemActionDef> ItemActions = new();
        public IReadOnlyList<double> WorkbenchSpeed = new[] { 1.0 };
        public global::Hollow.Simulation.Items.ItemActions? Actions;
        public ulong Hash;
        public List<string> Problems = new();
    }

    /// <summary>Region names in the character work's order (BodyRegions bit i is name i).</summary>
    public static readonly string[] BodyRegionNames =
    {
        "face", "scalp", "neck", "chest", "belly", "pelvis", "upper_arm", "forearm", "hand", "thigh", "calf", "ankle", "foot",
    };
}
