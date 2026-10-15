using System;
using Godot;
using Hollow.Client;
using Hollow.Client.Audio;
using Hollow.Net.Protocol;
using Hollow.Simulation.Ships;
using HollowUniverse.Audio;
using HollowUniverse.Fx;
using HollowUniverse.Ground;
using Num = System.Numerics;

namespace HollowUniverse.Ships;

/// <summary>
/// What one ship sounds like (GROUND.md §6.7, IRON_WAKE.md §10), driven by its replicated state and never by its speed
/// alone (SHIP_FEEL.md): the engines' start, idle, load by thrust, release and shutdown; the lift engines by lift thrust;
/// manoeuvring puffs; the boost's strain and release; each door's start, travel, stop at open and lock; the hull's strain
/// on hard turns, braking and hits, heard aboard; a touchdown at the legs; the interior's beds for a listener aboard; the
/// hull alarm under a quarter. Every sound is a role of the ship's kind in sounds.json's <c>items</c> (the Tin Finch's
/// hum and burn, the Iron Wake's turbines): a role the kind doesn't list is silent, so one rule drives every ship. With
/// no runtime pitch, a role's slow and fast loops (<c>idle</c> and <c>idleFast</c>...) are crossfaded at equal power.
/// </summary>
public sealed class ShipSounds
{
    private const float Silent = -80f;

    private readonly ShipDef _def;
    private readonly Node3D _drive;
    private readonly Node3D _lift;
    private readonly Node3D[] _doors;

    private SoundHandle _engine, _idle, _idleFast, _load, _loadFast, _liftSlow, _liftFast, _boostSlow, _boostFast;
    private SoundHandle _ventilation, _machinery;
    private SoundHandle[] _travel;
    private float[] _doorWas;
    private bool[] _opening;

    private bool _known, _running, _boosting;
    private ShipNetFlags _flags;
    private float _health = 1;
    private double _time, _idleFadeAt = double.NegativeInfinity, _boostSince, _nextAlarm, _nextStrain, _nextPuff, _nextRelease;
    private float _power, _peak, _liftShare, _air, _lateral, _down;
    private Num.Vector3 _velocity, _accel;
    private Quaternion _rotation;
    private float _spin, _spinAccel;

    public ShipSounds(ShipDef def, ShipView view)
    {
        _def = def;
        // The engines heard at the model's engine socket, else between the main nozzles; the lift engines between theirs.
        _drive = GunEffects.Socket(view.Model, "engine_audio") ?? Point(view, "DriveSound", Centre(def, NozzleDrive.Main) ?? def.CentreOfMass);
        _lift = Point(view, "LiftSound", Centre(def, NozzleDrive.Lift) ?? def.CentreOfMass);
        _doors = new Node3D[def.Doors.Count];
        for (int i = 0; i < _doors.Length; i++)
        {
            _doors[i] = Point(view, $"Door{i}Sound", def.PartPoint(def.PartOfDoor(i)));
        }
        _travel = new SoundHandle[def.Doors.Count];
        _doorWas = new float[def.Doors.Count];
        _opening = new bool[def.Doors.Count];
    }

    private static Node3D Point(ShipView view, string name, Num.Vector3 at)
    {
        var node = new Node3D { Name = name, Position = new Vector3(at.X, at.Y, at.Z) };
        view.AddChild(node);
        return node;
    }

    private static Num.Vector3? Centre(ShipDef def, NozzleDrive drive)
    {
        Num.Vector3 sum = default;
        int n = 0;
        foreach (ShipNozzle nozzle in def.Nozzles)
        {
            if (nozzle.Drive == drive)
            {
                sum += nozzle.At;
                n++;
            }
        }
        return n > 0 ? sum / n : null;
    }

    private string? Role(string role) => Sounds.Item(_def.Key, role);

    private static AudioDirector? Director => AudioDirector.Instance;

    private SoundHandle Loop(string role, Node3D at, float startDb = 0f) =>
        Role(role) is { } key && Director is { } d ? d.Loop(key, at, 0f, startDb) : SoundHandle.None;

