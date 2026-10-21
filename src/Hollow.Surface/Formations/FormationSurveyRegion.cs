using System;
using System.Collections.Generic;
using System.Threading;

namespace Hollow.Surface;

/// <summary>The finite survey lacks complete contour, fitting or neighbor-conflict evidence; no partial plan may be published.</summary>
public sealed class FormationIncompleteRegionException(string message) : Exception(message);

/// <summary>Explicit finite limits for copying core cells and constructing an edge-neighbor halo.</summary>
public sealed record FormationSurveyRegionLimits
{
    public int MaxCoreCells { get; init; } = 256;
    /// <summary>At most 98,304: all six faces at the deepest supported actual grid level, seven.</summary>
    public int MaxSurveyCells { get; init; } = 4096;
    public int MaxHaloRings { get; init; } = 16;
    public long MaxInputVisits { get; init; } = 256;
    /// <summary>All neighbor requests, including halo expansion and both boundary scans.</summary>
    public long MaxNeighborVisits { get; init; } = 65536;
    public long MaxEstimatedOwnedBytes { get; init; } = 2L * 1024 * 1024;
    public long MaxEstimatedScratchBytes { get; init; } = 4L * 1024 * 1024;
}

/// <summary>Counts are completed work; byte estimates are conservative accounting, not measured heap usage.</summary>
public readonly record struct FormationSurveyRegionDiagnostics(
    long InputVisits, long NeighborVisits, int CoreCellCount, int SurveyCellCount,
    int CoreBoundaryEdgeCount, int BoundaryEdgeCount, long EstimatedOwnedBytes,
    long ReservedOwnedBytes, long ReservedScratchBytes);

/// <summary>
/// Immutable sparse topology at the planner's actual grid level (survey level plus one).
/// Core cells are explicit; Cells includes every cell within HaloRings edge-neighbor steps of the core.
/// This class makes no geometric point, segment, support or physical-clearance containment claim.
/// </summary>
public sealed class FormationSurveyRegion
{
    private readonly TileKey[] _core;
    private readonly TileKey[] _cells;
    private readonly CubeEdgeKey[] _coreBoundary;
    private readonly CubeEdgeKey[] _boundary;

