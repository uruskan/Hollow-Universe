using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Hollow.Tests;

/// <summary>
/// The building's drawn meshes against their slots (GROUND.md §6.3, Fit), read from the GLBs without Godot: every level of
/// detail of every piece in every grade reaches its slot's faces, so neighbours meet without a gap, and every wall is
/// drawn tucked under its base, so whatever it stands on and whatever stands on it is the drawn face where they meet.
/// </summary>
public static class BuildingFitTests
{
    private static readonly string[] Grades = { "twig", "wood", "stone", "metal", "armoured" };

    [Test]
    public static void EveryPieceFillsItsSlot()
    {
        string root = Fixtures.Root;
        using JsonDocument building = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "game", "data", "ground", "building.json")));
        var pieces = building.RootElement.GetProperty("pieces").EnumerateArray().ToDictionary(p => p.GetProperty("key").GetString()!, p => p);
        double half = building.RootElement.GetProperty("module").GetProperty("width").GetDouble() / 2;
        double foundationDepth = building.RootElement.GetProperty("module").GetProperty("foundationDepth").GetDouble();
        double triangleBase = half * 2 * Math.Sqrt(3) / 6;
        const double reach = 0.002;
        int meshes = 0;
        foreach (string grade in Grades)
        {
            string path = Path.Combine(root, "game", "assets", "building", $"building_{grade}.glb");
            foreach ((string name, double[] min, double[] max) in Bounds(path))
            {
                string key = name.Split("_LOD")[0];
                if (name.Contains("-convcolonly", StringComparison.Ordinal) || !pieces.TryGetValue(key, out JsonElement piece))
                {
                    continue;
                }
                meshes++;
                string what = $"{grade} {name}";
                string family = piece.GetProperty("family").GetString()!;
                bool triangle = piece.GetProperty("shape").GetString() == "triangle";
                if (family == "wall")
                {
                    double h = piece.GetProperty("height").GetDouble();
                    Assert.True(min[1] < -reach, $"{what}: its base at {min[1]:0.000} m, not tucked under the slot's base");
                    Assert.True(max[1] < h + 0.015, $"{what}: its top at {max[1]:0.000} m, over what stands on it");
                    Assert.True(min[0] <= -half + reach && max[0] >= half - reach, $"{what}: {min[0]:0.000} to {max[0]:0.000} m, short of its ends");
                }
                else if (family is "foundation" or "floor")
                {
                    Assert.InRange(-reach, 0.025, max[1], $"{what}: its top");
                    if (family == "foundation")
                    {
                        Assert.Near(-foundationDepth, min[1], reach, $"{what}: its skirt reaches the placement/collision depth");
                    }
                    Assert.True(max[2] >= (triangle ? triangleBase : half) - reach, $"{what}: its front edge at {max[2]:0.000} m, short of the slot");
                    if (!triangle)
                    {
                        Assert.True(min[0] <= -half + reach && max[0] >= half - reach && min[2] <= -half + reach,
                            $"{what}: {min[0]:0.000} to {max[0]:0.000} by {min[2]:0.000} to {max[2]:0.000} m, short of the footprint");
                    }
                }
                else if (piece.TryGetProperty("stairs", out JsonElement stairs) && stairs.GetString() == "spiral")
                {
                    Assert.InRange(-reach, reach, min[1], $"{what}: its column's foot");
                }
            }
        }
        Assert.True(meshes >= 250, $"only {meshes} building meshes found");
        Fixtures.Report($"{meshes} building meshes fill their slots");
    }

    /// <summary>Every mesh node's name and its POSITION bounds (glTF requires them on the accessor).</summary>
    private static IEnumerable<(string Name, double[] Min, double[] Max)> Bounds(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Assert.True(data.Length > 20 && Encoding.ASCII.GetString(data, 0, 4) == "glTF", $"{path}: not a GLB");
        int length = BitConverter.ToInt32(data, 12);
        using JsonDocument json = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, length));
        JsonElement root = json.RootElement;
        JsonElement meshes = root.GetProperty("meshes"), accessors = root.GetProperty("accessors");
        var result = new List<(string, double[], double[])>();
        foreach (JsonElement node in root.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("mesh", out JsonElement mesh) || !node.TryGetProperty("name", out JsonElement name))
            {
                continue;
            }
            Assert.True(!node.TryGetProperty("translation", out _) && !node.TryGetProperty("rotation", out _) && !node.TryGetProperty("scale", out _)
                && !node.TryGetProperty("matrix", out _), $"{path} {name.GetString()}: a transform on the node (vertices carry the fit)");
            var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var max = new[] { double.MinValue, double.MinValue, double.MinValue };
            foreach (JsonElement primitive in meshes[mesh.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                JsonElement accessor = accessors[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                for (int i = 0; i < 3; i++)
                {
                    min[i] = Math.Min(min[i], accessor.GetProperty("min")[i].GetDouble());
                    max[i] = Math.Max(max[i], accessor.GetProperty("max")[i].GetDouble());
                }
            }
            result.Add((name.GetString()!, min, max));
        }
        return result;
    }
}