    private static void Gain(SoundHandle h, float db, float fade = 0.15f)
    {
        if (h.IsValid)
        {
            Director?.SetGain(h, Math.Max(db, Silent), fade);
        }
    }

    private static void Stop(ref SoundHandle h, float fade)
    {
        if (h.IsValid)
        {
            Sounds.Stop(h, fade);
            h = SoundHandle.None;
        }
    }

    private static float Db(float linear) => linear <= 1e-4f ? Silent : 20f * MathF.Log10(linear);

    // An equal-power crossfade at w (0 the slow loop alone, 1 the fast): each side's gain in dB.
    private static (float Slow, float Fast) Cross(float w)
    {
        w = Math.Clamp(w, 0f, 1f);
        return (Db(MathF.Cos(w * MathF.PI * 0.5f)), Db(MathF.Sin(w * MathF.PI * 0.5f)));
    }

    /// <summary>
    /// This frame: the ship as drawn (<paramref name="look"/>), its replicated details, and the listener's space when the
    /// listener is aboard this ship (null otherwise).
    /// </summary>
    public void Update(in ShipLook look, RemoteShip ship, Num.Vector3 velocity, double tick, bool visible, SoundSpace? aboard, double delta)
    {
        float dt = (float)Math.Max(delta, 1e-4);
        _time += delta;
        Vector3 centre = look.Pivot * new Vector3(_def.CentreOfMass.X, _def.CentreOfMass.Y, _def.CentreOfMass.Z);
        Vector3 up = look.Pivot.Basis.Y.Normalized();
        Motion(look, velocity, dt);

        bool running = look.Engines && !look.Destroyed && visible;
        if (running && !_running)
        {
            StartEngines();
        }
        else if (!running && _running)
        {
            StopEngines(look.Destroyed || !visible);
        }
        _running = running;
        if (running)
        {
            Engines(look, ship, dt);
        }
        Boost(running && look.Boosting);
        Doors(ship, tick);
        if (_known)
        {
            Events(ship, look, centre, up, aboard);
        }
        Beds(aboard, look.Engines && !look.Destroyed);
        Alarm(ship.Health, look.Destroyed, centre, aboard);
        _known = true;
        _flags = ship.Flags;
        _health = ship.Health;
    }

    // The ship's acceleration and turn rate from what was replicated (its velocity, its drawn rotation), smoothed.
    private void Motion(in ShipLook look, Num.Vector3 velocity, float dt)
    {
        Quaternion q = look.Pivot.Basis.GetRotationQuaternion();
        if (_known)
        {
            float k = 1 - MathF.Exp(-dt / 0.25f);
            Num.Vector3 a = (velocity - _velocity) / dt;
            _accel += (a - _accel) * k;
            float angle = (_rotation.Inverse() * q).Normalized().GetAngle();
            float spin = Math.Min(angle, MathF.PI) / dt;
            float spinAccel = MathF.Abs(spin - _spin) / dt;
            _spin += (spin - _spin) * k;
            _spinAccel += (spinAccel - _spinAccel) * k;
        }
        _velocity = velocity;
        _rotation = q;
    }

    // ---------------------------------------------------------------- the engines

    private void StartEngines()
    {
        // A start (when one is heard: not a ship first seen running): the run-up's sound, the idle fading in over its last
        // 1.5 s (their levels meet within 1 dB, §10.1).
        float idleStart = 0f;
        if (_known && Role("start") is { } start)
        {
            Sounds.At(start, _drive.GlobalPosition);
            float length = Length(start);
            _idleFadeAt = _time + Math.Max(0, length - 1.5f);
            idleStart = Silent;
        }
        else
        {
            _idleFadeAt = double.NegativeInfinity;
        }
        _engine = Loop("engine", _drive);
        _idle = Loop("idle", _drive, idleStart);
        _idleFast = Loop("idleFast", _drive, Silent);
        _load = Loop("load", _drive, Silent);
        _loadFast = Loop("loadFast", _drive, Silent);
        _liftSlow = Loop("lift", _lift, Silent);
        _liftFast = Loop("liftFast", _lift, Silent);
        _power = _peak = _liftShare = 0;
    }

