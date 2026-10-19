using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Hollow.Tests;

/// <summary>
/// The ground's texture catalogue (docs/WALKING_GROUND.md §8, game/assets/surface/ground/layers/layers.json, read by
/// GroundLayers): the sixteen materials own the first sixteen textures in GroundMaterial order, every alternative
/// names a texture that exists with a selector the shader knows, and every texture's three files are there.
/// </summary>
public static class GroundLayerCatalogueTests
{
    private static readonly HashSet<string> Selectors = new() { "noise", "dryness", "lush", "lush-patch", "heat", "cold", "steep", "snowline", "thaw" };

    private static string Folder => Path.Combine(Fixtures.Root, "game", "assets", "surface", "ground", "layers");

    [Test]
    public static void GroundLayerCatalogueIsComplete()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "layers.json")));
        JsonElement root = doc.RootElement;
        Assert.Equal(4096, root.GetProperty("size").GetInt32(), "the colour and normal maps are 4K");
        var textures = new List<JsonElement>();
        foreach (JsonElement t in root.GetProperty("textures").EnumerateArray())
        {
            textures.Add(t);
        }
        Assert.True(textures.Count >= 16 && textures.Count <= 40, $"{textures.Count} textures: sixteen materials' and at most 24 alternatives");
        var keys = new HashSet<string>();
        for (int i = 0; i < textures.Count; i++)
        {
            JsonElement t = textures[i];
            Assert.Equal(i, t.GetProperty("index").GetInt32(), $"texture {i} numbered");
            Assert.True(keys.Add(t.GetProperty("key").GetString()!), $"texture {i}'s key is its own");
            foreach (string map in new[] { "albedo", "normal", "roughHeight" })
            {
                string file = t.GetProperty(map).GetString()!;
                Assert.True(File.Exists(Path.Combine(Folder, file)), $"texture {i}'s {map} {file} exists");
                Assert.True(File.Exists(Path.Combine(Folder, file + ".import")), $"{file} has its import settings");
            }
            double metres = t.GetProperty("metres").GetDouble();
            Assert.True(metres >= 0.25 && metres <= 50, $"texture {i} repeats every {metres} m");
            JsonElement flip = t.GetProperty("flip");
            Assert.True(System.Math.Abs(flip[0].GetDouble()) == 1 && System.Math.Abs(flip[1].GetDouble()) == 1, $"texture {i}'s normal axes are signs");
            Assert.Equal(3, t.GetProperty("meanLinear").GetArrayLength(), $"texture {i}'s mean linear colour");
        }
        var layers = new List<JsonElement>();
        foreach (JsonElement l in root.GetProperty("layers").EnumerateArray())
        {
            layers.Add(l);
        }
        Assert.Equal(16, layers.Count, "sixteen materials");
        int variants = 0;
        for (int i = 0; i < layers.Count; i++)
        {
            JsonElement l = layers[i];
            Assert.Equal(i, l.GetProperty("index").GetInt32(), $"layer {i} numbered");
            Assert.Equal(i, l.GetProperty("texture").GetInt32(), $"layer {i} owns texture {i}");
            Assert.Equal(textures[i].GetProperty("key").GetString(), l.GetProperty("key").GetString(), $"layer {i} and its texture share a key");
            int count = 0;
            foreach (JsonElement v in l.GetProperty("variants").EnumerateArray())
            {
                int texture = v.GetProperty("texture").GetInt32();
                Assert.True(texture >= 0 && texture < textures.Count && texture != i, $"layer {i}'s variant {count} names another texture that exists");
                Assert.True(Selectors.Contains(v.GetProperty("select").GetString()!), $"layer {i}'s variant {count} has a selector the shader knows");
                JsonElement gain = v.GetProperty("gain");
                Assert.True(gain.GetArrayLength() == 3 && gain[0].GetDouble() > 0 && gain[1].GetDouble() > 0 && gain[2].GetDouble() > 0, $"layer {i}'s variant {count} has a positive gain");
                if (i == 3 || i == 13)
                {
                    Assert.Equal(textures[i].GetProperty("metres").GetDouble(), textures[texture].GetProperty("metres").GetDouble(),
                        $"layer {i} is triplanar on cliffs: its variant {count} repeats at its size");
                }
                count++;
            }
            Assert.True(count <= 3, $"layer {i} has at most three variants");
            variants += count;
        }
        Fixtures.Report($"{textures.Count} textures, {layers.Count} materials, {variants} alternatives");
    }
}
