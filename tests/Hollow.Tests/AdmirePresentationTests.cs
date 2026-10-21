using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hollow.Tests;

/// <summary>Admiring is a cosmetic overlay: every held model can be inspected and returns to its existing idle.</summary>
public static class AdmirePresentationTests
{
    [Test]
    public static void EveryHeldItemHasACompleteCosmeticAdmireCurve()
    {
        string folder = Path.Combine(Fixtures.Root, "game", "data", "ground");
        using JsonDocument models = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "models.json")));
        using JsonDocument presentations = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "held-presentations.json")));
        var expected = models.RootElement.GetProperty("models").EnumerateArray()
            .Where(model => model.TryGetProperty("held", out _))
            .Select(model => model.GetProperty("key").GetString()!).ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement profile in presentations.RootElement.GetProperty("profiles").EnumerateArray())
        {
            string item = profile.GetProperty("item").GetString()!;
            string label = item + ".admire";
            Assert.True(actual.Add(item), label + ": one profile per held item");
            Assert.True(profile.GetProperty("actions").TryGetProperty("admire", out JsonElement admire), label + ": action missing");
            double duration = admire.GetProperty("duration").GetDouble();
            Assert.True(double.IsFinite(duration) && duration >= 1 && duration <= 8, label + ": time to inspect and return");
            Assert.Near(-1, admire.GetProperty("marker").GetDouble(), 0.000001, label + ": no gameplay contact or release marker");
            Assert.True(!admire.TryGetProperty("skeletal", out JsonElement skeletal) || skeletal.ValueKind == JsonValueKind.Null,
                label + ": additive inspection preserves the existing skeletal idle");

            JsonElement[] keys = admire.GetProperty("keys").EnumerateArray().ToArray();
            Assert.True(keys.Length >= 4, label + ": rest, two inspection directions, and return");
            Assert.Near(0, keys[0].GetProperty("at").GetDouble(), 0.000001, label + ": timeline begins at rest");
            Assert.Near(1, keys[^1].GetProperty("at").GetDouble(), 0.000001, label + ": timeline completes at rest");
            double previous = -1;
            var positions = new List<double[]>();
            var rotations = new List<double[]>();
            foreach (JsonElement key in keys)
            {
                double at = key.GetProperty("at").GetDouble();
                Assert.True(double.IsFinite(at) && at >= 0 && at <= 1 && at > previous, label + ": ordered normalized keyframes");
                previous = at;
                positions.Add(Vector(key, "position", label));
                rotations.Add(Vector(key, "rotation", label));
            }

            foreach (int endpoint in new[] { 0, keys.Length - 1 })
            {
                Assert.True(positions[endpoint].All(value => Math.Abs(value) < 0.000001), label + ": seamless position at rest");
                Assert.True(rotations[endpoint].All(value => Math.Abs(value) < 0.000001), label + ": seamless rotation at rest");
            }
            Assert.True(positions.Skip(1).SkipLast(1).Any(position => position.Any(value => Math.Abs(value) > 0.001)),
                label + ": bring the held item into view");
            double[][] inspectionRotations = rotations.Skip(1).SkipLast(1)
                .Where(rotation => rotation.Any(value => Math.Abs(value) >= 1)).ToArray();
            Assert.True(inspectionRotations.Length >= 2 && inspectionRotations.Any(first =>
                inspectionRotations.Any(second => first.Zip(second).Any(pair => Math.Abs(pair.First - pair.Second) >= 1))),
                label + ": turn the item through distinct inspection directions");
        }

        Assert.True(expected.Count > 0 && expected.SetEquals(actual),
            "Admire coverage must match held models; missing: " + string.Join(", ", expected.Except(actual)));
        Fixtures.Report($"{actual.Count} held items have cosmetic admire curves with inspection turns and seamless endpoints");
    }

    private static double[] Vector(JsonElement key, string channel, string label)
    {
        double[] values = key.GetProperty(channel).EnumerateArray().Select(value => value.GetDouble()).ToArray();
        Assert.True(values.Length == 3 && values.All(double.IsFinite), label + "." + channel + ": finite 3D vector");
        return values;
    }
}
