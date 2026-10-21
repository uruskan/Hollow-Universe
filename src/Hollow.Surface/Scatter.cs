using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// One natural object placed by the scatter (GROUND.md §4.6). Its identity is its tile plus <see cref="Index"/>
/// (ARCHITECTURE.md §7): the index is fixed by the kind and the candidate cell, so it never shifts when anything
/// else changes.
/// </summary>
public readonly struct ScatterItem
{
    public ScatterItem(int index, ScatterKind kind, byte variant, Vector3D direction, float height, float yaw, float scale)
    {
        Index = index;
        Kind = kind;
        Variant = variant;
        Direction = direction;
        Height = height;
        Yaw = yaw;
        Scale = scale;
    }

    /// <summary>Kind in the top byte, candidate cell below: unique and stable within the tile.</summary>
    public int Index { get; }

    public ScatterKind Kind { get; }

    /// <summary>Which model of the kind, 0–255 (the client takes it modulo the models it has).</summary>
    public byte Variant { get; }

    /// <summary>Unit direction from the body's centre (body frame).</summary>
    public Vector3D Direction { get; }

    /// <summary>Ground height there, metres above the mean radius.</summary>
    public float Height { get; }

    /// <summary>Rotation about the local up, radians.</summary>
    public float Yaw { get; }

    /// <summary>Size relative to the model.</summary>
    public float Scale { get; }

    /// <summary>Body-frame position of the object's foot.</summary>
    public Vector3D Position(double radius) => Direction * (radius + Height);
}

/// <summary>
/// What the scatter reads from a surface (GROUND.md §4.6): its ground and fields at a point, and the densities of
/// everything it places there. The real planet (<see cref="PlanetSurface"/>) and the walking-scale ground
/// (WalkingWorld) both feed the one <see cref="Scatter"/>.
/// </summary>
public interface IScatterField
{
    /// <summary>Mean radius in metres.</summary>
    double Radius { get; }

    /// <summary>The seed every scatter stream of this surface derives from.</summary>
    ulong ScatterSeed { get; }

    /// <summary>The level of this surface's scatter tiles and their candidate cells.</summary>
    ScatterPlan ScatterPlan { get; }

    /// <summary>The ground and the fields it was built from, at a unit direction.</summary>
    SurfaceSample Sample(Vector3D direction);

    /// <summary>Height of the ground above the mean radius at a unit direction, metres.</summary>
    double HeightAt(Vector3D direction);

    /// <summary>Footprints reserved by world structures, checked at the actual candidate after density interpolation.</summary>
    bool AllowsScatter(Vector3D direction) => true;

    /// <summary>
    /// Densities of everything the scatter places at a point, from its sample and the ground's slope (the sine of its
    /// tilt).
    /// </summary>
    ScatterDensity Densities(Vector3D direction, in SurfaceSample sample, double slope);
}

/// <summary>Densities at a point for the scatter: plants and rocks per square metre, the tree kinds' mix, outcrops.</summary>
public readonly struct ScatterDensity
{
    public ScatterDensity(in VegetationSample vegetation, ReadOnlySpan<double> treeKinds, double outcrops, double mountain)
    {
        Vegetation = vegetation;
        Fir = treeKinds[0];
        Pine = treeKinds[1];
        Birch = treeKinds[2];
        Broadleaf = treeKinds[3];
        Outcrops = outcrops;
        Mountain = mountain;
    }

    public VegetationSample Vegetation { get; }

    /// <summary>Weights of the four tree kinds (any scale; only their ratios count).</summary>
    public double Fir { get; }
    public double Pine { get; }
    public double Birch { get; }
    public double Broadleaf { get; }

    /// <summary>Stone and ore outcrops per square metre.</summary>
    public double Outcrops { get; }

    /// <summary>How mountainous, 0 to about 1 (more ore in the mountains).</summary>
    public double Mountain { get; }
}

