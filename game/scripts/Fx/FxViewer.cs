using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Fx;

/// <summary>
/// The effects viewer (<c>--view fx</c>, GROUND.md §9.3). By default every effect of the catalogue plays in turn on its
/// station (ground of each kind, rock, wood and metal walls, water, a target dummy, the guns, the campfire, furnace and
/// torch) while the camera turns about it. <c>--fx &lt;key&gt;</c> starts at one effect; with <c>--shot</c> it is
/// caught <c>--at &lt;s&gt;</c> after it starts (its catalogue <c>hero</c> moment by default). Other modes:
/// <c>--sheet &lt;family&gt;</c> (every effect of a family at its hero moment, one cell each), <c>--action
/// &lt;family&gt;</c> (a scene of the family in use) and <c>--stress firefight --measure &lt;s&gt;</c> (ten players
/// fighting: frame time with and without the effects). <c>--night</c> lights it by the moon. Keys: ← → effect, ↑ ↓
/// family, Space replay, P pause, N night, R reload fx.json.
/// </summary>
public partial class FxViewer : Node3D
{
    public Dictionary<string, string> Options { get; set; } = new();

    public bool ReadyForShot { get; private set; }

    private FxDirector _fx = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private WorldEnvironment _environment = null!;
    private Label _title = null!;
    private Label _detail = null!;
    private Label _stats = null!;
    private CanvasLayer _hud = null!;
    private readonly Dictionary<string, FxStation> _stations = new(StringComparer.Ordinal);
    private List<FxDef> _effects = new();
    private int _index;
    private double _clock;
    private double _nextPlay;
    private double _orbit;
    private bool _night;
    private FxHandle _loop;
    private double _shotAt = -1;
    private int _frames;
    private readonly FxRandom _random = new(1234);
    private readonly List<(double At, Action Act)> _schedule = new();
    private string _mode = "turntable";

    public override void _Ready()
    {
        _fx = FxDirector.Install(this);
        _fx.OnlyBatch = Options.TryGetValue("only-batch", out string? only) ? only : null;
        _night = Options.ContainsKey("night");
        BuildWorld();
        BuildHud();
        if (Options.TryGetValue("sheet", out string? family))
        {
            _mode = "sheet";
            StartSheet(family);
            return;
        }
        if (Options.TryGetValue("strip", out string? strip))
        {
            _mode = "sheet";
            StartStrip(strip, Options.TryGetValue("times", out string? times) ? times : "0.05,0.15,0.3,0.6,1.2,2.5");
            return;
        }
        if (Options.TryGetValue("action", out string? action))
        {
            _mode = "action";
            StartAction(action);
            return;
        }
        if (Options.TryGetValue("stress", out string? stress))
        {
            _mode = "stress";
            StartStress(stress);
            return;
        }
        StartTurntable();
    }

    // ---------------------------------------------------------------- the world

