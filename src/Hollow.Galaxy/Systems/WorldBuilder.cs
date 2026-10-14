using System;
using Hollow.Core;

namespace Hollow.Galaxy.Systems;

/// <summary>How a planet formed: a solid body (which may keep a gas envelope), an ice giant or a gas giant.</summary>
internal enum Formation : byte
{
    Solid = 0,
    IceGiant = 1,
    GasGiant = 2,
}

/// <summary>What the world builder needs to know about a planet or moon and its surroundings (SI units unless noted).</summary>
internal readonly record struct WorldInput
{
    public double MassEarth { get; init; }
    public Formation Formation { get; init; }

    /// <summary>Formation distance over the young star's snow line.</summary>
    public double FormationZone { get; init; }

    /// <summary>Starlight at the body, L☉/AU² (1 = what Earth gets).</summary>
    public double Flux { get; init; }

    /// <summary>High-energy (XUV and wind) output of the light's sources, relative to the Sun's.</summary>
    public double Activity { get; init; }

    /// <summary>Age of the system, Gyr.</summary>
    public double Age { get; init; }

    /// <summary>Mass of what the body orbits (a star or pair for a planet, the planet for a moon), kg.</summary>
    public double HostMass { get; init; }

    public double OrbitRadius { get; init; }
    public double OrbitPeriod { get; init; }
    public double Eccentricity { get; init; }
    public double RotationPeriod { get; init; }
    public bool TidallyLocked { get; init; }
    public bool IsMoon { get; init; }

    /// <summary>A moon's planet: its radius (m) and whether it is a gas giant heavy enough (over 150 M⊕) for strong radiation belts.</summary>
    public double ParentRadius { get; init; }

    public bool ParentHasBelts { get; init; }

    /// <summary>The light comes from a long-lived, steady star (main sequence or subgiant): life has time.</summary>
    public bool StableHost { get; init; }
}

/// <summary>The builder's output: the state and the labels and headline numbers derived from it.</summary>
internal readonly record struct World(PlanetState State, PlanetType Type, AtmosphereType Atmosphere, double Pressure,
    double Temperature, double Radius);

/// <summary>
/// Builds the physical state of a planet or moon top-down (GALAXY.md §10.3): formation zone → bulk composition →
/// radius → internal and tidal heat → atmosphere (volatile inventory, escape, stripping, condensation, greenhouse,
/// iterated with the climate) → hydrosphere → crust and volcanism → magnetic field → life; then derives the class,
/// the atmosphere label, the surface kind and the hazards from that state. Approximate fits to planetary science,
/// calibrated on the Solar System; deterministic (<see cref="DMath"/> and exactly rounded operations only).
/// </summary>
internal static class WorldBuilder
{
    private const double G = Units.GravitationalConstant;
    private const double Sigma = 5.670374419e-8;
    private const double Boltzmann = 1.380649e-23;
    private const double AtomicMass = 1.66053907e-27;
    private const double Atmosphere = 101325.0;

    // Molar masses (g/mol) in the order of Gases' fields.
    // Odds that life starts on a habitable world, that it learns photosynthesis and fills the air with oxygen, and that
    // it grows complex (GALAXY.md §10.3). Nobody knows them; these make life rare but findable.
    private const double LifeChance = 0.3;
    private const double OxygenChance = 0.3;
    private const double ComplexChance = 0.45;

    private static readonly double[] MolarMass = { 2.016, 4.003, 28.01, 32.0, 44.01, 16.04, 18.02, 17.03, 64.07, 39.95 };
    private const int H2 = 0, He = 1, N2 = 2, O2 = 3, CO2 = 4, CH4 = 5, H2O = 6, NH3 = 7, SO2 = 8, Ar = 9, GasCount = 10;

    public static World Build(in WorldInput input, ref Rng rng)
    {
        return input.Formation == Formation.Solid ? Solid(input, ref rng) : Giant(input, ref rng);
    }

    // ---------------------------------------------------------------- giants

