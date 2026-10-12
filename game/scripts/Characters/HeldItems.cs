using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>A model that can be held: its kind, our name for it and where its grips are.</summary>
public sealed class HeldItemModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required HeldKind Kind { get; init; }

    /// <summary>The model file, game/assets/items/&lt;id&gt;.glb (built by art/items/build_items.py).</summary>
    public string Path => "res://assets/items/" + Id + ".glb";

    /// <summary>Where the camera looks through the sights, behind the sight socket (metres), for aiming down sights.</summary>
    public float EyeRelief { get; init; } = 0.14f;
}

/// <summary>
/// The held items the character work tests with (GROUND.md §4.8): the PSG starter kit's tools and pistol and the PSG
/// Frontier rifle, under our own names. Models carry socket nodes: socket_grip_r (right palm), socket_grip_l (left
/// palm), socket_sight, socket_muzzle, socket_impact.
/// </summary>
public static class HeldItems
{
    public static readonly HeldItemModel FlintAxe = new() { Id = "flint_axe", Name = "Flint axe", Kind = HeldKind.Tool };
    public static readonly HeldItemModel FlintPick = new() { Id = "flint_pick", Name = "Flint pick", Kind = HeldKind.Tool };
    public static readonly HeldItemModel HandStone = new() { Id = "hand_stone", Name = "Hand stone", Kind = HeldKind.Tool };
    public static readonly HeldItemModel WrenPistol = new() { Id = "wren_pistol", Name = "Wren pistol", Kind = HeldKind.Pistol, EyeRelief = 0.32f };
    public static readonly HeldItemModel KestrelRifle = new() { Id = "kestrel_rifle", Name = "Kestrel rifle", Kind = HeldKind.Rifle, EyeRelief = 0.11f };

    public static readonly IReadOnlyList<HeldItemModel> All = new[] { FlintAxe, FlintPick, HandStone, WrenPistol, KestrelRifle };

    private static readonly Dictionary<string, PackedScene> Scenes = new();

    public static HeldItemModel? Find(string? id)
    {
        foreach (HeldItemModel m in All)
        {
            if (m.Id == id)
            {
                return m;
            }
        }
        return null;
    }

    /// <summary>The item named, or the default model of a kind (null when unarmed).</summary>
    public static HeldItemModel? For(HeldKind kind, string? id)
    {
        HeldItemModel? named = Find(id);
        if (named != null)
        {
            return named;
        }
        return kind switch
        {
            HeldKind.Tool => FlintAxe,
            HeldKind.Pistol => WrenPistol,
            HeldKind.Rifle => KestrelRifle,
            _ => null,
        };
    }

    /// <summary>A new instance of the model, or null if its file is missing.</summary>
    public static Node3D? Instantiate(HeldItemModel model)
    {
        if (!Scenes.TryGetValue(model.Id, out PackedScene? scene))
        {
            if (!ResourceLoader.Exists(model.Path))
            {
                return null;
            }
            scene = GD.Load<PackedScene>(model.Path);
            Scenes[model.Id] = scene;
        }
        return scene.Instantiate<Node3D>();
    }

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

    /// <summary>
    /// The constant rotation from the grip frame to the hand bone, and the palm centre in the grip frame (relative to
    /// the wrist), from the skeleton's rest pose.
    /// </summary>
    public static (Basis FrameToBone, Vector3 Palm) Measure(Skeleton3D s, bool right)
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
        // The handle sits in the fist: half way to the knuckles, a little out from the palm.
        Vector3 palmWorld = wrist + f * (length * 0.62f) + palmNormal * 0.022f;
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
