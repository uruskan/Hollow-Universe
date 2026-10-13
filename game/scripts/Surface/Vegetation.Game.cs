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
    /// Set by the ground game: the meshes near the camera are then rebuilt on a worker thread (10–15 ms of work every
    /// 8 m walked and whenever the game changes a plant), and the old ones show until the new are ready. It returns
    /// <see cref="Changes"/> frozen as they are when a rebuild starts: a function safe to call from another thread.
    /// Null: rebuilt on the main thread, as the planet viewer does.
    /// </summary>
    public Func<Func<TileKey, ScatterItem, ScatterKind>>? ChangesSnapshot { get; set; }

    /// <summary>How many times the meshes near the camera were put in place (the game notes it when measuring).</summary>
    public int NearBuilds { get; private set; }

    private Task<NearData>? _nearJob;

    private sealed record NearData(Vector3D Anchor, Dictionary<(FloraLibrary.Variant, int), float[]> Buffers, int Count);

    private void RebuildNear(List<TileKey> tiles, Vector3D camera)
    {
        if (ChangesSnapshot == null)
        {
            BuildNear(tiles, camera);
            NearBuilds++;
            return;
        }
        if (_nearJob != null)
        {
            return; // one at a time: what asked for this one is still there when it lands
        }
        _nearCentre = camera;
        _nearDirty = false;
        Func<TileKey, ScatterItem, ScatterKind> kinds = ChangesSnapshot();
        var list = new List<TileKey>(tiles);
        Vector3D anchor = _frame.Origin;
        _nearJob = Task.Run(() => NearBuffers(list, camera, anchor, kinds));
    }

    // Puts a finished worker build in place (on the main thread: the upload of the buffers).
    private void ApplyNear()
    {
        if (_nearJob is not { IsCompleted: true } job)
        {
            return;
        }
        _nearJob = null;
        if (!job.IsCompletedSuccessfully)
        {
            _nearDirty = true;
            return;
        }
        NearData data = job.Result;
        _nearAnchor = data.Anchor;
        foreach (((FloraLibrary.Variant, int) key, Set set) in _sets)
        {
            set.Buffer.Clear();
            if (data.Buffers.TryGetValue(key, out float[]? buffer))
            {
                set.Buffer.AddRange(buffer);
            }
            Fill(set);
        }
        MeshInstances = data.Count;
        NearBuilds++;
    }

    // On a worker: what BuildNear lays out, into buffers of its own.
    private NearData NearBuffers(List<TileKey> tiles, Vector3D camera, Vector3D anchor, Func<TileKey, ScatterItem, ScatterKind> kinds)
    {
        var buffers = new Dictionary<(FloraLibrary.Variant, int), List<float>>();
        List<float> Buffer(FloraLibrary.Variant variant, int lod)
        {
            if (!buffers.TryGetValue((variant, lod), out List<float>? b))
            {
                buffers[(variant, lod)] = b = new List<float>();
            }
            return b;
        }
        int count = 0;
        double margin = 2 * RebuildDistance;
        foreach (TileKey key in tiles)
        {
            if (!_full.TryGetValue(key.Packed, out List<ScatterItem>? items))
            {
                continue;
            }
            foreach (ScatterItem item in items)
            {
                FloraLibrary.Species? species = _library!.For(kinds(key, item));
                if (species == null || species.Variants.Count == 0)
                {
                    continue;
                }
                Vector3D body = item.Position(_surface.Radius);
                double d = Vector3D.Distance(body, camera);
                FloraLibrary.Bands bands = species.Bands;
                if (d > bands.Far + margin)
                {
                    continue;
                }
                FloraLibrary.Variant variant = species.Variants[item.Variant % species.Variants.Count];
                float scale = item.Scale * species.Scale;
                float sink = item.Kind == ScatterKind.Rock ? -0.12f * scale : -0.05f;
                Transform3D t = InstanceTransform(item, body, anchor, scale, sink);
                (float a, float b) = Shade(item);
                if (d < bands.Near + margin)
                {
                    Append(Buffer(variant, 0), t, a, b, 0, 0);
                }
                if (d > bands.Near - FadeWidth - margin)
                {
                    Append(Buffer(variant, 1), t, a, b, 0, 0);
                }
                count++;
            }
        }
        var arrays = new Dictionary<(FloraLibrary.Variant, int), float[]>(buffers.Count);
        foreach (((FloraLibrary.Variant, int) key, List<float> b) in buffers)
        {
            arrays[key] = b.ToArray();
        }
        return new NearData(anchor, arrays, count);
    }

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
