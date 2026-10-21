using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Explicit allowances for one selected collision operation. They are not a concurrent-job or native-shape budget;
/// the physics/view owner must separately reserve admitted jobs, completed results and resident native resources.
/// </summary>
public sealed record SelectedGroundCollisionLimits
{
    public const int MaximumVertices = 524288, MaximumTriangles = 1048576, MaximumMonuments = 1024;
    public const long MaximumScratchBytes = 128L * 1024 * 1024, MaximumMeshBytes = 32L * 1024 * 1024;
    public required TerrainCollisionLimits Terrain { get; init; }
    public required FormationFieldQueryLimits Query { get; init; }
    public required int MaxMonuments { get; init; }
    public required int MaxVertices { get; init; }
    public required int MaxTriangles { get; init; }
    public required long MaxEstimatedScratchBytes { get; init; }
    public required long MaxEstimatedMeshBytes { get; init; }

    internal (long Scratch, long Mesh) Reserve()
    {
        ArgumentNullException.ThrowIfNull(Terrain); ArgumentNullException.ThrowIfNull(Query);
        (long terrainScratch, long terrainMesh) = SurfaceGrid.ValidateLimits(Terrain);
        if (MaxMonuments < 0 || MaxMonuments > MaximumMonuments || MaxVertices < 1 || MaxVertices > MaximumVertices ||
            MaxTriangles < 1 || MaxTriangles > MaximumTriangles || Query.MaxVisitedNodes < 0 || Query.MaxVisitedNodes > 8191 ||
            Query.MaxPieces < 0 || Query.MaxPieces > 4096 || Query.MaxEstimatedBytes < 0 || Query.MaxEstimatedBytes > 256 * 1024 ||
            MaxEstimatedScratchBytes < 1 || MaxEstimatedScratchBytes > MaximumScratchBytes ||
            MaxEstimatedMeshBytes < 1 || MaxEstimatedMeshBytes > MaximumMeshBytes)
            throw new FormationValidationException("Invalid explicit selected collision limits.");
        long mesh = 4096L + 12L * MaxVertices + 12L * MaxTriangles;
        // Includes the base arrays, canonical cell/frontier/weld storage, query stack/list/copy, monument selection,
        // immutable footprint and final arrays simultaneously. It does not estimate a Jolt/Godot native shape.
        long scratch = 16384L + terrainScratch + terrainMesh + Query.MaxEstimatedBytes + 4L * MaxMonuments + mesh;
        if (mesh > MaxEstimatedMeshBytes || scratch > MaxEstimatedScratchBytes)
            throw new FormationCapacityException("Selected collision aggregate reservation exceeds its caller allowance.");
        return (scratch, mesh);
    }
}

/// <summary>
/// Exact immutable component plan, authenticated by an opaque selected-bundle token. No surface or sampler is
/// retained. A build attempt consumes the plan once, including cancellation/failure; callers retain reservations
/// until that attempt exits. Native estimates are supplied separately by the physics owner.
/// </summary>
public sealed class SelectedGroundCollisionPlan
{
    private readonly object _owner;
    private readonly int[] _selectedMonuments;
    private readonly int _selectedCount;
    private int _consumed;
    internal SelectedGroundCollisionPlan(object owner, TerrainCollisionPlan terrain, ZoneFrame frame,
        FormationFieldPieceQuery query, SelectedGroundCollision.Monument[] monuments, int[] selectedMonuments,
        int selectedCount, int vertices, int triangles, long scratch)
    {
        _owner = owner; Terrain = terrain; Frame = frame; Query = query; Monuments = monuments;
        _selectedMonuments = selectedMonuments; _selectedCount = selectedCount; VertexCount = vertices; TriangleCount = triangles;
        EstimatedBuildScratchBytes = scratch;
    }
    public TerrainCollisionPlan Terrain { get; }
    public int VertexCount { get; }
    public int TriangleCount { get; }
    public long EstimatedBuildScratchBytes { get; }
    public long EstimatedRetainedBytes => Terrain.EstimatedRetainedBytes + 4096L + 16L * Query.Pieces.Length + 4L * _selectedMonuments.Length;
    internal ZoneFrame Frame { get; }
    internal FormationFieldPieceQuery Query { get; }
    internal SelectedGroundCollision.Monument[] Monuments { get; }
    internal ReadOnlySpan<int> SelectedMonuments => _selectedMonuments.AsSpan(0, _selectedCount);
    internal void Consume(object owner)
    {
        if (!ReferenceEquals(_owner, owner)) throw new FormationValidationException("Collision plan belongs to another selected bundle.");
        if (Interlocked.CompareExchange(ref _consumed, 1, 0) != 0)
            throw new InvalidOperationException("Collision plan has already been consumed.");
    }
}

