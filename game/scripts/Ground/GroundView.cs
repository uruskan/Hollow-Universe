using System;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Surface;
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
    /// <summary>Radians of view per mouse count (RustMVP's default).</summary>
    private const float MouseSensitivity = 0.002f;

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
    private FirstPersonView _firstPerson = null!;
    private GroundUi _ui = null!;
    private GroundOverlay _overlay = null!;
    private ClientGroundUi _model = null!;
    private GroundAudio? _audio;
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
        _firstPerson = new FirstPersonView { Name = "FirstPerson", Client = Client };
        AddChild(_firstPerson);

        var layer = new CanvasLayer { Name = "Interface" };
        AddChild(layer);
        _overlay = new GroundOverlay { Name = "Overlay", Client = Client };
        layer.AddChild(_overlay);
        _model = new ClientGroundUi(Client) { ViewYaw = () => _yaw, Command = Command };
        _ui = new GroundUi { Name = "GroundUi", Model = _model };
        _ui.QuitRequested += () => QuitRequested?.Invoke();
        layer.AddChild(_ui);
        _overlay.ShowNetGraph = ShowNetGraph;
        InventoryBody.Use(Client);
    }

    public override void _ExitTree()
    {
        _model.Dispose();
        InventoryBody.Release();
    }

    private System.Threading.Tasks.Task<Image>? _map;

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
            _camera = _world.Camera;
            _nature.World = _world;
            _map = ZoneMap.Build(found.Surface, Client.Frame);
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
        _camera.Fov = 75;
        _camera.Near = 0.05f;
        _camera.Current = true;
        _firstPerson.Attach(_camera);
        Warmup(_camera);
        // Sound: the director listens at the camera (GROUND.md §4.9); --mute leaves it out.
        if (!Muted)
        {
            _audio = new GroundAudio { Name = "Audio", Client = Client, Camera = _camera, World = _world, Trace = SoundTrace };
            AddChild(_audio);
        }
    }

    // Everything the game may show later, shown once, tiny, while the world opens: each new material makes the
    // renderer build its pipelines the first time it is drawn (a stall of 100 ms or more in the middle of play).
    private void Warmup(Camera3D camera)
    {
        var warm = new Node3D { Name = "Warmup", Position = new Vector3(0, 0, -2), Scale = Vector3.One * 0.004f };
        camera.AddChild(warm);
        warm.AddChild(NatureView.Spot());
        ThingsView.AddFire(warm, Vector3.Zero, 1);
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
            if (GroundModels.Provider.Outcrop(key, 1, 0) is { } rock)
            {
                rock.Position = new Vector3(x += 3, 0, 0);
                warm.AddChild(rock);
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
        InputFrame input = ReadInput();
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
            _overlay.Refresh(_camera, Array.Empty<(Vector3, string, float)>(), status ?? "Arriving...");
            return;
        }

        Glow.Exposure = _world?.Sky?.Exposure ?? 1;
        if (_map is { IsCompleted: true } map)
        {
            if (map.IsCompletedSuccessfully)
            {
                DeathMap.Use(ImageTexture.CreateFromImage(map.Result));
            }
            _map = null;
        }
        Num.Vector3 feet = Client.RenderPosition;
        Num.Vector3 up = Client.Frame.UpAt(feet);
        Num.Vector3 eye = feet + up * Client.EyeHeight;
        FaceTarget(eye, up);
        Num.Vector3 forward = CharacterMotor.ViewDirection(_yaw, _pitch, up);
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(forward), ToGodot(up)), ToGodot(eye));

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
        Time("people", ref t0);
        _audio?.Refresh(delta, input.Sprint);
        Time("audio", ref t0);

        // What the interact key would act on, and where a held deployable would go.
        LocalPlayer me = Client.Local;
        bool alive = !me.IsDead;
        _aimed = alive ? Client.Aim(eye, forward) : null;
        _placeAt = alive && me.HeldItem?.Deploys != null ? Client.AimGround(eye, forward, Client.Content.PlacementReach) : null;
        _firstPerson.PlacementPoint = _placeAt;
        _firstPerson.PlacementYaw = _yaw;
        Time("aim", ref t0);
        _firstPerson.Refresh(delta, input, _yaw, _pitch);
        Time("first person", ref t0);
        UpdateInteract(delta);
        _model.Prompt = _aimed is { } aimed
            ? new InteractionPrompt("E", $"{aimed.Verb} {aimed.Name}".ToUpperInvariant(), MoreOptions: IsDevice(aimed))
            : null;
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
        Input.MouseModeEnum wanted = playing && !_ui.WantsCursor && GetWindow().HasFocus() ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != wanted)
        {
            Input.MouseMode = wanted;
        }
    }

    private InputFrame ReadInput()
    {
        if (Scripted is { } scripted)
        {
            scripted.Yaw = _yaw;
            scripted.Pitch = _pitch;
            return scripted;
        }
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch };
        // Keys move the character only while the game has the mouse (not in the inventory, crafting, chat or menu).
        if (_ui.WantsCursor || Client.Local.IsDead || !GetWindow().HasFocus())
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
        bool place = held?.Deploys != null, eat = held?.Consume != null;
        // A deployable in hand is placed, food or medicine used, with the attack button (Rust); otherwise it attacks.
        input.Primary = Input.IsMouseButtonPressed(MouseButton.Left) && !place && !eat;
        input.Secondary = Input.IsMouseButtonPressed(MouseButton.Right);
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
        if (!Input.IsPhysicalKeyPressed(Key.E))
        {
            if (!_interactDone && _aimed is { } tap)
            {
                Client.Interact(tap);
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
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_ui.WantsCursor)
        {
            return;
        }
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                Yaw = _yaw - motion.Relative.X * MouseSensitivity;
                Pitch = _pitch - motion.Relative.Y * MouseSensitivity;
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                _primaryPressed = true;
                UseHeld();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Space:
                        _jumpPressed = true;
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

    /// <summary>The attack button with a deployable in hand places it; with food or medicine, uses it (Rust).</summary>
    public void UseHeld()
    {
        LocalPlayer me = Client.Local;
        if (me.IsDead || me.HeldSlot < 0 || me.HeldItem is not { } item)
        {
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

    // Chat lines starting with '/' are test commands (AGENTS.md).
    private void Command(string line)
    {
        string[] w = line.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0)
        {
            return;
        }
        int Arg(int i, int fallback) => w.Length > i && int.TryParse(w[i], out int n) ? n : fallback;
        switch (w[0].ToLowerInvariant())
        {
            case "give" when w.Length > 1 && HostCommand != null:
                HostCommand($"give {Client.Name} {w[1]} {Arg(2, 1)}");
                break;
            case "kit" when w.Length > 1 && HostCommand != null:
                HostCommand($"kit {Client.Name} {w[1]}");
                break;
            case "heal" when HostCommand != null:
                HostCommand($"heal {Client.Name}");
                break;
            case "kill" when HostCommand != null:
                HostCommand($"kill {Client.Name}");
                break;
            default:
                _model.ShowNotice(HostCommand != null ? "Commands: /give <item> [n], /kit <table>, /heal, /kill" : "Test commands work when hosting.");
                break;
        }
    }

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
