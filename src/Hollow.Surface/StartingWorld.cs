using System.Linq;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>The playable starting world required by LOOP-01, independent of optional star-catalogue files.</summary>
public static class StartingWorld
{
    public static readonly Galaxy.SystemAddress System = SolSystem.Address;
    public const uint BodyPath = 0x10300000; // Sol's third planet: Earth.
    // Defaults are a sentinel: walking terrain selects a dry landing site; normal arrivals choose beaches.
    public const double LandingLatitude = FirstWorld.LandingLatitude;
    public const double LandingLongitude = FirstWorld.LandingLongitude;

    public static (StarSystem System, Body Planet) Find()
    {
        StarSystem system = SolSystem.Create(SolSystem.Primary);
        return (system, system.Bodies.Single(body => body.Path == BodyPath));
    }
}
