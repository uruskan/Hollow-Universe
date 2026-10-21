using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Characters;

/// <summary>An item that can be held: its item key, its name, how it is held and in which hand.</summary>
public sealed class HeldItemModel
{
    /// <summary>The item key (data/ground/items.json and models.json).</summary>
    public required string Id { get; init; }

    /// <summary>Its name, from items.json (or made from the key while the item has no entry there).</summary>
    public required string Name { get; init; }

    public required HeldKind Kind { get; init; }

    /// <summary>The model's class in the catalogue: tool, blade, spear, bow, gun, food, medical, lock, explosive.</summary>
    public required string Class { get; init; }

    /// <summary>The model: file, levels of detail and the catalogue's held poses.</summary>
    public required ModelEntry Entry { get; init; }

    /// <summary>Held in the left hand (the bow); its pivot is then the left grip.</summary>
    public bool LeftHanded => Entry.Held?.Hand == "left";

    /// <summary>Distance from the eye to the sight socket when aiming down the sights (metres); 0 puts the model's own
    /// socket_eye at the camera.</summary>
    public float EyeRelief { get; init; }

    /// <summary>How long a swing takes, and when in it the blow lands (seconds; items.json, melee).</summary>
    public float SwingTime { get; init; } = 0.85f;

    public float StrikeTime { get; init; } = 0.3f;

    public float DrawSeconds { get; init; } = 1f;

    public Basis SupportFrame => Entry.Held?.SupportHand is { } support
        ? HandFrame.Axes(support.Fingers, support.Thumb) : HandFrame.SupportGrip;

    /// <summary>Swung like a hatchet: held by a handle at the hip, head forward (tools and blades).</summary>
    public bool Swung => Kind == HeldKind.Tool && Class is "tool" or "blade" && Id != "building-plan" && !LeftHanded;

    /// <summary>
    /// Placed in first person by its handle at the hip: a swung tool held by a handle. One whose catalogue entry gives a
    /// grip of its own (a rock, in the palm) sits where its first-person pose puts that grip, as unswung items do.
    /// </summary>
    public bool HeldByHandle => Swung && Entry.Held?.Grip == null;

    /// <summary>
    /// How the first-person right hand's fingers sit: on a gun's grip, the trigger finger out; open under what a
    /// catalogue grip of radius 0 lays on the palm (food); else closed round it (a handle, a rock).
    /// </summary>
    public HandPoses.Grip Fingers => Kind is HeldKind.Pistol or HeldKind.Rifle ? HandPoses.Grip.Pistol
        : Entry.Held?.Grip is { Radius: <= 0f } ? HandPoses.Grip.Relaxed : HandPoses.Grip.Fist;

    /// <summary>The radius the right hand's fingers close round: the catalogue's grip (a rock's girth), else a handle's.</summary>
    public float GripRadius => (LeftHanded ? Presentation.LeftHand : Presentation.RightHand).Radius;

    /// <summary>How far out from the palm what is held sits: the catalogue's seat, else its radius (a handle's 1.8 cm).</summary>
    public float GripSeat => (LeftHanded ? Presentation.LeftHand : Presentation.RightHand).Seat;

    /// <summary>Where a first-person swing turns about, from the grip (camera space): the hand, or the catalogue's pivot.</summary>
    public Vector3 SwingPivot => Presentation.SwingPivot;

    /// <summary>Resolved on demand so editor replacement and reload also update already-equipped models.</summary>
    public HeldPresentationProfile Presentation => HeldPresentations.Get(Id);
}

/// <summary>
/// Everything the hands can hold (GROUND.md §4.8): the item models the model catalogue gives a held pose
/// (data/ground/models.json; GROUND.md §9), loaded through <see cref="ModelCatalog"/> with the materials of their
/// import settings. Models are in the hand frame: +X forward (barrel or blade), +Y up, palm sockets socket_grip_r and
/// socket_grip_l, and socket_sight, socket_eye, socket_muzzle, socket_impact where they have them.
/// </summary>
public static class HeldItems
{
    /// <summary>What a kind holds when no item is named.</summary>
    public const string DefaultTool = "stone-hatchet", DefaultPistol = "pistol", DefaultRifle = "rifle";

    private static List<HeldItemModel>? _all;
    private static Dictionary<string, HeldItemModel>? _byId;

    /// <summary>Every item with a held pose, in the catalogue's order.</summary>
    public static IReadOnlyList<HeldItemModel> All
    {
        get
        {
            Load();
            return _all!;
        }
    }

    public static HeldItemModel? Find(string? id)
    {
        if (id == null)
        {
            return null;
        }
        Load();
        return _byId!.TryGetValue(id, out HeldItemModel? model) ? model : null;
    }

