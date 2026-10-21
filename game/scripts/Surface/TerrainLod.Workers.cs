using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class TerrainLod
{
    // A setup owns one managed session. Workers never capture TerrainLod, RenderFrame, a Godot resource, or a
    // replacement surface. Job.Node is a main-thread publication target only; workers use its copied Key.
    private sealed class BuildSession
    {
        private ISurface? _surface;
        private readonly object _gate = new();
        private readonly List<Job> _queue = new();
        private readonly Queue<Job> _done = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _workerCount;
        private readonly WorkReservation _reservation;
        private readonly int _maximumOutstanding;
        private int _remaining, _pending, _failed, _builds;
        private long _buildTicks;
        private bool _stopped;

        public BuildSession(ISurface surface, int workers, WorkReservation reservation, int maximumOutstanding)
        {
            _surface = surface;
            _reservation = reservation;
            _maximumOutstanding = maximumOutstanding;
            _workerCount = _remaining = workers;
            for (int i = 0; i < workers; i++)
                new Thread(Work) { IsBackground = true, Name = $"Terrain {i}", Priority = ThreadPriority.BelowNormal }.Start();
        }

        public Task Completion => _finished.Task;
        public bool Stopped { get { lock (_gate) return _stopped; } }
        public int Pending { get { lock (_gate) return _pending; } }
        public int Failed { get { lock (_gate) return _failed; } }
        public (int Queued, int Completed) QueueCounts { get { lock (_gate) return (_queue.Count, _done.Count); } }
        public double BuildMicroseconds
        {
            get { lock (_gate) return _builds == 0 ? 0 : _buildTicks * 1e6 / Stopwatch.Frequency / _builds; }
        }

        public bool TryRequest(Job job)
        {
            lock (_gate)
            {
                if (_stopped) throw new InvalidOperationException("Cannot schedule terrain on a retired world.");
                if (!_reservation.TryAcquire(_maximumOutstanding)) return false;
                _queue.Add(job);
                _pending++;
                _signal.Release();
                return true;
            }
        }

        public void Cancel(Job job)
        {
            lock (_gate)
            {
                job.Cancelled = true;
                if (_queue.Remove(job)) { _pending--; _reservation.Release(); _signal.Wait(0); }
            }
        }

        public void Reprioritize(Func<Job, double> priority)
        {
            lock (_gate)
                foreach (Job job in _queue) job.Priority = priority(job);
        }

        public bool TryTake(out Job? job)
        {
            lock (_gate)
            {
                if (!_done.TryDequeue(out job)) return false;
                _pending--;
                _reservation.Release();
                return true;
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_stopped) return;
                _stopped = true;
                foreach (Job job in _queue) job.Cancelled = true;
                _pending -= _queue.Count + _done.Count;
                _reservation.Release(_queue.Count + _done.Count);
                foreach (Job job in _done) { job.Grid = null; job.Data = null; }
                _queue.Clear();
                _done.Clear();
                _signal.Release(_workerCount);
            }
        }

        private void Work()
        {
            ISurface surface = _surface!;
            try
            {
                while (true)
                {
                    _signal.Wait();
                    Job? job = null;
                    lock (_gate)
                    {
                        if (_stopped) return;
                        int best = -1;
                        for (int i = 0; i < _queue.Count; i++)
                            if (best < 0 || _queue[i].Priority < _queue[best].Priority ||
                                _queue[i].Priority == _queue[best].Priority && _queue[i].Key.Packed < _queue[best].Key.Packed) best = i;
                        if (best >= 0)
                        {
                            job = _queue[best];
                            _queue.RemoveAt(best);
                        }
                    }
                    if (job == null) continue;
                    long start = Stopwatch.GetTimestamp();
                    try
                    {
                        if (!job.Cancelled)
                        {
                            job.Grid = TileMesh.Build(surface, job.Key);
                            if (!job.Cancelled && !Stopped) job.Data = TileMeshData.From(job.Grid);
                        }
                    }
                    catch (Exception error) { job.Error = error; }
                    lock (_gate)
                    {
                        _buildTicks += Stopwatch.GetTimestamp() - start;
                        _builds++;
                        if (job.Error != null) _failed++;
                        if (_stopped || job.Cancelled)
                        {
                            _pending--;
                            _reservation.Release();
                            // The retired tree and result arrays must not be retained by the session.
                            job.Grid = null;
                            job.Data = null;
                        }
                        else _done.Enqueue(job);
                    }
                }
            }
            finally
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    _surface = null;
                    _signal.Dispose();
                    _finished.TrySetResult();
                }
            }
        }
    }
}
