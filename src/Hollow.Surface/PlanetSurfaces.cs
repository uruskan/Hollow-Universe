using System;
using System.Collections.Concurrent;
using System.Linq;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>
/// The real surfaces of bodies, built once per process and shared (GROUND.md §4.6): the server's zone, the client's
/// prediction and the client's terrain all draw from the same <see cref="PlanetSurface"/>, a pure function of the body,
/// so sharing it only saves time and memory. Bodies are found by address in a generated universe without the
/// real-star catalogue (the first world is far from the catalogue's stars, and a test checks that both universes give
/// the same surface). Thread-safe.
/// </summary>
public static class PlanetSurfaces
{
    /// <summary>A body with a surface: its system, the body and the surface.</summary>
    public sealed record Entry(StarSystem System, Body Body, PlanetSurface Surface);

    private static readonly Lazy<Galaxy.Galaxy> Universe = new(() => new Galaxy.Galaxy(null));
    private static readonly ConcurrentDictionary<(ulong System, uint Body), Lazy<Entry?>> Cache = new();

    /// <summary>
    /// The surface of a body by address, or null if the universe has no such body or it has no surface (an address
    /// from outside is untrusted: a made-up one simply isn't found).
    /// </summary>
    public static Entry? Find(ulong systemAddress, uint bodyPath) =>
        Cache.GetOrAdd((systemAddress, bodyPath), key => new Lazy<Entry?>(() => Build(key.System, key.Body))).Value;

    private static Entry? Build(ulong systemAddress, uint bodyPath)
    {
        Galaxy.Galaxy galaxy = Universe.Value;
        StarSystem? system;
        try
        {
            system = galaxy.GetSystem(new SystemAddress(systemAddress));
        }
        catch (ArgumentException)
        {
            return null;
        }
        Body? body = system?.Bodies.FirstOrDefault(b => b.Path == bodyPath);
        if (system == null || body?.State == null)
        {
            return null;
        }
        return new Entry(system, body, PlanetSurface.For(galaxy, system, body));
    }
}
