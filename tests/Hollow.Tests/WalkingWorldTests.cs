using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// The walking-scale ground (docs/WALKING_GROUND.md, package S4): the same world every time, the real sea cover, heights
/// in the walking range, streams that run downhill to the sea or a lake, no seams, scatter at the level by size, speed.
/// </summary>
public static class WalkingWorldTests
{
    // Pins heights, water and biomes at fixed points of the first world's walking-scale ground. A change to the
    // generator or its table moves this: decide on purpose, then update it (and say so in the commit).
    private const ulong WalkingGolden = 0x1D83ABE87CA2D9EAUL;

    /// <summary>The first world's game radius (ZoneSpec.FirstWorldRadius).</summary>
    private const double Radius = 2410;

    private static readonly Lazy<(WalkingWorld World, double BuildMs)> First = new(() =>
    {
        var found = FirstWorld.Find(Fixtures.Galaxy)!.Value;
        var watch = Stopwatch.StartNew();
        WalkingWorld world = WalkingWorld.For(found.System, found.Planet, Radius);
        return (world, watch.Elapsed.TotalMilliseconds);
    });

    /// <summary>Two worlds from the same body agree bit for bit; heights, water and biomes at 600 points hash to a pinned value.</summary>
    [Test]
    public static void WalkingWorldIsDeterministic()
    {
        WalkingWorld world = First.Value.World;
        var found = FirstWorld.Find(Fixtures.Galaxy)!.Value;
        WalkingWorld again = WalkingWorld.For(found.System, found.Planet, Radius);
        ulong h = Hash64.Of((ulong)BitConverter.DoubleToInt64Bits(world.Radius));
        foreach (Vector3D d in PlanetSurface.FibonacciSphere(600))
        {
            WalkingSample a = world.Fields(d), b = again.Fields(d);
            Assert.True(a.Height.Equals(b.Height) && a.Water.Equals(b.Water), "two worlds agree");
            Biome biome = world.BiomeAt(d);
            Assert.Equal(biome, again.BiomeAt(d), "same biome");
            h = Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(a.Height));
            h = Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(a.Water));
            h = Hash64.Combine(h, (ulong)biome);
        }
        Assert.Equal(world.Lakes.Count, again.Lakes.Count, "same lakes");
        Assert.Equal(world.Drainage.Count, again.Drainage.Count, "same drainage");
        Fixtures.Report($"{world.Lakes.Count} lakes, {world.Drainage.Count} drainage pieces, hash 0x{h:X16}");
        Assert.Equal(WalkingGolden, h, "walking-scale ground golden hash");
    }

    /// <summary>The sea covers the first world's real 45 %, and land stands in the walking range: hills of tens of metres, peaks of a few hundred.</summary>
    [Test]
    public static void SeaCoverAndHeightsAreInRange()
    {
        WalkingWorld world = First.Value.World;
        Vector3D[] points = PlanetSurface.FibonacciSphere(8000);
        var land = new List<double>();
        foreach (Vector3D d in points)
        {
            double height = world.HeightAt(d);
            if (height >= 0)
            {
                land.Add(height);
            }
        }
        land.Sort();
        double sea = 1 - (double)land.Count / points.Length;
        double median = land[land.Count / 2], highest = land[^1];
        Fixtures.Report($"sea {sea:P1}; land heights: median {median:0} m, 90 % under {land[land.Count * 9 / 10]:0} m, highest {highest:0} m; ice below {world.IceTemperature:0.0} °C");
        Assert.InRange(0.43, 0.47, sea, "sea cover near the first world's 45 %");
        Assert.InRange(5, 60, median, "most land is low: plains and hills");
        Assert.InRange(200, 600, highest, "a few mountains of a few hundred metres");
    }

    /// <summary>
    /// Every stream runs from its source to the sea or a lake, never uphill, ends at the sea's level or the lake's, and
    /// its water lies in its channel (above the ground at the channel's middle).
    /// </summary>
    [Test]
    public static void StreamsReachTheSeaOrALake()
    {
        WalkingWorld world = First.Value.World;
        Drainage grid = world.DrainageGrid!;
        List<int> sources = grid.StreamSources();
        int toSea = 0, toLakes = 0;
        foreach (int source in sources)
        {
            int cell = source, steps = 0;
            double last = double.PositiveInfinity;
            while (grid.Downstream(cell) >= 0 && grid.LakeOf(cell) < 0)
            {
                double surface = grid.StreamSurface(cell);
                Assert.True(grid.IsStream(cell), "a stream stays a stream down to its mouth");
                Assert.True(surface <= last + 1e-9, "water never runs uphill");
                last = surface;
                cell = grid.Downstream(cell);
                Assert.True(++steps < grid.CellCount, "the way down ends");
            }
            if (grid.LakeOf(cell) >= 0)
            {
                toLakes++;
                Assert.True(last >= world.Lakes[grid.LakeOf(cell)].Level - 1e-9, "a stream meets a lake at its level or above");
            }
            else
            {
                toSea++;
                Assert.True(grid.IsSea(cell), "a way that leaves no lake ends in the sea");
                Assert.True(last >= 0, "a stream meets the sea at sea level or above");
            }
        }
        int wet = 0, checkedPieces = 0;
        foreach (DrainageSegment s in world.Drainage)
        {
            if (!s.Stream)
            {
                continue;
            }
            Vector3D middle = (s.A + s.B).Normalized;
            WalkingSample here = world.Fields(middle);
            if (here.Height < 0)
            {
                continue; // the mouth's last piece, out in the sea
            }
            checkedPieces++;
            if (here.Water > here.Height)
            {
                wet++;
            }
        }
        double share = (double)wet / Math.Max(1, checkedPieces);
        Fixtures.Report($"{sources.Count} sources: {toSea} to the sea, {toLakes} into lakes; water at {share:P1} of {checkedPieces} stream pieces' middles");
        Assert.True(sources.Count >= 10, "the world has streams");
        Assert.True(toSea > 0, "some reach the sea");
        Assert.True(share > 0.97, "streams' water lies in their channels");
    }

    /// <summary>
    /// No seams: along two long lines over land and sea, the ground moves less than 1.5 m every 5 cm (a slope of 30, a
    /// cliff), so no piece of the generator (provinces, tiles of the drainage's index, lakes' shapes) leaves a step.
    /// </summary>
    [Test]
    public static void WalkingGroundHasNoSeams()
    {
        WalkingWorld world = First.Value.World;
        double worst = 0;
        int samples = 0;
        foreach ((Vector3D from, Vector3D toward) in new[]
                 {
                     (new Vector3D(0.3, -0.4, 0.86), new Vector3D(1, 0.2, 0)),
                     (new Vector3D(-0.6, 0.6, 0.5), new Vector3D(0, 0.3, 1)),
                 })
        {
            Vector3D start = from.Normalized;
            Vector3D along = Vector3D.Cross(Vector3D.Cross(start, toward), start).Normalized;
            const double Step = 0.05, Length = 2500;
            double previous = world.HeightAt(start);
            for (double s = Step; s <= Length; s += Step)
            {
                double angle = s / Radius;
                Vector3D d = (start * DMath.Cos(angle) + along * DMath.Sin(angle)).Normalized;
                double h = world.HeightAt(d);
                worst = Math.Max(worst, Math.Abs(h - previous));
                previous = h;
                samples++;
            }
        }
        Fixtures.Report($"{samples} samples 5 cm apart: the largest step {worst:0.000} m");
        Assert.True(worst < 1.5, "no step over 1.5 m in 5 cm");
    }

    /// <summary>The scatter runs at the level by size (about 118 m on the first world), the same twice, everything on dry ground.</summary>
    [Test]
    public static void ScatterAtTheLevelBySize()
    {
        WalkingWorld world = First.Value.World;
        ScatterPlan plan = world.ScatterPlan;
        Assert.Equal(5, plan.Level, "level 5 on a 2.41 km world");
        // A tile on land in the temperate belt.
        TileKey tile = default;
        foreach (Vector3D d in PlanetSurface.FibonacciSphere(4000))
        {
            double latitude = DMath.Asin(d.Y) * DMath.RadToDeg;
            if (latitude > 35 && latitude < 55 && world.HeightAt(d) > 8 && world.ForestCover(d, world.Fields(d)) > 0.5)
            {
                tile = TileKey.At(d, plan.Level);
                break;
            }
        }
        IReadOnlyList<ScatterItem> a = world.ScatterFor(tile), b = world.ScatterFor(tile);
        Assert.Equal(a.Count, b.Count, "same count");
        int trees = 0;
        var ids = new HashSet<int>();
        for (int i = 0; i < a.Count; i++)
        {
            Assert.True(a[i].Index == b[i].Index && a[i].Direction == b[i].Direction, "same item");
            Assert.True(ids.Add(a[i].Index), "unique index in the tile");
            Assert.Near(world.HeightAt(a[i].Direction), a[i].Height, 1e-3, "on the ground");
            WalkingSample s = world.Fields(a[i].Direction);
            Assert.True(!(s.Water > s.Height), "nothing grows in the water");
            if (a[i].Kind <= ScatterKind.Broadleaf)
            {
                trees++;
            }
        }
        double edge = TileKey.EdgeLength(plan.Level, Radius);
        double perHectare = trees / (edge * edge / 10_000);
        Fixtures.Report($"tile {tile}: {a.Count} objects, {trees} trees ({perHectare:0} a hectare, in woods)");
        Assert.True(a.Count > 0, "a wooded tile has scatter");
        Assert.True(perHectare < 200, "woods are realistic, not a tree every metre");
    }

    /// <summary>Speed: the world builds in a few seconds and a height costs a few microseconds (the server builds collision tiles all the time).</summary>
    [Test]
    public static void WalkingWorldIsFastEnough()
    {
        (WalkingWorld world, double buildMs) = First.Value;
        Vector3D[] points = PlanetSurface.FibonacciSphere(20_000);
        double sink = 0;
        var watch = Stopwatch.StartNew();
        foreach (Vector3D d in points)
        {
            sink += world.HeightAt(d);
        }
        double perHeight = watch.Elapsed.TotalMilliseconds * 1000 / points.Length;
        Fixtures.Report($"built in {buildMs:0} ms; {perHeight:0.00} µs a height [{sink:0}]");
        Assert.True(buildMs < 5000, "builds in under 5 s");
        Assert.True(perHeight < 15, "a height in well under 15 µs");
    }
}
