using System;
using Godot;
using Hollow.Client.Audio;
using Hollow.Client.Vehicles;
using Hollow.Simulation.Ships;
using HollowUniverse.Audio;
using HollowUniverse.Ground;
using Num = System.Numerics;

namespace HollowUniverse.Ships;

/// <summary>
/// What a ground vehicle sounds like as it drives (VEHICLES.md §7.1), from its revs (<see cref="VehicleRevs"/>) and what
/// every client sees of it: its engine as loops made at several revs, on the throttle and off it, each pitched to the revs
/// and crossfaded with its neighbours (two speeds and the load at a time); the turbo's whistle by its boost and its
/// release, louder and higher overboosted (VEHICLES.md §9.10), with a labouring layer over the loops; a clunk at each
/// gear change; a heavy vehicle's air brakes hissing as it comes to rest and its Jake brake hard on the brakes; the tyres'
/// loop of the ground under them (by its physical surface, or water),
/// pitched and swelled by speed; a skid when it slides sideways or spins its wheels on loose ground; the suspension's
/// knock at a stop or a hard hit and its creaks as it works; a splash into water. Its start, stop and landing are the
/// kind's roles in <see cref="ShipSounds"/>; their loops wait for the start's run-up. Everything comes from sounds.json's
/// <c>engines</c> entry of the kind.
/// </summary>
public sealed class VehicleSounds
{
    private const float Silent = -80f;
    private static readonly string[] Loose = { "gravel", "dirt", "grass", "sand", "snow" };

    private readonly EngineSoundData _e;
    private readonly ShipDef _def;
    private readonly ShipView _view;
    private readonly Node3D _engine;
    private readonly Node3D _wheels;
    private readonly SoundHandle[] _on, _off;
    private readonly float[] _quietFor;
    private SoundHandle _turbo, _labour, _tyre, _skid;
    private string? _tyreKey, _skidKey;
    private string _surface = "unknown";
    private bool _running, _known, _flying, _driven;
    private float _fall, _overrunWas;
    private double _time, _loudFrom, _nextJake, _nextKnock, _nextCreak, _tyreQuiet, _skidQuiet, _airborneAt;

    public VehicleSounds(ShipDef def, ShipView view, EngineSoundData engine)
    {
        _def = def;
        _view = view;
        _e = engine;
        // The engine heard at its exhaust (a socket of the model), else its engine's; the tyres on the ground between them.
        _engine = GunEffects.Socket(view.Model, "exhaust") ?? GunEffects.Socket(view.Model, "engine_audio")
            ?? Point(view, "EngineSound", def.CentreOfMass);
        Num.Vector3 hubs = default;
        foreach (WheelDef w in def.Wheels)
        {
            hubs += w.Hub;
        }
        hubs = def.Wheels.Count > 0 ? hubs / def.Wheels.Count : def.CentreOfMass;
        _wheels = Point(view, "TyreSound", new Num.Vector3(hubs.X, 0.1f, hubs.Z));
        _on = new SoundHandle[engine.Loops.Count];
        _off = new SoundHandle[engine.Loops.Count];
        _quietFor = new float[engine.Loops.Count];
    }

    private static Node3D Point(ShipView view, string name, Num.Vector3 at)
    {
        var node = new Node3D { Name = name, Position = new Vector3(at.X, at.Y, at.Z) };
        view.AddChild(node);
        return node;
    }

    private static AudioDirector? Director => AudioDirector.Instance;

    private static float Db(float linear) => linear <= 1e-4f ? Silent : 20f * MathF.Log10(linear);

    private static SoundHandle Start(string key, Node3D at) => Director?.Loop(key, at, 0f, Silent) ?? SoundHandle.None;

