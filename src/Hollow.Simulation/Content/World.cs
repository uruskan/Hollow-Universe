using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Hollow.Simulation.Content;

/// <summary>What a world prop is (world.json <c>props</c>, GROUND.md §6.8).</summary>
public enum WorldPropKind : byte
{
    /// <summary>Broken with blows and shots; its loot spills on the ground (Rust's barrels).</summary>
    Barrel,

    /// <summary>Opened and looted, take only; it goes once empty (Rust's crates).</summary>
    Crate,

    /// <summary>A public machine that turns items into components and salvage (Rust's recycler).</summary>
    Recycler,
}

/// <summary>
/// A world prop (world.json): a barrel, crate or recycler the world places, never a player. It lives in the zone as a
/// deployable (<see cref="Deployable"/>, built from these numbers), so blows, shots, blasts, opening and replication
/// treat it as one; <see cref="Id"/> is what clients see it as (a thing of kind Prop).
/// </summary>
public sealed class PropDef
{
    /// <summary>Its number on the wire (1 … 255), stable like an item's.</summary>
    public byte Id { get; init; }

    /// <summary>Its model in the catalogue (models.json, kind "world").</summary>
    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public WorldPropKind Kind { get; init; }

    /// <summary>The loot table it is filled from (barrels and crates).</summary>
    public string? LootKey { get; init; }

    public LootTable? Loot { get; internal set; }

    /// <summary>It comes back this long after it was broken or emptied (the real clock).</summary>
    public double RespawnSeconds { get; init; }

    /// <summary>The deployable the zone keeps it as: health, size, slots, and the recycler's numbers.</summary>
    public DeployableDef Deployable { get; internal set; } = null!;

    public bool Breakable => Kind == WorldPropKind.Barrel;

    /// <summary>Opened with the interact key (crates, recyclers); a barrel is broken instead.</summary>
    public bool Openable => Kind != WorldPropKind.Barrel;

    public override string ToString() => Key;
}

/// <summary>The recycler (world.json <c>recycler</c>): its slots, its pace and how much of a recipe comes back.</summary>
public sealed class RecyclerStats
{
    public IReadOnlyList<int> InputSlots { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> OutputSlots { get; init; } = Array.Empty<int>();

    /// <summary>One batch every this many seconds.</summary>
    public double Seconds { get; init; } = 5;

    /// <summary>The share of a crafted item's recipe that comes back (Rust's 50%).</summary>
    public double Efficiency { get; init; } = 0.5;

    /// <summary>A batch is this share of the item's stack size (at least one).</summary>
    public double Batch { get; init; } = 0.1;

    public int Ticks => Math.Max(1, CharacterTicks.From(Seconds));
}

/// <summary>The research table (world.json <c>research</c>): which slots hold the item and the salvage, how long, what it costs.</summary>
public sealed class ResearchStats
{
    public string TableKey { get; init; } = "";

    public int ItemSlot { get; init; }

    public int SalvageSlot { get; init; } = 1;

    /// <summary>Where the blueprint comes out (it takes nothing put in).</summary>
    public int OutputSlot { get; init; } = 2;

    public double Seconds { get; init; } = 10;

    /// <summary>Salvage by the recipe's tier (0 … 3: no workbench, then workbench levels).</summary>
    public IReadOnlyList<int> Costs { get; init; } = Array.Empty<int>();

    public int Ticks => Math.Max(1, CharacterTicks.From(Seconds));

    public int CostFor(int tier) => Costs.Count == 0 ? 0 : Costs[Math.Clamp(tier, 0, Costs.Count - 1)];
}

/// <summary>A node of a workbench's tech tree: the blueprint it unlocks, its salvage cost and the nodes before it.</summary>
public sealed class TechNode
{
    public Recipe Recipe { get; init; } = null!;

    public ItemDef Item => Recipe.Output;

    /// <summary>The workbench level whose tree it is in.</summary>
    public int Tier { get; init; }

    public int Cost { get; init; }

    /// <summary>Unlocked once any of these is known (none: a root).</summary>
    public IReadOnlyList<TechNode> After { get; internal set; } = Array.Empty<TechNode>();

