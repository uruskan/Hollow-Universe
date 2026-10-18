using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Simulation.Combat;
using HollowUniverse.Fx;
using HollowUniverse.Settings;

namespace HollowUniverse.Gibs;

/// <summary>
/// Opt-in check of the real gib runtime and preference/UI path in a small independent world. Loads one box's fragments,
/// no terrain, server or save. Preference round trips use a diagnostic file; the player's files are never written.
/// </summary>
public partial class GibBudgetCheck : Node
{
    public string OutputPath { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";

    private sealed record Result(string Name, bool Passed, string Detail);
    private sealed record BatchCount(string Name, int Allocated, int RendererVisible, int ResourceVisible);
    private sealed record VisibilitySnapshot(string Checkpoint, int Live, int Limit, BatchCount[] Batches)
    {
        public int Visible => Batches.Sum(batch => batch.RendererVisible == -1 ? batch.Allocated : batch.RendererVisible);
        public bool Valid => Batches.All(batch => batch.RendererVisible >= -1 && batch.RendererVisible <= batch.Allocated);
        public string Detail => $"live={Live}, renderer visible={Visible}, limit={Limit}, " +
            $"resource visible={Batches.Sum(batch => batch.ResourceVisible == -1 ? batch.Allocated : batch.ResourceVisible)}, " +
            $"allocated={Batches.Sum(batch => batch.Allocated)}";
    }
    private readonly List<Result> _results = new();
    private readonly List<string> _screenshots = new();
    private readonly List<VisibilitySnapshot> _visibility = new();
    private SubViewport? _viewport;
    private GibWorld _world = null!;
    private FxDirector _fx = null!;
    private GibSet _set = null!;
    private GraphicsValues _original = null!;
    private int _floorQueries;
    private bool _finished;
    private string? _preferencesPath;

    public override async void _Ready()
    {
        await Frames(2);
        _original = GraphicsSettings.Current with { };
        GraphicsValues saved = GraphicsSettings.Saved with { };
        Dictionary<string, string> preferenceHashes = PreferenceHashes();
        try
        {
            CheckPreferencesAndRows();
            CreateWorld();
            await Frames(2);
            Expect("bounded fixture", GibCatalog.MaxPieces is > 0 and <= 1024,
                $"catalogue cap={GibCatalog.MaxPieces}; this diagnostic refuses larger workloads");
            if (GibCatalog.MaxPieces > 1024) throw new InvalidOperationException("Diagnostic piece ceiling exceeded.");

            Preview(0);
            _fx.ClearAll();
            _fx._Process(0);
            bool shown = Break();
            _fx._Process(0);
            Expect("minimum is dust only", !shown && _world.Live == 0 && _world.PieceLimit == 0 && _fx.PlayedThisFrame == 1,
                $"pieces={_world.Live}, dust events={_fx.PlayedThisFrame}");
            int probes = _floorQueries;
            for (int i = 0; i < 5; i++) _world._Process(1.0 / 60);
            Expect("minimum skips fragment floor work", probes == _floorQueries, $"floor queries={_floorQueries - probes}");
            await Capture("minimum");

            Preview(100);
            _fx.ClearAll();
            _fx._Process(0);
            shown = Break();
            _fx._Process(0);
            Expect("maximum keeps a full break", shown && _world.Live == _set.Chunks.Length &&
                _world.PieceLimit == GibCatalog.MaxPieces && _fx.PlayedThisFrame == 1,
                $"pieces={_world.Live}/{_set.Chunks.Length}, limit={_world.PieceLimit}, dust events={_fx.PlayedThisFrame}");
            for (int i = 0; i < 18; i++) _world._Process(1.0 / 60);
            Expect("normal floor integration retained", _floorQueries > probes, $"floor queries={_floorQueries - probes}");
            await Capture("maximum");

            _world.Clear();
            _world.Paused = true;
            FillBudget();
            VisibilitySnapshot counts = ReadVisibility("maximum is bounded");
            Expect(counts.Checkpoint, _world.Live == _world.PieceLimit && counts.Valid && counts.Visible == _world.Live, counts.Detail);
            Preview(10);
            counts = ReadVisibility("live reduction while paused");
            Expect(counts.Checkpoint, _world.Live == GibCatalog.MaxPieces / 10 && counts.Valid && counts.Visible == _world.Live, counts.Detail);
            for (int i = 0; i < 12; i++) Break();
            _world._Process(0);
            counts = ReadVisibility("repeated breaks respect reduced cap");
            Expect(counts.Checkpoint, _world.Live == _world.PieceLimit && counts.Valid && counts.Visible == _world.Live, counts.Detail);
            int reduced = _world.Live;
            Preview(100);
            Expect("raising cap does not recreate debris", _world.Live == reduced && _world.PieceLimit == GibCatalog.MaxPieces,
                $"live={_world.Live}, limit={_world.PieceLimit}");
            FillBudget();
            Preview(0);
            counts = ReadVisibility("zero clears every batch immediately");
            Expect("zero clears every batch immediately", _world.Live == 0 && _world.Moving == 0 &&
                counts.Valid && counts.Visible == 0 && _world.ActiveBatches == 0, counts.Detail);
            await Frames(3);
            counts = ReadVisibility("zero stays clear across frames");
            Expect(counts.Checkpoint, _world.Live == 0 && _world.Moving == 0 && counts.Valid &&
                counts.Visible == 0 && _world.ActiveBatches == 0, counts.Detail);
            _fx.ClearAll();
            await Capture("cleared");

            // A detached world must not retain a settings callback; entering again should pick up the current budget.
            Preview(100);
            _viewport!.RemoveChild(_world);
            Preview(0);
            Expect("exit unsubscribes", _world.PieceLimit == GibCatalog.MaxPieces, $"detached limit={_world.PieceLimit}");
            _viewport.AddChild(_world);
            Expect("reentry applies current setting", _world.PieceLimit == 0, $"reentered limit={_world.PieceLimit}");
            Preview(100);
            Break();
            _world._Process(0);
            counts = ReadVisibility("reentered world can break");
            Expect(counts.Checkpoint, _world.Live == _set.Chunks.Length && counts.Valid && counts.Visible == _world.Live, counts.Detail);
            _world.QueueFree();
            await Frames(2);
            Preview(0);
            Expect("freed world stays detached", !GodotObject.IsInstanceValid(_world), "settings change after world deletion completed");
        }
        catch (Exception exception)
        {
            Expect("runtime exception", false, exception.ToString());
        }
        finally
        {
            if (GodotObject.IsInstanceValid(_world) && !_world.IsInsideTree()) _world.Free();
            if (GodotObject.IsInstanceValid(_viewport)) _viewport!.QueueFree();
            await Frames(2);
            try
            {
                GraphicsSettings.Revert();
                Expect("revert restores saved values", GraphicsSettings.Current == saved, "real shared Revert path");
                GraphicsSettings.Preview(_original);
                Expect("preview restored", GraphicsSettings.Current == _original, "original settings restored");
                Expect("saved values unchanged", GraphicsSettings.Saved == saved, "no player Save call");
                Dictionary<string, string> after = PreferenceHashes();
                Expect("player preference bytes unchanged", preferenceHashes.All(pair => after[pair.Key] == pair.Value),
                    "graphics.cfg, ground-settings.cfg and settings.cfg hashes unchanged");
            }
            catch (Exception exception) { Expect("restore exception", false, exception.ToString()); }
            Finish();
        }
    }

    private void CheckPreferencesAndRows()
    {
        using var config = new ConfigFile();
        Expect("missing preference preserves maximum", GraphicsSettings.ReadGibAmount(config) == 100, "legacy/missing key defaults to 100");
        foreach ((Variant Value, int Expected) entry in new (Variant, int)[]
        {
            (-100, 0), (150, 100), (long.MaxValue, 100), (long.MinValue, 0),
            (double.NaN, 100), (double.PositiveInfinity, 100), ("invalid", 100), (32.5, 32),
        })
        {
            config.SetValue("graphics", "gib_amount", entry.Value);
            Expect($"preference {entry.Value}", GraphicsSettings.ReadGibAmount(config) == entry.Expected,
                $"expected={entry.Expected}");
        }
        string folder = string.IsNullOrWhiteSpace(OutputPath)
            ? Path.Combine(Path.GetTempPath(), "hollow-gib-check-" + Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(Absolute(OutputPath))!;
        Directory.CreateDirectory(folder);
        _preferencesPath = Path.Combine(folder, "gib-preferences-" + Guid.NewGuid().ToString("N") + ".cfg");
        foreach (int amount in new[] { 0, 10, 100 })
        {
            GraphicsSettings.WriteGibAmount(config, amount);
            Error saved = config.Save(_preferencesPath);
            using var loaded = new ConfigFile();
            Error read = loaded.Load(_preferencesPath);
            Expect($"saved preference {amount}", saved == Error.Ok && read == Error.Ok && GraphicsSettings.ReadGibAmount(loaded) == amount,
                $"save={saved}, load={read}; isolated diagnostic file");
        }
        SettingRow row = SettingRows.Graphics.Single(r => r.Label == "GIB AMOUNT");
        Expect("minimum and maximum controls", row.SetFraction(_original, 0).GibAmount == 0 &&
            row.SetFraction(_original, 1).GibAmount == 100 && row.Text(_original with { GibAmount = 0 }) == "MIN" &&
            row.Text(_original with { GibAmount = 100 }) == "MAX", "real settings row endpoints");
        Expect("settings fit existing panel", SettingRows.Graphics.Length == 24 && SettingRows.Shadows.Any(r => r.Label == "SHADOW DISTANCE"),
            "two twelve-row columns; shadow distance retained in Shadows");
        foreach (QualityPreset preset in Enum.GetValues<QualityPreset>())
            Expect($"preset {preset} preserves gib preference", GraphicsSettings.WithPreset(_original with { GibAmount = 30 }, preset).GibAmount == 30,
                "no automatic cosmetic quality reduction");
        Preview(-1);
        Expect("preview clamps minimum", GraphicsSettings.Current.GibAmount == 0, "negative preview is zero");
        Preview(101);
        Expect("preview clamps maximum", GraphicsSettings.Current.GibAmount == 100, "oversized preview is 100");
    }

    private void CreateWorld()
    {
        _viewport = new SubViewport
        {
            Name = "GibBudgetWorld", Size = new Vector2I(960, 540), OwnWorld3D = true,
            World3D = new World3D { Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.12f, 0.15f, 0.18f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f,
            } },
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);
        var camera = new Camera3D { Position = new Vector3(2.1f, 1.7f, 2.6f), Current = true };
        _viewport.AddChild(camera);
        camera.LookAt(new Vector3(0, 0.3f, 0));
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 0.9f });
        _viewport.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(8, 0.1f, 8) }, Position = new Vector3(0, -0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.34f, 0.36f), Roughness = 1 },
        });
        _fx = FxDirector.Install(_viewport);
        _fx.CameraOverride = camera;
        _fx.SetProcess(false);
        _set = GibLibrary.Find("small-box", 0) ?? throw new InvalidOperationException("The small-box gib set is missing.");
        _world = new GibWorld { CameraOverride = camera, Floor = Floor };
        _viewport.AddChild(_world);
        _world.SetProcess(false);
    }

    private bool Floor(Vector3 from, Vector3 down, float length, out Vector3 point, out Vector3 normal)
    {
        _floorQueries++;
        point = new Vector3(from.X, 0, from.Z);
        normal = Vector3.Up;
        return true;
    }

    private bool Break() => _world.Break(_set, Transform3D.Identity, Vector3.Right, GibCatalog.Cause(DestroyCause.Melee),
        Vector3.Up, 9.81f, 7, out _);

    private void FillBudget()
    {
        for (int i = 0; i < 512 && _world.Live < _world.PieceLimit; i++) Break();
        _world._Process(0);
    }

    private VisibilitySnapshot ReadVisibility(string checkpoint)
    {
        // Upload writes through RenderingServer, so read the same RID. Keep resource-side metadata
        // alongside the renderer count to expose stale values rather than mistaking capacity for visibility.
        BatchCount[] batches = _world.GetChildren().OfType<MultiMeshInstance3D>().Select(node =>
        {
            MultiMesh mesh = node.Multimesh;
            Rid rid = mesh.GetRid();
            return new BatchCount(node.Name.ToString(), RenderingServer.MultimeshGetInstanceCount(rid),
                RenderingServer.MultimeshGetVisibleInstances(rid), mesh.VisibleInstanceCount);
        }).ToArray();
        var snapshot = new VisibilitySnapshot(checkpoint, _world.Live, _world.PieceLimit, batches);
        _visibility.Add(snapshot);
        return snapshot;
    }

    private void Preview(int amount) => GraphicsSettings.Preview(_original with { GibAmount = amount });

    private async Task Capture(string name)
    {
        if (string.IsNullOrWhiteSpace(ScreenshotPath)) return;
        await Frames(3);
        string requested = Absolute(ScreenshotPath);
        string path = Path.Combine(Path.GetDirectoryName(requested)!, Path.GetFileNameWithoutExtension(requested) + "-" + name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using Image image = _viewport!.GetTexture().GetImage();
        Error result = image.SavePng(path);
        Expect($"capture {name}", result == Error.Ok, $"{path}: {result}");
        if (result == Error.Ok) _screenshots.Add(path);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Expect(string name, bool passed, string detail) => _results.Add(new Result(name, passed, detail));
    private static string Absolute(string path) => Path.GetFullPath(ProjectSettings.GlobalizePath(path));
    private static Dictionary<string, string> PreferenceHashes() =>
        new[] { "user://graphics.cfg", "user://ground-settings.cfg", "user://settings.cfg" }.ToDictionary(path => path,
            path => File.Exists(Absolute(path)) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Absolute(path)))) : "missing");

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        string Json() => JsonSerializer.Serialize(new
        {
            diagnostic = "gib-budget-check", passed = _results.All(r => r.Passed),
            checks = _results.Count, failures = _results.Count(r => !r.Passed), results = _results,
            preferences = _preferencesPath, screenshots = _screenshots, visibility = _visibility,
            limits = "One box asset and at most 1024 cosmetic fragments; no authoritative gameplay or performance claim.",
        }, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            if (!string.IsNullOrWhiteSpace(OutputPath))
            {
                string path = Absolute(OutputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Json());
            }
        }
        catch (Exception exception) { Expect("report output", false, exception.ToString()); }
        GD.Print($"[gib-budget-check] {Json()}");
        GetTree().Quit(_results.All(r => r.Passed) ? 0 : 1);
    }
}
