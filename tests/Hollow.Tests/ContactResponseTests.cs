using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Client.Audio;
using Hollow.Client.Feedback;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

public static class ContactResponseTests
{
    private static string Data(string name) => Path.Combine(Fixtures.Root, "game", "data", "ground", name);
    private static ContactResponseData Responses() => ContactResponseData.Load(Data(ContactResponseData.FileName));

    [Test]
    public static void EveryPhysicalSurfaceResolvesCompleteExistingAudioAndEffects()
    {
        ContactResponseData responses = Responses();
        SoundCatalogData audio = SoundCatalogData.Load(Data("sounds.json"));
        using JsonDocument fx = JsonDocument.Parse(File.ReadAllText(Data("fx.json")));
        var effects = fx.RootElement.GetProperty("effects").EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToHashSet();
        foreach (var surface in ContentFixture.Content.Surfaces.Definitions)
            Assert.True(responses.Surfaces.ContainsKey(surface.Key), $"{surface.Key}: explicit response coverage");
        foreach (var surface in responses.Surfaces)
        {
            foreach (var action in surface.Value.Actions)
            {
                Assert.True(audio.Find(action.Value.Sound) != null, $"{surface.Key}/{action.Key}: existing sound");
                Assert.True(effects.Contains(action.Value.Effect), $"{surface.Key}/{action.Key}: existing effect");
            }
        }
    }

    [Test]
    public static void CompoundStoneMetalAndFleshUseTheirDeclaredResponses()
    {
        var materials = ContentFixture.Content.Surfaces;
        var responses = Responses();
        foreach (string item in new[] { "stone-compound-wall", "stone-compound-gate", "large-furnace" })
            Assert.Equal("impact.stone", responses.Resolve(materials.Key(materials.FromDeployable(item)), "bullet").Sound, item);
        foreach (string item in new[] { "auto-turret", "sam-site", "metal-barricade" })
            Assert.Equal("impact.metal", responses.Resolve(materials.Key(materials.FromDeployable(item)), "bullet").Sound, item);
        Assert.Equal("arrow.impact.flesh", responses.Resolve(materials.Key(materials.FleshId), "arrow").Sound, "all flesh targets share response");
        Assert.Equal("impact.bullet.water", responses.Resolve("water", "bullet", "incendiary").Effect, "water suppresses incendiary response by data");
        Assert.Equal("impact.incendiary", responses.Resolve("wood", "bullet", "incendiary").Effect, "wood incendiary override");
        Assert.Equal("impact.hull", responses.Resolve("hull", "bullet").Effect, "hull retains authored response");
        Assert.Equal(FootSurface.Metal, responses.Surface("hull").Footstep, "same identity has a distinct footstep response");
        Assert.Equal("impact.dirt", responses.Resolve("not-known", "bullet").Sound, "unknown is explicit neutral fallback, never guessed wood");
    }

    [Test]
    public static void ResponseDataRejectsMissingActionsAndAllowsNewMaterialsWithoutCode()
    {
        string json = File.ReadAllText(Data(ContactResponseData.FileName));
        string added = json.Replace("\"unknown\":", "\"new-composite\": {\"footstep\":\"wood\",\"dust\":[0.1,0.2,0.3],\"actions\":{\"bullet\":{\"sound\":\"impact.wood\",\"effect\":\"impact.bullet.wood\"},\"arrow\":{\"sound\":\"arrow.impact\",\"effect\":\"impact.bullet.wood\"}}},\n\"unknown\":", StringComparison.Ordinal);
        Assert.Equal("impact.wood", ContactResponseData.Parse(added).Resolve("new-composite", "bullet").Sound, "new material needs only data");
        bool rejected = false;
        try { ContactResponseData.Parse(json.Replace("\"arrow\":", "\"missing-arrow\":", StringComparison.Ordinal)); }
        catch (InvalidDataException) { rejected = true; }
        Assert.True(rejected, "missing action coverage is an authoring error");
    }

    [Test]
    public static void SharedReloadCuesHandleTimeJumpsLateObserversCancellationAndRestart()
    {
        var timeline = ActionCueData.Load(Data(ActionCueData.FileName)).For("reload");
        var local = new ActionCuePlayer();
        var remote = new ActionCuePlayer();
        var a = new List<string>(); var b = new List<string>();
        local.Start(timeline, 2); remote.Start(timeline, 2);
        foreach (double time in new[] { 0.0, 0.4, 1.2, 1.2, 1.9, 2.5 })
        { local.Advance(time, true, a.Add); remote.Advance(time, true, b.Add); }
        Assert.Equal("magOut,magIn,rack", string.Join(',', a), "each cue once across skipped frames");
        Assert.True(a.SequenceEqual(b), "owner and observer use the same timeline");
        remote.Start(timeline, 2, 1.2); b.Clear(); remote.Advance(1.9, true, b.Add);
        Assert.Equal("rack", string.Join(',', b), "late observer skips past cues");
        local.Start(timeline, 2); a.Clear(); local.Advance(0, true, a.Add); local.Advance(1.2, false, a.Add); local.Advance(1.9, true, a.Add);
        Assert.Equal("magOut", string.Join(',', a), "cancelled action never resumes stale cues");
        local.Start(timeline, 4); a.Clear(); local.Advance(2.1, true, a.Add);
        Assert.Equal("magOut", string.Join(',', a), "new gameplay duration drives cue timing");
        local.Advance(2.3, true, a.Add); Assert.Equal("magOut,magIn", string.Join(',', a), "new magazine cue retimed");
    }

    [Test]
    public static void ContactHistorySeparatesVictimsSurvivesSameTickBurstsAndClears()
    {
        var history = new RecentContactFeedback();
        var first = new TargetRef(TargetFamily.Character, 10, 1);
        var other = new TargetRef(TargetFamily.Character, 11, 1);
        for (int i = 0; i < 300; i++) history.Note(first, 1);
        Assert.True(history.Contains(first with { Part = 2 }, 1.1), "same victim across body parts survives eviction of older equal-time entries");
        Assert.True(!history.Contains(other, 1.1), "nearby victim isn't suppressed");
        Assert.True(!history.Contains(first, 1.5), "history expires");
        history.Clear(); Assert.True(!history.Contains(first, 1.1), "world retirement clears history");
    }

    [Test]
    public static void AcceptedActionCuesRequireOneTimelySuccessfulAnswer()
    {
        var pending = new ConfirmedFeedback();
        pending.Track(1, "build.repair", default, 0);
        Assert.True(pending.Complete(1, false, 1) == null, "refused repair stays silent");
        pending.Track(2, "build.repair", default, 0);
        Assert.Equal("build.repair", pending.Complete(2, true, 1)!.Value.Sound, "accepted repair emits cue");
        Assert.True(pending.Complete(2, true, 2) == null, "duplicate result stays silent");
        pending.Track(3, "build.repair", default, 0);
        Assert.True(pending.Complete(3, true, 11) == null, "expired result stays silent");
        pending.Track(4, "build.repair", default, 0); pending.Clear();
        Assert.True(pending.Complete(4, true, 1) == null, "world retirement clears pending effects");
    }
}
