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
            if (profile.TryGetProperty("firstPersonRig", out JsonElement rig) && rig.ValueKind != JsonValueKind.Null)
            {
                Vector(rig.GetProperty("position"), item + ".firstPersonRig.position");
                Vector(rig.GetProperty("rotation"), item + ".firstPersonRig.rotation");
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

    [Test]
    public static void HeldSkeletalBindingsResolveToBothBodyArmAnimations()
    {
        using JsonDocument art = Read("held-presentations.json");
        var bindings = new List<(string Label, string Clip)>();
        foreach (JsonElement profile in art.RootElement.GetProperty("profiles").EnumerateArray())
            foreach (JsonProperty action in profile.GetProperty("actions").EnumerateObject())
            {
                if (!action.Value.TryGetProperty("skeletal", out JsonElement skeletal) || skeletal.ValueKind == JsonValueKind.Null) continue;
                string label = profile.GetProperty("item").GetString() + "." + action.Name;
                string clip = skeletal.GetProperty("clip").GetString() ?? "";
                Assert.True(!string.IsNullOrWhiteSpace(clip) && clip.Length <= 128 &&
                    clip.IndexOfAny(new[] { '/', '\\', ':', '[', ']', ',' }) < 0, label + ": simple skeletal clip name");
                float start = skeletal.TryGetProperty("start", out JsonElement startValue) ? startValue.GetSingle() : 0f;
                float end = skeletal.TryGetProperty("end", out JsonElement endValue) ? endValue.GetSingle() : 1f;
                bool wrap = skeletal.TryGetProperty("wrap", out JsonElement wrapValue) && wrapValue.GetBoolean();
                Assert.True(float.IsFinite(start) && float.IsFinite(end) && start >= 0 && start <= 1 && end >= 0 && end <= 1 &&
                    (wrap ? end <= start : start <= end), label + ": source segment must be valid within [0,1]");
                bindings.Add((label, clip));
            }
        foreach (string stem in new[] { "animations", "animations_female" })
        {
            string folder = Path.Combine(Fixtures.Root, "game", "assets", "characters");
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, stem + ".json")));
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, stem + ".glb"));
            int jsonLength = BitConverter.ToInt32(bytes, 12);
            using JsonDocument glb = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, 20, jsonLength));
            JsonElement[] nodes = glb.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
            var animations = glb.RootElement.GetProperty("animations").EnumerateArray()
                .ToDictionary(a => a.GetProperty("name").GetString()!, a => a);
            foreach ((string label, string clip) in bindings)
            {
                Assert.True(metadata.RootElement.GetProperty("clips").TryGetProperty(clip, out JsonElement source) &&
                    source.GetProperty("length").GetDouble() > 0, label + ": source metadata in " + stem);
                Assert.True(animations.TryGetValue(clip, out JsonElement animation), label + ": source animation in " + stem);
                var rotations = animation.GetProperty("channels").EnumerateArray()
                    .Select(c => c.GetProperty("target"))
                    .Where(t => t.GetProperty("path").GetString() == "rotation")
                    .Select(t => nodes[t.GetProperty("node").GetInt32()].GetProperty("name").GetString())
                    .ToHashSet();
                foreach (string bone in new[] { "spine_01", "clavicle_r", "upperarm_r", "lowerarm_r", "hand_r",
                    "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l" })
                    Assert.True(rotations.Contains(bone), label + ": authored " + bone + " rotation in " + stem);
            }
        }
        Fixtures.Report($"{bindings.Count} skeletal held-action bindings resolve on both bodies with authored torso and arm rotations");
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
