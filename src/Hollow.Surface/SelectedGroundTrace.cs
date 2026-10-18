using System;
using System.Numerics;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

public enum SelectedGroundTraceStatus : byte { Complete, CapacityExceeded, Cancelled, NumericallyUnresolved, Unsupported }
// Equal exact distances prefer an explicit physical object over base terrain. This is obstruction
// provenance, not permission to walk, plant or build. No epsilon comparator is used.
public enum GroundPrimitiveKind : byte { Formation, Monument, Terrain }

public readonly record struct GroundPrimitive(GroundPrimitiveKind Kind, TileKey Cell, int Triangle,
    PhysicalPrimitiveId Formation, string? MonumentKey) : IComparable<GroundPrimitive>
{
    public int CompareTo(GroundPrimitive other)
    {
        int order = Kind.CompareTo(other.Kind);
        if (order != 0) return order;
        if (Kind == GroundPrimitiveKind.Formation) return Formation.CompareTo(other.Formation);
        order = Cell.Packed.CompareTo(other.Cell.Packed);
        if (order == 0) order = StringComparer.Ordinal.Compare(MonumentKey, other.MonumentKey);
        return order != 0 ? order : Triangle.CompareTo(other.Triangle);
    }
}

/// <summary>Body-space evidence. Normal is the original winding normal, including a back-face hit.</summary>
public readonly record struct SelectedGroundHit(GroundPrimitive Primitive, double Distance, Vector3D Point,
    Vector3D Normal, Vector3D Barycentric, bool FrontFace, SupportHit? FormationSupport);

/// <summary>A finite centreline ray. Positive radii explicitly return Unsupported; this is not a swept sphere.</summary>
public readonly record struct SelectedGroundTraceRequest(Vector3D Origin, Vector3D Direction, double Length,
    double MinDistance = 0, FormationBackfacePolicy Backfaces = FormationBackfacePolicy.TwoSided, double Radius = 0)
{
    internal FormationRayRequest Ray
    {
        get
        {
            if (!double.IsFinite(Radius) || Radius < 0) throw new FormationValidationException("Invalid trace radius.");
            var ray = new FormationRayRequest(Origin, Direction, Length, MinDistance, Backfaces);
            ray.Validate();
            return ray;
        }
    }
}

/// <summary>Explicit per-operation work and managed-scratch allowances, not production defaults or native memory.</summary>
public sealed record SelectedGroundTraceLimits
{
    public required int MaxTerrainNodes { get; init; }
    public required int MaxTerrainCells { get; init; }
    public required int MaxVertices { get; init; }
    public required int MaxVertexLookups { get; init; }
    public required int MaxFormationNodes { get; init; }
    public required int MaxMonuments { get; init; }
    public required int MaxTriangles { get; init; }
    public required int MaxExactTests { get; init; }
    public required long MaxEstimatedScratchBytes { get; init; }

    internal (int Slots, long Bytes) Reserve()
    {
        if (MaxTerrainNodes < 0 || MaxTerrainNodes > 262144 || MaxTerrainCells < 0 || MaxTerrainCells > 65536 ||
            MaxVertices < 0 || MaxVertices > 65536 || MaxVertexLookups < 0 || MaxVertexLookups > 2097152 ||
            MaxFormationNodes < 0 || MaxFormationNodes > 262144 || MaxMonuments < 0 || MaxMonuments > 1024 ||
            MaxTriangles < 0 || MaxTriangles > 131072 || MaxExactTests < 0 || MaxExactTests > 131072 ||
            MaxEstimatedScratchBytes < 0 || MaxEstimatedScratchBytes > 32L * 1024 * 1024)
            throw new FormationValidationException("Invalid selected trace allowances.");
        int slots = 1;
        while (slots < MaxVertices * 2) slots <<= 1; // At most 17 iterations, before allocation.
        // Fixed stacks/query state plus bounded exact-integer temporaries (inputs are finite IEEE doubles).
        // The open-address vertex table has no unaccounted collection growth or retained world callback.
        return (slots, 262144L + 64L * slots);
    }
}

public readonly record struct SelectedGroundTraceDiagnostics(int TerrainNodes, int TerrainCells, int Vertices,
    int VertexLookups, int FormationNodes, int Monuments, int Triangles, int ExactTests, long ReservedScratchBytes)
{
    public int FilteredTests { get; init; }
    public int FilteredHits { get; init; }
    /// <summary>Fixed conservative scalar-operation charge per filter, including its ordinary-value check.</summary>
    public long ReservedFilteredArithmeticSteps => 2048L * FilteredTests;
    /// <summary>Conservative fixed-step charge, not a measured instruction count. No exact refinement loop.</summary>
    public long ReservedExactArithmeticSteps => 256L * ExactTests;
    public int MaximumExactIntegerBits => 16384;
}

/// <summary>Only Complete with no Hit means clear. Incomplete results never expose a provisional nearest hit.</summary>
public readonly record struct SelectedGroundTraceResult(SelectedGroundTraceStatus Status, SelectedGroundHit? Hit,
    string? GenerationDigest, string? TerrainIdentity, SelectedGroundTraceDiagnostics Diagnostics);

