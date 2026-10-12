using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using HollowUniverse.Common;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The first-person view of the ground game (GROUND.md §4.3). Godot only draws what <see cref="GameClient"/> holds:
/// the camera at the predicted character's eye (blended between ticks, so it is smooth at any frame rate), mouse look
/// applied every frame, other players as mannequins with name labels, and placeholder ground from the collision tiles.
/// Zone-frame metres map straight onto Godot's axes (+X east, +Y up at the zone's origin, +Z south).
/// </summary>
public partial class GroundView : Node3D
{
    /// <summary>Radians of view per mouse count (RustMVP's default).</summary>
    private const float MouseSensitivity = 0.002f;

    private const float MaxPitch = 85f * Mathf.Pi / 180f;

    private readonly Dictionary<uint, Mannequin> _mannequins = new();
    private Camera3D _camera = null!;
    private GroundTerrain _terrain = null!;
    private GroundHud _hud = null!;
    private Node3D _people = null!;
    private float _yaw;
    private float _pitch;
    private bool _viewRestored;
    private bool _jumpPressed;
    private bool _primaryPressed;
    private bool _faced;
    private SurvivalView _survival = null!;
    private SurvivalOverlay _overlay = null!;
    private Interaction? _aimed;
    private Num.Vector3? _placeAt;
    private Demo? _demo;

    public GameClient Client { get; set; } = null!;

    /// <summary>The port of the server this game hosts, if it hosts one.</summary>
    public int? HostedPort { get; set; }

    /// <summary>A player to turn toward once in view (screenshots).</summary>
    public string? FaceName { get; set; }

    public bool ShowNetGraph { get; set; }

    /// <summary>Runs an admin command on the server this game hosts (null when it joined another).</summary>
    public Func<string, bool>? HostCommand { get; set; }

    /// <summary>Sets up a little camp for screenshots (<c>--demo</c>, host only).</summary>
    public bool RunDemo { get; set; }

    public bool ReadyForShot =>
        Client.State == ClientState.Playing && Client.HasCharacter && _terrain.IsSettled &&
        (FaceName == null || _faced) && (_demo == null || _demo.Done);

    public override void _Ready()
    {
        BuildEnvironment();
        _terrain = new GroundTerrain { Name = "Terrain" };
        AddChild(_terrain);
        _people = new Node3D { Name = "People" };
        AddChild(_people);
        _survival = new SurvivalView { Name = "Survival", Client = Client };
        AddChild(_survival);
        _camera = new Camera3D { Name = "Camera", Fov = 75, Near = 0.05f, Far = 4000, Current = true };
        AddChild(_camera);
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        _hud = new GroundHud { Client = Client, HostedPort = HostedPort, ShowNetGraph = ShowNetGraph };
        layer.AddChild(_hud);
        _hud.ChatOpened += () => Input.MouseMode = Input.MouseModeEnum.Visible;
        _hud.ChatClosed += Capture;
        _hud.CommandEntered += Command;
        _overlay = new SurvivalOverlay { Client = Client };
        layer.AddChild(_overlay);
        if (RunDemo && HostCommand != null)
        {
            _demo = new Demo(this);
        }
        Capture();
    }

