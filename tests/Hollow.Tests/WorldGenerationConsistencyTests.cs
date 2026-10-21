using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Bounded checks of complete surface results and cold/warm shared placement queries.</summary>
public static class WorldGenerationConsistencyTests
{
    private const int Workers = 4;
    private const int FormationProbes = 256;

    [Test]
    public static void LayoutZeroIsConsistentAcrossWorldsQueryOrderAndThreads() => CheckLayout(0);

    [Test]
    public static void LayoutFiveIsConsistentAcrossWorldsQueryOrderAndThreads() => CheckLayout(5);

    [Test]
    public static void OffshoreAndShoreSamplesHaveExplicitReliefFields()
    {
        var (system, earth) = StartingWorld.Find();
        // With no secondary landmass fields, Landmass == Continents and shore == Continent.
        // A negative continent value alone does NOT prove offshore on the production profiles:
        // they can lift inland pockets of sea with relief. No production profile is changed here.
        WalkingParameters profile = WalkingParameters.Earth with { IslandShare = 0, CoastDetail = 0 };
        WalkingWorld world = WalkingWorld.For(system, earth, 1200, profile);
        Vector3D[] survey = PlanetSurface.FibonacciSphere(96);
        (Vector3D sea, Vector3D land) = ShorePair(world, survey);
        int offshore = 0, inland = 0;
        foreach (Vector3D direction in survey.Concat(new[] { sea, land }))
        {
            SampleSnapshot result = Capture(world, direction);
            WalkingSample sample = result.Fields;
            if (sample.Continent <= 0)
            {
                offshore++;
                Assert.Equal(0.0, sample.Plains, "offshore plains weight is initialized");
                Assert.Equal(0.0, sample.Hills, "offshore hills weight is initialized");
                Assert.Equal(0.0, sample.Tableland, "offshore tableland weight is initialized");
                Assert.Equal(0.0, sample.Mountains, "offshore mountain weight is initialized");
                Assert.Equal(0.0, sample.Roughness, "offshore roughness has no inland relief");
            }
            else
            {
                inland++;
                Assert.Near(1, ReliefSum(sample), 1e-12, "inland relief retains normalized province weights");
            }
        }
        Assert.True(offshore > 0 && inland > 0, "the fixture exercises both sides of the relief branch");
        Fixtures.Report($"{offshore} offshore and {inland} inland complete samples, including a bounded shoreline bracket");
    }

