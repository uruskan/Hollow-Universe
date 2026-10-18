using System;
using System.Numerics;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// An actual fixed terrain triangle and its canonical address. The caller supplies the frozen collision/render
/// positions; identity validation does not authenticate those positions against a surface or asset.
/// </summary>
public readonly record struct TerrainSealTriangle(TerrainLatticeTriangle Primitive, Vector3D A, Vector3D B, Vector3D C);

public enum FormationTerrainSealRejection : byte
{
    None, InvalidLoop, InvalidTriangle, DuplicatePrimitive, IncompleteCoverage,
}

public sealed record FormationTerrainSealLimits
{
    public int MaxLoopVertices { get; init; } = 512;
    public int MaxTerrainTriangles { get; init; } = 2048;
    public int MaxSegmentTriangleAttempts { get; init; } = 65536;
    public int MaxExactArithmeticOperations { get; init; } = 8 * 1024 * 1024;
    public int MaxIntegerBits { get; init; } = 8192;
    public int MaxDependencyTriangles { get; init; } = 2048;
    public long MaxEstimatedScratchBytes { get; init; } = 32L * 1024 * 1024;
    public long MaxEstimatedResultBytes { get; init; } = 512L * 1024;
}

public readonly record struct FormationTerrainSealDiagnostics(int LoopVertices, int TerrainTriangles,
    int CompletedSegments, int SegmentTriangleAttempts, int ExactArithmeticOperations, int PeakIntegerBits,
    int PeakIntervals, int DependencyTriangles, long EstimatedScratchBytes, long EstimatedResultBytes);

public sealed class FormationTerrainSealResult
{
    private readonly TerrainLatticeTriangle[] _dependencies;
    internal FormationTerrainSealResult(FormationQueryStatus status, FormationTerrainSealRejection rejection,
        int failedSegment, TerrainLatticeTriangle[] dependencies, FormationTerrainSealDiagnostics diagnostics)
    { Status = status; Rejection = rejection; FailedSegment = failedSegment; _dependencies = dependencies; Diagnostics = diagnostics; }
    public FormationQueryStatus Status { get; }
    public FormationTerrainSealRejection Rejection { get; }
    public bool IsSealed => Status == FormationQueryStatus.Complete && Rejection == FormationTerrainSealRejection.None;
    /// <summary>Zero-based implicit-loop edge, or -1 when no specific edge failed.</summary>
    public int FailedSegment { get; }
    /// <summary>Only a complete seal publishes its deterministic covering-triangle set.</summary>
    public ReadOnlySpan<TerrainLatticeTriangle> Dependencies => _dependencies;
    public FormationTerrainSealDiagnostics Diagnostics { get; }
}

/// <summary>
/// Continuous burial of a closed polyline in a finite union of fixed radial terrain solids. All coordinates are
/// interpreted as exact dyadic rationals. Each solid is the tetrahedron (origin,A,B,C), with its outward top plane
/// moved inward by burial*L1(normal). This guarantees at least the requested perpendicular triangle-plane burial,
/// conservatively up to sqrt(3) stronger. It is not a terrain signed-distance, clearance-ball, manifold, fit,
/// coverage-area, source-authentication or arbitrary camera-morph certificate.
/// </summary>
public static class FormationTerrainSeal
{
    public const string Contract = "fixed-radial-terrain-loop-seal-l1-v1";
    public const int MaximumLoopVertices = 512;
    public const int MaximumTerrainTriangles = 8192;
    public const int MaximumAttempts = 65536;
    public const int MaximumArithmeticOperations = 16 * 1024 * 1024;
    public const int MaximumIntegerBits = 8192;
    public const double MaximumCoordinateMetres = 1_000_000;

