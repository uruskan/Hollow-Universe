using System;
using System.Collections.Generic;
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
    private const ulong CellsGolden = 0x22954A90D19DDA8EUL;
    private const ulong SystemsGolden = 0x5F09B25668332B31UL;
    private const ulong RealSystemsGolden = 0xD0DADDB204F53B3BUL;

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
        // cells 1,024 ly tall. Count the stars within 165 ly of the midplane and compare with the density's share of
        // each cell (a uniform spread inside the cells would give about 15% fewer).
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        double observed = 0;
        double expected = 0;
        double uniform = 0;
        int stars = 0;
        for (int i = 0; i < 36; i++)
        {
            // Along a ring around the Galactic Centre, well away from Sol's catalogue region.
            double angle = i * 0.17;
            Vector3D model = new(DMath.Sin(angle) * 18000, 0, -DMath.Cos(angle) * 18000);
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
    public static void ClassMixMatchesTheNeighbourhood()
    {
        // Count primary classes in a shell 100–400 ly from Sol (outside the catalogue's complete region for faint stars).
        Galaxy.Galaxy galaxy = Fixtures.Galaxy;
        var counts = new Dictionary<char, double>();
        double total = 0;
        var centre = new Vector3D(0, 0, 0);
        for (int level = 3; level < StarClassTable.LevelCount; level++)
        {
            double size = SectorGrid.CellSize(level);
            // Sample a handful of cells per level and weight by cell volume share.
            foreach (CellKey cell in CellsAround(level, new Vector3D(250, 0, 0), size * 1.5).Take(27))
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
            double radius = CellGenerator.CatalogueRadius[level];
            // Cells across the edge of that region, where generated stars fade back in.
            foreach (CellKey cell in CellsAround(level, new Vector3D(0, 0, radius * 1.1), SectorGrid.CellSize(level)).Take(12))
            {
                GalaxyStar[] a = with.GenerateProcedural(cell);
                Assert.Equal(HashStars(a), HashStars(without.GenerateProcedural(cell)), $"level {level} cell with and without the catalogue");
                foreach (GalaxyStar s in a)
                {
                    Assert.True(s.Position.Length >= radius,
                        $"generated star {s.Address} at {s.Position.Length:0.0} ly is inside the catalogue's {radius} ly");
                }
                compared += a.Length;
            }
        }
        Fixtures.Report($"{compared} generated stars around the catalogue's edge compared");
        Assert.True(compared > 1000, "enough stars compared");
    }

    [Test]
    public static void SuppressionLeavesSurvivingStarsUnchanged()
    {
        // Where generated stars fade back in near the catalogue's radius, the ones that survive must be exactly
        // the stars an unsuppressed generator makes: same addresses, positions, classes and physics.
        var density = new DensityModel();
        var suppressed = new CellGenerator(Galaxy.Galaxy.DefaultSeed, density);
        var everything = new CellGenerator(Galaxy.Galaxy.DefaultSeed, density, leaveSolToCatalogue: false);
        int kept = 0;
        int dropped = 0;
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            double radius = CellGenerator.CatalogueRadius[level];
            foreach (CellKey cell in CellsAround(level, new Vector3D(0, 0, radius * 1.12), SectorGrid.CellSize(level)).Take(12))
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
        foreach (CellKey cell in CellsAround(6, new Vector3D(400, 0, 400), 120).Concat(CellsAround(4, new Vector3D(400, 0, 400), 300)))
        {
            foreach (GalaxyStar star in galaxy.GenerateProcedural(cell))
            {
                StarSystem system = galaxy.GetSystem(star.Address)!;
                systems++;
                double previous = 0;
                foreach (Body b in system.Bodies)
                {
                    Assert.True(double.IsFinite(b.Mass) && double.IsFinite(b.Radius) && double.IsFinite(b.Temperature), $"finite values in {b.Name}");
                    if (b.Kind == BodyKind.Planet)
                    {
                        planets++;
                        types[b.PlanetType] = types.GetValueOrDefault(b.PlanetType) + 1;
                        earthLikes += b.PlanetType == PlanetType.EarthLike ? 1 : 0;
                        hotGiants += b.PlanetType == PlanetType.GasGiant && b.Temperature > 1000 ? 1 : 0;
                        Assert.True(b.Orbit.SemiMajorAxis > previous, $"{b.Name} orbits beyond the previous planet");
                        previous = b.Orbit.SemiMajorAxis;
                    }
                    if (b.Kind == BodyKind.Moon)
                    {
                        moons++;
                        Body planet = system.Bodies[b.Parent];
                        double hill = planet.Orbit.Periapsis * Math.Cbrt(planet.Mass / (3 * system.Root.Mass));
                        Assert.True(b.Orbit.SemiMajorAxis < 0.5 * hill, $"{b.Name} inside its planet's Hill sphere");
                        Assert.True(b.Orbit.SemiMajorAxis > planet.Radius, $"{b.Name} outside its planet");
                    }
                }
            }
        }
        string mix = string.Join(", ", types.OrderByDescending(kv => kv.Value).Select(kv => $"{Body.PlanetTypeName(kv.Key)} {kv.Value}"));
        Fixtures.Report($"{systems} systems, {planets} planets, {moons} moons, {earthLikes} Earth-like, {hotGiants} gas giants above 1,000 K; {mix}");
        Assert.True(systems > 50 && planets > systems, "systems have planets");
        // Close-in hot worlds exist (GALAXY.md §10): metal-rich worlds and hot giants.
        Assert.True(types.GetValueOrDefault(PlanetType.MetalRich) > 0 && hotGiants > 0, "metal-rich worlds and hot giants exist");
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
            Body root = system.Root;
            List<Body> around = system.Bodies
                .Where(b => b.Parent == root.Index && b.Kind is BodyKind.Planet or BodyKind.Belt)
                .OrderBy(b => b.Orbit.SemiMajorAxis)
                .ToList();
            for (int i = 1; i < around.Count; i++)
            {
                Assert.True(OuterEdge(around[i - 1]) < InnerEdge(around[i]),
                    $"{system.Name}: {around[i - 1].Name} reaches {OuterEdge(around[i - 1]) / Units.MetresPerAu:0.###} AU, " +
                    $"past {around[i].Name} at {InnerEdge(around[i]) / Units.MetresPerAu:0.###} AU");
                pairs++;
            }
            List<Body> stars = system.Bodies.Where(b => b.Kind == BodyKind.Star && b.Parent == root.Index)
                .OrderBy(b => b.Orbit.SemiMajorAxis).ToList();
            multiples += stars.Count > 0 ? 1 : 0;
            for (int i = 0; i < stars.Count; i++)
            {
                Body star = stars[i];
                Assert.True(star.Orbit.Periapsis >= 3 * (root.Radius + star.Radius) * (1 - 1e-9),
                    $"{star.Name} comes within three radii of {root.Name}");
                if (i > 0)
                {
                    Assert.True(star.Orbit.Periapsis >= 3 * stars[i - 1].Orbit.Apoapsis * (1 - 1e-9),
                        $"{star.Name}'s orbit is too close to {stars[i - 1].Name}'s");
                }
                bool close = star.Orbit.Apoapsis < 5 * Units.MetresPerAu;
                foreach (Body p in around)
                {
                    Assert.True(close
                            ? InnerEdge(p) >= 3 * star.Orbit.Apoapsis * (1 - 1e-9)
                            : OuterEdge(p) <= star.Orbit.Periapsis / 3 * (1 + 1e-9),
                        $"{p.Name} strays too near {star.Name}");
                }
            }
        }
        Fixtures.Report($"{systems.Count} systems ({multiples} multiple), {pairs} neighbouring orbits checked");
        Assert.True(pairs > 200 && multiples > 20, "enough systems checked");
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
