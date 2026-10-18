using System;
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
    // Captured once on the main thread. Jobs never read the Node, RenderFrame, graphics settings or native resources.
    private sealed record BuildContext(ISurface Surface, Vector3D East, Vector3D Up, Vector3D South,
        long FaceCells, int HeightShift, int LatticeSize, int MaximumFaces, float Density, int SpatialCells);

    private sealed class WorkerLifetime
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private int _pending;
        private bool _stopped, _disposed;
        public GrassWorkMonitor Monitor { get; } = new();

        public Task<FieldData> Start(BuildContext context, Vector3D anchor)
        {
            lock (_gate)
            {
                if (_stopped) throw new InvalidOperationException("A retired grass field cannot start work.");
                CancellationToken token = _cancellation.Token;
                _pending++;
                Monitor.Started();
                Task<FieldData> task;
                try { task = Task.Run(() => Build(context, anchor, token), token); }
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

        private void Finish(Task<FieldData> task)
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
    private BuildContext? _buildContext;
    private bool _setupStarted, _setupComplete, _stopped, _requiredJobFailed;
    private int _epoch, _jobEpoch;

    public bool IsStopped => _stopped;
    public GrassWorkMonitor WorkMonitor => _workers.Monitor;

    private void BeginSetup()
    {
        if (_setupStarted || _stopped) throw new InvalidOperationException("GrassField.Setup is single-use; create a new field for a new world or settings.");
        _setupStarted = true;
    }

    private BuildContext CaptureBuildContext(ISurface surface, RenderFrame frame) =>
        new(surface, frame.DirectionToBody(Vector3.Right), frame.DirectionToBody(Vector3.Up),
            frame.DirectionToBody(Vector3.Back), _faceCells, _heightShift, _latticeSize, _grids.Length, BuiltWith.Density, _spatialCells);

    /// <summary>Main-thread, idempotent retirement. Cancels without waiting; call before QueueFree on replacement.</summary>
    public void Stop()
    {
        if (_stopped) { StopDrawnContact(); return; }
        _stopped = true;
        _epoch++;
        _ready = false;
        _workers.Stop();
        StopDrawnContact();
        if (_job != null) _workers.Monitor.Discarded();
        _job = null;
        _field = null;
        _buildContext = null;
        _surface = null!;
        _frame = null!;
        _coverCandidates.Clear();
        _coverCandidates.TrimExcess();
        _coverCount = 0;
        _diagnosticGeometry.Clear();
        Visible = false;
        foreach (FaceGrid? grid in _grids)
        {
            if (grid == null) continue;
            grid.Nodes?.Dispose(); grid.Nodes = null;
            grid.Cover?.Dispose(); grid.Cover = null;
            grid.NodesTexture?.Dispose(); grid.NodesTexture = null;
            grid.CoverTexture?.Dispose(); grid.CoverTexture = null;
        }
        foreach (CoverBatch? batch in _coverBatches)
        {
            if (batch == null) continue;
            batch.Buffer.Clear();
            batch.Buffer.TrimExcess();
        }
        StopSpatialResources();
    }

    public override void _ExitTree() => Stop();
}
