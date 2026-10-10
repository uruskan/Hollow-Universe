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

    public static readonly IReadOnlyList<StarClassEntry> Entries = new StarClassEntry[]
    {
        new("O main sequence", 0, StarKind.MainSequence, 3, 10, 0.000015, 0, 0),
        new("B0 main sequence", 0, StarKind.MainSequence, 10, 11, 0.002, 0, 0),
        new("Supergiant", 0, StarKind.Supergiant, 10, 66, 0.0015, 0, 0),
        new("Wolf–Rayet", 0, StarKind.WolfRayet, 3, 3, 0.000005, 0, 0),
        new("B1–B6 main sequence", 1, StarKind.MainSequence, 11, 17, 0.028, 0.0001, 0),
        new("Red bright giant", 1, StarKind.BrightGiant, 55, 66, 0.0005, 0.0007, 0.0015),
        new("Carbon star", 1, StarKind.CarbonStar, 66, 69, 0, 0.00005, 0.0001),
        new("B7–A2 main sequence", 2, StarKind.MainSequence, 17, 23, 0.035, 0.0004, 0),
        new("Giant", 2, StarKind.Giant, 48, 56, 0.001, 0.0045, 0.008),
        new("Herbig Ae/Be", 2, StarKind.HerbigAeBe, 15, 25, 0.0025, 0, 0),
        new("A3–F0 main sequence", 3, StarKind.MainSequence, 23, 31, 0.06, 0.004, 0.0005),
        new("F1–F9 main sequence", 4, StarKind.MainSequence, 31, 40, 0.06, 0.030, 0.015),
        new("Subgiant", 4, StarKind.Subgiant, 35, 55, 0, 0.010, 0.012),
        new("G main sequence", 5, StarKind.MainSequence, 40, 50, 0.06, 0.077, 0.06),
        new("T Tauri", 5, StarKind.TTauri, 45, 60, 0.04, 0, 0),
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

    /// <summary>Rolls the class and physical properties of a procedural star from a class entry.</summary>
    public static (StarClass Class, StarPhysics Physics) Roll(int entryIndex, ref Rng rng)
    {
        StarClassEntry entry = Entries[entryIndex];
        double code = entry.CodeMin + (entry.CodeMax - entry.CodeMin) * rng.NextDouble();
        code = Math.Floor(code * 2.0) / 2.0; // half subclasses, e.g. M4.5
        double teff, mv, mass, radius;

        switch (entry.Kind)
        {
            case StarKind.WhiteDwarf:
            {
                double u = rng.NextDouble();
                teff = 4500.0 * DMath.Pow(40000.0 / 4500.0, u * u); // most white dwarfs are cool
                mass = StellarTables.Clamp(rng.Gaussian(0.6, 0.08), 0.45, 1.3);
                radius = 0.0126 * DMath.Pow(mass / 0.6, -1.0 / 3.0);
                mv = StellarTables.WhiteDwarfMagnitude(teff);
                code = Math.Floor(50400.0 / teff * 2.0) / 2.0;
                break;
            }
            case StarKind.NeutronStar:
                teff = rng.LogUniform(1e5, 1e6);
                mass = StellarTables.Clamp(rng.Gaussian(1.4, 0.08), 1.1, 2.1);
                radius = 1.725e-5;
                mv = 30.0;
                code = 0;
                break;
            case StarKind.BlackHole:
                mass = rng.LogUniform(5.0, 25.0);
                radius = 4.24e-6 * mass;
                teff = 0;
                mv = 99.0;
                code = 0;
                break;
            case StarKind.WolfRayet:
                teff = rng.Range(40000, 90000);
                mass = rng.Range(10, 25);
                radius = rng.Range(1, 5);
                mv = rng.Range(-6.5, -3.5);
                break;
            case StarKind.CarbonStar:
                teff = rng.Range(2600, 3300);
                mass = rng.Range(1.5, 3.0);
                radius = rng.Range(150, 400);
                mv = rng.Range(-3.0, -1.0);
                break;
            default:
            {
                (teff, mv, mass, radius) = StellarTables.Typical(entry.Kind, code);
                teff *= 1.0 + 0.03 * ClampedGaussian(ref rng);
                mv += 0.15 * ClampedGaussian(ref rng);
                mass *= 1.0 + 0.05 * ClampedGaussian(ref rng);
                radius *= 1.0 + 0.08 * ClampedGaussian(ref rng);
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
            new StarPhysics((float)teff, (float)mv, (float)mass, (float)radius, (float)luminosity));
    }

    private static double ClampedGaussian(ref Rng rng) => StellarTables.Clamp(rng.Gaussian(), -2.5, 2.5);
}
