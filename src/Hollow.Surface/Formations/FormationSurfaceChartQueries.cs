using System;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

public enum FormationChartDiskQualification : byte
{
    NotEvaluated, Supported, InvalidAnchor, DefinitionMismatch, PatchMismatch, ScaleOutsideQualification,
    MissingCapabilities, BoundaryCrossing, SlopeExceeded, HeightSpreadExceeded, NumericallyUnresolved,
}

/// <summary>
/// A continuous disk in the qualified projection, centred on the selected physical triangle. Radius and height
/// spread are world metres; Up and the normal-dot policy are world-space. No volume clearance is requested.
/// </summary>
public readonly record struct FormationChartDiskRequest(SupportHit Anchor, double RadiusMetres, Vector3D Up,
    double MinimumNormalDot = 0.7, double MaximumHeightSpread = 0.25,
    SupportCapabilities RequiredCapabilities = SupportCapabilities.None)
{
    internal void Validate()
    {
        Anchor.Validate(); FormationInput.Unit(Up, nameof(Up));
        FormationInput.Capabilities(RequiredCapabilities, nameof(RequiredCapabilities));
        double squared = RadiusMetres * RadiusMetres;
        if (!double.IsFinite(RadiusMetres) || RadiusMetres <= 0 || !double.IsFinite(squared) || squared <= 0 ||
            !double.IsFinite(MinimumNormalDot) || MinimumNormalDot < 0 || MinimumNormalDot > 1 ||
            !double.IsFinite(MaximumHeightSpread) || MaximumHeightSpread < 0)
            throw new FormationValidationException("Invalid chart disk radius, slope or height policy.");
    }
}

public sealed record FormationChartQueryLimits
{
    public int MaxVisitedNodes { get; init; } = 262144;
    public int MaxComparedVertices { get; init; } = 262144;
    public int MaxComparedTriangles { get; init; } = 65536;
    public int MaxComparedPatches { get; init; } = 1024;
    public int MaxTestedTriangles { get; init; } = 65536;
    public int MaxBoundaryEdges { get; init; } = 65536;
    public int MaxPredicateTests { get; init; } = 524288;
    public long MaxEstimatedScratchBytes { get; init; } = 256L * 1024;
    public long MaxEstimatedResultBytes { get; init; } = 4096;
}

public readonly record struct FormationChartQueryDiagnostics(int VisitedNodes, int ComparedVertices,
    int ComparedTriangles, int ComparedPatches, int TestedTriangles, int BoundaryEdges, int PredicateTests,
    int OverlappingTriangles, long ReservedScratchBytes, long ReservedResultBytes);

/// <summary>
/// Continuous support within one qualified chart, with conservative whole-triangle slope/height checks.
/// This is not an overhead, intersection, or volume-clearance result. No partial anchor is published on failure.
/// </summary>
public sealed class FormationChartDiskResult
{
    internal FormationChartDiskResult(FormationQueryStatus status, FormationChartDiskQualification qualification,
        SupportHit? anchor, FormationChartQueryDiagnostics diagnostics)
    { Status = status; Qualification = qualification; Anchor = anchor; Diagnostics = diagnostics; }
    public FormationQueryStatus Status { get; }
    public FormationChartDiskQualification Qualification { get; }
    public bool Supported => Status == FormationQueryStatus.Complete && Qualification == FormationChartDiskQualification.Supported;
    public SupportHit? Anchor { get; }
    public FormationChartQueryDiagnostics Diagnostics { get; }
}

public static class FormationSurfaceChartQueries
{
    private const double MachineEpsilon = 2.2204460492503131e-16;
    // Basis and placement accept unit/orthogonal errors of at most 1e-10. This conservative factor bounds
    // their combined projection stretch and inverse placement contraction, including arithmetic roundoff.
    private const double MetricInflation = 1 + 1e-8;

