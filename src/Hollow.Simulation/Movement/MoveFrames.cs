using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Movement;

/// <summary>
/// The frame a character moves in (IRON_WAKE.md §4.3): the zone's, or a ship's. It says which way is up, how strong
/// gravity is, how deep the water is, and which physics the character steps in (its space's one body for the characters
/// stepping there). <see cref="CharacterMotor.Step(ref CharacterState, in MoveInput, CharacterBody, in MoveContext)"/>
/// runs unchanged against whichever it is.
/// </summary>
public interface IMoveFrame
{
    /// <summary>The frame's key, as <see cref="CharacterState.Frame"/> holds it: 0 the zone's, else the ship's (its ID on the server, its net ID on a client).</summary>
    ulong Key { get; }

    /// <summary>Up at a point of this frame (unit).</summary>
    Vector3 UpAt(Vector3 position);

    /// <summary>A character's gravity here, in m/s².</summary>
    float Gravity { get; }

    /// <summary>How deep the feet are under water, as a fraction of the standing height (0 dry).</summary>
    float Submersion(Vector3 position, MovementTuning tuning);

    /// <summary>The world's mean radius, for noclip's bounds (0: no noclip in this frame).</summary>
    double Radius { get; }

    /// <summary>The one body of this frame's space that the characters stepping in it use in turn (null: the caller brings one).</summary>
    CharacterBody? Body { get; }
}

/// <summary>
/// The zone's frame (GROUND.md §4.5): up away from the planet's centre, the planet's gravity times the characters'
/// scale, the sea from the surface, the zone's physics.
/// </summary>
public sealed class ZoneMoveFrame(ZoneFrame frame, double seaRadius, float gravity, double radius, CharacterBody? body = null) : IMoveFrame
{
    public ulong Key => 0;

    public ZoneFrame Frame { get; } = frame;

    /// <summary>Radius of the sea surface in metres, 0 without one (<see cref="ISurface.SeaRadius"/>).</summary>
    public double SeaRadius { get; } = seaRadius;

    public float Gravity { get; } = gravity;

    public double Radius { get; } = radius;

    public CharacterBody? Body { get; internal set; } = body;

    public Vector3 UpAt(Vector3 position) => Frame.UpAt(position);

    public float Submersion(Vector3 position, MovementTuning tuning)
    {
        if (SeaRadius <= 0)
        {
            return 0f;
        }
        double depth = SeaRadius - Frame.ToBody(position).Length;
        return depth <= 0 ? 0f : (float)Math.Min(1.0, depth / tuning.StandingHeight);
    }
}

/// <summary>
/// Where a ship is at the moment a character's step is handed over (IRON_WAKE.md §4.5): its centre of mass and turn in
/// the zone's frame, its velocity, and its turn rate in the zone's frame (the ground's turn under it included, the motion
/// a point of its deck really has). Converts points, directions and velocities between the ship's frame and the zone's.
/// </summary>
public readonly record struct ShipPose(Vector3 Position, Quaternion Rotation, Vector3 Velocity, Vector3 Spin, Vector3 CentreOfMass)
{
    /// <summary>A point of the ship's frame (from its model's origin) in the zone's frame.</summary>
    public Vector3 ToWorld(Vector3 local) => Position + Vector3.Transform(local - CentreOfMass, Rotation);

    /// <summary>A point of the zone's frame in the ship's frame.</summary>
    public Vector3 ToLocal(Vector3 world) => Vector3.Transform(world - Position, Quaternion.Conjugate(Rotation)) + CentreOfMass;

    public Vector3 DirectionToWorld(Vector3 local) => Vector3.Transform(local, Rotation);

    public Vector3 DirectionToLocal(Vector3 world) => Vector3.Transform(world, Quaternion.Conjugate(Rotation));

    /// <summary>How fast a point of the zone's frame that rides the ship moves: the centre's velocity and the turn's, ω × r.</summary>
    public Vector3 VelocityAt(Vector3 world) => Velocity + Vector3.Cross(Spin, world - Position);

    /// <summary>The ship's up in the zone's frame.</summary>
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);
}

