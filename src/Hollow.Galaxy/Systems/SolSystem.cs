using Hollow.Core;

namespace Hollow.Galaxy.Systems;

/// <summary>
/// The real Solar System: planets with their J2000 orbital elements (ecliptic reference plane), the
/// major moons, Pluto, and the two belts. Values are physical data from standard references.
/// </summary>
public static class SolSystem
{
    private const double Km = 1000.0;
    private const double Deg = DMath.DegToRad;
    private const double Hour = 3600.0;
    private const double Day = Units.SecondsPerDay;
    private const double SunMass = Units.SolarMass;

    private sealed class Writer
    {
        public readonly StarSystem System;
        public Writer(StarSystem system) => System = system;

        public int Add(Body body)
        {
            System.Bodies.Add(body);
            if (body.Parent >= 0)
            {
                System.Bodies[body.Parent].Children.Add(body.Index);
            }
            return body.Index;
        }

        public int Next => System.Bodies.Count;
    }

    public static StarSystem Create(GalaxyStar sun)
    {
        var system = new StarSystem { Address = sun.Address, Name = "Sol", Primary = sun };
        var w = new Writer(system);
        w.Add(new Body
        {
            Index = 0,
            Parent = -1,
            Kind = BodyKind.Star,
            Name = "Sol",
            Designation = "A",
            StarClass = new StarClass(StarKind.MainSequence, 42),
            Mass = SunMass,
            Radius = Units.SolarRadius,
            Temperature = 5772,
            Luminosity = 1.0,
            RotationPeriod = 25.38 * Day,
            AxialTilt = 7.25 * Deg,
        });

        // name, number, type, a (AU), e, i, Ω, ω, M0 (deg), mass (Earth masses), radius (km), rotation (h), tilt (deg),
        // temperature (K), atmosphere, pressure (atm)
        int mercury = Planet(w, "Mercury", 1, PlanetType.MetalRich, 0.38709927, 0.20563593, 7.00497902, 48.33076593, 29.12703035, 174.79252722,
            0.0553, 2439.7, 1407.6, 0.034, 440, AtmosphereType.None, 0);
        int venus = Planet(w, "Venus", 2, PlanetType.Rocky, 0.72333566, 0.00677672, 3.39467605, 76.67984255, 54.92262463, 50.37663232,
            0.815, 6051.8, -5832.5, 177.36, 737, AtmosphereType.Thick, 92);
        int earth = Planet(w, "Earth", 3, PlanetType.EarthLike, 1.00000261, 0.01671123, 0.00001531, 0.0, 102.93768193, 357.52688973,
            1.0, 6371.0, 23.9345, 23.44, 288, AtmosphereType.Breathable, 1.0);
        int mars = Planet(w, "Mars", 4, PlanetType.Rocky, 1.52371034, 0.09339410, 1.84969142, 49.55953891, 286.49683150, 19.39019754,
            0.107, 3389.5, 24.6229, 25.19, 210, AtmosphereType.Thin, 0.006);
        Belt(w, "Main Belt", 2.7, 1.2);
        int jupiter = Planet(w, "Jupiter", 5, PlanetType.GasGiant, 5.20288700, 0.04838624, 1.30439695, 100.47390909, 274.25457074, 19.66796068,
            317.8, 69911, 9.925, 3.13, 165, AtmosphereType.HydrogenHelium, 1000);
        int saturn = Planet(w, "Saturn", 6, PlanetType.GasGiant, 9.53667594, 0.05386179, 2.48599187, 113.66242448, 338.93645383, 317.35536592,
            95.16, 58232, 10.656, 26.73, 134, AtmosphereType.HydrogenHelium, 1000, 74500, 136780);
        int uranus = Planet(w, "Uranus", 7, PlanetType.IceGiant, 19.18916464, 0.04725744, 0.77263783, 74.01692503, 96.93735127, 142.28382821,
            14.54, 25362, -17.24, 97.77, 76, AtmosphereType.HydrogenHelium, 1000, 41837, 51149);
        int neptune = Planet(w, "Neptune", 8, PlanetType.IceGiant, 30.06992276, 0.00859048, 1.77004347, 131.78422574, 273.18053653, 259.91520804,
            17.15, 24622, 16.11, 28.32, 72, AtmosphereType.HydrogenHelium, 1000, 41900, 62930);
        int pluto = Planet(w, "Pluto", 9, PlanetType.Icy, 39.48211675, 0.24882730, 17.14001206, 110.30393684, 113.76497945, 14.86012204,
            0.0022, 1188.3, -153.29, 122.53, 44, AtmosphereType.Thin, 0.00001);
        Belt(w, "Kuiper Belt", 42, 18);

        // parent, name, letter, type, a (km), e, i (deg, to the ecliptic), period (days), radius (km), mass (kg), temperature (K)
        Moon(w, earth, "Moon", "a", PlanetType.Rocky, 384400, 0.0549, 5.145, 27.3217, 1737.4, 7.342e22, 250);
        Moon(w, mars, "Phobos", "a", PlanetType.Rocky, 9376, 0.0151, 26.0, 0.31891, 11.27, 1.0659e16, 233);
        Moon(w, mars, "Deimos", "b", PlanetType.Rocky, 23463, 0.00033, 27.0, 1.26244, 6.2, 1.4762e15, 233);
        Moon(w, jupiter, "Io", "a", PlanetType.Rocky, 421700, 0.0041, 2.2, 1.769, 1821.6, 8.932e22, 110);
        Moon(w, jupiter, "Europa", "b", PlanetType.Icy, 671034, 0.009, 2.6, 3.551, 1560.8, 4.80e22, 102);
        Moon(w, jupiter, "Ganymede", "c", PlanetType.Icy, 1070412, 0.0013, 2.2, 7.155, 2634.1, 1.4819e23, 110);
        Moon(w, jupiter, "Callisto", "d", PlanetType.RockyIce, 1882709, 0.0074, 2.0, 16.689, 2410.3, 1.0759e23, 134);
        Moon(w, saturn, "Mimas", "a", PlanetType.Icy, 185539, 0.0196, 28.3, 0.942, 198.2, 3.75e19, 64);
        Moon(w, saturn, "Enceladus", "b", PlanetType.Icy, 237948, 0.0047, 26.7, 1.370, 252.1, 1.08e20, 75);
        Moon(w, saturn, "Tethys", "c", PlanetType.Icy, 294619, 0.0001, 27.8, 1.888, 531.1, 6.17e20, 86);
        Moon(w, saturn, "Dione", "d", PlanetType.Icy, 377396, 0.0022, 26.7, 2.737, 561.4, 1.095e21, 87);
        Moon(w, saturn, "Rhea", "e", PlanetType.Icy, 527108, 0.0013, 27.1, 4.518, 763.8, 2.31e21, 76);
        Moon(w, saturn, "Titan", "f", PlanetType.Icy, 1221870, 0.0288, 27.1, 15.945, 2574.7, 1.3452e23, 94, AtmosphereType.Thick, 1.45);
        Moon(w, saturn, "Iapetus", "g", PlanetType.Icy, 3560820, 0.0286, 15.5, 79.32, 734.5, 1.806e21, 110);
        Moon(w, uranus, "Miranda", "a", PlanetType.Icy, 129390, 0.0013, 97.8, 1.413, 235.8, 6.4e19, 60);
        Moon(w, uranus, "Ariel", "b", PlanetType.Icy, 190900, 0.0012, 97.8, 2.520, 578.9, 1.25e21, 60);
        Moon(w, uranus, "Umbriel", "c", PlanetType.Icy, 266000, 0.0039, 97.8, 4.144, 584.7, 1.28e21, 75);
        Moon(w, uranus, "Titania", "d", PlanetType.Icy, 435910, 0.0011, 97.8, 8.706, 788.9, 3.4e21, 70);
        Moon(w, uranus, "Oberon", "e", PlanetType.Icy, 583520, 0.0014, 97.8, 13.463, 761.4, 3.08e21, 75);
        Moon(w, neptune, "Triton", "a", PlanetType.Icy, 354759, 0.000016, 156.9, 5.877, 1353.4, 2.14e22, 38, AtmosphereType.Thin, 0.000014);
        Moon(w, pluto, "Charon", "a", PlanetType.Icy, 19591, 0.0002, 112.9, 6.387, 606, 1.586e21, 53);
        _ = mercury;
        _ = venus;
        return system;
    }

