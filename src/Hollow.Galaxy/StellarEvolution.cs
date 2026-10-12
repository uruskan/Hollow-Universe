using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>
/// Stellar ages and what they decide (GALAXY.md §4.1): each star's age comes from its population, and the age
/// decides what an evolved star or remnant is like (a giant's mass is the turnoff mass of its age, a white dwarf's
/// temperature is set by how long it has been cooling, a brown dwarf cools as it ages). Approximate fits to standard
/// stellar-evolution results; deterministic (DMath only). Ages in Gyr, masses in solar masses.
/// </summary>
public static class StellarEvolution
{
    /// <summary>Age of the oldest stars (Gyr).</summary>
    public const double OldestStar = 13.2;

    /// <summary>Main-sequence lifetime in Gyr: log₁₀ t[yr] = 10 − 3.6 x + x², x = log₁₀ M (1 M☉ → 10 Gyr, 2 → 1.0, 10 → 25 Myr).</summary>
    public static double MainSequenceLifetime(double mass)
    {
        double x = DMath.Log10(mass > 0.08 ? mass : 0.08);
        return DMath.Pow10(1.0 - 3.6 * x + x * x); // 10^(10 − 3.6x + x²) yr in Gyr
    }

    /// <summary>
    /// The mass whose main-sequence lifetime is <paramref name="age"/>: stars above it have evolved. Above about 150 M☉
    /// for ages under 6 Myr (every star is still on the main sequence).
    /// </summary>
    public static double TurnoffMass(double age)
    {
        // Solve x² − 3.6x + (10 − log₁₀ t[yr]) = 0 for the smaller root (the falling branch).
        double y = DMath.Log10(age > 1e-6 ? age : 1e-6) + 9.0;
        double discriminant = 3.6 * 3.6 - 4.0 * (10.0 - y);
        if (discriminant <= 0)
        {
            return 150;
        }
        double x = (3.6 - System.Math.Sqrt(discriminant)) / 2.0;
        double mass = DMath.Pow10(x);
        return mass < 150 ? mass : 150;
    }

    /// <summary>Age range (Gyr) of a component's stars: star formation spread evenly over it.</summary>
    public static (double Min, double Max) AgeRange(GalaxyComponent component) => component switch
    {
        GalaxyComponent.YoungDisk => (0.0, 1.0),
        GalaxyComponent.ThinDisk => (0.3, 9.0),
        GalaxyComponent.ThickDisk => (8.0, 12.0),
        GalaxyComponent.Bulge => (7.5, 12.5),
        GalaxyComponent.NuclearDisk => (0.01, 12.0),
        GalaxyComponent.Halo => (10.0, OldestStar),
        _ => (0.5, 10.0), // catalogue stars: the solar neighbourhood, mostly thin disk
    };

    /// <summary>
    /// A star's age: uniform over its component's range, but never more than <paramref name="maximum"/> (a star can't
    /// outlive its main sequence) nor less than <paramref name="minimum"/>. If the two exclude each other, the star
    /// formed recently: uniform in [0, maximum].
    /// </summary>
    public static double DrawAge(GalaxyComponent component, double minimum, double maximum, ref Rng rng)
    {
        (double lo, double hi) = AgeRange(component);
        if (minimum > lo)
        {
            lo = minimum;
        }
        if (maximum < hi)
        {
            hi = maximum;
        }
        if (hi <= lo)
        {
            return maximum * rng.NextDouble();
        }
        return lo + (hi - lo) * rng.NextDouble();
    }

    /// <summary>Mass of the white dwarf left by a star of this initial mass (initial–final mass relation).</summary>
    public static double WhiteDwarfMass(double initialMass)
    {
        double m = 0.109 * initialMass + 0.394;
        return m < 1.35 ? m : 1.35;
    }

    /// <summary>Initial mass of the star that left a white dwarf of this mass (inverse of <see cref="WhiteDwarfMass"/>).</summary>
    public static double WhiteDwarfProgenitor(double whiteDwarfMass)
    {
        double m = (whiteDwarfMass - 0.394) / 0.109;
        return m > 0.9 ? m : 0.9;
    }

