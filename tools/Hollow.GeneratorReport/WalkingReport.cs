using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.GeneratorReport;

/// <summary>
/// The walking-scale ground of the first world (docs/WALKING_GROUND.md, package S4): look-dev pictures and numbers.
/// <c>dotnet run -c Release --project tools/Hollow.GeneratorReport -- walking stats|relief|biomes|rivers|globe|zoom|slopes|spots|compare|all</c>
/// </summary>
public static class WalkingReport
{
    /// <summary>The first world's game radius (SCALE.md §3.1; ZoneSpec.FirstWorldRadius until S1's GameScale).</summary>
    public const double FirstWorldRadius = 2410;

    private static string Folder => Path.Combine(Program.RepoRoot(), "docs", "images", "scale", "s4");

    public static int Run(string[] args)
    {
        string command = args.Length > 1 ? args[1] : "stats";
        string Arg(int i, string fallback) => args.Length > i ? args[i] : fallback;
        double Num(int i, double fallback) => args.Length > i ? double.Parse(args[i], CultureInfo.InvariantCulture) : fallback;
        var watch = Stopwatch.StartNew();
        WalkingWorld world = Build();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Walking-scale first world: radius {world.Radius:0} m, built in {watch.ElapsedMilliseconds} ms, ice below {world.IceTemperature:0.0} °C"));
        switch (command)
        {
            case "stats":
                Stats(world);
                return 0;
            case "relief":
            case "biomes":
            case "rivers":
                Map(world, command, Arg(2, Path.Combine(Folder, $"world_{command}.png")), (int)Num(3, 2048));
                return 0;
            case "globe":
                Globe(world, Arg(2, Path.Combine(Folder, "world_globe.png")), (int)Num(3, 700));
                return 0;
            case "zoom":
                Zoom(world, Num(2, 45), Num(3, -170), Num(4, 800), Arg(5, Path.Combine(Folder, "zoom.png")), (int)Num(6, 800), trees: true);
                return 0;
            case "slopes":
                Slopes(world, Num(2, 45), Num(3, -170), Num(4, 800), Arg(5, Path.Combine(Folder, "slopes.png")), (int)Num(6, 800));
                return 0;
            case "closeups":
                Closeups(world, Arg(2, Folder));
                return 0;
            case "spots":
                foreach (Spot spot in Spots(world))
                {
                    Console.WriteLine(spot);
                }
                return 0;
            case "probe":
                Probe(world, Num(2, 45), Num(3, -170), Num(4, 800), (int)Num(5, 800), Num(6, 400), Num(7, 380), Num(8, 480));
                return 0;
            case "compare":
                Compare(world, Arg(2, Path.Combine(Folder, "compare_standin.png")), (int)Num(3, 1536));
                return 0;
            case "all":
                All(world, Arg(2, Folder));
                return 0;
            default:
                Console.Error.WriteLine("usage: walking stats | relief|biomes|rivers [png] [width] | globe [png] [size] | zoom|slopes lat lon metres [png] [size] | spots | compare | all");
                return 2;
        }
    }

    internal static WalkingWorld Build()
    {
        var galaxy = new Galaxy.Galaxy(null);
        var found = FirstWorld.Find(galaxy) ?? throw new InvalidOperationException("The first world is gone.");
        WalkingParameters parameters = WalkingParameters.Default;
        // Look-dev: HOLLOW_WALKING="StreamValley=20;Meander=0" changes numbers of the table for one run.
        foreach (string pair in (Environment.GetEnvironmentVariable("HOLLOW_WALKING") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split('=');
            var property = typeof(WalkingParameters).GetProperty(kv[0].Trim()) ?? throw new ArgumentException($"No parameter {kv[0]}");
            object value = property.PropertyType == typeof(int) ? int.Parse(kv[1], CultureInfo.InvariantCulture) : double.Parse(kv[1], CultureInfo.InvariantCulture);
            parameters = parameters with { };
            property.SetValue(parameters, value);
            Console.WriteLine($"look-dev: {kv[0].Trim()} = {kv[1]}");
        }
        return WalkingWorld.For(found.System, found.Planet, FirstWorldRadius, parameters);
    }

    // ---------------------------------------------------------------- numbers

    private static void Stats(WalkingWorld world)
    {
        // Speed: one thread, points spread over the land.
        Vector3D[] points = PlanetSurface.FibonacciSphere(40_000);
        double sink = 0;
        var watch = Stopwatch.StartNew();
        foreach (Vector3D d in points)
        {
            sink += world.HeightAt(d);
        }
        double perHeight = watch.Elapsed.TotalMilliseconds * 1000 / points.Length;
        watch.Restart();
        var standIn = new StandInWorld(world.Radius, 1);
        foreach (Vector3D d in points)
        {
            sink += standIn.HeightAt(d);
        }
        double standInPerHeight = watch.Elapsed.TotalMilliseconds * 1000 / points.Length;
        // A physics tile (level 6, about 59 m, 64 cells and a 2 m margin), as the server builds them.
        int tileLevel = TileKey.LevelFor(world.Radius, 64);
        TileKey tile = TileKey.At(ZoneFrame.Direction(45 * DMath.DegToRad, -170 * DMath.DegToRad), tileLevel);
        watch.Restart();
        const int Tiles = 6;
        for (int i = 0; i < Tiles; i++)
        {
            sink += ((ISurface)world).GroundCollision(ZoneFrame.World, new TileKey(tile.Face, tile.Level, tile.X + i, tile.Y), 2).TriangleCount;
        }
        double perTile = watch.Elapsed.TotalMilliseconds / Tiles;
        watch.Restart();
        for (int i = 0; i < Tiles; i++)
        {
            sink += ((ISurface)standIn).GroundCollision(ZoneFrame.World, new TileKey(tile.Face, tile.Level, tile.X + i, tile.Y), 2).TriangleCount;
        }
        double standInTile = watch.Elapsed.TotalMilliseconds / Tiles;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"speed: {perHeight:0.00} µs a height (stand-in {standInPerHeight:0.00}); physics tile (level {tileLevel}) {perTile:0.0} ms (stand-in {standInTile:0.0}) [{sink:0}]"));