    private static World Giant(in WorldInput input, ref Rng rng)
    {
        bool gas = input.Formation == Formation.GasGiant;
        double massEarth = input.MassEarth;
        float envelope = (float)(gas ? rng.Range(0.80, 0.94) : rng.Range(0.08, 0.20));
        float ice = (float)((1 - envelope) * (gas ? rng.Range(0.3, 0.6) : rng.Range(0.75, 0.85)));
        float rock = (1 - envelope - ice) * 0.7f;
        var composition = new Composition(1 - envelope - ice - rock, rock, ice, envelope);

        double teq0 = 278.6 * Root4(Math.Max(input.Flux, 1e-9));
        double radiusEarth = gas
            ? (massEarth >= 95 ? 11.2 * DMath.Pow(massEarth / 318.0, -0.04) : 9.45 * DMath.Pow(massEarth / 95.0, 0.5))
            : 3.9 * DMath.Pow(massEarth / 17.0, 0.3);
        if (gas && teq0 > 1000)
        {
            radiusEarth *= 1.0 + 0.2 * Math.Min(1.0, (teq0 - 1000) / 800); // irradiated giants puff up
        }
        double radius = radiusEarth * Units.EarthRadius;

        // Hot giants absorb nearly all light (no reflective clouds), cold ones reflect about a third.
        double albedo = teq0 > 1000 ? 0.08 : teq0 > 350 ? 0.2 : 0.32;
        // Giants still glow from contraction: Jupiter emits 5.4 W/m² of its own heat.
        double ownHeat = 5.4 * DMath.Pow(massEarth / 318.0, 0.5) * DMath.Pow(Math.Max(input.Age, 0.05) / 4.6, -1.2);
        double teq = 278.6 * Root4((1 - albedo) * Math.Max(input.Flux, 1e-9));
        double temperature = Root4(teq * teq * teq * teq + ownHeat / Sigma);

        // Hydrogen and helium, with methane and ammonia in cold ones and water vapour in hot ones.
        var gases = new double[GasCount];
        gases[H2] = 0.86;
        gases[He] = 0.135;
        if (temperature < 200)
        {
            gases[CH4] = gas ? 0.003 : 0.02;
            gases[NH3] = temperature < 160 ? 0.0002 : 0.0005;
        }
        else
        {
            gases[H2O] = 0.004;
        }
        Normalise(gases);

        var state = new PlanetState
        {
            FormationZone = input.FormationZone,
            Composition = composition,
            CoreRadius = 0.15,
            HeatFlow = ownHeat,
            MagneticField = gas ? 10 * DMath.Pow(massEarth / 318.0, 0.8) : 0.5,
            Albedo = albedo,
            EquilibriumTemperature = teq,
            MinTemperature = temperature * 0.95,
            MaxTemperature = temperature * 1.05,
            Gases = ToGases(gases),
            Surface = SurfaceKind.None,
        };
        return new World(state, gas ? PlanetType.GasGiant : PlanetType.IceGiant, AtmosphereType.HydrogenHelium, 1000, temperature, radius);
    }

    // ---------------------------------------------------------------- solid bodies