    /// <summary>
    /// Tests the entire projected disk against all qualified boundary edges, including holes and every edge
    /// incident to a pinch. Radius/scale is inflated for the accepted basis/transform tolerances, chart guard,
    /// and anchor reconstruction error. Exact predicates operate on these represented chart coordinates.
    /// Every triangle touching the inflated disk contributes its full world-space vertex height interval:
    /// this can conservatively reject a disk whose clipped triangle portion would have passed. No samples,
    /// lazy callbacks, mesh copies, support recursion, or overhead/volume-clearance claims are involved.
    /// Slope and height limits include outward numerical uncertainty. Exact threshold equality (including
    /// normal dot one or zero height spread) can therefore fail conservatively; poorly conditioned normals
    /// return NumericallyUnresolved instead of granting support from an unreliable rounded direction.
    /// </summary>
    public static FormationChartDiskResult ContainsDisk(FormationSupportIndex index, FormationSurfaceChart chart,
        FormationChartDiskRequest request, FormationChartQueryLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(index); ArgumentNullException.ThrowIfNull(chart);
        request.Validate();
        var budget = new Budget(limits ?? new FormationChartQueryLimits());
        FormationChartDiskResult Incomplete(FormationQueryStatus status) =>
            new(status, FormationChartDiskQualification.NotEvaluated, null, budget.Diagnostics);
        FormationChartDiskResult Complete(FormationChartDiskQualification qualification) => cancellation.IsCancellationRequested
            ? Incomplete(FormationQueryStatus.Cancelled)
            : new(FormationQueryStatus.Complete, qualification,
                qualification == FormationChartDiskQualification.Supported ? request.Anchor : null, budget.Diagnostics);
        if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
        if (!budget.Admitted) return Incomplete(FormationQueryStatus.CapacityExceeded);

        FormationPiece? piece = null;
        // BVH leaves are spatially ordered. Do not assume identity order or trust the supplied point to cull
        // the search for its own identity. Each examined world node consumes the explicit node budget.
        foreach (FormationSupportIndex.Node node in index.Nodes)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if (!budget.Node()) return Incomplete(FormationQueryStatus.CapacityExceeded);
            if (node.Instance >= 0 && index.Instances[node.Instance].Piece.Id == request.Anchor.Primitive.Piece)
            { piece = index.Instances[node.Instance].Piece; break; }
        }
        if (piece == null) return Complete(FormationChartDiskQualification.InvalidAnchor);
        FormationGeometry geometry = piece.Geometry;
        PhysicalPrimitiveId identity = request.Anchor.Primitive;
        if (identity.GeometryKey != geometry.Key || identity.GeometryRevision != geometry.Revision ||
            (uint)identity.Triangle >= (uint)geometry.TriangleCount)
            return Complete(FormationChartDiskQualification.InvalidAnchor);
        FormationQueryStatus matchStatus = MatchDefinition(geometry, chart.Geometry, budget, cancellation, out bool matches);
        if (matchStatus != FormationQueryStatus.Complete) return Incomplete(matchStatus);
        if (!matches) return Complete(FormationChartDiskQualification.DefinitionMismatch);
        if (piece.Transform.Scale < chart.Definition.MinimumScale || piece.Transform.Scale > chart.Definition.MaximumScale)
            return Complete(FormationChartDiskQualification.ScaleOutsideQualification);

        if (!budget.Triangle()) return Incomplete(FormationQueryStatus.CapacityExceeded);
        GeometryTriangle selected = geometry.Triangles[identity.Triangle];
        SupportPatch patch = geometry.Patches[selected.Patch];
        if (selected.Patch != chart.PatchIndex || patch.Key != chart.PatchKey)
            return Complete(FormationChartDiskQualification.PatchMismatch);
        if (request.Anchor.PatchKey != patch.Key || request.Anchor.FamilyKey != geometry.FamilyKey ||
            request.Anchor.SubstrateProfileKey != patch.SubstrateProfileKey || request.Anchor.Capabilities != patch.Capabilities)
            return Complete(FormationChartDiskQualification.InvalidAnchor);
        if ((patch.Capabilities & request.RequiredCapabilities) != request.RequiredCapabilities)
            return Complete(FormationChartDiskQualification.MissingCapabilities);
        Vector3D a = piece.Transform.Transform(geometry.Vertices[selected.A]);
        Vector3D b = piece.Transform.Transform(geometry.Vertices[selected.B]);
        Vector3D c = piece.Transform.Transform(geometry.Vertices[selected.C]);
        Vector3D physicalNormal = FormationGeometryIndex.WindingNormal(a, b, c);
        Vector3D bary = request.Anchor.Barycentric;
        double firstWeight = 1 - bary.Y - bary.Z;
        if (bary.X < 0 || bary.Y < 0 || bary.Z < 0 || firstWeight < 0 ||
            Math.Abs(firstWeight - bary.X) > 1e-10 || (physicalNormal - request.Anchor.Normal).LengthSquared > 1e-20)
            return Complete(FormationChartDiskQualification.InvalidAnchor);
        // Reconstruct using edge differences, avoiding a large translated barycentric weighted sum. Real ray
        // hits need not lie exactly on their rounded world plane; bounded inconsistency enlarges the disk.
        Vector3D anchorPoint = a + (b - a) * bary.Y + (c - a) * bary.Z;
        double anchorErrorSquared = (request.Anchor.Point - anchorPoint).LengthSquared;
        double coordinateScale = Math.Max(1, Math.Max(MaximumAbs(request.Anchor.Point),
            Math.Max(MaximumAbs(a), Math.Max(MaximumAbs(b), MaximumAbs(c)))));
        double worldGuard = 128 * MachineEpsilon * coordinateScale;
        if (!double.IsFinite(anchorErrorSquared) || anchorErrorSquared > worldGuard * worldGuard)
            return Complete(FormationChartDiskQualification.InvalidAnchor);

