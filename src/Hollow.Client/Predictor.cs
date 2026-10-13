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
/// server will.
/// </summary>
public sealed class Predictor : IDisposable
{
    /// <summary>Ticks of input and state kept (4 s): more than any round trip.</summary>
    public const int History = 128;

    private readonly PhysicsWorld _world;
    private readonly bool _ownsWorld;
    private readonly CharacterBody _body;
    private readonly MoveContext _context;
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
        _context = new MoveContext(Frame, surface.SeaRadius, MoveContext.CharacterGravity(spec.SurfaceGravity, tuning), tuning);
        _body = new CharacterBody(_world, tuning, Vector3.Zero, Vector3.UnitY);
    }

    public ZoneFrame Frame { get; }

    public PhysicsWorld World => _world;

    public MovementTuning Tuning => _context.Tuning;

    /// <summary>True once the server has given a state to start from.</summary>
    public bool HasState { get; private set; }

    /// <summary>The newest tick predicted.</summary>
    public uint LatestTick { get; private set; }

    public CharacterState Latest => _states[LatestTick % History];

    /// <summary>Ticks replayed after corrections, for statistics.</summary>
    public long Replayed { get; private set; }

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
            Step(next, ref state);
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
    /// prediction); null when unknown (the seated character then stays where it was).
    /// </summary>
    public Func<uint, (Vector3 Position, Vector3 Velocity)?>? Seat { get; set; }

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

    private void Step(uint tick, ref CharacterState state)
    {
        if (state.Stance == Stance.Seated && !state.IsDead)
        {
            // In a ship's seat (G6): the ship carries the character, as the server's zone does after the ship moves.
            if (Seat?.Invoke(tick) is { } seat)
            {
                state.Position = seat.Position;
                state.Velocity = seat.Velocity;
            }
            state.Flags = MoveFlags.None;
            return;
        }
        InputCommand command = CommandFor(tick);
        lock (JoltRuntime.Gate)
        {
            _world.EnsureGround(state.Position);
            _world.Prefetch(state.Position);
            CharacterMotor.Step(ref state, NetConvert.ToMoveInput(command), _body, _context);
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
