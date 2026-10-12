using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Items;

/// <summary>A position (metres) and a rotation (degrees, Godot's Euler order YXZ) from the catalogue.</summary>
public readonly record struct Pose(Vector3 Position, Vector3 RotationDegrees)
{
    public static readonly Pose Identity = new(Vector3.Zero, Vector3.Zero);

    public Transform3D Transform => new(Basis.FromEuler(RotationDegrees * (MathF.PI / 180f)), Position);
}

/// <summary>
/// How an item sits in a hand (GROUND.md §9): its pivot is the grip of <see cref="Hand"/> ("right", or "left" for the
/// bow). <see cref="FirstPerson"/> places the grip in camera space; <see cref="ThirdPerson"/> places the item on the
/// hand's attach point; <see cref="OffHand"/> names the socket the other hand holds, if any.
/// </summary>
public sealed record HeldInfo(Pose FirstPerson, Pose ThirdPerson, string? OffHand, string Hand);

/// <summary>One model of <c>data/ground/models.json</c>: an item key or a world-object kind.</summary>
public sealed record ModelEntry(
    string Key,
    string Kind,
    string Class,
    IReadOnlyList<string> Files,
    int Lods,
    bool Collision,
    string? IconKey,
    Vector3? IconView,
    HeldInfo? Held,
    float DropScale,
    float[]? LodDistances,
    string Source,
    string? StandIn)
{
    public bool IsItem => Kind == "item";

    /// <summary>Resource nodes have one file per depletion stage; everything else has one.</summary>
    public int Stages => Files.Count;
}

/// <summary>What a model instance is for: <see cref="Display"/> drops the collision hulls, <see cref="Static"/>
/// gathers them under one StaticBody3D.</summary>
public enum ModelUse
{
    Display,
    Static,
}

/// <summary>
/// The model catalogue (GROUND.md §9): which GLB, icon, grip, drop scale, LODs and collision each item key and
/// world-object kind has. Read from <c>res://data/ground/models.json</c> once.
/// </summary>
public static class ModelCatalog
{
    public const string DataPath = "res://data/ground/models.json";

    private static List<ModelEntry>? _entries;
    private static Dictionary<string, ModelEntry>? _byKey;
    private static string _iconPattern = "res://assets/items/icons/{size}/{key}.png";
    private static int[] _iconSizes = { 128, 256 };

    public static IReadOnlyList<ModelEntry> Entries
    {
        get
        {
            Load();
            return _entries!;
        }
    }

    public static IReadOnlyList<int> IconSizes
    {
        get
        {
            Load();
            return _iconSizes;
        }
    }

    public static ModelEntry? Find(string key)
    {
        Load();
        return _byKey!.TryGetValue(key, out ModelEntry? entry) ? entry : null;
    }

    /// <summary>The icon PNG of <paramref name="iconKey"/> rendered at <paramref name="size"/> pixels.</summary>
    public static string IconPath(string iconKey, int size) =>
        _iconPattern.Replace("{size}", size.ToString(CultureInfo.InvariantCulture)).Replace("{key}", iconKey);

    /// <summary>
    /// A new instance of a model: LOD meshes switch by distance (visibility ranges), hull bodies follow
    /// <paramref name="use"/>. Null when the key is unknown or the file is missing.
    /// </summary>
    public static Node3D? Instantiate(string key, int stage = 0, ModelUse use = ModelUse.Display)
    {
        ModelEntry? entry = Find(key);
        if (entry == null || entry.Files.Count == 0)
        {
            return null;
        }
        string file = entry.Files[Math.Clamp(stage, 0, entry.Files.Count - 1)];
        PackedScene? scene = ResourceLoader.Exists(file) ? ResourceLoader.Load<PackedScene>(file) : null;
        if (scene == null)
        {
            GD.PushWarning($"ModelCatalog: {key}: cannot load {file}");
            return null;
        }
        Node3D root = scene.Instantiate<Node3D>();
        root.Name = key;
        Collision(root, use);
        float[] distances = entry.LodDistances ?? DefaultLodDistances(LocalBounds(root, 0));
        SetLodRanges(root, distances);
        return root;
    }

    /// <summary>LOD switch distances from a model's size: LOD1, LOD2, and where it disappears (0: never).</summary>
    public static float[] DefaultLodDistances(Aabb bounds)
    {
        float r = Math.Max(0.05f, bounds.Size.Length() * 0.5f);
        return new[] { MathF.Max(6f, 22f * r), MathF.Max(16f, 60f * r), r > 2.5f ? 0f : MathF.Max(45f, 220f * r) };
    }

