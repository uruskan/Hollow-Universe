using System;
using System.Numerics;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>An explicitly authored local projection basis. U cross V points toward positive support winding.</summary>
public readonly record struct FormationChartBasis
{
    public FormationChartBasis(Vector3D origin, Vector3D u, Vector3D v)
    {
        FormationInput.Finite(origin, nameof(origin));
        FormationInput.Unit(u, nameof(u)); FormationInput.Unit(v, nameof(v));
        if (Math.Abs(Vector3D.Dot(u, v)) > 1e-10)
            throw new FormationValidationException("Chart axes must be orthogonal.");
        Origin = origin; U = u; V = v; Normal = Vector3D.Cross(u, v).Normalized;
    }
    public Vector3D Origin { get; }
    public Vector3D U { get; }
    public Vector3D V { get; }
    public Vector3D Normal { get; }
    internal void Validate() => _ = new FormationChartBasis(Origin, U, V);
}

/// <summary>Represented local chart coordinates; interpolation within a triangle is barycentric.</summary>
public readonly record struct FormationChartPoint(double X, double Y);
public readonly record struct FormationChartEdge(int A, int B);

public enum FormationChartQualificationStatus : byte
{
    Qualified, CapacityExceeded, Cancelled, MissingPatch, EmptyPatch, NonPositiveProjection,
    DegenerateProjection, BoundaryCrossing, NonInjectiveProjection, NumericallyUnresolved, UnsupportedDomain,
}

public sealed record FormationChartQualificationLimits
{
    public int MaxVertices { get; init; } = 8192;
    public int MaxSourceTriangles { get; init; } = 16384;
    public int MaxSourcePatches { get; init; } = 1024;
    public int MaxPatchTriangles { get; init; } = 8192;
    public int MaxBoundaryEdges { get; init; } = 8192;
    public long MaxPairChecks { get; init; } = 64000000;
    public long MaxPredicateTests { get; init; } = 128000000;
    public long MaxEstimatedResidentBytes { get; init; } = 192L * 1024 * 1024;
    public long MaxEstimatedScratchBytes { get; init; } = 8L * 1024 * 1024;
}

public readonly record struct FormationChartQualificationDiagnostics(int ProjectedVertices, int SourceTriangles,
    int PatchTriangles, int BoundaryEdges, long PairChecks, long PredicateTests,
    long EstimatedResidentBytes, long ReservedScratchBytes);

public sealed class FormationChartQualificationResult
{
    internal FormationChartQualificationResult(FormationChartQualificationStatus status, FormationSurfaceChart? chart,
        FormationChartQualificationDiagnostics diagnostics)
    { Status = status; Chart = chart; Diagnostics = diagnostics; }
    public FormationChartQualificationStatus Status { get; }
    /// <summary>Published only after all geometric checks complete within their limits.</summary>
    public FormationSurfaceChart? Chart { get; }
    public FormationChartQualificationDiagnostics Diagnostics { get; }
}

/// <summary>
/// A qualified piecewise affine graph over explicitly projected vertex doubles. Exact predicates establish the
/// topology of those represented coordinates, not an unrounded projection. NumericalGuardLocal bounds ordinary
/// projection roundoff for conservative queries. Physical triangles and boundary edges are never welded, filled,
/// moved or simplified. Coincident endpoint-only source pinches remain boundary exclusions. Nonconforming
/// T-junctions and coincident boundary edge intervals are deliberately unsupported in this bounded implementation.
/// Qualification establishes a surface graph; it establishes no overhead or volume clearance.
/// </summary>
public sealed class FormationSurfaceChart
{
    private readonly FormationChartPoint[] _vertices;
    private readonly int[] _triangles;
    private readonly FormationChartEdge[] _edges;
    private readonly int _triangleCount;

    private FormationSurfaceChart(CliffSupportDefinition definition, int patchIndex, FormationChartBasis basis,
        FormationChartPoint[] vertices, int[] triangles, int triangleCount, FormationChartEdge[] edges,
        double numericalGuardLocal, long resident)
    {
        Definition = definition; PatchIndex = patchIndex; Basis = basis;
        _vertices = vertices; _triangles = triangles; _triangleCount = triangleCount; _edges = edges;
        NumericalGuardLocal = numericalGuardLocal; EstimatedResidentBytes = resident;
    }
    public CliffSupportDefinition Definition { get; }
    public FormationGeometry Geometry => Definition.Geometry;
    public int PatchIndex { get; }
    public string PatchKey => Geometry.Patches[PatchIndex].Key;
    public FormationChartBasis Basis { get; }
    public ReadOnlySpan<FormationChartPoint> ProjectedVertices => _vertices;
    public ReadOnlySpan<int> Triangles => _triangles.AsSpan(0, _triangleCount);
    public ReadOnlySpan<FormationChartEdge> BoundaryEdges => _edges;
    public double NumericalGuardLocal { get; }
    /// <summary>Includes the complete referenced qualified definition, even when another owner also retains it.</summary>
    public long EstimatedResidentBytes { get; }

