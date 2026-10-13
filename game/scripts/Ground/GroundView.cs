using System;
using System.Linq;
using Godot;
using Hollow.Client;
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
/// The ground game in first person (GROUND.md §4.3), one whole on the real planet: the surface view draws the world
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

    /// <summary>Holding the interact key this long on a fire or furnace lights it or puts it out (Rust's "more options").</summary>
    private const double HoldSeconds = 0.4;

    private SurfaceView? _world;
    private Camera3D? _camera;
    private GroundTerrain? _flat;
    private GroundCollision _collision = null!;
    private NatureView _nature = null!;
    private ThingsView _things = null!;
    private PeopleView _people = null!;
    private HollowUniverse.Animals.AnimalsView _animals = null!;
    private FirstPersonView _firstPerson = null!;
    private GroundUi _ui = null!;
    private GroundOverlay _overlay = null!;
    private ClientGroundUi _model = null!;
    private GroundAudio? _audio;
    private BuildView _build = null!;
    private RaidView _raid = null!;
    private ShipsView _ships = null!;
    private HollowUniverse.Ships.ShipHud _shipHud = null!;
    private readonly HollowUniverse.Ships.ShipControls _shipControls = new();
    private readonly HollowUniverse.Ships.ShipCamera _shipCamera = new();
    private bool _wasPiloting;
    private ShipInteraction? _aimedShip;

    // A workbench in reach (G7): E opens its tech tree.
    private RemoteThing? _aimedBench;
    private double _thrownAt = -10;
    private bool _effectsWarmed;
    private Keypad _keypad = null!;
    private OptionsMenu _options = null!;
    private Hollow.Simulation.Building.Door? _keypadDoor;
    private bool _keypadSets;
    private uint _codeSeq;
    private float _lookDYaw, _lookDPitch;

    /// <summary>The camera's field of view when not zoomed (degrees, vertical): the console's fov, 75 by default.</summary>
    public float FieldOfView => _settings.Fov;

    // The view's settings the console sets, and the F1 panel's model (GROUND.md §8.2).
    private ViewSettings _settings = new();
    private ClientConsole _console = null!;
    private float _yaw;
    private float _pitch;
    private bool _viewRestored;
    private bool _jumpPressed;
    private bool _primaryPressed;
    private Interaction? _aimed;
    private Num.Vector3? _placeAt;
    private double _interactHeld = -1;
    private bool _interactDone;
    private bool _faced;
    private bool _menuAfterDisconnect;

    public GameClient Client { get; set; } = null!;

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

    /// <summary>The surface view, once the world is open.</summary>
    public SurfaceView? World => _world;

    /// <summary>The people drawn (scripted runs face them).</summary>
    public PeopleView People => _people;

    /// <summary>The building in the game (scripted runs choose the plan's piece).</summary>
    public BuildView Building => _build;

    /// <summary>Explosives, blasts and rockets drawn (scripted runs wait for a lamp to light).</summary>
    public RaidView Raid => _raid;

    /// <summary>The code lock's keypad (scripted runs show and close it).</summary>
    public Keypad Keypad => _keypad;

    /// <summary>Asks a door's code on the keypad, or a new code from its owner (<paramref name="set"/>).</summary>
    public void ShowKeypad(Hollow.Simulation.Building.Door door, bool set) => OpenKeypad(door, set);

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

    /// <summary>What the interact key would act on now.</summary>
    public Interaction? Aimed => _aimed;

    /// <summary>Scripted input instead of the keyboard (screenshots, checks): attack, aim, walking.</summary>
    public InputFrame? Scripted { get; set; }

    /// <summary>Playing, the world drawn around the player, and the player to face (if any) in view.</summary>
    public bool ReadyForShot =>
        Client.State == ClientState.Playing && Client.HasCharacter && (_world?.IsReady ?? _flat?.IsSettled ?? false) &&
        (FaceName == null || _faced);

    public override void _Ready()
    {
        _collision = new GroundCollision { Name = "GroundCollision" };
        AddChild(_collision);
        _nature = new NatureView { Name = "Nature", Client = Client };
        AddChild(_nature);
        _things = new ThingsView { Name = "Things", Client = Client };
        AddChild(_things);
        _people = new PeopleView { Name = "People", Client = Client };
        AddChild(_people);
        // G7's animals (GROUND.md §6.9): drawn from the client's replicated ones.
        _animals = new HollowUniverse.Animals.AnimalsView { Name = "Animals", Client = Client };
        AddChild(_animals);
        _firstPerson = new FirstPersonView { Name = "FirstPerson", Client = Client };
        AddChild(_firstPerson);
        var buildHud = new HollowUniverse.Build.BuildHud { Name = "BuildHud" };
        _build = new BuildView { Name = "Building", Client = Client, Hud = buildHud };
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
        _model = new ClientGroundUi(Client) { ViewYaw = () => _yaw, Command = Command, Build = _build };
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
        Client.ZoneDayChanged += ApplyDay;
        _build.Say = _model.ShowNotice;
        _ui = new GroundUi { Name = "GroundUi", Model = _model };
        _ui.QuitRequested += () => QuitRequested?.Invoke();
        layer.AddChild(_ui);
        // The code lock's keypad and the held-E options, over the interface.
        _options = new OptionsMenu { Name = "Options" };
        layer.AddChild(_options);
        _keypad = new Keypad { Name = "Keypad" };
        layer.AddChild(_keypad);
        _keypad.Pressed += () => Sounds.Flat("codelock.beep");
        _keypad.Entered += code =>
        {
            if (_keypadDoor is not { } door)
            {
                return;
            }
            if (_keypadSets)
            {
                Client.Local.SetCode(door, code);
            }
            else
            {
                _codeSeq = Client.Local.EnterCode(door, code);
            }
        };
        Client.Local.BuildAnswered += answer =>
        {
            if (answer.Seq == _codeSeq && _codeSeq != 0)
            {
                Sounds.Flat(answer.Done ? "codelock.accept" : "codelock.deny");
                _codeSeq = 0;
            }
        };
        Client.Local.BuildRejected += answer => _model.ShowNotice(answer.Text);
        _overlay.ShowNetGraph = ShowNetGraph;
        InventoryBody.Use(Client);
    }

    public override void _ExitTree()
    {
        Client.ZoneDayChanged -= ApplyDay;
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
        if (Client.Surface is PlanetSurface && PlanetSurfaces.Find(zone.SystemAddress, zone.BodyPath) is { } found)
        {
            _world = new SurfaceView { Name = "World", Driven = true };
            AddChild(_world);
            MoveChild(_world, 0);
            _world.OpenZone(found.System, found.Body, found.Surface, Client.Frame.Origin, time, zone.RotationTimeScale);
            if (Client.Day is { } day)
            {
                ApplyDay(day);
            }
            _camera = _world.Camera;
            _nature.World = _world;
        }
        else
        {
            _flat = new GroundTerrain { Name = "Terrain" };
            AddChild(_flat);
            _camera = new Camera3D { Name = "Camera", Far = 4000 };
            AddChild(_camera);
            AddChild(new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Sky,
                    Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                    AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                },
            });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 140, 0), ShadowEnabled = true });
        }
        _camera.Fov = FieldOfView;
        _camera.Near = 0.05f;
        _camera.Current = true;
        _firstPerson.Attach(_camera);
        _things.Muzzle = _firstPerson.Muzzle;
        _things.TheirMuzzle = _people.MuzzleOf;
        Warmup(_camera);
        _people.MakeSpares();
        // Sound: the director listens at the camera (GROUND.md §4.9); --mute leaves it out.
        if (!Muted)
        {
            _audio = new GroundAudio { Name = "Audio", Client = Client, Camera = _camera, World = _world, Trace = SoundTrace };
            AddChild(_audio);
        }
    }

    // The effects' materials drawn once, tiny, two metres ahead, when the camera is first in place (their pipelines are
    // built the first time they draw, a stall in the middle of a fight otherwise).
    private void WarmEffects()
    {
        if (_effectsWarmed || _camera == null)
        {
            return;
        }
        _effectsWarmed = true;
        Vector3 at = _camera.GlobalTransform * new Vector3(0, 0, -2);
        var tiny = new FxParams { Scale = 0.003f };
        foreach (string key in new[] { "explosion.timed-charge", "impact.bullet.rock", "impact.bullet.wood", "blood.hit", "gather.sparks" })
        {
            FxDirector.Play(key, at, Vector3.Up, tiny);
        }
        FxHandle fire = FxDirector.Start("fire.campfire", at, Vector3.Up, tiny);
        GetTree().CreateTimer(1.5).Timeout += () => FxDirector.Stop(fire, immediate: true);
    }

    // Everything the game may show later, shown once, tiny, while the world opens: each new material makes the
    // renderer build its pipelines the first time it is drawn (a stall of 100 ms or more in the middle of play).
    private void Warmup(Camera3D camera)
    {
        var warm = new Node3D { Name = "Warmup", Position = new Vector3(0, 0, -2), Scale = Vector3.One * 0.004f };
        camera.AddChild(warm);
        warm.AddChild(NatureView.Spot());
        float x = 0;
        foreach (DeployableDef def in Client.Content.Deployables)
        {
            if (GroundModels.Provider.Deployable(def.Key, new Vector3(def.HalfExtents.X, def.HalfExtents.Y, def.HalfExtents.Z)) is { } model)
            {
                model.Position = new Vector3(x += 2, 0, 0);
                warm.AddChild(model);
            }
        }
        foreach (string key in new[] { "stone-node", "metal-node", "sulphur-node" })
        {
            for (int stage = 0; stage < 3; stage++)
            {
                if (GroundModels.Provider.Outcrop(key, stage, 0) is { } rock)
                {
                    rock.Position = new Vector3(x += 3, 0, 0);
                    warm.AddChild(rock);
                }
            }
        }
        if (GroundModels.Provider.Dropped("wood") is { } dropped)
        {
            warm.AddChild(dropped);
        }
        if (GroundModels.Provider.Backpack() is { } bag)
        {
            bag.Position = new Vector3(x += 2, 0, 0);
            warm.AddChild(bag);
        }
        var body = new HollowUniverse.Characters.CharacterView { Name = "Body", Position = new Vector3(x + 3, 0, 0) };
        warm.AddChild(body);
        // Every gib set read now and a piece of each drawn, so a first break never waits on a file or a pipeline.
        HollowUniverse.Gibs.GibDirector.Warm(camera);
        body.Wear(HollowUniverse.Characters.Wardrobe.All);
        body.Apply(HollowUniverse.Characters.CharacterFrame.Standing(Vector3.Zero, Vector3.Up));
        GetTree().CreateTimer(1.5).Timeout += warm.QueueFree;
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        bool playing = Client.State == ClientState.Playing;
        if (playing && !_viewRestored)
        {
            _viewRestored = true;
            _yaw = Client.InitialYaw;
            _pitch = Client.InitialPitch;
        }
        if (playing)
        {
            OpenWorld();
        }
        UpdateMouse(playing);
        // The gun's recoil (G4, GROUND.md §6.4): the mouse's turn this frame goes to the recoil's countersteer, and what
        // the recovering recoil gives back is added to the view the player controls.
        LocalGun gun = Client.Local.Gun;
        gun.ObserveLook(_lookDYaw, _lookDPitch);
        _lookDYaw = _lookDPitch = 0;
        (float giveYaw, float givePitch) = gun.TakeCompensation();
        if (giveYaw != 0 || givePitch != 0)
        {
            Yaw = _yaw + giveYaw;
            Pitch = _pitch + givePitch;
        }
        InputFrame input = ReadInput(delta);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Client.Update(delta, input);
        Time("client", ref t0);
        _jumpPressed = false;
        _primaryPressed = false;
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
        if (!_mapWarm && Client.Map != null)
        {
            _mapWarm = _ui.MapReady;
        }
        _model.Frame();
        Num.Vector3 feet = Client.RenderPosition;
        Num.Vector3 up = Client.Frame.UpAt(feet);
        Num.Vector3 eye = feet + up * Client.EyeHeight;
        FaceTarget(eye, up);
        Num.Vector3 forward = CharacterMotor.ViewDirection(_yaw, _pitch, up);
        bool piloting = Client.Piloting && Client.OwnShipDef != null;
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
            (Num.Vector3 from, Num.Vector3 towards, Num.Vector3 camUp) = _shipCamera.Follow(shown, Client.OwnShipDef!,
                Client.Frame.UpAt(shown.Position), (float)delta, OverGround);
            eye = from;
            forward = towards;
            Vector3 jolt = FxDirector.CameraShake(ToGodot(eye));
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(towards), ToGodot(camUp)) * Basis.FromEuler(jolt), ToGodot(eye));
            _camera.Fov = _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit ? 74 : 68;
            _ships.Cockpit = _shipCamera.Mode == HollowUniverse.Ships.ShipCameraMode.Cockpit;
        }
        else
        {
            // The camera carries the recoil's kick on top of the view (the view itself goes to the server clean), and
            // zooms by the sight's magnification while aimed.
            (float kickYaw, float kickPitch) = gun.ViewKick;
            Num.Vector3 look = CharacterMotor.ViewDirection(_yaw + kickYaw, Math.Clamp(_pitch + kickPitch, -MaxPitch, MaxPitch), up);
            // Blasts shake the camera (the effects library sums them by distance), never the view the player controls.
            Vector3 shake = FxDirector.CameraShake(ToGodot(eye));
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(look), ToGodot(up)) * Basis.FromEuler(shake), ToGodot(eye));
            float zoom = gun.Aimed ? Math.Max(1, gun.Zoom) : 1;
            float fov = 2 * MathF.Atan(MathF.Tan(Mathf.DegToRad(FieldOfView) / 2) / zoom);
            _camera.Fov = Mathf.MoveToward(_camera.Fov, Mathf.RadToDeg(fov), (float)delta * 240);
        }
        WarmEffects();
        _overlay.Spread = gun.Weapon != null && !gun.Aimed && !Client.Local.IsDead && !piloting ? gun.Spread : null;
        _overlay.Fov = _camera.Fov;
        _overlay.ReloadProgress = gun.Reloading ? gun.ReloadProgress : null;
        _overlay.WoundedSeconds = Client.Local.IsWounded ? Math.Max(0, Client.Local.Life.WoundedUntilSeconds - Client.ClockSeconds) : null;

        if (Client.Physics != null)
        {
            _collision.Follow(Client.Physics, feet);
            Time("collision", ref t0);
            _flat?.Follow(Client.Physics, feet);
        }
        _nature.Refresh(feet, delta);
        Time("nature", ref t0);
        _things.Refresh(Client.ClockSeconds);
        Time("things", ref t0);
        _people.Refresh(delta);
        _animals.Refresh(delta);
        Time("people", ref t0);
        _build.Refresh(delta, eye, forward);
        // The lines under the crosshair give way to the keypad and the options.
        _build.Hud.Visible = !_keypad.Visible && !_options.Visible;
        Time("building", ref t0);
        _raid.Refresh(delta);
        Time("raid", ref t0);
        _ships.Refresh(delta, _world?.Sky?.Exposure ?? 1);
        FillShipHud(piloting);
        Time("ships", ref t0);
        _audio?.Refresh(delta, input.Sprint);
        Time("audio", ref t0);

        // What the interact key would act on, and where a held deployable would go.
        LocalPlayer me = Client.Local;
        bool alive = !me.IsDead;
        _aimed = alive && !piloting ? Client.Aim(eye, forward) : null;
        _aimedShip = alive && !piloting ? Client.AimShip(eye, forward) : null;
        _aimedBench = alive && !piloting && _aimed == null && _aimedShip == null ? AimWorkbench(eye, forward) : null;
        _placeAt = alive && me.HeldItem?.Deploys != null ? Client.AimGround(eye, forward, Client.Content.PlacementReach) : null;
        _firstPerson.PlacementPoint = _placeAt;
        _firstPerson.PlacementYaw = _yaw;
        Time("aim", ref t0);
        _firstPerson.Refresh(delta, input, _yaw, _pitch);
        Time("first person", ref t0);
        UpdateInteract(delta);
        _model.Prompt = _aimed is { } aimed
            ? new InteractionPrompt("E", $"{aimed.Verb} {aimed.Name}".ToUpperInvariant(), MoreOptions: IsDevice(aimed))
            : _aimedShip is { } ship
                ? new InteractionPrompt("E", $"{ship.Verb} {ship.Name}".ToUpperInvariant())
                : _aimedBench != null
                    ? new InteractionPrompt("E", "OPEN TECH TREE")
                    : _build.Prompt();
        if (_keypad.Visible || piloting)
        {
            _model.Prompt = null;
        }
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

    // The workbench the view meets within reach, if one with a tech tree (G7, GROUND.md §6.8).
    private RemoteThing? AimWorkbench(Num.Vector3 eye, Num.Vector3 direction)
    {
        float reach = Client.InteractReach, best = float.MaxValue;
        RemoteThing? found = null;
        foreach (RemoteThing thing in Client.Things)
        {
            if (thing.Kind != Hollow.Simulation.World.StaticKind.Deployable || Num.Vector3.DistanceSquared(thing.Position, eye) > (reach + 2) * (reach + 2) ||
                thing.Deployable(Client.Content) is not { Workbench: > 0 } def || Client.Content.World.Tree(def.Workbench) == null)
            {
                continue;
            }
            Num.Vector3 up = Client.Frame.UpAt(thing.Position);
            (Num.Vector3 right, Num.Vector3 front) = Hollow.Simulation.Combat.Geometry.Axes(up, thing.Yaw);
            float t = Hollow.Simulation.Combat.Geometry.RayBox(eye, direction, reach, thing.Position + up * def.HalfExtents.Y,
                def.HalfExtents + new Num.Vector3(0.1f), right, up, front);
            if (t >= 0 && t < best)
            {
                (best, found) = (t, thing);
            }
        }
        return found;
    }

    private bool IsDevice(in Interaction target) =>
        target.Kind == InteractionKind.Open && Client.ThingOf(target.Entity)?.Deployable(Client.Content)?.Device != null;

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

    private InputFrame ReadInput(double delta)
    {
        if (Scripted is { } scripted)
        {
            scripted.Yaw = _yaw;
            scripted.Pitch = _pitch;
            return scripted;
        }
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch };
        if (Client.Piloting)
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
        // Keys move the character only while the game has the mouse (not in the inventory, crafting, chat or menu).
        if (_ui.WantsCursor || _keypad.Visible || Client.Local.IsDead || !GetWindow().HasFocus())
        {
            return input;
        }
        input.Forward = (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0);
        input.Right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
        input.Sprint = Input.IsPhysicalKeyPressed(Key.Shift);
        input.Crouch = Input.IsPhysicalKeyPressed(Key.Ctrl);
        input.Jump = Input.IsPhysicalKeyPressed(Key.Space);
        input.JumpPressed = _jumpPressed;
        ItemDef? held = Client.Local.HeldItem;
        // A building tool builds with the attack button (it never swings).
        bool place = held?.Deploys != null || held?.Building is not (null or Hollow.Simulation.Content.BuildingRole.None) || held?.Throwable != null,
            eat = held?.Consume != null;
        // A deployable in hand is placed, food or medicine used, an explosive thrown, with the attack button (Rust);
        // otherwise it attacks.
        input.Primary = Input.IsMouseButtonPressed(MouseButton.Left) && !place && !eat;
        input.Secondary = Input.IsMouseButtonPressed(MouseButton.Right) && held?.Throwable == null;
        input.PrimaryPressed = _primaryPressed && !place && !eat;
        return input;
    }

    // E: a tap acts on what is aimed at; held on a fire or furnace, it lights it or puts it out.
    private void UpdateInteract(double delta)
    {
        if (_interactHeld < 0)
        {
            return;
        }
        if (Client.Piloting)
        {
            // In the seat E rolls the ship.
            _interactHeld = -1;
            return;
        }
        if (!Input.IsPhysicalKeyPressed(Key.E))
        {
            if (_options.Visible)
            {
                _options.Choose();
            }
            else if (!_interactDone && _aimed is { } tap)
            {
                Client.Interact(tap);
            }
            else if (!_interactDone && _aimedShip is { } ship)
            {
                // A ship: its hatch, the seat, the fuel cap or the cargo (which opens like a box).
                Client.Interact(ship);
            }
            else if (!_interactDone && _aimedBench != null)
            {
                // A workbench: its tech tree (G7).
                _ui.OpenTechTree();
            }
            else if (!_interactDone)
            {
                _build.Interact(OpenKeypad);
            }
            _interactHeld = -1;
            return;
        }
        _interactHeld += delta;
        if (!_interactDone && _interactHeld >= HoldSeconds && _aimed is { } target && IsDevice(target))
        {
            Client.Local.Toggle(target.Entity);
            _interactDone = true;
        }
        else if (!_interactDone && _interactHeld >= HoldSeconds && _aimed == null && _build.Interactable is { } building)
        {
            _options.Offer(_build.Options(building, OpenKeypad));
            _interactDone = true;
        }
    }

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
                EntityId ship = Client.OwnShipRemote?.Id ?? EntityId.None;
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
        ShipState shown = Client.OwnShip;
        shown.Position = Client.OwnShipRenderPosition;
        shown.Rotation = Client.OwnShipRenderRotation;
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
        ShipDef def = Client.OwnShipDef!;
        ShipState s = OwnShipShown();
        d.Def = def;
        d.State = s;
        Num.Vector3 up = Client.Frame.UpAt(s.Position);
        d.Speed = s.Velocity.Length();
        d.VerticalSpeed = Num.Vector3.Dot(s.Velocity, up);
        d.Height = (float)(Client.Frame.ToBody(s.Position).Length - (Client.Surface?.Radius ?? 0));
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

    // The code lock's keypad: a code to open the door, or (its owner) a new one.
    private void OpenKeypad(Hollow.Simulation.Building.Door door, bool set)
    {
        _keypadDoor = door;
        _keypadSets = set;
        _keypad.Ask(set ? "NEW CODE" : "ENTER CODE");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_ui.WantsCursor || _keypad.Visible)
        {
            return;
        }
        if (Client.Piloting && Pilot(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured && _options.Visible:
                // Held E: the mouse picks an option, the view stays.
                _options.Move(motion.Relative.Y);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Yaw = _yaw - motion.Relative.X * MouseSensitivity;
                Pitch = _pitch - motion.Relative.Y * MouseSensitivity;
                _lookDYaw -= motion.Relative.X * MouseSensitivity;
                _lookDPitch -= motion.Relative.Y * MouseSensitivity;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                _primaryPressed = true;
                UseHeld();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when Client.Local.HeldItem?.Throwable != null:
                UseHeld(secondary: true);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Space:
                        _jumpPressed = true;
                        break;
                    case Key.R when _build.Held != Hollow.Simulation.Content.BuildingRole.None:
                        _build.Rotate();
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.R when Client.Local.Gun.Weapon != null && !Client.Local.IsDead:
                        Client.Local.Gun.Reload();
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.E when !Client.Local.IsDead:
                        _interactHeld = 0;
                        _interactDone = false;
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
    /// The attack button with a deployable in hand places it; with food or medicine, uses it; with an explosive, throws
    /// it (Rust). The aim button (<paramref name="secondary"/>) with an explosive puts a charge on the surface aimed at
    /// within reach, or lobs a grenade gently.
    /// </summary>
    public void UseHeld(bool secondary = false)
    {
        LocalPlayer me = Client.Local;
        if (me.IsDead || me.HeldSlot < 0 || me.HeldItem is not { } item || Client.Piloting)
        {
            return;
        }
        if (item.Throwable is { } throwable)
        {
            if (Client.ClockSeconds - _thrownAt < throwable.CooldownSeconds)
            {
                return;
            }
            _thrownAt = Client.ClockSeconds;
            if (secondary && throwable.Placeable)
            {
                me.PlaceCharge(me.HeldSlot, _yaw, _pitch);
            }
            else
            {
                me.Throw(me.HeldSlot, _yaw, _pitch, lob: secondary);
                _firstPerson.Throw(item.Key);
            }
            return;
        }
        if (secondary)
        {
            return;
        }
        if (item.Building != Hollow.Simulation.Content.BuildingRole.None)
        {
            _build.Use();
            return;
        }
        if (item.Deploys != null && _placeAt is { } at)
        {
            me.Place(ContainerRef.Belt, me.HeldSlot, at, _yaw);
        }
        else if (item.Consume != null)
        {
            Sounds.Used(Client.Content, me.Inventory.Belt[me.HeldSlot].Item);
            me.Use(ContainerRef.Belt, me.HeldSlot);
        }
    }

    // Chat lines starting with '/' are console commands (GROUND.md §8.2): "/give rifle" runs as "give rifle" does in
    // the F1 console, with the player's own rights; the answer's first line shows on the HUD.
    private void Command(string line) => _console.SubmitFromChat(line);

    /// <summary>Where this game is connected (the F1 panel's INFO tab).</summary>
    public string ServerAddress { get; set; } = "";

    /// <summary>The F1 panel's model (scripted runs type into it).</summary>
    public ClientConsole Console => _console;

    // The zone's clock as the server keeps it (an admin's time or timescale): the sky turns with it.
    private void ApplyDay(ZoneDay day) => _world?.Sky?.Ephemeris.SetSpin(day.BaseTime, day.BaseTurns, day.Scale);

    // Turns toward the player named by --face once they are in view (for screenshots), looking a little down at them.
    private void FaceTarget(Num.Vector3 eye, Num.Vector3 up)
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
        Num.Vector3 chest = target.Position + up * 1.2f - eye;
        Num.Vector3 flat = chest - up * Num.Vector3.Dot(chest, up);
        _yaw = MathF.Atan2(-flat.X, -flat.Z);
        _pitch = MathF.Atan2(Num.Vector3.Dot(chest, up), flat.Length());
        _faced = _world?.IsReady ?? true;
    }

    public static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
}
