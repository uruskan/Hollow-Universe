using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Fx;

/// <summary>
/// The firefight measurement (<c>--stress firefight --measure &lt;s&gt;</c>, GROUND.md §9.3): ten players in two teams
/// trade fire across an arena of walls, rock and open ground, with the effects off and on in turn (off, on, off, on).
/// Every shot plays what the game would: the muzzle effect on the shooter's gun (first person for the camera's own), a
/// tracer for some, the impact where it lands after its flight (ground, wood, metal, stone, now and then a body), and
/// every six seconds a grenade. The difference between the halves is what the effects cost: frame time, GPU time and
/// the director's own CPU time, printed at the end, with vsync off.
/// </summary>
public partial class FxViewer
{
    private sealed class Shooter
    {
        public Node3D Muzzle = null!;
        public bool Pistol;
        public bool FirstPerson;
        public Vector3 Eye;
        public int Team;
        public double NextShot;
        public int BurstLeft;
        public uint Seq;
        public uint Id;
    }

    private readonly List<Shooter> _shooters = new();
    private readonly List<(Vector3 Position, Vector3 Normal, FxSurface Surface)> _targets = new();
    private readonly List<(double At, Vector3 Position, Vector3 Normal, FxSurface Surface, Vector3 Direction, int Tracer)> _inFlight = new();
    private double _phaseStart;
    private int _phase = -1;
    private double _measureSeconds = 8;
    private double _nextGrenade = 2;
    private readonly List<double> _frameMs = new();
    private readonly List<double> _gpuMs = new();
    private readonly List<double> _fxUs = new();
    private readonly Dictionary<int, (double Frame, double Frame95, double Gpu, double Fx)> _results = new();
    private int _shotsThisPhase;
    private readonly FxRandom _fight = new(99);

    /// <summary>Phase 0 warms up (on), then off, on, off, on.</summary>
    private bool EffectsOn => _phase is 0 or 2 or 4;

    private void StartStress(string kind)
    {
        _measureSeconds = Options.TryGetValue("measure", out string? m) ? double.Parse(m, CultureInfo.InvariantCulture) : 8;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        BuildArena();
        _phase = 0;
        _phaseStart = _clock;
        GD.Print($"[fx-measure] {kind}: {_shooters.Count} players, warm-up 4 s, then {_measureSeconds:0} s each: effects off, on, off, on");
    }

