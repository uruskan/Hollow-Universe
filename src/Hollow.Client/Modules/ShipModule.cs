using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using JoltPhysicsSharp;

namespace Hollow.Client;

// Ships in the client (GROUND.md §6.7): every ship near, interpolated, with its details (doors, seats, health; IRON_WAKE.md
// §5); the one the player flies from its helm, predicted (its controls ride in each tick's input); parked ships solid in the
// prediction's own physics. A ship people walk in (IRON_WAKE.md §4) has a frame near the player: its space, posed for each
// predicted tick as the server's is (its doors at their progress), in which the player's character is predicted aboard and
// the aim meets its pieces; everyone aboard is placed on it as drawn. The player in any seat is carried by the one seat
// rule (ShipSeats). What the interact key does at a ship is its parts' verbs (TargetModule, GROUND_SYSTEMS.md §5).
public sealed class ShipModule : ClientModule
{
    /// <summary>A ship with an interior has a space on this client within this many metres of the player (IRON_WAKE.md §4.3).</summary>
    public const float SpaceRadius = 300f;

    /// <summary>A space is kept until its ship is this much farther (so one at the edge isn't made over and over).</summary>
    public const float SpaceHysteresis = 50f;

    private readonly Dictionary<uint, RemoteShip> _ships = new();
    private readonly Dictionary<uint, uint> _parkedBodies = new();
    private readonly Dictionary<uint, uint> _shipsByBody = new();
    private readonly Dictionary<uint, ClientShipFrame> _frames = new();
    private readonly List<uint> _framesGone = new();
    private float[] _scratchParts = Array.Empty<float>();
    private ShipPredictor? _shipPredictor;
    private ShipCollider? _shipCollider;
    private bool _ownsShipBodies;
    private Vector3 _shipSmoothing;
    private int _ownSeat = -1;
    private uint _ownSeatFrame;
    private float[] _seatParts = Array.Empty<float>();

    /// <summary>Ships this client was told about, interpolated for drawing.</summary>
    public IEnumerable<RemoteShip> All => _ships.Values.Where(s => s.Spawned && s.HasState);

    /// <summary>Every ship known here, spawned or not, for the views that filter themselves.</summary>
    internal Dictionary<uint, RemoteShip>.ValueCollection Known => _ships.Values;

    public RemoteShip? ShipOf(EntityId id) => _ships.Values.FirstOrDefault(s => s.Id == id);

    /// <summary>A ship by its net ID (the wire's name for it, and for its frame), or null.</summary>
    public RemoteShip? ShipByNetId(uint netId) => _ships.GetValueOrDefault(netId);

    /// <summary>A parked collision body's ship, including ships without walkable interiors.</summary>
    public RemoteShip? ShipForBody(uint body) => _shipsByBody.TryGetValue(body, out uint netId) ? ShipByNetId(netId) : null;

    /// <summary>The ships' frames this client keeps (IRON_WAKE.md §4.3): their spaces, for the aim and the views.</summary>
    public IReadOnlyCollection<ClientShipFrame> Frames => _frames.Values;

    /// <summary>A ship's frame by its net ID, or null when this client keeps none for it.</summary>
    public ClientShipFrame? FrameOf(uint netId) => _frames.GetValueOrDefault(netId);

    /// <summary>The player is flying a ship from its helm (its prediction runs; the controls go out with every input).</summary>
    public bool Piloting => _shipPredictor?.Active == true;

    /// <summary>The player sits in a ship's seat (any: the helm, a passenger's, a station's; IRON_WAKE.md §5.2), as the server says.</summary>
    public bool Seated => _ownSeat >= 0 && SeatShip != null;

    /// <summary>The ship whose seat the player sits in (null on foot, or before it is known here).</summary>
    public RemoteShip? SeatShip
    {
        get
        {
            if (_ownSeat < 0)
            {
                return null;
            }
            if (_ownSeatFrame != 0)
            {
                return _ships.GetValueOrDefault(_ownSeatFrame);
            }
            if (_shipPredictor is { Active: true } own && _ships.TryGetValue(own.NetId, out RemoteShip? flown))
            {
                return flown;
            }
            EntityId me = HasCharacter ? EntityId : EntityId.None;
            foreach (RemoteShip ship in _ships.Values)
            {
                if (ship.SeatOf(me) >= 0)
                {
                    return ship;
                }
            }
            return null;
        }
    }