    private void StopEngines(bool quiet)
    {
        // A shutdown fades the loops over its first half second (−3 dB matches its start to the idle, §10.1).
        string? key = quiet ? null : Role("shutdown");
        bool shutdown = key != null;
        if (shutdown)
        {
            Sounds.At(key, _drive.GlobalPosition);
        }
        float fade = shutdown ? 0.5f : 0.6f;
        Stop(ref _engine, fade);
        Stop(ref _idle, fade);
        Stop(ref _idleFast, fade);
        Stop(ref _load, shutdown ? 0.5f : 0.4f);
        Stop(ref _loadFast, shutdown ? 0.5f : 0.4f);
        Stop(ref _liftSlow, 0.8f);
        Stop(ref _liftFast, 0.8f);
    }

    private static float Length(string key)
    {
        SoundEventData? e = SoundLibrary.Find(key);
        return e is { Files.Count: > 0 } && SoundLibrary.Stream(e.Files[0]) is { } s ? (float)s.GetLength() : 0f;
    }

    private void Engines(in ShipLook look, RemoteShip ship, float dt)
    {
        Num.Vector3 t = look.Thrust;
        bool liftEngines = _liftSlow.IsValid || _liftFast.IsValid;
        // The load: what the drive gives (with lift engines of its own, the main and side drives; else every axis).
        float want = liftEngines
            ? MathF.Max(MathF.Abs(t.Z), MathF.Max(MathF.Abs(t.X) * 0.6f, MathF.Max(0, -t.Y) * 0.6f))
            : MathF.Max(MathF.Abs(t.Z), MathF.Max(MathF.Abs(t.Y) * 0.8f, MathF.Abs(t.X) * 0.6f));
        want = Math.Clamp(want, 0, 1.8f);
        float k = 1 - MathF.Exp(-dt / 0.12f);
        _power += (want - _power) * k;
        _liftShare += (MathF.Max(0, t.Y) - _liftShare) * k;
        float p = _power;

        // The release: the load letting go quickly (the loops carry on under it).
        _peak = MathF.Max(p, _peak - dt * 0.25f);
        if (_peak - p > 0.4f && _time >= _nextRelease && Role("release") is { } release)
        {
            Sounds.At(release, _drive.GlobalPosition);
            _nextRelease = _time + 4;
            _peak = p;
        }

        // The hum that swells with the power (a small ship's engine).
        Gain(_engine, -8 + 8 * MathF.Min(p, 1));

        // Idle: ground idle on its legs, flight idle in the air (by spool and airborne), a little under the load.
        float airborne = (ship.Flags & ShipNetFlags.Landed) != 0 ? 0f : 1f;
        _air += (airborne - _air) * (1 - MathF.Exp(-dt / 1.5f));
        // After a start the idle fades in over 1.5 s from its time, then follows.
        bool fadingIn = _time >= _idleFadeAt && _time - dt < _idleFadeAt;
        if (_time >= _idleFadeAt + (fadingIn ? 0 : 1.5) || fadingIn || double.IsNegativeInfinity(_idleFadeAt))
        {
            float idle = -1.5f * MathF.Min(p, 1);
            (float slow, float fast) = _idleFast.IsValid ? Cross(_air) : (0f, Silent);
            float fade = fadingIn ? 1.5f : 0.15f;
            Gain(_idle, idle + slow, fade);
            Gain(_idleFast, idle + fast, fade);
        }

        // Load: by thrust over the idle; slow to fast with the thrust.
        float load = p < 0.04f ? -40f : Db(p) - 2 + (look.Boosting ? 3 : 0);
        (float ls, float lf) = _loadFast.IsValid ? Cross((p - 0.35f) / 0.55f) : (0f, Silent);
        Gain(_load, load + ls);
        Gain(_loadFast, load + lf);

        // The lift engines by lift thrust (a hover is about half of it).
        float q = _liftShare;
        float lift = q < 0.03f ? -40f : MathF.Min(3f, Db(MathF.Min(q, 1.5f) / 0.7f));
        (float qs, float qf) = _liftFast.IsValid ? Cross((q - 0.3f) / 0.6f) : (0f, Silent);
        Gain(_liftSlow, lift + qs, 0.2f);
        Gain(_liftFast, lift + qf, 0.2f);
    }

