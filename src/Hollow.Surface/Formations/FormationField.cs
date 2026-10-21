using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

public interface IFormationSurface { FormationField? Formations { get; } }

public sealed class FormationFieldPieceQuery
{
    private readonly FormationPiece[] _pieces;
    internal FormationFieldPieceQuery(FormationQueryStatus status, FormationPiece[] pieces, int visitedNodes)
    { Status = status; _pieces = pieces; VisitedNodes = visitedNodes; }
    public FormationQueryStatus Status { get; }
    public ReadOnlySpan<FormationPiece> Pieces => _pieces;
    public int VisitedNodes { get; }
}

public sealed class FormationFieldCollisionResult
{
    internal FormationFieldCollisionResult(FormationQueryStatus status, TriangleMesh? mesh, int pieces)
    { Status = status; Mesh = mesh; Pieces = pieces; }
    public FormationQueryStatus Status { get; }
    /// <summary>Null on any incomplete outcome. No partial base/piece mesh is published.</summary>
    public TriangleMesh? Mesh { get; }
    public int Pieces { get; }
}

/// <summary>
/// Completed sealed physical snapshot. Retains no terrain, planner records, callbacks, leases, tasks or graphics.
/// ProposalDiagnostics describes the pre-seal plan only; no pre-seal coverage estimate is exposed as final coverage.
/// </summary>
public sealed class FormationField
{
    private readonly FormationPiece[] _pieces;
    private readonly FormationCohort[] _cohorts;
    private readonly FormationFieldRejection[] _rejections;
    private readonly FormationSupportIndex _support;
    internal FormationField(FormationFieldContract contract, string generationDigest, FormationPiece[] pieces,
        FormationCohort[] cohorts, FormationFieldRejection[] rejections, FormationSupportIndex support,
        FormationPlanDiagnostics proposalDiagnostics, FormationFieldDiagnostics diagnostics)
    {
        Contract = contract; GenerationDigest = generationDigest; _pieces = pieces; _cohorts = cohorts;
        _rejections = rejections; _support = support; ProposalDiagnostics = proposalDiagnostics; Diagnostics = diagnostics;
    }
    public FormationFieldContract Contract { get; }
    public string GenerationDigest { get; }
    public ReadOnlySpan<FormationPiece> Pieces => _pieces;
    public ReadOnlySpan<FormationCohort> Cohorts => _cohorts;
    public ReadOnlySpan<FormationFieldRejection> Rejections => _rejections;
    public FormationPlanDiagnostics ProposalDiagnostics { get; }
    public FormationFieldDiagnostics Diagnostics { get; }
    public FormationSupportIndexDiagnostics SupportDiagnostics => _support.Diagnostics;
    // Shared physical trace traversal; the immutable index remains owned by this field.
    internal FormationSupportIndex SupportIndex => _support;

    public static FormationField Build(IGradedTerrain graded, TerrainLattice lattice, FormationWorldSelection selection,
        FormationFieldLimits limits, CancellationToken cancellation = default) =>
        FormationFieldBuilder.Build(graded, lattice, selection, limits, cancellation);

