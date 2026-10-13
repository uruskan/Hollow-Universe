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
/// One body's files (GROUND.md §4.8, §9), loaded once and shared by every character of that body: the skeleton, the
/// body and clothing meshes with their skins, and the animation library retargeted onto that skeleton.
/// </summary>
public sealed class BodyAssets
{
    private readonly Skeleton3D _skeleton;
    private readonly Dictionary<string, MeshPart> _parts;
    private readonly Dictionary<string, ClipInfo> _clips;

    internal BodyAssets(BodyType type, Skeleton3D skeleton, Dictionary<string, MeshPart> parts,
        Dictionary<string, ClipInfo> clips, AnimationLibrary library, float eyeHeight, string[] hairStyles, string[] faces)
    {
        Type = type;
        _skeleton = skeleton;
        _parts = parts;
        _clips = clips;
        Library = library;
        EyeHeight = eyeHeight;
        HairStyles = hairStyles;
        Faces = faces;
    }

    public BodyType Type { get; }

    /// <summary>The MakeHuman hair styles built for this body (meshes hair_&lt;style&gt;_lod0/1), the default first.</summary>
    public IReadOnlyList<string> HairStyles { get; }

    /// <summary>The face shapes on the nearest body (blend shapes face_1 …); face 0 is the base face.</summary>
    public IReadOnlyList<string> Faces { get; }

    /// <summary>Eye height above the feet in the rest pose.</summary>
    public float EyeHeight { get; }

    public AnimationLibrary Library { get; }

    public IReadOnlyDictionary<string, ClipInfo> Clips => _clips;

    public IReadOnlyDictionary<string, MeshPart> Meshes => _parts;

    public MeshPart? Part(string name) => _parts.TryGetValue(name, out MeshPart? p) ? p : null;

    /// <summary>A new skeleton with the body's bones at rest.</summary>
    public Skeleton3D NewSkeleton()
    {
        var skeleton = new Skeleton3D { Name = "Skeleton" };
        int count = _skeleton.GetBoneCount();
        for (int i = 0; i < count; i++)
        {
            skeleton.AddBone(_skeleton.GetBoneName(i));
        }
        for (int i = 0; i < count; i++)
        {
            skeleton.SetBoneParent(i, _skeleton.GetBoneParent(i));
            skeleton.SetBoneRest(i, _skeleton.GetBoneRest(i));
        }
        skeleton.ResetBonePoses();
        return skeleton;
    }
}

/// <summary>
/// The character files (GROUND.md §4.8, §9), by body, loaded on first use: human_male.glb with clothing.glb and
/// animations.glb, or human_female.glb with clothing_female.glb and animations_female.glb. Built by the Blender
/// scripts in art/characters. The members without a body are the male body's.
/// </summary>
public static class CharacterAssets
{
    public const string Folder = "res://assets/characters/";

    /// <summary>Camera distance at which each level of detail ends (metres). Beyond the last, characters are not drawn.</summary>
    public static readonly float[] LodEnd = { 9f, 28f, 450f };

    private static readonly Dictionary<BodyType, BodyAssets> Bodies = new();
    private static readonly Dictionary<(string, bool), Material> Materials = new();

    public static bool Loaded => Bodies.ContainsKey(BodyType.Male);

    /// <summary>The male body's eye height above the feet in the rest pose.</summary>
    public static float EyeHeight => For(BodyType.Male).EyeHeight;

    public static AnimationLibrary Library => For(BodyType.Male).Library;

    public static IReadOnlyDictionary<string, ClipInfo> Clips => For(BodyType.Male).Clips;

    public static IReadOnlyDictionary<string, MeshPart> Meshes => For(BodyType.Male).Meshes;

    public static void Load() => For(BodyType.Male);

    /// <summary>Whether a body's files are there (the female body is optional).</summary>
    public static bool Has(BodyType body) => ResourceLoader.Exists(Folder + Stem(body) + ".glb");

    /// <summary>The files a body is read from (the warm-up loads them ahead, GROUND.md §4.3.1).</summary>
    public static IReadOnlyList<string> Files(BodyType body)
    {
        string suffix = body == BodyType.Male ? "" : "_" + body.ToString().ToLowerInvariant();
        var files = new List<string> { Folder + Stem(body) + ".glb" };
        foreach (string stem in new[] { "clothing" + suffix, "animations" + suffix })
        {
            if (ResourceLoader.Exists(Folder + stem + ".glb"))
            {
                files.Add(Folder + stem + ".glb");
            }
        }
        return files;
    }

