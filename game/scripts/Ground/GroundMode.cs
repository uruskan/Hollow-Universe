using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Client;
using Hollow.Client.Loading;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Net.Transport;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Surface;
using HollowUniverse.App;
using HollowUniverse.Surface;
using HollowUniverse.UI;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game (GROUND.md §4.3). With neither <c>--host</c> nor <c>--connect</c> it opens the main menu over the
/// landing site: PLAY hosts a game on this computer, CONNECT joins one by address, the name is remembered, QUIT. With
/// them it starts at once: <c>--host [--port n]</c> hosts a game on this computer (others can join on the port): the
/// dedicated server as a process of its own (<see cref="ChildServer"/>), or with <c>--in-process</c> on a thread of
/// this one (tests, the screenshot run's statistics); <c>--connect host:port</c> joins one. Other options (after "--"): <c>--name</c>, <c>--lag</c>,
/// <c>--jitter</c>, <c>--loss</c> (bad-network test), <c>--netgraph</c>, <c>--face &lt;name&gt;</c> (turn toward a
/// player once seen), <c>--db &lt;path&gt;</c> (the hosted server's database), <c>--spawn origin</c> (hosted: arrive at
/// the landing site, not on a beach), <c>--time &lt;UTC&gt;</c> or <c>--hour &lt;h&gt;</c> (show another time of day: a
/// universe time, or the next time it is h o'clock at the landing site), <c>--measure &lt;s&gt; [--expect &lt;n&gt;]</c>
/// (frame and tick times with n other players in view, <see cref="GroundMeasure"/>), <c>--tour &lt;folder&gt;</c>
/// (the scripted screenshot run, <see cref="GroundTour"/>).
/// </summary>
public partial class GroundMode : Node
{
    private GameServer? _server;
    private ChildServer? _child;
    private (string Host, int Port)? _connectWhenReady;
    private GameClient? _client;
    private GroundView? _view;
    private GroundTour? _tour;
    private Node3D? _menu;
    private GameContent _content = null!;

    // The body chosen in the menu (or with --body female|male), sent at connect.
    private bool _female;

    /// <summary>The command-line options; set before adding to the tree.</summary>
    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>For automated screenshots: playing, the world drawn, and the player to face (if any) in view.</summary>
    public bool ReadyForShot => _tour == null && (_view?.ReadyForShot ?? _menuWorld?.IsReady ?? _menu != null);

    private SurfaceView? _menuWorld;

    /// <summary>The scripted screenshot run takes its own shots (Main doesn't).</summary>
    public bool TakesOwnShots => Options.ContainsKey("tour");

    public override void _Ready()
    {
        StartLoadTrace();
        Prefetch();
        // A game: no blocking full collections in the middle of play where they can be avoided.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        // The game's data, the same files the server reads (GROUND.md section 7).
        _content = GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
        // Item icons: the catalogue's renders (Main installs them); with --placeholder-icons, the drawings under
        // items.json's keys.
        if (ItemIcons.Provider is not (HollowUniverse.Items.CatalogItemIcons or ItemIconAliases))
        {
            ItemIcons.Provider = new ItemIconAliases(ItemIcons.Provider);
        }
        // World models: the catalogue's (GROUND.md §9.1); --placeholder-models keeps the shapes made in code.
        if (!Options.ContainsKey("placeholder-models") && GroundModels.Provider is not CatalogModels)
        {
            GroundModels.Provider = new CatalogModels(new PlaceholderModels());
        }
        string name = Option("name", "Survivor");
        _female = Options.TryGetValue("body", out string? body) && body != "true" ? body == "female" : GroundUi.SavedFemaleBody();
        if (Options.TryGetValue("connect", out string? target) && target != "true")
        {
            string[] parts = target.Split(':');
            Start(name, parts[0], parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : Protocol.DefaultPort);
        }
        else if (Options.ContainsKey("host") || Options.ContainsKey("connect"))
        {
            Start(name, null, (int)Number("port", Protocol.DefaultPort));
        }
        else
        {
            ShowMenu();
        }
    }

    // ---------------------------------------------------------------- the main menu

