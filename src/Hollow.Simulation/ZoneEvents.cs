using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

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

    /// <summary>
    /// To everyone near (GROUND.md §6.4.2): something was destroyed: a deployable or world prop broken, a building piece,
    /// door or claim device broken, brought down, decayed or demolished, a ship wrecked. <see cref="ZoneEvent.Target"/> names
    /// it (its family and ID), <see cref="ZoneEvent.Text"/> is its model key (a piece's with <see cref="ZoneEvent.Grade"/>);
    /// <see cref="ZoneEvent.Position"/> and <see cref="ZoneEvent.Rotation"/> where it stood (zone frame, as it is drawn: a
    /// door's are its frame piece's, with <see cref="ZoneEvent.Flag"/> set when it was open);
    /// <see cref="ZoneEvent.Velocity"/> the blow's direction (zero for a collapse, decay or demolition);
    /// <see cref="ZoneEvent.Cause"/> what did it; <see cref="ZoneEvent.Player"/> who and <see cref="ZoneEvent.Item"/> with what.
    /// Made in one place, <c>Zone.Destroyed</c>.
    /// </summary>
    Destroyed,
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

    /// <summary>
    /// What was hit, landed on, was on or was destroyed (GROUND_SYSTEMS.md §3.1): its family, ID and part (a body's bone);
    /// only the family where the ID doesn't matter (a projectile's end, what an explosion was on).
    /// </summary>
    public TargetRef Target;
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

    /// <summary>A destroyed thing's turn as it was drawn (<see cref="ZoneEventKind.Destroyed"/>).</summary>
    public Quaternion Rotation;

    /// <summary>What destroyed it, and a piece's grade (<see cref="ZoneEventKind.Destroyed"/>).</summary>
    public DestroyCause Cause;

    public byte Grade;
}
