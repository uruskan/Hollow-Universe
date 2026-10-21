using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

public enum FormationQueryStatus : byte { Complete, CapacityExceeded, Cancelled }
public enum FormationBackfacePolicy : byte { FrontFacesOnly, TwoSided }

public enum FormationDiskQualification : byte
{
    NotEvaluated, Supported, InvalidAnchor, DefinitionMismatch, ScaleOutsideQualification,
    NonPlanarPatch, BoundaryCrossing, UnsupportedDomain, NumericallyUnresolved,
}

/// <summary>Finite body-space ray interval. MinDistance explicitly selects past an upper shelf.</summary>
public readonly record struct FormationRayRequest(Vector3D Origin, Vector3D Direction, double Length,
    double MinDistance = 0, FormationBackfacePolicy Backfaces = FormationBackfacePolicy.FrontFacesOnly,
    SupportCapabilities RequiredCapabilities = SupportCapabilities.None)
{
    internal void Validate()
    {
        FormationInput.Finite(Origin, nameof(Origin)); FormationInput.Unit(Direction, nameof(Direction));
        FormationInput.Capabilities(RequiredCapabilities, nameof(RequiredCapabilities));
        if (!double.IsFinite(Length) || Length <= 0 || !double.IsFinite(MinDistance) || MinDistance < 0 ||
            MinDistance > Length || !Enum.IsDefined(Backfaces))
            throw new FormationValidationException("Invalid support ray interval or backface policy.");
        FormationInput.Finite(Origin + Direction * Length, nameof(Length));
    }
}

public sealed record FormationSupportQueryLimits
{
    public int MaxVisitedNodes { get; init; } = 262144;
    public int MaxTestedTriangles { get; init; } = 131072;
    public int MaxHits { get; init; } = 4096;
    public int MaxProbes { get; init; } = 64;
    public int MaxDomainTriangles { get; init; } = 65536;
    public int MaxDomainVertices { get; init; } = 262144;
    public int MaxDomainEdges { get; init; } = 65536;
    public int MaxDomainPatches { get; init; } = 1024;
    public long MaxEstimatedScratchBytes { get; init; } = 8L * 1024 * 1024;
    public long MaxEstimatedResultBytes { get; init; } = 8L * 1024 * 1024;
}

public readonly record struct FormationQueryDiagnostics(int VisitedNodes, int TestedTriangles, int Hits,
    long ReservedScratchBytes, long ReservedResultBytes)
{
    public int DomainTriangles { get; init; }
    public int DomainVertices { get; init; }
    public int DomainEdges { get; init; }
    public int DomainPatches { get; init; }
}

/// <summary>A continuous disk in the selected physical patch's plane, with radius in world metres.</summary>
public readonly record struct FormationDiskRequest(SupportHit Anchor, double RadiusMetres)
{
    internal void Validate()
    {
        Anchor.Validate();
        double squared = RadiusMetres * RadiusMetres;
        if (!double.IsFinite(RadiusMetres) || RadiusMetres <= 0 || !double.IsFinite(squared) || squared <= 0)
            throw new FormationValidationException("Disk radius must have finite positive representable square.");
    }
}

/// <summary>Continuous planar support only. Neither this result nor sampled footprints establish volume clearance.</summary>
public sealed class FormationDiskResult
{
    internal FormationDiskResult(FormationQueryStatus status, FormationDiskQualification qualification,
        SupportHit? anchor, FormationQueryDiagnostics diagnostics)
    { Status = status; Qualification = qualification; Anchor = anchor; Diagnostics = diagnostics; }
    public FormationQueryStatus Status { get; }
    public FormationDiskQualification Qualification { get; }
    public bool Supported => Status == FormationQueryStatus.Complete && Qualification == FormationDiskQualification.Supported;
    public SupportHit? Anchor { get; }
    public FormationQueryDiagnostics Diagnostics { get; }
}

public sealed class FormationRayResult
{
    private readonly SupportHit[] _hits;
    internal FormationRayResult(FormationQueryStatus status, SupportHit[] hits, FormationQueryDiagnostics diagnostics)
    { Status = status; _hits = hits; Diagnostics = diagnostics; }
    public FormationQueryStatus Status { get; }
    public ReadOnlySpan<SupportHit> Hits => _hits;
    public FormationQueryDiagnostics Diagnostics { get; }
}

