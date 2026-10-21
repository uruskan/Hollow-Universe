using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client.Effects;
using HollowUniverse.Settings;

namespace HollowUniverse.Fx;

/// <summary>Opt-in native saturation/lifecycle probe of the actual effect director, with no server or saves.</summary>
public partial class FxBudgetCheck : Node
{
    public string OutputPath { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
    private sealed record Check(string Name, bool Passed, string Detail);
    private readonly List<Check> _checks = new();
    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private FxDirector _director = null!;
    private readonly List<FxHandle> _handles = new();
    private double _populationUpdateMs;

    public override async void _Ready()
    {
        int shadowsBefore = LocalShadowBudget.RegisteredCount;
        try
        {
            _viewport = new SubViewport { Size = new Vector2I(800, 600), OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(_viewport);
            _camera = new Camera3D { Position = new Vector3(0, 3, 6), Current = true };
            _viewport.AddChild(_camera);
            _camera.LookAt(new Vector3(0, 1, -12));
            var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-45, -30, 0), LightEnergy = 1.3f };
            _viewport.AddChild(sun);
            _viewport.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(70, 0.2f, 70) },
                Position = new Vector3(0, -0.1f, -15), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.22f, 0.24f) } });
            _director = new FxDirector { CameraOverride = _camera, PreviewOnly = true,
                BudgetSettings = new EffectBudgetSettings { ParticlesPerFrame = 31, ActiveLoops = 24, LoopFlames = 6, LoopLights = 4 } };
            // Starting before entering the tree must retain a cancellable handle, rather than enqueue an orphan loop.
            FxHandle early = _director.StartNow("fire.torch", new Vector3(0, 1, -15), Vector3.Up, default);
            _viewport.AddChild(_director);
            _director.SetProcess(false);
            Expect("pre-ready handle", early.IsValid && _director.LogicalLoops == 1, "Start before _Ready retains its logical handle");
            _director.StopNow(early, true);
            await Step(0.1f);
            Expect("pre-ready cancellation", _director.LogicalLoops == 0, "the early handle stops without leaving an unowned loop");
            CheckTransientClear();

            Populate();
            var owner = new Node3D { Position = new Vector3(0, 0, -1000) };
            _viewport.AddChild(owner);
            FxHandle recovering = _director.StartNow("fire.torch", owner.Position, Vector3.Up, new FxParams { Follow = owner });
            await Step(0.25f);
            Expect("large logical population", _director.LogicalLoops == 289, $"logical={_director.LogicalLoops}, rendered={_director.ActiveLoops}");
            Bounds("saturated population");
            Expect("catalogue loop cap", _director.RenderedOf("wash.lift") <= FxCatalog.Find("wash.lift")!.Cap &&
                _director.RenderedOf("trail.rocket") <= FxCatalog.Find("trail.rocket")!.Cap,
                $"wash={_director.RenderedOf("wash.lift")}, trail={_director.RenderedOf("trail.rocket")}");
            Expect("distant loop is virtual", !_director.LoopIsRendered(recovering), "culled logical source allocates no rendering slot");
            // A nearer distance band wins without restarting the source or needing the caller to retry Start.
            owner.Position = _camera.Position - Vector3.Back;
            await Step(0.25f);
            Expect("distant source recovers", _director.LoopIsRendered(recovering), "moving its owner near the camera reactivates the existing handle");
            Bounds("recovered population");

            // Pure loop emissions and bursts/delayed layers all hit the same real per-renderer-frame counter.
            await Step(0.5f);
            Expect("loop particle ceiling", _director.ParticlesThisFrame == 31, $"emitted={_director.ParticlesThisFrame}");
            for (int i = 0; i < 12; i++) _director.PlayNow("explosion.rocket", new Vector3(0, 0, -10), Vector3.Up, default);
            _director._Process(0.5);
            Bounds("bursts and delayed layers share loop ceiling");
            Expect("same-frame update cannot replenish particles", _director.ParticlesThisFrame == 31,
                $"emitted={_director.ParticlesThisFrame}; a second update in the renderer frame retains the spent budget");

            var timed = _director.StartNow("fire.torch", new Vector3(0, 0, -1000), Vector3.Up, new FxParams { Duration = 0.1f });
            await Step(0.2f);
            Expect("virtual duration expires", !_director.LoopIsRendered(timed) && _director.LogicalLoops == 289,
                "virtual loops retain duration and stop semantics");
            owner.QueueFree();
            await Step(1);
            await Step(1);
            Expect("freed owner releases handle", _director.LogicalLoops == 288, "a freed followed node retires its loop and pooled resources");
            Bounds("owner removal");

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 120; i++) _director._Process(1.0 / 60);
            watch.Stop();
            _populationUpdateMs = watch.Elapsed.TotalMilliseconds / 120;
            Expect("population update measured", double.IsFinite(_populationUpdateMs),
                $"289-request scenario, {_populationUpdateMs:F4} ms/update (CPU-only diagnostic, not gameplay GPU performance)");

            if (!string.IsNullOrWhiteSpace(ScreenshotPath))
            {
                for (int i = 0; i < 12; i++) await Step(1f / 30);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image image = _viewport.GetTexture().GetImage();
                string path = Absolute(ScreenshotPath);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                Expect("saturation screenshot", image.SavePng(path) == Error.Ok, path);
            }

            int allocatedLights = _director.AllocatedLoopLights, allocatedFlames = _director.AllocatedLoopFlames;
            int children = _director.GetChildCount();
            for (int cycle = 0; cycle < 4; cycle++)
            {
                foreach (FxHandle handle in _handles) _director.StopNow(handle, true);
                await Step(0.1f);
                Expect($"stop cycle {cycle}", _director.LogicalLoops == 0 && _director.ActiveLoops == 0 &&
                    _director.ActiveLoopLights == 0 && _director.ActiveLoopFlames == 0, "all handles and active resources retired");
                Populate();
                await Step(0.25f);
                Bounds($"restart cycle {cycle}");
                Expect($"pool reuse cycle {cycle}", _director.AllocatedLoopLights == allocatedLights &&
                    _director.AllocatedLoopFlames == allocatedFlames && _director.GetChildCount() == children,
                    $"lights={_director.AllocatedLoopLights}, flames={_director.AllocatedLoopFlames}, children={_director.GetChildCount()}");
            }
            _director.ClearAll();
            Expect("clear virtual and active population", _director.LogicalLoops == 0 && _director.ActiveLoops == 0 &&
                _director.ActiveLoopLights == 0 && _director.ActiveLoopFlames == 0, "ClearAll retires every logical handle and returns all loop resources");
            Expect("clear unregisters shadows", LocalShadowBudget.RegisteredCount == shadowsBefore, "pooled inactive loop lights do not retain shadow registrations");
        }
        catch (Exception exception) { Expect("native exception", false, exception.ToString()); }
        finally
        {
            if (IsInstanceValid(_viewport)) _viewport.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Expect("director destruction unregisters shadows", LocalShadowBudget.RegisteredCount == shadowsBefore,
                "diagnostic cleanup leaves prior registrations intact");
        }
        int failed = _checks.Count(check => !check.Passed);
        string json = JsonSerializer.Serialize(new { passed = failed == 0, checks = _checks, populationUpdateCpuMs = _populationUpdateMs },
            new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            string path = Absolute(OutputPath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, json);
        }
        GD.Print($"[fx-budget-check] {_checks.Count - failed}/{_checks.Count} passed; CPU {_populationUpdateMs:F4} ms/update");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }

    private void Populate()
    {
        _handles.Clear();
        for (int i = 0; i < 96; i++)
        {
            _handles.Add(_director.StartNow("wash.lift", new Vector3((i % 8) - 4, 0, -25), Vector3.Up, default));
            _handles.Add(_director.StartNow("trail.rocket", new Vector3((i % 8) - 4, 2, -25), Vector3.Up, default));
            _handles.Add(_director.StartNow("fire.torch", new Vector3((i % 8) - 4, 1, -15), Vector3.Up, default));
        }
    }

    private void CheckTransientClear()
    {
        var muzzle = new Node3D { Position = new Vector3(0, 1, -4) };
        _viewport.AddChild(muzzle);
        OmniLight3D[] lights = _director.GetChildren().OfType<OmniLight3D>()
            .Where(node => node.Name.ToString().StartsWith("FxLight", StringComparison.Ordinal)).ToArray();
        MeshInstance3D[] flashes = _director.GetChildren().OfType<MeshInstance3D>()
            .Where(node => node.Name.ToString().StartsWith("FxFlash", StringComparison.Ordinal)).ToArray();
        int children = _director.GetChildCount();
        void Fire() => _director.PlayNow("muzzle.rifle", muzzle.GlobalPosition, Vector3.Right,
            new FxParams { Follow = muzzle, Direction = Vector3.Right });
        try
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Fire();
                _director._Process(0);
                Expect($"transient ownership cycle {cycle}", lights.Any(light => light.Visible) && flashes.Any(flash => flash.Visible) &&
                    FollowReferences("_lights") == 1 && FollowReferences("_flashes") == 1,
                    "the actual muzzle effect owns one transient light and one flash attached to its live source");
                _director.ClearAll();
                Expect($"transient clear cycle {cycle}", _director.ActiveLights == 0 && lights.All(light => !light.Visible && light.LightEnergy == 0) &&
                    flashes.All(flash => !flash.Visible) && FollowReferences("_lights") == 0 && FollowReferences("_flashes") == 0,
                    "world retirement immediately hides transient entries and releases all followed-node references");
                muzzle.Position += Vector3.Right;
                _director._Process(0.01);
                Expect($"transient pool reuse cycle {cycle}", _director.GetChildCount() == children &&
                    lights.All(light => !light.Visible) && flashes.All(flash => !flash.Visible),
                    "updates after retirement do not revive a flash or light, and replay reuses the original pool");
            }
            Fire();
            _director._Process(0.2);
            Expect("expired transient releases owner", _director.ActiveLights == 0 && FollowReferences("_lights") == 0 && FollowReferences("_flashes") == 0,
                "ordinary expiry also releases followed-node references without waiting for world retirement");
        }
        finally
        {
            _director.ClearAll();
            muzzle.QueueFree();
        }
    }

    // Inspect ownership directly in this opt-in fixture; production pools need no extra diagnostic counters.
    private int FollowReferences(string poolField)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        object pool = typeof(FxDirector).GetField(poolField, fields)!.GetValue(_director)!;
        var entries = (System.Collections.IEnumerable)pool.GetType().GetField("_pool", fields)!.GetValue(pool)!;
        return entries.Cast<object>().Count(entry => entry.GetType().GetField("Follow")!.GetValue(entry) != null);
    }

    private async Task Step(float dt)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _director._Process(dt);
    }

    private void Bounds(string name)
    {
        int particles = _director.Batches.Values.Sum(batch => batch.EmittedThisFrame);
        int flames = _director.GetChildren().OfType<MeshInstance3D>().Count(node => node.Name.ToString().StartsWith("Flames", StringComparison.Ordinal) && node.Visible);
        int lights = _director.GetChildren().OfType<OmniLight3D>().Count(node => node.Name.ToString().StartsWith("LoopLight", StringComparison.Ordinal) && node.Visible);
        Expect(name, _director.ActiveLoops <= 24 && _director.ActiveLoopFlames <= 6 && _director.ActiveLoopLights <= 4 &&
            _director.AllocatedLoopFlames <= 6 && _director.AllocatedLoopLights <= 4 &&
            particles <= 31 && particles == _director.ParticlesThisFrame && flames == _director.ActiveLoopFlames && lights == _director.ActiveLoopLights,
            $"loops={_director.ActiveLoops}, flames={flames}/{_director.AllocatedLoopFlames}, lights={lights}/{_director.AllocatedLoopLights}, particles={particles}");
    }

    private void Expect(string name, bool passed, string detail)
    {
        _checks.Add(new Check(name, passed, detail));
        GD.Print($"[fx-budget-check] {(passed ? "PASS" : "FAIL")} {name}: {detail}");
    }

    private static string Absolute(string path) => System.IO.Path.GetFullPath(path.StartsWith("res://", StringComparison.Ordinal)
        || path.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path);
}
