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
    public const ulong CatalogueAge = 0x43414745; // "CAGE": age estimates of real stars

    // Stage seeds: the universe seed mixed with a stage's generator version (GeneratorVersion).
    public const ulong GalaxyStage = 0x47414C56; // "GALV"
    public const ulong SystemStage = 0x53595356; // "SYSV"
    public const ulong SurfaceStage = 0x53555256; // "SURV"
}

/// <summary>
/// Generates the procedural stars of one cell (GALAXY.md §7). A pure function of (seed, cell): thread-safe,
/// and the same on every machine. Deliberately independent of whether the real-star catalogue is loaded: the
/// region near Sol is always left to the catalogue, so a client without it can never disagree with the server.
/// </summary>
public sealed class CellGenerator
{
    /// <summary>Hard limit per cell, far above anything the density cap allows; protects memory.</summary>
    public const long MaxStarsPerCell = 4_000_000;

    // The density is sampled on a grid of NX × NY × NZ points per cell (NY doubled for the tall cells of levels
    // 0–2). Each point stands for a sub-cell: the grid integrates the density, and a star's position is drawn by
    // first choosing a sub-cell by its sampled density, then rejection sampling inside it.
    private const int GridX = 4;
    private const int GridZ = 4;
    private const int MaxGridY = 8;
    private const int MaxSubCells = GridX * MaxGridY * GridZ;
    private const int PositionTries = 24;

    // Rejection bound inside a sub-cell: this many times the largest sample of the sub-cell and its neighbours.
    private const double BoundFactor = 1.3;

    private readonly ulong _seed;
    private readonly DensityModel _density;
    private readonly bool _leaveSolToCatalogue;

    /// <summary>A cell generator for a universe seed, at the current galaxy-stage version.</summary>
    public CellGenerator(ulong seed, DensityModel density)
        : this(seed, density, leaveSolToCatalogue: true, GeneratorVersion.Galaxy)
    {
    }

    /// <summary>
    /// Test seam: <paramref name="leaveSolToCatalogue"/> = false generates the region near Sol too, to prove
    /// suppression changes nothing else; <paramref name="version"/> stands in for another galaxy-stage version.
    /// </summary>
    internal CellGenerator(ulong seed, DensityModel density, bool leaveSolToCatalogue, int version = GeneratorVersion.Galaxy)
    {
        _seed = GeneratorVersion.GalaxySeed(seed, version);
        _density = density;
        _leaveSolToCatalogue = leaveSolToCatalogue;
    }

    /// <summary>Expected number of procedural systems in a cell at its level (before catalogue suppression).</summary>
    public double ExpectedCount(CellKey cell)
    {
        Span<double> lambda = stackalloc double[DensityModel.ComponentCount];
        Span<double> samples = stackalloc double[MaxSubCells * DensityModel.ComponentCount];
        return Integrate(cell, lambda, samples);
    }

