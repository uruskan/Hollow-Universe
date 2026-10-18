using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Optional caller reservations for canonical collision only; production owners choose their own limits.</summary>
public sealed record TerrainCollisionLimits
{
    public required int MaxCells { get; init; }
    public required int MaxVertices { get; init; }
    public required long MaxEstimatedScratchBytes { get; init; }
    public required long MaxEstimatedMeshBytes { get; init; }
}

/// <summary>
/// Ground triangles of a cube-sphere tile from any surface's heights (docs/WHOLE_WORLD.md §6.1): a grid of the face's
/// own dyadic coordinates, a power of two cells a tile, widened by whole cells for the margin, each point lifted to the
/// ground along its direction. Neighbouring tiles of a face share their grid points bit for bit where they overlap,
/// and everything is a pure function of the surface, so the server and every client build the same triangles. Points
/// may run a little past the face's edge on the legacy path. Opted-in lattices instead extend through actual adjacent
/// face cells, using canonical identities, diagonals and frozen positions shared with rendering and contact seals.
/// </summary>
public static class SurfaceGrid
{
    /// <summary>About a metre between grid points.</summary>
    public const double DefaultSpacing = 1.0;

    /// <summary>The most cells a side of one tile's grid gets, whatever its size.</summary>
    public const int MostCells = 128;

    /// <summary>Must join the selected world's identity before this inactive capability is activated.</summary>
    public const string CanonicalCollisionContract = "canonical-cube-collision-vertex-rings-v1";
    public const int MaximumCanonicalCells = 147456;
    public const int MaximumCanonicalVertices = 262144;
    public const long MaximumCanonicalScratchBytes = 64L * 1024 * 1024;
    public const long MaximumCanonicalMeshBytes = 16L * 1024 * 1024;
    private static readonly TerrainCollisionLimits HardLimits = new()
    {
        MaxCells = MaximumCanonicalCells, MaxVertices = MaximumCanonicalVertices,
        MaxEstimatedScratchBytes = MaximumCanonicalScratchBytes, MaxEstimatedMeshBytes = MaximumCanonicalMeshBytes,
    };

    /// <summary>Cells a side for a tile: the power of two nearest its edge over the spacing, 1 … <see cref="MostCells"/>.</summary>
    public static int CellsFor(TileKey tile, double radius, double spacing)
    {
        double wanted = TileKey.EdgeLength(tile.Level, radius) / spacing;
        int cells = 1;
        while (cells < MostCells && cells * 1.5 < wanted)
        {
            cells *= 2;
        }
        return cells;
    }

    /// <summary>
    /// Legacy surfaces retain their original grid. Canonical margins use ceil(margin / nominal canonical cell metres)
    /// vertex-neighbor rings; on a face this is the same square extension, across edges/corners it includes every
    /// incident face. This is a topological margin policy, not a minimum distance over arbitrary terrain relief.
    /// Supplied limits reserve estimated managed scratch/output; omitted limits enforce hard ceilings only, not a
    /// production per-world reservation. Capacity failure never returns a partial mesh. Collision stores positions
    /// and outward triangles, not shading normals; pinned normals remain the renderer's canonical stencil normals.
    /// </summary>
    public static TriangleMesh Collision(ISurface surface, in ZoneFrame frame, TileKey tile, double margin, double spacing,
        TerrainCollisionLimits? canonicalLimits = null, CancellationToken cancellation = default)
        => Build(surface, frame, tile, margin, spacing, canonicalLimits, false, out _, cancellation);

    /// <summary>One bounded canonical enumeration provides both mesh and the conservative physical query footprint.</summary>
    public static TerrainCollisionMesh CollisionWithFootprint(ISurface surface, in ZoneFrame frame, TileKey tile,
        double margin, TerrainCollisionLimits limits, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        TriangleMesh mesh = Build(surface, frame, tile, margin, DefaultSpacing, limits, true, out TerrainCollisionFootprint? footprint, cancellation);
        return new TerrainCollisionMesh(mesh, footprint!);
    }

