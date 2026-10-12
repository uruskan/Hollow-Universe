using System.Linq;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>
/// The ground game's first world (GROUND.md §3): Nolcas Peris CQ-SLY 7-0 B 3, a breathable Earth-like planet
/// 331.5 ly from Sol. A golden test pins its state, so a generator change can't swap it silently.
/// </summary>
public static class FirstWorld
{
    public static readonly SystemAddress System = new(0x7810135EB5240000);

    /// <summary>Body path of planet B 3 (ARCHITECTURE.md §7).</summary>
    public const uint BodyPath = 0x20600000;

    /// <summary>Latitude of the landing site, the origin of the first zone (GROUND.md §3), degrees north.</summary>
    public const double LandingLatitude = 44.8683;

    /// <summary>Longitude of the landing site, degrees east of the prime meridian (+X of the body frame).</summary>
    public const double LandingLongitude = -170.0010;

    /// <summary>The system and the planet, or null if the generator no longer makes them.</summary>
    public static (StarSystem System, Body Planet)? Find(Hollow.Galaxy.Galaxy galaxy)
    {
        StarSystem? system = galaxy.GetSystem(System);
        Body? planet = system?.Bodies.FirstOrDefault(b => b.Path == BodyPath);
        return system != null && planet != null ? (system, planet) : null;
    }
}
