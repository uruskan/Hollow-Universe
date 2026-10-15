using System.Collections.Generic;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Where the surface view's plants come from (GROUND.md §4.6, docs/WHOLE_WORLD.md §10.3): tiles of one cube-sphere level,
/// each a fixed list of <see cref="ScatterItem"/>s (a plant's kind, place, size, turn and model). The look-dev view draws
/// a surface's own scatter (<see cref="SurfacePlants"/>); the ground game draws the zone's natural objects, the ones its
/// server has (<c>NaturePlants</c>). Implementations are safe to call from worker threads.
/// </summary>
public interface IPlantSource
{
    /// <summary>The cube-sphere level of the tiles.</summary>
    int Level { get; }

    /// <summary>A tile's plants, always in the same order; with <paramref name="treesOnly"/> its trees alone (far impostors).</summary>
    IReadOnlyList<ScatterItem> Tile(TileKey tile, bool treesOnly);

    /// <summary>The lasting ID of a plant of a tile (the one the game's changes to it name).</summary>
    ulong Id(TileKey tile, in ScatterItem item);
}

/// <summary>A surface's own scatter (<see cref="ISurface.ScatterFor(TileKey, bool)"/>), at its level.</summary>
public sealed class SurfacePlants : IPlantSource
{
    private readonly ISurface _surface;

    public SurfacePlants(ISurface surface) => _surface = surface;

    public int Level => _surface.ScatterLevel;

    public IReadOnlyList<ScatterItem> Tile(TileKey tile, bool treesOnly) => _surface.ScatterFor(tile, treesOnly);

    public ulong Id(TileKey tile, in ScatterItem item) => ScatterNaturalObjects.IdOf(tile, item.Index);
}
