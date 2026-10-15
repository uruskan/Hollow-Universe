using System;
using System.Linq;
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
/// The ground game in first person (GROUND.md §4.3), one whole on the first world (the stand-in world until S4,
/// docs/WHOLE_WORLD.md §4): the surface view draws the world
/// (terrain, sea, sky at the universe time, plants, grass) in the zone's own frame, so the server's collision and the
/// drawn ground are the same triangles; the natural objects as the game changed them; deployables, drops and arrows;
/// the other players as characters; the player's arms and tools; and the RustMVP interface over
/// <see cref="ClientGroundUi"/>. Mouse look is applied every frame; the camera sits at the predicted eye, blended
/// between ticks. Zone-frame metres are Godot's coordinates (+X east, +Y up at the zone's origin, +Z south).
/// </summary>
public partial class GroundView : Node3D
{
    /// <summary>Radians of view per mouse count at sensitivity 1 (RustMVP's default); the console's sensitivity scales it.</summary>
    private const float MouseRadians = 0.002f;

    private float MouseSensitivity => MouseRadians * _settings.Sensitivity;

    private const float MaxPitch = 85f * Mathf.Pi / 180f;

    private SurfaceView? _world;
    private Camera3D? _camera;
    private GroundTerrain? _flat;
    private Godot.Environment? _flatSky;
    private DirectionalLight3D? _flatSun;
    private GroundCollision _collision = null!;
    private NatureView _nature = null!;
    private ThingsView _things = null!;
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
    private readonly HollowUniverse.Ships.ShipControls _shipControls = new();
    private readonly HollowUniverse.Ships.ShipCamera _shipCamera = new();
    private bool _wasPiloting;
    private RenderWarmup _warmup = null!;
    private System.Collections.Generic.IReadOnlyList<LoadStage> _worldStages = Array.Empty<LoadStage>();
    private bool _greeted;

    // What the item in hand does (GROUND_SYSTEMS.md §7): a control per held behaviour, and what they see.
    private readonly Hollow.Simulation.Registry<IHeldControl> _held = HeldControls.Create();
    private HeldContext _hands = null!;
    private Keypad _keypad = null!;
    private Func<string, uint>? _keypadSends;
    private uint _codeSeq;
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

    /// <summary>Playing, the world drawn around the player, and the player to face (if any) in view.</summary>
    public bool ReadyForShot =>
        Client.State == ClientState.Playing && Client.HasCharacter && (_world?.IsReady ?? _flat?.IsSettled ?? false) &&
        !LoadingShown && (FaceName == null || _faced);

