using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;
using JoltPhysicsSharp;

namespace Hollow.Client;

// Ships in the client (GROUND.md §6.7): every ship near, interpolated; the one the player flies, predicted (its controls
// ride in each tick's input); parked ships solid in the prediction's own physics. A ship people walk in (IRON_WAKE.md §4)
// has a frame near the player: its space, in which the player's character is predicted aboard and the aim meets its
// pieces; everyone aboard is placed on the ship as drawn. What the interact key does at a ship is its parts' verbs
// (TargetModule, GROUND_SYSTEMS.md §5).
public sealed class ShipModule : ClientModule
{
    /// <summary>A ship with an interior has a space on this client within this many metres of the player (IRON_WAKE.md §4.3).</summary>
    public const float SpaceRadius = 300f;

    /// <summary>A space is kept until its ship is this much farther (so one at the edge isn't made over and over).</summary>
    public const float SpaceHysteresis = 50f;

    private readonly Dictionary<uint, RemoteShip> _ships = new();
    private readonly Dictionary<uint, uint> _parkedBodies = new();
    private readonly Dictionary<uint, ClientShipFrame> _frames = new();
    private readonly List<uint> _framesGone = new();
    private float[] _scratchParts = Array.Empty<float>();
    private ShipPredictor? _shipPredictor;
    private ShipCollider? _shipCollider;
    private bool _ownsShipBodies;
    private Vector3 _shipSmoothing;

    /// <summary>Ships this client was told about, interpolated for drawing.</summary>
    public IEnumerable<RemoteShip> All => _ships.Values.Where(s => s.Spawned && s.HasState);

    /// <summary>Every ship known here, spawned or not, for the views that filter themselves.</summary>
    internal Dictionary<uint, RemoteShip>.ValueCollection Known => _ships.Values;

    public RemoteShip? ShipOf(EntityId id) => _ships.Values.FirstOrDefault(s => s.Id == id);

    /// <summary>A ship by its net ID (the wire's name for it, and for its frame), or null.</summary>
    public RemoteShip? ShipByNetId(uint netId) => _ships.GetValueOrDefault(netId);

    /// <summary>The ships' frames this client keeps (IRON_WAKE.md §4.3): their spaces, for the aim and the views.</summary>
    public IReadOnlyCollection<ClientShipFrame> Frames => _frames.Values;

    /// <summary>A ship's frame by its net ID, or null when this client keeps none for it.</summary>
    public ClientShipFrame? FrameOf(uint netId) => _frames.GetValueOrDefault(netId);

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
    public ShipContext ShipContext => new(Frame, Surface!, ShipContext.GravityOf(Zone!.SurfaceGravity), (float)Zone.AirDensity,
        (float)Zone.AirScaleHeight, _shipCollider);

    /// <summary>Parked ships' bodies in the prediction's physics (null before joining): the placement world avoids them.</summary>
    internal ShipCollider? Collider => _shipCollider;

    protected internal override bool Seated => Piloting;

    protected internal override void Init(MessageHandlers<GameClient> handlers) => handlers.On<ShipPartStates>((_, in m) => OnParts(m));

    protected internal override void Welcome()
    {
        _ownsShipBodies = Client.Options.SharedPhysics == null;
        _shipCollider = new ShipCollider(Prediction!.World);
        _shipPredictor = new ShipPredictor(Prediction, () => ShipContext)
        {
            IgnoreBody = () => _shipPredictor != null && _parkedBodies.TryGetValue(_shipPredictor.NetId, out uint body) ? body : ShipCollider.NoBody,
        };
        Prediction.Seat = SeatAt;
        Client.DrawnFrame = DrawnPose;
    }

