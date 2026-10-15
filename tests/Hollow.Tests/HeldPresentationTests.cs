using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Content;

namespace Hollow.Tests;

/// <summary>Content integration gates: new held items cannot silently ship without editable FPS/TPS art.</summary>
public static class HeldPresentationTests
{
    private static JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(Fixtures.Root, "game", "data", "ground", name)));

    [Test]
    public static void EveryHeldModelHasAnExplicitPresentation()
    {
        using JsonDocument models = Read("models.json");
        using JsonDocument art = Read("held-presentations.json");
        Assert.Equal(1, art.RootElement.GetProperty("version").GetInt32(), "presentation format");
        var expected = models.RootElement.GetProperty("models").EnumerateArray()
            .Where(m => m.TryGetProperty("held", out _)).Select(m => m.GetProperty("key").GetString()!).ToHashSet();
        var actual = new HashSet<string>();
        foreach (JsonElement profile in art.RootElement.GetProperty("profiles").EnumerateArray())
        {
            string item = profile.GetProperty("item").GetString()!;
            Assert.True(actual.Add(item), $"Duplicate presentation: {item}");
            Assert.True(expected.Contains(item), $"No held model for presentation {item}");
            Assert.True(!string.IsNullOrWhiteSpace(profile.GetProperty("family").GetString()), $"{item}: family");
            foreach (string view in new[] { "firstPerson", "thirdPerson" })
            {
                Vector(profile.GetProperty(view).GetProperty("position"), item + "." + view);
                Vector(profile.GetProperty(view).GetProperty("rotation"), item + "." + view);
            }
            foreach (string hand in new[] { "rightHand", "leftHand" })
            {
                JsonElement h = profile.GetProperty(hand);
                Vector(h.GetProperty("pole"), item + "." + hand);
                float[] fingers = h.GetProperty("fingers").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                Assert.True(fingers.Length is 0 or 15, $"{item}: finger pose needs all five fingers or explicit measured wrapping");
                Assert.True(fingers.All(f => float.IsFinite(f) && f >= -30 && f <= 140), $"{item}: finger joint bounds");
            }
        }
        Assert.True(expected.SetEquals(actual), "Missing held profiles: " + string.Join(", ", expected.Except(actual)));
        Fixtures.Report($"{actual.Count} held models have explicit editable profiles");
    }

    [Test]
    public static void HeldActionsCoverGameplayAndHaveContinuousTimelines()
    {
        using JsonDocument items = Read("items.json");
        using JsonDocument art = Read("held-presentations.json");
        var definitions = items.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("key").GetString()!, i => i);
        int actionsChecked = 0;
        foreach (JsonElement profile in art.RootElement.GetProperty("profiles").EnumerateArray())
        {
            string item = profile.GetProperty("item").GetString()!;
            JsonElement actions = profile.GetProperty("actions");
            Assert.True(actions.TryGetProperty("idle", out _), $"{item}: idle missing");
            Assert.True(actions.TryGetProperty(profile.GetProperty("useAction").GetString()!, out _), $"{item}: primary presentation missing");
            if (definitions.TryGetValue(item, out JsonElement definition) && definition.TryGetProperty("melee", out _))
                Assert.True(actions.TryGetProperty("swing", out _), $"{item}: melee swing missing");
            ItemDef gameplay = ContentFixture.Content.ItemByKey(item);
            string[] required = gameplay.Held switch
            {
                HeldUse.Firearm => new[] { "aim", "fire", "reload" },
                HeldUse.Bow => new[] { "draw", "release" },
                HeldUse.Throwable => new[] { "throw" },
                HeldUse.Hammer => new[] { "repair" },
                _ => Array.Empty<string>(),
            };
            foreach (string action in required)
                Assert.True(actions.TryGetProperty(action, out _), $"{item}: {action} required by gameplay");
            foreach (JsonProperty action in actions.EnumerateObject())
            {
                string label = item + "." + action.Name;
                float duration = action.Value.GetProperty("duration").GetSingle();
                Assert.True(float.IsFinite(duration) && duration > 0 && duration <= 30, label + ": usable duration");
                float marker = action.Value.GetProperty("marker").GetSingle();
                Assert.True(float.IsFinite(marker) && marker >= -1 && marker <= 1, label + ": event marker");
                JsonElement[] keys = action.Value.GetProperty("keys").EnumerateArray().ToArray();
                Assert.True(keys.Length >= 2, label + ": complete curve");
                Assert.Near(0, keys[0].GetProperty("at").GetDouble(), 0.00001, label + ": start");
                Assert.Near(1, keys[^1].GetProperty("at").GetDouble(), 0.00001, label + ": end");
                double previous = -1;
                foreach (JsonElement key in keys)
                {
                    double at = key.GetProperty("at").GetDouble();
                    Assert.True(double.IsFinite(at) && at > previous && at <= 1, label + ": ordered distinct keyframes");
                    previous = at;
                    foreach (string channel in new[] { "position", "rotation", "thirdPersonPosition", "thirdPersonRotation",
                        "rightHandPosition", "rightHandRotation", "leftHandPosition", "leftHandRotation" })
                        if (key.TryGetProperty(channel, out JsonElement value)) Vector(value, label + "." + channel);
                    foreach (string hand in new[] { "rightFingers", "leftFingers" })
                    {
                        if (!key.TryGetProperty(hand, out JsonElement value)) continue;
                        float[] curls = value.EnumerateArray().Select(v => v.GetSingle()).ToArray();
                        Assert.True(curls.Length is 0 or 15 && curls.All(v => float.IsFinite(v) && v >= -30 && v <= 140),
                            label + ": animated finger channel is complete and bounded");
                    }
                }
                if (action.Name == profile.GetProperty("useAction").GetString())
                    foreach (string view in new[] { "", "thirdPerson" })
                    {
                        string position = view.Length == 0 ? "position" : view + "Position";
                        string rotation = view.Length == 0 ? "rotation" : view + "Rotation";
                        Assert.True(keys.Any(k => k.GetProperty(position).EnumerateArray().Any(v => Math.Abs(v.GetDouble()) > 0.00001) ||
                            k.GetProperty(rotation).EnumerateArray().Any(v => Math.Abs(v.GetDouble()) > 0.00001)),
                            label + ": primary action must animate in " + (view.Length == 0 ? "FPS" : "TPS"));
                    }
                actionsChecked++;
            }
        }
        Fixtures.Report($"{actionsChecked} authored action curves cover their whole normalized timeline");
    }

    private static void Vector(JsonElement element, string label)
    {
        float[] values = element.EnumerateArray().Select(v => v.GetSingle()).ToArray();
        Assert.True(values.Length == 3 && values.All(float.IsFinite), label + ": finite 3D vector");
    }

    [Test]
    public static void ReloadableMagazinesAreSeparateMeshesAtEveryLod()
    {
        // Regression: the rifle importer used the complete static pickup as Body, so a reload could move
        // the hand while its magazine remained baked into the receiver. These two fixtures use box magazines.
        foreach (string weapon in new[] { "pistol", "rifle" })
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Fixtures.Root, "game", "assets", "items", weapon + ".glb"));
            int jsonLength = BitConverter.ToInt32(bytes, 12);
            using JsonDocument glb = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, 20, jsonLength));
            JsonElement[] nodes = glb.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
            JsonElement magazine = nodes.Single(n => n.TryGetProperty("name", out JsonElement name) &&
                name.GetString() == "Magazine");
            Assert.True(magazine.TryGetProperty("children", out JsonElement children), weapon + ": movable magazine group");
            int[] childIndices = children.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            foreach (int lod in Enumerable.Range(0, 3))
                Assert.True(childIndices.Any(i => nodes[i].GetProperty("name").GetString() == "Magazine_lod" + lod &&
                    nodes[i].TryGetProperty("mesh", out _)), weapon + ": separate magazine geometry at LOD " + lod);
        }
    }
}