/// <summary>
/// Optional per-caller scratch, reusable across planets without retaining any world/field. Exclusive use is
/// checked; concurrent callers need separate bounded owners. Generation stamps avoid clearing the full table
/// per shot. Construction admits its entire retained allocation; this is not a global cache.
/// </summary>
public sealed class SelectedGroundTraceWorkspace
{
    internal readonly CubeVertexKey[] Keys;
    internal readonly Vector3D[] Positions;
    internal readonly ulong[] Stamps;
    internal ulong Epoch { get; private set; }
    private int _busy;
    public long ReservedScratchBytes { get; }
    public SelectedGroundTraceWorkspace(SelectedGroundTraceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        (int slots, long bytes) = limits.Reserve();
        if (bytes > limits.MaxEstimatedScratchBytes) throw new FormationCapacityException("Trace workspace exceeds its explicit allowance.");
        Keys = new CubeVertexKey[slots]; Positions = new Vector3D[slots]; Stamps = new ulong[slots];
        ReservedScratchBytes = bytes;
    }
    internal Use Acquire(int slots)
    {
        if (slots > Keys.Length) throw new ArgumentException("Trace workspace is smaller than the requested vertex allowance.");
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("Trace workspace is already in use.");
        if (Epoch == ulong.MaxValue)
        { Volatile.Write(ref _busy, 0); throw new InvalidOperationException("Trace workspace generation exhausted; replace this scratch owner."); }
        Epoch++;
        return new Use(this);
    }
    internal readonly struct Use(SelectedGroundTraceWorkspace owner) : IDisposable
    { public void Dispose() => Volatile.Write(ref owner._busy, 0); }
}

public interface ISelectedGroundTraceSurface
{
    bool HasSelectedGroundTrace { get; }
    SelectedGroundTraceResult TraceGround(SelectedGroundTraceRequest request, SelectedGroundTraceLimits limits,
        CancellationToken cancellation = default, SelectedGroundTraceWorkspace? workspace = null);
}

/// <summary>
/// Selected physical centreline trace: canonical pinned terrain, every physical formation face and the same
/// immutable monument snapshot used by selected collision. No renderer, whole-planet mesh or static cache.
/// Terrain candidates use conservative angular cones, not a height/slope assumption. Exact retained-triangle
/// predicates are intentionally bounded and measured by ExactTests; runtime cost is not yet qualified.
/// </summary>
public static class SelectedGroundTrace
{
    public const string Contract = "selected-canonical-exact-nearest-ray-v1";
    public const string RadialPinContract = "exact-positive-canonical-direction-product-v1";
    private const double Epsilon = 2.2204460492503131e-16;
    // Test oracle also retains the original independently evaluated cone boundaries. Neither choice changes
    // the selected physical contract: uncertain predicates and overlapping travel intervals are exact.
    internal enum PredicateMode : byte { Filtered, ExactOracle }

    internal static SelectedGroundTraceResult Trace(ISurface surface, FormationField field,
        ReadOnlySpan<SelectedGroundCollision.Monument> monuments, SelectedGroundTraceRequest request,
        SelectedGroundTraceLimits limits, CancellationToken cancellation = default, SelectedGroundTraceWorkspace? workspace = null,
        PredicateMode mode = PredicateMode.Filtered)
    {
        ArgumentNullException.ThrowIfNull(surface); ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(limits);
        FormationRayRequest ray = request.Ray;
        (int slots, long bytes) = limits.Reserve();
        var budget = new Budget(limits, workspace?.ReservedScratchBytes ?? bytes, cancellation);
        if (!budget.Check()) return Incomplete(budget.Status, field, budget);
        if (bytes > limits.MaxEstimatedScratchBytes || (workspace != null && workspace.ReservedScratchBytes > limits.MaxEstimatedScratchBytes))
            return Incomplete(SelectedGroundTraceStatus.CapacityExceeded, field, budget);
        TerrainLattice? lattice = TerrainLattice.Of(surface);
        TerrainPinSet? pins = TerrainPinSet.Of(surface);
        if ((surface as IFormationSurface)?.Formations is { } owner && !ReferenceEquals(owner, field))
            throw new FormationValidationException("Selected trace field does not belong to its surface.");
        if (request.Radius != 0 || lattice == null || (pins != null && !pins.SupportsCanonicalRadialTrace))
            return Incomplete(SelectedGroundTraceStatus.Unsupported, field, budget);
        field.Contract.Validate(lattice);
        // Exact arithmetic below supports all finite IEEE inputs; the cone's error expansion must also remain
        // finite. Refuse an extreme public ray instead of overflowing its broad phase into a false clear result.
        if (MaximumAbs(ray.Origin) > FormationTerrainSeal.MaximumCoordinateMetres ||
            MaximumAbs(ray.Origin + ray.Direction * ray.Length) > FormationTerrainSeal.MaximumCoordinateMetres)
            return Incomplete(SelectedGroundTraceStatus.Unsupported, field, budget);

        workspace ??= new SelectedGroundTraceWorkspace(limits);
        using var workspaceUse = workspace.Acquire(slots);
        var query = new Query(surface, field, lattice, pins, ray, mode, cancellation, workspace, budget);
        return query.Run(monuments);
    }