    private void ShowMenu()
    {
        _menu = new Node3D { Name = "Menu" };
        AddChild(_menu);
        // Behind the menu: the landing site, the camera turning slowly at eye height.
        if (PlanetSurfaces.Find(FirstWorld.System.Value, FirstWorld.BodyPath) is { } planet)
        {
            var world = new SurfaceView { Name = "World", Driven = true };
            _menu.AddChild(world);
            _menuWorld = world;
            Vector3D direction = ZoneFrame.Direction(FirstWorld.LandingLatitude * Math.PI / 180, FirstWorld.LandingLongitude * Math.PI / 180);
            Vector3D origin = direction * (planet.Surface.Radius + planet.Surface.HeightAt(direction));
            world.OpenZone(planet.System, planet.Body, planet.Surface, origin, TimeOption() ?? UniverseTime.Now, ZoneSpec.DefaultRotationTimeScale);
            world.Camera.Current = true;
            world.Camera.Fov = 70;
            _menu.AddChild(new MenuCamera { Camera = world.Camera });
        }
        var layer = new CanvasLayer { Name = "MenuInterface" };
        _menu.AddChild(layer);
        var ui = new GroundUi { Name = "GroundUi", Model = new MenuGroundUi() };
        layer.AddChild(ui);
        ui.ShowMenu(inGame: false);
        ui.HostRequested += player => Begin(player, null, Protocol.DefaultPort, ui.FemaleBody);
        ui.ConnectRequested += (player, host, port) => Begin(player, host, port, ui.FemaleBody);
        ui.QuitRequested += () => GetTree().Quit();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Turns the menu's camera slowly over the landing site.</summary>
    private partial class MenuCamera : Node
    {
        public Camera3D Camera = null!;
        private double _time;

        public override void _Process(double delta)
        {
            _time += delta;
            float yaw = 1.05f + (float)(_time * 0.012);
            Camera.Transform = new Transform3D(Basis.FromEuler(new Vector3(0.02f, -yaw, 0), EulerOrder.Yxz), new Vector3(0, 1.7f, 0));
        }
    }

    // From a menu: the menu (or the game before) goes, the game starts.
    private void Begin(string name, string? host, int port, bool female)
    {
        _female = female;
        Callable.From(() =>
        {
            _menu?.QueueFree();
            _menu = null;
            _menuWorld = null;
            Stop();
            Start(name, host, port);
        }).CallDeferred();
    }

    // ---------------------------------------------------------------- a game

    // Hosts (host null) or joins a game and shows it.
    private void Start(string name, string? host, int port)
    {
        LoadingPipeline loading = BeginLoading();
        // The save: --db, else this branch's own (Checkout.HostedSave: a branch's newer schema never meets an older build).
        string database = Options.TryGetValue("db", out string? dbOption) && dbOption != "true" ? dbOption : ProjectSettings.GlobalizePath(Checkout.HostedSave);
        bool connectNow = true;
        if (host == null && !Options.ContainsKey("in-process"))
        {
            // Hosting: the dedicated server in a process of its own, so neither stalls the other (GROUND.md §4.3).
            string repository = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            if (ChildServer.Find(repository) is { } dll)
            {
                _child = ChildServer.Start(dll, repository, port, System.IO.Path.GetFullPath(database), $"{name}'s game",
                    Option("spawn", "beach") == "origin", admin: name, ground: Option("ground", "stand-in"));
                loading.Add(_child.LoadStage());
                host = "127.0.0.1";
                connectNow = false;
                _connectWhenReady = (host, port);
            }
            else
            {
                GD.PushWarning("The dedicated server isn't built (dotnet build HollowUniverse.sln): hosting in this process.");
            }
        }
        if (host == null)
        {
            // Hosting in this process: the same server on a background thread (--in-process).
            try
            {
                _server = new GameServer(new ServerOptions
                {
                    Port = port,
                    Name = $"{name}'s game",
                    DatabasePath = database,
                    Content = _content,
                    SpawnAtOrigin = Option("spawn", "beach") == "origin",
                    Ground = Option("ground", "stand-in") == "walking" ? Hollow.Simulation.GroundKind.Walking : Hollow.Simulation.GroundKind.StandIn,
                    // The hosting player is the game's admin (GROUND.md §8.2).
                    Admins = new[] { name },
                    Log = line => GD.Print("[server] " + line),
                }, GalaxyService.Galaxy);
            }
            catch (System.Net.Sockets.SocketException)
            {
                GD.PushWarning($"Port {port} is taken: another game hosts on it here; joining that one instead.");
                _server = null;
                Start(name, "127.0.0.1", port);
                return;
            }
            _server.StartThread();
            port = _server.Port;
            host = "127.0.0.1";
        }
        NetConditions conditions = NetConditions.FromOptions(key => Options.TryGetValue(key, out string? v) ? v : null);
        _client = new GameClient(new ClientOptions
        {
            Name = name,
            Body = (byte)(_female ? 1 : 0),
            Conditions = conditions,
            Content = _content,
            Log = line => GD.Print("[client] " + line),
        });
        loading.Add(_client.Loading().LoadStages(loading.Plan));
        if (connectNow)
        {
            _client.Connect(host, port);
        }
        _view = new GroundView
        {
            Name = "GroundView",
            Client = _client,
            Loading = loading,
            HostedPort = _server?.Port ?? _child?.Port,
            FaceName = Options.TryGetValue("face", out string? face) ? face : null,
            ShowNetGraph = Options.ContainsKey("netgraph"),
            Muted = Options.ContainsKey("mute"),
            SoundTrace = Options.ContainsKey("sound-trace"),
            HostCommand = Command,
            TimeOverride = TimeOption(),
            ServerAddress = host == "127.0.0.1" && (_server != null || _child != null)
                ? $"this computer, port {port} (hosted here)"
                : $"{host}:{port}",
        };
        _view.QuitRequested += () => GetTree().Quit();
        AddChild(_view);
        _view.Interface.HostRequested += player => Begin(player, null, Protocol.DefaultPort, _view.Interface.FemaleBody);
        _view.Interface.ConnectRequested += (player, address, p) => Begin(player, address, p, _view.Interface.FemaleBody);
        if (Options.ContainsKey("measure"))
        {
            AddChild(new GroundMeasure
            {
                Name = "Measure", View = _view, Server = _server, Command = Command, Seconds = Number("measure", 20), Expect = (int)Number("expect", 0),
            });
        }
        if (Options.TryGetValue("tour", out string? folder) && folder != "true")
        {
            _tour = new GroundTour
            {
                Name = "Tour", View = _view, Folder = folder, Command = Command,
                Only = Options.TryGetValue("tour-steps", out string? steps) ? new HashSet<string>(steps.Split(',')) : null,
                Tree = Options.TryGetValue("tour-tree", out string? tree) ? tree : null,
            };
            AddChild(_tour);
        }
        ShowLoading(_server != null || _child != null ? $"{name}'s game, on this computer" : $"{host}:{port}");
    }

    // An admin command to the game hosted here (null when joined elsewhere).
    private Func<string, bool>? Command => _server != null
        ? command =>
        {
            _server.Execute(command);
            return true;
        }
        : _child != null
            ? command =>
            {
                _child.Execute(command);
                return true;
            }
            : null;

    public override void _Process(double delta)
    {
        UpdateLoading();
        if (_child == null)
        {
            return;
        }
        foreach (string line in _child.TakeLines())
        {
            GD.Print("[server] " + line);
        }
        if (_connectWhenReady is { } target)
        {
            if (_child.Ready)
            {
                _connectWhenReady = null;
                _client?.Connect(target.Host, target.Port);
            }
            else if (_child.Exited)
            {
                _connectWhenReady = null;
                if (_child.PortTaken)
                {
                    // Another game hosts on this port here: join that one instead.
                    GD.PushWarning($"Port {target.Port} is taken: another game hosts on it here; joining that one instead.");
                    _client?.Connect(target.Host, target.Port);
                }
                else
                {
                    _view?.Interface.ShowMenu(inGame: false);
                    _view?.Interface.SetMenuStatus(_child.Error is { } why
                        ? $"The server stopped while starting: {why}"
                        : "The server stopped while starting (see the log).");
                }
                _child.Dispose();
                _child = null;
            }
        }
    }

    private void Stop()
    {
        EndLoading();
        _tour?.QueueFree();
        _tour = null;
        _view?.QueueFree();
        _view = null;
        _client?.Dispose();
        _client = null;
        _connectWhenReady = null;
        if (_server != null)
        {
            _server.Stop();
            _server.Dispose();
            _server = null;
        }
        if (_child != null)
        {
            _child.Dispose();
            _child = null;
        }
    }

    public override void _ExitTree() => Stop();

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            Stop();
        }
    }

    // --time <UTC>, or --hour <h>: the next moment it is h o'clock, local solar time, at the landing site (on the
    // compressed day of play, as the server keeps it).
    private double? TimeOption()
    {
        if (Options.TryGetValue("time", out string? t) && t != "true")
        {
            return UniverseTime.SecondsSinceJ2000(DateTime.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        }
        if (!Options.ContainsKey("hour") || PlanetSurfaces.Find(FirstWorld.System.Value, FirstWorld.BodyPath) is not { } planet)
        {
            return null;
        }
        double hour = Number("hour", 10);
        var sky = new SkyEphemeris(planet.System, planet.Body);
        sky.SetRotationTimeScale(ZoneSpec.DefaultRotationTimeScale, 0);
        Vector3D site = ZoneFrame.Direction(FirstWorld.LandingLatitude * Math.PI / 180, FirstWorld.LandingLongitude * Math.PI / 180);
        double now = UniverseTime.Now, best = now, bestError = double.MaxValue;
        for (double s = 0; s < 6 * 3600; s += 20)
        {
            double h = sky.LocalSolarHours(site, sky.Sun.Index, now + s);
            double error = Math.Abs(((h - hour) % 24 + 36) % 24 - 12);
            if (error < bestError)
            {
                bestError = error;
                best = now + s;
            }
        }
        return best;
    }

    private string Option(string key, string fallback) => Options.TryGetValue(key, out string? v) && v != "true" ? v : fallback;

    private double Number(string key, double fallback) =>
        Options.TryGetValue(key, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
}