    public static FormationChartQualificationResult Qualify(CliffSupportDefinition definition, string patchKey,
        FormationChartBasis basis, FormationChartQualificationLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(definition); FormationInput.Key(patchKey, nameof(patchKey)); basis.Validate();
        limits ??= new FormationChartQualificationLimits();
        if (limits.MaxVertices < 0 || limits.MaxSourceTriangles < 0 || limits.MaxSourcePatches < 0 ||
            limits.MaxPatchTriangles < 0 || limits.MaxBoundaryEdges < 0 || limits.MaxPairChecks < 0 ||
            limits.MaxPredicateTests < 0 || limits.MaxEstimatedResidentBytes < 0 || limits.MaxEstimatedScratchBytes < 0)
            throw new FormationValidationException("Chart qualification limits must be nonnegative.", nameof(limits));
        return new Qualification(definition, patchKey, basis, limits, cancellation).Run();
    }

    private sealed class Qualification(CliffSupportDefinition definition, string patchKey, FormationChartBasis basis,
        FormationChartQualificationLimits limits, CancellationToken cancellation)
    {
        private FormationChartPoint[] _points = Array.Empty<FormationChartPoint>();
        private int[] _triangles = Array.Empty<int>();
        private FormationChartEdge[] _edges = Array.Empty<FormationChartEdge>();
        private int _projected, _scanned, _selected, _edgeCount;
        private long _pairs, _predicates, _resident, _scratch;
        private FormationGeometry Geometry => definition.Geometry;
        private FormationChartQualificationDiagnostics Diagnostics => new(_projected, _scanned, _selected, _edgeCount,
            _pairs, _predicates, _resident, _scratch);
        private FormationChartQualificationResult Result(FormationChartQualificationStatus status, FormationSurfaceChart? chart = null) =>
            cancellation.IsCancellationRequested
                ? new(FormationChartQualificationStatus.Cancelled, null, Diagnostics)
                : new(status, chart, Diagnostics);

        internal FormationChartQualificationResult Run()
        {
            try { return Validate(); }
            catch (Stop stop) { return Result(stop.Status); }
        }

        private FormationChartQualificationResult Validate()
        {
            CheckCancellation();
            if (Geometry.VertexCount > limits.MaxVertices || Geometry.TriangleCount > limits.MaxSourceTriangles ||
                Geometry.PatchCount > limits.MaxSourcePatches || definition.Patches.Length > limits.MaxSourcePatches)
                return Result(FormationChartQualificationStatus.CapacityExceeded);
            int patchIndex = -1;
            for (int i = 0; i < Geometry.PatchCount; i++)
            {
                CheckCancellation();
                if (Geometry.Patches[i].Key == patchKey) { patchIndex = i; break; }
            }
            if (patchIndex < 0) return Result(FormationChartQualificationStatus.MissingPatch);
            CliffPatchDomain domain = definition.RequirePatch(patchKey);
            int edgeTotal = 0;
            foreach (CliffPatchBoundary loop in domain.Boundaries)
            {
                CheckCancellation();
                if (loop.VertexIndices.Length > limits.MaxBoundaryEdges - edgeTotal)
                    return Result(FormationChartQualificationStatus.CapacityExceeded);
                edgeTotal += loop.VertexIndices.Length;
            }
            int triangleCapacity = Math.Min(Geometry.TriangleCount, limits.MaxPatchTriangles);
            _resident = 4096L + definition.EstimatedResidentBytes + 16L * Geometry.VertexCount +
                4L * triangleCapacity + 8L * edgeTotal;
            // Predicates convert at most seven finite doubles at a time. No pair matrix or exact-coordinate mesh
            // is retained. Dyadic intermediates have <=~8500 bits, covered by this fixed transient reservation.
            _scratch = 160L * 1024;
            if (_resident > limits.MaxEstimatedResidentBytes || _scratch > limits.MaxEstimatedScratchBytes)
                return Result(FormationChartQualificationStatus.CapacityExceeded);
            _triangles = new int[triangleCapacity];
            for (int i = 0; i < Geometry.TriangleCount; i++)
            {
                CheckCancellation(); _scanned++;
                if (Geometry.Triangles[i].Patch != patchIndex) continue;
                if (_selected == triangleCapacity) return Result(FormationChartQualificationStatus.CapacityExceeded);
                _triangles[_selected++] = i;
            }
            if (_selected == 0) return Result(FormationChartQualificationStatus.EmptyPatch);
            if (edgeTotal == 0) return Result(FormationChartQualificationStatus.UnsupportedDomain);
            // Every unordered pair will be visited, including pairs eliminated by inclusive AABBs. Admit that
            // finite work before allocating projection arrays; detailed predicate work is charged dynamically.
            long requiredPairs = (long)_selected * (_selected - 1) / 2 + (long)edgeTotal * (edgeTotal - 1) / 2;
            if (requiredPairs > limits.MaxPairChecks) return Result(FormationChartQualificationStatus.CapacityExceeded);
            _points = new FormationChartPoint[Geometry.VertexCount];
            double magnitude = Math.Max(1, MaximumAbs(basis.Origin));
            for (int i = 0; i < _points.Length; i++)
            {
                CheckCancellation();
                Vector3D source = Geometry.Vertices[i], delta = source - basis.Origin;
                double x = Vector3D.Dot(delta, basis.U), y = Vector3D.Dot(delta, basis.V);
                if (!double.IsFinite(x) || !double.IsFinite(y)) return Result(FormationChartQualificationStatus.NumericallyUnresolved);
                _points[i] = new(x, y); _projected++;
                magnitude = Math.Max(magnitude, Math.Max(MaximumAbs(source), Math.Max(Math.Abs(x), Math.Abs(y))));
            }
            double guard = 64 * 2.2204460492503131e-16 * magnitude;
            for (int i = 0; i < _selected; i++)
            {
                GeometryTriangle triangle = Geometry.Triangles[_triangles[i]];
                int orientation = Orient(_points[triangle.A], _points[triangle.B], _points[triangle.C]);
                if (orientation == 0) return Result(FormationChartQualificationStatus.DegenerateProjection);
                if (orientation < 0) return Result(FormationChartQualificationStatus.NonPositiveProjection);
            }
            _edges = new FormationChartEdge[edgeTotal];
            foreach (CliffPatchBoundary loop in domain.Boundaries)
            {
                ReadOnlySpan<int> vertices = loop.VertexIndices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    CheckCancellation();
                    _edges[_edgeCount++] = new(vertices[i], vertices[(i + 1) % vertices.Length]);
                }
            }
            // Boundary crossings have their own diagnostic, before the more general triangle-overlap check.
            for (int i = 0; i < _edges.Length; i++)
                for (int j = i + 1; j < _edges.Length; j++)
                {
                    Pair();
                    FormationChartQualificationStatus status = CheckBoundaryPair(_edges[i], _edges[j]);
                    if (status != FormationChartQualificationStatus.Qualified) return Result(status);
                }
            for (int i = 0; i < _selected; i++)
                for (int j = i + 1; j < _selected; j++)
                {
                    Pair();
                    FormationChartQualificationStatus status = CheckTrianglePair(Geometry.Triangles[_triangles[i]], Geometry.Triangles[_triangles[j]]);
                    if (status != FormationChartQualificationStatus.Qualified) return Result(status);
                }
            CheckCancellation();
            return Result(FormationChartQualificationStatus.Qualified, new FormationSurfaceChart(definition, patchIndex,
                basis, _points, _triangles, _selected, _edges, guard, _resident));
        }