        FormationChartPoint pa = chart.ProjectedVertices[selected.A], pb = chart.ProjectedVertices[selected.B], pc = chart.ProjectedVertices[selected.C];
        var centre = new FormationChartPoint(pa.X + (pb.X - pa.X) * bary.Y + (pc.X - pa.X) * bary.Z,
            pa.Y + (pb.Y - pa.Y) * bary.Y + (pc.Y - pa.Y) * bary.Z);
        double localCoordinateScale = Math.Max(1, Math.Max(Math.Abs(centre.X), Math.Abs(centre.Y)));
        double localGuard = chart.NumericalGuardLocal + 128 * MachineEpsilon * localCoordinateScale +
            (Math.Sqrt(anchorErrorSquared) + worldGuard) / piece.Transform.Scale;
        double radius = request.RadiusMetres / piece.Transform.Scale * MetricInflation + localGuard;
        if (!Finite(centre) || !double.IsFinite(localGuard) || localGuard < 0 || !double.IsFinite(radius) || radius <= 0)
            return Complete(FormationChartDiskQualification.NumericallyUnresolved);
        if (chart.BoundaryEdges.Length == 0 || chart.Triangles.Length == 0)
            return Complete(FormationChartDiskQualification.NumericallyUnresolved);
        FormationQueryStatus centreStatus = CentreInside(centre, pa, pb, pc, budget, cancellation, out bool centreInside);
        if (centreStatus != FormationQueryStatus.Complete) return Incomplete(centreStatus);
        if (!centreInside) return Complete(FormationChartDiskQualification.NumericallyUnresolved);

