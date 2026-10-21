using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;
using HollowUniverse.Common;

namespace HollowUniverse.Surface;

/// <summary>
/// The surface view (GROUND.md §4.6): a planet's ground out to the horizon, its sea, plants, grass, air, clouds, sun, moon
/// and stars at the universe time. Two ways in: the look-dev view of a real planet of the universe at its real size
/// (<see cref="Open"/>: a tangent frame whose origin follows a free camera that flies, or walks at eye height, G), and the
/// ground game's whole world (<see cref="OpenWorld"/>, docs/WHOLE_WORLD.md §10: any <see cref="ISurface"/> drawn in the
/// body's own frame, origin at the centre, the game placing the camera). Both draw with the same parts.
/// </summary>
public partial class SurfaceView : Node3D
{
    private const float EyeHeight = 1.7f;
    private const double RebaseDistance = 1500;
    private const string SettingsPath = "user://settings.cfg";

    /// <summary>The far plane on a whole world: its edge is 6.8 km away from the leaving height (docs/WHOLE_WORLD.md §10.5).</summary>
    public const float WorldFar = 15_000;

    /// <summary>The ground's sixteen texture layers' files (a game loads them ahead, GROUND.md §4.3.1; GroundLayers).</summary>
    public static IReadOnlyList<string> GroundTextures => GroundLayers.Files();

    private Camera3D _camera = null!;
    private Godot.Environment _environment = null!;
    private ShaderMaterial _skyMaterial = null!;
    private DirectionalLight3D _sunLight = null!;
    private DirectionalLight3D _moonLight = null!;
    private TerrainLod _terrain = null!;
    private Vegetation? _vegetation;
    private GrassField? _grass;
    private CliffField? _cliffs;
    private CliffKitField? _kitCliffs;
    private MonumentField? _monuments;
    private TextureLayered _groundAlbedo = null!;
    private TextureLayered _groundNormal = null!;
    private ShaderMaterial _terrainMaterial = null!;
    private SurfaceWorldPhases? _worldPhases;
    private CanvasLayer _uiLayer = null!;
    private SurfaceUi _ui = null!;

    private ISurface? _surface;
    private StarSystem? _system;
    private Body? _planet;
    private AtmosphereModel? _air;
    private SurfaceSky? _sky;
    private RenderFrame? _frame;
    private ViewLifetime? _lifetime;
    private long _planetOwner;
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
    public bool IsReady => _frame != null && PlanetGlobals.IsOwner(_planetOwner) && _idleFrames > 20;

    /// <summary>
    /// Current opening's stable completion handle. Stop closes admission; completion includes every retired field
    /// from quality changes and previous openings, then releases the transferred world lease. Never wait synchronously.
    /// </summary>
    public Task WorkersStopped => _lifetime?.Stopped ?? Task.CompletedTask;

    public GrassField? Grass => _grass;

    // Sealed cliff soil shares the exact same phase owner as the terrain. The returned registration is borrowed:
    // release it before disposing the material, and before publishing a replacement into this view.
    internal IDisposable BindGroundPhase(ShaderMaterial material) =>
        (_worldPhases ?? throw new InvalidOperationException("No active surface phase owner.")).BindGround(material);

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
            if (_globals && PlanetGlobals.IsOwner(_planetOwner))
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

