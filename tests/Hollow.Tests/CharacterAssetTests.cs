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
    /// clip the graph plays, a body at each of the three levels of detail, and each garment of the first outfit.
    /// </summary>
    [Test]
    public static void CharacterFilesAgreeWithTheClient()
    {
        string folder = Path.Combine(Fixtures.Root, "game", "assets", "characters");
        using JsonDocument body = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "human_male.json")));
        string[] regions = body.RootElement.GetProperty("regions").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(string.Join(",", Regions), string.Join(",", regions), "body regions");

        Dictionary<string, int[]> meshes = Lods(body.RootElement);
        for (int level = 0; level < 3; level++)
        {
            int bodies = meshes.Count(m => m.Key.StartsWith("body_", System.StringComparison.Ordinal) && m.Value.Contains(level));
            Assert.Equal(1, bodies, $"body meshes at level {level}");
        }
        int triangles = body.RootElement.GetProperty("meshes").GetProperty("body_lod0").GetProperty("triangles").GetInt32();
        Assert.InRange(15000, 25000, triangles, "body triangles at level 0");

        using JsonDocument clothing = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "clothing.json")));
        Dictionary<string, int[]> garments = Lods(clothing.RootElement);
        foreach (string item in new[] { "knit_cap", "woven_shirt", "woven_trousers", "laced_boots", "riveted_breastplate" })
        {
            for (int level = 0; level < 3; level++)
            {
                Assert.True(garments.ContainsKey($"{item}_lod{level}"), $"{item} has level {level}");
            }
        }

        using JsonDocument animations = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "animations.json")));
        JsonElement clips = animations.RootElement.GetProperty("clips");
        foreach (string clip in Clips)
        {
            Assert.True(clips.TryGetProperty(clip, out _), $"animations.json has {clip}");
        }
        Fixtures.Report($"{regions.Length} regions, {meshes.Count} body meshes, {garments.Count} garment meshes, {clips.EnumerateObject().Count()} clips");
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
