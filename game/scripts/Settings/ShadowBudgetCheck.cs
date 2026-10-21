using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.App;
using HollowUniverse.Fx;

namespace HollowUniverse.Settings;

/// <summary>
/// Opt-in engine smoke check for the shadow settings and local-light registry. Runs in its own small world without
/// loading a save; previews are restored and never saved. The command-line owner adds this node and lets it quit.
/// </summary>
public partial class ShadowBudgetCheck : Node
{
    public string? OutputPath { get; set; }
    public string? ScreenshotPath { get; set; }

    private readonly List<CheckResult> _results = new();
    private readonly List<OmniLight3D> _lights = new();
    private readonly Dictionary<ulong, float> _energies = new();
    private SubViewport? _viewport;
    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private Node3D _hiddenParent = null!;
    private GraphicsValues _original = null!;
    private Action<GraphicsValues>? _applySun;

    private sealed record CheckResult(string Name, bool Passed, string Detail);

    public override async void _Ready()
    {
        // Main installs GraphicsNode and loads preferences before adding the selected view. Let that settle first.
        await Frames(2);
        _original = GraphicsSettings.Current with { };
        GraphicsValues saved = GraphicsSettings.Saved with { };
        Dictionary<string, string>? preferences = null;
        int registeredBefore = LocalShadowBudget.RegisteredCount;
        try
        {
            preferences = PreferenceHashes();
            CreateWorld();
            await Frames(2);
            Expect("isolated camera", _viewport!.GetCamera3D() == _camera &&
                _camera.GetWorld3D() != GetViewport().FindWorld3D(), "test camera belongs to its own viewport/world");
            Expect("registry registration", LocalShadowBudget.RegisteredCount == registeredBefore + _lights.Count,
                $"added {_lights.Count} lights");

            foreach ((int cascades, DirectionalLight3D.ShadowMode mode) in new[]
            {
                (1, DirectionalLight3D.ShadowMode.Orthogonal),
                (2, DirectionalLight3D.ShadowMode.Parallel2Splits),
                (4, DirectionalLight3D.ShadowMode.Parallel4Splits),
            })
            {
                Preview(2, 2, cascades);
                Expect($"cascade preview {cascades}", _sun.ShadowEnabled && _sun.DirectionalShadowMode == mode &&
                    _sun.DirectionalShadowBlendSplits == (cascades > 1),
                    $"mode={_sun.DirectionalShadowMode}, blend={_sun.DirectionalShadowBlendSplits}");
            }

            foreach (int cap in new[] { 0, 1, 2, 8 })
            {
                Preview(2, cap);
                UpdateAndCheck($"local cap {cap}", cap);
                if (cap is 1 or 2)
                    Expect($"closest {cap}", _lights.Take(cap).All(light => light.ShadowEnabled), "nearest lights win");
            }
            Preview(0, 8);
            UpdateAndCheck("shadows off", 0);
            Expect("directional shadows off", !_sun.ShadowEnabled, "sun disabled by the same shadow setting");

            Preview(2, 1);
            LocalShadowBudget.Track(_lights[9], 5, priority: 10);
            UpdateAndCheck("priority", 1);
            Expect("priority beats distance", _lights[9].ShadowEnabled, "farther priority light selected");
            LocalShadowBudget.Track(_lights[9], 5);

            Preview(2, 8);
            _lights[0].LightEnergy = 0;
            _energies[_lights[0].GetInstanceId()] = 0;
            _hiddenParent.Visible = false;
            Vector3 originalPosition = _lights[2].Position;
            _lights[2].Position = _camera.Position + new Vector3(0, 0, -1000);
            UpdateAndCheck("zero energy, hidden parent, outside range", 7);
            Expect("ineligible shadows disabled", !_lights[0].ShadowEnabled && !_lights[1].ShadowEnabled &&
                !_lights[2].ShadowEnabled, "all three ineligible lights remain unshadowed");
            _lights[0].LightEnergy = 0.35f;
            _energies[_lights[0].GetInstanceId()] = 0.35f;
            _hiddenParent.Visible = true;
            _lights[2].Position = originalPosition;
            UpdateAndCheck("eligibility restored", 8);

            // Isolate one light to check the distance setting and influence radius independently of ranking.
            for (int i = 0; i < 9; i++) _lights[i].Visible = false;
            Vector3 lastPosition = _lights[9].Position;
            _lights[9].Position = _camera.Position + new Vector3(0, 0, -44);
            Preview(2, 1, distance: 40);
            UpdateAndCheck("influence reaches shadow distance", 1);
            Preview(2, 1, distance: 30);
            UpdateAndCheck("distance preview excludes light", 0);
            Preview(2, 1, distance: 40);
            UpdateAndCheck("distance preview restores light", 1);
            _lights[9].Position = lastPosition;
            for (int i = 0; i < 9; i++) _lights[i].Visible = true;

            Preview(2, 2);
            UpdateAndCheck("before other viewport update", 2);
            LocalShadowBudget.Update(GetViewport());
            Expect("viewport isolation", ShadowCount() == 2, "another viewport cannot toggle test-world shadows");
            _camera.Current = false;
            UpdateAndCheck("no camera", 0);
            _camera.Current = true;
            UpdateAndCheck("camera restored", 2);

            int beforeFree = LocalShadowBudget.RegisteredCount;
            _lights[0].QueueFree();
            await Frames(2);
            UpdateAndCheck("freed light replaced", 2);
            Expect("freed light unregistered", LocalShadowBudget.RegisteredCount == beforeFree - 1,
                "TreeExiting removes the registry entry");
            LocalShadowBudget.Untrack(_lights[1]);
            UpdateAndCheck("explicit untrack replaced", 2);
            Expect("untracked light stays illuminated", !_lights[1].ShadowEnabled && _lights[1].LightEnergy > 0,
                "withdrawing shadow eligibility keeps light energy");

            await CheckTorchIntegration();
            await CheckCaptureRestoration();

            if (!string.IsNullOrWhiteSpace(ScreenshotPath))
            {
                await Frames(3);
                using Image image = _viewport.GetTexture().GetImage();
                string path = AbsolutePath(ScreenshotPath);
                EnsureParent(path);
                Error result = image.SavePng(path);
                Expect("screenshot", result == Error.Ok, $"{path}: {result}");
            }
        }
        catch (Exception exception)
        {
            Expect("runtime exception", false, exception.ToString());
        }
        finally
        {
            if (_applySun != null) GraphicsSettings.Changed -= _applySun;
            foreach (OmniLight3D light in _lights)
                if (GodotObject.IsInstanceValid(light)) LocalShadowBudget.Untrack(light);
            if (GodotObject.IsInstanceValid(_viewport)) _viewport!.QueueFree();
            try
            {
                GraphicsSettings.Preview(_original);
                Expect("preview restored", GraphicsSettings.Current == _original, "original current values restored");
                Expect("saved values unchanged", GraphicsSettings.Saved == saved, "no Save call during diagnostics");
                if (preferences != null)
                {
                    Dictionary<string, string> after = PreferenceHashes();
                    Expect("preference files unchanged", preferences.All(pair => after[pair.Key] == pair.Value),
                        "graphics, ground and interface preference file hashes match");
                }
                Expect("registry cleanup", LocalShadowBudget.RegisteredCount == registeredBefore,
                    "only this diagnostic's entries removed");
            }
            catch (Exception exception)
            {
                Expect("cleanup exception", false, exception.ToString());
            }
        }
        Finish();
    }

