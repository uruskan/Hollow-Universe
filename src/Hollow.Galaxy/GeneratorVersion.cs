using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>
/// Generator versions, one per stage of the universe pipeline (ARCHITECTURE.md §8.4, GALAXY.md §14). Each version is
/// mixed into the seed of its stage, so the output of a stage is a function of (galaxy seed, address, version): a
/// shard records the versions it was generated with, and changing a stage's code for a live universe means a new
/// version, which changes that stage everywhere instead of drifting a little. Stages are independent: a new system
/// version leaves every star where it was. While the generator is in development, versions stay at 1 and changes
/// update the golden tests instead.
/// </summary>
public static class GeneratorVersion
{
    /// <summary>The galaxy stage: density model, cells, primary stars and their physics.</summary>
    public const int Galaxy = 1;

    /// <summary>The star-system stage: companion stars, planets, moons, rings and belts.</summary>
    public const int System = 1;

    /// <summary>The surface stage (milestone M4): terrain, biomes, resources. Reserved; nothing reads it yet.</summary>
    public const int Surface = 1;

    /// <summary>The seed of the galaxy stage for a universe seed.</summary>
    public static ulong GalaxySeed(ulong universeSeed) => GalaxySeed(universeSeed, Galaxy);

    /// <summary>The seed of the system stage for a universe seed.</summary>
    public static ulong SystemSeed(ulong universeSeed) => SystemSeed(universeSeed, System);

    /// <summary>The seed of the surface stage for a universe seed (for M4).</summary>
    public static ulong SurfaceSeed(ulong universeSeed) => Hash64.Of(universeSeed, Streams.SurfaceStage, (ulong)Surface);

    internal static ulong GalaxySeed(ulong universeSeed, int version) =>
        Hash64.Of(universeSeed, Streams.GalaxyStage, (ulong)version);

    internal static ulong SystemSeed(ulong universeSeed, int version) =>
        Hash64.Of(universeSeed, Streams.SystemStage, (ulong)version);
}
