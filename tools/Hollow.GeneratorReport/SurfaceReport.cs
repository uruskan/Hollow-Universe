using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.GeneratorReport;

/// <summary>
/// The first world's surface (GROUND.md §3, §4.6): maps, speed, the landing-site search and the times of day used for
/// the look-dev screenshots. <c>dotnet run -c Release --project tools/Hollow.GeneratorReport -- surface map|zoom|bench|site|times</c>
/// </summary>
public static class SurfaceReport
{
    public static int Run(string[] args)
    {
        var galaxy = new Galaxy.Galaxy(Program.LoadCatalogue());
        var found = FirstWorld.Find(galaxy) ?? throw new InvalidOperationException("The first world is gone.");
        var watch = Stopwatch.StartNew();
        PlanetSurface surface = PlanetSurface.For(galaxy, found.System, found.Planet);
        Console.WriteLine($"Surface of {found.Planet.Name}: built in {watch.ElapsedMilliseconds} ms, ice below {surface.IceTemperature:0.0} °C");
        string command = args.Length > 1 ? args[1] : "map";
        string Arg(int i, string fallback) => args.Length > i ? args[i] : fallback;
        double Num(int i, double fallback) => args.Length > i ? double.Parse(args[i], CultureInfo.InvariantCulture) : fallback;
        switch (command)
        {
            case "map":
            case "relief":
                Map(surface, Arg(2, Path.Combine(Path.GetTempPath(), "first-world.png")), (int)Num(3, 2048), command == "relief");
                return 0;
            case "zoom":
                Zoom(surface, Num(2, 0), Num(3, 0), Num(4, 200), Arg(5, Path.Combine(Path.GetTempPath(), "first-world-zoom.png")), (int)Num(6, 768));
                return 0;
            case "stats":
                Stats(surface);
                return 0;
            case "bench":
                Bench(surface);
                return 0;
            case "site":
                Site(surface, Num(2, 0));
                return 0;
            case "spot":
                Spot(surface, Arg(2, "forest"), Num(3, FirstWorld.LandingLatitude), Num(4, FirstWorld.LandingLongitude), Num(5, 4000));
                return 0;
            case "times":
                Times(found.System, found.Planet, Num(2, FirstWorld.LandingLatitude), Num(3, FirstWorld.LandingLongitude), Arg(4, "2026-10-12T00:00:00Z"));
                return 0;
            default:
                Console.Error.WriteLine("usage: surface map [png] [width] | zoom lat lon km [png] [size] | bench | site | times [lat lon start]");
                return 2;
        }
    }

    // ---------------------------------------------------------------- maps

    private static void Map(PlanetSurface surface, string path, int width, bool relief)
    {
        int height = width / 2;
        var heights = new double[width * height];
        var colours = new byte[width * height * 3];
        long ocean = 0, ice = 0;
        double totalArea = 0;
        var lockObject = new object();
        Parallel.For(0, height, y =>
        {
            double lat = (0.5 - (y + 0.5) / height) * Math.PI;
            double localArea = Math.Cos(lat);
            long o = 0, c = 0;
            Span<double> biomes = stackalloc double[BiomeTable.Count];
            for (int x = 0; x < width; x++)
            {
                double lon = ((x + 0.5) / width - 0.5) * 2 * Math.PI;
                Vector3D d = ZoneFrame.Direction(lat, lon);
                SurfaceSample s = surface.Sample(d);
                heights[y * width + x] = s.Height;
                ClimateSample climate = surface.Climate(d, s);
                surface.BiomeWeights(d, s, climate, biomes);
                Biome biome = BiomeTable.Dominant(biomes);
                (byte r, byte g, byte b) = relief ? Hypsometric(s.Height) : Colour(biome, s.Height);
                int k = (y * width + x) * 3;
                colours[k] = r; colours[k + 1] = g; colours[k + 2] = b;
                if (s.Height < 0) o++;
                if (biome == Biome.IceSheet || (s.Height < 0 && climate.Temperature < surface.IceTemperature)) c++;
            }
            lock (lockObject)
            {
                ocean += (long)(o * localArea * 1000);
                ice += (long)(c * localArea * 1000);
                totalArea += width * localArea * 1000;
            }
        });
        Shade(heights, colours, width, height, 2 * Math.PI * surface.Radius / width);
        Png.Write(path, width, height, colours);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{path}: ocean {ocean / totalArea:P1} (state {surface.State.OceanCover:P1}), ice {ice / totalArea:P1} (state {surface.State.IceCover:P1})"));
    }