    /// <summary>
    /// Visibility ranges for every <c>*_lod{n}</c> mesh: LOD n shows from distances[n-1] to distances[n]; the last
    /// LOD shows until the last distance (0: always).
    /// </summary>
    public static void SetLodRanges(Node root, float[] distances)
    {
        int last = LastLod(root);
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            int lod = LodOf(mesh);
            if (lod < 0)
            {
                continue;
            }
            float begin = lod == 0 ? 0 : distances[Math.Min(lod - 1, distances.Length - 1)];
            float end = lod >= last ? distances[^1] : distances[Math.Min(lod, distances.Length - 1)];
            // No margins: with fading off they act as hysteresis, and a model created inside a margin band would show
            // no level at all until the camera moved across it.
            mesh.VisibilityRangeBegin = begin;
            mesh.VisibilityRangeEnd = end;
            mesh.VisibilityRangeBeginMargin = 0;
            mesh.VisibilityRangeEndMargin = 0;
            mesh.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
        }
    }

    /// <summary>Shows only LOD <paramref name="lod"/> at every distance (viewer and icons).</summary>
    public static void ForceLod(Node root, int lod)
    {
        int last = LastLod(root);
        int shown = Math.Min(lod, last);
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            int l = LodOf(mesh);
            if (l < 0)
            {
                continue;
            }
            mesh.Visible = l == shown;
            mesh.VisibilityRangeBegin = 0;
            mesh.VisibilityRangeEnd = 0;
        }
    }

    /// <summary>The bounds of one LOD's meshes in the root's space.</summary>
    public static Aabb LocalBounds(Node3D root, int lod = 0)
    {
        bool any = false;
        Aabb box = default;
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            if (LodOf(mesh) != lod || mesh.Mesh == null)
            {
                continue;
            }
            Transform3D toRoot = RelativeTransform(root, mesh);
            Aabb b = toRoot * mesh.Mesh.GetAabb();
            box = any ? box.Merge(b) : b;
            any = true;
        }
        return box;
    }

    /// <summary>Every LOD-0 vertex of a model in the root's space (icon framing).</summary>
    public static List<Vector3> Points(Node3D root, int lod = 0)
    {
        var points = new List<Vector3>();
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            if (LodOf(mesh) != lod || mesh.Mesh is not ArrayMesh arrays)
            {
                continue;
            }
            Transform3D toRoot = RelativeTransform(root, mesh);
            for (int s = 0; s < arrays.GetSurfaceCount(); s++)
            {
                var surface = arrays.SurfaceGetArrays(s);
                foreach (Vector3 v in surface[(int)Mesh.ArrayType.Vertex].AsVector3Array())
                {
                    points.Add(toRoot * v);
                }
            }
        }
        return points;
    }

    public static int LodOf(Node node)
    {
        string name = node.Name;
        int at = name.LastIndexOf("_lod", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && int.TryParse(name.AsSpan(at + 4), NumberStyles.None, CultureInfo.InvariantCulture, out int lod) ? lod : -1;
    }

    public static IEnumerable<MeshInstance3D> Meshes(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            if (node is MeshInstance3D mesh)
            {
                yield return mesh;
            }
            foreach (Node child in node.GetChildren())
            {
                stack.Push(child);
            }
        }
    }

    private static int LastLod(Node root)
    {
        int last = 0;
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            last = Math.Max(last, LodOf(mesh));
        }
        return last;
    }

    private static Transform3D RelativeTransform(Node3D root, Node3D node)
    {
        Transform3D t = Transform3D.Identity;
        for (Node? n = node; n != null && n != root; n = n.GetParent())
        {
            if (n is Node3D n3)
            {
                t = n3.Transform * t;
            }
        }
        return t;
    }

    /// <summary>Hull bodies from the GLB's <c>-convcolonly</c> nodes: removed for display, merged for static use.</summary>
    private static void Collision(Node3D root, ModelUse use)
    {
        var bodies = new List<StaticBody3D>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            if (node is StaticBody3D body)
            {
                bodies.Add(body);
                continue;
            }
            foreach (Node child in node.GetChildren())
            {
                stack.Push(child);
            }
        }
        if (bodies.Count == 0)
        {
            return;
        }
        StaticBody3D? merged = null;
        if (use == ModelUse.Static)
        {
            merged = new StaticBody3D { Name = "Collision" };
            root.AddChild(merged);
        }
        foreach (StaticBody3D body in bodies)
        {
            if (merged != null)
            {
                Transform3D toRoot = RelativeTransform(root, body);
                foreach (Node child in body.GetChildren())
                {
                    if (child is CollisionShape3D shape)
                    {
                        Transform3D t = toRoot * shape.Transform;
                        body.RemoveChild(shape);
                        shape.Transform = t;
                        merged.AddChild(shape);
                    }
                }
            }
            body.GetParent().RemoveChild(body);
            body.QueueFree();
        }
    }

    // ---------------------------------------------------------------- loading

    private static void Load()
    {
        if (_entries != null)
        {
            return;
        }
        _entries = new List<ModelEntry>();
        _byKey = new Dictionary<string, ModelEntry>(StringComparer.Ordinal);
        if (!FileAccess.FileExists(DataPath))
        {
            GD.PushWarning($"ModelCatalog: {DataPath} is missing.");
            return;
        }
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(DataPath));
        JsonElement top = doc.RootElement;
        if (top.TryGetProperty("icons", out JsonElement icons))
        {
            _iconPattern = icons.GetProperty("path").GetString() ?? _iconPattern;
            var sizes = new List<int>();
            foreach (JsonElement s in icons.GetProperty("sizes").EnumerateArray())
            {
                sizes.Add(s.GetInt32());
            }
            sizes.Sort();
            _iconSizes = sizes.ToArray();
        }
        foreach (JsonElement m in top.GetProperty("models").EnumerateArray())
        {
            ModelEntry entry = Parse(m);
            _entries.Add(entry);
            _byKey[entry.Key] = entry;
        }
    }

    private static ModelEntry Parse(JsonElement m)
    {
        string key = m.GetProperty("key").GetString()!;
        var files = new List<string>();
        if (m.TryGetProperty("file", out JsonElement file))
        {
            files.Add(file.GetString()!);
        }
        if (m.TryGetProperty("stages", out JsonElement stages))
        {
            foreach (JsonElement s in stages.EnumerateArray())
            {
                files.Add(s.GetString()!);
            }
        }
        string? iconKey = null;
        Vector3? iconView = null;
        if (m.TryGetProperty("icon", out JsonElement icon))
        {
            iconKey = icon.TryGetProperty("key", out JsonElement ik) ? ik.GetString() : key;
            if (icon.TryGetProperty("view", out JsonElement iv))
            {
                iconView = Vec(iv);
            }
        }
        HeldInfo? held = null;
        if (m.TryGetProperty("held", out JsonElement h))
        {
            held = new HeldInfo(PoseOf(h, "firstPerson"), PoseOf(h, "thirdPerson"),
                h.TryGetProperty("offHand", out JsonElement off) ? off.GetString() : null,
                h.TryGetProperty("hand", out JsonElement hand) ? hand.GetString() ?? "right" : "right");
        }
        float[]? lodDistances = null;
        if (m.TryGetProperty("lodDistances", out JsonElement ld))
        {
            var list = new List<float>();
            foreach (JsonElement d in ld.EnumerateArray())
            {
                list.Add(d.GetSingle());
            }
            lodDistances = list.ToArray();
        }
        return new ModelEntry(
            key,
            m.GetProperty("kind").GetString()!,
            m.TryGetProperty("class", out JsonElement c) ? c.GetString()! : "",
            files,
            m.TryGetProperty("lods", out JsonElement lods) ? lods.GetInt32() : 1,
            m.TryGetProperty("collision", out JsonElement col) && col.GetString() != "none",
            iconKey,
            iconView,
            held,
            m.TryGetProperty("drop", out JsonElement drop) && drop.TryGetProperty("scale", out JsonElement ds) ? ds.GetSingle() : 1f,
            lodDistances,
            m.TryGetProperty("source", out JsonElement src) ? src.GetString() ?? "" : "",
            m.TryGetProperty("standIn", out JsonElement si) ? si.GetString() : null);
    }

    private static Pose PoseOf(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement p))
        {
            return Pose.Identity;
        }
        return new Pose(
            p.TryGetProperty("position", out JsonElement pos) ? Vec(pos) : Vector3.Zero,
            p.TryGetProperty("rotation", out JsonElement rot) ? Vec(rot) : Vector3.Zero);
    }

    private static Vector3 Vec(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
}
