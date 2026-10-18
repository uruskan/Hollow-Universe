using System;
using System.Collections.Generic;
using System.Threading;

namespace Hollow.Surface;

/// <summary>
/// Full bounded contour identity. Start cell/edge pair identifies the canonical first undirected contour segment;
/// sections are numbered while tracing, before any fitting/rejection. Recipe domains are unique in the catalog.
/// Owner packs X7,Y7,Face3,GridLevel3,EdgeA2,EdgeB2,Revision31. Section packs Domain16,Section16.
/// This is injective over the validated tuple, uses no hash, and preserves 9 unused Owner bits.
/// </summary>
public readonly record struct FormationContourIdentity
{
    public FormationContourIdentity(int face, int gridLevel, int x, int y, int edgeA, int edgeB,
        ushort recipeDomain, int recipeRevision, int section)
    {
        if (face < 0 || face >= 6 || gridLevel < 1 || gridLevel > 7 || x < 0 || x >= (1 << gridLevel) ||
            y < 0 || y >= (1 << gridLevel) || edgeA < 0 || edgeA > 3 || edgeB < 0 || edgeB > 3 ||
            edgeA == edgeB || recipeRevision < 0 || section < 0 || section > ushort.MaxValue)
            throw new FormationValidationException("Contour identity exceeds its reversible bit allocation.");
        Face = face; GridLevel = gridLevel; X = x; Y = y;
        EdgeA = Math.Min(edgeA, edgeB); EdgeB = Math.Max(edgeA, edgeB);
        RecipeDomain = recipeDomain; RecipeRevision = recipeRevision; Section = section;
    }
    public int Face { get; }
    public int GridLevel { get; }
    public int X { get; }
    public int Y { get; }
    public int EdgeA { get; }
    public int EdgeB { get; }
    public ushort RecipeDomain { get; }
    public int RecipeRevision { get; }
    public int Section { get; }
    public CubeEdgeKey FirstEdge => CellEdge(EdgeA);
    public CubeEdgeKey SecondEdge => CellEdge(EdgeB);
    public FormationId Id => new((ulong)(uint)X | ((ulong)(uint)Y << 7) | ((ulong)(uint)Face << 14) | ((ulong)(uint)GridLevel << 17) |
        ((ulong)(uint)EdgeA << 20) | ((ulong)(uint)EdgeB << 22) | ((ulong)(uint)RecipeRevision << 24),
        ((uint)RecipeDomain << 16) | (uint)Section);
    private CubeEdgeKey CellEdge(int edge)
    {
        var tile = new TileKey(Face, GridLevel, X, Y);
        return CubeTopology.EdgeKey(tile, edge switch { 0 => CubeEdge.VMin, 1 => CubeEdge.UMax, 2 => CubeEdge.VMax, _ => CubeEdge.UMin });
    }
    public static FormationContourIdentity FromId(FormationId id)
    {
        if ((id.Owner >> 55) != 0) throw new FormationValidationException("Unknown packed contour identity bits.");
        var decoded = new FormationContourIdentity((int)((id.Owner >> 14) & 7), (int)((id.Owner >> 17) & 7), (int)(id.Owner & 127),
            (int)((id.Owner >> 7) & 127), (int)((id.Owner >> 20) & 3), (int)((id.Owner >> 22) & 3),
            (ushort)(id.Section >> 16), (int)(id.Owner >> 24), (int)(id.Section & 65535));
        if (decoded.Id != id) throw new FormationValidationException("Noncanonical contour identity bit spelling.");
        return decoded;
    }
}

/// <summary>
/// Survey, contour and work counts include halo evidence; Records, AcceptedPieces and RejectedSlots count published
/// core output. EstimatedOwnedBytes conservatively includes simultaneous provisional and output record storage.
/// RegionCellTests counts completed angular-domain and provenance/neighbor-closure cell visits, including repeats.
/// </summary>
public readonly record struct FormationPlanDiagnostics(int SurveyVertices, int SurveyCells, int ContourSegments,
    int Contours, int Records, int AcceptedPieces, int RejectedSlots, long TerrainSamples, int ContactSamples,
    int CoverageSamples, long TriangleTests, long GeometryVertexVisits, long ConflictTests, int ConservativeSiteRejections,
    int ConservativeConflictRejections, long EstimatedOwnedBytes, long ReservedScratchBytes, long RegionCellTests = 0);

public sealed class FormationPlanEntry
{
    internal FormationPlanEntry(FormationContourIdentity identity, FormationRecord record)
    { Identity = identity; Record = record; }
    public FormationContourIdentity Identity { get; }
    public FormationRecord Record { get; }
}

public sealed record FormationPlanQueryLimits
{
    public int MaxVisitedNodes { get; init; } = 8191;
    public int MaxResults { get; init; } = 4096;
    public long MaxScratchBytes { get; init; } = 1024 * 1024;
}

