using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Hollow.Client.Audio;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;

namespace Hollow.Tests;

public static class FootwearAudioTests
{
    private const string ProfilesJson = """
        {"defaultProfile":"barefoot","profiles":[{"key":"barefoot"},{"key":"shoes"},{"key":"boots"}]}
        """;

    [Test]
    public static void FootwearFollowsEquipAndUnequipForLocalAndRemotePlayers()
    {
        FootwearAudioData audio = FootwearAudioData.Parse(ProfilesJson);
        GameContent content = ContentFixture.Content;
        var inventory = new PlayerInventory();
        inventory.Main.Set(0, ItemStack.Of(content.ItemByKey("work-boots"), 1));
        inventory.Main.Set(1, ItemStack.Of(content.ItemByKey("woven-shirt"), 1));

        void Expect(string expected)
        {
            ushort[] remote = inventory.Wear.Slots.ToArray().Where(stack => !stack.IsEmpty).Select(stack => stack.Item).ToArray();
            Assert.Equal(expected, audio.Profile(content, inventory.Wear.Slots), "local equipped profile");
            Assert.Equal(expected, audio.Profile(content, remote), "replicated worn IDs choose the same profile");
        }

        Expect("barefoot"); // Carrying boots has no effect until they are worn.
        Assert.Equal(OutcomeCode.Done, InventoryRules.Move(content, inventory.Main, 1, inventory.Wear, 0, 0), "wear shirt");
        Expect("barefoot");
        Assert.Equal(OutcomeCode.Done, InventoryRules.Move(content, inventory.Main, 0, inventory.Wear, 4, 0), "wear boots in any wear slot");
        Expect("boots");
        Assert.Equal(OutcomeCode.Done, InventoryRules.Move(content, inventory.Wear, 4, inventory.Main, 0, 0), "remove boots");
        Expect("barefoot");
    }

    [Test]
    public static void FootwearSupportsAdditionalShoeProfilesWithoutItemSwitches()
    {
        // Use an existing footwear item in an in-memory fixture: the shipped game does not gain a new item.
        foreach (string profile in new[] { "shoes", "soft_shoes-6" })
        {
            GameContent content = ContentWithProfile("work-boots", profile);
            Assert.True(content.Problems.Count == 0, "new profile names are valid item data");
            FootwearAudioData audio = FootwearAudioData.Parse("""
                {"defaultProfile":"barefoot","profiles":[{"key":"barefoot"},{"key":"shoes"},{"key":"soft_shoes-6"}]}
                """);
            ushort id = content.ItemByKey("work-boots").Id;
            Assert.Equal(profile, audio.Profile(content, new[] { new ItemStack(id, 1) }), "local custom footwear assignment");
            Assert.Equal(profile, audio.Profile(content, new[] { id }), "remote custom footwear assignment");
            Assert.Equal("footstep.walk.wood." + profile, audio.EventKey("footstep.walk.wood", profile), "custom contact bank");
            Assert.Equal(Footsteps.SwimKey, audio.EventKey(Footsteps.SwimKey, profile), "swim remains shared");
        }
    }

    [Test]
    public static void FootwearFallbacksStayAudibleAndReportUnknownReferences()
    {
        FootwearAudioData audio = FootwearAudioData.Parse(ProfilesJson);
        GameContent content = ContentWithProfile("work-boots", "missing-profile");
        ushort boots = content.ItemByKey("work-boots").Id;
        Assert.Equal("barefoot", audio.Profile(content, new[] { boots }), "unknown equipment profile falls back");
        Assert.Equal("barefoot", audio.Profile(content, new[] { ushort.MaxValue }), "unknown replicated item falls back");
        Assert.Equal("barefoot", audio.Profile(content, new[] { new ItemStack(boots, 0) }), "empty local stacks have no footwear");
        Assert.Equal("footstep.walk.grass.barefoot", audio.EventKey("footstep.walk.grass", null), "unspecified default");
        Assert.Equal("footstep.walk.grass.barefoot", audio.EventKey("footstep.walk.grass", "missing-profile"), "unknown bank default");

        SoundCatalogData catalogue = SoundCatalogData.Parse("""
            {"events":[{"key":"footstep.walk.grass.barefoot","files":["barefoot.ogg"]},
                       {"key":"footstep.walk.wood","files":["legacy.ogg"]}]}
            """);
        Assert.Equal("footstep.walk.grass.barefoot", audio.ResolveEvent("footstep.walk.grass", "boots", catalogue), "missing bank uses default");
        Assert.Equal("footstep.walk.wood", audio.ResolveEvent("footstep.walk.wood", "boots", catalogue), "missing default uses legacy");
        Assert.True(audio.Validate(content, catalogue).Any(problem => problem.Contains("unknown footstep profile 'missing-profile'", StringComparison.Ordinal)),
            "authoring validation reports the fallback instead of concealing a bad assignment");
    }

