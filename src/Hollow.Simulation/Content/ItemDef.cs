using System;
using System.Collections.Generic;

namespace Hollow.Simulation.Content;

/// <summary>
/// Clothing layers (GROUND.md §6): two worn items may not share a layer, so a shirt and a chest plate go together but
/// two shirts don't. The seven wear slots hold at most one item per layer.
/// </summary>
[Flags]
public enum WearLayer : ushort
{
    None = 0,
    Head = 1,
    Face = 2,
    Torso = 4,
    TorsoArmour = 8,
    Legs = 16,
    LegsArmour = 32,
    Hands = 64,
    Feet = 128,
}

/// <summary>
/// Regions of the character's body, in the order of the character work's region mask (art/lib/hollow_art.py,
/// <c>REGIONS</c>): a worn item covers some, so the body under it can be hidden and hits on it are protected.
/// </summary>
[Flags]
public enum BodyRegions : ushort
{
    None = 0,
    Face = 1 << 0,
    Scalp = 1 << 1,
    Neck = 1 << 2,
    Chest = 1 << 3,
    Belly = 1 << 4,
    Pelvis = 1 << 5,
    UpperArm = 1 << 6,
    Forearm = 1 << 7,
    Hand = 1 << 8,
    Thigh = 1 << 9,
    Calf = 1 << 10,
    Ankle = 1 << 11,
    Foot = 1 << 12,

    /// <summary>What a hit on the head lands on.</summary>
    Head = Face | Scalp | Neck,

    /// <summary>What a hit on the body lands on.</summary>
    Body = Chest | Belly | Pelvis,

    /// <summary>What a hit on a limb lands on.</summary>
    Limbs = UpperArm | Forearm | Hand | Thigh | Calf | Ankle | Foot,
}

/// <summary>Where a hit on a character landed (RustMVP's regions): it scales the damage (survival.json, combat).</summary>
public enum HitRegion : byte
{
    None,
    Head,
    Body,
    Limb,
}

/// <summary>An item held in the hand and swung: tools and melee weapons (seconds in data, ticks for the rules).</summary>
public sealed class MeleeStats
{
    /// <summary>Damage to a character, before body part and armour.</summary>
    public float Damage { get; init; }

    /// <summary>How far from the eye a swing reaches, in metres.</summary>
    public float Reach { get; init; }

    /// <summary>Seconds from one swing to the next, and from the start of a swing to its hit.</summary>
    public float SwingSeconds { get; init; }

    public float StrikeSeconds { get; init; }

    /// <summary>Health a hit takes from a tree and from a stone or ore outcrop (the gather rate).</summary>
    public float Tree { get; init; }

    public float Ore { get; init; }

    /// <summary>The gather rate a class of natural objects takes (gather.json <c>classes.*.rate</c>).</summary>
    public float Rate(GatherRate rate) => rate == GatherRate.Ore ? Ore : Tree;

    /// <summary>Damage to a deployable (boxes, furnaces, bags).</summary>
    public float Structure { get; init; }

    /// <summary>Bleeding a hit on a character starts, as a fraction of its damage.</summary>
    public float Bleed { get; init; }

    public int SwingTicks => Math.Max(1, (int)Math.Round(SwingSeconds * CharacterTicks.Rate));

    public int StrikeTicks => Math.Clamp((int)Math.Round(StrikeSeconds * CharacterTicks.Rate), 0, SwingTicks);
}

/// <summary>A bow: draw with the secondary button, loose with the primary.</summary>
public sealed class BowStats
{
    public IReadOnlyList<string> AmmoKeys { get; init; } = Array.Empty<string>();

    /// <summary>The arrows it shoots, resolved from <see cref="AmmoKeys"/>.</summary>
    public IReadOnlyList<ItemDef> Ammo { get; internal set; } = Array.Empty<ItemDef>();

    public float DrawSeconds { get; init; }

    /// <summary>Multipliers on the arrow's speed and damage.</summary>
    public float SpeedScale { get; init; } = 1;

    public float DamageScale { get; init; } = 1;

    public int DrawTicks => Math.Max(1, (int)Math.Round(DrawSeconds * CharacterTicks.Rate));
}

/// <summary>A projectile: an arrow (G2), later bullets (G4).</summary>
public sealed class AmmoStats
{
    public float Damage { get; init; }

    /// <summary>Muzzle speed in m/s.</summary>
    public float Speed { get; init; }

    /// <summary>Multiplier on the planet's surface gravity.</summary>
    public float Gravity { get; init; } = 1;

    public float Bleed { get; init; }

    /// <summary>Fraction of the damage a deployable takes.</summary>
    public float Structure { get; init; }

    /// <summary>Chance that a projectile that hits the ground or an object can be picked up again.</summary>
    public float Recover { get; init; }

    public float LifetimeSeconds { get; init; } = 4;
}

/// <summary>What eating or using an item does.</summary>
public sealed class ConsumeStats
{
    public float Calories { get; init; }

    public float Hydration { get; init; }

    /// <summary>Health at once, and health over time.</summary>
    public float Health { get; init; }

    public float Heal { get; init; }

    public bool StopsBleeding { get; init; }

    /// <summary>Health it costs (raw and burnt meat, G7: Rust's poison), taken as damage when eaten.</summary>
    public float Poison { get; init; }
}