    /// <summary>
    /// The last loop point joins the first; omit a repeated closing point. Inputs remain frozen for the call.
    /// The result is all-or-nothing. Every segment/triangle pair consumes the same aggregate attempt budget;
    /// no floating-point prefilter can omit a possible interval. No surface callback or exact geometry cache is retained.
    /// </summary>
    public static FormationTerrainSealResult Evaluate(ReadOnlySpan<Vector3D> loop,
        ReadOnlySpan<TerrainSealTriangle> terrain, double minimumPlaneBurialMetres,
        FormationTerrainSealLimits? limits = null, CancellationToken cancellation = default)
    {
        limits ??= new FormationTerrainSealLimits();
        var budget = new Budget(limits, loop.Length, terrain.Length, cancellation);
        FormationTerrainSealResult Reject(FormationTerrainSealRejection reason, int segment = -1) =>
            Result(FormationQueryStatus.Complete, reason, segment, budget);
        try
        {
            budget.CheckCancellation();
            if (!budget.Admitted) return Result(FormationQueryStatus.CapacityExceeded, FormationTerrainSealRejection.None, -1, budget);
            if (loop.Length < 3 || !double.IsFinite(minimumPlaneBurialMetres) || minimumPlaneBurialMetres < 0 ||
                minimumPlaneBurialMetres > MaximumCoordinateMetres)
                return Reject(FormationTerrainSealRejection.InvalidLoop);
            for (int i = 0; i < loop.Length; i++)
            {
                budget.CheckCancellation();
                if (!FiniteBounded(loop[i]) || loop[i] == Vector3D.Zero || loop[i] == loop[(i + 1) % loop.Length])
                    return Reject(FormationTerrainSealRejection.InvalidLoop, i);
            }

            var triangles = terrain.ToArray();
            var exact = new Exact(budget);
            for (int i = 0; i < triangles.Length; i++)
            {
                budget.CheckCancellation();
                TerrainSealTriangle triangle = triangles[i];
                if (!ValidPrimitive(triangle.Primitive) || !FiniteBounded(triangle.A) || !FiniteBounded(triangle.B) || !FiniteBounded(triangle.C) ||
                    !Outward(triangle, exact)) return Reject(FormationTerrainSealRejection.InvalidTriangle);
            }
            Array.Sort(triangles, static (a, b) => ComparePrimitive(a.Primitive, b.Primitive));
            budget.CheckCancellation();
            for (int i = 1; i < triangles.Length; i++)
                if (ComparePrimitive(triangles[i - 1].Primitive, triangles[i].Primitive) == 0)
                    return Reject(FormationTerrainSealRejection.DuplicatePrimitive);

            var intervals = new Interval[triangles.Length];
            var sortScratch = new Interval[triangles.Length];
            var used = new bool[triangles.Length];
            for (int segment = 0; segment < loop.Length; segment++)
            {
                budget.CheckCancellation();
                int count = 0;
                for (int i = 0; i < triangles.Length; i++)
                {
                    budget.Attempt();
                    if (Clip(loop[segment], loop[(segment + 1) % loop.Length], triangles[i], minimumPlaneBurialMetres,
                        exact, out Fraction low, out Fraction high))
                        intervals[count++] = new Interval(low, high, i);
                }
                budget.PeakIntervals = Math.Max(budget.PeakIntervals, count);
                Sort(intervals, sortScratch, count, exact);
                if (!Cover(intervals, count, used, exact, budget))
                    return Reject(FormationTerrainSealRejection.IncompleteCoverage, segment);
                budget.CompletedSegments++;
                Array.Clear(intervals, 0, count);
                Array.Clear(sortScratch, 0, count);
            }

            var dependencies = new TerrainLatticeTriangle[budget.Dependencies];
            int next = 0;
            for (int i = 0; i < triangles.Length; i++)
                if (used[i]) dependencies[next++] = triangles[i].Primitive;
            budget.CheckCancellation();
            return new FormationTerrainSealResult(FormationQueryStatus.Complete, FormationTerrainSealRejection.None,
                -1, dependencies, budget.Diagnostics);
        }
        catch (Stop stopped)
        {
            return Result(stopped.Status, FormationTerrainSealRejection.None, -1, budget);
        }
    }

    private static FormationTerrainSealResult Result(FormationQueryStatus status, FormationTerrainSealRejection rejection,
        int segment, Budget budget) => new(status, rejection, segment, Array.Empty<TerrainLatticeTriangle>(), budget.Diagnostics);

    private static bool FiniteBounded(Vector3D value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z) &&
        Math.Abs(value.X) <= MaximumCoordinateMetres && Math.Abs(value.Y) <= MaximumCoordinateMetres && Math.Abs(value.Z) <= MaximumCoordinateMetres;

