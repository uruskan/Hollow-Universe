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

    /// <summary>Collision triangles cover the square asked for, sit on the ground and face up.</summary>
    [Test]
    public static void GroundCollisionLiesOnTheGround()
    {
        var surface = new SphereSurface(6_435_000, 7);
        ZoneFrame frame = ZoneFrame.At(surface, 0.2, 1.1);
        TriangleMesh mesh = surface.GroundCollision(frame, 300, -150, 64);
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        double worst = 0;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            var p = new Vector3(mesh.Positions[3 * v], mesh.Positions[3 * v + 1], mesh.Positions[3 * v + 2]);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            Vector3D body = frame.ToBody(p);
            worst = Math.Max(worst, Math.Abs(body.Length - (surface.Radius + surface.HeightAt(body.Normalized))));
        }
        Fixtures.Report($"{mesh.TriangleCount} triangles, x {minX:0.0}..{maxX:0.0}, z {minZ:0.0}..{maxZ:0.0}, worst height error {worst * 1000:0.0} mm");
        Assert.True(minX <= 268.01 && maxX >= 331.99 && minZ <= -181.99 && maxZ >= -118.01, "covers the square");
        Assert.True(worst < 0.01, "vertices on the ground within 1 cm");
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 a = Vertex(mesh, mesh.Indices[3 * t]), b = Vertex(mesh, mesh.Indices[3 * t + 1]), c = Vertex(mesh, mesh.Indices[3 * t + 2]);
            Assert.True(Vector3.Cross(b - a, c - a).Y > 0, "triangles face up");
        }
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