    private static SelectedGroundTraceResult Incomplete(SelectedGroundTraceStatus status, FormationField field, Budget budget) =>
        new(status, null, field.GenerationDigest, field.Contract.TerrainIdentity, budget.Diagnostics);

    // Passed by ref through the one shared BVH traversal. Neither the nearest accumulator nor its work
    // counters escape to a closure/boxed visitor; routine filtered queries need no per-operation heap owner.
    private struct Query : FormationSupportQueries.ITriangleVisitor
    {
        private readonly ISurface surface;
        private readonly FormationField field;
        private readonly TerrainLattice lattice;
        private readonly TerrainPinSet? pins;
        private readonly FormationRayRequest ray;
        private readonly PredicateMode mode;
        private readonly CancellationToken cancellation;
        private readonly SelectedGroundTraceWorkspace workspace;
        private Budget budget;
        private SelectedGroundHit? nearest;
        private Evidence nearestEvidence;
        internal Query(ISurface surface, FormationField field, TerrainLattice lattice, TerrainPinSet? pins,
            FormationRayRequest ray, PredicateMode mode, CancellationToken cancellation, SelectedGroundTraceWorkspace workspace, Budget budget)
        {
            this.surface = surface; this.field = field; this.lattice = lattice; this.pins = pins; this.ray = ray;
            this.mode = mode; this.cancellation = cancellation; this.workspace = workspace; this.budget = budget;
            nearest = null; nearestEvidence = default;
        }
        private readonly SelectedGroundTraceResult Result(SelectedGroundTraceStatus status) => new(status,
            status == SelectedGroundTraceStatus.Complete ? nearest : null, field.GenerationDigest,
            field.Contract.TerrainIdentity, budget.Diagnostics);
        internal SelectedGroundTraceResult Run(ReadOnlySpan<SelectedGroundCollision.Monument> monuments)
        {
            Span<ConeCell> stack = stackalloc ConeCell[3 * TileKey.MaxLevel + 6];
            Span<FaceRay> faces = stackalloc FaceRay[6];
            for (int face = 0; face < 6; face++) faces[face] = new(ray, face);
            double coneGuard = 128 * Epsilon * Math.Max(1, MaximumAbs(ray.Origin) + ray.Length);
            int count = 0;
            for (int face = 5; face >= 0; face--) stack[count++] = new(new TileKey(face, 0, 0, 0), -1, 1, -1, 1);
            while (count > 0)
            {
                if (!budget.TerrainNode()) return Result(budget.Status);
                ConeCell cone = stack[--count];
                TileKey cell = cone.Cell;
                if (!(mode == PredicateMode.ExactOracle ? MeetsConeOracle(cell, ray) : MeetsCone(cone, faces[cell.Face], ray, coneGuard))) continue;
                if (cell.Level < lattice.VertexLevel)
                {
                    // Reverse push yields stable child 0,1,2,3 traversal. Every pushed candidate is charged on pop.
                    // Warp the exact dyadic chart midpoint, never the average of warped endpoints. Each
                    // parent's two new boundaries are reused by all four children; leaves do no Warp work.
                    double um = mode == PredicateMode.Filtered ? CubeSphere.Warp(cell.U0 + cell.Span * 0.5) : 0;
                    double vm = mode == PredicateMode.Filtered ? CubeSphere.Warp(cell.V0 + cell.Span * 0.5) : 0;
                    for (int child = 3; child >= 0; child--)
                        stack[count++] = new(cell.Child(child), (child & 1) == 0 ? cone.U0 : um,
                            (child & 1) == 0 ? um : cone.U1, (child & 2) == 0 ? cone.V0 : vm,
                            (child & 2) == 0 ? vm : cone.V1);
                    continue;
                }
                if (!budget.Cell()) return Result(budget.Status);
                for (int half = 0; half < 2; half++)
                {
                    TerrainLatticeTriangle triangle = lattice.Triangle(cell, half);
                    if (!Position(triangle.A, out Vector3D a) || !Position(triangle.B, out Vector3D b) || !Position(triangle.C, out Vector3D c))
                        return Result(budget.Status);
                    if (!Test(a, b, c, new(GroundPrimitiveKind.Terrain, cell, half, default, null), null, -1))
                        return Result(budget.Status);
                }
            }
            FormationQueryStatus formationStatus = FormationSupportQueries.VisitTriangles(field.SupportIndex, ray,
                ref this, cancellation, conservativeBounds: true);
            if (formationStatus != FormationQueryStatus.Complete)
                return Result(budget.Status != SelectedGroundTraceStatus.Complete ? budget.Status :
                    formationStatus == FormationQueryStatus.Cancelled ? SelectedGroundTraceStatus.Cancelled : SelectedGroundTraceStatus.CapacityExceeded);
            for (int index = 0; index < monuments.Length; index++)
            {
                if (!budget.Monument()) return Result(budget.Status);
                SelectedGroundCollision.Monument entry = monuments[index];
                if (!MeetsBounds(entry.Bounds.Min, entry.Bounds.Max, ray)) continue;
                MonumentPlacement site = entry.Site;
                for (int t = 0; t < site.Definition.Indices.Length; t += 3)
                {
                    ReadOnlySpan<int> indices = site.Definition.Indices;
                    if (!Test(site.Transform(site.Definition.Positions[indices[t]]), site.Transform(site.Definition.Positions[indices[t + 1]]),
                        site.Transform(site.Definition.Positions[indices[t + 2]]),
                        new(GroundPrimitiveKind.Monument, site.Tile, t / 3, default, site.Definition.Key), null, -1))
                        return Result(budget.Status);
                }
            }
            return Result(budget.Check() ? SelectedGroundTraceStatus.Complete : budget.Status);
        }