    /// <summary>The seat the player sits in (its index in its ship's seats), or −1.</summary>
    public int SeatIndex => Seated ? _ownSeat : -1;

    /// <summary>The seat the player sits in, as its kind has it (its role, its doors, its name), or null.</summary>
    public ShipSeatDef? Seat => SeatShip?.Def is { } def && _ownSeat < def.Seats.Count ? def.Seats[_ownSeat] : null;

    /// <summary>The ship flown, as predicted (its newest tick), its kind, and its remote copy (the hatch, the flags).</summary>
    public ShipState OwnShip => _shipPredictor?.Latest ?? default;

    /// <summary>The server's retained helm command, used when taking over a ship already in flight.</summary>
    public ShipInput OwnShipCruise { get; private set; }

    public ShipDef? OwnShipDef => _shipPredictor?.Def;

    public RemoteShip? OwnShipRemote => Piloting && _ships.TryGetValue(_shipPredictor!.NetId, out RemoteShip? s) ? s : null;

    /// <summary>The frames this client keeps, posed for a tick's step (the prediction asks before each).</summary>
    private void PoseFrames(uint tick)
    {
        foreach (ClientShipFrame frame in _frames.Values)
        {
            frame.PoseFor(tick);
        }
    }

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

    protected internal override bool Flies => Piloting;

    protected internal override void Init(MessageHandlers<GameClient> handlers) => handlers.On<ShipDetails>((_, in m) => OnDetails(m));

