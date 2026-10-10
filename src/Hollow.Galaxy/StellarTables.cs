using System;

namespace Hollow.Galaxy;

/// <summary>Physical properties of a star. Temperature in K, others in solar units, magnitude in V.</summary>
public readonly record struct StarPhysics(float Temperature, float AbsoluteMagnitude, float Mass, float Radius, float Luminosity);

/// <summary>
/// Interpolation tables of typical stellar properties by kind and spectral code (approximate values compiled
/// from standard astrophysical references). Only exact arithmetic is used, so lookups are deterministic.
/// </summary>
public static class StellarTables
{
    // code, Teff (K), M_V, mass (Msun), radius (Rsun)
    private static readonly double[,] MainSequence =
    {
        { 3, 44900, -5.7, 59, 13.0 }, { 5, 41400, -5.2, 40, 11.0 }, { 7, 36900, -4.6, 26, 9.0 }, { 9, 33300, -4.1, 18, 7.5 },
        { 10, 31400, -3.4, 15, 6.6 }, { 11, 26000, -2.6, 11, 5.4 }, { 12, 20600, -1.8, 8.3, 4.4 }, { 13, 17000, -1.3, 6.3, 3.8 },
        { 15, 15700, -0.9, 5.0, 3.2 }, { 16, 14500, -0.6, 4.6, 3.0 }, { 17, 14000, -0.4, 4.0, 2.8 }, { 18, 12300, -0.2, 3.4, 2.6 },
        { 19, 10700, 0.2, 2.8, 2.4 }, { 20, 9700, 0.6, 2.4, 2.2 }, { 22, 8840, 1.3, 2.1, 1.9 }, { 25, 8080, 1.9, 1.85, 1.7 },
        { 27, 7700, 2.2, 1.75, 1.65 }, { 30, 7220, 2.6, 1.6, 1.6 }, { 32, 6810, 3.0, 1.45, 1.5 }, { 35, 6510, 3.4, 1.33, 1.4 },
        { 38, 6170, 4.0, 1.18, 1.2 }, { 40, 5920, 4.4, 1.08, 1.1 }, { 42, 5770, 4.8, 1.00, 1.0 }, { 45, 5660, 5.1, 0.95, 0.92 },
        { 48, 5490, 5.5, 0.90, 0.88 }, { 50, 5280, 5.9, 0.87, 0.85 }, { 52, 5040, 6.3, 0.80, 0.78 }, { 55, 4440, 7.2, 0.69, 0.68 },
        { 57, 4050, 8.1, 0.62, 0.62 }, { 60, 3850, 8.9, 0.57, 0.57 }, { 61, 3660, 9.6, 0.50, 0.49 }, { 62, 3560, 10.3, 0.44, 0.44 },
        { 63, 3430, 11.4, 0.37, 0.36 }, { 64, 3210, 12.8, 0.23, 0.26 }, { 65, 3060, 14.3, 0.16, 0.20 }, { 66, 2810, 16.6, 0.10, 0.14 },
        { 67, 2680, 17.8, 0.09, 0.12 }, { 68, 2570, 18.6, 0.08, 0.11 }, { 69, 2380, 19.5, 0.078, 0.10 },
        { 70, 2250, 20.5, 0.075, 0.10 }, { 75, 1700, 23.0, 0.065, 0.095 }, { 80, 1300, 26.0, 0.055, 0.09 }, { 85, 1050, 28.0, 0.045, 0.09 },
        { 88, 750, 30.0, 0.035, 0.09 }, { 90, 450, 33.0, 0.02, 0.10 }, { 92, 350, 35.0, 0.015, 0.10 },
    };

    private static readonly double[,] Subgiants =
    {
        { 20, 9500, 0.4, 2.6, 2.6 }, { 30, 7000, 2.0, 1.8, 2.0 }, { 35, 6400, 2.4, 1.6, 2.0 }, { 40, 5850, 3.0, 1.4, 2.0 },
        { 45, 5350, 3.1, 1.2, 2.3 }, { 50, 5050, 3.1, 1.2, 2.5 }, { 55, 4600, 3.4, 1.0, 2.8 },
    };

    private static readonly double[,] Giants =
    {
        { 10, 29000, -4.0, 15, 10 }, { 15, 15000, -1.5, 6, 5 }, { 20, 10000, 0.0, 3.5, 3.5 }, { 25, 8000, 0.7, 2.5, 3.5 },
        { 30, 7000, 1.0, 2.2, 4 }, { 35, 6400, 1.0, 2.0, 5 }, { 40, 5800, 1.0, 2.5, 7 }, { 45, 5050, 0.9, 2.5, 10 },
        { 48, 4900, 0.8, 2.3, 10.5 }, { 50, 4750, 0.7, 2.0, 11 }, { 52, 4500, 0.5, 1.8, 15 }, { 55, 3950, -0.2, 1.5, 25 },
        { 60, 3850, -0.4, 1.4, 40 }, { 62, 3700, -0.6, 1.3, 60 }, { 65, 3400, -0.8, 1.2, 100 }, { 66, 3250, -1.0, 1.2, 120 },
    };

