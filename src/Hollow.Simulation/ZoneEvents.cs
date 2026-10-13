using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation;

/// <summary>What a <see cref="ZoneEvent"/> reports.</summary>
public enum ZoneEventKind : byte
{
    /// <summary>To the attacker: their hit landed (<see cref="ZoneEvent.Other"/>: what was hit).</summary>
    HitConfirmed,

    /// <summary>To the victim: damage taken, from <see cref="ZoneEvent.Position"/>.</summary>
    DamageTaken,

    /// <summary>To a player: items gained (gathering, picking up, crafting, looting) or lost (a tool broke).</summary>
    ItemsGained,

    /// <summary>To a player: a crafted unit is done.</summary>
    CraftFinished,

    /// <summary>To a player: they died (<see cref="ZoneEvent.Text"/>: how; <see cref="ZoneEvent.Other"/>: by whom).</summary>
    Died,

    /// <summary>To everyone near: a projectile was loosed (position, velocity, ammo).</summary>
    ProjectileLaunched,

    /// <summary>To everyone near: a projectile stopped (position).</summary>
    ProjectileEnded,
}

/// <summary>Why items arrived, for the UI's notifications.</summary>
public enum GainReason : byte
{
    Gathered,
    PickedUp,
    Crafted,
    Looted,
    Refunded,
    Broke,
    Used,
}

/// <summary>What was hit, for hit markers.</summary>
public enum HitTarget : byte
{
    Character,
    Natural,
    Deployable,
    Ground,

    /// <summary>A building piece, door or claim device (G3).</summary>
    Building,
}

/// <summary>
/// Something that happened in a tick that a player should hear about (GROUND.md §4.3: hit markers, gather yields,
/// damage taken, a craft finished). The zone collects them; the server sends each to whom it concerns and clears
/// the list.
/// </summary>
public struct ZoneEvent
{
    public ZoneEventKind Kind;

    /// <summary>Who it is for (for projectiles: who shot).</summary>
    public EntityId Player;

    /// <summary>The other side: the victim, the attacker, the object hit.</summary>
    public EntityId Other;

    public HitTarget Target;
    public HitRegion Region;
    public GainReason Reason;

    public ushort Item;
    public int Count;
    public float Amount;

    /// <summary>Killed (hits), on the weak spot (gathering).</summary>
    public bool Flag;

    public Vector3 Position;
    public Vector3 Velocity;
    public uint Projectile;
    public string? Text;
}