/// <summary>
/// A ship's frame (IRON_WAKE.md §4.3): +Y up, the zone's character gravity times the decks' along −Y, no water, the
/// ship's own space. What the hand-over between frames needs of the ship is here too: its interior, its parts' states, its
/// pose at a tick and its parked body in the zone's physics. The server's ships and a client's both give one.
/// </summary>
public interface IShipFrame : IMoveFrame
{
    Ships.ShipInterior Interior { get; }

    /// <summary>Its parts' states now (<see cref="Ships.Ship.Parts"/>).</summary>
    float[] States { get; }

    /// <summary>Its space, in which aboard characters step and queries through its frame are answered.</summary>
    ShipSpace Space { get; }

    /// <summary>Where it is as the step of <paramref name="tick"/> begins (after the ship's own step of the tick before), or false if not known.</summary>
    bool TryPose(long tick, out ShipPose pose);

    /// <summary>Its static body in the zone's physics while parked, or 0 (a character standing on it is aboard, §4.6).</summary>
    uint ParkedBody { get; }
}

/// <summary>
/// The frames characters move in (IRON_WAKE.md §4.2): the zone's, and every ship's with an interior, by key. A zone's
/// step and a client's prediction step each character through this, so both run the same code: the view turned into
/// the character's frame when the input gave it in another, the step in the frame's space, then the hand-over at the
/// step's end (§4.5): aboard when the ground under the feet is a ship's parked body or the feet are inside its boarding
/// volume over its deck; ashore when nothing of the ship is under the feet more than the margin outside it. The pose and
/// velocity are kept in the world exactly, the velocity gaining or losing the ship's at that point.
/// </summary>
public sealed class MoveFrames
{
    private readonly Dictionary<ulong, IShipFrame> _ships = new();
    private readonly List<IShipFrame> _order = new();

    public MoveFrames(ZoneMoveFrame zone, MovementTuning tuning)
    {
        Zone = zone;
        Tuning = tuning;
    }

    public ZoneMoveFrame Zone { get; }

    public MovementTuning Tuning { get; }

    /// <summary>The ships' frames, in the order they were added (stable: the hand-over tests them in it).</summary>
    public IReadOnlyList<IShipFrame> Ships => _order;

    /// <summary>Hand-overs so far (statistics, tests).</summary>
    public long Boarded { get; private set; }

    public long Left { get; private set; }

    public void Add(IShipFrame frame)
    {
        if (_ships.TryAdd(frame.Key, frame))
        {
            _order.Add(frame);
        }
    }

    public void Remove(ulong key)
    {
        if (_ships.Remove(key, out IShipFrame? frame))
        {
            _order.Remove(frame);
        }
    }

    /// <summary>A ship's frame by its key, or null.</summary>
    public IShipFrame? Ship(ulong key) => key != 0 && _ships.TryGetValue(key, out IShipFrame? frame) ? frame : null;

    /// <summary>The frame of a key: the zone's for 0, a ship's, or null when that ship isn't here.</summary>
    public IMoveFrame? Find(ulong key) => key == 0 ? Zone : Ship(key);

    /// <summary>
    /// One tick of a character in its frame, then the hand-over (callers hold <see cref="JoltRuntime.Gate"/>). A character
    /// whose ship isn't here (a client that hasn't heard of it yet) holds still: false.
    /// </summary>
    public bool Step(ref CharacterState state, in MoveInput input, long tick, out StepEvents events)
    {
        events = default;
        IMoveFrame? frame = Find(state.Frame);
        if (frame?.Body is not { } body)
        {
            return false;
        }
        MoveInput turned = input;
        if (input.Frame != state.Frame)
        {
            // Given in the frame the client believed the character was in: turned through the ship's pose (§4.11).
            turned.View = ViewIn(input.View, input.Frame, state.Frame, tick);
        }
        turned.Frame = state.Frame;
        events = CharacterMotor.Step(ref state, turned, body, new MoveContext(frame, Tuning));
        if (!state.IsDead && state.Stance != Stance.Seated && !state.Has(MoveCheats.Noclip))
        {
            HandOver(ref state, frame, body, tick);
        }
        return true;
    }

