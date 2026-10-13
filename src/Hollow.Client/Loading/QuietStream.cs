using System;

namespace Hollow.Client.Loading;

/// <summary>
/// A stream that has gone quiet (GROUND.md §4.3.1): the server sends what is around a player that arrives as fast as its
/// budgets allow, a part every snapshot while there is a backlog; once nothing new has come for a while, the backlog is
/// in. A busy place where things never stop changing is let through after a longest wait.
/// </summary>
public sealed class QuietStream
{
    private double _began;
    private double _last;

    /// <param name="quietSeconds">Nothing new for this long: the backlog is in.</param>
    /// <param name="mostSeconds">Waited this long since the stream began: in, whatever still comes.</param>
    public QuietStream(double quietSeconds, double mostSeconds)
    {
        QuietSeconds = Math.Max(quietSeconds, 0);
        MostSeconds = Math.Max(mostSeconds, QuietSeconds);
    }

    public double QuietSeconds { get; }

    public double MostSeconds { get; }

    /// <summary>The stream begins (the player arrived): the quiet is counted from now.</summary>
    public void Begin(double now) => _began = _last = now;

    /// <summary>Something new arrived.</summary>
    public void Arrived(double now) => _last = Math.Max(_last, now);

    public bool Settled(double now) => now - _last >= QuietSeconds || now - _began >= MostSeconds;

    /// <summary>The quiet so far against the quiet needed (0–1).</summary>
    public double Progress(double now) => QuietSeconds > 0 ? Math.Clamp((now - _last) / QuietSeconds, 0, 1) : 1;
}
