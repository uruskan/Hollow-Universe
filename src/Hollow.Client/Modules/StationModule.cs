using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

/// <summary>A battery's shot as the views play it (IW3): the ship, its station and barrel, the round, the muzzle and the way it left (world), whose.</summary>
public readonly record struct StationShotView(uint Ship, int Station, int Barrel, uint Number, ushort Kind, Vector3 Origin, Vector3 Direction, bool Own, bool Predicted)
{
    public AttackId Attack { get; init; }
}

// The crewed batteries in the client (IW3, IRON_WAKE.md §6.8, §6.9): each ship's stations as the server last said
// (ShipStations: their loaders' records, chambers, shots, kinds, magazines' counts, refusals), the shots others fire
// (TurretShot), and the player's own station when they sit at one: its controls into each tick's input, its mount
// predicted from the player's own aim by the same rule as the server's, and its shots predicted on the same loader
// timeline. Each press has an actor-scoped attempt identity, independent of accepted shot counts. An accepted shot
// keeps its chamber reservation until the matching station state arrives; only an explicit refusal takes it back.
public sealed class StationModule : ClientModule
{
    private sealed class Pending
    {
        public AttackId Attack;
        public uint Ship;
        public int Station;
        public uint Number;
        public int Barrel = -1;
        public long Tick;
        public ushort Kind;
        public ClientProjectile? Rocket;
        public bool Predicted;
        public bool Confirmed;
    }

    private readonly Dictionary<uint, StationState[]> _records = new();
    private readonly List<Pending> _pending = new();
    private readonly Dictionary<(uint, int), StationState> _display = new();
    private StationState? _mount;
    private uint _mountShip;
    private int _mountStation = -1;
    private byte _choice, _cycle;
    private bool _fireWas;
    private long _nextRipple;
    private uint _nextAttack = 1;
    private byte _viewDelay;
    private float[] _scratch = Array.Empty<float>();
    private ShipModule? _shipModule;
    private ShipModule Ships => _shipModule ??= Client.Require<ShipModule>();
    private SurvivalModule? _survivalModule;
    private SurvivalModule Survival => _survivalModule ??= Client.Require<SurvivalModule>();

    /// <summary>Raised for every battery shot to play: the player's own predicted ones at once, others' as the server's arrive.</summary>
    public event Action<StationShotView>? Shot;

    /// <summary>The player's own last refusal (§6.7: the sight's line) and when (the client's clock).</summary>
    public Outcome Refused { get; private set; }

    public double RefusedAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Predicted shots and how many the server confirmed or took back (tests, the network graph).</summary>
    public long ShotsPredicted { get; private set; }

    public long ShotsConfirmed { get; private set; }

    public long ShotsUndone { get; private set; }

