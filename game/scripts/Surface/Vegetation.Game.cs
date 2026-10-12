using System;
using Hollow.Surface;

namespace HollowUniverse.Surface;

// What the ground game changed about scatter objects (GROUND.md §6.1): felled trees, picked ferns and berry bushes.
// Additive to the plants of the surface view: the view asks, per object, which kind to draw it as.
public sealed partial class Vegetation
{
    /// <summary>A kind no species has: an object drawn as this isn't drawn at all.</summary>
    public const ScatterKind NotDrawn = (ScatterKind)255;

    /// <summary>
    /// Set by the ground game: the kind to draw a scatter object of a tile as: its own, another (a picked berry bush as
    /// a plain bush), or <see cref="NotDrawn"/> (felled, picked). Null: everything as generated. Called while meshes are
    /// rebuilt, on the main thread; call <see cref="Refresh"/> when an answer changes.
    /// </summary>
    public Func<TileKey, ScatterItem, ScatterKind>? Changes { get; set; }

    private ScatterKind DrawnKind(TileKey tile, in ScatterItem item) => Changes == null ? item.Kind : Changes(tile, item);

    /// <summary>Draws a scatter tile again after the game changed its objects: the near meshes, and its impostor block.</summary>
    public void Refresh(TileKey tile)
    {
        _nearDirty = true;
        int shift = Scatter.Level - BlockLevel;
        var block = new TileKey(tile.Face, BlockLevel, tile.X >> shift, tile.Y >> shift);
        if (_blocks.TryGetValue(block.Packed, out Block? b) && b.Built)
        {
            b.Instance?.QueueFree();
            b.Instance = null;
            b.Built = false;
        }
    }
}
