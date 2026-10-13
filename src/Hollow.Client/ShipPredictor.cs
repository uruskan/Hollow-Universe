using System;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;

namespace Hollow.Client;

/// <summary>
/// The ship the player flies, predicted (GROUND.md §6.7), as <see cref="Predictor"/> predicts the character: every tick
/// runs the server's flight model on the client's own copy of the ground with the controls that are also sent; when a
/// snapshot brings the server's state of the ship after a tick, it goes back to that state and replays the newer
/// inputs. The flight model is a pure function of state, input and the static world, so on a clean line nothing is
/// ever corrected.
/// </summary>
public sealed class ShipPredictor
{
    private readonly ShipState[] _states = new ShipState[Predictor.History];
    private readonly uint[] _stateTicks = new uint[Predictor.History];
    private readonly Predictor _inputs;
    private readonly Func<ShipContext> _context;

    /// <param name="inputs">The character's predictor, which records every tick's command (the ship's controls ride in it).</param>
    /// <param name="context">The ships' world on the client: the zone's frame, ground, gravity and air, the prediction's physics.</param>
    public ShipPredictor(Predictor inputs, Func<ShipContext> context)
    {
        _inputs = inputs;
        _context = context;
    }

    /// <summary>The ship being flown (its kind and net ID), or null on foot.</summary>
    public ShipDef? Def { get; private set; }

    /// <summary>The body of the ship's own parked copy in the client's world, if it still has one (its sweeps ignore it).</summary>
    public Func<uint>? IgnoreBody { get; set; }

    public uint NetId { get; private set; }

    public bool Active => Def != null;

    public uint LatestTick { get; private set; }

    public ShipState Latest => _states[LatestTick % Predictor.History];

    /// <summary>Ticks replayed after corrections, and how many corrections, for statistics.</summary>
    public long Replayed { get; private set; }

    public long Corrections { get; private set; }

    /// <summary>The predicted state after a tick, if kept.</summary>
    public bool TryGetState(uint tick, out ShipState state)
    {
        bool kept = Active && _stateTicks[tick % Predictor.History] == tick && tick <= LatestTick;
        state = kept ? _states[tick % Predictor.History] : default;
        return kept;
    }

    /// <summary>Stops predicting (the pilot stepped out, or died).</summary>
    public void Stop()
    {
        Def = null;
        NetId = 0;
    }

    /// <summary>Starts from the server's state of a ship after a tick, then replays the inputs already sent after it.</summary>
    public void Reset(ShipDef def, uint netId, uint tick, in ShipState state, uint lastInputTick)
    {
        Def = def;
        NetId = netId;
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
        if (!Active)
        {
            return;
        }
        while (LatestTick < tick)
        {
            ShipState state = Latest;
            uint next = LatestTick + 1;
            Step(next, ref state);
            LatestTick = next;
            Store(next, state);
        }
    }

    /// <summary>
    /// The server's state of the ship after <paramref name="tick"/>. Returns how far the prediction for that tick was
    /// off (metres), or null when it started again from the server's state (the tick no longer kept, another ship).
    /// </summary>
    public float? Reconcile(ShipDef def, uint netId, uint tick, in ShipState server, uint lastInputTick)
    {
        if (!Active || def != Def || netId != NetId || tick > LatestTick || _stateTicks[tick % Predictor.History] != tick)
        {
            Reset(def, netId, tick, server, Math.Max(lastInputTick, tick));
            return null;
        }
        ShipState predicted = _states[tick % Predictor.History];
        if (predicted.Equals(server))
        {
            return 0f;
        }
        float error = Vector3.Distance(predicted.Position, server.Position);
        Corrections++;
        Store(tick, server);
        ShipState state = server;
        for (uint t = tick + 1; t <= LatestTick; t++)
        {
            Step(t, ref state);
            Store(t, state);
            Replayed++;
        }
        return error;
    }

    private void Step(uint tick, ref ShipState state)
    {
        ShipInput input = ShipConvert.ToShipInput(_inputs.CommandFor(tick));
        input.Engines = true; // the pilot is in the seat (the server's zone: alive, seated, awake)
        ShipContext context = _context();
        lock (JoltRuntime.Gate)
        {
            ShipEvents events = ShipMotor.Step(ref state, input, Def!, context, IgnoreBody?.Invoke() ?? ShipCollider.NoBody);
            if (events.ReachedCeiling && !state.Has(ShipFlags.Destroyed))
            {
                ShipMotor.ParkInOrbit(ref state); // as the zone does when nothing takes the ship to space
            }
        }
    }

    private void Store(uint tick, in ShipState state)
    {
        _states[tick % Predictor.History] = state;
        _stateTicks[tick % Predictor.History] = tick;
    }
}