/// <summary>
/// The level of a surface's scatter tiles and the candidate cells per tile edge of each kind (GROUND.md §4.6). The real
/// planet keeps its level-16 plan (<see cref="Planet"/>); a small world takes the level whose tiles are about a size
/// and spaces its candidates in metres, so densities per square metre stay the same (<see cref="BySize"/>,
/// docs/WHOLE_WORLD.md §3.2).
/// </summary>
public readonly record struct ScatterPlan(int Level, int TreeCells, int BushCells, int FernCells, int RockCells, int BerryCells, int OutcropCells)
{
    /// <summary>Candidate spacing of each kind in metres (the planet's plan on the first world: 154 m tiles).</summary>
    public const double TreeSpacing = 3.5, BushSpacing = 2.4, FernSpacing = 1.6, RockSpacing = 3.2, BerrySpacing = 6.4, OutcropSpacing = 12.8;

    /// <summary>The real planet's plan: level 16 (about 154 m on the first world), 44 tree cells a tile edge.</summary>
    public static ScatterPlan Planet => new(Scatter.Level, 44, 64, 96, 48, 24, 12);

    /// <summary>
    /// The plan for a sphere of a radius: the level whose tiles are nearest <paramref name="tileMetres"/>, and candidate
    /// cells spaced as on the planet (+ − × ÷ and floor only, so every machine agrees).
    /// </summary>
    public static ScatterPlan BySize(double radius, double tileMetres)
    {
        int level = TileKey.LevelFor(radius, tileMetres);
        double edge = TileKey.EdgeLength(level, radius);
        int Cells(double spacing) => Math.Max(1, (int)Math.Floor(edge / spacing + 0.5));
        return new ScatterPlan(level, Cells(TreeSpacing), Cells(BushSpacing), Cells(FernSpacing), Cells(RockSpacing),
            Cells(BerrySpacing), Cells(OutcropSpacing));
    }

    /// <summary>Candidate cells per tile edge of a kind.</summary>
    public int CellsFor(ScatterKind kind) => kind switch
    {
        ScatterKind.Fir => TreeCells,
        ScatterKind.Bush => BushCells,
        ScatterKind.Fern => FernCells,
        ScatterKind.BerryRed or ScatterKind.BerryBlue => BerryCells,
        ScatterKind.StoneOutcrop or ScatterKind.HighGradeOutcrop => OutcropCells,
        _ => RockCells,
    };
}

/// <summary>
/// Explicit bounded generation admission. Managed bytes are conservative estimates, not process memory.
/// </summary>
public sealed record ScatterGenerationLimits
{
    public const int MaximumCandidates = 4194304, MaximumItems = 1048576;
    public const long MaximumEstimatedBytes = 128L * 1024 * 1024;
    public required long MaxCandidateVisits { get; init; }
    public required int MaxItems { get; init; }
    public required long MaxEstimatedBytes { get; init; }
}

/// <summary>
/// Places trees, bushes, ferns and rocks on scatter tiles: a jittered grid of candidates per kind, each kept with
/// the probability the climate, slope and clearings give at that spot. A pure function of the surface and the tile.
/// </summary>
public static class Scatter
{
    /// <summary>Quadtree level of the real planet's scatter tiles (about 154 m across on the first world, <see cref="ScatterPlan.Planet"/>).</summary>
    public const int Level = 16;

    private const int DensityGrid = 8; // density samples per tile edge (plus one)

    /// <summary>Outcrops per square metre on open, gentle land of average roughness (about one per 3,000 m²).</summary>
    public const double OutcropDensity = 1.0 / 3000;

    private readonly struct Densities
    {
        public readonly float Trees, Bushes, Ferns, Rocks, Area, RedBerries, BlueBerries;
        public readonly float Fir, Pine, Birch, Broadleaf;
        public readonly float Outcrops, Mountain;

        public Densities(in ScatterDensity d, double area)
        {
            VegetationSample v = d.Vegetation;
            Trees = (float)v.Trees;
            Bushes = (float)v.Bushes;
            Ferns = (float)v.Ferns;
            Rocks = (float)v.Rocks;
            RedBerries = (float)v.RedBerries;
            BlueBerries = (float)v.BlueBerries;
            Area = (float)area;
            Fir = (float)d.Fir;
            Pine = (float)d.Pine;
            Birch = (float)d.Birch;
            Broadleaf = (float)d.Broadleaf;
            Outcrops = (float)d.Outcrops;
            Mountain = (float)d.Mountain;
        }
    }

    /// <summary>Everything the scatter places on a tile of the surface's scatter level, in a fixed order.</summary>
    public static List<ScatterItem> ForTile(IScatterField surface, TileKey tile) => ForTile(surface, tile, treesOnly: false);

