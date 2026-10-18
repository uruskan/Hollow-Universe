using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public sealed partial class Vegetation
{
    // The session is managed-only. Neither its threads nor task observers retain the Vegetation Node.
    private sealed class WorkSession
    {
        private readonly object _gate = new();
        private readonly List<(double Priority, TileKey Key, bool Full)> _waiting = new();
        private readonly Queue<(TileKey Key, bool Full, IReadOnlyList<ScatterItem> Items)> _results = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IPlantSource? _source;
        private int _threads, _scatter, _tasks;
        private bool _closed;
        private int _faults, _cancelled, _discarded;
        private string? _lastError;
        public Task Stopped => _stopped.Task;
        public int Count { get { lock (_gate) return _waiting.Count; } }
        public int Busy { get { lock (_gate) return _scatter; } }
        public int Faults { get { lock (_gate) return _faults; } }
        public string? LastError { get { lock (_gate) return _lastError; } }
        public (int Scatter, int Tasks, int Cancelled, int Discarded) Counts
        { get { lock (_gate) return (_scatter, _tasks, _cancelled, _discarded); } }

        public void Start(IPlantSource source)
        {
            lock (_gate)
            {
                if (_closed || _source != null) throw new InvalidOperationException("Vegetation workers are single-use.");
                _source = source;
                for (int i = 0; i < 2; i++)
                {
                    _threads++;
                    try { new Thread(Work) { IsBackground = true, Name = $"Scatter {i}", Priority = ThreadPriority.BelowNormal }.Start(); }
                    catch { _threads--; throw; }
                }
            }
        }

        public bool Add(double priority, TileKey key, bool full)
        {
            lock (_gate)
            {
                if (_closed) return false;
                _waiting.Add((priority, key, full));
                _scatter++;
                Monitor.Pulse(_gate);
                return true;
            }
        }

        public bool TakeResult(out (TileKey Key, bool Full, IReadOnlyList<ScatterItem> Items) result)
        {
            lock (_gate) return _results.TryDequeue(out result);
        }

        private void Work()
        {
            try
            {
                while (true)
                {
                    (double Priority, TileKey Key, bool Full) request;
                    IPlantSource source;
                    lock (_gate)
                    {
                        while (!_closed && _waiting.Count == 0) Monitor.Wait(_gate);
                        if (_closed) return;
                        int best = 0;
                        for (int i = 1; i < _waiting.Count; i++)
                            if (_waiting[i].Priority < _waiting[best].Priority) best = i;
                        request = _waiting[best];
                        _waiting.RemoveAt(best);
                        source = _source!;
                    }
                    IReadOnlyList<ScatterItem>? items = null;
                    string? error = null;
                    try { items = source.Tile(request.Key, treesOnly: !request.Full); }
                    catch (Exception e) { error = $"Scatter {request.Key} failed: {e}"; }
                    lock (_gate)
                    {
                        _scatter--;
                        if (error != null) { _faults++; _lastError = error; }
                        else if (_closed) _discarded++;
                        else _results.Enqueue((request.Key, request.Full, items!));
                    }
                }
            }
            finally
            {
                lock (_gate) { _threads--; FinishStop(); }
            }
        }

        public Task<T> Run<TInput, T>(TInput input, Func<TInput, CancellationToken, T> build)
        {
            lock (_gate)
            {
                if (_closed) throw new InvalidOperationException("Retired vegetation cannot start work.");
                CancellationToken token = _cancel.Token;
                _tasks++;
                Task<T> task;
                try { task = Task.Run(() => build(input, token), token); }
                catch { _tasks--; throw; }
                _ = task.ContinueWith(static (done, state) => ((WorkSession)state!).TaskFinished(done), this,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return task;
            }
        }

        private void TaskFinished(Task task)
        {
            string? error = task.Exception?.ToString(); // Observe even if the native owner has gone away.
            lock (_gate)
            {
                _tasks--;
                if (task.IsCanceled) _cancelled++;
                else if (error != null) { _faults++; _lastError = error; }
                else if (_closed) _discarded++;
                FinishStop();
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                _cancelled += _waiting.Count;
                _scatter -= _waiting.Count;
                _waiting.Clear();
                _discarded += _results.Count;
                _results.Clear();
                _cancel.Cancel();
                Monitor.PulseAll(_gate);
                FinishStop();
            }
        }

        private void FinishStop()
        {
            if (!_closed || _threads != 0 || _tasks != 0) return;
            _source = null;
            _cancel.Dispose();
            _stopped.TrySetResult(true);
        }
    }

    // Seed-only shared appearance work. Its CPU Images belong to this holder until the main thread takes them.
    // It never retains a planet, source, RenderFrame or Node; abandoned results are disposed on its completion thread.
    private sealed class LibraryBuild
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<bool> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private FloraLibrary.Data? _result;
        private string? _error;
        private bool _done, _closed;
        public Task Finished => _finished.Task;
        public LibraryBuild(int seed)
        {
            _ = Task.Run(() => FloraLibrary.Build(seed)).ContinueWith(static (task, state) =>
            {
                var owner = (LibraryBuild)state!;
                lock (owner._gate)
                {
                    try
                    {
                        owner._error = task.Exception?.ToString();
                        if (task.IsCompletedSuccessfully)
                        {
                            if (owner._closed) DisposeData(task.Result);
                            else owner._result = task.Result;
                        }
                    }
                    catch (Exception error)
                    {
                        owner._error = error.ToString();
                        owner._finished.TrySetException(error);
                    }
                    finally
                    {
                        owner._done = true;
                        owner._finished.TrySetResult(true);
                    }
                }
            }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        public bool TryTake(out FloraLibrary.Data? data, out string? error)
        {
            lock (_gate)
            {
                data = null; error = null;
                if (!_done || _closed) return false;
                data = _result; _result = null; error = _error;
                return true;
            }
        }
        public void Stop()
        {
            lock (_gate)
            {
                _closed = true;
                FloraLibrary.Data? result = _result; _result = null;
                if (result != null) DisposeData(result);
            }
        }
    }

    private static void DisposeData(FloraLibrary.Data data)
    {
        data.Atlas.Dispose(); data.Bark.Dispose(); data.BarkNormal.Dispose();
        data.Birch.Dispose(); data.BirchNormal.Dispose(); data.Species.Clear();
    }

    private readonly record struct Axes(Vector3D East, Vector3D Up, Vector3D South)
    {
        public Vector3 ToRender(Vector3D value) => new((float)Vector3D.Dot(value, East),
            (float)Vector3D.Dot(value, Up), (float)Vector3D.Dot(value, South));
    }
    private sealed record VariantInput(int Id, int ImpostorRow, float Packed);
    private sealed record SpeciesInput(float Scale, FloraLibrary.Bands Bands, bool Tree, VariantInput[] Variants);
    private readonly record struct PlantInput(TileKey Tile, ScatterItem Item, ScatterKind Kind);
    private sealed record AppearanceInput(double Radius, Axes Axes, float Detail, bool HasImpostors,
        Dictionary<ScatterKind, SpeciesInput> Species);
    private sealed record NearInput(AppearanceInput Appearance, PlantInput[] Plants, Vector3D Camera,
        Vector3D Anchor, float Clock, int Rebuild, Dictionary<(ulong Tile, int Index), LodState> Lods);
    private sealed record BlockInput(AppearanceInput Appearance, PlantInput[] Plants, Vector3D Anchor);
    private readonly List<FloraLibrary.Variant> _variants = new();
    private readonly Dictionary<FloraLibrary.Variant, int> _variantIds = new();
    private bool _setup, _requiredFailed;
    private Task<ImpostorBaker.Result>? _bakeTask;
    private readonly TaskCompletionSource<bool> _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _reportedFaults, _assetFailures;

    public bool IsStopped => _stopping;
    /// <summary>Stable handle: completes after Stop drains world queries and seed-only appearance cleanup; never wait synchronously.</summary>
    public Task WorkersStopped => _retired.Task;
    public int FailedJobs => _jobs.Faults + _assetFailures;

    private AppearanceInput CaptureAppearance()
    {
        var species = new Dictionary<ScatterKind, SpeciesInput>();
        foreach (FloraLibrary.Species s in _library!.All)
        {
            var variants = new VariantInput[s.Variants.Count];
            for (int i = 0; i < variants.Length; i++)
            {
                FloraLibrary.Variant v = s.Variants[i];
                variants[i] = new(_variantIds[v], v.ImpostorRow, Pack(v));
            }
            species[s.Kind] = new(s.Scale, s.Bands, s.Tree, variants);
        }
        return new(_surface.Radius, new(_frame.DirectionToBody(Vector3.Right), _frame.DirectionToBody(Vector3.Up),
            _frame.DirectionToBody(Vector3.Back)), Detail, _impostors != null, species);
    }

    // Resolve all callbacks on the main thread. Workers receive values only, including every gameplay override.
    private PlantInput[] CapturePlants(List<TileKey> tiles, bool fullOnly, Func<TileKey, ScatterItem, ScatterKind> kinds)
    {
        var plants = new List<PlantInput>();
        foreach (TileKey tile in tiles)
        {
            IReadOnlyList<ScatterItem>? items = _full.TryGetValue(tile.Packed, out var f) ? f
                : !fullOnly && _trees.TryGetValue(tile.Packed, out var t) ? t : null;
            if (items == null) continue;
            foreach (ScatterItem item in items) plants.Add(new(tile, item, kinds(tile, item)));
        }
        return plants.ToArray();
    }

    private NearInput CaptureNear(List<TileKey> tiles, Vector3D camera, Func<TileKey, ScatterItem, ScatterKind> kinds) =>
        new(CaptureAppearance(), CapturePlants(tiles, true, kinds), camera, camera, Clock, _rebuild + 1,
            new Dictionary<(ulong Tile, int Index), LodState>(_lods));
    private BlockInput CaptureBlock(List<TileKey> tiles, Vector3D anchor, Func<TileKey, ScatterItem, ScatterKind> kinds) =>
        new(CaptureAppearance(), CapturePlants(tiles, false, kinds), anchor);

    private void CollectScatter()
    {
        while (_jobs.TakeResult(out var result)) (result.Full ? _full : _trees)[result.Key.Packed] = result.Items;
        Volatile.Write(ref _busy, _jobs.Busy);
        if (_reportedFaults != _jobs.Faults)
        {
            _reportedFaults = _jobs.Faults;
            GD.PushError($"Vegetation worker failed: {_jobs.LastError}");
        }
    }

    /// <summary>Main-thread nonblocking retirement; native child resources are removed by QueueFree.</summary>
    public void Stop()
    {
        if (_stopping) return;
        _stopping = true;
        _jobs.Stop();
        _data?.Stop();
        _baker?.Stop();
        Task assets = _baker?.Stopped ?? Task.CompletedTask;
        Task retirement = Task.WhenAll(_jobs.Stopped, _data?.Finished ?? Task.CompletedTask, assets);
        // Baker owns discarded atlas results, including completion between the last update and retirement.
        _bakeTask = null; _data = null; _baker = null;
        _nearJob = null; _blockJobs.Clear(); _blockAgain.Clear();
        Changes = null; ChangesSnapshot = null; Lineup = null;
        _surface = null!; _plants = null!; _frame = null!;
        _full.Clear(); _trees.Clear(); _requested = new(); _lods.Clear();
        foreach (LodSet set in _sets.Values) set.Buffer.Clear();
        _sets.Clear(); _blocks.Clear(); _variants.Clear(); _variantIds.Clear();
        _nearImpostorBuffer.Clear(); _nearImpostorBuffer.TrimExcess();
        _library = null; _impostors = null; _nearImpostors = null; _quad = null!;
        _groundAlbedo = null!; _groundNormal = null!;
        _busy = 0; _nearMissing = 0; MeshInstances = ImpostorInstances = 0;
        Visible = false;
        _ = retirement.ContinueWith(static (done, state) =>
        {
            var completion = (TaskCompletionSource<bool>)state!;
            if (done.IsFaulted) completion.TrySetException(done.Exception!.InnerExceptions);
            else completion.TrySetResult(true);
        }, _retired, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public override void _ExitTree() => Stop();
}
