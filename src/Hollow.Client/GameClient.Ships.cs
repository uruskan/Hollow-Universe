using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;

namespace Hollow.Client;

/// <summary>What the interact key does at a ship (G6).</summary>
public enum ShipTouch : byte
{
    /// <summary>Open the hatch, or shut it.</summary>
    Hatch,

    /// <summary>Climb into the seat (the hatch is open, the seat free).</summary>
    Board,

    /// <summary>Fill the tank from the inventory's lamp oil.</summary>
    Refuel,

    /// <summary>Open the cargo.</summary>
    Cargo,
}

/// <summary>A ship within reach the interact key acts on, and how to say so ("E: Board Tin Finch").</summary>
public readonly record struct ShipInteraction(ShipTouch Touch, RemoteShip Ship, string Verb, string Name, float Distance);

// Ships in the client (GROUND.md §6.7): every ship near, interpolated; the one the player flies, predicted (its controls
// ride in each tick's input); parked ships solid in the prediction's own physics; what the interact key does at a ship.
public sealed partial class GameClient
{
    private readonly Dictionary<uint, RemoteShip> _ships = new();
    private readonly List<(uint NetId, ShipNetState State)> _decodedShips = new();
    private readonly Dictionary<uint, uint> _parkedBodies = new();
    private ShipPredictor? _shipPredictor;
    private ShipCollider? _shipCollider;
    private bool _ownsShipBodies;
    private Vector3 _shipSmoothing;

    /// <summary>Ships this client was told about, interpolated for drawing.</summary>
    public IEnumerable<RemoteShip> Ships => _ships.Values.Where(s => s.Spawned && s.HasState);

    public RemoteShip? ShipOf(EntityId id) => _ships.Values.FirstOrDefault(s => s.Id == id);

    /// <summary>The player is flying a ship (its prediction runs; the controls go out with every input).</summary>
    public bool Piloting => _shipPredictor?.Active == true;

    /// <summary>The ship flown, as predicted (its newest tick), its kind, and its remote copy (the hatch, the flags).</summary>
    public ShipState OwnShip => _shipPredictor?.Latest ?? default;

    public ShipDef? OwnShipDef => _shipPredictor?.Def;

    public RemoteShip? OwnShipRemote => Piloting && _ships.TryGetValue(_shipPredictor!.NetId, out RemoteShip? s) ? s : null;

    /// <summary>The ship flown as drawn now: between its last two predicted ticks, plus what is left of a smoothed correction.</summary>
    public Vector3 OwnShipRenderPosition { get; private set; }

    public Quaternion OwnShipRenderRotation { get; private set; } = Quaternion.Identity;

    /// <summary>The ship prediction, for statistics and tests.</summary>
    public ShipPredictor? ShipPrediction => _shipPredictor;

    /// <summary>The ships' world on this client: the zone's frame and ground, the planet's gravity and air, the prediction's physics.</summary>
    public ShipContext ShipContext => new(Frame, _surface!, ShipContext.GravityOf(Zone!.SurfaceGravity), (float)Zone.AirDensity,
        (float)Zone.AirScaleHeight, _shipCollider);

    private void StartShips()
    {
        DisposeShips();
        _ownsShipBodies = _options.SharedPhysics == null;
        _shipCollider = new ShipCollider(_predictor!.World);
        _shipPredictor = new ShipPredictor(_predictor, () => ShipContext)
        {
            IgnoreBody = () => _shipPredictor != null && _parkedBodies.TryGetValue(_shipPredictor.NetId, out uint body) ? body : ShipCollider.NoBody,
        };
        _predictor.Seat = SeatAt;
    }

    private void DisposeShips()
    {
        if (_shipCollider != null)
        {
            lock (JoltRuntime.Gate)
            {
                foreach (uint body in _parkedBodies.Values)
                {
                    _shipCollider.RemoveParked(body);
                }
            }
            _shipCollider.Dispose();
        }
        _parkedBodies.Clear();
        _ships.Clear();
        _shipCollider = null;
        _shipPredictor = null;
    }

    // Where the predicted ship's seat is after a tick (the character's prediction carries the pilot there).
    private (Vector3 Position, Vector3 Velocity)? SeatAt(uint tick)
    {
        if (_shipPredictor is not { Active: true } ships || !ships.TryGetState(tick, out ShipState s))
        {
            return null;
        }
        return (s.PointOf(ships.Def!.Seat, ships.Def.CentreOfMass), s.Velocity);
    }

    private RemoteShip RemoteShipOf(uint netId)
    {
        if (!_ships.TryGetValue(netId, out RemoteShip? ship))
        {
            ship = new RemoteShip(netId);
            _ships.Add(netId, ship);
        }
        return ship;
    }

    private void SpawnShip(uint netId, ulong entityId, string key)
    {
        RemoteShip ship = RemoteShipOf(netId);
        ship.Spawned = true;
        ship.Id = new EntityId(entityId);
        ship.Def ??= Content.Ship(key);
    }

    private void DespawnShip(uint netId)
    {
        if (_ships.Remove(netId))
        {
            Unpark(netId);
        }
    }

