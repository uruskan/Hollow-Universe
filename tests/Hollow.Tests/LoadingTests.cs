using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.Client;
using Hollow.Client.Loading;

namespace Hollow.Tests;

/// <summary>
/// The loading pipeline's rules (GROUND.md §4.3.1), without Godot: the game's plan reads and names every stage; the bar
/// is the stages' progress weighted and never goes back; play is handed over only when every stage is done; the steady
/// frames wait for the others; a far jump runs the arrival's stages again behind a grace; a failed stage stops the load;
/// and the client's own stages finish on a real join and a teleport.
/// </summary>
public static class LoadingTests
{
    private static string Data => Path.Combine(Fixtures.Root, "game", "data", "ground", LoadingPlan.FileName);

    // A clock the test moves.
    private sealed class Clock
    {
        public double Now;
    }

    private static LoadingPlan Plan(string stages) => LoadingPlan.Parse(
        "{ \"fadeSeconds\": 0.5, \"tipSeconds\": 8, \"arrival\": { \"metres\": 100, \"graceSeconds\": 0.25 }, \"stages\": [" + stages + "] }");

    [Test]
    public static void TheGamesPlanNamesEveryStage()
    {
        LoadingPlan plan = LoadingPlan.Load(Data);
        string[] keys = { "server", LoadingModule.ConnectStage, LoadingModule.WorldStage, "land", "forest", "renderer", SteadyFrames.Key };
        foreach (string key in keys)
        {
            StagePlan stage = plan.Stage(key);
            Assert.True(stage.Order != int.MaxValue, $"{key}: not in {LoadingPlan.FileName}");
            Assert.True(stage.Title.Length is > 0 and <= 32, $"{key}: a title of 1–32 characters ({stage.Title})");
            Assert.True(stage.Weight > 0, $"{key}: weight");
        }
        Assert.True(plan.Tips.Count >= 8, "at least eight tips");
        foreach (string tip in plan.Tips)
        {
            Assert.True(tip.Length <= 100, $"a tip fits on one line: {tip}");
        }
        Assert.True(plan.Picture.StartsWith("res://", StringComparison.Ordinal), "the picture is a res:// path");
        string picture = Path.Combine(Fixtures.Root, "game", plan.Picture["res://".Length..]);
        Assert.True(File.Exists(picture), $"{plan.Picture} exists");
        Assert.True(File.Exists(picture + ".import"), $"{plan.Picture} is imported (run Godot's import)");
        bool refused = false;
        try
        {
            Plan("{ \"key\": \"a\", \"title\": \"A\", \"weight\": 1 }, { \"key\": \"a\", \"title\": \"\", \"weight\": 0 }");
        }
        catch (InvalidDataException e)
        {
            refused = e.Message.Contains("listed twice", StringComparison.Ordinal) && e.Message.Contains("no title", StringComparison.Ordinal);
        }
        Assert.True(refused, "a plan with a stage twice, without a title or weight is refused, naming each problem");
    }

    [Test]
    public static void PlayStartsWhenEveryStageIsDoneAndTheBarNeverGoesBack()
    {
        var clock = new Clock();
        var pipeline = new LoadingPipeline(Plan(
            "{ \"key\": \"a\", \"title\": \"Alpha\", \"weight\": 3 }, { \"key\": \"b\", \"title\": \"Beta\", \"weight\": 1 }"), () => clock.Now);
        bool aDone = false, bDone = false;
        double bProgress = 0;
        var phases = new List<LoadPhase>();
        pipeline.PhaseChanged += phases.Add;
        // Registered in any order: the plan's order names them.
        pipeline.Add(new LoadStage("b", () => bDone, () => bProgress));
        pipeline.Add(new LoadStage("a", () => aDone));
        Assert.Equal("a", pipeline.Stages[0].Key, "the plan's order");
        pipeline.Update(0.016);
        Assert.Equal("Alpha", pipeline.Current?.Title, "the first stage not done is named");
        Assert.True(pipeline.Blocking && pipeline.ShowScreen && !pipeline.Playing, "loading blocks play and shows the screen");

        // "a" has no measure of its own: estimated by its weight's seconds, at most 95 %.
        clock.Now = 1.5;
        bProgress = 0.8;
        pipeline.Update(0.016);
        Assert.Near(0.5, pipeline.Stages[0].Progress, 1e-9, "half of a's seconds");
        Assert.Near((3 * 0.5 + 1 * 0.8) / 4, pipeline.Progress, 1e-9, "the bar weighs each stage's progress by its weight");
        bProgress = 0.3;
        pipeline.Update(0.016);
        Assert.Near(0.8, pipeline.Stages[1].Progress, 1e-9, "a stage's progress never goes back");
        clock.Now = 10;
        pipeline.Update(0.016);
        Assert.Near(LoadStage.MostEstimated, pipeline.Stages[0].Progress, 1e-9, "an estimate stops short of done");

        bDone = true;
        pipeline.Update(0.016);
        bDone = false;
        pipeline.Update(0.016);
        Assert.True(pipeline.Stages[1].Done, "done stays done for the load");
        Assert.Equal("Alpha", pipeline.Current?.Title, "still loading a");
        Assert.True(!pipeline.Playing, "one stage left: no play");
        aDone = true;
        pipeline.Update(0.016);
        Assert.True(pipeline.Playing && !pipeline.Blocking && !pipeline.ShowScreen, "every stage done: play is handed over");
        Assert.Near(1, pipeline.Progress, 1e-9, "the bar is full");
        Assert.Equal(1, phases.Count, "one phase change");
        Assert.True(pipeline.Timeline().Contains("played after 10.00 s", StringComparison.Ordinal), pipeline.Timeline());
    }

