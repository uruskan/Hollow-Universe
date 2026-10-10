using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>Stream tags: every kind of generated object hashes its address with its own tag.</summary>
public static class Streams
{
    public const ulong Cell = 0x43454C4C; // "CELL"
    public const ulong Star = 0x53544152; // "STAR"
    public const ulong System = 0x53595354; // "SYST"
    public const ulong Suppression = 0x53555050; // "SUPP"
}

/// <summary>
/// Generates the procedural stars of one cell (GALAXY.md §7). A pure function of (seed, cell): thread-safe,
/// and the same on every machine. Deliberately independent of whether the real-star catalogue is loaded: the
/// region near Sol is always left to the catalogue, so a client without it can never disagree with the server.
/// </summary>
public sealed class CellGenerator
{
    /// <summary>Per level: within this distance of Sol the real-star catalogue is complete (GALAXY.md §8.3).</summary>
    public static readonly double[] CatalogueRadius = { 3000, 1500, 650, 350, 190, 90, 50, 30 };

    /// <summary>Procedural stars fade back in over this fraction of the radius beyond it.</summary>
    public const double CatalogueRampFraction = 0.25;

    /// <summary>Hard limit per cell, far above anything the density cap allows; protects memory.</summary>
    public const long MaxStarsPerCell = 4_000_000;

    private const int PositionTries = 16;

    private readonly ulong _seed;
    private readonly DensityModel _density;
    private readonly bool _leaveSolToCatalogue;

    public CellGenerator(ulong seed, DensityModel density)
        : this(seed, density, leaveSolToCatalogue: true)
    {
    }

    /// <summary>Test seam: generates the region near Sol too, to prove suppression changes nothing else.</summary>
    internal CellGenerator(ulong seed, DensityModel density, bool leaveSolToCatalogue)
    {
        _seed = seed;
        _density = density;
        _leaveSolToCatalogue = leaveSolToCatalogue;
    }

    /// <summary>Expected number of procedural systems in a cell at its level (before catalogue suppression).</summary>
    public double ExpectedCount(CellKey cell)
    {
        Span<double> lambda = stackalloc double[DensityModel.ComponentCount];
        Span<double> maxDensity = stackalloc double[DensityModel.ComponentCount];
        return Integrate(cell, lambda, maxDensity);
    }

    public GalaxyStar[] Generate(CellKey cell)
    {
        Span<double> lambda = stackalloc double[DensityModel.ComponentCount];
        Span<double> maxDensity = stackalloc double[DensityModel.ComponentCount];
        Span<double> components = stackalloc double[DensityModel.ComponentCount];

        double total = Integrate(cell, lambda, maxDensity);
        if (total <= 0)
        {
            return Array.Empty<GalaxyStar>();
        }

        var cellRng = new Rng(Hash64.Of(_seed, Streams.Cell, cell.Packed));
        long count = cellRng.Poisson(total);
        long capacity = (1L << cell.StarIndexBits) - 1;
        count = Math.Min(count, Math.Min(capacity, MaxStarsPerCell));
        if (count == 0)
        {
            return Array.Empty<GalaxyStar>();
        }

        int level = cell.Level;
        double size = cell.Size;
        Vector3D min = cell.Min;
        double suppressRadius = CatalogueRadius[level];
        var stars = new List<GalaxyStar>((int)Math.Min(count, 1 << 16));

        for (long i = 0; i < count; i++)
        {
            SystemAddress address = SystemAddress.Procedural(cell, i);
            var rng = new Rng(Hash64.Of(_seed, Streams.Star, address.Value));

            int c = rng.PickWeighted(lambda);
            if (c < 0)
            {
                continue;
            }
            var component = (GalaxyComponent)c;
            int entry = StarClassTable.Pick(DensityModel.PopulationOf(component), level, ref rng);
            if (entry < 0)
            {
                continue;
            }

            // Position: rejection sampling against this component's density inside the cell.
            Vector3D position = min;
            double bound = maxDensity[c] * 1.3;
            for (int t = 0; t < PositionTries; t++)
            {
                position = min + new Vector3D(rng.NextDouble() * size, rng.NextDouble() * size, rng.NextDouble() * size);
                _density.Evaluate(position, components);
                if (rng.NextDouble() * bound <= components[c])
                {
                    break;
                }
            }

            // Near Sol the real catalogue already has these stars (GALAXY.md §8.3). The keep roll has its own
            // stream, so a star that survives is identical to the one an unsuppressed run would make.
            double d = position.Length;
            if (_leaveSolToCatalogue && d < suppressRadius * (1.0 + CatalogueRampFraction))
            {
                if (d < suppressRadius)
                {
                    continue;
                }
                double keep = (d - suppressRadius) / (CatalogueRampFraction * suppressRadius);
                var keepRng = new Rng(Hash64.Of(_seed, Streams.Suppression, address.Value));
                if (keepRng.NextDouble() >= keep)
                {
                    continue;
                }
            }

            (StarClass starClass, StarPhysics physics) = StarClassTable.Roll(entry, ref rng);
            (double mean, double sigma) = DensityModel.MetallicityOf(component);
            float metallicity = (float)StellarTables.Clamp(rng.Gaussian(mean, sigma), -3.0, 0.6);
            stars.Add(new GalaxyStar(address, position, starClass, physics, metallicity, component, level));
        }
        return stars.ToArray();
    }

    // Integrates the density over the cell; fills the expected star count per component at this level and the
    // largest sampled density per component. Returns the total expected count.
    private double Integrate(CellKey cell, Span<double> lambda, Span<double> maxDensity)
    {
        Span<double> components = stackalloc double[DensityModel.ComponentCount];
        int level = cell.Level;
        int nx = 4;
        int ny = level <= 2 ? 8 : 4;
        int nz = 4;
        double size = cell.Size;
        Vector3D min = cell.Min;
        double volumePerSample = size * size * size / (nx * ny * nz);
        lambda.Clear();
        maxDensity.Clear();

        for (int iz = 0; iz < nz; iz++)
        {
            for (int iy = 0; iy < ny; iy++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    var p = new Vector3D(
                        min.X + (ix + 0.5) / nx * size,
                        min.Y + (iy + 0.5) / ny * size,
                        min.Z + (iz + 0.5) / nz * size);
                    _density.Evaluate(p, components);
                    for (int c = 0; c < DensityModel.ComponentCount; c++)
                    {
                        lambda[c] += components[c];
                        if (components[c] > maxDensity[c])
                        {
                            maxDensity[c] = components[c];
                        }
                    }
                }
            }
        }

        double total = 0;
        for (int c = 0; c < DensityModel.ComponentCount; c++)
        {
            Population population = DensityModel.PopulationOf((GalaxyComponent)c);
            lambda[c] *= volumePerSample * StarClassTable.Share(population, level);
            total += lambda[c];
        }
        return total;
    }
}
