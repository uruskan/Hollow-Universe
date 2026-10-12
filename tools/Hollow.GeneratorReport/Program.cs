using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.GeneratorReport;

/// <summary>
/// Measurements of the generator that are too slow or too visual for the test suite: generation speed, and later
/// maps and star counts (docs/progress-generator-v2.md). Usage:
/// <c>dotnet run -c Release --project tools/Hollow.GeneratorReport -- bench</c>
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string command = args.Length > 0 ? args[0] : "";
        switch (command)
        {
            case "bench":
                Bench();
                return 0;
            case "stats":
                Stats();
                return 0;
            default:
                Console.Error.WriteLine("usage: Hollow.GeneratorReport bench|stats");
                return 2;
        }
    }

    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HollowUniverse.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Repository root (HollowUniverse.sln) not found.");
    }

    internal static StarCatalogue LoadCatalogue() =>
        StarCatalogue.Load(Path.Combine(RepoRoot(), "game", "data", "catalog", "hyg41.bin.gz"));

    internal static IEnumerable<CellKey> CellsAround(int level, Vector3D centre, double radius)
    {
        double size = SectorGrid.CellSize(level);
        Vector3D rel = centre - SectorGrid.Origin;
        long x0 = (long)Math.Floor((rel.X - radius) / size);
        long x1 = (long)Math.Floor((rel.X + radius) / size);
        long y0 = (long)Math.Floor((rel.Y - radius) / size);
        long y1 = (long)Math.Floor((rel.Y + radius) / size);
        long z0 = (long)Math.Floor((rel.Z - radius) / size);
        long z1 = (long)Math.Floor((rel.Z + radius) / size);
        for (long z = z0; z <= z1; z++)
        {
            for (long y = y0; y <= y1; y++)
            {
                for (long x = x0; x <= x1; x++)
                {
                    if (CellKey.TryFromGlobal(level, x, y, z, out CellKey key))
                    {
                        yield return key;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- stats

    /// <summary>What the system generator makes: the mix of bodies in a fixed sample of procedural systems.</summary>
    private static void Stats()
    {
        var galaxy = new Galaxy.Galaxy(LoadCatalogue());
        var systems = new List<StarSystem>();
        foreach (CellKey cell in CellsAround(6, new Vector3D(400, 0, 400), 120).Concat(CellsAround(4, new Vector3D(400, 0, 400), 300))
                     .Concat(CellsAround(3, new Vector3D(-2000, 40, 9000), 300)))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                systems.Add(galaxy.GetSystem(star.Address)!);
            }
        }
        var types = new Dictionary<string, int>();
        var atmospheres = new Dictionary<string, int>();
        var temperatures = new List<double>();
        int planets = 0, moons = 0, belts = 0, multiples = 0, landable = 0, hotGiants = 0;
        Body? hottest = null;
        foreach (StarSystem system in systems)
        {
            multiples += system.Count(BodyKind.Star) > 1 ? 1 : 0;
            foreach (Body b in system.Bodies)
            {
                if (b.Kind is BodyKind.Planet or BodyKind.Moon)
                {
                    string key = (b.Kind == BodyKind.Moon ? "moon: " : "") + b.TypeName;
                    types[key] = types.GetValueOrDefault(key) + 1;
                    string atmosphere = b.Atmosphere.ToString();
                    atmospheres[atmosphere] = atmospheres.GetValueOrDefault(atmosphere) + 1;
                    landable += b.Landable ? 1 : 0;
                }
                if (b.Kind == BodyKind.Planet)
                {
                    planets++;
                    temperatures.Add(b.Temperature);
                    if (hottest == null || b.Temperature > hottest.Temperature)
                    {
                        hottest = b;
                    }
                    hotGiants += b.PlanetType == PlanetType.GasGiant && b.Temperature > 1000 ? 1 : 0;
                }
                moons += b.Kind == BodyKind.Moon ? 1 : 0;
                belts += b.Kind == BodyKind.Belt ? 1 : 0;
            }
        }
        temperatures.Sort();
        CultureInfo c = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Create(c, $"{systems.Count} systems ({100.0 * multiples / systems.Count:0.0}% multiple), " +
            $"{planets} planets, {moons} moons, {belts} belts; {100.0 * landable / (planets + moons):0.0}% of planets and moons landable"));
        Console.WriteLine(string.Create(c, $"planet temperature: median {temperatures[temperatures.Count / 2]:0} K, " +
            $"99th percentile {temperatures[(int)(temperatures.Count * 0.99)]:0} K, hottest {hottest!.Temperature:0} K ({hottest.Name}, {hottest.TypeName}); " +
            $"{hotGiants} gas giants above 1,000 K"));
        Console.WriteLine("types: " + string.Join(", ", types.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
        Console.WriteLine("atmospheres: " + string.Join(", ", atmospheres.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
    }

    // ---------------------------------------------------------------- bench

    /// <summary>
    /// Milliseconds per generated star system, single-threaded, for a fixed sample of procedural systems (the
    /// neighbourhood 400 ly from Sol and a patch of the inner disk) and of real multiple stars.
    /// </summary>
    private static void Bench()
    {
        var galaxy = new Galaxy.Galaxy(LoadCatalogue());
        var procedural = new List<(SystemAddress Address, string Name)>();
        foreach (CellKey cell in CellsAround(6, new Vector3D(400, 0, 400), 120).Concat(CellsAround(4, new Vector3D(400, 0, 400), 300))
                     .Concat(CellsAround(5, new Vector3D(-2000, 40, 9000), 100)))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                procedural.Add((star.Address, galaxy.NameOf(star.Address)));
            }
        }
        var stars = new Dictionary<SystemAddress, GalaxyStar>();
        foreach ((SystemAddress address, string _) in procedural)
        {
            galaxy.TryGetStar(address, out GalaxyStar s);
            stars[address] = s;
        }
        var generator = new SystemGenerator(galaxy.Seed);

        // Warm up the JIT, then time three passes and keep the best (least disturbed) one.
        long bodies = 0;
        for (int i = 0; i < Math.Min(3000, procedural.Count); i++)
        {
            generator.Generate(stars[procedural[i].Address], procedural[i].Name);
        }
        double best = double.MaxValue;
        for (int pass = 0; pass < 3; pass++)
        {
            bodies = 0;
            var watch = Stopwatch.StartNew();
            foreach ((SystemAddress address, string name) in procedural)
            {
                bodies += generator.Generate(stars[address], name).Bodies.Count;
            }
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"procedural: {procedural.Count} systems, {bodies} bodies ({(double)bodies / procedural.Count:0.0} per system): " +
            $"{best / procedural.Count * 1000:0.0} µs per system, {best / bodies * 1000:0.00} µs per body"));

        // Through the facade (name lookup, catalogue companions, Sol), real stars within 60 ly.
        List<SystemAddress> real = galaxy.Catalogue!.Primaries.Where(s => s.Position.Length < 60)
            .Select(s => SystemAddress.Catalogue(galaxy.Catalogue.Number, s.Id)).ToList();
        foreach (SystemAddress address in real)
        {
            galaxy.GetSystem(address);
        }
        double bestReal = double.MaxValue;
        long realBodies = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            realBodies = 0;
            var watch = Stopwatch.StartNew();
            foreach (SystemAddress address in real)
            {
                realBodies += galaxy.GetSystem(address)!.Bodies.Count;
            }
            bestReal = Math.Min(bestReal, watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"real (GetSystem, < 60 ly): {real.Count} systems, {realBodies} bodies: {bestReal / real.Count * 1000:0.0} µs per system"));
    }
}