    private static World Solid(in WorldInput input, ref Rng rng)
    {
        double massEarth = Math.Max(input.MassEarth, 1e-9);
        double mass = massEarth * Units.EarthMass;
        double zone = input.FormationZone;
        double flux = Math.Max(input.Flux, 1e-9);
        double activity = Math.Max(input.Activity, 0.01);
        double age = Math.Max(input.Age, 0.001);
        double teq0 = 278.6 * Root4(flux);

        // ---- 1. Bulk composition, by where it formed: metal-rich where only metal condensed (the disc above about
        // 1,300 K), Earth-like rock inside the snow line (with a little water delivered late), water-rich near it,
        // half ice beyond it. A giant impact can strip a rocky planet's mantle and leave it metal-rich (like Mercury),
        // most often close in.
        double metal;
        double ice;
        double mantleLoss = zone < 0.25 ? 0.15 : 0.03;
        double stripRoll = rng.NextDouble();
        if (zone < 0.03)
        {
            metal = rng.Range(0.45, 0.70);
            ice = 0;
        }
        else if (zone < 0.85)
        {
            metal = stripRoll < mantleLoss ? rng.Range(0.45, 0.70) : rng.Range(0.22, 0.38);
            ice = rng.LogUniform(1e-5, 3e-3);
        }
        else if (zone < 1.3)
        {
            metal = rng.Range(0.15, 0.30);
            ice = rng.Range(0.02, 0.4);
        }
        else
        {
            metal = rng.Range(0.08, 0.18);
            ice = rng.Range(0.30, 0.55);
        }
        double rock = 1 - metal - ice;

        // A heavy solid planet may have captured a hydrogen–helium envelope (a gas dwarf); it keeps it only if its
        // gravity holds hydrogen against the heat and high-energy light of its star.
        double envelopeChance = StellarTables.Clamp((DMath.Log10(massEarth) - 0.18) / 0.6, 0, 0.9);
        double envelope = rng.Chance(envelopeChance) ? rng.LogUniform(0.002, 0.05) : 0;
        double coreRadius = SolidRadius(mass, metal, rock, ice);
        double escapeCore = Math.Sqrt(2 * G * mass / coreRadius);
        // The exosphere is heated by the star's high-energy light: about 1,050 K at Earth, 170 K at Titan.
        double exosphere = teq0 + 750 * Math.Sqrt(flux * activity);
        if (envelope > 0 && !(Holds(escapeCore, exosphere, MolarMass[H2]) && flux * activity < 50 * (massEarth / 5) * (massEarth / 5)))
        {
            envelope = 0;
        }

        // Volatile inventories as fractions of the body's mass, drawn now so the random stream doesn't depend on
        // which branch is taken below.
        double co2Inventory = rng.LogUniform(2e-5, 2e-4);
        double n2Inventory = rng.LogUniform(3e-7, 5e-6) * (zone > 2 ? 20 : 1);
        double ch4Inventory = zone > 2 ? rng.LogUniform(1e-7, 1e-4) : 0;
        double nh3Inventory = zone > 2 ? ice * 0.02 : 0;
        double lifeRoll = rng.NextDouble();
        double oxygenRoll = rng.NextDouble();
        double complexRoll = rng.NextDouble();
        double oxygenShare = rng.Range(0.12, 0.30);

        if (envelope > 0)
        {
            return GasDwarf(input, metal, rock, ice, envelope, coreRadius);
        }

        var composition = new Composition((float)metal, (float)rock, (float)ice, 0f);
        double radius = coreRadius;
        double area = 4 * DMath.Pi * radius * radius;
        double gravity = G * mass / (radius * radius);
        double escape = Math.Sqrt(2 * G * mass / radius);
        double rocky = metal + rock;

        // ---- 2. Heat: radiogenic and primordial (Earth 47 TW today, fading with a 2.8 Gyr time scale) and tides
        // (for synchronous bodies: (21/2)(k₂/Q) G M² R⁵ n e² / a⁶).
        // Small bodies lose their primordial heat sooner: the power scales as M^1.3 (Mars comes out at 0.018 W/m²,
        // Ganymede's rock at 0.002, as measured or modelled).
        double internalPower = 47e12 * massEarth * rocky * DMath.Pow(massEarth, 0.3) * DMath.Exp(-(age - 4.57) / 2.8);
        double tidalPower = 0;
        if ((input.TidallyLocked || input.IsMoon) && input.OrbitPeriod > 0 && input.OrbitRadius > 0)
        {
            double n = DMath.TwoPi / input.OrbitPeriod;
            double k2OverQ = ice > 0.1 ? 0.02 : 0.01;
            double r5 = radius * radius * radius * radius * radius;
            double a6 = input.OrbitRadius * input.OrbitRadius * input.OrbitRadius;
            a6 *= a6;
            double e = input.Eccentricity;
            tidalPower = 10.5 * k2OverQ * G * input.HostMass * input.HostMass * r5 * n * e * e / a6;
        }
        double internalFlow = internalPower / area;
        // Above a few W/m² (Io: 2.5) the interior melts, the moon or planet migrates and its orbit circularises:
        // tidal heating limits itself.
        double tidalFlow = Math.Min(tidalPower / area, 5);
        double heat = internalFlow + tidalFlow;

        // ---- 3. Atmosphere. Retention: high-energy light and wind strip a body whose escape speed is low for the
        // light it gets (a "cosmic shoreline": kept fraction exp(−14 (I / I_s)²), I_s = 0.6 (v_esc / 5 km/s)⁴, which
        // leaves Earth and Venus their air, Mars 0.1%, the Moon and Mercury nothing); each gas also needs an escape
        // speed six times its thermal speed at the exosphere.
        double shoreline = 0.6 * Square(Square(escape / 5000.0));
        double stripping = flux * activity / shoreline;
        double kept = DMath.Exp(-14 * stripping * stripping);
        bool runaway = 0.7 * flux * 340 > 282; // absorbed light beyond what a wet atmosphere can radiate
        var columns = new double[GasCount]; // kg of each gas per body
        columns[CO2] = co2Inventory * mass;
        columns[N2] = n2Inventory * mass;
        columns[Ar] = 1e-8 * rocky * mass * Math.Min(1.0, age / 4.57);
        columns[CH4] = ch4Inventory * mass;
        columns[NH3] = nh3Inventory * mass;
        // Water at the surface: rocky worlds keep half their water in the mantle. A runaway greenhouse puts it all in
        // the air, where starlight splits it and the hydrogen escapes as fast as the other gases are stripped: old
        // runaway worlds are dry (Venus kept 0.001% of its water), unless they are heavy.
        double water = (ice >= 0.05 ? ice : ice * 0.5) * mass;
        if (runaway && age > 0.3)
        {
            water *= Math.Max(1e-5, DMath.Exp(-60 * stripping));
        }
        for (int i = 0; i < GasCount; i++)
        {
            if (i != H2O)
            {
                columns[i] *= Holds(escape, exosphere, MolarMass[i]) ? kept : 0;
            }
        }
        // Without an atmosphere, surface water sublimates and escapes unless the body is cold enough to keep its ice;
        // strong volcanism drives it off a small body (Io is dry).
        bool airless = kept < 0.01;
        if (airless && (teq0 > 200 || (ice < 0.05 && heat > 0.3)))
        {
            water = 0;
        }
        // A water-rich world has no exposed rock to weather, but its deep ocean holds most of the carbon dioxide.
        if (ice >= 0.05)
        {
            columns[CO2] *= 0.05;
        }

        // ---- 4. Climate (Evaluate). On a world with liquid water and exposed rock, weathering regulates carbon dioxide
        // (the carbonate–silicate cycle): it settles where the surface stays near 288 K, between a trace (0.0003 atm)
        // and everything the world has. Life, if it starts, adds oxygen.
        double rotationHours = Math.Abs(input.RotationPeriod) / 3600;
        var model = new ClimateModel
        {
            Mass = mass,
            Gravity = gravity,
            Area = area,
            Flux = flux,
            Heat = heat,
            Water = water,
            Ice = ice,
            MetalRich = metal > 0.45,
            Ammonia = nh3Inventory > 0,
            Methane = columns[CH4] > 0,
            Runaway = runaway,
            Airless = airless,
            Slowness = input.TidallyLocked ? 1 : StellarTables.Clamp(DMath.Log10(Math.Max(rotationHours, 1) / 10) / 2, 0.15, 1),
        };
        Climate climate = Evaluate(model, columns);
        double traceCo2 = 3e-4 * Atmosphere * area / gravity * (MolarMass[CO2] / 29.0);
        if (!airless && !runaway && ice < 0.05 && water > 0 && columns[CO2] > traceCo2
            && (climate.Liquid == LiquidKind.Water || climate.Temperature > 273.15))
        {
            double inventory = columns[CO2];
            columns[CO2] = traceCo2;
            Climate trace = Evaluate(model, columns);
            if (trace.Temperature >= 288)
            {
                climate = trace;
            }
            else
            {
                // Bisect (in log of the amount) for the carbon dioxide that keeps the surface at 288 K.
                double low = DMath.Log(traceCo2);
                double high = DMath.Log(inventory);
                for (int step = 0; step < 6; step++)
                {
                    double middle = 0.5 * (low + high);
                    columns[CO2] = DMath.Exp(middle);
                    if (Evaluate(model, columns).Temperature < 288)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }
                columns[CO2] = DMath.Exp(high);
                climate = Evaluate(model, columns);
            }
        }

        // ---- Life: liquid water, a mild climate, a steady star and time. On Earth life took under a billion years,
        // oxygen from photosynthesis two, animals four; each step is a chance, not a certainty (LifeChance and below).
        Biosphere biosphere = Biosphere.None;
        bool habitable = climate.Liquid == LiquidKind.Water && climate.Temperature > 260 && climate.Temperature < 330
            && climate.Pressure > 0.05 && input.StableHost && age > 0.8;
        if (habitable && lifeRoll < LifeChance)
        {
            biosphere = Biosphere.Microbial;
            if (age > 2 && oxygenRoll < OxygenChance)
            {
                biosphere = Biosphere.Simple;
                if (age > 3.5 && complexRoll < ComplexChance)
                {
                    biosphere = Biosphere.Complex;
                }
            }
        }
        if (biosphere >= Biosphere.Simple)
        {
            // Oxygen builds up against the nitrogen and argon background (Earth: 21% of the air).
            double background = columns[N2] + columns[Ar];
            columns[O2] = background * oxygenShare / (1 - oxygenShare);
            columns[CH4] = columns[CH4] * 0.01 + 1e-6 * columns[N2];
            climate = Evaluate(model, columns);
        }

        double temperature = climate.Temperature;
        double pressure = climate.Pressure;
        double[] air = climate.Air;
        LiquidKind liquid = climate.Liquid;
        double oceanCover = climate.OceanCover;

        // ---- 5. Crust, volcanism and magnetic field.
        bool icyBody = ice >= 0.1;
        Tectonics tectonics;
        if (temperature > 1300)
        {
            tectonics = Tectonics.Molten;
        }
        else if (heat < (icyBody ? 0.003 : 0.01))
        {
            tectonics = Tectonics.None;
        }
        else if (!icyBody && liquid == LiquidKind.Water && massEarth > 0.3 && massEarth < 6 && heat > 0.03)
        {
            tectonics = Tectonics.Plates;
        }
        else
        {
            tectonics = Tectonics.StagnantLid;
        }
        // Eruptions need more heat than the crust can conduct away: about Earth's flow for rock; for ice, tidal heating
        // like Enceladus's or Europa's.
        double threshold = icyBody ? 0.01 : 0.04;
        double volcanicActivity = heat > threshold ? StellarTables.Clamp(DMath.Log10(heat / threshold) / 2, 0, 1) : 0;
        Volcanism volcanism = Volcanism.None;
        if (volcanicActivity > 0)
        {
            if (!icyBody)
            {
                volcanism = input.IsMoon && tidalFlow > internalFlow ? Volcanism.Sulphur : Volcanism.Silicate;
            }
            else if (temperature < 60 && zone > 5)
            {
                volcanism = Volcanism.Nitrogen;
            }
            else
            {
                volcanism = nh3Inventory > 0 ? Volcanism.AmmoniaWater : Volcanism.Water;
            }
        }
        if (volcanism is Volcanism.Sulphur or Volcanism.Silicate && pressure < 1e-3)
        {
            // Volcanic gases leave a trace atmosphere of sulphur dioxide.
            air[SO2] += 1e-9 * volcanicActivity * Atmosphere * area / gravity;
            pressure = PressureOf(air, gravity, area);
        }
        double spin = rotationHours < 30 ? 1 : Math.Sqrt(30 / rotationHours);
        double magnetic = metal > 0.05 && massEarth > 0.02 && internalFlow > 0.04
            ? metal / 0.32 * Math.Sqrt(massEarth) * spin * Math.Min(1.0, internalFlow / 0.06)
            : 0;
        // An ice shell keeps an ocean liquid underneath if enough heat comes up (more for a small body, whose shell is
        // thinner and loses heat faster).
        bool subsurfaceOcean = ice >= 0.05 && temperature < 273 && radius > 2e5 && heat > 0.004 * (1e6 / radius)
            && liquid is LiquidKind.None or LiquidKind.Hydrocarbons;

        // ---- 6. Temperatures by day and night: thin air and slow spin make them extreme.
        double swing = Swing(model.Slowness, pressure);
        double maxTemperature = temperature * (1 + 0.55 * swing);
        double minTemperature = temperature * Math.Max(1 - swing, 0.25);

        // ---- 7. Cratering: old surfaces that nothing renews.
        double renewal = Math.Max(Math.Max(volcanicActivity, tectonics == Tectonics.Plates ? 0.9 : 0), Math.Max(oceanCover, subsurfaceOcean ? 0.5 : 0));
        if (tectonics == Tectonics.Molten)
        {
            renewal = 1;
        }
        double cratering = StellarTables.Clamp(Math.Min(1, age / 3) * (1 - renewal) / (1 + 10 * pressure), 0, 1);

        // ---- 8. Labels, derived from the state.
        double[] fractions = MoleFractions(air);
        Gases gases = ToGases(fractions);
        AtmosphereType atmosphere = LabelAtmosphere(fractions, pressure, temperature);
        PlanetType type;
        if (metal >= 0.45)
        {
            type = PlanetType.MetalRich;
        }
        else if (liquid == LiquidKind.Magma)
        {
            type = PlanetType.LavaWorld;
        }
        else if (biosphere >= Biosphere.Simple && fractions[O2] >= 0.1 && liquid == LiquidKind.Water)
        {
            type = PlanetType.EarthLike;
        }
        else if (liquid == LiquidKind.Water && oceanCover >= 0.95)
        {
            type = PlanetType.WaterWorld;
        }
        else if (liquid == LiquidKind.AmmoniaWater && oceanCover >= 0.5)
        {
            type = PlanetType.AmmoniaWorld;
        }
        else if (ice >= 0.3)
        {
            type = PlanetType.Icy;
        }
        else if (ice >= 0.05 || climate.IceCover >= 0.9)
        {
            type = PlanetType.RockyIce;
        }
        else
        {
            type = PlanetType.Rocky;
        }

        SurfaceKind surface = liquid is LiquidKind.Water or LiquidKind.AmmoniaWater && oceanCover >= 0.95
            ? SurfaceKind.Ocean
            : climate.IceCover >= 0.5 || (ice >= 0.3 && temperature < 250) ? SurfaceKind.Ice : SurfaceKind.Rock;

        // A Jupiter-class giant's radiation belts reach its inner moons (Io and Europa, not Ganymede or Callisto).
        bool beltRadiation = input.IsMoon && input.ParentHasBelts && input.OrbitRadius < 10 * input.ParentRadius;
        Hazards hazards = HazardsFor(pressure, minTemperature, maxTemperature, temperature, gases, gravity / Units.StandardGravity,
            volcanicActivity, beltRadiation, magnetic, flux * activity, tectonics, tidalFlow);

        var state = new PlanetState
        {
            FormationZone = zone,
            Composition = composition,
            CoreRadius = metal > 0.01 ? Math.Sqrt(metal) : 0,
            HeatFlow = heat,
            TidalHeating = tidalFlow,
            Tectonics = tectonics,
            Volcanism = volcanism,
            VolcanicActivity = volcanicActivity,
            MagneticField = magnetic,
            Cratering = cratering,
            Albedo = climate.Albedo,
            EquilibriumTemperature = climate.EquilibriumTemperature,
            MinTemperature = minTemperature,
            MaxTemperature = maxTemperature,
            Gases = gases,
            Greenhouse = climate.Greenhouse,
            Liquid = liquid,
            OceanCover = oceanCover,
            IceCover = climate.IceCover,
            SubsurfaceOcean = subsurfaceOcean,
            Biosphere = biosphere,
            Surface = surface,
            Hazards = hazards,
        };
        return new World(state, type, atmosphere, pressure, temperature, radius);
    }

