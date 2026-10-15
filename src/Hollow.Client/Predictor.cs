using System;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Client;

/// <summary>
/// The player's own character, predicted (GROUND.md §4.3): every tick runs the server's movement code on the client's
/// own copy of the ground, at once, with the input that is also sent to the server. When the server says where the
/// character really was after a tick, the predictor goes back to that state and replays the newer inputs. Movement is
/// a pure function of state and input (GROUND.md §4.5), so with the same inputs the replay lands exactly where the
/// server will. It steps through the same frames as the server's zone (IRON_WAKE.md §4.8): the zone's, or aboard a ship
/// that ship's space, whose motion never enters the step; the ships' module keeps a frame for every ship near with an
/// interior, keyed by its net ID as the wire names frames.
/// </summary>
public sealed class Predictor : IDisposable
{
    /// <summary>Ticks of input and state kept (4 s): more than any round trip.</summary>
    public const int History = 128;

    private readonly PhysicsWorld _world;
    private readonly bool _ownsWorld;
    private readonly CharacterBody _body;
    private readonly InputCommand[] _inputs = new InputCommand[History];
    private readonly uint[] _inputTicks = new uint[History];
    private readonly CharacterState[] _states = new CharacterState[History];
    private readonly uint[] _stateTicks = new uint[History];

    /// <param name="shared">A ground world shared with other clients in the process (bots), or null for an own one.</param>
    public Predictor(ZoneSpec spec, ISurface surface, PhysicsWorld? shared, MovementTuning? tuning = null)
    {
        tuning ??= MovementTuning.Default;
        Frame = spec.CreateFrame(surface);
        _world = shared ?? new PhysicsWorld(surface, Frame);
        _ownsWorld = shared == null;
        _body = new CharacterBody(_world, tuning, Vector3.Zero, Vector3.UnitY);
        // The zone's frame as the server's zone has it (its radius bounds noclip there too).
        Frames = new MoveFrames(new ZoneMoveFrame(Frame, surface.SeaRadius, MoveContext.CharacterGravity(spec.SurfaceGravity, tuning), surface.Radius, _body, surface), tuning);
    }

    public ZoneFrame Frame { get; }

    public PhysicsWorld World => _world;

    /// <summary>The frames the character moves in (IRON_WAKE.md §4.2): the zone's, and the ships' the ships' module adds by net ID.</summary>
    public MoveFrames Frames { get; }

    public MovementTuning Tuning => Frames.Tuning;

    /// <summary>Up in a frame where the newest predicted state is: the zone's away from the planet (aboard, where the ship carries the character), a ship's +Y.</summary>
    public Vector3 UpIn(ulong frame) => frame != 0 ? Vector3.UnitY : Frame.UpAt(WorldOf(Latest, LatestTick + 1));

    /// <summary>A predicted state's feet in the zone's frame as its ship stood when the tick began (itself in the zone's frame).</summary>
    public Vector3 WorldOf(in CharacterState state, long tick) => Frames.ToWorld(state.Position, state.Frame, tick);

    /// <summary>
    /// The newest predicted state's feet, up and eye (<paramref name="eyeHeight"/> above the feet) in the zone's frame: aboard
    /// a ship, where it is as the next tick begins (the server measures reach and places things from there, Zone.EyeOf).
    /// </summary>
    public (Vector3 Feet, Vector3 Up, Vector3 Eye) WorldPose(float eyeHeight)
    {
        CharacterState s = Latest;
        long tick = LatestTick + 1;
        Vector3 feet = WorldOf(s, tick);
        Vector3 up = s.Frame == 0 ? Frame.UpAt(s.Position) : Frames.DirectionToWorld(Vector3.UnitY, s.Frame, tick);
        return (feet, up, feet + up * eyeHeight);
    }

    /// <summary>True once the server has given a state to start from.</summary>
    public bool HasState { get; private set; }

    /// <summary>The newest tick predicted.</summary>
    public uint LatestTick { get; private set; }

    public CharacterState Latest => _states[LatestTick % History];

    /// <summary>Ticks replayed after corrections, for statistics.</summary>
    public long Replayed { get; private set; }

    /// <summary>
    /// New ticks the character was held because its ground wasn't built yet (arrivals), or its ship not heard of yet (a
    /// join aboard); replays not counted. For statistics.
    /// </summary>
    public long HeldForGround { get; private set; }

    /// <summary>A tick that starts this far from where the last one did is an arrival (a respawn, a teleport).</summary>
    public const float ArrivalMetres = 32f;

    // Where the last tick stepped from (none yet: the start is an arrival).
    private Vector3 _lastStep = new(float.NaN);

    /// <summary>The predicted state after a tick, if still kept.</summary>
    public bool TryGetState(uint tick, out CharacterState state)
    {
        bool kept = HasState && _stateTicks[tick % History] == tick && tick <= LatestTick;
        state = kept ? _states[tick % History] : default;
        return kept;
    }

    /// <summary>Remembers the input sent for a tick (before there is a state to predict from, too).</summary>
    public void RecordInput(uint tick, in InputCommand command)
    {
        _inputs[tick % History] = command;
        _inputTicks[tick % History] = tick;
    }

    /// <summary>Starts from a server state at a tick, then replays any inputs already sent after it.</summary>
    public void Reset(uint tick, in CharacterState state, uint lastInputTick)
    {
        HasState = true;
        LatestTick = tick;
        Store(tick, state);
        for (uint t = tick + 1; t <= lastInputTick; t++)
        {
            Predict(t);
        }
    }