    private static int Planet(Writer w, string name, int number, PlanetType type, double aAu, double e, double iDeg,
        double nodeDeg, double periDeg, double meanDeg, double massEarth, double radiusKm, double rotationHours,
        double tiltDeg, double temperature, AtmosphereType atmosphere, double pressure, double ringInnerKm = 0,
        double ringOuterKm = 0)
    {
        double a = aAu * Units.MetresPerAu;
        double mass = massEarth * Units.EarthMass;
        string designation = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return w.Add(new Body
        {
            Index = w.Next,
            Parent = 0,
            Kind = BodyKind.Planet,
            Name = name,
            Designation = designation,
            Path = (uint)number << 16,
            PlanetType = type,
            Mass = mass,
            Radius = radiusKm * Km,
            Temperature = temperature,
            Atmosphere = atmosphere,
            SurfacePressure = pressure,
            Orbit = new Orbit(a, e, iDeg * Deg, nodeDeg * Deg, periDeg * Deg, meanDeg * Deg, Orbit.PeriodFor(a, SunMass + mass)),
            RotationPeriod = rotationHours * Hour,
            AxialTilt = tiltDeg * Deg,
            RingInnerRadius = ringInnerKm * Km,
            RingOuterRadius = ringOuterKm * Km,
            Landable = type is not (PlanetType.GasGiant or PlanetType.IceGiant),
        });
    }

