using System;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

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

    /// <summary>
    /// Its base on the ground (zone frame), its up there and its turn about up. Aboard a ship (<see cref="Aboard"/>), where
    /// the ship as drawn carries it, set every frame (<c>ShipModule.Sample</c>): never a zone pose that goes stale.
    /// </summary>
    public Vector3 Position { get; set; }

    public Vector3 Up { get; set; } = Vector3.UnitY;

    public float Yaw { get; set; }

    /// <summary>Built or lying aboard a ship (IRON_WAKE.md §8): <see cref="Support"/> names the ship (its entity ID).</summary>
    public bool Aboard => Support.Family == TargetFamily.Ship;

    /// <summary>Aboard: its base in the ship's frame, and its turn about the ship's up (as the server sent them).</summary>
    public Vector3 LocalPosition { get; set; }

    public float LocalYaw { get; set; }

    /// <summary>Aboard: its turn in the ship's frame (X right, Y up, −Z forward).</summary>
    public Quaternion LocalRotation => Simulation.Combat.Geometry.Upright(Vector3.UnitY, LocalYaw);

    /// <summary>Carries it where a ship is drawn (aboard).</summary>
    public void Place(in Simulation.Movement.ShipPose ship)
    {
        Position = ship.ToWorld(LocalPosition);
        Up = ship.Up;
        Yaw = Simulation.Movement.Facing.YawOf(ship.DirectionToWorld(Vector3.Transform(-Vector3.UnitZ, LocalRotation)), Up);
    }

    /// <summary>Health as a fraction (deployables).</summary>
    public float Health { get; set; } = 1;

    /// <summary>How many stacks a bag holds (one is picked up at once; more open like a box).</summary>
    public int Count { get; set; }

    /// <summary>What the server tells this player of it (GROUND_SYSTEMS.md §3.3): lit, theirs, empty, a teammate's.</summary>
    public ViewerFlags Flags { get; set; }

    /// <summary>Its own name, as the server gives it: a body's (the dead player's), a bag's, a world prop's; empty for a deployable.</summary>
    public string Name { get; set; } = "";

    /// <summary>Its capabilities, as the server's rules see them (GROUND_SYSTEMS.md §2.1): what the interaction rules offer on it.</summary>
    public Simulation.Capabilities.CapabilitySet Capabilities { get; set; }

    public bool Lit => (Flags & ViewerFlags.Lit) != 0;

    /// <summary>The receiving player's own (their sleeping bag).</summary>
    public bool Mine => (Flags & ViewerFlags.Mine) != 0;

    /// <summary>Raised when it changes, for views that cache what they built.</summary>
    public uint Revision { get; set; }

    /// <summary>
    /// What it stands on (a building piece, a ship; none on the ground): a structure's or a ship's limit counts what stands
    /// on it.
    /// </summary>
    public TargetRef Support { get; set; }

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
public sealed partial class ClientProjectile
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

    /// <summary>
    /// The planet's centre in the zone's frame: gravity is re-aimed toward it as the projectile flies, as the server's
    /// is (docs/WHOLE_WORLD.md §6.3; <see cref="Arc.Gravity"/>). Null: one parabola along <see cref="Gravity"/>.
    /// </summary>
    public Vector3? Centre { get; init; }

    /// <summary>Speed lost a second, as a fraction (bullets, G4; 0 for arrows).</summary>
    public float Drag { get; init; }

    /// <summary>A bullet's shot: the shooter's gun action (0 for arrows); ours when <see cref="Own"/> (drawn since we fired).</summary>
    public uint Seq { get; init; }

    public bool Own { get; init; }

    /// <summary>Fired through a silencer: no muzzle flash, a quiet shot.</summary>
    public bool Silenced { get; init; }

    /// <summary>
    /// A battery's rocket (IW3, IRON_WAKE.md §6.4): its battery's number + 1 (0: a gun's or a bow's), and its motor, under
    /// which it is drawn by the server's own steps (<see cref="Simulation.Combat.RocketFlight"/>); <see cref="Seq"/> is its
    /// shot's number.
    /// </summary>
    public int Battery { get; init; }

    public Simulation.Combat.RocketMotor? Motor { get; init; }

    /// <summary>A rocket's flight as the server steps it (positions and velocities a step apart), made when first drawn.</summary>
    private Vector3[]? _flight;

    public bool IsBullet => Seq != 0;

    /// <summary>The client's clock (seconds) when it was loosed.</summary>
    public double LaunchedAt { get; init; }

    /// <summary>Where it stopped, once known, and when the server said so.</summary>
    public Vector3? EndedAt { get; set; }

    public double EndedTime { get; set; }

    /// <summary>The family of what it stopped on (none: out of range or time).</summary>
    public Simulation.Targets.TargetFamily EndTarget { get; set; }

    /// <summary>Where it is drawn at a time on the client's clock: on its arc, held at its end once stopped.</summary>
    public Vector3 PositionAt(double time)
    {
        float t = (float)Math.Max(0, time - LaunchedAt);
        if (Motor != null)
        {
            (Vector3 at, _) = Flown(t);
            return EndedAt is { } stop && Vector3.DistanceSquared(Origin, at) >= Vector3.DistanceSquared(Origin, stop) ? stop : at;
        }
        Vector3 g = Arc.Gravity(Gravity, Centre, Origin, Velocity, t);
        Vector3 onArc = Drag > 1e-6f
            ? Origin + g / Drag * t + (Velocity - g / Drag) * ((1 - MathF.Exp(-Drag * t)) / Drag)
            : Origin + Velocity * t + g * (0.5f * t * t);
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
        if (Motor != null)
        {
            (_, Vector3 velocity) = Flown(t);
            return velocity.LengthSquared() > 1e-6f ? Vector3.Normalize(velocity) : Vector3.UnitZ;
        }
        Vector3 g = Arc.Gravity(Gravity, Centre, Origin, Velocity, t);
        Vector3 v = Drag > 1e-6f ? g / Drag + (Velocity - g / Drag) * MathF.Exp(-Drag * t) : Velocity + g * t;
        return v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.UnitZ;
    }
}

