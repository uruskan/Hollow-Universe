using System;
using System.Linq;
using System.Globalization;
using Hollow.Client.Diagnostics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace Hollow.Tests;

public static class EnvironmentRouteTests
{
    [Test]
    public static void ActualEarthHasOneConnectedSampledEnvironmentRoute()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        EnvironmentRoutePlan route = EarthEnvironmentRoute.Find(world, ContentFixture.Content.NaturalKinds);
        Assert.Equal(6, route.Stops.Count, "beach, woodland, clearing, water, cliff, small monument");
        Assert.Equal(5, route.Legs.Count, "every checkpoint belongs to the same walking circuit");
        for (int a = 0; a < route.Stops.Count; a++)
            for (int b = a + 1; b < route.Stops.Count; b++)
                Assert.True(EarthEnvironmentRoute.Distance(route.Stops[a].Position, route.Stops[b].Position) > 15,
                    $"{route.Stops[a].Label} and {route.Stops[b].Label} are different places");
        Assert.True(route.Metres is > 100 and <= EarthEnvironmentRoute.MaximumLength, "bounded real-world walk");
        Assert.Equal(Biome.Beach, world.BiomeAt(route.Stops[0].Position.Normalized), "starts on a real beach");
        Assert.True(route.Stops[1].Trees >= 3 && route.Stops[1].Woodland >= 0.38, "woodland contains actual nearby trees");
        Assert.True(route.Stops[2].Trees <= 2 && route.Stops[2].Grass >= 0.35, "clearing is distinct from woodland");
        Vector3D water = route.Stops[3].LookAt;
        Assert.True(world.WaterLevelAt(water.Normalized) > world.HeightAt(water.Normalized) + 0.3, "water is real geometry");
        Assert.True(world.Cliffs.Near(route.Stops[4].LookAt, 12).Count > 0, "cliff checkpoint faces a generated scan");
        Assert.True(world.Cliffs.Near(route.Stops[4].LookAt, 12).Any(cliff =>
            Vector3D.Dot(route.Stops[4].Position - cliff.Position, cliff.Downslope) > 12),
            "cliff viewpoint is outside its exposed downslope face, independent of the source mesh's Z convention");
        Assert.True(world.Monuments.At(route.Stops[5].LookAt.Normalized) != null, "monument checkpoint faces a generated site");
        for (int leg = 0; leg < route.Legs.Count; leg++)
        {
            var points = route.Legs[leg].Points;
            Assert.Near(0, Vector3D.Distance(points[0], route.Stops[leg].Position), 1e-7, "leg starts at previous checkpoint");
            Assert.Near(0, Vector3D.Distance(points[^1], route.Stops[leg + 1].Position), 1e-7, "leg ends at next checkpoint");
            for (int i = 1; i < points.Count; i++)
            {
                Assert.True(EarthEnvironmentRoute.Corridor(world, points[i - 1], points[i]), "two-metre samples remain dry, outside monuments and within grade bound");
                Assert.True(ClearOfSolids(world, points[i - 1], points[i]),
                    "the walk keeps clear of every solid tree, outcrop, boulder and cliff piece: routes ran into a boulder and into T2's rock");
            }
        }
        foreach (EnvironmentRouteStop stop in route.Stops)
            Fixtures.Report(string.Create(CultureInfo.InvariantCulture,
                $"route {stop.Label}: --lat {EarthEnvironmentRoute.Latitude(stop.Position):0.000000} --lon {EarthEnvironmentRoute.Longitude(stop.Position):0.000000}; height={stop.Height:0.00} grade={stop.Grade:0.000} grass={stop.Grass:0.00} woodland={stop.Woodland:0.00} trees18m={stop.Trees} feature={stop.Feature}"));
        Fixtures.Report($"Connected route {route.Region}; {route.Metres:0.0}m; {route.CandidatesChecked} candidates; " +
            "heightfield feasibility only, hosted object collision and visual quality remain unverified");
    }

    [Test]
    public static void RouteRejectsLegacyWorldAndInWaterCorridor()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        Vector3D wet = PlanetSurface.FibonacciSphere(256).First(d => world.HeightAt(d) < world.WaterLevelAt(d) - 1);
        Vector3D ground = EarthEnvironmentRoute.Ground(world, wet);
        Assert.True(!EarthEnvironmentRoute.Corridor(world, ground, ground), "a fully submerged point cannot be called a walkable corridor");
        bool rejected = false;
        try { EarthEnvironmentRoute.Find(WalkingWorld.For(system, earth, 2410)); }
        catch (ArgumentException) { rejected = true; }
        Assert.True(rejected, "legacy FirstWorld reports cannot silently select this route");
    }
    // An independent check of the route's clearance: every solid natural object (the game's solid kinds, a footprint of
    // the kind's radius times its scale, as the server builds it) stays the corridor's clearance from 1 m samples.
    private static bool ClearOfSolids(WalkingWorld world, Vector3D a, Vector3D b)
    {
        var tiles = new System.Collections.Generic.List<TileKey>();
        Vector3D middle = (a.Normalized + b.Normalized).Normalized * world.Radius;
        TileKey.Around(middle, EarthEnvironmentRoute.Distance(a, b) / 2 + 8, world.Radius, world.ScatterLevel, tiles);
        var solids = new System.Collections.Generic.List<(Vector3D Position, double Radius)>();
        foreach (TileKey tile in tiles)
            foreach (ScatterItem item in world.ScatterFor(tile))
                foreach (NaturalKind kind in ContentFixture.Content.NaturalKinds)
                    if (kind.IsSolid && item.Scale >= kind.ScatterMinScale && kind.ScatterKinds.Contains(item.Kind))
                        solids.Add((item.Position(world.Radius), kind.Radius * item.Scale));
        var cliffs = world.Cliffs.Near(middle, EarthEnvironmentRoute.Distance(a, b) / 2 + 8);
        int steps = Math.Max(1, (int)Math.Ceiling(EarthEnvironmentRoute.Distance(a, b)));
        for (int i = 0; i <= steps; i++)
        {
            Vector3D up = (a.Normalized * (1.0 - (double)i / steps) + b.Normalized * ((double)i / steps)).Normalized;
            Vector3D ground = up * (world.Radius + world.HeightAt(up));
            foreach (var (position, radius) in solids)
                if (Vector3D.Distance(ground, position) < radius + EarthEnvironmentRoute.CorridorClearance) return false;
            foreach (CliffPlacement cliff in cliffs)
                if (CliffPlacements.Holds(cliff, ground + up * 1.2, EarthEnvironmentRoute.CorridorClearance)) return false;
        }
        return true;
    }
}
