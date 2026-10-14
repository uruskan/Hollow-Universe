using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Combat;
using Hollow.Surface;

namespace Hollow.Simulation.Content;

/// <summary>How an animal meets a threat (GROUND.md §6.9).</summary>
public enum Temperament : byte
{
    /// <summary>Runs from anything it notices (the deer).</summary>
    Flee,

    /// <summary>Leaves people alone until hurt or crowded, then charges (the boar).</summary>
    Defend,

    /// <summary>Hunts when hungry, defends otherwise (the wolf, the bear).</summary>
    Hunt,
}

/// <summary>How an animal's attack lands.</summary>
public enum AttackKind : byte
{
    /// <summary>A lunge and a bite (the wolf).</summary>
    Bite,

    /// <summary>A charge that ends in a gore with the tusks (the boar).</summary>
    Charge,

    /// <summary>A swipe of a paw (the bear).</summary>
    Swipe,
}

/// <summary>An animal's attack (animals.json <c>attack</c>): damage before armour, reach from its head, one every so often.</summary>
public sealed class AttackStats
{
    public AttackKind Kind { get; init; }

    /// <summary>Damage to a character before armour; armour against melee on <see cref="Region"/> takes its share.</summary>
    public float Damage { get; init; }

    /// <summary>The bite or swipe lands when the target is within this many metres of the animal's front.</summary>
    public float Reach { get; init; }

    /// <summary>Seconds from one attack to the next.</summary>
    public float RateSeconds { get; init; }

    /// <summary>Seconds from the start of an attack to its hit: the wind-up a player can read and step out of.</summary>
    public float WindupSeconds { get; init; }

    /// <summary>Bleeding started, as a fraction of the damage.</summary>
    public float Bleed { get; init; }

    /// <summary>Where on a person it lands (which armour counts).</summary>
    public HitRegion Region { get; init; } = HitRegion.Body;

    /// <summary>Damage to another animal (wolves on deer), before its region.</summary>
    public float AnimalDamage { get; init; }

    public int RateTicks => Math.Max(1, (int)Math.Round(RateSeconds * CharacterTicks.Rate));

    public int WindupTicks => Math.Clamp((int)Math.Round(WindupSeconds * CharacterTicks.Rate), 1, RateTicks);
}

/// <summary>
/// One kind of animal (animals.json <c>species</c>, GROUND.md §6.9): health and size, speeds, senses, temperament, group
/// size, attack, where it lives and how many, how fast it comes back, and what its corpse gives.
/// </summary>
public sealed class AnimalSpecies
{
    private readonly Dictionary<Biome, float> _biomes = new();