    public FormationCohort? Cohort(FormationId id)
    {
        int low = 0, high = _cohorts.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2, order = _cohorts[middle].Id.CompareTo(id);
            if (order == 0) return _cohorts[middle];
            if (order < 0) low = middle + 1; else high = middle - 1;
        }
        return null;
    }

    /// <summary>Conservative physical AABB query, complete canonical piece order or explicitly empty failure.</summary>
    public FormationFieldPieceQuery QueryPieces(FormationBounds bounds, FormationFieldQueryLimits limits,
        CancellationToken cancellation = default)
        => QueryPieces(bounds, null, limits, cancellation);

    /// <summary>Canonical tile/margin radial footprint, including arbitrary-height walls and overhangs.</summary>
    public FormationFieldPieceQuery QueryPieces(TerrainCollisionFootprint footprint, FormationFieldQueryLimits limits,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        if (footprint.Radius != Contract.Radius || footprint.VertexLevel != Contract.FinestTileLevel + TerrainLattice.RenderCellLevel)
            throw new FormationValidationException("Collision footprint and sealed field lattice differ.");
        return QueryPieces(default, footprint, limits, cancellation);
    }

    private FormationFieldPieceQuery QueryPieces(FormationBounds bounds, TerrainCollisionFootprint? footprint,
        FormationFieldQueryLimits limits, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxVisitedNodes < 0 || limits.MaxPieces < 0 || limits.MaxPieces > 4096 || limits.MaxEstimatedBytes < 0)
            throw new FormationValidationException("Invalid field query limits.");
        int visited = 0;
        FormationFieldPieceQuery Incomplete(FormationQueryStatus status) => new(status, Array.Empty<FormationPiece>(), visited);
        if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
        if (8192L + 4L * _support.Nodes.Length + 32L * limits.MaxPieces > limits.MaxEstimatedBytes)
            return Incomplete(FormationQueryStatus.CapacityExceeded);
        var stack = new int[_support.Nodes.Length]; var found = new List<FormationPiece>(); int count = 0;
        if (stack.Length != 0) stack[count++] = 0;
        while (count > 0)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if (visited == limits.MaxVisitedNodes) return Incomplete(FormationQueryStatus.CapacityExceeded);
            visited++;
            FormationSupportIndex.Node node = _support.Nodes[stack[--count]];
            if (footprint != null ? !footprint.MayIntersect(node.Bounds) : !node.Bounds.Intersects(bounds)) continue;
            if (node.Instance < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
            if (found.Count == limits.MaxPieces) return Incomplete(FormationQueryStatus.CapacityExceeded);
            found.Add(_support.Instances[node.Instance].Piece);
        }
        found.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
        return new FormationFieldPieceQuery(FormationQueryStatus.Complete, found.ToArray(), visited);
    }

    /// <summary>Physical obstruction query includes capability-None faces. Assess habitat permissions after geometry.</summary>
    public FormationRayResult Raycast(FormationRayRequest request, FormationSupportQueryLimits limits,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (request.RequiredCapabilities != SupportCapabilities.None)
            throw new FormationValidationException("A field obstruction ray cannot filter away non-supporting physical faces.");
        return FormationSupportQueries.Raycast(_support, request, limits, cancellation);
    }

    /// <summary>
    /// Appends whole intersecting pieces to caller-owned frozen base terrain. The caller supplies a conservative
    /// BODY-SPACE volume covering the intended tile and margin, including overhangs; no centre/radius shortcut is used.
    /// No legacy geology is queried. Input mesh and bounds must remain unchanged for this operation.
    /// </summary>
    public FormationFieldCollisionResult AppendCollision(TriangleMesh baseTerrain, in ZoneFrame frame,
        FormationBounds conservativeTileVolume, FormationFieldCollisionLimits limits, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(baseTerrain); ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxVertices < 0 || limits.MaxVertices > int.MaxValue / 3 || limits.MaxTriangles < 0 ||
            limits.MaxTriangles > int.MaxValue / 3 || limits.MaxEstimatedOutputBytes < 0)
            throw new FormationValidationException("Invalid field collision limits.");
        FormationInput.Finite(frame.Origin, nameof(frame));
        FormationInput.Unit(frame.East, nameof(frame)); FormationInput.Unit(frame.Up, nameof(frame)); FormationInput.Unit(frame.South, nameof(frame));
        if ((Vector3D.Cross(frame.East, frame.Up) - frame.South).LengthSquared > 1e-20)
            throw new FormationValidationException("Collision frame must be right-handed and orthonormal.");
        FormationFieldCollisionResult Incomplete(FormationQueryStatus status) => new(status, null, 0);
        if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
        long vertices = baseTerrain.VertexCount, triangles = baseTerrain.TriangleCount;
        if (vertices > limits.MaxVertices || triangles > limits.MaxTriangles)
            return Incomplete(FormationQueryStatus.CapacityExceeded);
        FormationFieldPieceQuery query = QueryPieces(conservativeTileVolume, limits.Query, cancellation);
        if (query.Status != FormationQueryStatus.Complete) return Incomplete(query.Status);
        foreach (FormationPiece piece in query.Pieces)
        {
            vertices += piece.Geometry.VertexCount; triangles += piece.Geometry.TriangleCount;
            if (vertices > limits.MaxVertices || triangles > limits.MaxTriangles)
                return Incomplete(FormationQueryStatus.CapacityExceeded);
        }
        if (4096L + 12L * vertices + 12L * triangles > limits.MaxEstimatedOutputBytes)
            return Incomplete(FormationQueryStatus.CapacityExceeded);
        foreach (float position in baseTerrain.Positions)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if (!float.IsFinite(position)) throw new FormationValidationException("Base collision positions must be finite.");
        }
        foreach (int index in baseTerrain.Indices)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if ((uint)index >= (uint)baseTerrain.VertexCount) throw new FormationValidationException("Base collision index is outside its mesh.");
        }
        var positions = new float[checked((int)vertices * 3)]; var indices = new int[checked((int)triangles * 3)];
        baseTerrain.Positions.CopyTo(positions, 0); baseTerrain.Indices.CopyTo(indices, 0);
        int nextVertex = baseTerrain.VertexCount, nextIndex = baseTerrain.Indices.Length;
        foreach (FormationPiece piece in query.Pieces)
        {
            int offset = nextVertex;
            foreach (Vector3D vertex in piece.Geometry.Vertices)
            {
                if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
                var local = frame.ToLocal(piece.Transform.Transform(vertex));
                if (!float.IsFinite(local.X) || !float.IsFinite(local.Y) || !float.IsFinite(local.Z))
                    throw new FormationValidationException("A physical collision vertex is not representable in the supplied frame.");
                positions[3 * nextVertex] = local.X; positions[3 * nextVertex + 1] = local.Y; positions[3 * nextVertex + 2] = local.Z;
                nextVertex++;
            }
            foreach (GeometryTriangle triangle in piece.Geometry.Triangles)
            {
                if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
                indices[nextIndex++] = offset + triangle.A; indices[nextIndex++] = offset + triangle.B; indices[nextIndex++] = offset + triangle.C;
            }
        }
        if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
        return new FormationFieldCollisionResult(FormationQueryStatus.Complete, new TriangleMesh(positions, indices), query.Pieces.Length);
    }
}
