using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// The grass's one coverage rule (docs/WALKING_GROUND.md §18, Data/ground-cover-habitats.json): its species and accents
/// match the game's catalogues, habitat weights add up to one, plans are deterministic, and over a field of cells the
/// occupied share, the species mix and the flowers come out as the table means, patches moving grass about without
/// thinning it.
/// </summary>
public static class GroundCoverHabitatTests
{
    private static readonly GrassSite Meadow = new(Grass: 0.6, Litter: 0, Moisture: 0.6, Temperature: 14, Canopy: 0, Dryness: 0.3);
    private static readonly GrassSite Forest = new(Grass: 0.4, Litter: 0.8, Moisture: 0.7, Temperature: 10, Canopy: 1, Dryness: 0.2);

    [Test]
    public static void GrassHabitatsMatchTheGameCatalogues()
    {
        GroundCoverHabitats table = GroundCoverHabitats.Default;
        using JsonDocument grass = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "game", "assets", "surface", "grass", "grass.json")));
        string[] clumps = grass.RootElement.GetProperty("species").EnumerateArray().Select(s => s.GetProperty("key").GetString()!)
            .Where(k => k != "litter").ToArray();
        Assert.True(table.Species.SequenceEqual(clumps), $"habitat species {string.Join(",", table.Species)} are the catalogue's {string.Join(",", clumps)}");
        using JsonDocument cover = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "game", "assets", "surface", "ground-cover", "catalog.json")));
        string[] families = cover.RootElement.GetProperty("families").EnumerateArray().Select(f => f.GetProperty("key").GetString()!).ToArray();
        Assert.True(table.Accents.SequenceEqual(families), $"accents {string.Join(",", table.Accents)} are the authored families {string.Join(",", families)}");
        foreach (GrassHabitat h in Enum.GetValues<GrassHabitat>())
        {
            double flowers = table.Mix(h)[Array.IndexOf(table.Species, "flower")];
            Assert.Near(table.FlowerShare(h), flowers, 1e-9, $"{h}'s stated flower share is its mix's");
        }
    }

    [Test]
    public static void GrassHabitatWeightsAddUpToOne()
    {
        Span<double> w = stackalloc double[GroundCoverHabitats.HabitatCount];
        var r = new Hollow.Core.Rng(17);
        for (int i = 0; i < 2000; i++)
        {
            var site = new GrassSite(r.NextDouble(), r.NextDouble(), r.NextDouble(), r.Range(-20, 35), r.NextDouble(), r.NextDouble());
            GroundCoverHabitats.Weigh(site, w);
            double sum = 0;
            foreach (double x in w) { Assert.True(x >= 0, "a habitat weight is never negative"); sum += x; }
            Assert.Near(1, sum, 1e-9, "habitat weights add up to one");
        }
        GroundCoverHabitats.Weigh(Forest, w);
        Assert.True(w[(int)GrassHabitat.ForestFloor] > 0.99, "closed canopy is forest floor");
        GroundCoverHabitats.Weigh(Meadow with { Temperature = -8 }, w);
        Assert.True(w[(int)GrassHabitat.Tundra] > 0.99, "open and freezing is tundra");
        GroundCoverHabitats.Weigh(Meadow with { Moisture = 0.1 }, w);
        Assert.True(w[(int)GrassHabitat.Dry] > 0.99, "open and dry is dry country");
        GroundCoverHabitats.Weigh(Meadow, w);
        Assert.True(w[(int)GrassHabitat.Meadow] > 0.99, "open, mild and moist is meadow");
        GroundCoverHabitats.Weigh(Meadow with { Canopy = 0.25 }, w);
        Assert.True(w[(int)GrassHabitat.ForestEdge] > 0.5, "broken canopy is mostly forest edge");
    }

    [Test]
    public static void GrassPlansAreDeterministic()
    {
        GroundCoverHabitats table = GroundCoverHabitats.Default;
        int differ = 0;
        for (int i = 0; i < 500; i++)
        {
            GrassPlan a = table.Plan(Meadow, 2, 4000 + i, 900, i * 0.46, 412.0);
            GrassPlan b = table.Plan(Meadow, 2, 4000 + i, 900, i * 0.46, 412.0);
            Assert.Equal(a, b, "the same cell and site give the same plan");
            if (table.Plan(Meadow, 3, 4000 + i, 900, i * 0.46, 412.0) != a) differ++;
        }
        Assert.True(differ > 400, $"another face's cells have their own plans ({differ} of 500 differ)");
    }

    [Test]
    public static void GrassCoverageFollowsTheHabitatTable()
    {
        GroundCoverHabitats table = GroundCoverHabitats.Default;
        (double occupied, double[] species, double flowersOfClumps) Field(GrassSite site, int face)
        {
            int n = 260, grown = 0;
            var counts = new double[table.Species.Length];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                GrassPlan p = table.Plan(site, face, 10000 + x, 20000 + y, x * 0.46, y * 0.46);
                if (!p.Grows) continue;
                grown++;
                counts[p.Species]++;
                Assert.True(p.Height > 0 && p.Width > 0 && p.Rank >= 0 && p.Rank < 1, "a clump has a size and a rank");
            }
            return (grown / (double)(n * n), counts.Select(c => c / Math.Max(grown, 1)).ToArray(), counts[3] / Math.Max(grown, 1));
        }
        var meadow = Field(Meadow, 0);
        Assert.InRange(0.92, 1.0, meadow.occupied, "a meadow is closed: its clumps occupy the habitat's share");
        Assert.InRange(0.03, 0.09, meadow.flowersOfClumps, "a meadow keeps its flowers among the grass");
        Assert.True(meadow.species.All(s => s > 0.02), "every species grows in a meadow: " + string.Join(", ", meadow.species.Select(s => s.ToString("0.000"))));
        var forest = Field(Forest, 1);
        Assert.InRange(0.42, 0.58, forest.occupied, "a forest floor keeps its deliberate gaps (half the ground)");
        Assert.True(forest.species[4] > meadow.species[4] * 3, "herbs belong under the trees");
        var dry = Field(Meadow with { Dryness = 1.0 }, 0);
        Assert.True(dry.species[2] > meadow.species[2] * 1.4, "dry grass follows the grass colour's dryness");
        var bare = Field(Meadow with { Grass = 0 }, 0);
        Assert.Equal(0.0, bare.occupied, "no grass where the cover has none");
        Span<double> w = stackalloc double[GroundCoverHabitats.HabitatCount];
        GroundCoverHabitats.Weigh(Meadow, w);
        Assert.Near(0.97, table.Occupancy(Meadow, w), 1e-9, "occupancy is the habitat's ceiling over its grass range, applied once");
    }

    [Test]
    public static void GrassPatchNoiseIsSmoothAndCentred()
    {
        double sum = 0, largestStep = 0;
        int n = 0;
        for (int y = 0; y < 300; y++)
        for (int x = 0; x < 300; x++)
        {
            double a = GroundCoverHabitats.PatchNoise(4, x * 0.05, y * 0.05, 3);
            Assert.InRange(0, 1, a, "patch noise lies in 0..1");
            sum += a;
            n++;
            largestStep = Math.Max(largestStep, Math.Abs(a - GroundCoverHabitats.PatchNoise(4, x * 0.05 + 0.01, y * 0.05, 3)));
        }
        Assert.InRange(0.45, 0.55, sum / n, "patches neither add nor take away grass on average");
        Assert.True(largestStep < 0.08, $"patches vary smoothly (largest change over a fiftieth of a patch {largestStep:0.000})");
    }
}