    public IReadOnlyList<string> AfterKeys { get; init; } = Array.Empty<string>();

    /// <summary>Steps from a root (0 for a root): the interface's row.</summary>
    public int Depth { get; internal set; }

    public override string ToString() => $"{Item.Key} ({Tier}, {Cost})";
}

/// <summary>One workbench level's tech tree (Rust's).</summary>
public sealed class TechTree
{
    public int Workbench { get; init; }

    public IReadOnlyList<TechNode> Nodes { get; init; } = Array.Empty<TechNode>();
}

/// <summary>Where barrels and crates gather (world.json <c>loot</c>): the clusters' search and their make-up.</summary>
public sealed class LootSpotSettings
{
    public double Radius { get; init; } = 1600;

    public double Cell { get; init; } = 110;

    /// <summary>No loot this close to the zone's origin (where players arrive).</summary>
    public double Clear { get; init; } = 90;

    /// <summary>Barrels and the crate stand within this of the cluster's middle.</summary>
    public double Spread { get; init; } = 6;

    /// <summary>The chance a candidate is kept on a coast, at a forest edge, in the open.</summary>
    public double Coast { get; init; } = 0.7;

    public double ForestEdge { get; init; } = 0.5;

    public double Open { get; init; } = 0.08;

    public int LeastBarrels { get; init; } = 2;

    public int MostBarrels { get; init; } = 4;

    public double FuelBarrel { get; init; } = 0.2;

    public double Crate { get; init; } = 0.85;

    public double Military { get; init; } = 0.22;

    /// <summary>A cluster always kept near the origin (zone-frame metres x, y ignored, z), or null.</summary>
    public Vector3? Landing { get; init; }
}

/// <summary>
/// The world's economy and progression (world.json, GROUND.md §6.8): the currency, world props and their loot, the
/// recycler and what each item gives, the research table, the tech trees, teams and map markers.
/// </summary>
public sealed class WorldData
{
    private readonly Dictionary<ushort, TechNode> _nodes = new();
    private readonly Dictionary<ushort, IReadOnlyList<ItemAmount>> _yields = new();
    private readonly List<Recipe>[] _pools = { new(), new(), new(), new() };

    /// <summary>Salvage: what the world pays in.</summary>
    public ItemDef? Currency { get; internal set; }

    /// <summary>The blueprint item (its condition holds the recipe it teaches).</summary>
    public ItemDef? Blueprint { get; internal set; }

    public IReadOnlyList<PropDef> Props { get; internal set; } = Array.Empty<PropDef>();

    public RecyclerStats Recycler { get; internal set; } = new();

    public ResearchStats Research { get; internal set; } = new();

    public IReadOnlyList<TechTree> TechTrees { get; internal set; } = Array.Empty<TechTree>();

    public LootSpotSettings Loot { get; internal set; } = new();

    /// <summary>How many recyclers stand near the landing site, and the points (zone-frame x, z) tried in order.</summary>
    public int RecyclerCount { get; internal set; }

    public IReadOnlyList<Vector2> RecyclerPoints { get; internal set; } = Array.Empty<Vector2>();

    public int TeamSize { get; internal set; } = 8;

    public double InviteSeconds { get; internal set; } = 300;

    public int MaxMarkers { get; internal set; } = 5;

    /// <summary>The map's side in metres and its grid's cell.</summary>
    public double MapMetres { get; internal set; } = 3900;

    public double MapCell { get; internal set; } = 150;

    public PropDef? Prop(int id)
    {
        foreach (PropDef p in Props)
        {
            if (p.Id == id)
            {
                return p;
            }
        }
        return null;
    }

    public PropDef? Prop(string key) => Props.FirstOrDefault(p => p.Key == key);

    /// <summary>The tech tree node unlocking a recipe, or null (the recipe is in no tree).</summary>
    public TechNode? NodeOf(int recipe) => recipe is > 0 and <= ushort.MaxValue && _nodes.TryGetValue((ushort)recipe, out TechNode? n) ? n : null;

    public TechTree? Tree(int workbench) => TechTrees.FirstOrDefault(t => t.Workbench == workbench);