        public bool VisitNode() => budget.FormationNode();
        public bool VisitTriangle(FormationPiece piece, int ordinal)
        {
            GeometryTriangle triangle = piece.Geometry.Triangles[ordinal];
            return Test(piece.Transform.Transform(piece.Geometry.Vertices[triangle.A]),
                piece.Transform.Transform(piece.Geometry.Vertices[triangle.B]),
                piece.Transform.Transform(piece.Geometry.Vertices[triangle.C]),
                new(GroundPrimitiveKind.Formation, default, ordinal, piece.PrimitiveId(ordinal), null), piece, ordinal);
        }

        private bool Position(CubeVertexKey key, out Vector3D point)
        {
            point = default;
            int slots = workspace.Keys.Length;
            CubeVertexKey[] keys = workspace.Keys;
            Vector3D[] positions = workspace.Positions;
            ulong[] stamps = workspace.Stamps;
            ulong epoch = workspace.Epoch;
            // Explicit probe allowance counts even cache hits and adversarial collisions. No unbounded hash walk.
            uint hash = unchecked((uint)key.X * 73856093u ^ (uint)key.Y * 19349663u ^ (uint)key.Z * 83492791u ^ (uint)key.Level);
            int slot = (int)(hash & (uint)(slots - 1));
            while (true)
            {
                if (!budget.Lookup()) return false;
                if (stamps[slot] != epoch) break;
                if (keys[slot] == key) { point = positions[slot]; return true; }
                slot = (slot + 1) & (slots - 1);
            }
            if (!budget.Vertex()) return false;
            if (pins != null && pins.TryGet(key, out TerrainPinnedVertex pin)) point = pin.Position;
            else
            {
                Vector3D direction = lattice.Direction(key);
                double radial = lattice.Radius + surface.HeightAt(direction);
                if (!double.IsFinite(radial) || radial <= 0) return budget.Unresolved();
                point = direction * radial;
            }
            if (!Finite(point) || MaximumAbs(point) > FormationTerrainSeal.MaximumCoordinateMetres) return budget.Unresolved();
            if (!budget.Check()) return false;
            keys[slot] = key; positions[slot] = point; stamps[slot] = epoch; return true;
        }

        private bool Test(Vector3D a, Vector3D b, Vector3D c, GroundPrimitive primitive, FormationPiece? piece, int ordinal)
        {
            if (!budget.Triangle()) return false;
            Vector3D low = FormationBounds.Lower(a, FormationBounds.Lower(b, c));
            Vector3D high = FormationBounds.Upper(a, FormationBounds.Upper(b, c));
            if (!MeetsBounds(low, high, ray)) return true;
            TriangleHit triangle = default;
            Interval distance = Interval.Whole;
            TriangleResult status = TriangleResult.Unresolved;
            if (mode == PredicateMode.Filtered)
            {
                budget.Filter();
                status = FilteredTriangle(a, b, c, ray, out triangle, out distance);
            }
            bool exact = mode == PredicateMode.ExactOracle || status == TriangleResult.Unresolved;
            if (exact)
            {
                if (!budget.Exact()) return false;
                status = ExactTriangle(a, b, c, ray, out triangle);
            }
            else if (status == TriangleResult.Hit) budget.FilterHit();
            if (status == TriangleResult.Unresolved) return budget.Unresolved();
            if (status == TriangleResult.Miss) return true;
            var evidence = new Evidence(a, b, c, triangle, distance, exact);
            int order = -1;
            if (nearest != null)
            {
                if (distance.High < nearestEvidence.Distance.Low) order = -1;
                else if (distance.Low > nearestEvidence.Distance.High) order = 1;
                else
                {
                    // Interval overlap is never an epsilon tie. Reevaluate only the two retained candidates,
                    // charging each exact attempt, and compare their exact rational travel distances.
                    if (!MakeExact(ref evidence) || !MakeExact(ref nearestEvidence)) return false;
                    order = (evidence.Hit.Numerator * nearestEvidence.Hit.Denominator)
                        .CompareTo(nearestEvidence.Hit.Numerator * evidence.Hit.Denominator);
                    triangle = evidence.Hit;
                }
            }
            if (order > 0 || (order == 0 && primitive.CompareTo(nearest!.Value.Primitive) >= 0)) return true;
            SupportHit? support = null;
            if (piece != null)
            {
                SupportPatch patch = piece.Geometry.Patches[piece.Geometry.Triangles[ordinal].Patch];
                support = new SupportHit(piece.PrimitiveId(ordinal), triangle.Point, triangle.Normal,
                    triangle.Barycentric, triangle.Distance, patch.Key, piece.Geometry.FamilyKey,
                    patch.SubstrateProfileKey, patch.Capabilities);
            }
            var hit = new SelectedGroundHit(primitive, triangle.Distance, triangle.Point, triangle.Normal,
                triangle.Barycentric, triangle.FrontFace, support);
            nearest = hit; nearestEvidence = evidence;
            return true;
        }
        private bool MakeExact(ref Evidence evidence)
        {
            if (evidence.Exact) return true;
            if (!budget.Exact()) return false;
            if (ExactTriangle(evidence.A, evidence.B, evidence.C, ray, out TriangleHit exact) != TriangleResult.Hit)
                return budget.Unresolved(); // Never publish inconsistent filter/oracle evidence.
            evidence = evidence with { Hit = exact, Exact = true };
            return true;
        }
    }