    private static World GasDwarf(in WorldInput input, double metal, double rock, double ice, double envelope, double coreRadius)
    {
        double total = metal + rock + ice + envelope;
        var composition = new Composition((float)(metal / total), (float)(rock / total), (float)(ice / total), (float)(envelope / total));
        double radius = coreRadius * (1 + 3.9 * DMath.Pow(envelope, 0.6));
        double albedo = 0.3;
        double teq = 278.6 * Root4((1 - albedo) * Math.Max(input.Flux, 1e-9));
        var gases = new double[GasCount];
        gases[H2] = 0.86;
        gases[He] = 0.135;
        gases[teq < 200 ? CH4 : H2O] = 0.005;
        Normalise(gases);
        var state = new PlanetState
        {
            FormationZone = input.FormationZone,
            Composition = composition,
            CoreRadius = coreRadius / radius,
            HeatFlow = 47e12 * input.MassEarth * (metal + rock) / (4 * DMath.Pi * coreRadius * coreRadius),
            Albedo = albedo,
            EquilibriumTemperature = teq,
            MinTemperature = teq * 0.95,
            MaxTemperature = teq * 1.05,
            Gases = ToGases(gases),
            Surface = SurfaceKind.None,
            Hazards = Hazards.HighPressure | Hazards.ToxicAtmosphere | Hazards.Storms,
        };
        return new World(state, PlanetType.GasDwarf, AtmosphereType.HydrogenHelium, 1000, teq, radius);
    }

