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

    private readonly Dictionary<ulong, (Task<List<float>> Job, Block Block)> _blockJobs = new();
    private readonly HashSet<ulong> _blockAgain = new();

    /// <summary>
    /// Set by the ground game: the kind to draw a scatter object of a tile as: its own, another (a picked berry bush as
    /// the same bush bare), or <see cref="NotDrawn"/> (felled, picked). Null: everything as generated. Called while meshes are
    /// rebuilt, on the main thread; call <see cref="Refresh"/> when an answer changes.
    /// </summary>
    public Func<TileKey, ScatterItem, ScatterKind>? Changes { get; set; }

    private ScatterKind DrawnKind(TileKey tile, in ScatterItem item) => Changes == null ? item.Kind : Changes(tile, item);

    /// <summary>
    /// Set by the ground game: the meshes near the camera are then rebuilt on a worker thread (10–15 ms of work every
    /// 8 m walked and whenever the game changes a plant), and the old ones show until the new are ready. It returns
    /// <see cref="Changes"/> frozen as they are when a rebuild starts. That function is evaluated on the main thread;
    /// workers receive only copied kind decisions, placement values, appearance scalars and their own LOD history.
    /// Null: rebuilt on the main thread, as the planet viewer does.
    /// </summary>
    public Func<Func<TileKey, ScatterItem, ScatterKind>>? ChangesSnapshot { get; set; }

    /// <summary>How many times the meshes near the camera were put in place (the game notes it when measuring).</summary>
    public int NearBuilds { get; private set; }

    /// <summary>Opt-in main-thread upload timings for the ground game's frame profiler.</summary>
    public bool MeasureRefreshes { get; set; }

    public double LastNearUploadMilliseconds { get; private set; }

    public long LastNearUploadBytes { get; private set; }

    private Task<NearData>? _nearJob;

    private void RebuildNear(List<TileKey> tiles, Vector3D camera)
    {
        if (_stopping || _requiredFailed || _nearJob != null) return;
        if (ChangesSnapshot == null)
        {
            BuildNear(tiles, camera);
            NearBuilds++;
            return;
        }
        _nearCentre = camera;
        _nearDirty = false;
        Func<TileKey, ScatterItem, ScatterKind> kinds = ChangesSnapshot();
        NearInput input = CaptureNear(tiles, camera, kinds);
        _nearJob = _jobs.Run(input, static (snapshot, token) => ComputeNear(snapshot, token));
    }

    // Puts a near set laid out on a worker in place (the upload of its buffers).
    private void ApplyNear()
    {
        if (_stopping) return;
        if (_nearJob is not { IsCompleted: true } job)
        {
            return;
        }
        _nearJob = null;
        if (!job.IsCompletedSuccessfully)
        {
            _requiredFailed = true;
            return;
        }
        bool measure = MeasureRefreshes;
        long began = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long bytes = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
        PlaceNear(job.Result);
        if (measure)
        {
            LastNearUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds;
            LastNearUploadBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        }
        NearBuilds++;
    }

    /// <summary>
    /// Draws a scatter tile again after the game changed its objects: the meshes near the camera (<paramref name="near"/>,
    /// at the next update), and its impostor block (<paramref name="trees"/>: impostors are trees only). A block of
    /// twenty to thirty thousand impostors takes 30–50 ms to lay out, so with <see cref="ChangesSnapshot"/> a built one
    /// is laid out on a worker thread and swapped in by <see cref="ApplyRefreshes"/>; the old impostors show meanwhile.
    /// </summary>
    public void Refresh(TileKey tile, bool near = true, bool trees = true)
    {
        if (_stopping || _requiredFailed) return;
        _nearDirty |= near;
        if (!trees)
        {
            return;
        }
        if (tile.Level != _level)
        {
            return;
        }
        int shift = _level - _blockLevel;
        var key = new TileKey(tile.Face, _blockLevel, tile.X >> shift, tile.Y >> shift);
        if (!_blocks.TryGetValue(key.Packed, out Block? b) || !b.Built)
        {
            return; // not built yet: it will be, with the change
        }
        if (_blockJobs.ContainsKey(key.Packed))
        {
            // Its worker captured an earlier state. Retain the newer change until that job is applied.
            _blockAgain.Add(key.Packed);
            return;
        }
        if (b.Instance == null || _library == null || ChangesSnapshot == null)
        {
            b.Built = false; // built again on the main thread
            return;
        }
        int span = 1 << shift;
        var tiles = new List<TileKey>(span * span);
        for (int y = 0; y < span; y++)
        {
            for (int x = 0; x < span; x++)
            {
                tiles.Add(new TileKey(key.Face, _level, key.X * span + x, key.Y * span + y));
            }
        }
        Func<TileKey, ScatterItem, ScatterKind> kinds = ChangesSnapshot();
        BlockInput input = CaptureBlock(tiles, b.Anchor, kinds);
        _blockJobs[key.Packed] = (_jobs.Run(input, static (snapshot, token) => ImpostorBuffer(snapshot, token)), b);
    }

    /// <summary>Puts impostor blocks laid out on workers in place (call every frame).</summary>
    public void ApplyRefreshes()
    {
        if (_stopping) return;
        if (_blockJobs.Count == 0)
        {
            return;
        }
        var done = new List<ulong>();
        foreach ((ulong key, (Task<List<float>> job, Block block)) in _blockJobs)
        {
            if (!job.IsCompleted)
            {
                continue;
            }
            done.Add(key);
            if (!job.IsCompletedSuccessfully || block.Instance == null || !_blocks.TryGetValue(key, out Block? drawn) ||
                !ReferenceEquals(block, drawn))
            {
                continue;
            }
            MultiMesh mm = block.Instance.Multimesh;
            mm.InstanceCount = job.Result.Count / 16;
            mm.Buffer = job.Result.ToArray();
        }
        foreach (ulong key in done)
        {
            Block prior = _blockJobs[key].Block;
            _blockJobs.Remove(key);
            if (_blockAgain.Remove(key) && _blocks.TryGetValue(key, out Block? current) && ReferenceEquals(prior, current))
            {
                int span = 1 << (_level - _blockLevel);
                Refresh(new TileKey(current.Key.Face, _level, current.Key.X * span, current.Key.Y * span), near: false);
            }
        }
    }
}