    private async Task CheckTorchIntegration()
    {
        // Use the same independent director API as loading warm-up; never install or replace the gameplay singleton.
        var director = new FxDirector { Name = "TorchIntegration", CameraOverride = _camera, PreviewOnly = true };
        _viewport!.AddChild(director);
        director.SetProcess(false);
        int registered = LocalShadowBudget.RegisteredCount;
        try
        {
            FxHandle handle = director.StartNow("fire.torch", new Vector3(0, 2, -5), Vector3.Up,
                new FxParams { FirstPerson = true, Duration = 1, Seed = 1 });
            director._Process(0.25);
            OmniLight3D? torch = director.GetChildren().OfType<OmniLight3D>()
                .SingleOrDefault(light => light.Name.ToString() == $"LoopLight{handle.Id}");
            Expect("torch loop created", handle.IsValid && director.ActiveLoops == 1 && torch != null,
                "real fire.torch catalogue entry and FxDirector loop");
            Expect("torch registered", LocalShadowBudget.RegisteredCount == registered + 1,
                "catalogue shadow flag registers the actual loop light");
            if (torch != null)
            {
                float energy = torch.LightEnergy;
                Preview(2, 0);
                LocalShadowBudget.Update(_viewport);
                Expect("torch cap zero", !torch.ShadowEnabled && torch.LightEnergy == energy && energy > 0,
                    "cap zero removes shadows while the real torch stays lit");
                Preview(2, 1);
                LocalShadowBudget.Update(_viewport);
                Expect("held torch priority", torch.ShadowEnabled && ShadowCount() == 0 && torch.LightEnergy == energy,
                    "first-person torch wins the single slot over closer ordinary lights without changing energy");
            }
            director._Process(2);
            await Frames(2);
            Expect("torch loop cleanup", director.ActiveLoops == 0 && LocalShadowBudget.RegisteredCount == registered,
                "expired loop frees and unregisters its light");
        }
        finally
        {
            director.QueueFree();
            await Frames(2);
        }
    }