public sealed class FormationPlanQueryResult
{
    private readonly FormationRecord[] _records;
    internal FormationPlanQueryResult(FormationRecord[] records, int visited) { _records = records; VisitedNodes = visited; }
    public ReadOnlySpan<FormationRecord> Records => _records;
    public int VisitedNodes { get; }
}

/// <summary>Complete immutable snapshot. Catalog geometry is shared; no terrain, callback, survey or memo table survives.</summary>
public sealed class FormationPlan
{
    private readonly FormationPlanEntry[] _entries;
    private readonly Node[] _nodes;
    internal FormationPlan(FormationPlanEntry[] entries, FormationPlanDiagnostics diagnostics, CancellationToken cancellation)
    {
        _entries = (FormationPlanEntry[])entries.Clone(); Diagnostics = diagnostics;
        Array.Sort(_entries, static (a, b) => a.Record.Id.CompareTo(b.Record.Id));
        for (int i = 1; i < _entries.Length; i++)
            if (_entries[i - 1].Record.Id == _entries[i].Record.Id) throw new FormationValidationException("Duplicate planned identity.");
        _nodes = new Node[Math.Max(0, 2 * _entries.Length - 1)];
        var order = new int[_entries.Length]; for (int i = 0; i < order.Length; i++) order[i] = i;
        int next = 0;
        if (order.Length > 0) Build(0, order.Length);
        cancellation.ThrowIfCancellationRequested();
        int Build(int start, int count)
        {
            cancellation.ThrowIfCancellationRequested(); int node = next++;
            FormationBounds bounds = _entries[order[start]].Record.InfluenceBounds;
            for (int i = 1; i < count; i++) bounds = bounds.Union(_entries[order[start + i]].Record.InfluenceBounds);
            if (count == 1) { _nodes[node] = new Node(bounds, -1, -1, order[start]); return node; }
            var size = bounds.Extent; int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
            {
                var ca = _entries[a].Record.InfluenceBounds.Centre; var cb = _entries[b].Record.InfluenceBounds.Centre;
                int c = (axis == 0 ? ca.X : axis == 1 ? ca.Y : ca.Z).CompareTo(axis == 0 ? cb.X : axis == 1 ? cb.Y : cb.Z);
                return c != 0 ? c : _entries[a].Record.Id.CompareTo(_entries[b].Record.Id);
            }));
            int half = count / 2, left = Build(start, half), right = Build(start + half, count - half);
            _nodes[node] = new Node(bounds, left, right, -1); return node;
        }
    }
    public ReadOnlySpan<FormationPlanEntry> Entries => _entries;
    public FormationPlanDiagnostics Diagnostics { get; }
    /// <summary>
    /// Inclusive conservative influence-box query. Physical-only queries additionally test actual piece boxes.
    /// Capacity/cancellation throws before returning anything; result order is canonical, never BVH traversal order.
    /// </summary>
    public FormationPlanQueryResult QueryBounds(FormationBounds bounds, bool physicalOnly = false,
        FormationPlanQueryLimits? limits = null, CancellationToken cancellation = default)
    {
        limits ??= new FormationPlanQueryLimits();
        if (limits.MaxVisitedNodes < 0 || limits.MaxResults < 0 || limits.MaxScratchBytes < 0)
            throw new FormationValidationException("Invalid plan query limits.");
        if (4096L + 4L * _nodes.Length + 32L * limits.MaxResults > limits.MaxScratchBytes)
            throw new FormationCapacityException("Plan query scratch reservation exceeded.");
        cancellation.ThrowIfCancellationRequested();
        var stack = new int[_nodes.Length]; var found = new List<FormationRecord>(); int count = 0, visited = 0;
        if (_nodes.Length > 0) stack[count++] = 0;
        while (count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (visited == limits.MaxVisitedNodes) throw new FormationCapacityException("Plan query node budget exceeded.");
            visited++; Node node = _nodes[stack[--count]];
            if (!node.Bounds.Intersects(bounds)) continue;
            if (node.Entry < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
            FormationRecord record = _entries[node.Entry].Record;
            bool include = !physicalOnly;
            if (physicalOnly) foreach (FormationPiece piece in record.Pieces) if (piece.Bounds.Intersects(bounds)) { include = true; break; }
            if (!include) continue;
            if (found.Count == limits.MaxResults) throw new FormationCapacityException("Plan query result budget exceeded.");
            found.Add(record);
        }
        found.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        cancellation.ThrowIfCancellationRequested();
        return new FormationPlanQueryResult(found.ToArray(), visited);
    }
    private readonly record struct Node(FormationBounds Bounds, int Left, int Right, int Entry);
}
