using System;

namespace Hollow.Galaxy.Systems;

/// <summary>Bulk composition of a body, as mass fractions that add up to 1 (GALAXY.md §10.3).</summary>
/// <param name="Metal">Iron and nickel: the core.</param>
/// <param name="Rock">Silicates: mantle and crust.</param>
/// <param name="Ice">Water and other volatiles (ammonia, methane, carbon dioxide ices), frozen or liquid.</param>
/// <param name="Gas">Hydrogen and helium: a giant's bulk, or a gas dwarf's envelope.</param>
public readonly record struct Composition(float Metal, float Rock, float Ice, float Gas);

/// <summary>The gases of an atmosphere as mole fractions that add up to 1 (or all 0 without an atmosphere).</summary>
public readonly record struct Gases(
    float Hydrogen,
    float Helium,
    float Nitrogen,
    float Oxygen,
    float CarbonDioxide,
    float Methane,
    float Water,
    float Ammonia,
    float SulphurDioxide,
    float Argon);

/// <summary>What erupts at the surface, from internal and tidal heat (GALAXY.md §10.3).</summary>
public enum Volcanism : byte
{
    None = 0,

    /// <summary>Molten rock: lava flows and shield volcanoes.</summary>
    Silicate = 1,

    /// <summary>Sulphur and sulphur dioxide, from a rocky body squeezed by tides (like Io).</summary>
    Sulphur = 2,

    /// <summary>Cryovolcanism: liquid water from under an ice shell (like Enceladus).</summary>
    Water = 3,

    /// <summary>Cryovolcanism: an ammonia–water slush, from ice that formed far out.</summary>
    AmmoniaWater = 4,

    /// <summary>Nitrogen geysers on a very cold surface (like Triton).</summary>
    Nitrogen = 5,
}

/// <summary>How a solid body's crust moves (GALAXY.md §10.3).</summary>
public enum Tectonics : byte
{
    /// <summary>Geologically dead: no heat left to move anything.</summary>
    None = 0,

    /// <summary>One rigid shell over a warm interior: volcanoes, rifts, no plates.</summary>
    StagnantLid = 1,

    /// <summary>Moving plates: mountain belts, trenches, recycled crust. Needs heat and water.</summary>
    Plates = 2,

    /// <summary>A surface hot enough to stay molten.</summary>
    Molten = 3,
}

/// <summary>The liquid on a body's surface, if any.</summary>
public enum LiquidKind : byte
{
    None = 0,
    Water = 1,

    /// <summary>Water with dissolved ammonia: liquid down to about 176 K.</summary>
    AmmoniaWater = 2,

    /// <summary>Liquid methane and ethane, on very cold bodies with a thick atmosphere (like Titan's lakes).</summary>
    Hydrocarbons = 3,

    /// <summary>Molten rock: a lava ocean.</summary>
    Magma = 4,
}

/// <summary>What you would stand on (GALAXY.md §10.3). Replaces the old "landable unless giant".</summary>
public enum SurfaceKind : byte
{
    /// <summary>No surface to land on: a giant or gas dwarf.</summary>
    None = 0,

    Rock = 1,
    Ice = 2,

    /// <summary>A global ocean: no dry land.</summary>
    Ocean = 3,
}

/// <summary>Dangers of a body's surface for people and machines (GALAXY.md §10.3).</summary>
[Flags]
public enum Hazards : ushort
{
    None = 0,

    /// <summary>No air to breathe or to slow a fall (under 0.001 atm).</summary>
    Vacuum = 1 << 0,

    /// <summary>Days above 400 K.</summary>
    ExtremeHeat = 1 << 1,

    /// <summary>Nights below 150 K.</summary>
    ExtremeCold = 1 << 2,

    /// <summary>Over 10 atm at the surface.</summary>
    HighPressure = 1 << 3,

    /// <summary>An atmosphere (over 0.01 atm) that isn't breathable.</summary>
    ToxicAtmosphere = 1 << 4,

    /// <summary>Acid: sulphur dioxide over 1%, or a hot carbon dioxide atmosphere with sulphuric clouds.</summary>
    Corrosive = 1 << 5,

    /// <summary>Over 1.6 g.</summary>
    HighGravity = 1 << 6,