    private static TriangleMesh Build(ISurface surface, in ZoneFrame frame, TileKey tile, double margin, double spacing,
        TerrainCollisionLimits? canonicalLimits, bool includeFootprint, out TerrainCollisionFootprint? footprint, CancellationToken cancellation)
    {
        footprint = null;
        TerrainLattice? lattice = TerrainLattice.Of(surface);
        TerrainPinSet? pins = TerrainPinSet.Of(surface);
        if (lattice == null && canonicalLimits != null)
            throw new ArgumentException("Canonical collision reservations require an opted-in terrain lattice.", nameof(canonicalLimits));
        int cells = lattice?.CellsForTile(tile) ?? CellsFor(tile, surface.Radius, spacing);
        double cellMetres = TileKey.EdgeLength(tile.Level, surface.Radius) / cells;
        if (lattice != null && (!double.IsFinite(margin) || margin < 0 || margin / cellMetres > TerrainLattice.MaximumMarginCells))
            throw new ArgumentOutOfRangeException(nameof(margin), "Canonical collision margin exceeds its bounded grid extension.");
        int extra = Math.Max(0, (int)Math.Ceiling(margin / cellMetres));
        if (lattice != null)
        {
            TerrainCollisionLimits limits = canonicalLimits ?? HardLimits;
            TerrainCollisionPlan plan = PrepareCollision(lattice, tile, margin, limits, cancellation);
            if (includeFootprint) footprint = plan.Footprint;
            return BuildCollision(surface, frame, plan, cancellation);
        }
        int first = -extra, count = cells + 2 * extra, n = count + 1;
        var positions = new float[n * n * 3];
        for (int j = 0; j < n; j++)
        {
            cancellation.ThrowIfCancellationRequested();
            double v = TileKey.GridCoordinate(tile.Y, tile.Level, first + j, cells);
            for (int i = 0; i < n; i++)
            {
                double u = TileKey.GridCoordinate(tile.X, tile.Level, first + i, cells);
                Vector3D direction = lattice?.GridDirection(tile, cells, first + i, first + j) ?? CubeSphere.Direction(tile.Face, u, v);
                Vector3D point;
                if (pins != null && lattice!.TryGridVertex(tile, cells, first + i, first + j, out CubeVertexKey vertex) &&
                    pins.TryGet(vertex, out TerrainPinnedVertex pin)) point = pin.Position;
                else
                {
                    double radial = surface.Radius + surface.HeightAt(direction);
                    if (lattice != null && (!double.IsFinite(radial) || radial <= 0))
                        throw new FormationValidationException("Canonical collision requires finite positive radial terrain.");
                    point = direction * radial;
                }
                System.Numerics.Vector3 local = frame.ToLocal(point);
                if (!float.IsFinite(local.X) || !float.IsFinite(local.Y) || !float.IsFinite(local.Z))
                    throw new FormationValidationException("Collision position must be finite in its output frame.");
                int k = (j * n + i) * 3;
                positions[k] = local.X;
                positions[k + 1] = local.Y;
                positions[k + 2] = local.Z;
            }
        }
        var indices = new int[count * count * 6];
        int t = 0;
        for (int j = 0; j < count; j++)
        {
            for (int i = 0; i < count; i++)
            {
                // (u, v) runs counter-clockwise seen from outside the planet (CubeSphere), so (b − a) × (c − a) points up.
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                indices[t++] = a;
                indices[t++] = b;
                indices[t++] = d;
                indices[t++] = a;
                indices[t++] = d;
                indices[t++] = c;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        if (includeFootprint) footprint = TerrainCollisionFootprint.Core(lattice!, tile, cells);
        return new TriangleMesh(positions, indices);
    }

    public static TerrainCollisionPlan PrepareCollision(TerrainLattice lattice, TileKey tile, double margin,
        TerrainCollisionLimits limits, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(lattice); ArgumentNullException.ThrowIfNull(limits);
        ValidateLimits(limits); cancellation.ThrowIfCancellationRequested();
        int cells = lattice.CellsForTile(tile);
        double spacing = TileKey.EdgeLength(lattice.VertexLevel, lattice.Radius);
        if (!double.IsFinite(margin) || margin < 0 || margin / spacing > TerrainLattice.MaximumMarginCells)
            throw new ArgumentOutOfRangeException(nameof(margin), "Canonical collision margin exceeds its bounded grid extension.");
        int rings = (int)Math.Ceiling(margin / spacing);
        if (cells * cells > limits.MaxCells || (cells + 1) * (cells + 1) > limits.MaxVertices)
            throw new FormationCapacityException("Canonical collision core exceeds its caller allowance.");
        if (rings == 0)
        {
            var core = new TerrainCollisionPlan(lattice, tile, cells, Array.Empty<TileKey>(), (cells + 1) * (cells + 1),
                TerrainCollisionFootprint.Core(lattice, tile, cells));
            cancellation.ThrowIfCancellationRequested();
            return core;
        }
        // The list is a bounded breadth-first frontier as well as the eventual output order. A cell is expanded
        // once, with at most four corners * four incident cells = sixteen neighbor attempts. The last ring is
        // retained without expansion. No face-exterior coordinates or floating-point face selection occur.
        var seen = new HashSet<TileKey>();
        var ordered = new List<TileKey>();
        for (int j = 0; j < cells; j++)
        for (int i = 0; i < cells; i++)
            Add(new TileKey(tile.Face, lattice.VertexLevel, tile.X * cells + i, tile.Y * cells + j));
        int start = 0, end = ordered.Count;
        Span<TileKey> incident = stackalloc TileKey[4];
        for (int ring = 0; ring < rings && start < end; ring++)
        {
            for (int index = start; index < end; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                TileKey cell = ordered[index];
                for (int y = 0; y <= 1; y++)
                for (int x = 0; x <= 1; x++)
                {
                    int count = lattice.IncidentCells(lattice.VertexKey(cell.Face, cell.X + x, cell.Y + y), incident);
                    for (int next = 0; next < count; next++) Add(incident[next]);
                }
            }
            start = end; end = ordered.Count;
        }
        ordered.Sort(static (a, b) => a.Packed.CompareTo(b.Packed));
        var vertices = new HashSet<CubeVertexKey>();
        foreach (TileKey cell in ordered)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 0; y <= 1; y++)
            for (int x = 0; x <= 1; x++)
            {
                vertices.Add(lattice.VertexKey(cell.Face, cell.X + x, cell.Y + y));
                if (vertices.Count > limits.MaxVertices)
                    throw new FormationCapacityException("Canonical collision vertices exceed their caller allowance.");
            }
        }
        TerrainCollisionFootprint footprint = TerrainCollisionFootprint.Of(lattice, ordered, cancellation);
        cancellation.ThrowIfCancellationRequested();
        return new TerrainCollisionPlan(lattice, tile, cells, ordered.ToArray(), vertices.Count, footprint);

        void Add(TileKey cell)
        {
            if (seen.Contains(cell)) return;
            if (ordered.Count == limits.MaxCells)
                throw new FormationCapacityException("Canonical collision cell capacity exceeded.");
            seen.Add(cell); ordered.Add(cell);
        }
    }