    // Each cone contains every positive radial vertex and their convex triangle. Its five halfspaces are
    // outward-expanded for canonical multiplication and clipping roundoff, including exact face/corner seams.
    // A line through the body centre can retain many cells; caller work limits then refuse the whole result.
    private readonly record struct ConeCell(TileKey Cell, double U0, double U1, double V0, double V1);
    private readonly record struct FaceRay(double On, double Ou, double Ov, double Dn, double Du, double Dv)
    {
        internal FaceRay(FormationRayRequest ray, int face) : this(
            Vector3D.Dot(CubeSphere.Normal(face), ray.Origin), Vector3D.Dot(CubeSphere.AxisU(face), ray.Origin),
            Vector3D.Dot(CubeSphere.AxisV(face), ray.Origin), Vector3D.Dot(CubeSphere.Normal(face), ray.Direction),
            Vector3D.Dot(CubeSphere.AxisU(face), ray.Direction), Vector3D.Dot(CubeSphere.AxisV(face), ray.Direction)) { }
    }
    private static bool MeetsCone(ConeCell cell, FaceRay face, FormationRayRequest ray, double guard)
    {
        double low = ray.MinDistance, high = ray.Length;
        return Clip(face.On, face.Dn) && Clip(face.Ou - cell.U0 * face.On, face.Du - cell.U0 * face.Dn)
            && Clip(cell.U1 * face.On - face.Ou, cell.U1 * face.Dn - face.Du)
            && Clip(face.Ov - cell.V0 * face.On, face.Dv - cell.V0 * face.Dn)
            && Clip(cell.V1 * face.On - face.Ov, cell.V1 * face.Dn - face.Dv);
        bool Clip(double projectedOrigin, double slope)
        {
            double origin = projectedOrigin + guard;
            if (slope == 0) return origin >= 0;
            double crossing = -origin / slope;
            if (slope > 0) low = Math.Max(low, Math.BitDecrement(crossing));
            else high = Math.Min(high, Math.BitIncrement(crossing));
            return low <= high;
        }
    }

    private static bool MeetsConeOracle(TileKey cell, FormationRayRequest ray)
    {
        Vector3D n = CubeSphere.Normal(cell.Face), u = CubeSphere.AxisU(cell.Face), v = CubeSphere.AxisV(cell.Face);
        double Warp(double x) => x == -1 || x == 1 ? x : CubeSphere.Warp(x);
        double u0 = Warp(cell.U0), u1 = Warp(cell.U0 + cell.Span), v0 = Warp(cell.V0), v1 = Warp(cell.V0 + cell.Span);
        double low = ray.MinDistance, high = ray.Length;
        double guard = 128 * Epsilon * Math.Max(1, MaximumAbs(ray.Origin) + ray.Length);
        return Clip(n) && Clip(u - n * u0) && Clip(n * u1 - u) && Clip(v - n * v0) && Clip(n * v1 - v);
        bool Clip(Vector3D plane)
        {
            double origin = Vector3D.Dot(plane, ray.Origin) + guard, slope = Vector3D.Dot(plane, ray.Direction);
            if (slope == 0) return origin >= 0;
            double crossing = -origin / slope;
            if (slope > 0) low = Math.Max(low, Math.BitDecrement(crossing));
            else high = Math.Min(high, Math.BitIncrement(crossing));
            return low <= high;
        }
    }