/// <summary>Checked canonical terrain + sealed formations + physical monuments, in a stable primitive order.</summary>
public static class SelectedGroundCollision
{
    public const string Contract = "selected-canonical-radial-footprint-composite-collision-v1";

    internal readonly record struct Monument(MonumentPlacement Site, FormationBounds Bounds);

    internal static Monument[] Snapshot(IReadOnlyList<MonumentPlacement> sites, int maximumSites, CancellationToken cancellation)
    {
        if (sites.Count > maximumSites || sites.Count > SelectedGroundCollisionLimits.MaximumMonuments)
            throw new FormationCapacityException("Selected collision monument snapshot exceeds its allowance.");
        var result = new Monument[sites.Count];
        for (int i = 0; i < sites.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            MonumentPlacement site = sites[i];
            if (site.Definition.Positions.Length == 0 || site.Definition.Indices.Length % 3 != 0)
                throw new FormationValidationException("Selected monument requires a complete physical triangle mesh.");
            Vector3D low = site.Transform(site.Definition.Positions[0]), high = low;
            foreach (Vector3D vertex in site.Definition.Positions)
            {
                cancellation.ThrowIfCancellationRequested();
                Vector3D point = site.Transform(vertex); FormationInput.Finite(point, nameof(sites));
                low = FormationBounds.Lower(low, point); high = FormationBounds.Upper(high, point);
            }
            foreach (int index in site.Definition.Indices)
            {
                cancellation.ThrowIfCancellationRequested();
                if ((uint)index >= (uint)site.Definition.Positions.Length)
                    throw new FormationValidationException("Selected monument physical index is outside its mesh.");
            }
            result[i] = new Monument(site, new FormationBounds(low, high));
        }
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    internal static TriangleMesh Build(ISurface surface, in ZoneFrame frame, TileKey tile, double margin,
        FormationField field, Monument[] monuments, SelectedGroundCollisionLimits limits,
        CancellationToken cancellation = default)
    {
        var owner = new object();
        TerrainLattice lattice = TerrainLattice.Of(surface) ?? throw new FormationValidationException("Selected collision requires a canonical lattice.");
        return Build(surface, owner, Prepare(owner, lattice, frame, tile, margin, field, monuments, limits, cancellation), cancellation);
    }

    internal static SelectedGroundCollisionPlan Prepare(object owner, TerrainLattice lattice, in ZoneFrame frame,
        TileKey tile, double margin, FormationField field, Monument[] monuments, SelectedGroundCollisionLimits limits,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(limits); ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(monuments);
        long scratch = limits.Reserve().Scratch;
        cancellation.ThrowIfCancellationRequested();
        if (monuments.Length > limits.MaxMonuments)
            throw new FormationCapacityException("Selected collision monument visits exceed their caller allowance.");
        FormationInput.Finite(frame.Origin, nameof(frame));
        FormationInput.Unit(frame.East, nameof(frame)); FormationInput.Unit(frame.Up, nameof(frame)); FormationInput.Unit(frame.South, nameof(frame));
        if ((Vector3D.Cross(frame.East, frame.Up) - frame.South).LengthSquared > 1e-20)
            throw new FormationValidationException("Collision frame must be right-handed and orthonormal.");
        TerrainCollisionPlan terrain = SurfaceGrid.PrepareCollision(lattice, tile, margin, limits.Terrain, cancellation);
        FormationFieldPieceQuery query = field.QueryPieces(terrain.Footprint, limits.Query, cancellation);
        if (query.Status == FormationQueryStatus.Cancelled) throw new OperationCanceledException(cancellation);
        if (query.Status != FormationQueryStatus.Complete)
            throw new FormationCapacityException("Selected collision formation query did not complete.");

        long vertexCount = terrain.VertexCount, triangleCount = terrain.TriangleCount;
        CheckCounts();
        foreach (FormationPiece piece in query.Pieces)
        {
            cancellation.ThrowIfCancellationRequested();
            vertexCount += piece.Geometry.VertexCount; triangleCount += piece.Geometry.TriangleCount; CheckCounts();
        }
        var selectedMonuments = new int[monuments.Length];
        int selectedCount = 0;
        for (int i = 0; i < monuments.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!terrain.Footprint.MayIntersect(monuments[i].Bounds)) continue;
            selectedMonuments[selectedCount++] = i;
            vertexCount += monuments[i].Site.Definition.Positions.Length;
            triangleCount += monuments[i].Site.Definition.Indices.Length / 3;
            CheckCounts();
        }
        // Keep the full bounded selection array rather than allocate a second copy; unused entries are not read.
        cancellation.ThrowIfCancellationRequested();
        return new SelectedGroundCollisionPlan(owner, terrain, frame, query, monuments, selectedMonuments,
            selectedCount, checked((int)vertexCount), checked((int)triangleCount), scratch);

        void CheckCounts()
        {
            if (vertexCount > limits.MaxVertices || triangleCount > limits.MaxTriangles)
                throw new FormationCapacityException("Selected collision complete mesh exceeds its output allowance.");
        }
    }