    /// <summary>
    /// Copies nonempty, unique, same-level core cells. Enumeration order cannot change the result.
    /// Count, work and byte exhaustion throws FormationCapacityException; cancellation throws before publication.
    /// Reservations precede enumeration and cover simultaneous tree nodes, queues, copies and boundary arrays.
    /// </summary>
    public FormationSurveyRegion(int gridLevel, IEnumerable<TileKey> coreCells, int haloRings,
        FormationSurveyRegionLimits? limits = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(coreCells);
        limits ??= new FormationSurveyRegionLimits();
        Validate(gridLevel, haloRings, limits);

        long declaredCells = (long)limits.MaxCoreCells + limits.MaxSurveyCells;
        // Owned: two cell arrays and at most four canonical edge keys per cell, plus array/object overhead.
        // Scratch: includes owned output, initial core copy, BFS array, tree nodes, and untrimmed edge copies.
        // These are reservation coefficients, deliberately above current managed element sizes.
        long reservedOwned = 1024L + 256L * declaredCells;
        long reservedScratch = 4096L + 512L * declaredCells;
        if (reservedOwned > limits.MaxEstimatedOwnedBytes || reservedScratch > limits.MaxEstimatedScratchBytes)
            throw new FormationCapacityException("Survey region copy/expansion reservation exceeds its byte budget.");

        long inputVisits = 0, neighborVisits = 0;
        var input = new TileKey[limits.MaxCoreCells];
        int coreCount = 0;
        // Check cancellation before MoveNext as well as after it: arbitrary enumerators are outside our ownership.
        using (IEnumerator<TileKey> enumerator = coreCells.GetEnumerator())
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!enumerator.MoveNext()) break;
                cancellationToken.ThrowIfCancellationRequested();
                Charge(ref inputVisits, limits.MaxInputVisits, "Survey region input visit limit exceeded.");
                if (coreCount == limits.MaxCoreCells)
                    throw new FormationCapacityException("Survey region core cell limit exceeded.");
                TileKey cell = enumerator.Current;
                if (cell.Level != gridLevel || TileKey.Checked(cell.Face, cell.Level, cell.X, cell.Y) is null)
                    throw new FormationValidationException("Survey core cells must be valid and use the actual grid level.");
                input[coreCount++] = cell;
            }
        }
        if (coreCount == 0) throw new FormationValidationException("A survey region requires at least one core cell.");
        Array.Sort(input, 0, coreCount, CellComparer.Instance);
        for (int i = 1; i < coreCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input[i].Equals(input[i - 1]))
                throw new FormationValidationException("A survey region cannot contain duplicate core cells.");
        }
        var core = new TileKey[coreCount];
        Array.Copy(input, core, coreCount);

        // A deterministic ordered set bounds lookup work; no hash enumeration determines traversal or identity.
        var included = new SortedSet<TileKey>(CellComparer.Instance);
        var queue = new TileKey[limits.MaxSurveyCells];
        for (int i = 0; i < coreCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            included.Add(core[i]); queue[i] = core[i];
        }
        int count = coreCount, start = 0, end = coreCount;
        for (int ring = 0; ring < haloRings && start < end; ring++)
        {
            for (int index = start; index < end; index++)
            for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Charge(ref neighborVisits, limits.MaxNeighborVisits, "Survey region neighbor visit limit exceeded.");
                TileKey neighbor = CubeTopology.Neighbor(queue[index], (CubeEdge)edgeIndex);
                if (included.Contains(neighbor)) continue;
                if (count == limits.MaxSurveyCells)
                    throw new FormationCapacityException("Survey region expanded cell limit exceeded.");
                included.Add(neighbor); queue[count++] = neighbor;
            }
            start = end; end = count;
        }
        var cells = new TileKey[count];
        Array.Copy(queue, cells, count);
        Array.Sort(cells, CellComparer.Instance);
        CubeEdgeKey[] coreBoundary = Boundary(core, limits, ref neighborVisits, cancellationToken);
        CubeEdgeKey[] boundary = Boundary(cells, limits, ref neighborVisits, cancellationToken);
        long estimatedOwned = 1024L + 16L * (core.Length + (long)cells.Length)
            + 48L * (coreBoundary.Length + (long)boundary.Length);

        cancellationToken.ThrowIfCancellationRequested();
        GridLevel = gridLevel; HaloRings = haloRings;
        _core = core; _cells = cells; _coreBoundary = coreBoundary; _boundary = boundary;
        Diagnostics = new FormationSurveyRegionDiagnostics(inputVisits, neighborVisits, core.Length, cells.Length,
            coreBoundary.Length, boundary.Length, estimatedOwned, reservedOwned, reservedScratch);
    }

    public int GridLevel { get; }
    public int HaloRings { get; }
    /// <summary>Unique explicit core cells sorted by face, then Y, then X.</summary>
    public ReadOnlySpan<TileKey> CoreCells => _core;
    /// <summary>Core and all requested halo rings, sorted by face, then Y, then X.</summary>
    public ReadOnlySpan<TileKey> Cells => _cells;
    /// <summary>Canonical edges having exactly one adjacent core cell; includes holes and cross-face seams.</summary>
    public ReadOnlySpan<CubeEdgeKey> CoreBoundaryEdges => _coreBoundary;
    /// <summary>Canonical edges having exactly one adjacent expanded cell; includes holes and cross-face seams.</summary>
    public ReadOnlySpan<CubeEdgeKey> BoundaryEdges => _boundary;
    public FormationSurveyRegionDiagnostics Diagnostics { get; }

    public bool HasCore(TileKey cell) => cell.Level == GridLevel && Contains(_core, cell);
    public bool ContainsCell(TileKey cell) => cell.Level == GridLevel && Contains(_cells, cell);
    public bool IsCoreBoundaryEdge(CubeEdgeKey edge) => edge.IsValid && Array.BinarySearch(_coreBoundary, edge) >= 0;
    public bool IsBoundaryEdge(CubeEdgeKey edge) => edge.IsValid && Array.BinarySearch(_boundary, edge) >= 0;

    private static bool Contains(TileKey[] cells, TileKey cell) =>
        Array.BinarySearch(cells, cell, CellComparer.Instance) >= 0;

    private static CubeEdgeKey[] Boundary(TileKey[] cells, FormationSurveyRegionLimits limits,
        ref long neighborVisits, CancellationToken cancellationToken)
    {
        var edges = new CubeEdgeKey[cells.Length * 4];
        int count = 0;
        foreach (TileKey cell in cells)
        for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Charge(ref neighborVisits, limits.MaxNeighborVisits, "Survey region neighbor visit limit exceeded.");
            CubeEdge edge = (CubeEdge)edgeIndex;
            if (!Contains(cells, CubeTopology.Neighbor(cell, edge))) edges[count++] = CubeTopology.EdgeKey(cell, edge);
        }
        Array.Sort(edges, 0, count);
        cancellationToken.ThrowIfCancellationRequested();
        if (count == 0) return Array.Empty<CubeEdgeKey>();
        var result = new CubeEdgeKey[count];
        Array.Copy(edges, result, count);
        return result;
    }

    private static void Charge(ref long visits, long maximum, string message)
    {
        if (visits == maximum) throw new FormationCapacityException(message);
        visits++;
    }

    private static void Validate(int gridLevel, int haloRings, FormationSurveyRegionLimits limits)
    {
        if (gridLevel < 1 || gridLevel > 7 || haloRings < 1)
            throw new FormationValidationException("A planner survey uses grid level 1-7 and at least one halo ring.");
        if (limits.MaxCoreCells < 1 || limits.MaxSurveyCells < limits.MaxCoreCells ||
            limits.MaxSurveyCells > 6 * 128 * 128 || limits.MaxHaloRings < 1 ||
            limits.MaxInputVisits < 0 || limits.MaxNeighborVisits < 0 ||
            limits.MaxEstimatedOwnedBytes < 0 || limits.MaxEstimatedScratchBytes < 0)
            throw new FormationValidationException("Invalid survey region count, work or byte limits.");
        if (haloRings > limits.MaxHaloRings)
            throw new FormationCapacityException("Survey region halo ring limit exceeded.");
    }

    private sealed class CellComparer : IComparer<TileKey>
    {
        internal static readonly CellComparer Instance = new();
        public int Compare(TileKey a, TileKey b)
        {
            int order = a.Face.CompareTo(b.Face);
            if (order == 0) order = a.Y.CompareTo(b.Y);
            return order == 0 ? a.X.CompareTo(b.X) : order;
        }
    }
}
