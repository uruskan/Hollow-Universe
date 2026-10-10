using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using HollowUniverse.App;
using HollowUniverse.Common;

namespace HollowUniverse.GalaxyMap;

/// <summary>
/// The galaxy map (GALAXY.md §11): the whole Milky Way as a glowing volume, real and generated stars streamed
/// around the focus, the sector grid, search, selection and the way into a system map.
/// </summary>
public partial class GalaxyMapView : Node3D
{
    private const double RecentreDistance = 768;

    private Galaxy _galaxy = null!;
    private MapCamera _view = null!;
    private Camera3D _camera = null!;
    private Node3D _world = null!;
    private StarStreamer _stars = null!;
    private GalaxyGlow _glow = null!;
    private MeshInstance3D _grid = null!;
    private ShaderMaterial _gridMaterial = null!;
    private ShaderMaterial _starMaterial = null!;
    private CanvasLayer _uiLayer = null!;
    private GalaxyMapUi _ui = null!;
    private Vector3D _origin;
    private GalaxyStar? _selected;
    private GalaxyStar? _hover;
    private double _hoverTimer;
    private bool _rotating;
    private bool _panning;
    private Vector2 _pressPosition;
    private bool _dragged;
    private CatalogueStar[] _named = Array.Empty<CatalogueStar>();
    private double _fps;
    private MeshInstance3D _stalks = null!;
    private ImmediateMesh _stalkMesh = null!;
    private double _stalkTimer;
    private double _skyTimer;
    private Vector3D _skyMeasuredAt = new(double.NaN, 0, 0);
    private double _skyTarget = 1;
    private double _skyBrightness = double.NaN;
    private double _lastStalkMs;
    private double[] _lastCoverage = Array.Empty<double>();
    private float[] _lastExposure = Array.Empty<float>();

    /// <summary>Raised when the player asks to open a system's map.</summary>
    public event Action<SystemAddress>? OpenSystemRequested;

    /// <summary>Hover highlighting follows the mouse (switched off for automated screenshots).</summary>
    public bool HoverEnabled { get; set; } = true;

    public MapCamera View => _view;
    public StarStreamer Stars => _stars;
    public GalaxyStar? Selected => _selected;
    public bool IsGlowReady => _glow.IsReady;

