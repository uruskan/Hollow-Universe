using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Simulation.Content;

/// <summary>An amount of an item: a recipe's ingredient, a yield.</summary>
public readonly record struct ItemAmount(ItemDef Item, int Count);

/// <summary>A crafting recipe (recipes.json). Its ID on the wire is its output item's ID: one recipe per output.</summary>
public sealed class Recipe
{
    public ItemDef Output { get; init; } = null!;

    public int Amount { get; init; } = 1;

    public float Seconds { get; init; }

    /// <summary>The workbench level needed within reach (0: none).</summary>
    public int Workbench { get; init; }

    public IReadOnlyList<ItemAmount> Ingredients { get; init; } = Array.Empty<ItemAmount>();

    /// <summary>
    /// Known from the start (Rust's default blueprints): a recipe in no tech tree (world.json, G7), unless recipes.json
    /// says <c>"known": false</c>. The rest are learnt: unlocked in a tech tree, researched, or from a blueprint.
    /// </summary>
    public bool Known { get; internal set; } = true;

    /// <summary>A deployable that must be within reach to start it (G6: ships at the vehicle workbench), or null.</summary>
    public DeployableDef? Station { get; internal set; }

    public string? StationKey { get; init; }

    private readonly ushort? _id;
    private readonly string? _key;

    /// <summary>
    /// The recipe's number (the wire, the crafting queue and saves use it): its output's item ID, unless the recipe is a
    /// second way to make that item (G7: lamp oil from animal fat beside lamp oil from charcoal), which has a number of
    /// its own that no item uses.
    /// </summary>
    public ushort Id { get => _id ?? Output.Id; init => _id = value; }

    /// <summary>Its name in data and in the interface: its output's key, or its own for a second way to make the item.</summary>
    public string Key { get => _key ?? Output.Key; init => _key = value; }

    /// <summary>Ticks to make one unit.</summary>
    public int UnitTicks => Math.Max(1, (int)Math.Round(Seconds * CharacterTicks.Rate));

    public override string ToString() => Output.Key;
}

/// <summary>How a natural object is gathered.</summary>
public enum NaturalClass : byte
{
    /// <summary>Chopped with a tool's tree rate; felled at 0 health.</summary>
    Tree,

    /// <summary>Stone or ore outcrop: broken with a tool's ore rate, through depletion stages.</summary>
    Node,

    /// <summary>Picked up whole with the interact key (piles, plants); gone until it respawns.</summary>
    Pickup,

    /// <summary>Picked with the interact key but stays, bare, until it grows back (berry bushes).</summary>
    Bush,

    /// <summary>Only stands in the way: a solid rock that gives nothing (the planet's boulders).</summary>
    Boulder,
}