    private void BuildEnvironment()
    {
        // The first world's air is 2.23 atm: a deep blue sky and hazy distances. Placeholder until the planet package's
        // sky lands.
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.16f, 0.32f, 0.62f),
            SkyHorizonColor = new Color(0.62f, 0.72f, 0.82f),
            GroundBottomColor = new Color(0.20f, 0.22f, 0.20f),
            GroundHorizonColor = new Color(0.55f, 0.62f, 0.66f),
            SunAngleMax = 30,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.55f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            // Haze that thickens with distance and hides where the drawn ground ends (224 m out).
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogDepthBegin = 40,
            FogDepthEnd = 205,
            FogDepthCurve = 1.6f,
            FogLightColor = new Color(0.62f, 0.72f, 0.82f),
            FogAerialPerspective = 0.3f,
            FogSkyAffect = 0.0f,
        };
        AddChild(new WorldEnvironment { Name = "Environment", Environment = environment });
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.5f,
            LightColor = new Color(1.0f, 0.96f, 0.90f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 90,
        };
        sun.RotationDegrees = new Vector3(-38, 140, 0);
        AddChild(sun);
    }

    public override void _Process(double delta)
    {
        bool playing = Client.State == ClientState.Playing;
        if (playing && !_viewRestored)
        {
            _viewRestored = true;
            _yaw = Client.InitialYaw;
            _pitch = Client.InitialPitch;
        }
        InputFrame input = ReadInput();
        Client.Update(delta, input);
        _jumpPressed = false;
        _primaryPressed = false;
        if (!Client.HasCharacter)
        {
            _hud.Refresh(_camera, _mannequins.Values);
            _overlay.Refresh(delta);
            return;
        }

        Num.Vector3 feet = Client.RenderPosition;
        Num.Vector3 up = Client.Frame.UpAt(feet);
        Num.Vector3 eye = feet + up * Client.EyeHeight;
        FaceTarget(eye, up);
        Num.Vector3 forward = CharacterMotor.ViewDirection(_yaw, _pitch, up);
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(ToGodot(forward), ToGodot(up)), ToGodot(eye));

        if (Client.Physics != null)
        {
            _terrain.Follow(Client.Physics, feet);
        }
        UpdateMannequins();
        _hud.Refresh(_camera, _mannequins.Values);

        // What the interact key would act on, and where a held deployable would go.
        _aimed = Client.Local.IsDead ? null : Client.Aim(eye, forward);
        _placeAt = Client.Local.HeldItem?.Deploys != null && !Client.Local.IsDead ? Client.AimGround(eye, forward, Client.Content.PlacementReach) : null;
        _survival.PlacementPoint = _placeAt;
        _survival.PlacementYaw = _yaw;
        _overlay.Prompt = _aimed is { } aimed ? $"E   {aimed.Verb} {aimed.Name}"
            : _placeAt != null ? $"F   Place {Client.Local.HeldItem!.Name}" : "";
        _survival.Refresh(_camera, delta, input.Primary, input.Secondary);
        _overlay.Refresh(delta);
        _demo?.Step(delta);
    }

    private InputFrame ReadInput()
    {
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch };
        // Keys move the character only while the game has the mouse (not while chatting or after Esc).
        if (_hud.IsChatting || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return input;
        }
        input.Forward = (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0);
        input.Right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
        input.Sprint = Input.IsPhysicalKeyPressed(Key.Shift);
        input.Crouch = Input.IsPhysicalKeyPressed(Key.Ctrl);
        input.Jump = Input.IsPhysicalKeyPressed(Key.Space);
        input.JumpPressed = _jumpPressed;
        input.Primary = Input.IsMouseButtonPressed(MouseButton.Left);
        input.Secondary = Input.IsMouseButtonPressed(MouseButton.Right);
        input.PrimaryPressed = _primaryPressed;
        return input;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_hud.IsChatting)
        {
            return;
        }
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw = Mathf.Wrap(_yaw - motion.Relative.X * MouseSensitivity, -Mathf.Pi, Mathf.Pi);
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -MaxPitch, MaxPitch);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true } when Input.MouseMode != Input.MouseModeEnum.Captured:
                Capture();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                _primaryPressed = true;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Space:
                        _jumpPressed = true;
                        break;
                    case >= Key.Key1 and <= Key.Key9:
                        NumberKey((int)(key.PhysicalKeycode - Key.Key1));
                        break;
                    case Key.E when _aimed is { } target:
                        Client.Interact(target);
                        break;
                    case Key.F when _placeAt is { } at && Client.Local.HeldSlot >= 0:
                        Client.Local.Place(ContainerRef.Belt, Client.Local.HeldSlot, at, _yaw);
                        break;
                    case Key.G when Client.Local.HeldSlot >= 0:
                        Client.Local.Drop(ContainerRef.Belt, Client.Local.HeldSlot);
                        break;
                    case Key.H when Client.Local.HeldSlot >= 0:
                        Client.Local.Use(ContainerRef.Belt, Client.Local.HeldSlot);
                        break;
                    case Key.Tab when Client.Local.LootTarget.Value != 0:
                        Client.Local.Close();
                        break;
                    case Key.B when Client.Local.IsDead:
                        Client.Local.Respawn();
                        break;
                    case Key.Enter:
                    case Key.KpEnter:
                        _hud.OpenChat();
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.F3:
                        _hud.ShowNetGraph = !_hud.ShowNetGraph;
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.Escape:
                        Input.MouseMode = Input.MouseModeEnum.Visible;
                        GetViewport().SetInputAsHandled();
                        break;
                }
                break;
        }
    }

    // 1-6 take a belt slot in hand (again: put it away); when dead, 1-9 choose a sleeping bag.
    private void NumberKey(int index)
    {
        LocalPlayer me = Client.Local;
        if (me.IsDead)
        {
            if (index + 1 < me.Life.Options.Count)
            {
                me.Respawn(me.Life.Options[index + 1].Bag);
            }
            return;
        }
        if (index < PlayerInventory.BeltSlots)
        {
            me.HeldSlot = me.HeldSlot == index ? -1 : index;
        }
    }

    // Chat commands for testing until the inventory and crafting screens arrive (GROUND.md section 8).
    private void Command(string line)
    {
        string[] w = line.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0)
        {
            return;
        }
        LocalPlayer me = Client.Local;
        int Arg(int i, int fallback) => w.Length > i && int.TryParse(w[i], out int n) ? n : fallback;
        switch (w[0].ToLowerInvariant())
        {
            case "craft" when w.Length > 1 && Client.Content.Item(w[1]) is { } item:
                me.Craft(item.Id, Arg(2, 1));
                break;
            case "cancel" when me.Crafting.Count > 0:
                me.CancelCraft(me.Crafting[^1].Ticket);
                break;
            case "put" when w.Length > 1:
                me.QuickMove(ContainerRef.Main, Arg(1, 0));
                break;
            case "take" when w.Length > 1:
                me.QuickMove(ContainerRef.Loot, Arg(1, 0));
                break;
            case "belt" when w.Length > 2:
                me.MoveItem(ContainerRef.Main, Arg(1, 0), ContainerRef.Belt, Arg(2, 0));
                break;
            case "wear" when w.Length > 1:
                me.QuickMove(ContainerRef.Main, Arg(1, 0));
                break;
            case "light" when me.LootTarget.Value != 0:
                me.Toggle(me.LootTarget);
                break;
            case "give" when w.Length > 1 && HostCommand != null:
                HostCommand($"give {Client.Name} {w[1]} {Arg(2, 1)}");
                break;
            case "kit" when w.Length > 1 && HostCommand != null:
                HostCommand($"kit {Client.Name} {w[1]}");
                break;
            default:
                GD.Print("commands: /craft <item> [n], /cancel, /put <main slot>, /take <loot slot>, /belt <main slot> <belt slot>, " +
                    "/wear <main slot>, /light, and when hosting /give <item> [n], /kit <loot table>");
                break;
        }
    }

    /// <summary>
    /// <c>--demo</c>: a small camp in front of the player for screenshots: a lit campfire, a box, a sleeping bag, a
    /// stone hatchet in hand, wood dropped beside.
    /// </summary>
    private sealed class Demo(GroundView view)
    {
        private double _time;
        private int _step;

        public bool Done => _step >= 9 && _time > 1.5;

        public void Step(double delta)
        {
            _time += delta;
            GameClient client = view.Client;
            LocalPlayer me = client.Local;
            if (_time < 0.6 || _step >= 9 || me.PendingCount > 0)
            {
                return;
            }
            _time = 0.35;
            Num.Vector3 feet = client.Character.Position;
            Num.Vector3 Ahead(float x, float z) => client.AimGround(feet + new Num.Vector3(x, 3, z), -Num.Vector3.UnitY, 10) ?? feet;
            int Slot(string key) => Enumerable.Range(0, 24).FirstOrDefault(i => me.Inventory.Main[i].Item == client.Content.ItemByKey(key).Id);
            switch (_step++)
            {
                case 0:
                    view._yaw = 0;
                    view._pitch = -0.22f;
                    foreach (string give in new[] { "campfire 1", "small-box 1", "sleeping-bag 1", "wood 300", "stone-hatchet 1", "red-berries 6", "woven-shirt 1" })
                    {
                        view.HostCommand!($"give {client.Name} {give}");
                    }
                    break;
                case 1:
                    me.Place(ContainerRef.Main, Slot("campfire"), Ahead(0f, -2.1f), 0.3f);
                    break;
                case 2:
                    me.Place(ContainerRef.Main, Slot("small-box"), Ahead(-1.3f, -1.5f), -0.4f);
                    break;
                case 3:
                    me.Place(ContainerRef.Main, Slot("sleeping-bag"), Ahead(1.5f, -1.2f), 0f);
                    break;
                case 4:
                    if (client.Things.FirstOrDefault(t => t.Look == client.Content.ItemByKey("campfire").Id) is { } fire)
                    {
                        me.Open(fire.Id);
                    }
                    break;
                case 5:
                    me.MoveItem(ContainerRef.Main, Slot("wood"), ContainerRef.Loot, 0, 40);
                    break;
                case 6:
                    me.Toggle(me.LootTarget);
                    me.Close();
                    break;
                case 7:
                    me.MoveItem(ContainerRef.Main, Slot("stone-hatchet"), ContainerRef.Belt, 2);
                    me.QuickMove(ContainerRef.Main, Slot("woven-shirt"));
                    me.Drop(ContainerRef.Main, Slot("wood"), 60);
                    break;
                case 8:
                    me.HeldSlot = 2;
                    break;
            }
        }
    }

    private void Capture()
    {
        if (!OS.HasFeature("headless"))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
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
        _faced = _terrain.IsSettled;
    }

    private void UpdateMannequins()
    {
        var seen = new HashSet<uint>();
        foreach (RemoteEntity entity in Client.Entities)
        {
            seen.Add(entity.NetId);
            if (!_mannequins.TryGetValue(entity.NetId, out Mannequin? mannequin))
            {
                mannequin = new Mannequin { PlayerName = entity.Name, Name = "Mannequin" + entity.NetId };
                _people.AddChild(mannequin);
                _mannequins.Add(entity.NetId, mannequin);
            }
            Num.Vector3 up = Client.Frame.UpAt(entity.Position);
            mannequin.Show(entity, up);
        }
        foreach (uint gone in _mannequins.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _mannequins[gone].QueueFree();
            _mannequins.Remove(gone);
        }
    }

    public static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
}
