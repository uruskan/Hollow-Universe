using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

// What the ground game changed about scatter objects (GROUND.md §6.1): felled trees, picked ferns and berry bushes.
// Additive to the plants of the surface view: the view asks, per object, which kind to draw it as.
public sealed partial class Vegetation
{
    /// <summary>A kind no species has: an object drawn as this isn't drawn at all.</summary>
    public const ScatterKind NotDrawn = (ScatterKind)255;

    private readonly Dictionary<ulong, (Task<BlockData?> Job, Block Block)> _blockJobs = new();

    private sealed record BlockData(float[] Buffer, List<(TileKey Tile, ScatterItem Item)> Items);

    /// <summary>
    /// Set by the ground game: the kind to draw a scatter object of a tile as: its own, another (a picked berry bush as
    /// a plain bush), or <see cref="NotDrawn"/> (felled, picked). Null: everything as generated. Called while meshes are
    /// rebuilt, on the main thread; call <see cref="Refresh"/> when an answer changes.
    /// </summary>
    public Func<TileKey, ScatterItem, ScatterKind>? Changes { get; set; }

    private ScatterKind DrawnKind(TileKey tile, in ScatterItem item) => Changes == null ? item.Kind : Changes(tile, item);

    /// <summary>
    /// Draws a scatter tile again after the game changed its objects: the meshes near the camera (<paramref name="near"/>,
    /// at the next update), and its impostor block (<paramref name="trees"/>: impostors are trees only). A block of
    /// twenty to thirty thousand impostors takes 30–50 ms to build, so a built one is rebuilt on a worker thread and
    /// swapped in by <see cref="ApplyRefreshes"/>; the old impostors show meanwhile.
    /// </summary>
    public void Refresh(TileKey tile, bool near = true, bool trees = true)
    {
        _nearDirty |= near;
        if (!trees)
        {
            return;
        }
        int shift = Scatter.Level - BlockLevel;
        var key = new TileKey(tile.Face, BlockLevel, tile.X >> shift, tile.Y >> shift);
        if (!_blocks.TryGetValue(key.Packed, out Block? b) || !b.Built || _blockJobs.ContainsKey(key.Packed))
        {
            return; // not built yet: it will be, with the change
        }
        if (b.Instance == null || _library == null)
        {
            b.Built = false;
            return;
        }
        Vector3D anchor = b.Anchor;
        _blockJobs[key.Packed] = (Task.Run(() => BlockBuffer(key, anchor)), b);
    }

    /// <summary>Puts impostor blocks rebuilt on workers in place (call every frame).</summary>
    public void ApplyRefreshes()
    {
        if (_blockJobs.Count == 0)
        {
            return;
        }
        var done = new List<ulong>();
        foreach ((ulong key, (Task<BlockData?> job, Block block)) in _blockJobs)
        {
            if (!job.IsCompleted)
            {
                continue;
            }
            done.Add(key);
            if (!job.IsCompletedSuccessfully || job.Result is not { } data || block.Instance == null || !_blocks.ContainsKey(key))
            {
                continue;
            }
            // What the game removed doesn't show: its impostor is scaled to nothing.
            float[] buffer = data.Buffer;
            for (int i = 0; i < data.Items.Count; i++)
            {
                (TileKey tile, ScatterItem item) = data.Items[i];
                if (DrawnKind(tile, item) != item.Kind)
                {
                    int k = i * 16;
                    buffer[k] = buffer[k + 1] = buffer[k + 2] = 0;
                    buffer[k + 4] = buffer[k + 5] = buffer[k + 6] = 0;
                    buffer[k + 8] = buffer[k + 9] = buffer[k + 10] = 0;
                }
            }
            MultiMesh mm = block.Instance.Multimesh;
            mm.InstanceCount = buffer.Length / 16;
            mm.Buffer = buffer;
        }
        foreach (ulong key in done)
        {
            _blockJobs.Remove(key);
        }
    }

    // On a worker: every tree impostor of a block, as TryBuildBlock lays them out (the game's changes are applied on
    // the main thread). Null when a tile's scatter isn't there any more.
    private BlockData? BlockBuffer(TileKey blockKey, Vector3D anchor)
    {
        int span = 1 << (Scatter.Level - BlockLevel);
        var buffer = new List<float>();
        var items = new List<(TileKey, ScatterItem)>();
        for (int y = 0; y < span; y++)
        {
            for (int x = 0; x < span; x++)
            {
                var key = new TileKey(blockKey.Face, Scatter.Level, blockKey.X * span + x, blockKey.Y * span + y);
                List<ScatterItem>? list = _full.TryGetValue(key.Packed, out var f) ? f : _trees.TryGetValue(key.Packed, out var t) ? t : null;
                if (list == null)
                {
                    return null;
                }
                foreach (ScatterItem item in list)
                {
                    FloraLibrary.Species? species = _library!.For(item.Kind);
                    if (species == null || !species.Tree)
                    {
                        continue;
                    }
                    FloraLibrary.Variant variant = species.Variants[item.Variant % species.Variants.Count];
                    if (variant.ImpostorRow < 0)
                    {
                        continue;
                    }
                    float scale = item.Scale * species.Scale;
                    Transform3D tr = InstanceTransform(item, item.Position(_surface.Radius), anchor, scale, -0.05f);
                    (float a, _) = Shade(item);
                    Append(buffer, tr, a, variant.ImpostorRow, variant.Extent, variant.Height);
                    items.Add((key, item));
                }
            }
        }
        return new BlockData(buffer.ToArray(), items);
    }
}