    /// <summary>Stable small number (the wire carries it).</summary>
    public byte Id { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public float Health { get; init; }

    /// <summary>Body length (nose to tail), shoulder height and body radius, metres: the hit shapes and the steering.</summary>
    public float Length { get; init; }

    public float Height { get; init; }

    public float Radius { get; init; }

    public float HeadRadius { get; init; }

    /// <summary>Where its head is at rest: this far ahead of its feet's middle and this high (the model's; hit shapes and bites).</summary>
    public float HeadAhead { get; init; }

    public float HeadUp { get; init; }

    /// <summary>Gaits in metres a second; turning in radians a second; acceleration in m/s².</summary>
    public float WalkSpeed { get; init; }

    public float TrotSpeed { get; init; }

    public float RunSpeed { get; init; }

    public float TurnRate { get; init; }

    public float Acceleration { get; init; }

    /// <summary>Sees this far (metres) within its field of view (radians, full width); hears noises this many times as far.</summary>
    public float Sight { get; init; }

    public float Cone { get; init; }

    public float Hearing { get; init; }

    public Temperament Temperament { get; init; }

    /// <summary>A flighty animal runs from a person who comes this close (metres), and calms down beyond <see cref="Calm"/>.</summary>
    public float FleeDistance { get; init; }

    public float Calm { get; init; }

    /// <summary>A person this close provokes it (a warning first); 0 never.</summary>
    public float PersonalSpace { get; init; }

    /// <summary>Below this fraction of its health it runs instead of fighting.</summary>
    public float FleeHealth { get; init; }

    /// <summary>It gives up a chase after this many seconds, or this far from where the chase began.</summary>
    public float ChaseSeconds { get; init; }

    public float ChaseRange { get; init; }

    /// <summary>Hunger gained a second (0 … 1 is full to starving); a hunter hunts above <see cref="HuntHunger"/>.</summary>
    public float HungerRate { get; init; }

    public float HuntHunger { get; init; }

    /// <summary>Hunters chase it (deer, boars).</summary>
    public bool Prey { get; init; }

    /// <summary>A herd or pack holds this many (least, most); its members keep within <see cref="Cohesion"/> metres.</summary>
    public int GroupLeast { get; init; }

    public int GroupMost { get; init; }

    public float Cohesion { get; init; }

    public AttackStats? Attack { get; init; }

    /// <summary>Animals a square kilometre where it lives best.</summary>
    public float Density { get; init; }

    /// <summary>How much it likes open ground and woods (tree cover 0 and 1), multiplied with its biome's weight.</summary>
    public float Open { get; init; }

    public float Wooded { get; init; }

    /// <summary>The steepest ground it walks (radians).</summary>
    public float MaxSlope { get; init; }

    /// <summary>Seconds until a dead one is replaced.</summary>
    public float RespawnSeconds { get; init; }

    /// <summary>
    /// Its carcass's lifetime (GROUND_SYSTEMS.md §3.7): animals.json corpse seconds of world time, and gone at once when
    /// harvested out.
    /// </summary>
    public LifetimeRule Carcass { get; init; } = new(300, LifetimeRestart.None, true);

    public float CorpseSeconds => Carcass.Seconds;

    /// <summary>How much a corpse takes to harvest (a tool's rate a hit), and everything it gives in all.</summary>
    public float HarvestHealth { get; init; }

    public IReadOnlyList<ItemAmount> Harvest { get; internal set; } = Array.Empty<ItemAmount>();

    /// <summary>How well it lives in a biome (0: not at all).</summary>
    public float BiomeWeight(Biome biome) => _biomes.GetValueOrDefault(biome);

    public IReadOnlyDictionary<Biome, float> Biomes => _biomes;

    internal void SetBiome(Biome biome, float weight) => _biomes[biome] = weight;

    /// <summary>Its habitat's suitability for a mix of biomes (weights summing to 1) under a tree cover (0 … 1).</summary>
    public float Suitability(ReadOnlySpan<float> biomeShares, float treeCover)
    {
        float biome = 0;
        for (int i = 0; i < biomeShares.Length; i++)
        {
            biome += biomeShares[i] * BiomeWeight((Biome)i);
        }
        return biome * (Open + (Wooded - Open) * Math.Clamp(treeCover, 0, 1));
    }

    public override string ToString() => Key;
}

/// <summary>How far noises carry to animals (animals.json <c>senses</c>), in metres before a species' hearing.</summary>
public sealed class AnimalSenses
{
    public float Sprint { get; init; } = 28;
    public float Walk { get; init; } = 10;
    public float Crouch { get; init; } = 3;
    public float Swim { get; init; } = 12;
    public float Gunshot { get; init; } = 180;
    public float Silenced { get; init; } = 45;
    public float Bow { get; init; } = 14;
    public float Melee { get; init; } = 18;
    public float Gather { get; init; } = 30;
    public float Explosion { get; init; } = 400;

    /// <summary>A crouching person is seen at this fraction of the range, one standing still at this one.</summary>
    public float CrouchSight { get; init; } = 0.55f;

    public float StillSight { get; init; } = 0.8f;

    /// <summary>Anything this close is noticed whichever way the animal faces.</summary>
    public float Near { get; init; } = 5;
}

/// <summary>Where animals live and how they are kept (animals.json <c>spawning</c>).</summary>
public sealed class AnimalSpawning
{
    /// <summary>Spawn areas are squares of this many metres of the zone's plane.</summary>
    public float AreaSize { get; init; } = 256;

    /// <summary>An area's animals are out while a player is within this many metres of it, and go this long after the last one left beyond <see cref="Deactivate"/>.</summary>
    public float Activate { get; init; } = 380;

    public float Deactivate { get; init; } = 560;

    public float LingerSeconds { get; init; } = 30;

    /// <summary>Animals don't appear (or come back) within this many metres of a player.</summary>
    public float AwayFromPlayers { get; init; } = 70;

    /// <summary>At most this many animals live in the zone at once.</summary>
    public int MaxAnimals { get; init; } = 600;
}

/// <summary>How fast tools harvest a corpse (animals.json <c>harvest</c>): a tool's rate, or a share of its damage.</summary>
public sealed class HarvestRates
{
    private readonly Dictionary<ItemDef, float> _tools = new();

    /// <summary>For a melee item not listed: this share of its damage a hit.</summary>
    public float DefaultShare { get; init; } = 0.4f;

    public IReadOnlyDictionary<ItemDef, float> Tools => _tools;

    internal void Set(ItemDef item, float rate) => _tools[item] = rate;

    /// <summary>Harvest health a hit with this item takes (0: it can't harvest).</summary>
    public float Rate(ItemDef item) =>
        _tools.TryGetValue(item, out float rate) ? rate : item.Melee is { } melee ? melee.Damage * DefaultShare : 0;
}
