using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class TerrainLatticeTests
{
    [Test]
    public static void CanonicalResolutionScalesWithTheBodyAndBoundsEnumeration()
    {
        foreach (double radius in new[] { 2.0, 30.0, 1200.0, 1354.0 })
        {
            TerrainLattice lattice = TerrainLattice.ForRadius(radius);
            Assert.Equal(lattice.FinestTileLevel + 5, lattice.VertexLevel, "Thirty-two render cells define the canonical vertex level.");
            var tile = new TileKey(0, lattice.FinestTileLevel, 0, 0);
            Assert.Equal(32, lattice.CellsForTile(tile), "The finest render tile always has thirty-two cells.");
            Assert.Equal(1089, lattice.EnumerateVertices(tile, 1089).Count(), "Only the requested tile's vertices are enumerated.");
            Assert.Equal(2048, lattice.EnumerateTriangles(tile, 2048).Count(), "Only the requested tile's triangles are enumerated.");
            Throws<FormationCapacityException>(() => lattice.EnumerateTriangles(tile, 2047));
            Throws<FormationCapacityException>(() => lattice.EnumerateVertices(tile, 1088));
        }
        var deep = new TerrainLattice(1200, 6);
        Throws<ArgumentOutOfRangeException>(() => deep.CellsForTile(new TileKey(0, 3, 0, 0)));
        Throws<ArgumentOutOfRangeException>(() => deep.CellsForTile(new TileKey(0, deep.VertexLevel + 1, 0, 0)));
    }

    [Test]
    public static void CanonicalTrianglesMatchTheExistingDiagonalAndWinding()
    {
        var lattice = new TerrainLattice(1200, 2);
        var tile = new TileKey(4, 2, 1, 2);
        CubeVertexKey[] vertices = lattice.EnumerateVertices(tile).ToArray();
        TerrainLatticeTriangle[] triangles = lattice.EnumerateTriangles(tile).ToArray();
        int[] indices = TileMesh.Indices(32);
        for (int i = 0; i < triangles.Length; i++)
        {
            TerrainLatticeTriangle triangle = triangles[i];
            Assert.Equal(vertices[indices[3 * i]], triangle.A, "The first vertex matches the shared grid topology.");
            Assert.Equal(vertices[indices[3 * i + 1]], triangle.B, "The second vertex matches the shared grid topology.");
            Assert.Equal(vertices[indices[3 * i + 2]], triangle.C, "The third vertex matches the shared grid topology.");
            Assert.Equal(i & 1, triangle.Half, "Primitive halves have stable row-major identity.");
            Vector3D a = lattice.Direction(triangle.A), b = lattice.Direction(triangle.B), c = lattice.Direction(triangle.C);
            Assert.True(Vector3D.Dot(Vector3D.Cross(b - a, c - a), a) > 0, "Every canonical triangle points out of the body.");
        }
    }

    [Test]
    public static void FaceEdgesAndCornersHaveExactlyOneCanonicalPosition()
    {
        var lattice = new TerrainLattice(1200, 1);
        var directions = new Dictionary<CubeVertexKey, Vector3D>();
        int duplicates = 0;
        for (int face = 0; face < 6; face++)
        for (int y = 0; y <= 64; y += 8)
        for (int x = 0; x <= 64; x += 8)
        {
            if (x != 0 && x != 64 && y != 0 && y != 64) continue;
            int tx = Math.Min(1, x / 32), ty = Math.Min(1, y / 32);
            Vector3D direction = lattice.GridDirection(new TileKey(face, 1, tx, ty), 32, x - 32 * tx, y - 32 * ty);
            CubeVertexKey key = lattice.VertexKey(face, x, y);
            if (directions.TryGetValue(key, out Vector3D previous))
            {
                Assert.Equal(previous, direction, "Incident faces use identical directions, including corners.");
                duplicates++;
            }
            else directions.Add(key, direction);
        }
        Assert.True(duplicates > 80, "The test exercises shared edges and all eight corners.");
    }

    [Test]
    public static void CanonicalDirectionsRejectReducedHalfStepsButAcceptNestedAndSharedKeys()
    {
        var lattice = new TerrainLattice(1200, 1);
        Assert.Equal(6, lattice.VertexLevel, "The fixture selects the level-six canonical vertex grid.");
        for (int face = 0; face < 6; face++)
        {
            CubeVertexKey halfStep = CubeTopology.VertexKey(face, 7, 1, 1);
            Assert.Equal(6, halfStep.Level, "Reduction hides the finer source grid level.");
            Throws<ArgumentException>(() => lattice.Direction(halfStep));
            Throws<ArgumentException>(() => lattice.Direction(CubeTopology.VertexKey(face, 7, 0, 1)));
            for (int sourceLevel = 0; sourceLevel <= lattice.VertexLevel; sourceLevel++)
            {
                int sourceSide = 1 << sourceLevel, scale = 1 << (lattice.VertexLevel - sourceLevel);
                foreach (int x in new[] { 0, sourceSide / 2, sourceSide })
                foreach (int y in new[] { 0, sourceSide / 2, sourceSide })
                {
                    CubeVertexKey coarse = CubeTopology.VertexKey(face, sourceLevel, x, y);
                    CubeVertexKey canonical = lattice.VertexKey(face, x * scale, y * scale);
                    Assert.Equal(canonical, coarse, "Nested grids retain exactly the canonical identity, including seams.");
                    Assert.Equal(lattice.Direction(canonical), lattice.Direction(coarse),
                        "A nested key resolves to the identical canonical direction.");
                }
            }
        }
    }

    [Test]
    public static void OptedInRenderAndCollisionUseTheSameVerticesAcrossRadii()
    {
        foreach (double radius in new[] { 2.0, 30.0, 1200.0, 1354.0 })
        {
            var surface = new SelectedSurface(radius, TerrainLattice.ForRadius(radius));
            TerrainLattice lattice = surface.TerrainLattice!;
            foreach (int face in new[] { 0, 2, 5 })
            {
                var tile = new TileKey(face, lattice.FinestTileLevel, 0, 0);
                TileGrid grid = TileMesh.Build(surface, tile);
                TriangleMesh collision = SurfaceGrid.Collision(surface, ZoneFrame.World, tile, 0, spacing: 7);
                Assert.Equal(grid.VertexCount, collision.VertexCount, "Explicit legacy spacing cannot override an opted-in lattice.");
                for (int i = 0; i < grid.VertexCount; i++)
                {
                    var body = new Vector3D(collision.Positions[3 * i], collision.Positions[3 * i + 1], collision.Positions[3 * i + 2]);
                    Assert.True((grid.Vertex(i) - body).Length < 0.0003, "Render origins and collision floats agree within 0.3 mm.");
                }
                int[] indices = TileMesh.Indices(32);
                Assert.True(indices.AsSpan().SequenceEqual(collision.Indices), "Render and collision diagonals agree exactly.");
            }
        }
    }

    [Test]
    public static void NullOptInPreservesAllLegacyGridOutputs()
    {
        ISurface legacy = new PlainSurface(1200);
        ISurface inactive = new SelectedSurface(1200, null);
        var tile = new TileKey(0, 5, 7, 9);
        TileGrid a = TileMesh.Build(legacy, tile), b = TileMesh.Build(inactive, tile);
        Assert.True(a.Positions.AsSpan().SequenceEqual(b.Positions), "Null opt-in preserves legacy render positions exactly.");
        Assert.True(a.MorphPositions.AsSpan().SequenceEqual(b.MorphPositions), "Null opt-in preserves legacy morphs exactly.");
        Assert.True(a.Normals.AsSpan().SequenceEqual(b.Normals), "Null opt-in preserves legacy normals exactly.");
        TriangleMesh ca = SurfaceGrid.Collision(legacy, ZoneFrame.World, tile, 2, 1);
        TriangleMesh cb = SurfaceGrid.Collision(inactive, ZoneFrame.World, tile, 2, 1);
        Assert.True(ca.Positions.AsSpan().SequenceEqual(cb.Positions), "Null opt-in preserves legacy collision and margin vertices exactly.");
        Assert.True(ca.Indices.AsSpan().SequenceEqual(cb.Indices), "Null opt-in preserves legacy collision topology exactly.");
    }

    [Test]
    public static void CoarserPhysicsTileContainsTheSameFineRenderLattice()
    {
        var lattice = TerrainLattice.ForRadius(1200);
        var surface = new SelectedSurface(1200, lattice);
        var parent = new TileKey(0, lattice.FinestTileLevel - 1, 7, 9);
        Assert.Equal(64, lattice.CellsForTile(parent), "A physics tile twice as wide retains the same vertex spacing.");
        TriangleMesh collision = SurfaceGrid.Collision(surface, ZoneFrame.World, parent, 0, 1);
        for (int childIndex = 0; childIndex < 4; childIndex++)
        {
            TileKey child = parent.Child(childIndex);
            TileGrid grid = TileMesh.Build(surface, child);
            int startX = (child.X - parent.X * 2) * 32, startY = (child.Y - parent.Y * 2) * 32;
            for (int j = 0; j <= 32; j++)
            for (int i = 0; i <= 32; i++)
            {
                int index = (startY + j) * 65 + startX + i;
                var point = new Vector3D(collision.Positions[3 * index], collision.Positions[3 * index + 1], collision.Positions[3 * index + 2]);
                Assert.True((grid.Vertex(j * 33 + i) - point).Length < 0.0003, "Every child render vertex corresponds to the parent physics lattice.");
            }
        }
    }

    [Test]
    public static void OptedInCollisionMarginsAreBoundedAndSharedOnOneFace()
    {
        var lattice = TerrainLattice.ForRadius(1200);
        var surface = new SelectedSurface(1200, lattice);
        var tile = new TileKey(0, lattice.FinestTileLevel, 7, 9);
        TriangleMesh a = SurfaceGrid.Collision(surface, ZoneFrame.World, tile, 2, 1);
        TriangleMesh b = SurfaceGrid.Collision(surface, ZoneFrame.World,
            new TileKey(tile.Face, tile.Level, tile.X + 1, tile.Y), 2, 1);
        var points = new HashSet<(float, float, float)>();
        for (int i = 0; i < a.VertexCount; i++) points.Add((a.Positions[3 * i], a.Positions[3 * i + 1], a.Positions[3 * i + 2]));
        int shared = 0;
        for (int i = 0; i < b.VertexCount; i++)
            if (points.Contains((b.Positions[3 * i], b.Positions[3 * i + 1], b.Positions[3 * i + 2]))) shared++;
        Assert.True(shared > 100, "Adjacent collision patches share their entire overlap exactly.");
        Throws<ArgumentOutOfRangeException>(() => SurfaceGrid.Collision(surface, ZoneFrame.World, tile, double.PositiveInfinity, 1));
        Throws<ArgumentOutOfRangeException>(() => SurfaceGrid.Collision(surface, ZoneFrame.World, tile, 1000, 1));
    }

    [Test]
    public static void AdmissionRejectsInvalidOwnershipAndCancelledEnumeration()
    {
        Throws<ArgumentOutOfRangeException>(() => new TerrainLattice(double.NaN, 1));
        Throws<ArgumentOutOfRangeException>(() => new TerrainLattice(0, 1));
        Throws<ArgumentOutOfRangeException>(() => new TerrainLattice(1200, 24));
        var lattice = new TerrainLattice(1200, 1);
        var tile = new TileKey(0, 1, 0, 0);
        Throws<ArgumentException>(() => TerrainLattice.Of(new SelectedSurface(1000, lattice)));
        Throws<ArgumentException>(() => TileMesh.Build(new SelectedSurface(1200, lattice), tile, 16));
        Throws<ArgumentException>(() => TileMesh.Build(new SelectedSurface(1200, lattice), tile.Child(0)));
        Throws<ArgumentException>(() => lattice.Direction(default));
        Throws<ArgumentOutOfRangeException>(() => lattice.EnumerateTriangles(tile, -1));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Throws<OperationCanceledException>(() => lattice.EnumerateTriangles(tile, cancellation: cancelled.Token));
        using var running = new CancellationTokenSource();
        using IEnumerator<TerrainLatticeTriangle> iterator = lattice.EnumerateTriangles(tile, cancellation: running.Token).GetEnumerator();
        Assert.True(iterator.MoveNext(), "The admitted operation starts.");
        running.Cancel();
        Throws<OperationCanceledException>(() => iterator.MoveNext());
    }

    private class PlainSurface(double radius) : ISurface
    {
        public double Radius { get; } = radius;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0.02 * (2 * direction.X - 3 * direction.Y + direction.Z * direction.Z);
    }

    private sealed class SelectedSurface(double radius, TerrainLattice? lattice) : PlainSurface(radius), ITerrainLatticeSurface
    {
        public TerrainLattice? TerrainLattice { get; } = lattice;
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
