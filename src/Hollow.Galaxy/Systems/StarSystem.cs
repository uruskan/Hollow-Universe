using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Galaxy.Systems;

public enum BodyKind : byte
{
    Star = 0,
    Planet = 1,
    Moon = 2,
    Belt = 3,

    /// <summary>The centre of mass of two stars (or of a star and a pair) that orbit each other.</summary>
    Barycentre = 4,
}

/// <summary>A planet's or moon's class: a label derived from its physical state (GALAXY.md §10.3).</summary>
public enum PlanetType : byte
{
    None = 0,
    MetalRich = 1,
    Rocky = 2,
    RockyIce = 3,
    Icy = 4,
    WaterWorld = 5,
    EarthLike = 6,
    AmmoniaWorld = 7,
    IceGiant = 8,
    GasGiant = 9,
    AsteroidBelt = 10,

    /// <summary>A rocky core under a deep hydrogen–helium envelope (a "sub-Neptune"): no surface to reach.</summary>
    GasDwarf = 11,

    /// <summary>A surface hot enough to stay molten.</summary>
    LavaWorld = 12,
}

/// <summary>An atmosphere's label: its main gases (GALAXY.md §10.3). Its density is in the surface pressure.</summary>
public enum AtmosphereType : byte
{
    None = 0,
    HydrogenHelium = 1,
    CarbonDioxide = 2,
    Nitrogen = 3,
    NitrogenMethane = 4,
    Methane = 5,

    /// <summary>Water vapour: a runaway greenhouse, or a steamy water world.</summary>
    Steam = 6,

    Ammonia = 7,
    SulphurDioxide = 8,

    /// <summary>Nitrogen with oxygen from life; breathable when the oxygen is between 0.16 and 0.5 atm.</summary>
    OxygenNitrogen = 9,

    /// <summary>Vaporised rock above a molten surface.</summary>
    RockVapour = 10,
}

/// <summary>One body of a star system. SI units unless noted.</summary>
public sealed class Body
{
    public int Index { get; init; }

    /// <summary>Index of the body this one orbits; −1 for the root (the only star, or the outermost barycentre).</summary>
    public int Parent { get; init; }

