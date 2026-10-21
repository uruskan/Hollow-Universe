using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Optional immutable grid contract. A null value preserves the legacy surface's sampling policy.</summary>
public interface ITerrainLatticeSurface
{
    TerrainLattice? TerrainLattice { get; }
}

/// <summary>
/// One body's fixed dyadic terrain lattice. The renderer uses 32-cell tiles down to FinestTileLevel;
/// collision and contact consumers use VertexLevel. No surface, cache, mesh or callback is retained.
/// Choosing this contract is opt-in and must be part of the world's generation identity.
/// </summary>
public sealed class TerrainLattice
{
    public const string Contract = "canonical-cube-terrain-lattice-v1";
    public const int RenderCells = 32;
    public const int RenderCellLevel = 5;
    public const int MaximumCellsPerTile = 128;
    public const int MaximumMarginCells = 128;
    public const int MaximumVerticesPerEnumeration = (MaximumCellsPerTile + 1) * (MaximumCellsPerTile + 1);
    public const int MaximumTrianglesPerEnumeration = 2 * MaximumCellsPerTile * MaximumCellsPerTile;
    public const double DefaultFinestTileMetres = 36;

    public TerrainLattice(double radius, int finestTileLevel)
    {
        if (!double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (finestTileLevel < 0 || finestTileLevel > TileKey.MaxLevel - RenderCellLevel)
            throw new ArgumentOutOfRangeException(nameof(finestTileLevel));
        Radius = radius;
        FinestTileLevel = finestTileLevel;
        VertexLevel = finestTileLevel + RenderCellLevel;
        double cellMetres = TileKey.EdgeLength(VertexLevel, radius);
        if (!double.IsFinite(cellMetres) || cellMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Canonical cell size must be finite and representable.");
    }

    public double Radius { get; }
    public int FinestTileLevel { get; }
    public int VertexLevel { get; }
    public int SideVertices => (1 << VertexLevel) + 1;

    public static TerrainLattice ForRadius(double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        return new TerrainLattice(radius, TileKey.LevelFor(radius, DefaultFinestTileMetres,
            TileKey.MaxLevel - RenderCellLevel));
    }

    /// <summary>Resolves the optional contract once per consumer operation and rejects mismatched ownership.</summary>
    public static TerrainLattice? Of(ISurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        TerrainLattice? lattice = (surface as ITerrainLatticeSurface)?.TerrainLattice;
        if (lattice != null && lattice.Radius != surface.Radius)
            throw new ArgumentException("Terrain lattice radius does not match its surface.", nameof(surface));
        return lattice;
    }

    /// <summary>
    /// Exact canonical cells in a requested tile. Coarse requests exceeding 128 cells per side are refused before
    /// allocation; enumerate smaller neighbouring tiles instead. A tile finer than this lattice is also refused.
    /// </summary>
    public int CellsForTile(TileKey tile)
    {
        int difference = VertexLevel - tile.Level;
        if (difference < 0 || difference > 7)
            throw new ArgumentOutOfRangeException(nameof(tile), "Tile must contain between 1 and 128 canonical cells per side.");
        return 1 << difference;
    }

    public void ValidateRenderGrid(TileKey tile, int cells)
    {
        if (cells != RenderCells || tile.Level > FinestTileLevel)
            throw new ArgumentException("An opted-in render grid must use 32 cells and not exceed its canonical finest tile level.");
    }

    /// <summary>Canonical identity at the finest vertex level. Face coordinates include the upper edge.</summary>
    public CubeVertexKey VertexKey(int face, int x, int y) => CubeTopology.VertexKey(face, VertexLevel, x, y);

    /// <summary>
    /// One canonical direction for every identity, including shared face edges/corners. Maximal cube coordinates
    /// use exactly +/-1; they do not depend on which incident face happened to sample Warp(+/-1).
    /// </summary>
    public Vector3D Direction(CubeVertexKey vertex)
    {
        // Face-grid coordinates are (-n + 2*i), not every integer on the cube. Reduction of a level L+1
        // half-step can produce a key whose Level is L; comparing levels alone would admit that off-grid point.
        if (!Contains(vertex))
            throw new ArgumentException("Vertex is outside the terrain lattice.", nameof(vertex));
        double n = 1 << vertex.Level;
        double Warp(int coordinate) => Math.Abs((double)coordinate) == n ? coordinate / n : CubeSphere.Warp(coordinate / n);
        var point = new Vector3D(Warp(vertex.X), Warp(vertex.Y), Warp(vertex.Z));
        return point.Normalized;
    }

    public bool Contains(CubeVertexKey vertex) => vertex.IsValid && vertex.Level <= VertexLevel &&
        (vertex.Level != VertexLevel || ((vertex.X | vertex.Y | vertex.Z) & 1) == 0);

    /// <summary>Exact in-face grid identity. Outside-face margins have no canonical identity.</summary>
    public bool TryGridVertex(TileKey tile, int cells, int i, int j, out CubeVertexKey vertex)
    {
        int level = ValidateSampleGrid(tile, cells);
        if (i < -MaximumMarginCells || j < -MaximumMarginCells ||
            i > cells + MaximumMarginCells || j > cells + MaximumMarginCells)
            throw new ArgumentOutOfRangeException(nameof(cells), "Grid or margin exceeds the canonical lattice bounds.");
        int x = tile.X * cells + i, y = tile.Y * cells + j, n = 1 << level;
        vertex = default;
        if ((uint)x > (uint)n || (uint)y > (uint)n) return false;
        vertex = CubeTopology.VertexKey(tile.Face, level, x, y);
        return true;
    }

    /// <summary>
    /// All canonical cells incident to this vertex, including every incident face. At most four cells (three at a
    /// cube corner), sorted by packed key. A four-entry destination avoids allocations and partial output.
    /// </summary>
    public int IncidentCells(CubeVertexKey vertex, Span<TileKey> destination)
    {
        if (!Contains(vertex)) throw new ArgumentException("Vertex is outside the terrain lattice.", nameof(vertex));
        if (destination.Length < 4) throw new ArgumentException("Four cell slots are required.", nameof(destination));
        int shift = VertexLevel - vertex.Level, n = 1 << VertexLevel;
        int x = vertex.X << shift, y = vertex.Y << shift, z = vertex.Z << shift;
        int Coordinate(Vector3D axis) => (int)axis.X * x + (int)axis.Y * y + (int)axis.Z * z;
        int count = 0;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            if (Coordinate(CubeSphere.Normal(face)) != n) continue;
            int u = (Coordinate(CubeSphere.AxisU(face)) + n) / 2, v = (Coordinate(CubeSphere.AxisV(face)) + n) / 2;
            for (int j = v - 1; j <= v; j++)
            for (int i = u - 1; i <= u; i++)
                if ((uint)i < (uint)n && (uint)j < (uint)n)
                    destination[count++] = new TileKey(face, VertexLevel, i, j);
        }
        destination[..count].Sort(static (a, b) => a.Packed.CompareTo(b.Packed));
        return count;
    }

    /// <summary>
    /// Direction for a dyadic render/collision grid no finer than this lattice. Interior vertices use canonical
    /// identities. The bounded outside-face normal/collision margin retains the existing extrapolated cube warp;
    /// it is not a canonical contact primitive. Contact gates must enumerate actual neighbouring face cells.
    /// </summary>
    public Vector3D GridDirection(TileKey tile, int cells, int i, int j)
    {
        int level = ValidateSampleGrid(tile, cells);
        if (i < -MaximumMarginCells || j < -MaximumMarginCells ||
            i > cells + MaximumMarginCells || j > cells + MaximumMarginCells)
            throw new ArgumentOutOfRangeException(nameof(cells), "Grid or margin exceeds the canonical lattice bounds.");
        int x = tile.X * cells + i, y = tile.Y * cells + j, n = 1 << level;
        if ((uint)x <= (uint)n && (uint)y <= (uint)n)
            return Direction(CubeTopology.VertexKey(tile.Face, level, x, y));
        return CubeSphere.Direction(tile.Face, TileKey.GridCoordinate(tile.X, tile.Level, i, cells),
            TileKey.GridCoordinate(tile.Y, tile.Level, j, cells));
    }

    /// <summary>Validates a bounded dyadic sample grid and returns its global vertex level.</summary>
    public int ValidateSampleGrid(TileKey tile, int cells)
    {
        int level = tile.Level + CellLevel(cells);
        if (level > VertexLevel) throw new ArgumentOutOfRangeException(nameof(cells), "Grid is finer than the canonical lattice.");
        return level;
    }

    /// <summary>One finest-grid cell's triangle, in the same outward winding and diagonal as TileMesh/SurfaceGrid.</summary>
    public TerrainLatticeTriangle Triangle(TileKey cell, int half)
    {
        if (cell.Level != VertexLevel || (uint)half > 1)
            throw new ArgumentOutOfRangeException(nameof(cell), "A terrain primitive uses a canonical cell and half 0 or 1.");
        CubeVertexKey a = VertexKey(cell.Face, cell.X, cell.Y);
        CubeVertexKey d = VertexKey(cell.Face, cell.X + 1, cell.Y + 1);
        return half == 0
            ? new TerrainLatticeTriangle(cell, half, a, VertexKey(cell.Face, cell.X + 1, cell.Y), d)
            : new TerrainLatticeTriangle(cell, half, a, d, VertexKey(cell.Face, cell.X, cell.Y + 1));
    }

    /// <summary>Canonical vertices of one bounded tile, row-major including its shared boundary.</summary>
    public IEnumerable<CubeVertexKey> EnumerateVertices(TileKey tile,
        int maximumVertices = MaximumVerticesPerEnumeration, CancellationToken cancellation = default)
    {
        int cells = CellsForTile(tile);
        if (maximumVertices < 0 || maximumVertices > MaximumVerticesPerEnumeration)
            throw new ArgumentOutOfRangeException(nameof(maximumVertices));
        if ((cells + 1) * (cells + 1) > maximumVertices)
            throw new FormationCapacityException("Canonical terrain vertex enumeration exceeds its supplied budget.");
        cancellation.ThrowIfCancellationRequested();
        return Enumerate();

        IEnumerable<CubeVertexKey> Enumerate()
        {
            for (int j = 0; j <= cells; j++)
            for (int i = 0; i <= cells; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                yield return VertexKey(tile.Face, tile.X * cells + i, tile.Y * cells + j);
            }
        }
    }

    /// <summary>
    /// Row-major cells, lower then upper triangle. Capacity/cancellation is checked before returning an enumerable;
    /// cancellation is also checked for each yielded primitive. No array proportional to the world is created.
    /// </summary>
    public IEnumerable<TerrainLatticeTriangle> EnumerateTriangles(TileKey tile,
        int maximumTriangles = MaximumTrianglesPerEnumeration, CancellationToken cancellation = default)
    {
        int cells = CellsForTile(tile);
        if (maximumTriangles < 0 || maximumTriangles > MaximumTrianglesPerEnumeration)
            throw new ArgumentOutOfRangeException(nameof(maximumTriangles));
        if (2 * cells * cells > maximumTriangles)
            throw new FormationCapacityException("Canonical terrain triangle enumeration exceeds its supplied budget.");
        cancellation.ThrowIfCancellationRequested();
        return Enumerate();

        IEnumerable<TerrainLatticeTriangle> Enumerate()
        {
            for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
            {
                var cell = new TileKey(tile.Face, VertexLevel, tile.X * cells + i, tile.Y * cells + j);
                cancellation.ThrowIfCancellationRequested();
                yield return Triangle(cell, 0);
                cancellation.ThrowIfCancellationRequested();
                yield return Triangle(cell, 1);
            }
        }
    }

    private static int CellLevel(int cells)
    {
        if (cells < 1 || cells > MaximumCellsPerTile || (cells & (cells - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(cells), "A terrain grid needs 1..128 power-of-two cells.");
        int level = 0;
        while ((1 << level) < cells) level++;
        return level;
    }
}

/// <summary>World-independent primitive address and canonical vertex identities; the owning world supplies heights.</summary>
public readonly record struct TerrainLatticeTriangle(TileKey Cell, int Half, CubeVertexKey A, CubeVertexKey B, CubeVertexKey C);