/// <summary>A kind of natural object (gather.json): what it gives and how it is gathered.</summary>
public sealed class NaturalKind
{
    /// <summary>Position in gather.json: a small number for the wire and the providers.</summary>
    public ushort Index { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public NaturalClass Class { get; init; }

    /// <summary>Health of a full-size one (trees and outcrops; scaled by each object's size).</summary>
    public float Health { get; init; }

    /// <summary>Trees and outcrops: the whole amount over all their health. Pickups and bushes: one pick.</summary>
    public IReadOnlyList<ItemAmount> Yield { get; init; } = Array.Empty<ItemAmount>();

    /// <summary>Outcrops shrink through this many stages as they lose health.</summary>
    public int Stages { get; init; } = 1;

    public float RespawnSeconds { get; init; }

    /// <summary>Collision and hit shape: trees a vertical capsule, outcrops a sphere; reach for pickups.</summary>
    public float Radius { get; init; }

    public float Height { get; init; }

    /// <summary>How many the stand-in places per 64 m tile (stand-in zones only; real surfaces use the scatter).</summary>
    public float StandInPerTile { get; init; }

    /// <summary>
    /// Which of the planet's scatter kinds are this kind (GROUND.md §6.1, gather.json <c>scatter</c>): every scatter
    /// object of one of them, and at least <see cref="ScatterMinScale"/> in size, is one of these.
    /// </summary>
    public IReadOnlyList<Surface.ScatterKind> ScatterKinds { get; init; } = Array.Empty<Surface.ScatterKind>();

    public float ScatterMinScale { get; init; }

    /// <summary>
    /// How it is gathered (gather.json <c>classes</c> of its class, with its own drawn surface and look): the tool rate
    /// it takes, its weak spot, what the last hit pays, how it shows. Null for what isn't hit for resources.
    /// </summary>
    public GatherRules? Gather { get; internal set; }

    public bool IsHarvested => Class is NaturalClass.Tree or NaturalClass.Node;

    public bool IsPicked => Class is NaturalClass.Pickup or NaturalClass.Bush;

    /// <summary>Solid to walk into (trees, outcrops and boulders).</summary>
    public bool IsSolid => IsHarvested || Class == NaturalClass.Boulder;

    public override string ToString() => Key;
}

/// <summary>A device's slots and what it does (deployables.json, <c>device</c>).</summary>
public sealed class DeviceStats
{
    public IReadOnlyList<int> FuelSlots { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> InputSlots { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> OutputSlots { get; init; } = Array.Empty<int>();

    /// <summary>Which items it cooks: those whose <see cref="CookStats.In"/> is this.</summary>
    public string Cooks { get; init; } = "";

    /// <summary>Warmth while lit: within this radius, this many degrees.</summary>
    public float WarmthRadius { get; init; }

    public float WarmthCelsius { get; init; }
}

/// <summary>A placeable object (deployables.json).</summary>
public sealed class DeployableDef
{
    public string Key { get; init; } = "";

    public ItemDef Item { get; init; } = null!;

    public float Health { get; init; }

    /// <summary>Half extents in metres: x across, y up, z along (its yaw turns x and z).</summary>
    public Vector3 HalfExtents { get; init; }

    /// <summary>Blocks movement (a Jolt body); sleeping bags and campfires don't.</summary>
    public bool Solid { get; init; } = true;

    /// <summary>A respawn point with this cooldown in seconds (sleeping bags); 0 if not one.</summary>
    public float RespawnSeconds { get; init; }

    /// <summary>Storage slots (0: none).</summary>
    public int Slots { get; init; }

    public DeviceStats? Device { get; init; }

    /// <summary>The workbench level it gives within reach (0: not a workbench).</summary>
    public int Workbench { get; init; }

    /// <summary>Its storage is take only (a world crate, G7).</summary>
    public bool TakeOnly { get; init; }

    /// <summary>
    /// Picked back up as its item with E (deployables.json <c>pickup</c>, GROUND_SYSTEMS.md §11.3): when empty, where the
    /// player has building rights (Rust's).
    /// </summary>
    public bool Pickup { get; init; }

    /// <summary>A recycler (G7): its input and output slots and pace (world.json); null for anything else.</summary>
    public RecyclerStats? Recycler { get; internal set; }

    /// <summary>A research table (G7): its item and salvage slots and costs (world.json); null for anything else.</summary>
    public ResearchStats? Research { get; internal set; }

    /// <summary>A machine with slots that do different things: a campfire or furnace, a recycler, a research table.</summary>
    public bool IsMachine => Device != null || Recycler != null || Research != null;

    /// <summary>Where a ship it builds rolls out (a vehicle workbench, G6), or null.</summary>
    public BayStats? Bay { get; init; }

    /// <summary>The ID on the wire and in saves: its item's.</summary>
    public ushort Id => Item.Id;

    public bool IsRespawnPoint => RespawnSeconds > 0;

    public override string ToString() => Key;
}

/// <summary>
/// A station's bay (deployables.json, G6): where a ship it builds rolls out, in the station's frame (x across, y up, z out
/// of its front), the half size of the space that must be clear there, and the steepest ground it may stand on.
/// </summary>
public sealed class BayStats
{
    public Vector3 At { get; init; }

    public Vector3 Clear { get; init; }

    public float MaxSlopeDegrees { get; init; } = 20;
}

/// <summary>A loot table (loot.json).</summary>
public sealed class LootTable
{
    /// <summary>One line of a table; <paramref name="Tier"/> ≥ 0 makes it a blueprint of that workbench level (G7).</summary>
    public readonly record struct Entry(ItemDef Item, int Least, int Most, double Weight, int Tier = -1);

    public string Key { get; init; } = "";

    public IReadOnlyList<Entry> Guaranteed { get; init; } = Array.Empty<Entry>();

    public int LeastRolls { get; init; }

    public int MostRolls { get; init; }

    public IReadOnlyList<Entry> Entries { get; init; } = Array.Empty<Entry>();

    /// <summary>The items it gives, from a seeded stream (deterministic for a seed). Blueprints are left out (see <see cref="RollStacks"/>).</summary>
    public List<ItemAmount> Roll(ref Rng rng)
    {
        var result = new List<ItemAmount>();
        foreach (Entry e in Guaranteed)
        {
            if (e.Tier < 0)
            {
                result.Add(new ItemAmount(e.Item, e.Least + rng.NextInt(e.Most - e.Least + 1)));
            }
        }
        if (Entries.Count > 0)
        {
            int rolls = LeastRolls + rng.NextInt(MostRolls - LeastRolls + 1);
            Span<double> weights = stackalloc double[Entries.Count];
            for (int i = 0; i < Entries.Count; i++)
            {
                weights[i] = Entries[i].Weight;
            }
            for (int r = 0; r < rolls; r++)
            {
                int pick = rng.PickWeighted(weights);
                if (pick >= 0 && Entries[pick].Tier < 0)
                {
                    Entry e = Entries[pick];
                    result.Add(new ItemAmount(e.Item, e.Least + rng.NextInt(e.Most - e.Least + 1)));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// What it gives as stacks (the world's barrels and crates, G7): each roll a stack no bigger than the item's stack
    /// size, and a blueprint entry a blueprint of a recipe drawn from its workbench level's pool (world.json), its
    /// condition holding the recipe. Deterministic for a seed.
    /// </summary>
    public List<Items.ItemStack> RollStacks(ref Rng rng, GameContent content)
    {
        var result = new List<Items.ItemStack>();
        void Add(Entry e, ref Rng r)
        {
            int count = e.Least + r.NextInt(e.Most - e.Least + 1);
            if (e.Tier >= 0)
            {
                IReadOnlyList<Recipe> pool = content.World.BlueprintPool(e.Tier);
                if (pool.Count > 0)
                {
                    result.Add(new Items.ItemStack(e.Item.Id, 1, pool[r.NextInt(pool.Count)].Id));
                }
                return;
            }
            while (count > 0)
            {
                int n = Math.Min(count, e.Item.StackSize);
                result.Add(Items.ItemStack.Of(e.Item, n));
                count -= n;
            }
        }
        foreach (Entry e in Guaranteed)
        {
            Add(e, ref rng);
        }
        if (Entries.Count > 0)
        {
            int rolls = LeastRolls + rng.NextInt(MostRolls - LeastRolls + 1);
            Span<double> weights = stackalloc double[Entries.Count];
            for (int i = 0; i < Entries.Count; i++)
            {
                weights[i] = Entries[i].Weight;
            }
            for (int r = 0; r < rolls; r++)
            {
                int pick = rng.PickWeighted(weights);
                if (pick >= 0)
                {
                    Add(Entries[pick], ref rng);
                }
            }
        }
        return result;
    }
}

/// <summary>A crafting tab (categories.json).</summary>
public sealed record Category(string Key, string Name);

/// <summary>The survival numbers (survival.json). Seconds as given; the rules convert.</summary>
public sealed class SurvivalTuning
{
    public float MaxHealth { get; init; } = 100;
    public float MaxCalories { get; init; } = 500;
    public float MaxHydration { get; init; } = 250;
    public float SpawnHealth { get; init; } = 60;
    public float SpawnCalories { get; init; } = 260;
    public float SpawnHydration { get; init; } = 160;

    public float CaloriesPerSecond { get; init; }
    public float HydrationPerSecond { get; init; }
    public float SprintFactor { get; init; } = 1;
    public float ColdFactor { get; init; } = 1;
    public float StarvationDamagePerSecond { get; init; }
    public float ThirstDamagePerSecond { get; init; }
    public float RegenMinCalories { get; init; }
    public float RegenMinHydration { get; init; }
    public float RegenPerSecond { get; init; }
    public float ComfortRegenFactor { get; init; } = 1;
    public float HealPerSecond { get; init; } = 1;

    public float BleedDamagePerSecond { get; init; } = 1;
    public float BleedClotPerSecond { get; init; }

    /// <summary>Below this felt temperature (°C) a character starts to be cold; fully cold this many degrees lower.</summary>
    public float ComfortableCelsius { get; init; } = 5;
    public float ColdScale { get; init; } = 15;
    public float ColdDamageFrom { get; init; } = 0.6f;
    public float ColdDamagePerSecond { get; init; }
    public float WetCooling { get; init; }

    /// <summary>A fire warms the air felt up to this, never above it (°C).</summary>
    public float FireWarmsTo { get; init; } = 25;
    public float WetSeconds { get; init; }

    public float ClimateMeanCelsius { get; init; } = 18;
    public float ClimateDaySwing { get; init; } = 3.5f;
    public float ClimateLapsePerKilometre { get; init; } = 6.5f;
    public float DayLengthSeconds { get; init; } = 5235;

    public float HeadMultiplier { get; init; } = 2;
    public float BodyMultiplier { get; init; } = 1;
    public float LimbMultiplier { get; init; } = 0.5f;
    public float MaxRewindSeconds { get; init; } = 0.25f;
    public float EquipSeconds { get; init; } = 0.3f;

    /// <summary>
    /// How long loose things lie (survival.json's <c>lifetimes</c>, GROUND_SYSTEMS.md §3.7): a bag of dropped items (from
    /// the newest item put in), a body (a dead player's, a ship's wreck).
    /// </summary>
    public LifetimeRule BagLifetime { get; init; } = new(300, LifetimeRestart.Deposit, true);

    public LifetimeRule BodyLifetime { get; init; } = new(300, LifetimeRestart.None, true);

    /// <summary>A dud (a charge whose fuse went out) nobody lights again.</summary>
    public LifetimeRule DudLifetime { get; init; } = new(600, LifetimeRestart.None, false);

    /// <summary>A spent arrow stuck where it hit (GROUND_SYSTEMS.md §5): it lies this long unless someone takes it.</summary>
    public LifetimeRule ArrowLifetime { get; init; } = new(300, LifetimeRestart.None, false);

    /// <summary>At most this many spent arrows in a zone: the oldest goes first (survival.json's <c>arrows</c>).</summary>
    public int ArrowsMost { get; init; } = 600;

    /// <summary>How deep a spent arrow's tip is in what it hit, metres.</summary>
    public float ArrowDepth { get; init; } = 0.1f;

    /// <summary>
    /// Bags of dropped items (GROUND.md §6.1): what they are called, their slots, and how near a bag (metres) loose
    /// items must land to go into it rather than into a bag of their own.
    /// </summary>
    public string BagName { get; init; } = "Loot bag";
    public int BagSlots { get; init; } = 48;
    public float BagMergeRadius { get; init; } = 1.5f;
    /// <summary>
    /// The reaches (GROUND_SYSTEMS.md §3.4, survival.json's <c>reach</c>), each from the eye to the target's nearest
    /// surface (<c>Zone.Reach</c>): picking up and lighting a dud; opening, lighting, doors, locks and the claim device; an
    /// open container closes beyond <see cref="KeepOpenReach"/>; helping the wounded up; a team invite; a workbench's level.
    /// </summary>
    public float InteractReach { get; init; } = 3;

    public float OpenReach { get; init; } = 3.5f;

    public float KeepOpenReach { get; init; } = 4.5f;

    public float HelpReach { get; init; } = 3f;

    public float InviteReach { get; init; } = 6f;

    public float StationReach { get; init; } = 4f;
    public float BeachCooldownSeconds { get; init; }
    public string RespawnKitKey { get; init; } = "respawn-kit";

    public int IntentBurst { get; init; } = 16;
    public float IntentsPerSecond { get; init; } = 20;

    /// <summary>Shots and reloads have their own budget (RustMVP's weapon budget: 20 at once, 30 a second).</summary>
    public int GunBurst { get; init; } = 20;
    public float GunActionsPerSecond { get; init; } = 30;

    /// <summary>Health set burning (incendiary rounds) is lost at this rate (G4).</summary>
    public float BurnDamagePerSecond { get; init; } = 2;

    /// <summary>
    /// The wounded state (G4, Rust's): a lethal hit smaller than <see cref="WoundMaxDamage"/> knocks the player down with
    /// <see cref="WoundedHealthLeast"/> … <see cref="WoundedHealthMost"/> health for <see cref="WoundedSecondsLeast"/> …
    /// <see cref="WoundedSecondsMost"/> seconds; then they get up by themselves with <see cref="WoundedRecoverChance"/>
    /// (plus up to <see cref="WoundedFedBonus"/> when fed and watered) or die. Others help them up within
    /// <see cref="HelpReach"/>.
    /// </summary>
    public float WoundMaxDamage { get; init; } = 75;
    public float WoundedHealthLeast { get; init; } = 7;
    public float WoundedHealthMost { get; init; } = 12;
    public float WoundedSecondsLeast { get; init; } = 40;
    public float WoundedSecondsMost { get; init; } = 50;
    public float WoundedRecoverChance { get; init; } = 0.2f;
    public float WoundedFedBonus { get; init; } = 0.25f;

    public float RegionMultiplier(HitRegion region) => region switch
    {
        HitRegion.Head => HeadMultiplier,
        HitRegion.Limb => LimbMultiplier,
        _ => BodyMultiplier,
    };
}

/// <summary>What starts a loose thing's time again (GROUND_SYSTEMS.md §3.7).</summary>
public enum LifetimeRestart : byte
{
    None,

    /// <summary>Something put in it (a bag's time runs from its newest item).</summary>
    Deposit,
}

/// <summary>
/// How long a kind of loose thing lies in the world (GROUND_SYSTEMS.md §3.7, survival.json's <c>lifetimes</c>): seconds of
/// world time (paused while the server is down), what starts it again, and whether it goes at once when emptied.
/// </summary>
public sealed record LifetimeRule(float Seconds, LifetimeRestart Restart, bool EndsWhenEmptied)
{
    public long Ticks => CharacterTicks.From(Seconds);
}
