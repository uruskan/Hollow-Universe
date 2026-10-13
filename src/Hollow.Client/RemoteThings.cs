using System;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.World;

namespace Hollow.Client;

/// <summary>
/// A static thing as this client knows it (GROUND.md §6): a deployable, a bag of dropped items or a corpse, sent reliably
/// when it comes into range and again when it changes. Nothing to interpolate: it stands still.
/// </summary>
public sealed class RemoteThing
{
    public RemoteThing(uint netId)
    {
        NetId = netId;
    }

    public uint NetId { get; }

    public EntityId Id { get; set; }

    public StaticKind Kind { get; set; }

    /// <summary>A deployable's item, a world prop's number (0 for a corpse or a bag: a bag's contents show only when opened).</summary>
    public ushort Look { get; set; }

    /// <summary>Its base on the ground (zone frame) and turn about up.</summary>
    public Vector3 Position { get; set; }

    public float Yaw { get; set; }

    /// <summary>Health as a fraction (deployables).</summary>
    public float Health { get; set; } = 1;

    /// <summary>How many stacks a bag holds (one is picked up at once; more open like a box).</summary>
    public int Count { get; set; }

    public ThingFlags Flags { get; set; }

    /// <summary>A corpse's name.</summary>
    public string Name { get; set; } = "";

    public bool Lit => (Flags & ThingFlags.Lit) != 0;

    /// <summary>The receiving player's own (their sleeping bag).</summary>
    public bool Mine => (Flags & ThingFlags.Mine) != 0;

    /// <summary>Raised when it changes, for views that cache what they built.</summary>
    public uint Revision { get; set; }

    /// <summary>The deployable it is, if one (a world prop is one too, G7).</summary>
    public DeployableDef? Deployable(GameContent content) => Kind switch
    {
        StaticKind.Deployable => content.Deployable(Look),
        StaticKind.Prop => content.Prop(Look)?.Deployable,
        _ => null,
    };

    /// <summary>The world prop it is (a barrel, crate or recycler, G7), if one.</summary>
    public PropDef? Prop(GameContent content) => Kind == StaticKind.Prop ? content.Prop(Look) : null;
}

/// <summary>
/// A projectile in flight as this client draws it: from where and how fast it was loosed, under the planet's gravity,
/// until the server says where it stopped. The server alone decides what it hits.
/// </summary>
public sealed class ClientProjectile
{
    /// <summary>The server's ID (0 for our own shot until the server's launch message names it).</summary>
    public uint Id { get; set; }

    /// <summary>Who loosed it (a character's net ID; 0 if not known).</summary>
    public uint Shooter { get; init; }

    public ushort Ammo { get; init; }

    public Vector3 Origin { get; init; }

    public Vector3 Velocity { get; init; }

    /// <summary>Gravity (m/s², along −up at the origin) and up there.</summary>
    public Vector3 Gravity { get; init; }

    /// <summary>Speed lost a second, as a fraction (bullets, G4; 0 for arrows).</summary>
    public float Drag { get; init; }

    /// <summary>A bullet's shot: the shooter's gun action (0 for arrows); ours when <see cref="Own"/> (drawn since we fired).</summary>
    public uint Seq { get; init; }

    public bool Own { get; init; }

    /// <summary>Fired through a silencer: no muzzle flash, a quiet shot.</summary>
    public bool Silenced { get; init; }

    public bool IsBullet => Seq != 0;

    /// <summary>The client's clock (seconds) when it was loosed.</summary>
    public double LaunchedAt { get; init; }

    /// <summary>Where it stopped, once known, and when the server said so.</summary>
    public Vector3? EndedAt { get; set; }

    public double EndedTime { get; set; }

    public Simulation.HitTarget EndTarget { get; set; }

    /// <summary>Where it is drawn at a time on the client's clock: on its arc, held at its end once stopped.</summary>
    public Vector3 PositionAt(double time)
    {
        float t = (float)Math.Max(0, time - LaunchedAt);
        Vector3 onArc = Drag > 1e-6f
            ? Origin + Gravity / Drag * t + (Velocity - Gravity / Drag) * ((1 - MathF.Exp(-Drag * t)) / Drag)
            : Origin + Velocity * t + Gravity * (0.5f * t * t);
        if (EndedAt is { } end && Vector3.DistanceSquared(Origin, onArc) >= Vector3.DistanceSquared(Origin, end))
        {
            return end;
        }
        return onArc;
    }

    /// <summary>Its direction of flight at a time (for drawing the shaft).</summary>
    public Vector3 DirectionAt(double time)
    {
        float t = (float)Math.Max(0, time - LaunchedAt);
        Vector3 v = Drag > 1e-6f ? Gravity / Drag + (Velocity - Gravity / Drag) * MathF.Exp(-Drag * t) : Velocity + Gravity * t;
        return v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.UnitZ;
    }
}
