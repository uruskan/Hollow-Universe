using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Monuments;

/// <summary>
/// The monument viewer (<c>--view monuments</c>, MONUMENTS.md §8.5): every monument in <c>data/monuments</c> laid out
/// at its real size on open ground, a row per tier (roadside, small, medium, large, landmarks), each with its kit
/// pieces placed, its pad, a label, and its levels of detail switching by distance as they will in the game.
/// Fly: hold the right mouse button to look, W A S D, Q / E down and up, Shift four times faster, the wheel sets the
/// speed. ← → (or PgUp / PgDn) jump to the previous or next monument, Home shows the whole park, L hides the labels.
/// Options: <c>--focus &lt;key&gt;</c>, <c>--yaw</c>, <c>--pitch</c> and <c>--distance</c> (the jump camera),
/// <c>--park</c> (start over the whole park), <c>--shot</c> (Main saves it once everything is loaded).
/// </summary>
public partial class MonumentViewer : Node3D
{
    public Dictionary<string, string> Options { get; set; } = new();

    public bool ReadyForShot => _loaded && _frames > 40;

    private static readonly string[] TierOrder = { "roadside", "small", "medium", "large", "landmark" };
    private const float RowLimit = 9000f;   // a row longer than this wraps
    private const float PieceLod0 = 90f, PieceLod1 = 360f, PieceLod2 = 2500f;

    private readonly List<Monument> _monuments = new();
    private readonly Dictionary<string, PackedScene?> _scenes = new();
    private readonly HashSet<string> _pending = new();
    private Camera3D _camera = null!;
    private Label _title = null!;
    private Label _detail = null!;
    private Label _speedLabel = null!;
    private Node3D _park = null!;
    private int _index = -1;
    private float _speed = 40f;
    private float _yaw, _pitch;
    private bool _looking;
    private bool _labels = true;
    private bool _loaded;
    private int _frames;
    private int _requested;

    private sealed class Monument
    {
        public string Key = "";
        public string Title = "";
        public string Tier = "";
        public string Model = "";
        public Vector3 Min, Max;
        public float Height;
        public float[] Lods = { 120, 500, 3000 };
        public List<(string Piece, Vector3 At, Quaternion Rot, Color? Tint)> Pieces = new();
        public JsonElement? Pad;
        public Vector3 Origin;      // where it stands in the park
        public Node3D? Node;
        public Label3D? Label;

        public Vector3 Size => Max - Min;
        public Vector3 Centre => Origin + (Min + Max) * 0.5f;
    }

    public override void _Ready()
    {
        BuildWorld();
        BuildHud();
        ReadAll();
        Layout();
        foreach (Monument m in _monuments)
        {
            Request(m.Model);
            foreach (var p in m.Pieces)
            {
                Request(PiecePath(p.Piece));
            }
        }
        _requested = _pending.Count;
        if (Options.ContainsKey("park") || !Options.ContainsKey("focus"))
        {
            ShowPark();
        }
        else
        {
            int i = _monuments.FindIndex(m => m.Key == Options["focus"]);
            Focus(Math.Max(0, i));
        }
        UpdateHud();
    }

    // ---------------------------------------------------------------- data

    private static string PiecePath(string piece) => $"res://assets/monuments/kit/{piece}.glb";