    private static readonly double[,] Supergiants =
    {
        { 10, 26000, -6.4, 25, 30 }, { 15, 13600, -6.2, 20, 50 }, { 20, 9600, -6.3, 16, 60 }, { 30, 7700, -6.6, 12, 80 },
        { 40, 5550, -6.4, 10, 120 }, { 50, 4420, -6.0, 13, 200 }, { 60, 3650, -5.6, 15, 500 }, { 62, 3450, -5.6, 18, 800 },
        { 65, 3200, -5.5, 18, 900 },
    };

    // Teff (K) → M_V for white dwarfs (descending temperature).
    private static readonly double[,] WhiteDwarfs =
    {
        { 40000, 9.8 }, { 25000, 11.0 }, { 16500, 11.3 }, { 10000, 12.2 }, { 7700, 13.0 }, { 6000, 14.2 }, { 4500, 15.6 },
    };

    /// <summary>Typical (temperature, M_V, mass, radius) for a kind and spectral code.</summary>
    public static (double Teff, double Mv, double Mass, double Radius) Typical(StarKind kind, double code)
    {
        return kind switch
        {
            StarKind.Subgiant => Interpolate(Subgiants, code),
            StarKind.Giant or StarKind.CarbonStar => Interpolate(Giants, code),
            StarKind.BrightGiant => Brighten(Interpolate(Giants, code), 2.0, 2.0),
            StarKind.Supergiant => Interpolate(Supergiants, code),
            StarKind.TTauri => Brighten(Interpolate(MainSequence, code), 0.8, 2.5),
            StarKind.HerbigAeBe => Brighten(Interpolate(MainSequence, code), 0.6, 1.5),
            _ => Interpolate(MainSequence, code),
        };
    }

    /// <summary>Absolute V magnitude of a white dwarf of the given temperature.</summary>
    public static double WhiteDwarfMagnitude(double teff)
    {
        int n = WhiteDwarfs.GetLength(0);
        if (teff >= WhiteDwarfs[0, 0])
        {
            return WhiteDwarfs[0, 1];
        }
        for (int i = 1; i < n; i++)
        {
            if (teff >= WhiteDwarfs[i, 0])
            {
                double t = (teff - WhiteDwarfs[i, 0]) / (WhiteDwarfs[i - 1, 0] - WhiteDwarfs[i, 0]);
                return WhiteDwarfs[i, 1] + t * (WhiteDwarfs[i - 1, 1] - WhiteDwarfs[i, 1]);
            }
        }
        return WhiteDwarfs[n - 1, 1];
    }

    /// <summary>Main-sequence spectral code for a temperature (inverse of the table), for stars with no type.</summary>
    public static double MainSequenceCodeForTemperature(double teff)
    {
        int n = MainSequence.GetLength(0);
        if (teff >= MainSequence[0, 1])
        {
            return MainSequence[0, 0];
        }
        for (int i = 1; i < n; i++)
        {
            if (teff >= MainSequence[i, 1])
            {
                double t = (teff - MainSequence[i, 1]) / (MainSequence[i - 1, 1] - MainSequence[i, 1]);
                return MainSequence[i, 0] + t * (MainSequence[i - 1, 0] - MainSequence[i, 0]);
            }
        }
        return MainSequence[n - 1, 0];
    }

    /// <summary>Main-sequence absolute magnitude for a spectral code.</summary>
    public static double MainSequenceMagnitude(double code) => Interpolate(MainSequence, code).Mv;

    /// <summary>Luminosity in solar units from radius (solar radii) and temperature (K).</summary>
    public static double Luminosity(double radius, double teff)
    {
        double t = teff / 5772.0;
        double t2 = t * t;
        return radius * radius * t2 * t2;
    }

    private static (double Teff, double Mv, double Mass, double Radius) Brighten(
        (double Teff, double Mv, double Mass, double Radius) v, double magnitudes, double radiusFactor) =>
        (v.Teff, v.Mv - magnitudes, v.Mass, v.Radius * radiusFactor);

    private static (double Teff, double Mv, double Mass, double Radius) Interpolate(double[,] table, double code)
    {
        int n = table.GetLength(0);
        if (code <= table[0, 0])
        {
            return (table[0, 1], table[0, 2], table[0, 3], table[0, 4]);
        }
        if (code >= table[n - 1, 0])
        {
            return (table[n - 1, 1], table[n - 1, 2], table[n - 1, 3], table[n - 1, 4]);
        }
        int i = 1;
        while (table[i, 0] < code)
        {
            i++;
        }
        double t = (code - table[i - 1, 0]) / (table[i, 0] - table[i - 1, 0]);
        return (
            Lerp(table[i - 1, 1], table[i, 1], t),
            Lerp(table[i - 1, 2], table[i, 2], t),
            Lerp(table[i - 1, 3], table[i, 3], t),
            Lerp(table[i - 1, 4], table[i, 4], t));
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Clamps a value into a range; NaN-safe (returns <paramref name="min"/>).</summary>
    internal static double Clamp(double value, double min, double max) =>
        value >= min ? (value <= max ? value : max) : min;
}