    /// <summary>
    /// The scatter of a tile, or only its trees (for drawing far away). The trees are the same objects either way: each
    /// kind has its own candidates and random streams.
    /// </summary>
    public static List<ScatterItem> ForTile(IScatterField surface, TileKey tile, bool treesOnly) =>
        ForTileCore(surface, tile, treesOnly, null, default);

    /// <summary>Exact candidate visits, before density sampling. Tree species share one candidate stream.</summary>
    public static long CandidateCount(ScatterPlan plan, bool treesOnly = false)
    {
        if (plan.Level is < 0 or > 26) throw new ArgumentOutOfRangeException(nameof(plan));
        static long Square(int cells) => cells > 0 ? checked((long)cells * cells) :
            throw new ArgumentOutOfRangeException(nameof(plan));
        long trees = Square(plan.TreeCells);
        return treesOnly ? trees : checked(trees + Square(plan.BushCells) + Square(plan.FernCells) +
            Square(plan.RockCells) + 2 * Square(plan.BerryCells) + 2 * Square(plan.OutcropCells));
    }

    /// <summary>Managed estimate for density arrays and an exact-capacity item list, not process memory.</summary>
    public static long EstimatedBuildBytes(int maximumItems)
    {
        if (maximumItems < 0 || maximumItems > ScatterGenerationLimits.MaximumItems)
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        return checked(4096L + 121L * (2 * Unsafe.SizeOf<Vector3D>() + Unsafe.SizeOf<SurfaceSample>()) +
            81L * Unsafe.SizeOf<Densities>() + (long)maximumItems * Unsafe.SizeOf<ScatterItem>());
    }