/// <summary>
/// Explicit parallel probes, in caller order. Their first eligible hits must face Up and lie within a bounded
/// height band along Up. This checks the supplied footprint samples, not unsampled area or volume clearance.
/// </summary>
public sealed class FormationFootprintRequest
{
    private readonly FormationRayRequest[] _probes;
    public FormationFootprintRequest(IEnumerable<FormationRayRequest> probes, Vector3D up,
        double minimumNormalDot = 0.7, double maximumHeightSpread = 0.25)
    {
        FormationInput.Unit(up, nameof(up));
        if (!double.IsFinite(minimumNormalDot) || minimumNormalDot < 0 || minimumNormalDot > 1 ||
            !double.IsFinite(maximumHeightSpread) || maximumHeightSpread < 0)
            throw new FormationValidationException("Invalid footprint slope or height policy.");
        _probes = FormationInput.Copy(probes, 64, nameof(probes));
        if (_probes.Length == 0) throw new FormationValidationException("A footprint needs at least one explicit probe.");
        foreach (FormationRayRequest probe in _probes)
        {
            probe.Validate();
            if ((probe.Direction + up).LengthSquared > 1e-20)
                throw new FormationValidationException("Footprint probes must point opposite the supplied up direction.");
        }
        Up = up; MinimumNormalDot = minimumNormalDot; MaximumHeightSpread = maximumHeightSpread;
    }
    public ReadOnlySpan<FormationRayRequest> Probes => _probes;
    public Vector3D Up { get; }
    public double MinimumNormalDot { get; }
    public double MaximumHeightSpread { get; }
}

public sealed class FormationFootprintResult
{
    private readonly SupportHit[] _hits;
    internal FormationFootprintResult(FormationQueryStatus status, bool supported, SupportHit[] hits,
        FormationQueryDiagnostics diagnostics)
    { Status = status; Supported = supported; _hits = hits; Diagnostics = diagnostics; }
    public FormationQueryStatus Status { get; }
    public bool Supported { get; }
    /// <summary>One selected hit per probe only when the entire footprint is supported; otherwise empty.</summary>
    public ReadOnlySpan<SupportHit> Hits => _hits;
    public FormationQueryDiagnostics Diagnostics { get; }
}

