using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.Tests;

public static class GalaxyTests
{
    // Golden: a hash over a fixed set of generated cells and systems. Changing the generator changes it;
    // update on purpose (AGENTS.md). A mismatch on another platform means generation isn't deterministic.
    private const ulong CellsGolden = 0xF3C42388CE7F958CUL;
    private const ulong SystemsGolden = 0x3672732AE16E55F1UL;
    private const ulong RealSystemsGolden = 0x6D9ECAF5B04D1C7DUL;

    // ---------------------------------------------------------------- frames

    [Test]
    public static void GalacticCentreAndPoleConvertToTheirAxes()
    {
        Vector3D centre = GalaxyFrame.FromRaDec(266.405, -28.936);
        Assert.Near(1.0, centre.Z, 1e-5, "Galactic Centre z");
        Assert.Near(0.0, centre.X, 2e-3, "Galactic Centre x");
        Assert.Near(0.0, centre.Y, 2e-3, "Galactic Centre y");
        Vector3D pole = GalaxyFrame.FromRaDec(192.85948, 27.12825);
        Assert.Near(1.0, pole.Y, 1e-6, "north galactic pole y");
        Vector3D l90 = GalaxyFrame.FromLongitudeLatitude(90, 0);
        Assert.Near(1.0, l90.X, 1e-12, "l=90° is +X");
    }

    [Test]
    public static void SunSitsAboveTheMidplane()
    {
        Vector3D sun = GalaxyFrame.SunInModel;
        Assert.Near(GalaxyFrame.SunHeight, sun.Y, 1e-6, "Sun height in model frame");
        Assert.Near(GalaxyFrame.SunToCentre, Math.Sqrt(sun.X * sun.X + sun.Z * sun.Z), 1.0, "Sun's galactocentric radius");
        Vector3D back = GalaxyFrame.FromModel(GalaxyFrame.ToModel(new Vector3D(123, -45, 6789)));
        Assert.Near(0, Vector3D.Distance(back, new Vector3D(123, -45, 6789)), 1e-9, "model frame round trip");
    }

    // ---------------------------------------------------------------- addresses and names

    [Test]
    public static void AddressesRoundTrip()
    {
        var rng = new Rng(99);
        for (int i = 0; i < 20000; i++)
        {
            int level = rng.NextInt(8);
            int n = 1 << level;
            var cell = new CellKey(level, rng.NextInt(128), rng.NextInt(128), rng.NextInt(128), rng.NextInt(n), rng.NextInt(n), rng.NextInt(n));
            long index = (long)(rng.NextULong() % (1UL << cell.StarIndexBits));
            SystemAddress address = SystemAddress.Procedural(cell, index);
            Assert.True(!address.IsCatalogue, "procedural flag");
            Assert.Equal(cell, address.Cell, "cell");
            Assert.Equal(index, address.StarIndex, "index");
            Assert.Equal(cell, CellKey.FromPacked(cell.Packed), "packed cell");
            Assert.True(CellKey.TryFromPosition(level, cell.Centre, out CellKey back) && back == cell, "cell from its centre");
        }
        SystemAddress catalogue = SystemAddress.Catalogue(0, 71456);
        Assert.True(catalogue.IsCatalogue, "catalogue flag");
        Assert.Equal(71456L, catalogue.CatalogueId, "catalogue id");
        Assert.True(CellKey.TryFromPosition(5, Vector3D.Zero, out CellKey sol) && sol.SectorX == 64 && sol.SectorY == 64 && sol.SectorZ == 38,
            "Sol is in sector (64, 64, 38)");
    }