    private async Task CheckCaptureRestoration()
    {
        Viewport viewport = GetViewport();
        Window window = GetWindow();
        bool guiDisabled = viewport.GuiDisableInput, unresizable = window.Unresizable;
        Window.ModeEnum mode = window.Mode;
        Vector2I size = window.Size, position = window.Position;
        Vector2I captureSize = new(Math.Max(320, size.X - 80), Math.Max(240, size.Y - 60));
        var isolation = new CaptureIsolation { Name = "CaptureRestorationCheck", Resolution = captureSize };
        AddChild(isolation);
        try
        {
            await Frames(2);
            Expect("capture input isolation", viewport.GuiDisableInput && window.Unresizable,
                "capture child disables GUI input and window resize");
            if (DisplayServer.GetName() != "headless")
                Expect("capture window enforcement", window.Mode == Window.ModeEnum.Windowed && window.Size == captureSize,
                    $"capture size={window.Size}, requested={captureSize}");
        }
        finally
        {
            RemoveChild(isolation); // Invoke the real _ExitTree restoration before checking the shared root window.
            isolation.QueueFree();
        }
        await Frames(2);
        Expect("capture input restoration", viewport.GuiDisableInput == guiDisabled && window.Unresizable == unresizable,
            "GUI input and resize flags restored after child exit");
        if (DisplayServer.GetName() != "headless")
            Expect("capture window restoration", window.Mode == mode && window.Size == size && window.Position == position,
                $"mode={window.Mode}, size={window.Size}, position={window.Position}");
    }

    private void CreateWorld()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.04f, 0.05f, 0.07f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 0.2f,
        };
        _viewport = new SubViewport
        {
            Name = "ShadowBudgetWorld", Size = new Vector2I(640, 360), OwnWorld3D = true,
            World3D = new World3D { Environment = environment },
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, PositionalShadowAtlasSize = 1024,
        };
        AddChild(_viewport);
        _camera = new Camera3D { Name = "Camera", Position = new Vector3(0, 3, 8), Current = true };
        _viewport.AddChild(_camera);
        _camera.LookAt(new Vector3(0, 0.8f, 0));
        _sun = new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-50, -25, 0), LightEnergy = 0.65f };
        _viewport.AddChild(_sun);
        _applySun = values => GraphicsSettings.ApplyTo(_sun, values);
        GraphicsSettings.Changed += _applySun;
        _applySun(GraphicsSettings.Current);
        AddBox(new Vector3(0, -0.15f, 0), new Vector3(12, 0.3f, 12), new Color(0.35f, 0.4f, 0.45f));
        AddBox(new Vector3(-1.2f, 1, 0), new Vector3(1, 2, 1), new Color(0.85f, 0.45f, 0.2f));
        AddBox(new Vector3(1.2f, 0.5f, -1), Vector3.One, new Color(0.25f, 0.55f, 0.8f));
        _hiddenParent = new Node3D { Name = "HiddenParent" };
        _viewport.AddChild(_hiddenParent);
        for (int i = 0; i < 10; i++)
        {
            var light = new OmniLight3D
            {
                Name = $"Local{i}", Position = new Vector3((i % 3 - 1) * 2, 3, 4 - i * 2),
                LightEnergy = 0.35f + i * 0.025f, OmniRange = 5,
            };
            (i == 1 ? (Node)_hiddenParent : _viewport).AddChild(light);
            _lights.Add(light);
            _energies.Add(light.GetInstanceId(), light.LightEnergy);
            LocalShadowBudget.Track(light, light.OmniRange);
        }
    }

    private void AddBox(Vector3 position, Vector3 size, Color color) => _viewport!.AddChild(new MeshInstance3D
    {
        Position = position, Mesh = new BoxMesh { Size = size },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.8f },
    });

    private void Preview(int shadows, int cap, int cascades = 4, float distance = 40) =>
        GraphicsSettings.Preview(_original with
        {
            Shadows = shadows, MaxShadowLights = cap, ShadowCascades = cascades, LocalShadowDistance = distance,
        });

    private void UpdateAndCheck(string name, int expected)
    {
        LocalShadowBudget.Update(_viewport!);
        int actual = ShadowCount();
        Expect(name, actual == expected, $"expected={expected}, actual={actual}");
        Expect($"{name}: energy unchanged", _lights.Where(GodotObject.IsInstanceValid)
            .All(light => light.LightEnergy == _energies[light.GetInstanceId()]), "shadow selection preserves illumination");
    }

    private int ShadowCount() => _lights.Count(light => GodotObject.IsInstanceValid(light) && light.ShadowEnabled);

    private void Expect(string name, bool passed, string detail) => _results.Add(new CheckResult(name, passed, detail));

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static Dictionary<string, string> PreferenceHashes() =>
        new[] { "user://graphics.cfg", "user://ground-settings.cfg", "user://settings.cfg" }.ToDictionary(path => path,
            path => File.Exists(ProjectSettings.GlobalizePath(path))
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ProjectSettings.GlobalizePath(path)))) : "missing");

    private static string AbsolutePath(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));

    private static void EnsureParent(string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
    }

    private void Finish()
    {
        string Json() => JsonSerializer.Serialize(new
        {
            diagnostic = "shadow-budget-check", passed = _results.All(result => result.Passed),
            checks = _results.Count, failures = _results.Count(result => !result.Passed), results = _results,
        }, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            try
            {
                string path = AbsolutePath(OutputPath);
                EnsureParent(path);
                File.WriteAllText(path, Json());
            }
            catch (Exception exception)
            {
                Expect("report output", false, exception.ToString());
            }
        }
        GD.Print($"[shadow-budget-check] {Json()}");
        GetTree().Quit(_results.All(result => result.Passed) ? 0 : 1);
    }
}
