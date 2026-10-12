using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Items;

/// <summary>
/// The model viewer (<c>--view models</c>, GROUND.md §9). By default every catalogue model stands on the ground in
/// rows by size, beside a 1.8 m figure for scale; <c>--model &lt;key&gt;</c> (or Tab) shows one model on a turntable.
/// Keys: Tab lineup or turntable, Left/Right the model, Up/Down the depletion stage, 0/1/2 force a LOD, A automatic
/// LODs by distance, Space pauses the turntable, the mouse wheel zooms. Options: <c>--stage n</c>, <c>--lod n</c>,
/// <c>--yaw deg</c> (the turntable's angle, paused, for a screenshot).
/// </summary>
public partial class ModelViewer : Node3D
{
    public Dictionary<string, string> Options { get; set; } = new();

    public bool ReadyForShot => _frames > 20;

    private readonly List<ModelEntry> _entries = new();
    private Node3D _content = null!;
    private Camera3D _camera = null!;
    private Label _title = null!;
    private Label _detail = null!;
    private bool _turntable;
    private int _index;
    private int _stage;
    private int _lod = -1;
    private bool _paused;
    private float _angle = -35;
    private float _zoom = 1;
    private int _frames;
    private Node3D? _shown;
    private Aabb _shownBounds;

    public override void _Ready()
    {
        _entries.AddRange(ModelCatalog.Entries.Where(e => e.Files.Count > 0));
        BuildWorld();
        BuildHud();
        if (Options.TryGetValue("stage", out string? stage))
        {
            _stage = int.Parse(stage, CultureInfo.InvariantCulture);
        }
        if (Options.TryGetValue("lod", out string? lod))
        {
            _lod = int.Parse(lod, CultureInfo.InvariantCulture);
        }
        if (Options.TryGetValue("yaw", out string? yaw))
        {
            _angle = float.Parse(yaw, CultureInfo.InvariantCulture);
            _paused = true;
        }
        if (Options.TryGetValue("model", out string? key))
        {
            _index = Math.Max(0, _entries.FindIndex(e => e.Key == key));
            _turntable = true;
        }
        Rebuild();
    }