    private static bool ValidPrimitive(TerrainLatticeTriangle triangle)
    {
        if ((uint)triangle.Half > 1) return false;
        TileKey cell = triangle.Cell;
        CubeVertexKey a = CubeTopology.VertexKey(cell.Face, cell.Level, cell.X, cell.Y);
        CubeVertexKey d = CubeTopology.VertexKey(cell.Face, cell.Level, cell.X + 1, cell.Y + 1);
        return triangle.A == a && (triangle.Half == 0
            ? triangle.B == CubeTopology.VertexKey(cell.Face, cell.Level, cell.X + 1, cell.Y) && triangle.C == d
            : triangle.B == d && triangle.C == CubeTopology.VertexKey(cell.Face, cell.Level, cell.X, cell.Y + 1));
    }

    private static int ComparePrimitive(TerrainLatticeTriangle a, TerrainLatticeTriangle b)
    { int first = a.Cell.Packed.CompareTo(b.Cell.Packed); return first != 0 ? first : a.Half.CompareTo(b.Half); }

    private static bool Outward(TerrainSealTriangle triangle, Exact exact)
    {
        int exponent = MinimumExponent(stackalloc double[] { triangle.A.X, triangle.A.Y, triangle.A.Z,
            triangle.B.X, triangle.B.Y, triangle.B.Z, triangle.C.X, triangle.C.Y, triangle.C.Z });
        BigVector a = exact.Vector(triangle.A, exponent), b = exact.Vector(triangle.B, exponent), c = exact.Vector(triangle.C, exponent);
        return exact.Dot(exact.Cross(a, b), c).Sign > 0;
    }

    private static bool Clip(Vector3D start, Vector3D end, TerrainSealTriangle triangle, double burial, Exact exact,
        out Fraction low, out Fraction high)
    {
        int exponent = MinimumExponent(stackalloc double[] { start.X, start.Y, start.Z, end.X, end.Y, end.Z,
            triangle.A.X, triangle.A.Y, triangle.A.Z, triangle.B.X, triangle.B.Y, triangle.B.Z,
            triangle.C.X, triangle.C.Y, triangle.C.Z, burial });
        BigVector a = exact.Vector(triangle.A, exponent), b = exact.Vector(triangle.B, exponent), c = exact.Vector(triangle.C, exponent);
        BigVector p = exact.Vector(start, exponent), q = exact.Vector(end, exponent);
        low = Fraction.Zero; high = Fraction.One;
        BigVector ab = exact.Cross(a, b), bc = exact.Cross(b, c), ca = exact.Cross(c, a);
        if (!HalfSpace(exact.Dot(ab, p), exact.Dot(ab, q), ref low, ref high, exact) ||
            !HalfSpace(exact.Dot(bc, p), exact.Dot(bc, q), ref low, ref high, exact) ||
            !HalfSpace(exact.Dot(ca, p), exact.Dot(ca, q), ref low, ref high, exact)) return false;
        BigVector normal = exact.Cross(exact.Subtract(b, a), exact.Subtract(c, a));
        BigInteger norm1 = exact.Add(exact.Add(exact.Abs(normal.X), exact.Abs(normal.Y)), exact.Abs(normal.Z));
        // Every coordinate and burial share 2^exponent, so all four dot/offset terms share 2^(3*exponent).
        BigInteger top = exact.Subtract(exact.Dot(normal, a), exact.Multiply(exact.Integer(burial, exponent), norm1));
        return HalfSpace(exact.Subtract(top, exact.Dot(normal, p)), exact.Subtract(top, exact.Dot(normal, q)), ref low, ref high, exact);
    }

    // Exact closed half-space f(t)>=0. Rational endpoints need no rounding, epsilon or normalization.
    private static bool HalfSpace(BigInteger start, BigInteger end, ref Fraction low, ref Fraction high, Exact exact)
    {
        if (start.Sign < 0 && end.Sign < 0) return false;
        if (start.Sign >= 0 && end.Sign >= 0) return true;
        if (start.Sign < 0)
        {
            var crossing = new Fraction(exact.Negate(start), exact.Subtract(end, start));
            if (exact.Compare(crossing, low) > 0) low = crossing;
        }
        else
        {
            var crossing = new Fraction(start, exact.Subtract(start, end));
            if (exact.Compare(crossing, high) < 0) high = crossing;
        }
        return exact.Compare(low, high) <= 0;
    }

