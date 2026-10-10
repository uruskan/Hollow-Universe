using System;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>
/// Expected number of stars of each level within any distance of a point. Built by sampling the density model on
/// spherical shells around the point. It's approximate (to within a few tens of percent where the density changes
/// fast, such as across the thin disk). It's meant for budgets, for example how far around its focus the galaxy
/// map can afford to show each level (GALAXY.md §11). Generation doesn't use it, but it is deterministic anyway.
/// One instance is reused: <see cref="Compute"/> overwrites it. Not thread-safe.
/// </summary>
public sealed class StarCountProfile
{
    /// <summary>Spherical shells between <see cref="InnerRadius"/> and <see cref="OuterRadius"/>, geometrically spaced.</summary>
    public const int ShellCount = 40;

    public const double InnerRadius = 0.5;
    public const double OuterRadius = 8000;

    /// <summary>Density samples per shell, spread evenly over the sphere (a Fibonacci lattice).</summary>
    public const int SamplesPerShell = 64;

    private const double FourThirdsPi = 4.0 / 3.0 * DMath.Pi;

    private static readonly double[] Radii = BuildRadii();
    private static readonly double[] SampleRadii = BuildSampleRadii();
    private static readonly Vector3D[] Directions = BuildDirections();

    private readonly double[,] _cumulative = new double[StarClassTable.LevelCount, ShellCount + 1];
    private readonly double[] _outerDensity = new double[StarClassTable.LevelCount];

    /// <summary>The point the profile was last computed around.</summary>
    public Vector3D Centre { get; private set; }

    /// <summary>Samples the density around a point (ShellCount × SamplesPerShell evaluations, about a millisecond).</summary>
    public void Compute(DensityModel density, Vector3D centre)
    {
        Centre = centre;
        Span<double> components = stackalloc double[DensityModel.ComponentCount];
        Span<double> levels = stackalloc double[StarClassTable.LevelCount];
        Span<double> sum = stackalloc double[StarClassTable.LevelCount];

        // Inside the innermost radius the density is taken as uniform.
        LevelDensities(density, centre, components, levels);
        double ball = FourThirdsPi * Cube(Radii[0]);
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            _cumulative[level, 0] = levels[level] * ball;
        }

        for (int k = 1; k <= ShellCount; k++)
        {
            sum.Clear();
            double r = SampleRadii[k];
            int first = (k - 1) * SamplesPerShell;
            for (int i = 0; i < SamplesPerShell; i++)
            {
                LevelDensities(density, centre + Directions[first + i] * r, components, levels);
                for (int level = 0; level < StarClassTable.LevelCount; level++)
                {
                    sum[level] += levels[level];
                }
            }
            double shell = FourThirdsPi * (Cube(Radii[k]) - Cube(Radii[k - 1]));
            for (int level = 0; level < StarClassTable.LevelCount; level++)
            {
                double mean = sum[level] / SamplesPerShell;
                _cumulative[level, k] = _cumulative[level, k - 1] + mean * shell;
                if (k == ShellCount)
                {
                    _outerDensity[level] = mean;
                }
            }
        }
    }

    /// <summary>Expected number of stars of a level within a distance of the centre.</summary>
    public double CountWithin(int level, double radius)
    {
        if (radius <= 0)
        {
            return 0;
        }
        if (radius <= Radii[0])
        {
            return _cumulative[level, 0] * Cube(radius / Radii[0]);
        }
        if (radius >= Radii[ShellCount])
        {
            return _cumulative[level, ShellCount] + _outerDensity[level] * FourThirdsPi * (Cube(radius) - Cube(Radii[ShellCount]));
        }
        int k = 1;
        while (Radii[k] < radius)
        {
            k++;
        }
        double inner = Cube(Radii[k - 1]);
        double f = (Cube(radius) - inner) / (Cube(Radii[k]) - inner);
        return _cumulative[level, k - 1] + f * (_cumulative[level, k] - _cumulative[level, k - 1]);
    }

    /// <summary>
    /// The distance within which a level is expected to have <paramref name="count"/> stars;
    /// <see cref="double.PositiveInfinity"/> if the galaxy around the centre never holds that many.
    /// </summary>
    public double RadiusFor(int level, double count)
    {
        if (count <= 0)
        {
            return 0;
        }
        if (count <= _cumulative[level, 0])
        {
            return Radii[0] * DMath.Pow(count / _cumulative[level, 0], 1.0 / 3.0);
        }
        for (int k = 1; k <= ShellCount; k++)
        {
            double hi = _cumulative[level, k];
            if (hi >= count)
            {
                double lo = _cumulative[level, k - 1];
                double inner = Cube(Radii[k - 1]);
                double f = hi > lo ? (count - lo) / (hi - lo) : 1.0;
                return DMath.Pow(inner + f * (Cube(Radii[k]) - inner), 1.0 / 3.0);
            }
        }
        if (_outerDensity[level] <= 0)
        {
            return double.PositiveInfinity;
        }
        double extra = (count - _cumulative[level, ShellCount]) / (FourThirdsPi * _outerDensity[level]);
        return DMath.Pow(Cube(Radii[ShellCount]) + extra, 1.0 / 3.0);
    }

    // Density of each level's stars at a point: each component's density times the share of its population
    // that falls in the level (the same split the cell generator uses).
    private static void LevelDensities(DensityModel density, Vector3D position, Span<double> components, Span<double> levels)
    {
        density.Evaluate(position, components);
        levels.Clear();
        for (int c = 0; c < DensityModel.ComponentCount; c++)
        {
            if (components[c] <= 0)
            {
                continue;
            }
            Population population = DensityModel.PopulationOf((GalaxyComponent)c);
            for (int level = 0; level < StarClassTable.LevelCount; level++)
            {
                levels[level] += components[c] * StarClassTable.Share(population, level);
            }
        }
    }

    private static double Cube(double x) => x * x * x;

    private static double[] BuildRadii()
    {
        var radii = new double[ShellCount + 1];
        double ratio = DMath.Pow(OuterRadius / InnerRadius, 1.0 / ShellCount);
        radii[0] = InnerRadius;
        for (int k = 1; k <= ShellCount; k++)
        {
            radii[k] = radii[k - 1] * ratio;
        }
        radii[ShellCount] = OuterRadius;
        return radii;
    }

    // Each shell is sampled at the radius that splits its volume in half.
    private static double[] BuildSampleRadii()
    {
        var radii = new double[ShellCount + 1];
        for (int k = 1; k <= ShellCount; k++)
        {
            radii[k] = DMath.Pow(0.5 * (Cube(Radii[k - 1]) + Cube(Radii[k])), 1.0 / 3.0);
        }
        return radii;
    }

    // A Fibonacci lattice per shell, shifted in latitude and turned in longitude from shell to shell so that the
    // shells don't all sample the same directions.
    private static Vector3D[] BuildDirections()
    {
        const double goldenAngle = 2.399963229728653; // π(3 − √5)
        const double goldenFraction = 0.6180339887498949;
        var directions = new Vector3D[ShellCount * SamplesPerShell];
        for (int k = 0; k < ShellCount; k++)
        {
            double shift = k * goldenFraction % 1.0;
            for (int i = 0; i < SamplesPerShell; i++)
            {
                double y = 1.0 - 2.0 * (i + shift) / SamplesPerShell;
                double ring = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                double phi = i * goldenAngle + k;
                directions[k * SamplesPerShell + i] = new Vector3D(DMath.Cos(phi) * ring, y, DMath.Sin(phi) * ring);
            }
        }
        return directions;
    }
}