    public override void _Ready()
    {
        // The world's view (GROUND.md §4.6) exists from the start, so its loading stages are known; it opens once the
        // server says where the zone is. The renderer's warm-up starts loading every file the game can draw now.
        LoadingPlan plan = Loading?.Plan ?? LoadingPlan.Empty;
        _world = new SurfaceView { Name = "World", Driven = true };
        AddChild(_world);
        _world.InterfaceVisible = false;
        _warmup = new RenderWarmup(GroundWarmup.Items(Client.Content), plan.Stage(RenderWarmup.Key)) { Name = "Warmup" };
        AddChild(_warmup);
        _worldStages = _world.LoadStages(plan);
        Loading?.Add(_worldStages);
        Loading?.Add(_warmup.Stage());
        Loading?.Add(SteadyFrames.Stage(plan));
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
        AddChild(_ships);
        _shipHud = new HollowUniverse.Ships.ShipHud { Name = "ShipHud", Visible = false };
        layer.AddChild(_shipHud);
        // The effects library, ready before the first shot or blast; without heat haze, which draws black here (§11).
        FxDirector.Install(this);
        FxDirector.HeatHaze = false;
        // Things breaking apart (GROUND.md §9.4): every destruction the server reports, one path.
        AddChild(new GibsView { Name = "Gibs", Client = Client });
        _model = new ClientGroundUi(Client) { ViewYaw = () => _yaw, ViewDirection = () => Look(_viewFrame, _yaw, _pitch), Command = Command, Build = _build, AskCode = ShowKeypad };
        _hands = new HeldContext { Client = Client, Build = _build, Arms = _firstPerson, Placement = _placing, Say = _model.ShowNotice };
        // The radials the held item opens (the aim button, R) and the HUD's ammo come from its control.
        _model.HeldRadial = input => HandsFree() is { } held ? held.Radial(_hands, input) : null;
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
            }
        };
        Client.Local.IntentAnswered += answer =>
        {
            if (answer.Seq == _codeSeq && _codeSeq != 0)
            {
                Sounds.Flat(answer.Done ? "codelock.accept" : "codelock.deny");
                _codeSeq = 0;
            }
        };
        _overlay.ShowNetGraph = ShowNetGraph;
        InventoryBody.Use(Client);
    }

    public override void _ExitTree()
    {
        Client.Console().ZoneDayChanged -= ApplyDay;
        _console.Dispose();
        _model.Dispose();
        InventoryBody.Release();
    }

    // The zone's map picture (G7) is warmed once the zone is known: rendered on a worker thread, or read from the disk.
    private bool _mapWarm;

    // ---------------------------------------------------------------- the world

    // The world opens once the server has said where the zone is: the planet's own surface view in the zone's frame,
    // or on the stand-in ground (tests) the old collision-tile terrain under a plain sky.
    private void OpenWorld()
    {
        if (_camera != null || Client.Zone is not { } zone || Client.Surface == null)
        {
            return;
        }
        double time = TimeOverride ?? UniverseTime.Now;
        long lap = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_world != null && Client.Surface is PlanetSurface && PlanetSurfaces.Find(zone.SystemAddress, zone.BodyPath) is { } found)
        {
            _world.OpenZone(found.System, found.Body, found.Surface, Client.Frame.Origin, time, zone.RotationTimeScale);
            if (Client.Console().Day is { } day)
            {
                ApplyDay(day);
            }
            _camera = _world.Camera;
            _nature.World = _world;
        }
        else
        {
            // The stand-in's flat ground (tests): no surface view, nor its stages, to wait for.
            if (_world != null)
            {
                foreach (LoadStage stage in _worldStages)
                {
                    Loading?.Remove(stage);
                }
                _world.QueueFree();
                _world = null;
            }
            _flat = new GroundTerrain { Name = "Terrain" };
            AddChild(_flat);
            _camera = new Camera3D { Name = "Camera", Far = 4000 };
            AddChild(_camera);
            // A plain sky and sun, turned every frame to the up where the player is (TurnFlatSky): S3c draws the
            // world's own (docs/WHOLE_WORLD.md §10.2).
            _flatSky = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            };
            AddChild(new WorldEnvironment { Environment = _flatSky });
            _flatSun = new DirectionalLight3D { Name = "Sun", ShadowEnabled = true };
            AddChild(_flatSun);
        }
        Time("open: the world view", ref lap);
        _camera.Fov = FieldOfView;
        _camera.Near = 0.05f;
        _camera.Current = true;
        _firstPerson.Attach(_camera);
        _things.Muzzle = _firstPerson.Muzzle;
        _things.TheirMuzzle = _people.MuzzleOf;
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

    // The stand-in's sky and sun on the up where the player stands (the world's +Y is its north pole, so a sky left on it
    // leans everywhere but there): a morning sun to the south-east, 32° up, as the walk-in sandbox has it.
    private void TurnFlatSky(Num.Vector3 feet)
    {
        if (_flatSky == null || _flatSun == null)
        {
            return;
        }
        Num.Vector3 up = Client.Frame.UpAt(feet);
        Num.Quaternion tilt = Facing.Turn(Num.Vector3.UnitY, up);
        _flatSky.SkyRotation = new Basis(new Quaternion(tilt.X, tilt.Y, tilt.Z, tilt.W)).GetEuler();
        (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(up);
        float elevation = 32 * MathF.PI / 180, bearing = 125 * MathF.PI / 180;
        Num.Vector3 sun = up * MathF.Sin(elevation) + (north * MathF.Cos(bearing) + east * MathF.Sin(bearing)) * MathF.Cos(elevation);
        _flatSun.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(-sun), ToGodot(up)), Vector3.Zero);
    }

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
        }
        if (playing)
        {
            OpenWorld();
        }
        UpdateMouse(playing && !Blocked);
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
        _freeLook.Hold(Input.IsPhysicalKeyPressed(Key.Alt) && Input.MouseMode == Input.MouseModeEnum.Captured &&
            (!Client.Ships().Piloting || _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit));
        InputFrame input = ReadInput(delta);
        Hands(ref input, _presses);
        _presses = HandPress.None;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Client.Update(delta, input);
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
        _model.Frame();
        Num.Vector3 feet = Client.RenderPosition;
        // A respawn or a far teleport on foot is an arrival: the land, plants and things around stream again (§4.3.1).
        Loading?.Follow(feet.X, feet.Y, feet.Z, arrivals: !Client.Ships().Piloting);
        // Up and the eye where the character is drawn: aboard a ship, on its deck as drawn (IRON_WAKE.md §4.8).
        Num.Vector3 up = Client.RenderUp;
        Num.Vector3 eye = Client.RenderEye;
        FaceTarget(eye);
        Num.Vector3 forward = Look(_viewFrame, _yaw, _pitch);
        bool piloting = Client.Ships().Piloting && Client.Ships().OwnShipDef != null;
        if (piloting != _wasPiloting)
        {
            // Boarded or stepped out: the lever back to zero, the chase camera placed afresh.
            _wasPiloting = piloting;
            _shipControls.Reset();
            _shipCamera.Reset();
        }
        if (piloting)
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
            float fov = 2 * MathF.Atan(MathF.Tan(Mathf.DegToRad(FieldOfView) / 2) / zoom);
            _camera.Fov = Mathf.MoveToward(_camera.Fov, Mathf.RadToDeg(fov), (float)delta * 240);
        }
        _overlay.Spread = gun.Weapon != null && !gun.Aimed && !Client.Local.IsDead && !piloting ? gun.Spread : null;
        _overlay.Fov = _camera.Fov;
        _overlay.ReloadProgress = gun.Reloading ? gun.ReloadProgress : null;
        _overlay.WoundedSeconds = Client.Local.IsWounded ? Math.Max(0, Client.Local.Life.WoundedUntilSeconds - Client.ClockSeconds) : null;

        if (Client.Physics != null)
        {
            _collision.Follow(Client.Physics, feet);
            Time("collision", ref t0);
            _flat?.Follow(Client.Physics, feet);
            TurnFlatSky(feet);
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
        _model.Aim(eye, forward, alive && !me.IsWounded && !piloting);
        _developer.Aim(eye, forward);
        _devDraw.Refresh(feet);
        if (_keypad.Visible || piloting)
        {
            _model.Prompt = null;
        }
        _build.Aimed = _model.AimedTarget;
        // Where what is held would be placed, and its ghost (GROUND_SYSTEMS.md §6): the held item's control aims it.
        _placing.Ghost.Assets ??= _build.Assets;
        _placing.BeginFrame();
        Holding()?.Aim(_hands, eye, forward);
        _placing.EndFrame();
        Time("placing", ref t0);
        _build.Refresh(delta, eye, forward);
        // The lines under the crosshair give way to the keypad.
        _build.Hud.Visible = !_keypad.Visible;
        Time("building", ref t0);
        _raid.Refresh(delta);
        Time("raid", ref t0);
        _ships.Refresh(delta, _world?.Sky?.Exposure ?? 1);
        FillShipHud(piloting);
        Time("ships", ref t0);
        _fires.Refresh(delta);
        Time("fires", ref t0);
        _audio?.Refresh(delta, input.Sprint);
        Time("audio", ref t0);
        _firstPerson.Refresh(delta, input, _yaw, _pitch);
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
    }

    // The game has the mouse unless the interface wants it (inventory, crafting, menu, death, chat) or the window is
    // in the background.
    private void UpdateMouse(bool playing)
    {
        if (OS.HasFeature("headless") || Scripted != null)
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
        if (Scripted is { } scripted)
        {
            scripted.Yaw = _yaw;
            scripted.Pitch = _pitch;
            scripted.ViewFrame = _viewFrame;
            return scripted;
        }
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch, ViewFrame = _viewFrame, LookYaw = _freeLook.Yaw, LookPitch = _freeLook.Pitch };
        if (Blocked)
        {
            // Loading: the character stands as it arrived.
            return input;
        }
        if (Client.Ships().Piloting)
        {
            // Flying (G6): the stick, the lever and the thrusters instead of walking. With the interface open the lever
            // stays where it was and the stick lets go.
            bool hands = !_ui.WantsCursor && !_keypad.Visible && GetWindow().HasFocus();
            input.Ship = hands ? _shipControls.Read(delta) : new ShipInput
            {
                Throttle = _shipControls.Throttle,
                Buttons = _shipControls.AssistOff ? ShipButtons.AssistOff : ShipButtons.None,
            };
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
        input.Primary = !_ui.WantsCursor && Input.IsMouseButtonPressed(MouseButton.Left);
        input.Secondary = !_ui.WantsCursor && Input.IsMouseButtonPressed(MouseButton.Right);
        return input;
    }

    // ---------------------------------------------------------------- the hands

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
        if (me.IsDead || Client.Ships().Piloting || me.HeldItem is not { } item || _held.Find((int)item.Held) is not { } control)
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
    private IHeldControl? HandsFree() => _freeLook.Active ? null : Holding();

    // ---------------------------------------------------------------- flying (G6, GROUND.md §6.7)

    /// <summary>The ship camera (cockpit or chase), for scripted runs.</summary>
    public HollowUniverse.Ships.ShipCamera ShipCamera => _shipCamera;

    /// <summary>The ships drawn, for scripted runs.</summary>
    public ShipsView Ships => _ships;

    /// <summary>The pilot's controls (the lever the HUD shows), for scripted runs.</summary>
    public HollowUniverse.Ships.ShipControls ShipControls => _shipControls;

    // The pilot's input: the mouse moves the stick; Z flight assist, X the lever to zero, V the camera, F step out, H the
    // hatch, L the lock, J descend from orbit. The rest of the keys (W S A D, Q E, Space C, Shift) are read each frame.
    private bool Pilot(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && _freeLook.Active:
                // Free look in the cockpit: the head turns about the seat, the stick lets go.
                _freeLook.Turn(-motion.Relative.X * MouseSensitivity, -motion.Relative.Y * MouseSensitivity, 0);
                return true;
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                _shipControls.Mouse(motion.Relative / Math.Max(HollowUniverse.Common.Ui.Scale, 0.5f));
                return true;
            case InputEventMouseButton:
                return true;
            case InputEventKey { Pressed: true, Echo: false } key:
            {
                if (_shipControls.Key(key))
                {
                    return true;
                }
                LocalPlayer me = Client.Local;
                EntityId ship = Client.Ships().OwnShipRemote?.Id ?? EntityId.None;
                switch (key.PhysicalKeycode)
                {
                    case Key.V:
                        _shipCamera.Mode = _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Chase
                            ? HollowUniverse.Ships.ShipCameraMode.Cockpit : HollowUniverse.Ships.ShipCameraMode.Chase;
                        _shipCamera.Reset();
                        return true;
                    case Key.F:
                        me.ExitShip();
                        return true;
                    case Key.H:
                        me.ShipHatch(ship);
                        return true;
                    case Key.L:
                        me.LockShip(ship);
                        return true;
                    case Key.J:
                        me.Descend();
                        return true;
                    case Key.E or Key.R or Key.Space:
                        return true;
                }
                return false;
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
        Num.Vector3 f = s.Forward;
        d.Heading = MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI;
        d.Throttle = _shipControls.Throttle;
        d.Stick = _shipControls.Stick;
        d.AssistOff = _shipControls.AssistOff;
        d.TimeToGround = d.VerticalSpeed < -0.5f ? d.OverGround / -d.VerticalSpeed : float.PositiveInfinity;
        d.Prompt = s.Has(ShipFlags.InOrbit) ? "J  DESCEND OVER THE SAME SPOT"
            : s.Has(ShipFlags.Landed) || s.Asleep ? "F  STEP OUT     H  HATCH     L  LOCK" : null;
        d.Notice = null;
        d.Hints = $"V {(_shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit ? "chase" : "cockpit")} camera · Z flight assist · X throttle 0 · Shift boost";
        _shipHud.Refresh();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_ui.WantsCursor || _keypad.Visible || Blocked)
        {
            return;
        }
        if (Client.Ships().Piloting && Pilot(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
            {
                float yaw = -motion.Relative.X * MouseSensitivity, pitch = -motion.Relative.Y * MouseSensitivity;
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
        uint now = Client.OwnFrame;
        if (!_viewRestored || now == _viewFrame)
        {
            return;
        }
        (_yaw, _pitch) = Heading(now, Look(_viewFrame, _yaw, _pitch));
        _viewFrame = now;
    }

    public static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
}