    // A snapshot's ships: interpolation samples, and parked ships' bodies in the prediction's own physics.
    private void ReceiveShips(uint serverTick)
    {
        foreach ((uint netId, ShipNetState state) in _decodedShips)
        {
            RemoteShip ship = RemoteShipOf(netId);
            ship.Def ??= Content.Ship((int)Math.Min(state.Kind, ushort.MaxValue));
            ship.Add(serverTick, state);
            bool parked = (state.Flags & ShipNetFlags.Asleep) != 0 && (state.Flags & (ShipNetFlags.Piloted | ShipNetFlags.Destroyed | ShipNetFlags.InOrbit)) == 0;
            if (parked && _ownsShipBodies && ship.Def != null && !_parkedBodies.ContainsKey(netId))
            {
                lock (JoltRuntime.Gate)
                {
                    uint body = _shipCollider!.AddParked(ship.Def, ShipConvert.PositionOf(state), ShipConvert.RotationOf(state));
                    if (body != 0)
                    {
                        _parkedBodies.Add(netId, body);
                    }
                }
            }
            else if (!parked)
            {
                Unpark(netId);
            }
        }
    }

    private void Unpark(uint netId)
    {
        if (_parkedBodies.Remove(netId, out uint body))
        {
            lock (JoltRuntime.Gate)
            {
                _shipCollider?.RemoveParked(body);
            }
        }
    }

    // The ship the player flies, as the server had it after the snapshot's tick: start or reconcile its prediction.
    private void ReconcileShip(in SnapshotHeader header)
    {
        if (_shipPredictor == null)
        {
            return;
        }
        if (header.OwnShip is not { Length: > 0 } own || header.OwnTick == 0)
        {
            _shipPredictor.Stop();
            return;
        }
        OwnShipState o = own[0];
        if (Content.Ship((int)Math.Min(o.Kind, ushort.MaxValue)) is not { } def)
        {
            return;
        }
        Vector3 before = _shipPredictor.Active ? _shipPredictor.Latest.Position : Vector3.Zero;
        bool wasActive = _shipPredictor.Active;
        float? error = _shipPredictor.Reconcile(def, o.NetId, header.OwnTick, ShipConvert.FromOwn(o), _nextTick - 1);
        if (error is > 0)
        {
            Stats.ShipCorrections++;
        }
        Vector3 jump = before - _shipPredictor.Latest.Position;
        _shipSmoothing = wasActive && (_shipSmoothing + jump).LengthSquared() < 4f ? _shipSmoothing + jump : Vector3.Zero;
    }

    private void PredictShip(uint tick) => _shipPredictor?.Predict(tick);

    private void UpdateShipRender(double delta)
    {
        if (_shipPredictor is not { Active: true } ships)
        {
            _shipSmoothing = Vector3.Zero;
            return;
        }
        ShipState current = ships.Latest;
        Vector3 position = current.Position;
        Quaternion rotation = current.Rotation;
        if (ships.TryGetState(ships.LatestTick - 1, out ShipState previous))
        {
            float alpha = (float)Math.Clamp(_tickClock - _nextTick + 1, 0, 1);
            position = Vector3.Lerp(previous.Position, current.Position, alpha);
            rotation = Quaternion.Slerp(previous.Rotation, current.Rotation, alpha);
        }
        _shipSmoothing *= (float)Math.Exp(-delta * 12);
        if (_shipSmoothing.LengthSquared() < 1e-8f)
        {
            _shipSmoothing = Vector3.Zero;
        }
        OwnShipRenderPosition = position + _shipSmoothing;
        OwnShipRenderRotation = rotation;
    }

    private void SampleShips(double renderTick)
    {
        foreach (RemoteShip ship in _ships.Values)
        {
            ship.Sample(renderTick);
        }
    }

    /// <summary>
    /// What the interact key would do at a ship from the eye along a direction: its hatch (open, shut, board), its fuel
    /// cap or its cargo, within the ship's reach; null for none.
    /// </summary>
    public ShipInteraction? AimShip(Vector3 eye, Vector3 direction)
    {
        ShipInteraction? best = null;
        foreach (RemoteShip ship in _ships.Values)
        {
            if (!ship.Spawned || !ship.HasState || ship.Def is not { } def || ship.Has(ShipNetFlags.Destroyed) || ship.Has(ShipNetFlags.InOrbit) ||
                Vector3.DistanceSquared(ship.Position, eye) > (def.BoundingRadius + def.Reach + 1) * (def.BoundingRadius + def.Reach + 1))
            {
                continue;
            }
            void Consider(Vector3 local, float radius, ShipTouch touch, string verb)
            {
                float t = Geometry.RaySphere(eye, direction, def.Reach + 0.5f, ship.PointOf(local), radius);
                if (t >= 0 && (best == null || t < best.Value.Distance))
                {
                    best = new ShipInteraction(touch, ship, verb, def.Name, t);
                }
            }
            bool open = ship.Has(ShipNetFlags.HatchOpen);
            if (open && !ship.Has(ShipNetFlags.Piloted) && ship.Hatch > 0.5f)
            {
                Consider(def.Hatch, 0.7f, ShipTouch.Board, "Board");
            }
            else
            {
                Consider(def.Hatch, 0.7f, ShipTouch.Hatch, open ? "Shut the hatch of" : "Open the hatch of");
            }
            Consider(def.FuelCap, 0.45f, ShipTouch.Refuel, "Refuel");
            if (open)
            {
                Consider(def.Hatch + new Vector3(0.15f, -0.45f, -0.75f), 0.45f, ShipTouch.Cargo, "Open the cargo of");
            }
        }
        return best;
    }

    /// <summary>Acts on what <see cref="AimShip"/> found. Returns the intent's sequence number.</summary>
    public uint Interact(in ShipInteraction target) => target.Touch switch
    {
        ShipTouch.Board => Local.Board(target.Ship.Id),
        ShipTouch.Refuel => Local.RefuelShip(target.Ship.Id),
        ShipTouch.Cargo => Local.Open(target.Ship.Id),
        _ => Local.ShipHatch(target.Ship.Id),
    };
}
