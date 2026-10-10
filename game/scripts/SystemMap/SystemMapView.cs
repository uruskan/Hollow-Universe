using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using HollowUniverse.Common;
using HollowUniverse.GalaxyMap;

namespace HollowUniverse.SystemMap;

/// <summary>
/// The system map (GALAXY.md §12): an orrery of one star system, animated by universe time. Distances are
/// compressed logarithmically by default so inner planets stay visible; L switches to true scale.
/// </summary>
public partial class SystemMapView : Node3D
{
    private const int OrbitSegments = 160;
    private const double SystemRadius = 60.0;
    private static readonly double[] Speeds = { 1, 60, 3600, 86400, 7 * 86400, 30 * 86400, 365.25 * 86400 };
    private static readonly string[] SpeedNames = { "real time", "1 min/s", "1 h/s", "1 day/s", "1 week/s", "1 month/s", "1 year/s" };

    private sealed class BodyView
    {
        public Body Body = null!;
        public Node3D Pivot = null!;
        public MeshInstance3D? Mesh;
        public MeshInstance3D? Orbit;
        public float Radius;
        public Vector3 Position;
    }

    private Camera3D _camera = null!;
    private Node3D _root = null!;
    private CanvasLayer _uiLayer = null!;
    private SystemMapUi _ui = null!;
    private StarSystem? _system;
    private readonly List<BodyView> _views = new();
    private double _time;
    private int _speedIndex = 3;
    private bool _paused;
    private bool _logScale = true;
    private double _referenceA;
    private double _scaleK;
    private double _maxA;
    private int _focus;
    private int? _selected;
    private double _distance = 90;
    private double _targetDistance = 90;
    private double _yaw = 0.5;
    private double _pitch = 0.55;
    private Vector3 _focusPosition;
    private bool _rotating;
    private bool _dragged;
    private Vector2 _pressPosition;

    public event Action? BackRequested;

    public StarSystem? System => _system;