    private static bool Cover(Interval[] intervals, int count, bool[] used, Exact exact, Budget budget)
    {
        Fraction covered = Fraction.Zero;
        int scan = 0;
        while (exact.Compare(covered, Fraction.One) < 0)
        {
            Fraction farthest = covered;
            int selected = -1;
            while (scan < count && exact.Compare(intervals[scan].Low, covered) <= 0)
            {
                Interval interval = intervals[scan++];
                int further = exact.Compare(interval.High, farthest);
                if (further > 0 || (further == 0 && selected >= 0 && interval.Triangle < selected))
                { farthest = interval.High; selected = interval.Triangle; }
            }
            if (selected < 0 || exact.Compare(farthest, covered) <= 0) return false;
            if (!used[selected]) { budget.Dependency(); used[selected] = true; }
            covered = farthest;
        }
        return true;
    }

    // Bounded stable mergesort permits cancellation/arithmetic limits during comparisons without comparer exception wrapping.
    private static void Sort(Interval[] values, Interval[] scratch, int count, Exact exact)
    {
        for (int width = 1; width < count; width *= 2)
        {
            for (int first = 0; first < count; first += 2 * width)
            {
                int middle = Math.Min(first + width, count), end = Math.Min(first + 2 * width, count);
                int left = first, right = middle;
                for (int destination = first; destination < end; destination++)
                {
                    bool takeLeft = right == end || (left < middle && Compare(values[left], values[right], exact) <= 0);
                    scratch[destination] = takeLeft ? values[left++] : values[right++];
                }
            }
            Array.Copy(scratch, values, count);
        }
    }

    private static int Compare(Interval a, Interval b, Exact exact)
    {
        int order = exact.Compare(a.Low, b.Low);
        if (order == 0) order = exact.Compare(b.High, a.High);
        return order != 0 ? order : a.Triangle.CompareTo(b.Triangle);
    }

    private readonly record struct Fraction(BigInteger Numerator, BigInteger Denominator)
    {
        internal static Fraction Zero => new(BigInteger.Zero, BigInteger.One);
        internal static Fraction One => new(BigInteger.One, BigInteger.One);
    }
    private readonly record struct Interval(Fraction Low, Fraction High, int Triangle);
    private readonly record struct BigVector(BigInteger X, BigInteger Y, BigInteger Z);

    private static int MinimumExponent(ReadOnlySpan<double> values)
    {
        int minimum = 0;
        foreach (double value in values)
        {
            if (value == 0) continue;
            int raw = (int)((BitConverter.DoubleToInt64Bits(value) >> 52) & 0x7ff);
            minimum = Math.Min(minimum, raw == 0 ? -1074 : raw - 1023 - 52);
        }
        return minimum;
    }

    private sealed class Exact(Budget budget)
    {
        internal BigInteger Integer(double value, int exponent)
        {
            budget.Arithmetic();
            long bits = BitConverter.DoubleToInt64Bits(value);
            int raw = (int)((bits >> 52) & 0x7ff);
            long significand = bits & 0x000fffffffffffffL;
            if (raw != 0) significand |= 0x0010000000000000L;
            if (bits < 0) significand = -significand;
            int power = raw == 0 ? -1074 : raw - 1023 - 52;
            return budget.Bits(significand == 0 ? BigInteger.Zero : new BigInteger(significand) << (power - exponent));
        }
        internal BigVector Vector(Vector3D value, int exponent) => new(Integer(value.X, exponent), Integer(value.Y, exponent), Integer(value.Z, exponent));
        internal BigInteger Add(BigInteger a, BigInteger b) { budget.Arithmetic(); return budget.Bits(a + b); }
        internal BigInteger Subtract(BigInteger a, BigInteger b) { budget.Arithmetic(); return budget.Bits(a - b); }
        internal BigInteger Multiply(BigInteger a, BigInteger b) { budget.Arithmetic(); return budget.Bits(a * b); }
        internal BigInteger Abs(BigInteger a) { budget.Arithmetic(); return budget.Bits(BigInteger.Abs(a)); }
        internal BigInteger Negate(BigInteger a) { budget.Arithmetic(); return budget.Bits(-a); }
        internal BigVector Subtract(BigVector a, BigVector b) => new(Subtract(a.X, b.X), Subtract(a.Y, b.Y), Subtract(a.Z, b.Z));
        internal BigVector Cross(BigVector a, BigVector b) => new(Subtract(Multiply(a.Y, b.Z), Multiply(a.Z, b.Y)),
            Subtract(Multiply(a.Z, b.X), Multiply(a.X, b.Z)), Subtract(Multiply(a.X, b.Y), Multiply(a.Y, b.X)));
        internal BigInteger Dot(BigVector a, BigVector b) => Add(Add(Multiply(a.X, b.X), Multiply(a.Y, b.Y)), Multiply(a.Z, b.Z));
        internal int Compare(Fraction a, Fraction b)
        {
            BigInteger first = Multiply(a.Numerator, b.Denominator), second = Multiply(b.Numerator, a.Denominator);
            budget.Arithmetic();
            return first.CompareTo(second);
        }
    }

