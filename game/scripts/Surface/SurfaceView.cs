using System;
using Godot;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;
using HollowUniverse.Common;

namespace HollowUniverse.Surface;

/// <summary>
/// The surface view (GROUND.md §4.6): stand on or fly over a real planet of the universe, with its terrain out to the
/// horizon, its sea, plants, air, clouds, sun, moon and stars at the universe time. A free camera flies, or walks at
/// eye height (G). Look-dev for the ground game; the first-person game mode lives in scripts/Ground.
/// </summary>
public partial class SurfaceView : Node3D
{
    private const float EyeHeight = 1.7f;
    private const double RebaseDistance = 1500;
    private const string SettingsPath = "user://settings.cfg";

    private Camera3D _camera = null!;
    private Godot.Environment _environment = null!;
    private ShaderMaterial _skyMaterial = null!;
    private DirectionalLight3D _sunLight = null!;
    private DirectionalLight3D _moonLight = null!;
    private TerrainLod _terrain = null!;
    private ShaderMaterial _terrainMaterial = null!;
    private CanvasLayer _uiLayer = null!;
    private SurfaceUi _ui = null!;

    private PlanetSurface? _surface;
    private StarSystem? _system;
    private Body? _planet;
    private AtmosphereModel? _air;
    private SurfaceSky? _sky;
    private RenderFrame? _frame;
    private double _time;
    private double _timeSpeed;
    private double _yaw;
    private double _pitch;
    private bool _walk;
    private double _speed = 30;
    private bool _looking;
    private int _idleFrames;
    private double _cloudDrift;
    private float _frameMs;

    /// <summary>How much faster than real the planet turns (1 = its real day). A client setting (GROUND.md §11).</summary>
    public static double RotationTimeScale { get; private set; } = 1;

    public event Action? BackRequested;

    /// <summary>Debug views of the sky shader (0 = normal), for look-dev.</summary>
    public int DebugSky { get; set; }

    /// <summary>True once the terrain around the camera has finished streaming (for screenshots).</summary>
    public bool IsReady => _frame != null && _idleFrames > 20;

    public PlanetSurface? Surface => _surface;
    public TerrainLod Terrain => _terrain;
    public SurfaceSky? Sky => _sky;