    private static void Set(SoundHandle h, float db, float pitch, float fade = 0.06f)
    {
        if (h.IsValid && Director is { } d)
        {
            d.SetGain(h, Math.Max(db, Silent), fade);
            d.SetPitch(h, Math.Clamp(pitch, 0.3f, 3f));
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

    /// <summary>
    /// This frame: the vehicle's revs (stepped already), how it is drawn, its velocity and rotation, its wheels on the ground,
    /// the physical surface under it ("water" in water), and whether it is drawn at all.
    /// </summary>
    public void Update(VehicleRevs revs, Num.Vector3 velocity, Num.Quaternion rotation, bool landed, string surface, bool visible,
        double delta)
    {
        float dt = (float)Math.Max(delta, 1e-4);
        _time += delta;
        if (!visible)
        {
            StopAll();
            _known = false;
            return;
        }
        Engine(revs, dt);
        Ground(revs, velocity, rotation, landed, surface, dt);
        Landing(velocity, rotation, landed);
        Suspension();
        _known = true;
    }

    // ---------------------------------------------------------------- the engine

    private void Engine(VehicleRevs revs, float dt)
    {
        if (revs.Running && !_running)
        {
            // Started while heard: its loops come in as the start's own run-up ends; first seen running: at once.
            _loudFrom = _known ? _time + Math.Max(0, StartLength() - 0.8) : _time;
        }
        else if (!revs.Running && _running)
        {
            StopEngine(0.3f);
        }
        _running = revs.Running;
        if (!_running)
        {
            return;
        }
        float rise = Math.Clamp((float)(_time - _loudFrom) / 0.6f, 0, 1);
        if (rise <= 0)
        {
            return;
        }
        // Overboosted it works harder: the whole engine a little louder.
        float level = _e.Volume + Db(rise) + 2.5f * revs.Overboost;
        float rpm = Math.Max(revs.Rpm, 1);
        // On the throttle and off it, at equal power; the overrun's drag louder.
        float a = MathF.Pow(Math.Clamp(revs.Load, 0, 1), 0.75f) * MathF.PI * 0.5f;
        float onDb = Db(MathF.Sin(a)), offDb = Db(MathF.Cos(a)) + 2f * revs.Overrun;
        for (int i = 0; i < _e.Loops.Count; i++)
        {
            EngineLoop loop = _e.Loops[i];
            float w = Weight(i, rpm);
            if (w < 0.01f)
            {
                _quietFor[i] += dt;
                Set(_on[i], Silent, 1, 0.08f);
                Set(_off[i], Silent, 1, 0.08f);
                if (_quietFor[i] > 0.3f)
                {
                    Stop(ref _on[i], 0.05f);
                    Stop(ref _off[i], 0.05f);
                }
                continue;
            }
            _quietFor[i] = 0;
            float pitch = rpm / loop.Rpm * _e.Pitch;
            if (loop.On == loop.Off)
            {
                // One loop both ways (the idle): its level on the throttle.
                if (!_on[i].IsValid)
                {
                    _on[i] = Start(loop.On, _engine);
                }
                Set(_on[i], level + loop.OnDb + Db(w), pitch);
                continue;
            }
            if (!_on[i].IsValid)
            {
                _on[i] = Start(loop.On, _engine);
            }
            if (!_off[i].IsValid)
            {
                _off[i] = Start(loop.Off, _engine);
            }
            Set(_on[i], level + loop.OnDb + Db(w) + onDb, pitch);
            Set(_off[i], level + loop.OffDb + Db(w) + offDb, pitch);
        }
        // The turbo's whistle by its boost, louder and higher overboosted (VEHICLES.md §9.10); its release; a gear change's clunk.
        if (_e.Turbo != null)
        {
            if (!_turbo.IsValid)
            {
                _turbo = Start(_e.Turbo, _engine);
            }
            float over = revs.Overboost;
            Set(_turbo, level + Db(MathF.Pow(revs.Boost, 1.4f)) + 6f * over, (0.55f + 0.6f * revs.Boost + 0.18f * over) * _e.Pitch, 0.1f);
        }
        // Overboosted, the engine labouring at full song over its loops (VEHICLES.md §9.10).
        if (_e.Labour != null)
        {
            float labour = revs.Overboost * (0.3f + 0.7f * revs.Load);
            if (labour > 0.01f && !_labour.IsValid)
            {
                _labour = Start(_e.Labour, _engine);
            }
            Set(_labour, level - 2f + Db(labour), rpm / _e.LabourRpm * _e.Pitch, 0.15f);
            if (labour <= 0.01f && revs.Overboost < 0.01f)
            {
                Stop(ref _labour, 0.3f);
            }
        }
        // Hard on the brakes at high revs: the Jake brake barks once.
        if (_e.Jake != null && revs.Overrun > 0.7f && _overrunWas <= 0.7f && rpm > 0.55f * _e.Redline && _time >= _nextJake)
        {
            Sounds.At(_e.Jake, _engine.GlobalPosition, _e.Volume);
            _nextJake = _time + 6;
        }
        _overrunWas = revs.Overrun;
        if (revs.Released)
        {
            Sounds.At(_e.Blowoff, _engine.GlobalPosition, _e.Volume + Db(0.4f + 0.6f * revs.Boost) + 3f * revs.Overboost);
        }
        if (revs.Shifted)
        {
            Sounds.At(_e.Shift, _engine.GlobalPosition, _e.Volume);
        }
    }

    // A loop's share at these revs: its neighbours either side crossfaded by the revs' ratio (equal power), the lowest
    // below it and the highest above.
    private float Weight(int i, float rpm)
    {
        var loops = _e.Loops;
        if (rpm <= loops[0].Rpm)
        {
            return i == 0 ? 1 : 0;
        }
        if (rpm >= loops[^1].Rpm)
        {
            return i == loops.Count - 1 ? 1 : 0;
        }
        for (int j = 0; j < loops.Count - 1; j++)
        {
            float lo = loops[j].Rpm, hi = loops[j + 1].Rpm;
            if (rpm >= lo && rpm <= hi)
            {
                float t = MathF.Log(rpm / lo) / MathF.Log(hi / lo) * MathF.PI * 0.5f;
                return i == j ? MathF.Cos(t) : i == j + 1 ? MathF.Sin(t) : 0;
            }
        }
        return 0;
    }

    private float StartLength()
    {
        string? key = Sounds.Item(_def.Key, "start");
        SoundEventData? e = key != null ? SoundLibrary.Find(key) : null;
        return e is { Files.Count: > 0 } && SoundLibrary.Stream(e.Files[0]) is { } s ? (float)s.GetLength() : 0f;
    }

    private void StopEngine(float fade)
    {
        for (int i = 0; i < _on.Length; i++)
        {
            Stop(ref _on[i], fade);
            Stop(ref _off[i], fade);
        }
        Stop(ref _turbo, fade);
        Stop(ref _labour, fade);
    }

    // ---------------------------------------------------------------- the tyres and the ground

    private void Ground(VehicleRevs revs, Num.Vector3 velocity, Num.Quaternion rotation, bool landed, string surface, float dt)
    {
        float speed = velocity.Length();
        Vector3 at = _wheels.GlobalPosition;
        if (surface == "water" && _surface != "water" && _known && speed > 2.5f)
        {
            Sounds.At(_e.Splash, at, Db(Math.Clamp(speed / 8f, 0.4f, 1.3f)));
        }
        _surface = surface;

        // A heavy vehicle's air brakes hiss as it comes to rest after driving (VEHICLES.md §9.10).
        if (speed > 2.5f)
        {
            _driven = true;
        }
        else if (_driven && speed < 0.3f && landed)
        {
            _driven = false;
            if (_known && revs.Running && _e.AirBrake != null)
            {
                Sounds.At(_e.AirBrake, at, _e.Volume);
            }
        }

        // Rolling: the ground's loop, swelled and pitched by speed; a new ground crossfades in.
        bool rolling = landed && speed > 0.3f;
        string? key = rolling ? _e.TyreFor(surface) : null;
        if (key != null && key != _tyreKey)
        {
            Stop(ref _tyre, 0.35f);
            _tyre = Start(key, _wheels);
            _tyreKey = key;
        }
        if (_tyre.IsValid)
        {
            float ratio = speed / MathF.Max(_e.TyreSpeed, 0.1f);
            float db = rolling ? _e.TyreDb + 18f * MathF.Log10(MathF.Max(ratio, 0.01f)) + Db(Math.Clamp(speed / 1.5f, 0, 1)) : Silent;
            Set(_tyre, Math.Min(db, _e.TyreDb + 8f), _e.TyrePitch * Math.Clamp(MathF.Pow(ratio, 0.4f), 0.5f, 1.6f), 0.12f);
            _tyreQuiet = rolling ? 0 : _tyreQuiet + dt;
            if (_tyreQuiet > 0.6)
            {
                Stop(ref _tyre, 0.1f);
                _tyreKey = null;
            }
        }

        // Sliding sideways, or spinning the driven wheels to pull away on loose ground.
        Num.Vector3 right = Num.Vector3.Transform(Num.Vector3.UnitX, rotation);
        float lateral = MathF.Abs(Num.Vector3.Dot(velocity, right));
        bool loose = Array.IndexOf(Loose, surface) >= 0;
        float spin = loose && revs.Gear is 1 or -1 && revs.Load > 0.7f && speed < 5f ? (revs.Load - 0.7f) / 0.3f * (1 - speed / 5f) : 0;
        float skid = landed && surface != "water" ? MathF.Max(Math.Clamp((lateral - 1f) / 4f, 0, 1), spin * 0.7f) : 0;
        string? skidKey = skid > 0.05f ? loose ? _e.SkidLoose : _e.SkidHard : null;
        if (skidKey != null && skidKey != _skidKey)
        {
            Stop(ref _skid, 0.25f);
            _skid = Start(skidKey, _wheels);
            _skidKey = skidKey;
        }
        if (_skid.IsValid)
        {
            Set(_skid, skid > 0.02f ? _e.TyreDb + 3f + Db(skid) : Silent, _e.TyrePitch * (0.9f + 0.2f * Math.Clamp(speed / 15f, 0, 1)), 0.1f);
            _skidQuiet = skid > 0.02f ? 0 : _skidQuiet + dt;
            if (_skidQuiet > 0.5)
            {
                Stop(ref _skid, 0.1f);
                _skidKey = null;
            }
        }
    }

    // ---------------------------------------------------------------- the suspension

    // Landing after a jump (a quarter of a second off the ground or more), as hard as it fell; the wheels' every brief
    // parting from the ground isn't one.
    private void Landing(Num.Vector3 velocity, Num.Quaternion rotation, bool landed)
    {
        if (!landed)
        {
            if (!_flying)
            {
                _flying = true;
                _airborneAt = _time;
                _fall = 0;
            }
            _fall = MathF.Max(_fall, -Num.Vector3.Dot(velocity, Num.Vector3.Transform(Num.Vector3.UnitY, rotation)));
            return;
        }
        if (_flying && _known && _time - _airborneAt > 0.25 && _fall > 1f)
        {
            Sounds.At(_e.Land, _wheels.GlobalPosition, Db(Math.Clamp(_fall / 4f, 0.35f, 1.4f)));
        }
        _flying = false;
    }

    private void Suspension()
    {
        if (!_known)
        {
            return;
        }
        Vector3 at = _wheels.GlobalPosition;
        float bump = _view.SuspensionBump;
        if ((_view.SuspensionBottomed || bump > 1.2f) && _time >= _nextKnock)
        {
            Sounds.At(_e.Knock, at, Db(Math.Clamp(bump / 2.5f, 0.35f, 1.4f)));
            _nextKnock = _time + 0.18;
        }
        float work = _view.SuspensionWork;
        if (work is > 0.12f and < 0.9f && _time >= _nextCreak)
        {
            if (Random.Shared.NextDouble() < 0.35)
            {
                Sounds.At(_e.Creak, at, Db(Math.Clamp(work / 0.5f, 0.3f, 1.2f)));
            }
            _nextCreak = _time + 0.6 + 1.2 * Random.Shared.NextDouble();
        }
    }

    /// <summary>Everything it plays, stopped (the vehicle gone or out of sight).</summary>
    public void StopAll()
    {
        StopEngine(0.2f);
        Stop(ref _tyre, 0.2f);
        Stop(ref _skid, 0.2f);
        _tyreKey = _skidKey = null;
        _running = false;
    }
}