        // Cover, heights and relief, by area (evenly spread points).
        int land = 0, water = 0;
        var heights = new List<double>();
        var relief = new double[4];
        var biomes = new double[BiomeTable.Count];
        double treesSum = 0, cover = 0;
        Span<double> w = stackalloc double[BiomeTable.Count];
        foreach (Vector3D d in points)
        {
            WalkingSample s = world.Fields(d);
            if (s.Height < 0)
            {
                continue;
            }
            land++;
            if (s.Wetted)
            {
                water++;
                continue;
            }
            heights.Add(s.Height);
            relief[(int)s.Relief]++;
            ClimateSample climate = world.Climate(d, s);
            world.BiomeWeights(s, climate, w);
            biomes[(int)BiomeTable.Dominant(w)]++;
            VegetationSample v = world.Vegetation(d, s, climate, w, 0);
            treesSum += v.Trees;
            cover += world.ForestCover(d, s);
        }
        heights.Sort();
        string Q(double f) => heights[Math.Clamp((int)(f * heights.Count), 0, heights.Count - 1)].ToString("0", CultureInfo.InvariantCulture);
        int dry = heights.Count;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sea {(double)(points.Length - land) / points.Length:P1}; land {land * 73.0 / points.Length:0.0} km² of 73, of it under lakes and streams {(double)water / land:P1}"));
        Console.WriteLine($"land heights: p10 {Q(0.1)}  p50 {Q(0.5)}  p90 {Q(0.9)}  p99 {Q(0.99)}  max {Q(1)} m");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"relief (dry land): plains {relief[0] / dry:P0}, hills {relief[1] / dry:P0}, tablelands {relief[2] / dry:P0}, mountains {relief[3] / dry:P0}"));
        Console.WriteLine("biomes (dry land): " + string.Join(", ", Enumerable.Range(0, BiomeTable.Count)
            .Where(b => biomes[b] > 0).OrderByDescending(b => biomes[b])
            .Select(b => string.Create(CultureInfo.InvariantCulture, $"{(Biome)b} {biomes[b] / dry:P0}"))));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"woods: {cover / dry:P0} of dry land wooded; {treesSum / dry * 10_000:0} trees a hectare on average (today's planet: about 300 in its forests)"));

        // Water.
        double streamLength = 0, valleyLength = 0;
        foreach (DrainageSegment s in world.Drainage)
        {
            double length = (s.A - s.B).Length * world.Radius;
            if (s.Stream)
            {
                streamLength += length;
            }
            else
            {
                valleyLength += length;
            }
        }
        WalkingLake[] lakes = world.Lakes.OrderByDescending(l => l.Area).ToArray();
        int mouths = 0, intoLakes = 0;
        Drainage grid = world.DrainageGrid!;
        List<int> sources = grid.StreamSources();
        foreach (int source in sources)
        {
            int c = source;
            while (grid.Downstream(c) >= 0 && grid.LakeOf(c) < 0)
            {
                c = grid.Downstream(c);
            }
            if (grid.LakeOf(c) >= 0)
            {
                intoLakes++;
            }
            else
            {
                mouths++;
            }
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"streams: {sources.Count} sources, {streamLength / 1000:0.0} km of streams and rivers ({mouths} running to the sea, {intoLakes} into lakes), {valleyLength / 1000:0.0} km of dry valleys"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"lakes: {lakes.Length}, {lakes.Sum(l => l.Area) / 1e6:0.00} km²; largest {(lakes.Length > 0 ? lakes[0].Area / 1e4 : 0):0.0} ha, {(lakes.Length > 0 ? lakes[0].MaxDepth : 0):0.0} m deep; {lakes.Count(l => l.Area >= 10_000)} of a hectare or more"));

        // Scatter: a tile at the landing latitude.
        TileKey scatter = TileKey.At(ZoneFrame.Direction(45 * DMath.DegToRad, -170 * DMath.DegToRad), world.ScatterPlan.Level);
        watch.Restart();
        int items = 0;
        for (int i = 0; i < 4; i++)
        {
            items += world.ScatterFor(new TileKey(scatter.Face, scatter.Level, scatter.X + i, scatter.Y)).Count;
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"scatter: level {world.ScatterPlan.Level} ({TileKey.EdgeLength(world.ScatterPlan.Level, world.Radius):0} m), {watch.Elapsed.TotalMilliseconds / 4:0.0} ms and {items / 4} objects a tile"));
    }

    // ---------------------------------------------------------------- whole-world maps (Mollweide, equal area)

    private const double Sqrt2 = 1.4142135623730951;

    // Pixel → latitude and longitude (radians), or false outside the ellipse.
    private static bool MollweideInverse(double x, double y, int width, int height, double lon0, out double lat, out double lon)
    {
        double mx = ((x + 0.5) / width * 2 - 1) * 2 * Sqrt2, my = (1 - (y + 0.5) / height * 2) * Sqrt2;
        lat = lon = 0;
        if (mx * mx / 8 + my * my / 2 > 1)
        {
            return false;
        }
        double theta = Math.Asin(Math.Clamp(my / Sqrt2, -1, 1));
        lat = Math.Asin(Math.Clamp((2 * theta + Math.Sin(2 * theta)) / Math.PI, -1, 1));
        double c = Math.Cos(theta);
        lon = c > 1e-9 ? lon0 + Math.PI * mx / (2 * Sqrt2 * c) : lon0;
        return true;
    }

    private static (double X, double Y) MollweideForward(double lat, double lon, int width, int height, double lon0)
    {
        double theta = lat;
        for (int i = 0; i < 12; i++)
        {
            double f = 2 * theta + Math.Sin(2 * theta) - Math.PI * Math.Sin(lat);
            double d = 2 + 2 * Math.Cos(2 * theta);
            if (Math.Abs(d) < 1e-12)
            {
                break;
            }
            theta -= f / d;
        }
        double dl = lon - lon0;
        dl = Math.IEEERemainder(dl, 2 * Math.PI);
        double mx = 2 * Sqrt2 / Math.PI * dl * Math.Cos(theta), my = Sqrt2 * Math.Sin(theta);
        return ((mx / (2 * Sqrt2) + 1) / 2 * width - 0.5, (1 - my / Sqrt2) / 2 * height - 0.5);
    }

    private static (double Lat, double Lon) LatLon(Vector3D d) =>
        (Math.Asin(Math.Clamp(d.Y, -1, 1)), Math.Atan2(-d.Z, d.X));

    private static void Map(WalkingWorld world, string kind, string path, int width)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        int height = width / 2;
        var rgb = new byte[width * height * 3];
        double pixel = 2 * Math.PI * world.Radius / width * 0.8;
        Parallel.For(0, height, y =>
        {
            Span<double> w = stackalloc double[BiomeTable.Count];
            for (int x = 0; x < width; x++)
            {
                int k = (y * width + x) * 3;
                if (!MollweideInverse(x, y, width, height, 0, out double lat, out double lon))
                {
                    rgb[k] = rgb[k + 1] = rgb[k + 2] = 250;
                    continue;
                }
                Vector3D d = ZoneFrame.Direction(lat, lon);
                (byte r, byte g, byte b) = Shaded(world, d, kind, pixel, w);
                rgb[k] = r; rgb[k + 1] = g; rgb[k + 2] = b;
            }
        });
        Rivers(world, rgb, width, height, (lat, lon) => MollweideForward(lat, lon, width, height, 0), kind == "rivers" ? 1.0 : 0.6, width / 2048.0);
        Png.Write(path, width, height, rgb);
        Console.WriteLine($"{path}: {kind}, equal-area, {width} × {height}");
    }

    // A point's colour (by kind) lit by the ground's own normal, from the north-west.
    private static (byte, byte, byte) Shaded(WalkingWorld world, Vector3D d, string kind, double step, Span<double> biomes)
    {
        WalkingSample s = world.Fields(d);
        (double r, double g, double b) = kind switch
        {
            "relief" => Hypsometric(s.Height),
            "rivers" => s.Height < 0 ? (60, 78, 96) : (226, 222, 214),
            _ => BiomeColour(world, d, s, biomes),
        };
        if (s.Wetted && s.Height >= 0 || s.Height < 0 && kind != "rivers")
        {
            (r, g, b) = Water(s.Water - s.Height, kind == "rivers");
        }
        double light = Light(world, d, step);
        if (s.Height < 0)
        {
            light = 1 + (light - 1) * 0.25;
        }
        return (Byte(r * light), Byte(g * light), Byte(b * light));
    }

    private static double Light(WalkingWorld world, Vector3D d, double step)
    {
        Vector3D up = d;
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-12 ? east.Normalized : Vector3D.UnitX;
        Vector3D north = Vector3D.Cross(up, east);
        double r = world.Radius, a = step / r;
        double h0 = world.HeightAt(d);
        double he = world.HeightAt((d + east * a).Normalized), hn = world.HeightAt((d + north * a).Normalized);
        double dx = (he - h0) / step, dy = (hn - h0) / step;
        // Normal (−dx, −dy, 1) in east, north, up.
        return Relief(-dx, -dy);
    }

    // Cartographic hill shading: light from the north-west, the west and the north (half, a quarter, a quarter), 45° up,
    // over an ambient part, so slopes facing away read as shade rather than black.
    internal static double Relief(double nEast, double nNorth)
    {
        double len = Math.Sqrt(nEast * nEast + nNorth * nNorth + 1);
        double ex = nEast / len, ny = nNorth / len, up = 1 / len;
        const double S = 0.7071, D = 0.5;
        double nw = Math.Max(0, -ex * D + ny * D + up * S);
        double w = Math.Max(0, -ex * S + up * S);
        double n = Math.Max(0, ny * S + up * S);
        double lambert = 0.5 * nw + 0.25 * w + 0.25 * n;
        return Math.Clamp(0.42 + 0.78 * lambert / 0.7071, 0.4, 1.25);
    }

    private static (double, double, double) Water(double depth, bool plain)
    {
        if (plain)
        {
            return (70, 120, 190);
        }
        double t = Math.Clamp(depth / 40, 0, 1);
        return (70 - 45 * t, 140 - 70 * t, 190 - 50 * t);
    }

    private static (double, double, double) Hypsometric(double h)
    {
        ReadOnlySpan<double> stops = stackalloc double[] { -160, -40, -5, 0, 3, 15, 40, 80, 140, 220, 320, 450 };
        (double, double, double)[] colours =
        {
            (20, 40, 90), (40, 90, 150), (90, 150, 200), (120, 175, 210), (96, 150, 86), (130, 170, 96), (176, 186, 120),
            (196, 170, 120), (170, 130, 96), (150, 120, 105), (215, 212, 210), (255, 255, 255),
        };
        for (int i = 1; i < stops.Length; i++)
        {
            if (h <= stops[i])
            {
                double t = Math.Clamp((h - stops[i - 1]) / (stops[i] - stops[i - 1]), 0, 1);
                (double r0, double g0, double b0) = colours[i - 1];
                (double r1, double g1, double b1) = colours[i];
                return (r0 + (r1 - r0) * t, g0 + (g1 - g0) * t, b0 + (b1 - b0) * t);
            }
        }
        return colours[^1];
    }

    private static (double, double, double) BiomeColour(WalkingWorld world, Vector3D d, in WalkingSample s, Span<double> weights)
    {
        if (s.Height < 0)
        {
            return Water(-s.Height, false);
        }
        ClimateSample climate = world.Climate(d, s);
        world.BiomeWeights(s, climate, weights);
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < BiomeTable.Count; i++)
        {
            if (weights[i] <= 0)
            {
                continue;
            }
            (double cr, double cg, double cb) = Palette((Biome)i);
            r += weights[i] * cr;
            g += weights[i] * cg;
            b += weights[i] * cb;
        }
        // Woods darker, snow where it lies, bare rock on the steepest ground.
        double cover = world.ForestCover(d, s) * Math.Min(1, BiomeTable.Trees(weights) / 0.02) * climate.TreeLine;
        r *= 1 - 0.42 * cover;
        g *= 1 - 0.25 * cover;
        b *= 1 - 0.42 * cover;
        double snow = DMath.SmoothStep(-1.0, -5.0, climate.Temperature);
        r += (240 - r) * snow;
        g += (244 - g) * snow;
        b += (250 - b) * snow;
        return (r, g, b);
    }

    private static (double, double, double) Palette(Biome biome) => biome switch
    {
        Biome.Ocean => (40, 100, 160),
        Biome.Beach => (228, 214, 166),
        Biome.IceSheet => (240, 244, 250),
        Biome.Tundra => (150, 152, 122),
        Biome.BorealForest => (70, 112, 74),
        Biome.TemperateForest => (88, 142, 70),
        Biome.Grassland => (150, 180, 92),
        Biome.Shrubland => (172, 164, 102),
        Biome.Desert => (222, 196, 140),
        Biome.Savanna => (196, 182, 104),
        Biome.TropicalForest => (52, 124, 58),
        Biome.Alpine => (150, 142, 132),
        _ => (255, 0, 255),
    };

    private static byte Byte(double v) => (byte)Math.Clamp(v, 0, 255);

    // Streams and rivers drawn over a map, their width by catchment; dry valleys faintly on the rivers map.
    private static void Rivers(WalkingWorld world, byte[] rgb, int width, int height, Func<double, double, (double X, double Y)> project,
        double strength, double scale)
    {
        foreach (DrainageSegment s in world.Drainage)
        {
            if (!s.Stream && strength < 1)
            {
                continue;
            }
            (double latA, double lonA) = LatLon(s.A);
            (double latB, double lonB) = LatLon(s.B);
            (double xa, double ya) = project(latA, lonA);
            (double xb, double yb) = project(latB, lonB);
            if (Math.Abs(xa - xb) > width / 4.0)
            {
                continue; // across the map's seam
            }
            double radius = s.Stream ? Math.Clamp(0.5 + 0.9 * Math.Sqrt(s.Catchment / 1e6), 0.6, 2.4) * scale : 0.45 * scale;
            (double, double, double) colour = s.Stream ? (40, 95, 175) : (150, 140, 128);
            double alpha = s.Stream ? strength : 0.7;
            int steps = Math.Max(2, (int)(Math.Sqrt((xb - xa) * (xb - xa) + (yb - ya) * (yb - ya)) * 2));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                Disc(rgb, width, height, xa + (xb - xa) * t, ya + (yb - ya) * t, radius, colour, alpha);
            }
        }
    }

    private static void Disc(byte[] rgb, int width, int height, double cx, double cy, double radius, (double R, double G, double B) c, double alpha)
    {
        int x0 = (int)Math.Floor(cx - radius - 1), x1 = (int)Math.Ceiling(cx + radius + 1);
        int y0 = (int)Math.Floor(cy - radius - 1), y1 = (int)Math.Ceiling(cy + radius + 1);
        for (int y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
        {
            for (int x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
            {
                double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                double a = Math.Clamp(radius + 0.5 - dist, 0, 1) * alpha;
                if (a <= 0)
                {
                    continue;
                }
                int k = (y * width + x) * 3;
                rgb[k] = Byte(rgb[k] + (c.R - rgb[k]) * a);
                rgb[k + 1] = Byte(rgb[k + 1] + (c.G - rgb[k + 1]) * a);
                rgb[k + 2] = Byte(rgb[k + 2] + (c.B - rgb[k + 2]) * a);
            }
        }
    }

    // ---------------------------------------------------------------- globe

    private static void Globe(WalkingWorld world, string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        double[] longitudes = { -170, -80, 10, 100 };
        int width = size * 2, height = size * 2;
        var rgb = new byte[width * height * 3];
        Array.Fill(rgb, (byte)18);
        for (int view = 0; view < 4; view++)
        {
            int ox = (view % 2) * size, oy = (view / 2) * size;
            GlobeView(world, rgb, width, ox, oy, size, 25 * DMath.DegToRad, longitudes[view] * DMath.DegToRad);
        }
        Png.Write(path, width, height, rgb);
        Console.WriteLine($"{path}: four views of the globe, {width} × {height}");
    }

    private static void GlobeView(WalkingWorld world, byte[] rgb, int stride, int ox, int oy, int size, double lat0, double lon0)
    {
        Vector3D view = ZoneFrame.Direction(lat0, lon0);
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, view).Normalized;
        Vector3D north = Vector3D.Cross(view, east);
        double step = 2 * world.Radius / size;
        Parallel.For(0, size, y =>
        {
            Span<double> w = stackalloc double[BiomeTable.Count];
            for (int x = 0; x < size; x++)
            {
                double u = ((x + 0.5) / size * 2 - 1) * 1.04, v = (1 - (y + 0.5) / size * 2) * 1.04;
                double rr = u * u + v * v;
                if (rr >= 1)
                {
                    continue;
                }
                double z = Math.Sqrt(1 - rr);
                Vector3D d = (east * u + north * v + view * z).Normalized;
                (byte r, byte g, byte b) = Shaded(world, d, "biomes", step, w);
                // The ball's own shading: a little darker toward the limb.
                double limb = 0.55 + 0.45 * z;
                int k = ((oy + y) * stride + ox + x) * 3;
                rgb[k] = Byte(r * limb); rgb[k + 1] = Byte(g * limb); rgb[k + 2] = Byte(b * limb);
            }
        });
        foreach (DrainageSegment s in world.Drainage)
        {
            if (!s.Stream || Vector3D.Dot(s.A, view) < 0.05)
            {
                continue;
            }
            (double, double) P(Vector3D d) => (ox + ((Vector3D.Dot(d, east) / 1.04 + 1) / 2 * size) - 0.5, oy + ((1 - Vector3D.Dot(d, north) / 1.04) / 2 * size) - 0.5);
            (double xa, double ya) = P(s.A);
            (double xb, double yb) = P(s.B);
            int steps = 3;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                Disc(rgb, stride, size * 2, xa + (xb - xa) * t, ya + (yb - ya) * t, Math.Clamp(0.3 + 0.5 * Math.Sqrt(s.Catchment / 1e6), 0.35, 1.3),
                    (40, 95, 175), 0.8);
            }
        }
    }

    // ---------------------------------------------------------------- close-ups

    internal static void Zoom(WalkingWorld world, double latitude, double longitude, double metres, string path, int size, bool trees)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Vector3D centre = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        var frame = new ZoneFrame(centre * world.Radius);
        double step = metres / size;
        var heights = new double[size * size];
        var colour = new double[size * size * 3];
        var wet = new bool[size * size];
        Parallel.For(0, size, y =>
        {
            Span<double> w = stackalloc double[BiomeTable.Count];
            for (int x = 0; x < size; x++)
            {
                double lx = (x + 0.5 - size / 2.0) * step, lz = (y + 0.5 - size / 2.0) * step;
                Vector3D d = frame.ToBody(lx, 0, lz).Normalized;
                WalkingSample s = world.Fields(d);
                int i = y * size + x;
                heights[i] = s.Height;
                (double r, double g, double b) = BiomeColour(world, d, s, w);
                if (trees && s.Height >= 0)
                {
                    // Under the trees' dots, the ground: grass and litter; undo the woods' darkening of the maps.
                    double cover = world.ForestCover(d, s) * Math.Min(1, BiomeTable.Trees(w) / 0.02);
                    double lift = 1 / (1 - 0.3 * cover);
                    (r, g, b) = (r * lift, g * lift, b * lift);
                }
                if (s.Wetted || s.Height < 0)
                {
                    (r, g, b) = Water(s.Water - s.Height, false);
                    wet[i] = true;
                }
                colour[i * 3] = r; colour[i * 3 + 1] = g; colour[i * 3 + 2] = b;
            }
        });
        var rgb = new byte[size * size * 3];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                int xm = Math.Max(0, x - 1), xp = Math.Min(size - 1, x + 1), ym = Math.Max(0, y - 1), yp = Math.Min(size - 1, y + 1);
                double dx = (heights[y * size + xp] - heights[y * size + xm]) / ((xp - xm) * step);
                double dz = (heights[yp * size + x] - heights[ym * size + x]) / ((yp - ym) * step);
                // +x east, +z south: the normal's east part is −dx, its north part +dz.
                double light = Relief(-dx, dz);
                double slope = Math.Sqrt(dx * dx + dz * dz);
                double r = colour[i * 3], g = colour[i * 3 + 1], b = colour[i * 3 + 2];
                if (!wet[i] && slope > 1.0)
                {
                    // Cliffs: bare rock.
                    double rock = Math.Clamp((slope - 1.0) / 0.6, 0, 1);
                    (r, g, b) = (r + (128 - r) * rock, g + (122 - g) * rock, b + (116 - b) * rock);
                }
                if (wet[i])
                {
                    light = 1 + (light - 1) * 0.2;
                }
                rgb[i * 3] = Byte(r * light); rgb[i * 3 + 1] = Byte(g * light); rgb[i * 3 + 2] = Byte(b * light);
            }
        }
        int treeCount = 0;
        if (trees)
        {
            treeCount = DrawScatter(world, frame, rgb, size, step, metres);
        }
        ScaleBar(rgb, size, step);
        Png.Write(path, size, size, rgb);
        double min = heights.Min(), max = heights.Max();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{path}: {metres:0} m square at {latitude:0.0000}, {longitude:0.0000}; heights {min:0} to {max:0} m; {treeCount} trees ({treeCount / (metres * metres / 10_000):0} a hectare)"));
    }

    // The scatter's trees (dark green crowns), bushes and outcrops seen from above, at their places.
    private static int DrawScatter(WalkingWorld world, ZoneFrame frame, byte[] rgb, int size, double step, double metres)
    {
        var tiles = new List<TileKey>();
        TileKey.Around(frame.Up, metres * 0.75, world.Radius, world.ScatterPlan.Level, tiles);
        var all = new List<ScatterItem>[tiles.Count];
        Parallel.For(0, tiles.Count, i => all[i] = world.ScatterFor(tiles[i]).ToList());
        int trees = 0;
        foreach (List<ScatterItem> items in all)
        {
            foreach (ScatterItem item in items)
            {
                System.Numerics.Vector3 local = frame.ToLocal(item.Position(world.Radius));
                double x = local.X / step + size / 2.0 - 0.5, y = local.Z / step + size / 2.0 - 0.5;
                if (x < -4 || y < -4 || x > size + 4 || y > size + 4)
                {
                    continue;
                }
                switch (item.Kind)
                {
                    case ScatterKind.Fir or ScatterKind.Pine:
                        Disc(rgb, size, size, x, y, 2.4 * item.Scale / step, (34, 62, 40), 0.9);
                        trees++;
                        break;
                    case ScatterKind.Birch or ScatterKind.Broadleaf:
                        Disc(rgb, size, size, x, y, 3.0 * item.Scale / step, (52, 88, 42), 0.9);
                        trees++;
                        break;
                    case ScatterKind.StoneOutcrop or ScatterKind.MetalOutcrop or ScatterKind.SulphurOutcrop:
                        Disc(rgb, size, size, x, y, 1.6 / step, (120, 116, 110), 1);
                        break;
                    case ScatterKind.BerryRed or ScatterKind.BerryBlue:
                        Disc(rgb, size, size, x, y, 0.8 / step, item.Kind == ScatterKind.BerryRed ? (170, 50, 50) : (60, 70, 160), 0.9);
                        break;
                }
            }
        }
        return trees;
    }

    private static void ScaleBar(byte[] rgb, int size, double step)
    {
        int length = (int)Math.Round(100 / step);
        int x0 = 16, y0 = size - 18;
        for (int y = y0; y < y0 + 5; y++)
        {
            for (int x = x0; x < x0 + length && x < size; x++)
            {
                int k = (y * size + x) * 3;
                bool white = (x - x0) * 2 / Math.Max(1, length) % 2 == 0;
                rgb[k] = rgb[k + 1] = rgb[k + 2] = (byte)(white ? 250 : 20);
            }
        }
    }

    private static void Slopes(WalkingWorld world, double latitude, double longitude, double metres, string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Vector3D centre = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        var frame = new ZoneFrame(centre * world.Radius);
        double step = metres / size;
        var heights = new double[size * size];
        var water = new bool[size * size];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                Vector3D d = frame.ToBody((x + 0.5 - size / 2.0) * step, 0, (y + 0.5 - size / 2.0) * step).Normalized;
                WalkingSample s = world.Fields(d);
                heights[y * size + x] = s.Height;
                water[y * size + x] = s.Wetted || s.Height < 0;
            }
        });
        var rgb = new byte[size * size * 3];
        int[] counts = new int[5];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                int xm = Math.Max(0, x - 1), xp = Math.Min(size - 1, x + 1), ym = Math.Max(0, y - 1), yp = Math.Min(size - 1, y + 1);
                double dx = (heights[y * size + xp] - heights[y * size + xm]) / ((xp - xm) * step);
                double dz = (heights[yp * size + x] - heights[ym * size + x]) / ((yp - ym) * step);
                double degrees = Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI;
                (byte r, byte g, byte b) c;
                int cls;
                if (water[i])
                {
                    c = (90, 140, 200);
                    cls = -1;
                }
                else if (degrees < 8)
                {
                    c = (196, 220, 170); cls = 0; // flat: build anywhere
                }
                else if (degrees < 20)
                {
                    c = (236, 220, 140); cls = 1; // gentle
                }
                else if (degrees < 35)
                {
                    c = (232, 160, 90); cls = 2; // steep: scree
                }
                else if (degrees < 50)
                {
                    c = (200, 80, 60); cls = 3; // very steep
                }
                else
                {
                    c = (90, 30, 40); cls = 4; // cliff
                }
                if (cls >= 0)
                {
                    counts[cls]++;
                }
                rgb[i * 3] = c.r; rgb[i * 3 + 1] = c.g; rgb[i * 3 + 2] = c.b;
            }
        }
        ScaleBar(rgb, size, step);
        Png.Write(path, size, size, rgb);
        double total = counts.Sum();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{path}: slopes under 8° {counts[0] / total:P0}, 8–20° {counts[1] / total:P0}, 20–35° {counts[2] / total:P0}, 35–50° {counts[3] / total:P0}, cliffs over 50° {counts[4] / total:P0}"));
    }

    // Fields along a row of a close-up's pixels (x from one pixel to another, at row y), for finding faults.
    private static void Probe(WalkingWorld world, double latitude, double longitude, double metres, int size, double x0, double row, double x1)
    {
        Vector3D centre = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        var frame = new ZoneFrame(centre * world.Radius);
        double step = metres / size;
        bool vertical = x0 < 0; // a negative start probes a column: x = row, y from −x0 to x1
        if (vertical)
        {
            x0 = -x0;
        }
        for (double x = x0; x <= x1; x += Math.Max(1, (x1 - x0) / 40))
        {
            double px = vertical ? row : x, py = vertical ? x : row;
            Vector3D d = frame.ToBody((px + 0.5 - size / 2.0) * step, 0, (py + 0.5 - size / 2.0) * step).Normalized;
            WalkingSample s = world.Fields(d);
            var parts = world.Parts(d);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"base {parts.Base,7:0.00} ridge {parts.Ridge:0.000} massif {parts.Massif:0.000} centre {parts.Centrality:0.000} mtnfn {parts.Mountains,7:0.00} | {(vertical ? "y" : "x")} {x,5:0} h {s.Height,7:0.00} water {s.Water,7:0.00} wet {s.Wet:0.00} c {s.Continent:0.000} plains {s.Plains:0.00} hills {s.Hills:0.00} table {s.Tableland:0.00} mtn {s.Mountains:0.00} rough {s.Roughness:0.00} cell {world.DrainageGrid!.CellAt(d)} bucket {TileKey.At(d, 5)}"));
        }
    }

    // ---------------------------------------------------------------- places for the close-ups

    internal sealed record Spot(string Name, double Latitude, double Longitude, double Metres, string Why)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name,-10} {Latitude,9:0.0000} {Longitude,10:0.0000}  {Why}");
    }

    internal static List<Spot> Spots(WalkingWorld world)
    {
        Drainage grid = world.DrainageGrid!;
        int count = grid.CellCount;
        var fields = new WalkingSample[count];
        Parallel.For(0, count, i => fields[i] = world.Fields(grid.CellCentre(i)));
        (double, double) Ll(Vector3D d) => (Math.Asin(d.Y) * 180 / Math.PI, Math.Atan2(-d.Z, d.X) * 180 / Math.PI);
        double Temperate(Vector3D d) => 1 - Math.Clamp(Math.Abs(Math.Abs(Math.Asin(d.Y) * 180 / Math.PI) - 42) / 25, 0, 1);
        var spots = new List<Spot>();

        // The highest peak.
        int peak = Enumerable.Range(0, count).MaxBy(i => fields[i].Height);
        (double la, double lo) = Ll(grid.CellCentre(peak));
        spots.Add(new Spot("mountain", la, lo, 1000, string.Create(CultureInfo.InvariantCulture, $"the highest peak, {fields[peak].Height:0} m")));

        // A river in a valley: a stream with the most ground standing over it within 150 m, in the temperate belt.
        double Relief(int i)
        {
            Vector3D d = grid.CellCentre(i);
            Vector3D east = Vector3D.Cross(Vector3D.UnitY, d).Normalized, north = Vector3D.Cross(d, east);
            double highest = 0;
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4, r = 150 / world.Radius;
                highest = Math.Max(highest, world.HeightAt((d + east * (r * Math.Cos(a)) + north * (r * Math.Sin(a))).Normalized));
            }
            return highest - grid.StreamSurface(i);
        }
        int river = Enumerable.Range(0, count).Where(i => grid.IsStream(i) && fields[i].Height > 5 && grid.Catchment(i) > 300_000)
            .MaxBy(i => Math.Sqrt(grid.Catchment(i)) * Relief(i) * (0.3 + Temperate(grid.CellCentre(i))));
        (la, lo) = Ll(grid.CellCentre(river));
        spots.Add(new Spot("valley", la, lo, 800, string.Create(CultureInfo.InvariantCulture,
            $"a river draining {grid.Catchment(river) / 1e6:0.00} km², water at {grid.StreamSurface(river):0} m, {Relief(river):0} m of valley side")));

        // Sea cliffs: rocky shore cells next to the sea, standing highest.
        int cliff = Enumerable.Range(0, count).Where(i => fields[i].Height > 0 && fields[i].Rocky > 0.7 && NextToSea(grid, i))
            .MaxBy(i => fields[i].Height * (0.4 + Temperate(grid.CellCentre(i))));
        (la, lo) = Ll(grid.CellCentre(cliff));
        spots.Add(new Spot("coast", la, lo, 700, string.Create(CultureInfo.InvariantCulture, $"a rocky coast, {fields[cliff].Height:0} m above the sea")));

        // A sandy beach in the temperate belt.
        int beach = Enumerable.Range(0, count).Where(i => fields[i].Height > 0 && fields[i].Height < 3 && fields[i].Rocky < 0.15 && NextToSea(grid, i))
            .MaxBy(i => Temperate(grid.CellCentre(i)) - fields[i].Rocky);
        (la, lo) = Ll(grid.CellCentre(beach));
        spots.Add(new Spot("beach", la, lo, 700, "a sandy shore"));

        // Tableland cliffs: the steepest drop between neighbouring tableland cells.
        int mesa = Enumerable.Range(0, count).Where(i => fields[i].Tableland > 0.85 && !fields[i].Wetted)
            .MaxBy(i => Drop(grid, fields, i));
        (la, lo) = Ll(grid.CellCentre(mesa));
        spots.Add(new Spot("tableland", la, lo, 800, string.Create(CultureInfo.InvariantCulture, $"terraces and cliffs, a {Drop(grid, fields, mesa):0} m drop")));

        // The largest lake.
        if (world.Lakes.Count > 0)
        {
            WalkingLake lake = world.Lakes.MaxBy(l => l.Area);
            (la, lo) = Ll(lake.Centre);
            spots.Add(new Spot("lake", la, lo, 800, string.Create(CultureInfo.InvariantCulture,
                $"the largest lake, {lake.Area / 1e4:0.0} ha at {lake.Level:0} m, {lake.MaxDepth:0.0} m deep")));
        }

        // An open plain with woods at its edge, in the temperate belt.
        int plain = Enumerable.Range(0, count).Where(i => fields[i].Plains > 0.9 && !fields[i].Wetted && fields[i].Height > 2)
            .MaxBy(i => Temperate(grid.CellCentre(i)) + 0.2 * fields[i].Continent);
        (la, lo) = Ll(grid.CellCentre(plain));
        spots.Add(new Spot("plain", la, lo, 800, "open plains"));

        // Hills with woods and clearings.
        int hills = Enumerable.Range(0, count).Where(i => fields[i].Hills > 0.9 && !fields[i].Wetted)
            .MaxBy(i => Temperate(grid.CellCentre(i)) * 2 + fields[i].Height / 40);
        (la, lo) = Ll(grid.CellCentre(hills));
        spots.Add(new Spot("forest", la, lo, 600, string.Create(CultureInfo.InvariantCulture, $"wooded hills, {fields[hills].Height:0} m")));
        return spots;
    }

    private static bool NextToSea(Drainage grid, int cell)
    {
        Vector3D d = grid.CellCentre(cell);
        double step = TileKey.EdgeLength(grid.Level, 1);
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, d).Normalized, north = Vector3D.Cross(d, east);
        foreach ((double a, double b) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
        {
            if (grid.IsSea(grid.CellAt((d + east * (a * step) + north * (b * step)).Normalized)))
            {
                return true;
            }
        }
        return false;
    }

    private static double Drop(Drainage grid, WalkingSample[] fields, int cell)
    {
        Vector3D d = grid.CellCentre(cell);
        double step = TileKey.EdgeLength(grid.Level, 1);
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, d).Normalized, north = Vector3D.Cross(d, east);
        double drop = 0;
        foreach ((double a, double b) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
        {
            int j = grid.CellAt((d + east * (a * step) + north * (b * step)).Normalized);
            drop = Math.Max(drop, fields[cell].Height - fields[j].Height);
        }
        return drop;
    }

    // ---------------------------------------------------------------- the stand-in beside it

    private static void Compare(WalkingWorld world, string path, int width)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var galaxy = new Galaxy.Galaxy(null);
        var found = FirstWorld.Find(galaxy)!.Value;
        ulong zoneSeed = Hash64.Of(found.System.Address.Value, found.Planet.Path);
        Vector3D landing = ZoneFrame.Direction(FirstWorld.LandingLatitude * DMath.DegToRad, FirstWorld.LandingLongitude * DMath.DegToRad);
        var standIn = new StandInWorld(world.Radius, zoneSeed, landing);
        int height = width / 2;
        var rgb = new byte[width * height * 2 * 3];
        double pixel = 2 * Math.PI * world.Radius / width * 0.8;
        Parallel.For(0, height, y =>
        {
            Span<double> w = stackalloc double[BiomeTable.Count];
            for (int x = 0; x < width; x++)
            {
                int top = (y * width + x) * 3, bottom = ((y + height) * width + x) * 3;
                if (!MollweideInverse(x, y, width, height, 0, out double lat, out double lon))
                {
                    rgb[top] = rgb[top + 1] = rgb[top + 2] = 250;
                    rgb[bottom] = rgb[bottom + 1] = rgb[bottom + 2] = 250;
                    continue;
                }
                Vector3D d = ZoneFrame.Direction(lat, lon);
                double hs = standIn.HeightAt(d) - (standIn.SeaRadius - standIn.Radius);
                (double r, double g, double b) = hs < 0 ? Water(-hs, false) : Palette(standIn.BiomeAt(d));
                double light = StandInLight(standIn, d, pixel);
                rgb[top] = Byte(r * light); rgb[top + 1] = Byte(g * light); rgb[top + 2] = Byte(b * light);
                (byte r2, byte g2, byte b2) = Shaded(world, d, "biomes", pixel, w);
                rgb[bottom] = r2; rgb[bottom + 1] = g2; rgb[bottom + 2] = b2;
            }
        });
        Png.Write(path, width, height * 2, rgb);
        Console.WriteLine($"{path}: today's stand-in (top) and the walking-scale ground (bottom), {width} × {height * 2}");
    }

    private static double StandInLight(StandInWorld world, Vector3D d, double step)
    {
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, d);
        east = east.LengthSquared > 1e-12 ? east.Normalized : Vector3D.UnitX;
        Vector3D north = Vector3D.Cross(d, east);
        double a = step / world.Radius;
        double h0 = world.HeightAt(d), he = world.HeightAt((d + east * a).Normalized), hn = world.HeightAt((d + north * a).Normalized);
        return Relief(-(he - h0) / step, -(hn - h0) / step);
    }

    // ---------------------------------------------------------------- the whole set for the design document

    private static void All(WalkingWorld world, string folder)
    {
        Map(world, "relief", Path.Combine(folder, "world_relief.png"), 2048);
        Map(world, "biomes", Path.Combine(folder, "world_biomes.png"), 2048);
        Map(world, "rivers", Path.Combine(folder, "world_rivers.png"), 2048);
        Globe(world, Path.Combine(folder, "world_globe.png"), 700);
        Compare(world, Path.Combine(folder, "compare_standin.png"), 1536);
        Closeups(world, folder);
    }

    private static void Closeups(WalkingWorld world, string folder)
    {
        foreach (Spot spot in Spots(world))
        {
            Console.WriteLine(spot);
            Zoom(world, spot.Latitude, spot.Longitude, spot.Metres, Path.Combine(folder, $"zoom_{spot.Name}.png"), 800, trees: true);
            if (spot.Name is "tableland" or "mountain")
            {
                Slopes(world, spot.Latitude, spot.Longitude, spot.Metres, Path.Combine(folder, $"slopes_{spot.Name}.png"), 600);
            }
        }
    }
}