    private void Boost(bool boosting)
    {
        if (boosting && !_boosting)
        {
            if (_known)
            {
                Sounds.At(Role("boost"), _drive.GlobalPosition);
            }
            _boostSince = _time;
            _boostSlow = Loop("boostLoop", _drive);
            _boostFast = Loop("boostLoopFast", _drive, Silent);
        }
        else if (!boosting && _boosting)
        {
            Stop(ref _boostSlow, 0.4f);
            Stop(ref _boostFast, 0.4f);
            if (_running)
            {
                Sounds.At(Role("boostRelease"), _drive.GlobalPosition);
            }
        }
        _boosting = boosting;
        if (boosting && _boostFast.IsValid)
        {
            // Slow as it begins, fast toward its end (the strain building over the boost's seconds).
            (float slow, float fast) = Cross((float)(_time - _boostSince) / MathF.Max(1, _def.BoostSeconds));
            Gain(_boostSlow, slow, 0.2f);
            Gain(_boostFast, fast, 0.2f);
        }
    }

    // ---------------------------------------------------------------- doors

    private void Doors(RemoteShip ship, double tick)
    {
        for (int i = 0; i < _doors.Length && i < ship.Doors.Length; i++)
        {
            string door = _def.Doors[i].Key;
            float p = ship.DoorAt(i, tick);
            bool opening = ship.Doors[i].Opening;
            Vector3 at = _doors[i].GlobalPosition;
            if (_known)
            {
                // Its start each way (its own role, else the ship's hatch's), its stop open and its lock shut.
                if (opening != _opening[i])
                {
                    string way = opening ? "Open" : "Close";
                    Sounds.At(Role(door + way) ?? Role("hatch" + way), at);
                }
                if (p >= 0.999f && _doorWas[i] < 0.999f)
                {
                    Sounds.At(Role(door + "Opened"), at);
                }
                else if (p <= 0.001f && _doorWas[i] > 0.001f)
                {
                    Sounds.At(Role(door + "Shut"), at);
                }
            }
            // The actuators' loop while it travels.
            bool travelling = p > 0.001f && p < 0.999f && MathF.Abs(p - _doorWas[i]) > 1e-6f;
            if (travelling && !_travel[i].IsValid && Role(door + "Travel") != null)
            {
                _travel[i] = Loop(door + "Travel", _doors[i]);
            }
            else if (!travelling && _travel[i].IsValid)
            {
                Stop(ref _travel[i], 0.25f);
            }
            _opening[i] = opening;
            _doorWas[i] = p;
        }
    }

    // ---------------------------------------------------------------- what happened since the last frame