    /// <summary>The surface's dangers (GALAXY.md §10.3), from the state: one rule set for generated and hand-written bodies.</summary>
    internal static Hazards HazardsFor(double pressure, double minTemperature, double maxTemperature, double temperature, Gases gases,
        double gravity, double volcanicActivity, bool radiationBelt, double magneticField, double highEnergyDose, Tectonics tectonics,
        double tidalHeating)
    {
        Hazards hazards = Hazards.None;
        hazards |= pressure < 0.001 ? Hazards.Vacuum : Hazards.None;
        hazards |= maxTemperature > 400 ? Hazards.ExtremeHeat : Hazards.None;
        hazards |= minTemperature < 150 ? Hazards.ExtremeCold : Hazards.None;
        hazards |= pressure > 10 ? Hazards.HighPressure : Hazards.None;
        bool breathable = gases.Oxygen * pressure is >= 0.16 and <= 0.5 && gases.CarbonDioxide < 0.01 && gases.SulphurDioxide < 1e-4;
        hazards |= pressure > 0.01 && !breathable ? Hazards.ToxicAtmosphere : Hazards.None;
        hazards |= pressure > 0.01 && (gases.SulphurDioxide > 0.01 || (gases.CarbonDioxide > 0.5 && pressure > 10 && temperature > 500)) ? Hazards.Corrosive : Hazards.None;
        hazards |= gravity > 1.6 ? Hazards.HighGravity : Hazards.None;
        hazards |= volcanicActivity > 0.5 ? Hazards.Volcanic : Hazards.None;
        hazards |= radiationBelt || (magneticField < 0.1 && pressure < 0.1 && highEnergyDose > 2) ? Hazards.Radiation : Hazards.None;
        hazards |= tectonics == Tectonics.Molten ? Hazards.Molten : Hazards.None;
        hazards |= pressure > 5 || (pressure > 0.1 && maxTemperature - minTemperature > 150) ? Hazards.Storms : Hazards.None;
        hazards |= tidalHeating > 0.1 || tectonics == Tectonics.Plates ? Hazards.Quakes : Hazards.None;
        return hazards;
    }

