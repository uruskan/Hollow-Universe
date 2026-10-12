using System;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.World;

namespace Hollow.Client;

/// <summary>
/// A static thing as this client knows it (GROUND.md §6): a deployable, a dropped item or a corpse, sent reliably when
/// it comes into range and again when it changes. Nothing to interpolate: it stands still.
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

    /// <summary>A deployable's or dropped item's item (0 for a corpse).</summary>
    public ushort Look { get; set; }

    /// <summary>Its base on the ground (zone frame) and turn about up.</summary>
    public Vector3 Position { get; set; }

    public float Yaw { get; set; }

    /// <summary>Health as a fraction (deployables).</summary>
    public float Health { get; set; } = 1;

    /// <summary>A dropped stack's count.</summary>
    public int Count { get; set; }

    public ThingFlags Flags { get; set; }

    /// <summary>A corpse's name.</summary>
    public string Name { get; set; } = "";

    public bool Lit => (Flags & ThingFlags.Lit) != 0;

    /// <summary>The receiving player's own (their sleeping bag).</summary>
    public bool Mine => (Flags & ThingFlags.Mine) != 0;

    /// <summary>Raised when it changes, for views that cache what they built.</summary>
    public uint Revision { get; set; }

    /// <summary>The deployable it is, if one.</summary>
    public DeployableDef? Deployable(GameContent content) => Kind == StaticKind.Deployable ? content.Deployable(Look) : null;
}

/// <summary>
/// A projectile in flight as this client draws it: from where and how fast it was loosed, under the planet's gravity,
/// until the server says where it stopped. The server alone decides what it hits.
/// </summary>
public sealed class ClientProjectile
{
    public uint Id { get; init; }

    /// <summary>Who loosed it (a character's net ID; 0 if not known).</summary>
    public uint Shooter { get; init; }

    public ushort Ammo { get; init; }

    public Vector3 Origin { get; init; }

    public Vector3 Velocity { get; init; }

    /// <summary>Gravity (m/s², along −up at the origin) and up there.</summary>
    public Vector3 Gravity { get; init; }

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
        Vector3 onArc = Origin + Velocity * t + Gravity * (0.5f * t * t);
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
        Vector3 v = Velocity + Gravity * t;
        return v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.UnitZ;
    }
}