    public GalaxyStar[] Generate(CellKey cell)
    {
        const int components = DensityModel.ComponentCount;
        Span<double> lambda = stackalloc double[components];
        Span<double> samples = stackalloc double[MaxSubCells * components];
        Span<double> densities = stackalloc double[components];

        double total = Integrate(cell, lambda, samples);
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
        int gridY = GridY(level);
        int subCells = GridX * gridY * GridZ;
        Vector3D min = cell.Min;
        var subSize = new Vector3D(cell.Size / GridX, cell.Size / gridY, cell.Size / GridZ);
        double catalogueReach = CatalogueCompleteness.EmptyBeyond(level);
        var stars = new List<GalaxyStar>((int)Math.Min(count, 1 << 16));

        // Per component, the running sum of its samples over the sub-cells: choosing a sub-cell is a binary search.
        Span<double> cumulative = stackalloc double[MaxSubCells * components];
        for (int c = 0; c < components; c++)
        {
            double sum = 0;
            for (int k = 0; k < subCells; k++)
            {
                sum += samples[k * components + c];
                cumulative[c * MaxSubCells + k] = sum;
            }
        }

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

            // Position: a sub-cell chosen by this component's sampled density, then rejection sampling inside it
            // against the density itself. Every candidate lies in a sub-cell where the component has stars, so even
            // a star that runs out of tries can't land where the density says there are none.
            ReadOnlySpan<double> sums = cumulative.Slice(c * MaxSubCells, subCells);
            int s = PickSubCell(sums, rng.NextDouble() * sums[subCells - 1]);
            int ix = s % GridX;
            int iy = s / GridX % gridY;
            int iz = s / (GridX * gridY);
            double bound = BoundFactor * NeighbourhoodMax(samples, c, ix, iy, iz, gridY);
            Vector3D corner = min + new Vector3D(ix * subSize.X, iy * subSize.Y, iz * subSize.Z);
            Vector3D position = corner;
            for (int t = 0; t < PositionTries; t++)
            {
                position = corner + new Vector3D(rng.NextDouble() * subSize.X, rng.NextDouble() * subSize.Y, rng.NextDouble() * subSize.Z);
                _density.Evaluate(position, densities);
                if (rng.NextDouble() * bound <= densities[c])
                {
                    break;
                }
            }

            // Near Sol the real catalogue holds a share of this level's stars (GALAXY.md §8.3); a procedural star is
            // left out with that probability. The roll has its own stream, so a star that survives is identical to
            // the one an unsuppressed run would make.
            double d = position.Length;
            if (_leaveSolToCatalogue && d < catalogueReach)
            {
                double catalogued = CatalogueCompleteness.Of(level, d);
                if (catalogued >= 1)
                {
                    continue;
                }
                var keepRng = new Rng(Hash64.Of(_seed, Streams.Suppression, address.Value));
                if (keepRng.NextDouble() < catalogued)
                {
                    continue;
                }
            }

            (StarClass starClass, StarPhysics physics, double age) = StarClassTable.Roll(entry, component, ref rng);
            (double mean, double sigma) = DensityModel.MetallicityOf(component);
            float metallicity = (float)StellarTables.Clamp(rng.Gaussian(mean, sigma), -3.0, 0.6);
            stars.Add(new GalaxyStar(address, position, starClass, physics, metallicity, component, level, (float)age));
        }
        return stars.ToArray();
    }

    private static int GridY(int level) => level <= 2 ? MaxGridY : 4;

    // The first sub-cell whose running sum exceeds the target (sums never decrease, and target < the last sum), so
    // the chosen sub-cell always has a positive sample.
    private static int PickSubCell(ReadOnlySpan<double> sums, double target)
    {
        int lo = 0;
        int hi = sums.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sums[mid] > target)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return lo;
    }

    // The largest sampled density of a component in a sub-cell and its 26 neighbours: a local bound on the density,
    // since a peak between sample points shows up in the samples around it.
    private static double NeighbourhoodMax(ReadOnlySpan<double> samples, int c, int ix, int iy, int iz, int gridY)
    {
        double max = 0;
        for (int z = Math.Max(0, iz - 1); z <= Math.Min(GridZ - 1, iz + 1); z++)
        {
            for (int y = Math.Max(0, iy - 1); y <= Math.Min(gridY - 1, iy + 1); y++)
            {
                for (int x = Math.Max(0, ix - 1); x <= Math.Min(GridX - 1, ix + 1); x++)
                {
                    double v = samples[((z * gridY + y) * GridX + x) * DensityModel.ComponentCount + c];
                    if (v > max)
                    {
                        max = v;
                    }
                }
            }
        }
        return max;
    }

    // Integrates the density over the cell: fills the expected star count per component at this level, and the
    // density of each component at each grid point (sub-cell k = x + GridX·(y + gridY·z)). Returns the total.
    private double Integrate(CellKey cell, Span<double> lambda, Span<double> samples)
    {
        const int components = DensityModel.ComponentCount;
        Span<double> densities = stackalloc double[components];
        int level = cell.Level;
        int nx = GridX;
        int ny = GridY(level);
        int nz = GridZ;
        double size = cell.Size;
        Vector3D min = cell.Min;
        double volumePerSample = size * size * size / (nx * ny * nz);
        lambda.Clear();

        int k = 0;
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
                    _density.Evaluate(p, densities);
                    for (int c = 0; c < components; c++)
                    {
                        lambda[c] += densities[c];
                        samples[k * components + c] = densities[c];
                    }
                    k++;
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