    // ---------------------------------------------------------------- physics helpers

    /// <summary>
    /// Radius (m) of a solid body from its mass and composition: uncompressed density from the mix (iron 7.9, rock 3.3,
    /// ice 1.0 g/cm³), squeezed by its own gravity: R = (3M / 4πρ)^⅓ · (1 + 2 M/M⊕)^−0.065. Earth 1.0 R⊕, the Moon
    /// 1,738 km, Ganymede 2,630 km, 5 M⊕ of rock 1.55 R⊕.
    /// </summary>
    public static double SolidRadius(double mass, double metal, double rock, double ice)
    {
        double specificVolume = metal / 7900.0 + rock / 3300.0 + ice / 1000.0; // m³/kg
        double density = 1.0 / Math.Max(specificVolume, 1e-6);
        double uncompressed = DMath.Pow(3 * mass / (4 * DMath.Pi * density), 1.0 / 3.0);
        double massEarth = mass / Units.EarthMass;
        return uncompressed * DMath.Pow(1 + 2 * massEarth, -0.065);
    }

    // A gas of this molar mass stays bound if the escape speed is at least six times its thermal speed.
    private static bool Holds(double escape, double exosphere, double molarMass)
    {
        double thermal = Math.Sqrt(2 * Boltzmann * exosphere / (molarMass * AtomicMass));
        return escape > 6 * thermal;
    }

    // Saturation vapour pressure (atm) from the boiling or sublimation point at 1 atm and L/R (K).
    private static double Saturation(double temperature, double pointAtOneAtm, double latentOverR) =>
        DMath.Exp(latentOverR * (1.0 / pointAtOneAtm - 1.0 / Math.Max(temperature, 5)));

    private static double WaterSaturation(double temperature) =>
        0.00604 * DMath.Exp((temperature < 273.16 ? 6140 : 5420) * (1.0 / 273.16 - 1.0 / Math.Max(temperature, 5)));

    // Boiling point of water (K) at a pressure (atm).
    private static double BoilingPoint(double pressure) => 1.0 / (1.0 / 373.15 - DMath.Log(pressure) / 4884.0);

    // ---------------------------------------------------------------- climate

    /// <summary>What the climate of a solid body depends on, besides its gases.</summary>
    private sealed class ClimateModel
    {
        public double Mass;
        public double Gravity;
        public double Area;
        public double Flux;
        public double Heat;

        /// <summary>Water at the surface and in the air, kg.</summary>
        public double Water;

        public double Ice;
        public bool MetalRich;
        public bool Ammonia;
        public bool Methane;
        public bool Runaway;
        public bool Airless;

        /// <summary>0.15 (fast spin) to 1 (tidally locked): how much the night side cools.</summary>
        public double Slowness;
    }

    /// <summary>A settled climate.</summary>
    private sealed class Climate
    {
        public readonly double[] Air = new double[GasCount];
        public double Pressure;
        public double Temperature;
        public double Albedo;
        public double EquilibriumTemperature;
        public double Greenhouse;
        public LiquidKind Liquid;
        public double OceanCover;
        public double IceCover;
    }