    private void ReadAll()
    {
        foreach (string file in DirAccess.GetFilesAt("res://data/monuments").Where(f => f.EndsWith(".json")).OrderBy(f => f))
        {
            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://data/monuments/" + file));
            JsonElement j = doc.RootElement;
            if (!j.TryGetProperty("model", out JsonElement model))
            {
                continue;
            }
            var m = new Monument
            {
                Key = j.GetProperty("key").GetString() ?? file,
                Title = j.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? "" : file,
                Tier = j.TryGetProperty("tier", out JsonElement tier) ? tier.GetString() ?? "" : "",
                Model = model.GetString() ?? "",
                Height = j.TryGetProperty("height", out JsonElement h) ? h.GetSingle() : 10,
            };
            JsonElement bounds = j.GetProperty("bounds");
            m.Min = Vec(bounds.GetProperty("min"));
            m.Max = Vec(bounds.GetProperty("max"));
            if (j.TryGetProperty("lods", out JsonElement lods) && lods.GetArrayLength() == 3)
            {
                m.Lods = lods.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            }
            if (j.TryGetProperty("pieces", out JsonElement pieces))
            {
                foreach (JsonElement p in pieces.EnumerateArray())
                {
                    JsonElement r = p.GetProperty("rot");
                    var rot = new Quaternion(r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle(), r[0].GetSingle()).Normalized();
                    Color? tint = p.TryGetProperty("tint", out JsonElement c)
                        ? new Color(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle()) : null;
                    m.Pieces.Add((p.GetProperty("piece").GetString() ?? "", Vec(p.GetProperty("at")), rot, tint));
                }
            }
            if (j.TryGetProperty("pad", out JsonElement pad) && pad.ValueKind == JsonValueKind.Object)
            {
                m.Pad = pad.Clone();
            }
            _monuments.Add(m);
        }
        // Tier by tier, smallest first within each.
        _monuments.Sort((a, b) =>
        {
            int ta = Array.IndexOf(TierOrder, a.Tier), tb = Array.IndexOf(TierOrder, b.Tier);
            ta = ta < 0 ? TierOrder.Length : ta;
            tb = tb < 0 ? TierOrder.Length : tb;
            return ta != tb ? ta.CompareTo(tb) : Math.Max(a.Size.X, a.Size.Z).CompareTo(Math.Max(b.Size.X, b.Size.Z));
        });
    }

    private static Vector3 Vec(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());

    // ---------------------------------------------------------------- layout

    private void Layout()
    {
        // Rows run along +X; each new row (and each tier) starts further along -Z, away from the first.
        float z = 0;
        foreach (IGrouping<string, Monument> tier in _monuments.GroupBy(m => m.Tier))
        {
            var row = new List<Monument>();
            float x = 0;
            foreach (Monument m in tier)
            {
                float gap = Math.Max(30f, Math.Max(m.Size.X, m.Size.Z) * 0.15f);
                if (row.Count > 0 && x + gap + m.Size.X > RowLimit)
                {
                    z = PlaceRow(row, z);
                    row.Clear();
                    x = 0;
                }
                m.Origin = new Vector3(x + gap - m.Min.X, 0, 0);
                x = m.Origin.X + m.Max.X;
                row.Add(m);
            }
            z = PlaceRow(row, z) - 250f;
        }
    }

    private float PlaceRow(List<Monument> row, float z)
    {
        float depth = row.Max(m => m.Size.Z);
        foreach (Monument m in row)
        {
            // The row's front edge at z, each monument's front (its max Z) on it.
            m.Origin = new Vector3(m.Origin.X, 0, z - m.Max.Z);
        }
        return z - depth - 120f;
    }

    // ---------------------------------------------------------------- loading

