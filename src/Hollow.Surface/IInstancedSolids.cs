using System.Collections.Generic;

namespace Hollow.Surface;

/// <summary>
/// A surface whose ground carries solids placed whole, each one of a few shared closed shells (the cliff kit's pieces,
/// docs/WORLD_TERRAIN_PLAN.md §4.4): a physics engine can make each shell's shape once and place it many times, instead
/// of building every ground tile's mesh with copies of them.
/// </summary>
public interface IInstancedSolids
{
    /// <summary>The shells by number (<see cref="KitPlacement.Piece"/>): closed triangle meshes in their own frames.</summary>
    IReadOnlyList<CliffCollision> SolidShapes { get; }

    /// <summary>
    /// The solids whose pivot lies over a tile, added to <paramref name="into"/>: at any one level each solid is listed
    /// by exactly one tile.
    /// </summary>
    void SolidsOver(TileKey tile, List<KitPlacement> into);

    /// <summary><see cref="ISurface.GroundCollision"/> without the instanced solids.</summary>
    TriangleMesh GroundCollisionWithoutSolids(in ZoneFrame frame, TileKey tile, double margin);
}
