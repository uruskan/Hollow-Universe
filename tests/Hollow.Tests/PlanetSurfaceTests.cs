using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>The first world's real surface (GROUND.md §4.6): determinism, tiles, collision, speed.</summary>
public static class PlanetSurfaceTests
{
    // Pins heights and biomes at fixed points of the first world. A change to the surface generator moves this:
    // decide on purpose, then update it (and say so in the commit).
    private const ulong SurfaceGolden = 0x6F091AECE53612DBUL;

    private static readonly Lazy<PlanetSurface> FirstSurface = new(() =>
    {
        var found = FirstWorld.Find(Fixtures.Galaxy)!.Value;
        return PlanetSurface.For(Fixtures.Galaxy, found.System, found.Planet);
    });

    /// <summary>Heights and biomes at 600 fixed points hash to a pinned value, and a second surface agrees bit for bit.</summary>
    [Test]
    public static void SurfaceGoldenHeightsAndBiomes()
    {
        PlanetSurface surface = FirstSurface.Value;
        var found = FirstWorld.Find(Fixtures.Galaxy)!.Value;
        PlanetSurface again = PlanetSurface.For(Fixtures.Galaxy, found.System, found.Planet);
        Vector3D[] points = PlanetSurface.FibonacciSphere(500);
        var list = new List<Vector3D>(points);
        // And a cluster around the landing site, where the ground game starts.
        Vector3D site = ZoneFrame.Direction(FirstWorld.LandingLatitude * DMath.DegToRad, FirstWorld.LandingLongitude * DMath.DegToRad);
        var frame = new ZoneFrame(site * surface.Radius);
        for (int i = 0; i < 100; i++)
        {
            list.Add(frame.ToBody(37.0 * (i % 10) - 160, 0, 29.0 * (i / 10) - 140).Normalized);
        }
        ulong h = Hash64.Of((ulong)BitConverter.DoubleToInt64Bits(surface.Radius));
        int land = 0;
        foreach (Vector3D d in list)
        {
            double height = surface.HeightAt(d);
            Assert.True(height.Equals(again.HeightAt(d)), "two surfaces agree");
            Biome biome = surface.BiomeAt(d);
            h = Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(height));
            h = Hash64.Combine(h, (ulong)biome);
            if (height >= 0)
            {
                land++;
            }
        }
        Fixtures.Report($"{list.Count} points, {land} on land, ice below {surface.IceTemperature:0.0} °C, hash 0x{h:X16}");
        Assert.InRange(0.45, 0.65, (double)CountLand(surface, points) / points.Length, "land share near 1 − ocean cover");
        Assert.Equal(SurfaceGolden, h, "surface golden hash");
    }

    private static int CountLand(PlanetSurface surface, Vector3D[] points)
    {
        int n = 0;
        foreach (Vector3D d in points)
        {
            if (surface.HeightAt(d) >= 0)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>Face coordinates → direction → face coordinates comes back within 1e-12; tile keys pack and unpack.</summary>
    [Test]
    public static void TileMappingRoundTrips()
    {
        var rng = new Rng(3);
        double worst = 0;
        for (int i = 0; i < 20000; i++)
        {
            int face = rng.NextInt(6);
            double u = rng.Range(-1, 1), v = rng.Range(-1, 1);
            Vector3D d = CubeSphere.Direction(face, u, v);
            Assert.Near(1, d.Length, 1e-12, "unit direction");
            (int f2, double u2, double v2) = CubeSphere.FromDirection(d);
            if (Math.Abs(u) < 0.999999 && Math.Abs(v) < 0.999999)
            {
                Assert.Equal(face, f2, "face");
            }
            Vector3D back = CubeSphere.Direction(f2, u2, v2);
            worst = Math.Max(worst, (back - d).Length);
            int level = rng.NextInt(TileKey.MaxLevel + 1);
            TileKey key = TileKey.At(d, level);
            Assert.True(TileKey.FromPacked(key.Packed) == key, "packed key round trip");
            if (f2 == key.Face)
            {
                Assert.True(u2 >= key.U0 - 1e-12 && u2 <= key.U0 + key.Span + 1e-12, "tile holds the direction (u)");
                Assert.True(v2 >= key.V0 - 1e-12 && v2 <= key.V0 + key.Span + 1e-12, "tile holds the direction (v)");
            }
        }
        Assert.True(TileKey.FromPacked(ulong.MaxValue) == null, "untrusted bits are rejected");
        // Tile sizes stay within a factor of 1.5 across a face (the tangent warp).
        double corner = (CubeSphere.Direction(0, 1, 1) - CubeSphere.Direction(0, 1 - 1.0 / 512, 1 - 1.0 / 512)).Length;
        double centre = (CubeSphere.Direction(0, 0, 0) - CubeSphere.Direction(0, 1.0 / 512, 1.0 / 512)).Length;
        Fixtures.Report($"worst round trip {worst:0.0e0}, corner/centre cell size {corner / centre:0.00}");
        Assert.True(worst < 1e-12, "round trip within 1e-12");
        Assert.InRange(0.66, 1.5, corner / centre, "uniform tiles");
    }

    /// <summary>Collision is made of exactly the vertices the client draws for the same tiles.</summary>
    [Test]
    public static void CollisionMatchesRenderTiles()
    {
        PlanetSurface surface = FirstSurface.Value;
        ZoneFrame frame = ZoneFrame.At(surface, FirstWorld.LandingLatitude * DMath.DegToRad, FirstWorld.LandingLongitude * DMath.DegToRad);
        TriangleMesh mesh = surface.PatchCollision(frame, 10, -20, 60);
        List<TileKey> tiles = TileMesh.TilesCovering(surface, frame, 10, -20, 60, PlanetSurface.CollisionLevel);
        int n = PlanetSurface.TileCells + 1;
        Assert.Equal(tiles.Count * n * n, mesh.VertexCount, "one vertex per render vertex");
        double worst = 0;
        for (int t = 0; t < tiles.Count; t++)
        {
            TileGrid grid = TileMesh.Build(surface, tiles[t]);
            for (int v = 0; v < n * n; v++)
            {
                int k = (t * n * n + v) * 3;
                var local = new Vector3(mesh.Positions[k], mesh.Positions[k + 1], mesh.Positions[k + 2]);
                worst = Math.Max(worst, (frame.ToBody(local) - grid.Vertex(v)).Length);
            }
        }
        // Covers the square, faces up.
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            minX = Math.Min(minX, mesh.Positions[3 * v]); maxX = Math.Max(maxX, mesh.Positions[3 * v]);
            minZ = Math.Min(minZ, mesh.Positions[3 * v + 2]); maxZ = Math.Max(maxZ, mesh.Positions[3 * v + 2]);
        }
        int up = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 a = V(mesh, mesh.Indices[3 * t]), b = V(mesh, mesh.Indices[3 * t + 1]), c = V(mesh, mesh.Indices[3 * t + 2]);
            if (Vector3.Cross(b - a, c - a).Y > 0)
            {
                up++;
            }
        }
        Fixtures.Report($"{tiles.Count} tiles, {mesh.TriangleCount} triangles, worst mismatch {worst * 1000:0.000} mm");
        Assert.True(worst < 0.002, "collision vertex = render vertex (within 2 mm of float rounding)");
        Assert.True(minX <= -20 && maxX >= 40 && minZ <= -50 && maxZ >= 10, "covers the square");
        Assert.True(up >= mesh.TriangleCount * 0.99, "triangles face up");
        // The physics' pooled copy (GROUND.md §5.11) is the same mesh to the bit, its grids' heights kept from a
        // neighbouring square's.
        surface.PatchCollisionPooled(frame, 40, -20, 60).Dispose();
        using PooledTriangles pooled = surface.PatchCollisionPooled(frame, 10, -20, 60);
        bool same = pooled.VertexCount == mesh.VertexCount && pooled.TriangleCount == mesh.TriangleCount;
        for (int i = 0; same && i < mesh.Positions.Length; i++)
        {
            same = BitConverter.SingleToUInt32Bits(pooled.Positions[i]) == BitConverter.SingleToUInt32Bits(mesh.Positions[i]);
        }
        for (int i = 0; same && i < mesh.Indices.Length; i++)
        {
            same = pooled.Indices[i] == mesh.Indices[i];
        }
        Assert.True(same, "pooled collision = collision, bit for bit");
    }

    /// <summary>Generation speed: µs per height and per drawn vertex, and a scatter tile (reported, loosely bounded).</summary>
    [Test]
    public static void GenerationSpeedIsReported()
    {
        PlanetSurface surface = FirstSurface.Value;
        Vector3D site = ZoneFrame.Direction(FirstWorld.LandingLatitude * DMath.DegToRad, FirstWorld.LandingLongitude * DMath.DegToRad);
        var watch = Stopwatch.StartNew();
        double sink = 0;
        const int Heights = 20000;
        var frame = new ZoneFrame(site * surface.Radius);
        for (int i = 0; i < Heights; i++)
        {
            sink += surface.HeightAt(frame.ToBody(i % 141 * 3.1, 0, i / 141 * 2.9).Normalized);
        }
        double perHeight = watch.Elapsed.TotalMilliseconds * 1000 / Heights;
        TileKey key = TileKey.At(site, PlanetSurface.CollisionLevel - 2);
        watch.Restart();
        const int Tiles = 8;
        for (int i = 0; i < Tiles; i++)
        {
            sink += TileMesh.Build(surface, new TileKey(key.Face, key.Level, key.X + i, key.Y)).MaxHeight;
        }
        double perTile = watch.Elapsed.TotalMilliseconds / Tiles;
        int vertices = (PlanetSurface.TileCells + 1) * (PlanetSurface.TileCells + 1);
        watch.Restart();
        TileKey scatterKey = TileKey.At(site, Scatter.Level);
        IReadOnlyList<ScatterItem> items = surface.ScatterFor(scatterKey);
        double scatterMs = watch.Elapsed.TotalMilliseconds;
        Fixtures.Report($"{perHeight:0.00} µs per height; tile {perTile:0.0} ms = {perTile * 1000 / vertices:0.00} µs per vertex; scatter tile {scatterMs:0.0} ms, {items.Count} objects [{sink:0}]");
        Assert.True(perTile < 200, "a tile builds in well under 0.2 s");
    }

    /// <summary>Scatter is deterministic, its IDs are unique per tile, and everything stands on the ground.</summary>
    [Test]
    public static void ScatterIsDeterministicAndOnTheGround()
    {
        PlanetSurface surface = FirstSurface.Value;
        Vector3D site = ZoneFrame.Direction(FirstWorld.LandingLatitude * DMath.DegToRad, FirstWorld.LandingLongitude * DMath.DegToRad);
        TileKey key = TileKey.At(site, Scatter.Level);
        IReadOnlyList<ScatterItem> a = surface.ScatterFor(key);
        IReadOnlyList<ScatterItem> b = surface.ScatterFor(key);
        Assert.Equal(a.Count, b.Count, "same count");
        var ids = new HashSet<int>();
        ulong h = key.Packed;
        for (int i = 0; i < a.Count; i++)
        {
            Assert.True(a[i].Index == b[i].Index && a[i].Direction == b[i].Direction && a[i].Height.Equals(b[i].Height), "same item");
            Assert.True(ids.Add(a[i].Index), "unique index in the tile");
            Assert.Near(surface.HeightAt(a[i].Direction), a[i].Height, 1e-3, "on the ground");
            h = Hash64.Combine(h, (ulong)a[i].Index);
        }
        var counts = new int[256];
        foreach (ScatterItem item in a)
        {
            counts[(int)item.Kind]++;
        }
        Fixtures.Report($"{a.Count} objects (fir {counts[0]}, pine {counts[1]}, birch {counts[2]}, broadleaf {counts[3]}, bush {counts[4]}, fern {counts[5]}, rock {counts[6]}, red berries {counts[7]}, blue berries {counts[8]}; outcrops: stone {counts[9]}, metal {counts[10]}, sulphur {counts[11]}), hash 0x{h:X16}");
    }

    /// <summary>The sun rises and sets: over one rotation the landing site sees day and night, and local time runs 0–24.</summary>
    [Test]
    public static void SunRisesAndSets()
    {
        var found = FirstWorld.Find(Fixtures.Galaxy)!.Value;
        var sky = new SkyEphemeris(found.System, found.Planet);
        Vector3D site = ZoneFrame.Direction(FirstWorld.LandingLatitude * DMath.DegToRad, FirstWorld.LandingLongitude * DMath.DegToRad);
        Vector3D observer = site * found.Planet.Radius;
        double min = double.MaxValue, max = double.MinValue;
        double day = Math.Abs(found.Planet.RotationPeriod);
        for (int i = 0; i < 96; i++)
        {
            double t = 8e8 + day * i / 96.0;
            double e = sky.Elevation(sky.Sun.Index, observer, t);
            min = Math.Min(min, e);
            max = Math.Max(max, e);
            double hours = sky.LocalSolarHours(site, sky.Sun.Index, t);
            Assert.InRange(0, 24, hours, "local time");
        }
        Fixtures.Report($"sun between {min * DMath.RadToDeg:0.0}° and {max * DMath.RadToDeg:0.0}° over a day");
        Assert.True(min < 0 && max > 0, "day and night");
        // Body → system → body is the identity.
        Vector3D v = new Vector3D(0.3, -0.5, 0.8).Normalized;
        Vector3D back = sky.ToBody(sky.ToSystem(v, 8.1e8), 8.1e8);
        Assert.Near(0, (back - v).Length, 1e-12, "rotation round trip");
    }

    private static Vector3 V(TriangleMesh mesh, int i) => new(mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2]);
}