    [Test]
    public static void FootwearRegistryRejectsAmbiguousOrUnsafeDefinitions()
    {
        foreach (string json in new[]
        {
            """{"defaultProfile":"barefoot","profiles":[{"key":"barefoot"},{"key":"barefoot"}]}""",
            """{"defaultProfile":"missing","profiles":[{"key":"barefoot"}]}""",
            """{"defaultProfile":"barefoot","profiles":[{"key":"barefoot"},{"key":"boots.run"}]}""",
            """{"defaultProfile":"barefoot","profiles":[{"key":"barefoot"},{"key":"Boots"}]}""",
            """{"defaultProfile":"barefoot","profiles":[]}""",
            """{"defaultProfile":"barefoot","profiles":[{"key":"barefoot"},null]}""",
            "[]",
            "{",
        })
        {
            bool rejected = false;
            try { FootwearAudioData.Parse(json); }
            catch (FormatException) { rejected = true; }
            Assert.True(rejected, "malformed or ambiguous registry must fail: " + json);
        }

        GameContent shirt = ContentWithProfile("woven-shirt", "boots");
        Assert.True(shirt.Problems.Any(problem => problem.Contains("only clothing worn on the feet", StringComparison.Ordinal)),
            "non-footwear cannot claim a contact profile");
        GameContent invalid = ContentWithProfile("work-boots", "boots.run");
        Assert.True(invalid.Problems.Any(problem => problem.Contains("wear.footstepProfile: expected", StringComparison.Ordinal)),
            "item profile keys use the same syntax as the registry");
    }

    [Test]
    public static void FootwearCatalogueCoversEveryProfileSurfaceAndGait()
    {
        FootwearAudioData audio = FootwearAudioData.Load(Path.Combine(ContentFixture.Folder, FootwearAudioData.FileName));
        SoundCatalogData catalogue = SoundCatalogData.Load(Path.Combine(ContentFixture.Folder, SoundCatalogData.FileName));
        IReadOnlyList<string> problems = audio.Validate(ContentFixture.Content, catalogue);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        foreach (string profile in new[] { "barefoot", "shoes", "boots" }) Assert.True(audio.Contains(profile), "shipped profile " + profile);
        foreach (string profile in audio.Profiles)
        {
            foreach (string key in Footsteps.AllKeys().Where(key => key != Footsteps.SwimKey))
            {
                SoundEventData sound = catalogue.Find(audio.EventKey(key, profile))!;
                int minimum = key.StartsWith("footstep.land.", StringComparison.Ordinal) ? 1
                    : key.StartsWith("footstep.run.", StringComparison.Ordinal) ? 5 : 8;
                Assert.True(sound.Files.Count >= minimum && sound.Files.Distinct(StringComparer.Ordinal).Count() == sound.Files.Count,
                    sound.Key + $": at least {minimum} unique clean contacts (clipped sources are excluded)");
            }
        }
    }

    private static GameContent ContentWithProfile(string itemKey, string profile)
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        JsonNode root = JsonNode.Parse(texts["items.json"])!;
        JsonNode item = root["items"]!.AsArray().Single(node => node!["key"]!.GetValue<string>() == itemKey)!;
        item["wear"]!["footstepProfile"] = profile;
        texts["items.json"] = root.ToJsonString();
        return GameContent.FromTexts(texts);
    }
}
