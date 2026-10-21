using System;

namespace Hollow.Surface;

/// <summary>Admitted exact canonical topology, with no terrain samples or source/world reference.</summary>
public sealed class TerrainCollisionPlan
{
    private readonly TileKey[] _cells;
    internal TerrainCollisionPlan(TerrainLattice lattice, TileKey tile, int coreCells, TileKey[] cells,
        int vertices, TerrainCollisionFootprint footprint)
    {
        Lattice = lattice; Tile = tile; CoreCells = coreCells; _cells = cells;
        VertexCount = vertices; Footprint = footprint;
    }
    public TerrainLattice Lattice { get; }
    public TileKey Tile { get; }
    public int VertexCount { get; }
    public int CellCount => IsCore ? CoreCells * CoreCells : _cells.Length;
    public int TriangleCount => 2 * CellCount;
    public TerrainCollisionFootprint Footprint { get; }
    public long EstimatedRetainedBytes => 2048L + 16L * _cells.Length;
    internal int CoreCells { get; }
    internal bool IsCore => _cells.Length == 0;
    internal ReadOnlySpan<TileKey> Cells => _cells;
}