        private FormationChartQualificationStatus CheckBoundaryPair(FormationChartEdge first, FormationChartEdge second)
        {
            FormationChartPoint a = _points[first.A], b = _points[first.B], c = _points[second.A], d = _points[second.B];
            if (!BoxesMeet(a, b, c, d)) return FormationChartQualificationStatus.Qualified;
            int ac = Orient(a, b, c), ad = Orient(a, b, d), ca = Orient(c, d, a), cb = Orient(c, d, b);
            if (ac * ad < 0 && ca * cb < 0) return FormationChartQualificationStatus.BoundaryCrossing;
            if (ac == 0 && ad == 0)
            {
                bool xAxis = a.X != b.X;
                double a0 = xAxis ? a.X : a.Y, a1 = xAxis ? b.X : b.Y;
                double b0 = xAxis ? c.X : c.Y, b1 = xAxis ? d.X : d.Y;
                if (Math.Max(Math.Min(a0, a1), Math.Min(b0, b1)) < Math.Min(Math.Max(a0, a1), Math.Max(b0, b1)))
                    return FormationChartQualificationStatus.BoundaryCrossing;
            }
            FormationChartQualificationStatus contact;
            if (ac == 0 && OnSegment(c, a, b) && (contact = Contact(second.A, first.A, first.B, true)) != FormationChartQualificationStatus.Qualified) return contact;
            if (ad == 0 && OnSegment(d, a, b) && (contact = Contact(second.B, first.A, first.B, true)) != FormationChartQualificationStatus.Qualified) return contact;
            if (ca == 0 && OnSegment(a, c, d) && (contact = Contact(first.A, second.A, second.B, true)) != FormationChartQualificationStatus.Qualified) return contact;
            if (cb == 0 && OnSegment(b, c, d) && (contact = Contact(first.B, second.A, second.B, true)) != FormationChartQualificationStatus.Qualified) return contact;
            return FormationChartQualificationStatus.Qualified;
        }