    /// <summary>The item named, or the default model of a kind (null when unarmed).</summary>
    public static HeldItemModel? For(HeldKind kind, string? id) => Find(id) ?? kind switch
    {
        HeldKind.Tool => Find(DefaultTool),
        HeldKind.Pistol => Find(DefaultPistol),
        HeldKind.Rifle => Find(DefaultRifle),
        _ => null,
    };

    /// <summary>A new instance of the model (its most detailed level shown, no collision), or null if missing.</summary>
    public static Node3D? Instantiate(HeldItemModel model)
    {
        Node3D? item = ModelCatalog.Instantiate(model.Id);
        if (item != null)
        {
            ModelCatalog.ForceLod(item, 0);
        }
        return item;
    }

    /// <summary>Shows one level of detail of an item (meshes are named "…_lod0" to "…_lod2").</summary>
    public static void SetLod(Node node, int lod) => ModelCatalog.ForceLod(node, lod);

    /// <summary>A socket's transform in the item's space (identity if the model has none of that name).</summary>
    public static Transform3D Socket(Node3D item, string name)
    {
        Node3D? socket = item.FindChild("socket_" + name, true, false) as Node3D;
        if (socket == null)
        {
            return Transform3D.Identity;
        }
        Transform3D t = Transform3D.Identity;
        for (Node3D? n = socket; n != null && n != item; n = n.GetParent() as Node3D)
        {
            t = n.Transform * t;
        }
        return t;
    }

    public static bool HasSocket(Node3D item, string name) => item.FindChild("socket_" + name, true, false) != null;

    /// <summary>
    /// Where and how the holding hand closes on an item (item space): a frame in the grip frame's terms (fingers along
    /// X, thumb along Y; <see cref="HandFrame"/>) whose origin is on the axis the fingers close round (or, for an open
    /// hand, the item's middle). From the catalogue's grip (<see cref="GripInfo"/>: a rock's, food's), else the palm
    /// socket's own frame (a handle's).
    /// </summary>
    public static Transform3D Grip(Node3D item, HeldItemModel model)
    {
        HeldPresentationHand hand = model.LeftHanded ? model.Presentation.LeftHand : model.Presentation.RightHand;
        return RawGrip(item, model) * hand.GripOffset.Transform;
    }

    /// <summary>The source model grip before the shared presentation profile's artist adjustment.</summary>
    public static Transform3D RawGrip(Node3D item, HeldItemModel model)
    {
        if (model.Entry.Held?.Grip is not { } grip)
        {
            return Socket(item, model.LeftHanded ? "grip_l" : "grip_r");
        }
        Vector3 fingers = grip.Fingers.Normalized();
        Vector3 thumb = (grip.Thumb - fingers * grip.Thumb.Dot(fingers)).Normalized();
        return new Transform3D(new Basis(fingers, thumb, fingers.Cross(thumb)), grip.At ?? Middle(item));
    }