    public override void _Ready()
    {
        var settings = new ConfigFile();
        if (settings.Load(SettingsPath) == Error.Ok)
        {
            RotationTimeScale = Math.Clamp(settings.GetValue("surface", "rotation_time_scale", 1.0).AsDouble(), 0.01, 1000);
        }
        _camera = new Camera3D { Fov = 55, Near = 0.08f, Far = 900_000 };
        AddChild(_camera);
        _sunLight = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 1600,
            DirectionalShadowBlendSplits = true,
            ShadowBias = 0.04f,
            ShadowNormalBias = 1.2f,
            LightAngularDistance = 0.5f,
        };
        AddChild(_sunLight);
        _moonLight = new DirectionalLight3D { Name = "Moon", ShadowEnabled = true, DirectionalShadowMaxDistance = 300, LightSpecular = 0.3f };
        AddChild(_moonLight);
        _terrain = new TerrainLod { Name = "Terrain" };
        AddChild(_terrain);
        _uiLayer = new CanvasLayer();
        AddChild(_uiLayer);
        _ui = new SurfaceUi();
        _uiLayer.AddChild(_ui);
    }

    public void SetActive(bool active)
    {
        Visible = active;
        _uiLayer.Visible = active;
        ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (active)
        {
            _camera.MakeCurrent();
        }
    }

    /// <summary>
    /// Opens a planet at a latitude and longitude (degrees), <paramref name="altitude"/> metres above the ground,
    /// looking along <paramref name="yaw"/> (degrees from north toward east) and <paramref name="pitch"/> (degrees up),
    /// at a universe time (seconds since J2000).
    /// </summary>
    public void Open(StarSystem system, Body planet, PlanetSurface surface, double latitude, double longitude, double altitude,
        double yaw, double pitch, double time)
    {
        _system = system;
        _planet = planet;
        _surface = surface;
        _time = time;
        _yaw = yaw;
        _pitch = pitch;
        _air = AtmosphereModel.For(planet);
        _air.Build();
        _sky = new SurfaceSky(system, planet, _air);
        _sky.Ephemeris.RotationTimeScale = RotationTimeScale;

        Vector3D direction = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        double ground = Math.Max(surface.HeightAt(direction), 0);
        _frame = new RenderFrame(direction * (surface.Radius + ground));
        _walk = altitude <= EyeHeight + 0.01;
        _camera.Position = new Vector3(0, (float)Math.Max(altitude, 0.5), 0);
        ApplyCameraRotation();

        SetupGlobals();
        SetupEnvironment();
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_terrain.gdshader") };
        ShaderMaterial? water = null;
        if (surface.SeaRadius > 0)
        {
            water = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_water.gdshader") };
            water.SetShaderParameter("wave_normals", NoiseTextures.WaveNormals(11));
        }
        _terrain.Setup(surface, _frame, _terrainMaterial, water);
        _idleFrames = 0;
        UpdateWorld(0);
    }

    // ---------------------------------------------------------------- set-up

    private static bool _globals;

    private void SetupGlobals()
    {
        AtmosphereModel air = _air!;
        void Add(string name, RenderingServer.GlobalShaderParameterType type, Variant value)
        {
            if (_globals)
            {
                RenderingServer.GlobalShaderParameterSet(name, value);
            }
            else
            {
                RenderingServer.GlobalShaderParameterAdd(name, type, value);
            }
        }
        var T = RenderingServer.GlobalShaderParameterType.Float;
        var V3 = RenderingServer.GlobalShaderParameterType.Vec3;
        var V4 = RenderingServer.GlobalShaderParameterType.Vec4;
        var V2 = RenderingServer.GlobalShaderParameterType.Vec2;
        var S2 = RenderingServer.GlobalShaderParameterType.Sampler2D;
        Add("hu_planet_radius", T, (float)air.BottomRadius);
        Add("hu_atmosphere_top", T, (float)air.TopRadius);
        Add("hu_camera_altitude", T, 2f);
        Add("hu_camera_up", V3, Vector3.Up);
        Add("hu_camera_position", V3, Vector3.Zero);
        Add("hu_sun_direction", V3, Vector3.Up);
        Add("hu_sun_illuminance", V3, Vector3.One);
        Add("hu_moon_direction", V3, Vector3.Up);
        Add("hu_moon_illuminance", V3, Vector3.Zero);
        Vector3 rayleigh = air.RayleighScattering;
        Add("hu_rayleigh", V4, new Vector4(rayleigh.X, rayleigh.Y, rayleigh.Z, (float)air.RayleighHeight));
        Add("hu_mie", V4, new Vector4((float)air.MieScattering, (float)air.MieExtinction, (float)air.MieHeight, (float)air.MieG));
        Vector3 ozone = air.OzoneAbsorption;
        Add("hu_ozone", V4, new Vector4(ozone.X, ozone.Y, ozone.Z, (float)air.OzoneCentre));
        Add("hu_ozone_half_width", T, (float)air.OzoneHalfWidth);
        Add("hu_transmittance_lut", S2, ImageTexture.CreateFromImage(air.Transmittance!));
        Add("hu_multiscatter_lut", S2, ImageTexture.CreateFromImage(air.MultiScattering!));
        Add("hu_cloud_noise", S2, NoiseTextures.Clouds(7));
        Add("hu_cloud_wind", V2, Vector2.Zero);
        Add("hu_cloud_coverage", T, 0.5f);
        Add("hu_cloud_layer", V2, new Vector2(1600, 2900));
        Add("hu_texture_offset", V3, Vector3.Zero);
        _globals = true;
    }

    private void SetupEnvironment()
    {
        _skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_sky.gdshader") };
        var sky = new Godot.Sky
        {
            SkyMaterial = _skyMaterial,
            ProcessMode = Godot.Sky.ProcessModeEnum.Incremental,
            RadianceSize = Godot.Sky.RadianceSizeEnum.Size128,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            GlowEnabled = true,
            GlowIntensity = 0.35f,
            GlowBloom = 0.02f,
            GlowHdrThreshold = 1.4f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.4f,
        };
        // The camera carries its own environment: the maps' WorldEnvironment nodes stay in the tree.
        _environment = environment;
        _camera.Environment = environment;
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        if (_frame == null)
        {
            return;
        }
        _frameMs = (float)(delta * 1000);
        _time += delta * _timeSpeed;
        Fly((float)delta);
        UpdateWorld(delta);
        _idleFrames = _terrain.IsIdle ? _idleFrames + 1 : 0;
    }

    private void UpdateWorld(double delta)
    {
        RenderFrame frame = _frame!;
        PlanetSurface surface = _surface!;
        // Keep the render origin under the camera.
        Vector3 cam = _camera.Position;
        if (new Vector2(cam.X, cam.Z).Length() > RebaseDistance)
        {
            Vector3D body = frame.ToBody(cam);
            Vector3D dir = body.Normalized;
            Vector3 shift = frame.Move(dir * (surface.Radius + Math.Max(surface.HeightAt(dir), 0)));
            _camera.Position -= shift;
            _terrain.Reanchor();
        }
        Vector3D camera = frame.ToBody(_camera.Position);
        _sky!.Ephemeris.RotationTimeScale = RotationTimeScale;
        float pixelAngle = Mathf.DegToRad(_camera.Fov) / Math.Max(GetViewport().GetVisibleRect().Size.Y, 1);
        _sky.Update(_time, camera, frame, _skyMaterial, _sunLight, _moonLight, pixelAngle);

        double altitude = camera.Length - surface.Radius;
        RenderingServer.GlobalShaderParameterSet("hu_camera_altitude", (float)Math.Max(altitude, 0.05));
        RenderingServer.GlobalShaderParameterSet("hu_camera_up", frame.DirectionToRender(camera.Normalized));
        RenderingServer.GlobalShaderParameterSet("hu_camera_position", _camera.Position);
        RenderingServer.GlobalShaderParameterSet("hu_sun_direction", _sky.SunDirection);
        RenderingServer.GlobalShaderParameterSet("hu_sun_illuminance", _sky.SunIlluminance);
        RenderingServer.GlobalShaderParameterSet("hu_moon_direction", _sky.MoonDirection);
        RenderingServer.GlobalShaderParameterSet("hu_moon_illuminance", _sky.MoonIlluminance * SurfaceSky.MoonLightBoost);
        // Clouds drift with the wind (12 m/s from the west), and stay put on the ground when the origin moves.
        _cloudDrift += delta * Math.Max(Math.Abs(_timeSpeed), 1) * 12;
        Vector3D travelled = frame.DirectionToRenderD(frame.Travelled);
        RenderingServer.GlobalShaderParameterSet("hu_cloud_wind", new Vector2((float)(travelled.X - _cloudDrift), (float)travelled.Z));
        RenderingServer.GlobalShaderParameterSet("hu_texture_offset", frame.WrappedTravel(4096));
        _environment.TonemapExposure = _sky.Exposure;
        _skyMaterial.SetShaderParameter("debug_view", DebugSky < 10 ? DebugSky : 0);
        _terrainMaterial.SetShaderParameter("debug_view", DebugSky >= 10 ? DebugSky - 10 : 0);

        _terrain.UpdateLod(camera);
        _ui.Refresh(_planet!, surface, frame, camera, _sky, _time, _frameMs, _terrain, _walk);
    }

    // ---------------------------------------------------------------- camera

    private void ApplyCameraRotation()
    {
        _camera.Basis = Basis.FromEuler(new Vector3((float)(_pitch * Math.PI / 180), (float)(-_yaw * Math.PI / 180), 0), EulerOrder.Yxz);
    }

    private void Fly(float delta)
    {
        if (_frame == null || _surface == null)
        {
            return;
        }
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) move += -_camera.Basis.Z;
        if (Input.IsKeyPressed(Key.S)) move += _camera.Basis.Z;
        if (Input.IsKeyPressed(Key.A)) move += -_camera.Basis.X;
        if (Input.IsKeyPressed(Key.D)) move += _camera.Basis.X;
        if (Input.IsKeyPressed(Key.E)) move += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) move += Vector3.Down;
        float speed = (float)_speed * (Input.IsKeyPressed(Key.Shift) ? 10 : 1) * (Input.IsKeyPressed(Key.Ctrl) ? 0.1f : 1);
        if (_walk)
        {
            move.Y = 0;
            speed = Input.IsKeyPressed(Key.Shift) ? 9 : 4;
        }
        if (move != Vector3.Zero)
        {
            _camera.Position += move.Normalized() * speed * delta;
        }
        // Never below the ground; on foot, at eye height.
        Vector3D body = _frame.ToBody(_camera.Position);
        Vector3D dir = body.Normalized;
        double ground = Math.Max(_surface.HeightAt(dir), _surface.SeaRadius > 0 ? 0 : double.MinValue);
        double altitude = body.Length - _surface.Radius - ground;
        double target = _walk ? EyeHeight : Math.Max(altitude, 1.0);
        if (Math.Abs(target - altitude) > 1e-4)
        {
            _camera.Position += _frame.DirectionToRender(dir) * (float)(target - altitude);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible)
        {
            return;
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _looking = mb.Pressed;
                Input.MouseMode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                _speed = Math.Min(_speed * 1.25, 20000);
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                _speed = Math.Max(_speed / 1.25, 1);
            }
        }
        else if (e is InputEventMouseMotion motion && _looking)
        {
            _yaw += motion.Relative.X * 0.12;
            _pitch = Math.Clamp(_pitch - motion.Relative.Y * 0.12, -89, 89);
            ApplyCameraRotation();
        }
        else if (e is InputEventKey { Pressed: true } key)
        {
            switch (key.Keycode)
            {
                case Key.G:
                    _walk = !_walk;
                    break;
                case Key.Bracketleft:
                    _time -= 1800;
                    break;
                case Key.Bracketright:
                    _time += 1800;
                    break;
                case Key.T:
                    _timeSpeed = _timeSpeed == 0 ? 600 : 0;
                    break;
                case Key.Escape:
                    BackRequested?.Invoke();
                    break;
            }
        }
    }

    /// <summary>Sets the universe time and whether it runs.</summary>
    public void SetTime(double time, bool paused)
    {
        _time = time;
        _timeSpeed = paused ? 0 : 1;
    }
}
