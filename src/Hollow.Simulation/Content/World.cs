using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