    /// <summary>What recycling one unit of an item gives when it has its own list (loot-only components), or null.</summary>
    public IReadOnlyList<ItemAmount>? FixedYield(ushort item) => _yields.GetValueOrDefault(item);

    /// <summary>Recipes a blueprint of a tier can teach: not known from the start, in that workbench level's tree (or needing that level).</summary>
    public IReadOnlyList<Recipe> BlueprintPool(int tier) => tier is >= 0 and <= 3 ? _pools[tier] : Array.Empty<Recipe>();

    /// <summary>The tier a recipe is researched and found at: its tree's workbench level, else the level it needs.</summary>
    public int TierOf(Recipe recipe) => NodeOf(recipe.Id)?.Tier ?? recipe.Workbench;

    internal void SetYield(ushort item, IReadOnlyList<ItemAmount> yields) => _yields[item] = yields;

    internal void AddNode(TechNode node) => _nodes.TryAdd(node.Recipe.Id, node);

    internal void AddToPool(int tier, Recipe recipe)
    {
        if (tier is >= 0 and <= 3)
        {
            _pools[tier].Add(recipe);
        }
    }
}

/// <summary>Reads world.json (G7) and links it to the items, recipes, loot tables and deployables; then checks it.</summary>
internal static class WorldReader
{
    public const string File = "world.json";

