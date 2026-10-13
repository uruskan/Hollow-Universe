using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Hollow.Client.Loading;

/// <summary>Where a game is in its loading.</summary>
public enum LoadPhase
{
    /// <summary>The game is starting: every stage takes part.</summary>
    Loading,

    /// <summary>The player arrived somewhere new (a respawn, a far teleport): the arrival's stages run again.</summary>
    Arriving,

    /// <summary>Every stage is done: input, the interface and the camera are the player's.</summary>
    Playing,

    /// <summary>A stage can't get ready (the server stopped, the connection was refused): the game shows the menu.</summary>
    Failed,
}

/// <summary>
/// The game's one loading pipeline (GROUND.md §4.3.1). Each part of the game registers the stage it owns
/// (<see cref="Add(LoadStage)"/>: the hosted server's start, the connection, the world's things, the land and the
/// forest around the player, the renderer's warm-up, a run of steady frames); the plan gives each its title, weight and
/// place in the order. Updated once a frame, it measures every stage taking part, keeps their progress from going back,
/// and hands play over the moment all of them are done. The loading screen draws <see cref="Progress"/> and
/// <see cref="Current"/>; the game asks <see cref="Blocking"/>. Neither knows which parts exist. A jump of the player
/// (<see cref="Follow"/>) beyond the plan's distance runs the arrival's stages again.
/// </summary>
public sealed class LoadingPipeline
{
    private readonly List<LoadStage> _stages = new();
    private readonly Func<double> _clock;
    private double _now;
    private double _began;
    private double _played = -1;
    private bool _othersDone;
    private (double X, double Y, double Z)? _place;

    /// <param name="plan">The stages' words, weights and order, and the screen's numbers.</param>
    /// <param name="clock">Seconds, for estimates and the timeline (a stopwatch when null; tests pass their own).</param>
    public LoadingPipeline(LoadingPlan plan, Func<double>? clock = null)
    {
        Plan = plan;
        if (clock == null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed.TotalSeconds;
        }
        _clock = clock;
        _now = _began = _clock();
    }

    public LoadingPlan Plan { get; }

    public LoadPhase Phase { get; private set; } = LoadPhase.Loading;

    /// <summary>Play is the player's: every stage of the last load is done.</summary>
    public bool Playing => Phase == LoadPhase.Playing;

    /// <summary>A load is running: input, the interface and the camera wait.</summary>
    public bool Blocking => Phase is LoadPhase.Loading or LoadPhase.Arriving;

    /// <summary>The screen shows: the game's load, or an arrival not done within the plan's grace.</summary>
    public bool ShowScreen => Phase == LoadPhase.Loading ||
        (Phase == LoadPhase.Arriving && _now - _began >= Plan.ArrivalGraceSeconds);

    /// <summary>The current (or last) load is an arrival: the screen's short version.</summary>
    public bool IsArrival => _arrival;

    private bool _arrival;

    /// <summary>Every registered stage, in the plan's order.</summary>
    public IReadOnlyList<LoadStage> Stages => _stages;

    /// <summary>Seconds since the current load (or arrival) began.</summary>
    public double Elapsed => _now - _began;

    /// <summary>Seconds the last load took to hand play over (negative while it runs).</summary>
    public double HandedOverAfter => _played >= 0 ? _played - _began : -1;

    /// <summary>The stage that failed, if one did.</summary>
    public LoadStage? FailedStage { get; private set; }

    /// <summary>Raised when the phase changes (the screen fades, the game takes the mouse).</summary>
    public event Action<LoadPhase>? PhaseChanged;

    /// <summary>The stages taking part in the current load (or the last one, once playing).</summary>
    public IEnumerable<LoadStage> Taking => _stages.Where(TakesPart);

    /// <summary>How far the current load is, 0–1: each stage's progress weighted by its weight.</summary>
    public double Progress
    {
        get
        {
            double total = 0, done = 0;
            foreach (LoadStage s in Taking)
            {
                total += s.Weight;
                done += s.Weight * s.Progress;
            }
            return total > 0 ? Math.Clamp(done / total, 0, 1) : 1;
        }
    }

    /// <summary>The stage the screen names: the first of the load not done, in the plan's order (those waiting for the others last).</summary>
    public LoadStage? Current =>
        Taking.Where(s => !s.Done).OrderBy(s => s.AfterOthers).ThenBy(s => s.Order).FirstOrDefault();

