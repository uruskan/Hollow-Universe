using System;
using System.Linq;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class GroundCollisionPlanTests
{
    [Test]
    public static void GroundLayoutSupportsSmallAndPlayableRadiiAndExplicitlyRefusesUnrepresentableLattices()
    {
        foreach (double radius in new[] { 0.4, 2, 10, 64, 249, 250, 512, 1200, 1354.055000514614, 2410, 6000 })
        {
            TerrainLattice lattice = TerrainLattice.ForRadius(radius);
            GroundCollisionLayout.Validate(lattice);
            int level = GroundCollisionLayout.LevelOn(radius);
            Assert.Equal(Math.Min(TileKey.LevelFor(radius, 64), 6), level, "shared level is exactly the legacy expression at " + radius);
            Assert.True(lattice.CellsForTile(new TileKey(0, level, 0, 0)) <= 128, "admitted physical tile has bounded canonical cells");
        }
        Reject<FormationValidationException>(() => GroundCollisionLayout.Validate(TerrainLattice.ForRadius(0.3)));
        Reject<FormationValidationException>(() => GroundCollisionLayout.Validate(TerrainLattice.ForRadius(9000)));
        GroundCollisionLayout.Validate(new TerrainLattice(1e8, 6));
        Reject<ArgumentOutOfRangeException>(() => GroundCollisionLayout.LevelOn(double.NaN));
        SelectedGroundCollisionLimits limits = SelectedGroundCollisionTests.Limits();
        TerrainLattice tiny = TerrainLattice.ForRadius(0.4);
        Assert.Equal(6144, GroundCollisionLayout.MaximumTopology(tiny).Cells, "small-radius envelope saturates at the entire sphere");
        GroundCollisionLayout.Validate(tiny, limits);
        Reject<FormationCapacityException>(() => GroundCollisionLayout.Validate(tiny, limits with
            { Terrain = limits.Terrain with { MaxCells = 5000 } }));
    }

    [Test]
    public static void GroundAreaIsDeterministicCompleteAcrossFacesAndAdmittedBeforeOutput()
    {
        var limits = new GroundAreaLimits { MaxTiles = GroundCollisionLayout.TileIdCount, MaxEstimatedBytes = 1024 * 1024 };
        GroundAreaPlan all = GroundAreaPlan.Around(2, Vector3D.Zero, 4, limits);
        Assert.Equal(6, all.Tiles.Length, "an origin-containing tiny-world request covers every face");
        for (int i = 0; i < 6; i++) Assert.Equal(i, (int)all.Tiles[i].Face, "whole sphere order is canonical");
        Vector3D a = new Vector3D(1, 1, 1).Normalized * 1200, b = new Vector3D(1, 1, -1).Normalized * 1200;
        GroundAreaPlan forward = GroundAreaPlan.Along(1200, a, b, 12, limits);
        GroundAreaPlan reverse = GroundAreaPlan.Along(1200, b, a, 12, limits);
        Assert.True(forward.Tiles.SequenceEqual(reverse.Tiles), "swept area order and membership do not depend on direction");
        for (int i = 1; i < forward.Tiles.Length; i++) Assert.True(forward.Tiles[i - 1].Packed < forward.Tiles[i].Packed, "tiles are unique in packed order");
        foreach (Vector3D at in new[] { a, b, (a + b) * 0.5 })
            Assert.True(forward.Tiles.Contains(TileKey.At(at, forward.Level)), "the full swept segment is inside its radial area");
        Reject<FormationCapacityException>(() => GroundAreaPlan.Around(2, Vector3D.Zero, 4, limits with { MaxTiles = 5 }));
        Reject<FormationCapacityException>(() => GroundAreaPlan.Around(2, Vector3D.Zero, 4, limits with { MaxEstimatedBytes = all.EstimatedRetainedBytes - 1 }));
        Reject<OperationCanceledException>(() => GroundAreaPlan.Around(2, Vector3D.Zero, 4, limits, new CancellationToken(true)));
    }

    [Test]
    public static void CanonicalPlansCountExactSeamCornerAndSmallSphereTopologyWithoutSampling()
    {
        TerrainCollisionLimits limits = SelectedGroundCollisionTests.Limits().Terrain;
        foreach (double radius in new[] { 0.4, 2, 250, 1200, 1354.055000514614 })
        {
            TerrainLattice lattice = TerrainLattice.ForRadius(radius);
            var surface = new CountingSurface(lattice);
            int level = GroundCollisionLayout.LevelOn(radius), n = 1 << level;
            for (int face = 0; face < 6; face++)
            foreach ((int x, int y) in new[] { (0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1), (n / 2, 0) })
            {
                int before = surface.Calls;
                TerrainCollisionPlan plan = SurfaceGrid.PrepareCollision(lattice, new TileKey(face, level, x, y), 2, limits);
                Assert.Equal(before, surface.Calls, "topology planning has no terrain callback");
                TriangleMesh mesh = SurfaceGrid.BuildCollision(surface, ZoneFrame.World, plan);
                Assert.Equal(plan.VertexCount, mesh.VertexCount, "planned welded vertex count is exact");
                Assert.Equal(plan.TriangleCount, mesh.TriangleCount, "planned triangle count is exact");
                var bound = GroundCollisionLayout.MaximumTopology(lattice);
                Assert.True(plan.CellCount <= bound.Cells && plan.VertexCount <= bound.Vertices, "cross-face topology stays inside the pre-world envelope");
                if (radius >= 250)
                    Assert.True(plan.CellCount <= 5000 && plan.VertexCount <= 5200, "playable seam/corner fits the proposed profile");
            }
        }
    }

    [Test]
    public static void CompositePreparationAuthenticatesOwnerAndBuildConsumesOneExactPlan()
    {
        var fixture = new SelectedGroundCollisionTests.Fixture();
        fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        object owner = new(); TileKey tile = new(4, 2, 2, 2);
        int calls = fixture.Terrain.Calls;
        SelectedGroundCollisionPlan plan = Prepare(fixture, owner, tile);
        Assert.Equal(calls, fixture.Terrain.Calls, "complete component selection occurs without sampling");
        Reject<FormationValidationException>(() => SelectedGroundCollision.Build(fixture.Terrain, new object(), plan));
        Assert.Equal(calls, fixture.Terrain.Calls, "wrong bundle refuses before any sample or consumption");
        TriangleMesh mesh = SelectedGroundCollision.Build(fixture.Terrain, owner, plan);
        Assert.Equal(plan.VertexCount, mesh.VertexCount, "planned composite vertex count includes every selected component");
        Assert.Equal(plan.TriangleCount, mesh.TriangleCount, "planned composite triangle count includes every selected component");
        Assert.True(plan.TriangleCount > plan.Terrain.TriangleCount, "nonempty physical module is actually selected");
        Reject<InvalidOperationException>(() => SelectedGroundCollision.Build(fixture.Terrain, owner, plan));
        TriangleMesh direct = fixture.Collision(tile, 2);
        Assert.True(mesh.Positions.AsSpan().SequenceEqual(direct.Positions) && mesh.Indices.AsSpan().SequenceEqual(direct.Indices),
            "direct and prepared composite paths retain exact geometry bytes/order");
    }

    [Test]
    public static void CompositeCapacityAndCancellationNeverPublishOrResampleAnIncompletePlan()
    {
        var fixture = new SelectedGroundCollisionTests.Fixture(); fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        object owner = new(); TileKey tile = new(4, 2, 2, 2); int calls = fixture.Terrain.Calls;
        Reject<FormationCapacityException>(() => SelectedGroundCollision.Prepare(owner, fixture.Terrain.TerrainLattice,
            ZoneFrame.World, tile, 2, fixture.Field, Array.Empty<SelectedGroundCollision.Monument>(),
            SelectedGroundCollisionTests.Limits() with { MaxTriangles = 1 }));
        Assert.Equal(calls, fixture.Terrain.Calls, "output capacity refuses before terrain sampling");
        SelectedGroundCollisionPlan plan = Prepare(fixture, owner, tile);
        Reject<OperationCanceledException>(() => SelectedGroundCollision.Build(fixture.Terrain, owner, plan, new CancellationToken(true)));
        Reject<InvalidOperationException>(() => SelectedGroundCollision.Build(fixture.Terrain, owner, plan));
        Assert.Equal(calls, fixture.Terrain.Calls, "cancelled consumed plan cannot restart outside its original reservation");
        TerrainCollisionPlan topology = SurfaceGrid.PrepareCollision(fixture.Terrain.TerrainLattice, tile, 0, SelectedGroundCollisionTests.Limits().Terrain);
        Reject<FormationValidationException>(() => SurfaceGrid.BuildCollision(new CountingSurface(new TerrainLattice(11, 0)), ZoneFrame.World, topology));
    }

    internal static SelectedGroundCollisionPlan Prepare(SelectedGroundCollisionTests.Fixture fixture, object owner, TileKey tile) =>
        SelectedGroundCollision.Prepare(owner, fixture.Terrain.TerrainLattice, ZoneFrame.World, tile, 2, fixture.Field,
            Array.Empty<SelectedGroundCollision.Monument>(), SelectedGroundCollisionTests.Limits());
    private sealed class CountingSurface(TerrainLattice lattice) : ISurface, ITerrainLatticeSurface
    {
        public TerrainLattice TerrainLattice => lattice;
        public double Radius => lattice.Radius;
        public double SeaRadius => 0;
        internal int Calls;
        public double HeightAt(Vector3D direction) { Calls++; return 0; }
    }
    internal static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new TestFailure("Expected " + typeof(T).Name + "."); }
}
