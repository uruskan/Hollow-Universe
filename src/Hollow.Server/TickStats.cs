using System;

namespace Hollow.Server;

/// <summary>
/// How long ticks take (GROUND.md §5: ≤ 10 ms typical with 100 bots). Keeps the last few seconds for the console's
/// <c>status</c>, and totals since the last <see cref="ResetTotals"/> for load tests: average, the 99th percentile (from
/// a histogram of 0.1 ms steps) and the maximum.
/// </summary>
public sealed class TickStats
{
    private const int Window = 300;
    private const double BucketMs = 0.1;

    private readonly double[] _recent = new double[Window];
    private readonly long[] _histogram = new long[2000];
    private int _next;
    private int _filled;

    public long Ticks { get; private set; }

    public long Overruns { get; private set; }

    public double TotalAverageMs => Ticks == 0 ? 0 : TotalMs / Ticks;

    public double TotalMaxMs { get; private set; }

    public double TotalMs { get; private set; }

    public void Add(double milliseconds, double budgetMs)
    {
        _recent[_next] = milliseconds;
        _next = (_next + 1) % Window;
        _filled = Math.Min(Window, _filled + 1);
        Ticks++;
        TotalMs += milliseconds;
        TotalMaxMs = Math.Max(TotalMaxMs, milliseconds);
        _histogram[Math.Min(_histogram.Length - 1, (int)(milliseconds / BucketMs))]++;
        if (milliseconds > budgetMs)
        {
            Overruns++;
        }
    }

    public void ResetTotals()
    {
        Ticks = 0;
        Overruns = 0;
        TotalMs = 0;
        TotalMaxMs = 0;
        Array.Clear(_histogram);
    }

    /// <summary>Average and maximum over the last ten seconds.</summary>
    public (double Average, double Max) Recent
    {
        get
        {
            double sum = 0, max = 0;
            for (int i = 0; i < _filled; i++)
            {
                sum += _recent[i];
                max = Math.Max(max, _recent[i]);
            }
            return (_filled == 0 ? 0 : sum / _filled, max);
        }
    }

    /// <summary>A percentile of the tick time since <see cref="ResetTotals"/>, to 0.1 ms.</summary>
    public double TotalPercentileMs(double percentile)
    {
        long target = (long)Math.Ceiling(Ticks * percentile / 100);
        long seen = 0;
        for (int i = 0; i < _histogram.Length; i++)
        {
            seen += _histogram[i];
            if (seen >= target && seen > 0)
            {
                return (i + 1) * BucketMs;
            }
        }
        return TotalMaxMs;
    }
}