    /// <summary>Samples an admitted topology; selected-world callers separately authenticate world ownership.</summary>
    public static TriangleMesh BuildCollision(ISurface surface, in ZoneFrame frame, TerrainCollisionPlan plan,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(plan); cancellation.ThrowIfCancellationRequested();
        TerrainLattice lattice = TerrainLattice.Of(surface) ?? throw new ArgumentException("A canonical surface is required.", nameof(surface));
        if (lattice.Radius != plan.Lattice.Radius || lattice.VertexLevel != plan.Lattice.VertexLevel)
            throw new FormationValidationException("Collision topology belongs to a different lattice.");
        TerrainPinSet? pins = TerrainPinSet.Of(surface);
        ZoneFrame outputFrame = frame;
        if (plan.IsCore)
        {
            // Preserve the established zero-margin row-major vertex/index order exactly.
            int cells = plan.CoreCells, n = cells + 1;
            var corePositions = new float[plan.VertexCount * 3];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var point = Position(lattice.VertexKey(plan.Tile.Face, plan.Tile.X * cells + x, plan.Tile.Y * cells + y));
                int k = (y * n + x) * 3; corePositions[k] = point.X; corePositions[k + 1] = point.Y; corePositions[k + 2] = point.Z;
            }
            var coreIndices = new int[plan.TriangleCount * 3]; int coreAt = 0;
            for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
                coreIndices[coreAt++] = a; coreIndices[coreAt++] = b; coreIndices[coreAt++] = d;
                coreIndices[coreAt++] = a; coreIndices[coreAt++] = d; coreIndices[coreAt++] = c;
            }
            cancellation.ThrowIfCancellationRequested();
            return new TriangleMesh(corePositions, coreIndices);
        }
        var vertexIndices = new Dictionary<CubeVertexKey, int>();
        var positions = new List<float>();
        var indices = new int[plan.TriangleCount * 3];
        int at = 0;
        foreach (TileKey cell in plan.Cells)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int half = 0; half < 2; half++)
            {
                TerrainLatticeTriangle triangle = lattice.Triangle(cell, half);
                indices[at++] = Vertex(triangle.A);
                indices[at++] = Vertex(triangle.B);
                indices[at++] = Vertex(triangle.C);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        if (vertexIndices.Count != plan.VertexCount)
            throw new FormationValidationException("Collision topology count changed after planning.");
        return new TriangleMesh(positions.ToArray(), indices);

        int Vertex(CubeVertexKey key)
        {
            if (vertexIndices.TryGetValue(key, out int index)) return index;
            var local = Position(key);
            index = vertexIndices.Count;
            vertexIndices.Add(key, index);
            positions.Add(local.X); positions.Add(local.Y); positions.Add(local.Z);
            return index;
        }

        System.Numerics.Vector3 Position(CubeVertexKey key)
        {
            cancellation.ThrowIfCancellationRequested();
            Vector3D point;
            if (pins != null && pins.TryGet(key, out TerrainPinnedVertex pin)) point = pin.Position;
            else
            {
                Vector3D direction = lattice.Direction(key);
                double radial = surface.Radius + surface.HeightAt(direction);
                if (!double.IsFinite(radial) || radial <= 0)
                    throw new FormationValidationException("Canonical collision requires finite positive radial terrain.");
                point = direction * radial;
            }
            System.Numerics.Vector3 local = outputFrame.ToLocal(point);
            if (!float.IsFinite(local.X) || !float.IsFinite(local.Y) || !float.IsFinite(local.Z))
                throw new FormationValidationException("Canonical collision position must be finite in its output frame.");
            return local;
        }
    }

    internal static (long Scratch, long Mesh) ValidateLimits(TerrainCollisionLimits limits)
    {
        if (limits.MaxCells < 1 || limits.MaxCells > MaximumCanonicalCells ||
            limits.MaxVertices < 1 || limits.MaxVertices > MaximumCanonicalVertices ||
            limits.MaxEstimatedScratchBytes < 1 || limits.MaxEstimatedScratchBytes > MaximumCanonicalScratchBytes ||
            limits.MaxEstimatedMeshBytes < 1 || limits.MaxEstimatedMeshBytes > MaximumCanonicalMeshBytes)
            throw new ArgumentOutOfRangeException(nameof(limits), "Canonical collision limits exceed hard ceilings.");
        // Includes simultaneous hash/list capacities, dictionary growth, float-list backing and final arrays.
        // These conservative managed estimates do not claim to measure the physics engine's native shape memory.
        long scratch = 16384L + 128L * limits.MaxCells + 160L * limits.MaxVertices;
        long mesh = 4096L + 24L * limits.MaxCells + 12L * limits.MaxVertices;
        if (scratch > limits.MaxEstimatedScratchBytes || mesh > limits.MaxEstimatedMeshBytes)
            throw new FormationCapacityException("Canonical collision reservation exceeds its caller allowance.");
        return (scratch, mesh);
    }
}
