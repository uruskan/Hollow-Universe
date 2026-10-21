using System;

namespace Hollow.Client.Loading;

/// <summary>
/// The last stage of the game's start (GROUND.md §4.3.1): a run of frames, each under the frame budget, long enough to
/// let initial rendering settle. Any slower frame starts the run again. It waits for every other stage, then stops
/// waiting after a bounded time: a fully loaded world must remain playable even below the target frame rate.
/// </summary>
public sealed class SteadyFrames
{
    public const string Key = "steady";

    private double _run;
    private double _elapsed;

    /// <param name="seconds">How long the run must last.</param>
    /// <param name="frameMs">The budget of one frame (milliseconds).</param>
    /// <param name="mostSeconds">The longest wait after the other stages are ready.</param>
    public SteadyFrames(double seconds, double frameMs, double mostSeconds = 5)
    {
        Seconds = Math.Max(seconds, 0);
        FrameMs = frameMs;
        MostSeconds = double.IsFinite(mostSeconds) && mostSeconds > 0 ? mostSeconds : 5;
    }

    public double Seconds { get; }

    public double FrameMs { get; }

    public double MostSeconds { get; }

    /// <summary>Seconds spent waiting since the other stages became ready.</summary>
    public double Elapsed => _elapsed;

    /// <summary>Seconds of steady frames so far.</summary>
    public double Run => _run;

    /// <summary>The world was ready but the target frame rate did not settle within the longest wait.</summary>
    public bool TimedOut => _elapsed >= MostSeconds && _run < Seconds;

    public bool Done => _run >= Seconds || TimedOut;

    /// <summary>Starts the run again.</summary>
    public void Reset()
    {
        _run = 0;
        _elapsed = 0;
    }

    /// <summary>A frame that took <paramref name="seconds"/>.</summary>
    public void Frame(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return;
        }
        _elapsed += seconds;
        _run = seconds * 1000 > FrameMs ? 0 : _run + seconds;
    }

    /// <summary>The stage, with the run, frame budget and longest wait from the plan ("seconds", "frameMs", "mostSeconds").</summary>
    /// <param name="plan">The loading plan.</param>
    /// <param name="onTimeout">Optional diagnostic, called once when the stage finishes through its time limit.</param>
    public static LoadStage Stage(LoadingPlan plan, Action<SteadyFrames>? onTimeout = null)
    {
        StagePlan p = plan.Stage(Key);
        var frames = new SteadyFrames(p.Number("seconds", 0.75), p.Number("frameMs", 33.4), p.Number("mostSeconds", 5));
        return new LoadStage(Key, () =>
            {
                if (frames.TimedOut)
                {
                    onTimeout?.Invoke(frames);
                }
                return frames.Done;
            }, () => frames.Seconds > 0 ? frames.Run / frames.Seconds : 1,
            afterOthers: true, begin: frames.Reset, frame: frames.Frame);
    }
}
