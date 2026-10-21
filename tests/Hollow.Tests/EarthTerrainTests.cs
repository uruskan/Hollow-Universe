using System;
using System.Collections.Generic;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Earth's playable land budget and continent shape, measured from the same surface used by the game.</summary>
public static class EarthTerrainTests
{
    // Intentional Earth revision 4: reviewed layout variant 5. Golden updated from the production 600-point fixture.
    private const ulong EarthGolden = 0x3FD0EDEC0D0C72A5UL;

    private static readonly Lazy<WalkingWorld> Earth = new(() =>
    {
        var (system, body) = StartingWorld.Find();
        return WalkingWorld.For(system, body, GameScale.PlanetRadius(body));
    });

    [Test]
    public static void EarthHasMoreDryGroundWithinTheTotalSurfaceLimit()
    {
        WalkingWorld world = Earth.Value;
        double area = GameScale.SurfaceArea(world.Radius);
        Assert.Near(1200, world.Radius, 1e-9, "Earth keeps its existing radius");
        Assert.True(area <= GameScale.MaximumSurfaceArea, "the entire sphere, including water, stays within 23.04 km²");

        int dry = 0, sea = 0;
        Vector3D[] points = PlanetSurface.FibonacciSphere(16_000);
        foreach (Vector3D point in points)
        {
            double height = world.HeightAt(point);
            if (height >= world.WaterLevelAt(point)) dry++;
            if (height < 0) sea++;
        }
        double dryShare = (double)dry / points.Length;
        Fixtures.Report($"Earth: {area / 1e6:0.00} km² total, {dryShare:P1} dry ({area * dryShare / 1e6:0.00} km²), {(double)sea / points.Length:P1} ocean");
        Assert.InRange(0.55, 0.65, dryShare, "most of Earth is dry ground after lakes and streams");
        Assert.True(area * dryShare >= 9.5e6, "at least 9.5 km² of dry ground, up from about 5.1 km²");
    }

    [Test]
    public static void EarthHasALargeConnectedLandmass()
    {
        WalkingWorld world = Earth.Value;
        // Approximately 15 m cells. Four-edge adjacency avoids joining islands that merely touch at a corner.
        // Spherical tile areas prevent the cube corners from biasing the result; all six faces include the poles.
        const int Level = 7;
        int count = (int)TileKey.CountAt(Level);
        var dry = new bool[count];
        var areas = new double[count];
        double totalArea = 0, dryArea = 0;
        for (int i = 0; i < count; i++)
        {
            TileKey tile = TileKey.FromIndex(Level, i)!.Value;
            areas[i] = SolidAngle(tile) * world.Radius * world.Radius;
            totalArea += areas[i];
            Vector3D centre = tile.Centre;
            dry[i] = world.HeightAt(centre) >= world.WaterLevelAt(centre);
            if (dry[i]) dryArea += areas[i];
        }
        Assert.Near(GameScale.SurfaceArea(world.Radius), totalArea, 0.01, "sampled tiles cover the entire sphere once");

        var visited = new bool[count];
        var queue = new Queue<int>();
        double largest = 0;
        int continents = 0;
        for (int i = 0; i < count; i++)
        {
            if (!dry[i] || visited[i]) continue;
            continents++;
            visited[i] = true;
            queue.Enqueue(i);
            double connected = 0;
            while (queue.TryDequeue(out int current))
            {
                connected += areas[current];
                TileKey tile = TileKey.FromIndex(Level, current)!.Value;
                Visit(Neighbour(tile, -1, 0));
                Visit(Neighbour(tile, 1, 0));
                Visit(Neighbour(tile, 0, -1));
                Visit(Neighbour(tile, 0, 1));
            }
            largest = Math.Max(largest, connected);
        }
        Fixtures.Report($"Earth at ~15 m: {continents} dry components, largest {largest / 1e6:0.00} km² ({largest / dryArea:P1} of dry ground)");
        Assert.True(largest / dryArea > 0.95, "a connected landmass holds more than 95% of Earth's dry ground");
        Assert.True(largest > 9.5e6, "the main connected landmass exceeds 9.5 km²");

        void Visit(TileKey neighbour)
        {
            int index = (int)neighbour.Index;
            if (!dry[index] || visited[index]) return;
            visited[index] = true;
            queue.Enqueue(index);
        }
    }

