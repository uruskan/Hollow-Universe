using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;

namespace Hollow.Client;

/// <summary>A battery's shot as the views play it (IW3): the ship, its station and barrel, the round, the muzzle and the way it left (world), whose.</summary>
public readonly record struct StationShotView(uint Ship, int Station, int Barrel, uint Number, ushort Kind, Vector3 Origin, Vector3 Direction, bool Own, bool Predicted);

// The crewed batteries in the client (IW3, IRON_WAKE.md §6.8, §6.9): each ship's stations as the server last said
// (ShipStations: their loaders' records, chambers, shots, kinds, magazines' counts, refusals), the shots others fire
// (TurretShot), and the player's own station when they sit at one: its controls into each tick's input, its mount
// predicted from the player's own aim by the same rule as the server's, and its shots predicted on the same loader
// timeline (a press the rules say fires plays at once with its rocket, tagged with its number; the server's shot of that
// number confirms it and plays nothing again; one that doesn't come takes it back).
public sealed class StationModule : ClientModule
{
    /// <summary>A predicted shot the server hasn't confirmed this many ticks after its own tick was taken back.</summary>
    public const int UndoTicks = 15;

    private sealed class Pending
    {
        public uint Ship;
        public int Station;
        public uint Number;
        public int Barrel;
        public long Tick;
        public ushort Kind;
        public ClientProjectile? Rocket;
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
    private uint _ackTick;
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

    protected internal override void Init(MessageHandlers<GameClient> handlers) =>
        handlers.On<ShipStations>((_, in m) => OnStations(m)).On<TurretShot>((_, in m) => OnShot(m));

    public override void Dispose()
    {
        _records.Clear();
        _pending.Clear();
        _display.Clear();
        _mount = null;
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
            if (net.Station < states.Length)
            {
                StationConvert.Apply(net, states[net.Station], Content);
            }
        }
    }

    private void OnShot(TurretShot m)
    {
        if (m.Gunner == NetId && _pending.Find(p => p.Ship == m.Ship && p.Station == m.Station && p.Number == m.Number) is { } mine)
        {
            // Our own shot, played when we fired it: confirmed, nothing again (its rocket gets the server's ID by its launch).
            _pending.Remove(mine);
            ShotsConfirmed++;
            return;
        }
        if (m.Gunner == NetId)
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
        (Vector3 origin, Vector3 direction) = Muzzle(ship, def, interior, m.Station, m.Barrel, Display(ship, m.Station));
        Shot?.Invoke(new StationShotView(m.Ship, m.Station, m.Barrel, m.Number, (ushort)Math.Min(m.Kind, ushort.MaxValue), origin, direction, m.Gunner == NetId, false));
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
            if (p.Ship != ship.NetId || p.Station != station || p.Number <= record.Shots)
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
        st.Chambers[barrel] = 0;
        st.ShotTicks[barrel] = tick;
        st.NextBarrel = (barrel + 1) % st.Def.Barrels.Count;
        int li = st.Def.LoaderOf(barrel);
        (int first, int count) = st.Def.LoaderBarrels(li);
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
        long latest = long.MinValue;
        for (int b = 0; b < from.ShotTicks.Length; b++)
        {
            if (from.ShotTicks[b] > latest)
            {
                latest = from.ShotTicks[b];
                to.NextBarrel = (b + 1) % from.ShotTicks.Length;
            }
        }
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
        var input = new StationInput { Fire = frame.Station.Fire, Zoom = frame.Station.Zoom, Choice = _choice, Cycle = _cycle };
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
        StationRules.Slew(interior, _mount, rotation);
        Correct(ship, interior, def);
        PredictTrigger(tick, ship, def, interior, input, rotation);
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
        bool press = held && !_fireWas;
        _fireWas = held;
        bool wants = battery.Fire == BatteryFire.Alternate ? press : held && tick >= _nextRipple;
        if (!wants || Display(ship, def.Index) is not { } st)
        {
            return;
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
        Refused = Outcome.Done;
        ushort kind = kinds[barrel];
        uint number = st.Shots + 1;
        foreach (Pending p in _pending)
        {
            if (p.Ship == ship.NetId && p.Station == def.Index)
            {
                number = Math.Max(number, p.Number + 1);
            }
        }
        (Vector3 origin, Vector3 direction) = Muzzle(ship, ship.Def!, interior, def.Index, barrel, _scratch);
        Vector3 carriedVelocity = Ships.DrawnPose(ship.NetId) is { } drawn ? drawn.VelocityAt(origin) : ship.Velocity;
        ClientProjectile? rocket = battery.Launch.Of(kind) is { } profile
            ? Survival.AddOwnRocket(battery, kind, origin, direction * profile.Muzzle + carriedVelocity, number) : null;
        _pending.Add(new Pending { Ship = ship.NetId, Station = def.Index, Number = number, Barrel = barrel, Tick = tick, Kind = kind, Rocket = rocket });
        _nextRipple = tick + battery.IntervalTicks;
        ShotsPredicted++;
        Shot?.Invoke(new StationShotView(ship.NetId, def.Index, barrel, number, kind, origin, direction, true, true));
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
            return frame.Space.RayPast(breech, line / length, length + StationRules.HullLinePast, part => Carried(interior, yaw, part), out _);
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
        return (pose.ToWorld(muzzle), Vector3.Normalize(pose.DirectionToWorld(direction)));
    }

    // Each snapshot: predicted shots the server has had time to fire and didn't are taken back with their rockets, and the
    // server's reason shows.
    protected internal override void AfterSnapshot(in SnapshotHeader header)
    {
        if (header.OwnTick == 0)
        {
            return;
        }
        _ackTick = header.OwnTick;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Pending p = _pending[i];
            if (p.Tick + UndoTicks > _ackTick)
            {
                continue;
            }
            _pending.RemoveAt(i);
            ShotsUndone++;
            if (p.Rocket != null)
            {
                Survival.RemoveOwnRocket(p.Rocket);
            }
            if (Ships.ShipByNetId(p.Ship) is { } ship && Records(ship) is { } states && p.Station < states.Length && states[p.Station].RefusedTick >= p.Tick)
            {
                Refused = new Outcome(states[p.Station].Refused, states[p.Station].RefusedDetail);
                RefusedAt = ClockSeconds;
            }
        }
    }

    /// <summary>Predicted shots waiting for the server's word (tests).</summary>
    public int PendingShots => _pending.Count;
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class StationModuleAccess
{
    public static StationModule Stations(this GameClient client) => client.Require<StationModule>();
}