    public BodyKind Kind { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Short designation inside the system: "A", "B", "AB", "3", "B 3 a", "Belt".</summary>
    public string Designation { get; init; } = "";

    /// <summary>Permanent path inside the system (<see cref="BodyPath"/>, ARCHITECTURE.md §7).</summary>
    public uint Path { get; init; }

    public StarClass StarClass { get; init; }
    public PlanetType PlanetType { get; init; }

    /// <summary>Mass in kg (a belt's total mass; a barycentre's, all it holds).</summary>
    public double Mass { get; init; }

    public double Radius { get; init; }

    /// <summary>Effective temperature for stars, mean surface temperature for planets and moons (K).</summary>
    public double Temperature { get; init; }

    /// <summary>Luminosity in solar units (stars only).</summary>
    public double Luminosity { get; init; }

    /// <summary>The atmosphere's label by its main gases; the detail is in <see cref="State"/>.</summary>
    public AtmosphereType Atmosphere { get; init; }

    /// <summary>Surface pressure in atmospheres (giants: a nominal 1,000, they have no surface).</summary>
    public double SurfacePressure { get; init; }

    public Orbit Orbit { get; init; }

    /// <summary>Sidereal rotation period in seconds; negative means retrograde.</summary>
    public double RotationPeriod { get; init; }

    public double AxialTilt { get; init; }
    public bool TidallyLocked { get; init; }
    public double RingInnerRadius { get; init; }
    public double RingOuterRadius { get; init; }

    /// <summary>What the rings are made of (planets with rings only).</summary>
    public Material RingMaterial { get; init; }

    /// <summary>Mass of the rings in kg.</summary>
    public double RingMass { get; init; }

    /// <summary>Radial width of a belt (belts only).</summary>
    public double BeltWidth { get; init; }

    /// <summary>What a belt is made of (belts only).</summary>
    public Material BeltMaterial { get; init; }

    /// <summary>The physical state of a planet or moon (GALAXY.md §10.3); null for stars, barycentres and belts.</summary>
    public PlanetState? State { get; init; }

    /// <summary>Has a solid surface to land on (rock or ice), from <see cref="State"/>.</summary>
    public bool Landable => State is { Surface: SurfaceKind.Rock or SurfaceKind.Ice };

    public List<int> Children { get; } = new();

    public bool HasRings => RingOuterRadius > 0;

    /// <summary>Surface gravity in g.</summary>
    public double SurfaceGravity =>
        Radius > 0 ? Units.GravitationalConstant * Mass / (Radius * Radius) / Units.StandardGravity : 0;

    /// <summary>Human-readable type, e.g. "Gas giant", "G2 V".</summary>
    public string TypeName => Kind switch
    {
        BodyKind.Star => StarClass.ToString(),
        BodyKind.Belt => "Asteroid belt",
        BodyKind.Barycentre => "Barycentre",
        _ => PlanetTypeName(PlanetType),
    };

    /// <summary>The atmosphere in words, e.g. "Thin nitrogen–methane", "Thick carbon dioxide", "None".</summary>
    public string AtmosphereName => AtmosphereLabel(Atmosphere, SurfacePressure);

    public static string PlanetTypeName(PlanetType type) => type switch
    {
        PlanetType.MetalRich => "Metal-rich world",
        PlanetType.Rocky => "Rocky world",
        PlanetType.RockyIce => "Rocky ice world",
        PlanetType.Icy => "Icy world",
        PlanetType.WaterWorld => "Water world",
        PlanetType.EarthLike => "Earth-like world",
        PlanetType.AmmoniaWorld => "Ammonia world",
        PlanetType.IceGiant => "Ice giant",
        PlanetType.GasGiant => "Gas giant",
        PlanetType.AsteroidBelt => "Asteroid belt",
        PlanetType.GasDwarf => "Gas dwarf",
        PlanetType.LavaWorld => "Lava world",
        _ => "Unknown",
    };

    /// <summary>
    /// An atmosphere in words: a density word from the pressure (trace under 0.001 atm, thin under 0.5, thick from 2,
    /// crushing from 30) and the gases.
    /// </summary>
    public static string AtmosphereLabel(AtmosphereType type, double pressure)
    {
        string gases = type switch
        {
            AtmosphereType.None => "None",
            AtmosphereType.HydrogenHelium => "Hydrogen–helium",
            AtmosphereType.CarbonDioxide => "Carbon dioxide",
            AtmosphereType.Nitrogen => "Nitrogen",
            AtmosphereType.NitrogenMethane => "Nitrogen–methane",
            AtmosphereType.Methane => "Methane",
            AtmosphereType.Steam => "Steam",
            AtmosphereType.Ammonia => "Ammonia",
            AtmosphereType.SulphurDioxide => "Sulphur dioxide",
            AtmosphereType.OxygenNitrogen => "Oxygen–nitrogen",
            AtmosphereType.RockVapour => "Rock vapour",
            _ => "Unknown",
        };
        if (type is AtmosphereType.None or AtmosphereType.HydrogenHelium)
        {
            return gases;
        }
        string density = pressure < 0.001 ? "Trace " : pressure < 0.5 ? "Thin " : pressure >= 30 ? "Crushing " : pressure >= 2 ? "Thick " : "";
        return density.Length == 0 ? gases : density + char.ToLowerInvariant(gases[0]) + gases[1..];
    }
}

/// <summary>
/// A star system: a tree of bodies. The root (index 0) is the only star, or for multiple stars the outermost
/// barycentre, fixed at the origin of the system frame; every other body orbits its parent (GALAXY.md §10.1).
/// </summary>
public sealed class StarSystem
{
    public SystemAddress Address { get; init; }
    public string Name { get; init; } = "";
    public GalaxyStar Primary { get; init; }
    public List<Body> Bodies { get; } = new();

    /// <summary>The root of the orbit tree: the only star, or the outermost barycentre.</summary>
    public Body Root => Bodies[0];

    /// <summary>The primary star's body (star A).</summary>
    public Body PrimaryStar
    {
        get
        {
            foreach (Body b in Bodies)
            {
                if (b.Kind == BodyKind.Star)
                {
                    return b;
                }
            }
            return Bodies[0];
        }
    }

    /// <summary>Age of the system in Gyr: its stars and planets formed together (GALAXY.md §4.1).</summary>
    public double Age => Primary.Age;

    /// <summary>The body with this path, or null.</summary>
    public Body? Find(uint path)
    {
        foreach (Body b in Bodies)
        {
            if (b.Path == path)
            {
                return b;
            }
        }
        return null;
    }

    /// <summary>Position of a body relative to the root (metres, system frame).</summary>
    public Vector3D PositionOf(int bodyIndex, double secondsSinceJ2000)
    {
        Vector3D p = Vector3D.Zero;
        int i = bodyIndex;
        while (i > 0)
        {
            Body b = Bodies[i];
            p += b.Orbit.PositionAt(secondsSinceJ2000);
            i = b.Parent;
        }
        return p;
    }

    public int Count(BodyKind kind)
    {
        int n = 0;
        foreach (Body b in Bodies)
        {
            if (b.Kind == kind)
            {
                n++;
            }
        }
        return n;
    }
}
