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

    /// <summary>To a player: they were knocked down, wounded (<see cref="ZoneEvent.Text"/>: how; <see cref="ZoneEvent.Other"/>: by whom).</summary>
    Wounded,

    /// <summary>To a player: they got up again (<see cref="ZoneEvent.Other"/>: who helped them up, or none).</summary>
    Recovered,

    /// <summary>
    /// To everyone near (G5): an explosion at <see cref="ZoneEvent.Position"/>, of <see cref="ZoneEvent.Item"/> (the
    /// explosive or round), radius <see cref="ZoneEvent.Amount"/>, facing <see cref="ZoneEvent.Velocity"/> (the surface's
    /// normal), on <see cref="ZoneEvent.Target"/>; <see cref="ZoneEvent.Other"/> is the explosive that went off (none for a
    /// rocket or round, whose <see cref="ZoneEvent.Flag"/> is set for a round). <see cref="ZoneEvent.Player"/>: whose.
    /// </summary>
    Detonated,
    /// <summary>To a player: a line to read (<see cref="ZoneEvent.Text"/>), such as a built ship waiting for room (G6).</summary>
    Notice,
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

    /// <summary>Nothing: a projectile that ran out of range or time in the air.</summary>
    Nothing,

    /// <summary>A building piece, door or claim device (G3).</summary>
    Building,

    /// <summary>A ship's hull (G6; 6, as G5 may take 5's neighbours).</summary>
    Ship = 6,
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

    /// <summary>Killed (hits), on the weak spot (gathering), silenced (a bullet launched).</summary>
    public bool Flag;

    /// <summary>The hit knocked the victim down (wounded them), G4.</summary>
    public bool Downed;

    /// <summary>A bullet's shot: the shooter's gun action sequence number (0 for arrows), so their client knows it.</summary>
    public uint Seq;

    public Vector3 Position;
    public Vector3 Velocity;
    public uint Projectile;
    public string? Text;
}
