using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using HollowUniverse.Audio;
using HollowUniverse.Build;
using HollowUniverse.Fx;
using HollowUniverse.GalaxyMap;
using HollowUniverse.Ground;
using HollowUniverse.Surface;
using HollowUniverse.Items;
using HollowUniverse.Ships;
using HollowUniverse.SystemMap;
using HollowUniverse.UI.Viewer;

namespace HollowUniverse.App;

/// <summary>
/// Entry point. Opens the survival game's main menu; explicit views keep the development viewers available.
/// Command-line options (after "--") drive the views for automated screenshots, e.g.
/// <c>-- --view system --focus Sol --time 2026-10-10T12:00:00Z --shot sol.png</c>.
/// </summary>
public partial class Main : Node
{
    private const string DefaultView = "ground";

    private GalaxyMapView _galaxyMap = null!;
    private SystemMapView _systemMap = null!;
    private SurfaceView _surfaceView = null!;
    private Dictionary<string, string> _options = new();

    // A clean exit clears the crash marker (GROUND.md §4.3).
    public override void _ExitTree() => CrashReporter.Stop();
    private double _elapsed;
    private bool _capturing;
    private bool _rescaled;
    private UiScaler _scaler = null!;
    private GroundMode? _ground;
    private BuildSandbox? _build;
    private ShipSandbox? _ship;
    private UiViewer? _ui;
    private IconRenderer? _icons;
    private ModelViewer? _models;
    private HollowUniverse.Monuments.MonumentViewer? _monuments;
    private SoundViewer? _sounds;
    private FxViewer? _fx;
    private HollowUniverse.Animals.AnimalSandbox? _animals;