    /// <summary>A body's files, loaded on first use (the male body if the one asked for is missing).</summary>
    public static BodyAssets For(BodyType body)
    {
        if (Bodies.TryGetValue(body, out BodyAssets? assets))
        {
            return assets;
        }
        if (body != BodyType.Male && !Has(body))
        {
            return For(BodyType.Male);
        }
        ulong start = Time.GetTicksUsec();
        string suffix = body == BodyType.Male ? "" : "_" + body.ToString().ToLowerInvariant();
        var parts = new Dictionary<string, MeshPart>();
        Skeleton3D skeleton = LoadModel(Folder + Stem(body) + ".glb", parts, takeSkeleton: true)!;
        if (ResourceLoader.Exists(Folder + "clothing" + suffix + ".glb"))
        {
            LoadModel(Folder + "clothing" + suffix + ".glb", parts, takeSkeleton: false);
        }
        using JsonDocument facts = JsonDocument.Parse(FileAccess.GetFileAsString(Folder + Stem(body) + ".json"));
        float eye = facts.RootElement.GetProperty("eye_height_m").GetSingle();
        string[] hairStyles = Strings(facts.RootElement, "hair_styles");
        string[] faces = Strings(facts.RootElement, "faces");
        string animations = ResourceLoader.Exists(Folder + "animations" + suffix + ".glb") ? "animations" + suffix : "animations";
        var clips = new Dictionary<string, ClipInfo>();
        AnimationLibrary library = LoadAnimations(animations, clips);
        assets = new BodyAssets(body, skeleton, parts, clips, library, eye, hairStyles, faces);
        Bodies[body] = assets;
        GD.Print($"[characters] {body}: loaded {parts.Count} meshes, {clips.Count} clips in {(Time.GetTicksUsec() - start) / 1000.0:0} ms");
        return assets;
    }

    /// <summary>A new skeleton with the male body's bones at rest.</summary>
    public static Skeleton3D NewSkeleton() => For(BodyType.Male).NewSkeleton();

    public static MeshPart? Part(string name) => For(BodyType.Male).Part(name);

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

    private static string Stem(BodyType body) => "human_" + body.ToString().ToLowerInvariant();

    private static string[] Strings(JsonElement root, string property)
    {
        var list = new List<string>();
        if (root.TryGetProperty(property, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in array.EnumerateArray())
            {
                list.Add(e.GetString() ?? "");
            }
        }
        return list.ToArray();
    }

    // ---------------------------------------------------------------- loading

    private static Skeleton3D? LoadModel(string path, Dictionary<string, MeshPart> parts, bool takeSkeleton)
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
            parts[name] = new MeshPart
            {
                Name = name,
                Mesh = mi.Mesh,
                Skin = mi.Skin,
                Lods = lods.TryGetValue(name, out int[]? l) ? l : LodsFromName(name),
            };
        }
        Skeleton3D? kept = null;
        if (takeSkeleton)
        {
            skeleton.GetParent().RemoveChild(skeleton);
            foreach (Node child in skeleton.GetChildren())
            {
                skeleton.RemoveChild(child);
                child.Free();
            }
            kept = skeleton;
        }
        root.Free();
        return kept;
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

    private static AnimationLibrary LoadAnimations(string stem, Dictionary<string, ClipInfo> clips)
    {
        using (JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(Folder + stem + ".json")))
        {
            foreach (JsonProperty clip in doc.RootElement.GetProperty("clips").EnumerateObject())
            {
                JsonElement c = clip.Value;
                clips[clip.Name] = new ClipInfo
                {
                    Loop = c.GetProperty("loop").GetBoolean(),
                    Length = c.GetProperty("length").GetSingle(),
                    GroundSpeed = c.TryGetProperty("ground_speed", out JsonElement s) ? s.GetSingle() : 0f,
                };
            }
        }
        Node root = GD.Load<PackedScene>(Folder + stem + ".glb").Instantiate();
        AnimationPlayer player = FindFirst<AnimationPlayer>(root) ?? throw new InvalidOperationException(stem + ".glb has no animations");
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
                animation.LoopMode = clips.TryGetValue(clip, out ClipInfo? info) && info.Loop
                    ? Animation.LoopModeEnum.Linear
                    : Animation.LoopModeEnum.None;
                library.AddAnimation(clip, animation);
            }
        }
        root.Free();
        return library;
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