    /// <summary>Effective temperature (K) of a white dwarf after cooling for <paramref name="coolingAge"/> Gyr.</summary>
    public static double WhiteDwarfTemperature(double coolingAge, double mass)
    {
        double t = coolingAge > 1e-4 ? coolingAge : 1e-4;
        double teff = 8000.0 * DMath.Pow(t, -0.35) * DMath.Pow(mass / 0.6, 0.2);
        return teff < 3500 ? 3500 : teff > 150000 ? 150000 : teff;
    }

    /// <summary>Cooling age (Gyr) of a white dwarf of this temperature (inverse of <see cref="WhiteDwarfTemperature"/>).</summary>
    public static double WhiteDwarfCoolingAge(double teff, double mass)
    {
        double scaled = teff / (8000.0 * DMath.Pow(mass / 0.6, 0.2));
        return DMath.Pow(scaled > 1e-3 ? scaled : 1e-3, -1.0 / 0.35);
    }

    /// <summary>Effective temperature (K) of a brown dwarf: brown dwarfs never ignite hydrogen and cool for ever.</summary>
    public static double BrownDwarfTemperature(double mass, double age)
    {
        double t = age > 0.01 ? age : 0.01;
        double teff = 1550.0 * DMath.Pow(mass / 0.05, 0.83) * DMath.Pow(t, -0.32);
        return teff < 250 ? 250 : teff > 2900 ? 2900 : teff;
    }

    /// <summary>Surface temperature (K) of a neutron star that has been cooling for <paramref name="coolingAge"/> Gyr.</summary>
    public static double NeutronStarTemperature(double coolingAge)
    {
        double years = coolingAge * 1e9;
        double teff = 1e6 * DMath.Pow(1.0 + years / 1e5, -0.2);
        return teff < 3e4 ? 3e4 : teff;
    }

    /// <summary>Age of the Sun (Gyr).</summary>
    public const double SunAge = 4.57;

    /// <summary>
    /// An age estimate for a real star, which the catalogue doesn't give: from its class like a procedural star of the
    /// solar neighbourhood (a main-sequence star younger than its lifetime, a giant at the end of its main sequence, a
    /// white dwarf from its temperature). The same for every universe: seeded by the catalogue ID alone.
    /// </summary>
    public static double CatalogueAge(int catalogue, int id, StarClass starClass, StarPhysics physics)
    {
        if (catalogue == 0 && id == 0)
        {
            return SunAge;
        }
        var rng = new Rng(Hash64.Of(Streams.CatalogueAge, (ulong)GeneratorVersion.Galaxy, (ulong)catalogue, (ulong)id));
        double mass = physics.Mass > 0.01 ? physics.Mass : 0.01;
        switch (starClass.Kind)
        {
            case StarKind.WhiteDwarf:
                return WhiteDwarfCoolingAge(physics.Temperature, mass) + MainSequenceLifetime(WhiteDwarfProgenitor(mass));
            case StarKind.Subgiant:
            case StarKind.Giant:
            case StarKind.BrightGiant:
            case StarKind.CarbonStar:
            case StarKind.Supergiant:
                return MainSequenceLifetime(mass) * rng.Range(1.0, 1.12);
            case StarKind.TTauri:
                return rng.LogUniform(0.0005, 0.01);
            case StarKind.HerbigAeBe:
                return rng.LogUniform(0.0003, 0.005);
            case StarKind.WolfRayet:
                return rng.Range(0.002, 0.007);
            default:
                return DrawAge(GalaxyComponent.Catalogue, 0.0, MainSequenceLifetime(mass), ref rng);
        }
    }

    /// <summary>
    /// The mass a star was born with, from what it is now: a main-sequence star's current mass, a white dwarf's
    /// progenitor, roughly 12 M☉ for a neutron star and 30 M☉ for a black hole. Giants lose little mass before the tip.
    /// </summary>
    public static double InitialMass(StarKind kind, double mass) => kind switch
    {
        StarKind.WhiteDwarf => WhiteDwarfProgenitor(mass),
        StarKind.NeutronStar => 12.0,
        StarKind.BlackHole => mass * 1.5 > 25 ? mass * 1.5 : 25,
        _ => mass,
    };
}