        private FormationChartQualificationStatus CheckTrianglePair(GeometryTriangle first, GeometryTriangle second)
        {
            FormationChartPoint a = _points[first.A], b = _points[first.B], c = _points[first.C];
            FormationChartPoint d = _points[second.A], e = _points[second.B], f = _points[second.C];
            if (Math.Max(a.X, Math.Max(b.X, c.X)) < Math.Min(d.X, Math.Min(e.X, f.X)) ||
                Math.Max(d.X, Math.Max(e.X, f.X)) < Math.Min(a.X, Math.Min(b.X, c.X)) ||
                Math.Max(a.Y, Math.Max(b.Y, c.Y)) < Math.Min(d.Y, Math.Min(e.Y, f.Y)) ||
                Math.Max(d.Y, Math.Max(e.Y, f.Y)) < Math.Min(a.Y, Math.Min(b.Y, c.Y)))
                return FormationChartQualificationStatus.Qualified;
            // A separating axis touching at zero separates interiors, but its zero-area contacts still need
            // physical equality checks. Adjacency alone never exempts a pair from this overlap test.
            if (!Separates(a, b, d, e, f) && !Separates(b, c, d, e, f) && !Separates(c, a, d, e, f) &&
                !Separates(d, e, a, b, c) && !Separates(e, f, a, b, c) && !Separates(f, d, a, b, c))
                return FormationChartQualificationStatus.NonInjectiveProjection;
            for (int corner = 0; corner < 3; corner++)
            {
                int p = Vertex(first, corner), q = Vertex(second, corner);
                for (int edge = 0; edge < 3; edge++)
                {
                    int u = Vertex(second, edge), v = Vertex(second, (edge + 1) % 3);
                    if (Orient(_points[u], _points[v], _points[p]) == 0 && OnSegment(_points[p], _points[u], _points[v]))
                    {
                        FormationChartQualificationStatus contact = Contact(p, u, v, false);
                        if (contact != FormationChartQualificationStatus.Qualified) return contact;
                    }
                    u = Vertex(first, edge); v = Vertex(first, (edge + 1) % 3);
                    if (Orient(_points[u], _points[v], _points[q]) == 0 && OnSegment(_points[q], _points[u], _points[v]))
                    {
                        FormationChartQualificationStatus contact = Contact(q, u, v, false);
                        if (contact != FormationChartQualificationStatus.Qualified) return contact;
                    }
                }
            }
            return FormationChartQualificationStatus.Qualified;
        }