    // The hand-over at the end of a step (§4.5).
    private void HandOver(ref CharacterState state, IMoveFrame frame, CharacterBody body, long tick)
    {
        if (frame is IShipFrame aboard)
        {
            // Ashore: nothing of the ship under the feet, and the feet more than the margin outside the boarding volume.
            if (body.GroundState == JoltPhysicsSharp.GroundState.OnGround || body.GroundState == JoltPhysicsSharp.GroundState.OnSteepGround ||
                aboard.Interior.OutsideBoarding(state.Position, aboard.States) <= aboard.Interior.Margin || !aboard.TryPose(tick, out ShipPose pose))
            {
                return;
            }
            Vector3 world = pose.ToWorld(state.Position);
            state.Velocity = pose.DirectionToWorld(state.Velocity) + pose.VelocityAt(world);
            state.Position = world;
            state.Frame = 0;
            Left++;
            return;
        }
        uint ground = body.GroundState is JoltPhysicsSharp.GroundState.OnGround or JoltPhysicsSharp.GroundState.OnSteepGround ? body.GroundBody : uint.MaxValue;
        foreach (IShipFrame ship in _order)
        {
            if (!ship.TryPose(tick, out ShipPose pose) || Vector3.DistanceSquared(state.Position, pose.Position) > ship.Interior.Reach * ship.Interior.Reach)
            {
                continue;
            }
            Vector3 local = pose.ToLocal(state.Position);
            // Aboard: standing on its parked body, or the feet in its boarding volume, standing on nothing else, over its deck.
            bool on = ship.ParkedBody != 0 && ground == ship.ParkedBody;
            if (!on && ground == uint.MaxValue && ship.Interior.OutsideBoarding(local, ship.States) == 0)
            {
                on = ship.Space.Ray(local + Vector3.UnitY * 0.05f, -Vector3.UnitY, DeckBelow, out _);
            }
            if (!on)
            {
                continue;
            }
            state.Velocity = pose.DirectionToLocal(state.Velocity - pose.VelocityAt(state.Position));
            state.Position = local;
            state.Frame = ship.Key;
            Boarded++;
            return;
        }
    }

    /// <summary>How far below the feet a ship's deck must be for its boarding volume to take a character standing on nothing (m).</summary>
    public const float DeckBelow = 3f;

    /// <summary>
    /// A direction given in one frame, in another (the zone's or a ship's) at a tick: through the ships' poses. Unchanged
    /// when a ship isn't known.
    /// </summary>
    public Vector3 ViewIn(Vector3 view, ulong from, ulong to, long tick)
    {
        if (from == to)
        {
            return view;
        }
        Vector3 world = view;
        if (Ship(from) is { } source && source.TryPose(tick, out ShipPose fromPose))
        {
            world = fromPose.DirectionToWorld(view);
        }
        if (Ship(to) is { } target && target.TryPose(tick, out ShipPose toPose))
        {
            return Vector3.Normalize(toPose.DirectionToLocal(world));
        }
        return Vector3.Normalize(world);
    }

    /// <summary>A point of a frame in the zone's frame at a tick (itself for the zone's, or when the ship isn't known).</summary>
    public Vector3 ToWorld(Vector3 point, ulong frame, long tick) =>
        frame != 0 && Ship(frame) is { } ship && ship.TryPose(tick, out ShipPose pose) ? pose.ToWorld(point) : point;

    /// <summary>A direction of a frame in the zone's frame at a tick.</summary>
    public Vector3 DirectionToWorld(Vector3 direction, ulong frame, long tick) =>
        frame != 0 && Ship(frame) is { } ship && ship.TryPose(tick, out ShipPose pose) ? pose.DirectionToWorld(direction) : direction;
}