    protected internal override void Welcome()
    {
        _ownsShipBodies = Client.Options.SharedPhysics == null;
        _shipCollider = new ShipCollider(Prediction!.World);
        _shipPredictor = new ShipPredictor(Prediction, () => ShipContext)
        {
            IgnoreBody = () => _shipPredictor != null && _parkedBodies.TryGetValue(_shipPredictor.NetId, out uint body) ? body : ShipCollider.NoBody,
        };
        Prediction.Seat = SeatAt;
        Prediction.BeforeStep += PoseFrames;
        Client.DrawnFrame = DrawnPose;
        Client.SeatEye = SeatEye;
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
                    if (_ownsShipBodies) _shipCollider.RemoveParked(body);
                }
            }
            _shipCollider.Dispose();
        }
        _parkedBodies.Clear();
        _shipsByBody.Clear();
        _ships.Clear();
        _shipCollider = null;
        _shipPredictor = null;
    }

    // Where the player's seat is after a tick (the character's prediction carries the seated player there), by the one seat
    // rule (ShipSeats.Pose, the server's): in a ship people walk in, the seat's socket in the ship's frame at its parts'
    // states then (a gunner's turns with the turret), still; in one they only sit in, where the ship carries it: the ship
    // flown as predicted, or one someone else drives (an open boat's passenger) as its states have it, carried on past the
    // newest along its motion. Null when the seat's ship or state isn't known here (the character then stays where it was).
    private (ulong Frame, Vector3 Position, Vector3 Velocity)? SeatAt(uint tick)
    {
        if (SeatShip is not { Def: { } def } ship || _ownSeat >= def.Seats.Count)
        {
            return null;
        }
        ShipState state = default;
        if (def.Interior == null)
        {
            if (_shipPredictor is { Active: true } own && own.NetId == ship.NetId)
            {
                if (!own.TryGetState(tick, out state))
                {
                    return null;
                }
            }
            else if (ship.TryPoseAt(tick, out ShipPose carried))
            {
                state = new ShipState { Position = carried.Position, Rotation = carried.Rotation, Velocity = carried.Velocity };
            }
            else
            {
                return null;
            }
        }
        (bool aboard, Vector3 position, Vector3 velocity) = ShipSeats.Pose(def, _ownSeat, state, SeatParts(ship, def, tick));
        return (aboard ? ship.NetId : 0, position, velocity);
    }

    // A ship's parts' states at a tick, for its seats (none for a ship people only sit in).
    private ReadOnlySpan<float> SeatParts(RemoteShip ship, ShipDef def, long tick)
    {
        if (def.Interior is not { } interior)
        {
            return ReadOnlySpan<float>.Empty;
        }
        if (_seatParts.Length != interior.Parts.Count)
        {
            _seatParts = new float[interior.Parts.Count];
        }
        ship.PartsInto(interior, _seatParts, tick);
        // A gunner's own mount as predicted (IW3): the seat and the camera turn with the aim at once.
        Client.Stations().OverlayMount(ship.NetId, _seatParts);
        return _seatParts;
    }

    // The eye of the player's seat as drawn (IRON_WAKE.md §5.2: a station's camera socket), in a ship people walk in, or in
    // a seat of one they only sit in that someone else drives (an open boat's passenger: the seat's eye on the ship as drawn,
    // so the view rides the boat the player sees); null otherwise (the helm of one they only sit in has the ship's cameras
    // and the character's own eye).
    private Vector3? SeatEye()
    {
        if (SeatShip is not { Def: { } def } ship || _ownSeat >= def.Seats.Count || DrawnPose(ship.NetId) is not { } pose)
        {
            return null;
        }
        if (def.Interior == null)
        {
            return Piloting && _shipPredictor!.NetId == ship.NetId ? null : pose.ToWorld(ShipSeats.EyeInFrame(def, _ownSeat, ReadOnlySpan<float>.Empty));
        }
        ReadOnlySpan<float> parts = SeatParts(ship, def, (long)Client.RenderTick);
        if (_frames.TryGetValue(ship.NetId, out ClientShipFrame? frame))
        {
            // The space's states as posed (the doors where they are met), the gunner's own mount as predicted.
            frame.States.CopyTo(_seatParts, 0);
            Client.Stations().OverlayMount(ship.NetId, _seatParts);
            parts = _seatParts;
        }
        return pose.ToWorld(ShipSeats.EyeInFrame(def, _ownSeat, parts));
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
            ship.Def ??= Composed(Content.Ship(spawn.Name ?? ""), ship.Build);
        }
    }

    // A kind with a build as this client's data makes it (VEHICLES.md §9.1): the untrusted build made sane on the kind.
    private ShipDef? Composed(ShipDef? kind, ShipBuild build) =>
        kind == null || build.IsEmpty ? kind : Content.Compose(kind, ShipAssembler.Sane(kind, build.Entries, Content.Module));

    protected internal override void Despawned(uint netId)
    {
        if (_ships.Remove(netId))
        {
            Unpark(netId);
            DropFrame(netId);
        }
    }

    // The server's word on a ship's details (IRON_WAKE.md §5): its space follows them at the next step it is posed for; a
    // part no door drives that moved makes a parked one's body again, as the server does (a door's motion doesn't: the
    // server makes it again when the door stops, and its new body comes with the snapshots).
    private void OnDetails(in ShipDetails m)
    {
        RemoteShip ship = RemoteShipOf(m.Ship);
        int parts = ship.PartsVersion;
        // Its build first (VEHICLES.md §9.1): the layout the seats, doors and parts below are numbered by.
        ship.Build = new ShipBuild(ShipConvert.BuildOf(m));
        if (ship.Def is { } known)
        {
            ship.Def = Composed(known.Kind, ship.Build);
        }
        ship.SetDetails(m);
        if (ship.PartsVersion != parts && _parkedBodies.ContainsKey(m.Ship))
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
        _ownSeat = header.Own.Seat - 1;
        _ownSeatFrame = header.Own.Seat > 0 ? header.Own.Frame : 0;
        KeepFrames(header.OwnTick != 0 ? header.Own.Frame : 0);
        ReconcileShip(header);
    }

    private void ReceiveShips(uint serverTick)
    {
        foreach ((uint netId, ShipNetState state) in DecodedShips)
        {
            RemoteShip ship = RemoteShipOf(netId);
            ship.Def ??= Composed(Content.Ship((int)Math.Min(state.Kind, ushort.MaxValue)), ship.Build);
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
        if (ship.Def == null || _shipCollider == null || state.Body == 0)
        {
            return;
        }
        if (_shipsByBody.TryGetValue(state.Body, out uint other) && other != netId)
        {
            Unpark(other);
        }
        if (!_ownsShipBodies)
        {
            _parkedBodies.Add(netId, state.Body);
            _shipsByBody.Add(state.Body, netId);
            return;
        }
        Shape shape;
        if (ship.Def.Interior is { } interior)
        {
            if (_scratchParts.Length != interior.Parts.Count)
            {
                _scratchParts = new float[interior.Parts.Count];
            }
            ship.PartsInto(interior, _scratchParts, ship.NewestTick);
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
                _shipsByBody.Add(state.Body, netId);
            }
        }
    }

    private void Unpark(uint netId)
    {
        if (_parkedBodies.Remove(netId, out uint body))
        {
            _shipsByBody.Remove(body);
            lock (JoltRuntime.Gate)
            {
                if (_ownsShipBodies) _shipCollider?.RemoveParked(body);
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
                // The solid things aboard it, as boxes of its space: the prediction walks into them as the server does.
                foreach (RemoteThing thing in Client.Require<SurvivalModule>().ThingsAboard)
                {
                    if (thing.Support.Id == ship.Id.Value)
                    {
                        Box(frame, thing);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A thing aboard a ship came or went (IRON_WAKE.md §8): a solid one is a box of the ship's space where this client
    /// keeps one, as the server's is.
    /// </summary>
    internal void ThingAboard(RemoteThing thing, bool added)
    {
        if (ShipOf(new EntityId(thing.Support.Id)) is not { } ship || !_frames.TryGetValue(ship.NetId, out ClientShipFrame? frame))
        {
            return;
        }
        if (added)
        {
            Box(frame, thing);
            if (DrawnPose(ship.NetId) is { } pose)
            {
                thing.Place(pose);
            }
        }
        else
        {
            frame.Space.RemoveThing(thing.Id.Value);
        }
    }

    private void Box(ClientShipFrame frame, RemoteThing thing)
    {
        if (thing.Deployable(Content) is { Solid: true } def)
        {
            frame.Space.AddThing(thing.Id.Value, def.HalfExtents, thing.LocalPosition, thing.LocalRotation);
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
        OwnShipCruise = new ShipInput
        {
            Throttle = Math.Clamp(float.IsFinite(o.CruiseThrottle) ? o.CruiseThrottle : 0, -1, 1),
            Strafe = Math.Clamp(float.IsFinite(o.CruiseStrafe) ? o.CruiseStrafe : 0, -1, 1),
            Vertical = Math.Clamp(float.IsFinite(o.CruiseVertical) ? o.CruiseVertical : 0, -1, 1),
            Buttons = o.Flags.HasFlag(ShipFlags.AssistOff) ? ShipButtons.AssistOff : ShipButtons.None,
        };
        // The layout it is flown with: its own (its build's, from its details) when known, else its kind's.
        ShipDef? def = _ships.TryGetValue(o.NetId, out RemoteShip? flown) && flown.Def is { } known && known.Id == o.Kind
            ? known : Content.Ship((int)Math.Min(o.Kind, ushort.MaxValue));
        if (def == null)
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
        // The things aboard ships, where their ships are drawn (IRON_WAKE.md §8): a box on a deck never lags its deck.
        foreach (RemoteThing thing in Client.Require<SurvivalModule>().ThingsAboard)
        {
            if (ShipOf(new EntityId(thing.Support.Id)) is { } ship && DrawnPose(ship.NetId) is { } pose)
            {
                thing.Place(pose);
            }
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class ShipModuleAccess
{
    public static ShipModule Ships(this GameClient client) => client.Require<ShipModule>();
}