    public long ShotsUnpredicted { get; private set; }

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers.On<ShipStations>((_, in m) => OnStations(m)).On<TurretShot>((_, in m) => OnShot(m));
        Client.Attacks.Resolved += OnResolved;
    }

    public override void Dispose()
    {
        _records.Clear();
        _pending.Clear();
        _display.Clear();
        _mount = null;
        _fireWas = false;
        _nextRipple = 0;
        _nextAttack = 1;
    }

    protected internal override void Despawned(uint netId)
    {
        _records.Remove(netId);
        _pending.RemoveAll(p => p.Ship == netId);
    }

    // ---------------------------------------------------------------- the server's word

    /// <summary>A ship's stations as the server last said (made at their kinds' rest before any word).</summary>
    public StationState[]? Records(RemoteShip ship)
    {
        if (ship.Def is not { Stations.Count: > 0 } def)
        {
            return null;
        }
        if (!_records.TryGetValue(ship.NetId, out StationState[]? states) || states.Length != def.Stations.Count)
        {
            states = new StationState[def.Stations.Count];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = new StationState(def.Stations[i]);
            }
            _records[ship.NetId] = states;
        }
        return states;
    }

    private void OnStations(in ShipStations m)
    {
        if (Ships.ShipByNetId(m.Ship) is not { } ship || Records(ship) is not { } states)
        {
            return;
        }
        foreach (StationNet net in m.Stations ?? Array.Empty<StationNet>())
        {
            if (net.Station < states.Length && net.Shots >= states[net.Station].Shots)
            {
                StationConvert.Apply(net, states[net.Station], Content);
            }
        }
        RetireReservations(m.Ship, states);
    }

    private void OnShot(TurretShot m)
    {
        var attack = new AttackId(m.AttackKind, m.AttackSeq, m.AttackSession);
        bool own = m.Gunner == NetId && attack.Session == Client.Attacks.Session;
        if (own && !attack.IsNone && _pending.Find(p => p.Attack == attack && p.Ship == m.Ship && p.Station == m.Station) is { } mine)
        {
            Confirm(mine, m.Number, m.Barrel, m.Tick);
        }
        if (own && !Client.Attacks.TryPresent(attack))
        {
            return; // Presentation history outlives both reservation retirement and reliable retransmission.
        }
        if (own)
        {
            ShotsUnpredicted++;
        }
        if (Ships.ShipByNetId(m.Ship) is not { Def: { Interior: { } interior } def } ship || m.Station >= def.Stations.Count || m.Barrel >= def.Stations[m.Station].Barrels.Count)
        {
            return;
        }
        if (Records(ship) is { } states)
        {
            // The recoil starts now as drawn, whatever the next record says.
            StationState st = states[m.Station];
            st.ShotTicks[m.Barrel] = m.Tick;
        }
        Vector3 origin = new(m.OriginX, m.OriginY, m.OriginZ);
        Vector3 direction = new(m.DirectionX, m.DirectionY, m.DirectionZ);
        Shot?.Invoke(new StationShotView(m.Ship, m.Station, m.Barrel, m.Number, (ushort)Math.Min(m.Kind, ushort.MaxValue), origin, direction, own, false) { Attack = attack });
    }

    private void Confirm(Pending pending, uint number, int barrel, long tick)
    {
        if (!pending.Confirmed && pending.Predicted)
        {
            ShotsConfirmed++;
        }
        pending.Confirmed = true;
        pending.Number = number;
        pending.Barrel = barrel;
        pending.Tick = tick;
        if (_records.TryGetValue(pending.Ship, out StationState[]? states))
        {
            RetireReservations(pending.Ship, states);
        }
    }

    private void RetireReservations(uint ship, StationState[] states) =>
        _pending.RemoveAll(p => p.Ship == ship && p.Confirmed && p.Station < states.Length && p.Number <= states[p.Station].Shots);

    private void OnResolved(AttackResolution resolution)
    {
        if (resolution.Id.Kind != Hollow.Simulation.Combat.AttackDomain.Station ||
            _pending.Find(p => p.Attack == resolution.Id) is not { } pending)
        {
            return;
        }
        if (resolution.Code == OutcomeCode.Done && !resolution.Cancelled)
        {
            Confirm(pending, resolution.StateSequence, resolution.Barrel, resolution.StartTick);
            _nextRipple = Math.Max(_nextRipple, resolution.ReadyTick);
            return;
        }
        _pending.Remove(pending);
        if (pending.Predicted)
        {
            ShotsUndone++;
            if (pending.Rocket != null)
            {
                Survival.RemoveOwnRocket(pending.Rocket);
            }
        }
        Refused = new Outcome(resolution.Code);
        RefusedAt = ClockSeconds;
    }

    // ---------------------------------------------------------------- the player's own station

    /// <summary>The station the player sits at, with its ship, or null.</summary>
    public (RemoteShip Ship, ShipStationDef Station)? Own
    {
        get
        {
            if (Ships.Seat is not { Role: SeatRole.Station } seat || Ships.SeatShip is not { Def: { } def } ship || def.StationOfSeat(seat.Index) is not { } station)
            {
                return null;
            }
            return (ship, station);
        }
    }

    /// <summary>Whether the player sits at a station.</summary>
    public bool AtStation => Own != null;

    /// <summary>The kind asked for (its index in the battery's ammo), as the next inputs say it.</summary>
    public void Choose(int kind)
    {
        if (Own is { } own && kind >= 0 && kind < own.Station.Battery.Ammo.Count)
        {
            _choice = (byte)(kind + 1);
        }
    }

    /// <summary>
    /// A tap of R (§6.5): the next kind the magazine holds becomes the chosen one, and rounds of another kind already
    /// chambered go back to the magazine (the server draws them back through the mechanism in reverse).
    /// </summary>
    public void Cycle()
    {
        if (Own is not { } own || Display(own.Ship, own.Station.Index) is not { } st)
        {
            return;
        }
        BatteryDef battery = own.Station.Battery;
        int current = _choice > 0 ? _choice - 1 : st.Chosen;
        for (int k = 1; k <= battery.Ammo.Count; k++)
        {
            int next = (current + k) % battery.Ammo.Count;
            if (st.Count(battery.Ammo[next].Id) > 0 || next == current)
            {
                _choice = (byte)(next + 1);
                break;
            }
        }
        _cycle = (byte)((_cycle + 1) & 3);
    }

    /// <summary>
    /// A station as drawn and shown: the server's record, and for the player's own the shots predicted since and the mount
    /// as predicted. Null for a ship without stations.
    /// </summary>
    public StationState? Display(RemoteShip ship, int station)
    {
        if (Records(ship) is not { } states || station < 0 || station >= states.Length)
        {
            return null;
        }
        StationState record = states[station];
        bool own = _mount != null && _mountShip == ship.NetId && _mountStation == station;
        if (!own && !_pending.Exists(p => p.Ship == ship.NetId && p.Station == station))
        {
            return record;
        }
        if (!_display.TryGetValue((ship.NetId, station), out StationState? shown))
        {
            shown = new StationState(record.Def);
            _display[(ship.NetId, station)] = shown;
        }
        Copy(record, shown);
        foreach (Pending p in _pending)
        {
            if (p.Ship != ship.NetId || p.Station != station || p.Barrel < 0 ||
                !p.Predicted && !p.Confirmed || p.Confirmed && p.Number <= record.Shots)
            {
                continue;
            }
            Overlay(shown, p.Barrel, p.Tick);
            shown.Shots = Math.Max(shown.Shots, p.Number);
        }
        if (own)
        {
            (shown.Yaw, shown.Pitch, shown.Limit) = (_mount!.Yaw, _mount.Pitch, _mount.Limit);
        }
        return shown;
    }

    // A predicted shot on a copy of a station: its barrel empty and recoiling from its tick, its loader started then (a
    // mount's once its last barrel is empty), as the server's Fire does.
    private static void Overlay(StationState st, int barrel, long tick)
    {
        int li = st.Def.LoaderOf(barrel);
        (int first, int count) = st.Def.LoaderBarrels(li);
        LoaderState completed = st.Loaders[li];
        if (completed.Active && !completed.Reverse && !completed.Waiting && completed.Kind != 0 &&
            completed.Elapsed(tick) >= st.Def.Battery.Loader.Seconds)
        {
            // A carried cassette may have finished since the last station record. Materialize all its chambers
            // before reserving one, so its remaining rounds do not disappear behind a restarted shared loader.
            for (int b = 0; b < count && b < completed.Count; b++) st.Chambers[first + b] = completed.Kind;
            st.Loaders[li] = default;
        }
        st.Chambers[barrel] = 0;
        st.ShotTicks[barrel] = tick;
        st.NextBarrel = (barrel + 1) % st.Def.Barrels.Count;
        bool empty = true;
        for (int b = first; b < first + count; b++)
        {
            empty &= st.Chambers[b] == 0;
        }
        if (empty && (!st.Loaders[li].Active || st.Loaders[li].Since < tick))
        {
            st.Loaders[li] = new LoaderState { Active = true, Since = tick, At = 0 };
        }
    }

    private static void Copy(StationState from, StationState to)
    {
        (to.Yaw, to.Pitch, to.Limit, to.Chosen, to.Shots) = (from.Yaw, from.Pitch, from.Limit, from.Chosen, from.Shots);
        from.Chambers.CopyTo(to.Chambers, 0);
        from.ShotTicks.CopyTo(to.ShotTicks, 0);
        from.Loaders.CopyTo(to.Loaders, 0);
        to.Magazine.CopyFrom(from.Magazine);
        (to.Refused, to.RefusedDetail, to.RefusedTick) = (from.Refused, from.RefusedDetail, from.RefusedTick);
        to.NextBarrel = from.NextBarrel;
    }

    /// <summary>A ship's parts with the player's own mount as predicted put in (the seat, the camera and the view turn with it).</summary>
    public void OverlayMount(uint ship, Span<float> parts)
    {
        if (_mount != null && _mountShip == ship)
        {
            StationRules.Into(_mount, parts);
        }
    }

    // Each tick: the gunner's controls into the input (the asked direction is the view, in the zone's frame), the mount
    // predicted from it, and a press the rules say fires, fired.
    protected internal override void Command(uint tick, ref InputCommand command, in InputFrame frame)
    {
        if (Own is not { } own || own.Ship.Def?.Interior is not { } interior)
        {
            _mount = null;
            _mountStation = -1;
            _choice = 0;
            _fireWas = false;
            return;
        }
        RemoteShip ship = own.Ship;
        ShipStationDef def = own.Station;
        var input = new StationInput { Fire = frame.Station.Fire, FirePressed = frame.Station.FirePressed, Zoom = frame.Station.Zoom, Choice = _choice, Cycle = _cycle };
        command.Station = new[] { StationConvert.ToControls(input) };
        Vector3 view = NetConvert.View(command.ViewU, command.ViewV);
        if (command.Frame != 0 && Ships.DrawnPose(command.Frame) is { } seen)
        {
            view = Vector3.Normalize(seen.DirectionToWorld(view));
        }
        input.View = view;
        Quaternion rotation = ship.TryPoseAt(tick, out ShipPose pose) ? pose.Rotation : ship.Rotation;
        if (_mount == null || _mountShip != ship.NetId || _mountStation != def.Index)
        {
            // Sat down: the mount from where the server has it.
            _mount = new StationState(def);
            (_mountShip, _mountStation) = (ship.NetId, def.Index);
            PartsOf(ship, interior);
            _mount.Yaw = _scratch[def.YawPart];
            _mount.Pitch = _scratch[def.PitchPart];
            _fireWas = false;
        }
        _mount.Input = input;
        _viewDelay = command.ViewDelay;
        PartsOf(ship, interior);
        StationRules.Into(_mount, _scratch);
        ResolveAim(ship, def, interior, _mount, _scratch);
        StationRules.Slew(interior, _mount, rotation);
        Correct(ship, interior, def);
        StationRules.Into(_mount, _scratch);
        ResolveAim(ship, def, interior, _mount, _scratch);
        PredictTrigger(tick, ship, def, interior, input, rotation);
    }

    private void ResolveAim(RemoteShip ship, ShipStationDef def, ShipInterior interior, StationState mount, ReadOnlySpan<float> parts)
    {
        if (mount.Input.View.LengthSquared() < 0.25f)
        {
            mount.Aim = default;
            return;
        }
        ShipPose pose = Ships.DrawnPose(ship.NetId) ?? new ShipPose(ship.Position, ship.Rotation, ship.Velocity, Vector3.Zero, ship.Def!.CentreOfMass);
        Vector3 eye = pose.ToWorld(ShipSeats.EyeInFrame(ship.Def!, def.Seat, parts));
        AimHit hit = Client.Targets().PredictProjectileAim(eye, mount.Input.View, StationRules.AimDistance, ship.Id.Value);
        mount.Aim = StationRules.AimAt(interior, def, parts, pose, eye, mount.Input.View, hit.Hit ? hit.Distance : StationRules.AimDistance);
    }

    /// <summary>The own station's physical muzzle and converged shot line, for the sight's ballistic marks.</summary>
    public (Vector3 Origin, Vector3 Direction)? OwnLaunchLine(int barrel = 0)
    {
        if (Own is not { } own || own.Ship.Def is not { Interior: { } interior } def || _mount == null ||
            barrel < 0 || barrel >= own.Station.Barrels.Count) return null;
        return Muzzle(own.Ship, def, interior, own.Station.Index, barrel, _mount);
    }

    // The server's mount, when it is far from the prediction (a limit the client didn't know of, a seat taken over): eased
    // toward it, never snapped (the cross is the input's; IRON_WAKE.md §6.9).
    private void Correct(RemoteShip ship, ShipInterior interior, ShipStationDef def)
    {
        PartsOf(ship, interior);
        float yaw = _scratch[def.YawPart], pitch = _scratch[def.PitchPart];
        const float Far = 5 * MathF.PI / 180;
        if (MathF.Abs(yaw - _mount!.Yaw) > Far)
        {
            _mount.Yaw += (yaw - _mount.Yaw) * 0.2f;
        }
        if (MathF.Abs(pitch - _mount.Pitch) > Far)
        {
            _mount.Pitch += (pitch - _mount.Pitch) * 0.2f;
        }
    }

    private void PartsOf(RemoteShip ship, ShipInterior interior)
    {
        if (_scratch.Length != interior.Parts.Count)
        {
            _scratch = new float[interior.Parts.Count];
        }
        ship.PartsInto(interior, _scratch, ship.NewestTick);
    }

    private void PredictTrigger(uint tick, RemoteShip ship, ShipStationDef def, ShipInterior interior, in StationInput input, Quaternion rotation)
    {
        BatteryDef battery = def.Battery;
        bool held = input.Fire;
        bool press = input.FirePressed || held && !_fireWas;
        _fireWas = held;
        bool wants = battery.Fire == BatteryFire.Alternate ? press : (held || input.FirePressed) && tick >= _nextRipple;
        if (press && tick < _nextRipple)
        {
            Refused = OutcomeCode.Cooldown;
            RefusedAt = ClockSeconds;
            return;
        }
        if (!wants || Display(ship, def.Index) is not { } st)
        {
            return;
        }
        // Attempts and successful shots are different sequences. Only a locally valid press submits an attempt;
        // an empty press must not become an unexpected later shot if an older prediction is refused.
        if (_nextAttack == 0 || _pending.Count >= AttackLedger.PendingLimit)
        {
            return; // Bound accepted reservations too: their resource state may follow the unordered outcome later.
        }
        BatteryLoader loader = battery.Loader;
        Span<bool> loaded = stackalloc bool[def.Barrels.Count];
        Span<ushort> kinds = stackalloc ushort[def.Barrels.Count];
        bool carried = false;
        float progress = 0;
        for (int b = 0; b < loaded.Length; b++)
        {
            int li = def.LoaderOf(b);
            LoaderState l = st.Loaders[li];
            (int first, _) = def.LoaderBarrels(li);
            if (!l.Active && st.Chambers[b] != 0)
            {
                (loaded[b], kinds[b]) = (true, st.Chambers[b]);
            }
            else if (l.Active && !l.Reverse && !l.Waiting && l.Kind != 0 && l.Elapsed((long)tick) >= loader.Seconds && b - first < l.Count)
            {
                // Its loader finishes by this tick on the server too: the same timeline.
                (loaded[b], kinds[b]) = (true, l.Kind);
            }
        }
        foreach (LoaderState l in st.Loaders)
        {
            carried |= l.Active && !l.Reverse && !l.Waiting;
            progress = MathF.Max(progress, l.Active ? StationRules.Progress(loader, l, tick) : 0);
        }
        bool hasRounds = carried || st.Count(st.ChosenItem.Id) > 0;
        if (_scratch.Length != interior.Parts.Count)
        {
            _scratch = new float[interior.Parts.Count];
        }
        ship.PartsInto(interior, _scratch, tick);
        StationRules.Into(_mount!, _scratch);
        float misalignment = StationRules.Misalignment(interior, _mount!, _scratch, rotation);
        st.Limit = _mount!.Limit;
        st.Aim = _mount.Aim;
        int partIndex = def.Part;
        bool disabled = partIndex < ship.PartHealth.Length && ship.Def!.Parts[partIndex].Health != null && ship.PartHealth[partIndex] <= 0;
        Outcome why = StationRules.Check(st, disabled, loaded, progress, hasRounds, misalignment, out int barrel);
        if (why.Ok && HullBlocked(ship, interior, def, barrel))
        {
            why = OutcomeCode.HullBlocked;
        }
        if (!why.Ok)
        {
            Refused = why;
            RefusedAt = ClockSeconds;
            return;
        }
        AttackId attack = Client.Attacks.Id(AttackDomain.Station, _nextAttack);
        if (!Client.Attacks.Predict(attack, tick))
        {
            return;
        }
        _nextAttack++;
        _nextRipple = tick + battery.IntervalTicks;
        var pending = new Pending { Attack = attack, Ship = ship.NetId, Station = def.Index, Tick = tick };
        _pending.Add(pending);
        (ushort viewU, ushort viewV) = Quantize.Octahedral(input.View);
        SendUnordered(new AttackIntent
        {
            Kind = attack.Kind, Seq = attack.Sequence, Session = attack.Session, Tick = tick, Ship = ship.NetId, Station = (byte)def.Index,
            ViewU = viewU, ViewV = viewV, Frame = 0, ViewDelay = _viewDelay,
        });
        Refused = Outcome.Done;
        ushort kind = kinds[barrel];
        (Vector3 origin, Vector3 direction) = Muzzle(ship, ship.Def!, interior, def.Index, barrel, _scratch);
        Vector3 carriedVelocity = Ships.DrawnPose(ship.NetId) is { } drawn ? drawn.VelocityAt(origin) : ship.Velocity;
        ClientProjectile? rocket = battery.Launch.Of(kind) is { } profile
            ? Survival.AddOwnRocket(battery, kind, origin, direction * profile.Muzzle + carriedVelocity, attack.Sequence, attack) : null;
        pending.Barrel = barrel;
        pending.Kind = kind;
        pending.Rocket = rocket;
        pending.Predicted = true;
        ShotsPredicted++;
        if (Client.Attacks.TryPresent(attack))
        {
            Shot?.Invoke(new StationShotView(ship.NetId, def.Index, barrel, attack.Sequence, kind, origin, direction, true, true) { Attack = attack });
        }
    }

    // The hull's line through the client's copy of the ship's space (a ship near the player keeps one, IRON_WAKE.md §4.3);
    // without one, nothing blocks.
    private bool HullBlocked(RemoteShip ship, ShipInterior interior, ShipStationDef def, int barrel)
    {
        if (Ships.FrameOf(ship.NetId) is not { } frame)
        {
            return false;
        }
        (Vector3 muzzle, Vector3 breech) = StationRules.BarrelLine(interior, def, barrel, _scratch);
        Vector3 line = muzzle - breech;
        float length = line.Length();
        if (length < 1e-3f)
        {
            return false;
        }
        int yaw = def.YawPart;
        lock (JoltRuntime.Gate)
        {
            if (frame.Space.RayPast(breech, line / length, length + StationRules.HullLinePast, part => Carried(interior, yaw, part), out _)) return true;
            ShipPose pose = Ships.DrawnPose(ship.NetId) ?? new ShipPose(ship.Position, ship.Rotation, ship.Velocity, Vector3.Zero, ship.Def!.CentreOfMass);
            Vector3 launch = StationRules.LaunchDirection(_mount?.Aim ?? default, pose.ToWorld(muzzle), pose.DirectionToWorld(line / length));
            return frame.Space.RayPast(muzzle, pose.DirectionToLocal(launch), StationRules.HullLinePast, part => Carried(interior, yaw, part), out _);
        }
    }

    private static bool Carried(ShipInterior interior, int carrier, int part)
    {
        for (int p = part; p > 0 && p < interior.Parts.Count; p = interior.Parts[p].Parent)
        {
            if (p == carrier)
            {
                return true;
            }
        }
        return false;
    }

    // A barrel's muzzle and its way in the world, on the ship as drawn, at a station's mount (a record's, or the states given).
    private (Vector3 Origin, Vector3 Direction) Muzzle(RemoteShip ship, ShipDef def, ShipInterior interior, int station, int barrel, StationState? st)
    {
        float[] parts = new float[interior.Parts.Count];
        ship.PartsInto(interior, parts, (long)Client.RenderTick);
        if (st != null)
        {
            StationRules.Into(st, parts);
        }
        OverlayMount(ship.NetId, parts);
        return Muzzle(ship, def, interior, station, barrel, parts);
    }

    private (Vector3 Origin, Vector3 Direction) Muzzle(RemoteShip ship, ShipDef def, ShipInterior interior, int station, int barrel, ReadOnlySpan<float> parts)
    {
        ShipStationDef s = def.Stations[station];
        (Vector3 muzzle, _) = StationRules.BarrelLine(interior, s, barrel, parts);
        Vector3 direction = StationRules.BarrelDirection(interior, s, parts);
        ShipPose pose = Ships.DrawnPose(ship.NetId) ?? new ShipPose(ship.Position, ship.Rotation, ship.Velocity, Vector3.Zero, def.CentreOfMass);
        Vector3 origin = pose.ToWorld(muzzle), worldDirection = Vector3.Normalize(pose.DirectionToWorld(direction));
        if (_mount != null && _mountShip == ship.NetId && _mountStation == station)
            worldDirection = StationRules.LaunchDirection(_mount.Aim, origin, worldDirection);
        return (origin, worldDirection);
    }

    /// <summary>Attempts waiting for a decision, or accepted reservations waiting for their state (tests).</summary>
    public int PendingShots => _pending.Count;

    /// <summary>
    /// The tick a station is drawn at now (fractional): the player's own on the predicted timeline (its shots play when
    /// fired), others' as everything else of the ship is drawn, 100 ms behind.
    /// </summary>
    public double DrawTick(uint ship, int station) =>
        _mount != null && _mountShip == ship && _mountStation == station ? TickClock : Client.RenderTick;

    /// <summary>The player's own mount's turning rates (radians a second; the traverse's sound), or zero.</summary>
    public (float Yaw, float Pitch) OwnRates => _mount != null ? (_mount.YawRate, _mount.Pitch == 0 ? 0 : _mount.PitchRate) : (0, 0);
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class StationModuleAccess
{
    public static StationModule Stations(this GameClient client) => client.Require<StationModule>();
}