    private void BuildArena()
    {
        // Cover between the teams: walls of three grades, rock, a body, ground of each kind.
        var cover = new (string Kind, Vector3 At, float Yaw)[]
        {
            ("wood", new Vector3(14, 0, -6), -MathF.PI / 2), ("metal", new Vector3(15, 0, 3), -MathF.PI / 2), ("wall", new Vector3(17, 0, 10), -MathF.PI / 2),
            ("wood", new Vector3(-14, 0, 5), MathF.PI / 2), ("metal", new Vector3(-15, 0, -4), MathF.PI / 2), ("rock", new Vector3(4, 0, -8), 0),
            ("rock", new Vector3(-5, 0, 9), MathF.PI), ("dirt", new Vector3(0, 0, 0), 0), ("grass", new Vector3(6, 0, 4), 0), ("sand", new Vector3(-6, 0, -3), 0),
        };
        foreach ((string kind, Vector3 at, float yaw) in cover)
        {
            FxStation st = FxStage.Build(this, kind, at, yaw);
            FxSurface surface = kind switch
            {
                "wood" => FxSurface.Wood,
                "metal" => FxSurface.Metal,
                "wall" => FxSurface.Rock,
                "grass" => FxSurface.Grass,
                "sand" => FxSurface.Sand,
                _ => FxSurface.Dirt,
            };
            if (st.Scatter != null && kind != "rock")
            {
                for (int i = 0; i < 6; i++)
                {
                    (Vector3 p, Vector3 n) = st.Scatter(_fight);
                    _targets.Add((p, n, surface));
                }
            }
        }
        // Open ground round both teams: most shots that miss land in the dirt.
        for (int i = 0; i < 24; i++)
        {
            float side = i % 2 == 0 ? 1 : -1;
            _targets.Add((new Vector3(side * _fight.Range(8, 20), 0.0f, _fight.Range(-12, 12)), Vector3.Up, FxSurface.Dirt));
        }
        // Ten players, five a side, a pistol among each five; the camera is one of them, in first person.
        var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.3f, 0.26f), Roughness = 0.9f };
        for (int i = 0; i < 10; i++)
        {
            int team = i < 5 ? 0 : 1;
            float x = team == 0 ? -18 - (i % 5) * 1.5f : 20 + (i % 5) * 1.5f;
            float z = -8 + (i % 5) * 4f;
            bool pistol = i % 5 == 3;
            var body = new Node3D { Name = $"Player{i}", Position = new Vector3(x, 0, z), Rotation = new Vector3(0, team == 0 ? 0 : MathF.PI, 0) };
            AddChild(body);
            body.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.25f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0), MaterialOverride = cloth });
            var hand = new Node3D { Position = new Vector3(0.2f, 1.35f, 0) };
            body.AddChild(hand);
            Node3D muzzle = hand;
            Node3D? gun = ModelCatalog.Instantiate(pistol ? "pistol" : "rifle");
            if (gun != null)
            {
                hand.AddChild(gun);
                muzzle = gun.FindChild("socket_muzzle", true, false) as Node3D ?? hand;
            }
            _shooters.Add(new Shooter
            {
                Muzzle = muzzle, Pistol = pistol, Team = team, Eye = body.Position + new Vector3(0, 1.6f, 0), Id = (uint)i + 1,
                NextShot = _fight.Range(0f, 1.5f),
            });
        }
        // The camera: player 1's eyes, looking across the arena, its own rifle in first person.
        Shooter me = _shooters[1];
        _camera.Fov = 75;
        _camera.Position = me.Eye + new Vector3(0.3f, 0, 0);
        _camera.LookAt(new Vector3(18, 1.0f, 1), Vector3.Up);
        Node3D? fpGun = ModelCatalog.Instantiate("rifle");
        ModelEntry? entry = ModelCatalog.Find("rifle");
        if (fpGun != null && entry?.Held != null)
        {
            ModelCatalog.ForceLod(fpGun, 0);
            PrepareViewmodel(fpGun);
            _camera.AddChild(fpGun);
            fpGun.Transform = entry.Held.FirstPerson.Transform;
            me.Muzzle = fpGun.FindChild("socket_muzzle", true, false) as Node3D ?? me.Muzzle;
            me.FirstPerson = true;
            me.Pistol = false;
        }
    }

    private void UpdateStress(double delta)
    {
        if (_phase > 4)
        {
            return;
        }
        double t = _clock - _phaseStart;
        if (_phase > 0)
        {
            if (delta > 0.012 && Options.ContainsKey("spikes"))
            {
                GD.Print($"[fx-spike] phase {_phase} frame {delta * 1000:0.0} ms, gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):0.00}, "
                    + $"cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()):0.00}, fx {_fx.LastFrameMicroseconds:0} µs (play {_fx.LastPlayMicroseconds:0}), "
                    + $"decals {_fx.ActiveDecals}, lights {_fx.ActiveLights}, t {_clock - _phaseStart:0.00}");
            }
            _frameMs.Add(delta * 1000.0);
            _gpuMs.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()));
            _fxUs.Add(_fx.LastFrameMicroseconds);
        }
        if (t >= (_phase == 0 ? 4 : _measureSeconds))
        {
            EndPhase();
            if (_phase > 4)
            {
                return;
            }
        }
        FireRound();
    }

    private void EndPhase()
    {
        if (_phase > 0 && _frameMs.Count > 10)
        {
            var sorted = _frameMs.OrderBy(x => x).ToList();
            double p95 = sorted[(int)(sorted.Count * 0.95)];
            _results[_phase] = (_frameMs.Average(), p95, _gpuMs.Average(), _fxUs.Average());
            GD.Print($"[fx-measure] phase {_phase} ({(EffectsOn ? "effects on" : "effects off")}): {_frameMs.Count} frames, frame {_frameMs.Average():0.00} ms "
                + $"(95%: {p95:0.00}), GPU {_gpuMs.Average():0.00} ms, director {_fxUs.Average():0} µs, {_shotsThisPhase} shots, "
                + $"lights {_fx.ActiveLights}, decals {_fx.ActiveDecals}, tracers {_fx.ActiveTracers}");
        }
        _frameMs.Clear();
        _gpuMs.Clear();
        _fxUs.Clear();
        _shotsThisPhase = 0;
        _phase++;
        _phaseStart = _clock;
        if (!EffectsOn)
        {
            _fx.ClearAll();
        }
        if (_phase > 4)
        {
            Report();
        }
    }

    private void Report()
    {
        var off = new[] { _results[1], _results[3] };
        var on = new[] { _results[2], _results[4] };
        double frameOff = off.Average(r => r.Frame), frameOn = on.Average(r => r.Frame);
        double p95Off = off.Average(r => r.Frame95), p95On = on.Average(r => r.Frame95);
        double gpuOff = off.Average(r => r.Gpu), gpuOn = on.Average(r => r.Gpu);
        double fx = on.Average(r => r.Fx);
        GD.Print($"[fx-measure] RESULT firefight, 10 players, {_measureSeconds:0} s a phase, vsync off: frame {frameOff:0.00} → {frameOn:0.00} ms "
            + $"(+{frameOn - frameOff:0.00} ms; 95th percentile {p95Off:0.00} → {p95On:0.00}), GPU {gpuOff:0.00} → {gpuOn:0.00} ms (+{gpuOn - gpuOff:0.00} ms), "
            + $"director CPU {fx / 1000.0:0.000} ms a frame");
        ReadyForShot = true;
        if (!Options.ContainsKey("shot"))
        {
            GetTree().Quit();
        }
    }

    /// <summary>Every player's gun: bursts at its cadence, pauses between; the shots land after their flight.</summary>
    private void FireRound()
    {
        bool on = EffectsOn;
        foreach (Shooter s in _shooters)
        {
            while (_clock >= s.NextShot)
            {
                if (s.BurstLeft <= 0)
                {
                    s.BurstLeft = s.Pistol ? (int)_fight.Range(3, 8) : (int)_fight.Range(4, 11);
                }
                s.BurstLeft--;
                s.Seq++;
                double interval = s.Pistol ? 0.15 : 60.0 / 450.0;
                s.NextShot += s.BurstLeft > 0 ? interval : _fight.Range(0.5f, 1.8f);
                Shoot(s, on);
            }
        }
        if (_clock >= _nextGrenade)
        {
            _nextGrenade = _clock + 6.0;
            if (on)
            {
                FxDirector.Play("explosion.grenade", new Vector3(_fight.Range(-6, 10), 0, _fight.Range(-8, 8)), Vector3.Up);
            }
        }
        for (int i = _inFlight.Count - 1; i >= 0; i--)
        {
            if (_inFlight[i].At <= _clock)
            {
                (double _, Vector3 p, Vector3 n, FxSurface surface, Vector3 dir, int tracer) = _inFlight[i];
                _inFlight.RemoveAt(i);
                if (on)
                {
                    FxDirector.EndTracer(tracer, p);
                    FxDirector.Impact(surface, p, n, dir);
                }
            }
        }
    }

    private void Shoot(Shooter s, bool on)
    {
        _shotsThisPhase++;
        (Vector3 target, Vector3 normal, FxSurface surface) = _targets[(int)(_fight.Next() * _targets.Count)];
        if (_fight.Next() < 0.12f)
        {
            // A hit on someone of the other team.
            Shooter enemy = _shooters[(int)(_fight.Next() * 5) + (s.Team == 0 ? 5 : 0)];
            target = enemy.Eye + new Vector3(0, -0.35f, 0);
            normal = (s.Eye - target).Normalized();
            surface = FxSurface.Flesh;
        }
        Vector3 from = s.Muzzle.GlobalPosition;
        Vector3 dir = (target - from).Normalized();
        double flight = (target - from).Length() / 300.0;
        int tracer = 0;
        if (on)
        {
            FxDirector.Muzzle(FxDirector.MuzzleKey(s.Pistol ? "pistol" : "rifle", false, false), s.Muzzle, s.FirstPerson);
            if (FxDirector.WantsTracer(s.Id, s.Seq))
            {
                tracer = FxDirector.Tracer("tracer.bullet", from, dir * 300f, new Vector3(0, -9f, 0), 0.3f);
            }
        }
        _inFlight.Add((_clock + flight, target, normal, surface, dir, tracer));
    }
}
