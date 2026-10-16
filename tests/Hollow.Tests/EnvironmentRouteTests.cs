using System;
using System.Linq;
using System.Globalization;
using Hollow.Client.Diagnostics;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class EnvironmentRouteTests
{
    [Test]
    public static void ActualEarthHasOneConnectedSampledEnvironmentRoute()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        EnvironmentRoutePlan route = EarthEnvironmentRoute.Find(world);
        Assert.Equal(6, route.Stops.Count, "beach, woodland, clearing, water, cliff, small monument");
        Assert.Equal(5, route.Legs.Count, "every checkpoint belongs to the same walking circuit");
        Assert.True(route.Metres is > 100 and <= EarthEnvironmentRoute.MaximumLength, "bounded real-world walk");
        Assert.Equal(Biome.Beach, world.BiomeAt(route.Stops[0].Position.Normalized), "starts on a real beach");
        Assert.True(route.Stops[1].Trees >= 3 && route.Stops[1].Woodland >= 0.38, "woodland contains actual nearby trees");
        Assert.True(route.Stops[2].Trees <= 2 && route.Stops[2].Grass >= 0.35, "clearing is distinct from woodland");
        Vector3D water = route.Stops[3].LookAt;
        Assert.True(world.WaterLevelAt(water.Normalized) > world.HeightAt(water.Normalized) + 0.3, "water is real geometry");
        Assert.True(world.Cliffs.Near(route.Stops[4].LookAt, 12).Count > 0, "cliff checkpoint faces a generated scan");
        Assert.True(world.Monuments.At(route.Stops[5].LookAt.Normalized) != null, "monument checkpoint faces a generated site");
        for (int leg = 0; leg < route.Legs.Count; leg++)
        {
            var points = route.Legs[leg].Points;
            Assert.Near(0, Vector3D.Distance(points[0], route.Stops[leg].Position), 1e-7, "leg starts at previous checkpoint");
            Assert.Near(0, Vector3D.Distance(points[^1], route.Stops[leg + 1].Position), 1e-7, "leg ends at next checkpoint");
            for (int i = 1; i < points.Count; i++)
                Assert.True(EarthEnvironmentRoute.Corridor(world, points[i - 1], points[i]), "two-metre samples remain dry, outside monuments and within grade bound");
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
}