    /// <summary>Predicts up to a tick with the recorded inputs (normally just the next one).</summary>
    public void Predict(uint tick)
    {
        if (!HasState)
        {
            return;
        }
        while (LatestTick < tick)
        {
            CharacterState state = Latest;
            uint next = LatestTick + 1;
            if (!Step(next, ref state))
            {
                HeldForGround++;
            }
            LatestTick = next;
            Store(next, state);
        }
    }

    /// <summary>
    /// The server's state after <paramref name="tick"/>. Returns how far the prediction for that tick was off (metres),
    /// or null when the tick is no longer (or not yet) kept and the predictor started again from the server's state.
    /// </summary>
    public float? Reconcile(uint tick, in CharacterState server)
    {
        if (!HasState || tick > LatestTick || _stateTicks[tick % History] != tick)
        {
            uint last = Math.Max(LatestTick, tick);
            Reset(tick, server, last);
            return null;
        }
        CharacterState predicted = _states[tick % History];
        float error = Vector3.Distance(predicted.Position, server.Position);
        if (predicted.Equals(server))
        {
            return 0f;
        }
        Store(tick, server);
        CharacterState state = server;
        for (uint t = tick + 1; t <= LatestTick; t++)
        {
            Step(t, ref state);
            Store(t, state);
            Replayed++;
        }
        return error;
    }

    /// <summary>
    /// Where the seat of the ship the character sits in is after a tick, and how fast it goes (G6: the ship's
    /// prediction), in its frame: aboard a ship people walk in, the seat in the ship's frame, still (IRON_WAKE.md §4.2);
    /// null when unknown (the seated character then stays where it was).
    /// </summary>
    public Func<uint, (ulong Frame, Vector3 Position, Vector3 Velocity)?>? Seat { get; set; }

    /// <summary>
    /// The input a tick runs with. Ticks skipped when the clock moved forward have none: the server repeats the last
    /// input it had (without its jump press), so prediction repeats the last one recorded before.
    /// </summary>
    public InputCommand CommandFor(uint tick)
    {
        InputCommand command = default;
        if (_inputTicks[tick % History] == tick)
        {
            return _inputs[tick % History];
        }
        for (uint back = 1; back < History && back < tick; back++)
        {
            if (_inputTicks[(tick - back) % History] == tick - back)
            {
                command = _inputs[(tick - back) % History];
                break;
            }
        }
        command.Buttons &= ~MoveButtons.JumpPressed;
        return command;
    }

    // One tick of movement; false when the character was held for its ground.
    private bool Step(uint tick, ref CharacterState state)
    {
        if (state.Stance == Stance.Seated && !state.IsDead)
        {
            // In a ship's seat (G6): the ship carries the character, as the server's zone does after the ship moves.
            if (Seat?.Invoke(tick) is { } seat)
            {
                state.Frame = seat.Frame;
                state.Position = seat.Position;
                state.Velocity = seat.Velocity;
            }
            state.Flags = MoveFlags.None;
            return true;
        }
        InputCommand command = CommandFor(tick);
        if (state.Frame == 0)
        {
            // The ground first, outside Jolt's lock (a tile being built doesn't hold up a server in the same process).
            // Just arrived somewhere whose ground isn't built yet (the start, a respawn on a far beach, a teleport), the
            // character is held until worker threads have built it, as the server holds an arrival (GROUND.md §4.5), and
            // the server's states correct the wait: a dozen tiles built here would stall the frame, and a swarm of bots
            // arriving all round the world (docs/WHOLE_WORLD.md decision 8) for longer than a connection lasts. Ground
            // walked into is built now if it must be, as the server's zone does (the prefetch keeps it ahead).
            if (!(Vector3.DistanceSquared(state.Position, _lastStep) < ArrivalMetres * ArrivalMetres) && !_world.GroundReady(state.Position))
            {
                return false;
            }
            _lastStep = state.Position;
            _world.EnsureGround(state.Position);
            _world.Prefetch(state.Position);
        }
        else if (Frames.Ship(state.Frame) is { } ship && ship.TryPose(tick, out ShipPose pose))
        {
            // Aboard (IRON_WAKE.md §4.8): the ground under the ship is fetched ahead, so stepping off is no arrival.
            _lastStep = pose.ToWorld(state.Position);
            _world.Prefetch(_lastStep);
        }
        lock (JoltRuntime.Gate)
        {
            _world.OptimizeBroadPhase();
            // In its frame, then handed over between frames as the server's zone does (a ship not heard of yet: held).
            return Frames.Step(ref state, NetConvert.ToMoveInput(command), tick, out _);
        }
    }

    /// <summary>
    /// Call once a tick: drops ground tiles nobody stood near for a while, when this predictor owns its world (a shared
    /// world is its owner's to trim).
    /// </summary>
    public void EndTick()
    {
        if (_ownsWorld)
        {
            lock (JoltRuntime.Gate)
            {
                _world.ReleaseUnusedGround(Zone.GroundKeepTicks);
            }
        }
    }

    private void Store(uint tick, in CharacterState state)
    {
        _states[tick % History] = state;
        _stateTicks[tick % History] = tick;
    }

    public void Dispose()
    {
        _body.Dispose();
        if (_ownsWorld)
        {
            _world.Dispose();
        }
    }
}