public static class FormationSupportQueries
{
    internal interface ITriangleVisitor
    {
        bool VisitNode();
        bool VisitTriangle(FormationPiece piece, int ordinal);
    }
    /// <summary>
    /// Proves containment of a disk anchored inside an indexed physical triangle. The exact qualified patch
    /// boundary, including every hole, must stay strictly farther away than the radius plus a roundoff guard.
    /// This deliberately strict chart accepts only coplanarity in the represented world coordinates; even a
    /// valid rotated ray hit can return NumericallyUnresolved after floating-point projection. Curved or
    /// numerically unresolved patches are unsupported, never approximated by a few probes. Disconnected patch
    /// components can cause conservative rejection, but cannot bridge a gap. No overhead/volume clearance is tested.
    /// </summary>
    public static FormationDiskResult ContainsDisk(FormationSupportIndex index, CliffSupportDefinition definition,
        FormationDiskRequest request, FormationSupportQueryLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(index); ArgumentNullException.ThrowIfNull(definition);
        request.Validate();
        var budget = new Budget(limits ?? new FormationSupportQueryLimits(), 0, false, disk: true);
        FormationDiskResult Incomplete(FormationQueryStatus status) => new(status, FormationDiskQualification.NotEvaluated, null, budget.Diagnostics);
        FormationDiskResult Complete(FormationDiskQualification qualification) => cancellation.IsCancellationRequested
            ? Incomplete(FormationQueryStatus.Cancelled)
            : new(FormationQueryStatus.Complete, qualification,
                qualification == FormationDiskQualification.Supported ? request.Anchor : null, budget.Diagnostics);
        if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
        if (!budget.Admitted) return Incomplete(FormationQueryStatus.CapacityExceeded);
        // Search the immutable world nodes with the same bounded traversal accounting as ray queries. Do not
        // assume spatial BVH leaf order is piece-id order or trust a caller-supplied point to cull its own identity.
        FormationPiece? piece = null;
        foreach (FormationSupportIndex.Node node in index.Nodes)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if (!budget.Node()) return Incomplete(FormationQueryStatus.CapacityExceeded);
            if (node.Instance >= 0 && index.Instances[node.Instance].Piece.Id == request.Anchor.Primitive.Piece)
            { piece = index.Instances[node.Instance].Piece; break; }
        }
        if (piece == null) return Complete(FormationDiskQualification.InvalidAnchor);
        FormationGeometry geometry = piece.Geometry;
        PhysicalPrimitiveId primitive = request.Anchor.Primitive;
        if (primitive.GeometryKey != geometry.Key || primitive.GeometryRevision != geometry.Revision ||
            (uint)primitive.Triangle >= (uint)geometry.TriangleCount)
            return Complete(FormationDiskQualification.InvalidAnchor);
        FormationQueryStatus equalityStatus = MatchDefinition(geometry, definition.Geometry, budget, cancellation, out bool matches);
        if (equalityStatus != FormationQueryStatus.Complete) return Incomplete(equalityStatus);
        if (!matches) return Complete(FormationDiskQualification.DefinitionMismatch);
        if (piece.Transform.Scale < definition.MinimumScale || piece.Transform.Scale > definition.MaximumScale)
            return Complete(FormationDiskQualification.ScaleOutsideQualification);
        if (!budget.Triangle()) return Incomplete(FormationQueryStatus.CapacityExceeded);
        GeometryTriangle selected = geometry.Triangles[primitive.Triangle];
        SupportPatch patch = geometry.Patches[selected.Patch];
        Vector3D a = piece.Transform.Transform(geometry.Vertices[selected.A]);
        Vector3D b = piece.Transform.Transform(geometry.Vertices[selected.B]);
        Vector3D c = piece.Transform.Transform(geometry.Vertices[selected.C]);
        Vector3D normal = FormationGeometryIndex.WindingNormal(a, b, c);
        if (request.Anchor.PatchKey != patch.Key || request.Anchor.FamilyKey != geometry.FamilyKey ||
            request.Anchor.SubstrateProfileKey != patch.SubstrateProfileKey || request.Anchor.Capabilities != patch.Capabilities ||
            (request.Anchor.Normal - normal).LengthSquared > 1e-20 ||
            !Barycentric(a, b, c, normal, request.Anchor.Point, out Vector3D barycentric) ||
            (request.Anchor.Barycentric - barycentric).LengthSquared > 1e-20 ||
            request.Anchor.Barycentric.X < 0 || request.Anchor.Barycentric.Y < 0 || request.Anchor.Barycentric.Z < 0)
            return Complete(FormationDiskQualification.InvalidAnchor);
        if (Vector3D.Dot(normal, request.Anchor.Point - a) != 0)
            return Complete(FormationDiskQualification.NumericallyUnresolved);
        if (!budget.Hit()) return Incomplete(FormationQueryStatus.CapacityExceeded);
        CliffPatchDomain? domain = null;
        foreach (CliffPatchDomain candidate in definition.Patches)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            if (!budget.DomainPatch()) return Incomplete(FormationQueryStatus.CapacityExceeded);
            if (candidate.PatchKey == patch.Key) { domain = candidate; break; }
        }
        if (domain == null || domain.Boundaries.Length == 0) return Complete(FormationDiskQualification.UnsupportedDomain);
        foreach (GeometryTriangle triangle in geometry.Triangles)
        {
            if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
            // Charge scans of other patch triangles too; selecting a small patch cannot hide a large mesh scan.
            if (!budget.DomainTriangle()) return Incomplete(FormationQueryStatus.CapacityExceeded);
            if (triangle.Patch != selected.Patch) continue;
            for (int corner = 0; corner < 3; corner++)
            {
                if (!budget.DomainVertex()) return Incomplete(FormationQueryStatus.CapacityExceeded);
                int vertex = corner == 0 ? triangle.A : corner == 1 ? triangle.B : triangle.C;
                Vector3D point = piece.Transform.Transform(geometry.Vertices[vertex]);
                if (Vector3D.Dot(normal, point - a) != 0) return Complete(FormationDiskQualification.NonPlanarPatch);
            }
            Vector3D triangleNormal = FormationGeometryIndex.WindingNormal(
                piece.Transform.Transform(geometry.Vertices[triangle.A]),
                piece.Transform.Transform(geometry.Vertices[triangle.B]),
                piece.Transform.Transform(geometry.Vertices[triangle.C]));
            if (Vector3D.Dot(normal, triangleNormal) < 1 - 1e-10)
                return Complete(FormationDiskQualification.UnsupportedDomain);
        }
        double coordinateScale = Math.Max(1, Math.Max(MaximumAbs(request.Anchor.Point), MaximumAbs(piece.Bounds.Extent)));
        double guardedRadius = request.RadiusMetres + 64 * 2.2204460492503131e-16 * coordinateScale;
        double radiusSquared = guardedRadius * guardedRadius;
        if (!double.IsFinite(radiusSquared)) return Complete(FormationDiskQualification.NumericallyUnresolved);
        foreach (CliffPatchBoundary boundary in domain.Boundaries)
        {
            ReadOnlySpan<int> vertices = boundary.VertexIndices;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (cancellation.IsCancellationRequested) return Incomplete(FormationQueryStatus.Cancelled);
                if (!budget.DomainEdge()) return Incomplete(FormationQueryStatus.CapacityExceeded);
                Vector3D start = piece.Transform.Transform(geometry.Vertices[vertices[i]]);
                Vector3D end = piece.Transform.Transform(geometry.Vertices[vertices[(i + 1) % vertices.Length]]);
                Vector3D edge = end - start, delta = request.Anchor.Point - start;
                double lengthSquared = edge.LengthSquared;
                if (!double.IsFinite(lengthSquared) || lengthSquared <= 0) return Complete(FormationDiskQualification.NumericallyUnresolved);
                double parameter = Vector3D.Dot(delta, edge) / lengthSquared;
                if (!double.IsFinite(parameter)) return Complete(FormationDiskQualification.NumericallyUnresolved);
                Vector3D separation = delta - edge * Math.Clamp(parameter, 0, 1);
                double distanceSquared = separation.LengthSquared;
                if (!double.IsFinite(distanceSquared)) return Complete(FormationDiskQualification.NumericallyUnresolved);
                if (distanceSquared <= radiusSquared) return Complete(FormationDiskQualification.BoundaryCrossing);
            }
        }
        return Complete(FormationDiskQualification.Supported);
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
            if (!budget.DomainVertex()) return FormationQueryStatus.CapacityExceeded;
            if (actual.Vertices[i] != expected.Vertices[i]) return FormationQueryStatus.Complete;
        }
        for (int i = 0; i < actual.TriangleCount; i++)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!budget.DomainTriangle()) return FormationQueryStatus.CapacityExceeded;
            if (actual.Triangles[i] != expected.Triangles[i]) return FormationQueryStatus.Complete;
        }
        for (int i = 0; i < actual.PatchCount; i++)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!budget.DomainPatch()) return FormationQueryStatus.CapacityExceeded;
            SupportPatch a = actual.Patches[i], b = expected.Patches[i];
            if (a.Key != b.Key || a.SubstrateProfileKey != b.SubstrateProfileKey || a.Capabilities != b.Capabilities)
                return FormationQueryStatus.Complete;
        }
        matches = true;
        return FormationQueryStatus.Complete;
    }

    private static double MaximumAbs(Vector3D value) => Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));

    public static FormationRayResult Raycast(FormationSupportIndex index, FormationRayRequest request,
        FormationSupportQueryLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        request.Validate();
        var budget = new Budget(limits ?? new FormationSupportQueryLimits(), 1, false);
        if (cancellation.IsCancellationRequested) return new(FormationQueryStatus.Cancelled, Array.Empty<SupportHit>(), budget.Diagnostics);
        if (!budget.Admitted) return new(FormationQueryStatus.CapacityExceeded, Array.Empty<SupportHit>(), budget.Diagnostics);
        var hits = new List<SupportHit>();
        FormationQueryStatus status = Trace(index, request, budget, hits, cancellation);
        return new(status, status == FormationQueryStatus.Complete ? hits.ToArray() : Array.Empty<SupportHit>(), budget.Diagnostics);
    }

    public static FormationFootprintResult Footprint(FormationSupportIndex index, FormationFootprintRequest request,
        FormationSupportQueryLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(index); ArgumentNullException.ThrowIfNull(request);
        var budget = new Budget(limits ?? new FormationSupportQueryLimits(), request.Probes.Length, true);
        if (cancellation.IsCancellationRequested) return new(FormationQueryStatus.Cancelled, false, Array.Empty<SupportHit>(), budget.Diagnostics);
        if (!budget.Admitted) return new(FormationQueryStatus.CapacityExceeded, false, Array.Empty<SupportHit>(), budget.Diagnostics);
        var selected = new SupportHit[request.Probes.Length];
        var hits = new List<SupportHit>();
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int i = 0; i < selected.Length; i++)
        {
            hits.Clear();
            FormationQueryStatus status = Trace(index, request.Probes[i], budget, hits, cancellation);
            if (status != FormationQueryStatus.Complete) return new(status, false, Array.Empty<SupportHit>(), budget.Diagnostics);
            // Do not skip a nearer incompatible slope and accidentally support through it on a lower shelf.
            if (hits.Count == 0 || Vector3D.Dot(hits[0].Normal, request.Up) < request.MinimumNormalDot)
                return new(FormationQueryStatus.Complete, false, Array.Empty<SupportHit>(), budget.Diagnostics);
            selected[i] = hits[0];
            double height = Vector3D.Dot(hits[0].Point - selected[0].Point, request.Up);
            low = Math.Min(low, height); high = Math.Max(high, height);
            if (high - low > request.MaximumHeightSpread)
                return new(FormationQueryStatus.Complete, false, Array.Empty<SupportHit>(), budget.Diagnostics);
        }
        if (cancellation.IsCancellationRequested) return new(FormationQueryStatus.Cancelled, false, Array.Empty<SupportHit>(), budget.Diagnostics);
        return new(FormationQueryStatus.Complete, true, selected, budget.Diagnostics);
    }

    private static FormationQueryStatus Trace(FormationSupportIndex index, FormationRayRequest ray, Budget budget,
        List<SupportHit> hits, CancellationToken cancellation)
    {
        var visitor = new AllHitVisitor(ray, budget, hits);
        FormationQueryStatus status = VisitTriangles(index, ray, ref visitor, cancellation);
        if (status != FormationQueryStatus.Complete) return status;
        hits.Sort(static (a, b) => { int c = a.Distance.CompareTo(b.Distance); return c != 0 ? c : a.Primitive.CompareTo(b.Primitive); });
        return cancellation.IsCancellationRequested ? FormationQueryStatus.Cancelled : FormationQueryStatus.Complete;
    }

    private readonly struct AllHitVisitor(FormationRayRequest ray, Budget budget, List<SupportHit> hits) : ITriangleVisitor
    {
        public bool VisitNode() => budget.Node();
        public bool VisitTriangle(FormationPiece piece, int ordinal)
        {
            if (!budget.Triangle()) return false;
            SupportPatch patch = piece.Geometry.Patches[piece.Geometry.Triangles[ordinal].Patch];
            if ((patch.Capabilities & ray.RequiredCapabilities) != ray.RequiredCapabilities ||
                !Intersect(piece, ordinal, ray, out SupportHit hit)) return true;
            if (!budget.Hit()) return false;
            hits.Add(hit);
            return true;
        }
    }

    // One BVH walk for all-hit support and selected nearest-obstruction queries. The latter supplies its own
    // accumulator and complete work budget: it must not abuse MaxHits=1 and fail after the second real hit.
    // Legacy queries retain their exact existing bounds/intersection arithmetic. The selected caller opts into
    // outward conservative bounds and exact triangle predicates without changing legacy support output.
    internal static FormationQueryStatus VisitTriangles<TVisitor>(FormationSupportIndex index, FormationRayRequest ray,
        ref TVisitor visitor, CancellationToken cancellation, bool conservativeBounds = false)
        where TVisitor : struct, ITriangleVisitor
    {
        ReadOnlySpan<FormationSupportIndex.Node> nodes = index.Nodes;
        Span<int> stack = stackalloc int[64]; // Median splits over int-sized arrays have depth at most 31.
        int count = 0;
        if (nodes.Length > 0) stack[count++] = 0;
        while (count > 0)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!visitor.VisitNode()) return FormationQueryStatus.CapacityExceeded;
            FormationSupportIndex.Node node = nodes[stack[--count]];
            if (!(conservativeBounds ? SelectedGroundTrace.MeetsBounds(node.Bounds.Min, node.Bounds.Max, ray) : Meets(node.Bounds, ray))) continue;
            if (node.Instance < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
            FormationQueryStatus status = TraceGeometry(index.Instances[node.Instance], ray, ref visitor, cancellation, conservativeBounds);
            if (status != FormationQueryStatus.Complete) return status;
        }
        return cancellation.IsCancellationRequested ? FormationQueryStatus.Cancelled : FormationQueryStatus.Complete;
    }

    private static FormationQueryStatus TraceGeometry<TVisitor>(FormationSupportIndex.Instance instance,
        FormationRayRequest ray, ref TVisitor visitor, CancellationToken cancellation, bool conservativeBounds)
        where TVisitor : struct, ITriangleVisitor
    {
        ReadOnlySpan<FormationGeometryIndex.Node> nodes = instance.Geometry.Nodes;
        Span<int> stack = stackalloc int[64];
        int count = 0;
        if (nodes.Length > 0) stack[count++] = 0;
        while (count > 0)
        {
            if (cancellation.IsCancellationRequested) return FormationQueryStatus.Cancelled;
            if (!visitor.VisitNode()) return FormationQueryStatus.CapacityExceeded;
            FormationGeometryIndex.Node node = nodes[stack[--count]];
            // Transform local node bounds on demand, not triangles at index construction. Using world bounds
            // also avoids false-negative culls from inverse transforms of nearly orthonormal finite bases.
            if (!MeetsTransformedBounds(node.Bounds, instance.Piece.Transform, ray, conservativeBounds)) continue;
            if (node.Triangle < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
            if (!visitor.VisitTriangle(instance.Piece, node.Triangle)) return FormationQueryStatus.CapacityExceeded;
        }
        return FormationQueryStatus.Complete;
    }

    private static bool MeetsTransformedBounds(FormationBounds bounds, FormationTransform transform, FormationRayRequest ray,
        bool conservativeBounds)
    {
        Vector3D low = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vector3D high = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3D point = transform.Transform(new Vector3D((corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z));
            // A local AABB corner can overflow although its source triangle vertices do not. In that case
            // retain the candidate and let the bounded physical triangle test decide.
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)) return true;
            low = FormationBounds.Lower(low, point); high = FormationBounds.Upper(high, point);
        }
        return conservativeBounds ? SelectedGroundTrace.MeetsBounds(low, high, ray) : Meets(low, high, ray);
    }

    private static bool Meets(FormationBounds bounds, FormationRayRequest ray) => Meets(bounds.Min, bounds.Max, ray);

    private static bool Meets(Vector3D lower, Vector3D upper, FormationRayRequest ray)
    {
        double low = ray.MinDistance, high = ray.Length;
        for (int axis = 0; axis < 3; axis++)
        {
            double origin = Component(ray.Origin, axis), direction = Component(ray.Direction, axis);
            double min = Component(lower, axis), max = Component(upper, axis);
            if (direction == 0) { if (origin < min || origin > max) return false; continue; }
            double a = (min - origin) / direction, b = (max - origin) / direction;
            low = Math.Max(low, Math.Min(a, b)); high = Math.Min(high, Math.Max(a, b));
            if (low > high) return false;
        }
        return true;
    }

    private static bool Intersect(FormationPiece piece, int ordinal, FormationRayRequest ray, out SupportHit hit)
    {
        hit = default;
        FormationGeometry geometry = piece.Geometry;
        GeometryTriangle triangle = geometry.Triangles[ordinal];
        Vector3D a = piece.Transform.Transform(geometry.Vertices[triangle.A]);
        Vector3D b = piece.Transform.Transform(geometry.Vertices[triangle.B]);
        Vector3D c = piece.Transform.Transform(geometry.Vertices[triangle.C]);
        Vector3D normal = FormationGeometryIndex.WindingNormal(a, b, c);
        double denominator = Vector3D.Dot(normal, ray.Direction);
        if (denominator == 0 || (ray.Backfaces == FormationBackfacePolicy.FrontFacesOnly && denominator >= 0)) return false;
        double distance = Vector3D.Dot(normal, a - ray.Origin) / denominator;
        if (!double.IsFinite(distance) || distance < ray.MinDistance || distance > ray.Length) return false;
        Vector3D point = ray.Origin + ray.Direction * distance;
        if (!Barycentric(a, b, c, normal, point, out Vector3D barycentric)) return false;
        SupportPatch patch = geometry.Patches[triangle.Patch];
        hit = new SupportHit(piece.PrimitiveId(ordinal), point, normal, barycentric, distance,
            patch.Key, geometry.FamilyKey, patch.SubstrateProfileKey, patch.Capabilities);
        return true;
    }

    private static bool Barycentric(Vector3D a, Vector3D b, Vector3D c, Vector3D n, Vector3D point, out Vector3D barycentric)
    {
        barycentric = default;
        int omit = Math.Abs(n.X) >= Math.Abs(n.Y) && Math.Abs(n.X) >= Math.Abs(n.Z) ? 0 : Math.Abs(n.Y) >= Math.Abs(n.Z) ? 1 : 2;
        int x = (omit + 1) % 3, y = (omit + 2) % 3;
        double Cross(Vector3D left, Vector3D right) => Component(left, x) * Component(right, y) - Component(left, y) * Component(right, x);
        double area = Cross(b - a, c - a);
        double v = Cross(point - a, c - a) / area;
        double w = Cross(b - a, point - a) / area;
        double u = 1 - v - w;
        // Closed triangle edges are intentional: coincident edge hits retain distinct canonical primitive IDs.
        if (!double.IsFinite(u) || !double.IsFinite(v) || !double.IsFinite(w) || u < 0 || v < 0 || w < 0) return false;
        barycentric = new Vector3D(u, v, w);
        return true;
    }

    private static double Component(Vector3D value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

    private sealed class Budget
    {
        private readonly FormationSupportQueryLimits _limits;
        private int _nodes, _triangles, _hits, _domainTriangles, _domainVertices, _domainEdges, _domainPatches;
        private readonly long _scratch, _result;
        internal Budget(FormationSupportQueryLimits limits, int probes, bool footprint, bool disk = false)
        {
            if (limits.MaxVisitedNodes < 0 || limits.MaxTestedTriangles < 0 || limits.MaxHits < 0 || limits.MaxProbes < 0 ||
                limits.MaxDomainTriangles < 0 || limits.MaxDomainVertices < 0 || limits.MaxDomainEdges < 0 || limits.MaxDomainPatches < 0 ||
                limits.MaxEstimatedScratchBytes < 0 || limits.MaxEstimatedResultBytes < 0)
                throw new FormationValidationException("Invalid support query limits.", nameof(limits));
            _limits = limits;
            _scratch = disk ? 4096L : 4096L + 1024L * limits.MaxHits + 512L * probes;
            // Results can outlive the index and retain four bounded key strings per hit. Scratch shares those
            // strings while tracing; its estimate covers list growth, struct copies and the final result array.
            _result = 256L + 1536L * (disk ? 1 : footprint ? probes : limits.MaxHits);
            Admitted = probes <= limits.MaxProbes && _scratch <= limits.MaxEstimatedScratchBytes && _result <= limits.MaxEstimatedResultBytes;
        }
        internal bool Admitted { get; }
        internal bool Node() { if (_nodes == _limits.MaxVisitedNodes) return false; _nodes++; return true; }
        internal bool Triangle() { if (_triangles == _limits.MaxTestedTriangles) return false; _triangles++; return true; }
        internal bool Hit() { if (_hits == _limits.MaxHits) return false; _hits++; return true; }
        internal bool DomainTriangle()
        {
            if (_domainTriangles == _limits.MaxDomainTriangles || !Triangle()) return false;
            _domainTriangles++; return true;
        }
        internal bool DomainVertex() { if (_domainVertices == _limits.MaxDomainVertices) return false; _domainVertices++; return true; }
        internal bool DomainEdge() { if (_domainEdges == _limits.MaxDomainEdges) return false; _domainEdges++; return true; }
        internal bool DomainPatch() { if (_domainPatches == _limits.MaxDomainPatches) return false; _domainPatches++; return true; }
        internal FormationQueryDiagnostics Diagnostics => new(_nodes, _triangles, _hits, _scratch, _result)
        {
            DomainTriangles = _domainTriangles, DomainVertices = _domainVertices,
            DomainEdges = _domainEdges, DomainPatches = _domainPatches,
        };
    }
}
