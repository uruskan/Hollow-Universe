using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using HollowUniverse.GalaxyMap;
using HollowUniverse.Ground;
using HollowUniverse.Items;
using HollowUniverse.SystemMap;
using HollowUniverse.UI.Viewer;

namespace HollowUniverse.App;

/// <summary>
/// Entry point. Loads the galaxy and switches between the galaxy map and the system map.
/// Command-line options (after "--") drive the views for automated screenshots, e.g.
/// <c>-- --view system --focus Sol --time 2026-10-10T12:00:00Z --shot sol.png</c>.
/// </summary>
public partial class Main : Node
{
    private GalaxyMapView _galaxyMap = null!;
    private SystemMapView _systemMap = null!;
    private Dictionary<string, string> _options = new();
    private double _elapsed;
    private bool _capturing;
    private bool _rescaled;
    private UiScaler _scaler = null!;
    private GroundMode? _ground;
    private UiViewer? _ui;
    private IconRenderer? _icons;
    private ModelViewer? _models;

    public override void _Ready()
    {
        _options = ParseOptions(OS.GetCmdlineUserArgs());
        if (_options.ContainsKey("physics-check"))
        {
            GroundChecks.RunPhysicsCheck(this);
            return;
        }
        // The interface scale has to be known before any interface is built.
        _scaler = new UiScaler { Name = "UiScaler" };
        if (_options.ContainsKey("ui-scale"))
        {
            _scaler.Override = (float)Number("ui-scale", 1);
        }
        AddChild(_scaler);
        // Item icons rendered from the models (GROUND.md §9); --placeholder-icons keeps the vector drawings.
        if (!_options.ContainsKey("placeholder-icons"))
        {
            CatalogItemIcons.Install();
        }
        if (Option("view", "galaxy") == "icons")
        {
            // Renders the item icons (--out <dir>) and shows them all on one sheet (GROUND.md §9).
            _icons = new IconRenderer { Name = "Icons", Options = _options };
            AddChild(_icons);
            return;
        }
        if (Option("view", "galaxy") == "models")
        {
            // Every catalogue model on the ground, or one on a turntable (GROUND.md §9).
            _models = new ModelViewer { Name = "Models", Options = _options };
            AddChild(_models);
            return;
        }
        if (Option("view", "galaxy") == "ui")
        {
            // The ground game's interface on sample data (GROUND.md §8): --screen hud|inventory|crafting|...
            _ui = new UiViewer { Name = "UiViewer", Options = _options };
            AddChild(_ui);
            return;
        }
        GalaxyService.Load();
        if (Option("view", "galaxy") == "ground")
        {
            // The ground game (GROUND.md §4.3): its own view; the maps aren't built.
            _ground = new GroundMode { Name = "Ground", Options = _options };
            AddChild(_ground);
            return;
        }

        _galaxyMap = new GalaxyMapView { Name = "GalaxyMap" };
        AddChild(_galaxyMap);
        _systemMap = new SystemMapView { Name = "SystemMap" };
        AddChild(_systemMap);
        _galaxyMap.OpenSystemRequested += address => OpenSystem(address, UniverseTime.Now);
        _systemMap.BackRequested += ShowGalaxy;
        ShowGalaxy();
        ApplyOptions();
        if (_options.ContainsKey("shot"))
        {
            _galaxyMap.Stars.ApplyBudgetMs = 250; // screenshots: build meshes as fast as possible
            _galaxyMap.HoverEnabled = false;
        }
    }

    private void ShowGalaxy()
    {
        _systemMap.SetActive(false);
        _galaxyMap.SetActive(true);
    }

    private bool OpenSystem(SystemAddress address, double time)
    {
        StarSystem? system = GalaxyService.Galaxy.GetSystem(address);
        if (system == null)
        {
            return false;
        }
        _galaxyMap.SetActive(false);
        _systemMap.SetActive(true);
        _systemMap.Open(system, time);
        return true;
    }

    // ---------------------------------------------------------------- automation