    private static void Moon(Writer w, int parent, string name, string letter, PlanetType type, double aKm, double e,
        double iDeg, double periodDays, double radiusKm, double mass, double temperature,
        AtmosphereType atmosphere = AtmosphereType.None, double pressure = 0)
    {
        Body planet = w.System.Bodies[parent];
        // Spread the starting phases deterministically by name so moons don't line up.
        var rng = new Rng(Hash64.Of(name));
        int moonNumber = letter[0] - 'a' + 1;
        w.Add(new Body
        {
            Index = w.Next,
            Parent = parent,
            Kind = BodyKind.Moon,
            Name = name,
            Designation = planet.Designation + " " + letter,
            Path = planet.Path | (uint)moonNumber << 8,
            PlanetType = type,
            Mass = mass,
            Radius = radiusKm * Km,
            Temperature = temperature,
            Atmosphere = atmosphere,
            SurfacePressure = pressure,
            Orbit = new Orbit(aKm * Km, e, iDeg * Deg, rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi),
                rng.Range(0, DMath.TwoPi), periodDays * Day),
            RotationPeriod = periodDays * Day,
            TidallyLocked = true,
            Landable = true,
        });
    }

    private static void Belt(Writer w, string name, double aAu, double widthAu)
    {
        double a = aAu * Units.MetresPerAu;
        w.Add(new Body
        {
            Index = w.Next,
            Parent = 0,
            Kind = BodyKind.Belt,
            Name = name,
            Designation = name,
            Path = (0xF0u + (uint)w.Next) << 16,
            PlanetType = PlanetType.AsteroidBelt,
            Orbit = new Orbit(a, 0, 0, 0, 0, 0, Orbit.PeriodFor(a, SunMass)),
            BeltWidth = widthAu * Units.MetresPerAu,
        });
    }
}