    public override void _Ready()
    {
        // Crash reports first (GROUND.md §4.3): everything after may be what fails.
        CrashReporter.Install();
        _options = ParseOptions(OS.GetCmdlineUserArgs());
        bool isolatedShot = CaptureIsolation.Requested(_options);
        Vector2I captureResolution = CaptureIsolation.RequestedResolution(GetWindow().Size);
        CrashReporter.Running = "view " + Option("view", DefaultView) + (_options.ContainsKey("host") ? ", hosting" : "") +
            (_options.TryGetValue("connect", out string? server) ? ", connected to " + server : "");
        if (_options.ContainsKey("physics-check"))
        {
            GroundChecks.RunPhysicsCheck(this);
            return;
        }
        // The graphics settings (GROUND.md §8.6), saved or this computer's defaults; --quality <preset> for this run.
        HollowUniverse.Settings.GraphicsSettings.Load(HollowUniverse.Settings.GraphicsSettings.ParsePreset(Option("quality", "")));
        if (isolatedShot || Array.IndexOf(OS.GetCmdlineArgs(), "--windowed") >= 0)
            HollowUniverse.Settings.GraphicsSettings.Preview(HollowUniverse.Settings.GraphicsSettings.Current with
                { WindowMode = HollowUniverse.Settings.GameWindowMode.Windowed });
        AddChild(new HollowUniverse.Settings.GraphicsNode { Name = "Graphics" });
        if (_options.TryGetValue("flora-impostor-export", out string? exportDirectory))
        {
            string seedOption = Option("flora-seed", "");
            int exportSeed;
            if (seedOption == "earth") exportSeed = (int)(Hollow.Simulation.ZoneSpec.StartingWorld(0, 0).SurfaceSeed & 0x7FFFFFFF);
            else if (!int.TryParse(seedOption, NumberStyles.Integer, CultureInfo.InvariantCulture, out exportSeed))
            { GD.PushError("--flora-impostor-export requires --flora-seed earth or an integer."); GetTree().Quit(1); return; }
            AddChild(new HollowUniverse.Surface.Flora.FloraImpostorExport { OutputDirectory = exportDirectory, LibrarySeed = exportSeed });
            SetProcess(false);
            return;
        }
        if (_options.TryGetValue("environment-lifetime-check", out string? lifetimeOutput))
        {
            AddChild(new EnvironmentLifetimeCheck { OutputPath = lifetimeOutput });
            SetProcess(false);
            return;
        }
        if (_options.ContainsKey("gib-budget-check"))
        {
            AddChild(new HollowUniverse.Gibs.GibBudgetCheck
            {
                Name = "GibBudgetCheck", OutputPath = Option("gib-budget-check", ""),
                ScreenshotPath = Option("shot", ""),
            });
            SetProcess(false);
            return;
        }
        if (_options.ContainsKey("audio-loop-check"))
        {
            AddChild(new HollowUniverse.Audio.AudioLoopCheck());
            SetProcess(false);
            return;
        }
        if (_options.ContainsKey("fx-budget-check"))
        {
            AddChild(new HollowUniverse.Fx.FxBudgetCheck
            {
                OutputPath = Option("fx-budget-check", ""), ScreenshotPath = Option("shot", ""),
            });
            SetProcess(false);
            return;
        }
        if (_options.ContainsKey("contact-feedback-check"))
        {
            AddChild(new ContactFeedbackCheck
            {
                OutputPath = Option("contact-feedback-check", ""), ScreenshotPath = Option("shot", ""),
            });
            SetProcess(false);
            return;
        }
        if (_options.ContainsKey("shadow-budget-check"))
        {
            AddChild(new HollowUniverse.Settings.ShadowBudgetCheck
            {
                Name = "ShadowBudgetCheck", OutputPath = Option("shadow-budget-check", ""),
                ScreenshotPath = Option("shot", ""),
            });
            SetProcess(false);
            return;
        }
        if (isolatedShot) AddChild(new CaptureIsolation { Name = "CaptureIsolation", Resolution = captureResolution });
        // The interface scale has to be known before any interface is built.
        _scaler = new UiScaler { Name = "UiScaler" };
        if (_options.ContainsKey("ui-scale"))
        {
            _scaler.Override = (float)Number("ui-scale", 1);
        }
        AddChild(_scaler);
        if (_options.ContainsKey("build-wire-tool"))
        {
            WireToolAssetBuilder.Build();
            GetTree().Quit();
            SetProcess(false);
            return;
        }
        if (_options.TryGetValue("electricity-review", out string? electricityOutput))
        {
            AddChild(new ElectricityReview { OutputDirectory = electricityOutput });
            SetProcess(false);
            return;
        }
        // Item icons rendered from the models (GROUND.md §9); --placeholder-icons keeps the vector drawings.
        if (!_options.ContainsKey("placeholder-icons"))
        {
            CatalogItemIcons.Install();
        }
        if (Option("view", DefaultView) == "icons")
        {
            // Renders the item icons (--out <dir>) and shows them all on one sheet (GROUND.md §9).
            _icons = new IconRenderer { Name = "Icons", Options = _options };
            AddChild(_icons);
            return;
        }
        if (Option("view", DefaultView) == "models")
        {
            // Every catalogue model on the ground, or one on a turntable (GROUND.md §9).
            _models = new ModelViewer { Name = "Models", Options = _options };
            AddChild(_models);
            return;
        }
        if (Option("view", DefaultView) == "monuments")
        {
            // Every monument at its real size, a row per tier, with a fly camera (MONUMENTS.md §8.5).
            _monuments = new HollowUniverse.Monuments.MonumentViewer { Name = "Monuments", Options = _options };
            AddChild(_monuments);
            return;
        }
        if (Option("view", DefaultView) == "fx")
        {
            // The effects library on its stations, contact sheets, scenes and the firefight measurement (GROUND.md §9.3).
            _fx = new FxViewer { Name = "Fx", Options = _options };
            AddChild(_fx);
            return;
        }
        if (Option("view", DefaultView) == "ui")
        {
            // The ground game's interface on sample data (GROUND.md §8): --screen hud|inventory|crafting|...
            _ui = new UiViewer { Name = "UiViewer", Options = _options };
            AddChild(_ui);
            return;
        }
        if (Option("view", DefaultView) == "sounds")
        {
            // The audition viewer (GROUND.md §4.9): every sound event, a source walking away, the ambience.
            _sounds = new SoundViewer { Name = "Sounds", Options = _options };
            AddChild(_sounds);
            return;
        }
        if (Option("view", "") == "held-editor")
        {
            AddChild(new HollowUniverse.Characters.HeldItemEditor(_options));
            SetProcess(false);
            return;
        }
        if (Option("view", "") == "character")
        {
            // The character viewer (GROUND.md §4.8) needs no galaxy; it handles --shot itself.
            AddChild(new HollowUniverse.Characters.CharacterLab(_options));
            SetProcess(false);
            return;
        }
        GalaxyService.Load();
        if (Option("view", DefaultView) == "ground")
        {
            // The ground game (GROUND.md §4.3): its own view; the maps aren't built.
            _ground = new GroundMode { Name = "Ground", Options = _options };
            AddChild(_ground);
            return;
        }
        if (Option("view", DefaultView) == "animals")
        {
            // The animal sandbox (GROUND.md §6.9): herds, a wolf pack, the bear, butchering and cooking on the real surface.
            _animals = new HollowUniverse.Animals.AnimalSandbox { Name = "Animals", Options = _options };
            AddChild(_animals);
            return;
        }
        if (Option("view", DefaultView) == "build")
        {
            // The build sandbox (GROUND.md §6.3): the building rules and their art in a local zone.
            _build = new BuildSandbox { Name = "Build", Options = _options };
            AddChild(_build);
            return;
        }
        if (Option("view", DefaultView) == "ship")
        {
            // The ship sandbox (GROUND.md §6.7): the Tin Finch on the first world's real surface.
            _ship = new ShipSandbox { Name = "Ship", Options = _options };
            AddChild(_ship);
            return;
        }

        _galaxyMap = new GalaxyMapView { Name = "GalaxyMap" };
        AddChild(_galaxyMap);
        _systemMap = new SystemMapView { Name = "SystemMap" };
        AddChild(_systemMap);
        _surfaceView = new SurfaceView { Name = "Surface" };
        AddChild(_surfaceView);
        _galaxyMap.OpenSystemRequested += address => OpenSystem(address, UniverseTime.Now);
        _systemMap.BackRequested += ShowGalaxy;
        _surfaceView.BackRequested += ShowGalaxy;
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
        _surfaceView.SetActive(false);
        _galaxyMap.SetActive(true);
    }