    private void ApplyOptions()
    {
        Galaxy galaxy = GalaxyService.Galaxy;
        string view = Option("view", "galaxy");
        double distance = Number("distance", 80);
        double yaw = Number("yaw", 37);
        double pitch = Number("pitch", 34);

        Vector3D focus = Vector3D.Zero;
        SystemAddress? focusAddress = null;
        if (_options.TryGetValue("focus", out string? focusName))
        {
            List<SearchResult> hits = galaxy.Search(focusName, 1);
            if (hits.Count > 0)
            {
                focus = hits[0].Position;
                focusAddress = hits[0].IsSector ? null : hits[0].Address;
            }
            else
            {
                GD.PushWarning($"--focus: nothing called '{focusName}'.");
            }
        }
        if (_options.TryGetValue("position", out string? position))
        {
            string[] p = position.Split(',');
            focus = new Vector3D(Parse(p[0]), Parse(p[1]), Parse(p[2]));
        }
        _galaxyMap.SetView(focus, distance, yaw, pitch);
        if (_options.TryGetValue("select", out string? select) && !_galaxyMap.SelectByName(select))
        {
            GD.PushWarning($"--select: nothing called '{select}'.");
        }
        if (_options.TryGetValue("search", out string? search))
        {
            _galaxyMap.Search(search);
        }

        if (view == "system" && focusAddress.HasValue)
        {
            double time = UniverseTime.Now;
            if (_options.TryGetValue("time", out string? t))
            {
                time = UniverseTime.SecondsSinceJ2000(DateTime.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
            }
            if (OpenSystem(focusAddress.Value, time))
            {
                _systemMap.SetTime(time, paused: _options.ContainsKey("shot"));
                if (_options.ContainsKey("system-distance"))
                {
                    _systemMap.SetCamera(Number("system-distance", 90), Number("yaw", 30), Number("pitch", 32));
                }
                if (_options.TryGetValue("body", out string? body))
                {
                    _systemMap.SelectByName(body);
                }
            }
        }
    }

    private double _traceTimer;

    public override void _Process(double delta)
    {
        if (_options.ContainsKey("trace") && _ground == null && _ui == null && _icons == null && _models == null)
        {
            _traceTimer += delta;
            if (_traceTimer >= 1)
            {
                _traceTimer = 0;
                GD.Print($"[trace] t={_elapsed:0.0}s frame={delta * 1000:0}ms stars={_galaxyMap.Stars.LoadedStars} blocks={_galaxyMap.Stars.LoadedBlocks} pending={_galaxyMap.Stars.PendingBlocks} glow={_galaxyMap.IsGlowReady} sky={_galaxyMap.SkyBrightness:0.0} {_galaxyMap.TimingDebug} crowd {_galaxyMap.CrowdingDebug}");
            }
        }
        if (!_options.TryGetValue("shot", out string? path) || _capturing)
        {
            _elapsed += delta;
            return;
        }
        _elapsed += delta;
        // --rescale=F: change the interface scale after a moment, to test everything following a new scale.
        if (_options.ContainsKey("rescale") && !_rescaled && _elapsed > 0.5)
        {
            _rescaled = true;
            _scaler.Override = (float)Number("rescale", 1);
            _scaler.Apply();
        }
        double wait = Number("wait", 2);
        bool ready = _ui != null ? _ui.ReadyForShot : _ground != null ? _ground.ReadyForShot
            : _icons != null ? _icons.ReadyForShot : _models != null ? _models.ReadyForShot
            : _galaxyMap.Visible ? _galaxyMap.Stars.IsIdle && _galaxyMap.IsGlowReady : true;
        if (_elapsed >= wait && (ready || _elapsed > wait + 90))
        {
            _capturing = true;
            Capture(path);
        }
    }

    private async void Capture(string path)
    {
        // Let a few frames settle after streaming finished.
        for (int i = 0; i < 3; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error error = image.SavePng(path);
        GD.Print(error == Error.Ok ? $"Saved screenshot {path} ({image.GetWidth()}x{image.GetHeight()})." : $"Could not save {path}: {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }
            string key = args[i][2..];
            int equals = key.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                options[key[..equals]] = key[(equals + 1)..];
                continue;
            }
            string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            options[key] = value;
        }
        return options;
    }

    private string Option(string key, string fallback) => _options.TryGetValue(key, out string? v) ? v : fallback;

    private double Number(string key, double fallback) => _options.TryGetValue(key, out string? v) ? Parse(v) : fallback;

    private static double Parse(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
