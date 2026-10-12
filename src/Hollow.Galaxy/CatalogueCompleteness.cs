using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>
/// How much of the real sky the star catalogue holds, per level and distance from Sol (GALAXY.md §8.3): the
/// fraction <c>C</c> of a level's stars at that distance that come from the catalogue. Procedural stars there are
/// kept with probability <c>1 − C</c>, so real and generated stars add up to the density model with no doubling
/// and no gap; the catalogue builder keeps exactly the real stars where <c>C &gt; 0</c>.
/// <para>
/// Frozen in code, so generation never depends on whether the catalogue file loaded (ARCHITECTURE.md §8.6). The
/// form has a complete core that ends near <c>d₀</c> (the catalogue of nearby stars) and a bulk share <c>p</c> that
/// falls near <c>d₅₀</c> (the survey's magnitude limit):
/// <c>c = (1 − p) / (1 + (d / d₀)⁸) + p / (1 + (d / d₅₀)^k)</c>, fitted per level to real ÷ model counts in shells
/// around Sol (tools/Hollow.GeneratorReport shells), then stretched so it is exactly 1 where c ≥ 0.99 and exactly 0
/// where c ≤ 0.01 or beyond the catalogue's import limit.
/// </para>
/// </summary>
public static class CatalogueCompleteness
{
    // Per level 0–7, fitted to HYG 4.1 after the class table was calibrated (GALAXY.md §8.3).
    private static readonly double[] Bulk = { 0.58, 0.68, 0.72, 0.48, 0.40, 0.42, 0.56, 0.54 };
    private static readonly double[] CoreEdge = { 749.9, 749.9, 501.2, 421.7, 298.5, 177.8, 18.8, 33.5 };
    private static readonly double[] HalfDistance = { 2851.0, 1640.6, 955.0, 732.8, 473.2, 288.4, 146.2, 73.3 };
    private static readonly double[] Steepness = { 2.7, 3.4, 4.7, 6.6, 6.7, 6.4, 6.0, 9.2 };

    /// <summary>Per level: the catalogue's import limit (ly); it holds no stars beyond.</summary>
    private static readonly double[] ImportLimit = { 3000, 3000, 1500, 1500, 1500, 1500, 1500, 1500 };

    private const double Margin = 0.01;

    private static readonly double[] Full = new double[StarClassTable.LevelCount];
    private static readonly double[] Zero = new double[StarClassTable.LevelCount];

    static CatalogueCompleteness()
    {
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            // The raw curve falls monotonically, so bisection finds where it crosses 1 − Margin and Margin.
            Full[level] = Crossing(level, 1 - Margin);
            double zero = Crossing(level, Margin);
            Zero[level] = zero < ImportLimit[level] ? zero : ImportLimit[level];
        }
    }

    /// <summary>The fraction of a level's stars at this distance from Sol (ly) that the catalogue holds, 0–1.</summary>
    public static double Of(int level, double distance)
    {
        if (!(distance < Zero[level]))
        {
            return 0;
        }
        if (distance <= Full[level])
        {
            return 1;
        }
        double stretched = (Raw(level, distance) - Margin) / (1 - 2 * Margin);
        return stretched <= 0 ? 0 : stretched >= 1 ? 1 : stretched;
    }

    private static double Raw(int level, double distance)
    {
        double core = distance / CoreEdge[level];
        double core2 = core * core;
        double core8 = core2 * core2 * (core2 * core2);
        return (1 - Bulk[level]) / (1 + core8) + Bulk[level] / (1 + DMath.Pow(distance / HalfDistance[level], Steepness[level]));
    }

    private static double Crossing(int level, double value)
    {
        double lo = 0;
        double hi = 1e6;
        for (int i = 0; i < 80; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Raw(level, mid) > value)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    /// <summary>Within this distance (ly) the catalogue holds every star of the level: no procedural ones.</summary>
    public static double CompleteWithin(int level) => Full[level];

    /// <summary>Beyond this distance (ly) the catalogue holds none of the level: procedural stars only.</summary>
    public static double EmptyBeyond(int level) => Zero[level];
}
