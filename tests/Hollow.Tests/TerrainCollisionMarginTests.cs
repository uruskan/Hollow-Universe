using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class TerrainCollisionMarginTests
{
    [Test]
    public static void EveryDirectedFaceSeamUsesCanonicalTrianglesWithoutHoles()
    {
        var terrain = new Terrain(1200, new TerrainLattice(1200, 0));
        TerrainLattice lattice = terrain.TerrainLattice;
        Dictionary<Points, TerrainLatticeTriangle> canonical = Reference(terrain, Enumerable.Range(0, 6).Select(f => new TileKey(f, 0, 0, 0)));
        int side = 1 << lattice.VertexLevel;
        for (int face = 0; face < 6; face++)
        foreach (CubeEdge edge in Enum.GetValues<CubeEdge>())
        {
            var cell = edge switch
            {
                CubeEdge.UMin => new TileKey(face, lattice.VertexLevel, 0, side / 2),
                CubeEdge.UMax => new TileKey(face, lattice.VertexLevel, side - 1, side / 2),
                CubeEdge.VMin => new TileKey(face, lattice.VertexLevel, side / 2, 0),
                _ => new TileKey(face, lattice.VertexLevel, side / 2, side - 1),
            };
            TriangleMesh mesh = Collision(terrain, cell, 1);
            HashSet<TileKey> cells = CheckCanonical(mesh, canonical);
            Assert.Equal(9, cells.Count, "An edge ring retains the nine-cell square through both incident faces.");
            Assert.True(cells.Contains(CubeTopology.Neighbor(cell, edge)), "The actual adjacent-face edge cell is present.");
            Assert.Equal(2, cells.Select(c => c.Face).Distinct().Count(), "An edge midpoint crosses exactly two cube faces.");
            CheckManifold(mesh, closed: false);
        }
    }

    [Test]
    public static void CubeCornersIncludeAllThreeFacesAndCompleteIncidentFans()
    {
        var terrain = new Terrain(1200, new TerrainLattice(1200, 0));
        TerrainLattice lattice = terrain.TerrainLattice;
        Dictionary<Points, TerrainLatticeTriangle> canonical = Reference(terrain, Enumerable.Range(0, 6).Select(f => new TileKey(f, 0, 0, 0)));
        int side = 1 << lattice.VertexLevel;
        for (int face = 0; face < 6; face++)
        foreach (int x in new[] { 0, side - 1 })
        foreach (int y in new[] { 0, side - 1 })
        {
            var core = new TileKey(face, lattice.VertexLevel, x, y);
            TriangleMesh mesh = Collision(terrain, core, 1);
            HashSet<TileKey> cells = CheckCanonical(mesh, canonical);
            Assert.Equal(8, cells.Count, "A cube corner has three quadrants, giving eight distinct cells in the first ring.");
            Assert.Equal(3, cells.Select(c => c.Face).Distinct().Count(), "Corner coverage includes all three incident faces.");
            var coreKeys = new HashSet<CubeVertexKey> { lattice.VertexKey(face, x, y), lattice.VertexKey(face, x + 1, y),
                lattice.VertexKey(face, x, y + 1), lattice.VertexKey(face, x + 1, y + 1) };
            foreach (TerrainLatticeTriangle triangle in canonical.Values)
                if (coreKeys.Contains(triangle.A) || coreKeys.Contains(triangle.B) || coreKeys.Contains(triangle.C))
                    Assert.True(cells.Contains(triangle.Cell), "Every triangle touching a core vertex belongs to the collision ring.");
            CheckManifold(mesh, closed: false);
        }
    }

    [Test]
    public static void PracticalTwoMetreMarginsRemainCanonicalOnSmallAndMaximumWorlds()
    {
        foreach (double radius in new[] { 2.0, 30.0, 1200.0, 1354.0 })
        {
            var terrain = new Terrain(radius, TerrainLattice.ForRadius(radius));
            var tile = new TileKey(5, terrain.TerrainLattice.FinestTileLevel, 0, 0);
            var referenceTiles = new HashSet<TileKey> { tile };
            // Two tile-edge hops conservatively include the few-cell margin, including both faces at a corner.
            // At level zero this intentionally enumerates all six small-world faces, not a global production path.
            for (int ring = 0; ring < 2; ring++)
                foreach (TileKey current in referenceTiles.ToArray())
                foreach (CubeEdge edge in Enum.GetValues<CubeEdge>()) referenceTiles.Add(CubeTopology.Neighbor(current, edge));
            Dictionary<Points, TerrainLatticeTriangle> canonical = Reference(terrain, referenceTiles);
            TriangleMesh mesh = SurfaceGrid.Collision(terrain, ZoneFrame.World, tile, 2, 7);
            HashSet<TileKey> cells = CheckCanonical(mesh, canonical);
            Assert.True(cells.Count > 1024, "A positive margin adds real canonical cells to the finest render tile.");
            foreach (TerrainLatticeTriangle triangle in terrain.TerrainLattice.EnumerateTriangles(tile))
                Assert.True(cells.Contains(triangle.Cell), "The entire requested core remains covered.");
            Assert.True(mesh.VertexCount < SurfaceGrid.MaximumCanonicalVertices, "The smallest and largest practical cases stay bounded.");
            CheckManifold(mesh, closed: cells.Count == TileKey.CountAt(terrain.TerrainLattice.VertexLevel));
        }
    }

    [Test]
    public static void MaximumMarginOnATinyWorldSaturatesAsOneClosedCanonicalSphere()
    {
        var terrain = new Terrain(2, TerrainLattice.ForRadius(2));
        var tile = new TileKey(0, terrain.TerrainLattice.FinestTileLevel, 0, 0);
        Dictionary<Points, TerrainLatticeTriangle> canonical = Reference(terrain,
            Enumerable.Range(0, 6).Select(face => new TileKey(face, 0, 0, 0)));
        double margin = TerrainLattice.MaximumMarginCells * CellMetres(terrain);
        TriangleMesh mesh = SurfaceGrid.Collision(terrain, ZoneFrame.World, tile, margin, 1);
        HashSet<TileKey> cells = CheckCanonical(mesh, canonical);
        Assert.Equal(6144, cells.Count, "The maximum admitted margin saturates at every canonical cell of the tiny world.");
        Assert.Equal(6, cells.Select(cell => cell.Face).Distinct().Count(), "All six faces appear exactly once in the bounded sphere.");
        Assert.Equal(6146, mesh.VertexCount, "The closed cube lattice welds its face edges and eight corners.");
        CheckManifold(mesh, closed: true);
    }

    [Test]
    public static void InteriorRingsAndRepeatedCrossFaceRequestsHaveStableOrderAndOverlap()
    {
        var terrain = new Terrain(1200, new TerrainLattice(1200, 0));
        int level = terrain.TerrainLattice.VertexLevel;
        TriangleMesh interior = Collision(terrain, new TileKey(0, level, 15, 15), 2);
        Assert.Equal(25 * 2, interior.Indices.Length / 3, "Two vertex-neighbor rings preserve the five-by-five interior square.");
        var first = new TileKey(2, level, 0, 15);
        TileKey second = CubeTopology.Neighbor(first, CubeEdge.UMin);
        TriangleMesh a = Collision(terrain, first, 2), b = Collision(terrain, second, 2);
        TriangleMesh bAgain = Collision(terrain, second, 2), aAgain = Collision(terrain, first, 2);
        Assert.True(a.Positions.AsSpan().SequenceEqual(aAgain.Positions) && a.Indices.AsSpan().SequenceEqual(aAgain.Indices),
            "Request order does not affect the first patch's vertex or triangle order.");
        Assert.True(b.Positions.AsSpan().SequenceEqual(bAgain.Positions) && b.Indices.AsSpan().SequenceEqual(bAgain.Indices),
            "Request order does not affect the adjacent patch's vertex or triangle order.");
        var triangles = Enumerable.Range(0, a.Indices.Length / 3).Select(i => TrianglePoints(a, i)).ToHashSet();
        int shared = Enumerable.Range(0, b.Indices.Length / 3).Count(i => triangles.Contains(TrianglePoints(b, i)));
        Assert.Equal(40, shared, "Two five-by-five patches one cell apart share twenty complete cells with identical winding.");
        CheckManifold(a, closed: false); CheckManifold(b, closed: false);
    }

    [Test]
    public static void AdjacentFaceAndCornerMarginsUseTheRenderersFrozenPins()
    {
        foreach (bool corner in new[] { false, true })
        {
            var terrain = new Terrain(1200, new TerrainLattice(1200, 1));
            TerrainLattice lattice = terrain.TerrainLattice;
            var core = new TileKey(0, lattice.VertexLevel, 0, corner ? 0 : 20);
            TileKey neighbor = CubeTopology.Neighbor(core, CubeEdge.UMin);
            CubeVertexKey key = corner ? lattice.VertexKey(0, 0, 0) :
                new[] { lattice.Triangle(neighbor, 0).A, lattice.Triangle(neighbor, 0).B,
                    lattice.Triangle(neighbor, 0).C, lattice.Triangle(neighbor, 1).C }
                .First(k => !new[] { lattice.Triangle(core, 0).A, lattice.Triangle(core, 0).B,
                    lattice.Triangle(core, 0).C, lattice.Triangle(core, 1).C }.Contains(k));
            Vector3D direction = lattice.Direction(key);
            var pin = new FormationTerrainPin(key, direction * (terrain.Radius + terrain.HeightAt(direction) + 0.015));
            Install(terrain, pin);
            TriangleMesh mesh = Collision(terrain, core, 1);
            Vector3 expected = ZoneFrame.World.ToLocal(pin.Position);
            Assert.Equal(1, Enumerable.Range(0, mesh.VertexCount).Count(i => Point(mesh, i) == expected),
                "The cross-face frozen vertex appears exactly once in the welded collision patch.");
            TileKey renderTile = neighbor;
            while (renderTile.Level > lattice.FinestTileLevel) renderTile = renderTile.Parent;
            CubeVertexKey[] renderKeys = lattice.EnumerateVertices(renderTile).ToArray();
            int at = Array.IndexOf(renderKeys, key);
            Assert.True(at >= 0, "The adjacent render tile owns the same canonical key.");
            TileGrid render = TileMesh.Build(terrain, renderTile);
            Assert.True((render.Vertex(at) - pin.Position).Length < 0.0001, "Rendering and collision honor the same frozen position.");
            TerrainPinnedVertex frozen = terrain.TerrainPins!.Vertices[0];
            Assert.Equal((float)frozen.Normal.X, render.Normals[3 * at], "The renderer retains the canonical cross-face normal stencil.");
            Assert.Equal(render.Normals[3 * at], render.MorphNormals[3 * at], "The frozen normal cannot change under camera morph.");
            var frame = new ZoneFrame(pin.Position);
            TriangleMesh local = SurfaceGrid.Collision(terrain, frame, core, CellMetres(terrain) * 0.5, 1, Limits());
            Assert.True(Enumerable.Range(0, local.VertexCount).Any(i => Point(local, i) == Vector3.Zero),
                "Canonical margins also apply frozen body positions before conversion to a tangent frame.");
            CheckManifold(mesh, closed: false);
        }
    }

    [Test]
    public static void CapacityRefusesWholeOperationsAndBoundsCoreRingsVerticesAndReservations()
    {
        var terrain = new Terrain(1200, new TerrainLattice(1200, 0));
        var cell = new TileKey(0, terrain.TerrainLattice.VertexLevel, 15, 15);
        TerrainCollisionLimits limits = Limits();
        Throws<FormationCapacityException>(() => Collision(terrain, cell, 1, limits with { MaxCells = 8 }));
        Assert.Equal(0, terrain.Samples, "A cell-capacity refusal happens before sampling or publishing geometry.");
        Throws<FormationCapacityException>(() => Collision(terrain, cell, 1, limits with { MaxEstimatedScratchBytes = 1 }));
        Throws<FormationCapacityException>(() => Collision(terrain, cell, 1, limits with { MaxEstimatedMeshBytes = 1 }));
        Throws<FormationCapacityException>(() => SurfaceGrid.Collision(terrain, ZoneFrame.World, new TileKey(0, 0, 0, 0), 0, 1, limits));
        Assert.Equal(0, terrain.Samples, "Reservation and zero-margin core failures also precede any sample.");
        Throws<FormationCapacityException>(() => Collision(terrain, cell, 1, limits with { MaxVertices = 4 }));
        Throws<ArgumentOutOfRangeException>(() => Collision(terrain, cell, 1, limits with { MaxCells = SurfaceGrid.MaximumCanonicalCells + 1 }));
        Throws<ArgumentOutOfRangeException>(() => Collision(terrain, cell, 1, limits with { MaxVertices = SurfaceGrid.MaximumCanonicalVertices + 1 }));
        Throws<ArgumentOutOfRangeException>(() => Collision(terrain, cell, 1, limits with { MaxEstimatedScratchBytes = SurfaceGrid.MaximumCanonicalScratchBytes + 1 }));
        TriangleMesh recovered = Collision(terrain, cell, 1);
        Assert.Equal(18, recovered.Indices.Length / 3, "A later adequately bounded operation returns a complete patch after earlier failures.");
    }

    [Test]
    public static void CancellationInvalidMarginsAndNonfiniteSamplesCannotPublishPartialMeshes()
    {
        var terrain = new Terrain(1200, new TerrainLattice(1200, 0));
        var cell = new TileKey(0, terrain.TerrainLattice.VertexLevel, 0, 15);
        foreach (double margin in new[] { -1.0, double.NaN, double.PositiveInfinity, CellMetres(terrain) * 129 })
            Throws<ArgumentOutOfRangeException>(() => SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, margin, 1, Limits()));
        using var before = new CancellationTokenSource(); before.Cancel();
        Throws<OperationCanceledException>(() => SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, 2, 1, Limits(), before.Token));
        Assert.Equal(0, terrain.Samples, "Invalid requests and cancellation are refused before source sampling.");
        using var during = new CancellationTokenSource();
        terrain.SampleAction = during.Cancel;
        Throws<OperationCanceledException>(() => SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, 2, 1, Limits(), during.Token));
        terrain.SampleAction = null; terrain.BadHeight = true;
        Throws<FormationValidationException>(() => Collision(terrain, cell, 1));
    }

    [Test]
    public static void ZeroMarginRefusesNonfiniteSamplesAndCancellationOnTheFinalVertex()
    {
        var invalid = new Terrain(1200, new TerrainLattice(1200, 0)) { BadHeight = true };
        var cell = new TileKey(0, invalid.TerrainLattice.VertexLevel, 15, 15);
        Throws<FormationValidationException>(() => SurfaceGrid.Collision(invalid, ZoneFrame.World, cell, 0, 1, Limits()));
        Assert.Equal(1, invalid.Samples, "Zero-margin collision rejects the first nonfinite sample instead of publishing it.");
        var terrain = new Terrain(1200, new TerrainLattice(1200, 0));
        using var finalVertex = new CancellationTokenSource();
        terrain.SampleAction = () => { if (terrain.Samples == 4) finalVertex.Cancel(); };
        Throws<OperationCanceledException>(() => SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, 0, 1, Limits(), finalVertex.Token));
        Assert.Equal(4, terrain.Samples, "Cancellation from the last of four zero-margin vertices is checked before publication.");
        terrain.SampleAction = null;
        TriangleMesh mesh = SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, 0, 1, Limits());
        Assert.Equal(4, mesh.VertexCount, "A later uncancelled zero-margin operation still returns the complete row-major grid.");
        Assert.True(mesh.Indices.AsSpan().SequenceEqual(new[] { 0, 1, 3, 0, 3, 2 }), "Zero-margin diagonal and vertex order remain unchanged.");
    }

    [Test]
    public static void ZeroMarginAdmitsOnlyItsCompleteScratchAndOutputReservations()
    {
        var terrain = new Terrain(1200, new TerrainLattice(1200, 0));
        var cell = new TileKey(0, terrain.TerrainLattice.VertexLevel, 15, 15);
        var exact = new TerrainCollisionLimits
        {
            MaxCells = 1, MaxVertices = 4,
            MaxEstimatedScratchBytes = 16384 + 128 + 160 * 4,
            MaxEstimatedMeshBytes = 4096 + 24 + 12 * 4,
        };
        Throws<FormationCapacityException>(() => SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, 0, 1,
            exact with { MaxEstimatedScratchBytes = exact.MaxEstimatedScratchBytes - 1 }));
        Throws<FormationCapacityException>(() => SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, 0, 1,
            exact with { MaxEstimatedMeshBytes = exact.MaxEstimatedMeshBytes - 1 }));
        Assert.Equal(0, terrain.Samples, "Both zero-margin byte refusals happen before allocating or sampling the terrain grid.");
        TriangleMesh mesh = SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, 0, 1, exact);
        Assert.Equal(4, mesh.VertexCount, "The exact zero-margin reservation admits all four grid vertices.");
        Assert.True(mesh.Indices.AsSpan().SequenceEqual(new[] { 0, 1, 3, 0, 3, 2 }), "Byte admission preserves the complete row-major topology.");
    }

    private static TerrainCollisionLimits Limits() => new()
    { MaxCells = 64, MaxVertices = 128, MaxEstimatedScratchBytes = 1024 * 1024, MaxEstimatedMeshBytes = 1024 * 1024 };
    private static double CellMetres(Terrain terrain) => TileKey.EdgeLength(terrain.TerrainLattice.VertexLevel, terrain.Radius);
    private static TriangleMesh Collision(Terrain terrain, TileKey cell, int rings, TerrainCollisionLimits? limits = null) =>
        SurfaceGrid.Collision(terrain, ZoneFrame.World, cell, (rings - 0.5) * CellMetres(terrain), 1, limits ?? Limits());
    private readonly record struct Points(Vector3 A, Vector3 B, Vector3 C);
    private static Vector3 Point(TriangleMesh mesh, int at) => new(mesh.Positions[at * 3], mesh.Positions[at * 3 + 1], mesh.Positions[at * 3 + 2]);
    private static Points TrianglePoints(TriangleMesh mesh, int at) => new(Point(mesh, mesh.Indices[3 * at]),
        Point(mesh, mesh.Indices[3 * at + 1]), Point(mesh, mesh.Indices[3 * at + 2]));

    private static Dictionary<Points, TerrainLatticeTriangle> Reference(Terrain terrain, IEnumerable<TileKey> tiles)
    {
        var result = new Dictionary<Points, TerrainLatticeTriangle>();
        foreach (TileKey tile in tiles)
        foreach (TerrainLatticeTriangle triangle in terrain.TerrainLattice.EnumerateTriangles(tile))
            result.Add(new Points(At(triangle.A), At(triangle.B), At(triangle.C)), triangle);
        return result;
        Vector3 At(CubeVertexKey key)
        {
            Vector3D direction = terrain.TerrainLattice.Direction(key);
            return ZoneFrame.World.ToLocal(direction * (terrain.Radius + terrain.HeightAt(direction)));
        }
    }

    private static HashSet<TileKey> CheckCanonical(TriangleMesh mesh, Dictionary<Points, TerrainLatticeTriangle> reference)
    {
        var cells = new HashSet<TileKey>();
        ulong previous = 0;
        for (int i = 0; i < mesh.Indices.Length / 3; i++)
        {
            Points points = TrianglePoints(mesh, i);
            Assert.True(reference.TryGetValue(points, out TerrainLatticeTriangle triangle),
                "Every collision triangle is an exact canonical primitive, including its diagonal and outward winding.");
            Assert.Equal(i & 1, triangle.Half, "Each cell emits its two canonical halves consecutively.");
            if ((i & 1) == 0)
            {
                Assert.True(cells.Add(triangle.Cell), "No canonical cell is duplicated in the collision patch.");
                if (i > 0) Assert.True(previous < triangle.Cell.Packed, "Cells publish in stable canonical-key order.");
                previous = triangle.Cell.Packed;
            }
            Assert.True(Vector3.Dot(Vector3.Cross(points.B - points.A, points.C - points.A), points.A) > 0,
                "Every float collision triangle remains nondegenerate and outward at supported body sizes.");
        }
        return cells;
    }

    private static void CheckManifold(TriangleMesh mesh, bool closed)
    {
        var edges = new Dictionary<(int, int), (int Count, int Direction)>();
        var distinctPositions = new HashSet<Vector3>();
        for (int i = 0; i < mesh.VertexCount; i++) Assert.True(distinctPositions.Add(Point(mesh, i)), "Canonical vertices are welded once.");
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Edge(mesh.Indices[i], mesh.Indices[i + 1]); Edge(mesh.Indices[i + 1], mesh.Indices[i + 2]); Edge(mesh.Indices[i + 2], mesh.Indices[i]);
        }
        var boundary = new Dictionary<int, List<int>>();
        foreach (var edge in edges)
        {
            Assert.True(edge.Value.Count is 1 or 2, "No edge has overlapping or nonmanifold triangles.");
            if (edge.Value.Count == 2) Assert.Equal(0, edge.Value.Direction, "Shared triangle edges have opposite winding.");
            else { Boundary(edge.Key.Item1, edge.Key.Item2); Boundary(edge.Key.Item2, edge.Key.Item1); }
        }
        Assert.Equal(closed ? 2 : 1, mesh.VertexCount - edges.Count + mesh.Indices.Length / 3,
            "The connected mesh is a closed sphere or a disk with no interior holes.");
        if (closed) Assert.Equal(0, boundary.Count, "A full small-world mesh has no boundary.");
        else
        {
            Assert.True(boundary.Count > 0, "An open patch retains an outside boundary.");
            foreach (List<int> neighbors in boundary.Values) Assert.Equal(2, neighbors.Count, "The outside boundary has no crack ends or branches.");
            int first = boundary.Keys.First(), previous = -1, current = first, visited = 0;
            do
            {
                List<int> neighbors = boundary[current];
                int next = neighbors[0] == previous ? neighbors[1] : neighbors[0];
                previous = current; current = next; visited++;
                Assert.True(visited <= boundary.Count, "The boundary walk closes without repetition.");
            } while (current != first);
            Assert.Equal(boundary.Count, visited, "Exactly one outside boundary exists, with no hidden interior hole.");
        }
        void Edge(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            edges.TryGetValue(key, out var value);
            edges[key] = (value.Count + 1, value.Direction + (a < b ? 1 : -1));
        }
        void Boundary(int a, int b)
        {
            if (!boundary.TryGetValue(a, out List<int>? neighbors)) boundary.Add(a, neighbors = new List<int>());
            neighbors.Add(b);
        }
    }

    private static void Install(Terrain terrain, FormationTerrainPin pin)
    {
        string identity = new('a', 64), digest = new('b', 64);
        var cohort = new FormationCohort(new FormationId(1, 0), Array.Empty<FormationPiece>(), Array.Empty<FormationContactSealReceipt>(),
            Array.Empty<TerrainLatticeTriangle>(), new[] { pin }, Array.Empty<TileKey>());
        var contract = new FormationFieldContract(identity, terrain.Radius, terrain.TerrainLattice.FinestTileLevel,
            FormationFieldAdmissionPolicy.IndependentPieces);
        terrain.Formations = new FormationField(contract, digest, Array.Empty<FormationPiece>(), new[] { cohort },
            Array.Empty<FormationFieldRejection>(), new FormationSupportIndex(Array.Empty<FormationPiece>()), default, default);
        terrain.TerrainPins = TerrainPinSet.Build(terrain, terrain.TerrainLattice, identity, terrain.Formations,
            new TerrainPinSetLimits { MaxVertices = 16, MaxInputPins = 16, MaxNormalTriangles = 128, MaxAdditionalSamples = 128,
                MaxEstimatedResidentBytes = 8192, MaxEstimatedScratchBytes = 32768 });
    }

    private sealed class Terrain(double radius, TerrainLattice lattice) : ISurface, IGradedTerrain, ITerrainLatticeSurface, IFormationSurface, ITerrainPinnedSurface
    {
        public double Radius { get; } = radius;
        public double SeaRadius => 0;
        public bool IsFinalized => true;
        public TerrainLattice TerrainLattice { get; } = lattice;
        public FormationField? Formations { get; set; }
        public TerrainPinSet? TerrainPins { get; set; }
        public int Samples { get; private set; }
        public bool BadHeight { get; set; }
        public Action? SampleAction { get; set; }
        public double HeightAt(Vector3D direction)
        { Samples++; SampleAction?.Invoke(); return BadHeight ? double.NaN : 0.02 * (direction.X - 2 * direction.Y + direction.Z * direction.Z); }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new("fixture", 10, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
