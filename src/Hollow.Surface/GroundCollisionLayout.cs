using System;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>The fixed physical tile address/margin contract; operational capacities are deliberately separate.</summary>
public static class GroundCollisionLayout
{
    public const string Contract = "ground-64m-level6-canonical-2m-margin-v1";
    public const float TileMetres = 64, MarginMetres = 2;
    public const int MaximumLevel = 6, TileIdCount = 6 << (2 * MaximumLevel);

    public static int LevelOn(double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        return Math.Min(TileKey.LevelFor(radius, TileMetres), MaximumLevel);
    }

    /// <summary>All positive radii may opt in if their chosen lattice fits the exact cell and margin ceilings.</summary>
    public static void Validate(TerrainLattice lattice)
    {
        ArgumentNullException.ThrowIfNull(lattice);
        int level = LevelOn(lattice.Radius), difference = lattice.VertexLevel - level;
        if (difference < 0 || difference > 7)
            throw new FormationValidationException("The selected physical tile must contain 1..128 canonical cells per side.");
        double spacing = TileKey.EdgeLength(lattice.VertexLevel, lattice.Radius);
        if (!double.IsFinite(spacing) || spacing <= 0 || MarginMetres / spacing > TerrainLattice.MaximumMarginCells)
            throw new FormationValidationException("The selected radius/lattice needs more than 128 canonical collision margin rings.");
    }

    /// <summary>
    /// Conservative topology envelope over every tile, including diagnostic radii. Gluing the cube's square grids
    /// removes a quadrant at each cube corner; its vertex-neighbor rings cannot exceed the unfolded square. The
    /// full sphere is a second bound (6*N*N cells, 6*N*N+2 welded vertices). No terrain sampling is needed.
    /// </summary>
    public static (int Cells, int Vertices) MaximumTopology(TerrainLattice lattice)
    {
        Validate(lattice);
        int cells = 1 << (lattice.VertexLevel - LevelOn(lattice.Radius));
        int rings = (int)Math.Ceiling(MarginMetres / TileKey.EdgeLength(lattice.VertexLevel, lattice.Radius));
        long side = cells + 2L * rings, n = 1L << lattice.VertexLevel, sphereCells = 6 * n * n;
        return ((int)Math.Min(side * side, sphereCells), (int)Math.Min((side + 1) * (side + 1), sphereCells + 2));
    }

    public static void Validate(TerrainLattice lattice, SelectedGroundCollisionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Reserve();
        (int cells, int vertices) = MaximumTopology(lattice);
        if (cells > limits.Terrain.MaxCells || vertices > limits.Terrain.MaxVertices ||
            vertices > limits.MaxVertices || 2 * cells > limits.MaxTriangles)
            throw new FormationCapacityException("The selected collision profile does not admit the complete physical tile/margin topology at this radius and lattice.");
    }
}

public sealed record GroundAreaLimits
{
    public required int MaxTiles { get; init; }
    public required long MaxEstimatedBytes { get; init; }
}

/// <summary>
/// Immutable conservative radial shadow of a swept body-space AABB. No sampling, height callback or global cache.
/// The full tile count/retention is admitted before allocation; capacity never publishes a partial area.
/// </summary>
public sealed class GroundAreaPlan
{
    private readonly TileKey[] _tiles;
    private GroundAreaPlan(double radius, int level, TileKey[] tiles, long bytes)
    { Radius = radius; Level = level; _tiles = tiles; EstimatedRetainedBytes = bytes; }
    public double Radius { get; }
    public int Level { get; }
    public ReadOnlySpan<TileKey> Tiles => _tiles;
    public long EstimatedRetainedBytes { get; }

    public static GroundAreaPlan Around(double radius, Vector3D point, double reach, GroundAreaLimits limits,
        CancellationToken cancellation = default) => Along(radius, point, point, reach, limits, cancellation);

    public static GroundAreaPlan Along(double radius, Vector3D from, Vector3D to, double reach, GroundAreaLimits limits,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        cancellation.ThrowIfCancellationRequested();
        int level = GroundCollisionLayout.LevelOn(radius);
        FormationInput.Finite(from, nameof(from)); FormationInput.Finite(to, nameof(to));
        if (!double.IsFinite(reach) || reach < 0) throw new ArgumentOutOfRangeException(nameof(reach));
        if (limits.MaxTiles < 1 || limits.MaxTiles > GroundCollisionLayout.TileIdCount ||
            limits.MaxEstimatedBytes < 1 || limits.MaxEstimatedBytes > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(limits));
        var bounds = new FormationBounds(
            new Vector3D(Math.BitDecrement(Math.Min(from.X, to.X) - reach), Math.BitDecrement(Math.Min(from.Y, to.Y) - reach),
                Math.BitDecrement(Math.Min(from.Z, to.Z) - reach)),
            new Vector3D(Math.BitIncrement(Math.Max(from.X, to.X) + reach), Math.BitIncrement(Math.Max(from.Y, to.Y) + reach),
                Math.BitIncrement(Math.Max(from.Z, to.Z) + reach)));
        Span<TerrainFaceCellRange> ranges = stackalloc TerrainFaceCellRange[6];
        int count = 0, rangeCount = 0;
        for (int face = 0; face < 6; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (CanonicalRadialShadow.Range(bounds, face, level) is not { } range) continue;
            count = checked(count + (range.X1 - range.X0 + 1) * (range.Y1 - range.Y0 + 1));
            if (count > limits.MaxTiles) throw new FormationCapacityException("The complete ground area exceeds its tile allowance.");
            ranges[rangeCount++] = range;
        }
        long bytes = 2048L + 16L * count;
        if (bytes > limits.MaxEstimatedBytes) throw new FormationCapacityException("The complete ground area exceeds its retention allowance.");
        var tiles = new TileKey[count]; int at = 0;
        for (int r = 0; r < rangeCount; r++)
        {
            TerrainFaceCellRange range = ranges[r];
            for (int x = range.X0; x <= range.X1; x++)
            for (int y = range.Y0; y <= range.Y1; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                tiles[at++] = new TileKey(range.Face, level, x, y);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return new GroundAreaPlan(radius, level, tiles, bytes);
    }
}
