using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>A managed-only observation handle which remains valid after its GrassField leaves the scene.</summary>
public sealed class GrassWorkMonitor
{
    private readonly object _gate = new();
    private int _started, _pending, _completed, _cancelled, _faulted, _discarded;
    private string? _lastError;
    private TaskCompletionSource<bool>? _idle;

    internal void Started()
    {
        lock (_gate)
        {
            if (_pending == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _started++;
            _pending++;
        }
    }
    internal void Finished(bool cancelled, string? error)
    {
        lock (_gate)
        {
            _pending--;
            if (cancelled) _cancelled++;
            else if (error != null) { _faulted++; _lastError = error; }
            else _completed++;
            if (_pending == 0) _idle?.TrySetResult(true);
        }
    }
    internal void Discarded() { lock (_gate) _discarded++; }
    public GrassWorkSnapshot Snapshot()
    {
        lock (_gate) return new(_started, _pending, _completed, _cancelled, _faulted, _discarded, _lastError);
    }
    /// <summary>Completion of the currently pending jobs. Capture after Stop when no further jobs can start.</summary>
    public Task WhenIdle { get { lock (_gate) return _idle?.Task ?? Task.CompletedTask; } }
}

public readonly record struct GrassWorkSnapshot(int Started, int Pending, int Completed, int Cancelled,
    int Faulted, int Discarded, string? LastError);

public sealed partial class GrassField
{
    // Tile builds running at once: more while nothing is drawn yet (loading), fewer in play beside the other workers.
    private const int MaxJobs = 2, MaxJobsLoading = 4;

    private sealed class WorkerLifetime
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private int _pending;
        private bool _stopped, _disposed;
        public GrassWorkMonitor Monitor { get; } = new();

        public Task<TileBuild> Start(BuildContext context, TileJob job)
        {
            lock (_gate)
            {
                if (_stopped) throw new InvalidOperationException("A retired grass field cannot start work.");
                CancellationToken token = _cancellation.Token;
                _pending++;
                Monitor.Started();
                Task<TileBuild> task;
                try { task = Task.Run(() => BuildTile(context, job, token), token); }
                catch (Exception error)
                {
                    _pending--;
                    Monitor.Finished(false, error.ToString());
                    throw;
                }
                // This observer owns no Node. Reading Exception observes faults even after the view is freed.
                _ = task.ContinueWith(static (finished, state) => ((WorkerLifetime)state!).Finish(finished), this,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return task;
            }
        }

        private void Finish(Task<TileBuild> task)
        {
            string? error = task.Exception?.ToString();
            lock (_gate)
            {
                _pending--;
                Monitor.Finished(task.IsCanceled, error);
                if (_stopped && _pending == 0) DisposeCancellation();
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_stopped) return;
                _stopped = true;
                _cancellation.Cancel();
                if (_pending == 0) DisposeCancellation();
            }
        }

        private void DisposeCancellation()
        {
            if (_disposed) return;
            _disposed = true;
            _cancellation.Dispose();
        }
    }

    private readonly WorkerLifetime _workers = new();
    // Builds whose tiles were released while they ran: observed until they finish, their results never published.
    private readonly List<TileJob> _orphans = new();
    private bool _setupStarted, _setupComplete, _stopped, _requiredJobFailed;

    public bool IsStopped => _stopped;
    public GrassWorkMonitor WorkMonitor => _workers.Monitor;

    private void BeginSetup()
    {
        if (_setupStarted || _stopped) throw new InvalidOperationException("GrassField.Setup is single-use; create a new field for a new world or settings.");
        _setupStarted = true;
    }

    private void DrainOrphans()
    {
        for (int i = _orphans.Count - 1; i >= 0; i--)
        {
            if (!_orphans[i].Task.IsCompleted) continue;
            _workers.Monitor.Discarded();
            _orphans.RemoveAt(i);
        }
    }

    /// <summary>Main-thread, idempotent retirement. Cancels without waiting; call before QueueFree on replacement.</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _workers.Stop();
        foreach (GrassTile tile in _tiles.Values)
        {
            if (tile.Job != null) _workers.Monitor.Discarded();
            tile.Job = null;
            foreach (Batch batch in tile.Batches) FreeBatch(batch);
            tile.Batches.Clear();
            tile.Built = tile.Pending = null;
        }
        foreach (TileJob orphan in _orphans) _workers.Monitor.Discarded();
        _orphans.Clear();
        _tiles.Clear();
        _uploadQueue.Clear();
        foreach (Stack<Batch> free in _pool.Values)
            while (free.Count > 0) FreeBatch(free.Pop());
        _pool.Clear();
        _context = null;
        _surface = null!;
        _frame = null!;
        _terrain = null;
        _instances = 0;
        StopAuthoredCover();
        Visible = false;
    }

    public override void _ExitTree() => Stop();
}
