using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class TerrainLod
{
    /// <summary>
    /// The grid a tile of the finest level is drawn with now, and its morph range: what the grass roots on (GrassField,
    /// docs/WALKING_GROUND.md §18), so a clump stands on the very triangles the terrain draws. False when that tile is not
    /// drawn (still streaming, or merged into its parent beyond the finest level's reach). Main thread; the grid's arrays
    /// are frozen once published, so a worker may read them.
    /// </summary>
    internal bool TryDrawnGrid(TileKey key, out TileGrid grid, out Vector2 morphRange)
    {
        grid = null!;
        morphRange = default;
        if (key.Level != MaxLevel || !_nodes.TryGetValue(key, out Node? node) || !node.Drawn || node.Grid == null
            || node.Instance == null || !node.Grid.Key.Equals(key)) return false;
        grid = node.Grid;
        morphRange = node.MorphRange;
        return true;
    }
}