    /// <summary>All-or-fail bounded generation. Accepted order, values and seed streams are identical to ForTile.</summary>
    public static List<ScatterItem> ForTile(IScatterField surface, TileKey tile, ScatterGenerationLimits limits,
        CancellationToken cancellation = default, bool treesOnly = false)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return ForTileCore(surface, tile, treesOnly, limits, cancellation);
    }

    private static List<ScatterItem> ForTileCore(IScatterField surface, TileKey tile, bool treesOnly,
        ScatterGenerationLimits? limits, CancellationToken cancellation)
    {
        ScatterPlan plan = surface.ScatterPlan;
        int maximumItems = int.MaxValue, capacity = 0;
        if (limits != null)
        {
            long candidates = CandidateCount(plan, treesOnly);
            if (limits.MaxCandidateVisits < 0 || limits.MaxCandidateVisits > ScatterGenerationLimits.MaximumCandidates ||
                limits.MaxItems < 0 || limits.MaxItems > ScatterGenerationLimits.MaximumItems || limits.MaxEstimatedBytes < 1 ||
                limits.MaxEstimatedBytes > ScatterGenerationLimits.MaximumEstimatedBytes)
                throw new ArgumentOutOfRangeException(nameof(limits));
            capacity = (int)Math.Min(candidates, limits.MaxItems); maximumItems = limits.MaxItems;
            if (candidates > limits.MaxCandidateVisits || EstimatedBuildBytes(capacity) > limits.MaxEstimatedBytes)
                throw new FormationCapacityException("Scatter generation exceeds its admitted candidate or byte allowance.");
        }
        cancellation.ThrowIfCancellationRequested();
        var items = capacity == 0 ? new List<ScatterItem>() : new List<ScatterItem>(capacity);
        if (tile.Level != plan.Level)
        {
            return items;
        }
        Densities[] grid = DensityField(surface, tile, cancellation);
        ulong tileSeed = Hash64.Of(surface.ScatterSeed, 0x53434154, tile.Packed); // "SCAT"
        Place(surface, plan, tile, grid, tileSeed, ScatterKind.Fir, items, maximumItems, cancellation);
        if (!treesOnly)
        {
            Place(surface, plan, tile, grid, tileSeed, ScatterKind.Bush, items, maximumItems, cancellation);
            Place(surface, plan, tile, grid, tileSeed, ScatterKind.Fern, items, maximumItems, cancellation);
            Place(surface, plan, tile, grid, tileSeed, ScatterKind.Rock, items, maximumItems, cancellation);
            Place(surface, plan, tile, grid, tileSeed, ScatterKind.BerryRed, items, maximumItems, cancellation);
            Place(surface, plan, tile, grid, tileSeed, ScatterKind.BerryBlue, items, maximumItems, cancellation);
            // Gameplay kinds after the plants, each from its own candidates and streams, so adding them moved nothing.
            Place(surface, plan, tile, grid, tileSeed, ScatterKind.StoneOutcrop, items, maximumItems, cancellation);
            Place(surface, plan, tile, grid, tileSeed, ScatterKind.HighGradeOutcrop, items, maximumItems, cancellation);
        }
        cancellation.ThrowIfCancellationRequested();
        return items;
    }

    private static Densities[] DensityField(IScatterField surface, TileKey tile, CancellationToken cancellation)
    {
        int n = DensityGrid + 1, m = n + 2;
        var directions = new Vector3D[m * m];
        var samples = new SurfaceSample[m * m];
        var points = new Vector3D[m * m];
        for (int j = 0; j < m; j++)
        {
            for (int i = 0; i < m; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                Vector3D d = TileMesh.Direction(tile, DensityGrid, i - 1, j - 1);
                SurfaceSample s = surface.Sample(d);
                int k = j * m + i;
                directions[k] = d;
                samples[k] = s;
                points[k] = d * (surface.Radius + s.Height);
            }
        }
        var result = new Densities[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                int k = (j + 1) * m + (i + 1);
                Vector3D du = points[k + 1] - points[k - 1], dv = points[k + m] - points[k - m];
                Vector3D cross = Vector3D.Cross(du, dv);
                double length = cross.Length;
                double up = length > 0 ? Vector3D.Dot(cross, directions[k]) / length : 1;
                double slope = Math.Sqrt(Math.Max(0, 1 - up * up));
                // Ground area per grid cell: the cross product spans two cells each way.
                double area = length / 4;
                result[j * n + i] = new Densities(surface.Densities(directions[k], samples[k], slope), area);
            }
        }
        return result;
    }

    private static void Place(IScatterField surface, ScatterPlan plan, TileKey tile, Densities[] grid, ulong tileSeed,
        ScatterKind kind, List<ScatterItem> items, int maximumItems, CancellationToken cancellation)
    {
        int n = DensityGrid + 1;
        int cells = plan.CellsFor(kind);
        double span = tile.Span;
        double cellArea = 1.0 / ((double)cells * cells) * (DensityGrid * DensityGrid); // in density-grid cell areas
        for (int cj = 0; cj < cells; cj++)
        {
            for (int ci = 0; ci < cells; ci++)
            {
                cancellation.ThrowIfCancellationRequested();
                int candidate = cj * cells + ci;
                var rng = new Rng(Hash64.Of(tileSeed, (ulong)kind, (ulong)candidate));
                double fu = (ci + rng.Range(0.12, 0.88)) / cells, fv = (cj + rng.Range(0.12, 0.88)) / cells;
                // Bilinear density at the candidate.
                double gx = fu * DensityGrid, gy = fv * DensityGrid;
                int x0 = Math.Min((int)gx, DensityGrid - 1), y0 = Math.Min((int)gy, DensityGrid - 1);
                double tx = gx - x0, ty = gy - y0;
                Densities a = grid[y0 * n + x0], b = grid[y0 * n + x0 + 1], c = grid[(y0 + 1) * n + x0], d = grid[(y0 + 1) * n + x0 + 1];
                double density = Bilinear(Density(a, kind), Density(b, kind), Density(c, kind), Density(d, kind), tx, ty);
                double area = Bilinear(a.Area, b.Area, c.Area, d.Area, tx, ty) * cellArea;
                if (!(rng.NextDouble() < density * area))
                {
                    continue;
                }
                double u = tile.U0 + fu * span, v = tile.V0 + fv * span;
                Vector3D direction = CubeSphere.Direction(tile.Face, u, v);
                if (!surface.AllowsScatter(direction)) continue;
                ScatterKind placed = kind;
                float scale;
                if (kind == ScatterKind.Fir)
                {
                    placed = TreeKind(plan.Level, tileSeed, direction, ref rng,
                        Bilinear(a.Fir, b.Fir, c.Fir, d.Fir, tx, ty), Bilinear(a.Pine, b.Pine, c.Pine, d.Pine, tx, ty),
                        Bilinear(a.Birch, b.Birch, c.Birch, d.Birch, tx, ty), Bilinear(a.Broadleaf, b.Broadleaf, c.Broadleaf, d.Broadleaf, tx, ty));
                    scale = (float)rng.Range(0.7, 1.3);
                }
                else if (kind == ScatterKind.Rock)
                {
                    // Many small stones, few boulders.
                    double r = rng.NextDouble();
                    scale = (float)(0.25 + 2.2 * r * r * r);
                }
                else if (kind is ScatterKind.StoneOutcrop or ScatterKind.HighGradeOutcrop)
                {
                    placed = kind == ScatterKind.HighGradeOutcrop ? kind
                        : OutcropKind(ref rng, Bilinear(a.Mountain, b.Mountain, c.Mountain, d.Mountain, tx, ty));
                    scale = (float)rng.Range(0.8, 1.25);
                }
                else
                {
                    scale = (float)rng.Range(0.6, 1.4);
                }
                byte variant = (byte)rng.NextInt(256);
                float yaw = (float)rng.Range(0, DMath.TwoPi);
                float height = (float)surface.HeightAt(direction);
                if (items.Count == maximumItems)
                    throw new FormationCapacityException("Scatter output exceeds its admitted item allowance.");
                items.Add(new ScatterItem((int)placed << 24 | candidate, placed, variant, direction, height, yaw, scale));
            }
        }
    }

    /// <summary>
    /// Outcrops of stone and ore per square metre (gameplay, GROUND.md §6.1): on any land but sand, ice and cliffs, more on
    /// stony and mountain ground, fewer in closed forest, none at the waterline.
    /// </summary>
    public static double Outcrops(in SurfaceSample s, ReadOnlySpan<double> biomes, double slope)
    {
        double land = Math.Max(0, 1 - biomes[(int)Biome.Ocean] - biomes[(int)Biome.Beach] - biomes[(int)Biome.IceSheet]);
        if (land <= 0 || s.Height < 1.5)
        {
            return 0;
        }
        double stony = Math.Min(3, 0.6 + 1.4 * s.Roughness + 1.5 * s.Mountain + 0.8 * biomes[(int)Biome.Alpine]);
        double forest = biomes[(int)Biome.BorealForest] + biomes[(int)Biome.TemperateForest] + biomes[(int)Biome.TropicalForest];
        double cliffs = 1 - DMath.SmoothStep(0.55, 0.75, slope);
        double shore = DMath.SmoothStep(1.5, 5, s.Height);
        return OutcropDensity * land * stony * cliffs * shore * (1 - 0.4 * Math.Min(1, forest));
    }

    // Which outcrop a candidate is: mostly stone, metal about a third, sulphur a sixth; more ore in the mountains.
    private static ScatterKind OutcropKind(ref Rng rng, double mountain)
    {
        double metal = 0.32 + 0.15 * Math.Min(1, mountain), sulphur = 0.17 + 0.05 * Math.Min(1, mountain);
        double r = rng.NextDouble();
        return r < sulphur ? ScatterKind.SulphurOutcrop : r < sulphur + metal ? ScatterKind.MetalOutcrop : ScatterKind.StoneOutcrop;
    }

    // Trees grow in stands: a stand (about 40 m across) mostly holds one kind, picked from the local mix.
    private static ScatterKind TreeKind(int level, ulong tileSeed, Vector3D direction, ref Rng rng,
        double fir, double pine, double birch, double broadleaf)
    {
        Span<double> w = stackalloc double[] { fir, pine, birch, broadleaf };
        TileKey stand = TileKey.At(direction, level + 2);
        var standRng = new Rng(Hash64.Of(tileSeed, 0x5354414E, stand.Packed)); // "STAN"
        int dominant = standRng.PickWeighted(w);
        int pick = rng.Chance(0.7) && dominant >= 0 ? dominant : rng.PickWeighted(w);
        return pick switch
        {
            1 => ScatterKind.Pine,
            2 => ScatterKind.Birch,
            3 => ScatterKind.Broadleaf,
            _ => ScatterKind.Fir,
        };
    }

    private static double Density(in Densities d, ScatterKind kind) => kind switch
    {
        ScatterKind.Fir => d.Trees,
        ScatterKind.Bush => d.Bushes,
        ScatterKind.Fern => d.Ferns,
        ScatterKind.BerryRed => d.RedBerries,
        ScatterKind.BerryBlue => d.BlueBerries,
        ScatterKind.StoneOutcrop => d.Outcrops,
        ScatterKind.HighGradeOutcrop => d.Outcrops * 0.075,
        _ => d.Rocks,
    };

    private static double Bilinear(double a, double b, double c, double d, double tx, double ty) =>
        (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
}
