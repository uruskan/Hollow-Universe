using System;
using System.Collections.Generic;
using Hollow.Client.Audio;

namespace Hollow.Tests;

public static class AudioLoopBudgetTests
{
    [Test]
    public static void AudioLoopBudgetSelectsNearbySourcesAndRecoversAfterContention()
    {
        var scheduler = new SoundVoiceScheduler();
        AudioVoiceBudget budget = AudioVoiceBudget.Default;
        var candidates = new List<SoundVoiceCandidate>();
        // A crowd beyond the old logical cap is retained by the owner. Only the closest audible sources win.
        for (int i = 0; i < 2000; i++)
            candidates.Add(new SoundVoiceCandidate(i + 1, "engine", 2, 65,
                SoundVoiceScheduler.AudibleDb(0, 1000 + i, 4, 300, true), false, i + 1));
        scheduler.Select(candidates, 2, budget);
        Assert.Equal(0, scheduler.SelectedCount, "distant loops consume no physical voices");
        candidates[1999] = candidates[1999] with { AudibleDb = -3 };
        candidates[1998] = candidates[1998] with { AudibleDb = -6 };
        scheduler.Select(candidates, 2, budget);
        Assert.True(scheduler.Selected(2000) && scheduler.Selected(1999), "late-created sources recover on approach");
        candidates.Add(new SoundVoiceCandidate(-1, "shot", 2, 100, 0, false, 2001));
        scheduler.Select(candidates, 2, budget);
        Assert.True(scheduler.Selected(-1) && scheduler.Selected(2000) && !scheduler.Selected(1999), "important audible shot displaces quieter loop");
        candidates.RemoveAt(candidates.Count - 1);
        scheduler.Select(candidates, 2, budget);
        Assert.True(scheduler.Selected(2000) && scheduler.Selected(1999), "same logical loops reacquire after shot ends");
    }

    [Test]
    public static void AudioLoopBudgetEnforcesEventLimitsWithoutStarvingOtherFamilies()
    {
        var scheduler = new SoundVoiceScheduler();
        var candidates = new List<SoundVoiceCandidate>();
        // Hundreds of louder requests from one family cannot crowd quieter eligible families out of a shortlist.
        for (int i = 1; i <= 1000; i++) candidates.Add(new SoundVoiceCandidate(i, "fire", 1, 50, 0, false, i));
        candidates.Add(new SoundVoiceCandidate(1001, "engine", 2, 50, -5, false, 1001));
        candidates.Add(new SoundVoiceCandidate(1002, "engine", 2, 50, -6, false, 1002));
        candidates.Add(new SoundVoiceCandidate(1003, "engine", 2, 50, -7, false, 1003));
        candidates.Add(new SoundVoiceCandidate(1004, "machine", 1, 50, -8, false, 1004));
        scheduler.Select(candidates, 4, AudioVoiceBudget.Default);
        Assert.Equal(4, scheduler.SelectedCount, "global capacity");
        Assert.True(scheduler.Selected(1000) && scheduler.Selected(1001) && scheduler.Selected(1002) && scheduler.Selected(1004), "per-family and total caps both hold");
        candidates.Reverse();
        scheduler.Select(candidates, 4, AudioVoiceBudget.Default);
        Assert.True(scheduler.Selected(1000) && scheduler.Selected(1001) && scheduler.Selected(1002) && scheduler.Selected(1004), "selection is independent of emitter iteration order");
    }

    [Test]
    public static void AudioLoopBudgetKeepsSilentLayersVirtualAndAvoidsBoundaryThrashing()
    {
        var scheduler = new SoundVoiceScheduler();
        var candidates = new List<SoundVoiceCandidate>
        {
            new(1, "idle", 2, 50, -10, true, 1),
            new(2, "idle", 2, 50, -9, false, 2),
            new(3, "boost", 2, 100, -80, false, 3),
        };
        scheduler.Select(candidates, 1, AudioVoiceBudget.Default);
        Assert.True(scheduler.Selected(1), "one dB difference retains current loop");
        candidates[1] = candidates[1] with { AudibleDb = -6 };
        scheduler.Select(candidates, 1, AudioVoiceBudget.Default);
        Assert.True(scheduler.Selected(2), "a meaningfully nearer/louder loop wins");
        Assert.True(!scheduler.Selected(3), "priority cannot make an inaudible layer consume a voice");
        Assert.True(double.IsNegativeInfinity(SoundVoiceScheduler.AudibleDb(0, 35, 4, 35, true)), "range boundary virtualizes");
        Assert.Near(-20, SoundVoiceScheduler.AudibleDb(0, 40, 4, 100, true), 1e-6, "inverse-distance ranking");
        Assert.Near(-5, SoundVoiceScheduler.AudibleDb(-5, 10000, 4, 35, false), 1e-6, "flat beds ignore world distance");
    }

    [Test]
    public static void AudioLoopEnvelopeFadesAndAdvancesWhileVirtual()
    {
        var envelope = new SoundLoopEnvelope(-80);
        envelope.SetGain(0, 2);
        envelope.Advance(1);
        Assert.Near(-40, envelope.GainDb, 1e-6, "halfway through virtual fade-in");
        envelope.SetPitch(2);
        envelope.Advance(1);
        Assert.Near(0, envelope.GainDb, 1e-6, "virtual gain reaches target");
        Assert.Near(3, envelope.Cursor, 1e-6, "virtual playhead follows pitch changes");
        envelope.Stop(0.5f);
        envelope.SetGain(12, 0);
        envelope.Advance(0.25);
        Assert.Near(-40, envelope.GainDb, 1e-6, "stopped loop cannot be revived by late controller gain");
        envelope.Advance(0.25);
        Assert.True(envelope.Finished, "virtual stop retires without a physical voice");
        var immediate = new SoundLoopEnvelope(-80);
        immediate.SetGain(0, 0);
        Assert.Near(0, immediate.GainDb, 1e-6, "zero fade is immediate");
        immediate.Stop(0);
        Assert.True(immediate.Finished, "immediate stop");
    }

    [Test]
    public static void AudioLoopBudgetReadsDataAndRejectsUnboundedOrMalformedSettings()
    {
        AudioVoiceBudget budget = AudioVoiceBudget.Parse("""{"voiceBudget":{"spatialVoices":12,"flatVoices":3,"updateHz":10,"virtualizeBelowDb":-55,"hysteresisDb":1,"priorityWeightDb":0.25}}""");
        Assert.Equal(12, budget.SpatialVoices, "spatial budget from data");
        Assert.Equal(3, budget.FlatVoices, "flat budget from data");
        Assert.Near(10, budget.UpdateHz, 0, "update rate from data");
        foreach (string json in new[]
        {
            """{"voiceBudget":{"spatialVoices":100000}}""", """{"voiceBudget":{"flatVoices":-1}}""",
            """{"voiceBudget":{"updateHz":1000}}""", """{"voiceBudget":{"hysteresisDb":-1}}""",
            """{"voiceBudget":{"virtualizeBelowDb":-100}}""", """{"voiceBudget":{"spatialVoices":1.5}}""",
            """{"voiceBudget":{"updateHz":"fast"}}""", """{"voiceBudget":[]} """,
        })
        {
            bool rejected = false;
            try { AudioVoiceBudget.Parse(json); } catch (FormatException) { rejected = true; }
            Assert.True(rejected, "bad configuration rejected: " + json);
        }
    }
}