    private static void Zoom(PlanetSurface surface, double latitude, double longitude, double km, string path, int size)
    {
        ZoneFrame frame = ZoneFrame.At(surface, latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        var heights = new double[size * size];
        var colours = new byte[size * size * 3];
        double step = km * 1000 / size;
        Parallel.For(0, size, y =>
        {
            Span<double> biomes = stackalloc double[BiomeTable.Count];
            for (int x = 0; x < size; x++)
            {
                double lx = (x + 0.5 - size / 2.0) * step, lz = (y + 0.5 - size / 2.0) * step;
                Vector3D d = frame.ToBody(lx, 0, lz).Normalized;
                SurfaceSample s = surface.Sample(d);
                heights[y * size + x] = s.Height;
                ClimateSample climate = surface.Climate(d, s);
                surface.BiomeWeights(d, s, climate, biomes);
                Biome biome = BiomeTable.Dominant(biomes);
                (byte r, byte g, byte b) = Colour(biome, s.Height);
                if (s.Height >= 0)
                {
                    VegetationSample v = surface.Vegetation(d, s, climate, biomes, 0);
                    double forest = Math.Clamp(v.Trees / 0.03, 0, 1);
                    r = (byte)(r * (1 - 0.45 * forest));
                    g = (byte)(g * (1 - 0.3 * forest));
                    b = (byte)(b * (1 - 0.45 * forest));
                }
                int k = (y * size + x) * 3;
                colours[k] = r; colours[k + 1] = g; colours[k + 2] = b;
            }
        });
        Shade(heights, colours, size, size, step);
        // Mark the centre.
        for (int i = -4; i <= 4; i++)
        {
            foreach ((int x, int y) in new[] { (size / 2 + i, size / 2), (size / 2, size / 2 + i) })
            {
                int k = (y * size + x) * 3;
                colours[k] = 255; colours[k + 1] = 40; colours[k + 2] = 40;
            }
        }
        Png.Write(path, size, size, colours);
        double min = double.MaxValue, max = double.MinValue;
        foreach (double h in heights)
        {
            min = Math.Min(min, h);
            max = Math.Max(max, h);
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{path}: {km} km square, heights {min:0} to {max:0} m, centre {heights[size / 2 * size + size / 2]:0.0} m"));
    }

    private static (byte, byte, byte) Hypsometric(double h)
    {
        ReadOnlySpan<double> stops = stackalloc double[] { -6000, -3000, -200, 0, 1, 200, 600, 1200, 2200, 3500, 5500 };
        (byte, byte, byte)[] colours =
        {
            (8, 20, 60), (20, 50, 110), (60, 120, 180), (110, 170, 210), (70, 130, 70), (120, 160, 80), (190, 190, 110),
            (180, 140, 90), (150, 110, 80), (200, 200, 200), (255, 255, 255),
        };
        for (int i = 1; i < stops.Length; i++)
        {
            if (h <= stops[i])
            {
                double t = Math.Clamp((h - stops[i - 1]) / (stops[i] - stops[i - 1]), 0, 1);
                (byte r0, byte g0, byte b0) = colours[i - 1];
                (byte r1, byte g1, byte b1) = colours[i];
                return ((byte)(r0 + (r1 - r0) * t), (byte)(g0 + (g1 - g0) * t), (byte)(b0 + (b1 - b0) * t));
            }
        }
        return colours[^1];
    }

    private static (byte, byte, byte) Colour(Biome biome, double h)
    {
        if (biome == Biome.Ocean)
        {
            double t = Math.Clamp(-h / 5000, 0, 1);
            return ((byte)(40 - 30 * t), (byte)(110 - 80 * t), (byte)(170 - 90 * t));
        }
        return biome switch
        {
            Biome.Beach => (226, 210, 160),
            Biome.IceSheet => (240, 244, 250),
            Biome.Tundra => (150, 150, 120),
            Biome.BorealForest => (52, 92, 62),
            Biome.TemperateForest => (64, 122, 56),
            Biome.Grassland => (134, 170, 82),
            Biome.Shrubland => (168, 160, 96),
            Biome.Desert => (220, 190, 130),
            Biome.Savanna => (190, 176, 96),
            Biome.TropicalForest => (34, 110, 44),
            Biome.Alpine => (140, 130, 122),
            _ => (255, 0, 255),
        };
    }

    // Hill shading from the north-west, by the height differences of neighbouring pixels.
    private static void Shade(double[] heights, byte[] colours, int width, int height, double spacing)
    {
        Shade(heights, colours, width, height, spacing, Math.Clamp(Math.Sqrt(spacing / 4), 2.5, 40));
    }

    private static void Shade(double[] heights, byte[] colours, int width, int height, double spacing, double exaggeration)
    {
        var shaded = (byte[])colours.Clone();
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int i = y * width + x;
                double dx = (heights[i + 1] - heights[i - 1]) / (2 * spacing);
                double dy = (heights[i + width] - heights[i - width]) / (2 * spacing);
                double light = Math.Clamp(1 - exaggeration * (dx + dy), 0.4, 1.6);
                if (heights[i] < 0)
                {
                    light = 1 + (light - 1) * 0.3;
                }
                for (int c = 0; c < 3; c++)
                {
                    shaded[i * 3 + c] = (byte)Math.Clamp(colours[i * 3 + c] * light, 0, 255);
                }
            }
        }
        Array.Copy(shaded, colours, colours.Length);
    }

