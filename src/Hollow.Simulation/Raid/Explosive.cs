using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Raid;

/// <summary>Where a thrown or placed explosive is (GROUND.md §6.6).</summary>
public enum ExplosiveState : byte
{
    /// <summary>In the air, on the arc from <see cref="LiveExplosive.ArcStart"/> and <see cref="LiveExplosive.ArcVelocity"/> at its state tick.</summary>
    Flying,

    /// <summary>Lying where it came to rest (a grenade, or a charge whose support went).</summary>
    Resting,

    /// <summary>Stuck to the surface it touched (charges).</summary>
    Stuck,
}

/// <summary>What a stuck or resting explosive is on: it falls when that goes.</summary>
public enum SupportKind : byte
{
    /// <summary>The ground (or nothing: it is flying).</summary>
    Ground,
    Piece,
    Door,
    Deployable,

    /// <summary>A tree or outcrop (by its natural object ID).</summary>
    Natural,
}

/// <summary>
/// A timed charge, bag charge or tin grenade in the world (GROUND.md §6.6): thrown (it flies, sticks or bounces and comes
/// to rest) or placed (stuck where it was put), with its fuse burning; a dud lies there until lit again. Simulated on
/// the server only; clients get its state when it changes (and draw a flight on its arc themselves).
/// </summary>
public sealed class LiveExplosive
{
    internal LiveExplosive(EntityId id, ThrowableStats stats, EntityId owner, string ownerName)
    {
        Id = id;
        Stats = stats;
        Owner = owner;
        OwnerName = ownerName;
    }

    /// <summary>A persistent ID (charges survive a restart).</summary>
    public EntityId Id { get; }

    public ThrowableStats Stats { get; }

    public ItemDef Item => Stats.Item;

    /// <summary>Who threw or placed it (its blast is theirs).</summary>
    public EntityId Owner { get; }

    public string OwnerName { get; }

    public ExplosiveState State { get; internal set; }

    /// <summary>Its fuse went out: it lies there (or flies on) until someone lights it again.</summary>
    public bool Dud { get; internal set; }

    /// <summary>Where it is now and how fast it moves (the server's truth), in the zone frame.</summary>
    public Vector3 Position { get; internal set; }

    public Vector3 Velocity { get; internal set; }

    /// <summary>
    /// Where its current arc starts and how fast it set off, at <see cref="StateTick"/>: what clients get, so they draw
    /// the flight themselves (the same parabola under the planet's gravity); where it lies once it stops.
    /// </summary>
    public Vector3 ArcStart { get; internal set; }

    public Vector3 ArcVelocity { get; internal set; }

    /// <summary>The zone tick the arc starts at.</summary>
    public long StateTick { get; internal set; }

    /// <summary>The surface's normal where it sticks or rests (up while flying).</summary>
    public Vector3 Normal { get; internal set; } = Vector3.UnitY;

    /// <summary>The tick its fuse ends on; −1 when it isn't burning (a dud).</summary>
    public long FuseEndTick { get; internal set; } = -1;

    /// <summary>The whole fuse it was lit with, in ticks (for a blinking light's pace).</summary>
    public int FuseTicks { get; internal set; }

    /// <summary>Decided when it is lit, from a seed of its ID and how often it was lit: this fuse goes out.</summary>
    public bool WillDud { get; internal set; }

    /// <summary>How many times it was lit (re-arming a dud lights it again).</summary>
    public int Arms { get; internal set; }

    /// <summary>What it is stuck to or lying on, and that thing's ID.</summary>
    public SupportKind SupportKind { get; internal set; }

    public ulong SupportId { get; internal set; }

    /// <summary>The thing itself (a <c>Piece</c>, <c>Door</c> or <c>Deployable</c>), resolved; null for the ground or a natural object.</summary>
    public object? Support { get; internal set; }

    /// <summary>A door's leaf it is on, and where on it (the leaf's own frame): it moves with the leaf.</summary>
    public int Leaf { get; internal set; }

    public Vector3 SupportLocal { get; internal set; }

    public Vector3 SupportNormal { get; internal set; }

    /// <summary>A dud left alone goes after this tick (−1: never).</summary>
    public long DespawnTick { get; internal set; } = -1;

    /// <summary>Raised whenever what clients see changes (state, position, fuse), so the server sends it again.</summary>
    public uint Revision { get; internal set; }

    /// <summary>Gone: exploded or despawned.</summary>
    public bool Gone { get; internal set; }

