using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
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
    private bool _faced;

    public GameClient Client { get; set; } = null!;

    /// <summary>The port of the server this game hosts, if it hosts one.</summary>
    public int? HostedPort { get; set; }

    /// <summary>A player to turn toward once in view (screenshots).</summary>
    public string? FaceName { get; set; }

    public bool ShowNetGraph { get; set; }

    public bool ReadyForShot =>
        Client.State == ClientState.Playing && Client.HasCharacter && _terrain.IsSettled &&
        (FaceName == null || _faced);

    public override void _Ready()
    {
        BuildEnvironment();
        _terrain = new GroundTerrain { Name = "Terrain" };
        AddChild(_terrain);
        _people = new Node3D { Name = "People" };
        AddChild(_people);
        _camera = new Camera3D { Name = "Camera", Fov = 75, Near = 0.05f, Far = 4000, Current = true };
        AddChild(_camera);
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        _hud = new GroundHud { Client = Client, HostedPort = HostedPort, ShowNetGraph = ShowNetGraph };
        layer.AddChild(_hud);
        _hud.ChatOpened += () => Input.MouseMode = Input.MouseModeEnum.Visible;
        _hud.ChatClosed += Capture;
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
        Client.Update(delta, ReadInput());
        _jumpPressed = false;
        if (!Client.HasCharacter)
        {
            _hud.Refresh(_camera, _mannequins.Values);
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
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Space:
                        _jumpPressed = true;
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