    [Test]
    public static void SectorNamesAreUniqueAndParse()
    {
        Assert.Equal(1 << 21, SectorNames.TableSize, "one name per sector");
        var rng = new Rng(5);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 100000; i++)
        {
            int sx = rng.NextInt(128);
            int sy = rng.NextInt(128);
            int sz = rng.NextInt(128);
            string name = SectorNames.Name(sx, sy, sz);
            Assert.True(SectorNames.TryParse(name, out int x, out int y, out int z) && x == sx && y == sy && z == sz,
                $"'{name}' parses back to ({sx},{sy},{sz})");
            Assert.True(SectorNames.TryParse(name.ToUpperInvariant(), out _, out _, out _), "case-insensitive");
            seen.Add(SectorGrid.SectorIndex(sx, sy, sz) + ":" + name);
        }
        for (uint i = 0; i < 1u << 21; i++)
        {
            if (SectorNames.Unscramble(SectorNames.Scramble(i)) != i)
            {
                throw new TestFailure($"sector scramble is not invertible at {i}");
            }
        }
        // Neighbouring sectors should rarely share a first word.
        int shared = 0;
        for (int z = 0; z < 127; z++)
        {
            string a = SectorNames.Name(64, 64, z).Split(' ')[0];
            string b = SectorNames.Name(64, 64, z + 1).Split(' ')[0];
            shared += a == b ? 1 : 0;
        }
        Assert.True(shared <= 6, $"{shared} neighbouring sectors share a first word");
        Fixtures.Report($"Sol's sector: {SectorNames.Name(64, 64, 38)}; centre: {SectorNames.Name(64, 64, 64)}; next to Sol: {SectorNames.Name(65, 64, 38)}");
    }

    [Test]
    public static void SystemNamesRoundTrip()
    {
        var rng = new Rng(17);
        for (int i = 0; i < 20000; i++)
        {
            int level = rng.NextInt(8);
            int n = 1 << level;
            var cell = new CellKey(level, rng.NextInt(128), rng.NextInt(128), rng.NextInt(128), rng.NextInt(n), rng.NextInt(n), rng.NextInt(n));
            SystemAddress address = SystemAddress.Procedural(cell, rng.NextInt(5000));
            string name = SystemNames.Format(address);
            Assert.True(SystemNames.TryParse(name, out SystemAddress back) && back == address, $"'{name}' parses back");
        }
        Assert.True(!SystemNames.TryParse("Sirius", out _), "a catalogue name isn't a procedural name");
    }

    // ---------------------------------------------------------------- density and counts

    [Test]
    public static void LocalDensityMatchesTheSolarNeighbourhood()
    {
        var model = new DensityModel();
        double total = model.Total(Vector3D.Zero);
        Assert.Near(DensityModel.LocalDensity, total, 1e-9, "density at the Sun");
        double centre = model.Total(GalaxyFrame.GalacticCentre);
        Fixtures.Report($"density at Sol {total:E3} /ly³, at the centre {centre:E3} /ly³");
        Assert.True(centre > 100 * total, "the core is much denser than the neighbourhood");
    }

    [Test]
    public static void TotalSystemCountIsMilkyWayLike()
    {
        var model = new DensityModel();
        const double step = 1024;
        int columns = SectorGrid.SectorsPerAxis;
        var totals = new double[columns];
        Parallel.For(0, columns, ix =>
        {
            Span<double> c = stackalloc double[DensityModel.ComponentCount];
            double sum = 0;
            for (int iz = 0; iz < columns; iz++)
            {
                double x = SectorGrid.Origin.X + (ix + 0.5) * step;
                double z = SectorGrid.Origin.Z + (iz + 0.5) * step;
                // Fine vertical steps near the plane, coarse far from it.
                for (double y = -3000; y < 3000; y += 50)
                {
                    sum += model.Evaluate(new Vector3D(x, y + 25, z), c) * step * step * 50;
                }
                for (double y = 3000; y < 60000; y += 1000)
                {
                    sum += model.Evaluate(new Vector3D(x, y + 500, z), c) * step * step * 1000;
                    sum += model.Evaluate(new Vector3D(x, -y - 500, z), c) * step * step * 1000;
                }
            }
            totals[ix] = sum;
        });
        double total = totals.Sum();
        Fixtures.Report($"systems in the galaxy: {total:E3}");
        Assert.InRange(1.1e11, 1.7e11, total, "total number of systems");
    }

    [Test]
    public static void NeighbourhoodCountsAreRealistic()
    {
        int within10 = CountSystemsWithin(10, out int catalogue10);
        int within50 = CountSystemsWithin(50, out int catalogue50);
        Fixtures.Report($"within 10 ly: {within10} ({catalogue10} real); within 50 ly: {within50} ({catalogue50} real)");
        Assert.InRange(8, 14, within10, "systems within 10 ly");
        Assert.InRange(900, 1900, within50, "systems within 50 ly");
    }

    private static int CountSystemsWithin(double radius, out int real)
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        real = galaxy.Catalogue!.Primaries.Count(s => s.Position.Length <= radius);
        int count = real;
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            foreach (CellKey cell in CellsAround(level, Vector3D.Zero, radius))
            {
                foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
                {
                    if (star.Position.Length <= radius)
                    {
                        count++;
                    }
                }
            }
        }
        return count;
    }

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

    // ---------------------------------------------------------------- cells

    private static IEnumerable<CellKey> GoldenCells()
    {
        Vector3D[] places =
        {
            Vector3D.Zero, // Sol
            new(300, -40, 900), // near Sol
            GalaxyFrame.GalacticCentre + new Vector3D(400, 0, 300), // the core
            GalaxyFrame.FromModel(new Vector3D(0, 0, -22500)), // the near arm
            new(0, 9000, 0), // halo above Sol
        };
        foreach (Vector3D place in places)
        {
            for (int level = 0; level < StarClassTable.LevelCount; level++)
            {
                CellKey.TryFromPosition(level, place, out CellKey key);
                yield return key;
            }
        }
    }

    [Test]
    public static void CellGenerationIsDeterministic()
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        List<CellKey> cells = GoldenCells().ToList();
        ulong sequential = 0;
        int stars = 0;
        foreach (CellKey cell in cells)
        {
            GalaxyStar[] generated = galaxy.GenerateProcedural(cell);
            stars += generated.Length;
            sequential = Hash64.Combine(sequential, HashStars(generated));
        }
        // Again, in parallel and in reverse order.
        var parallel = new ulong[cells.Count];
        Parallel.For(0, cells.Count, i =>
        {
            int j = cells.Count - 1 - i;
            parallel[j] = HashStars(galaxy.GenerateProcedural(cells[j]));
        });
        ulong combined = 0;
        foreach (ulong h in parallel)
        {
            combined = Hash64.Combine(combined, h);
        }
        Fixtures.Report($"{stars} stars in {cells.Count} cells, hash 0x{sequential:X16}");
        Assert.Equal(sequential, combined, "parallel generation matches sequential");
        Assert.Equal(CellsGolden, sequential, "cells golden hash");
    }

    [Test]
    public static void GeneratedStarsMatchTheirCellAndLevel()
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        foreach (CellKey cell in GoldenCells())
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                Vector3D local = star.Position - cell.Min;
                Assert.True(local.X >= 0 && local.Y >= 0 && local.Z >= 0 && local.X <= cell.Size && local.Y <= cell.Size && local.Z <= cell.Size,
                    $"star {star.Address} lies in its cell");
                Assert.Equal(cell.Level, StarClassTable.LevelForMagnitude(star.Physics.AbsoluteMagnitude), $"level of {star.Class}");
                Assert.True(star.Physics.Mass > 0 && float.IsFinite(star.Physics.Radius), "physical values");
            }
        }
    }

    [Test]
    public static void SamplerFollowsTheDensity()
    {
        // Level-0 stars are all young-disk stars, whose density falls as sech²(h / 660 ly) away from the midplane, inside
        // cells 1,024 ly tall. Count the stars within 165 ly of the midplane in 120 such cells and compare with the
        // density's share of each cell (a uniform spread inside the cells would give about 15% fewer).
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        double observed = 0;
        double expected = 0;
        double uniform = 0;
        int stars = 0;
        for (int i = 0; i < 120; i++)
        {
            // Along two rings around the Galactic Centre, well away from Sol's catalogue region.
            double angle = i % 60 * 0.105;
            double ring = i < 60 ? 15000 : 21000;
            Vector3D model = new(DMath.Sin(angle) * ring, 0, -DMath.Cos(angle) * ring);
            CellKey.TryFromPosition(0, GalaxyFrame.FromModel(model), out CellKey cell);
            double hMin = double.MaxValue;
            double hMax = double.MinValue;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3D p = cell.Min + new Vector3D((corner & 1) * cell.Size, (corner >> 1 & 1) * cell.Size, (corner >> 2) * cell.Size);
                double h = GalaxyFrame.ToModel(p).Y;
                hMin = Math.Min(hMin, h);
                hMax = Math.Max(hMax, h);
            }
            double Tanh(double h) => Math.Tanh(h / 660);
            double inside = Tanh(Math.Clamp(165, hMin, hMax)) - Tanh(Math.Clamp(-165, hMin, hMax));
            double share = inside / (Tanh(hMax) - Tanh(hMin));
            double uniformShare = (Math.Clamp(165, hMin, hMax) - Math.Clamp(-165, hMin, hMax)) / (hMax - hMin);
            foreach (GalaxyStar s in galaxy.GenerateProcedural(cell))
            {
                observed += Math.Abs(GalaxyFrame.ToModel(s.Position).Y) < 165 ? 1 : 0;
                expected += share;
                uniform += uniformShare;
                stars++;
            }
        }
        double sigma = Math.Sqrt(expected * (1 - expected / stars));
        Fixtures.Report($"{stars} level-0 stars: {observed} within 165 ly of the midplane, density says {expected:0}, uniform {uniform:0}");
        Assert.True(stars > 1000 && expected - uniform > 6 * sigma, "the test can tell the density from a uniform spread");
        Assert.True(Math.Abs(observed - expected) < 4 * sigma, "stars follow the density inside their cells");
    }

    [Test]
    public static void AgesFollowPopulationsAndEvolution()
    {
        // GALAXY.md §4.1: ages come from the population; main-sequence stars are younger than their lifetime;
        // giants have the turnoff mass of their age; companions that would have died by now are remnants.
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        int stars = 0;
        int oldGiants = 0;
        foreach (CellKey cell in GoldenCells())
        {
            foreach (GalaxyStar s in galaxy.GenerateProcedural(cell).Take(400))
            {
                stars++;
                (double min, double max) = StellarEvolution.AgeRange(s.Component);
                Assert.True(s.Age >= 0 && s.Age <= StellarEvolution.OldestStar + 1e-6, $"{s.Address}: age {s.Age} Gyr");
                switch (s.Class.Kind)
                {
                    case StarKind.MainSequence:
                        Assert.True(s.Age <= StellarEvolution.MainSequenceLifetime(s.Physics.Mass) * 1.0001,
                            $"{s.Class} star of {s.Physics.Mass} M☉ is {s.Age} Gyr old, past its main sequence");
                        break;
                    case StarKind.Giant:
                    case StarKind.Subgiant:
                    case StarKind.BrightGiant:
                        Assert.True(s.Age >= Math.Max(0.04, min) - 1e-6 && s.Age <= max + 1e-6, $"{s.Class} age {s.Age} in {s.Component}");
                        double turnoff = StellarEvolution.TurnoffMass(s.Age);
                        Assert.True(Math.Abs(s.Physics.Mass / Math.Clamp(turnoff, 0.8, 8.0) - 1) < 0.06, $"{s.Class} mass {s.Physics.Mass} at turnoff {turnoff}");
                        oldGiants += s.Component is GalaxyComponent.Halo or GalaxyComponent.ThickDisk && s.Physics.Mass < 1.1 ? 1 : 0;
                        break;
                    case StarKind.WhiteDwarf:
                        Assert.True(s.Physics.Mass is >= 0.49f and <= 1.36f && s.Physics.Temperature >= 3499, $"white dwarf {s.Physics}");
                        break;
                }
            }
        }
        // Companions: none is still on the main sequence past its lifetime.
        int companions = 0;
        int remnants = 0;
        foreach (CellKey cell in CellsAround(5, new Vector3D(600, 0, 600), 64))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                StarSystem system = galaxy.GetSystem(star.Address)!;
                foreach (Body b in system.Bodies.Where(b => b.Kind == BodyKind.Star && b.Index > 0))
                {
                    companions++;
                    remnants += b.StarClass.Kind is StarKind.WhiteDwarf or StarKind.NeutronStar or StarKind.BlackHole ? 1 : 0;
                    if (b.StarClass.Kind == StarKind.MainSequence)
                    {
                        Assert.True(StellarEvolution.MainSequenceLifetime(b.Mass / Units.SolarMass) * 1.01 >= system.Age,
                            $"{b.Name} ({b.StarClass}, {b.Mass / Units.SolarMass:0.00} M☉) outlived its main sequence at {system.Age:0.0} Gyr");
                    }
                }
            }
        }
        Fixtures.Report($"{stars} stars checked, {oldGiants} old-population giants near 1 M☉; {companions} companions, {remnants} remnants");
        Assert.True(oldGiants > 0 && companions > 50 && remnants > 0, "the sample has old giants, companions and remnants");
    }

    [Test]
    public static void ClassMixMatchesTheNeighbourhood()
    {
        // Count primary classes 2,500 ly from Sol, beyond the catalogue's reach for levels 3–7 (GALAXY.md §8.3).
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        var counts = new Dictionary<char, double>();
        double total = 0;
        var centre = new Vector3D(0, 0, 0);
        for (int level = 3; level < StarClassTable.LevelCount; level++)
        {
            double size = SectorGrid.CellSize(level);
            // Sample a handful of cells per level and weight by cell volume share.
            foreach (CellKey cell in CellsAround(level, new Vector3D(2500, 0, 0), size * 1.5).Take(27))
            {
                foreach (GalaxyStar s in galaxy.GenerateProcedural(cell))
                {
                    double w = 1.0 / (size * size * size);
                    char key = s.Class.Kind == StarKind.WhiteDwarf ? 'D' : s.Class.Kind == StarKind.BrownDwarf ? 'L' : s.Class.Letter;
                    counts[key] = counts.GetValueOrDefault(key) + w;
                    total += w;
                }
            }
        }
        _ = centre;
        string mix = string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {100 * kv.Value / total:0.0}%"));
        Fixtures.Report("class mix (levels 3–7): " + mix);
        Assert.True(counts.GetValueOrDefault('M') / total > 0.4, "M dwarfs dominate");
    }

    [Test]
    public static void ProceduralStarsDoNotDependOnTheCatalogue()
    {
        // A client that failed to load the catalogue must still agree with the server about every procedural star,
        // so the region near Sol is left to the catalogue whether it is loaded or not.
        var without = new Galaxy.Galaxy(null);
        Galaxy.Galaxy with = Fixtures.Galaxy;
        int compared = 0;
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            double radius = CatalogueCompleteness.CompleteWithin(level);
            // Cells across the edge of the region where the catalogue holds every star of the level, and further out.
            foreach (CellKey cell in CellsAround(level, new Vector3D(0, 0, radius * 1.1), SectorGrid.CellSize(level))
                         .Concat(CellsAround(level, new Vector3D(0, 0, radius * 1.6), SectorGrid.CellSize(level))).Distinct())
            {
                GalaxyStar[] a = with.GenerateProcedural(cell);
                Assert.Equal(HashStars(a), HashStars(without.GenerateProcedural(cell)), $"level {level} cell with and without the catalogue");
                foreach (GalaxyStar s in a)
                {
                    Assert.True(s.Position.Length >= radius,
                        $"generated star {s.Address} at {s.Position.Length:0.0} ly is where the catalogue is complete ({radius:0} ly)");
                }
                compared += a.Length;
            }
        }
        Fixtures.Report($"{compared} generated stars around the catalogue's edge compared");
        Assert.True(compared > 1000, "enough stars compared");
    }

    [Test]
    public static void RealAndGeneratedStarsAddUpToTheModel()
    {
        // Where the catalogue holds part of a level (GALAXY.md §8.3), real plus generated stars match the density
        // model: no doubled neighbourhood beyond the catalogue's reach, no gap inside it.
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        var profile = new StarCountProfile();
        profile.Compute(galaxy.Density, Vector3D.Zero);
        (int Level, double Inner, double Outer)[] shells =
        {
            (2, 500, 800), (2, 800, 1500), (4, 250, 400), (4, 400, 800), (6, 20, 65), (6, 65, 250), (7, 25, 50), (7, 50, 100),
        };
        var parts = new List<string>();
        foreach ((int level, double inner, double outer) in shells)
        {
            double expected = profile.CountWithin(level, outer) - profile.CountWithin(level, inner);
            int real = galaxy.Catalogue!.Primaries.Count(s => s.Level == level && s.Position.Length >= inner && s.Position.Length < outer);
            int generated = 0;
            foreach (CellKey cell in CellsAround(level, Vector3D.Zero, outer))
            {
                foreach (GalaxyStar s in galaxy.GenerateProcedural(cell))
                {
                    double d = s.Position.Length;
                    generated += d >= inner && d < outer ? 1 : 0;
                }
            }
            double ratio = (real + generated) / expected;
            parts.Add($"L{level} {inner:0}–{outer:0} ly: {real} real + {generated} generated = {ratio:0.00} × model");
            Assert.InRange(0.85, 1.15, ratio, $"level {level} stars between {inner} and {outer} ly against the model");
        }
        Fixtures.Report(string.Join("; ", parts));
    }

    [Test]
    public static void SuppressionLeavesSurvivingStarsUnchanged()
    {
        // Where the catalogue holds part of a level's stars, the generated stars that survive must be exactly the
        // stars an unsuppressed generator makes: same addresses, positions, classes and physics.
        var density = new DensityModel();
        var suppressed = new CellGenerator(Galaxy.Galaxy.DefaultSeed, density);
        var everything = new CellGenerator(Galaxy.Galaxy.DefaultSeed, density, leaveSolToCatalogue: false);
        int kept = 0;
        int dropped = 0;
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            // Around the distance where the catalogue holds half of the level.
            double radius = CatalogueCompleteness.CompleteWithin(level);
            while (CatalogueCompleteness.Of(level, radius) > 0.5)
            {
                radius *= 1.02;
            }
            foreach (CellKey cell in CellsAround(level, new Vector3D(0, 0, radius), SectorGrid.CellSize(level)).Take(12))
            {
                Dictionary<SystemAddress, GalaxyStar> all = everything.Generate(cell).ToDictionary(s => s.Address);
                GalaxyStar[] survivors = suppressed.Generate(cell);
                foreach (GalaxyStar s in survivors)
                {
                    Assert.True(all.TryGetValue(s.Address, out GalaxyStar u), $"{s.Address} exists without suppression");
                    Assert.True(s.Position == u.Position && s.Class == u.Class && s.Physics == u.Physics
                        && s.Metallicity == u.Metallicity && s.Component == u.Component, $"{s.Address} is unchanged");
                }
                kept += survivors.Length;
                dropped += all.Count - survivors.Length;
            }
        }
        Fixtures.Report($"{kept} stars kept, {dropped} left to the catalogue");
        Assert.True(kept > 500 && dropped > 500, "the cells straddle the fade-in zone");
    }

    [Test]
    public static void StarCountProfileMatchesTheDensity()
    {
        var density = new DensityModel();
        var profile = new StarCountProfile();
        var rng = new Rng(3);
        Span<double> components = stackalloc double[DensityModel.ComponentCount];
        (Vector3D Centre, int Level, double Radius)[] cases =
        {
            (Vector3D.Zero, 7, 60),
            (Vector3D.Zero, 4, 600),
            (Vector3D.Zero, 1, 1800),
            (new Vector3D(500, 300, 9000), 5, 400),
            (GalaxyFrame.GalacticCentre, 7, 40),
            (GalaxyFrame.GalacticCentre + new Vector3D(0, 0, -2500), 2, 1500),
        };
        foreach ((Vector3D centre, int level, double radius) in cases)
        {
            profile.Compute(density, centre);
            // Reference: Monte Carlo integral of the level's density over the sphere.
            const int samples = 200_000;
            double sum = 0;
            for (int i = 0; i < samples; i++)
            {
                Vector3D p;
                do
                {
                    p = new Vector3D(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1));
                }
                while (p.LengthSquared > 1);
                density.Evaluate(centre + p * radius, components);
                for (int c = 0; c < DensityModel.ComponentCount; c++)
                {
                    sum += components[c] * StarClassTable.Share(DensityModel.PopulationOf((GalaxyComponent)c), level);
                }
            }
            double expected = sum / samples * 4.0 / 3.0 * Math.PI * radius * radius * radius;
            double estimate = profile.CountWithin(level, radius);
            Fixtures.Report($"L{level} within {radius} ly of ({centre.X:0}, {centre.Y:0}, {centre.Z:0}): profile {estimate:E3}, integral {expected:E3}");
            Assert.InRange(0.8 * expected, 1.25 * expected, estimate, $"level {level} count within {radius} ly");
            Assert.Near(radius, profile.RadiusFor(level, estimate), 1e-6 * radius, "RadiusFor inverts CountWithin");
        }
    }

    [Test]
    public static void UntrustedCatalogueAddressesAreRejected()
    {
        // Addresses arrive from the network: anything that doesn't name a catalogued system is simply not found.
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        int number = galaxy.Catalogue!.Number;
        SystemAddress[] bad =
        {
            SystemAddress.Catalogue(number + 1, 1), // another catalogue
            SystemAddress.Catalogue(number, (long)int.MaxValue + 1), // beyond any catalogue id
            SystemAddress.Catalogue(number, (1L << 32) + 1), // would truncate to id 1, a real system
            SystemAddress.Catalogue(number, (1L << 56) - 1),
            SystemAddress.Catalogue(number, 99_999_999), // no such star
        };
        foreach (SystemAddress address in bad)
        {
            Assert.True(!galaxy.TryGetStar(address, out _), $"{address} is not a system");
            Assert.True(galaxy.GetSystem(address) == null, $"{address} has no star system");
            Assert.True(galaxy.NameOf(address).StartsWith("Catalogue ", StringComparison.Ordinal), $"{address} has a placeholder name");
        }
        // A companion is a real catalogue star, but not a system of its own.
        CatalogueStar toliman = galaxy.Catalogue.FindByName("Alpha Centauri")!.Companions.First(c => c.Name == "Toliman");
        Assert.True(!galaxy.TryGetStar(SystemAddress.Catalogue(number, toliman.Id), out _), "a companion is not a system");
    }

    // ---------------------------------------------------------------- catalogue

    [Test]
    public static void CatalogueHasTheNeighbours()
    {
        StarCatalogue catalogue = Fixtures.Catalogue;
        CatalogueStar sol = catalogue.FindByName("Sol")!;
        Assert.Equal(0, sol.Id, "Sol's id");
        Assert.Near(0, sol.Position.Length, 1e-4, "Sol at the origin");
        Assert.Near(8.6, catalogue.FindByName("Sirius")!.Position.Length, 0.1, "Sirius distance");
        Assert.Near(4.24, catalogue.FindByName("Proxima Centauri")!.Position.Length, 0.05, "Proxima distance");
        Assert.Near(25.0, catalogue.FindByName("Vega")!.Position.Length, 0.4, "Vega distance");
        CatalogueStar alphaCen = catalogue.FindByName("Alpha Centauri")!;
        Assert.Equal("Alpha Centauri", alphaCen.SystemName, "Alpha Centauri system name");
        Assert.True(alphaCen.Companions.Any(c => c.Name == "Toliman"), "Toliman is Alpha Centauri's companion");
        Vector3D siriusDir = catalogue.FindByName("Sirius")!.Position.Normalized;
        Vector3D expected = GalaxyFrame.FromLongitudeLatitude(227.23, -8.89);
        Assert.Near(0, Vector3D.Distance(siriusDir, expected), 0.01, "Sirius lies at l=227°, b=−9°");
        Assert.True(catalogue.Search("siri", 10).Any(r => r.Name == "Sirius"), "prefix search finds Sirius");
        Assert.Equal(StarKind.Supergiant, catalogue.FindByName("Rigel")!.Class.Kind, "Rigel is a supergiant");
        Fixtures.Report($"{catalogue.Stars.Count} catalogue stars, {catalogue.Primaries.Count()} systems");
    }

    [Test]
    public static void SearchFindsCatalogueProceduralAndSectorNames()
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        Assert.True(galaxy.Search("Sol", 5).Any(r => r.Name == "Sol"), "finds Sol");
        CellKey.TryFromPosition(7, new Vector3D(200, 10, 300), out CellKey cell);
        GalaxyStar[] stars = galaxy.GenerateProcedural(cell);
        int tries = 0;
        while (stars.Length == 0 && tries++ < 50)
        {
            CellKey.TryFromGlobal(7, cell.GlobalX + 1, cell.GlobalY, cell.GlobalZ, out cell);
            stars = galaxy.GenerateProcedural(cell);
        }
        Assert.True(stars.Length > 0, "found a procedural star");
        string name = galaxy.NameOf(stars[0].Address);
        List<SearchResult> hits = galaxy.Search(name, 5);
        Assert.True(hits.Count > 0 && hits[0].Address == stars[0].Address, $"finds '{name}'");
        string sector = Galaxy.Galaxy.SectorNameAt(Vector3D.Zero);
        Assert.True(galaxy.Search(sector, 5).Any(r => r.IsSector), $"finds sector '{sector}'");
    }

    // ---------------------------------------------------------------- star systems

    [Test]
    public static void SolSystemIsReal()
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        StarSystem sol = galaxy.GetSystem(galaxy.Search("Sol", 1)[0].Address)!;
        Assert.Equal(9, sol.Count(BodyKind.Planet), "planets (with Pluto)");
        Body earth = sol.Bodies.First(b => b.Name == "Earth");
        Assert.Near(365.256, earth.Orbit.Period / Units.SecondsPerDay, 0.01, "Earth's year in days");
        Assert.Near(4332.6, sol.Bodies.First(b => b.Name == "Jupiter").Orbit.Period / Units.SecondsPerDay, 3, "Jupiter's year");
        Vector3D atJ2000 = sol.PositionOf(earth.Index, 0);
        Assert.Near(0.9833, atJ2000.Length / Units.MetresPerAu, 0.001, "Earth-Sun distance at J2000");
        double longitude = DMath.Atan2(-atJ2000.Z, atJ2000.X) * DMath.RadToDeg;
        Assert.Near(100.4, longitude < 0 ? longitude + 360 : longitude, 0.5, "Earth's heliocentric longitude at J2000");
        Assert.True(sol.Bodies.Any(b => b.Name == "Titan" && b.Parent == sol.Bodies.First(p => p.Name == "Saturn").Index), "Titan orbits Saturn");
        // Hand-written physical states (GALAXY.md §10.3).
        PlanetState Of(string name) => sol.Bodies.First(b => b.Name == name).State!;
        Assert.True(Of("Earth").Biosphere == Biosphere.Complex && Of("Earth").Hazards == Hazards.Quakes && Of("Io").Volcanism == Volcanism.Sulphur
            && Of("Europa").SubsurfaceOcean && Of("Titan").Liquid == LiquidKind.Hydrocarbons && Of("Jupiter").Surface == SurfaceKind.None
            && (Of("Venus").Hazards & Hazards.Corrosive) != 0, "Sol's bodies have their real states");
    }

    [Test]
    public static void GeneratedSystemsArePlausible()
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        int systems = 0;
        int planets = 0;
        int moons = 0;
        int earthLikes = 0;
        int hotGiants = 0;
        var types = new Dictionary<PlanetType, int>();
        var liquids = new Dictionary<LiquidKind, int>();
        int alive = 0;
        int subsurfaceOceans = 0;
        foreach (CellKey cell in CellsAround(6, new Vector3D(400, 0, 400), 120).Concat(CellsAround(4, new Vector3D(400, 0, 400), 300)))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                StarSystem system = galaxy.GetSystem(star.Address)!;
                systems++;
                var previous = new Dictionary<int, double>();
                foreach (Body b in system.Bodies)
                {
                    Assert.True(double.IsFinite(b.Mass) && double.IsFinite(b.Radius) && double.IsFinite(b.Temperature), $"finite values in {b.Name}");
                    if (b.Kind is BodyKind.Planet or BodyKind.Moon)
                    {
                        CheckState(b);
                        PlanetState s = b.State!;
                        liquids[s.Liquid] = liquids.GetValueOrDefault(s.Liquid) + 1;
                        alive += s.Biosphere != Biosphere.None ? 1 : 0;
                        subsurfaceOceans += s.SubsurfaceOcean ? 1 : 0;
                    }
                    if (b.Kind == BodyKind.Planet)
                    {
                        planets++;
                        types[b.PlanetType] = types.GetValueOrDefault(b.PlanetType) + 1;
                        earthLikes += b.PlanetType == PlanetType.EarthLike ? 1 : 0;
                        hotGiants += b.PlanetType == PlanetType.GasGiant && b.Temperature > 1000 ? 1 : 0;
                        Assert.True(b.Orbit.SemiMajorAxis > previous.GetValueOrDefault(b.Parent), $"{b.Name} orbits beyond the previous planet of its host");
                        previous[b.Parent] = b.Orbit.SemiMajorAxis;
                    }
                    if (b.Kind == BodyKind.Moon)
                    {
                        moons++;
                        Body planet = system.Bodies[b.Parent];
                        double hill = planet.Orbit.Periapsis * Math.Cbrt(planet.Mass / (3 * system.Bodies[planet.Parent].Mass));
                        Assert.True(b.Orbit.SemiMajorAxis < 0.5 * hill, $"{b.Name} inside its planet's Hill sphere");
                        Assert.True(b.Orbit.SemiMajorAxis > planet.Radius, $"{b.Name} outside its planet");
                    }
                }
            }
        }
        string mix = string.Join(", ", types.OrderByDescending(kv => kv.Value).Select(kv => $"{Body.PlanetTypeName(kv.Key)} {kv.Value}"));
        string wet = string.Join(", ", liquids.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
        Fixtures.Report($"{systems} systems, {planets} planets, {moons} moons, {earthLikes} Earth-like, {hotGiants} gas giants above 1,000 K; {mix}; " +
            $"liquids: {wet}; {alive} with life, {subsurfaceOceans} subsurface oceans");
        Assert.True(systems > 50 && planets > systems, "systems have planets");
        // Close-in hot worlds exist (GALAXY.md §10): metal-rich worlds and hot giants.
        Assert.True(types.GetValueOrDefault(PlanetType.MetalRich) > 0 && hotGiants > 0, "metal-rich worlds and hot giants exist");
        // The physical state makes the variety (GALAXY.md §10.3): oceans, lava, lakes, buried oceans and life.
        Assert.True(liquids.GetValueOrDefault(LiquidKind.Water) > 0 && liquids.GetValueOrDefault(LiquidKind.Magma) > 0
            && liquids.GetValueOrDefault(LiquidKind.Hydrocarbons) > 0 && subsurfaceOceans > 0 && alive > 0,
            "oceans, lava, hydrocarbon lakes, subsurface oceans and life occur");
    }

    // A body's labels follow from its physical state (GALAXY.md §10.3), and the state is self-consistent.
    private static void CheckState(Body b)
    {
        PlanetState? s = b.State;
        Assert.True(s != null, $"{b.Name} has a physical state");
        Composition c = s!.Composition;
        Assert.Near(1, c.Metal + c.Rock + c.Ice + c.Gas, 1e-3, $"{b.Name}: composition adds up");
        Gases g = s.Gases;
        double gases = g.Hydrogen + g.Helium + g.Nitrogen + g.Oxygen + g.CarbonDioxide + g.Methane + g.Water + g.Ammonia + g.SulphurDioxide + g.Argon;
        Assert.True(Math.Abs(gases - 1) < 1e-3 || (gases == 0 && b.SurfacePressure < 1e-9), $"{b.Name}: gases add up");
        bool giant = b.PlanetType is PlanetType.GasGiant or PlanetType.IceGiant or PlanetType.GasDwarf;
        Assert.True(giant == (s.Surface == SurfaceKind.None), $"{b.Name}: only giants and gas dwarfs have no surface");
        Assert.True(b.Landable == (s.Surface is SurfaceKind.Rock or SurfaceKind.Ice), $"{b.Name}: landable on rock or ice");
        Assert.True(giant || (s.MinTemperature <= b.Temperature + 1e-9 && b.Temperature <= s.MaxTemperature + 1e-9), $"{b.Name}: mean between night and day");
        Assert.True(giant || ((s.Hazards & Hazards.Vacuum) != 0) == (b.SurfacePressure < 0.001), $"{b.Name}: vacuum below 0.001 atm");
        Assert.True(b.PlanetType != PlanetType.EarthLike || (s.Biosphere >= Biosphere.Simple && s.Liquid == LiquidKind.Water && g.Oxygen >= 0.1),
            $"{b.Name}: Earth-like means oxygen from life over liquid water");
        Assert.True(s.Surface != SurfaceKind.Ocean || s.OceanCover >= 0.95, $"{b.Name}: an ocean surface covers it");
    }

    [Test]
    public static void WorldsMatchTheSolarSystem()
    {
        // The world builder (GALAXY.md §10.3) given the Solar System's inputs, over many random inventories: Earth
        // keeps a temperate climate with liquid water, Venus becomes a dry hothouse, Mars a cold desert with thin air,
        // the Moon airless, Titan a cold world that keeps its air.
        const double year = 365.25 * 86400;
        var sun = new WorldInput { HostMass = Units.SolarMass, Activity = 1.07, Age = 4.57, StableHost = true, Formation = Formation.Solid };
        var earth = sun with
        {
            MassEarth = 1, FormationZone = 0.37, Flux = 1, OrbitRadius = Units.MetresPerAu, OrbitPeriod = year,
            Eccentricity = 0.017, RotationPeriod = 86164,
        };
        var venus = sun with
        {
            MassEarth = 0.815, FormationZone = 0.27, Flux = 1.91, OrbitRadius = 0.723 * Units.MetresPerAu, OrbitPeriod = 0.615 * year,
            Eccentricity = 0.007, RotationPeriod = -2.1e7,
        };
        var mars = sun with
        {
            MassEarth = 0.107, FormationZone = 0.56, Flux = 0.43, OrbitRadius = 1.524 * Units.MetresPerAu, OrbitPeriod = 1.88 * year,
            Eccentricity = 0.093, RotationPeriod = 88643,
        };
        var moon = earth with
        {
            MassEarth = 0.0123, HostMass = Units.EarthMass, OrbitRadius = 3.844e8, OrbitPeriod = 27.32 * 86400, Eccentricity = 0.055,
            RotationPeriod = 27.32 * 86400, TidallyLocked = true, IsMoon = true, ParentRadius = Units.EarthRadius,
        };
        var titan = sun with
        {
            MassEarth = 0.0225, FormationZone = 3.5, Flux = 0.011, HostMass = 95.2 * Units.EarthMass, OrbitRadius = 1.222e9,
            OrbitPeriod = 15.95 * 86400, Eccentricity = 0.0288, RotationPeriod = 15.95 * 86400, TidallyLocked = true, IsMoon = true,
            ParentRadius = 5.82e7,
        };
        int temperate = 0, hothouse = 0, desert = 0, airless = 0, titanAir = 0;
        var misses = new List<string>();
        const int Seeds = 20;
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            World Make(WorldInput input, ulong salt)
            {
                var rng = new Rng(Hash64.Combine(seed, salt));
                return WorldBuilder.Build(input, ref rng);
            }
            World e = Make(earth, 1);
            bool isTemperate = e.State.Liquid == LiquidKind.Water && e.Temperature is > 270 and < 320 && e.Pressure is > 0.3 and < 8;
            temperate += isTemperate ? 1 : 0;
            World v = Make(venus, 2);
            bool isHothouse = v.Temperature > 500 && v.Pressure > 10 && v.State.Liquid == LiquidKind.None;
            hothouse += isHothouse ? 1 : 0;
            foreach ((string name, World w, bool ok) in new[] { ("Earth", e, isTemperate), ("Venus", v, isHothouse) })
            {
                if (!ok)
                {
                    misses.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{name} {w.Temperature:0} K {w.Pressure:0.###} atm {w.Atmosphere} {w.State.Liquid} metal {w.State.Composition.Metal:0.00} ice {w.State.Composition.Ice:0.0000}"));
                }
            }
            World m = Make(mars, 3);
            desert += m.Pressure < 0.1 && m.Temperature < 250 && m.State.Liquid == LiquidKind.None ? 1 : 0;
            World l = Make(moon, 4);
            airless += l.Pressure < 1e-6 ? 1 : 0;
            World t = Make(titan, 5);
            titanAir += t.Pressure > 0.05 && t.Temperature < 120 ? 1 : 0;
        }
        Fixtures.Report($"of {Seeds}: temperate Earths {temperate}, hothouse Venuses {hothouse}, desert Marses {desert}, airless Moons {airless}, Titans with air {titanAir}"
            + (misses.Count > 0 ? "; misses: " + string.Join("; ", misses.Take(8)) : ""));
        Assert.True(temperate >= 18 && hothouse >= 18 && desert >= 18 && airless == Seeds && titanAir >= 14,
            "the Solar System's inputs give the Solar System's worlds");
    }

    [Test]
    public static void OrbitsNeverCross()
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        var systems = new List<StarSystem>();
        foreach (CellKey cell in CellsAround(5, new Vector3D(400, 0, 400), 100).Concat(CellsAround(3, new Vector3D(-900, 30, 2000), 250)))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                systems.Add(galaxy.GetSystem(star.Address)!);
            }
        }
        // Real multiple stars: their companions come from the catalogue.
        foreach (string name in new[] { "Alpha Centauri", "Sirius", "Capella", "Castor", "Procyon", "Mizar", "Albireo" })
        {
            SearchResult hit = galaxy.Search(name, 1).FirstOrDefault();
            if (hit.Name != null && !hit.IsSector && galaxy.GetSystem(hit.Address) is StarSystem real)
            {
                systems.Add(real);
            }
        }
        int pairs = 0;
        int multiples = 0;
        foreach (StarSystem system in systems)
        {
            foreach (Body host in system.Bodies.Where(b => b.Kind is BodyKind.Star or BodyKind.Barycentre))
            {
                // Planets and belts of one host never overlap.
                List<Body> around = host.Children.Select(i => system.Bodies[i])
                    .Where(b => b.Kind is BodyKind.Planet or BodyKind.Belt)
                    .OrderBy(b => b.Orbit.SemiMajorAxis)
                    .ToList();
                for (int i = 1; i < around.Count; i++)
                {
                    Assert.True(OuterEdge(around[i - 1]) < InnerEdge(around[i]),
                        $"{system.Name}: {around[i - 1].Name} reaches {OuterEdge(around[i - 1]) / Units.MetresPerAu:0.###} AU, " +
                        $"past {around[i].Name} at {InnerEdge(around[i]) / Units.MetresPerAu:0.###} AU");
                    pairs++;
                }
                if (host.Kind != BodyKind.Barycentre)
                {
                    continue;
                }
                // A pair: its two members orbit on opposite sides, far enough apart; planets around both stay outside.
                multiples += host.Parent < 0 ? 1 : 0;
                List<Body> members = host.Children.Select(i => system.Bodies[i]).Where(b => b.Kind is BodyKind.Star or BodyKind.Barycentre).ToList();
                Assert.Equal(2, members.Count, $"{host.Name} has two members");
                double a = members[0].Orbit.SemiMajorAxis + members[1].Orbit.SemiMajorAxis;
                double e = members[0].Orbit.Eccentricity;
                Assert.Near(members[0].Mass * members[0].Orbit.SemiMajorAxis, members[1].Mass * members[1].Orbit.SemiMajorAxis,
                    1e-9 * members[0].Mass * a, $"{host.Name}: both members balance at the barycentre");
                // Two stars keep three times their radii apart; a star and an inner pair, three times the pair's width.
                double sizes = members.All(m => m.Kind == BodyKind.Star)
                    ? 3 * (members[0].Radius + members[1].Radius)
                    : 3 * members.Where(m => m.Kind == BodyKind.Barycentre).Max(m => InnerPairApoapsis(system, m));
                Assert.True(a * (1 - e) >= sizes * (1 - 1e-9), $"{host.Name}: the pair's members come too close");
                foreach (Body p in around)
                {
                    Assert.True(InnerEdge(p) >= 1.5 * a, $"{p.Name} orbits too close to the pair {host.Name}");
                }
                // Planets around one member stay well inside the pair's closest approach.
                foreach (Body member in members)
                {
                    foreach (Body p in member.Children.Select(i => system.Bodies[i]).Where(b => b.Kind is BodyKind.Planet or BodyKind.Belt))
                    {
                        Assert.True(OuterEdge(p) <= 0.5 * a * (1 - e) * (1 + 1e-9), $"{p.Name} strays too near {member.Name}'s companion");
                    }
                }
            }
        }
        Fixtures.Report($"{systems.Count} systems ({multiples} multiple), {pairs} neighbouring orbits checked");
        Assert.True(pairs > 200 && multiples > 20, "enough systems checked");
    }

    // How far the stars of an inner pair swing from its barycentre at most (their relative apoapsis).
    private static double InnerPairApoapsis(StarSystem system, Body pair)
    {
        List<Body> members = pair.Children.Select(i => system.Bodies[i]).Where(b => b.Kind is BodyKind.Star or BodyKind.Barycentre).ToList();
        return (members[0].Orbit.SemiMajorAxis + members[1].Orbit.SemiMajorAxis) * (1 + members[0].Orbit.Eccentricity);
    }

    [Test]
    public static void MultipleStarsFormABarycentricTree()
    {
        // GALAXY.md §10.1: barycentres keep the centre of mass fixed; planets orbit single stars and close pairs;
        // companions add their light; moons can hold atmospheres; every body has a unique, decodable path.
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        int multiple = 0;
        int triples = 0;
        int companionPlanets = 0;
        int circumbinary = 0;
        int moonAtmospheres = 0;
        int warmer = 0;
        foreach (CellKey cell in CellsAround(5, new Vector3D(600, 0, 600), 100))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                StarSystem system = galaxy.GetSystem(star.Address)!;
                var paths = new HashSet<uint>();
                foreach (Body b in system.Bodies)
                {
                    Assert.True(paths.Add(b.Path), $"{b.Name}: path 0x{b.Path:X8} is unique in its system");
                    Assert.True(system.Find(b.Path) == b, $"{b.Name} is found by its path");
                    int node = BodyPath.NodeOf(b.Path);
                    Body parent = b.Parent >= 0 ? system.Bodies[b.Parent] : b;
                    switch (b.Kind)
                    {
                        case BodyKind.Planet:
                        case BodyKind.Belt:
                            Assert.True(node == BodyPath.NodeOf(parent.Path) && BodyPath.SlotOf(b.Path) >= 0
                                && BodyPath.IsBelt(b.Path) == (b.Kind == BodyKind.Belt), $"{b.Name}'s path names its host and slot");
                            companionPlanets += b.Kind == BodyKind.Planet && parent.Kind == BodyKind.Star && parent.Index != system.PrimaryStar.Index ? 1 : 0;
                            circumbinary += b.Kind == BodyKind.Planet && parent.Kind == BodyKind.Barycentre ? 1 : 0;
                            break;
                        case BodyKind.Moon:
                            Assert.True(BodyPath.MoonOf(b.Path) >= 0 && (b.Path & 0xFFF00000u) == (parent.Path & 0xFFF00000u), $"{b.Name}'s path extends its planet's");
                            moonAtmospheres += b.Atmosphere != AtmosphereType.None ? 1 : 0;
                            break;
                    }
                }
                List<Body> stars = system.Bodies.Where(b => b.Kind == BodyKind.Star).ToList();
                if (stars.Count < 2)
                {
                    continue;
                }
                multiple++;
                triples += stars.Count >= 3 ? 1 : 0;
                // The stars' centre of mass stays at the origin (the root barycentre) at any time.
                foreach (double t in new[] { 0.0, 3.1e8, 1.7e10 })
                {
                    Vector3D weighted = Vector3D.Zero;
                    double total = 0;
                    foreach (Body s in stars)
                    {
                        weighted += system.PositionOf(s.Index, t) * s.Mass;
                        total += s.Mass;
                    }
                    double extent = stars.Max(s => system.PositionOf(s.Index, t).Length);
                    Assert.True((weighted / total).Length <= 1e-9 * extent + 1, $"{system.Name}: the centre of mass drifts");
                }
                // A planet around a star of a multiple system is at least as warm as its own star alone would make it
                // (with the planet's albedo).
                foreach (Body p in system.Bodies.Where(b => b.Kind == BodyKind.Planet && system.Bodies[b.Parent].Kind == BodyKind.Star))
                {
                    Body host = system.Bodies[p.Parent];
                    double au = p.Orbit.SemiMajorAxis / Units.MetresPerAu;
                    double alone = 278.6 * Math.Pow((1 - p.State!.Albedo) * host.Luminosity / (au * au), 0.25);
                    Assert.True(p.Temperature >= alone * (1 - 1e-9), $"{p.Name} gets its star's light");
                    warmer += p.Temperature > alone * 1.001 && p.Atmosphere == AtmosphereType.None ? 1 : 0;
                }
            }
        }
        Fixtures.Report($"{multiple} multiple systems ({triples} with three or more stars), {companionPlanets} planets around companions, " +
            $"{circumbinary} circumbinary planets, {warmer} airless planets warmed by a companion, {moonAtmospheres} moons with atmospheres");
        Assert.True(triples > 0 && companionPlanets > 0 && circumbinary > 0 && warmer > 0 && moonAtmospheres > 0,
            "triples, planets around companions and pairs, companion light and moon atmospheres all occur");
    }

    private static double InnerEdge(Body b) => b.Kind == BodyKind.Belt ? b.Orbit.SemiMajorAxis - b.BeltWidth / 2 : b.Orbit.Periapsis;

    private static double OuterEdge(Body b) => b.Kind == BodyKind.Belt ? b.Orbit.SemiMajorAxis + b.BeltWidth / 2 : b.Orbit.Apoapsis;

    [Test]
    public static void SystemGenerationIsDeterministic()
    {
        // Every stored field of every body (Golden.Of), so future surface inputs can't drift unnoticed.
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        ulong h = 0;
        int count = 0;
        foreach (CellKey cell in GoldenCells().Where(c => c.Level >= 4).Take(12))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell).Take(8))
            {
                StarSystem a = galaxy.GetSystem(star.Address)!;
                StarSystem b = galaxy.GetSystem(star.Address)!;
                ulong ha = Golden.Of(a);
                Assert.Equal(ha, Golden.Of(b), "same system twice");
                h = Hash64.Combine(h, ha);
                count++;
            }
        }
        Fixtures.Report($"{count} systems, hash 0x{h:X16}");
        Assert.Equal(SystemsGolden, h, "systems golden hash");
    }

    /// <summary>Real systems: Sol (hand data) and catalogue stars with generated planets and real companions.</summary>
    [Test]
    public static void RealSystemsMatchTheirGolden()
    {
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        ulong h = 0;
        var names = new List<string>();
        foreach (string name in RealGoldenSystems)
        {
            SearchResult hit = galaxy.Search(name, 1).FirstOrDefault();
            Assert.True(hit.Name != null && !hit.IsSector, $"'{name}' is a real system");
            StarSystem system = galaxy.GetSystem(hit.Address)!;
            h = Hash64.Combine(h, Golden.Of(system));
            names.Add($"{system.Name} ({system.Bodies.Count})");
        }
        Fixtures.Report($"{string.Join(", ", names)}: hash 0x{h:X16}");
        Assert.Equal(RealSystemsGolden, h, "real systems golden hash");
    }

    private static readonly string[] RealGoldenSystems =
    {
        "Sol", "Alpha Centauri", "Proxima Centauri", "Barnard's Star", "Sirius", "Procyon", "Tau Ceti", "Epsilon Eridani",
        "61 Cygni", "Vega", "Capella", "Castor", "Mizar", "Albireo", "Polaris", "Betelgeuse", "Rigel",
    };

    [Test]
    public static void VersionsSeedTheirStage()
    {
        // A stage's version is mixed into its seed: another galaxy version moves the stars, another system version
        // rebuilds the systems, and neither touches the other stage.
        var density = new DensityModel();
        ulong seed = Galaxy.Galaxy.DefaultSeed;
        CellKey.TryFromPosition(5, new Vector3D(300, -40, 900), out CellKey cell);
        GalaxyStar[] current = new CellGenerator(seed, density).Generate(cell);
        GalaxyStar[] other = new CellGenerator(seed, density, leaveSolToCatalogue: true, GeneratorVersion.Galaxy + 1).Generate(cell);
        Assert.True(current.Length > 0 && HashStars(current) != HashStars(other), "a new galaxy version changes the stars");
        string name = SystemNames.Format(current[0].Address);
        StarSystem a = new SystemGenerator(seed).Generate(current[0], name);
        StarSystem b = new SystemGenerator(seed, GeneratorVersion.System + 1).Generate(current[0], name);
        Assert.True(Golden.Of(a) != Golden.Of(b), "a new system version changes the system");
        Assert.True(GeneratorVersion.GalaxySeed(seed) != GeneratorVersion.SystemSeed(seed), "stages have their own seeds");
    }

    private static ulong HashStars(GalaxyStar[] stars)
    {
        ulong h = (ulong)stars.Length;
        foreach (GalaxyStar s in stars)
        {
            h = Hash64.Combine(h, s.Address.Value);
            h = Hash64.Combine(h, Bits(s.Position.X));
            h = Hash64.Combine(h, Bits(s.Position.Y));
            h = Hash64.Combine(h, Bits(s.Position.Z));
            h = Hash64.Combine(h, (ulong)s.Class.Kind << 32 | BitConverter.SingleToUInt32Bits(s.Class.Code));
            h = Hash64.Combine(h, BitConverter.SingleToUInt32Bits(s.Physics.AbsoluteMagnitude));
            h = Hash64.Combine(h, BitConverter.SingleToUInt32Bits(s.Physics.Temperature));
            h = Hash64.Combine(h, BitConverter.SingleToUInt32Bits(s.Metallicity));
        }
        return h;
    }

    private static ulong Bits(double d) => (ulong)BitConverter.DoubleToInt64Bits(d);
}
