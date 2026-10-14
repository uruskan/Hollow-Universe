using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>One row of the class table (GALAXY.md §4).</summary>
public sealed record StarClassEntry(
    string Name,
    int Level,
    StarKind Kind,
    double CodeMin,
    double CodeMax,
    double WeightYoung,
    double WeightDisk,
    double WeightOld)
{
    public double Weight(Population population) => population switch
    {
        Population.Young => WeightYoung,
        Population.Disk => WeightDisk,
        _ => WeightOld,
    };
}

/// <summary>
/// Which primary stars exist, at which level, in which population. Tunable until the generator is frozen;
/// any change alters the galaxy (update the golden tests).
/// </summary>
public static class StarClassTable
{
    public const int LevelCount = 8;

    /// <summary>Lower bound of each level's absolute-magnitude range (level 0 has none).</summary>
    private static readonly double[] LevelMagnitudeMin = { double.NegativeInfinity, -3.0, -0.5, 1.5, 2.8, 4.2, 5.8, 9.0 };

    /// <summary>Upper bound (exclusive) of each level's absolute-magnitude range.</summary>
    private static readonly double[] LevelMagnitudeMax = { -3.0, -0.5, 1.5, 2.8, 4.2, 5.8, 9.0, double.PositiveInfinity };

    // Calibrated against the real-star catalogue inside the distance where it holds every star of a level (V ≤ 7.5:
    // level 0 within 1,000 ly, 1 within 800, 2 within 400, 3 within 200, 4 within 160, 5 within 80, 6 and 7 within
    // 20), GALAXY.md §4. tools/Hollow.GeneratorReport calibrate prints the comparison.
    public static readonly IReadOnlyList<StarClassEntry> Entries = new StarClassEntry[]
    {
        new("O main sequence", 0, StarKind.MainSequence, 3, 10, 0.000005, 0, 0),
        new("B0 main sequence", 0, StarKind.MainSequence, 10, 11, 0.00006, 0, 0),
        new("Supergiant", 0, StarKind.Supergiant, 10, 66, 0.00038, 0, 0),
        new("Wolf–Rayet", 0, StarKind.WolfRayet, 3, 3, 0.000003, 0, 0),
        new("B1–B6 main sequence", 1, StarKind.MainSequence, 11, 17, 0.0041, 0.000015, 0),
        new("Red bright giant", 1, StarKind.BrightGiant, 55, 66, 0.0001, 0.00014, 0.0003),
        new("Carbon star", 1, StarKind.CarbonStar, 66, 69, 0, 0.000001, 0.000002),
        new("B7–A2 main sequence", 2, StarKind.MainSequence, 17, 23, 0.061, 0.0007, 0),
        new("Giant", 2, StarKind.Giant, 48, 56, 0.00047, 0.0021, 0.0038),
        new("Herbig Ae/Be", 2, StarKind.HerbigAeBe, 15, 25, 0.00075, 0, 0),
        new("A3–F0 main sequence", 3, StarKind.MainSequence, 23, 31, 0.06, 0.004, 0.0005),
        new("Bright subgiant", 3, StarKind.Subgiant, 28, 38, 0, 0.004, 0.004),
        new("F1–F9 main sequence", 4, StarKind.MainSequence, 31, 40, 0.047, 0.0249, 0.003),
        new("Subgiant", 4, StarKind.Subgiant, 35, 55, 0, 0.0047, 0.0056),
        new("G main sequence", 5, StarKind.MainSequence, 40, 50, 0.042, 0.054, 0.042),
        new("T Tauri", 5, StarKind.TTauri, 45, 60, 0.006, 0, 0),
        new("K0–M0 main sequence", 6, StarKind.MainSequence, 50, 61, 0.10, 0.18, 0.18),
        new("M1–M9 main sequence", 7, StarKind.MainSequence, 61, 70, 0.40, 0.52, 0.50),
        new("Brown dwarf", 7, StarKind.BrownDwarf, 70, 92, 0.15, 0.15, 0.14),
        new("White dwarf", 7, StarKind.WhiteDwarf, 0, 0, 0, 0.065, 0.09),
        new("Neutron star", 7, StarKind.NeutronStar, 0, 0, 0.0005, 0.0003, 0.0004),
        new("Black hole", 7, StarKind.BlackHole, 0, 0, 0.00005, 0.00005, 0.00008),
    };

    // [population, level] → share of that population's systems whose primary sits at that level.
    private static readonly double[,] LevelShare = new double[3, LevelCount];

    // [population, level] → weights of every entry (zero for entries on other levels), for picking.
    private static readonly double[][][] PickWeights = new double[3][][];

