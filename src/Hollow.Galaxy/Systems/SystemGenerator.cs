using System;
using System.Collections.Generic;
using System.Globalization;
using Hollow.Core;

namespace Hollow.Galaxy.Systems;

/// <summary>
/// Generates a star system's companion stars, planets, moons, rings and belts from its primary star
/// (GALAXY.md §10). A pure function of (seed, address): the same system on every machine.
/// Every object draws from its own random stream (ARCHITECTURE.md §8.3): the layout of the system, each
/// companion, each orbit slot and each moon. Changing how one moon is generated never shifts a planet.
/// </summary>
public sealed class SystemGenerator
{
    private const double Au = Units.MetresPerAu;
    private const double AuPerLightYear = Units.MetresPerLightYear / Units.MetresPerAu;
    private const double AuPerSolarRadius = Units.SolarRadius / Units.MetresPerAu;
    private const int MaxPlanets = 14;
    private const int MaxSlots = 40;

    // Widest rolled companion orbit (semi-major axis), and the farthest any rolled companion may swing out
    // (0.63 ly; at most 0.85 eccentricity keeps the widest orbit inside it). Bound pairs much wider than that are
    // rare and fragile.
    private const double MaxCompanionSeparationAu = 20000;
    private const double MaxCompanionApoapsisAu = 40000;

    // Stream tags inside a system.
    private const ulong LayoutStream = 1;
    private const ulong PrimaryStream = 2;
    private const ulong CompanionStream = 0x100;
    private const ulong SlotStream = 0x10000;
    private const ulong MoonStream = 0x1000000;

    private readonly ulong _seed;

    /// <summary>A system generator for a universe seed, at the current system-stage version.</summary>
    public SystemGenerator(ulong seed)
        : this(seed, GeneratorVersion.System)
    {
    }

    /// <summary>Test seam: <paramref name="version"/> stands in for another system-stage version.</summary>
    internal SystemGenerator(ulong seed, int version)
    {
        _seed = GeneratorVersion.SystemSeed(seed, version);
    }

    private readonly record struct Companion(StarClass Class, StarPhysics Physics, double SeparationAu, double Eccentricity);

    private sealed class Builder
    {
        public readonly StarSystem System;
        public Builder(StarSystem system) => System = system;

        public Body Add(Body body)
        {
            System.Bodies.Add(body);
            if (body.Parent >= 0)
            {
                System.Bodies[body.Parent].Children.Add(body.Index);
            }
            return body;
        }

        public int Next => System.Bodies.Count;
    }

    /// <summary>
    /// Generates the system of <paramref name="primary"/>. <paramref name="knownCompanions"/> are real companion
    /// stars from the catalogue (if any); procedural systems roll their own.
    /// </summary>
    public StarSystem Generate(GalaxyStar primary, string name, IReadOnlyList<GalaxyStar>? knownCompanions = null)
    {
        ulong key = Hash64.Of(_seed, Streams.System, primary.Address.Value);
        var layout = new Rng(Hash64.Combine(key, LayoutStream));
        var own = new Rng(Hash64.Combine(key, PrimaryStream));
        var system = new StarSystem { Address = primary.Address, Name = name, Primary = primary };
        var b = new Builder(system);

        double primaryMassSun = Math.Max(primary.Physics.Mass, 0.01);
        double primaryRadiusAu = Math.Max(primary.Physics.Radius, 1e-6) * AuPerSolarRadius;
        List<Companion> companions = knownCompanions != null
            ? CompanionsFromCatalogue(primary, knownCompanions, key)
            : RollCompanions(primary, key, ref layout);
        bool multiple = companions.Count > 0;

        b.Add(new Body
        {
            Index = 0,
            Parent = -1,
            Kind = BodyKind.Star,
            Name = multiple ? name + " A" : name,
            Designation = "A",
            Path = 0,
            StarClass = primary.Class,
            Mass = primaryMassSun * Units.SolarMass,
            Radius = Math.Max(primary.Physics.Radius, 1e-6) * Units.SolarRadius,
            Temperature = primary.Physics.Temperature,
            Luminosity = primary.Physics.Luminosity,
            RotationPeriod = own.Range(5, 40) * Units.SecondsPerDay,
            AxialTilt = own.Range(0, 0.3),
        });

        for (int i = 0; i < companions.Count; i++)
        {
            Companion c = companions[i];
            var rng = new Rng(Hash64.Combine(key, CompanionStream + (ulong)i));
            double massKg = Math.Max(c.Physics.Mass, 0.01) * Units.SolarMass;
            double a = c.SeparationAu * Au;
            string letter = ((char)('B' + i)).ToString();
            b.Add(new Body
            {
                Index = b.Next,
                Parent = 0,
                Kind = BodyKind.Star,
                Name = name + " " + letter,
                Designation = letter,
                Path = (uint)(i + 1) << 24,
                StarClass = c.Class,
                Mass = massKg,
                Radius = Math.Max(c.Physics.Radius, 1e-6) * Units.SolarRadius,
                Temperature = c.Physics.Temperature,
                Luminosity = c.Physics.Luminosity,
                RotationPeriod = rng.Range(5, 40) * Units.SecondsPerDay,
                Orbit = new Orbit(a, c.Eccentricity, rng.Range(0, 0.6),
                    rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi),
                    Orbit.PeriodFor(a, primaryMassSun * Units.SolarMass + massKg)),
            });
        }