    private static void CheckLayout(int variant)
    {
        var (system, earth) = StartingWorld.Find();
        WalkingParameters profile = WalkingParameters.Earth with { LayoutVariant = variant };
        WalkingWorld world = WalkingWorld.For(system, earth, 1200, profile);
        WalkingWorld independent = WalkingWorld.For(system, earth, 1200, profile with { });
        Assert.True(!ReferenceEquals(world, independent), "independent worlds have separate generation and caches");
        Assert.Equal(profile, independent.Parameters, "independent worlds use identical complete profiles");
        Assert.Equal(variant, world.Parameters.LayoutVariant, "layout variant is explicit in this fixture");

        MonumentPlacement[] sites = world.Monuments.All.ToArray();
        Assert.True(sites.Length > 0, "site comparison is not empty");
        Vector3D[] points = SamplePoints(world, sites);
        SampleSnapshot[] expected = points.Select(p => Capture(world, p)).ToArray();
        Assert.True(expected.Any(p => p.Fields.Height < 0), "complete samples include sea floor");
        Assert.True(expected.Any(p => double.IsNegativeInfinity(p.Fields.Water)), "complete samples include dry ground's water sentinel");

        TileKey[] tiles = PlanetSurface.FibonacciSphere(FormationProbes)
            .Select(p => TileKey.At(p, world.Cliffs.Level)).Distinct().ToArray();
        CliffPlacement[][] formations = tiles.Select(t => world.Cliffs.FormationAt(t).ToArray()).ToArray();
        int occupied = Array.FindIndex(formations, f => f.Length > 0);
        Assert.True(occupied >= 0, "the bounded formation sample includes real geometry");

        // Dedicated workers guarantee concurrent callers even on a constrained thread pool. All
        // buffers belong to an invocation. The second world's sites/formations are still cold;
        // pre-reading them serially would only exercise cache hits in this part of the test.
        using var start = new Barrier(Workers);
        var tasks = new Task[Workers];
        for (int worker = 0; worker < Workers; worker++)
        {
            int offset = worker;
            tasks[worker] = Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                IReadOnlyList<MonumentPlacement> independentSites = independent.Monuments.All;
                // Site generation is lazy and serializes its first readers. Rendezvous again
                // before the shared cold formation request; do not put an assertion before it.
                start.SignalAndWait();
                Assert.True(sites.SequenceEqual(independentSites), $"layout {variant}: concurrent independent sites agree");
                AssertFormation(formations[occupied], independent, tiles[occupied]);
                for (int step = 0; step < points.Length; step++)
                {
                    int index = (points.Length - 1 - step + offset * (points.Length / Workers)) % points.Length;
                    AssertSnapshot(expected[index], Capture(independent, points[index]), $"layout {variant}, worker {offset}, point {index}");
                    // Replay across both instances on each thread as well as changing query order.
                    // Identical profiles alone do not test cache isolation between different worlds.
                    AssertSnapshot(expected[index], Capture(world, points[index]), $"layout {variant}, interleaved world, point {index}");
                }
                for (int step = 0; step < tiles.Length; step++)
                {
                    int index = (tiles.Length - 1 - step + offset * (tiles.Length / Workers)) % tiles.Length;
                    AssertFormation(formations[index], independent, tiles[index]);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        Task.WaitAll(tasks);

        // Warm replay in reverse tile/point order after the independent cold concurrent requests.
        for (int i = points.Length - 1; i >= 0; i--)
            AssertSnapshot(expected[i], Capture(independent, points[i]), $"layout {variant}, reverse warm point {i}");
        for (int i = tiles.Length - 1; i >= 0; i--)
            AssertFormation(formations[i], independent, tiles[i]);
        Assert.Equal(world.LandingSite, independent.LandingSite, "independent landing sites agree");
        Assert.True(world.Lakes.SequenceEqual(independent.Lakes), "independent lake records agree");
        Assert.True(world.Drainage.SequenceEqual(independent.Drainage), "independent drainage records agree");
        Fixtures.Report($"layout {variant}: {points.Length} complete samples, {sites.Length} sites, {tiles.Length} formation tiles " +
            $"({formations.Count(f => f.Length > 0)} occupied), {Workers} concurrent workers and reverse warm replay");
    }

    private static Vector3D[] SamplePoints(WalkingWorld world, MonumentPlacement[] sites)
    {
        Vector3D[] survey = PlanetSurface.FibonacciSphere(96);
        var points = new List<Vector3D>(survey);
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            points.Add(CubeSphere.Normal(face)); // both poles and the four equatorial axes
            foreach (double side in new[] { -1.0, 1.0 })
            {
                points.Add(CubeSphere.Direction(face, side, 0));
                points.Add(CubeSphere.Direction(face, 0, side));
                // Both sides of each cube edge, not only directions exactly on the seam.
                foreach (double across in new[] { side - 1e-6, side + 1e-6 })
                {
                    points.Add(CubeSphere.Direction(face, across, 0));
                    points.Add(CubeSphere.Direction(face, 0, across));
                }
                points.Add(CubeSphere.Direction(face, side, -1));
                points.Add(CubeSphere.Direction(face, side, 1));
            }
        }
        (Vector3D sea, Vector3D land) = ShorePair(world, survey);
        points.Add(sea);
        points.Add(land);
        points.AddRange(sites.Take(4).Select(s => s.Up)); // finalized monument pads as well as raw relief
        return points.Distinct().ToArray();
    }

    private static (Vector3D Sea, Vector3D Land) ShorePair(WalkingWorld world, Vector3D[] survey)
    {
        Vector3D[] sea = survey.Where(p => world.Fields(p).Height < 0).ToArray();
        Vector3D[] land = survey.Where(p => world.Fields(p).Height > Math.Max(0, world.WaterLevelAt(p))).ToArray();
        Assert.True(sea.Length > 0 && land.Length > 0, "bounded shoreline search brackets sea and dry land");
        Vector3D low = sea[0], high = land[0];
        double best = double.PositiveInfinity;
        foreach (Vector3D a in sea)
        foreach (Vector3D b in land)
        {
            double distance = (a - b).LengthSquared;
            if (distance < best) { best = distance; low = a; high = b; }
        }
        for (int step = 0; step < 24; step++)
        {
            Vector3D middle = (low + high).Normalized;
            if (world.Fields(middle).Height < 0) low = middle;
            else high = middle;
        }
        Assert.True(world.Fields(low).Height < 0 && world.Fields(high).Height >= 0, "shore bracket retains both sea-level sides");
        Assert.True((low - high).Length * world.Radius < 0.01, "shore samples are less than one centimetre apart");
        return (low, high);
    }

    private sealed record SampleSnapshot(WalkingSample Fields, SurfaceSample Surface, GroundCover Cover,
        double[] Biomes, double[] Materials, MonumentPlacement? Site);

    private static SampleSnapshot Capture(WalkingWorld world, Vector3D direction)
    {
        WalkingSample fields = world.Fields(direction);
        AssertFields(fields);
        SurfaceSample surface = world.Sample(direction);
        Assert.Equal(world.HeightAt(direction), surface.Height, "drawn height includes the authoritative monument pad");
        Assert.Equal(fields.Water, world.WaterLevelAt(direction), "water query agrees with the complete sample");
        foreach (double value in new[] { surface.Height, surface.Continent, surface.Mountain, surface.Beach, surface.Roughness })
            Assert.True(double.IsFinite(value), "every public surface field is finite");
        var biomes = new double[BiomeTable.Count];
        var materials = new double[BiomeTable.MaterialCount];
        // Two fixed slope inputs exercise gentle/steep material rules without testing a second
        // implementation of terrain normals. The input is identical for all query orders.
        double slope = direction.Y >= 0 ? 0.15 : 0.7;
        GroundCover cover = world.Cover(direction, surface, slope, biomes, materials);
        AssertWeights(biomes, "biome");
        AssertWeights(materials, "material");
        Assert.True(double.IsFinite(cover.Temperature), "cover temperature is finite");
        Assert.InRange(0, 1, cover.Moisture, "cover moisture");
        Assert.InRange(0, 1, cover.Grass, "cover grass");
        Assert.True(double.IsFinite(cover.Trees) && cover.Trees >= 0, "cover tree density is finite and nonnegative");
        return new SampleSnapshot(fields, surface, cover, biomes, materials, world.Monuments.At(direction));
    }

    private static void AssertFields(WalkingSample sample)
    {
        foreach (double value in new[] { sample.Height, sample.Continent, sample.Plains, sample.Hills, sample.Tableland,
                     sample.Mountains, sample.Rocky, sample.Roughness, sample.Wet, sample.Ice })
            Assert.True(double.IsFinite(value), "every non-water WalkingSample field is finite");
        Assert.True(double.IsFinite(sample.Water) || double.IsNegativeInfinity(sample.Water), "water is finite or the explicit dry sentinel");
        Assert.True(double.IsNegativeInfinity(sample.Water) || sample.Water > sample.Height, "finite water stands above raw ground");
        foreach (double value in new[] { sample.Plains, sample.Hills, sample.Tableland, sample.Mountains,
                     sample.Rocky, sample.Roughness, sample.Wet, sample.Ice })
            Assert.InRange(0, 1, value, "bounded relief, coast, roughness, wetness and ice fields");
        double relief = ReliefSum(sample);
        Assert.True(relief == 0 || Math.Abs(relief - 1) <= 1e-12, "relief is absent offshore or normalized inland");
        Assert.True(sample.Ice == 0 || sample.Ice == 1, "ice is a defined binary field");
    }

    private static double ReliefSum(WalkingSample sample) => sample.Plains + sample.Hills + sample.Tableland + sample.Mountains;

    private static void AssertWeights(double[] weights, string name)
    {
        foreach (double weight in weights)
            Assert.True(double.IsFinite(weight) && weight >= 0 && weight <= 1, $"{name} weights are finite and bounded");
        Assert.Near(1, weights.Sum(), 1e-9, $"{name} weights are normalized");
    }

    private static void AssertSnapshot(SampleSnapshot expected, SampleSnapshot actual, string context)
    {
        Assert.Equal(expected.Fields, actual.Fields, context + ": all walking fields");
        Assert.Equal(expected.Surface, actual.Surface, context + ": finalized surface");
        Assert.Equal(expected.Cover, actual.Cover, context + ": cover");
        Assert.True(expected.Biomes.SequenceEqual(actual.Biomes), context + ": biome weights");
        Assert.True(expected.Materials.SequenceEqual(actual.Materials), context + ": material weights");
        Assert.Equal(expected.Site, actual.Site, context + ": site lookup");
    }

    private static void AssertFormation(CliffPlacement[] expected, WalkingWorld world, TileKey tile) =>
        Assert.True(expected.SequenceEqual(world.Cliffs.FormationAt(tile)), $"{tile}: all formation identities and transforms agree");
}
