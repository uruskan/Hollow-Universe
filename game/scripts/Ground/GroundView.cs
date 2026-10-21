using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Client.Loading;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Surface;
using HollowUniverse.Fx;
using HollowUniverse.Surface;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game in first person (GROUND.md §4.3), on a whole world (the stand-in world until S4,
/// docs/WHOLE_WORLD.md §4): the surface view draws the ball (terrain, sea, sky at the universe time, plants, grass;
/// WHOLE_WORLD.md §10) in the zone's own frame, so the server's collision and the drawn ground are the same lattice;
/// the natural objects as the game changed them; deployables, drops and arrows; the other players as characters; the
/// player's arms and tools; and the RustMVP interface over <see cref="ClientGroundUi"/>. Mouse look is applied every
/// frame; the camera sits at the predicted eye, blended between ticks. Zone-frame metres are Godot's coordinates: the
/// world's own frame, origin at its centre, +Y its north pole; up is away from the centre.
/// </summary>
public partial class GroundView : Node3D
{
    /// <summary>Radians of view per mouse count at sensitivity 1 (RustMVP's default); the console's sensitivity scales it.</summary>
    private const float MouseRadians = 0.002f;

    private float MouseSensitivity => MouseRadians * _settings.Sensitivity;

    private const float MaxPitch = 85f * Mathf.Pi / 180f;

    private SurfaceView? _world;
    private Camera3D? _camera;
    private GroundCollision _collision = null!;
    private NatureView _nature = null!;
    private ThingsView _things = null!;
    private FxDirector? _effects;
    private ArrowsView _arrows = null!;
    private PeopleView _people = null!;
    private HollowUniverse.Animals.AnimalsView _animals = null!;
    private FirstPersonView _firstPerson = null!;
    private GroundUi _ui = null!;
    private GroundOverlay _overlay = null!;
    private ClientGroundUi _model = null!;
    private GroundAudio? _audio;
    private BuildView _build = null!;
    private PlacementView _placing = null!;
    private RaidView _raid = null!;
    private FiresView _fires = null!;
    private ShipsView _ships = null!;
    private HollowUniverse.Ships.ShipHud _shipHud = null!;
    private HollowUniverse.Ships.StationSight _stationSight = null!;
    private bool _inventoryAtStation;
    private readonly HollowUniverse.Ships.ShipControls _shipControls = new();
    private readonly HollowUniverse.Ships.ShipCamera _shipCamera = new();
    private bool _wasPiloting;
    private readonly OrbitCamera _seatOrbit = new();
    private uint _cameraSeatShip;
    private int _cameraSeat = -1;
    private Num.Vector3 _seatOrbitFacing = -Num.Vector3.UnitZ;
    private bool _seatSecondaryCaptured;
    private bool SeatThirdPerson => Client.Ships().Seated && _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Chase;
    private RenderWarmup _warmup = null!;
    private System.Collections.Generic.IReadOnlyList<LoadStage> _worldStages = Array.Empty<LoadStage>();
    private bool _greeted;
    private WorldLease? _worldLease;
    public Task WorkersStopped { get; private set; } = Task.CompletedTask;
    private bool _replaceWorldReaders;
    private long _inventoryBodyOwner;

    // What the item in hand does (GROUND_SYSTEMS.md §7): a control per held behaviour, and what they see.
    private readonly Hollow.Simulation.Registry<IHeldControl> _held = HeldControls.Create();
    private HeldContext _hands = null!;
    private Keypad _keypad = null!;
    private Func<string, uint>? _keypadSends;
    private uint _codeSeq;
    private bool _entryWasText;
    private float _lookDYaw, _lookDPitch;

    /// <summary>The camera's field of view when not zoomed (degrees, vertical): the console's fov, 75 by default.</summary>
    public float FieldOfView => _settings.Fov;

    // The view's settings the console sets, and the F1 panel's model (GROUND.md §8.2).
    private ViewSettings _settings = new();
    private ClientConsole _console = null!;
    private ClientDeveloper _developer = null!;
    private DevDraw _devDraw = null!;
    private float _yaw;
    private float _pitch;

    // The frame the view's heading and pitch are read in (IRON_WAKE.md §4.8): the zone's (0), or aboard a ship its frame,
    // by the ship's net ID. Turned into the character's new frame when it boards or leaves, so the camera looks on.
    private uint _viewFrame;

    // Free look (Alt, GROUND.md §4.5): the camera's and the head's turn from the view.
    private FreeLook _freeLook;
    private bool _viewRestored;
    private bool _jumpPressed;

    // The buttons pressed for the hands since the last frame (Hands acts on them).
    private HandPress _presses;
    private bool _faced;
    private bool _menuAfterDisconnect;

    public GameClient Client { get; set; } = null!;

    /// <summary>
    /// The game's loading pipeline (GROUND.md §4.3.1): the view registers its stages (the world's, the renderer's
    /// warm-up, the steady frames) and holds input, the interface and the camera's turning until it hands play over.
    /// </summary>
    public LoadingPipeline? Loading { get; set; }

    // A load is running: the player's input, the interface and mouse look wait.
    private bool Blocked => Loading?.Blocking == true;

    /// <summary>Whether the loading screen shows over the view, fading included (set by the game that owns the screen).</summary>
    public Func<bool>? Covered { get; set; }

    /// <summary>A load runs or its screen still shows: scripted runs wait before a picture.</summary>
    public bool LoadingShown => Blocked || (Covered?.Invoke() ?? false);

    /// <summary>The port of the server this game hosts, if it hosts one.</summary>
    public int? HostedPort { get; set; }

    /// <summary>A player to turn toward once in view (screenshots).</summary>
    public string? FaceName { get; set; }

    public bool ShowNetGraph { get; set; }

    /// <summary>No sound (--mute: tests, measuring runs side by side).</summary>
    public bool Muted { get; set; }

    /// <summary>Prints every sound started (--sound-trace).</summary>
    public bool SoundTrace { get; set; }

    /// <summary>Runs an admin command on the server this game hosts (null when it joined another).</summary>
    public Func<string, bool>? HostCommand { get; set; }

    /// <summary>A universe time to show instead of now (seconds since J2000; screenshots of a time of day).</summary>
    public double? TimeOverride { get; set; }

    /// <summary>
    /// Metres the camera is lifted along up above the eye (--lift, screenshots: the ball from the leaving height); the
    /// world streams around the camera, the character stays where it is.
    /// </summary>
    public float CameraLift { get; set; }

    /// <summary>A pitch to look at once the view is restored (--pitch, radians; screenshots).</summary>
    public float? ShotPitch { get; set; }

    /// <summary>
    /// Screenshots: the heading to turn to once the player has arrived, radians from local north toward the east
    /// (<c>--yaw</c>, in degrees).
    /// </summary>
    public float? ShotYaw { get; set; }

    /// <summary>Cloud cover for this client's sky, 0 (clear) to 1 (overcast); null keeps the default (--clouds).</summary>
    public float? Clouds { get; set; }

    /// <summary>The surface shaders' debug view (<c>--debug-sky</c>, as the surface view's: 10 + the ground's debug_view).</summary>
    public int DebugView { get; set; }

    /// <summary>The menu's QUIT.</summary>
    public event Action? QuitRequested;

    /// <summary>The interface (scripted runs drive it).</summary>
    public GroundUi Interface => _ui;

    /// <summary>The view model under the interface.</summary>
    public ClientGroundUi Model => _model;

    /// <summary>The camera, once the world is open.</summary>
    public Camera3D? Camera => _camera;

    /// <summary>The surface view (opened once the server says where the zone is).</summary>
    public SurfaceView? World => _world;

    /// <summary>The renderer's warm-up (the start's timeline reports it).</summary>
    public RenderWarmup Warmup => _warmup;

    /// <summary>The people drawn (scripted runs face them).</summary>
    public PeopleView People => _people;

    /// <summary>The building in the game (scripted runs choose the plan's piece).</summary>
    public BuildView Building => _build;

    /// <summary>Explosives, blasts and rockets drawn (scripted runs wait for a lamp to light).</summary>
    public RaidView Raid => _raid;

    /// <summary>The code lock's keypad (scripted runs show and close it).</summary>
    public Keypad Keypad => _keypad;

    /// <summary>Asks for a code on the keypad (its title), then sends it as <paramref name="sends"/> says (none: the keypad alone).</summary>
    public void ShowKeypad(string title, Func<string, uint>? sends = null)
    {
        _keypadSends = sends;
        _keypad.Ask(title);
    }

    public void ShowTextEntry(string title, string initial, Func<string, uint> sends)
    {
        _keypadSends = sends;
        _keypad.AskText(title, initial);
    }

    /// <summary>
    /// Turns the view's heading toward a direction of the world (scripted runs), its pitch kept: the heading is the
    /// movement code's, from local north (docs/WHOLE_WORLD.md §5), never an angle on a plane.
    /// </summary>
    public void TurnToward(Num.Vector3 direction)
    {
        if (direction.LengthSquared() > 1e-10f)
        {
            Yaw = Heading(_viewFrame, Num.Vector3.Normalize(direction)).Yaw;
        }
    }

    /// <summary>Points the view along a direction of the world, heading and pitch (scripted runs).</summary>
    public void LookToward(Num.Vector3 direction)
    {
        if (direction.LengthSquared() > 1e-10f)
        {
            (float yaw, float pitch) = Heading(_viewFrame, Num.Vector3.Normalize(direction));
            Yaw = yaw;
            Pitch = pitch;
        }
    }

    /// <summary>The view's yaw and pitch (radians, the movement code's convention); scripted runs set them.</summary>
    public float Yaw
    {
        get => _yaw;
        set => _yaw = Mathf.Wrap(value, -Mathf.Pi, Mathf.Pi);
    }

    public float Pitch
    {
        get => _pitch;
        set => _pitch = Mathf.Clamp(value, -MaxPitch, MaxPitch);
    }

    /// <summary>What the interact key acts on now (its target, what is known of it, its options).</summary>
    public InteractAim? Aimed => _model.Aimed;

    /// <summary>Scripted input instead of the keyboard (screenshots, checks): attack, aim, walking.</summary>
    public InputFrame? Scripted { get; set; }

    /// <summary>Standalone screenshots accept only the authored camera and neutral character input.</summary>
    public bool IsolatedCapture { get; set; }

    /// <summary>Playing, the world drawn around the player, and the player to face (if any) in view.</summary>
    public bool ReadyForShot =>
        Client.State == ClientState.Playing && Client.HasCharacter && (_world?.IsReady ?? false) &&
        !LoadingShown && (FaceName == null || _faced);

    public override void _Ready()
    {
        Client.WorldRetiring += RetireWorld;
        // The world's view (GROUND.md §4.6) exists from the start, so its loading stages are known; it opens once the
        // server says where the zone is. The renderer's warm-up starts loading every file the game can draw now.
        LoadingPlan plan = Loading?.Plan ?? LoadingPlan.Empty;
        _world = new SurfaceView { Name = "World", Driven = true };
        AddChild(_world);
        _world.InterfaceVisible = false;
        if (Clouds is { } clouds)
        {
            _world.CloudCoverage = clouds;
        }
        _world.DebugSky = DebugView;
        _warmup = new RenderWarmup(GroundWarmup.Items(Client.Content), plan.Stage(RenderWarmup.Key)) { Name = "Warmup" };
        AddChild(_warmup);
        _worldStages = _world.LoadStages(plan);
        Loading?.Add(_worldStages);
        Loading?.Add(_warmup.Stage());
        Loading?.Add(SteadyFrames.Stage(plan, frames => GD.Print(
            $"[load] world ready; frame settling reached its {frames.MostSeconds:0.0}s limit " +
            $"(target {frames.FrameMs:0.0}ms). Starting play.")));
        _collision = new GroundCollision { Name = "GroundCollision" };
        AddChild(_collision);
        _nature = new NatureView { Name = "Nature", Client = Client };
        AddChild(_nature);
        _things = new ThingsView { Name = "Things", Client = Client };
        AddChild(_things);
        // Spent arrows stuck where they hit (GROUND_SYSTEMS.md §5), riding what holds them.
        _arrows = new ArrowsView { Name = "Arrows", Client = Client };
        AddChild(_arrows);
        _people = new PeopleView { Name = "People", Client = Client };
        AddChild(_people);
        // G7's animals (GROUND.md §6.9): drawn from the client's replicated ones.
        _animals = new HollowUniverse.Animals.AnimalsView { Name = "Animals", Client = Client };
        AddChild(_animals);
        _firstPerson = new FirstPersonView { Name = "FirstPerson", Client = Client };
        AddChild(_firstPerson);
        var buildHud = new HollowUniverse.Build.BuildHud { Name = "BuildHud" };
        // Placing anything (GROUND_SYSTEMS.md §6): one controller and one ghost, whatever the hands hold.
        _placing = new PlacementView { Name = "Placing", Client = Client };
        AddChild(_placing);
        _build = new BuildView { Name = "Building", Client = Client, Hud = buildHud, Placing = _placing };
        AddChild(_build);

        var layer = new CanvasLayer { Name = "Interface" };
        AddChild(layer);
        _overlay = new GroundOverlay { Name = "Overlay", Client = Client };
        layer.AddChild(_overlay);
        layer.AddChild(buildHud);
        // Raiding: explosives, blasts, rockets, and the raid alerts at the top right.
        var alerts = new RaidAlertsPanel { Name = "RaidAlerts" };
        layer.AddChild(alerts);
        _raid = new RaidView { Name = "Raid", Client = Client, Alerts = alerts };
        AddChild(_raid);
        // Fires (IW4): the pools of burning fuel incendiary rockets leave, riding what they burn on.
        _fires = new FiresView { Name = "Fires", Client = Client };
        AddChild(_fires);
        // Ships (G6): every ship drawn and heard; the flight HUD in place of the belt while piloting.
        _ships = new ShipsView { Name = "Ships", Client = Client };
        _people.Ships = _ships;
        AddChild(_ships);
        _shipHud = new HollowUniverse.Ships.ShipHud { Name = "ShipHud", Visible = false };
        layer.AddChild(_shipHud);
        // A gunner's sight (IW3, IRON_WAKE.md §6.8), over the HUD while seated at a station.
        _stationSight = new HollowUniverse.Ships.StationSight { Name = "StationSight", Visible = false };
        layer.AddChild(_stationSight);
        // The effects library, ready before the first shot or blast; without heat haze, which draws black here (§11).
        _effects = FxDirector.Install(this);
        FxDirector.HeatHaze = false;
        // Things breaking apart (GROUND.md §9.4): every destruction the server reports, one path.
        AddChild(new GibsView { Name = "Gibs", Client = Client });
        _model = new ClientGroundUi(Client) { ViewYaw = () => _yaw, ViewDirection = () => Look(_viewFrame, _yaw, _pitch), Command = Command, Build = _build, AskCode = ShowKeypad, AskText = ShowTextEntry };
        _hands = new HeldContext { Client = Client, Build = _build, Arms = _firstPerson, Placement = _placing, Say = _model.ShowNotice };
        // The radials the held item opens (the aim button, R) and the HUD's ammo come from its control.
        _model.HeldRadial = input => Client.Stations().AtStation
            ? (input == RadialInput.Reload ? new HollowUniverse.Ships.StationAmmoRadial(Client) : null)
            : HandsFree() is { } held ? held.Radial(_hands, input) : null;
        _model.HeldAmmo = () => Holding() is { } held ? held.Ammo(_hands) : null;
        // The F1 panel (GROUND.md §8.2): its console runs the view's own commands here, the rest on the server.
        _settings = ViewSettings.Load();
        _console = new ClientConsole(Client, _settings)
        {
            ToggleNetGraph = () => _overlay.ShowNetGraph = !_overlay.ShowNetGraph,
            Quit = () => QuitRequested?.Invoke(),
            Notice = _model.ShowNotice,
            LocalHours = () => _world?.Sky?.LocalHours,
            Address = ServerAddress,
            ThirdPerson = () => AdminThirdPerson,
            SetThirdPerson = SetAdminThirdPerson,
            Admire = TryAdmire,
        };
        _model.Console = _console;
        // The F2 panel (GROUND.md §8.5): its model over the client's dev module, and its drawings in the world.
        _developer = new ClientDeveloper(Client);
        _model.Developer = _developer;
        _devDraw = new DevDraw { Name = "DevDraw", Client = Client };
        AddChild(_devDraw);
        Client.Console().ZoneDayChanged += ApplyDay;
        _build.Say = _model.ShowNotice;
        _placing.Say = _model.ShowNotice;
        _ui = new GroundUi { Name = "GroundUi", Model = _model };
        _ui.QuitRequested += () => QuitRequested?.Invoke();
        layer.AddChild(_ui);
        // The panels a verb opens, by the name interactions.json gives them (GROUND_SYSTEMS.md §5): one table.
        var panels = new System.Collections.Generic.Dictionary<string, Action>(StringComparer.Ordinal) { ["techtree"] = _ui.OpenTechTree };
        _model.OpenPanel = panel =>
        {
            if (panels.TryGetValue(panel, out Action? open))
            {
                open();
            }
        };
        // The code lock's keypad, over the interface: a code verb asks for its digits, then sends them.
        _keypad = new Keypad { Name = "Keypad" };
        layer.AddChild(_keypad);
        _keypad.Pressed += () => Sounds.Flat("codelock.beep");
        _keypad.Entered += code =>
        {
            if (_keypadSends is { } sends)
            {
                _codeSeq = sends(code);
                _entryWasText = _keypad.TextMode;
            }
        };
        Client.Local.IntentAnswered += AnswerIntent;
        _overlay.ShowNetGraph = ShowNetGraph;
        _inventoryBodyOwner = InventoryBody.Use(Client);
    }

    private void AnswerIntent(IntentAnswer answer)
    {
        if (answer.Seq != _codeSeq || _codeSeq == 0) return;
        Sounds.Flat(_entryWasText ? (answer.Done ? "ui.click" : "ui.error") : (answer.Done ? "codelock.accept" : "codelock.deny"));
        _codeSeq = 0;
    }

    public override void _ExitTree()
    {
        Client.WorldRetiring -= RetireWorld;
        Client.Local.IntentAnswered -= AnswerIntent;
        Client.Console().ZoneDayChanged -= ApplyDay;
        var failures = new System.Collections.Generic.List<Exception>();
        void Clean(Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error); }
        }
        Clean(RetireWorld);
        Clean(() => _ui?.StopMapWork());
        Clean(() => _console?.Dispose());
        Clean(() => _model?.Dispose());
        InventoryBody.Release(_inventoryBodyOwner);
        if (failures.Count != 0) throw new AggregateException("Ground view exit failed after retiring its readers.", failures).Flatten();
    }

    // Runs before the client changes its world. Reader tasks hold this pin until all old readers have returned.
    internal void RetireWorld()
    {
        var failures = new System.Collections.Generic.List<Exception>();
        void Clean(Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error); }
        }
        Clean(() => _build?.RetireWorld());
        Clean(() => _things?.RetireWorld());
        Clean(() => _ships?.RetireWorld());
        Clean(() => _placing?.RetireWorld());
        Clean(() => _people?.RetireWorld());
        Clean(() => _fires?.RetireWorld());
        Clean(() => _raid?.RetireWorld());
        Clean(() => _effects?.ClearAll());
        Clean(() => _devDraw?.RetireWorld());
        Clean(() => _warmup?.RetirePreview());
        Clean(() => _nature?.Stop());
        Clean(() => _collision?.Stop());
        Clean(() => _world?.Stop());
        Task maps = Task.CompletedTask;
        Clean(() => maps = _ui?.ResetMapWork() ?? Task.CompletedTask);
        Task readers = Task.WhenAll(WorkersStopped, maps, _nature?.WorkersStopped ?? Task.CompletedTask,
            _collision?.WorkersStopped ?? Task.CompletedTask, _world?.WorkersStopped ?? Task.CompletedTask);
        WorldLease? lease = _worldLease;
        _worldLease = null;
        WorkersStopped = readers.ContinueWith(static (done, state) =>
        {
            try { done.GetAwaiter().GetResult(); }
            finally { ((WorldLease?)state)?.Dispose(); }
        }, lease, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = WorkersStopped.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _camera = null;
        Clean(() => _audio?.QueueFree());
        _audio = null;
        _keypadSends = null;
        _codeSeq = 0;
        _viewRestored = _faced = _mapWarm = _greeted = false;
        _replaceWorldReaders = true;
        if (failures.Count != 0) throw new AggregateException("Ground view retirement failed after stopping all readers.", failures).Flatten();
    }

    // The zone's map picture (G7) is warmed once the zone is known: rendered on a worker thread, or read from the disk.
    private bool _mapWarm;

    // ---------------------------------------------------------------- the world

    // The world opens once the server has said where the zone is: the surface view draws the zone's own ground, whole,
    // in the zone's frame (docs/WHOLE_WORLD.md §10), under the sky of the planet the world stands for, with the zone's
    // natural objects as its plants.
    private void OpenWorld()
    {
        if (_camera != null || _world == null || Client.Zone is not { } zone || Client.Surface is not { } surface)
        {
            return;
        }
        if (PlanetBodies.Find(zone.SystemAddress, zone.BodyPath) is not { } found)
        {
            if (!_noPlanet)
            {
                _noPlanet = true;
                GD.PushError($"The zone's body ({zone.SystemAddress:X16}/{zone.BodyPath:X8}) is not in this universe: no sky to draw it under.");
            }
            return;
        }
        double time = TimeOverride ?? UniverseTime.Now;
        if (_replaceWorldReaders)
        {
            _collision.QueueFree();
            _nature.QueueFree();
            _collision = new GroundCollision { Name = "GroundCollision" };
            _nature = new NatureView { Name = "Nature", Client = Client };
            AddChild(_collision);
            AddChild(_nature);
            _replaceWorldReaders = false;
        }
        if (_worldLease != null) throw new InvalidOperationException("The previous ground view must retire before opening another world.");
        _worldLease = Client.ForkWorldLease();
        try
        {
        long lap = System.Diagnostics.Stopwatch.GetTimestamp();
        NaturePlants? plants = Client.Survival().Nature is { } nature ? new NaturePlants(nature, Client.Content, Client.Frame, surface.Radius) : null;
        _world.OpenWorld(found.System, found.Body, surface, plants ?? (IPlantSource)new SurfacePlants(surface), zone.SurfaceSeed, time,
            zone.RotationTimeScale, (float)(zone.SurfaceGravity * 9.80665), lease: Client.ForkWorldLease());
        if (Client.Console().Day is { } day)
        {
            ApplyDay(day);
        }
        _camera = _world.Camera;
        _nature.World = _world;
        _nature.Plants = plants;
        Time("open: the world view", ref lap);
        _camera.Fov = FieldOfView;
        _camera.Near = 0.05f;
        _camera.Current = true;
        _firstPerson.Attach(_camera);
        _things.Muzzle = _firstPerson.Muzzle;
        _things.TheirMuzzle = _people.MuzzleOf;
        _people.Contacts = _things.Contacts;
        _warmup.Begin(_camera);
        Time("open: the arms", ref lap);
        _people.MakeSpares();
        Time("open: spare bodies", ref lap);
        // Sound: the director listens at the camera (GROUND.md §4.9); --mute leaves it out.
        if (!Muted)
        {
            _audio = new GroundAudio { Name = "Audio", Client = Client, Camera = _camera, World = _world, Trace = SoundTrace };
            AddChild(_audio);
        }
        Time("open: the sound", ref lap);
        }
        catch (Exception opening)
        {
            try { RetireWorld(); }
            catch (Exception cleanup) { throw new AggregateException("Ground presentation opening and retirement failed.", opening, cleanup); }
            throw;
        }
    }

    private bool _noPlanet;

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        bool playing = Client.State == ClientState.Playing;
        if (playing && Client.HasCharacter && !_viewRestored)
        {
            // The view the server kept, in the frame the character came back in (aboard a ship, its own).
            _viewRestored = true;
            _viewFrame = Client.OwnFrame;
            _yaw = Client.InitialYaw;
            _pitch = Client.InitialPitch;
            if (ShotPitch is { } pitch)
            {
                Pitch = pitch;
            }
            if (ShotYaw is { } bearing)
            {
                Num.Vector3 local = Client.Frame.UpAt(Client.RenderPosition);
                Num.Vector3 east = Num.Vector3.Cross(Num.Vector3.UnitY, local);
                east = east.LengthSquared() > 1e-8f ? Num.Vector3.Normalize(east) : -Num.Vector3.UnitZ;
                Num.Vector3 north = Num.Vector3.Cross(local, east);
                TurnToward(north * MathF.Cos(bearing) + east * MathF.Sin(bearing));
            }
        }
        if (playing)
        {
            OpenWorld();
        }
        UpdateMouse(playing && !Blocked);
        ValidateAdminCamera();
        if (_world != null)
        {
            _world.Hurry = Blocked;
        }
        if (playing && !Blocked && !_greeted)
        {
            // Play is the player's: the interface (drawn all along under the loading screen) greets them now.
            _greeted = true;
            _ui.Greet();
        }
        // Free look while Alt is held and the game has the mouse (on foot, or in the cockpit): letting go, or the cursor
        // showing (inventory, crafting, map, menu, chat, keypad), turns the camera back to the view at once.
        // At a helm whose mouse doesn't steer (a boat's), the mouse always looks round in the cockpit view.
        bool lookingRound = Client.Ships().Piloting && !_shipControls.Scheme.MouseSteers;
        _freeLook.Hold(!AdminThirdPerson && !IsolatedCapture && (Input.IsPhysicalKeyPressed(Key.Alt) || lookingRound) &&
            Input.MouseMode == Input.MouseModeEnum.Captured &&
            (!Client.Ships().Piloting || _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit));
        InputFrame input = ReadInput(delta);
        if (!AdmireAvailable || input.Primary || input.PrimaryPressed || input.Secondary || input.Sprint ||
            _presses != HandPress.None) _firstPerson.CancelAdmire();
        if (!IsolatedCapture) Hands(ref input, _presses);
        _presses = HandPress.None;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Client.Update(delta, input);
        ValidateAdminCamera();
        FollowFrame();
        Time("client", ref t0);
        // The gun's recoil (G4, GROUND.md §6.4), in RustMVP's order once the frame's ticks have run: what the recovering
        // recoil gives back of the player's countersteer goes onto the view they control (up to the moment the camera
        // shows), then the mouse's turn since the last frame earns credit against the recoil showing now.
        LocalGun gun = Client.Local.Gun;
        (float giveYaw, float givePitch) = gun.TakeCompensation();
        if (giveYaw != 0 || givePitch != 0)
        {
            Yaw = _yaw + giveYaw;
            Pitch = _pitch + givePitch;
        }
        gun.ObserveLook(_lookDYaw, _lookDPitch);
        _lookDYaw = _lookDPitch = 0;
        _jumpPressed = false;
        string? status = Client.State switch
        {
            ClientState.Playing => null,
            ClientState.Disconnected => "Disconnected" + (Client.DisconnectReason is { Length: > 0 } why ? ": " + why : ""),
            _ => HostedPort != null ? "Starting the world..." : "Connecting...",
        };
        if (Client.State == ClientState.Disconnected && !_menuAfterDisconnect)
        {
            // The connection is gone: back to the menu, to host or join again.
            _menuAfterDisconnect = true;
            _ui.ShowMenu(inGame: false);
            _ui.SetMenuStatus(status ?? "Disconnected.");
        }
        if (_camera == null || !Client.HasCharacter)
        {
            _people.ShowOwnPlayer(false, default, 0);
            _overlay.Refresh(_camera, Array.Empty<(Vector3, string, float, bool)>(), status ?? "Arriving...");
            return;
        }

        Glow.Exposure = _world?.Sky?.Exposure ?? 1;
        _overlay.ShowFps = _settings.ShowFps;
        // The effects' numbers are seen at exposure 1 under a sun of 1.3 (their viewer): the camera's exposure divides them,
        // so a blast or a fire looks as bright by day as by night, like everything that glows.
        FxDirector.Brightness = 1.3f / Glow.Exposure;
        // The map (G) and the death screen draw the zone's map: its picture starts as soon as the zone is known.
        if (!_mapWarm && Client.World().Map != null)
        {
            _mapWarm = _ui.MapReady;
        }
        _model.SeatOrbit = SeatThirdPerson;
        _model.AdminCamera = AdminThirdPerson;
        _model.Frame();
        Num.Vector3 feet = Client.RenderPosition;
        // A respawn or a far teleport on foot is an arrival: the land, plants and things around stream again (§4.3.1).
        Loading?.Follow(feet.X, feet.Y, feet.Z, arrivals: !Client.Ships().Piloting);
        // Up and the eye where the character is drawn: aboard a ship, on its deck as drawn (IRON_WAKE.md §4.8).
        Num.Vector3 up = Client.RenderUp;
        Num.Vector3 eye = Client.RenderEye + up * CameraLift;
        FaceTarget(eye);
        Num.Vector3 forward = Look(_viewFrame, _yaw, _pitch);
        Num.Vector3 bodyForward = forward;
        bool piloting = Client.Ships().Piloting && Client.Ships().OwnShipDef != null;
        uint seatedShip = Client.Ships().SeatShip?.NetId ?? 0;
        int seatedIndex = Client.Ships().SeatIndex;
        if (seatedShip != _cameraSeatShip || seatedIndex != _cameraSeat)
        {
            _cameraSeatShip = seatedShip;
            _cameraSeat = seatedIndex;
            _seatOrbit.Dragging = false;
            _seatOrbitFacing = piloting ? OwnShipShown().Forward : forward;
            _shipCamera.Reset();
        }
        _ships.Cockpit = piloting && !SeatThirdPerson;
        if (AdminThirdPerson)
        {
            (eye, forward) = PlaceAdminCamera(up, bodyForward);
        }
        else if (SeatThirdPerson)
        {
            (Num.Vector3 from, Num.Vector3 towards, Num.Vector3 camUp) = _seatOrbit.Follow(Client.RenderEye, up, _seatOrbitFacing);
            float clearance = OverGround(from);
            if (clearance < 0.3f) from += up * (0.3f - clearance);
            eye = from;
            forward = Num.Vector3.Normalize(Client.RenderEye - from);
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(forward), ToGodot(camUp)), ToGodot(eye));
            _camera.Fov = FieldOfView;
        }
        else if (piloting)
        {
            // The ship's camera (cockpit or chase, V) on the predicted ship as drawn now; blasts shake it too.
            ShipState shown = OwnShipShown();
            (Num.Vector3 from, Num.Vector3 towards, Num.Vector3 camUp) = _shipCamera.Follow(shown, Client.Ships().OwnShipDef!,
                Client.Frame.UpAt(shown.Position), (float)delta, OverGround);
            eye = from;
            forward = towards;
            Vector3 jolt = FxDirector.CameraShake(ToGodot(eye));
            // In the cockpit, free look turns the pilot's head about the seat (the stick keeps the mouse otherwise).
            Basis head = Basis.FromEuler(new Vector3(_freeLook.Pitch, _freeLook.Yaw, 0));
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(towards), ToGodot(camUp)) * head * Basis.FromEuler(jolt), ToGodot(eye));
            _camera.Fov = _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit ? 74 : 68;
            _ships.Cockpit = _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit;
        }
        else
        {
            // The camera carries the recoil's kick on top of the view (the view itself goes to the server clean), and
            // zooms by the sight's magnification while aimed.
            (float kickYaw, float kickPitch) = gun.ViewKick;
            float aimPitch = Math.Clamp(_pitch + kickPitch, -MaxPitch, MaxPitch);
            Basis aim = Basis.LookingAt(ToGodot(Look(_viewFrame, _yaw + kickYaw, aimPitch)), ToGodot(up));
            // Free look turns the camera from the aim; the arms belong to the body and stay on the aim (§4.8).
            Basis view = Basis.LookingAt(ToGodot(Look(_viewFrame, _yaw + kickYaw + _freeLook.Yaw,
                Math.Clamp(aimPitch + _freeLook.Pitch, -MaxPitch, MaxPitch))), ToGodot(up));
            _firstPerson.ArmsTurn = view.Inverse() * aim;
            // Blasts shake the camera (the effects library sums them by distance), never the view the player controls.
            Vector3 shake = FxDirector.CameraShake(ToGodot(eye));
            _camera.GlobalTransform = new Transform3D(view * Basis.FromEuler(shake), ToGodot(eye));
            float zoom = gun.Aimed ? Math.Max(1, gun.Zoom) : 1;
            if (StationZoom() is { } sight)
            {
                zoom = sight; // a gunner's zoom (IW3: the battery's, the right mouse held)
            }
            float fov = 2 * MathF.Atan(MathF.Tan(Mathf.DegToRad(FieldOfView) / 2) / zoom);
            _camera.Fov = Mathf.MoveToward(_camera.Fov, Mathf.RadToDeg(fov), (float)delta * 240);
        }
        _model.Crosshair = new CrosshairView(gun.Aimed, gun.Weapon != null ? gun.Spread : 0, _camera.Fov,
            Hidden: piloting || Client.Stations().AtStation || AdminThirdPerson);
        _overlay.ReloadProgress = gun.Reloading ? gun.ReloadProgress : null;
        _overlay.WoundedSeconds = Client.Local.IsWounded ? Math.Max(0, Client.Local.Life.WoundedUntilSeconds - Client.ClockSeconds) : null;

        if (Client.Physics != null)
        {
            _collision.Follow(Client.Physics, feet);
            Time("collision", ref t0);
        }
        _nature.Refresh(feet, delta);
        Time("nature", ref t0);
        _things.Refresh(Client.ClockSeconds);
        Time("things", ref t0);
        _people.Refresh(delta);
        _animals.Refresh(delta);
        Time("people", ref t0);
        _arrows.Refresh();
        // What the interact key acts on (GROUND_SYSTEMS.md §5: the one query, the options from the same rules as the
        // server's), and its prompt; the building's lines say what it is (a door, the tool cupboard).
        LocalPlayer me = Client.Local;
        bool alive = !me.IsDead;
        _model.Aim(eye, forward, alive && !me.IsWounded && !Client.Ships().Seated && !AdminThirdPerson);
        _developer.Aim(AdminThirdPerson ? Client.RenderEye : eye, AdminThirdPerson ? bodyForward : forward);
        _devDraw.Refresh(feet);
        if (_keypad.Visible || piloting)
        {
            _model.Prompt = null;
            _model.TargetHealth = null;
        }
        _build.Aimed = _model.AimedTarget;
        // Where what is held would be placed, and its ghost (GROUND_SYSTEMS.md §6): the held item's control aims it.
        _placing.Ghost.Assets ??= _build.Assets;
        _placing.BeginFrame();
        if (!AdminThirdPerson) Holding()?.Aim(_hands, eye, forward);
        _placing.EndFrame();
        Time("placing", ref t0);
        _build.Refresh(delta, eye, forward);
        // The lines under the crosshair give way to the keypad.
        _build.Hud.Visible = !_keypad.Visible && !AdminThirdPerson;
        Time("building", ref t0);
        _raid.Refresh(delta);
        Time("raid", ref t0);
        _ships.Refresh(delta, _world?.Sky?.Exposure ?? 1);
        _people.ShowOwnPlayer(SeatThirdPerson || AdminThirdPerson, bodyForward, _pitch);
        FillShipHud(piloting);
        FillStationSight(eye);
        Time("ships", ref t0);
        _fires.Refresh(delta);
        Time("fires", ref t0);
        _audio?.Refresh(delta, input.Sprint);
        Time("audio", ref t0);
        _firstPerson.ShowBody = !AdminThirdPerson;
        _firstPerson.Refresh(delta, input, _yaw, _pitch, !Blocked && !_freeLook.Active && !AdminThirdPerson);
        Holding()?.Arms(_hands, delta, input);
        Time("first person", ref t0);
        _overlay.Refresh(_camera, _people.Names(ToGodot(eye)), status);
        Time("overlay", ref t0);
    }

    /// <summary>Milliseconds spent in each part of the frame since the last reset (for --measure).</summary>
    public System.Collections.Generic.Dictionary<string, double> Timings { get; } = new();

    /// <summary>The slowest time of each part since the last reset (for --measure).</summary>
    public System.Collections.Generic.Dictionary<string, double> Slowest { get; } = new();

    /// <summary>Bytes allocated on the main thread by each part since the last reset (for --measure).</summary>
    public System.Collections.Generic.Dictionary<string, long> Allocations { get; } = new();

    private long _allocatedBefore;

    private void Time(string part, ref long since)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = (now - since) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Timings[part] = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(Timings, part) + ms;
        if (ms > System.Collections.Generic.CollectionExtensions.GetValueOrDefault(Slowest, part))
        {
            Slowest[part] = ms;
        }
        since = now;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        if (part != "client" || _allocatedBefore != 0)
        {
            Allocations[part] = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(Allocations, part) + allocated - _allocatedBefore;
        }
        _allocatedBefore = allocated;
        if (IsolatedCapture && part.StartsWith("open:", StringComparison.Ordinal))
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            HollowUniverse.Common.ProcessCommit.Split commit = HollowUniverse.Common.ProcessCommit.Measure();
            GD.Print("[load-memory] " + System.Text.Json.JsonSerializer.Serialize(new
            {
                stage = part, milliseconds = ms, workingSetMiB = process.WorkingSet64 / 1048576.0,
                privateMiB = commit.PrivateMiB, graphicsCommitMiB = commit.GraphicsMiB, cpuCommitMiB = commit.CpuMiB,
                managedMiB = GC.GetTotalMemory(false) / 1048576.0,
                managedCommittedMiB = GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576.0,
            }));
        }
    }

    // The game has the mouse unless the interface wants it (inventory, crafting, menu, death, chat) or the window is
    // in the background.
    private void UpdateMouse(bool playing)
    {
        if (IsolatedCapture || OS.HasFeature("headless") || Scripted != null)
        {
            return;
        }
        Input.MouseModeEnum wanted = playing && !_ui.WantsCursor && !_keypad.Visible && GetWindow().HasFocus() ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != wanted)
        {
            Input.MouseMode = wanted;
        }
    }

    // The devices this frame: the view (and free look), the movement keys, and the attack and aim buttons as held
    // (what they do with the item in hand is the hands', below).
    private InputFrame ReadInput(double delta)
    {
        if (IsolatedCapture) return new InputFrame { Yaw = _yaw, Pitch = _pitch, ViewFrame = _viewFrame };
        if (!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _seatSecondaryCaptured = false;
            _seatOrbit.Dragging = false;
        }
        if (_ui.WantsCursor || _keypad.Visible || !GetWindow().HasFocus()) _seatOrbit.Dragging = false;
        if (Scripted is { } scripted)
        {
            scripted.Yaw = _yaw;
            scripted.Pitch = _pitch;
            scripted.ViewFrame ??= _viewFrame;
            return scripted;
        }
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch, ViewFrame = _viewFrame, LookYaw = _freeLook.Yaw, LookPitch = _freeLook.Pitch,
            NoclipSpeed = _settings.NoclipSpeed };
        if (Blocked)
        {
            // Loading: the character stands as it arrived.
            return input;
        }
        if (Client.Ships().Piloting)
        {
            if (!_wasPiloting)
            {
                _wasPiloting = true;
                _shipControls.Scheme = HollowUniverse.Ships.ShipControlSchemes.For(Client.Ships().OwnShipDef?.Controls);
                _shipControls.Reset();
                _shipControls.Throttle = Client.Ships().OwnShipCruise.Throttle;
                _shipControls.AssistOff = Client.Ships().OwnShip.Has(ShipFlags.AssistOff);
            }
            // Flying (G6): the stick, the lever and the thrusters instead of walking. With the interface open the lever
            // stays where it was and the stick lets go.
            bool hands = !_ui.WantsCursor && !_keypad.Visible && GetWindow().HasFocus();
            input.Ship = hands ? _shipControls.Read(delta, mouseSteering: !SeatThirdPerson) : new ShipInput
            {
                Throttle = _shipControls.Throttle,
                Buttons = _shipControls.AssistOff ? ShipButtons.AssistOff : ShipButtons.None,
            };
            return input;
        }
        _wasPiloting = false;
        if (Client.Ships().Seated)
        {
            // In a seat that doesn't fly (IRON_WAKE.md §5.2): the view alone; the seat holds the body. At a station (IW3)
            // the left button fires in either camera; the right zooms only in first person (orbit uses it to drag).
            if (Client.Stations().AtStation && !_ui.WantsCursor && !_keypad.Visible && GetWindow().HasFocus() && _ui.Radial == null)
            {
                input.Station.Fire = !_adminCamera.PrimaryCaptured && Input.IsMouseButtonPressed(MouseButton.Left);
                input.Station.FirePressed = (_presses & HandPress.Attack) != 0;
                input.Station.Zoom = !SeatThirdPerson && !_seatSecondaryCaptured && !_adminCamera.SecondaryCaptured && Input.IsMouseButtonPressed(MouseButton.Right);
            }
            return input;
        }
        // Keys move the character unless a screen takes them (the inventory, crafting, chat, the menu, the keypad); a radial
        // doesn't (RustMVP's: the player walks while choosing), though its pointer keeps the buttons.
        if (_ui.TakesKeys || _keypad.Visible || Client.Local.IsDead || !GetWindow().HasFocus())
        {
            return input;
        }
        input.Forward = (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0);
        input.Right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
        input.Sprint = Input.IsPhysicalKeyPressed(Key.Shift);
        input.Crouch = Input.IsPhysicalKeyPressed(Key.Ctrl);
        input.Jump = Input.IsPhysicalKeyPressed(Key.Space);
        input.JumpPressed = _jumpPressed;
        input.Primary = !_ui.WantsCursor && !_adminCamera.PrimaryCaptured && Input.IsMouseButtonPressed(MouseButton.Left);
        input.Secondary = !_ui.WantsCursor && !_adminCamera.SecondaryCaptured && Input.IsMouseButtonPressed(MouseButton.Right);
        return input;
    }

    // ---------------------------------------------------------------- the hands

    private bool AdmireAvailable => !IsolatedCapture && !Blocked && Client.HasCharacter &&
        Client.State == ClientState.Playing && !_ui.WantsCursor && !_ui.TakesKeys && !_keypad.Visible &&
        GetWindow().HasFocus() && !_freeLook.Active && !AdminThirdPerson && !Client.Ships().Seated &&
        (Client.Character.Flags & (MoveFlags.Sprinting | MoveFlags.Swimming)) == 0;

    private bool TryAdmire() => AdmireAvailable && _presses == HandPress.None &&
        !Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsMouseButtonPressed(MouseButton.Right) &&
        !Input.IsPhysicalKeyPressed(Key.Alt) && _firstPerson.Admire();

    public bool Admiring => _firstPerson.Admiring;

    /// <summary>
    /// The hands (Rust's, GROUND_SYSTEMS.md §7): what the item in hand does this frame with the attack button (left), the aim
    /// button (right) and R is its held behaviour's control's to say (<see cref="IHeldControl"/>, found by the item's
    /// <see cref="ItemDef.Held"/>). <paramref name="input"/> comes in with the buttons as held and leaves with what the tick
    /// sends. Free look (Alt), death and the pilot's seat leave the hands still, here alone (<see cref="HandsFree"/>): the
    /// camera turns, nothing is fired, swung, placed, thrown, reloaded or turned (RustMVP: free look never disagrees with
    /// the aim), and no radial of the item opens.
    /// </summary>
    private void Hands(ref InputFrame input, HandPress pressed)
    {
        bool attack = input.Primary, aim = input.Secondary;
        if (input.PrimaryPressed)
        {
            pressed |= HandPress.Attack;
        }
        input.Primary = input.Secondary = input.PrimaryPressed = false;
        HandsFree()?.Hands(_hands, ref input, new HandButtons(attack, aim, pressed));
    }

    // The item in hand's control, the context brought up to date; null when nothing that does anything is held, or the
    // player is dead or flying.
    private IHeldControl? Holding()
    {
        LocalPlayer me = Client.Local;
        if (me.IsDead || me.IsWounded || Client.Ships().Seated || me.HeldItem is not { } item || _held.Find((int)item.Held) is not { } control)
        {
            return null;
        }
        _hands.Item = item;
        _hands.Slot = me.HeldSlot;
        _hands.Yaw = _yaw;
        _hands.Pitch = _pitch;
        return control;
    }

    // The control when the hands may act: not while free looking (the one place free look stops them).
    private IHeldControl? HandsFree() => _freeLook.Active || AdminThirdPerson ? null : Holding();

    // ---------------------------------------------------------------- a gunner's station (IW3, IRON_WAKE.md §6.8)

    // The battery's zoom while the right mouse is held at a station, else null.
    private float? StationZoom() =>
        !IsolatedCapture && !SeatThirdPerson && !_seatSecondaryCaptured && !_adminCamera.SecondaryCaptured && Client.Stations().Own is { } own && !_ui.WantsCursor && Input.IsMouseButtonPressed(MouseButton.Right) ? Math.Max(1, own.Station.Battery.Mount.Zoom) : null;

    // The gunner's sight from the station as drawn (its mount the player's own prediction), and Tab opening the magazine
    // with the inventory.
    private void FillStationSight(Num.Vector3 eye)
    {
        HollowUniverse.Ships.StationSightData d = _stationSight.Data;
        bool inventory = _ui.Screen == GroundScreen.Inventory;
        if (_camera == null || Client.Stations().Own is not { } own || own.Ship.Def is not { Interior: { } interior } def ||
            Client.Stations().Display(own.Ship, own.Station.Index) is not { } st || Client.Ships().DrawnPose(own.Ship.NetId) is not { } pose)
        {
            _inventoryAtStation = false;
            if (d.Show)
            {
                d.Show = false;
                _stationSight.Visible = false;
                _stationSight.Refresh();
            }
            return;
        }
        if (inventory && !_inventoryAtStation)
        {
            Client.Local.OpenMagazine(own.Ship.Id);
        }
        _inventoryAtStation = inventory;
        ShipStationDef s = own.Station;
        BatteryDef battery = s.Battery;
        d.Show = _ui.Screen == GroundScreen.Hud && !SeatThirdPerson;
        _stationSight.Visible = d.Show;
        d.Name = (def.Seats[s.Seat].Name ?? s.Id).ToUpperInvariant();
        d.Zoom = StationZoom() ?? 1;
        // The ring reports physical mount alignment; the trajectory uses the same muzzle convergence as firing.
        float[] parts = new float[interior.Parts.Count];
        own.Ship.PartsInto(interior, parts, (long)Client.RenderTick);
        Client.Stations().OverlayMount(own.Ship.NetId, parts);
        Num.Vector3 barrel = Num.Vector3.Normalize(pose.DirectionToWorld(StationRules.BarrelDirection(interior, s, parts)));
        (Num.Vector3 muzzleLocal, _) = StationRules.BarrelLine(interior, s, 0, parts);
        Num.Vector3 muzzle = pose.ToWorld(muzzleLocal);
        Vector3 far = ToGodot(muzzle + barrel * 2000);
        d.Ring = _camera.IsPositionBehind(far) ? null : _camera.UnprojectPosition(far);
        d.Marks.Clear();
        ushort chosen = st.ChosenItem.Id;
        if (battery.Launch.Of(chosen) is { } profile && Client.Zone is { } zone)
        {
            if (Client.Stations().OwnLaunchLine() is { } launch)
            {
                muzzle = launch.Origin;
                barrel = launch.Direction;
            }
            var motor = Hollow.Simulation.Combat.RocketMotor.Of(battery.Launch, profile);
            Num.Vector3 p = muzzle, v = barrel * profile.Muzzle + pose.VelocityAt(muzzle);
            Num.Vector3 centre = Client.Frame.ToLocal(Vector3D.Zero);
            float g = (float)(zone.SurfaceGravity * 9.80665), flown = 0, burnt = 0;
            int next = 100;
            const float h = Hollow.Simulation.Combat.RocketFlight.StepSeconds;
            for (float t = 0; t < profile.Lifetime && next <= 2000; t += h)
            {
                (Num.Vector3 q, Num.Vector3 w, burnt) = Hollow.Simulation.Combat.RocketFlight.Step(p, v, Num.Vector3.Normalize(centre - p), g, motor, flown, burnt, h);
                flown += (q - p).Length();
                (p, v) = (q, w);
                if (Num.Vector3.Distance(p, muzzle) >= next)
                {
                    Vector3 at = ToGodot(p);
                    if (!_camera.IsPositionBehind(at))
                    {
                        d.Marks.Add((_camera.UnprojectPosition(at), next));
                    }
                    next += 100;
                }
            }
        }
        // Each barrel: loaded (its round), loading (its loader's progress), empty.
        d.Barrels.Clear();
        double tick = Client.Stations().DrawTick(own.Ship.NetId, s.Index);
        foreach (StationBarrel b in s.Barrels)
        {
            LoaderState l = st.Loaders[s.LoaderOf(b.Index)];
            ushort round = st.Chambers[b.Index] != 0 ? st.Chambers[b.Index] : l.Kind;
            string? icon = round != 0 ? Client.Content.Item(round)?.Key : null;
            bool loaded = st.Chambers[b.Index] != 0 && !l.Active;
            d.Barrels.Add(new HollowUniverse.Ships.SightBarrel(loaded, icon ?? st.ChosenItem.Key, StationRules.Progress(battery.Loader, l, tick), l.Active));
        }
        d.Kinds.Clear();
        foreach (ItemDef kind in battery.Ammo)
        {
            d.Kinds.Add(new HollowUniverse.Ships.SightKind(kind.Key, kind.Name, st.Count(kind.Id), kind.Id == chosen));
        }
        // The refusal (§6.7): the player's own press's, for two seconds.
        Hollow.Simulation.Outcome refused = Client.Stations().Refused;
        d.Refusal = !refused.Ok && Client.ClockSeconds - Client.Stations().RefusedAt < 2 ? Client.Content.Texts.Line(refused).ToUpperInvariant() : null;
        // The arcs' dial near a limit (within 15° of one, or past it).
        const float Deg = 180 / MathF.PI;
        float asked = st.Yaw;
        {
            Num.Vector3 local = pose.DirectionToLocal(Look(_viewFrame, _yaw, _pitch));
            asked = StationRules.Angles(interior, s, local).Yaw;
        }
        d.Dial = !s.FullCircle && (st.Limit || st.Yaw - s.YawMin < 15 / Deg || s.YawMax - st.Yaw < 15 / Deg);
        (d.DialMin, d.DialMax, d.DialYaw, d.DialAsked) = (s.YawMin * Deg, s.YawMax * Deg, st.Yaw * Deg, StationRules.ClampYaw(s, asked).Yaw * Deg);
        if (StationRules.ClampYaw(s, asked).Beyond)
        {
            d.DialAsked = asked * Deg;
        }
        // The ship's heading and speed, small at the side.
        Num.Vector3 up = Client.Frame.UpAt(pose.Position);
        (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(up);
        Num.Vector3 nose = pose.DirectionToWorld(Num.Vector3.UnitZ);
        d.Heading = MathF.Atan2(Num.Vector3.Dot(nose, east), Num.Vector3.Dot(nose, north)) * Deg;
        d.Speed = pose.Velocity.Length();
        _ = eye;
        _stationSight.Refresh();
    }

    // ---------------------------------------------------------------- flying (G6, GROUND.md §6.7)

    /// <summary>The ship camera (cockpit or chase), for scripted runs.</summary>
    public HollowUniverse.Ships.ShipCamera ShipCamera => _shipCamera;

    /// <summary>Read-only orbit state for input checks, independent of the seated ship's movement.</summary>
    public (float Yaw, float Pitch, float Distance, bool Dragging) SeatOrbitState =>
        (_seatOrbit.Yaw, _seatOrbit.Pitch, _seatOrbit.Distance, _seatOrbit.Dragging);

    /// <summary>Turns and zooms the seated third-person camera, as the right mouse and the wheel do (scripted runs).</summary>
    public void OrbitSeat(float yaw, float pitch, float zoomSteps)
    {
        _seatOrbit.Drag(yaw, pitch);
        _seatOrbit.Zoom(zoomSteps);
    }

    /// <summary>The ships drawn, for scripted runs.</summary>
    public ShipsView Ships => _ships;

    /// <summary>The pilot's controls (the lever the HUD shows), for scripted runs.</summary>
    public HollowUniverse.Ships.ShipControls ShipControls => _shipControls;

    // A seated player's input (KNOWN_PROBLEMS.md finding 28: the keys in one place, SeatKeys). At the helm the mouse is the
    // stick only in the cockpit (Alt: free look); third person uses the mouse only for RMB orbit. The flight keys are
    // ShipControls' (W S A D, Shift Ctrl, Q E, arrows, B, Z, X, read each frame); in
    // any other seat the mouse looks, as on foot. The seat's own keys come from its data: Space leaves, 1–9 the doors it
    // works, L the lock, V the camera, J the descent; the hands' keys (E, R, Q, the buttons) do nothing.
    private bool SeatInput(InputEvent e)
    {
        bool helm = Client.Ships().Piloting;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } button when SeatThirdPerson:
                _seatOrbit.Dragging = button.Pressed;
                _seatSecondaryCaptured = button.Pressed;
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when SeatThirdPerson:
                _seatOrbit.Zoom(1);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when SeatThirdPerson:
                _seatOrbit.Zoom(-1);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle } when helm:
                _shipControls.CentreSteering();
                return true;
            case InputEventMouseMotion motion when SeatThirdPerson && _seatOrbit.Dragging && Input.MouseMode == Input.MouseModeEnum.Captured:
                _seatOrbit.Drag(-motion.Relative.X * MouseSensitivity, motion.Relative.Y * MouseSensitivity);
                return true;
            case InputEventMouseMotion when SeatThirdPerson:
                return true; // orbit input never steers the ship, turns a gunner's aim or a passenger's body
            case InputEventMouseMotion motion when helm && Input.MouseMode == Input.MouseModeEnum.Captured && _freeLook.Active:
                // Free look in the cockpit: the head turns about the seat, the stick lets go.
                _freeLook.Turn(-motion.Relative.X * MouseSensitivity, -motion.Relative.Y * MouseSensitivity, 0);
                return true;
            case InputEventMouseMotion motion when helm && Input.MouseMode == Input.MouseModeEnum.Captured:
                _shipControls.Mouse(motion.Relative / Math.Max(HollowUniverse.Common.Ui.Scale, 0.5f));
                return true;
            case InputEventMouseMotion:
                return false;
            case InputEventMouseButton:
                return true;
            case InputEventKey { Pressed: true, Echo: false } key:
            {
                if (helm && _shipControls.Key(key))
                {
                    return true;
                }
                if (Client.Ships() is not { SeatShip: { Def: { } def } ship, Seat: { } seat })
                {
                    return false;
                }
                if (SeatKeys.Find(SeatKeys.Of(def, seat), key.PhysicalKeycode) is not { } k)
                {
                    return key.PhysicalKeycode is Key.E or Key.R or Key.Q or Key.F;
                }
                LocalPlayer me = Client.Local;
                switch (k.Action)
                {
                    case SeatAction.Leave:
                        me.LeaveSeat();
                        break;
                    case SeatAction.Door:
                        me.ShipDoor(ship.Id, k.Door);
                        break;
                    case SeatAction.Lock:
                        me.LockShip(ship.Id);
                        break;
                    case SeatAction.Descend:
                        me.Descend();
                        break;
                    case SeatAction.Camera:
                        _shipCamera.Mode = _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Chase
                            ? HollowUniverse.Ships.ShipCameraMode.Cockpit : HollowUniverse.Ships.ShipCameraMode.Chase;
                        _shipControls.CentreSteering();
                        _shipCamera.Reset();
                        _seatOrbit.Dragging = false;
                        _seatSecondaryCaptured |= Input.IsMouseButtonPressed(MouseButton.Right);
                        _seatOrbitFacing = helm ? OwnShipShown().Forward : Look(_viewFrame, _yaw, _pitch);
                        break;
                }
                return true;
            }
        }
        return false;
    }

    // The ship flown as drawn this frame: its predicted state at the drawn position and turn.
    private ShipState OwnShipShown()
    {
        ShipState shown = Client.Ships().OwnShip;
        shown.Position = Client.Ships().OwnShipRenderPosition;
        shown.Rotation = Client.Ships().OwnShipRenderRotation;
        return shown;
    }

    // How far a point (zone frame) is above the planet's ground or sea.
    private float OverGround(Num.Vector3 point)
    {
        if (Client.Surface is not { } surface)
        {
            return float.MaxValue;
        }
        Vector3D body = Client.Frame.ToBody(point);
        double r = body.Length;
        double ground = surface.HeightAt(body / r);
        if (surface.SeaRadius > 0)
        {
            ground = Math.Max(ground, surface.SeaRadius - surface.Radius);
        }
        return (float)(r - surface.Radius - ground);
    }

    // The flight HUD in place of the belt, the vitals and the crosshair while piloting (and only over the game, not the
    // inventory or the menu): speed, height and climb, heading, the lever, fuel and hull, warnings, the keys.
    private void FillShipHud(bool piloting)
    {
        bool show = piloting && _ui.Screen == GroundScreen.Hud;
        _shipHud.Visible = show;
        if (!show)
        {
            return;
        }
        HollowUniverse.Ships.ShipHudData d = _shipHud.Data;
        ShipDef def = Client.Ships().OwnShipDef!;
        ShipState s = OwnShipShown();
        d.Def = def;
        d.State = s;
        Num.Vector3 up = Client.Frame.UpAt(s.Position);
        d.Speed = s.Velocity.Length();
        d.VerticalSpeed = Num.Vector3.Dot(s.Velocity, up);
        d.Height = (float)(Client.Frame.ToBody(s.Position).Length - (Client.Surface?.Radius ?? 0));
        d.Ceiling = def.CeilingOn(Client.Surface?.Radius ?? 0);
        d.OverGround = OverGround(s.Origin(def.CentreOfMass));
        // The heading from local north (docs/WHOLE_WORLD.md §5); within a metre of a pole there is none (north there).
        (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(up);
        Num.Vector3 f = s.Forward;
        d.Heading = MathF.Atan2(Num.Vector3.Dot(f, east), Num.Vector3.Dot(f, north)) * 180 / MathF.PI;
        d.Throttle = _shipControls.Throttle;
        d.Stick = _shipControls.Stick;
        d.AssistOff = _shipControls.AssistOff;
        d.TimeToGround = d.VerticalSpeed < -0.5f ? d.OverGround / -d.VerticalSpeed : float.PositiveInfinity;
        // The helm's keys from its seat's data (SeatKeys): the seat's line at rest (or in orbit, the descent), the flight
        // keys in the corner.
        string seatLine = SeatKeys.Line(SeatKeys.Of(def, def.Helm), inOrbit: false, includeCamera: false, includeLeave: false);
        d.Prompt = s.Has(ShipFlags.InOrbit) ? "J  RESUME FLIGHT" : null;
        d.Notice = null;
        d.Flies = _shipControls.Scheme.Flies;
        d.Gauge = _shipControls.Scheme.Gauge;
        d.Hints = _shipControls.Scheme.Hints(_shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit) +
            (seatLine.Length > 0 ? " · " + seatLine : "");
        _shipHud.Refresh();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (IsolatedCapture) return;
        if (_ui.WantsCursor || _ui.TakesKeys || _keypad.Visible || Blocked || !GetWindow().HasFocus())
        {
            return;
        }
        if (Client.Ships().Seated && SeatInput(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (AdminCameraInput(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (ConsoleShortcut(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
            {
                // Magnification changes projection, never the player's configured angular mouse sensitivity.
                float sensitivity = MouseSensitivity;
                float yaw = -motion.Relative.X * sensitivity, pitch = -motion.Relative.Y * sensitivity;
                if (_freeLook.Active)
                {
                    // Free look: the camera alone turns; the view, the aim and the recoil's countersteer never see it.
                    _freeLook.Turn(yaw, pitch, _pitch);
                }
                else
                {
                    // The recoil's countersteer counts the view's change: the pitch as it moved within its clamp (RustMVP).
                    float pitchBefore = _pitch;
                    Yaw = _yaw + yaw;
                    Pitch = _pitch + pitch;
                    _lookDYaw += yaw;
                    _lookDPitch += _pitch - pitchBefore;
                }
                GetViewport().SetInputAsHandled();
                break;
            }
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                _presses |= HandPress.Attack;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                _presses |= HandPress.Aim;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Space:
                        _jumpPressed = true;
                        break;
                    case Key.R:
                        _presses |= HandPress.Reload;
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.F3:
                        _overlay.ShowNetGraph = !_overlay.ShowNetGraph;
                        GetViewport().SetInputAsHandled();
                        break;
                }
                break;
        }
    }

    // Menus/text fields and seat controls get first refusal. Shortcuts dispatch through the ordinary console;
    // the server alone grants noclip, and only its acknowledged state enables the default flight-speed wheel.
    private bool ConsoleShortcut(InputEvent e)
    {
        string? key = null;
        InputEventWithModifiers? modifiers = null;
        int wheel = 0;
        if (e is InputEventKey { Pressed: true, Echo: false } press)
        {
            string name = press.PhysicalKeycode.ToString();
            if (name.Length == 4 && name.StartsWith("Key", StringComparison.Ordinal) && char.IsDigit(name[3])) name = name[3..];
            key = name;
            modifiers = press;
        }
        else if (e is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            wheel = mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
            key = wheel > 0 ? "MWHEELUP" : "MWHEELDOWN";
            modifiers = mouse;
        }
        if (key == null || modifiers == null) return false;
        string chord = (modifiers.CtrlPressed && key != "Ctrl" ? "CTRL+" : "") +
            (modifiers.AltPressed && key != "Alt" ? "ALT+" : "") +
            (modifiers.ShiftPressed && key != "Shift" ? "SHIFT+" : "") + key;
        if (_console.RunBinding(chord)) return true;
        if (wheel != 0 && !Client.Ships().Seated && Client.HasCharacter && Client.Character.Has(MoveCheats.Noclip))
        {
            _console.AdjustNoclipSpeed(wheel);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A press of the attack button (or, <paramref name="secondary"/>, the aim button) for the item in hand, as if
    /// clicked: the hands act on it this frame (scripted runs use it).
    /// </summary>
    public void UseHeld(bool secondary = false) => _presses |= secondary ? HandPress.Aim : HandPress.Attack;

    // Chat lines starting with '/' are console commands (GROUND.md §8.2): "/give rifle" runs as "give rifle" does in
    // the F1 console, with the player's own rights; the answer's first line shows on the HUD.
    private void Command(string line) => _console.SubmitFromChat(line);

    /// <summary>Where this game is connected (the F1 panel's INFO tab).</summary>
    public string ServerAddress { get; set; } = "";

    /// <summary>The F1 panel's model (scripted runs type into it).</summary>
    public ClientConsole Console => _console;

    /// <summary>The F2 panel's model (scripted runs).</summary>
    public ClientDeveloper Developer => _developer;

    // The zone's clock as the server keeps it (an admin's time or timescale): the sky turns with it.
    private void ApplyDay(ZoneDay day) => _world?.Sky?.Ephemeris.SetSpin(day.BaseTime, day.BaseTurns, day.Scale);

    // Turns toward the player named by --face once they are in view (for screenshots), looking a little down at them.
    private void FaceTarget(Num.Vector3 eye)
    {
        if (FaceName == null || _faced)
        {
            return;
        }
        RemoteEntity? target = Client.Entities.FirstOrDefault(e => string.Equals(e.Name, FaceName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return;
        }
        (_yaw, _pitch) = Heading(_viewFrame, Num.Vector3.Normalize(target.Position + target.Up * 1.2f - eye));
        _faced = _world?.IsReady ?? true;
    }

    // ---------------------------------------------------------------- the view's frame (IRON_WAKE.md §4.8)

    // A heading and pitch read in a frame, as a direction in the world: the zone's where the player stands, or a ship's
    // as it is drawn now.
    private Num.Vector3 Look(uint frame, float yaw, float pitch)
    {
        if (frame != 0 && Client.Ships().DrawnPose(frame) is { } ship)
        {
            return Num.Vector3.Normalize(ship.DirectionToWorld(CharacterMotor.ViewDirection(yaw, pitch, Num.Vector3.UnitY)));
        }
        return CharacterMotor.ViewDirection(yaw, pitch, Client.Frame.UpAt(Client.RenderPosition));
    }

    // A direction in the world as a heading and pitch read in a frame.
    private (float Yaw, float Pitch) Heading(uint frame, Num.Vector3 world)
    {
        Num.Vector3 up = Client.Frame.UpAt(Client.RenderPosition);
        if (frame != 0 && Client.Ships().DrawnPose(frame) is { } ship)
        {
            world = ship.DirectionToLocal(world);
            up = Num.Vector3.UnitY;
        }
        return (Facing.YawOf(world, up), Math.Clamp(Facing.PitchOf(world, up), -MaxPitch, MaxPitch));
    }

    // Boarded or left a ship: the view's heading and pitch turned into the character's new frame, the camera looking on
    // the same way in the world.
    private void FollowFrame()
    {
        // A gunner's aim is held in the world (IRON_WAKE.md §6.2): their view is the zone's, whatever the ship does.
        uint now = Client.Stations().AtStation ? 0 : Client.OwnFrame;
        if (!_viewRestored || now == _viewFrame)
        {
            return;
        }
        (_yaw, _pitch) = Heading(now, Look(_viewFrame, _yaw, _pitch));
        _viewFrame = now;
    }

    public static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
}
