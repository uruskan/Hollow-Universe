using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Surface;

namespace Hollow.GeneratorReport;

/// <summary>Six sequential offline geography candidates; never selects a production world or opens a save.</summary>
internal static class EarthCandidatesReport
{
    private sealed record Candidate(int Variant, double TotalKm2, double DryKm2, double GentleKm2, double OpenGentleKm2,
        double ForestShare, double MountainShare, int SignificantBiomes, double HeightP10, double HeightP90,
        double MaximumHeight, double ConnectedDryShare, Dictionary<string, double> BiomeShares)
    {
        public bool BroadVariedLand => DryKm2 / TotalKm2 >= 0.50 && ConnectedDryShare >= 0.90
            && ForestShare >= 0.10 && MountainShare >= 0.05 && SignificantBiomes >= 4;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("earth candidates requires a new output folder.");
        string folder = Path.GetFullPath(args[2]);
        int samples = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 6000;
        int width = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 512;
        if (samples is < 2000 or > 16000 || width is < 256 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(args), "Samples: 2000..16000; map width: 256..1024.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new ArgumentException("Use a new/empty folder so earlier comparison evidence is preserved.");
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOLLOW_WALKING")))
            throw new ArgumentException("Clear HOLLOW_WALKING before comparing the recorded Earth profile.");
        Directory.CreateDirectory(folder);
        var results = new List<Candidate>();
        Console.WriteLine("Six variants, including current geography 0; identical Earth profile, independent layout seeds. No production selection/save changes.");
        Console.WriteLine("variant dry-km2 gentle-km2 open-gentle-km2 forest% mountains% biomes connected% relief-p10..p90-m");
        for (int variant = 0; variant < 6; variant++)
        {
            Candidate result = Measure(variant, samples);
            results.Add(result);
            File.WriteAllText(Path.Combine(folder, $"candidate-{variant}.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{variant,7} {result.DryKm2,7:0.00} {result.GentleKm2,10:0.00} {result.OpenGentleKm2,15:0.00} {100 * result.ForestShare,7:0.0} {100 * result.MountainShare,10:0.0} {result.SignificantBiomes,6} {100 * result.ConnectedDryShare,10:0.0} {result.HeightP10:0.0}..{result.HeightP90:0.0}"));
            // Candidate objects are short-lived; avoid retaining several drainage/scatter caches between runs.
            GC.Collect();
        }
        Candidate[] ranked = results.Where(c => c.Variant != 0).OrderByDescending(c => c.BroadVariedLand)
            .ThenByDescending(c => c.OpenGentleKm2).ThenByDescending(c => c.GentleKm2).ToArray();
        int[] review = ranked.Take(3).Select(c => c.Variant).Prepend(0).ToArray();
        var report = new
        {
            productionSelectionChanged = false, productionVariant = WalkingParameters.Earth.LayoutVariant,
            productionTerrainRevision = WalkingParameters.Earth.TerrainRevision,
            surfaceModule = typeof(WalkingWorld).Assembly.ManifestModule.ModuleVersionId,
            profile = WalkingParameters.Earth, samples, connectivityLevel = 6, mapWidth = width,
            method = "Equal-area Fibonacci samples. Gentle: dry 16m cardinal footprint, <=10-degree central grade, <=2.8m local height range, all four neighbours dry. Open: tree density <60/ha and no monument within 12m. These are terrain suitability proxies, not placement/claim/collision acceptance. Connectivity uses area-weighted ~30m cube cells and edge adjacency; channels below that resolution may be missed.",
            reviewOrder = ranked.Select(c => c.Variant).ToArray(),
            ranking = "Prefer >=50% dry sphere, >=90% connected dry land, >=10% forest, >=5% mountain relief and >=4 biomes each covering 3% of dry land; then larger open gentle area. Ranking is for map review, not final selection.",
            mapVariants = review, candidates = results,
        };
        File.WriteAllText(Path.Combine(folder, "comparison.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        foreach (int variant in review)
        {
            WriteMaps(variant, folder, width);
            GC.Collect();
        }
        Console.WriteLine($"Review order: {string.Join(", ", ranked.Select(c => c.Variant))}. Metrics and maps: {folder}");
        Console.WriteLine("Production Earth and terrain revision are unchanged; a selected variant needs explicit save compatibility and visual review.");
        return 0;
    }

    private static WalkingWorld Build(int variant)
    {
        var (system, body) = StartingWorld.Find();
        return WalkingWorld.For(system, body, GameScale.PlanetRadius(body), WalkingParameters.Earth with { LayoutVariant = variant });
    }

    private static Candidate Measure(int variant, int samples)
    {
        WalkingWorld world = Build(variant);
        double area = GameScale.SurfaceArea(world.Radius) / 1e6;
        int dry = 0, gentle = 0, open = 0, forest = 0;
        double mountains = 0;
        var heights = new List<double>();
        var biomeTotals = new double[BiomeTable.Count];
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        foreach (Vector3D d in PlanetSurface.FibonacciSphere(samples))
        {
            double h = world.HeightAt(d);
            if (h < world.WaterLevelAt(d) + 0.15) continue;
            dry++; heights.Add(h);
            Vector3D east = Vector3D.Cross(Vector3D.UnitY, d);
            east = east.LengthSquared > 1e-12 ? east.Normalized : Vector3D.UnitX;
            Vector3D north = Vector3D.Cross(d, east);
            const double Step = 8;
            double low = h, high = h;
            bool footprintDry = true;
            double At(Vector3D offset)
            {
                Vector3D p = (d + offset * (Step / world.Radius)).Normalized;
                double value = world.HeightAt(p);
                footprintDry &= value >= world.WaterLevelAt(p) + 0.15;
                low = Math.Min(low, value); high = Math.Max(high, value);
                return value;
            }
            double de = (At(east) - At(-east)) / (2 * Step), dn = (At(north) - At(-north)) / (2 * Step);
            double grade = Math.Sqrt(de * de + dn * dn), slope = grade / Math.Sqrt(1 + grade * grade);
            GroundCover cover = world.Cover(d, world.Sample(d), slope, biomes, materials);
            for (int i = 0; i < biomeTotals.Length; i++) biomeTotals[i] += biomes[i];
            if (cover.Trees >= 0.008) forest++;
            mountains += world.Fields(d).Mountains;
            if (footprintDry && grade <= 0.17632698 && high - low <= 2.8)
            {
                gentle++;
                if (cover.Trees < 0.006 && world.Monuments.At(d, 12) == null) open++;
            }
        }
        heights.Sort();
        double Q(double fraction) => heights.Count == 0 ? 0 : heights[(int)((heights.Count - 1) * fraction)];
        var shares = Enumerable.Range(0, biomeTotals.Length).ToDictionary(i => ((Biome)i).ToString(), i => biomeTotals[i] / Math.Max(1, dry));
        return new Candidate(variant, area, area * dry / samples, area * gentle / samples, area * open / samples,
            (double)forest / Math.Max(1, dry), mountains / Math.Max(1, dry), shares.Values.Count(v => v >= 0.03),
            Q(0.1), Q(0.9), Q(1), Connectivity(world), shares);
    }

    internal static double Connectivity(WalkingWorld world)
    {
        const int Level = 6;
        int count = (int)TileKey.CountAt(Level);
        var dry = new bool[count]; var seen = new bool[count]; var areas = new double[count];
        double total = 0, largest = 0;
        for (int i = 0; i < count; i++)
        {
            TileKey tile = TileKey.FromIndex(Level, i)!.Value;
            Vector3D a = CubeSphere.Direction(tile.Face, tile.U0, tile.V0);
            Vector3D b = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span, tile.V0);
            Vector3D c = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span, tile.V0 + tile.Span);
            Vector3D d = CubeSphere.Direction(tile.Face, tile.U0, tile.V0 + tile.Span);
            areas[i] = Triangle(a, b, c) + Triangle(a, c, d);
            dry[i] = world.HeightAt(tile.Centre) >= world.WaterLevelAt(tile.Centre);
            if (dry[i]) total += areas[i];
        }
        var queue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if (!dry[i] || seen[i]) continue;
            double component = 0; seen[i] = true; queue.Enqueue(i);
            while (queue.TryDequeue(out int current))
            {
                component += areas[current];
                TileKey tile = TileKey.FromIndex(Level, current)!.Value;
                Visit(tile, -1, 0); Visit(tile, 1, 0); Visit(tile, 0, -1); Visit(tile, 0, 1);
            }
            largest = Math.Max(largest, component);
        }
        return total > 0 ? largest / total : 0;
        void Visit(TileKey tile, int dx, int dy)
        {
            int n = 1 << Level, x = tile.X + dx, y = tile.Y + dy;
            TileKey neighbour = x >= 0 && x < n && y >= 0 && y < n ? new TileKey(tile.Face, Level, x, y)
                : TileKey.At(CubeSphere.Direction(tile.Face, tile.U0 + tile.Span * (0.5 + dx * 0.5001), tile.V0 + tile.Span * (0.5 + dy * 0.5001)), Level);
            int index = (int)neighbour.Index;
            if (dry[index] && !seen[index]) { seen[index] = true; queue.Enqueue(index); }
        }
    }

    private static double Triangle(Vector3D a, Vector3D b, Vector3D c) =>
        2 * Math.Atan2(Math.Abs(Vector3D.Dot(a, Vector3D.Cross(b, c))), 1 + Vector3D.Dot(a, b) + Vector3D.Dot(b, c) + Vector3D.Dot(c, a));

    private static void WriteMaps(int variant, string folder, int width)
    {
        WalkingWorld world = Build(variant);
        WalkingReport.Map(world, "biomes", Path.Combine(folder, $"candidate-{variant}-biomes.png"), width, serial: true);
        WalkingReport.Map(world, "provinces", Path.Combine(folder, $"candidate-{variant}-relief.png"), width, serial: true);
    }
}
