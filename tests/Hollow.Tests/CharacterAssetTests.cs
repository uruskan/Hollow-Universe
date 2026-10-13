using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hollow.Tests;

public static class CharacterAssetTests
{
    // The body regions, in the order the art scripts number them (art/lib/hollow_art.py REGIONS) and the client's
    // BodyRegions bits (game/scripts/Characters/CharacterTypes.cs). A garment hides regions by these bits.
    private static readonly string[] Regions =
    {
        "face", "scalp", "neck", "chest", "belly", "pelvis", "upper_arm", "forearm", "hand", "thigh", "calf", "ankle",
        "foot", "collar",
    };

    // Clips the client's animation graph and events play (CharacterAnimator).
    private static readonly string[] Clips =
    {
        "idle", "walk", "jog", "sprint", "crouch_idle", "crouch_walk", "swim_idle", "swim", "fall", "jump_start",
        "jump_land", "torch_idle", "pistol_idle", "tool_swing", "chop", "punch_jab", "pistol_fire", "pistol_reload",
        "throw", "hit_chest", "hit_head",
    };

    /// <summary>
    /// GROUND.md §4.8: the built character files agree with the client: the same regions in the same order, every
    /// clip the graph plays, a body at each of the three levels of detail, the looks' hair styles, faces and skin maps,
    /// and every garment.
    /// </summary>
    [Test]
    public static void CharacterFilesAgreeWithTheClient()
    {
        // Both bodies: the male's files, and the female's (human_female, clothing_female, animations_female).
        foreach ((string body, string suffix) in new[] { ("male", ""), ("female", "_female") })
        {
            Check(body, suffix);
        }
    }

    private static void Check(string which, string suffix)
    {
        string folder = Path.Combine(Fixtures.Root, "game", "assets", "characters");
        using JsonDocument body = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, $"human_{which}.json")));
        string[] regions = body.RootElement.GetProperty("regions").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(string.Join(",", Regions), string.Join(",", regions), $"{which} body regions");

        Dictionary<string, int[]> meshes = Lods(body.RootElement);
        for (int level = 0; level < 3; level++)
        {
            int bodies = meshes.Count(m => m.Key.StartsWith("body_", System.StringComparison.Ordinal) && m.Value.Contains(level));
            Assert.Equal(1, bodies, $"{which} body meshes at level {level}");
        }
        int triangles = body.RootElement.GetProperty("meshes").GetProperty("body_lod0").GetProperty("triangles").GetInt32();
        Assert.InRange(15000, 25000, triangles, $"{which} body triangles at level 0");

        // Looks (Looks.cs): four to six hair styles, each drawn near and in the middle distance, and four to six faces.
        string[] styles = body.RootElement.GetProperty("hair_styles").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.InRange(4, 6, styles.Length, $"{which} hair styles");
        foreach (string style in styles)
        {
            Assert.True(meshes.ContainsKey($"hair_{style}_lod0") && meshes.ContainsKey($"hair_{style}_lod1"), $"{which}: hair {style} at levels 0 and 1");
            Assert.True(File.Exists(Path.Combine(folder, "textures", $"hair_{style}_albedo.png")), $"hair {style} has its texture");
        }
        Assert.InRange(4, 6, body.RootElement.GetProperty("faces").GetArrayLength(), $"{which} face shapes");
        foreach (string map in new[] { "albedo", "dark", "mask", "normal" })
        {
            Assert.True(File.Exists(Path.Combine(folder, "textures", $"skin_{which}_{map}.png")), $"skin_{which}_{map}.png");
        }

        using JsonDocument clothing = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, $"clothing{suffix}.json")));
        Dictionary<string, int[]> garments = Lods(clothing.RootElement);
        foreach (string item in new[] { "knit_cap", "woven_shirt", "woven_trousers", "laced_boots", "riveted_breastplate" })
        {
            for (int level = 0; level < 3; level++)
            {
                Assert.True(garments.ContainsKey($"{item}_lod{level}"), $"{which}: {item} has level {level}");
            }
        }

        using JsonDocument animations = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, $"animations{suffix}.json")));
        JsonElement clips = animations.RootElement.GetProperty("clips");
        foreach (string clip in Clips)
        {
            Assert.True(clips.TryGetProperty(clip, out _), $"animations{suffix}.json has {clip}");
        }
        Fixtures.Report($"{which}: {regions.Length} regions, {meshes.Count} body meshes, {styles.Length} hair styles, {garments.Count} garment meshes, {clips.EnumerateObject().Count()} clips");
    }

    private static Dictionary<string, int[]> Lods(JsonElement root)
    {
        var result = new Dictionary<string, int[]>();
        foreach (JsonProperty mesh in root.GetProperty("meshes").EnumerateObject())
        {
            result[mesh.Name] = mesh.Value.GetProperty("lods").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        }
        return result;
    }
}