    private void BuildWorld()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.36f, 0.52f, 0.74f),
                SkyHorizonColor = new Color(0.72f, 0.77f, 0.82f),
                GroundHorizonColor = new Color(0.55f, 0.55f, 0.52f),
                GroundBottomColor = new Color(0.25f, 0.24f, 0.22f),
            } },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.8f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            SsaoEnabled = true,
            SsaoRadius = 0.6f,
            SsaoIntensity = 1.6f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.25f,
            LightColor = new Color(1f, 0.96f, 0.9f),
            ShadowEnabled = true,
            LightAngularDistance = 0.8f,
            DirectionalShadowMaxDistance = 70,
        };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-0.55f, 1f, 0.75f), Vector3.Zero, Vector3.Up);
        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(400, 400) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.37f, 0.33f), Roughness = 1 },
        };
        AddChild(ground);
        _content = new Node3D { Name = "Content" };
        AddChild(_content);
        _camera = new Camera3D { Name = "Camera", Fov = 45, Current = true, Near = 0.02f, Far = 600 };
        AddChild(_camera);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        (PanelContainer card, VBoxContainer content) = Ui.Card();
        Ui.Place(card, Control.LayoutPreset.TopLeft);
        layer.AddChild(card);
        _title = Ui.Label("Models", TextStyle.Title);
        _detail = Ui.Label("", TextStyle.Secondary);
        content.AddChild(_title);
        content.AddChild(_detail);
        content.AddChild(Ui.KeyHints(("Tab", "lineup / turntable"), ("← →", "model"), ("↑ ↓", "stage"), ("0 1 2", "LOD"),
            ("A", "auto LOD"), ("Space", "pause")));
    }

    // ---------------------------------------------------------------- layout

    private void Rebuild()
    {
        foreach (Node child in _content.GetChildren())
        {
            child.QueueFree();
        }
        _shown = null;
        if (_turntable)
        {
            ShowOne();
        }
        else
        {
            ShowLineup();
        }
        UpdateHud();
    }

    private void ShowOne()
    {
        ModelEntry entry = _entries[_index];
        int stage = Math.Clamp(_stage, 0, entry.Stages - 1);
        Node3D? model = ModelCatalog.Instantiate(entry.Key, stage);
        if (model == null)
        {
            return;
        }
        var pivot = new Node3D { Name = "Turntable" };
        _content.AddChild(pivot);
        Aabb bounds = ModelCatalog.LocalBounds(model);
        if (entry.Class == "vehicle")
        {
            float lift = 0.45f;
            pivot.AddChild(Stand(bounds, lift));
            model.Position = new Vector3(0, lift, 0);
        }
        else if (!IsPlaced(entry) && !IsUpright(entry))
        {
            Rest(model, bounds);
        }
        else
        {
            model.Position = new Vector3(0, -bounds.Position.Y, 0);
        }
        pivot.AddChild(model);
        ApplyLod(model);
        _shown = pivot;
        _shownBounds = ModelCatalog.LocalBounds(model);
        _shownBounds.Position += model.Position;
    }

    private void ShowLineup()
    {
        // Groups from the camera outward, each wrapped into rows: small items, larger items, deployables and doors,
        // outcrops. The ship has its own shot (--model tin-finch); --lineup items|world shows one half.
        string only = Options.TryGetValue("lineup", out string? l) ? l : "all";
        var groups = new List<List<(ModelEntry Entry, Node3D Model, Aabb Bounds)>> { new(), new(), new(), new() };
        foreach (ModelEntry entry in _entries)
        {
            if (entry.Class == "vehicle" || (only == "items" && !entry.IsItem) || (only == "world" && entry.IsItem && !IsPlaced(entry)))
            {
                continue;
            }
            for (int stage = 0; stage < entry.Stages; stage++)
            {
                Node3D? model = ModelCatalog.Instantiate(entry.Key, stage);
                if (model == null)
                {
                    continue;
                }
                Aabb bounds = ModelCatalog.LocalBounds(model);
                if (!IsPlaced(entry) && !IsUpright(entry))
                {
                    bounds = Rest(model, bounds);
                }
                else
                {
                    model.Position = new Vector3(0, -bounds.Position.Y, 0);
                    bounds.Position += model.Position;
                }
                float size = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
                int group = entry.Class == "node" ? 3 : IsPlaced(entry) ? 2 : size < 0.36f ? 0 : 1;
                if (only == "world" && group < 2)
                {
                    continue;
                }
                groups[group].Add((entry, model, bounds));
            }
        }
        // Lower things in front of taller ones within each group.
        foreach (var group in groups)
        {
            group.Sort((a, b) => a.Bounds.Size.Y.CompareTo(b.Bounds.Size.Y));
        }
        float[] widths = { 3.2f, 6.5f, 11.5f, 13.5f };
        float[] gaps = { 0.08f, 0.16f, 0.45f, 0.4f };
        float z = 0;
        Aabb all = default;
        bool any = false;
        bool figure = false;
        for (int g = 0; g < groups.Count; g++)
        {
            var row = new List<(ModelEntry Entry, Node3D Model, Aabb Bounds)>();
            void Flush()
            {
                if (row.Count == 0)
                {
                    return;
                }
                float depth = row.Max(m => m.Bounds.Size.Z);
                float width = row.Sum(m => m.Bounds.Size.X) + gaps[g] * (row.Count - 1) + (g == 2 && !figure ? 0.9f : 0);
                float x = -width / 2;
                z -= depth / 2;
                foreach ((ModelEntry _, Node3D model, Aabb bounds) in row)
                {
                    model.Position += new Vector3(x - bounds.Position.X, 0, z - bounds.GetCenter().Z);
                    _content.AddChild(model);
                    ApplyLod(model);
                    Aabb placed = new(bounds.Position + new Vector3(x - bounds.Position.X, 0, z - bounds.GetCenter().Z), bounds.Size);
                    all = any ? all.Merge(placed) : placed;
                    any = true;
                    x += bounds.Size.X + gaps[g];
                }
                if (g == 2 && !figure)
                {
                    // A 1.8 m figure for scale, at the end of the first row of deployables.
                    Node3D person = Figure();
                    person.Position = new Vector3(x + 0.35f, 0, z);
                    _content.AddChild(person);
                    figure = true;
                }
                z -= depth / 2 + gaps[g] * 1.5f;
                row.Clear();
            }
            foreach (var item in groups[g])
            {
                float used = row.Sum(m => m.Bounds.Size.X) + gaps[g] * row.Count;
                if (row.Count > 0 && used + item.Bounds.Size.X > widths[g])
                {
                    Flush();
                }
                row.Add(item);
            }
            Flush();
            z -= g == 0 ? 0.2f : 0.6f;
        }
        if (!any)
        {
            return;
        }
        // Frame the whole layout from in front and above: the nearest distance at which every corner is in view.
        float pitch = Mathf.DegToRad(Options.TryGetValue("pitch", out string? p) ? float.Parse(p, CultureInfo.InvariantCulture) : 44f);
        Vector3 centre = all.GetCenter();
        Vector3 target = new(centre.X, all.Position.Y + all.Size.Y * 0.15f, centre.Z);
        var forward = new Vector3(0, -MathF.Sin(pitch), -MathF.Cos(pitch));
        Vector3 right = Vector3.Right, up = right.Cross(forward).Normalized();
        float tanV = MathF.Tan(Mathf.DegToRad(_camera.Fov) / 2), tanH = tanV * 16f / 9f;
        float lo = 0.5f, hi = 200f;
        for (int i = 0; i < 40; i++)
        {
            float d = (lo + hi) / 2;
            Vector3 eye = target - forward * d;
            bool fits = true;
            for (int c = 0; c < 8 && fits; c++)
            {
                Vector3 v = all.GetEndpoint(c) - eye;
                float depth = v.Dot(forward);
                fits = depth > 0.1f && MathF.Abs(v.Dot(right)) <= depth * tanH * 0.96f && MathF.Abs(v.Dot(up)) <= depth * tanV * 0.94f;
            }
            if (fits)
            {
                hi = d;
            }
            else
            {
                lo = d;
            }
        }
        _camera.Position = target - forward * hi;
        _camera.LookAt(target, Vector3.Up);
    }

    /// <summary>Things that stand in the world as they are placed (deployables, doors, outcrops...).</summary>
    private static bool IsPlaced(ModelEntry entry) => !entry.IsItem || entry.Class is "deployable" or "container" or "door" or "building";

    /// <summary>Carried items that keep their upright pose on the ground (tools, weapons and the like lie down).</summary>
    private static bool IsUpright(ModelEntry entry) => entry.Class is "resource" or "food" or "medical" or "ammo" or "clothing";

    /// <summary>Lays an item down: its longest side across the view, its thinnest side up, resting on the ground.</summary>
    private static Aabb Rest(Node3D model, Aabb bounds)
    {
        Vector3 s = bounds.Size;
        Basis basis = Basis.Identity;
        int thin = s.X <= s.Y && s.X <= s.Z ? 0 : s.Y <= s.Z ? 1 : 2;
        if (thin == 0)
        {
            basis = new Basis(Vector3.Forward, MathF.PI / 2);
        }
        else if (thin == 2)
        {
            basis = new Basis(Vector3.Right, -MathF.PI / 2);
        }
        Aabb turned = new Transform3D(basis, Vector3.Zero) * bounds;
        if (turned.Size.Z > turned.Size.X)
        {
            basis = new Basis(Vector3.Up, MathF.PI / 2) * basis;
            turned = new Transform3D(basis, Vector3.Zero) * bounds;
        }
        model.Basis = basis;
        model.Position = new Vector3(0, -turned.Position.Y, 0);
        turned.Position += model.Position;
        return turned;
    }

    /// <summary>A plain stand under a vehicle: a steel deck on four blocks, <paramref name="lift"/> high.</summary>
    private static Node3D Stand(Aabb bounds, float lift)
    {
        var stand = new Node3D { Name = "Stand" };
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.28f, 0.3f, 0.31f), Metallic = 0.6f, Roughness = 0.55f };
        var concrete = new StandardMaterial3D { AlbedoColor = new Color(0.52f, 0.51f, 0.48f), Roughness = 0.95f };
        float w = bounds.Size.X * 0.92f, d = bounds.Size.Z * 0.78f;
        Vector3 c = bounds.GetCenter();
        var deck = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(w, 0.08f, d) }, MaterialOverride = steel };
        deck.Position = new Vector3(c.X, lift - 0.04f, c.Z);
        stand.AddChild(deck);
        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                var block = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, lift - 0.08f, 0.5f) }, MaterialOverride = concrete };
                block.Position = new Vector3(c.X + sx * (w / 2 - 0.35f), (lift - 0.08f) / 2, c.Z + sz * (d / 2 - 0.35f));
                stand.AddChild(block);
            }
        }
        return stand;
    }

    /// <summary>A plain 1.8 m figure for scale.</summary>
    private static Node3D Figure()
    {
        var figure = new MeshInstance3D
        {
            Name = "Figure",
            Mesh = new CapsuleMesh { Radius = 0.22f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.6f, 0.56f), Roughness = 0.8f },
        };
        var holder = new Node3D { Name = "Figure" };
        figure.Position = new Vector3(0, 0.9f, 0);
        holder.AddChild(figure);
        return holder;
    }

    private void ApplyLod(Node3D model)
    {
        if (_lod >= 0)
        {
            ModelCatalog.ForceLod(model, _lod);
        }
    }

    private void UpdateHud()
    {
        string lod = _lod < 0 ? "LOD by distance" : $"LOD{_lod}";
        if (!_turntable)
        {
            _title.Text = "Models";
            _detail.Text = $"{_entries.Count} models · {lod}";
            return;
        }
        ModelEntry entry = _entries[_index];
        _title.Text = entry.Key;
        string stage = entry.Stages > 1 ? $" · stage {Math.Clamp(_stage, 0, entry.Stages - 1) + 1} of {entry.Stages}" : "";
        string size = _shown == null ? "" : $" · {_shownBounds.Size.X:0.00} × {_shownBounds.Size.Y:0.00} × {_shownBounds.Size.Z:0.00} m";
        _detail.Text = $"{entry.Class} · {entry.Source}{stage} · {lod}{size}";
    }

    // ---------------------------------------------------------------- input and turntable

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp)
            {
                _zoom = Math.Max(0.25f, _zoom * 0.9f);
            }
            else if (wheel.ButtonIndex == MouseButton.WheelDown)
            {
                _zoom = Math.Min(4f, _zoom / 0.9f);
            }
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Tab:
                _turntable = !_turntable;
                break;
            case Key.Right:
                _index = (_index + 1) % _entries.Count;
                _stage = 0;
                _turntable = true;
                break;
            case Key.Left:
                _index = (_index + _entries.Count - 1) % _entries.Count;
                _stage = 0;
                _turntable = true;
                break;
            case Key.Up:
                _stage++;
                break;
            case Key.Down:
                _stage = Math.Max(0, _stage - 1);
                break;
            case Key.Key0 or Key.Key1 or Key.Key2:
                _lod = (int)(key.Keycode - Key.Key0);
                break;
            case Key.A:
                _lod = -1;
                break;
            case Key.Space:
                _paused = !_paused;
                return;
            default:
                return;
        }
        Rebuild();
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (!_turntable || _shown == null)
        {
            return;
        }
        if (!_paused)
        {
            _angle += (float)delta * 24f;
        }
        _shown.Rotation = new Vector3(0, Mathf.DegToRad(_angle), 0);
        Vector3 size = _shownBounds.Size;
        float radius = Math.Max(0.05f, size.Length() * 0.5f);
        float distance = radius / MathF.Tan(Mathf.DegToRad(_camera.Fov * 0.5f)) * 1.15f * _zoom;
        Vector3 target = new(0, _shownBounds.GetCenter().Y, 0);
        _camera.Position = target + new Vector3(0, distance * 0.38f, distance);
        _camera.LookAt(target, Vector3.Up);
    }
}