    public static void Read(JsonFields f, JsonElement root, GameContent.Loaded loaded, Func<string, string, ItemDef?> resolve)
    {
        var world = new WorldData();
        loaded.World = world;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        f.OnlyKnown(root, File, "source", "note", "currency", "blueprint", "props", "recycler", "recycle", "research", "techTrees", "loot",
            "recyclers", "teams", "map");
        world.Currency = resolve(f.Text(root, "currency", File), File + ": currency");
        world.Blueprint = resolve(f.Text(root, "blueprint", File), File + ": blueprint");

        // The recycler's numbers first: the recycler prop carries them.
        if (f.TryChild(root, "recycler", out JsonElement r))
        {
            string where = File + ": recycler";
            f.OnlyKnown(r, where, "input", "output", "seconds", "efficiency", "batch", "note");
            world.Recycler = new RecyclerStats
            {
                InputSlots = f.Integers(r, "input", where),
                OutputSlots = f.Integers(r, "output", where),
                Seconds = f.Double(r, "seconds", where, 5),
                Efficiency = f.Double(r, "efficiency", where, 0.5),
                Batch = f.Double(r, "batch", where, 0.1),
            };
        }
        else
        {
            f.Problem(File, "\"recycler\" is missing");
        }

        var props = new List<PropDef>();
        JsonElement[] propList = f.List(root, "props", File);
        for (int i = 0; i < propList.Length; i++)
        {
            JsonElement e = propList[i];
            string key = f.Text(e, "key", $"{File}: props[{i}]");
            string where = $"{File}: prop {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "id", "name", "kind", "health", "size", "slots", "loot", "respawn", "note");
            string kindName = f.Text(e, "kind", where);
            WorldPropKind? kind = kindName switch
            {
                "barrel" => WorldPropKind.Barrel,
                "crate" => WorldPropKind.Crate,
                "recycler" => WorldPropKind.Recycler,
                _ => null,
            };
            if (kind == null)
            {
                f.Problem(where, $"kind \"{kindName}\" is not barrel, crate or recycler");
            }
            int id = f.Integer(e, "id", where);
            if (id is < 1 or > 255)
            {
                f.Problem(where, $"id {id} must be 1 … 255");
            }
            Vector3 half = Vector3.Zero;
            JsonElement[] size = f.List(e, "size", where);
            if (size.Length == 3 && size.All(s => s.ValueKind == JsonValueKind.Number))
            {
                half = new Vector3((float)size[0].GetDouble(), (float)size[1].GetDouble(), (float)size[2].GetDouble());
            }
            else
            {
                f.Problem(where, "\"size\" must be three numbers (half extents x, y, z)");
            }
            string? lootKey = f.Has(e, "loot") ? f.Text(e, "loot", where) : null;
            string name = f.Text(e, "name", where);
            var prop = new PropDef
            {
                Id = (byte)Math.Clamp(id, 0, 255),
                Key = key,
                Name = name,
                Kind = kind ?? WorldPropKind.Crate,
                LootKey = lootKey,
                Loot = lootKey != null ? loaded.Loot.FirstOrDefault(t => t.Key == lootKey) : null,
                RespawnSeconds = f.Double(e, "respawn", where, 0),
            };
            if (lootKey != null && prop.Loot == null)
            {
                f.Problem(where, $"no loot table \"{lootKey}\" in loot.json");
            }
            // The deployable it lives as: an item of its own that no inventory holds, so the wire's look is the prop's ID.
            prop.Deployable = new DeployableDef
            {
                Key = key,
                Item = new ItemDef { Id = prop.Id, Key = key, Name = name, Category = "world" },
                Health = f.Number(e, "health", where, 1000),
                HalfExtents = half,
                MaxSlopeDegrees = 60,
                Solid = true,
                Slots = f.Integer(e, "slots", where, 0),
                TakeOnly = prop.Kind == WorldPropKind.Crate,
                Recycler = prop.Kind == WorldPropKind.Recycler ? world.Recycler : null,
            };
            props.Add(prop);
        }
        world.Props = props;

        foreach (JsonElement e in f.List(root, "recycle", File))
        {
            string key = f.Text(e, "item", File + ": recycle");
            string where = $"{File}: recycle {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "item", "yield", "note");
            ItemDef? item = resolve(key, where);
            var yields = new List<ItemAmount>();
            foreach (JsonElement y in f.List(e, "yield", where))
            {
                if (f.Tuple(y, where + ".yield", 1, out string k, out double[] v) && resolve(k, where + ".yield") is { } def)
                {
                    yields.Add(new ItemAmount(def, (int)v[0]));
                }
            }
            if (item != null)
            {
                if (world.FixedYield(item.Id) != null)
                {
                    f.Problem(where, "is listed twice");
                }
                world.SetYield(item.Id, yields);
            }
        }

        if (f.TryChild(root, "research", out JsonElement research))
        {
            string where = File + ": research";
            f.OnlyKnown(research, where, "table", "item", "salvage", "output", "seconds", "cost", "note");
            world.Research = new ResearchStats
            {
                TableKey = f.Text(research, "table", where),
                ItemSlot = f.Integer(research, "item", where, 0),
                SalvageSlot = f.Integer(research, "salvage", where, 1),
                OutputSlot = f.Integer(research, "output", where, 2),
                Seconds = f.Double(research, "seconds", where, 10),
                Costs = f.Integers(research, "cost", where),
            };
            DeployableDef? table = loaded.Deployables.FirstOrDefault(d => d.Key == world.Research.TableKey);
            if (table == null)
            {
                f.Problem(where, $"no deployable \"{world.Research.TableKey}\" in deployables.json");
            }
            else
            {
                table.Research = world.Research;
            }
        }
        else
        {
            f.Problem(File, "\"research\" is missing");
        }

        // Tech trees: nodes by recipe, then their links; a recipe in a tree is not known from the start.
        var recipes = loaded.Recipes.ToDictionary(x => x.Output.Key, StringComparer.Ordinal);
        var trees = new List<TechTree>();
        foreach (JsonElement t in f.List(root, "techTrees", File))
        {
            int bench = f.Integer(t, "workbench", File + ": techTrees");
            string where = $"{File}: tech tree {bench}";
            f.OnlyKnown(t, where, "workbench", "nodes", "note");
            var nodes = new List<TechNode>();
            foreach (JsonElement n in f.List(t, "nodes", where))
            {
                string key = f.Text(n, "item", where);
                string nw = $"{where}: {JsonFields.Quote(key)}";
                f.OnlyKnown(n, nw, "item", "cost", "after", "note");
                if (!recipes.TryGetValue(key, out Recipe? recipe))
                {
                    f.Problem(nw, $"no recipe makes \"{key}\"");
                    continue;
                }
                var node = new TechNode { Recipe = recipe, Tier = bench, Cost = f.Integer(n, "cost", nw), AfterKeys = f.Texts(n, "after", nw) };
                nodes.Add(node);
                if (world.NodeOf(recipe.Id) != null)
                {
                    f.Problem(nw, "is in more than one tech tree");
                }
                world.AddNode(node);
            }
            foreach (TechNode node in nodes)
            {
                var after = new List<TechNode>();
                foreach (string key in node.AfterKeys)
                {
                    TechNode? before = nodes.FirstOrDefault(x => x.Item.Key == key);
                    if (before == null)
                    {
                        f.Problem($"{where}: {JsonFields.Quote(node.Item.Key)}", $"\"after\": \"{key}\" is not a node of this tree");
                    }
                    else
                    {
                        after.Add(before);
                    }
                }
                node.After = after;
            }
            foreach (TechNode node in nodes)
            {
                node.Depth = DepthOf(node, 0);
            }
            trees.Add(new TechTree { Workbench = bench, Nodes = nodes });
        }
        world.TechTrees = trees;
        foreach (Recipe recipe in loaded.Recipes)
        {
            recipe.Known &= world.NodeOf(recipe.Id) == null;
        }
        foreach (Recipe recipe in loaded.Recipes.Where(x => !x.Known))
        {
            world.AddToPool(world.TierOf(recipe), recipe);
        }

        if (f.TryChild(root, "loot", out JsonElement loot))
        {
            string where = File + ": loot";
            f.OnlyKnown(loot, where, "radius", "cell", "clear", "spread", "coast", "forestEdge", "open", "barrels", "fuelBarrel", "crate",
                "military", "landing", "note");
            int[] barrels = f.Integers(loot, "barrels", where);
            JsonElement[] landing = f.List(loot, "landing", where, required: false);
            world.Loot = new LootSpotSettings
            {
                Radius = f.Double(loot, "radius", where),
                Cell = f.Double(loot, "cell", where),
                Clear = f.Double(loot, "clear", where),
                Spread = f.Double(loot, "spread", where),
                Coast = f.Double(loot, "coast", where),
                ForestEdge = f.Double(loot, "forestEdge", where),
                Open = f.Double(loot, "open", where),
                LeastBarrels = barrels.Length == 2 ? barrels[0] : 2,
                MostBarrels = barrels.Length == 2 ? barrels[1] : 4,
                FuelBarrel = f.Double(loot, "fuelBarrel", where),
                Crate = f.Double(loot, "crate", where),
                Military = f.Double(loot, "military", where),
                Landing = landing.Length == 3 && landing.All(x => x.ValueKind == JsonValueKind.Number)
                    ? new Vector3((float)landing[0].GetDouble(), (float)landing[1].GetDouble(), (float)landing[2].GetDouble())
                    : null,
            };
            if (barrels.Length != 2)
            {
                f.Problem(where, "\"barrels\" must be [least, most]");
            }
        }
        else
        {
            f.Problem(File, "\"loot\" is missing");
        }

        if (f.TryChild(root, "recyclers", out JsonElement recyclers))
        {
            string where = File + ": recyclers";
            f.OnlyKnown(recyclers, where, "count", "points", "note");
            world.RecyclerCount = f.Integer(recyclers, "count", where);
            var points = new List<Vector2>();
            foreach (JsonElement p in f.List(recyclers, "points", where))
            {
                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() == 2 && p[0].ValueKind == JsonValueKind.Number && p[1].ValueKind == JsonValueKind.Number)
                {
                    points.Add(new Vector2((float)p[0].GetDouble(), (float)p[1].GetDouble()));
                }
                else
                {
                    f.Problem(where, "each point is [x, z] in metres from the zone's origin");
                }
            }
            world.RecyclerPoints = points;
        }
        if (f.TryChild(root, "teams", out JsonElement teams))
        {
            f.OnlyKnown(teams, File + ": teams", "size", "inviteSeconds", "note");
            world.TeamSize = f.Integer(teams, "size", File + ": teams", 8);
            world.InviteSeconds = f.Double(teams, "inviteSeconds", File + ": teams", 300);
        }
        if (f.TryChild(root, "map", out JsonElement map))
        {
            f.OnlyKnown(map, File + ": map", "markers", "metres", "cell", "note");
            world.MaxMarkers = f.Integer(map, "markers", File + ": map", 5);
            world.MapMetres = f.Double(map, "metres", File + ": map", 3900);
            world.MapCell = f.Double(map, "cell", File + ": map", 150);
        }
    }

