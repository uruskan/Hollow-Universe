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
/// hand's attach point; <see cref="OffHand"/> names the socket the other hand holds, if any; <see cref="Grip"/> says how
/// the hand closes on it, if not as on a handle. <see cref="Pose"/> is the arms' pose for it (<c>pistol</c> or
/// <c>rifle</c>; a tool's by default), <see cref="EyeRelief"/> how far the eye sits from its sight aimed down it (metres;
/// 0: at its eye socket), <see cref="Flame"/> the effect that burns on its flame socket while it is held (the torch's).
/// </summary>
public sealed record HeldInfo(Pose FirstPerson, Pose ThirdPerson, string? OffHand, string Hand, GripInfo? Grip = null,
    string? Pose = null, float EyeRelief = 0, string? Flame = null, SupportHandInfo? SupportHand = null);

/// <summary>Support-hand axes and grip offset in item space; elbow pole in camera space. Per item, not per weapon class.</summary>
public sealed record SupportHandInfo(Vector3 Fingers, Vector3 Thumb, Vector3 Offset, Vector3 Pole);

/// <summary>
/// How the first-person hand holds an item (GROUND.md §4.8), in the item's frame: which way the fingers run from the
/// knuckles and the thumb points (the axis the fingers close round runs along the thumb); the radius they close round
/// (0: the hand stays open, the item lying on it); how far out from the palm that axis, or the item's middle, sits
/// (<see cref="Seat"/>; null: the radius); and a point on that axis (<see cref="At"/>; null: the item's middle). Without
/// one an item is held as by a handle: fingers along +X, thumb along +Y, round the grip socket, 1.8 cm.
/// <see cref="Pivot"/> is where a swing turns about, from the grip in camera space (metres: right, up, back): zero turns
/// it about the hand, as a handle swings; a rock in the palm swings from the wrist.
/// </summary>
public sealed record GripInfo(Vector3 Fingers, Vector3 Thumb, float Radius, float? Seat, Vector3? At, Vector3 Pivot);

/// <summary>
/// What a model breaks into (GROUND.md §9.4): its gib file (pieces fractured by <c>art/gibs/build.py</c>; a kit's are
/// grouped by part, a building piece's or a door's key), the gib material (<c>gibs.json</c>) its pieces bounce, dust and
/// sound as, and for a grade's kit the grade its parts are (building.json's order: the Destroyed event's grade).
/// </summary>
public sealed record GibsInfo(string File, string Material, int? Grade);

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
    float IconCrop,
    int IconBundle,
    HeldInfo? Held,
    float[]? LodDistances,
    string Source,
    string? StandIn,
    string? Slot,
    IReadOnlyDictionary<string, string> Sockets,
    GibsInfo? Gibs,
    OpticInfo? Optic = null,
    AttachmentLightInfo? AttachmentLight = null)
{
    public bool IsItem => Kind == "item";

    /// <summary>A kit (the building's grades, the doors): art the building package draws by its own names; listed for its parts' gibs.</summary>
    public bool IsKit => Kind == "kit";

    /// <summary>An attachment (<see cref="Slot"/> sight, muzzle or under) mounts on a weapon's socket for that slot.</summary>
    public bool IsAttachment => Slot != null;

    /// <summary>Resource nodes have one file per depletion stage; everything else has one.</summary>
    public int Stages => Files.Count;
}

/// <summary>What a model instance is for: <see cref="Display"/> drops the collision hulls, <see cref="Static"/>
/// gathers them under one StaticBody3D, <see cref="Parts"/> keeps each where it is (under the part it moves with) on the
/// feet's layer only: a ship people walk on, its decks under drawn feet (IRON_WAKE.md §9.8).</summary>
public enum ModelUse
{
    Display,
    Static,
    Parts,
}