    /// <summary>Active volcanoes or geysers.</summary>
    Volcanic = 1 << 7,

    /// <summary>Unshielded radiation: no magnetic field and a thin atmosphere around an active star, or deep in a giant's radiation belts.</summary>
    Radiation = 1 << 8,

    /// <summary>A molten surface.</summary>
    Molten = 1 << 9,

    /// <summary>Violent weather: a dense atmosphere or strong temperature contrasts.</summary>
    Storms = 1 << 10,

    /// <summary>Quakes from tidal flexing or moving plates.</summary>
    Quakes = 1 << 11,
}

/// <summary>Life on a body (GALAXY.md §10.3): what the surface milestone grows biomes, vegetation and animals from.</summary>
public enum Biosphere : byte
{
    None = 0,

    /// <summary>Single cells: mats, slimes, coloured water.</summary>
    Microbial = 1,

    /// <summary>Simple multicellular life and photosynthesis: it has made oxygen.</summary>
    Simple = 2,

    /// <summary>Complex life: vegetation and animals.</summary>
    Complex = 3,
}

/// <summary>What a ring or belt is made of (GALAXY.md §10.3).</summary>
public enum Material : byte
{
    None = 0,
    Icy = 1,
    Rocky = 2,
    MetalRich = 3,
    Metallic = 4,
}

/// <summary>
/// The physical state of a planet or moon (GALAXY.md §10.3): where it formed, what it is made of, its heat, crust,
/// air, water and life. Generated top-down from the star, the orbit and the system's age; the class label, the
/// atmosphere label and the surface kind are derived from it. The planet-surface milestone (M4) reads these fields
/// to build terrain, biomes and resources, so every field and its range is documented.
/// </summary>
public sealed record PlanetState
{
    /// <summary>Where the body formed relative to the snow line of the young star (0.1 = hot inner disc, 1 = snow line).</summary>
    public double FormationZone { get; init; }

    public Composition Composition { get; init; }

    /// <summary>Radius of the metal core as a fraction of the body's radius (0 for bodies without a core).</summary>
    public double CoreRadius { get; init; }

    /// <summary>Heat flowing out through the surface, W/m² (Earth 0.087, Io about 2.5): internal plus tidal.</summary>
    public double HeatFlow { get; init; }

    /// <summary>The part of <see cref="HeatFlow"/> raised by tides, W/m².</summary>
    public double TidalHeating { get; init; }

    public Tectonics Tectonics { get; init; }
    public Volcanism Volcanism { get; init; }

    /// <summary>How active the volcanism is, 0 (none) to 1 (Io-like).</summary>
    public double VolcanicActivity { get; init; }

    /// <summary>Intrinsic magnetic field relative to Earth's (0 = none).</summary>
    public double MagneticField { get; init; }

    /// <summary>How cratered the surface is, 0 (fresh: oceans, lava, active ice) to 1 (saturated, like the Moon).</summary>
    public double Cratering { get; init; }

    /// <summary>Bond albedo, 0–1: the share of starlight reflected.</summary>
    public double Albedo { get; init; }

    /// <summary>Equilibrium temperature with this albedo and no greenhouse, K.</summary>
    public double EquilibriumTemperature { get; init; }

    /// <summary>Typical night-side (or polar) surface temperature, K.</summary>
    public double MinTemperature { get; init; }

    /// <summary>Typical day-side surface temperature, K.</summary>
    public double MaxTemperature { get; init; }

    /// <summary>The atmosphere's gases (mole fractions).</summary>
    public Gases Gases { get; init; }

    /// <summary>Greenhouse optical depth of the atmosphere (0 = none; Earth about 1, Venus about 190).</summary>
    public double Greenhouse { get; init; }

    public LiquidKind Liquid { get; init; }

    /// <summary>Share of the surface covered by liquid, 0–1 (Earth 0.71).</summary>
    public double OceanCover { get; init; }

    /// <summary>Share of the surface covered by ice, 0–1 (Earth about 0.1).</summary>
    public double IceCover { get; init; }

    /// <summary>A liquid-water ocean under an ice shell (like Europa or Enceladus).</summary>
    public bool SubsurfaceOcean { get; init; }

    public Biosphere Biosphere { get; init; }

    public SurfaceKind Surface { get; init; }
    public Hazards Hazards { get; init; }
}