    /// <summary>
    /// Settles the climate for given gas inventories (kg): condensable gases are cold-trapped at the coldest surface
    /// temperature, water vapour follows the temperature (60% humidity), then liquids and ice, the albedo from them and
    /// from clouds, and the greenhouse from the partial pressures, until the temperature stops moving. Starts warm, as
    /// young planets do, so a world that can keep liquid water does.
    /// </summary>
    private static Climate Evaluate(ClimateModel w, double[] columns)
    {
        var c = new Climate();
        double[] air = c.Air;
        double unit = Atmosphere * w.Area / w.Gravity; // kg of air per atm
        double temperature = Math.Max(Root4(Square(Square(278.6 * Root4(0.7 * w.Flux))) + w.Heat / Sigma), w.Water > 0 ? 300 : 0);
        for (int pass = 0; pass < 12; pass++)
        {
            Array.Copy(columns, air, GasCount);
            double dry = SumMass(air) / unit;
            double coldest = temperature * Math.Max(1 - Math.Max(Swing(w.Slowness, dry), 0.15), 0.25);

            // Water vapour: 60% of saturation at most; a runaway greenhouse puts all of it in the air.
            if (!w.Airless && w.Water > 0)
            {
                air[H2O] = w.Runaway
                    ? w.Water
                    : Math.Min(w.Water, 0.6 * WaterSaturation(temperature) * unit * (MolarMass[H2O] / MeanMolar(air)));
            }
            double total = SumMass(air) / unit;
            if (total > 0)
            {
                Condense(air, CO2, total, coldest, 194.7, 3030);
                Condense(air, N2, total, coldest, 77.35, 830);
                Condense(air, CH4, total, coldest, 111.7, 986);
                Condense(air, NH3, total, coldest, 239.8, 2800);
            }
            double pressure = SumMass(air) / unit;

            // Liquids and ice on the surface.
            double surfaceWater = Math.Max(0, w.Water - air[H2O]) / w.Mass;
            LiquidKind liquid = LiquidKind.None;
            double ocean = 0;
            double iceCover = 0;
            if (temperature > 1300)
            {
                liquid = LiquidKind.Magma;
                ocean = StellarTables.Clamp((temperature - 1300) / 700, 0.2, 1);
            }
            else if (surfaceWater > 0 && pressure > 0.006 && temperature > 273.15 && temperature < BoilingPoint(pressure))
            {
                liquid = LiquidKind.Water;
                ocean = surfaceWater >= 0.005 ? 1 : 1 - DMath.Exp(-surfaceWater / 1.9e-4);
                iceCover = StellarTables.Clamp((300 - temperature) / 120, 0, 0.9); // polar caps and sea ice
            }
            else if (w.Ammonia && surfaceWater > 0.01 && pressure > 0.006 && temperature > 176 && temperature <= 273.15)
            {
                liquid = LiquidKind.AmmoniaWater;
                ocean = StellarTables.Clamp(surfaceWater / 0.05, 0.3, 1);
            }
            else if (surfaceWater > 0 && temperature <= 273.15)
            {
                iceCover = w.Ice >= 0.05 ? 1 : 1 - DMath.Exp(-surfaceWater / 2e-5);
            }
            if (liquid == LiquidKind.None && w.Methane && pressure > 0.5 && temperature > 85 && temperature < 115)
            {
                liquid = LiquidKind.Hydrocarbons;
                ocean = StellarTables.Clamp(columns[CH4] / w.Mass / 1e-4 * 0.1, 0.01, 0.2);
            }
            // Frozen gases on cold surfaces count as ice too.
            if (coldest < 200 && (columns[N2] > air[N2] * 1.5 || columns[CO2] > air[CO2] * 1.5))
            {
                iceCover = Math.Max(iceCover, 0.2);
            }

            // Albedo: ice over part of the oceans and land, then clouds (sulphuric acid on hot carbon dioxide worlds,
            // water clouds over oceans) and haze.
            double ground = ocean * (liquid == LiquidKind.Magma ? 0.05 : 0.06) + (1 - ocean) * (w.MetalRich ? 0.1 : 0.15);
            double albedo = (1 - iceCover) * ground + iceCover * 0.55;
            if (pressure > 0.01)
            {
                bool acid = temperature > 400 && (air[SO2] > 0 || air[CO2] > 0.5 * SumMass(air));
                albedo += ((acid ? 0.75 : 0.4) - albedo) * (1 - DMath.Exp(-pressure / 20));
            }
            if (liquid == LiquidKind.Water)
            {
                albedo = Math.Max(albedo, 0.28);
            }
            if (w.Methane && pressure > 0.1 && temperature < 150)
            {
                albedo = Math.Max(albedo, 0.25);
            }

            // Greenhouse: grey optical depth from partial pressures (atm), calibrated on Venus (737 K), Earth (288 K),
            // Mars and Titan. Water vapour and carbon dioxide absorb logarithmically at first (each doubling of carbon
            // dioxide warms Earth about 3 K), every gas's bands saturate in deep atmospheres, and pressure broadening
            // adds to it all (twice Earth's nitrogen warms it about 5 K); the surface never gets hotter than 3,000 K.
            double tau = 0;
            if (pressure > 0)
            {
                double moles = Moles(air);
                double Partial(int gas) => air[gas] / MolarMass[gas] / moles * pressure;
                double co2 = Partial(CO2);
                double h2o = Partial(H2O);
                tau = (co2 > 0 ? 0.08 * DMath.Log(1 + co2 / 3e-4) + Band(1.75, co2, 300) : 0)
                    + (h2o > 0 ? 0.2 * DMath.Log(1 + h2o / 4e-4) + Band(2, h2o, 300) : 0)
                    + Band(2.5, Partial(CH4), 50) + Band(3, Partial(NH3), 50) + Band(5, Partial(SO2), 50)
                    + 0.07 * pressure * pressure / (1 + pressure / 2) / (1 + pressure / 200);
            }
            double teq = 278.6 * Root4((1 - albedo) * w.Flux);
            double next = Math.Min(Root4(Square(Square(teq)) + w.Heat / Sigma) * Root4(1 + 0.75 * tau), 3000);

            c.Pressure = pressure;
            c.Albedo = albedo;
            c.EquilibriumTemperature = teq;
            c.Greenhouse = tau;
            c.Liquid = liquid;
            c.OceanCover = ocean;
            c.IceCover = iceCover;
            bool settled = Math.Abs(next - temperature) < 0.2;
            temperature = next;
            if (settled)
            {
                break;
            }
        }
        c.Temperature = temperature;
        return c;
    }

