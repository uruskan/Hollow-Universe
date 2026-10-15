using System.Collections.Generic;
using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.Tests;

public static class SurfaceTests
{
    // Pins the first world's whole state (GROUND.md §3). If a generator change moves this, the ground game's world
    // changed: decide on purpose, then update it.
    private const ulong FirstWorldGolden = 0x5A9DA4B48BC6A8B6UL;

    /// <summary>GROUND.md §3: the first world exists, is still breathable and Earth-like, and hasn't changed.</summary>
    [Test]
    public static void FirstWorldIsPinned()
    {
        var found = FirstWorld.Find(Fixtures.Galaxy);
        Assert.True(found != null, "the first world exists");
        (StarSystem system, Body planet) = found!.Value;
        PlanetState state = planet.State!;
        Assert.True(state.Biosphere == Biosphere.Complex, "it has plants and animals");
        Assert.True((state.Hazards & Hazards.ToxicAtmosphere) == 0, "its air is breathable");
        Assert.True(planet.Landable, "it can be landed on");
        Assert.Near(0.92, planet.SurfaceGravity, 0.01, "gravity (g)");
        ulong h = Golden.Of(planet);
        Fixtures.Report($"{system.Name} / {planet.Name}: {planet.Radius / 1000:0} km, {planet.SurfaceGravity:0.00} g, hash 0x{h:X16}");
        Assert.Equal(FirstWorldGolden, h, "first world golden hash");
    }

    /// <summary>A zone frame maps body points to local floats and back within a millimetre, 10 km out.</summary>
    [Test]
    public static void ZoneFrameRoundTrips()
    {
        var surface = new SphereSurface(6_435_000, 7);
        ZoneFrame frame = ZoneFrame.At(surface, 0.71, -2.3);
        Assert.Near(0, frame.UpAt(Vector3.Zero).X, 1e-6, "up at the origin, x");
        Assert.Near(1, frame.UpAt(Vector3.Zero).Y, 1e-6, "up at the origin, y");
        double worst = 0;
        var rng = new Rng(11);
        for (int i = 0; i < 1000; i++)
        {
            var local = new Vector3((float)(rng.NextDouble() * 20_000 - 10_000), (float)(rng.NextDouble() * 400 - 200),
                (float)(rng.NextDouble() * 20_000 - 10_000));
            Vector3 back = frame.ToLocal(frame.ToBody(local));
            worst = Math.Max(worst, (back - local).Length());
        }
        Fixtures.Report($"worst round trip {worst * 1000:0.000} mm");
        Assert.True(worst < 1e-3, "round trip within 1 mm");
        // East is +X, north is −Z: walking north from the frame raises the latitude.
        Vector3D north = frame.ToBody(new Vector3(0, 0, -1000)).Normalized;
        Assert.True(north.Y > frame.Origin.Normalized.Y, "−Z points north");
    }

    /// <summary>
    /// A cube-sphere tile's collision triangles (docs/WHOLE_WORLD.md §6.1) cover the tile and its margin, sit on the ground
    /// and face up, in the world's frame; neighbouring tiles of a face share their grid points where they overlap.
    /// </summary>
    [Test]
    public static void GroundCollisionLiesOnTheGround()
    {
        ISurface surface = new SphereSurface(2410, 7);
        ZoneFrame frame = ZoneFrame.World;
        TileKey tile = TileKey.At(ZoneFrame.Direction(0.2, 1.1), 6);
        TriangleMesh mesh = surface.GroundCollision(frame, tile, 2);
        double worst = 0;
        var corners = new[] { CubeSphere.Direction(tile.Face, tile.U0, tile.V0), CubeSphere.Direction(tile.Face, tile.U0 + tile.Span, tile.V0 + tile.Span) };
        var nearest = new double[2] { double.MaxValue, double.MaxValue };
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            Vector3D body = frame.ToBody(Vertex(mesh, v));
            worst = Math.Max(worst, Math.Abs(body.Length - (surface.Radius + surface.HeightAt(body.Normalized))));
            for (int k = 0; k < 2; k++)
            {
                nearest[k] = Math.Min(nearest[k], (body.Normalized - corners[k]).Length * surface.Radius);
            }
        }
        Fixtures.Report($"{mesh.TriangleCount} triangles on {tile}, worst height error {worst * 1000:0.0} mm, corners {nearest[0]:0.00} m and {nearest[1]:0.00} m from a vertex");
        Assert.True(worst < 0.01, "vertices on the ground within 1 cm");
        Assert.True(nearest[0] < 0.01 && nearest[1] < 0.01, "the grid passes through the tile's corners: it covers the tile");
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 a = Vertex(mesh, mesh.Indices[3 * t]), b = Vertex(mesh, mesh.Indices[3 * t + 1]), c = Vertex(mesh, mesh.Indices[3 * t + 2]);
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), frame.UpAt(a)) > 0, "triangles face up");
        }
        // The tile beside it on the same face: where their margins overlap, their points are the same, bit for bit.
        var next = new TileKey(tile.Face, tile.Level, tile.X + 1 < (1 << tile.Level) ? tile.X + 1 : tile.X - 1, tile.Y);
        var mine = new HashSet<(float, float, float)>();
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            Vector3 p = Vertex(mesh, v);
            mine.Add((p.X, p.Y, p.Z));
        }
        TriangleMesh other = surface.GroundCollision(frame, next, 2);
        int shared = 0;
        for (int v = 0; v < other.VertexCount; v++)
        {
            Vector3 p = Vertex(other, v);
            shared += mine.Contains((p.X, p.Y, p.Z)) ? 1 : 0;
        }
        Assert.True(shared > 100, $"neighbours share their overlap's points ({shared})");
    }

    /// <summary>The same direction gives the same height, bit for bit, every time.</summary>
    [Test]
    public static void SphereSurfaceIsDeterministic()
    {
        var a = new SphereSurface(6_435_000, 99);
        var b = new SphereSurface(6_435_000, 99);
        ulong h = 0;
        for (int i = 0; i < 2000; i++)
        {
            Vector3D d = ZoneFrame.Direction(-1.5 + 3.0 * i / 2000, i * 0.37);
            double x = a.HeightAt(d);
            Assert.True(x.Equals(b.HeightAt(d)), "same height");
            h = Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(x));
        }
        Fixtures.Report($"heights hash 0x{h:X16}");
    }

    private static Vector3 Vertex(TriangleMesh mesh, int i) => new(mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2]);
}