    /// <summary>Bounces in its last flight.</summary>
    public int Bounces { get; internal set; }

    /// <summary>Ticks in the air so far.</summary>
    internal int Airborne;

    public bool Burning => FuseEndTick >= 0;

    public override string ToString() => $"{Item.Key} {Id} {State} at {Position}";
}

/// <summary>
/// A claimed base hurt by a raid (an explosive, or a blow or shot by someone it doesn't list): the zone collects these
/// each tick and the server hands them to the notification service, which alerts every authorised player
/// (ARCHITECTURE.md §14).
/// </summary>
public readonly record struct RaidHit
{
    /// <summary>The base: its claim device's ID and where it stands (zone frame).</summary>
    public EntityId Base { get; init; }

    public Vector3 Where { get; init; }

    /// <summary>Everyone the claim device lists, as it was at the hit.</summary>
    public IReadOnlyList<ulong> Authorised { get; init; }

    public EntityId Attacker { get; init; }

    public string AttackerName { get; init; }

    /// <summary>What did it: the explosive or weapon's item.</summary>
    public ushort Item { get; init; }

    /// <summary>What was hit, for the alert ("Stone wall", "Sheet Metal Door"), the most telling first.</summary>
    public string What { get; init; }

    /// <summary>Pieces and doors hit, and how many of them broke.</summary>
    public int Hits { get; init; }

    public int Destroyed { get; init; }

    /// <summary>The real clock (Unix ms).</summary>
    public long UnixMilliseconds { get; init; }
}

/// <summary>An explosive's stored form (GROUND.md §6.6: charges with their fuse left survive a restart), positions in the zone frame.</summary>
public sealed record ExplosiveSave
{
    public EntityId Id { get; init; }

    public ushort Item { get; init; }

    public EntityId Owner { get; init; }

    public string OwnerName { get; init; } = "";

    public ExplosiveState State { get; init; }

    public bool Dud { get; init; }

    public Vector3 Position { get; init; }

    public Vector3 Velocity { get; init; }

    public Vector3 Normal { get; init; }

    /// <summary>Ticks of fuse left (world time: it pauses while the server is down); −1 when not burning.</summary>
    public long FuseLeft { get; init; } = -1;

    public int FuseTicks { get; init; }

    public bool WillDud { get; init; }

    public int Arms { get; init; }

    public SupportKind SupportKind { get; init; }

    public ulong SupportId { get; init; }

    public int Leaf { get; init; }

    public Vector3 SupportLocal { get; init; }

    public Vector3 SupportNormal { get; init; }

    /// <summary>Ticks until a dud goes (−1: never).</summary>
    public long DespawnLeft { get; init; } = -1;
}

/// <summary>What one explosion did, for tests and the load test's counts.</summary>
public sealed class BlastReport
{
    public int PiecesHit { get; internal set; }

    public int PiecesDestroyed { get; internal set; }

    public int DoorsHit { get; internal set; }

    public int DoorsDestroyed { get; internal set; }

    public int DeployablesHit { get; internal set; }

    public int CharactersHit { get; internal set; }

    /// <summary>Damage dealt to each piece, door, deployable or character (by its object), after the falloff, splash and armour.</summary>
    public Dictionary<object, double> Damage { get; } = new();

    /// <summary>Things within the radius that something stood in front of: they took nothing.</summary>
    public List<object> Shielded { get; } = new();
}

/// <summary>Counts of the raiding game (the load test reports them).</summary>
public sealed class RaidCounters
{
    public long Thrown { get; internal set; }

    public long Placed { get; internal set; }

    public long Duds { get; internal set; }

    public long Rearmed { get; internal set; }

    public long Detonations { get; internal set; }

    public long PiecesDestroyed { get; internal set; }

    public long DoorsDestroyed { get; internal set; }

    public long StructureDamage { get; internal set; }

    public long RaidHits { get; internal set; }

    /// <summary>Time spent on explosives and blasts (the zone's share of raiding).</summary>
    public TimeSpan Time { get; internal set; }

    public override string ToString() =>
        $"{Thrown} thrown and {Placed} placed by hand, {Detonations} detonations, {Duds} duds ({Rearmed} lit again), " +
        $"{PiecesDestroyed} pieces and {DoorsDestroyed} doors broken, {StructureDamage} damage to buildings, {RaidHits} raid hits on claimed bases";
}