    /// <summary>Registers a stage (its owner calls this once it exists). It takes part in the current load at once.</summary>
    public void Add(LoadStage stage)
    {
        if (_stages.Contains(stage))
        {
            return;
        }
        stage.Plan(Plan.Stage(stage.Key));
        int at = _stages.FindIndex(s => s.Order > stage.Order);
        _stages.Insert(at < 0 ? _stages.Count : at, stage);
        if (Blocking && TakesPart(stage) && !stage.AfterOthers)
        {
            stage.Begin(_now);
        }
    }

    public void Add(IEnumerable<LoadStage> stages)
    {
        foreach (LoadStage stage in stages)
        {
            Add(stage);
        }
    }

    /// <summary>Withdraws a stage whose part is gone (or never needed in this game).</summary>
    public void Remove(LoadStage stage) => _stages.Remove(stage);

    /// <summary>Once a frame: measures the stages, hands play over when all are done, or fails.</summary>
    public void Update(double frameSeconds)
    {
        _now = _clock();
        if (!Blocking)
        {
            return;
        }
        bool others = true;
        foreach (LoadStage s in _stages)
        {
            if (!TakesPart(s) || s.AfterOthers)
            {
                continue;
            }
            s.Frame(frameSeconds);
            s.Measure(_now);
            others &= s.Done;
            if (s.Failed)
            {
                Fail(s);
                return;
            }
        }
        foreach (LoadStage s in _stages)
        {
            if (!TakesPart(s) || !s.AfterOthers)
            {
                continue;
            }
            if (!others)
            {
                continue;
            }
            if (!_othersDone)
            {
                // The others just finished: this frame was theirs; the waiting stages count from the next.
                s.Begin(_now);
                continue;
            }
            s.Frame(frameSeconds);
            s.Measure(_now);
            if (s.Failed)
            {
                Fail(s);
                return;
            }
        }
        _othersDone = others;
        if (others && Taking.All(s => s.Done))
        {
            _played = _now;
            SetPhase(LoadPhase.Playing);
        }
    }

    /// <summary>
    /// The player's place, every frame they are on foot (zone metres): moving farther than the plan's
    /// <see cref="LoadingPlan.ArrivalMetres"/> at once while playing is an arrival (<see cref="Arrive"/>).
    /// </summary>
    public void Follow(double x, double y, double z)
    {
        if (_place is { } last && Playing)
        {
            double dx = x - last.X, dy = y - last.Y, dz = z - last.Z;
            if (dx * dx + dy * dy + dz * dz > Plan.ArrivalMetres * Plan.ArrivalMetres)
            {
                Arrive();
            }
        }
        _place = (x, y, z);
    }

    /// <summary>The player arrived somewhere new: the arrival's stages run again (the screen shows past the grace).</summary>
    public void Arrive()
    {
        if (!Playing)
        {
            return;
        }
        _now = _clock();
        _began = _now;
        _played = -1;
        _arrival = true;
        _othersDone = false;
        SetPhase(LoadPhase.Arriving);
        foreach (LoadStage s in _stages.Where(TakesPart))
        {
            if (!s.AfterOthers)
            {
                s.Begin(_now);
            }
        }
    }

    /// <summary>The current (or last) load's stages: when each began and was done, in seconds from the load's start.</summary>
    public string Timeline()
    {
        var c = CultureInfo.InvariantCulture;
        var text = new StringBuilder(IsArrival ? "arrival: " : "start: ");
        foreach (LoadStage s in Taking)
        {
            text.Append(s.Key).Append(' ');
            text.Append(s.Started is { } a ? (a - _began).ToString("0.00", c) : "-").Append('–');
            text.Append(s.Finished is { } b ? (b - _began).ToString("0.00", c) : "…").Append(" s, ");
        }
        text.Append(_played >= 0 ? string.Create(c, $"played after {_played - _began:0.00} s") : Phase == LoadPhase.Failed ? $"failed at {FailedStage?.Key}" : "still loading");
        return text.ToString();
    }

    private bool TakesPart(LoadStage s) => !IsArrival || s.Scope == LoadScope.Arrival;

    private void Fail(LoadStage stage)
    {
        FailedStage = stage;
        SetPhase(LoadPhase.Failed);
    }

    private void SetPhase(LoadPhase phase)
    {
        if (Phase == phase)
        {
            return;
        }
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