        foreach (FormationChartEdge edge in chart.BoundaryEdges)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if (!budget.Edge() || !budget.Predicate()) return Incomplete(FormationQueryStatus.CapacityExceeded);
            FormationChartPoint start = chart.ProjectedVertices[edge.A], end = chart.ProjectedVertices[edge.B];
            // The exact comparison includes endpoints and tangencies. Keeping every incident edge prevents
            // the continuous disk from slipping through admitted endpoint-only source pinches.
            if (FormationChartMath.CompareSegmentDistance(centre, start, end, radius) <= 0)
                return Complete(FormationChartDiskQualification.BoundaryCrossing);
        }

        double minimumHeight = double.PositiveInfinity, maximumHeight = double.NegativeInfinity;
        bool foundAnchor = false;
        foreach (int ordinal in chart.Triangles)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if (!budget.Triangle()) return Incomplete(FormationQueryStatus.CapacityExceeded);
            if (ordinal == identity.Triangle) foundAnchor = true;
            GeometryTriangle triangle = geometry.Triangles[ordinal];
            FormationQueryStatus overlapStatus = OverlapsDisk(centre, radius,
                chart.ProjectedVertices[triangle.A], chart.ProjectedVertices[triangle.B], chart.ProjectedVertices[triangle.C],
                budget, cancellation, out bool overlaps);
            if (overlapStatus != FormationQueryStatus.Complete) return Incomplete(overlapStatus);
            if (!overlaps) continue;
            budget.Overlap();
            Vector3D wa = piece.Transform.Transform(geometry.Vertices[triangle.A]);
            Vector3D wb = piece.Transform.Transform(geometry.Vertices[triangle.B]);
            Vector3D wc = piece.Transform.Transform(geometry.Vertices[triangle.C]);
            Vector3D normal = FormationGeometryIndex.WindingNormal(wa, wb, wc);
            double upDot = Vector3D.Dot(normal, request.Up);
            double triangleMagnitude = Math.Max(1, Math.Max(MaximumAbs(anchorPoint),
                Math.Max(MaximumAbs(wa), Math.Max(MaximumAbs(wb), MaximumAbs(wc)))));
            if (!double.IsFinite(upDot) || !TryNormalGuard(wa, wb, wc, triangleMagnitude, out double normalGuard))
                return Complete(FormationChartDiskQualification.NumericallyUnresolved);
            if (Math.BitDecrement(upDot - normalGuard) < request.MinimumNormalDot)
                return Complete(FormationChartDiskQualification.SlopeExceeded);
            double ha = Vector3D.Dot(wa - anchorPoint, request.Up);
            double hb = Vector3D.Dot(wb - anchorPoint, request.Up);
            double hc = Vector3D.Dot(wc - anchorPoint, request.Up);
            if (!double.IsFinite(ha) || !double.IsFinite(hb) || !double.IsFinite(hc))
                return Complete(FormationChartDiskQualification.NumericallyUnresolved);
            double heightMagnitude = Math.Max(Math.Abs(ha), Math.Max(Math.Abs(hb), Math.Abs(hc)));
            double heightGuard = 128 * MachineEpsilon * triangleMagnitude + 1e-9 * heightMagnitude;
            minimumHeight = Math.Min(minimumHeight, Math.BitDecrement(Math.Min(ha, Math.Min(hb, hc)) - heightGuard));
            maximumHeight = Math.Max(maximumHeight, Math.BitIncrement(Math.Max(ha, Math.Max(hb, hc)) + heightGuard));
            double spread = maximumHeight - minimumHeight;
            if (!double.IsFinite(spread)) return Complete(FormationChartDiskQualification.NumericallyUnresolved);
            if (spread > request.MaximumHeightSpread) return Complete(FormationChartDiskQualification.HeightSpreadExceeded);
        }
        if (!foundAnchor) return Complete(FormationChartDiskQualification.InvalidAnchor);
        if (budget.OverlappingTriangles == 0) return Complete(FormationChartDiskQualification.NumericallyUnresolved);
        return Complete(FormationChartDiskQualification.Supported);
    }

    private static FormationQueryStatus OverlapsDisk(FormationChartPoint centre, double radius,
        FormationChartPoint a, FormationChartPoint b, FormationChartPoint c, Budget budget,
        CancellationToken cancellation, out bool overlaps)
    {
        overlaps = false;
        FormationQueryStatus status = CentreInside(centre, a, b, c, budget, cancellation, out bool inside);
        if (status != FormationQueryStatus.Complete) return status;
        if (inside) { overlaps = true; return FormationQueryStatus.Complete; }
        // Positive chart triangles are convex. Outside their interior, a disk overlaps iff it reaches an
        // inclusive edge segment (which includes its vertices). No approximate AABB can drop a tangent.
        for (int edge = 0; edge < 3; edge++)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!budget.Predicate()) return FormationQueryStatus.CapacityExceeded;
            FormationChartPoint start = edge == 0 ? a : edge == 1 ? b : c;
            FormationChartPoint end = edge == 0 ? b : edge == 1 ? c : a;
            if (FormationChartMath.CompareSegmentDistance(centre, start, end, radius) <= 0)
            { overlaps = true; return FormationQueryStatus.Complete; }
        }
        return FormationQueryStatus.Complete;
    }

    private static FormationQueryStatus CentreInside(FormationChartPoint centre, FormationChartPoint a,
        FormationChartPoint b, FormationChartPoint c, Budget budget, CancellationToken cancellation, out bool inside)
    {
        inside = true;
        for (int edge = 0; edge < 3; edge++)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!budget.Predicate()) return FormationQueryStatus.CapacityExceeded;
            FormationChartPoint start = edge == 0 ? a : edge == 1 ? b : c;
            FormationChartPoint end = edge == 0 ? b : edge == 1 ? c : a;
            inside &= FormationChartMath.Orient(start, end, centre) >= 0;
        }
        return FormationQueryStatus.Complete;
    }

    private static FormationQueryStatus MatchDefinition(FormationGeometry actual, FormationGeometry expected,
        Budget budget, CancellationToken cancellation, out bool matches)
    {
        matches = false;
        if (ReferenceEquals(actual, expected)) { matches = true; return FormationQueryStatus.Complete; }
        if (actual.Key != expected.Key || actual.Revision != expected.Revision || actual.FamilyKey != expected.FamilyKey ||
            actual.VertexCount != expected.VertexCount || actual.TriangleCount != expected.TriangleCount || actual.PatchCount != expected.PatchCount)
            return FormationQueryStatus.Complete;
        for (int i = 0; i < actual.VertexCount; i++)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!budget.ComparedVertex()) return FormationQueryStatus.CapacityExceeded;
            if (actual.Vertices[i] != expected.Vertices[i]) return FormationQueryStatus.Complete;
        }
        for (int i = 0; i < actual.TriangleCount; i++)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!budget.ComparedTriangle()) return FormationQueryStatus.CapacityExceeded;
            if (actual.Triangles[i] != expected.Triangles[i]) return FormationQueryStatus.Complete;
        }
        for (int i = 0; i < actual.PatchCount; i++)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!budget.ComparedPatch()) return FormationQueryStatus.CapacityExceeded;
            SupportPatch a = actual.Patches[i], b = expected.Patches[i];
            if (a.Key != b.Key || a.SubstrateProfileKey != b.SubstrateProfileKey || a.Capabilities != b.Capabilities)
                return FormationQueryStatus.Complete;
        }
        matches = true;
        return FormationQueryStatus.Complete;
    }

    private static bool Finite(FormationChartPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static double MaximumAbs(Vector3D value) => Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));

    private static bool TryNormalGuard(Vector3D a, Vector3D b, Vector3D c, double coordinateMagnitude, out double guard)
    {
        guard = 0;
        Vector3D first = b - a, second = c - a;
        double firstMagnitude = MaximumAbs(first), secondMagnitude = MaximumAbs(second);
        double edgeError = 4 * MachineEpsilon * coordinateMagnitude;
        // Bound two product errors, edge-subtraction uncertainty, and the cross-component subtraction.
        // Scaling by cross magnitude detects slivers/cancellation instead of assuming every normal has
        // a fixed relative accuracy. Overflow, or an uncertainty comparable to the direction, is refused.
        double crossError = 8 * (edgeError * (firstMagnitude + secondMagnitude + edgeError) +
            MachineEpsilon * firstMagnitude * secondMagnitude);
        double largestCross = MaximumAbs(Vector3D.Cross(first, second));
        double relativeError = 4 * crossError / largestCross;
        if (!double.IsFinite(relativeError) || relativeError >= 0.25 || relativeError < 0) return false;
        guard = 2 * relativeError / (1 - relativeError) + 1e-9;
        return double.IsFinite(guard);
    }

    private sealed class Budget
    {
        // One exact predicate may use the chart helper's documented 128 KiB transient allowance; the
        // additional 32 KiB covers query state/results and ordinary arithmetic. These are estimates, not GC measurements.
        private const long Scratch = 160L * 1024;
        private const long Result = 2048;
        private readonly FormationChartQueryLimits _limits;
        private int _nodes, _vertices, _comparedTriangles, _patches, _triangles, _edges, _predicates, _overlaps;
        internal Budget(FormationChartQueryLimits limits)
        {
            if (limits.MaxVisitedNodes < 0 || limits.MaxComparedVertices < 0 || limits.MaxComparedTriangles < 0 ||
                limits.MaxComparedPatches < 0 || limits.MaxTestedTriangles < 0 || limits.MaxBoundaryEdges < 0 ||
                limits.MaxPredicateTests < 0 || limits.MaxEstimatedScratchBytes < 0 || limits.MaxEstimatedResultBytes < 0)
                throw new FormationValidationException("Invalid chart query limits.", nameof(limits));
            _limits = limits;
            Admitted = Scratch <= limits.MaxEstimatedScratchBytes && Result <= limits.MaxEstimatedResultBytes;
        }
        internal bool Admitted { get; }
        internal int OverlappingTriangles => _overlaps;
        internal bool Node() => Increment(ref _nodes, _limits.MaxVisitedNodes);
        internal bool ComparedVertex() => Increment(ref _vertices, _limits.MaxComparedVertices);
        internal bool ComparedTriangle() => Increment(ref _comparedTriangles, _limits.MaxComparedTriangles);
        internal bool ComparedPatch() => Increment(ref _patches, _limits.MaxComparedPatches);
        internal bool Triangle() => Increment(ref _triangles, _limits.MaxTestedTriangles);
        internal bool Edge() => Increment(ref _edges, _limits.MaxBoundaryEdges);
        internal bool Predicate() => Increment(ref _predicates, _limits.MaxPredicateTests);
        // Every overlap was already charged as a tested triangle, so this counter cannot overflow first.
        internal void Overlap() => _overlaps++;
        internal FormationChartQueryDiagnostics Diagnostics => new(_nodes, _vertices, _comparedTriangles, _patches,
            _triangles, _edges, _predicates, _overlaps, Scratch, Result);
        private static bool Increment(ref int count, int maximum)
        { if (count == maximum) return false; count++; return true; }
    }
}
