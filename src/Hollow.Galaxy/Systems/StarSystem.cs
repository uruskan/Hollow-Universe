using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Galaxy.Systems;

public enum BodyKind : byte
{
    Star = 0,
    Planet = 1,
    Moon = 2,
    Belt = 3,
}

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
}

public enum AtmosphereType : byte
{
    None = 0,
    Thin = 1,
    Thick = 2,
    Breathable = 3,
    HydrogenHelium = 4,
}

/// <summary>One body of a star system. SI units unless noted.</summary>
public sealed class Body
{
    public int Index { get; init; }

    /// <summary>Index of the body this one orbits; −1 for the primary star.</summary>
    public int Parent { get; init; }

    public BodyKind Kind { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Short designation inside the system: "A", "B", "3", "3 a", "Belt A".</summary>
    public string Designation { get; init; } = "";

    /// <summary>Packed path (star · planet · moon), unique inside the system (ARCHITECTURE.md §7).</summary>
    public uint Path { get; init; }

    public StarClass StarClass { get; init; }
    public PlanetType PlanetType { get; init; }

    public double Mass { get; init; }
    public double Radius { get; init; }

    /// <summary>Effective temperature for stars, mean surface temperature for planets and moons (K).</summary>
    public double Temperature { get; init; }

    /// <summary>Luminosity in solar units (stars only).</summary>
    public double Luminosity { get; init; }

    public AtmosphereType Atmosphere { get; init; }

    /// <summary>Surface pressure in atmospheres.</summary>
    public double SurfacePressure { get; init; }

    public Orbit Orbit { get; init; }

    /// <summary>Sidereal rotation period in seconds; negative means retrograde.</summary>
    public double RotationPeriod { get; init; }

    public double AxialTilt { get; init; }
    public bool TidallyLocked { get; init; }
    public double RingInnerRadius { get; init; }
    public double RingOuterRadius { get; init; }

    /// <summary>Radial width of a belt (belts only).</summary>
    public double BeltWidth { get; init; }

    public bool Landable { get; init; }

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
        _ => PlanetTypeName(PlanetType),
    };

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
        _ => "Unknown",
    };
}

/// <summary>A star system: the primary star at index 0, then everything that orbits.</summary>
public sealed class StarSystem
{
    public SystemAddress Address { get; init; }
    public string Name { get; init; } = "";
    public GalaxyStar Primary { get; init; }
    public List<Body> Bodies { get; } = new();

    public Body Root => Bodies[0];

    /// <summary>Position of a body relative to the primary star (metres, system frame).</summary>
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