    private static void Stats(PlanetSurface surface)
    {
        Vector3D[] points = PlanetSurface.FibonacciSphere(40_000);
        var h = new List<double>();
        var c = new List<double>();
        var crust = new List<double>();
        var m = new List<double>();
        foreach (Vector3D d in points)
        {
            SurfaceSample s = surface.Sample(d);
            if (s.Height < 0) continue;
            h.Add(s.Height);
            c.Add(s.Continent);
            crust.Add(PlanetSurface.CrustCurve.Evaluate(s.Continent));
            m.Add(s.Mountain);
        }
        void Print(string name, List<double> v)
        {
            v.Sort();
            string q(double f) => v[Math.Clamp((int)(f * v.Count), 0, v.Count - 1)].ToString("0.###", CultureInfo.InvariantCulture);
            Console.WriteLine($"{name,-8} p5 {q(0.05)}  p25 {q(0.25)}  p50 {q(0.5)}  p75 {q(0.75)}  p95 {q(0.95)}  p99 {q(0.99)}  max {q(1)}");
        }
        Console.WriteLine($"{h.Count} land samples of {points.Length}");
        Print("height", h);
        Print("cont", c);
        Print("crust", crust);
        Print("mountain", m);
    }

    // ---------------------------------------------------------------- speed

    private static void Bench(PlanetSurface surface)
    {
        var rng = new Rng(5);
        const int N = 200_000;
        var directions = new Vector3D[N];
        for (int i = 0; i < N; i++)
        {
            directions[i] = new Vector3D(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1)).Normalized;
        }
        double sink = 0;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
        {
            sink += surface.HeightAt(directions[i]);
        }
        double perHeight = watch.Elapsed.TotalMilliseconds * 1000 / N;
        var key = TileKey.At(ZoneFrame.Direction(0.7, 0.3), 14);
        watch.Restart();
        int tiles = 0;
        for (int i = 0; i < 40; i++)
        {
            TileGrid grid = TileMesh.Build(surface, new TileKey(key.Face, key.Level, key.X + i, key.Y));
            sink += grid.MaxHeight;
            tiles++;
        }
        double perTile = watch.Elapsed.TotalMilliseconds / tiles;
        int vertices = (PlanetSurface.TileCells + 1) * (PlanetSurface.TileCells + 1);
        watch.Restart();
        int items = 0;
        var scatterKey = TileKey.At(ZoneFrame.Direction(0.7, 0.3), Scatter.Level);
        for (int i = 0; i < 20; i++)
        {
            items += Scatter.ForTile(surface, new TileKey(scatterKey.Face, scatterKey.Level, scatterKey.X + i, scatterKey.Y)).Count;
        }
        double perScatter = watch.Elapsed.TotalMilliseconds / 20;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"height {perHeight:0.00} µs per point; tile ({vertices} vertices) {perTile:0.00} ms = {perTile * 1000 / vertices:0.00} µs per vertex; scatter tile {perScatter:0.00} ms ({items / 20} items) [{sink:0}]"));
    }

    // ---------------------------------------------------------------- landing site

    /// <summary>
    /// Scores points on temperate sea coasts: open sea (deep water that reaches far out) on one side, forest and
    /// meadow around, hills near and mountains in view (within about 30 km), gentle ground at the spot itself.
    /// </summary>
    private static void Site(PlanetSurface surface, double minimumMountain)
    {
        Vector3D[] candidates = PlanetSurface.FibonacciSphere(1_500_000);
        var results = new List<(double Score, double Lat, double Lon, string Note)>();
        var lockObject = new object();
        double needMountain = minimumMountain > 0 ? minimumMountain : 1000;
        Parallel.For(0, candidates.Length, i =>
        {
            Vector3D d = candidates[i];
            double lat = DMath.Asin(d.Y) * DMath.RadToDeg;
            if (Math.Abs(lat) < 25 || Math.Abs(lat) > 55)
            {
                return;
            }
            SurfaceSample s = surface.Sample(d);
            if (s.Height < 3 || s.Height > 30)
            {
                return;
            }
            Span<double> biomes = stackalloc double[BiomeTable.Count];
            ClimateSample climate = surface.Climate(d, s);
            if (climate.Temperature < 8 || climate.Temperature > 17)
            {
                return;
            }
            surface.BiomeWeights(d, s, climate, biomes);
            double temperate = biomes[(int)Biome.TemperateForest] + biomes[(int)Biome.Grassland] + biomes[(int)Biome.Beach];
            if (temperate < 0.5)
            {
                return;
            }
            var frame = new ZoneFrame(d * surface.Radius);
            Vector3D At(double radius, double angle) =>
                frame.ToBody(radius * DMath.Cos(angle), 0, radius * DMath.Sin(angle)).Normalized;
            // Open sea: deep water 3 km out that is still sea 12 and 25 km out in the same direction.
            int openSea = 0, sea2 = 0;
            for (int a = 0; a < 24; a++)
            {
                double angle = a * Math.PI / 12;
                if (surface.HeightAt(At(2000, angle)) < -2) sea2++;
                if (surface.HeightAt(At(4000, angle)) < -20 && surface.HeightAt(At(12000, angle)) < -60
                    && surface.HeightAt(At(25000, angle)) < -60)
                {
                    openSea++;
                }
            }
            if (openSea < 4 || sea2 < 4 || sea2 > 14)
            {
                return;
            }
            // Forest and meadow around the spot.
            int forest = 0, open = 0, land = 0;
            foreach (double radius in new[] { 200.0, 500, 900, 1400 })
            {
                for (int a = 0; a < 12; a++)
                {
                    Vector3D p = At(radius, a * Math.PI / 6);
                    SurfaceSample ps = surface.Sample(p);
                    if (ps.Height < 1) continue;
                    land++;
                    ClimateSample pc = surface.Climate(p, ps);
                    surface.BiomeWeights(p, ps, pc, biomes);
                    VegetationSample v = surface.Vegetation(p, ps, pc, biomes, 0);
                    if (v.Trees > 0.012) forest++;
                    else if (v.Grass > 0.4) open++;
                }
            }
            if (forest < 5 || open < 4)
            {
                return;
            }
            double hills = 0, mountains = 0, mountainDistance = 1e9;
            foreach (double radius in new[] { 2500.0, 5000, 8000, 12000, 18000, 25000, 32000 })
            {
                for (int a = 0; a < 24; a++)
                {
                    double h = surface.HeightAt(At(radius, a * Math.PI / 12));
                    if (radius <= 5000) hills = Math.Max(hills, h);
                    mountains = Math.Max(mountains, h);
                    if (h > needMountain && radius < mountainDistance) mountainDistance = radius;
                }
            }
            if (mountains < needMountain || hills < 60)
            {
                return;
            }
            double score = Math.Min(mountains, 3000) / 1000 + Math.Min(hills, 400) / 250 + 0.08 * Math.Min(forest, 20)
                + 0.08 * Math.Min(open, 15) + 0.05 * openSea - mountainDistance / 25000 - Math.Abs(climate.Temperature - 12.5) * 0.06;
            lock (lockObject)
            {
                results.Add((score, lat, DMath.Atan2(-d.Z, d.X) * DMath.RadToDeg,
                    string.Create(CultureInfo.InvariantCulture, $"h {s.Height:0.0} m, {climate.Temperature:0.0} °C, moisture {climate.Moisture:0.00}, open sea {openSea}/24, sea at 2 km {sea2}/24, forest {forest}/{land}, open {open}, hills {hills:0} m, mountains {mountains:0} m at {mountainDistance / 1000:0} km")));
            }
        });
        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        Console.WriteLine($"{results.Count} candidate sites");
        for (int i = 0; i < Math.Min(20, results.Count); i++)
        {
            var r = results[i];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{r.Score:0.00}  lat {r.Lat:0.0000} lon {r.Lon:0.0000}  {r.Note}"));
        }
    }

    // ---------------------------------------------------------------- look-dev spots

    /// <summary>
    /// The best spot of a kind within a radius of a point: "forest" (dense trees), "meadow" (open grass with trees
    /// near), "beach" (sand at the waterline), "mountain" (a view toward high ground). Prints lat, lon and the yaw
    /// to look along.
    /// </summary>
    private static void Spot(PlanetSurface surface, string kind, double latitude, double longitude, double radius)
    {
        ZoneFrame frame = ZoneFrame.At(surface, latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        double bestScore = double.MinValue, bestX = 0, bestZ = 0, bestYaw = 0;
        string note = "";
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        for (double z = -radius; z <= radius; z += 40)
        {
            for (double x = -radius; x <= radius; x += 40)
            {
                if (x * x + z * z > radius * radius)
                {
                    continue;
                }
                Vector3D d = frame.ToBody(x, 0, z).Normalized;
                SurfaceSample s = surface.Sample(d);
                if (s.Height < 0.5)
                {
                    if (kind != "beach")
                    {
                        continue;
                    }
                }
                ClimateSample c = surface.Climate(d, s);
                surface.BiomeWeights(d, s, c, biomes);
                VegetationSample v = surface.Vegetation(d, s, c, biomes, 0);
                double score;
                double yaw = 0;
                string n = "";
                switch (kind)
                {
                    case "forest":
                        score = v.Trees * 1000 - Math.Abs(s.Height - 40) * 0.01;
                        n = $"trees {v.Trees:0.000}/m², height {s.Height:0}";
                        break;
                    case "meadow":
                    {
                        // Open grass here, trees around within 150 m.
                        double around = 0;
                        for (int a = 0; a < 8; a++)
                        {
                            Vector3D p = frame.ToBody(x + 120 * Math.Cos(a * Math.PI / 4), 0, z + 120 * Math.Sin(a * Math.PI / 4)).Normalized;
                            SurfaceSample ps = surface.Sample(p);
                            ClimateSample pc = surface.Climate(p, ps);
                            surface.BiomeWeights(p, ps, pc, biomes);
                            around += surface.Vegetation(p, ps, pc, biomes, 0).Trees;
                        }
                        surface.BiomeWeights(d, s, c, biomes);
                        score = v.Grass * 2 - v.Trees * 80 + Math.Min(around, 0.12) * 10 - Math.Max(0, s.Height - 80) * 0.01;
                        n = $"grass {v.Grass:0.00}, trees here {v.Trees:0.000}, around {around:0.000}";
                        break;
                    }
                    case "beach":
                    {
                        score = s.Height is > 0.3 and < 2.5 ? biomes[(int)Biome.Beach] * 2 + s.Beach : -1;
                        // Look toward the sea: the direction where it is deepest 300 m out.
                        double deepest = 1e9;
                        for (int a = 0; a < 16; a++)
                        {
                            double ang = a * Math.PI / 8;
                            double h = surface.HeightAt(frame.ToBody(x + 300 * Math.Sin(ang), 0, z - 300 * Math.Cos(ang)).Normalized);
                            if (h < deepest)
                            {
                                deepest = h;
                                yaw = ang * 180 / Math.PI;
                            }
                        }
                        if (deepest > -2)
                        {
                            score = -1;
                        }
                        n = $"beach {biomes[(int)Biome.Beach]:0.00}, sand {s.Beach:0.00}, height {s.Height:0.0}";
                        break;
                    }
                    default:
                    {
                        // A view: the highest ground seen between 3 and 25 km, from open ground.
                        double best = 0;
                        for (int a = 0; a < 24; a++)
                        {
                            double ang = a * Math.PI / 12;
                            for (double r = 3000; r <= 25000; r += 2000)
                            {
                                double h = surface.HeightAt(frame.ToBody(x + r * Math.Sin(ang), 0, z - r * Math.Cos(ang)).Normalized);
                                double rise = (h - s.Height) / r;
                                if (rise > best)
                                {
                                    best = rise;
                                    yaw = ang * 180 / Math.PI;
                                }
                            }
                        }
                        score = best * 100 - v.Trees * 50 + Math.Min(s.Height, 300) * 0.002;
                        n = $"rise {best:0.000}, trees {v.Trees:0.000}, height {s.Height:0}";
                        break;
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestZ = z;
                    bestYaw = yaw;
                    note = n;
                }
            }
        }
        Vector3D spot = frame.ToBody(bestX, 0, bestZ).Normalized;
        double lat = DMath.Asin(spot.Y) * DMath.RadToDeg, lon = DMath.Atan2(-spot.Z, spot.X) * DMath.RadToDeg;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{kind}: --lat {lat:0.00000} --lon {lon:0.00000} --yaw {bestYaw:0}  ({bestX:0} m east, {-bestZ:0} m north; {note})"));
    }

    // ---------------------------------------------------------------- times of day

    private static void Times(StarSystem system, Body planet, double latitude, double longitude, string start)
    {
        var sky = new SkyEphemeris(system, planet);
        int sun = sky.Sun.Index;
        Vector3D site = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        Body? moon = null;
        foreach (Body b in system.Bodies)
        {
            if (b.Parent == planet.Index && b.Kind == BodyKind.Moon)
            {
                moon = b;
            }
        }
        double t0 = UniverseTime.SecondsSinceJ2000(DateTime.Parse(start, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        Vector3D observer = site * planet.Radius;
        Console.WriteLine($"Site {latitude:0.000}, {longitude:0.000}; sun {sky.Sun.Name}; moon {moon?.Name ?? "none"}");
        for (double t = t0; t < t0 + 3 * 86400; t += 1800)
        {
            double hours = sky.LocalSolarHours(site, sun, t);
            double sunElevation = sky.Elevation(sun, observer, t) * DMath.RadToDeg;
            string moonText = "";
            if (moon != null)
            {
                double moonElevation = sky.Elevation(moon.Index, observer, t) * DMath.RadToDeg;
                Vector3D m = sky.Position(moon.Index, t) - observer, s = sky.Position(sun, t) - observer;
                double phase = 0.5 * (1 - Vector3D.Dot(m.Normalized, s.Normalized)); // 1 = full
                moonText = string.Create(CultureInfo.InvariantCulture, $"  moon {moonElevation:0.0}° lit {phase:P0}");
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{UniverseTime.ToUtc(t):yyyy-MM-ddTHH:mm:ssZ}  local {hours:00.00} h  sun {sunElevation:0.0}°{moonText}"));
        }
    }
}