    public override void Dispose()
    {
        foreach (ClientShipFrame frame in _frames.Values)
        {
            Prediction?.Frames.Remove(frame.Key);
            frame.Dispose();
        }
        _frames.Clear();
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

    // Where the predicted ship's seat is after a tick (the character's prediction carries the pilot there): in a ship people
    // walk in, the seat in the ship's frame, still (IRON_WAKE.md §4.2); in one they only sit in, where the ship carries it.
    private (ulong Frame, Vector3 Position, Vector3 Velocity)? SeatAt(uint tick)
    {
        if (_shipPredictor is not { Active: true } ships || !ships.TryGetState(tick, out ShipState s))
        {
            return null;
        }
        if (ships.Def!.Interior != null)
        {
            return (ships.NetId, ships.Def.Seat, Vector3.Zero);
        }
        return (0, s.PointOf(ships.Def.Seat, ships.Def.CentreOfMass), s.Velocity);
    }

    // ---------------------------------------------------------------- frames as drawn (IRON_WAKE.md §4.8)

    /// <summary>
    /// Where a ship is drawn now, as a frame (IRON_WAKE.md §4.8): the one the player flies as predicted, any other as
    /// interpolated 100 ms behind; null when not known. The player's camera aboard, the people aboard and shots from a
    /// deck are placed through it.
    /// </summary>
    public ShipPose? DrawnPose(uint netId)
    {
        if (_shipPredictor is { Active: true } own && own.NetId == netId)
        {
            ShipState s = own.Latest;
            return new ShipPose(OwnShipRenderPosition, OwnShipRenderRotation, s.Velocity, Vector3.Transform(s.AngularVelocity, s.Rotation), own.Def!.CentreOfMass);
        }
        if (!_ships.TryGetValue(netId, out RemoteShip? ship) || !ship.HasState || ship.Def == null)
        {
            return null;
        }
        return new ShipPose(ship.Position, ship.Rotation, ship.Velocity, Vector3.Zero, ship.Def.CentreOfMass);
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

    protected internal override void Spawned(in Spawn spawn)
    {
        if (spawn.Kind == EntityKind.Ship)
        {
            RemoteShip ship = RemoteShipOf(spawn.NetId);
            ship.Spawned = true;
            ship.Id = new EntityId(spawn.EntityId);
            ship.Def ??= Content.Ship(spawn.Name ?? "");
        }
    }

    protected internal override void Despawned(uint netId)
    {
        if (_ships.Remove(netId))
        {
            Unpark(netId);
            DropFrame(netId);
        }
    }

    // The server moved a ship's parts (IRON_WAKE.md §4.7): its space follows, and a parked one's body is made again.
    private void OnParts(in ShipPartStates m)
    {
        RemoteShip ship = RemoteShipOf(m.Ship);
        ship.SetParts(m.Parts);
        _frames.GetValueOrDefault(m.Ship)?.FollowParts();
        if (_parkedBodies.ContainsKey(m.Ship))
        {
            Unpark(m.Ship);
            Park(m.Ship, ship, ship.Newest);
        }
    }

    // A snapshot's ships: interpolation samples, and parked ships' bodies in the prediction's own physics; the frames near
    // the player (and the one the server says the player is in); then the ship the player flies, as the server had it
    // after the snapshot's tick: start or reconcile its prediction, before the character's (it is carried by its seat).
    protected internal override void AfterSnapshot(in SnapshotHeader header)
    {
        ReceiveShips(header.ServerTick);
        if (header.OwnTick == 0 || Prediction == null)
        {
            return;
        }
        KeepFrames(header.OwnTick != 0 ? header.Own.Frame : 0);
        ReconcileShip(header);
    }

    private void ReceiveShips(uint serverTick)
    {
        foreach ((uint netId, ShipNetState state) in DecodedShips)
        {
            RemoteShip ship = RemoteShipOf(netId);
            ship.Def ??= Content.Ship((int)Math.Min(state.Kind, ushort.MaxValue));
            ship.Add(serverTick, state);
            // Parked as the server has it: its body there, with that body's ID (a new ID is a new parking).
            bool parked = state.Body != 0;
            if (parked && _parkedBodies.TryGetValue(netId, out uint held) && held != state.Body)
            {
                Unpark(netId);
            }
            if (parked && !_parkedBodies.ContainsKey(netId))
            {
                Park(netId, ship, state);
            }
            else if (!parked)
            {
                Unpark(netId);
            }
        }
    }

    // A parked ship's static body in the prediction's physics, as the server's zone has it: the hull's boxes, or the whole
    // collision of a ship people walk in at its parts' states (§4.6), at the pose the server rested on the wire's grid, with
    // the server's body ID (the same contacts in the same order). Another ship's body holding that ID here is a stale one.
    private void Park(uint netId, RemoteShip ship, in ShipNetState state)
    {
        if (!_ownsShipBodies || ship.Def == null || _shipCollider == null || state.Body == 0)
        {
            return;
        }
        foreach ((uint other, uint held) in _parkedBodies)
        {
            if (held == state.Body && other != netId)
            {
                Unpark(other);
                break;
            }
        }
        Shape shape;
        if (ship.Def.Interior is { } interior)
        {
            if (_scratchParts.Length != interior.Parts.Count)
            {
                _scratchParts = new float[interior.Parts.Count];
            }
            ship.PartsInto(interior, _scratchParts);
            shape = ShipSpace.ParkedShape(interior, _scratchParts);
        }
        else
        {
            shape = ShipCollider.HullShape(ship.Def, swept: false);
        }
        lock (JoltRuntime.Gate)
        {
            if (_shipCollider.AddParkedAt(state.Body, shape, ShipConvert.PositionOf(state), ShipConvert.RotationOf(state)))
            {
                _parkedBodies.Add(netId, state.Body);
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

    private uint ParkedBodyOf(uint netId) => _parkedBodies.GetValueOrDefault(netId);

    // The frames the prediction needs: every ship with an interior within SpaceRadius of the player's character (kept to
    // the hysteresis), and the one it is aboard, here or as the server says (§4.8).
    private void KeepFrames(uint serverFrame)
    {
        if (Prediction is not { } prediction || _shipPredictor == null)
        {
            return;
        }
        // Before the first state (a join), only the frame the server names.
        uint aboard = prediction.HasState ? (uint)prediction.Latest.Frame : 0;
        Vector3 at = prediction.HasState ? prediction.WorldOf(prediction.Latest, prediction.LatestTick + 1) : new Vector3(float.NaN);
        float keep = (SpaceRadius + SpaceHysteresis) * (SpaceRadius + SpaceHysteresis);
        _framesGone.Clear();
        foreach ((uint netId, ClientShipFrame frame) in _frames)
        {
            bool near = frame.Ship.HasState && Vector3.DistanceSquared(ShipConvert.PositionOf(frame.Ship.Newest), at) <= keep;
            if (!_ships.ContainsKey(netId) || (netId != aboard && netId != serverFrame && !near))
            {
                _framesGone.Add(netId);
            }
        }
        foreach (uint netId in _framesGone)
        {
            DropFrame(netId);
        }
        foreach ((uint netId, RemoteShip ship) in _ships)
        {
            if (_frames.ContainsKey(netId) || ship.Def?.Interior == null || !ship.HasState)
            {
                continue;
            }
            if (netId == aboard || netId == serverFrame || Vector3.DistanceSquared(ShipConvert.PositionOf(ship.Newest), at) <= SpaceRadius * SpaceRadius)
            {
                var frame = new ClientShipFrame(ship, ship.Def, _shipPredictor, ParkedBodyOf, prediction.Tuning, prediction.Frames.Zone.Gravity);
                _frames.Add(netId, frame);
                prediction.Frames.Add(frame);
            }
        }
    }

    private void DropFrame(uint netId)
    {
        if (_frames.Remove(netId, out ClientShipFrame? frame))
        {
            Prediction?.Frames.Remove(netId);
            frame.Dispose();
        }
    }

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
        float? error = _shipPredictor.Reconcile(def, o.NetId, header.OwnTick, ShipConvert.FromOwn(o), NextTick - 1);
        if (error is > 0)
        {
            Stats.ShipCorrections++;
        }
        Vector3 jump = before - _shipPredictor.Latest.Position;
        _shipSmoothing = wasActive && (_shipSmoothing + jump).LengthSquared() < 4f ? _shipSmoothing + jump : Vector3.Zero;
    }

    // The ship flown is predicted first, as the server steps it; then the frames the character's step may need.
    protected internal override void BeforePredict(uint tick)
    {
        _shipPredictor?.Predict(tick);
        KeepFrames(0);
    }

    protected internal override void Render(double delta)
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
            float alpha = (float)Math.Clamp(TickClock - NextTick + 1, 0, 1);
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

    // Every ship as drawn now; then everyone aboard one, placed on it as drawn (§4.8: interpolated in its frame, never
    // in the zone's, which would cut the corner of a turning ship's arc).
    protected internal override void Sample(double renderTick)
    {
        foreach (RemoteShip ship in _ships.Values)
        {
            ship.Sample(renderTick);
        }
        foreach (RemoteEntity remote in Remotes.Values)
        {
            if (remote.Frame != 0)
            {
                remote.Place(DrawnPose(remote.Frame));
            }
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class ShipModuleAccess
{
    public static ShipModule Ships(this GameClient client) => client.Require<ShipModule>();
}