    /// <summary>Milliseconds the last stalk update took, and a pick at the middle of the screen takes (for --trace).</summary>
    public string TimingDebug
    {
        get
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Pick(GetViewport().GetVisibleRect().Size / 2, 12);
            return $"pick {watch.Elapsed.TotalMilliseconds:0.0} ms, stalks {_lastStalkMs:0.0} ms";
        }
    }

    /// <summary>Per level: light per pixel before crowding / the exposure applied (for --trace).</summary>
    public string CrowdingDebug => string.Join(" ", Enumerable.Range(0, _lastExposure.Length)
        .Select(l => $"L{l}:{_lastCoverage[l]:0.000}/{_lastExposure[l]:0.00}"));

    /// <summary>The sky's brightness around the camera relative to Sol's (the glow is dimmed by it).</summary>
    public double SkyBrightness => _skyBrightness;

    public override void _Ready()
    {
        _galaxy = GalaxyService.Galaxy;
        _view = new MapCamera(Vector3D.Zero, 80, 0.65, 0.6);

        _glow = new GalaxyGlow(_galaxy.Density);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = _glow.Material,
                // The shader follows the camera, so the sky is real-time (which requires 256); nothing samples its
                // radiance, and the cubemap pass returns a constant, so this costs little.
                ProcessMode = Sky.ProcessModeEnum.Realtime,
                RadianceSize = Sky.RadianceSizeEnum.Size256,
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0, 0, 0),
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            GlowEnabled = true,
            GlowIntensity = 0.55f,
            GlowStrength = 1.0f,
            GlowBloom = 0.0f,
            GlowHdrThreshold = 0.9f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        _camera = new Camera3D { Fov = 60, Far = 500000, Near = 0.05f };
        AddChild(_camera);

        _world = new Node3D { Name = "World" };
        AddChild(_world);

        _gridMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sector_grid.gdshader") };
        _grid = new MeshInstance3D
        {
            Name = "SectorGrid",
            Mesh = new PlaneMesh { Size = new Vector2(48000, 48000) },
            MaterialOverride = _gridMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _world.AddChild(_grid);

        _stalkMesh = new ImmediateMesh();
        _stalks = new MeshInstance3D
        {
            Name = "Stalks",
            Mesh = _stalkMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                NoDepthTest = true,
            },
        };
        _world.AddChild(_stalks);

        _starMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/stars.gdshader") };
        _stars = new StarStreamer { Name = "Stars", Galaxy = _galaxy, Material = _starMaterial };
        _world.AddChild(_stars);

        _uiLayer = new CanvasLayer();
        AddChild(_uiLayer);
        _ui = new GalaxyMapUi();
        _uiLayer.AddChild(_ui);
        _ui.SearchChanged += text => _ui.SetResults(text.Trim().Length < 2 ? new List<SearchResult>() : _galaxy.Search(text, 8));
        _ui.ResultChosen += OnResultChosen;
        _ui.OpenSystemRequested += () =>
        {
            if (_selected.HasValue)
            {
                OpenSystemRequested?.Invoke(_selected.Value.Address);
            }
        };

        if (_galaxy.Catalogue != null)
        {
            _named = _galaxy.Catalogue.Primaries.Where(s => IsFriendlyName(s.SystemName.Length > 0 ? s.SystemName : s.Name)).ToArray();
        }
        Recentre(Vector3D.Zero);
    }

    /// <summary>Shows or hides the map (the system map replaces it while open).</summary>
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

    /// <summary>Puts the camera somewhere directly (used by command-line options and buttons).</summary>
    public void SetView(Vector3D focus, double distance, double yawDegrees, double pitchDegrees)
    {
        _view.Set(focus, distance, yawDegrees * Math.PI / 180, pitchDegrees * Math.PI / 180);
        Recentre(focus);
    }

    public void Select(GalaxyStar? star)
    {
        _selected = star;
        if (star is GalaxyStar s)
        {
            int companions = 0;
            if (_galaxy.FindCatalogueStar(s.Address) is CatalogueStar real)
            {
                companions = real.Companions.Count;
            }
            _ui.ShowSelection(_galaxy.NameOf(s.Address), s, Galaxy.SectorNameAt(s.Position), companions);
        }
        else
        {
            _ui.HideSelection();
        }
    }

    /// <summary>Shows search results for a text, as if typed (for automation).</summary>
    public void Search(string text) => _ui.Search(text);

    public bool SelectByName(string name)
    {
        List<SearchResult> results = _galaxy.Search(name, 1);
        if (results.Count == 0 || results[0].IsSector || !_galaxy.TryGetStar(results[0].Address, out GalaxyStar star))
        {
            return false;
        }
        Select(star);
        return true;
    }

    private void OnResultChosen(SearchResult result)
    {
        if (result.IsSector)
        {
            _view.FlyTo(result.Position, Math.Max(_view.TargetDistance, 2500));
            return;
        }
        if (_galaxy.TryGetStar(result.Address, out GalaxyStar star))
        {
            Select(star);
            _view.FlyTo(star.Position, Math.Min(_view.TargetDistance, 40));
        }
    }

    public override void _Process(double delta)
    {
        _fps = _fps * 0.95 + 0.05 / Math.Max(delta, 1e-4);
        HandleKeys(delta);
        _view.Update(delta);
        if (Vector3D.Distance(_view.Focus, _origin) > RecentreDistance)
        {
            Recentre(_view.Focus);
        }

        Vector3 cameraPosition = ToRender(_view.Position);
        Vector3 focus = ToRender(_view.Focus);
        // Keep the depth range modest: the galaxy glow is the sky, so the far plane only needs to reach the stars.
        _camera.Near = (float)Math.Clamp(_view.Distance * 0.0005, 0.0005, 5);
        _camera.Far = (float)(_view.Distance + 12000);
        _camera.LookAtFromPosition(cameraPosition, focus, Vector3.Up);

        _stars.SetView(_view.Focus, _view.Distance);
        UpdateSkyBrightness(delta);
        Tune(_view.Distance, focus);
        _glow.Update();

        _hoverTimer -= delta;
        if (HoverEnabled && _hoverTimer <= 0 && !_rotating && !_panning)
        {
            _hoverTimer = 0.08;
            _hover = Pick(GetViewport().GetMousePosition(), 10);
        }

        _stalkTimer -= delta;
        if (_stalkTimer <= 0 || _view.IsMoving)
        {
            _stalkTimer = 0.25;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            UpdateStalks();
            _lastStalkMs = watch.Elapsed.TotalMilliseconds;
        }

        _ui.SetFocus(Galaxy.SectorNameAt(_view.Focus), _view.Focus, _view.Distance);
        _ui.SetStatus($"{_stars.LoadedStars:N0} stars loaded · {_stars.LoadedBlocks:N0} blocks · {_stars.PendingBlocks:N0} generating · {_fps:0} fps");
        DrawOverlay();
    }

    // Brightness, size and glow by zoom level, so the map reads well from 1 ly to the whole galaxy.
    private void Tune(double distance, Vector3 focus)
    {
        double logD = Math.Log10(Math.Max(distance, 0.1));
        // Zooming out shows ever more luminous stars at ever greater distances, so the apparent brightness of a
        // typical visible star barely changes: the reference magnitude only drifts slightly.
        float referenceMagnitude = (float)(8.5 - 0.5 * (logD - Math.Log10(80)));
        float sizeScale = (float)Math.Clamp(1.15 - 0.12 * (logD - 2), 0.6, 1.2);
        // From far away the galaxy is the glow; individual stars fade out.
        float exposure = (float)(1.0 - 0.85 * SmoothStep(8000, 60000, distance));
        _starMaterial.SetShaderParameter("reference_magnitude", referenceMagnitude);
        _starMaterial.SetShaderParameter("size_scale", sizeScale);
        _starMaterial.SetShaderParameter("exposure", exposure);
        _starMaterial.SetShaderParameter("focus_position", focus);
        _starMaterial.SetShaderParameter("view_radius", (float)StarStreamer.ViewRadius(distance));
        var radii = new float[StarClassTable.LevelCount];
        var fadeStart = new float[StarClassTable.LevelCount];
        for (int level = 0; level < radii.Length; level++)
        {
            radii[level] = StarStreamer.LevelVisible(level, distance) ? (float)_stars.LevelRadius(level) : 0f;
            // A level cut short by the budget fades out gradually, so its edge doesn't read as a solid ball.
            fadeStart[level] = _stars.IsBudgetLimited(level) ? 0.3f : 0.7f;
        }
        float[] levelExposure = CrowdingExposure(distance, referenceMagnitude, sizeScale, exposure, fadeStart);
        _starMaterial.SetShaderParameter("level_radius", radii);
        _starMaterial.SetShaderParameter("level_exposure", levelExposure);
        _starMaterial.SetShaderParameter("level_fade_start", fadeStart);

        // Inside the disk the glow is a dark sky with the Milky Way band; from outside it is the whole galaxy.
        // Where the sky is much brighter than at Sol (inside the bulge) the glow is dimmed, like an eye adapting.
        double outside = SmoothStep(3.5, 4.8, logD);
        double adapt = 1.0 / Math.Pow(Math.Max(1.0, _skyBrightness), 0.7);
        _glow.Intensity = (float)((0.10 + 0.20 * outside) * (adapt + (1 - adapt) * outside));
        _glow.Gamma = (float)(2.0 - 1.1 * outside);

        // The grid sits on the horizontal plane through the focus. Sector lines (1024 ly) when zoomed out,
        // a finer power-of-two grid when zoomed in, always aligned with the sector grid.
        double gridAlpha = 1 - SmoothStep(25000, 60000, distance);
        _grid.Visible = gridAlpha > 0.01;
        _grid.Position = new Vector3(focus.X, focus.Y, focus.Z);
        _grid.Scale = Vector3.One * (float)Math.Clamp(distance / 8000, 0.002, 1);
        double fine = Math.Pow(2, Math.Round(Math.Log2(Math.Max(distance / 5, 0.25))));
        fine = Math.Min(fine, 1024);
        double major = distance > 2000 ? 1024 : Math.Min(fine * 8, 1024);
        Vector3D gridOrigin = SectorGrid.Origin - _origin;
        _gridMaterial.SetShaderParameter("grid_origin", new Vector3((float)gridOrigin.X, (float)gridOrigin.Y, (float)gridOrigin.Z));
        _gridMaterial.SetShaderParameter("focus", focus);
        _gridMaterial.SetShaderParameter("cell", (float)major);
        _gridMaterial.SetShaderParameter("fine_cell", (float)fine);
        _gridMaterial.SetShaderParameter("fade_radius", (float)Math.Clamp(distance * 1.5, 3, 24000));
        _gridMaterial.SetShaderParameter("alpha", (float)(0.42 * gridAlpha));
        _gridMaterial.SetShaderParameter("fine_alpha", (float)(fine < major ? 0.6 : 0.0));
    }

    // Vertical lines from the stars nearest the focus down (or up) to the focus plane, so their 3D position
    // reads on a flat screen.
    private void UpdateStalks()
    {
        _stalkMesh.ClearSurfaces();
        double radius = StarStreamer.ViewRadius(_view.Distance) * 0.5;
        // Stalks help when looking down on the map; edge-on they are just noise.
        double fromAbove = SmoothStep(0.15, 0.45, Math.Abs(_view.Pitch));
        if (_view.Distance > 1500 || fromAbove <= 0.01)
        {
            return;
        }
        // Only the nearest stars get stalks, so look no further than where a few hundred shown stars are expected
        // (a few light-years at the core), and keep the nearest in a bounded heap: farthest first.
        double search = Math.Min(radius, _stars.NearbyRadius(4 * StalkCount));
        double searchSq = search * search;
        Vector3D focus = _view.Focus;
        var nearest = new PriorityQueue<GalaxyStar, double>(StalkCount + 1);
        foreach (StarStreamer.StarBlock block in _stars.BlocksWithStars())
        {
            double bucketRadius = 0.87 * block.BucketSize;
            if (Vector3D.Distance(block.Min + new Vector3D(block.Size / 2, block.Size / 2, block.Size / 2), focus) - 8 * bucketRadius > search)
            {
                continue;
            }
            GalaxyStar[] stars = block.Stars;
            // Small blocks are scanned whole; big ones (towards the core) bucket by bucket.
            bool whole = stars.Length < WholeBlockScan;
            for (int b = 0; b < (whole ? 1 : block.BucketStarts.Length - 1); b++)
            {
                int first = whole ? 0 : block.BucketStarts[b];
                int end = whole ? stars.Length : block.BucketStarts[b + 1];
                if (first == end || (!whole && Vector3D.Distance(block.BucketCentre(b), focus) - bucketRadius > search))
                {
                    continue;
                }
                for (int i = first; i < end; i++)
                {
                    ref readonly GalaxyStar s = ref stars[i];
                    double dx = s.Position.X - focus.X;
                    double dy = s.Position.Y - focus.Y;
                    double dz = s.Position.Z - focus.Z;
                    double d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 >= searchSq || (nearest.Count == StalkCount && nearest.TryPeek(out _, out double farthest) && d2 >= -farthest))
                    {
                        continue;
                    }
                    if (_stars.IsShown(s, 0.7))
                    {
                        nearest.Enqueue(s, -d2);
                        if (nearest.Count > StalkCount)
                        {
                            nearest.Dequeue();
                        }
                    }
                }
            }
        }
        if (nearest.Count == 0)
        {
            return;
        }
        double planeY = focus.Y;
        _stalkMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        while (nearest.TryDequeue(out GalaxyStar s, out double negativeD2))
        {
            float fade = (float)((1 - SmoothStep(radius * 0.4, radius, Math.Sqrt(-negativeD2))) * fromAbove);
            bool above = s.Position.Y >= planeY;
            var colour = above ? new Color(0.35f, 0.65f, 0.95f, 0.32f * fade) : new Color(0.95f, 0.6f, 0.35f, 0.28f * fade);
            Vector3 top = ToRender(s.Position);
            Vector3 foot = ToRender(new Vector3D(s.Position.X, planeY, s.Position.Z));
            _stalkMesh.SurfaceSetColor(colour);
            _stalkMesh.SurfaceAddVertex(top);
            _stalkMesh.SurfaceSetColor(new Color(colour, colour.A * 0.5f));
            _stalkMesh.SurfaceAddVertex(foot);
        }
        _stalkMesh.SurfaceEnd();
    }

    private const int StalkCount = 120;

    // Blocks with fewer stars than this are scanned whole rather than bucket by bucket.
    private const int WholeBlockScan = 4096;

    // Typical absolute magnitude of each level's stars (the middle of its range).
    private static readonly double[] LevelTypicalMagnitude = { -4.5, -1.5, 0.5, 2.2, 3.5, 5.0, 7.5, 12.0 };

    // Per level, how much to cut the light of its stars where many overlap on screen (towards the core), so that
    // they add up to a glow instead of a white blob. Mirrors the star shader at the middle of the screen, where
    // the focus is: a level's light per pixel is its stars per steradian towards the focus times the light of one
    // typical point (brightness × area × the mean of the point's profile), times the shader's fade and exposure.
    // Levels drawn over a wider area add their light to narrower ones.
    private float[] CrowdingExposure(double distance, double referenceMagnitude, double sizeScale, double exposure, float[] fadeStart)
    {
        int count = StarClassTable.LevelCount;
        var coverage = new double[count];
        var solidAngle = new double[count];
        double focalPixels = 0.5 * GetViewport().GetVisibleRect().Size.Y / Math.Tan(0.5 * _camera.Fov * Math.PI / 180);
        for (int level = 0; level < count; level++)
        {
            double radius = _stars.LevelRadius(level);
            double stars = _stars.ShownCount(level) * MeanFade(fadeStart[level]);
            solidAngle[level] = 4 * Math.PI;
            if (!StarStreamer.LevelVisible(level, distance) || radius <= 0 || stars <= 0)
            {
                continue;
            }
            // Stars per steradian along the line of sight through a uniform ball of them, from a camera at
            // `distance` from its centre: continuous as the camera moves into the ball.
            double perVolume = stars / (4.0 / 3.0 * Math.PI * radius * radius * radius);
            double far = distance + radius;
            double near = Math.Max(distance - radius, 0);
            double perSteradian = perVolume * (far * far * far - near * near * near) / 3;
            solidAngle[level] = stars / perSteradian;

            double typicalDistance = Math.Sqrt(distance * distance + 0.6 * radius * radius);
            double magnitude = LevelTypicalMagnitude[level] + 5 * Math.Log10(typicalDistance / 32.616);
            double flux = Math.Min(Math.Pow(10, -0.4 * (magnitude - referenceMagnitude)), 900);
            double brightness = Math.Clamp(0.95 * Math.Sqrt(flux), 0.1, 1.8);
            double size = Math.Clamp(1.7 + 2 * Math.Pow(flux, 0.35), 1.7, 20) * sizeScale;
            double light = brightness * exposure * Math.PI * 0.25 * size * size * 0.255;
            coverage[level] = perSteradian / (focalPixels * focalPixels) * light;
        }
        var result = new float[count];
        for (int level = 0; level < count; level++)
        {
            double sum = 0;
            for (int other = 0; other < count; other++)
            {
                sum += coverage[other] * Math.Min(1.0, solidAngle[other] / solidAngle[level]);
            }
            // Leaves sparse views alone and holds crowded ones near CrowdingTarget.
            double excess = sum / CrowdingTarget;
            result[level] = (float)(1.0 / Math.Sqrt(1.0 + excess * excess));
        }
        _lastCoverage = coverage;
        _lastExposure = result;
        return result;
    }

    // The mean of the shader's fade (1 − smoothstep(start·r, r, d)) over a uniform ball of radius r.
    private static double MeanFade(double start)
    {
        const int steps = 64;
        double sum = 0;
        for (int i = 0; i < steps; i++)
        {
            double x = (i + 0.5) / steps;
            sum += 3 * x * x * (1 - SmoothStep(start, 1, x)) / steps;
        }
        return sum;
    }

    // The most light per pixel (linear) that crowded stars add up to.
    private const double CrowdingTarget = 0.1;

    // The sky's brightness around the camera relative to Sol's, re-measured when the camera has moved and eased
    // in over a second or so.
    private void UpdateSkyBrightness(double delta)
    {
        _skyTimer -= delta;
        Vector3D camera = _view.Position;
        if (_skyTimer <= 0 && (double.IsNaN(_skyMeasuredAt.X) || Vector3D.Distance(camera, _skyMeasuredAt) > Math.Max(5, 0.02 * _view.Distance)))
        {
            _skyTimer = 0.2;
            _skyMeasuredAt = camera;
            _skyTarget = _glow.RelativeSkyBrightness(camera);
        }
        if (double.IsNaN(_skyBrightness))
        {
            _skyBrightness = _skyTarget;
        }
        double k = 1 - Math.Exp(-3 * delta);
        _skyBrightness = Math.Exp(Math.Log(_skyBrightness) + (Math.Log(_skyTarget) - Math.Log(_skyBrightness)) * k);
    }

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void Recentre(Vector3D focus)
    {
        _origin = new Vector3D(Math.Round(focus.X / 256) * 256, Math.Round(focus.Y / 256) * 256, Math.Round(focus.Z / 256) * 256);
        _stars.SetRenderOrigin(_origin);
        _glow.SetRenderOrigin(_origin);
    }

    private Vector3 ToRender(Vector3D galactic)
    {
        Vector3D d = galactic - _origin;
        return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
    }

    // ---------------------------------------------------------------- input

    private void HandleKeys(double delta)
    {
        if (_ui.IsTyping)
        {
            return;
        }
        double speed = 0.9 * delta;
        double right = 0;
        double forward = 0;
        double up = 0;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            forward += speed;
        }
        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            forward -= speed;
        }
        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            right += speed;
        }
        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            right -= speed;
        }
        if (Input.IsPhysicalKeyPressed(Key.R))
        {
            up += speed;
        }
        if (Input.IsPhysicalKeyPressed(Key.F))
        {
            up -= speed;
        }
        if (right != 0 || forward != 0 || up != 0)
        {
            _view.Pan(right, forward, up);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton button:
                OnMouseButton(button);
                break;
            case InputEventMouseMotion motion:
                if (_rotating)
                {
                    _view.Orbit(-motion.Relative.X * 0.005, motion.Relative.Y * 0.005);
                }
                else if (_panning)
                {
                    double perPixel = 1.2 / Math.Max(GetViewport().GetVisibleRect().Size.Y, 1);
                    _view.Pan(-motion.Relative.X * perPixel, motion.Relative.Y * perPixel, 0);
                }
                if ((_rotating || _panning) && motion.Position.DistanceTo(_pressPosition) > 4)
                {
                    _dragged = true;
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key when !_ui.IsTyping:
                OnKey(key);
                break;
        }
    }

    private void OnMouseButton(InputEventMouseButton button)
    {
        switch (button.ButtonIndex)
        {
            case MouseButton.WheelUp when button.Pressed:
                _view.Zoom(0.85);
                break;
            case MouseButton.WheelDown when button.Pressed:
                _view.Zoom(1.0 / 0.85);
                break;
            case MouseButton.Left:
                if (button.Pressed)
                {
                    _rotating = true;
                    _dragged = false;
                    _pressPosition = button.Position;
                    if (button.DoubleClick && Pick(button.Position, 12) is GalaxyStar target)
                    {
                        Select(target);
                        _view.FlyTo(target.Position, Math.Min(_view.TargetDistance, 25));
                    }
                }
                else
                {
                    _rotating = false;
                    if (!_dragged)
                    {
                        Select(Pick(button.Position, 12));
                    }
                }
                break;
            case MouseButton.Right:
            case MouseButton.Middle:
                _panning = button.Pressed;
                _dragged = false;
                _pressPosition = button.Position;
                break;
        }
    }

    private void OnKey(InputEventKey key)
    {
        switch (key.PhysicalKeycode)
        {
            case Key.H:
                _view.FlyTo(Vector3D.Zero, 60);
                break;
            case Key.G:
                _view.FlyTo(GalaxyFrame.GalacticCentre, 95000);
                _view.Orbit(0, 0.9 - _view.Pitch);
                break;
            case Key.Enter:
            case Key.KpEnter:
                if (_selected.HasValue)
                {
                    OpenSystemRequested?.Invoke(_selected.Value.Address);
                }
                break;
            case Key.Escape:
                Select(null);
                _ui.ClearSearch();
                break;
        }
    }

    // ---------------------------------------------------------------- picking and overlay

    /// <summary>The star drawn closest to a screen position (within <paramref name="maxPixels"/>), brighter first.</summary>
    public GalaxyStar? Pick(Vector2 screen, float maxPixels)
    {
        Projection projection = _camera.GetCameraProjection();
        Transform3D view = _camera.GetCameraTransform().AffineInverse();
        Vector2 size = GetViewport().GetVisibleRect().Size;
        GalaxyStar? best = null;
        double bestScore = double.MaxValue;
        double maxSq = maxPixels * maxPixels;
        Vector3D camera = _view.Position;
        float focalPixels = projection.Y.Y * size.Y * 0.5f;
        // The cursor's ray, for a cheap first test: stars further off it than the pick radius (with room for the
        // stretch of the projection towards the screen edges) can't be within maxPixels of the cursor.
        Vector3 ray = _camera.ProjectRayNormal(screen);
        double rx = ray.X;
        double ry = ray.Y;
        double rz = ray.Z;
        double tolerance = 1.6 * maxPixels / focalPixels;
        double toleranceSq = tolerance * tolerance;
        foreach (StarStreamer.StarBlock block in _stars.BlocksWithStars())
        {
            // Skip blocks, then buckets, whose bounding sphere lands nowhere near the cursor.
            Vector3D blockCentre = block.Min + new Vector3D(block.Size / 2, block.Size / 2, block.Size / 2);
            if (!NearRay(blockCentre, 0.87 * block.Size))
            {
                continue;
            }
            double bucketRadius = 0.87 * block.BucketSize;
            GalaxyStar[] stars = block.Stars;
            bool whole = stars.Length < WholeBlockScan;
            for (int b = 0; b < (whole ? 1 : block.BucketStarts.Length - 1); b++)
            {
                int first = whole ? 0 : block.BucketStarts[b];
                int end = whole ? stars.Length : block.BucketStarts[b + 1];
                if (first == end || (!whole && !NearRay(block.BucketCentre(b), bucketRadius)))
                {
                    continue;
                }
                for (int i = first; i < end; i++)
                {
                    ref readonly GalaxyStar s = ref stars[i];
                    double vx = s.Position.X - camera.X;
                    double vy = s.Position.Y - camera.Y;
                    double vz = s.Position.Z - camera.Z;
                    double along = vx * rx + vy * ry + vz * rz;
                    if (along <= 0 || vx * vx + vy * vy + vz * vz - along * along > along * along * toleranceSq)
                    {
                        continue;
                    }
                    // Loaded stars past their level's radius are faded out by the shader; they can't be picked either.
                    if (!_stars.IsShown(s) || !Project(s.Position, view, projection, size, out Vector2 p))
                    {
                        continue;
                    }
                    double d2 = p.DistanceSquaredTo(screen);
                    if (d2 > maxSq)
                    {
                        continue;
                    }
                    double distance = Math.Max(Math.Sqrt(vx * vx + vy * vy + vz * vz), 0.01);
                    double apparent = StarColors.DisplayMagnitude(s.Class, s.Physics.AbsoluteMagnitude) + 5 * Math.Log10(distance / 32.616);
                    double score = d2 + apparent * 6;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = s;
                    }
                }
            }
        }

        // Whether a sphere comes within the pick cone around the cursor's ray.
        bool NearRay(Vector3D centre, double radius)
        {
            double cx = centre.X - camera.X;
            double cy = centre.Y - camera.Y;
            double cz = centre.Z - camera.Z;
            double along = cx * rx + cy * ry + cz * rz;
            if (along < -radius)
            {
                return false;
            }
            double off = Math.Sqrt(Math.Max(0, cx * cx + cy * cy + cz * cz - along * along));
            return off <= radius * (1 + tolerance) + tolerance * Math.Max(along, 0);
        }
        return best;
    }

    private bool Project(Vector3D galactic, Transform3D view, Projection projection, Vector2 size, out Vector2 screen)
    {
        Vector3 v = view * ToRender(galactic);
        screen = default;
        if (v.Z >= -1e-6f)
        {
            return false;
        }
        Vector4 clip = projection * new Vector4(v.X, v.Y, v.Z, 1);
        if (clip.W <= 0)
        {
            return false;
        }
        float nx = clip.X / clip.W;
        float ny = clip.Y / clip.W;
        if (nx < -1.2f || nx > 1.2f || ny < -1.2f || ny > 1.2f)
        {
            return false;
        }
        screen = new Vector2((nx * 0.5f + 0.5f) * size.X, (0.5f - ny * 0.5f) * size.Y);
        return true;
    }

    private void DrawOverlay()
    {
        MapOverlay overlay = _ui.Overlay;
        overlay.Clear();
        Projection projection = _camera.GetCameraProjection();
        Transform3D view = _camera.GetCameraTransform().AffineInverse();
        Vector2 size = GetViewport().GetVisibleRect().Size;

        // Labels never overlap each other or the panels. They are placed in order of importance: the selection, the
        // hovered star, Sol, sector names, then the brightest named stars. A label without a free spot is left out.
        var labeller = new Labeller(size);
        foreach (Rect2 panel in _ui.PanelRects())
        {
            labeller.Block(panel);
        }
        var regions = new List<MapOverlay.Text>();
        var names = new List<MapOverlay.Text>();
        var front = new List<MapOverlay.Text>();
        var anchors = new List<Vector2>();

        // The selected and hovered stars have their own labels, so they're left out of the named stars below.
        long selectedId = _selected is GalaxyStar sel && sel.IsCatalogue ? sel.Address.CatalogueId : -1;
        long hoveredId = _hover is GalaxyStar hov && hov.IsCatalogue ? hov.Address.CatalogueId : -1;

        // Sol's dot is kept clear, even of the selection's label, so Sol can always be found.
        Vector2 sol = default;
        bool solInView = selectedId != 0 && hoveredId != 0 && _view.Distance < 30000
            && Project(Vector3D.Zero, view, projection, size, out sol)
            && sol.X > 0 && sol.Y > 0 && sol.X < size.X && sol.Y < size.Y && !labeller.IsCovered(sol);
        if (solInView)
        {
            labeller.Block(sol, Ui.Pxf(4));
        }

        SystemAddress? selectedAddress = null;
        if (_selected is GalaxyStar selected && Project(selected.Position, view, projection, size, out Vector2 sp))
        {
            selectedAddress = selected.Address;
            overlay.Rings.Add(new MapOverlay.Ring(sp, Ui.Pxf(13), Ui.Warm, Ui.Pxf(2)));
            overlay.Rings.Add(new MapOverlay.Ring(sp, Ui.Pxf(18), new Color(Ui.Warm, 0.35f), Ui.Pxf(1)));
            front.Add(AlwaysLabel(labeller, sp, Ui.Pxf(19), _galaxy.NameOf(selected.Address), Ui.Warm, 16,
                [LabelSide.Below, LabelSide.Above, LabelSide.Right, LabelSide.Left]));
        }
        if (_hover is GalaxyStar hover && !hover.Address.Equals(selectedAddress) && Project(hover.Position, view, projection, size, out Vector2 hp))
        {
            overlay.Rings.Add(new MapOverlay.Ring(hp, Ui.Pxf(10), new Color(Ui.Accent, 0.7f), Ui.Pxf(1.5f)));
            front.Insert(0, AlwaysLabel(labeller, hp, Ui.Pxf(11), _galaxy.NameOf(hover.Address), Ui.Text, 15,
                [LabelSide.Right, LabelSide.Left, LabelSide.Above, LabelSide.Below]));
        }

        if (solInView)
        {
            anchors.Add(sol);
            if (labeller.TryPlaceAround(sol, Ui.Pxf(2), "Sol", new Color(1f, 0.92f, 0.75f, 0.95f), 15, MapLabel.Strong,
                    [LabelSide.Right, LabelSide.Left, LabelSide.Below, LabelSide.Above], out MapOverlay.Text label))
            {
                names.Add(label);
            }
        }

        // Sector names around the focus while the grid is visible.
        if (_grid.Visible && _view.Distance > 900)
        {
            if (SectorGrid.TrySectorOf(_view.Focus, out int fx, out int fy, out int fz))
            {
                int span = _view.Distance < 8000 ? 1 : 2;
                for (int dz = -span; dz <= span; dz++)
                {
                    for (int dx = -span; dx <= span; dx++)
                    {
                        int sx = fx + dx;
                        int sz = fz + dz;
                        if (sx < 0 || sz < 0 || sx >= SectorGrid.SectorsPerAxis || sz >= SectorGrid.SectorsPerAxis)
                        {
                            continue;
                        }
                        Vector3D centre = SectorGrid.SectorCentre(sx, fy, sz);
                        centre = new Vector3D(centre.X, _view.Focus.Y, centre.Z);
                        if (Project(centre, view, projection, size, out Vector2 p))
                        {
                            float alpha = dx == 0 && dz == 0 ? 0.75f : 0.4f;
                            var text = new MapOverlay.Text(p, SectorNames.Name(sx, fy, sz).ToUpperInvariant(), new Color(0.48f, 0.74f, 0.96f, alpha), 13, true, MapLabel.Region);
                            if (labeller.TryPlace(text))
                            {
                                regions.Add(text);
                            }
                        }
                    }
                }
            }
        }

        // Names of the brightest named stars as seen from the camera.
        Vector3D camera = _view.Position;
        var candidates = new List<(double Apparent, Vector2 Screen, string Name)>();
        foreach (CatalogueStar star in _named)
        {
            if (star.Id == 0 || star.Id == selectedId || star.Id == hoveredId)
            {
                continue;
            }
            if (!Project(star.Position, view, projection, size, out Vector2 p) || p.X < 0 || p.Y < 0 || p.X > size.X || p.Y > size.Y)
            {
                continue;
            }
            double distance = Math.Max(Vector3D.Distance(star.Position, camera), 0.01);
            if (!IsLoadedLevel(star.Level) || Vector3D.Distance(star.Position, _view.Focus) > 0.75 * _stars.LevelRadius(star.Level))
            {
                continue;
            }
            double apparent = star.Physics.AbsoluteMagnitude + 5 * Math.Log10(distance / 32.616);
            candidates.Add((apparent, p, star.SystemName.Length > 0 ? star.SystemName : star.Name));
        }
        candidates.Sort((a, b) => a.Apparent.CompareTo(b.Apparent));
        int labelled = 0;
        foreach ((double _, Vector2 screen, string name) in candidates)
        {
            if (labelled >= 14)
            {
                break;
            }
            // Spread out: no two names closer than 80 px, and none for a star hidden by a panel or another label.
            if (anchors.Exists(q => q.DistanceTo(screen) < Ui.Pxf(80)) || labeller.IsCovered(screen))
            {
                continue;
            }
            if (labeller.TryPlaceAround(screen, Ui.Pxf(2), name, new Color(0.85f, 0.9f, 1f, 0.85f), 14, MapLabel.Name,
                    [LabelSide.Right, LabelSide.Left], out MapOverlay.Text label))
            {
                anchors.Add(screen);
                labeller.Block(screen, Ui.Pxf(4));
                names.Add(label);
                labelled++;
            }
        }

        overlay.Texts.AddRange(regions);
        overlay.Texts.AddRange(names);
        overlay.Texts.AddRange(front);
        overlay.QueueRedraw();
    }

    // A label that always shows: on the first free side, or on the first side regardless.
    private static MapOverlay.Text AlwaysLabel(Labeller labeller, Vector2 centre, float ring, string name, Color colour, float size,
        ReadOnlySpan<LabelSide> sides)
    {
        labeller.Block(centre, ring);
        if (!labeller.TryPlaceAround(centre, ring, name, colour, size, MapLabel.Strong, sides, out MapOverlay.Text label))
        {
            label = Labeller.Beside(centre, ring, sides[0], name, colour, size, MapLabel.Strong);
            labeller.Force(label);
        }
        return label;
    }

    private bool IsLoadedLevel(int level) => StarStreamer.LevelVisible(level, _view.Distance);

    private static bool IsFriendlyName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }
        string first = name.Split(' ')[0];
        string[] catalogues = { "HD", "HIP", "HR", "Gliese", "GJ", "HYG", "NN", "Wo", "LHS", "LTT", "LP", "G", "GL" };
        return !catalogues.Contains(first) && first.All(char.IsLetter);
    }
}