        string planetPrefix = multiple ? name + " A" : name;
        GeneratePlanets(b, primary, primaryMassSun, primaryRadiusAu, companions, planetPrefix, key, ref layout);
        return system;
    }

    // Separation floor: periapsis at least three times the sum of the two stars' radii.
    private static double MinimumSeparationAu(StarPhysics a, StarPhysics b, double eccentricity) =>
        3.0 * (Math.Max(a.Radius, 1e-6) + Math.Max(b.Radius, 1e-6)) * AuPerSolarRadius / (1.0 - eccentricity);

    private static List<Companion> CompanionsFromCatalogue(GalaxyStar primary, IReadOnlyList<GalaxyStar> known, ulong key)
    {
        var list = new List<Companion>(known.Count);
        for (int i = 0; i < known.Count; i++)
        {
            GalaxyStar c = known[i];
            var rng = new Rng(Hash64.Combine(key, CompanionStream + 0x80 + (ulong)i));
            double eccentricity = StellarTables.Clamp(rng.Rayleigh(0.3), 0, 0.8);
            double separation = Vector3D.Distance(c.Position, primary.Position) * AuPerLightYear;
            if (separation < 1)
            {
                separation = rng.LogNormal(40, 0.8);
            }
            separation = Math.Max(StellarTables.Clamp(separation, 0.5, 20000), MinimumSeparationAu(primary.Physics, c.Physics, eccentricity));
            list.Add(new Companion(c.Class, c.Physics, separation, eccentricity));
        }
        MakeHierarchical(list, dropTooWide: false);
        return list;
    }

    // Multiple stars are stable only as a hierarchy: sorts the companions outward and moves each one out until its
    // closest approach is at least three times the previous companion's farthest point. Rolled companions that
    // would end up swinging out beyond MaxCompanionApoapsisAu are dropped instead; real ones are always kept.
    private static void MakeHierarchical(List<Companion> list, bool dropTooWide)
    {
        list.Sort((x, y) => x.SeparationAu.CompareTo(y.SeparationAu));
        for (int i = 1; i < list.Count; i++)
        {
            Companion inner = list[i - 1];
            Companion c = list[i];
            double minimum = 3 * inner.SeparationAu * (1 + inner.Eccentricity) / (1 - c.Eccentricity);
            if (c.SeparationAu < minimum)
            {
                if (dropTooWide && minimum * (1 + c.Eccentricity) > MaxCompanionApoapsisAu)
                {
                    list.RemoveRange(i, list.Count - i);
                    return;
                }
                list[i] = c with { SeparationAu = minimum };
            }
        }
    }

    private static List<Companion> RollCompanions(GalaxyStar primary, ulong key, ref Rng layout)
    {
        var list = new List<Companion>();
        double mass = primary.Physics.Mass;
        StarKind kind = primary.Class.Kind;
        double chance = mass < 0.08 ? 0.15 : mass < 0.5 ? 0.27 : mass < 1.3 ? 0.45 : mass < 3 ? 0.55 : 0.75;
        if (kind is StarKind.BlackHole or StarKind.NeutronStar or StarKind.WolfRayet)
        {
            chance = 0.2;
        }
        if (!layout.Chance(chance))
        {
            return list;
        }
        int count = layout.Chance(0.15) ? 2 : 1;
        // Companions were born with the primary: the mass ratio applies to its initial mass, and the system's age
        // decides what each companion is now (GALAXY.md §4.1).
        double initialMass = StellarEvolution.InitialMass(kind, mass);
        for (int i = 0; i < count; i++)
        {
            var rng = new Rng(Hash64.Combine(key, CompanionStream + (ulong)i + 0x40));
            double companionMass = Math.Max(0.013, initialMass * rng.Range(0.1, 1.0));
            // Sometimes the companion was born the heavier star and has already died: a white dwarf (or neutron star)
            // fainter than the primary now, like Sirius B. If it would still shine, it would be the primary.
            double heavier = initialMass * rng.Range(1.0, 3.0);
            if (rng.Chance(0.1) && StellarEvolution.MainSequenceLifetime(heavier) * 1.08 < primary.Age)
            {
                companionMass = heavier;
            }
            // A giant companion would outshine anything but another giant.
            bool giantPrimary = kind is StarKind.Subgiant or StarKind.Giant or StarKind.BrightGiant or StarKind.Supergiant or StarKind.CarbonStar;
            (StarClass cls, StarPhysics phys) = EvolvedCompanion(companionMass, primary.Age, giantPrimary, ref rng);
            double eccentricity = StellarTables.Clamp(rng.Rayleigh(0.3), 0, 0.85);
            double separation = StellarTables.Clamp(rng.LogNormal(50 * DMath.Pow(Math.Max(mass, 0.05), 0.3), 1.5), 0.05, MaxCompanionSeparationAu);
            separation = Math.Max(separation, MinimumSeparationAu(primary.Physics, phys, eccentricity));
            list.Add(new Companion(cls, phys, separation, eccentricity));
        }
        MakeHierarchical(list, dropTooWide: true);
        return list;
    }

    // What a star born with this mass is at this age (Gyr): a brown dwarf cooling since birth, a main-sequence star, a
    // giant just past its main sequence, or the white dwarf, neutron star or black hole it left.
    private static (StarClass Class, StarPhysics Physics) EvolvedCompanion(double initialMass, double age, bool allowGiant, ref Rng rng)
    {
        if (initialMass < 0.075)
        {
            double teff = StellarEvolution.BrownDwarfTemperature(initialMass, age);
            double code = Math.Floor(StellarTables.MainSequenceCodeForTemperature(teff) * 2) / 2;
            (_, double mv, _, double r) = StellarTables.Typical(StarKind.MainSequence, code);
            return (new StarClass(StarKind.BrownDwarf, (float)code),
                new StarPhysics((float)teff, (float)mv, (float)initialMass, (float)r, (float)StellarTables.Luminosity(r, teff)));
        }
        double lifetime = StellarEvolution.MainSequenceLifetime(initialMass);
        if (age < lifetime)
        {
            double code = Math.Floor(CodeForMass(initialMass) * 2) / 2;
            (double teff, double mv, double m, double r) = StellarTables.Typical(StarKind.MainSequence, code);
            return (new StarClass(StarKind.MainSequence, (float)code),
                new StarPhysics((float)teff, (float)mv, (float)m, (float)r, (float)StellarTables.Luminosity(r, teff)));
        }
        if (initialMass < 8 && age < lifetime * 1.08 && allowGiant)
        {
            const double giantCode = 50; // K0 III: the red giant branch and clump
            (double teff, double mv, _, double r) = StellarTables.Typical(StarKind.Giant, giantCode);
            return (new StarClass(StarKind.Giant, (float)giantCode),
                new StarPhysics((float)teff, (float)mv, (float)initialMass, (float)r, (float)StellarTables.Luminosity(r, teff)));
        }
        if (initialMass < 8)
        {
            double wdMass = StellarEvolution.WhiteDwarfMass(initialMass);
            double teff = StellarEvolution.WhiteDwarfTemperature(age - lifetime, wdMass);
            double radius = 0.0126 * DMath.Pow(wdMass / 0.6, -1.0 / 3.0);
            return (new StarClass(StarKind.WhiteDwarf, (float)(Math.Floor(50400.0 / teff * 2) / 2)),
                new StarPhysics((float)teff, (float)StellarTables.WhiteDwarfMagnitude(teff), (float)wdMass,
                    (float)radius, (float)StellarTables.Luminosity(radius, teff)));
        }
        if (initialMass < 20)
        {
            double teff = StellarEvolution.NeutronStarTemperature(age - lifetime);
            double nsMass = StellarTables.Clamp(rng.Gaussian(1.4, 0.08), 1.1, 2.1);
            return (new StarClass(StarKind.NeutronStar, 0),
                new StarPhysics((float)teff, 30f, (float)nsMass, 1.725e-5f, (float)StellarTables.Luminosity(1.725e-5, teff)));
        }
        double bhMass = initialMass * rng.Range(0.2, 0.4);
        return (new StarClass(StarKind.BlackHole, 0), new StarPhysics(0f, 99f, (float)bhMass, (float)(4.24e-6 * bhMass), 0f));
    }

    // Main-sequence spectral code of a star of the given mass (solar masses).
    private static double CodeForMass(double mass)
    {
        for (double code = 3; code <= 92; code += 0.5)
        {
            if (StellarTables.Typical(StarKind.MainSequence, code).Mass <= mass)
            {
                return code;
            }
        }
        return 92;
    }

    private static double PlanetRate(GalaxyStar star)
    {
        StarKind kind = star.Class.Kind;
        char letter = star.Class.Letter;
        return kind switch
        {
            StarKind.WhiteDwarf => 0.5,
            StarKind.BrownDwarf => 0.8,
            StarKind.NeutronStar => 0.2,
            StarKind.BlackHole => 0.1,
            StarKind.WolfRayet => 0.2,
            StarKind.Supergiant => 0.5,
            StarKind.Giant or StarKind.BrightGiant or StarKind.Subgiant => 1.5,
            StarKind.CarbonStar => 1.0,
            StarKind.TTauri => 3.0,
            StarKind.HerbigAeBe => 2.0,
            _ => letter switch
            {
                'O' => 0.3,
                'B' => 1.0,
                'A' => 2.5,
                'F' => 4.0,
                'G' => 4.5,
                'K' => 3.5,
                'M' => 3.0,
                _ => 0.8,
            },
        };
    }

    private static void GeneratePlanets(Builder b, GalaxyStar star, double starMassSun, double starRadiusAu,
        List<Companion> companions, string prefix, ulong key, ref Rng layout)
    {
        double luminosity = Math.Max(star.Physics.Luminosity, 1e-7);
        double sqrtL = Math.Sqrt(luminosity);
        double metallicity = star.Metallicity;
        double metalFactor = DMath.Pow10(0.3 * metallicity);
        double giantFactor = DMath.Pow10(2.0 * metallicity);
        double massFactor = StellarTables.Clamp(starMassSun, 0.3, 2.0);

        // The planet zone: outside the star (and any close companion), inside a third of the nearest approach of
        // every wide companion. The inner edge sits where the equilibrium temperature reaches about 1,600 K, so
        // close-in hot worlds (metal-rich worlds, hot giants) exist; real ones orbit even closer.
        double inner = Math.Max(Math.Max(0.01, 0.03 * sqrtL), 3 * starRadiusAu);
        if (star.Class.Kind is StarKind.NeutronStar or StarKind.BlackHole)
        {
            inner = 0.1;
        }
        double outer = Math.Max(2.0, 50.0 * Math.Sqrt(starMassSun));
        double centralMass = starMassSun;
        foreach (Companion c in companions)
        {
            double periapsis = c.SeparationAu * (1 - c.Eccentricity);
            double apoapsis = c.SeparationAu * (1 + c.Eccentricity);
            if (apoapsis < 5)
            {
                inner = Math.Max(inner, 3 * apoapsis); // circumbinary planets
                centralMass += c.Physics.Mass;
            }
            else
            {
                outer = Math.Min(outer, periapsis / 3);
            }
        }
        if (outer <= inner * 1.2)
        {
            return;
        }

        var slots = new List<double>();
        double a = inner * layout.Range(1.0, 1.6);
        while (a < outer && slots.Count < MaxSlots)
        {
            slots.Add(a);
            a *= layout.Range(1.4, 2.4);
        }
        int count = (int)Math.Min(Math.Min(layout.Poisson(PlanetRate(star) * metalFactor), slots.Count), MaxPlanets);

        // Choose which slots hold planets; slot indices (not planet numbers) identify bodies permanently. Slots are
        // equally likely beyond about ten-day orbits (0.1 AU·√L); closer in, real planets get rarer fast (occurrence
        // per log period falls roughly as P²), so those slots are weighted down by (a / 0.1 AU·√L)³. A weighted random
        // subset: each slot gets the key ln(u) / weight and the largest keys win.
        double hotEdge = 0.1 * sqrtL;
        var keys = new double[slots.Count];
        var order = new int[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            double ratio = Math.Min(1.0, slots[i] / hotEdge);
            double weight = Math.Max(ratio * ratio * ratio, 1e-6);
            keys[i] = DMath.Log(layout.NextDoubleNonZero()) / weight;
            order[i] = i;
        }
        Array.Sort(order, (x, y) => keys[x] != keys[y] ? keys[y].CompareTo(keys[x]) : x.CompareTo(y));
        var chosen = new List<int>(order[..count]);
        chosen.Sort();
        bool wantBelt = layout.Chance(0.3);
        double trailingBelt = layout.Range(1.5, 2.5);

        double snowLine = 2.7 * sqrtL;
        bool earthLikeHost = star.Class.Kind == StarKind.MainSequence && star.Class.Letter is 'F' or 'G' or 'K';
        bool mDwarf = star.Class.Kind == StarKind.MainSequence && star.Class.Letter == 'M';
        double centralMassKg = centralMass * Units.SolarMass;
        int planetNumber = 0;
        bool beltPlaced = !wantBelt;
        double previousApoapsis = inner;

        for (int k = 0; k < chosen.Count; k++)
        {
            int slot = chosen[k];
            double semiMajor = slots[slot];
            // Stay well inside the next body's orbit (or the edge of the zone).
            double apoapsisLimit = Math.Min(outer, k + 1 < chosen.Count ? slots[chosen[k + 1]] / 1.3 : outer);
            var rng = new Rng(Hash64.Combine(key, SlotStream + (ulong)slot));
            double teq = 278.6 * DMath.Pow(luminosity, 0.25) / Math.Sqrt(semiMajor);

            // A belt takes the place of one planet, preferably near the snow line, if there's room for it.
            if (!beltPlaced && semiMajor > 0.5 * snowLine && semiMajor > 1.2 * previousApoapsis)
            {
                beltPlaced = true;
                previousApoapsis = AddBelt(b, prefix, slot, semiMajor, centralMassKg, 1.05 * previousApoapsis, apoapsisLimit, ref rng);
                continue;
            }

            planetNumber++;
            PlanetType type = ChoosePlanetType(teq, semiMajor, snowLine, giantFactor, massFactor, earthLikeHost, mDwarf, ref rng);
            double apoapsis = AddPlanet(b, prefix, planetNumber, slot, type, semiMajor, teq, centralMassKg,
                previousApoapsis, apoapsisLimit, key, ref rng);
            previousApoapsis = apoapsis;
        }
        if (!beltPlaced && chosen.Count > 0)
        {
            double beltA = Math.Min(slots[chosen[^1]] * trailingBelt, outer * 0.9);
            if (beltA > previousApoapsis * 1.3)
            {
                var rng = new Rng(Hash64.Combine(key, SlotStream + MaxSlots));
                AddBelt(b, prefix, MaxSlots, beltA, centralMassKg, 1.05 * previousApoapsis, outer, ref rng);
            }
        }
    }

    private static PlanetType ChoosePlanetType(double teq, double a, double snowLine, double giantFactor,
        double massFactor, bool earthLikeHost, bool mDwarf, ref Rng rng)
    {
        if (teq > 1000)
        {
            if (rng.Chance(StellarTables.Clamp(0.05 * giantFactor, 0, 0.3)))
            {
                return PlanetType.GasGiant;
            }
            return teq > 1300 || rng.Chance(0.4) ? PlanetType.MetalRich : PlanetType.Rocky;
        }
        if (a < snowLine)
        {
            if (rng.Chance(StellarTables.Clamp(0.08 * giantFactor * massFactor, 0, 0.5)))
            {
                return PlanetType.GasGiant;
            }
            if (teq > 230 && teq < 310)
            {
                double earthChance = earthLikeHost ? 0.04 : mDwarf ? 0.006 : 0.0;
                if (rng.Chance(earthChance))
                {
                    return PlanetType.EarthLike;
                }
            }
            if (teq > 200 && teq < 400 && rng.Chance(0.08))
            {
                return PlanetType.WaterWorld;
            }
            return teq > 150 ? PlanetType.Rocky : PlanetType.RockyIce;
        }
        double roll = rng.NextDouble();
        double pGas = StellarTables.Clamp(0.3 * giantFactor * massFactor, 0.05, 0.7);
        if (roll < pGas)
        {
            return PlanetType.GasGiant;
        }
        roll -= pGas;
        if (roll < 0.25)
        {
            return PlanetType.IceGiant;
        }
        roll -= 0.25;
        if (roll < 0.03)
        {
            return PlanetType.AmmoniaWorld;
        }
        return rng.Chance(0.5) ? PlanetType.Icy : PlanetType.RockyIce;
    }

    private static double MassForType(PlanetType type, ref Rng rng) => type switch
    {
        PlanetType.EarthLike => rng.LogUniform(0.5, 3),
        PlanetType.WaterWorld => rng.LogUniform(0.8, 8),
        PlanetType.Icy => rng.LogUniform(0.01, 4),
        PlanetType.AmmoniaWorld => rng.LogUniform(0.5, 6),
        PlanetType.IceGiant => rng.LogUniform(8, 40),
        PlanetType.GasGiant => rng.LogUniform(40, 4000),
        _ => rng.LogUniform(0.05, 8),
    };

    // Radius in Earth radii for a mass in Earth masses.
    private static double RadiusFor(PlanetType type, double massEarth, double teq)
    {
        switch (type)
        {
            case PlanetType.GasGiant:
            {
                double r = massEarth >= 95 ? 11.2 * DMath.Pow(massEarth / 318.0, -0.04) : 9.45 * DMath.Pow(massEarth / 95.0, 0.5);
                return teq > 1000 ? r * 1.2 : r;
            }
            case PlanetType.IceGiant:
                return 3.9 * DMath.Pow(massEarth / 17.0, 0.3);
            case PlanetType.Icy:
            case PlanetType.RockyIce:
            case PlanetType.WaterWorld:
            case PlanetType.AmmoniaWorld:
                return 1.15 * DMath.Pow(massEarth, 0.28);
            case PlanetType.MetalRich:
                return 0.85 * DMath.Pow(massEarth, 0.28);
            default:
                return DMath.Pow(massEarth, 0.28);
        }
    }

    // Adds a planet and its moons; returns the planet's apoapsis in AU.
    private static double AddPlanet(Builder b, string prefix, int number, int slot, PlanetType type, double semiMajorAu,
        double teq, double centralMassKg, double previousApoapsisAu, double apoapsisLimitAu, ulong key, ref Rng rng)
    {
        double massEarth = MassForType(type, ref rng);
        double radiusEarth = RadiusFor(type, massEarth, teq);
        double mass = massEarth * Units.EarthMass;
        double radius = radiusEarth * Units.EarthRadius;
        bool giant = type is PlanetType.GasGiant or PlanetType.IceGiant;

        AtmosphereType atmosphere;
        double pressure;
        switch (type)
        {
            case PlanetType.GasGiant:
            case PlanetType.IceGiant:
                atmosphere = AtmosphereType.HydrogenHelium;
                pressure = 1000;
                break;
            case PlanetType.EarthLike:
                atmosphere = AtmosphereType.Breathable;
                pressure = rng.Range(0.5, 2.0);
                break;
            case PlanetType.WaterWorld:
            case PlanetType.AmmoniaWorld:
                atmosphere = AtmosphereType.Thick;
                pressure = rng.Range(1, 10);
                break;
            default:
            {
                double roll = rng.NextDouble();
                if (massEarth < 0.2 || roll < 0.5)
                {
                    atmosphere = AtmosphereType.None;
                    pressure = 0;
                }
                else if (roll < 0.8)
                {
                    atmosphere = AtmosphereType.Thin;
                    pressure = rng.LogUniform(0.001, 0.5);
                }
                else
                {
                    atmosphere = AtmosphereType.Thick;
                    pressure = rng.LogUniform(1, 90);
                }
                break;
            }
        }
        double greenhouse = atmosphere switch
        {
            AtmosphereType.Thin => 1.05,
            AtmosphereType.Thick => 1.0 + 0.5 * Math.Min(1.0, pressure / 10.0),
            AtmosphereType.Breathable => 1.06,
            _ => 1.0,
        };

        // Orbits never cross: periapsis stays beyond the previous body's apoapsis, apoapsis inside the next orbit.
        double eccentricityLimit = Math.Min(Math.Min(0.5, 1 - 1.15 * previousApoapsisAu / semiMajorAu), apoapsisLimitAu / semiMajorAu - 1);
        double eccentricity = StellarTables.Clamp(rng.Rayleigh(giant ? 0.08 : 0.05), 0, Math.Max(0, eccentricityLimit));
        double a = semiMajorAu * Au;
        var orbit = new Orbit(a, eccentricity, StellarTables.Clamp(rng.Rayleigh(2 * DMath.DegToRad), 0, 15 * DMath.DegToRad),
            rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi),
            Orbit.PeriodFor(a, centralMassKg + mass));

        double lockDistance = 0.1 * DMath.Pow(centralMassKg / Units.SolarMass, 1.0 / 3.0);
        bool locked = !giant && semiMajorAu < lockDistance;
        double rotation = giant
            ? rng.Range(8, 20) * 3600
            : locked ? orbit.Period : rng.LogUniform(10 * 3600, 100 * Units.SecondsPerDay);
        if (!locked && rng.Chance(0.1))
        {
            rotation = -rotation;
        }

        double ringInner = 0;
        double ringOuter = 0;
        double ringChance = type == PlanetType.GasGiant ? 0.4 : type == PlanetType.IceGiant ? 0.3 : 0.02;
        if (rng.Chance(ringChance))
        {
            ringInner = radius * rng.Range(1.2, 1.6);
            ringOuter = ringInner * rng.Range(1.3, 2.2);
        }

        uint path = (uint)(slot + 1) << 16;
        string designation = number.ToString(CultureInfo.InvariantCulture);
        Body planet = b.Add(new Body
        {
            Index = b.Next,
            Parent = 0,
            Kind = BodyKind.Planet,
            Name = prefix + " " + designation,
            Designation = designation,
            Path = path,
            PlanetType = type,
            Mass = mass,
            Radius = radius,
            Temperature = teq * greenhouse,
            Atmosphere = atmosphere,
            SurfacePressure = pressure,
            Orbit = orbit,
            RotationPeriod = rotation,
            AxialTilt = StellarTables.Clamp(Math.Abs(rng.Gaussian(0, 25 * DMath.DegToRad)), 0, DMath.Pi),
            TidallyLocked = locked,
            RingInnerRadius = ringInner,
            RingOuterRadius = ringOuter,
            Landable = !giant,
        });

        double moonRate = type switch
        {
            PlanetType.GasGiant => 4.0,
            PlanetType.IceGiant => 2.5,
            _ => massEarth > 0.5 ? 0.4 : 0.0,
        };
        int moons = (int)Math.Min(rng.Poisson(moonRate), 12);
        double hill = orbit.Periapsis * DMath.Pow(mass / (3 * centralMassKg), 1.0 / 3.0);
        double moonLimit = 0.4 * hill;
        double moonA = Math.Max(2.5 * radius, ringOuter * 1.15) * rng.Range(1.0, 1.5);
        for (int m = 0; m < moons && moonA < moonLimit; m++)
        {
            var moonRng = new Rng(Hash64.Combine(key, MoonStream + (ulong)slot * 64 + (ulong)m));
            bool icy = teq < 170;
            double maxMoonMass = Math.Min(0.03, 3e-4 * massEarth);
            double moonMassEarth = moonRng.LogUniform(1e-5, Math.Max(2e-5, maxMoonMass));
            PlanetType moonType = icy ? (moonRng.Chance(0.6) ? PlanetType.Icy : PlanetType.RockyIce) : PlanetType.Rocky;
            double moonRadius = RadiusFor(moonType, moonMassEarth, teq) * Units.EarthRadius;
            double moonMass = moonMassEarth * Units.EarthMass;
            bool retrograde = moonRng.Chance(0.1);
            double inclination = StellarTables.Clamp(moonRng.Rayleigh(1 * DMath.DegToRad), 0, 20 * DMath.DegToRad);
            var moonOrbit = new Orbit(moonA, StellarTables.Clamp(moonRng.Rayleigh(0.01), 0, 0.2),
                retrograde ? DMath.Pi - inclination : inclination,
                moonRng.Range(0, DMath.TwoPi), moonRng.Range(0, DMath.TwoPi), moonRng.Range(0, DMath.TwoPi),
                Orbit.PeriodFor(moonA, mass + moonMass));
            string letter = ((char)('a' + m)).ToString();
            b.Add(new Body
            {
                Index = b.Next,
                Parent = planet.Index,
                Kind = BodyKind.Moon,
                Name = planet.Name + " " + letter,
                Designation = designation + " " + letter,
                Path = path | (uint)(m + 1) << 8,
                PlanetType = moonType,
                Mass = moonMass,
                Radius = moonRadius,
                Temperature = teq,
                Atmosphere = AtmosphereType.None,
                Orbit = moonOrbit,
                RotationPeriod = moonOrbit.Period,
                TidallyLocked = true,
                Landable = true,
            });
            moonA *= rng.Range(1.3, 2.2);
        }
        return semiMajorAu * (1 + eccentricity);
    }

    // Adds a belt between innerLimitAu and outerLimitAu; returns its outer edge in AU.
    private static double AddBelt(Builder b, string prefix, int slot, double semiMajorAu, double centralMassKg,
        double innerLimitAu, double outerLimitAu, ref Rng rng)
    {
        double widthAu = semiMajorAu * rng.Range(0.2, 0.4);
        widthAu = Math.Max(0, Math.Min(widthAu, 2 * Math.Min(semiMajorAu - innerLimitAu, outerLimitAu - semiMajorAu)));
        double a = semiMajorAu * Au;
        b.Add(new Body
        {
            Index = b.Next,
            Parent = 0,
            Kind = BodyKind.Belt,
            Name = prefix + " Belt",
            Designation = "Belt",
            Path = (uint)(slot + 1) << 16 | 0x80,
            PlanetType = PlanetType.AsteroidBelt,
            Orbit = new Orbit(a, 0, 0, 0, 0, 0, Orbit.PeriodFor(a, centralMassKg)),
            BeltWidth = widthAu * Au,
        });
        return semiMajorAu + widthAu / 2;
    }
}
