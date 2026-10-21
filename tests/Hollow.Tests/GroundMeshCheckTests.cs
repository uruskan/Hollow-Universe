using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// A malformed ground tile fails as a named managed error before Jolt sees it, instead of a native access violation
/// (docs/HANDOFF_CODEX_2026-10-19.md, Phase 1).
/// </summary>
public static class GroundMeshCheckTests
{
    private static readonly TileKey Tile = new(2, 9, 17, 300);

    private static string? Problem(float[] positions, int[] indices)
    {
        try
        {
            GroundMeshCheck.Require(Tile, positions, indices, positions.Length / 3, indices.Length / 3);
            return null;
        }
        catch (MalformedGroundTileException error)
        {
            Assert.Equal(Tile, error.Tile, "the error names its tile");
            Assert.True(error.Message.Contains("face 2, level 9, (17, 300)"), $"the message names the tile: {error.Message}");
            return error.Problem;
        }
    }

    [Test]
    public static void MalformedTrianglesAreRefusedByName()
    {
        float[] square = [0, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1];
        Assert.Equal(null, Problem(square, [0, 1, 2, 0, 2, 3]), "a whole square passes");
        Assert.True(Problem([], [])!.Contains("0 vertices and 0 triangles"), "no triangles");
        Assert.True(Problem([0, 0, 0, float.NaN, 0, 0, 1, 0, 1], [0, 1, 2])!.Contains("vertex 1 has coordinate NaN"), "NaN");
        Assert.True(Problem([0, 0, 0, 1, 0, 0, float.PositiveInfinity, 0, 1], [0, 1, 2])!.Contains("vertex 2"), "infinity");
        Assert.True(Problem(square, [0, 1, 4])!.Contains("triangle 0 indexes vertices 0, 1, 4 of 4"), "index past the end");
        Assert.True(Problem(square, [0, 1, 2, 0, -1, 3])!.Contains("triangle 1"), "negative index");
        Assert.True(Problem(square, [0, 0, 1, 2, 3, 3])!.Contains("all 2 triangles repeat a vertex"), "only degenerate triangles");
        try
        {
            GroundMeshCheck.Require(Tile, square, [0, 1, 2], 4, 2);
            throw new TestFailure("counts longer than the arrays pass");
        }
        catch (MalformedGroundTileException error)
        {
            Assert.True(error.Problem.Contains("arrays of 12 coordinates and 3 indices"), error.Problem);
        }
    }

    [Test]
    public static void AWorldGivenANaNTileThrowsTheNamedErrorAndStillDisposes()
    {
        var surface = new NaNSurface(1200);
        var point = new Vector3(0, 1200, 0);
        var world = new PhysicsWorld(surface, ZoneFrame.World) { MakePropBodiesAhead = false };
        MalformedGroundTileException? caught = null;
        try
        {
            world.EnsureGroundWithin(point, 40);
        }
        catch (MalformedGroundTileException error)
        {
            caught = error;
        }
        world.Dispose();
        Assert.True(caught != null, "building a NaN tile throws MalformedGroundTileException");
        Assert.True(caught!.Problem.Contains("NaN"), caught.Problem);
        Fixtures.Report(caught.Message);
    }

    // Level ground whose collision mesh carries one NaN vertex, as a broken height function would give.
    private sealed class NaNSurface(double radius) : ISurface
    {
        public double Radius { get; } = radius;

        public double SeaRadius => 0;

        public double HeightAt(Vector3D direction) => 0;

        public TriangleMesh GroundCollision(in ZoneFrame frame, TileKey tile, double margin)
        {
            TriangleMesh level = SurfaceGrid.Collision(this, frame, tile, margin, SurfaceGrid.DefaultSpacing);
            float[] positions = (float[])level.Positions.Clone();
            positions[positions.Length / 2] = float.NaN;
            return new TriangleMesh(positions, level.Indices);
        }
    }
}
