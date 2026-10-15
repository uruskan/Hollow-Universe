using System;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

/// <summary>
/// An explosive in the world as this client knows it (GROUND.md §6.6): a timed charge, bag charge or tin grenade the
/// server says is there, with its state and fuse. A flying one is drawn on its arc from where it set off (the server
/// sends each bounce); a stuck or resting one lies at <see cref="ArcStart"/>, facing <see cref="Normal"/>.
/// </summary>
public sealed class ClientExplosive
{
    public EntityId Id { get; init; }

    /// <summary>What it is: its item (the model and icon key), and its throwable numbers.</summary>
    public ItemDef Item { get; internal set; } = null!;

    public ThrowableStats? Stats => Item.Throwable;

    /// <summary>Who threw or placed it (a character's net ID; 0 if not known).</summary>
    public uint Owner { get; internal set; }

    public ExplosiveState State { get; internal set; }

    /// <summary>Its fuse went out: it lies there until someone lights it again (<see cref="InteractionKind.Rearm"/>).</summary>
    public bool Dud { get; internal set; }

    /// <summary>Where its current arc starts (or where it lies) and how fast it set off, at <see cref="StateTick"/> (server ticks).</summary>
    public Vector3 ArcStart { get; internal set; }

    public Vector3 ArcVelocity { get; internal set; }

    public uint StateTick { get; internal set; }

    /// <summary>The surface it sticks to or lies on faces this way (up while flying): orient the model by it.</summary>
    public Vector3 Normal { get; internal set; }

    /// <summary>The server tick its fuse ends on (0: not burning), and the whole fuse's length in ticks.</summary>
    public uint FuseEndTick { get; internal set; }

    public int FuseTicks { get; internal set; }

    /// <summary>Gravity along −up where it is (m/s²), for the arc.</summary>
    public Vector3 Gravity { get; internal set; }

    /// <summary>The planet's centre in the zone's frame: gravity is re-aimed toward it along the arc (<see cref="Arc.Gravity"/>).</summary>
    public Vector3? Centre { get; internal set; }

    /// <summary>Raised whenever the server sends it again (thrown, bounced, stuck, a dud, lit again).</summary>
    public uint Revision { get; internal set; }

    public bool Burning => FuseEndTick != 0;

    /// <summary>Where it is drawn at a (fractional) server tick: on its arc while flying, else where it lies.</summary>
    public Vector3 PositionAt(double serverTick)
    {
        if (State != ExplosiveState.Flying)
        {
            return ArcStart;
        }
        float t = (float)(Math.Max(0, serverTick - StateTick) / Simulation.Movement.CharacterMotor.TickRate);
        return ArcStart + ArcVelocity * t + Arc.Gravity(Gravity, Centre, ArcStart, ArcVelocity, t) * (0.5f * t * t);
    }

    /// <summary>Seconds of fuse left at a server tick (for a blinking light that quickens; infinity when not burning).</summary>
    public double FuseSecondsLeft(double serverTick) =>
        Burning ? Math.Max(0, (FuseEndTick - serverTick) / Simulation.Movement.CharacterMotor.TickRate) : double.PositiveInfinity;

    public override string ToString() => $"{Item.Key} {Id} {State}{(Dud ? " (dud)" : "")} at {ArcStart}";
}

/// <summary>
/// An explosion (GROUND.md §6.6), for the visual layer and sound: where, facing which way (the surface's normal), how far
/// its blast reaches, of what (the explosive or round: a charge's boom, a rocket's, a round's small burst), on what, and
/// whose (a character's net ID). <see cref="Explosive"/> is the explosive that went off (none for a rocket or round).
/// </summary>
public readonly record struct DetonationEvent(EntityId Explosive, ItemDef? Item, Vector3 Position, Vector3 Normal, float Radius, TargetFamily Target,
    bool Round, uint Owner, double ClientSeconds);

/// <summary>
/// A raid alert (GROUND.md §6.6): a base the player is authorised on was raided. Which base (its claim device), where it
/// stands (zone frame), when it was first and last hit, how many pieces and doors were hit and broken, what (the most
/// telling first: "Stone wall broken, Sheet Metal Door") and with what. <see cref="WhileAway"/>: it waited while they were
/// offline and came at login.
/// </summary>
public sealed record RaidAlertView(EntityId Base, Vector3 Where, DateTimeOffset First, DateTimeOffset Last, int Hits, int Destroyed, string What,
    ItemDef? Item, bool WhileAway);