    internal static TriangleMesh Build(ISurface surface, object owner, SelectedGroundCollisionPlan plan,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(plan); plan.Consume(owner);
        cancellation.ThrowIfCancellationRequested();
        TriangleMesh terrain = SurfaceGrid.BuildCollision(surface, plan.Frame, plan.Terrain, cancellation);
        var positions = new float[checked(plan.VertexCount * 3)];
        var indices = new int[checked(plan.TriangleCount * 3)];
        terrain.Positions.CopyTo(positions, 0); terrain.Indices.CopyTo(indices, 0);
        int nextVertex = terrain.VertexCount, nextIndex = terrain.Indices.Length;
        ZoneFrame outputFrame = plan.Frame;
        foreach (FormationPiece piece in plan.Query.Pieces)
        {
            int offset = nextVertex;
            foreach (Vector3D vertex in piece.Geometry.Vertices) Vertex(piece.Transform.Transform(vertex));
            foreach (GeometryTriangle triangle in piece.Geometry.Triangles)
            {
                cancellation.ThrowIfCancellationRequested();
                indices[nextIndex++] = offset + triangle.A; indices[nextIndex++] = offset + triangle.B; indices[nextIndex++] = offset + triangle.C;
            }
        }
        foreach (int selected in plan.SelectedMonuments)
        {
            MonumentPlacement site = plan.Monuments[selected].Site;
            int offset = nextVertex;
            foreach (Vector3D vertex in site.Definition.Positions) Vertex(site.Transform(vertex));
            foreach (int index in site.Definition.Indices)
            { cancellation.ThrowIfCancellationRequested(); indices[nextIndex++] = offset + index; }
        }
        cancellation.ThrowIfCancellationRequested();
        return new TriangleMesh(positions, indices);

        void Vertex(Vector3D point)
        {
            cancellation.ThrowIfCancellationRequested();
            var local = outputFrame.ToLocal(point);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y) || !float.IsFinite(local.Z))
                throw new FormationValidationException("Selected physical collision position is not finite in its output frame.");
            positions[nextVertex * 3] = local.X; positions[nextVertex * 3 + 1] = local.Y; positions[nextVertex * 3 + 2] = local.Z;
            nextVertex++;
        }
    }
}