    private sealed class Budget
    {
        private readonly FormationTerrainSealLimits _limits;
        private readonly CancellationToken _cancellation;
        private readonly int _loop, _triangles;
        private readonly long _scratch, _result;
        private int _attempts, _arithmetic, _bits;
        internal Budget(FormationTerrainSealLimits limits, int loop, int triangles, CancellationToken cancellation)
        {
            if (limits.MaxLoopVertices < 0 || limits.MaxLoopVertices > MaximumLoopVertices ||
                limits.MaxTerrainTriangles < 0 || limits.MaxTerrainTriangles > MaximumTerrainTriangles ||
                limits.MaxSegmentTriangleAttempts < 0 || limits.MaxSegmentTriangleAttempts > MaximumAttempts ||
                limits.MaxExactArithmeticOperations < 0 || limits.MaxExactArithmeticOperations > MaximumArithmeticOperations ||
                limits.MaxIntegerBits < 1 || limits.MaxIntegerBits > MaximumIntegerBits ||
                limits.MaxDependencyTriangles < 0 || limits.MaxDependencyTriangles > MaximumTerrainTriangles ||
                limits.MaxEstimatedScratchBytes < 0 || limits.MaxEstimatedResultBytes < 0)
                throw new FormationValidationException("Invalid continuous terrain seal limits.");
            _limits = limits; _loop = loop; _triangles = triangles; _cancellation = cancellation;
            // Four exact endpoint integers per interval, two struct buffers sharing immutable integer magnitudes,
            // copied source triangles, dependency flags/results, and bounded exact-operation temporaries.
            // These are conservative retained/scratch reservations, not a measurement of cumulative GC allocations.
            _scratch = 512L * 1024 + 64L * loop + (512L + 4L * (64 + (limits.MaxIntegerBits + 7) / 8)) * triangles;
            _result = 4096L + 160L * triangles;
            Admitted = loop <= limits.MaxLoopVertices && triangles <= limits.MaxTerrainTriangles &&
                _scratch <= limits.MaxEstimatedScratchBytes && _result <= limits.MaxEstimatedResultBytes;
        }
        internal bool Admitted { get; }
        internal int CompletedSegments { get; set; }
        internal int PeakIntervals { get; set; }
        internal int Dependencies { get; private set; }
        internal void CheckCancellation() { if (_cancellation.IsCancellationRequested) throw new Stop(FormationQueryStatus.Cancelled); }
        internal void Attempt()
        { CheckCancellation(); if (_attempts == _limits.MaxSegmentTriangleAttempts) throw new Stop(FormationQueryStatus.CapacityExceeded); _attempts++; }
        internal void Arithmetic()
        { CheckCancellation(); if (_arithmetic == _limits.MaxExactArithmeticOperations) throw new Stop(FormationQueryStatus.CapacityExceeded); _arithmetic++; }
        internal BigInteger Bits(BigInteger value)
        {
            long bits = BigInteger.Abs(value).GetBitLength();
            _bits = Math.Max(_bits, (int)bits);
            if (bits > _limits.MaxIntegerBits) throw new Stop(FormationQueryStatus.CapacityExceeded);
            return value;
        }
        internal void Dependency()
        { if (Dependencies == _limits.MaxDependencyTriangles) throw new Stop(FormationQueryStatus.CapacityExceeded); Dependencies++; }
        internal FormationTerrainSealDiagnostics Diagnostics => new(_loop, _triangles, CompletedSegments, _attempts,
            _arithmetic, _bits, PeakIntervals, Dependencies, _scratch, _result);
    }

    private sealed class Stop(FormationQueryStatus status) : Exception
    { internal FormationQueryStatus Status { get; } = status; }
}