    // Steps from a root along the shortest path back (a cycle reads as deep, and the check below reports it).
    private static int DepthOf(TechNode node, int guard)
    {
        if (node.After.Count == 0 || guard > 64)
        {
            return guard > 64 ? 64 : 0;
        }
        return 1 + node.After.Min(n => DepthOf(n, guard + 1));
    }

    /// <summary>The checks of world.json against the rest (GROUND.md §7).</summary>
    public static void Check(GameContent.Loaded c, List<string> problems)
    {
        void Problem(string where, string what) => problems.Add($"{where}: {what}");
        WorldData? w = c.World;
        if (w == null)
        {
            return;
        }
        if (w.Currency != null && (w.Currency.StackSize < 100 || w.Currency.HasCondition))
        {
            Problem(File + ": currency", "salvage stacks (at least 100) and has no condition");
        }
        if (w.Blueprint != null && (w.Blueprint.StackSize != 1 || w.Blueprint.HasCondition || w.Blueprint.Deploys != null))
        {
            Problem(File + ": blueprint", "the blueprint item stacks to 1 and has no condition of its own (it holds the recipe)");
        }
        foreach (IGrouping<int, PropDef> g in w.Props.GroupBy(p => (int)p.Id).Where(g => g.Count() > 1))
        {
            Problem(File, $"prop id {g.Key} is used by {string.Join(", ", g.Select(p => p.Key))}");
        }
        foreach (IGrouping<string, PropDef> g in w.Props.GroupBy(p => p.Key).Where(g => g.Count() > 1))
        {
            Problem(File, $"prop \"{g.Key}\" is listed twice");
        }
        foreach (PropDef p in w.Props)
        {
            string where = $"{File}: prop \"{p.Key}\"";
            DeployableDef d = p.Deployable;
            if (d.Health <= 0 || d.HalfExtents.X <= 0 || d.HalfExtents.Y <= 0 || d.HalfExtents.Z <= 0 || d.Slots is < 1 or > 64)
            {
                Problem(where, "health and size positive, 1 … 64 slots");
            }
            if (p.Kind != WorldPropKind.Recycler && (p.Loot == null || p.RespawnSeconds <= 0))
            {
                Problem(where, "a barrel or crate needs a loot table and a respawn time");
            }
            if (p.Kind == WorldPropKind.Recycler && p.LootKey != null)
            {
                Problem(where, "a recycler has no loot");
            }
            if (p.Loot is { } t && t.Guaranteed.Count + t.MostRolls > d.Slots)
            {
                Problem(where, $"its {d.Slots} slots can't hold what \"{t.Key}\" gives at most ({t.Guaranteed.Count + t.MostRolls} stacks)");
            }
        }
        if (w.Props.Count(p => p.Kind == WorldPropKind.Recycler) != 1 && w.RecyclerCount > 0)
        {
            Problem(File, "exactly one recycler prop when recyclers stand in the world");
        }
        RecyclerStats r = w.Recycler;
        int recyclerSlots = w.Props.FirstOrDefault(p => p.Kind == WorldPropKind.Recycler)?.Deployable.Slots ?? 0;
        int[] all = r.InputSlots.Concat(r.OutputSlots).ToArray();
        if (r.InputSlots.Count == 0 || r.OutputSlots.Count == 0 || all.Distinct().Count() != all.Length || all.Any(s => s < 0 || s >= recyclerSlots))
        {
            Problem(File + ": recycler", "input and output slots, distinct and within the recycler's slots");
        }
        if (r.Seconds <= 0 || r.Efficiency is <= 0 or > 1 || r.Batch is <= 0 or > 1)
        {
            Problem(File + ": recycler", "seconds positive, efficiency and batch 0 … 1");
        }
        foreach (ItemDef item in c.Items)
        {
            if (w.FixedYield(item.Id) is not { } yields)
            {
                continue;
            }
            string where = $"{File}: recycle \"{item.Key}\"";
            if (yields.Count == 0 || yields.Any(y => y.Count < 1 || y.Item == item))
            {
                Problem(where, "yields at least one unit of each, never itself");
            }
            if (item == w.Currency || item == w.Blueprint)
            {
                Problem(where, "salvage and blueprints aren't recycled");
            }
        }
        ResearchStats rs = w.Research;
        DeployableDef? table = c.Deployables.FirstOrDefault(d => d.Research == rs);
        int[] researchSlots = { rs.ItemSlot, rs.SalvageSlot, rs.OutputSlot };
        if (table != null && (researchSlots.Distinct().Count() != 3 || researchSlots.Any(x => x < 0 || x >= table.Slots) || table.Device != null))
        {
            Problem(File + ": research", "the item, salvage and output slots, distinct and within the table's slots (not a furnace)");
        }
        if (rs.Seconds <= 0 || rs.Costs.Count != 4 || rs.Costs.Any(x => x < 0))
        {
            Problem(File + ": research", "seconds positive; four costs (no workbench, levels 1 to 3), none negative");
        }
        foreach (TechTree tree in w.TechTrees)
        {
            string where = $"{File}: tech tree {tree.Workbench}";
            if (tree.Workbench is < 1 or > 3 || w.TechTrees.Count(t => t.Workbench == tree.Workbench) > 1)
            {
                Problem(where, "workbench levels 1 … 3, one tree each");
            }
            foreach (TechNode n in tree.Nodes)
            {
                if (n.Cost < 1 || n.Recipe.Workbench > tree.Workbench || n.Depth >= 64)
                {
                    Problem($"{where}: \"{n.Item.Key}\"", "a positive cost, a recipe this bench or a lower one makes, and no loop through \"after\"");
                }
            }
        }
        foreach (LootTable t in c.Loot)
        {
            foreach (LootTable.Entry e in t.Entries.Concat(t.Guaranteed))
            {
                bool blueprint = e.Item == w.Blueprint;
                if (blueprint != e.Tier >= 0)
                {
                    Problem($"loot.json: \"{t.Key}\"", blueprint ? "a blueprint entry needs a fifth number, the workbench level" : $"only blueprints take a workbench level ({e.Item.Key})");
                }
                else if (blueprint && (e.Least != 1 || e.Most != 1 || w.BlueprintPool(e.Tier).Count == 0))
                {
                    Problem($"loot.json: \"{t.Key}\"", $"a blueprint entry gives one, of a level with recipes to learn ({e.Tier})");
                }
            }
        }
        LootSpotSettings l = w.Loot;
        if (l.Radius <= 0 || l.Cell < 10 || l.Clear < 0 || l.Spread <= 0 || l.LeastBarrels < 0 || l.MostBarrels < l.LeastBarrels ||
            new[] { l.Coast, l.ForestEdge, l.Open, l.FuelBarrel, l.Crate, l.Military }.Any(x => x is < 0 or > 1) ||
            l.Radius > Physics.PhysicsWorld.Extent - Physics.PhysicsWorld.TileSize)
        {
            Problem(File + ": loot", "radius within the zone's ground, cells of 10 m or more, chances 0 … 1, barrels [least ≤ most]");
        }
        foreach (string key in new[] { "barrel", "crate" })
        {
            if (!w.Props.Any(p => p.Kind == (key == "barrel" ? WorldPropKind.Barrel : WorldPropKind.Crate)))
            {
                Problem(File, $"no {key} prop for the loot spots");
            }
        }
        if (w.RecyclerCount < 0 || w.RecyclerCount > w.RecyclerPoints.Count)
        {
            Problem(File + ": recyclers", "at most as many as there are points to try");
        }
        if (w.TeamSize is < 2 or > 16 || w.InviteSeconds <= 0)
        {
            Problem(File + ": teams", "2 … 16 members, invites last a while");
        }
        if (w.MaxMarkers is < 1 or > 10 || w.MapMetres is < 500 or > 16000 || w.MapCell < 25 || w.MapMetres / w.MapCell > 52)
        {
            Problem(File + ": map", "1 … 10 markers, 500 … 16,000 m across, cells of 25 m or more and at most 52 a side");
        }
    }
}