    /// <summary>Opens the first world's surface (GROUND.md §3): the surface view's options are degrees and metres.</summary>
    private void OpenSurface(double time)
    {
        var found = Hollow.Surface.FirstWorld.Find(GalaxyService.Galaxy);
        if (found == null)
        {
            GD.PushError("--view surface: the first world is not in this universe.");
            return;
        }
        (StarSystem system, Body planet) = found.Value;
        var surface = Hollow.Surface.PlanetSurface.For(GalaxyService.Galaxy, system, planet);
        _galaxyMap.SetActive(false);
        _systemMap.SetActive(false);
        _surfaceView.SetActive(true);
        _surfaceView.Open(system, planet, surface, Number("lat", Hollow.Surface.FirstWorld.LandingLatitude),
            Number("lon", Hollow.Surface.FirstWorld.LandingLongitude), Number("alt", 1.7), Number("yaw", 0), Number("pitch", 0), time);
        if (_options.ContainsKey("time-scale"))
        {
            _surfaceView.SetRotationTimeScale(Math.Clamp(Number("time-scale", 1), 0.01, 100000));
        }
        _surfaceView.SetTime(time, paused: _options.ContainsKey("shot"));
        _surfaceView.DebugSky = (int)Number("debug-sky", 0);
        if (_options.ContainsKey("hide-ui"))
        {
            _surfaceView.InterfaceVisible = false;
        }
        if (_options.ContainsKey("clouds"))
        {
            _surfaceView.CloudCoverage = (float)Number("clouds", 0.5);
        }
        if (_options.ContainsKey("flora-lineup"))
        {
            _surfaceView.ShowFloraLineup((float)Number("flora-lineup", 40), (int)Number("flora-lod", 0));
        }
    }

