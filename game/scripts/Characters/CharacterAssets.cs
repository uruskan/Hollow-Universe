using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>A skinned mesh of the character files and the levels of detail it is drawn at.</summary>
public sealed class MeshPart
{
    public required string Name { get; init; }
    public required Mesh Mesh { get; init; }
    public required Skin Skin { get; init; }
    public required int[] Lods { get; init; }
}

/// <summary>What animations.json says about a clip.</summary>
public sealed class ClipInfo
{
    public bool Loop { get; init; }
    public float Length { get; init; }
    public float GroundSpeed { get; init; }
}

/// <summary>
/// The character files (GROUND.md §4.8, §9), loaded once and shared by every <see cref="CharacterView"/>: the skeleton,
/// the body and clothing meshes with their skins, the materials, and the animation library. Built by the Blender
/// scripts in art/characters.
/// </summary>
public static class CharacterAssets
{
    public const string Folder = "res://assets/characters/";

    /// <summary>Camera distance at which each level of detail ends (metres). Beyond the last, characters are not drawn.</summary>
    public static readonly float[] LodEnd = { 9f, 28f, 450f };

    private static Skeleton3D? _skeleton;
    private static readonly Dictionary<string, MeshPart> Parts = new();
    private static readonly Dictionary<string, ClipInfo> ClipTable = new();
    private static readonly Dictionary<(string, bool), Material> Materials = new();
    private static AnimationLibrary? _library;

    public static bool Loaded => _skeleton != null;

    /// <summary>Eye height above the feet in the rest pose (from human_male.json).</summary>
    public static float EyeHeight { get; private set; } = 1.68f;

    public static AnimationLibrary Library => _library ?? throw new InvalidOperationException("CharacterAssets.Load first");

    public static IReadOnlyDictionary<string, ClipInfo> Clips => ClipTable;

    public static IReadOnlyDictionary<string, MeshPart> Meshes => Parts;

    public static void Load()
    {
        if (Loaded)
        {
            return;
        }
        ulong start = Time.GetTicksUsec();
        LoadModel(Folder + "human_male.glb", takeSkeleton: true);
        if (ResourceLoader.Exists(Folder + "clothing.glb"))
        {
            LoadModel(Folder + "clothing.glb", takeSkeleton: false);
        }
        ReadBodyFacts();
        LoadAnimations();
        GD.Print($"[characters] loaded {Parts.Count} meshes, {ClipTable.Count} clips in {(Time.GetTicksUsec() - start) / 1000.0:0} ms");
    }

    /// <summary>A new skeleton with the character's bones at rest.</summary>
    public static Skeleton3D NewSkeleton()
    {
        Skeleton3D template = _skeleton ?? throw new InvalidOperationException("CharacterAssets.Load first");
        var skeleton = new Skeleton3D { Name = "Skeleton" };
        int count = template.GetBoneCount();
        for (int i = 0; i < count; i++)
        {
            skeleton.AddBone(template.GetBoneName(i));
        }
        for (int i = 0; i < count; i++)
        {
            skeleton.SetBoneParent(i, template.GetBoneParent(i));
            skeleton.SetBoneRest(i, template.GetBoneRest(i));
        }
        skeleton.ResetBonePoses();
        return skeleton;
    }

    public static MeshPart? Part(string name) => Parts.TryGetValue(name, out MeshPart? p) ? p : null;

    /// <summary>The shared material for a surface name the art scripts gave (skin, underwear, eye, hair, a garment ...).</summary>
    public static Material MaterialFor(string surface, bool viewmodel = false)
    {
        if (Materials.TryGetValue((surface, viewmodel), out Material? m))
        {
            return m;
        }
        m = CharacterMaterials.Create(surface, viewmodel);
        Materials[(surface, viewmodel)] = m;
        return m;
    }

    // ---------------------------------------------------------------- loading