    [Test]
    public static void EarthTerrainRemainsDeterministic()
    {
        WalkingWorld world = Earth.Value;
        var (system, body) = StartingWorld.Find();
        WalkingWorld again = WalkingWorld.For(system, body, world.Radius);
        ulong hash = Hash64.Of((ulong)BitConverter.DoubleToInt64Bits(world.Radius));
        foreach (Vector3D point in PlanetSurface.FibonacciSphere(600))
        {
            WalkingSample sample = world.Fields(point);
            Assert.Equal(sample, again.Fields(point), "independent Earth generation agrees for all terrain fields");
            Biome biome = world.BiomeAt(point);
            Assert.Equal(biome, again.BiomeAt(point), "independent Earth generation agrees on biomes");
            hash = Hash64.Combine(hash, (ulong)BitConverter.DoubleToInt64Bits(sample.Height));
            hash = Hash64.Combine(hash, (ulong)BitConverter.DoubleToInt64Bits(sample.Water));
            hash = Hash64.Combine(hash, (ulong)biome);
        }
        Assert.Equal(world.Lakes.Count, again.Lakes.Count, "same lakes");
        Assert.Equal(world.Drainage.Count, again.Drainage.Count, "same drainage");
        Assert.Equal(world.LandingSite, again.LandingSite, "same initial landing site");
        Assert.True(world.HeightAt(world.LandingSite) >= world.WaterLevelAt(world.LandingSite), "selected landing site is dry");
        Fixtures.Report($"Earth terrain hash 0x{hash:X16}; {world.Lakes.Count} lakes, {world.Drainage.Count} drainage pieces");
        Assert.Equal(EarthGolden, hash, "Earth revision 4 layout-5 terrain golden hash");
    }

    [Test]
    public static void EarthProfileLeavesPhysicalDataAndExplicitOverridesIntact()
    {
        var (system, body) = StartingWorld.Find();
        WalkingWorld inherited = WalkingWorld.For(system, body, Earth.Value.Radius, WalkingParameters.Default);
        int dry = 0;
        Vector3D[] points = PlanetSurface.FibonacciSphere(8000);
        foreach (Vector3D point in points)
            if (inherited.HeightAt(point) >= inherited.WaterLevelAt(point)) dry++;
        Assert.InRange(0.26, 0.32, (double)dry / points.Length, "an explicit inherited profile retains Earth's earlier land proportion");
        Assert.Near(0.71, body.State!.OceanCover, 1e-12, "playable terrain tuning does not rewrite Solar System physical data");
    }

    [Test]
    public static void EarthProfileDoesNotChangeOtherWorlds()
    {
        var found = FirstWorld.Find(Fixtures.Galaxy)!.Value;
        WalkingWorld world = WalkingWorld.For(found.System, found.Planet, 2410);
        WalkingWorld inherited = WalkingWorld.For(found.System, found.Planet, 2410, WalkingParameters.Default);
        foreach (Vector3D point in PlanetSurface.FibonacciSphere(600))
        {
            Assert.Equal(inherited.Fields(point), world.Fields(point), "other planets retain their inherited terrain");
            Assert.Equal(inherited.BiomeAt(point), world.BiomeAt(point), "other planets retain their inherited biomes");
        }
    }

    private static TileKey Neighbour(TileKey tile, int dx, int dy)
    {
        int n = 1 << tile.Level;
        int x = tile.X + dx, y = tile.Y + dy;
        if (x >= 0 && x < n && y >= 0 && y < n)
            return new TileKey(tile.Face, tile.Level, x, y);

        // Sample just past the midpoint of the shared edge, then resolve its face. This joins seams and poles
        // without treating a cube face as a separate island or accidentally selecting a diagonal neighbour.
        double u = tile.U0 + tile.Span * (0.5 + dx * 0.5001);
        double v = tile.V0 + tile.Span * (0.5 + dy * 0.5001);
        return TileKey.At(CubeSphere.Direction(tile.Face, u, v), tile.Level);
    }

    private static double SolidAngle(TileKey tile)
    {
        Vector3D a = CubeSphere.Direction(tile.Face, tile.U0, tile.V0);
        Vector3D b = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span, tile.V0);
        Vector3D c = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span, tile.V0 + tile.Span);
        Vector3D d = CubeSphere.Direction(tile.Face, tile.U0, tile.V0 + tile.Span);
        return Triangle(a, b, c) + Triangle(a, c, d);
    }

    private static double Triangle(Vector3D a, Vector3D b, Vector3D c) =>
        2 * Math.Atan2(Math.Abs(Vector3D.Dot(a, Vector3D.Cross(b, c))),
            1 + Vector3D.Dot(a, b) + Vector3D.Dot(b, c) + Vector3D.Dot(c, a));
}
