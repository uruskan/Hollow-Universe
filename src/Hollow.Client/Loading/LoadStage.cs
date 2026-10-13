using System;

namespace Hollow.Client.Loading;

/// <summary>When a stage takes part in a load.</summary>
public enum LoadScope
{
    /// <summary>Once, when the game starts: the server, the connection, the renderer's warm-up, the first steady frames.</summary>
    Start,

    /// <summary>
    /// Also every time the player arrives somewhere new (a respawn, a far teleport): what is streamed around the player.
    /// </summary>
    Arrival,
}

/// <summary>
/// One part of the game that must be ready before play (GROUND.md §4.3.1), as the part that owns it registers it: a key
/// (its title and weight are the plan's), when it takes part (<see cref="LoadScope"/>), and its own measure: whether it
/// is done, its progress (none: estimated from the seconds its weight says it takes), and whether it failed (the game
/// can't start: it goes back to the menu). A stage that waits for the others (<see cref="AfterOthers"/>) begins only
/// once every other stage of the load is done: the steady frames. The pipeline keeps what the measure said: done stays
/// done for the rest of the load, and progress never goes back.
/// </summary>
public sealed class LoadStage
{
    /// <summary>Progress shown at most until the stage says it is done.</summary>
    public const double MostBeforeDone = 0.99;

    /// <summary>A time estimate stops here: a stage that takes longer than its weight waits at the end of its share.</summary>
    public const double MostEstimated = 0.95;

    private readonly Func<bool> _done;
    private readonly Func<double>? _progress;
    private readonly Func<bool>? _failed;
    private readonly Action? _begin;
    private readonly Action<double>? _frame;

    /// <param name="key">The stage's key in the plan (loading.json).</param>
    /// <param name="done">Whether the part is ready.</param>
    /// <param name="progress">How far along it is, 0–1; null: estimated from the time since the stage began and its weight.</param>
    /// <param name="failed">Whether it can't get ready (the game returns to the menu).</param>
    /// <param name="scope">When it takes part.</param>
    /// <param name="afterOthers">It begins only once every other stage of the load is done.</param>
    /// <param name="begin">Called when a load it takes part in begins (and, for <paramref name="afterOthers"/>, when the others are done).</param>
    /// <param name="frame">Called every frame of a load it takes part in (from its beginning), with the frame's seconds.</param>
    public LoadStage(string key, Func<bool> done, Func<double>? progress = null, Func<bool>? failed = null, LoadScope scope = LoadScope.Start,
        bool afterOthers = false, Action? begin = null, Action<double>? frame = null)
    {
        Key = key;
        _done = done;
        _progress = progress;
        _failed = failed;
        Scope = scope;
        AfterOthers = afterOthers;
        _begin = begin;
        _frame = frame;
    }

    public string Key { get; }

    public LoadScope Scope { get; }

    public bool AfterOthers { get; }

    /// <summary>The words on the screen while this stage is the current one (the plan's).</summary>
    public string Title { get; private set; } = "";

    /// <summary>The seconds it takes on the reference PC: its share of the bar (the plan's).</summary>
    public double Weight { get; private set; } = 1;

    /// <summary>Its place in the plan's order (the screen names the first stage not done).</summary>
    public int Order { get; private set; }

    /// <summary>Done in the current load (kept until the next load it takes part in begins).</summary>
    public bool Done { get; private set; }

    /// <summary>It can't get ready.</summary>
    public bool Failed { get; private set; }

    /// <summary>How far along it is, 0–1 (1 once done; never lower than it was in this load).</summary>
    public double Progress { get; private set; }

    /// <summary>When it began in the current load (seconds on the pipeline's clock), if it has.</summary>
    public double? Started { get; private set; }

    /// <summary>When it was done in the current load, if it is.</summary>
    public double? Finished { get; private set; }

    internal void Plan(StagePlan plan)
    {
        Title = plan.Title;
        Weight = plan.Weight;
        Order = plan.Order;
    }

    internal void Begin(double now)
    {
        Done = false;
        Failed = false;
        Progress = 0;
        Started = now;
        Finished = null;
        _begin?.Invoke();
    }

    internal void Frame(double seconds) => _frame?.Invoke(seconds);

    internal void Measure(double now)
    {
        if (Done || Started == null)
        {
            return;
        }
        if (_done())
        {
            Done = true;
            Progress = 1;
            Finished = now;
            return;
        }
        if (_failed?.Invoke() == true)
        {
            Failed = true;
            return;
        }
        double measured = _progress != null ? _progress() : Math.Min(MostEstimated, (now - Started.Value) / Math.Max(Weight, 1e-3));
        Progress = Math.Max(Progress, Math.Clamp(double.IsFinite(measured) ? measured : 0, 0, MostBeforeDone));
    }
}