    private bool OpenSystem(SystemAddress address, double time)
    {
        StarSystem? system = GalaxyService.Playable.GetSystem(address);
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
        string view = Option("view", DefaultView);
        double distance = Number("distance", 780);
        double yaw = Number("yaw", 37);
        double pitch = Number("pitch", 58);

        Vector3D focus = Vector3D.Zero;
        SystemAddress? focusAddress = null;
        if (_options.TryGetValue("focus", out string? focusName))
        {
            List<SearchResult> hits = GalaxyService.Playable.Search(focusName, 1);
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

        if (view == "surface")
        {
            OpenSurface(OptionTime());
        }

        if (view == "system" && focusAddress.HasValue)
        {
            double time = OptionTime();
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

    private double OptionTime() => _options.TryGetValue("time", out string? t)
        ? UniverseTime.SecondsSinceJ2000(DateTime.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal))
        : UniverseTime.Now;

    private double _traceTimer;

    public override void _Process(double delta)
    {
        if (_options.ContainsKey("trace") && _ground == null && _ui == null && _icons == null && _models == null && _monuments == null && _build == null
            && _sounds == null && _fx == null && _ship == null && _animals == null)
        {
            _traceTimer += delta;
            if (_traceTimer >= 1)
            {
                _traceTimer = 0;
                if (_surfaceView.Visible)
                {
                    GD.Print($"[trace] t={_elapsed:0.0}s frame={delta * 1000:0.0}ms fps={Engine.GetFramesPerSecond()} tiles={_surfaceView.Terrain.DrawnTiles} pending={_surfaceView.Terrain.Pending} build={_surfaceView.Terrain.BuildMicroseconds:0}us plants={_surfaceView.Plants?.MeshInstances}/{_surfaceView.Plants?.ImpostorInstances} grass={_surfaceView.Grass?.Tufts} ready={_surfaceView.IsReady} exposure={_surfaceView.Sky?.Exposure:0.###} sun={_surfaceView.Sky?.SunElevation:0.0} {_surfaceView.LightDebug}");
                }
                else
                GD.Print($"[trace] t={_elapsed:0.0}s frame={delta * 1000:0}ms stars={_galaxyMap.Stars.LoadedStars} blocks={_galaxyMap.Stars.LoadedBlocks} pending={_galaxyMap.Stars.PendingBlocks} glow={_galaxyMap.IsGlowReady} sky={_galaxyMap.SkyBrightness:0.0} {_galaxyMap.TimingDebug} crowd {_galaxyMap.CrowdingDebug}");
            }
        }
        if (!_options.TryGetValue("shot", out string? path) || _capturing || _ground?.TakesOwnShots == true)
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
            : _build != null ? _build.ReadyForShot
            : _ship != null ? _ship.ReadyForShot
            : _sounds != null ? _sounds.ReadyForShot
            : _icons != null ? _icons.ReadyForShot : _models != null ? _models.ReadyForShot
            : _monuments != null ? _monuments.ReadyForShot
            : _fx != null ? _fx.ReadyForShot
            : _animals != null ? _animals.ReadyForShot
            : _galaxyMap.Visible ? _galaxyMap.Stars.IsIdle && _galaxyMap.IsGlowReady
            : _surfaceView.Visible ? _surfaceView.IsReady : true;
        if (_elapsed >= wait && (ready || _elapsed > wait + 90))
        {
            _capturing = true;
            Capture(path, ready);
        }
    }

    private async void Capture(string path, bool ready)
    {
        // Opt-in phase markers distinguish readiness from a stalled frame/readback/save. They do not force drawing.
        bool trace = _options.ContainsKey("capture-trace");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Progress(string stage)
        {
            if (!trace) return;
            GD.Print("[capture-progress] " + System.Text.Json.JsonSerializer.Serialize(new
            {
                stage, ready, captureSeconds = clock.Elapsed.TotalSeconds, sceneSeconds = _elapsed,
                processId = System.Environment.ProcessId, processFrame = Engine.GetProcessFrames(), path,
                windowVisible = GetWindow().Visible, windowMode = GetWindow().Mode.ToString(),
                renderLoopEnabled = RenderingServer.RenderLoopEnabled,
            }));
        }
        Progress("begin");
        // Let a few frames settle after streaming finished.
        for (int i = 0; i < 3; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Progress($"settled-frame-{i + 1}");
        }
        Progress("await-frame-post-draw");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Progress("frame-post-draw");
        GD.Print($"[capture] ready={ready} elapsed={_elapsed:0.00}s path={path}");
        CaptureIsolation.Report(GetViewport());
        _ground?.ReportCapture();
        Progress("readback-begin");
        using Image image = GetViewport().GetTexture().GetImage();
        Progress("readback-complete");
        Progress("save-begin");
        Error error = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? image.SaveJpg(path, 0.9f) : image.SavePng(path);
        Progress(error == Error.Ok ? "save-complete" : "save-failed");
        GD.Print(error == Error.Ok ? $"Saved screenshot {path} ({image.GetWidth()}x{image.GetHeight()})." : $"Could not save {path}: {error}");
        Progress("quit-requested");
        GetTree().Quit(error == Error.Ok && ready ? 0 : 1);
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