    [Test]
    public static void SteadyFramesWaitForTheOthers()
    {
        var clock = new Clock();
        var pipeline = new LoadingPipeline(Plan(
            "{ \"key\": \"a\", \"title\": \"A\", \"weight\": 1 }, { \"key\": \"steady\", \"title\": \"Arriving\", \"weight\": 1, \"seconds\": 0.5, \"frameMs\": 33 }"),
            () => clock.Now);
        bool aDone = false;
        pipeline.Add(SteadyFrames.Stage(pipeline.Plan));
        pipeline.Add(new LoadStage("a", () => aDone, () => 0));
        void Frames(int count, double seconds)
        {
            for (int i = 0; i < count; i++)
            {
                clock.Now += seconds;
                pipeline.Update(seconds);
            }
        }
        Frames(60, 1 / 60.0);
        Assert.Equal("A", pipeline.Current?.Title, "the steady frames wait for the others");
        Assert.Near(0, pipeline.Progress, 1e-9, "a second of smooth frames while a stage loads counts for nothing");
        aDone = true;
        Frames(20, 1 / 60.0);
        Frames(1, 0.12);
        Assert.True(!pipeline.Playing, "a slow frame starts the run again");
        Frames(29, 1 / 60.0);
        Assert.True(!pipeline.Playing, "not yet half a second of steady frames");
        Frames(2, 1 / 60.0);
        Assert.True(pipeline.Playing, "half a second of steady frames: play");
    }