        private FormationChartQualificationStatus Contact(int point, int start, int end, bool boundary)
        {
            int endpoint = _points[point] == _points[start] ? start : _points[point] == _points[end] ? end : -1;
            if (endpoint < 0) return boundary ? FormationChartQualificationStatus.BoundaryCrossing : FormationChartQualificationStatus.UnsupportedDomain;
            return Geometry.Vertices[point] == Geometry.Vertices[endpoint]
                ? FormationChartQualificationStatus.Qualified : FormationChartQualificationStatus.NonInjectiveProjection;
        }
        private bool Separates(FormationChartPoint a, FormationChartPoint b, FormationChartPoint p, FormationChartPoint q, FormationChartPoint r) =>
            Orient(a, b, p) <= 0 && Orient(a, b, q) <= 0 && Orient(a, b, r) <= 0;
        private int Orient(FormationChartPoint a, FormationChartPoint b, FormationChartPoint c)
        {
            CheckCancellation();
            if (_predicates == limits.MaxPredicateTests) throw new Stop(FormationChartQualificationStatus.CapacityExceeded);
            _predicates++;
            return FormationChartMath.Orient(a, b, c);
        }
        private void Pair()
        {
            CheckCancellation();
            if (_pairs == limits.MaxPairChecks) throw new Stop(FormationChartQualificationStatus.CapacityExceeded);
            _pairs++;
        }
        private void CheckCancellation()
        {
            if (cancellation.IsCancellationRequested) throw new Stop(FormationChartQualificationStatus.Cancelled);
        }
        private sealed class Stop(FormationChartQualificationStatus status) : Exception
        { internal FormationChartQualificationStatus Status { get; } = status; }
        private static int Vertex(GeometryTriangle triangle, int corner) => corner == 0 ? triangle.A : corner == 1 ? triangle.B : triangle.C;
        private static double MaximumAbs(Vector3D value) => Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
        private static bool OnSegment(FormationChartPoint p, FormationChartPoint a, FormationChartPoint b) =>
            p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
        private static bool BoxesMeet(FormationChartPoint a, FormationChartPoint b, FormationChartPoint c, FormationChartPoint d) =>
            Math.Min(a.X, b.X) <= Math.Max(c.X, d.X) && Math.Min(c.X, d.X) <= Math.Max(a.X, b.X) &&
            Math.Min(a.Y, b.Y) <= Math.Max(c.Y, d.Y) && Math.Min(c.Y, d.Y) <= Math.Max(a.Y, b.Y);
    }
}

/// <summary>
/// Exact signs over finite IEEE-754 doubles, interpreted as dyadic rationals. At most seven values are expanded
/// at once; no unbounded precision input or geometry-sized exact cache. Orientation needs <=~4200-bit products;
/// segment/disk comparisons <=~8500-bit products. Callers reserve128KiB for these temporary arithmetic values.
/// </summary>
internal static class FormationChartMath
{
    internal static int Orient(FormationChartPoint a, FormationChartPoint b, FormationChartPoint c)
    {
        Span<double> values = stackalloc double[] { a.X, a.Y, b.X, b.Y, c.X, c.Y };
        int exponent = MinimumExponent(values);
        BigInteger ax = Integer(a.X, exponent), ay = Integer(a.Y, exponent);
        BigInteger bx = Integer(b.X, exponent), by = Integer(b.Y, exponent);
        BigInteger cx = Integer(c.X, exponent), cy = Integer(c.Y, exponent);
        return ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax)).Sign;
    }

    /// <summary>Sign of squared point/closed-segment distance minus radius squared. Tangency is exactly zero.</summary>
    internal static int CompareSegmentDistance(FormationChartPoint p, FormationChartPoint a, FormationChartPoint b, double radius)
    {
        Span<double> values = stackalloc double[] { p.X, p.Y, a.X, a.Y, b.X, b.Y, radius };
        int exponent = MinimumExponent(values);
        BigInteger px = Integer(p.X, exponent), py = Integer(p.Y, exponent);
        BigInteger ax = Integer(a.X, exponent), ay = Integer(a.Y, exponent);
        BigInteger bx = Integer(b.X, exponent), by = Integer(b.Y, exponent), r = Integer(radius, exponent);
        BigInteger vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
        BigInteger length = vx * vx + vy * vy, dot = wx * vx + wy * vy, radiusSquared = r * r;
        if (length.IsZero || dot.Sign <= 0) return (wx * wx + wy * wy - radiusSquared).Sign;
        if (dot >= length)
        {
            BigInteger dx = px - bx, dy = py - by;
            return (dx * dx + dy * dy - radiusSquared).Sign;
        }
        return (((wx * wx + wy * wy) - radiusSquared) * length - dot * dot).Sign;
    }

    private static int MinimumExponent(ReadOnlySpan<double> values)
    {
        int minimum = 0;
        foreach (double value in values)
        {
            if (!double.IsFinite(value)) throw new FormationValidationException("Exact chart predicates require finite coordinates.");
            if (value == 0) continue;
            int raw = (int)((BitConverter.DoubleToInt64Bits(value) >> 52) & 0x7ff);
            int exponent = raw == 0 ? -1074 : raw - 1023 - 52;
            minimum = Math.Min(minimum, exponent);
        }
        return minimum;
    }
    private static BigInteger Integer(double value, int exponent)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int raw = (int)((bits >> 52) & 0x7ff);
        long significand = bits & 0x000fffffffffffffL;
        if (raw != 0) significand |= 0x0010000000000000L;
        if (bits < 0) significand = -significand;
        int power = raw == 0 ? -1074 : raw - 1023 - 52;
        return significand == 0 ? BigInteger.Zero : new BigInteger(significand) << (power - exponent);
    }
}