    internal static bool MeetsBounds(Vector3D lower, Vector3D upper, FormationRayRequest ray)
    {
        if (!Finite(lower) || !Finite(upper)) return true;
        double low = ray.MinDistance, high = ray.Length;
        double guard = 128 * Epsilon * Math.Max(1, Math.Max(MaximumAbs(lower), MaximumAbs(upper)) + MaximumAbs(ray.Origin) + ray.Length);
        for (int axis = 0; axis < 3; axis++)
        {
            double origin = Component(ray.Origin, axis), direction = Component(ray.Direction, axis);
            double min = Component(lower, axis) - guard, max = Component(upper, axis) + guard;
            if (direction == 0) { if (origin < min || origin > max) return false; continue; }
            double a = (min - origin) / direction, b = (max - origin) / direction;
            low = Math.Max(low, Math.BitDecrement(Math.Min(a, b)));
            high = Math.Min(high, Math.BitIncrement(Math.Max(a, b)));
            if (low > high) return false;
        }
        return true;
    }

    private enum TriangleResult : byte { Miss, Hit, Unresolved }
    private readonly record struct TriangleHit(double Distance, Vector3D Point, Vector3D Normal, Vector3D Barycentric,
        bool FrontFace, BigInteger Numerator, BigInteger Denominator);
    private readonly record struct Evidence(Vector3D A, Vector3D B, Vector3D C, TriangleHit Hit, Interval Distance, bool Exact);

