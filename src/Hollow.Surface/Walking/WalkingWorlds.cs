using System;
using System.Collections.Concurrent;

namespace Hollow.Surface;

/// <summary>
/// The walking-scale grounds of bodies, built once per process and shared (docs/WALKING_GROUND.md §8), as
/// <see cref="PlanetSurfaces"/> shares the real ones: a server, its clients' prediction in the same process and a
/// hundred bots all draw from one <see cref="WalkingWorld"/>, a pure function of the body and its game radius (building
/// one takes about half a second and some 15 MB). Thread-safe.
/// </summary>
public static class WalkingWorlds
{
    private static readonly ConcurrentDictionary<(ulong System, uint Body, double Radius), Lazy<WalkingWorld?>> Cache = new();

    /// <summary>The walking-scale ground of a body by address at a game radius, or null if the universe has no such body (untrusted: not found).</summary>
    public static WalkingWorld? Find(ulong systemAddress, uint bodyPath, double radius) =>
        Cache.GetOrAdd((systemAddress, bodyPath, radius), key => new Lazy<WalkingWorld?>(() =>
            PlanetSurfaces.Find(key.System, key.Body) is { } found ? WalkingWorld.For(found.System, found.Body, key.Radius) : null)).Value;
}
