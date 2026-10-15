using System.Numerics;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Combat;

// The damage pipeline's common types (GROUND.md §6.4.2, after RustMVP's damage plan, docs/damage_architecture.md): a
// delivery (a swing, an arrow, a bullet, a blast, an animal's attack) finds what it hits and builds one DamageRequest;
// Zone.Damage hands it to the receiver registered for that kind of thing, which applies its defences and then its
// consequences and answers with a DamageOutcome; the shared step after it gives the attacker a hit marker. Damage type
// (the channel: the building rules' DamageType) is kept apart from delivery shape (DamageDelivery) and from what is hit
// (the receiver).

/// <summary>How a blow found its target: the delivery's shape, not the kind of damage it does.</summary>
public enum DamageDelivery : byte
{
    /// <summary>A swing of the tool or weapon in hand.</summary>
    Melee,

    /// <summary>An arrow in flight.</summary>
    Arrow,

    /// <summary>A bullet in flight (a rocket's hit is its blast).</summary>
    Bullet,

    /// <summary>An explosion's reach: a charge, a grenade, a rocket, an explosive round.</summary>
    Blast,

    /// <summary>An animal's bite, charge or swipe.</summary>
    Bite,

    /// <summary>A fire's heat (IW4, IRON_WAKE.md §7): what is in a burning pool, every so often, on the building rules' heat channel.</summary>
    Heat,
}

/// <summary>Why something was destroyed (the <see cref="ZoneEventKind.Destroyed"/> event): the blow, or the building's own reasons.</summary>
public enum DestroyCause : byte
{
    Melee,
    Bullet,
    Arrow,
    Explosion,

    /// <summary>It came down when what held it up went.</summary>
    Collapse,

    /// <summary>Its upkeep went unpaid and it decayed away.</summary>
    Decay,

    /// <summary>Its builder took it down with the hammer.</summary>
    Demolish,

    /// <summary>A ship that crashed hard enough to wreck.</summary>
    Crash,

    /// <summary>Burnt down in a fire (IW4: a fire's heat took the last of its health).</summary>
    Fire,
}

/// <summary>Target families, as a set (those that give the attacker a hit marker).</summary>
[System.Flags]
public enum TargetFamilies : uint
{
    None = 0,
    Character = 1 << (int)TargetFamily.Character,
    Animal = 1 << (int)TargetFamily.Animal,
    Ship = 1 << (int)TargetFamily.Ship,

    /// <summary>What lives, and hulls: a blast's markers (the walls and boxes it breaks are seen and heard instead).</summary>
    Living = Character | Animal | Ship,

    All = uint.MaxValue,
}

/// <summary>Sets of target families.</summary>
public static class TargetFamilySets
{
    public static bool Has(this TargetFamilies set, TargetFamily family) => ((uint)set & 1u << (int)family) != 0;
}

/// <summary>
/// What a blow brings to each kind of thing it may meet, before that thing's own defences (RustMVP's attack amounts by
/// target category): each delivery fills them from its data, each receiver takes its own.
/// </summary>
public struct DamageAmounts
{
    /// <summary>To characters and animals, already scaled for the body part hit (its weapon's or survival.json's multipliers).</summary>
    public float Flesh;

    /// <summary>To deployables and world props (a weapon's structure damage, a blast's damage to deployables).</summary>
    public float Structure;

    /// <summary>To building pieces, doors and claim devices, through the building rules' channel (a grade's transmission, a wall's soft side).</summary>
    public float Building;

    /// <summary>To ships' hulls, before the hull's share for the delivery (ships.json).</summary>
    public float Hull;
}

/// <summary>
/// One blow against one thing (RustMVP's damage request): who did it and with what, how it came (the delivery) and what
/// kind of damage it does (the channel), what each kind of thing takes, where it landed and from where. Every delivery
/// builds one and hands it to <see cref="Zone.Damage"/>.
/// </summary>
public struct DamageRequest
{
    /// <summary>The attacking player while in the zone: their hit marker, raid alerts, an animal turning on them.</summary>
    public Player? By;

    /// <summary>Who is blamed (a kill, a wound, the victim's damage indicator): a player's ID, also once they've left; none for the world or an animal.</summary>
    public EntityId Attacker;

    /// <summary>Their name (or an animal's), kept when they are gone.</summary>
    public string AttackerName;

    /// <summary>An animal's attack.</summary>
    public Animal? ByAnimal;

    /// <summary>The item that did it: the tool or weapon swung, the gun, the arrow, the explosive or round (0 for an animal).</summary>
    public ushort Weapon;

    public DamageDelivery Delivery;

    /// <summary>
    /// The kind of damage (the building rules' channels): melee for blows and bites, bullet for arrows and bullets, explosive
    /// for blasts, heat for fires.
    /// </summary>
    public DamageType Channel;

    public DamageAmounts Amounts;

    /// <summary>Of the flesh damage a body takes, the share that bleeds; and the health set burning before armour (incendiary rounds).</summary>
    public float Bleed;

    public float Burn;

    /// <summary>How a character dies of it ("Shot", "Struck down", "Blown up", "Bitten", "Burned").</summary>
    public string Cause;

    /// <summary>A lethal blow kills outright rather than knocking down (a fire's, as the burning vital's: GROUND.md §6.4).</summary>
    public bool Outright;

    /// <summary>
    /// A blast's own table by grade and door (explosives.json): what a building piece or door takes of it, times the
    /// <see cref="Falloff"/>, and times the blast's splash share unless it is <see cref="Direct"/>.
    /// </summary>
    public BlastStats? Blast;

    /// <summary>A blast's falloff at the thing's distance (already in the <see cref="Amounts"/>).</summary>
    public double Falloff;

    /// <summary>The thing the blow was aimed at or stuck to: every blow's, a blast's piece it hit (a raid alert names it first).</summary>
    public bool Direct;

    /// <summary>What it met: the thing (its family, ID and the bone hit), the body part, the point.</summary>
    public TraceHit Hit;

    /// <summary>Where it came from: the attacker's eye, the arrow, the muzzle, the blast, the animal's head.</summary>
    public Vector3 From;

    /// <summary>Which way it travelled (a wall's soft side; the Destroyed event's direction).</summary>
    public Vector3 Direction;

    /// <summary>The families of thing that give the attacker a hit marker.</summary>
    public TargetFamilies Markers;

    /// <summary>A blast's raid alerts, summed per base over the whole blast; null: a blow's own alert.</summary>
    internal BlastSystem.RaidTallies? Raids;
}

/// <summary>What a blow came to (RustMVP's damage result).</summary>
/// <param name="Landed">It reached something that took it (a body hurt, a wall or box struck): the attacker's hit marker.</param>
/// <param name="Dealt">After the thing's own defences (armour, a grade, a hull's share), before what it had left.</param>
/// <param name="Taken">The health it lost.</param>
/// <param name="Killed">It died, broke or was wrecked.</param>
/// <param name="Downed">It was knocked down (the wounded state).</param>
/// <param name="Victim">The thing hit, as the marker names it.</param>
public readonly record struct DamageOutcome(bool Landed, float Dealt, float Taken, bool Killed, bool Downed, EntityId Victim);