    /// <summary>The ground drawn.</summary>
    public ISurface? Surface => _surface;

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
        HollowUniverse.Settings.GraphicsSettings.Changed += ApplyGraphics;
        ApplyGraphics(HollowUniverse.Settings.GraphicsSettings.Current);
        _uiLayer = new CanvasLayer();
        AddChild(_uiLayer);
        _ui = new SurfaceUi();
        _uiLayer.AddChild(_ui);
        SetupWaterView();
    }

    public override void _ExitTree()
    {
        Stop();
        HollowUniverse.Settings.GraphicsSettings.Changed -= ApplyGraphics;
    }

    // Only this managed holder survives retirement. It holds no Node, native resource, frame or callback into a view.
    private sealed class ViewLifetime
    {
        private readonly List<Task> _work = new();
        private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WorldLease? _lease;
        public bool Retired { get; private set; }
        public Task Stopped => _stopped.Task;
        public ViewLifetime(WorldLease? lease, Task previous)
        {
            _lease = lease;
            Add(previous);
        }
        public bool Owns(WorldLease lease) => ReferenceEquals(_lease, lease);
        public void Add(Task work)
        {
            if (Retired) throw new InvalidOperationException("Retired surface views cannot accept work.");
            // Successful finished handles retain no query ownership and need not accumulate across quality changes.
            _work.RemoveAll(static task => task.IsCompletedSuccessfully);
            if (!work.IsCompletedSuccessfully) _work.Add(work);
        }
        public void Stop()
        {
            if (Retired) return;
            Retired = true;
            Task pending = Task.WhenAll(_work);
            _work.Clear();
            WorldLease? lease = _lease;
            _lease = null;
            _ = Release(pending, lease, _stopped);
        }
        private static async Task Release(Task pending, WorldLease? lease, TaskCompletionSource<bool> stopped)
        {
            Exception? failure = null;
            try { await pending.ConfigureAwait(false); }
            catch (Exception error) { failure = pending.Exception ?? error; }
            finally
            {
                try { lease?.Dispose(); }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            }
            if (failure != null) stopped.TrySetException(failure);
            else stopped.TrySetResult(true);
        }
    }

    private void ValidateLease(ISurface surface, WorldLease? lease)
    {
        if (lease == null) return; // Explicit borrowed-source path for diagnostic and legacy look-dev callers.
        if (_lifetime?.Owns(lease) == true)
            throw new ArgumentException("A new opening requires its own world lease; fork before passing it.", nameof(lease));
        try
        {
            if (!ReferenceEquals(surface, lease.Surface))
                throw new ArgumentException("The transferred lease must own the surface being opened.", nameof(lease));
        }
        catch { lease.Dispose(); throw; }
    }

    private void RetireGrass()
    {
        if (_grass is not { } grass) return;
        grass.Stop();
        _lifetime?.Add(grass.WorkMonitor.WhenIdle);
        grass.QueueFree();
        _grass = null;
    }

    private void BeginLifetime(WorldLease? lease)
    {
        Stop();
        _lifetime = new ViewLifetime(lease, WorkersStopped);
    }

    // Native lifetime probe: use the production holder/quality-retirement/Stop paths without loading a planet's art.
    internal void SetupLifecycleFixture(WorldLease lease, GrassField? grass = null, Task? pending = null)
    {
        ISurface surface = lease.Surface;
        ValidateLease(surface, lease);
        BeginLifetime(lease);
        _surface = surface;
        if (grass != null) { AddChild(grass); _grass = grass; }
        if (pending != null) _lifetime!.Add(pending);
    }

    internal void RetireLifecycleGrass() => RetireGrass();

    /// <summary>
    /// Main-thread retirement closes every world's producer before freeing children. Running queries finish against
    /// their original inputs; their managed barriers retain the world lease until the last completion, including faults.
    /// </summary>
    public void Stop()
    {
        if (_lifetime is not { Retired: false } lifetime) return;
        RetireGrass();
        _terrain.Stop();
        lifetime.Add(_terrain.WorkersStopped);
        if (_vegetation is { } vegetation)
        {
            vegetation.Stop();
            lifetime.Add(vegetation.WorkersStopped);
            vegetation.QueueFree();
            _vegetation = null;
        }
        if (_cliffs is { } cliffs)
        {
            cliffs.Stop();
            lifetime.Add(cliffs.WorkersStopped);
            cliffs.QueueFree();
            _cliffs = null;
        }
        if (_kitCliffs is { } kitCliffs)
        {
            kitCliffs.Stop();
            lifetime.Add(kitCliffs.WorkersStopped);
            kitCliffs.QueueFree();
            _kitCliffs = null;
        }
        _monuments?.Stop();
        _monuments?.QueueFree();
        _monuments = null;
        _gravity?.QueueFree();
        _gravity = null;
        _worldPhases?.Dispose();
        _worldPhases = null;
        PlanetGlobals.Clear(_planetOwner);
        _planetOwner = 0;
        _surface = null; _frame = null; _system = null; _planet = null; _sky = null; _air = null;
        // Aerial work owns only an atmosphere/value snapshot, never this world's query source or lease.
        _aerial = null;
        _idleFrames = 0;
        lifetime.Stop();
    }

    public void SetActive(bool active)
    {
        Visible = active;
        _uiLayer.Visible = active;
        _waterLayer.Visible = active;
        ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (active)
        {
            _camera.MakeCurrent();
        }
    }

    /// <summary>
    /// Opens a real planet at its real size, at a latitude and longitude (degrees), <paramref name="altitude"/> metres
    /// above the ground, looking along <paramref name="yaw"/> (degrees from north toward east) and
    /// <paramref name="pitch"/> (degrees up), at a universe time (seconds since J2000): the look-dev view, in a tangent
    /// frame whose origin follows the camera, its plants the surface's own scatter. An optional lease transfers ownership
    /// to this view, including cleanup if opening fails; otherwise the surface is explicitly borrowed.
    /// </summary>
    public void Open(StarSystem system, Body planet, PlanetSurface surface, double latitude, double longitude, double altitude,
        double yaw, double pitch, double time, WorldLease? lease = null)
    {
        ValidateLease(surface, lease);
        ViewLifetime? previous = _lifetime;
        try
        {
            Vector3D direction = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
            double ground = Math.Max(surface.HeightAt(direction), 0);
            var frame = new RenderFrame(direction * (surface.Radius + ground));
            _walk = altitude <= EyeHeight + 0.01;
            _yaw = yaw;
            _pitch = pitch;
            _camera.Position = new Vector3(0, (float)Math.Max(altitude, 0.5), 0);
            ApplyCameraRotation();
            Begin(system, planet, surface, new SurfacePlants(surface), surface.Seed, frame, time, lease);
        }
        catch
        {
            // Begin owns the lease as soon as it starts; before that this call owns it.
            if (!ReferenceEquals(_lifetime, previous)) Stop();
            else lease?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a whole world for the ground game (docs/WHOLE_WORLD.md §10): any surface drawn in the body's own frame
    /// (render coordinates are a zone's: metres from the centre, the body's axes), its plants from
    /// <paramref name="plants"/> (the zone's natural objects), the air, sun and moon of the planet the world stands for at
    /// a universe time, the planet turning <paramref name="rotationTimeScale"/> times faster than real counted from
    /// J2000, as the server's clock. The game drives the camera; nothing moves the origin. An optional lease transfers
    /// ownership to this view, including cleanup if opening fails; otherwise the surface is explicitly borrowed.
    /// </summary>
    public void OpenWorld(StarSystem system, Body planet, ISurface surface, IPlantSource plants, ulong seed, double time, double rotationTimeScale,
        float gravity, WorldLease? lease = null)
    {
        ValidateLease(surface, lease);
        ViewLifetime? previous = _lifetime;
        try
        {
            Driven = true;
            _walk = true;
            _camera.Far = WorldFar;
            Begin(system, planet, surface, plants, seed, RenderFrame.World, time, lease);
            _sky!.Ephemeris.SetRotationTimeScale(rotationTimeScale, 0);
            InterfaceVisible = false;
            // What Godot's own physics drops (ragdolls) falls toward the centre (docs/WHOLE_WORLD.md §10.4).
            _gravity?.QueueFree();
            _gravity = PlanetGravity(surface.Radius, gravity);
            AddChild(_gravity);
        }
        catch
        {
            if (!ReferenceEquals(_lifetime, previous)) Stop();
            else lease?.Dispose();
            throw;
        }
    }

    private Area3D? _gravity;

    // A point gravity toward the world's centre over the whole of it, in place of the physics space's −Y; the ground it
    // falls on (layer 1) is static and takes no part.
    private static Area3D PlanetGravity(double radius, float gravity)
    {
        var area = new Area3D
        {
            Name = "PlanetGravity",
            GravitySpaceOverride = Area3D.SpaceOverride.Replace,
            GravityPoint = true,
            GravityPointCenter = Vector3.Zero,
            GravityPointUnitDistance = 0,
            Gravity = gravity,
            CollisionLayer = 0,
            CollisionMask = ~1u,
            Monitoring = true,
            Monitorable = false,
        };
        area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = (float)(radius * 4) } });
        return area;
    }

    // Everything a view of a surface needs, in a render frame.
    private void Begin(StarSystem system, Body planet, ISurface surface, IPlantSource plants, ulong seed, RenderFrame frame, double time,
        WorldLease? lease)
    {
        BeginLifetime(lease);
        _openTimes.Clear();
        _lapAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _system = system;
        _planet = planet;
        _surface = surface;
        _time = time;
        _frame = frame;
        _air = AtmosphereModel.Take(planet);
        _sky = new SurfaceSky(system, planet, _air);
        _sky.Ephemeris.SetRotationTimeScale(RotationTimeScale, time);
        Lap("air");

        SetupGlobals();
        Lap("globals");
        SetupEnvironment();
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_terrain.gdshader") };
        _groundAlbedo = GroundLayers.Albedo;
        _groundNormal = GroundLayers.Normal;
        GroundLayers.Apply(_terrainMaterial);
        _worldPhases = new SurfaceWorldPhases(_planetOwner, GroundLayers.Metres);
        _worldPhases.Update(frame, new Vector3(.86f, 0, -.5f));
        _worldPhases.BindGround(_terrainMaterial);
        _terrainMaterial.SetShaderParameter("macro_noise", _noise ?? throw new InvalidOperationException("Surface noise was not initialized."));
        _canopy ??= NoiseTextures.Canopy(13);
        _terrainMaterial.SetShaderParameter("canopy_texture", _canopy);
        // One water for the sea, lakes and streams (ISurface.WaterLevelAt; a world without any draws none).
        var water = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_water.gdshader") };
        water.SetShaderParameter("wave_normals", NoiseTextures.WaveNormals(11));
        Lap("materials");
        _terrain.Setup(surface, _frame, _terrainMaterial, water);
        _vegetation = new Vegetation { Name = "Vegetation" };
        AddChild(_vegetation);
        _vegetation.Setup(surface, plants, seed, _frame, _groundAlbedo, _groundNormal);
        if (surface is WalkingWorld walking)
        {
            _cliffs = new CliffField { Name = "Cliffs" };
            AddChild(_cliffs);
            _cliffs.Setup(walking.Cliffs, frame);
            if (walking.KitCliffs is { } kit)
            {
                _kitCliffs = new CliffKitField { Name = "CliffKit" };
                AddChild(_kitCliffs);
                _kitCliffs.Setup(kit, frame);
            }
            _monuments = new MonumentField { Name = "Monuments" };
            AddChild(_monuments);
            _monuments.Setup(walking.Monuments, frame);
        }
        _idleFrames = 0;
        _firstFrame = true;
        Lap("terrain and plants");
        UpdateWorld(0);
        Lap("first update");
    }

    private readonly System.Text.StringBuilder _openTimes = new();
    private long _lapAt;

    /// <summary>What the last opening spent its time on (milliseconds by part), for the start's timeline.</summary>
    public string OpenTimes => _openTimes.ToString();

    private void Lap(string what)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = (now - _lapAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _openTimes.Append(_openTimes.Length > 0 ? ", " : "").Append(what).Append(' ').Append(ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(" ms");
        _lapAt = now;
    }

    // ---------------------------------------------------------------- set-up

    // The cloud field repeats every 780 km of drift (its three scales, 60, 15 and 2.6 km, all divide it): wrapped there.
    private const double CloudPeriod = 780_000;

    private static bool _globals;
    private ImageTexture? _noise;
    private ImageTexture? _canopy;
    private AerialTable? _aerial;

    private void SetupGlobals()
    {
        AtmosphereModel air = _air!;
        // The planet under the drawn world first: every surface shader reads its up from it.
        _planetOwner = PlanetGlobals.Claim(_frame!, _surface!.Radius);
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
        Add("hu_phase_wave_main", V3, SurfaceWorldPhases.MainWave);
        Add("hu_phase_wave_foliage", V3, SurfaceWorldPhases.FoliageDetailWave);
        Add("hu_phase_wave_tree", V3, Vector3.Zero);
        Add("hu_phase_wave_angles", V3, Vector3.Zero);
        Add("hu_wind", V4, new Vector4(0.86f, 0, -0.5f, 1));
        Add("hu_lod_clock", T, 0f);
        // A tool's blow shaking one plant's crown (the ground game's NatureView sets them; GROUND.md §9.3).
        Add("hu_shake_at", V4, Vector4.Zero);
        Add("hu_shake_dir", V4, Vector4.Zero);
        var S3 = RenderingServer.GlobalShaderParameterType.Sampler3D;
        // The aerial-perspective table reaches as far as anything can be seen: the far plane on a whole world.
        double range = _frame!.IsWorld ? AtmosphereModel.WorldApRange : AtmosphereModel.ApRange;
        _aerial = new AerialTable(air, range);
        Add("hu_ap_up", V3, Vector3.Up);
        Add("hu_ap_east", V3, Vector3.Right);
        Add("hu_ap_range", T, (float)range);
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
            // Glow and bloom (0.35 and 0.02 at strength 1), SSAO and the rest: the graphics settings (GROUND.md §8.6).
            GlowHdrThreshold = 1.4f,
            // Gentle: on dense grass and leaf cards strong SSAO turns everything in shade black.
            SsaoRadius = 0.7f,
            SsaoIntensity = 0.6f,
        };
        // The camera carries its own environment: the maps' WorldEnvironment nodes stay in the tree.
        _environment = environment;
        _camera.Environment = environment;
        ApplyGraphics(HollowUniverse.Settings.GraphicsSettings.Current);
    }

    // The graphics settings (GROUND.md §8.6): the environment's effects, the sun's shadows, and the grass rebuilt when
    // its distance or density changes. The exposure stays the sky's, set every frame, whatever lights the ground.
    private void ApplyGraphics(HollowUniverse.Settings.GraphicsValues values)
    {
        if (_environment is { } environment)
        {
            HollowUniverse.Settings.GraphicsSettings.ApplyTo(environment, values, FogLook());
        }
        HollowUniverse.Settings.GraphicsSettings.ApplyTo(_sunLight, values);
        HollowUniverse.Settings.GraphicsSettings.ApplyTo(_moonLight,
            values with { ShadowDistance = Math.Min(values.ShadowDistance, 300), ShadowCascades = 1 });
        if (_grass != null && _grass.BuiltWith != (values.GrassDistance, values.GrassDensity))
        {
            RetireGrass();
        }
    }

    // The volumetric fog as this planet's air near the ground: faint (0.0008 per metre with Earth's haze, so a
    // fifth of the light at the fog's far end), its density following the air's Mie extinction, its colour the air's
    // scattering, and the haze's forward scattering for shafts of sun.
    private HollowUniverse.Settings.GraphicsSettings.FogLook FogLook()
    {
        if (_air is not { } air)
        {
            return new(0.0008f, new Color(0.85f, 0.9f, 1f), 0.6f);
        }
        float haze = (float)Math.Clamp(air.MieExtinction / 5e-6, 0.3, 3);
        Vector3 scatter = air.RayleighScattering + Vector3.One * (float)air.MieScattering;
        float top = Math.Max(scatter.X, Math.Max(scatter.Y, scatter.Z));
        Vector3 tint = Vector3.One.Lerp(top > 0 ? scatter / top : Vector3.One, 0.25f);
        return new(0.0008f * haze, new Color(tint.X, tint.Y, tint.Z), Math.Clamp((float)air.MieG, 0f, 0.85f));
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        if (_frame == null || !PlanetGlobals.IsOwner(_planetOwner))
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
        _idleFrames = _terrain.IsIdle && (_vegetation?.IsIdle ?? true) && (_grass?.IsIdle ?? false) && (_cliffs?.IsIdle ?? true)
            && (_kitCliffs?.IsIdle ?? true) && (_monuments?.IsIdle ?? true) ? _idleFrames + 1 : 0;
    }

    // The ground under a body-frame point, metres above the mean radius: the land, or the sea where it is higher.
    private double GroundAt(Vector3D direction)
    {
        ISurface surface = _surface!;
        double ground = surface.HeightAt(direction);
        return surface.SeaRadius > 0 ? Math.Max(ground, surface.SeaRadius - surface.Radius) : ground;
    }

    private void UpdateWorld(double delta)
    {
        RenderFrame frame = _frame!;
        ISurface surface = _surface!;
        // Keep the render origin under the camera (a tangent frame only: the world frame holds a whole world).
        Vector3 cam = _camera.Position;
        if (!Driven && !frame.IsWorld && new Vector2(cam.X, cam.Z).Length() > RebaseDistance)
        {
            Vector3D body = frame.ToBody(cam);
            Vector3D dir = body.Normalized;
            Vector3 shift = frame.Move(dir * (surface.Radius + Math.Max(surface.HeightAt(dir), 0)));
            _camera.Position -= shift;
            _terrain.Reanchor();
            _vegetation?.Reanchor();
        }
        if (!frame.IsWorld)
        {
            PlanetGlobals.Set(_planetOwner, frame, surface.Radius);
        }
        Vector3D camera = frame.ToBody(_camera.GlobalPosition);
        float pixelAngle = Mathf.DegToRad(_camera.Fov) / Math.Max(GetViewport().GetVisibleRect().Size.Y, 1);
        double altitude = camera.Length - surface.Radius;
        _sky!.Update(_time, camera, frame, _skyMaterial, _sunLight, _moonLight, pixelAngle, surface.Radius);

        (Vector3 up, Vector3 east, Vector3 north) = frame.LocalAxes(camera);
        RenderingServer.GlobalShaderParameterSet("hu_camera_altitude", (float)Math.Max(altitude, 0.05));
        RenderingServer.GlobalShaderParameterSet("hu_camera_up", up);
        RenderingServer.GlobalShaderParameterSet("hu_camera_position", _camera.GlobalPosition);
        RenderingServer.GlobalShaderParameterSet("hu_sun_direction", _sky.SunDirection);
        RenderingServer.GlobalShaderParameterSet("hu_sun_illuminance", _sky.SunIlluminance);
        RenderingServer.GlobalShaderParameterSet("hu_moon_direction", _sky.MoonDirection);
        // The air scatters the moon's real light; only the light on the ground is boosted (nights stay dark, the ground
        // readable).
        RenderingServer.GlobalShaderParameterSet("hu_moon_illuminance", _sky.MoonIlluminance * SurfaceSky.MoonSkyBoost);
        // The table's axes: the camera's up, and east there (docs/WHOLE_WORLD.md §3.1: the pole axis crossed with up).
        _aerial!.Update(Time.GetTicksMsec() / 1000.0, Math.Max(altitude, 0.5), up, east, _sky.SunDirection, _sky.SunIlluminance,
            _sky.MoonDirection, _sky.MoonIlluminance * SurfaceSky.MoonSkyBoost, wait: _firstFrame);
        _firstFrame = false;
        // Clouds drift with the wind (12 m/s), faster when the day is sped up; they are fixed to the planet (sampled by
        // direction), so moving the origin moves nothing.
        _cloudDrift += (_timeRuns ? delta * RotationTimeScale : 0) * 12;
        RenderingServer.GlobalShaderParameterSet("hu_cloud_wind", new Vector2((float)-(_cloudDrift % CloudPeriod), 0));
        _environment.TonemapExposure = DebugSky == 15 ? 1 : _sky.Exposure;
        UpdateWaterView(camera);
        if (DebugSky == 15)
        {
            _environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
            _environment.GlowEnabled = false;
            _environment.SsaoEnabled = false;
        }
        _skyMaterial.SetShaderParameter("debug_view", DebugSky < 10 ? DebugSky : 0);
        _terrainMaterial.SetShaderParameter("debug_view", DebugSky >= 10 ? DebugSky - 10 : 0);

        // A breeze from the west-south-west, along the ground where the camera is, that rises and falls over minutes.
        float gusts = (float)(0.85 + 0.3 * Math.Sin(_time * 0.011) * Math.Sin(_time * 0.0037 + 1.3));
        var windFrame = new ZoneFrame(camera);
        Vector3D bodyWind = (windFrame.East * 0.86 - windFrame.South * 0.5).Normalized;
        Vector3 wind = frame.DirectionToRender(bodyWind).Normalized();
        RenderingServer.GlobalShaderParameterSet("hu_wind", new Vector4(wind.X, wind.Y, wind.Z, gusts));
        _worldPhases!.Update(frame, new Vector3((float)bodyWind.X, (float)bodyWind.Y, (float)bodyWind.Z));

        _terrain.UpdateLod(camera);
        _vegetation?.UpdateVegetation(camera);
        _cliffs?.Follow(camera);
        _kitCliffs?.Follow(camera);
        _monuments?.Follow(camera);
        if (_grass == null && _vegetation?.Library is { } library)
        {
            _terrainMaterial.SetShaderParameter("blade_mean", library.BladeMean);
            _grass = new GrassField { Name = "Grass" };
            AddChild(_grass);
            _grass.Setup(surface, frame, _terrain, _noise!, library.BladeMean);
            // The terrain's grass takes the blades' colour where they thin out, at their own reach (GrassField).
            _terrainMaterial.SetShaderParameter("grass_reach", _grass.TerrainHandoff);
        }
        double overGround = camera.Length - surface.Radius - GroundAt(camera.Normalized);
        _grass?.UpdateGrass(camera, overGround, _walk);
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
        double altitude = body.Length - _surface.Radius - GroundAt(dir);
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
    public void SetTime(double time, bool paused, bool resetCloudDrift = false)
    {
        _time = time;
        _timeRuns = !paused;
        if (resetCloudDrift) _cloudDrift = 0;
        _sky?.Ephemeris.SetRotationTimeScale(RotationTimeScale, time);
    }
}