    [Test]
    public static void SlowFramesCannotHoldAReadyWorldAtNinetySevenPercent()
    {
        var clock = new Clock();
        var pipeline = new LoadingPipeline(Plan(
            "{ \"key\": \"world\", \"title\": \"World\", \"weight\": 21.1 }, { \"key\": \"steady\", \"title\": \"Arriving\", \"weight\": 0.75, \"seconds\": 0.75, \"frameMs\": 33.4, \"mostSeconds\": 1 }"),
            () => clock.Now);
        bool worldReady = false;
        int timeouts = 0;
        double timedOutAfter = 0;
        pipeline.Add(new LoadStage("world", () => worldReady));
        pipeline.Add(SteadyFrames.Stage(pipeline.Plan, frames =>
        {
            timeouts++;
            timedOutAfter = frames.Elapsed;
        }));
        void SlowFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.Now += 0.125;
                pipeline.Update(0.125);
            }
        }

        SlowFrames(80);
        Assert.True(pipeline.Blocking, "the limit must never bypass an unfinished world stage");
        Assert.Equal(0, timeouts, "the steady wait has not started while the world loads");
        worldReady = true;
        SlowFrames(1);
        Assert.Equal("Arriving", pipeline.Current?.Title, "the steady stage begins only when the world is ready");
        SlowFrames(7);
        Assert.True(pipeline.Blocking, "slow frames still get the configured settling time");
        Assert.Near(97, Math.Round(pipeline.Progress * 100), 1e-9, "the reported stall's final percentage");
        SlowFrames(1);
        Assert.True(pipeline.Playing, "a ready world enters play even at eight frames per second");
        Assert.Near(1, pipeline.Progress, 1e-9, "the loading bar completes");
        Assert.Near(1, timedOutAfter, 1e-9, "the limit is measured after the world became ready");
        SlowFrames(20);
        Assert.Equal(1, timeouts, "the fallback diagnostic is emitted once");
    }

    [Test]
    public static void ResettingSteadyFramesClearsTheLongestWait()
    {
        var frames = new SteadyFrames(0.75, 33.4, 1);
        frames.Frame(1);
        Assert.True(frames.Done && frames.TimedOut, "a slow frame reaches the longest wait");
        frames.Reset();
        Assert.True(!frames.Done && !frames.TimedOut, "the next start gets its own settling period");
        Assert.Near(0, frames.Elapsed, 1e-9, "elapsed time resets");
        for (int i = 0; i < 48; i++)
        {
            frames.Frame(1 / 64.0);
        }
        Assert.True(frames.Done && !frames.TimedOut, "smooth frames finish normally before the time limit");
    }

    [Test]
    public static void AFarJumpRunsTheArrivalStagesAgain()
    {
        var clock = new Clock();
        var pipeline = new LoadingPipeline(Plan(
            "{ \"key\": \"server\", \"title\": \"S\", \"weight\": 1 }, { \"key\": \"land\", \"title\": \"Painting the land\", \"weight\": 1 }"),
            () => clock.Now);
        bool land = true;
        int begun = 0;
        pipeline.Add(new LoadStage("server", () => true));
        pipeline.Add(new LoadStage("land", () => land, scope: LoadScope.Arrival, begin: () => begun++));
        pipeline.Update(0.016);
        Assert.True(pipeline.Playing, "both done");
        pipeline.Follow(0, 0, 0);
        pipeline.Follow(60, 0, 0);
        Assert.True(pipeline.Playing, "walking (60 m in one frame is a hitch, not an arrival)");
        pipeline.Follow(1060, 0, 0, arrivals: false);
        pipeline.Follow(1061, 0, 0);
        Assert.True(pipeline.Playing, "a flight's kilometre and stepping out at its end are no arrival");
        pipeline.Follow(60, 0, 0);
        land = false;
        pipeline.Follow(400, 0, 300);
        Assert.Equal(LoadPhase.Arriving, pipeline.Phase, "a 450 m jump is an arrival");
        Assert.Equal(2, begun, "the arrival's stage began again");
        Assert.True(pipeline.Blocking && !pipeline.ShowScreen, "input waits, the screen waits for the grace");
        Assert.True(pipeline.Stages[0].Done, "the start's stages stay done");
        Assert.Equal("Painting the land", pipeline.Current?.Title, "the arrival names its own stage");
        clock.Now += 0.3;
        pipeline.Update(0.016);
        Assert.True(pipeline.ShowScreen, "past the grace the short screen shows");
        land = true;
        pipeline.Update(0.016);
        Assert.True(pipeline.Playing && pipeline.IsArrival, "the land is in: play again");
        Assert.True(pipeline.Timeline().StartsWith("arrival: land", StringComparison.Ordinal), pipeline.Timeline());
    }

    [Test]
    public static void AFailedStageStopsTheLoad()
    {
        var pipeline = new LoadingPipeline(Plan("{ \"key\": \"server\", \"title\": \"S\", \"weight\": 1 }"));
        bool exited = false;
        pipeline.Add(new LoadStage("server", () => false, failed: () => exited));
        pipeline.Update(0.016);
        Assert.True(pipeline.Blocking, "loading");
        exited = true;
        pipeline.Update(0.016);
        Assert.Equal(LoadPhase.Failed, pipeline.Phase, "failed");
        Assert.True(!pipeline.Blocking && !pipeline.ShowScreen, "the menu gets the screen and the mouse");
        Assert.Equal("server", pipeline.FailedStage?.Key, "names the stage");
    }

    [Test]
    public static void AQuietStreamSettles()
    {
        var stream = new QuietStream(0.5, 3);
        stream.Begin(10);
        Assert.True(!stream.Settled(10.4), "not quiet long enough");
        stream.Arrived(10.4);
        Assert.True(!stream.Settled(10.8), "something new restarts the quiet");
        Assert.True(stream.Settled(10.95), "half a second quiet");
        stream.Begin(20);
        for (double t = 20; t < 23; t += 0.1)
        {
            stream.Arrived(t);
        }
        Assert.True(stream.Settled(23.01), "a place that never goes quiet is let through after the longest wait");
    }

    [Test]
    public static void TheClientsStagesFinishOnAJoinAndATeleport()
    {
        using var rig = new LoopbackRig();
        GameClient client = rig.Join("Loada");
        var pipeline = new LoadingPipeline(LoadingPlan.Load(Data));
        pipeline.Add(client.Loading().LoadStages(pipeline.Plan));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void Run(Func<bool> until, string what)
        {
            while (!until())
            {
                Assert.True(watch.Elapsed.TotalSeconds < 20, $"{what}: {pipeline.Timeline()}");
                rig.Run(0.05, (_, _) => default);
                System.Numerics.Vector3 p = client.RenderPosition;
                if (client.HasCharacter)
                {
                    pipeline.Follow(p.X, p.Y, p.Z);
                }
                pipeline.Update(0.05);
            }
        }
        Run(() => pipeline.Playing, "the join");
        Fixtures.Report(pipeline.Timeline());
        Assert.True(pipeline.Stages.All(s => s.Done), "connected, placed and the world received");
        rig.Server.Execute("teleport Loada 400 300");
        Run(() => pipeline.Phase == LoadPhase.Arriving, "the teleport seen");
        Assert.True(!pipeline.Stages.First(s => s.Key == LoadingModule.WorldStage).Done, "the world streams again");
        Assert.True(pipeline.Stages.First(s => s.Key == LoadingModule.ConnectStage).Done, "still connected");
        Run(() => pipeline.Playing, "the arrival");
        Fixtures.Report(pipeline.Timeline());
    }
}