    // The middle of an item's most detailed meshes (item space).
    private static Vector3 Middle(Node3D item)
    {
        Aabb? box = null;
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(item))
        {
            if (!mesh.Name.ToString().EndsWith("_lod0", StringComparison.Ordinal))
            {
                continue;
            }
            Transform3D t = Transform3D.Identity;
            for (Node3D? n = mesh; n != null && n != item; n = n.GetParent() as Node3D)
            {
                t = n.Transform * t;
            }
            Aabb b = t * mesh.GetAabb();
            box = box is { } a ? a.Merge(b) : b;
        }
        return box?.GetCenter() ?? Vector3.Zero;
    }

    private static void Load()
    {
        if (_all != null)
        {
            return;
        }
        _all = new List<HeldItemModel>();
        _byId = new Dictionary<string, HeldItemModel>(StringComparer.Ordinal);
        Dictionary<string, (string Name, float Swing, float Strike, float Draw)> items = ItemData();
        foreach (ModelEntry entry in ModelCatalog.Entries)
        {
            if (!entry.IsItem || entry.Held == null)
            {
                continue;
            }
            var model = new HeldItemModel
            {
                Id = entry.Key,
                Name = items.TryGetValue(entry.Key, out var data) ? data.Name : NameFromKey(entry.Key),
                SwingTime = data.Swing > 0f ? data.Swing : 0.85f,
                StrikeTime = data.Strike > 0f ? data.Strike : 0.3f,
                DrawSeconds = data.Draw > 0f ? data.Draw : 1f,
                // The arms' pose is the catalogue's (held.pose: pistol or rifle), a tool's by default.
                Kind = Enum.TryParse(entry.Held.Pose, ignoreCase: true, out HeldKind pose) ? pose : HeldKind.Tool,
                Class = entry.Class,
                Entry = entry,
                // A rifle's eye socket sits at the cheek weld, where the stock fills the bottom of a first-person
                // view; the catalogue's eye relief puts the camera closer to the rear sight instead.
                EyeRelief = entry.Held.EyeRelief,
            };
            _all.Add(model);
            _byId[model.Id] = model;
        }
    }

    /// <summary>Each item's name and melee timing by key, from items.json (read once).</summary>
    private static Dictionary<string, (string Name, float Swing, float Strike, float Draw)> ItemData()
    {
        var items = new Dictionary<string, (string, float, float, float)>(StringComparer.Ordinal);
        const string path = "res://data/ground/items.json";
        if (!FileAccess.FileExists(path))
        {
            return items;
        }
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
        foreach (JsonElement item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("key", out JsonElement key) || !item.TryGetProperty("name", out JsonElement name))
            {
                continue;
            }
            float swing = 0f, strike = 0f;
            if (item.TryGetProperty("melee", out JsonElement melee))
            {
                swing = melee.TryGetProperty("swing", out JsonElement sw) ? sw.GetSingle() : 0f;
                strike = melee.TryGetProperty("strike", out JsonElement st) ? st.GetSingle() : 0f;
            }
            float draw = item.TryGetProperty("bow", out JsonElement bow) && bow.TryGetProperty("draw", out JsonElement d) ? d.GetSingle() : 1f;
            items[key.GetString()!] = (name.GetString()!, swing, strike, draw);
        }
        return items;
    }

    private static string NameFromKey(string key)
    {
        string words = key.Replace('-', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}

/// <summary>
/// The hand's grip frame: X along the fingers, Y towards the thumb side, Z = X × Y. A right palm faces −Z, a left palm
/// +Z. Item grips are written in this frame: a right hand's power grip is the item's own axes (barrel or blade along
/// +X, up along +Y), a left support hand under a barrel puts its thumb along +X and its palm up.
/// </summary>
public static class HandFrame
{
    /// <summary>Right palm on a handle, item space: the item's axes.</summary>
    public static readonly Basis PowerGrip = Basis.Identity;

    /// <summary>Left palm under a barrel, item space: thumb forward (+X), palm up (+Y), fingers to the right (+Z).</summary>
    public static readonly Basis SupportGrip = new(new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0));

    /// <summary>Orthonormal grip axes; parallel or zero axes are rejected instead of twisting the wrist unpredictably.</summary>
    public static Basis Axes(Vector3 fingers, Vector3 thumb)
    {
        Vector3 f = fingers.Normalized();
        Vector3 t = thumb - f * thumb.Dot(f);
        return f.LengthSquared() < 0.5f || t.LengthSquared() < 1e-6f ? Basis.Identity
            : new Basis(f, t.Normalized(), f.Cross(t.Normalized()));
    }

    /// <summary>
    /// The constant rotation from the grip frame to the hand bone, and the palm centre in the grip frame (relative to
    /// the wrist), from the skeleton's rest pose.
    /// </summary>
    public static (Basis FrameToBone, Vector3 Palm) Measure(Skeleton3D s, bool right, float along = 0.62f,
        float lift = 0.022f)
    {
        string side = right ? "_r" : "_l";
        Transform3D hand = s.GetBoneGlobalRest(s.FindBone("hand" + side));
        Vector3 wrist = hand.Origin;
        Vector3 middle = s.GetBoneGlobalRest(s.FindBone("middle_01" + side)).Origin;
        Vector3 index = s.GetBoneGlobalRest(s.FindBone("index_01" + side)).Origin;
        Vector3 pinky = s.GetBoneGlobalRest(s.FindBone("pinky_01" + side)).Origin;
        Vector3 f = (middle - wrist).Normalized();
        Vector3 t = index - pinky;
        t = (t - f * t.Dot(f)).Normalized();
        if (!right)
        {
            // Index minus pinky points to the thumb side on either hand.
        }
        Vector3 z = f.Cross(t).Normalized();
        var frame = new Basis(f, t, z);
        Basis frameToBone = frame.Inverse() * hand.Basis;
        Vector3 palmNormal = right ? -z : z;
        float length = (middle - wrist).Length();
        // The handle sits in the fist: `along` of the way to the knuckles, `lift` out from the palm's bones.
        Vector3 palmWorld = wrist + f * (length * along) + palmNormal * lift;
        Vector3 palm = frame.Inverse() * (palmWorld - wrist);
        return (frameToBone, palm);
    }

    /// <summary>The hand bone's transform (in the space of <paramref name="grip"/>) holding a grip point.</summary>
    public static Transform3D HandFor(Transform3D grip, Basis gripFrame, (Basis FrameToBone, Vector3 Palm) hand)
    {
        Basis frame = grip.Basis * gripFrame;
        Vector3 wrist = grip.Origin - frame * hand.Palm;
        return new Transform3D(frame * hand.FrameToBone, wrist);
    }
}