    // How far day and night temperatures swing about the mean (as a fraction): thin air and slow spin make it large.
    private static double Swing(double slowness, double pressure) =>
        0.6 * slowness / (1 + 5 * (pressure > 0 ? DMath.Pow(pressure, 0.7) : 0));

    // Removes the part of a gas above its saturation partial pressure at the coldest surface temperature (it
    // condenses there): saturation from the gas's boiling or sublimation point at 1 atm and its L/R (K).
    private static void Condense(double[] air, int gas, double total, double coldest, double pointAtOneAtm, double latentOverR)
    {
        if (air[gas] <= 0)
        {
            return;
        }
        double saturation = Saturation(coldest, pointAtOneAtm, latentOverR);
        double fraction = air[gas] / MolarMass[gas] / Moles(air);
        double partial = fraction * total;
        if (partial > saturation)
        {
            air[gas] *= saturation / partial;
        }
    }

    private static double PressureOf(double[] air, double gravity, double area) => SumMass(air) * gravity / area / Atmosphere;

    private static double SumMass(double[] air)
    {
        double sum = 0;
        foreach (double m in air)
        {
            sum += m;
        }
        return sum;
    }

    private static double Moles(double[] air)
    {
        double moles = 0;
        for (int i = 0; i < GasCount; i++)
        {
            moles += air[i] / MolarMass[i];
        }
        return moles;
    }

    private static double MeanMolar(double[] air)
    {
        double moles = Moles(air);
        return moles > 0 ? SumMass(air) / moles : 28.97;
    }

    private static double[] MoleFractions(double[] air)
    {
        var fractions = new double[GasCount];
        double moles = Moles(air);
        if (moles <= 0)
        {
            return fractions;
        }
        for (int i = 0; i < GasCount; i++)
        {
            fractions[i] = air[i] / MolarMass[i] / moles;
        }
        return fractions;
    }

    private static void Normalise(double[] fractions)
    {
        double sum = SumMass(fractions);
        for (int i = 0; i < fractions.Length; i++)
        {
            fractions[i] /= sum;
        }
    }

    private static Gases ToGases(double[] f) => new((float)f[H2], (float)f[He], (float)f[N2], (float)f[O2], (float)f[CO2],
        (float)f[CH4], (float)f[H2O], (float)f[NH3], (float)f[SO2], (float)f[Ar]);

    // The atmosphere's label by its main gases (mole fractions).
    private static AtmosphereType LabelAtmosphere(double[] f, double pressure, double temperature)
    {
        if (pressure < 1e-9)
        {
            return temperature > 1500 ? AtmosphereType.RockVapour : AtmosphereType.None;
        }
        if (f[H2] + f[He] > 0.5)
        {
            return AtmosphereType.HydrogenHelium;
        }
        if (f[O2] >= 0.1 && f[N2] > 0.3)
        {
            return AtmosphereType.OxygenNitrogen;
        }
        if (f[H2O] > 0.5)
        {
            return AtmosphereType.Steam;
        }
        if (f[CO2] > 0.5)
        {
            return AtmosphereType.CarbonDioxide;
        }
        if (f[N2] > 0.5)
        {
            return f[CH4] > 0.01 ? AtmosphereType.NitrogenMethane : AtmosphereType.Nitrogen;
        }
        if (f[CH4] > 0.5)
        {
            return AtmosphereType.Methane;
        }
        if (f[NH3] > 0.5)
        {
            return AtmosphereType.Ammonia;
        }
        if (f[SO2] > 0.5)
        {
            return AtmosphereType.SulphurDioxide;
        }
        // No gas dominates: the largest one names it.
        int largest = 0;
        for (int i = 1; i < GasCount; i++)
        {
            if (f[i] > f[largest])
            {
                largest = i;
            }
        }
        return largest switch
        {
            CO2 => AtmosphereType.CarbonDioxide,
            N2 or Ar => AtmosphereType.Nitrogen,
            CH4 => AtmosphereType.Methane,
            H2O => AtmosphereType.Steam,
            NH3 => AtmosphereType.Ammonia,
            SO2 => AtmosphereType.SulphurDioxide,
            O2 => AtmosphereType.OxygenNitrogen,
            _ => AtmosphereType.HydrogenHelium,
        };
    }

    // Optical depth of one gas: k · p, saturating above the pressure s.
    private static double Band(double k, double partial, double saturation) => k * partial / (1 + partial / saturation);

    private static double Square(double x) => x * x;

    private static double Root4(double x) => Math.Sqrt(Math.Sqrt(Math.Max(x, 0)));
}
