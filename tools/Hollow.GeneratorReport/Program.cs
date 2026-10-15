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
            case "names":
                ExampleNames();
                return 0;
            case "shells":
                Shells.Report(args.Length > 1 ? StarCatalogue.Load(args[1]) : LoadCatalogue());
                return 0;
            case "calibrate":
                Calibrate.Report(LoadCatalogue());
                return 0;
            case "stars":
                StarStats();
                return 0;
            case "find":
                FindShowcases();
                return 0;
            case "worlds":
                ShowWorlds();
                return 0;
            case "near":
                Near(args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 20);
                return 0;
            case "completeness":
                for (int level = 0; level < StarClassTable.LevelCount; level++)
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"level {level}: catalogue only within {CatalogueCompleteness.CompleteWithin(level):0.0} ly, " +
                        $"procedural only beyond {CatalogueCompleteness.EmptyBeyond(level):0.0} ly"));
                }
                return 0;
            case "surface":
                return SurfaceReport.Run(args);
            case "walking":
                return WalkingReport.Run(args);
            case "maps":
                Maps.Render(args.Length > 1 ? args[1] : Path.Combine(RepoRoot(), "docs", "images", "generator"),
                    args.Length > 2 ? args[2] : "",
                    args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : StarClassTable.LevelCount - 1);
                return 0;
            default:
                Console.Error.WriteLine("usage: Hollow.GeneratorReport bench|stats|names|maps [folder] [prefix]|shells|calibrate|completeness|stars|find|worlds|surface|walking");
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

    // ---------------------------------------------------------------- names

    /// <summary>Real examples of generated names for the docs: one system per level near Sol, and a moon.</summary>
    private static void ExampleNames()
    {
        var galaxy = new Galaxy.Galaxy(LoadCatalogue());
        Console.WriteLine($"Sol's sector: {Galaxy.Galaxy.SectorNameAt(Vector3D.Zero)}");
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            foreach (CellKey cell in CellsAround(level, new Vector3D(600, -40, 900), SectorGrid.CellSize(level) * 2))
            {
                GalaxyStar[] stars = galaxy.GenerateProcedural(cell);
                if (stars.Length == 0)
                {
                    continue;
                }
                StarSystem system = galaxy.GetSystem(stars[^1].Address)!;
                Body? moon = system.Bodies.FirstOrDefault(b => b.Kind == BodyKind.Moon);
                Console.WriteLine($"level {level}: {system.Name} ({system.Root.StarClass}){(moon != null ? $"; a moon: {moon.Name}" : "")}");
                break;
            }
        }
    }

    // ---------------------------------------------------------------- find

    /// <summary>Generated systems worth a screenshot near Sol: multiple stars with planets around companions and pairs.</summary>
    private static void FindShowcases()
    {
        var galaxy = new Galaxy.Galaxy(LoadCatalogue());
        int found = 0;
        foreach (CellKey cell in CellsAround(5, new Vector3D(300, 0, 300), 200))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                StarSystem system = galaxy.GetSystem(star.Address)!;
                int stars = system.Count(BodyKind.Star);
                int circumbinary = system.Bodies.Count(b => b.Kind == BodyKind.Planet && system.Bodies[b.Parent].Kind == BodyKind.Barycentre);
                int companion = system.Bodies.Count(b => b.Kind == BodyKind.Planet && system.Bodies[b.Parent].Kind == BodyKind.Star
                    && b.Parent != system.PrimaryStar.Index);
                int primaryPlanets = system.Bodies.Count(b => b.Kind == BodyKind.Planet && b.Parent == system.PrimaryStar.Index);
                if (stars >= 2 && circumbinary + companion >= 3 && (primaryPlanets > 0 || circumbinary > 0))
                {
                    Console.WriteLine($"{system.Name}: {stars} stars, {primaryPlanets} planets around A, {companion} around companions, " +
                        $"{circumbinary} around pairs, {system.Count(BodyKind.Moon)} moons");
                    if (++found >= 12)
                    {
                        return;
                    }
                }
            }
        }
    }

    /// <summary>Every catalogue star (primaries and their companions) within a distance of Sol, nearest first.</summary>
    private static void Near(double radiusLy)
    {
        StarCatalogue catalogue = LoadCatalogue();
        CultureInfo c = CultureInfo.InvariantCulture;
        foreach (CatalogueStar s in catalogue.Primaries.Where(s => s.Position.Length <= radiusLy).OrderBy(s => s.Position.Length))
        {
            Console.WriteLine(string.Create(c, $"{s.Position.Length,6:0.00} ly  {s.Id,7}  {s.Class,-10} {s.Physics.AbsoluteMagnitude,6:0.0}  {s.SystemName} / {s.Name}  [{string.Join(", ", s.Aliases.Take(4))}]"));
            foreach (CatalogueStar m in s.Companions)
            {
                Console.WriteLine(string.Create(c, $"           {m.Id,7}  {m.Class,-10} {m.Physics.AbsoluteMagnitude,6:0.0}    + {m.Name} at {Vector3D.Distance(m.Position, s.Position) * 63241.1:0} AU  [{string.Join(", ", m.Aliases.Take(3))}]"));
            }
        }
    }

    /// <summary>The first world of each notable kind near a fixed point, with its whole physical state (GALAXY.md §10.3).</summary>
    private static void ShowWorlds()
    {
        var galaxy = new Galaxy.Galaxy(LoadCatalogue());
        var wanted = new (string Label, Func<Body, bool> Test)[]
        {
            ("Complex life", b => b.State is { Biosphere: Biosphere.Complex }),
            ("Oxygen world", b => b.State is { Biosphere: Biosphere.Simple }),
            ("Ocean world", b => b.State is { Surface: SurfaceKind.Ocean, Liquid: LiquidKind.Water }),
            ("Lava world", b => b.PlanetType == PlanetType.LavaWorld && b.SurfacePressure < 0.01),
            ("Io-like moon", b => b.Kind == BodyKind.Moon && b.State is { Volcanism: Volcanism.Sulphur, VolcanicActivity: > 0.6 }),
            ("Ice shell over an ocean", b => b.Kind == BodyKind.Moon && b.State is { SubsurfaceOcean: true, Volcanism: Volcanism.Water }),
            ("Hydrocarbon lakes", b => b.State is { Liquid: LiquidKind.Hydrocarbons }),
            ("Ammonia sea", b => b.State is { Liquid: LiquidKind.AmmoniaWater }),
            ("Hothouse", b => b.State != null && b.Atmosphere == AtmosphereType.CarbonDioxide && b.SurfacePressure > 30),
        };
        var found = new Dictionary<string, (StarSystem System, Body Body)>();
        foreach (CellKey cell in CellsAround(6, new Vector3D(400, 0, 400), 150).Concat(CellsAround(5, new Vector3D(400, 0, 400), 300)))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                StarSystem system = galaxy.GetSystem(star.Address)!;
                foreach (Body b in system.Bodies)
                {
                    foreach ((string label, Func<Body, bool> test) in wanted)
                    {
                        if (!found.ContainsKey(label) && test(b))
                        {
                            found[label] = (system, b);
                        }
                    }
                }
            }
            if (found.Count == wanted.Length)
            {
                break;
            }
        }
        CultureInfo c = CultureInfo.InvariantCulture;
        foreach ((string label, _) in wanted)
        {
            if (!found.TryGetValue(label, out var hit))
            {
                Console.WriteLine($"{label}: none found");
                continue;
            }
            Body b = hit.Body;
            PlanetState s = b.State!;
            Body host = hit.System.PrimaryStar;
            Console.WriteLine(string.Create(c, $"{label}: {b.Name} ({b.TypeName}) around {host.StarClass}, age {hit.System.Age:0.0} Gyr, " +
                $"{b.Mass / Units.EarthMass:0.###} M⊕, {b.Radius / 1000:0} km, {b.Temperature:0} K ({s.MinTemperature:0}–{s.MaxTemperature:0}), " +
                $"{b.SurfacePressure:0.####} atm {b.AtmosphereName}"));
            Console.WriteLine(string.Create(c, $"    zone {s.FormationZone:0.00}, metal {s.Composition.Metal:0.00} rock {s.Composition.Rock:0.00} ice {s.Composition.Ice:0.000}; " +
                $"heat {s.HeatFlow:0.000} W/m² (tidal {s.TidalHeating:0.000}), {s.Tectonics}, {s.Volcanism} {s.VolcanicActivity:0.00}, field {s.MagneticField:0.00}, " +
                $"cratering {s.Cratering:0.00}"));
            Console.WriteLine(string.Create(c, $"    albedo {s.Albedo:0.00}, greenhouse {s.Greenhouse:0.00}; N2 {s.Gases.Nitrogen:0.###} O2 {s.Gases.Oxygen:0.###} " +
                $"CO2 {s.Gases.CarbonDioxide:0.####} H2O {s.Gases.Water:0.####} CH4 {s.Gases.Methane:0.####}; {s.Liquid} {s.OceanCover:0.00}, ice {s.IceCover:0.00}, " +
                $"subsurface ocean {s.SubsurfaceOcean}, {s.Biosphere}, {s.Surface}; {s.Hazards}"));
        }
    }

    // ---------------------------------------------------------------- stars

    /// <summary>Ages and evolved stars (GALAXY.md §4.1) in a sample of generated stars: near Sol's radius in the disk, and the bulge.</summary>
    private static void StarStats()
    {
        var galaxy = new Galaxy.Galaxy(null);
        var stars = new List<GalaxyStar>();
        foreach (Vector3D centre in new[] { new Vector3D(3000, 0, 2000), new Vector3D(-2000, 1500, 6000), GalaxyFrame.GalacticCentre + new Vector3D(1500, 300, 0) })
        {
            for (int level = 0; level < StarClassTable.LevelCount; level++)
            {
                foreach (CellKey cell in CellsAround(level, centre, SectorGrid.CellSize(level)).Take(level < 5 ? 27 : 8))
                {
                    stars.AddRange(galaxy.GenerateProcedural(cell));
                }
            }
        }
        CultureInfo c = CultureInfo.InvariantCulture;
        Console.WriteLine($"{stars.Count} stars");
        foreach (IGrouping<GalaxyComponent, GalaxyStar> group in stars.GroupBy(s => s.Component).OrderBy(g => g.Key))
        {
            List<double> ages = group.Select(s => (double)s.Age).OrderBy(a => a).ToList();
            (double min, double _) = StellarEvolution.AgeRange(group.Key);
            int late = group.Count(s => s.Class.Kind == StarKind.MainSequence && s.Age < min);
            Console.WriteLine(string.Create(c, $"{group.Key}: {group.Count()} stars, age median {ages[ages.Count / 2]:0.00} Gyr, " +
                $"5–95% {ages[ages.Count / 20]:0.00}–{ages[ages.Count * 19 / 20]:0.00}; main-sequence stars younger than the population: {late}"));
        }
        foreach (StarKind kind in new[] { StarKind.Giant, StarKind.Subgiant, StarKind.BrightGiant, StarKind.WhiteDwarf, StarKind.BrownDwarf, StarKind.NeutronStar })
        {
            List<GalaxyStar> list = stars.Where(s => s.Class.Kind == kind).ToList();
            if (list.Count == 0)
            {
                continue;
            }
            List<double> masses = list.Select(s => (double)s.Physics.Mass).OrderBy(m => m).ToList();
            List<double> temps = list.Select(s => (double)s.Physics.Temperature).OrderBy(t => t).ToList();
            Console.WriteLine(string.Create(c, $"{kind}: {list.Count}, mass median {masses[masses.Count / 2]:0.000} (5–95% {masses[masses.Count / 20]:0.000}–{masses[masses.Count * 19 / 20]:0.000}), " +
                $"Teff median {temps[temps.Count / 2]:0} K (5–95% {temps[temps.Count / 20]:0}–{temps[temps.Count * 19 / 20]:0})"));
        }
        var bd = stars.Where(s => s.Class.Kind == StarKind.BrownDwarf).GroupBy(s => s.Class.Letter).OrderBy(g => StarClass.LetterIndex(g.Key));
        Console.WriteLine("brown dwarfs by spectral letter: " + string.Join(", ", bd.Select(g => $"{g.Key} {g.Count()}")));
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
        var companions = new Dictionary<string, int>();
        var temperatures = new List<double>();
        int planets = 0, moons = 0, belts = 0, multiples = 0, landable = 0, hotGiants = 0;
        Body? hottest = null;
        foreach (StarSystem system in systems)
        {
            multiples += system.Count(BodyKind.Star) > 1 ? 1 : 0;
            foreach (Body b in system.Bodies)
            {
                if (b.Kind == BodyKind.Star && b.Path != BodyPath.Node(1))
                {
                    string kind = b.StarClass.Kind.ToString();
                    companions[kind] = companions.GetValueOrDefault(kind) + 1;
                }
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
        Console.WriteLine("companion stars: " + string.Join(", ", companions.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));

        // The physical state (GALAXY.md §10.3) of planets and moons.
        List<PlanetState> states = systems.SelectMany(s => s.Bodies).Where(b => b.State != null).Select(b => b.State!).ToList();
        string Tally<T>(Func<PlanetState, T> key) where T : notnull =>
            string.Join(", ", states.GroupBy(key).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"));
        Console.WriteLine("surface: " + Tally(s => s.Surface));
        Console.WriteLine("liquid: " + Tally(s => s.Liquid));
        Console.WriteLine("biosphere: " + Tally(s => s.Biosphere));
        Console.WriteLine("tectonics: " + Tally(s => s.Tectonics));
        Console.WriteLine("volcanism: " + Tally(s => s.Volcanism));
        Console.WriteLine(string.Create(c, $"subsurface oceans: {states.Count(s => s.SubsurfaceOcean)}; magnetic fields above 0.1: {states.Count(s => s.MagneticField > 0.1)}"));
        var solids = systems.SelectMany(s => s.Bodies).Where(b => b.State is { Surface: not SurfaceKind.None }).ToList();
        foreach (BodyKind kind in new[] { BodyKind.Planet, BodyKind.Moon })
        {
            List<Body> group = solids.Where(b => b.Kind == kind).ToList();
            int active = group.Count(b => b.State!.Volcanism != Volcanism.None);
            int tidal = group.Count(b => b.State!.Volcanism != Volcanism.None && b.State.TidalHeating > b.State.HeatFlow / 2);
            List<double> flows = group.Select(b => b.State!.HeatFlow).OrderBy(x => x).ToList();
            Console.WriteLine(string.Create(c, $"{kind}s: {group.Count} solid, {100.0 * active / group.Count:0.0}% volcanic ({100.0 * tidal / group.Count:0.0}% by tides); " +
                $"heat flow p50 {flows[flows.Count / 2]:0.0000}, p90 {flows[flows.Count * 9 / 10]:0.000}, p99 {flows[flows.Count * 99 / 100]:0.00} W/m²"));
        }
        var hazardCounts = Enum.GetValues<Hazards>().Where(h => h != Hazards.None)
            .Select(h => (h, states.Count(s => (s.Hazards & h) != 0))).OrderByDescending(x => x.Item2);
        Console.WriteLine("hazards: " + string.Join(", ", hazardCounts.Select(x => $"{x.h} {x.Item2}")));
        int earthLikeSystems = systems.Count(s => s.Bodies.Any(b => b.PlanetType == PlanetType.EarthLike));
        Console.WriteLine(string.Create(c, $"systems with an Earth-like world: {earthLikeSystems} (1 in {(double)systems.Count / Math.Max(1, earthLikeSystems):0})"));

        // Where the water and the life are, by the system's primary star: share of systems with surface water, with any
        // life, with oxygen (photosynthesis) and with complex life, in a volume-complete sample (every level inside
        // one sphere), which is what a traveller meets.
        var centre = new Vector3D(400, 0, 400);
        const double radius = 70;
        systems.Clear();
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            foreach (CellKey cell in CellsAround(level, centre, radius))
            {
                foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
                {
                    if ((star.Position - centre).Length <= radius)
                    {
                        systems.Add(galaxy.GetSystem(star.Address)!);
                    }
                }
            }
        }
        static bool Wet(StarSystem s) => s.Bodies.Any(b => b.State is { Liquid: LiquidKind.Water });
        static bool Alive(StarSystem s, Biosphere least) => s.Bodies.Any(b => b.State != null && b.State.Biosphere >= least);
        static string Primary(StarSystem s) => s.PrimaryStar.StarClass.Kind == StarKind.MainSequence
            ? s.PrimaryStar.StarClass.Letter.ToString()
            : s.PrimaryStar.StarClass.Kind.ToString();
        string Rate(int n, int total) => n == 0 ? "-" : string.Create(c, $"1 in {(double)total / n:0}");
        Console.WriteLine("water / life / oxygen / complex life, by primary:");
        foreach (var g in systems.GroupBy(Primary).OrderByDescending(g => g.Count()).Take(9))
        {
            int n = g.Count();
            Console.WriteLine(string.Create(c, $"  {g.Key,-12} {n,6} systems: {Rate(g.Count(Wet), n),-10} {Rate(g.Count(s => Alive(s, Biosphere.Microbial)), n),-10} " +
                $"{Rate(g.Count(s => Alive(s, Biosphere.Simple)), n),-10} {Rate(g.Count(s => Alive(s, Biosphere.Complex)), n)}"));
        }
        int all = systems.Count;
        Console.WriteLine(string.Create(c, $"  {"all",-12} {all,6} systems: {Rate(systems.Count(Wet), all),-10} {Rate(systems.Count(s => Alive(s, Biosphere.Microbial)), all),-10} " +
            $"{Rate(systems.Count(s => Alive(s, Biosphere.Simple)), all),-10} {Rate(systems.Count(s => Alive(s, Biosphere.Complex)), all)}"));
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