    static StarClassTable()
    {
        for (int p = 0; p < 3; p++)
        {
            var population = (Population)p;
            double total = 0;
            foreach (StarClassEntry e in Entries)
            {
                total += e.Weight(population);
            }
            PickWeights[p] = new double[LevelCount][];
            for (int level = 0; level < LevelCount; level++)
            {
                var weights = new double[Entries.Count];
                double levelTotal = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].Level == level)
                    {
                        weights[i] = Entries[i].Weight(population);
                        levelTotal += weights[i];
                    }
                }
                PickWeights[p][level] = weights;
                LevelShare[p, level] = levelTotal / total;
            }
        }
    }

    /// <summary>Share of a population's systems whose primary star belongs to <paramref name="level"/>.</summary>
    public static double Share(Population population, int level) => LevelShare[(int)population, level];

    /// <summary>The level a star of absolute magnitude <paramref name="mv"/> belongs to.</summary>
    public static int LevelForMagnitude(double mv)
    {
        for (int level = 0; level < LevelCount - 1; level++)
        {
            if (mv < LevelMagnitudeMax[level])
            {
                return level;
            }
        }
        return LevelCount - 1;
    }

    /// <summary>Picks a class entry for a star of this population at this level, or −1 if none can exist.</summary>
    public static int Pick(Population population, int level, ref Rng rng) =>
        rng.PickWeighted(PickWeights[(int)population][level]);

    /// <summary>
    /// Rolls the class, physical properties and age (Gyr) of a procedural star from a class entry and the component
    /// it belongs to (GALAXY.md §4.1). The age comes from the component's star-formation history; for evolved stars
    /// and remnants it decides the star: a giant's mass is the turnoff mass of its age, a white dwarf's temperature
    /// its cooling time, a brown dwarf's temperature how long it has been cooling.
    /// </summary>
    public static (StarClass Class, StarPhysics Physics, double Age) Roll(int entryIndex, GalaxyComponent component, ref Rng rng)
    {
        StarClassEntry entry = Entries[entryIndex];
        double code = entry.CodeMin + (entry.CodeMax - entry.CodeMin) * rng.NextDouble();
        code = Math.Floor(code * 2.0) / 2.0; // half subclasses, e.g. M4.5
        double teff, mv, mass, radius, age;

        switch (entry.Kind)
        {
            case StarKind.WhiteDwarf:
            {
                // The progenitor was heavier than the turnoff mass of the population's age (initial mass function
                // dN/dM ∝ M^−2.3, up to 8 M☉); it has cooled since it shed its envelope.
                age = StellarEvolution.DrawAge(component, 0.05, StellarEvolution.OldestStar, ref rng);
                double initial = SampleInitialMass(Math.Max(StellarEvolution.TurnoffMass(age), 0.9), 8.0, ref rng);
                double cooling = Math.Max(0.0, age - StellarEvolution.MainSequenceLifetime(initial));
                mass = StellarEvolution.WhiteDwarfMass(initial);
                radius = 0.0126 * DMath.Pow(mass / 0.6, -1.0 / 3.0);
                teff = StellarEvolution.WhiteDwarfTemperature(cooling, mass);
                mv = StellarTables.WhiteDwarfMagnitude(teff);
                code = Math.Floor(50400.0 / teff * 2.0) / 2.0;
                break;
            }
            case StarKind.NeutronStar:
            {
                double initial = SampleInitialMass(8.0, 20.0, ref rng);
                double lifetime = StellarEvolution.MainSequenceLifetime(initial);
                age = StellarEvolution.DrawAge(component, lifetime, StellarEvolution.OldestStar, ref rng);
                teff = StellarEvolution.NeutronStarTemperature(age - lifetime);
                mass = StellarTables.Clamp(rng.Gaussian(1.4, 0.08), 1.1, 2.1);
                radius = 1.725e-5;
                mv = 30.0;
                code = 0;
                break;
            }
            case StarKind.BlackHole:
                mass = rng.LogUniform(5.0, 25.0);
                radius = 4.24e-6 * mass;
                teff = 0;
                mv = 99.0;
                code = 0;
                age = StellarEvolution.DrawAge(component, 0.006, StellarEvolution.OldestStar, ref rng);
                break;
            case StarKind.BrownDwarf:
            {
                // Brown dwarfs never ignite hydrogen: their temperature, and so their spectral type, comes from mass
                // and age.
                age = StellarEvolution.DrawAge(component, 0.0, StellarEvolution.OldestStar, ref rng);
                mass = SampleSubstellarMass(ref rng);
                teff = StellarEvolution.BrownDwarfTemperature(mass, age);
                code = Math.Floor(StellarTables.MainSequenceCodeForTemperature(teff) * 2.0) / 2.0;
                (_, mv, _, radius) = StellarTables.Typical(StarKind.MainSequence, code);
                radius *= 1.0 + 0.08 * ClampedGaussian(ref rng);
                break;
            }
            case StarKind.WolfRayet:
                teff = rng.Range(40000, 90000);
                mass = rng.Range(10, 25);
                radius = rng.Range(1, 5);
                mv = rng.Range(-6.5, -3.5);
                age = rng.Range(0.002, 0.007);
                break;
            case StarKind.CarbonStar:
                teff = rng.Range(2600, 3300);
                radius = rng.Range(150, 400);
                mv = rng.Range(-3.0, -1.0);
                age = EvolvedAge(component, ref rng);
                mass = EvolvedMass(age, ref rng);
                break;
            default:
            {
                (teff, mv, mass, radius) = StellarTables.Typical(entry.Kind, code);
                teff *= 1.0 + 0.03 * ClampedGaussian(ref rng);
                mv += 0.15 * ClampedGaussian(ref rng);
                mass *= 1.0 + 0.05 * ClampedGaussian(ref rng);
                radius *= 1.0 + 0.08 * ClampedGaussian(ref rng);
                switch (entry.Kind)
                {
                    case StarKind.MainSequence:
                        // Younger than its main-sequence lifetime (a heavy star in an old population formed late).
                        age = StellarEvolution.DrawAge(component, 0.0, StellarEvolution.MainSequenceLifetime(mass), ref rng);
                        break;
                    case StarKind.Subgiant:
                    case StarKind.Giant:
                    case StarKind.BrightGiant:
                        // Just past the main sequence: its mass is the turnoff mass of its age.
                        age = EvolvedAge(component, ref rng);
                        mass = EvolvedMass(age, ref rng);
                        break;
                    case StarKind.Supergiant:
                        age = StellarEvolution.MainSequenceLifetime(mass) * rng.Range(1.0, 1.1);
                        break;
                    case StarKind.TTauri:
                        age = rng.LogUniform(0.0005, 0.01);
                        break;
                    case StarKind.HerbigAeBe:
                        age = rng.LogUniform(0.0003, 0.005);
                        break;
                    default:
                        age = StellarEvolution.DrawAge(component, 0.0, StellarEvolution.OldestStar, ref rng);
                        break;
                }
                break;
            }
        }

        // A star always matches its level's magnitude range, so the map's level-of-detail stays consistent.
        double lo = LevelMagnitudeMin[entry.Level];
        double hi = LevelMagnitudeMax[entry.Level];
        // Margins keep the value inside the range after rounding to float.
        if (mv < lo + 0.001)
        {
            mv = lo + 0.001;
        }
        if (mv > hi - 0.01)
        {
            mv = hi - 0.01;
        }

        double luminosity = entry.Kind == StarKind.BlackHole ? 0.0 : StellarTables.Luminosity(radius, teff);
        return (new StarClass(entry.Kind, (float)code),
            new StarPhysics((float)teff, (float)mv, (float)mass, (float)radius, (float)luminosity), age);
    }

    private static double ClampedGaussian(ref Rng rng) => StellarTables.Clamp(rng.Gaussian(), -2.5, 2.5);

    // A giant or subgiant: the population's age, but at least 40 Myr (younger, it would be a supergiant).
    private static double EvolvedAge(GalaxyComponent component, ref Rng rng) =>
        StellarEvolution.DrawAge(component, 0.04, StellarEvolution.OldestStar, ref rng);

    // Stars leave the main sequence at the turnoff mass of their age; giants have lost a little since.
    private static double EvolvedMass(double age, ref Rng rng) =>
        StellarTables.Clamp(StellarEvolution.TurnoffMass(age) * rng.Range(0.95, 1.05), 0.8, 8.0);

    // Brown-dwarf mass, 0.013–0.078 M☉, from the substellar mass function dN/dM ∝ M^−0.6.
    private static double SampleSubstellarMass(ref Rng rng)
    {
        double a = DMath.Pow(0.013, 0.4);
        double b = DMath.Pow(0.078, 0.4);
        return DMath.Pow(a + rng.NextDouble() * (b - a), 1.0 / 0.4);
    }

    // Initial mass between lo and hi drawn from the initial mass function dN/dM ∝ M^−2.3.
    private static double SampleInitialMass(double lo, double hi, ref Rng rng)
    {
        if (hi <= lo)
        {
            return lo;
        }
        double a = DMath.Pow(lo, -1.3);
        double b = DMath.Pow(hi, -1.3);
        return DMath.Pow(a + rng.NextDouble() * (b - a), -1.0 / 1.3);
    }
}