    // Each elementary operation encloses the exact result of represented IEEE inputs, including subnormal
    // rounding. Nonfinite arithmetic or a denominator touching zero yields the entire line and forces the
    // exact fallback. These are fixed-operation enclosures, not a scale-dependent determinant epsilon.
    private readonly record struct Interval(double Low, double High)
    {
        internal static Interval Whole => new(double.NegativeInfinity, double.PositiveInfinity);
        internal bool Finite => double.IsFinite(Low) && double.IsFinite(High);
        internal bool Contains(double value) => double.IsFinite(value) && value >= Low && value <= High;
        internal double Middle => Low * 0.5 + High * 0.5;
        internal static Interval Point(double value) => new(value, value);
        private static Interval Enclose(double low, double high) => double.IsFinite(low) && double.IsFinite(high)
            ? new(Math.BitDecrement(low), Math.BitIncrement(high)) : Whole;
        public static Interval operator +(Interval a, Interval b) => Enclose(a.Low + b.Low, a.High + b.High);
        public static Interval operator -(Interval a, Interval b) => Enclose(a.Low - b.High, a.High - b.Low);
        public static Interval operator -(Interval a) => new(-a.High, -a.Low);
        public static Interval operator *(Interval a, Interval b)
        {
            if (!a.Finite || !b.Finite) return Whole;
            double p = a.Low * b.Low, q = a.Low * b.High, r = a.High * b.Low, s = a.High * b.High;
            return Enclose(Math.Min(Math.Min(p, q), Math.Min(r, s)), Math.Max(Math.Max(p, q), Math.Max(r, s)));
        }
        public static Interval operator /(Interval a, Interval b)
        {
            if (!a.Finite || !b.Finite || (b.Low <= 0 && b.High >= 0)) return Whole;
            double p = a.Low / b.Low, q = a.Low / b.High, r = a.High / b.Low, s = a.High / b.High;
            return Enclose(Math.Min(Math.Min(p, q), Math.Min(r, s)), Math.Max(Math.Max(p, q), Math.Max(r, s)));
        }
    }
    private readonly record struct IntervalVector(Interval X, Interval Y, Interval Z)
    {
        internal IntervalVector(Vector3D value) : this(Interval.Point(value.X), Interval.Point(value.Y), Interval.Point(value.Z)) { }
        public static IntervalVector operator -(IntervalVector a, IntervalVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        internal static IntervalVector Cross(IntervalVector a, IntervalVector b) => new(a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        internal static Interval Dot(IntervalVector a, IntervalVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    private static TriangleResult FilteredTriangle(Vector3D a, Vector3D b, Vector3D c, FormationRayRequest ray,
        out TriangleHit hit, out Interval distance)
    {
        hit = default; distance = Interval.Whole;
        if (!Finite(a) || !Finite(b) || !Finite(c)) return TriangleResult.Unresolved;
        var ia = new IntervalVector(a);
        IntervalVector edge1 = new IntervalVector(b) - ia, edge2 = new IntervalVector(c) - ia;
        var direction = new IntervalVector(ray.Direction);
        IntervalVector delta = new IntervalVector(ray.Origin) - ia;
        IntervalVector p = IntervalVector.Cross(direction, edge2), q = IntervalVector.Cross(delta, edge1);
        Interval det = IntervalVector.Dot(edge1, p), un = IntervalVector.Dot(delta, p), vn = IntervalVector.Dot(direction, q);
        Interval tn = IntervalVector.Dot(edge2, q);
        if (!det.Finite || (det.Low <= 0 && det.High >= 0)) return TriangleResult.Unresolved;
        bool front = det.Low > 0;
        if (!front && ray.Backfaces == FormationBackfacePolicy.FrontFacesOnly) return TriangleResult.Miss;
        if (!front) { det = -det; un = -un; vn = -vn; tn = -tn; }
        Interval an = det - un - vn, pastMinimum = tn - det * Interval.Point(ray.MinDistance), beforeMaximum = det * Interval.Point(ray.Length) - tn;
        if (un.High < 0 || vn.High < 0 || an.High < 0 || pastMinimum.High < 0 || beforeMaximum.High < 0)
            return TriangleResult.Miss;
        distance = tn / det;
        if (un.Low < 0 || vn.Low < 0 || an.Low < 0 || pastMinimum.Low < 0 || beforeMaximum.Low < 0 || !distance.Finite)
            return TriangleResult.Unresolved;
        Interval wa = an / det, wb = un / det, wc = vn / det;
        if (!wa.Finite || !wb.Finite || !wc.Finite) return TriangleResult.Unresolved;
        // Limit the representation error of fast hit values as well as proving the hit. Ill-conditioned
        // ratios remain exact even when their sign alone is decidable. Provenance/nearest ordering never use
        // these rounded values; they use separated enclosing intervals or exact cross-products above.
        if (distance.High - distance.Low > 64 * Epsilon * Math.Max(1, Math.Abs(distance.Middle)) ||
            wa.High - wa.Low > 64 * Epsilon || wb.High - wb.Low > 64 * Epsilon || wc.High - wc.Low > 64 * Epsilon)
            return TriangleResult.Unresolved;
        // Use the ordinary represented determinant ratios for the returned floating values. The enclosure
        // must contain them, and the width gate above bounds their error; an interval midpoint is not a new
        // physical contact convention. Exact primitive ordering still uses evidence, never these values.
        Vector3D e1 = b - a, e2 = c - a, d = ray.Origin - a;
        Vector3D dp = Vector3D.Cross(ray.Direction, e2), dq = Vector3D.Cross(d, e1);
        double dd = Vector3D.Dot(e1, dp), du = Vector3D.Dot(d, dp), dv = Vector3D.Dot(ray.Direction, dq);
        double travel = Vector3D.Dot(e2, dq) / dd, ba = (dd - du - dv) / dd, bb = du / dd, bc = dv / dd;
        if (!distance.Contains(travel) || !wa.Contains(ba) || !wb.Contains(bb) || !wc.Contains(bc)) return TriangleResult.Unresolved;
        travel = Math.Clamp(travel, ray.MinDistance, ray.Length);
        double sum = ba + bb + bc;
        Vector3D cross = Vector3D.Cross(e1, e2);
        double largest = MaximumAbs(cross);
        if (!(largest > 0) || !double.IsFinite(largest)) return TriangleResult.Unresolved;
        Vector3D winding = (cross / largest).Normalized, point = ray.Origin + ray.Direction * travel;
        if (!Finite(winding) || winding == Vector3D.Zero || !Finite(point)) return TriangleResult.Unresolved;
        hit = new(travel, point, winding, new(ba / sum, bb / sum, bc / sum), front, default, default);
        return TriangleResult.Hit;
    }
    private readonly record struct ExactVector(BigInteger X, BigInteger Y, BigInteger Z)
    {
        public static ExactVector operator -(ExactVector a, ExactVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        internal static ExactVector Cross(ExactVector a, ExactVector b) => new(a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        internal static BigInteger Dot(ExactVector a, ExactVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    // Exact Moller-Trumbore determinants on the represented IEEE inputs. One test performs a fixed amount
    // of arithmetic (no iterative refinement); finite doubles require <= 2100-bit inputs, < 6400-bit triple
    // products and < 12800-bit nearest-distance comparisons. Each attempt reserves 256 arithmetic steps,
    // including conversion, ratios, misses and coplanar refusal. The 16384-bit ceiling follows directly from
    // IEEE exponent/mantissa bounds; no untrusted integer or unbounded precision input is accepted. The scratch
    // reservation includes those temporaries; cumulative allocation/driver memory are not this reservation.
    private static TriangleResult ExactTriangle(Vector3D a, Vector3D b, Vector3D c, FormationRayRequest ray, out TriangleHit hit)
    {
        hit = default;
        if (!Finite(a) || !Finite(b) || !Finite(c)) return TriangleResult.Unresolved;
        int exponent = 0;
        Minimum(a); Minimum(b); Minimum(c); Minimum(ray.Origin); Minimum(ray.Direction);
        ExactVector ea = Integer(a), edge1 = Integer(b) - ea, edge2 = Integer(c) - ea;
        ExactVector direction = Integer(ray.Direction), delta = Integer(ray.Origin) - ea;
        ExactVector p = ExactVector.Cross(direction, edge2), q = ExactVector.Cross(delta, edge1);
        BigInteger det = ExactVector.Dot(edge1, p), un = ExactVector.Dot(delta, p), vn = ExactVector.Dot(direction, q);
        BigInteger tn = ExactVector.Dot(edge2, q);
        if (det.IsZero)
        {
            ExactVector normal = ExactVector.Cross(edge1, edge2);
            if (normal.X.IsZero && normal.Y.IsZero && normal.Z.IsZero) return TriangleResult.Unresolved;
            return ExactVector.Dot(normal, delta).IsZero ? TriangleResult.Unresolved : TriangleResult.Miss;
        }
        bool front = det.Sign > 0;
        if (!front && ray.Backfaces == FormationBackfacePolicy.FrontFacesOnly) return TriangleResult.Miss;
        if (!front) { det = -det; un = -un; vn = -vn; tn = -tn; }
        if (un.Sign < 0 || vn.Sign < 0 || un + vn > det || Compare(tn, det, ray.MinDistance) < 0 || Compare(tn, det, ray.Length) > 0)
            return TriangleResult.Miss;
        double distance = Math.Clamp(Ratio(tn, det), ray.MinDistance, ray.Length);
        double wb = Ratio(un, det), wc = Ratio(vn, det), wa = Ratio(det - un - vn, det);
        double sum = wa + wb + wc;
        Vector3D barycentric = new(wa / sum, wb / sum, wc / sum);
        Vector3D cross = Vector3D.Cross(b - a, c - a);
        double largest = MaximumAbs(cross);
        if (!(largest > 0) || !double.IsFinite(largest)) return TriangleResult.Unresolved;
        Vector3D winding = (cross / largest).Normalized, point = ray.Origin + ray.Direction * distance;
        if (!Finite(winding) || winding == Vector3D.Zero || !Finite(point) || !double.IsFinite(distance)) return TriangleResult.Unresolved;
        hit = new(distance, point, winding, barycentric, front, tn, det);
        return TriangleResult.Hit;

        void Minimum(Vector3D value)
        {
            for (int axis = 0; axis < 3; axis++)
            { double component = Component(value, axis); if (component != 0) exponent = Math.Min(exponent, Parts(component).Exponent); }
        }
        ExactVector Integer(Vector3D value) => new(Convert(value.X), Convert(value.Y), Convert(value.Z));
        BigInteger Convert(double value)
        {
            var parts = Parts(value);
            return new BigInteger(parts.Significand) << (parts.Exponent - exponent);
        }
    }

    private static (long Significand, int Exponent) Parts(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int e = (int)((bits >> 52) & 2047);
        long significand = bits & 0x000fffffffffffffL;
        if (e != 0) significand |= 1L << 52;
        if (bits < 0) significand = -significand;
        return (significand, e == 0 ? -1074 : e - 1075);
    }

    private static int Compare(BigInteger numerator, BigInteger denominator, double value)
    {
        var p = Parts(value);
        return p.Exponent >= 0 ? numerator.CompareTo((denominator * p.Significand) << p.Exponent) :
            (numerator << -p.Exponent).CompareTo(denominator * p.Significand);
    }

    private static double Ratio(BigInteger numerator, BigInteger denominator)
    {
        if (numerator.IsZero) return 0;
        int n = Math.Max(0, checked((int)BigInteger.Abs(numerator).GetBitLength()) - 54);
        int d = Math.Max(0, checked((int)denominator.GetBitLength()) - 54);
        return Math.ScaleB((double)(numerator >> n) / (double)(denominator >> d), n - d);
    }

    private static bool Finite(Vector3D value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    private static double MaximumAbs(Vector3D value) => Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
    private static double Component(Vector3D value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

    private struct Budget(SelectedGroundTraceLimits limits, long bytes, CancellationToken cancellation)
    {
        private int _terrainNodes, _cells, _vertices, _lookups, _formationNodes, _monuments, _triangles, _exact, _filtered, _filteredHits;
        internal SelectedGroundTraceStatus Status { get; private set; }
        internal SelectedGroundTraceDiagnostics Diagnostics => new(_terrainNodes, _cells, _vertices, _lookups,
            _formationNodes, _monuments, _triangles, _exact, bytes) { FilteredTests = _filtered, FilteredHits = _filteredHits };
        internal void Filter() => _filtered++; // At most one per already charged triangle.
        internal void FilterHit() => _filteredHits++;
        internal bool Check()
        {
            if (cancellation.IsCancellationRequested) Status = SelectedGroundTraceStatus.Cancelled;
            return Status == SelectedGroundTraceStatus.Complete;
        }
        internal bool Unresolved() { Status = SelectedGroundTraceStatus.NumericallyUnresolved; return false; }
        private bool Charge(ref int value, int maximum)
        {
            if (!Check()) return false;
            if (value == maximum) { Status = SelectedGroundTraceStatus.CapacityExceeded; return false; }
            value++; return true;
        }
        internal bool TerrainNode() => Charge(ref _terrainNodes, limits.MaxTerrainNodes);
        internal bool Cell() => Charge(ref _cells, limits.MaxTerrainCells);
        internal bool Vertex() => Charge(ref _vertices, limits.MaxVertices);
        internal bool Lookup() => Charge(ref _lookups, limits.MaxVertexLookups);
        internal bool FormationNode() => Charge(ref _formationNodes, limits.MaxFormationNodes);
        internal bool Monument() => Charge(ref _monuments, limits.MaxMonuments);
        internal bool Triangle() => Charge(ref _triangles, limits.MaxTriangles);
        internal bool Exact() => Charge(ref _exact, limits.MaxExactTests);
    }
}