public sealed partial class ClientProjectile
{
    /// <summary>The longest a rocket is drawn flying (the projectiles' lifetime cap, IRON_WAKE.md §6.4).</summary>
    public const float MostSeconds = 15f;

    // A rocket's place and velocity <paramref name="t"/> seconds after it left, between the server's steps (its motor, the
    // gravity toward the planet's centre, its drag), the steps made once up to the cap.
    private (Vector3 Position, Vector3 Velocity) Flown(float t)
    {
        const float h = Simulation.Combat.RocketFlight.StepSeconds;
        if (_flight == null)
        {
            int steps = (int)(MostSeconds / h) + 1;
            var flight = new Vector3[2 * (steps + 1)];
            Vector3 p = Origin, v = Velocity;
            float flown = 0, burnt = 0, g = Gravity.Length();
            flight[0] = p;
            flight[1] = v;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 down = Centre is { } c && (p - c).LengthSquared() > 1 ? Vector3.Normalize(c - p) : (g > 1e-6f ? Gravity / g : -Vector3.UnitY);
                (Vector3 next, Vector3 nextVelocity, burnt) = Simulation.Combat.RocketFlight.Step(p, v, down, g, Motor!, flown, burnt, h);
                flown += (next - p).Length();
                (p, v) = (next, nextVelocity);
                flight[2 * i] = p;
                flight[2 * i + 1] = v;
            }
            _flight = flight;
        }
        float at = Math.Clamp(t / h, 0, _flight.Length / 2 - 1.001f);
        int k = (int)at;
        float f = at - k;
        return (Vector3.Lerp(_flight[2 * k], _flight[2 * k + 2], f), Vector3.Lerp(_flight[2 * k + 1], _flight[2 * k + 3], f));
    }
}

/// <summary>
/// Arcs drawn under a planet's gravity (docs/WHOLE_WORLD.md §6.3, §10.4): on a small world gravity turns 2.4° every 100 m,
/// so a projectile's arc can't be one parabola along the gravity where it set off. The server re-aims gravity toward the
/// centre every tick; drawn in closed form, an arc takes the gravity at the point a third of the way along its straight
/// flight so far, where the drop it has gathered is weighted (∫(t − s) g(s) ds): good to millimetres over a few hundred
/// metres on a 2.41 km world.
/// </summary>
public static class Arc
{
    /// <summary>The gravity to draw an arc with after <paramref name="t"/> seconds: <paramref name="gravity"/>'s strength toward <paramref name="centre"/>.</summary>
    public static Vector3 Gravity(Vector3 gravity, Vector3? centre, Vector3 origin, Vector3 velocity, float t)
    {
        if (centre is not { } c)
        {
            return gravity;
        }
        Vector3 away = origin + velocity * (t / 3) - c;
        float length = away.Length();
        return length > 1e-3f ? away * (-gravity.Length() / length) : gravity;
    }
}