    private void BuildWorld()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.26f, 0.42f, 0.66f),
            SkyHorizonColor = new Color(0.66f, 0.72f, 0.78f),
            GroundHorizonColor = new Color(0.45f, 0.44f, 0.4f),
            GroundBottomColor = new Color(0.2f, 0.19f, 0.17f),
            SunAngleMax = 20f,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.75f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            TonemapWhite = 6f,
            SsaoEnabled = true,
            SsaoRadius = 0.8f,
            SsaoIntensity = 1.4f,
            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.1f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            FogEnabled = true,
            FogLightColor = new Color(0.62f, 0.68f, 0.76f),
            FogDensity = 0.0015f,
            FogAerialPerspective = 0.3f,
        };
        _environment = new WorldEnvironment { Environment = env };
        AddChild(_environment);
        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.3f,
            LightColor = new Color(1f, 0.95f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 90f,
        };
        AddChild(_sun);
        _sun.LookAtFromPosition(new Vector3(-0.35f, 1f, 0.55f), Vector3.Zero, Vector3.Up);
        FxStage.Ground(this);
        _camera = new Camera3D { Name = "Camera", Fov = 70, Current = true, Near = 0.03f, Far = 1500 };
        AddChild(_camera);
        ApplyNight();
    }

    private void ApplyNight()
    {
        Godot.Environment env = _environment.Environment;
        var sky = (ProceduralSkyMaterial)env.Sky.SkyMaterial;
        if (_night)
        {
            sky.SkyTopColor = new Color(0.01f, 0.015f, 0.035f);
            sky.SkyHorizonColor = new Color(0.03f, 0.04f, 0.07f);
            sky.GroundHorizonColor = new Color(0.02f, 0.02f, 0.025f);
            sky.GroundBottomColor = new Color(0.01f, 0.01f, 0.01f);
            env.AmbientLightEnergy = 0.6f;
            env.FogLightColor = new Color(0.03f, 0.04f, 0.06f);
            _sun.LightColor = new Color(0.55f, 0.65f, 0.9f);
            _sun.LightEnergy = 0.08f;
            env.TonemapExposure = 1.6f;
        }
        else
        {
            sky.SkyTopColor = new Color(0.26f, 0.42f, 0.66f);
            sky.SkyHorizonColor = new Color(0.66f, 0.72f, 0.78f);
            sky.GroundHorizonColor = new Color(0.45f, 0.44f, 0.4f);
            sky.GroundBottomColor = new Color(0.2f, 0.19f, 0.17f);
            env.AmbientLightEnergy = 0.75f;
            env.FogLightColor = new Color(0.62f, 0.68f, 0.76f);
            _sun.LightColor = new Color(1f, 0.95f, 0.88f);
            _sun.LightEnergy = 1.3f;
            env.TonemapExposure = 1.0f;
        }
    }

    private void BuildHud()
    {
        _hud = new CanvasLayer { Name = "Hud" };
        AddChild(_hud);
        if (Options.ContainsKey("hide-ui"))
        {
            _hud.Visible = false;
        }
        (PanelContainer card, VBoxContainer content) = Ui.Card();
        Ui.Place(card, Control.LayoutPreset.TopLeft);
        _hud.AddChild(card);
        _title = Ui.Label("Effects", TextStyle.Title);
        _detail = Ui.Label("", TextStyle.Secondary);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Ui.Size(_detail, 440);
        _stats = Ui.Label("", TextStyle.Secondary);
        content.AddChild(_title);
        content.AddChild(_detail);
        content.AddChild(_stats);
        content.AddChild(Ui.KeyHints(("← →", "effect"), ("↑ ↓", "family"), ("Space", "replay"), ("P", "pause"), ("N", "night"), ("R", "reload")));
    }

    // ---------------------------------------------------------------- the turntable

    private static readonly string[] StationKinds =
        { "dirt", "grass", "sand", "rock", "wood", "metal", "water", "flesh", "rifle", "pistol", "launcher", "campfire", "furnace", "torch", "wall", "air" };

    private void StartTurntable()
    {
        // The stations stand round a wide ring, each facing the middle.
        for (int i = 0; i < StationKinds.Length; i++)
        {
            float a = MathF.Tau * i / StationKinds.Length;
            var at = new Vector3(MathF.Sin(a) * 26f, 0, MathF.Cos(a) * 26f);
            _stations[StationKinds[i]] = FxStage.Build(this, StationKinds[i], at, a + MathF.PI);
        }
        _effects = FxCatalog.All.ToList();
        if (Options.TryGetValue("fx", out string? key))
        {
            _index = Math.Max(0, _effects.FindIndex(e => e.Key == key));
        }
        if (Options.TryGetValue("shot", out _))
        {
            float at = Options.TryGetValue("at", out string? s) ? float.Parse(s, CultureInfo.InvariantCulture) : _effects[_index].Hero;
            _shotAt = 1.5 + at;
            _orbit = Options.TryGetValue("yaw", out string? y) ? float.Parse(y, CultureInfo.InvariantCulture) : 25;
        }
        Select(_index);
    }

    private FxStation StationFor(FxDef def)
    {
        string kind = def.Stage switch
        {
            "gun" => def.Key.Contains("pistol", StringComparison.Ordinal) ? "pistol" : def.Key.Contains("rocket", StringComparison.Ordinal) ? "launcher" : "rifle",
            _ => def.Stage,
        };
        return _stations.TryGetValue(kind, out FxStation? s) ? s : _stations["dirt"];
    }

    private void Select(int index)
    {
        FxDirector.Stop(_loop, immediate: true);
        _loop = default;
        _fx.ClearAll();
        _index = (index + _effects.Count) % _effects.Count;
        FxDef def = _effects[_index];
        _title.Text = def.Key;
        _detail.Text = $"{def.Family} · {def.Description}";
        _nextPlay = _clock + (_shotAt > 0 ? 1.5 : 0.4);
    }

    private void PlayCurrent()
    {
        FxDef def = _effects[_index];
        FxStation st = StationFor(def);
        PlayAt(def, st, _random);
    }

    /// <summary>Plays an effect on a station the way the game would: impacts on fresh spots, muzzle effects on the gun's
    /// muzzle, loops on their prop, tracers flying past, the world's effects over the wall.</summary>
    private FxHandle PlayAt(FxDef def, FxStation st, FxRandom random, ulong seed = 0, bool scatter = true)
    {
        if (def.IsTracer)
        {
            float speed = def.Key == "trail.arrow" ? 55f : 300f;
            Basis b = st.Root.Basis;
            Vector3 from = st.Look - b.X * 30f + b.Z * 2f + Vector3.Up * 0.4f;
            Vector3 to = st.Look + b.X * 30f - b.Z * 1.0f;
            Vector3 v = (to - from).Normalized() * speed;
            int id = FxDirector.Tracer(def.Key, from, v, new Vector3(0, -9f, 0), def.Key == "trail.arrow" ? 0f : 0.3f);
            _ = id;
            return default;
        }
        if (def.IsLoop)
        {
            if (def.Key == "trail.rocket")
            {
                return default;
            }
            Node3D? follow = st.Follow;
            return FxDirector.Start(def.Key, st.Spot, Vector3.Up, new FxParams { Follow = follow, Seed = seed });
        }
        var p = new FxParams { Seed = seed, Direction = st.Direction };
        Vector3 at = st.Spot, normal = st.Normal;
        switch (def.Family)
        {
            case "muzzle":
                Node3D? muzzle = st.Follow;
                if (def.Key == "backblast.rocket" && st.Root.HasMeta("exhaust"))
                {
                    muzzle = (Node3D)st.Root.GetMeta("exhaust");
                }
                if (muzzle != null)
                {
                    FxDirector.Muzzle(def.Key, muzzle, false);
                    return default;
                }
                break;
            case "impact":
            case "blood":
            case "gather":
                if (st.Scatter != null && def.Key != "blood.drip")
                {
                    (at, normal) = st.Scatter(scatter ? random : new FxRandom(7));
                }
                break;
        }
        if (def.Key.StartsWith("world.", StringComparison.Ordinal) || def.Key == "gather.tree-fall")
        {
            Basis b = st.Root.Basis;
            switch (def.Key)
            {
                case "world.collapse":
                    at = st.Root.GlobalPosition + Vector3.Up * 1.5f;
                    normal = Vector3.Up;
                    p.Direction = b.X;
                    p.Area = new Vector3(1.5f, 1.5f, 0.2f);
                    break;
                case "world.door-slam":
                    at = st.Root.GlobalPosition + b.Z * 0.2f;
                    normal = Vector3.Up;
                    p.Direction = b.X;
                    p.Area = new Vector3(0.5f, 0.02f, 0.05f);
                    break;
                case "world.footstep.sand":
                    break;
                case "gather.tree-fall":
                    at = st.Spot;
                    normal = Vector3.Up;
                    p.Direction = b.X;
                    p.Area = new Vector3(4f, 0.1f, 0.4f);
                    break;
                default:
                    at = st.Root.GlobalPosition + Vector3.Up * 1.5f + b.Z * 0.16f;
                    normal = b.Z;
                    p.Direction = b.X;
                    p.Area = new Vector3(1.5f, 0.15f, 1.5f);
                    break;
            }
        }
        if (def.Key.StartsWith("explosion.", StringComparison.Ordinal) && def.Key != "explosion.ammo")
        {
            normal = Vector3.Up;
        }
        FxDirector.Play(def.Key, at, normal, p);
        return default;
    }

    // ---------------------------------------------------------------- the frame

    public override void _Process(double delta)
    {
        _frames++;
        _clock += delta;
        for (int i = _schedule.Count - 1; i >= 0; i--)
        {
            if (_schedule[i].At <= _clock)
            {
                Action act = _schedule[i].Act;
                _schedule.RemoveAt(i);
                act();
            }
        }
        switch (_mode)
        {
            case "turntable":
                UpdateTurntable(delta);
                break;
            case "stress":
                UpdateStress(delta);
                break;
            case "action":
                UpdateAction(delta);
                break;
        }
        _stats.Text = $"{Engine.GetFramesPerSecond():0} fps · fx {_fx.LastFrameMicroseconds:0} µs · lights {_fx.ActiveLights} · decals {_fx.ActiveDecals} · tracers {_fx.ActiveTracers} · loops {_fx.ActiveLoops}";
    }

    private void UpdateTurntable(double delta)
    {
        FxDef def = _effects[_index];
        FxStation st = StationFor(def);
        if (_clock >= _nextPlay && !_fx.Paused)
        {
            if (def.IsLoop)
            {
                if (!_loop.IsValid)
                {
                    _loop = PlayAt(def, st, _random);
                }
                _nextPlay = double.MaxValue;
            }
            else
            {
                PlayAt(def, st, _random);
                _nextPlay = _clock + Math.Max(1.6, def.Duration + 1.0);
            }
        }
        if (_shotAt > 0 && _clock >= _shotAt && !_fx.Paused)
        {
            _fx.Paused = true;
            ReadyForShot = true;
        }
        if (!_fx.Paused && _shotAt < 0)
        {
            _orbit += delta * 14.0;
        }
        float yaw = st.FullTurn ? (float)_orbit : 35f * MathF.Sin((float)_orbit * 0.05f);
        Aim(_camera, st, yaw, st.Pitch, st.Distance * Zoom(def));
    }

    /// <summary>How far the camera stands back for an effect, relative to its station's distance.</summary>
    private static float Zoom(FxDef def) => def.Family switch
    {
        "explosion" when def.Key is not ("explosion.ammo" or "explosion.dud" or "fuse.burning") => 4.5f,
        "tracer" => def.Key == "trail.rocket" ? 3f : 1.5f,
        "world" when def.Key == "world.collapse" => 1.6f,
        "gather" when def.Key is "gather.tree-fall" or "gather.node-break" => 3.2f,
        _ => 1f,
    };

    private static void Aim(Camera3D camera, FxStation st, float yawDegrees, float pitchDegrees, float distance)
    {
        float yaw = st.Yaw + Mathf.DegToRad(yawDegrees);
        float pitch = Mathf.DegToRad(pitchDegrees);
        var dir = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        camera.Position = st.Look + dir * distance;
        camera.LookAt(st.Look, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Right when _mode == "turntable":
                Select(_index + 1);
                break;
            case Key.Left when _mode == "turntable":
                Select(_index - 1);
                break;
            case Key.Up or Key.Down when _mode == "turntable":
            {
                string family = _effects[_index].Family;
                int step = key.Keycode == Key.Down ? 1 : -1;
                int i = _index;
                do
                {
                    i = (i + step + _effects.Count) % _effects.Count;
                } while (_effects[i].Family == family && i != _index);
                string next = _effects[i].Family;
                Select(_effects.FindIndex(x => x.Family == next));
                break;
            }
            case Key.Space when _mode == "turntable":
                Select(_index);
                break;
            case Key.P:
                _fx.Paused = !_fx.Paused;
                break;
            case Key.N:
                _night = !_night;
                ApplyNight();
                break;
            case Key.R:
                FxCatalog.Reload();
                if (_mode == "turntable")
                {
                    _effects = FxCatalog.All.ToList();
                    Select(_index);
                }
                break;
        }
    }

    private void At(double seconds, Action act) => _schedule.Add((_clock + seconds, act));
}
