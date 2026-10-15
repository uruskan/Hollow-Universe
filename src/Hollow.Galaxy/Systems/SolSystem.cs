using Hollow.Core;

namespace Hollow.Galaxy.Systems;

/// <summary>
/// The real Solar System: planets with their J2000 orbital elements (ecliptic reference plane), the major moons,
/// Pluto and the two belts, with their physical state (composition, heat, air, water, life) from standard references,
/// rounded. Hazards and landability follow the same rules as generated worlds (GALAXY.md §10.3).
/// </summary>
public static class SolSystem
{
    /// <summary>Sol is built-in content; its identity and physical definition do not require the optional HYG file.</summary>
    public static readonly SystemAddress Address = SystemAddress.Catalogue(0, 0);
    public static readonly GalaxyStar Primary = new(Address, Vector3D.Zero,
        new StarClass(StarKind.MainSequence, 42), new StarPhysics(5772, 4.83f, 1, 1, 1),
        0, GalaxyComponent.Catalogue, 0, 4.6f);

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

    // Formation zones: distance over the young Sun's snow line (2.7 AU); moons share their planet's.
    private const double SnowLine = 2.7;

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
            Path = BodyPath.Node(1),
            StarClass = new StarClass(StarKind.MainSequence, 42),
            Mass = SunMass,
            Radius = Units.SolarRadius,
            Temperature = 5772,
            Luminosity = 1.0,
            RotationPeriod = 25.38 * Day,
            AxialTilt = 7.25 * Deg,
        });

        // name, number, type, a (AU), e, i, Ω, ω, M0 (deg), mass (Earth masses), radius (km), rotation (h), tilt (deg),
        // temperature (K), atmosphere, pressure (atm), state
        int mercury = Planet(w, "Mercury", 1, PlanetType.MetalRich, 0.38709927, 0.20563593, 7.00497902, 48.33076593, 29.12703035, 174.79252722,
            0.0553, 2439.7, 1407.6, 0.034, 440, AtmosphereType.None, 0,
            Rocky(0.387, metal: 0.70, ice: 0, heat: 0.02, tectonics: Tectonics.None, field: 0.01, cratering: 0.9, albedo: 0.088,
                teq: 437, min: 100, max: 700, iceCover: 0.01));
        int venus = Planet(w, "Venus", 2, PlanetType.Rocky, 0.72333566, 0.00677672, 3.39467605, 76.67984255, 54.92262463, 50.37663232,
            0.815, 6051.8, -5832.5, 177.36, 737, AtmosphereType.CarbonDioxide, 92,
            Rocky(0.723, metal: 0.32, ice: 0, heat: 0.06, tectonics: Tectonics.StagnantLid, volcanism: Volcanism.Silicate, volcanic: 0.3,
                cratering: 0.1, albedo: 0.76, teq: 229, min: 735, max: 740, greenhouse: 190,
                gases: new Gases(0, 0, 0.035f, 0, 0.965f, 0, 0.00002f, 0, 0.00015f, 0.00007f)));
        int earth = Planet(w, "Earth", 3, PlanetType.EarthLike, 1.00000261, 0.01671123, 0.00001531, 0.0, 102.93768193, 357.52688973,
            1.0, 6371.0, 23.9345, 23.44, 288, AtmosphereType.OxygenNitrogen, 1.0,
            Rocky(1.0, metal: 0.32, ice: 0.0005, heat: 0.087, tectonics: Tectonics.Plates, volcanism: Volcanism.Silicate, volcanic: 0.4,
                field: 1.0, cratering: 0.02, albedo: 0.306, teq: 254, min: 245, max: 305, greenhouse: 1.0,
                gases: new Gases(0, 0, 0.78f, 0.209f, 0.00042f, 0.0000019f, 0.004f, 0, 0, 0.0093f),
                liquid: LiquidKind.Water, ocean: 0.71, iceCover: 0.1, life: Biosphere.Complex));
        int mars = Planet(w, "Mars", 4, PlanetType.Rocky, 1.52371034, 0.09339410, 1.84969142, 49.55953891, 286.49683150, 19.39019754,
            0.107, 3389.5, 24.6229, 25.19, 210, AtmosphereType.CarbonDioxide, 0.006,
            Rocky(1.524, metal: 0.24, ice: 0.0001, heat: 0.019, tectonics: Tectonics.StagnantLid, cratering: 0.7, albedo: 0.25,
                teq: 210, min: 150, max: 270, greenhouse: 0.01,
                gases: new Gases(0, 0, 0.028f, 0.0017f, 0.95f, 0, 0.0003f, 0, 0, 0.02f), iceCover: 0.03));
        Belt(w, "Main Belt", 20, 2.7, 1.2, Material.Rocky, 0.0005);
        int jupiter = Planet(w, "Jupiter", 5, PlanetType.GasGiant, 5.20288700, 0.04838624, 1.30439695, 100.47390909, 274.25457074, 19.66796068,
            317.8, 69911, 9.925, 3.13, 165, AtmosphereType.HydrogenHelium, 1000,
            Giant(5.2, gas: 0.9, ice: 0.05, heat: 5.4, field: 14, albedo: 0.343, teq: 110, temperature: 165,
                gases: new Gases(0.86f, 0.136f, 0, 0, 0, 0.0018f, 0, 0.0003f, 0, 0)));
        int saturn = Planet(w, "Saturn", 6, PlanetType.GasGiant, 9.53667594, 0.05386179, 2.48599187, 113.66242448, 338.93645383, 317.35536592,
            95.16, 58232, 10.656, 26.73, 134, AtmosphereType.HydrogenHelium, 1000,
            Giant(9.54, gas: 0.85, ice: 0.08, heat: 2.0, field: 0.7, albedo: 0.342, teq: 81, temperature: 134,
                gases: new Gases(0.96f, 0.0325f, 0, 0, 0, 0.0045f, 0, 0.0001f, 0, 0)),
            ringInnerKm: 74500, ringOuterKm: 136780, ringMaterial: Material.Icy, ringMass: 1.5e19);
        int uranus = Planet(w, "Uranus", 7, PlanetType.IceGiant, 19.18916464, 0.04725744, 0.77263783, 74.01692503, 96.93735127, 142.28382821,
            14.54, 25362, -17.24, 97.77, 76, AtmosphereType.HydrogenHelium, 1000,
            Giant(19.2, gas: 0.15, ice: 0.7, heat: 0.04, field: 0.75, albedo: 0.3, teq: 58, temperature: 76,
                gases: new Gases(0.83f, 0.15f, 0, 0, 0, 0.023f, 0, 0, 0, 0)),
            ringInnerKm: 41837, ringOuterKm: 51149, ringMaterial: Material.Rocky, ringMass: 1e16);
        int neptune = Planet(w, "Neptune", 8, PlanetType.IceGiant, 30.06992276, 0.00859048, 1.77004347, 131.78422574, 273.18053653, 259.91520804,
            17.15, 24622, 16.11, 28.32, 72, AtmosphereType.HydrogenHelium, 1000,
            Giant(30.1, gas: 0.15, ice: 0.7, heat: 0.43, field: 0.45, albedo: 0.29, teq: 47, temperature: 72,
                gases: new Gases(0.8f, 0.19f, 0, 0, 0, 0.015f, 0, 0, 0, 0)),
            ringInnerKm: 41900, ringOuterKm: 62930, ringMaterial: Material.Rocky, ringMass: 1e15);
        int pluto = Planet(w, "Pluto", 9, PlanetType.Icy, 39.48211675, 0.24882730, 17.14001206, 110.30393684, 113.76497945, 14.86012204,
            0.0022, 1188.3, -153.29, 122.53, 44, AtmosphereType.Nitrogen, 0.00001,
            Icy(39.5, ice: 0.33, heat: 0.003, tectonics: Tectonics.StagnantLid, volcanism: Volcanism.AmmoniaWater, volcanic: 0.1,
                cratering: 0.4, albedo: 0.72, teq: 33, min: 33, max: 55, gases: new Gases(0, 0, 0.995f, 0, 0, 0.005f, 0, 0, 0, 0),
                subsurface: true));
        Belt(w, "Kuiper Belt", 21, 42, 18, Material.Icy, 0.02);

        // parent, name, letter, type, a (km), e, i (deg, to the ecliptic), period (days), radius (km), mass (kg), temperature (K),
        // atmosphere, pressure (atm), state
        Moon(w, earth, "Moon", "a", PlanetType.Rocky, 384400, 0.0549, 5.145, 27.3217, 1737.4, 7.342e22, 250, AtmosphereType.None, 0,
            Rocky(1.0, metal: 0.02, ice: 0, heat: 0.012, tectonics: Tectonics.None, cratering: 1.0, albedo: 0.11, teq: 270, min: 100, max: 390));
        Moon(w, mars, "Phobos", "a", PlanetType.Rocky, 9376, 0.0151, 26.0, 0.31891, 11.27, 1.0659e16, 233, AtmosphereType.None, 0,
            Rocky(1.524, metal: 0.02, ice: 0, heat: 0, tectonics: Tectonics.None, cratering: 0.9, albedo: 0.07, teq: 233, min: 160, max: 300));
        Moon(w, mars, "Deimos", "b", PlanetType.Rocky, 23463, 0.00033, 27.0, 1.26244, 6.2, 1.4762e15, 233, AtmosphereType.None, 0,
            Rocky(1.524, metal: 0.02, ice: 0, heat: 0, tectonics: Tectonics.None, cratering: 0.9, albedo: 0.07, teq: 233, min: 160, max: 300));
        Moon(w, jupiter, "Io", "a", PlanetType.Rocky, 421700, 0.0041, 2.2, 1.769, 1821.6, 8.932e22, 110, AtmosphereType.SulphurDioxide, 3e-9,
            Rocky(5.2, metal: 0.2, ice: 0, heat: 2.5, tidal: 2.4, tectonics: Tectonics.StagnantLid, volcanism: Volcanism.Sulphur, volcanic: 1.0,
                cratering: 0, albedo: 0.63, teq: 110, min: 90, max: 130, gases: new Gases(0, 0, 0, 0, 0, 0, 0, 0, 1, 0)));
        Moon(w, jupiter, "Europa", "b", PlanetType.Icy, 671034, 0.009, 2.6, 3.551, 1560.8, 4.80e22, 102, AtmosphereType.None, 0,
            Icy(5.2, ice: 0.08, heat: 0.05, tidal: 0.04, tectonics: Tectonics.StagnantLid, volcanism: Volcanism.Water, volcanic: 0.15,
                cratering: 0.05, albedo: 0.68, teq: 102, min: 86, max: 125, subsurface: true));
        Moon(w, jupiter, "Ganymede", "c", PlanetType.Icy, 1070412, 0.0013, 2.2, 7.155, 2634.1, 1.4819e23, 110, AtmosphereType.None, 0,
            Icy(5.2, ice: 0.45, heat: 0.005, tectonics: Tectonics.StagnantLid, field: 0.025, cratering: 0.5, albedo: 0.43, teq: 110,
                min: 70, max: 152, subsurface: true));
        Moon(w, jupiter, "Callisto", "d", PlanetType.RockyIce, 1882709, 0.0074, 2.0, 16.689, 2410.3, 1.0759e23, 134, AtmosphereType.None, 0,
            Icy(5.2, ice: 0.5, heat: 0.003, tectonics: Tectonics.None, cratering: 1.0, albedo: 0.22, teq: 134, min: 80, max: 165,
                subsurface: true));
        Moon(w, saturn, "Mimas", "a", PlanetType.Icy, 185539, 0.0196, 28.3, 0.942, 198.2, 3.75e19, 64, AtmosphereType.None, 0,
            Icy(9.54, ice: 0.65, heat: 0.001, tectonics: Tectonics.None, cratering: 1.0, albedo: 0.6, teq: 64, min: 55, max: 80));
        Moon(w, saturn, "Enceladus", "b", PlanetType.Icy, 237948, 0.0047, 26.7, 1.370, 252.1, 1.08e20, 75, AtmosphereType.None, 0,
            Icy(9.54, ice: 0.5, heat: 0.04, tidal: 0.035, tectonics: Tectonics.StagnantLid, volcanism: Volcanism.Water, volcanic: 0.8,
                cratering: 0.3, albedo: 0.81, teq: 75, min: 33, max: 145, subsurface: true));
        Moon(w, saturn, "Tethys", "c", PlanetType.Icy, 294619, 0.0001, 27.8, 1.888, 531.1, 6.17e20, 86, AtmosphereType.None, 0,
            Icy(9.54, ice: 0.9, heat: 0.001, tectonics: Tectonics.None, cratering: 0.9, albedo: 0.67, teq: 86, min: 70, max: 95));
        Moon(w, saturn, "Dione", "d", PlanetType.Icy, 377396, 0.0022, 26.7, 2.737, 561.4, 1.095e21, 87, AtmosphereType.None, 0,
            Icy(9.54, ice: 0.6, heat: 0.002, tectonics: Tectonics.None, cratering: 0.7, albedo: 0.55, teq: 87, min: 70, max: 100));
        Moon(w, saturn, "Rhea", "e", PlanetType.Icy, 527108, 0.0013, 27.1, 4.518, 763.8, 2.31e21, 76, AtmosphereType.None, 0,
            Icy(9.54, ice: 0.75, heat: 0.001, tectonics: Tectonics.None, cratering: 0.95, albedo: 0.55, teq: 76, min: 53, max: 99));
        Moon(w, saturn, "Titan", "f", PlanetType.Icy, 1221870, 0.0288, 27.1, 15.945, 2574.7, 1.3452e23, 94, AtmosphereType.NitrogenMethane, 1.45,
            Icy(9.54, ice: 0.45, heat: 0.005, tectonics: Tectonics.StagnantLid, cratering: 0.05, albedo: 0.27, teq: 85, min: 91, max: 95,
                greenhouse: 0.5, gases: new Gases(0.001f, 0, 0.95f, 0, 0, 0.049f, 0, 0, 0, 0), liquid: LiquidKind.Hydrocarbons, ocean: 0.02,
                subsurface: true));
        Moon(w, saturn, "Iapetus", "g", PlanetType.Icy, 3560820, 0.0286, 15.5, 79.32, 734.5, 1.806e21, 110, AtmosphereType.None, 0,
            Icy(9.54, ice: 0.75, heat: 0.001, tectonics: Tectonics.None, cratering: 0.95, albedo: 0.2, teq: 110, min: 90, max: 130));
        Moon(w, uranus, "Miranda", "a", PlanetType.Icy, 129390, 0.0013, 97.8, 1.413, 235.8, 6.4e19, 60, AtmosphereType.None, 0,
            Icy(19.2, ice: 0.6, heat: 0.002, tectonics: Tectonics.None, cratering: 0.6, albedo: 0.32, teq: 60, min: 50, max: 85));
        Moon(w, uranus, "Ariel", "b", PlanetType.Icy, 190900, 0.0012, 97.8, 2.520, 578.9, 1.25e21, 60, AtmosphereType.None, 0,
            Icy(19.2, ice: 0.5, heat: 0.002, tectonics: Tectonics.None, cratering: 0.4, albedo: 0.23, teq: 60, min: 50, max: 85));
        Moon(w, uranus, "Umbriel", "c", PlanetType.Icy, 266000, 0.0039, 97.8, 4.144, 584.7, 1.28e21, 75, AtmosphereType.None, 0,
            Icy(19.2, ice: 0.5, heat: 0.001, tectonics: Tectonics.None, cratering: 0.95, albedo: 0.1, teq: 75, min: 55, max: 85));
        Moon(w, uranus, "Titania", "d", PlanetType.Icy, 435910, 0.0011, 97.8, 8.706, 788.9, 3.4e21, 70, AtmosphereType.None, 0,
            Icy(19.2, ice: 0.5, heat: 0.002, tectonics: Tectonics.None, cratering: 0.7, albedo: 0.17, teq: 70, min: 55, max: 89));
        Moon(w, uranus, "Oberon", "e", PlanetType.Icy, 583520, 0.0014, 97.8, 13.463, 761.4, 3.08e21, 75, AtmosphereType.None, 0,
            Icy(19.2, ice: 0.5, heat: 0.002, tectonics: Tectonics.None, cratering: 0.9, albedo: 0.14, teq: 75, min: 55, max: 85));
        Moon(w, neptune, "Triton", "a", PlanetType.Icy, 354759, 0.000016, 156.9, 5.877, 1353.4, 2.14e22, 38, AtmosphereType.Nitrogen, 0.000014,
            Icy(30.1, ice: 0.3, heat: 0.003, tectonics: Tectonics.StagnantLid, volcanism: Volcanism.Nitrogen, volcanic: 0.3, cratering: 0.1,
                albedo: 0.76, teq: 38, min: 35, max: 40, gases: new Gases(0, 0, 0.9998f, 0, 0, 0.0002f, 0, 0, 0, 0), subsurface: true));
        Moon(w, pluto, "Charon", "a", PlanetType.Icy, 19591, 0.0002, 112.9, 6.387, 606, 1.586e21, 53, AtmosphereType.None, 0,
            Icy(39.5, ice: 0.4, heat: 0.001, tectonics: Tectonics.None, cratering: 0.6, albedo: 0.4, teq: 53, min: 40, max: 60));
        _ = mercury;
        _ = venus;
        return system;
    }

    // ---------------------------------------------------------------- physical states (hand data)

    private static PlanetState Rocky(double au, double metal, double ice, double heat, Tectonics tectonics, double albedo, double teq,
        double min, double max, double tidal = 0, Volcanism volcanism = Volcanism.None, double volcanic = 0, double field = 0,
        double cratering = 0, double greenhouse = 0, Gases gases = default, LiquidKind liquid = LiquidKind.None, double ocean = 0,
        double iceCover = 0, Biosphere life = Biosphere.None) => new()
    {
        FormationZone = au / SnowLine,
        Composition = new Composition((float)metal, (float)(1 - metal - ice), (float)ice, 0f),
        CoreRadius = metal > 0.01 ? System.Math.Sqrt(metal) : 0,
        HeatFlow = heat,
        TidalHeating = tidal,
        Tectonics = tectonics,
        Volcanism = volcanism,
        VolcanicActivity = volcanic,
        MagneticField = field,
        Cratering = cratering,
        Albedo = albedo,
        EquilibriumTemperature = teq,
        MinTemperature = min,
        MaxTemperature = max,
        Gases = gases,
        Greenhouse = greenhouse,
        Liquid = liquid,
        OceanCover = ocean,
        IceCover = iceCover,
        Biosphere = life,
        Surface = SurfaceKind.Rock,
    };

    private static PlanetState Icy(double au, double ice, double heat, Tectonics tectonics, double albedo, double teq, double min,
        double max, double tidal = 0, Volcanism volcanism = Volcanism.None, double volcanic = 0, double field = 0, double cratering = 0,
        double greenhouse = 0, Gases gases = default, LiquidKind liquid = LiquidKind.None, double ocean = 0, bool subsurface = false)
    {
        double metal = 0.1 * (1 - ice);
        return new PlanetState
        {
            FormationZone = au / SnowLine,
            Composition = new Composition((float)metal, (float)(1 - metal - ice), (float)ice, 0f),
            CoreRadius = System.Math.Sqrt(metal),
            HeatFlow = heat,
            TidalHeating = tidal,
            Tectonics = tectonics,
            Volcanism = volcanism,
            VolcanicActivity = volcanic,
            MagneticField = field,
            Cratering = cratering,
            Albedo = albedo,
            EquilibriumTemperature = teq,
            MinTemperature = min,
            MaxTemperature = max,
            Gases = gases,
            Greenhouse = greenhouse,
            Liquid = liquid,
            OceanCover = ocean,
            IceCover = 1 - ocean,
            SubsurfaceOcean = subsurface,
            Surface = SurfaceKind.Ice,
        };
    }

    private static PlanetState Giant(double au, double gas, double ice, double heat, double field, double albedo, double teq,
        double temperature, Gases gases)
    {
        double rock = (1 - gas - ice) * 0.7;
        return new PlanetState
        {
            FormationZone = au / SnowLine,
            Composition = new Composition((float)(1 - gas - ice - rock), (float)rock, (float)ice, (float)gas),
            CoreRadius = 0.15,
            HeatFlow = heat,
            MagneticField = field,
            Albedo = albedo,
            EquilibriumTemperature = teq,
            MinTemperature = temperature * 0.95,
            MaxTemperature = temperature * 1.05,
            Gases = gases,
            Surface = SurfaceKind.None,
        };
    }

    // The hazards follow from the state, by the same rules as generated worlds.
    private static PlanetState WithHazards(PlanetState state, double mass, double radius, double pressure, double temperature,
        bool radiationBelt, double flux)
    {
        if (state.Surface == SurfaceKind.None)
        {
            return state;
        }
        double gravity = Units.GravitationalConstant * mass / (radius * radius) / Units.StandardGravity;
        return state with
        {
            Hazards = WorldBuilder.HazardsFor(pressure, state.MinTemperature, state.MaxTemperature, temperature, state.Gases, gravity,
                state.VolcanicActivity, radiationBelt, state.MagneticField, flux, state.Tectonics, state.TidalHeating),
        };
    }

    // ---------------------------------------------------------------- bodies

    private static int Planet(Writer w, string name, int number, PlanetType type, double aAu, double e, double iDeg,
        double nodeDeg, double periDeg, double meanDeg, double massEarth, double radiusKm, double rotationHours,
        double tiltDeg, double temperature, AtmosphereType atmosphere, double pressure, PlanetState state, double ringInnerKm = 0,
        double ringOuterKm = 0, Material ringMaterial = Material.None, double ringMass = 0)
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
            Path = BodyPath.Planet(1, number - 1),
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
            RingMaterial = ringMaterial,
            RingMass = ringMass,
            State = WithHazards(state, mass, radiusKm * Km, pressure, temperature, radiationBelt: false, flux: 1 / (aAu * aAu)),
        });
    }

    private static void Moon(Writer w, int parent, string name, string letter, PlanetType type, double aKm, double e,
        double iDeg, double periodDays, double radiusKm, double mass, double temperature, AtmosphereType atmosphere, double pressure,
        PlanetState state)
    {
        Body planet = w.System.Bodies[parent];
        // Spread the starting phases deterministically by name so moons don't line up.
        var rng = new Rng(Hash64.Of(name));
        int moonNumber = letter[0] - 'a' + 1;
        bool radiationBelt = planet.PlanetType is PlanetType.GasGiant or PlanetType.IceGiant && aKm * Km < 15 * planet.Radius;
        w.Add(new Body
        {
            Index = w.Next,
            Parent = parent,
            Kind = BodyKind.Moon,
            Name = name,
            Designation = planet.Designation + " " + letter,
            Path = BodyPath.Moon(planet.Path, moonNumber - 1),
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
            State = WithHazards(state, mass, radiusKm * Km, pressure, temperature, radiationBelt,
                flux: 1 / Square(planet.Orbit.SemiMajorAxis / Units.MetresPerAu)),
        });
    }

    private static double Square(double x) => x * x;

    // Sol's belts take orbit slots 20 and 21, apart from the planets' 0–8, so their paths never collide.
    private static void Belt(Writer w, string name, int slot, double aAu, double widthAu, Material material, double massEarth)
    {
        double a = aAu * Units.MetresPerAu;
        w.Add(new Body
        {
            Index = w.Next,
            Parent = 0,
            Kind = BodyKind.Belt,
            Name = name,
            Designation = name,
            Path = BodyPath.Belt(1, slot),
            PlanetType = PlanetType.AsteroidBelt,
            Mass = massEarth * Units.EarthMass,
            Orbit = new Orbit(a, 0, 0, 0, 0, 0, Orbit.PeriodFor(a, SunMass)),
            BeltWidth = widthAu * Units.MetresPerAu,
            BeltMaterial = material,
        });
    }
}
