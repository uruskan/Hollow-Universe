using System;

namespace Hollow.Client.Loading;

/// <summary>
/// The last stage of the game's start (GROUND.md §4.3.1): a run of frames, each under the frame budget, long enough to
/// trust that play will be smooth from its first frame. Any slower frame starts the run again. It waits for every other
/// stage: frames are only steady once nothing is streaming or compiling any more.
/// </summary>
public sealed class SteadyFrames
{
    public const string Key = "steady";

    private double _run;

    /// <param name="seconds">How long the run must last.</param>
    /// <param name="frameMs">The budget of one frame (milliseconds).</param>
    public SteadyFrames(double seconds, double frameMs)
    {
        Seconds = Math.Max(seconds, 0);
        FrameMs = frameMs;
    }

    public double Seconds { get; }

    public double FrameMs { get; }

    /// <summary>Seconds of steady frames so far.</summary>
    public double Run => _run;

    public bool Done => _run >= Seconds;

    /// <summary>Starts the run again.</summary>
    public void Reset() => _run = 0;

    /// <summary>A frame that took <paramref name="seconds"/>.</summary>
    public void Frame(double seconds) => _run = seconds * 1000 > FrameMs ? 0 : _run + seconds;

    /// <summary>The stage, with the run's length and the budget from the plan ("seconds", "frameMs").</summary>
    public static LoadStage Stage(LoadingPlan plan)
    {
        StagePlan p = plan.Stage(Key);
        var frames = new SteadyFrames(p.Number("seconds", 0.75), p.Number("frameMs", 33.4));
        return new LoadStage(Key, () => frames.Done, () => frames.Seconds > 0 ? frames.Run / frames.Seconds : 1,
            afterOthers: true, begin: frames.Reset, frame: frames.Frame);
    }
}