    private void Request(string path)
    {
        if (_scenes.ContainsKey(path) || _pending.Contains(path))
        {
            return;
        }
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"Monument viewer: {path} is missing (run art/monuments/build.py, then import).");
            _scenes[path] = null;
            return;
        }
        ResourceLoader.LoadThreadedRequest(path, "PackedScene", true);
        _pending.Add(path);
    }

    private void PollLoads()
    {
        foreach (string path in _pending.ToArray())
        {
            ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress)
            {
                continue;
            }
            _scenes[path] = status == ResourceLoader.ThreadLoadStatus.Loaded
                ? ResourceLoader.LoadThreadedGet(path) as PackedScene : null;
            _pending.Remove(path);
        }
        if (_pending.Count == 0 && !_loaded)
        {
            Assemble();
            _loaded = true;
        }
    }

    private void Assemble()
    {
        // The monuments' materials draw the planet's air on a planet; here there is none.
        foreach (string file in DirAccess.GetFilesAt("res://assets/materials").Where(f => f.StartsWith("monument_") && f.EndsWith(".tres")))
        {
            if (ResourceLoader.Load("res://assets/materials/" + file) is ShaderMaterial sm)
            {
                sm.SetShaderParameter("use_air", false);
            }
        }
        foreach (Monument m in _monuments)
        {
            var node = new Node3D { Name = m.Key, Position = m.Origin };
            _park.AddChild(node);
            m.Node = node;
            if (_scenes.GetValueOrDefault(m.Model) is PackedScene scene)
            {
                Node3D model = scene.Instantiate<Node3D>();
                node.AddChild(model);
                SetRanges(model, m.Lods[0], m.Lods[1], m.Lods[2]);
            }
            foreach (var p in m.Pieces)
            {
                if (_scenes.GetValueOrDefault(PiecePath(p.Piece)) is not PackedScene ps)
                {
                    continue;
                }
                Node3D piece = ps.Instantiate<Node3D>();
                piece.Position = p.At;
                piece.Quaternion = p.Rot;
                node.AddChild(piece);
                SetRanges(piece, PieceLod0, PieceLod1, Math.Max(PieceLod2, m.Lods[2] * 0.5f));
                if (p.Tint is Color tint)
                {
                    foreach (GeometryInstance3D g in Meshes(piece))
                    {
                        g.SetInstanceShaderParameter("piece_tint", tint);
                    }
                }
            }
            AddPad(node, m);
            AddLabel(node, m);
        }
        UpdateHud();
    }

    private static IEnumerable<GeometryInstance3D> Meshes(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is GeometryInstance3D g)
            {
                yield return g;
            }
            foreach (GeometryInstance3D d in Meshes(child))
            {
                yield return d;
            }
        }
    }

    /// <summary>The model's levels by distance, as in the game: level 0 to <paramref name="d0"/>, level 1 to
    /// <paramref name="d1"/>, level 2 to <paramref name="d2"/>; a little overlap so the switch doesn't flicker.</summary>
    private static void SetRanges(Node root, float d0, float d1, float d2)
    {
        foreach (GeometryInstance3D g in Meshes(root))
        {
            string name = g.Name.ToString().ToLowerInvariant();
            int level = name.EndsWith("_lod0") ? 0 : name.EndsWith("_lod1") ? 1 : name.EndsWith("_lod2") ? 2 : -1;
            if (level < 0)
            {
                continue;
            }
            g.VisibilityRangeBegin = level == 0 ? 0 : level == 1 ? d0 : d1;
            g.VisibilityRangeEnd = level == 0 ? d0 : level == 1 ? d1 : d2;
            g.VisibilityRangeBeginMargin = level == 0 ? 0 : g.VisibilityRangeBegin * 0.05f;
            g.VisibilityRangeEndMargin = g.VisibilityRangeEnd * 0.05f;
        }
    }

    private static void AddPad(Node3D node, Monument m)
    {
        if (m.Pad is not JsonElement pad || !pad.TryGetProperty("shape", out JsonElement shape))
        {
            return;
        }
        string ground = pad.TryGetProperty("ground", out JsonElement g) ? g.GetString() ?? "gravel" : "gravel";
        Color colour = ground switch
        {
            "concrete" => new Color(0.46f, 0.45f, 0.43f),
            "asphalt" => new Color(0.17f, 0.17f, 0.18f),
            "dirt" => new Color(0.34f, 0.27f, 0.2f),
            "sand" => new Color(0.62f, 0.55f, 0.42f),
            "snow" => new Color(0.86f, 0.88f, 0.92f),
            _ => new Color(0.38f, 0.36f, 0.33f),
        };
        Vector2[] outline;
        if (shape.TryGetProperty("rect", out JsonElement rect))
        {
            float w = rect[0].GetSingle() / 2, d = rect[1].GetSingle() / 2;
            outline = new[] { new Vector2(-w, -d), new Vector2(w, -d), new Vector2(w, d), new Vector2(-w, d) };
        }
        else if (shape.TryGetProperty("circle", out JsonElement circle))
        {
            float r = circle.GetSingle();
            outline = Enumerable.Range(0, 48).Select(i => new Vector2(r * Mathf.Cos(Mathf.Tau * i / 48), r * Mathf.Sin(Mathf.Tau * i / 48))).ToArray();
        }
        else if (shape.TryGetProperty("poly", out JsonElement poly))
        {
            outline = poly.EnumerateArray().Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray();
        }
        else
        {
            return;
        }
        int[] tris = Geometry2D.TriangulatePolygon(outline);
        if (tris.Length == 0)
        {
            return;
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        // TriangulatePolygon winds as the outline does; the pad must face up whatever the outline's direction.
        bool clockwise = Geometry2D.IsPolygonClockwise(outline);
        for (int i = 0; i < tris.Length; i += 3)
        {
            int[] order = clockwise ? new[] { 0, 1, 2 } : new[] { 0, 2, 1 };
            foreach (int k in order)
            {
                Vector2 p = outline[tris[i + k]];
                st.AddVertex(new Vector3(p.X, 0.03f, p.Y));
            }
        }
        node.AddChild(new MeshInstance3D
        {
            Name = "Pad",
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private void AddLabel(Node3D node, Monument m)
    {
        var label = new Label3D
        {
            Name = "Label",
            Text = $"{m.Title}\n{m.Size.X:0} × {m.Size.Z:0} m, {m.Height:0} m tall",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            PixelSize = 0.0006f,
            FontSize = 36,
            OutlineSize = 10,
            Font = Ui.Medium,
            Modulate = new Color(1, 1, 1),
            OutlineModulate = new Color(0, 0, 0, 0.8f),
            NoDepthTest = true,
            Position = new Vector3((m.Min.X + m.Max.X) / 2, m.Height + Math.Max(6f, m.Height * 0.08f), (m.Min.Z + m.Max.Z) / 2),
            VisibilityRangeEnd = Math.Max(1500f, Math.Max(m.Size.X, m.Size.Z) * 12f),
        };
        node.AddChild(label);
        m.Label = label;
    }

    // ---------------------------------------------------------------- world and interface

    private void BuildWorld()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.30f, 0.47f, 0.72f),
                SkyHorizonColor = new Color(0.70f, 0.77f, 0.84f),
                GroundHorizonColor = new Color(0.55f, 0.6f, 0.55f),
                GroundBottomColor = new Color(0.2f, 0.24f, 0.18f),
                SunAngleMax = 20,
            } },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.75f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.05f,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.5f,
            // Distance haze, so a monument kilometres away sits in the air as it will on a planet.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogLightColor = new Color(0.68f, 0.76f, 0.86f),
            FogDensity = 0.00009f,
            FogSkyAffect = 0.0f,
            FogAerialPerspective = 0.6f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.3f,
            LightColor = new Color(1f, 0.95f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 1800,
            DirectionalShadowBlendSplits = true,
        };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-0.5f, 0.85f, 0.7f), Vector3.Zero, Vector3.Up);
        var ground = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.36f, 0.43f, 0.24f),
            AlbedoTexture = new NoiseTexture2D { Width = 512, Height = 512, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.02f } },
            Roughness = 1,
            Uv1Scale = new Vector3(400, 400, 1),
        };
        AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(120000, 120000) },
            MaterialOverride = ground,
            Position = new Vector3(0, -0.02f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _park = new Node3D { Name = "Park" };
        AddChild(_park);
        _camera = new Camera3D { Name = "Camera", Fov = 55, Current = true, Near = 0.1f, Far = 150000 };
        AddChild(_camera);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        (PanelContainer card, VBoxContainer content) = Ui.Card();
        Ui.Place(card, Control.LayoutPreset.TopLeft);
        layer.AddChild(card);
        _title = Ui.Label("Monuments", TextStyle.Title);
        _detail = Ui.Label("", TextStyle.Secondary);
        _speedLabel = Ui.Label("", TextStyle.Secondary);
        content.AddChild(_title);
        content.AddChild(_detail);
        content.AddChild(_speedLabel);
        content.AddChild(Ui.KeyHints(("RMB", "look"), ("W A S D", "fly"), ("Q E", "down / up"), ("Shift", "faster"),
            ("Wheel", "speed"), ("← →", "monument"), ("Home", "whole park"), ("L", "labels")));
    }

    private void UpdateHud()
    {
        if (!_loaded)
        {
            _title.Text = "Monuments";
            _detail.Text = $"Loading {_requested - _pending.Count} of {_requested}";
        }
        else if (_index >= 0 && _index < _monuments.Count)
        {
            Monument m = _monuments[_index];
            _title.Text = m.Title;
            _detail.Text = $"{Capital(m.Tier)} · {m.Size.X:0} × {m.Size.Z:0} m, {m.Height:0} m tall · {_index + 1} of {_monuments.Count}";
        }
        else
        {
            _title.Text = "Monuments";
            _detail.Text = $"{_monuments.Count} monuments, a row per tier";
        }
        _speedLabel.Text = $"Speed {_speed:0} m/s";
    }

    private static string Capital(string s) => s.Length == 0 ? "Other" : char.ToUpperInvariant(s[0]) + s[1..];

    // ---------------------------------------------------------------- camera

    private void Focus(int index)
    {
        if (_monuments.Count == 0)
        {
            return;
        }
        _index = (index % _monuments.Count + _monuments.Count) % _monuments.Count;
        Monument m = _monuments[_index];
        float size = Math.Max(Math.Max(m.Size.X, m.Size.Z), m.Height * 1.4f);
        float distance = Number("distance", size * 1.15f + 25f);
        float yaw = Number("yaw", 35f), pitch = Number("pitch", 22f);
        Vector3 target = m.Centre with { Y = m.Height * 0.3f };
        Place(target, distance, yaw, pitch);
        _speed = Math.Clamp(size * 0.25f, 10f, 2000f);
        UpdateHud();
    }

    private void ShowPark()
    {
        _index = -1;
        Vector3 lo = new(float.MaxValue, 0, float.MaxValue), hi = new(float.MinValue, 0, float.MinValue);
        foreach (Monument m in _monuments)
        {
            lo = new Vector3(Math.Min(lo.X, m.Origin.X + m.Min.X), 0, Math.Min(lo.Z, m.Origin.Z + m.Min.Z));
            hi = new Vector3(Math.Max(hi.X, m.Origin.X + m.Max.X), 0, Math.Max(hi.Z, m.Origin.Z + m.Max.Z));
        }
        Vector3 centre = (lo + hi) / 2;
        float extent = Math.Max(hi.X - lo.X, hi.Z - lo.Z);
        Place(centre, extent * 0.75f, Number("yaw", 20f), Number("pitch", 35f));
        _speed = 600f;
        UpdateHud();
    }

    private void Place(Vector3 target, float distance, float yawDeg, float pitchDeg)
    {
        _yaw = Mathf.DegToRad(yawDeg);
        _pitch = -Mathf.DegToRad(pitchDeg);
        Vector3 back = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch) * Vector3.Back;
        _camera.Position = target + back * distance;
        ApplyRotation();
    }

    private void ApplyRotation() =>
        _camera.Basis = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    private float Number(string key, float fallback) =>
        Options.TryGetValue(key, out string? v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
            ? f : fallback;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _looking = mb.Pressed;
                Input.MouseMode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                _speed = Math.Min(_speed * 1.25f, 5000f);
                UpdateHud();
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                _speed = Math.Max(_speed / 1.25f, 1f);
                UpdateHud();
            }
        }
        else if (e is InputEventMouseMotion mm && _looking)
        {
            _yaw -= mm.Relative.X * 0.0025f;
            _pitch = Math.Clamp(_pitch - mm.Relative.Y * 0.0025f, -1.55f, 1.55f);
            ApplyRotation();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Right or Key.Pagedown:
                    Focus(_index + 1);
                    break;
                case Key.Left or Key.Pageup:
                    Focus(_index - 1);
                    break;
                case Key.Home:
                    ShowPark();
                    break;
                case Key.L:
                    _labels = !_labels;
                    foreach (Monument m in _monuments)
                    {
                        if (m.Label != null)
                        {
                            m.Label.Visible = _labels;
                        }
                    }
                    break;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_loaded)
        {
            PollLoads();
            UpdateHud();
        }
        else
        {
            _frames++;
        }
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) move += Vector3.Forward;
        if (Input.IsKeyPressed(Key.S)) move += Vector3.Back;
        if (Input.IsKeyPressed(Key.A)) move += Vector3.Left;
        if (Input.IsKeyPressed(Key.D)) move += Vector3.Right;
        if (Input.IsKeyPressed(Key.E)) move += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) move += Vector3.Down;
        if (move != Vector3.Zero)
        {
            float speed = _speed * (Input.IsKeyPressed(Key.Shift) ? 4f : 1f);
            Vector3 world = _camera.Basis * move.Normalized();
            _camera.Position += world * speed * (float)delta;
        }
    }
}
