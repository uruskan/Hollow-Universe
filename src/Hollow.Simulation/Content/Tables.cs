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

    /// <summary>Known from the start (the tech tree of G7 will hide some).</summary>
    public bool Known { get; init; } = true;

    public ushort Id => Output.Id;

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

    public bool IsHarvested => Class is NaturalClass.Tree or NaturalClass.Node;

    public bool IsPicked => Class is NaturalClass.Pickup or NaturalClass.Bush;

    /// <summary>Solid to walk into (trees, outcrops and boulders).</summary>
    public bool IsSolid => IsHarvested || Class == NaturalClass.Boulder;

    public override string ToString() => Key;
}

/// <summary>Rust's weak spot on trees and outcrops (gather.json).</summary>
public readonly record struct WeakSpot(float Radius, float Bonus);

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

    public float MaxSlopeDegrees { get; init; } = 30;

    /// <summary>Blocks movement (a Jolt body); sleeping bags and campfires don't.</summary>
    public bool Solid { get; init; } = true;

    /// <summary>A respawn point with this cooldown in seconds (sleeping bags); 0 if not one.</summary>
    public float RespawnSeconds { get; init; }

    /// <summary>Storage slots (0: none).</summary>
    public int Slots { get; init; }

    public DeviceStats? Device { get; init; }

    /// <summary>The workbench level it gives within reach (0: not a workbench).</summary>
    public int Workbench { get; init; }

    /// <summary>The ID on the wire and in saves: its item's.</summary>
    public ushort Id => Item.Id;

    public bool IsRespawnPoint => RespawnSeconds > 0;

    public override string ToString() => Key;
}

/// <summary>A loot table (loot.json).</summary>
public sealed class LootTable
{
    public readonly record struct Entry(ItemDef Item, int Least, int Most, double Weight);

    public string Key { get; init; } = "";

    public IReadOnlyList<Entry> Guaranteed { get; init; } = Array.Empty<Entry>();

    public int LeastRolls { get; init; }

    public int MostRolls { get; init; }

    public IReadOnlyList<Entry> Entries { get; init; } = Array.Empty<Entry>();

    /// <summary>The items it gives, from a seeded stream (deterministic for a seed).</summary>
    public List<ItemAmount> Roll(ref Rng rng)
    {
        var result = new List<ItemAmount>();
        foreach (Entry e in Guaranteed)
        {
            result.Add(new ItemAmount(e.Item, e.Least + rng.NextInt(e.Most - e.Least + 1)));
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
                    Entry e = Entries[pick];
                    result.Add(new ItemAmount(e.Item, e.Least + rng.NextInt(e.Most - e.Least + 1)));
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

    public float CorpseSeconds { get; init; } = 300;
    public float DroppedItemSeconds { get; init; } = 300;
    public float InteractReach { get; init; } = 3;
    public float LootReach { get; init; } = 3.5f;
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
    public float HelpReach { get; init; } = 2.5f;

    public float RegionMultiplier(HitRegion region) => region switch
    {
        HitRegion.Head => HeadMultiplier,
        HitRegion.Limb => LimbMultiplier,
        _ => BodyMultiplier,
    };
}