/// <summary>
/// The model catalogue (GROUND.md §9): which GLB, icon, grip, LODs and collision each item key and
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
        GunAttachmentPresentation.Prepare(root, entry);
        HideBlurs(root);
        return root;
    }

    /// <summary>
    /// A rotor's blurred disc (a mesh named <c>…Blur</c>, docs/MINICOPTER.md §8) is shown only by a view that spins it: at
    /// rest, in an icon or the model viewer, the blades show alone.
    /// </summary>
    private static void HideBlurs(Node root)
    {
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            if (mesh.Name.ToString().EndsWith("Blur", StringComparison.Ordinal))
            {
                mesh.Visible = false;
            }
        }
    }

    /// <summary>
    /// Mounts an attachment on a weapon instance: the attachment's pivot (its mount point, +X along the barrel) goes
    /// on the weapon's socket for the attachment's slot (<c>sockets</c> in the catalogue). Returns the attachment, or
    /// null when either is unknown or the weapon has no such socket.
    /// </summary>
    public static Node3D? Attach(Node3D weapon, string weaponKey, string attachmentKey)
    {
        ModelEntry? gun = Find(weaponKey);
        ModelEntry? part = Find(attachmentKey);
        if (gun == null || part?.Slot == null || !gun.Sockets.TryGetValue(part.Slot, out string? socketName))
        {
            return null;
        }
        if (weapon.FindChild(socketName, true, false) is not Node3D socket)
        {
            return null;
        }
        Node3D? attachment = Instantiate(attachmentKey);
        if (attachment == null)
        {
            return null;
        }
        attachment.Name = part.Slot;
        attachment.SetMeta("attachment_key", attachmentKey);
        socket.AddChild(attachment);
        return attachment;
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
        // The graphics settings' model detail (GROUND.md §8.6) moves every hand-over farther or nearer.
        float detail = Math.Clamp(HollowUniverse.Settings.GraphicsSettings.Current.ModelDetail, 0.5f, 2f);
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            int lod = LodOf(mesh);
            if (lod < 0)
            {
                continue;
            }
            float begin = lod == 0 ? 0 : distances[Math.Min(lod - 1, distances.Length - 1)] * detail;
            float end = (lod >= last ? distances[^1] : distances[Math.Min(lod, distances.Length - 1)]) * detail;
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

    /// <summary>A node's transform in the space of one of its ancestors.</summary>
    public static Transform3D RelativeTransform(Node3D root, Node3D node)
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
        if (use == ModelUse.Parts)
        {
            foreach (StaticBody3D body in bodies)
            {
                body.CollisionLayer = 1;
                body.CollisionMask = 0;
            }
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
        float iconCrop = 1f;
        int iconBundle = 1;
        if (m.TryGetProperty("icon", out JsonElement icon))
        {
            iconKey = icon.TryGetProperty("key", out JsonElement ik) ? ik.GetString() : key;
            if (icon.TryGetProperty("view", out JsonElement iv))
            {
                iconView = Vec(iv);
            }
            if (icon.TryGetProperty("crop", out JsonElement ic))
            {
                iconCrop = ic.GetSingle();
            }
            if (icon.TryGetProperty("bundle", out JsonElement ib))
            {
                iconBundle = ib.GetInt32();
            }
        }
        HeldInfo? held = null;
        if (m.TryGetProperty("held", out JsonElement h))
        {
            held = new HeldInfo(PoseOf(h, "firstPerson"), PoseOf(h, "thirdPerson"),
                h.TryGetProperty("offHand", out JsonElement off) ? off.GetString() : null,
                h.TryGetProperty("hand", out JsonElement hand) ? hand.GetString() ?? "right" : "right",
                h.TryGetProperty("grip", out JsonElement grip) ? GripOf(grip) : null,
                h.TryGetProperty("pose", out JsonElement pose) ? pose.GetString() : null,
                h.TryGetProperty("eyeRelief", out JsonElement relief) ? relief.GetSingle() : 0,
                h.TryGetProperty("flame", out JsonElement flame) ? flame.GetString() : null,
                h.TryGetProperty("supportHand", out JsonElement support) ? new SupportHandInfo(
                    Vec(support.GetProperty("fingers")), Vec(support.GetProperty("thumb")),
                    support.TryGetProperty("offset", out JsonElement offset) ? Vec(offset) : Vector3.Zero,
                    support.TryGetProperty("pole", out JsonElement pole) ? Vec(pole) : new Vector3(-0.4f, -1f, 0.1f)) : null);
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
            iconCrop,
            iconBundle,
            held,
            lodDistances,
            m.TryGetProperty("source", out JsonElement src) ? src.GetString() ?? "" : "",
            // A stand-in names why it is one; some entries only say true.
            m.TryGetProperty("standIn", out JsonElement si) ? (si.ValueKind == JsonValueKind.String ? si.GetString() :
                si.ValueKind == JsonValueKind.True ? "stand-in" : null) : null,
            m.TryGetProperty("slot", out JsonElement slot) ? slot.GetString() : null,
            SocketsOf(m),
            m.TryGetProperty("gibs", out JsonElement gibs) && gibs.TryGetProperty("file", out JsonElement gibFile)
                ? new GibsInfo(gibFile.GetString()!, gibs.TryGetProperty("material", out JsonElement gm) ? gm.GetString() ?? "" : "",
                    gibs.TryGetProperty("grade", out JsonElement gg) && gg.ValueKind == JsonValueKind.Number ? gg.GetInt32() : null)
                : null,
            m.TryGetProperty("optic", out JsonElement optic) ? new OpticInfo(
                optic.GetProperty("lens").GetString()!, optic.GetProperty("eye").GetString()!,
                optic.GetProperty("reticle").GetString() == "crosshair" ? 1 : 0,
                optic.TryGetProperty("magnified", out JsonElement magnified) && magnified.GetBoolean()) : null,
            m.TryGetProperty("attachmentLight", out JsonElement lamp) ? new AttachmentLightInfo(
                lamp.GetProperty("beam").GetString()!, Math.Clamp(lamp.GetProperty("range").GetSingle(), 1, 40),
                Math.Clamp(lamp.GetProperty("angle").GetSingle(), 1, 80), Math.Clamp(lamp.GetProperty("energy").GetSingle(), 0, 20)) : null);
    }

    private static Dictionary<string, string> SocketsOf(JsonElement m)
    {
        var sockets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (m.TryGetProperty("sockets", out JsonElement s))
        {
            foreach (JsonProperty p in s.EnumerateObject())
            {
                sockets[p.Name] = p.Value.GetString()!;
            }
        }
        return sockets;
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

    private static GripInfo GripOf(JsonElement g) => new(
        g.TryGetProperty("fingers", out JsonElement f) ? Vec(f) : Vector3.Right,
        g.TryGetProperty("thumb", out JsonElement t) ? Vec(t) : Vector3.Up,
        g.TryGetProperty("radius", out JsonElement r) ? r.GetSingle() : 0.018f,
        g.TryGetProperty("seat", out JsonElement seat) ? seat.GetSingle() : (float?)null,
        g.TryGetProperty("at", out JsonElement at) ? Vec(at) : (Vector3?)null,
        g.TryGetProperty("pivot", out JsonElement pivot) ? Vec(pivot) : Vector3.Zero);

    private static Vector3 Vec(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
}