    public override void _Ready()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.005f, 0.007f, 0.012f),
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            GlowEnabled = true,
            GlowIntensity = 0.8f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.0f,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.05f, 0.06f, 0.08f),
        };
        AddChild(new WorldEnvironment { Environment = environment });
        _camera = new Camera3D { Fov = 55, Near = 0.01f, Far = 5000 };
        AddChild(_camera);
        _root = new Node3D { Name = "Bodies" };
        AddChild(_root);
        _uiLayer = new CanvasLayer();
        AddChild(_uiLayer);
        _ui = new SystemMapUi();
        _uiLayer.AddChild(_ui);
        _ui.BackRequested += () => BackRequested?.Invoke();
        _ui.BodyChosen += index =>
        {
            Select(index);
            FocusOn(index);
        };
        _ui.SpeedStep += step => _speedIndex = Math.Clamp(_speedIndex + step, 0, Speeds.Length - 1);
        _ui.PauseToggled += () => _paused = !_paused;
        _ui.ScaleToggled += ToggleScale;
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

    /// <summary>Shows a system at a universe time (seconds since J2000).</summary>
    public void Open(StarSystem system, double secondsSinceJ2000)
    {
        _system = system;
        _time = secondsSinceJ2000;
        _selected = null;
        _focus = 0;
        _ui.HideBody();
        Rebuild();
        _ui.ShowSystem(system, system.Primary.Position.Length);
        _distance = _targetDistance = SystemRadius * 1.6;
        _yaw = 0.5;
        _pitch = 0.55;
        _focusPosition = Vector3.Zero;
    }

    public void SetTime(double secondsSinceJ2000, bool paused)
    {
        _time = secondsSinceJ2000;
        _paused = paused;
    }

    public bool SelectByName(string name)
    {
        if (_system == null)
        {
            return false;
        }
        foreach (Body body in _system.Bodies)
        {
            if (string.Equals(body.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Select(body.Index);
                FocusOn(body.Index);
                return true;
            }
        }
        return false;
    }

    public void SetCamera(double distance, double yawDegrees, double pitchDegrees)
    {
        _distance = _targetDistance = distance;
        _yaw = yawDegrees * Math.PI / 180;
        _pitch = pitchDegrees * Math.PI / 180;
    }

    private void ToggleScale()
    {
        _logScale = !_logScale;
        if (_system != null)
        {
            Rebuild();
        }
    }

    // ---------------------------------------------------------------- building

    private void Rebuild()
    {
        foreach (Node child in _root.GetChildren())
        {
            child.QueueFree();
        }
        _views.Clear();
        StarSystem system = _system!;

        double minA = double.MaxValue;
        _maxA = 0;
        foreach (int child in system.Root.Children)
        {
            Body b = system.Bodies[child];
            minA = Math.Min(minA, b.Orbit.SemiMajorAxis);
            _maxA = Math.Max(_maxA, b.Kind == BodyKind.Belt ? b.Orbit.SemiMajorAxis + b.BeltWidth / 2 : b.Orbit.Apoapsis);
        }
        if (_maxA <= 0)
        {
            _maxA = Units.MetresPerAu;
            minA = 0.1 * Units.MetresPerAu;
        }
        _referenceA = Math.Clamp(minA / 6, 0.0005 * Units.MetresPerAu, 0.3 * Units.MetresPerAu);
        _scaleK = SystemRadius / Math.Log10(1 + _maxA / _referenceA);

        foreach (Body body in system.Bodies)
        {
            var view = new BodyView { Body = body, Pivot = new Node3D { Name = body.Name } };
            _root.AddChild(view.Pivot);
            view.Radius = DisplayRadius(body);
            switch (body.Kind)
            {
                case BodyKind.Star:
                    BuildStar(view);
                    break;
                case BodyKind.Belt:
                    BuildBelt(view);
                    break;
                default:
                    BuildPlanet(view);
                    break;
            }
            _views.Add(view);
        }
        foreach (BodyView view in _views)
        {
            if (view.Body.Parent >= 0)
            {
                BuildOrbit(view);
            }
        }
        UpdatePositions();
    }

    private static float DisplayRadius(Body body) => body.Kind switch
    {
        BodyKind.Star => body.StarClass.Kind is Hollow.Galaxy.StarKind.BlackHole or Hollow.Galaxy.StarKind.NeutronStar
            ? 0.45f
            : (float)Math.Clamp(1.0 + 0.45 * Math.Log10(body.Radius / Units.SolarRadius), 0.45, 3.0),
        BodyKind.Planet => (float)(0.2 + 0.13 * Math.Log2(1 + body.Radius / Units.EarthRadius)),
        BodyKind.Moon => (float)(0.08 + 0.1 * Math.Log2(1 + body.Radius / Units.EarthRadius)),
        _ => 0,
    };

    private void BuildStar(BodyView view)
    {
        Body body = view.Body;
        Color colour = StarColors.For(body.StarClass, (float)body.Temperature);
        bool blackHole = body.StarClass.Kind == Hollow.Galaxy.StarKind.BlackHole;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/star.gdshader") };
        material.SetShaderParameter("colour", blackHole ? new Color(0, 0, 0) : colour);
        material.SetShaderParameter("intensity", blackHole ? 0f : 2.6f);
        material.SetShaderParameter("seed", (float)(body.Index * 3.7));
        view.Mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = view.Radius, Height = view.Radius * 2, RadialSegments = 48, Rings = 24 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        view.Pivot.AddChild(view.Mesh);

        var halo = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/halo.gdshader") };
        halo.SetShaderParameter("colour", blackHole ? new Color(0.55f, 0.4f, 1f) : colour);
        halo.SetShaderParameter("strength", blackHole ? 0.6f : 1.0f);
        float haloSize = view.Radius * (blackHole ? 4f : 7f);
        view.Pivot.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(haloSize, haloSize) },
            MaterialOverride = halo,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        if (blackHole)
        {
            AddRing(view.Pivot, view.Radius * 1.6f, view.Radius * 3.2f, new Color(1f, 0.6f, 0.3f), 0.9f, 0.3f, body.Index);
        }
        if (!blackHole)
        {
            view.Pivot.AddChild(new OmniLight3D
            {
                LightColor = colour,
                LightEnergy = body.Index == 0 ? 1.6f : 0.8f,
                OmniRange = 4000,
                OmniAttenuation = 0.0001f,
                ShadowEnabled = false,
            });
        }
    }

    private void BuildPlanet(BodyView view)
    {
        Body body = view.Body;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/planet.gdshader") };
        PlanetLook.Apply(material, body);
        view.Mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = view.Radius, Height = view.Radius * 2, RadialSegments = 40, Rings = 20 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            RotationDegrees = new Vector3(0, 0, (float)(body.AxialTilt * 180 / Math.PI)),
        };
        view.Pivot.AddChild(view.Mesh);
        if (body.HasRings)
        {
            float inner = (float)(view.Radius * body.RingInnerRadius / body.Radius);
            float outer = (float)(view.Radius * body.RingOuterRadius / body.Radius);
            Node3D ring = AddRing(view.Pivot, inner, outer, new Color(0.86f, 0.80f, 0.68f), 0.7f, (float)body.AxialTilt, body.Index);
            _ = ring;
        }
    }

    private Node3D AddRing(Node3D parent, float inner, float outer, Color colour, float opacity, float tilt, int seed)
    {
        const int segments = 96;
        var vertices = new Vector3[(segments + 1) * 2];
        var uvs = new Vector2[(segments + 1) * 2];
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            vertices[i * 2] = dir * inner;
            vertices[i * 2 + 1] = dir * outer;
            uvs[i * 2] = new Vector2(0, 0);
            uvs[i * 2 + 1] = new Vector2(1, 0);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.TriangleStrip, arrays);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ring.gdshader") };
        material.SetShaderParameter("colour", colour);
        material.SetShaderParameter("opacity", opacity);
        material.SetShaderParameter("seed", seed * 1.37f);
        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Rotation = new Vector3(0, 0, tilt),
        };
        parent.AddChild(instance);
        return instance;
    }

    private void BuildBelt(BodyView view)
    {
        Body body = view.Body;
        var rng = new Rng(Hash64.Of((ulong)body.Index, (ulong)body.Path));
        const int count = 900;
        var points = new Vector3[count];
        var colours = new Color[count];
        double a = body.Orbit.SemiMajorAxis;
        for (int i = 0; i < count; i++)
        {
            double r = a + (rng.NextDouble() - 0.5) * body.BeltWidth;
            double angle = rng.NextDouble() * Math.PI * 2;
            float d = (float)MapRoot(r);
            points[i] = new Vector3(Mathf.Cos((float)angle) * d, (float)(rng.Gaussian() * 0.15), Mathf.Sin((float)angle) * d);
            float shade = (float)rng.Range(0.35, 0.6);
            colours[i] = new Color(shade, shade * 0.92f, shade * 0.82f);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Points, arrays);
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            UsePointSize = true,
            PointSize = 2.0f,
        };
        view.Mesh = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        view.Pivot.AddChild(view.Mesh);
    }

    private void BuildOrbit(BodyView view)
    {
        Body body = view.Body;
        if (body.Kind == BodyKind.Belt)
        {
            return;
        }
        BodyView parent = _views[body.Parent];
        var points = new Vector3[OrbitSegments + 1];
        for (int i = 0; i <= OrbitSegments; i++)
        {
            Hollow.Core.Vector3D p = body.Orbit.PositionAtMeanAnomaly(DMath.TwoPi * i / OrbitSegments);
            points[i] = Map(body, parent, p);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.LineStrip, arrays);
        Color colour = body.Kind switch
        {
            BodyKind.Star => new Color(1f, 0.75f, 0.4f, 0.35f),
            BodyKind.Moon => new Color(0.5f, 0.7f, 0.9f, 0.18f),
            _ => new Color(0.45f, 0.75f, 1f, 0.32f),
        };
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = colour,
            NoDepthTest = false,
        };
        view.Orbit = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _root.AddChild(view.Orbit);
    }

    // ---------------------------------------------------------------- positions

    private double MapRoot(double metres) => _logScale
        ? _scaleK * Math.Log10(1 + metres / _referenceA)
        : SystemRadius * metres / _maxA;

    private double MapMoon(BodyView planet, double metres)
    {
        double planetRadius = Math.Max(planet.Body.Radius, 1);
        return planet.Radius * (1.9 + 1.5 * Math.Log10(1 + metres / (4 * planetRadius)));
    }

    // Display offset from the parent for a true offset in metres.
    private Vector3 Map(Body body, BodyView parent, Hollow.Core.Vector3D offset)
    {
        double length = offset.Length;
        if (length <= 0)
        {
            return Vector3.Zero;
        }
        double mapped = body.Kind == BodyKind.Moon ? MapMoon(parent, length) : MapRoot(length);
        Hollow.Core.Vector3D dir = offset / length;
        return new Vector3((float)(dir.X * mapped), (float)(dir.Y * mapped), (float)(dir.Z * mapped));
    }

    private void UpdatePositions()
    {
        foreach (BodyView view in _views)
        {
            Body body = view.Body;
            if (body.Parent < 0)
            {
                view.Position = Vector3.Zero;
            }
            else
            {
                BodyView parent = _views[body.Parent];
                Vector3 offset = body.Kind == BodyKind.Belt ? Vector3.Zero : Map(body, parent, body.Orbit.PositionAt(_time));
                view.Position = parent.Position + offset;
                if (view.Orbit != null)
                {
                    view.Orbit.Position = parent.Position;
                }
            }
            view.Pivot.Position = view.Position;
            if (body.Kind == BodyKind.Belt && view.Mesh != null)
            {
                view.Mesh.Rotation = new Vector3(0, (float)(-DMath.TwoPi * (_time / body.Orbit.Period % 1.0)), 0);
            }
            else if (body.Kind is BodyKind.Planet or BodyKind.Moon && view.Mesh != null && body.RotationPeriod != 0)
            {
                double turns = _time / body.RotationPeriod;
                view.Mesh.Rotation = new Vector3(0, (float)(DMath.TwoPi * (turns - Math.Floor(turns))), (float)body.AxialTilt);
            }
        }
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        if (_system == null)
        {
            return;
        }
        if (!_paused)
        {
            _time += delta * Speeds[_speedIndex];
        }
        UpdatePositions();

        double k = 1 - Math.Exp(-delta * 7);
        _distance = Math.Exp(Math.Log(_distance) + (Math.Log(_targetDistance) - Math.Log(_distance)) * k);
        Vector3 target = _views[_focus].Position;
        _focusPosition = _focusPosition.Lerp(target, (float)Math.Min(1, k * 1.5));
        var back = new Vector3((float)(Math.Cos(_pitch) * Math.Sin(_yaw)), (float)Math.Sin(_pitch), (float)(Math.Cos(_pitch) * Math.Cos(_yaw)));
        _camera.LookAtFromPosition(_focusPosition + back * (float)_distance, _focusPosition, Vector3.Up);
        _camera.Near = (float)Math.Clamp(_distance * 0.002, 0.002, 1);

        _ui.SetTime(UniverseTime.ToUtc(_time), _paused ? "paused" : SpeedNames[_speedIndex], _logScale);
        DrawOverlay();
    }

    private void DrawOverlay()
    {
        MapOverlay overlay = _ui.Overlay;
        overlay.Clear();
        Vector3 cameraPosition = _camera.GlobalPosition;
        foreach (BodyView view in _views)
        {
            Body body = view.Body;
            Vector3 world = view.Pivot.GlobalPosition;
            if (_camera.IsPositionBehind(world))
            {
                continue;
            }
            if (body.Kind == BodyKind.Moon)
            {
                // Moons are labelled only when their planet is close to the camera.
                Vector3 planet = _views[body.Parent].Pivot.GlobalPosition;
                if (planet.DistanceTo(cameraPosition) > 14 * _views[body.Parent].Radius + 6)
                {
                    continue;
                }
            }
            Vector2 p = _camera.UnprojectPosition(body.Kind == BodyKind.Belt ? world + new Vector3((float)MapRoot(body.Orbit.SemiMajorAxis), 0, 0) : world);
            Color colour = body.Kind switch
            {
                BodyKind.Star => new Color(1f, 0.85f, 0.6f, 0.9f),
                BodyKind.Moon => new Color(0.7f, 0.8f, 0.9f, 0.7f),
                BodyKind.Belt => new Color(0.75f, 0.7f, 0.6f, 0.6f),
                _ => new Color(0.85f, 0.92f, 1f, 0.85f),
            };
            string label = body.Kind == BodyKind.Planet || body.Kind == BodyKind.Moon || body.Kind == BodyKind.Belt
                ? (body.Name.StartsWith(_system!.Name, StringComparison.Ordinal) ? body.Designation : body.Name)
                : body.Name;
            float radiusPixels = ScreenRadius(view);
            overlay.Texts.Add(new MapOverlay.Text(p + new Vector2(0, radiusPixels + 16), label, colour, body.Kind == BodyKind.Moon ? 11 : 13, true));
            if (_selected == body.Index)
            {
                overlay.Rings.Add(new MapOverlay.Ring(p, radiusPixels + 8, Ui.Warm, 2));
            }
        }
        overlay.QueueRedraw();
    }

    private float ScreenRadius(BodyView view)
    {
        Vector3 world = view.Pivot.GlobalPosition;
        float distance = Math.Max(world.DistanceTo(_camera.GlobalPosition), 0.001f);
        float viewportHeight = GetViewport().GetVisibleRect().Size.Y;
        return view.Radius / (distance * Mathf.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f)) * viewportHeight * 0.5f;
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _targetDistance = Math.Max(_targetDistance * 0.85, MinDistance());
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _targetDistance = Math.Min(_targetDistance / 0.85, SystemRadius * 6);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _rotating = true;
                    _dragged = false;
                    _pressPosition = button.Position;
                    if (button.DoubleClick && Pick(button.Position) is int target)
                    {
                        Select(target);
                        FocusOn(target);
                    }
                }
                else
                {
                    _rotating = false;
                    if (!_dragged && Pick(button.Position) is int picked)
                    {
                        Select(picked);
                    }
                }
                break;
            case InputEventMouseMotion motion when _rotating:
                _yaw -= motion.Relative.X * 0.005;
                _pitch = Math.Clamp(_pitch + motion.Relative.Y * 0.005, -1.5, 1.5);
                if (motion.Position.DistanceTo(_pressPosition) > 4)
                {
                    _dragged = true;
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Escape:
                    case Key.Backspace:
                        BackRequested?.Invoke();
                        break;
                    case Key.Space:
                        _paused = !_paused;
                        break;
                    case Key.Comma:
                        _speedIndex = Math.Max(0, _speedIndex - 1);
                        break;
                    case Key.Period:
                        _speedIndex = Math.Min(Speeds.Length - 1, _speedIndex + 1);
                        break;
                    case Key.L:
                        ToggleScale();
                        break;
                }
                break;
        }
    }

    private double MinDistance() => Math.Max(_views.Count > 0 ? _views[_focus].Radius * 3 : 1, 0.3);

    private int? Pick(Vector2 screen)
    {
        int? best = null;
        float bestDistance = float.MaxValue;
        foreach (BodyView view in _views)
        {
            Vector3 world = view.Body.Kind == BodyKind.Belt ? view.Pivot.GlobalPosition + new Vector3((float)MapRoot(view.Body.Orbit.SemiMajorAxis), 0, 0) : view.Pivot.GlobalPosition;
            if (_camera.IsPositionBehind(world))
            {
                continue;
            }
            float d = _camera.UnprojectPosition(world).DistanceTo(screen);
            float reach = ScreenRadius(view) + 12;
            if (d < reach && d < bestDistance)
            {
                bestDistance = d;
                best = view.Body.Index;
            }
        }
        return best;
    }

    private void Select(int index)
    {
        _selected = index;
        _ui.ShowBody(_system!, _system!.Bodies[index]);
    }

    private void FocusOn(int index)
    {
        _focus = index;
        BodyView view = _views[index];
        _targetDistance = view.Body.Kind switch
        {
            BodyKind.Star when index == 0 => SystemRadius * 1.6,
            BodyKind.Belt => SystemRadius * 1.2,
            _ => Math.Max(view.Radius * 14, 2.5),
        };
        if (view.Body.Kind == BodyKind.Belt)
        {
            _focus = 0;
        }
    }
}
