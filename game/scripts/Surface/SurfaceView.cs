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
    private Vegetation? _vegetation;
    private GrassField? _grass;
    private TextureLayered _groundAlbedo = null!;
    private TextureLayered _groundNormal = null!;
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
    private bool _timeRuns = true;
    private double _yaw;
    private double _pitch;
    private bool _walk;
    private double _speed = 30;
    private bool _looking;
    private int _idleFrames;
    private double _cloudDrift;
    private float _frameMs;
    private bool _firstFrame = true;

    /// <summary>
    /// How much faster than real the planet turns (1 = its real day): a client setting (GROUND.md §11), read from
    /// settings.cfg ([surface] rotation_time_scale) or --time-scale; T cycles it live through <see cref="Scales"/>.
    /// </summary>
    public static double RotationTimeScale { get; private set; } = 1;

    /// <summary>The scales T steps through.</summary>
    public static readonly double[] Scales = { 1, 60, 600, 3600 };

    public event Action? BackRequested;

    /// <summary>Debug views of the sky shader (0 = normal), for look-dev.</summary>
    public int DebugSky { get; set; }

    /// <summary>True once the terrain, plants and grass around the camera have finished streaming (for screenshots).</summary>
    public bool IsReady => _frame != null && _idleFrames > 20;

    public GrassField? Grass => _grass;

    /// <summary>Shows or hides the interface (H), e.g. for clean screenshots.</summary>
    public bool InterfaceVisible
    {
        get => _uiLayer.Visible;
        set => _uiLayer.Visible = value;
    }

    /// <summary>Cloud cover, 0 (clear) to 1 (overcast).</summary>
    public float CloudCoverage
    {
        get => _cloudCoverage;
        set
        {
            _cloudCoverage = Math.Clamp(value, 0, 1);
            if (_globals)
            {
                RenderingServer.GlobalShaderParameterSet("hu_cloud_coverage", _cloudCoverage);
            }
        }
    }

    private float _cloudCoverage = 0.35f; // fair-weather cumulus

    /// <summary>Look-dev: show one of each plant at a distance and level of detail (see <see cref="Vegetation.Lineup"/>).</summary>
    public void ShowFloraLineup(float distance, int lod)
    {
        if (_vegetation != null)
        {
            _vegetation.Lineup = (distance, lod, -_camera.GlobalBasis.Z);
        }
    }

    public PlanetSurface? Surface => _surface;

    /// <summary>The sun light's colour and energy, for --trace.</summary>
    public string LightDebug => $"sunlight {_sunLight.LightColor} x {_sunLight.LightEnergy:0.000}";
    public TerrainLod Terrain => _terrain;
    public Vegetation? Plants => _vegetation;
    public SurfaceSky? Sky => _sky;

    public override void _Ready()
    {
        var settings = new ConfigFile();
        if (settings.Load(SettingsPath) == Error.Ok)
        {
            RotationTimeScale = Math.Clamp(settings.GetValue("surface", "rotation_time_scale", 1.0).AsDouble(), 0.01, 100000);
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
        _sky.Ephemeris.SetRotationTimeScale(RotationTimeScale, time);

        Vector3D direction = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        double ground = Math.Max(surface.HeightAt(direction), 0);
        _frame = new RenderFrame(direction * (surface.Radius + ground));
        _walk = altitude <= EyeHeight + 0.01;
        _camera.Position = new Vector3(0, (float)Math.Max(altitude, 0.5), 0);
        ApplyCameraRotation();

        SetupGlobals();
        SetupEnvironment();
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_terrain.gdshader") };
        _groundAlbedo = GD.Load<TextureLayered>("res://assets/surface/ground/ground_albedo.webp");
        _groundNormal = GD.Load<TextureLayered>("res://assets/surface/ground/ground_normal.webp");
        _terrainMaterial.SetShaderParameter("albedo_array", _groundAlbedo);
        _terrainMaterial.SetShaderParameter("normal_array", _groundNormal);
        _terrainMaterial.SetShaderParameter("macro_noise", _noise);
        _canopy ??= NoiseTextures.Canopy(13);
        _terrainMaterial.SetShaderParameter("canopy_texture", _canopy);
        ShaderMaterial? water = null;
        if (surface.SeaRadius > 0)
        {
            water = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_water.gdshader") };
            water.SetShaderParameter("wave_normals", NoiseTextures.WaveNormals(11));
        }
        _terrain.Setup(surface, _frame, _terrainMaterial, water);
        _vegetation?.QueueFree();
        _vegetation = new Vegetation { Name = "Vegetation" };
        AddChild(_vegetation);
        _vegetation.Setup(surface, _frame, _groundAlbedo, _groundNormal);
        _grass?.QueueFree();
        _grass = null;
        _idleFrames = 0;
        _firstFrame = true;
        UpdateWorld(0);
    }

    // ---------------------------------------------------------------- set-up

    private static bool _globals;
    private ImageTexture? _noise;
    private ImageTexture? _canopy;
    private AerialTable? _aerial;

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
        _noise ??= NoiseTextures.Clouds(7);
        Add("hu_cloud_noise", S2, _noise);
        Add("hu_cloud_wind", V2, Vector2.Zero);
        Add("hu_cloud_coverage", T, _cloudCoverage);
        Add("hu_cloud_layer", V2, new Vector2(1600, 2900));
        Add("hu_texture_offset", V3, Vector3.Zero);
        Add("hu_wind", V4, new Vector4(0.86f, 0, -0.5f, 1));
        Add("hu_lod_clock", T, 0f);
        var S3 = RenderingServer.GlobalShaderParameterType.Sampler3D;
        _aerial = new AerialTable(air);
        Add("hu_ap_up", V3, Vector3.Up);
        Add("hu_ap_east", V3, Vector3.Right);
        Add("hu_ap_range", T, (float)AtmosphereModel.ApRange);
        _aerial.Update(0, 2, Vector3.Up, Vector3.Right, Vector3.Up, Vector3.One, Vector3.Down, Vector3.Zero, wait: true);
        Add("hu_ap_inscatter", S3, _aerial.Inscatter);
        Add("hu_ap_transmittance", S3, _aerial.Transmittance);
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
            SsaoEnabled = OS.GetEnvironment("HU_NO_SSAO").Length == 0,
            // Gentle: on dense grass and leaf cards strong SSAO turns everything in shade black.
            SsaoRadius = 0.7f,
            SsaoIntensity = 0.6f,
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
        if (_timeRuns)
        {
            _time += delta;
        }
        Fly((float)delta);
        UpdateWorld(delta);
        _idleFrames = _terrain.IsIdle && (_vegetation?.IsIdle ?? true) && (_grass?.IsIdle ?? false) ? _idleFrames + 1 : 0;
    }

    private void UpdateWorld(double delta)
    {
        RenderFrame frame = _frame!;
        PlanetSurface surface = _surface!;
        // Keep the render origin under the camera.
        Vector3 cam = _camera.Position;
        if (!Driven && new Vector2(cam.X, cam.Z).Length() > RebaseDistance)
        {
            Vector3D body = frame.ToBody(cam);
            Vector3D dir = body.Normalized;
            Vector3 shift = frame.Move(dir * (surface.Radius + Math.Max(surface.HeightAt(dir), 0)));
            _camera.Position -= shift;
            _terrain.Reanchor();
            _vegetation?.Reanchor();
        }
        Vector3D camera = frame.ToBody(_camera.Position);
        float pixelAngle = Mathf.DegToRad(_camera.Fov) / Math.Max(GetViewport().GetVisibleRect().Size.Y, 1);
        _sky!.Update(_time, camera, frame, _skyMaterial, _sunLight, _moonLight, pixelAngle);

        double altitude = camera.Length - surface.Radius;
        RenderingServer.GlobalShaderParameterSet("hu_camera_altitude", (float)Math.Max(altitude, 0.05));
        RenderingServer.GlobalShaderParameterSet("hu_camera_up", frame.DirectionToRender(camera.Normalized));
        RenderingServer.GlobalShaderParameterSet("hu_camera_position", _camera.Position);
        RenderingServer.GlobalShaderParameterSet("hu_sun_direction", _sky.SunDirection);
        RenderingServer.GlobalShaderParameterSet("hu_sun_illuminance", _sky.SunIlluminance);
        RenderingServer.GlobalShaderParameterSet("hu_moon_direction", _sky.MoonDirection);
        // The air scatters the moon's real light; only the light on the ground is boosted (nights stay dark, the ground
        // readable).
        RenderingServer.GlobalShaderParameterSet("hu_moon_illuminance", _sky.MoonIlluminance * SurfaceSky.MoonSkyBoost);
        // The table's axes: the camera's up, and east made level there.
        Vector3 apUp = frame.DirectionToRender(camera.Normalized);
        Vector3 apEast = (Vector3.Right - apUp * apUp.Dot(Vector3.Right)).Normalized();
        _aerial!.Update(Time.GetTicksMsec() / 1000.0, Math.Max(altitude, 0.5), apUp, apEast, _sky.SunDirection, _sky.SunIlluminance,
            _sky.MoonDirection, _sky.MoonIlluminance * SurfaceSky.MoonSkyBoost, wait: _firstFrame);
        _firstFrame = false;
        // Clouds drift with the wind (12 m/s from the west), faster when the day is sped up, and stay put on the
        // ground when the origin moves.
        _cloudDrift += (_timeRuns ? delta * RotationTimeScale : 0) * 12;
        Vector3D travelled = frame.DirectionToRenderD(frame.Travelled);
        RenderingServer.GlobalShaderParameterSet("hu_cloud_wind", new Vector2((float)(travelled.X - _cloudDrift), (float)travelled.Z));
        RenderingServer.GlobalShaderParameterSet("hu_texture_offset", frame.WrappedTravel(4096));
        _environment.TonemapExposure = DebugSky == 15 ? 1 : _sky.Exposure;
        if (DebugSky == 15)
        {
            _environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
            _environment.GlowEnabled = false;
            _environment.SsaoEnabled = false;
        }
        _skyMaterial.SetShaderParameter("debug_view", DebugSky < 10 ? DebugSky : 0);
        _terrainMaterial.SetShaderParameter("debug_view", DebugSky >= 10 ? DebugSky - 10 : 0);

        // A breeze from the west-south-west that rises and falls over minutes.
        float gusts = (float)(0.85 + 0.3 * Math.Sin(_time * 0.011) * Math.Sin(_time * 0.0037 + 1.3));
        RenderingServer.GlobalShaderParameterSet("hu_wind", new Vector4(0.86f, 0, -0.5f, gusts));

        _terrain.UpdateLod(camera);
        _vegetation?.UpdateVegetation(camera);
        if (_grass == null && _vegetation?.Library is { } library)
        {
            _terrainMaterial.SetShaderParameter("blade_mean", library.BladeMean);
            _grass = new GrassField { Name = "Grass" };
            AddChild(_grass);
            _grass.Setup(surface, frame, library, _groundAlbedo, _noise!, library.BladeMean);
        }
        double overGround = camera.Length - surface.Radius - Math.Max(surface.HeightAt(camera.Normalized), 0);
        _grass?.UpdateGrass(_camera.Position, overGround, _walk);
        _ui.Refresh(_planet!, surface, frame, camera, _sky, _time, _frameMs, _terrain, _walk, _timeRuns);
    }

    // ---------------------------------------------------------------- camera

    private void ApplyCameraRotation()
    {
        _camera.Basis = Basis.FromEuler(new Vector3((float)(_pitch * Math.PI / 180), (float)(-_yaw * Math.PI / 180), 0), EulerOrder.Yxz);
    }

    private void Fly(float delta)
    {
        if (_frame == null || _surface == null || Driven)
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
        if (!Visible || Driven)
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
                    JumpClock(key.ShiftPressed ? -6 * 3600 : -3600);
                    break;
                case Key.Bracketright:
                    JumpClock(key.ShiftPressed ? 6 * 3600 : 3600);
                    break;
                case Key.T:
                    int next = (Array.IndexOf(Scales, RotationTimeScale) + 1) % Scales.Length;
                    SetRotationTimeScale(Scales[Math.Max(next, 0)]);
                    break;
                case Key.P:
                    _timeRuns = !_timeRuns;
                    break;
                case Key.H:
                    _uiLayer.Visible = !_uiLayer.Visible;
                    break;
                case Key.Escape:
                    BackRequested?.Invoke();
                    break;
            }
        }
    }

    /// <summary>Moves the universe time by <paramref name="seconds"/> (the planet turns by as much, whatever the scale).</summary>
    public void JumpClock(double seconds)
    {
        _time += seconds;
        _sky?.Ephemeris.JumpClock(seconds);
    }

    /// <summary>Sets how much faster than real the planet turns, from now on (the sky doesn't jump).</summary>
    public void SetRotationTimeScale(double scale)
    {
        RotationTimeScale = scale;
        _sky?.Ephemeris.SetRotationTimeScale(scale, _time);
    }

    /// <summary>Sets the universe time and whether it runs (screenshots pause it: they never wait for time).</summary>
    public void SetTime(double time, bool paused)
    {
        _time = time;
        _timeRuns = !paused;
        _sky?.Ephemeris.SetRotationTimeScale(RotationTimeScale, time);
    }
}