    private void Events(RemoteShip ship, in ShipLook look, Vector3 centre, Vector3 up, SoundSpace? aboard)
    {
        ShipNetFlags was = _flags, now = ship.Flags;
        bool Rose(ShipNetFlags f) => (now & f) != 0 && (was & f) == 0;
        float hit = _health - ship.Health;
        if (Rose(ShipNetFlags.Landed) && _velocity.Length() > 0.6f)
        {
            // At the legs: the pivot is on the ground between them.
            Sounds.At(Role("touchdown"), look.Pivot.Origin);
        }
        // A crash: the hull hurt as the ship stopped short (gunfire hurts it without the jolt).
        if (hit > 0.002f && !ship.Has(ShipNetFlags.Destroyed) && (ship.Velocity - _velocity).Length() > 3f)
        {
            Sounds.At(Role("crash"), centre);
            FxDirector.Play("impact.bullet.metal", centre, up, new FxParams { Scale = 4 });
        }
        if (Rose(ShipNetFlags.Destroyed))
        {
            Sounds.At(Role("destroyed") ?? "explosion", centre);
            Sounds.At("explosion.debris", centre, -2);
        }

        // Manoeuvring puffs: a jet firing as a sideways or downward push or a turn begins.
        float lateral = MathF.Abs(look.Thrust.X), down = MathF.Max(0, -look.Thrust.Y);
        bool turnKick = _spinAccel > MathF.Max(_def.Yaw.Acceleration, 1e-3f) * 0.6f;
        if (_running && _time >= _nextPuff && Role("rcs") is { } puff && (lateral > 0.2f && _lateral <= 0.2f || down > 0.2f && _down <= 0.2f || turnKick))
        {
            float side = look.Thrust.X >= 0 ? 1 : -1;
            var local = new Vector3(_def.BoundsCentre.X + side * _def.BoundsHalf.X * 0.9f, _def.BoundsCentre.Y,
                _def.BoundsCentre.Z + (turnKick ? _def.BoundsHalf.Z * 0.7f : 0));
            Sounds.At(puff, look.Pivot * local);
            _nextPuff = _time + 0.25;
        }
        _lateral = lateral;
        _down = down;

        // The hull's strain, structure-borne (heard aboard, at the space's level): hard turns and braking against what
        // the ship's own drive can do, and hits by their size.
        if (aboard is { StructureDb: > -60f } && _time >= _nextStrain && !look.Destroyed)
        {
            float drive = MathF.Max(_def.MainThrust / MathF.Max(_def.Mass, 1), 0.5f);
            float strain = MathF.Max(_accel.Length() / drive, _spin / MathF.Max(_def.Yaw.MaxRate, 1e-3f) * 0.8f);
            string? role = hit > 0.02f || strain > 1.2f ? "strainHeavy" : hit > 0.004f || strain > 0.85f ? "strainMedium"
                : hit > 0 || strain > 0.55f ? "strainLight" : null;
            if (role != null && Role(role) is { } key)
            {
                Sounds.At(key, centre, aboard.StructureDb);
                _nextStrain = _time + (role == "strainHeavy" ? 3.5 : role == "strainMedium" ? 2.8 : 2.2) + Random.Shared.NextDouble();
            }
        }
    }

    // The interior's beds, flat, for a listener aboard: at the space's structure-borne level (the open weather decks none),
    // the machinery quieter with the engines off.
    private void Beds(SoundSpace? aboard, bool engines)
    {
        if (aboard == null)
        {
            Stop(ref _ventilation, 1f);
            Stop(ref _machinery, 1f);
            return;
        }
        if (!_ventilation.IsValid && Role("ventilation") is { } vent)
        {
            _ventilation = Director?.Loop2D(vent, 0, Silent) ?? SoundHandle.None;
        }
        if (!_machinery.IsValid && Role("machinery") is { } machinery)
        {
            _machinery = Director?.Loop2D(machinery, 0, Silent) ?? SoundHandle.None;
        }
        Gain(_ventilation, aboard.StructureDb, 0.8f);
        Gain(_machinery, aboard.StructureDb - (engines ? 0 : 4), 0.8f);
    }

    // The hull alarm while the hull is under a quarter: sparse, a phrase every 20 to 30 s; flat for the crew aboard.
    private void Alarm(float health, bool destroyed, Vector3 centre, SoundSpace? aboard)
    {
        if (destroyed || health >= 0.25f || Role("alarmHull") is not { } alarm)
        {
            _nextAlarm = 0;
            return;
        }
        if (_time < _nextAlarm)
        {
            return;
        }
        if (aboard != null)
        {
            Sounds.Flat(alarm);
        }
        else
        {
            Sounds.At(alarm, centre);
        }
        _nextAlarm = _time + 20 + 10 * Random.Shared.NextDouble();
    }

    /// <summary>Everything this ship plays, stopped (the ship gone).</summary>
    public void StopAll()
    {
        StopEngines(true);
        Stop(ref _boostSlow, 0.3f);
        Stop(ref _boostFast, 0.3f);
        Stop(ref _ventilation, 0.5f);
        Stop(ref _machinery, 0.5f);
        for (int i = 0; i < _travel.Length; i++)
        {
            Stop(ref _travel[i], 0.2f);
        }
    }
}