    private static void LoadModel(string path, bool takeSkeleton)
    {
        var scene = GD.Load<PackedScene>(path);
        Node root = scene.Instantiate();
        Skeleton3D? skeleton = FindFirst<Skeleton3D>(root);
        if (skeleton == null)
        {
            throw new InvalidOperationException(path + " has no skeleton");
        }
        Dictionary<string, int[]> lods = ReadLodTable(path.Replace(".glb", ".json"));
        foreach (Node child in skeleton.GetChildren())
        {
            if (child is not MeshInstance3D mi || mi.Mesh == null || mi.Skin == null)
            {
                continue;
            }
            string name = mi.Name;
            Parts[name] = new MeshPart
            {
                Name = name,
                Mesh = mi.Mesh,
                Skin = mi.Skin,
                Lods = lods.TryGetValue(name, out int[]? l) ? l : LodsFromName(name),
            };
        }
        if (takeSkeleton)
        {
            skeleton.GetParent().RemoveChild(skeleton);
            foreach (Node child in skeleton.GetChildren())
            {
                skeleton.RemoveChild(child);
                child.Free();
            }
            _skeleton = skeleton;
        }
        root.Free();
    }

    private static int[] LodsFromName(string name)
    {
        int at = name.LastIndexOf("_lod", StringComparison.Ordinal);
        return at >= 0 && int.TryParse(name[(at + 4)..], out int level) ? new[] { level } : new[] { 0, 1, 2 };
    }

    private static Dictionary<string, int[]> ReadLodTable(string jsonPath)
    {
        var table = new Dictionary<string, int[]>();
        if (!FileAccess.FileExists(jsonPath))
        {
            return table;
        }
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(jsonPath));
        if (doc.RootElement.TryGetProperty("meshes", out JsonElement meshes))
        {
            foreach (JsonProperty mesh in meshes.EnumerateObject())
            {
                var levels = new List<int>();
                foreach (JsonElement level in mesh.Value.GetProperty("lods").EnumerateArray())
                {
                    levels.Add(level.GetInt32());
                }
                table[mesh.Name] = levels.ToArray();
            }
        }
        return table;
    }

    private static void ReadBodyFacts()
    {
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(Folder + "human_male.json"));
        EyeHeight = doc.RootElement.GetProperty("eye_height_m").GetSingle();
    }

    private static void LoadAnimations()
    {
        using (JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(Folder + "animations.json")))
        {
            foreach (JsonProperty clip in doc.RootElement.GetProperty("clips").EnumerateObject())
            {
                JsonElement c = clip.Value;
                ClipTable[clip.Name] = new ClipInfo
                {
                    Loop = c.GetProperty("loop").GetBoolean(),
                    Length = c.GetProperty("length").GetSingle(),
                    GroundSpeed = c.TryGetProperty("ground_speed", out JsonElement s) ? s.GetSingle() : 0f,
                };
            }
        }
        Node root = GD.Load<PackedScene>(Folder + "animations.glb").Instantiate();
        AnimationPlayer player = FindFirst<AnimationPlayer>(root) ?? throw new InvalidOperationException("animations.glb has no animations");
        var library = new AnimationLibrary();
        foreach (StringName libraryName in player.GetAnimationLibraryList())
        {
            AnimationLibrary source = player.GetAnimationLibrary(libraryName);
            foreach (StringName name in source.GetAnimationList())
            {
                var animation = (Animation)source.GetAnimation(name).Duplicate();
                for (int t = animation.GetTrackCount() - 1; t >= 0; t--)
                {
                    string bone = animation.TrackGetPath(t).GetConcatenatedSubNames();
                    Animation.TrackType type = animation.TrackGetType(t);
                    bool keep = type == Animation.TrackType.Rotation3D || (type == Animation.TrackType.Position3D && bone == "pelvis");
                    if (!keep || bone.Length == 0)
                    {
                        animation.RemoveTrack(t);
                        continue;
                    }
                    animation.TrackSetPath(t, new NodePath("Skeleton:" + bone));
                }
                string clip = name.ToString();
                animation.LoopMode = ClipTable.TryGetValue(clip, out ClipInfo? info) && info.Loop
                    ? Animation.LoopModeEnum.Linear
                    : Animation.LoopModeEnum.None;
                library.AddAnimation(clip, animation);
            }
        }
        root.Free();
        _library = library;
    }

    private static T? FindFirst<T>(Node node) where T : Node
    {
        if (node is T t)
        {
            return t;
        }
        foreach (Node child in node.GetChildren())
        {
            T? found = FindFirst<T>(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