/// <summary>Clothing: where it is worn, what it covers and how it protects.</summary>
public sealed class WearStats
{
    public WearLayer Layers { get; init; }

    public BodyRegions Covers { get; init; }

    /// <summary>Fractions of melee and projectile damage taken off hits on the covered regions.</summary>
    public float Melee { get; init; }

    public float Projectile { get; init; }

    /// <summary>Degrees Celsius of warmth.</summary>
    public float Insulation { get; init; }
}

/// <summary>An item that burns in a fire or furnace.</summary>
public sealed class FuelStats
{
    public float Seconds { get; init; }

    public string? ByproductKey { get; init; }

    public ItemDef? Byproduct { get; internal set; }

    public int Ticks => Math.Max(1, (int)Math.Round(Seconds * CharacterTicks.Rate));
}

/// <summary>An item that turns into another in a device: ore in a furnace, food on a campfire.</summary>
public sealed class CookStats
{
    public string IntoKey { get; init; } = "";

    public ItemDef? Into { get; internal set; }

    public float Seconds { get; init; }

    /// <summary>The kind of device that cooks it (<see cref="DeviceStats.Cooks"/>).</summary>
    public string In { get; init; } = "";

    public int Ticks => Math.Max(1, (int)Math.Round(Seconds * CharacterTicks.Rate));
}

/// <summary>What an item does in building (GROUND.md §6.3; items.json's <c>"building"</c>).</summary>
public enum BuildingRole : byte
{
    None,

    /// <summary>The building plan: places twig pieces.</summary>
    Plan,

    /// <summary>Upgrades, turns, demolishes and repairs pieces.</summary>
    Hammer,

    /// <summary>The claim device, placed on a foundation or floor.</summary>
    Claim,
    KeyLock,
    CodeLock,

    /// <summary>A key cut for a key lock; its condition holds the lock's number.</summary>
    Key,

    /// <summary>A door; <see cref="ItemDef.Door"/> says which.</summary>
    Door,
}

/// <summary>One kind of item (items.json).</summary>
public sealed class ItemDef
{
    /// <summary>Stable number: saves and the wire use it; a retired one is never reused.</summary>
    public ushort Id { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>The crafting tab (categories.json).</summary>
    public string Category { get; init; } = "";

    public int StackSize { get; init; } = 1;

    /// <summary>Uses before it breaks (tools, weapons); 0 for items without condition.</summary>
    public int MaxCondition { get; init; }

    public MeleeStats? Melee { get; init; }

    public BowStats? Bow { get; init; }

    public AmmoStats? Ammo { get; init; }

    public ConsumeStats? Consume { get; init; }

    public WearStats? Wear { get; init; }

    public FuelStats? Fuel { get; init; }

    public CookStats? Cook { get; init; }

    /// <summary>The deployable this item places, by key, and resolved.</summary>
    public string? DeploysKey { get; init; }

    public DeployableDef? Deploys { get; internal set; }

    /// <summary>
    /// What placing this item makes (placement.json, GROUND_SYSTEMS.md §6): a deployable, a door, a lock, the claim device,
    /// a charge on a surface, a ship; null when it places nothing itself (the building plan places the piece chosen on it).
    /// </summary>
    public Placement.Placeable? Places { get; internal set; }

    /// <summary>The ship this item is (ships.json, G6): crafted at a station, it rolls out as a ship, never into an inventory.</summary>
    public string? ShipKey { get; init; }

    public Ships.ShipDef? Ship { get; internal set; }

    /// <summary>A firearm's numbers (weapons.json), a round's, an attachment's; null for other items (G4).</summary>
    public FirearmStats? Firearm { get; internal set; }

    public BulletStats? Bullet { get; internal set; }

    public AttachmentStats? Attachment { get; internal set; }

    /// <summary>What it does in building (G3); a door names its door in building.json.</summary>
    public BuildingRole Building { get; init; }

    public string? DoorKey { get; init; }

    public Building.DoorDef? Door { get; internal set; }
    /// <summary>An explosive thrown or placed by hand (explosives.json, G5); null for other items.</summary>
    public ThrowableStats? Throwable { get; internal set; }

    /// <summary>Its parts (GROUND_SYSTEMS.md §7): the blocks of its data that give it behaviour, worked out when the data is read.</summary>
    public ItemParts Parts { get; internal set; }

    /// <summary>
    /// What it does in the hands (GROUND_SYSTEMS.md §7): the held behaviour its one part that gives one gives
    /// (<see cref="HeldPart"/>), else <see cref="HeldUse.None"/>. The client's and the server's handlers are found by it.
    /// </summary>
    public HeldUse Held { get; internal set; }

    /// <summary>The part that gives it its held behaviour, or null.</summary>
    public ItemPart? HeldPart { get; internal set; }

    /// <summary>The recipes that take it (what it is a component of), in recipes.json's order.</summary>
    public IReadOnlyList<Recipe> UsedIn { get; internal set; } = Array.Empty<Recipe>();

    public bool HasCondition => MaxCondition > 0;

    public override string ToString() => Key;
}

/// <summary>The fixed tick rate the rules count time in (the zone's).</summary>
public static class CharacterTicks
{
    public const int Rate = Movement.CharacterMotor.TickRate;

    /// <summary>Seconds as whole ticks, rounded.</summary>
    public static int From(double seconds) => (int)Math.Round(seconds * Rate);
}
