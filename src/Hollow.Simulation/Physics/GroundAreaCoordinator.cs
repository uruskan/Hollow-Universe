using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Surface;

namespace Hollow.Simulation.Physics;

public enum GroundAreaStatus { Ready, Pending, Capacity, Failed, Cancelled, Retired }
public readonly record struct GroundAreaRequest(GroundAreaStatus Status, GroundAreaTicket? Ticket, string? Reason = null);

/// <summary>Explicit qualification limits. No selected gameplay/session consumer is enabled by these options.</summary>
public sealed record GroundPhysicsOptions
{
    public required int MaxRequestTiles { get; init; }
    public required int MaxWorkers { get; init; }
    public required int MaxAdoptionsPerPump { get; init; }
    internal void Validate(GroundStreamingBudget budget)
    {
        if (MaxRequestTiles < 1 || MaxRequestTiles > budget.Limits.MaxOutstanding ||
            MaxWorkers < 1 || MaxWorkers > budget.Limits.MaxExecuting ||
            MaxAdoptionsPerPump < 1 || MaxAdoptionsPerPump > budget.Limits.MaxCompletionSlots)
            throw new ArgumentOutOfRangeException(nameof(GroundPhysicsOptions));
    }
}

/// <summary>An owner-thread dependency ticket. Releasing one ticket cannot evict another ticket's ground.</summary>
public sealed class GroundAreaTicket : IDisposable
{
    internal GroundAreaCoordinator? Owner;
    internal GroundAreaLease? Area;
    internal GroundAreaTicket(GroundAreaCoordinator owner, GroundAreaLease area) { Owner = owner; Area = area; }
    public GroundAreaStatus State => Owner?.State(this) ?? GroundAreaStatus.Retired;
    public string? Failure => Owner?.Failure(this);
    public void Dispose() => Owner?.Release(this);
    internal void Detach() { Owner = null; Area = null; }
}

/// <summary>
/// One selected physics owner's immutable area requests and shared tile references. All public orchestration stays
/// on the owner thread. Workers hold admitted leases only; no task is created until its execution slot is reserved.
/// </summary>
internal sealed class GroundAreaCoordinator
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly GroundStreamingOwner _owner;
    private readonly GroundPhysicsOptions _options;
    private readonly ZoneFrame _frame;
    private IPlannedGroundCollision? _source;
    private Func<TileKey, SelectedGroundCollisionPlan, CancellationToken, IDisposable>? _build;
    private Action<IDisposable>? _publish;
    private readonly Dictionary<TileKey, Entry> _entries = new();
    private readonly HashSet<GroundAreaTicket> _tickets = new();
    private readonly Task?[] _workers;
    private readonly long _preparationBytes;
    private bool _closed;

    private sealed class Entry(TileKey key, GroundBuildLease work)
    {
        internal readonly TileKey Key = key;
        internal readonly GroundBuildLease Work = work;
        internal readonly CancellationTokenSource Cancellation = new();
        internal int References;
    }

    internal GroundAreaCoordinator(IPlannedGroundCollision source, ZoneFrame frame, GroundStreamingOwner owner,
        GroundPhysicsOptions options, Func<TileKey, SelectedGroundCollisionPlan, CancellationToken, IDisposable> build,
        Action<IDisposable> publish)
    {
        _source = source; _frame = frame; _owner = owner; _options = options; _build = build; _publish = publish;
        _workers = new Task?[options.MaxWorkers];
        _preparationBytes = source.CollisionPreparationBounds.BatchBytes(options.MaxRequestTiles);
    }

    internal GroundAreaRequest Request(Vector3 from, Vector3 to, float reach, CancellationToken cancellation)
    {
        CheckThread();
        if (_closed) return new(GroundAreaStatus.Retired, null);
        TileKey[] missing = Array.Empty<TileKey>();
        try
        {
            Reap();
            bool admitted = _owner.TryPrepareAreaAndAdmit(_preparationBytes, _options.MaxRequestTiles, () =>
            {
                IPlannedGroundCollision source = _source!;
                var area = GroundAreaPlan.Along(source.Radius, _frame.ToBody(from), _frame.ToBody(to), reach,
                    new GroundAreaLimits { MaxTiles = _options.MaxRequestTiles,
                        MaxEstimatedBytes = 2048L + 16L * _options.MaxRequestTiles }, cancellation);
                var keys = new List<TileKey>();
                foreach (TileKey key in area.Tiles)
                {
                    if (!_entries.TryGetValue(key, out Entry? entry)) keys.Add(key);
                    else if (entry.References == 0 || entry.Work.Failure != null || entry.Work.State == GroundWorkState.Released)
                        throw new InvalidOperationException("A required tile is failed or still retiring; release its old request before retrying.");
                }
                // Failed work can release its ledger charge while a live failed ticket still retains this Entry.
                // Bound the actual retained dictionary as well as the shared live-work count before making plans.
                if ((long)_entries.Count + keys.Count > _owner.Budget.Limits.MaxTiles)
                    throw new FormationCapacityException("Retained ground entries exhaust this owner's backing capacity; release failed requests before retrying.");
                missing = keys.ToArray();
                var plans = new SelectedGroundCollisionPlan[missing.Length];
                for (int i = 0; i < plans.Length; i++)
                    plans[i] = source.PrepareGroundCollision(_frame, missing[i], GroundCollisionLayout.MarginMetres, cancellation);
                cancellation.ThrowIfCancellationRequested();
                return (area, plans);
            }, out GroundAreaLease? held, out GroundBuildLease[] work);
            if (!admitted) return new(GroundAreaStatus.Capacity, null, "The complete area and its future output do not fit the shared ground allowance.");
            if (cancellation.IsCancellationRequested)
            {
                foreach (GroundBuildLease item in work) item.Retire();
                held!.Dispose(); return new(GroundAreaStatus.Cancelled, null);
            }
            for (int i = 0; i < missing.Length; i++) _entries.Add(missing[i], new Entry(missing[i], work[i]));
            var ticket = new GroundAreaTicket(this, held!);
            foreach (TileKey key in held!.Plan.Tiles) _entries[key].References++;
            _tickets.Add(ticket);
            return new(State(ticket), ticket);
        }
        catch (OperationCanceledException) { return new(GroundAreaStatus.Cancelled, null); }
        catch (FormationCapacityException error) { return new(GroundAreaStatus.Capacity, null, Detail(error)); }
        catch (Exception error) when (error is FormationValidationException or InvalidOperationException or ArgumentException)
        { return new(GroundAreaStatus.Failed, null, Detail(error)); }
    }

    internal GroundAreaStatus State(GroundAreaTicket ticket)
    {
        CheckThread();
        if (_closed || ticket.Owner != this || !_tickets.Contains(ticket)) return GroundAreaStatus.Retired;
        bool pending = false;
        foreach (TileKey key in ticket.Area!.Plan.Tiles)
        {
            Entry entry = _entries[key];
            if (entry.Work.Failure != null || entry.Work.State is GroundWorkState.Retiring or GroundWorkState.Released)
                return GroundAreaStatus.Failed;
            if (!entry.Work.Ready) pending = true;
        }
        return pending ? GroundAreaStatus.Pending : GroundAreaStatus.Ready;
    }

    internal string? Failure(GroundAreaTicket ticket)
    {
        CheckThread();
        if (_closed || ticket.Owner != this || !_tickets.Contains(ticket)) return null;
        foreach (TileKey key in ticket.Area!.Plan.Tiles)
            if (_entries[key].Work.Failure is { } error) return Detail(error);
        return null;
    }

    internal void Release(GroundAreaTicket ticket)
    {
        CheckThread();
        if (ticket.Owner != this || !_tickets.Remove(ticket)) return;
        List<Exception>? errors = null;
        foreach (TileKey key in ticket.Area!.Plan.Tiles)
        {
            Entry entry = _entries[key];
            if (--entry.References != 0) continue;
            entry.Cancellation.Cancel();
            try { entry.Work.Retire(); } catch (Exception error) { (errors ??= new()).Add(error); }
        }
        ticket.Area.Dispose(); ticket.Detach(); Reap();
        if (errors != null) throw new AggregateException("Released ground remains charged until native retirement succeeds.", errors);
    }

    internal void Pump(bool synchronous)
    {
        CheckThread();
        if (_closed) return;
        Reap(); Adopt();
        int started = 0;
        foreach (Entry entry in _entries.Values)
        {
            if (started == _options.MaxWorkers) break;
            if (entry.References == 0 || entry.Work.State != GroundWorkState.Queued) continue;
            int slot = synchronous ? -1 : Array.FindIndex(_workers, static task => task == null);
            if (!synchronous && slot < 0) break;
            if (!entry.Work.TryReserveRun()) continue;
            started++;
            void Run() => entry.Work.RunReserved(plan => _build!(entry.Key, plan, entry.Cancellation.Token));
            if (synchronous) Run();
            else
            {
                try { _workers[slot] = Task.Run(Run); }
                catch (Exception error) { entry.Work.RunReserved(_ => throw new InvalidOperationException("Ground worker dispatch failed.", error)); }
            }
        }
        if (synchronous) Adopt();
    }

    private void Adopt()
    {
        int count = 0;
        foreach (Entry entry in _entries.Values)
        {
            if (count == _options.MaxAdoptionsPerPump) break;
            if (entry.References > 0 && entry.Work.State == GroundWorkState.Completed)
            { entry.Work.TryAdopt(_publish!); count++; }
        }
    }

    private void Reap()
    {
        // A worker can fault only if owned native cleanup itself threw; its lease remains charged for retry.
        for (int i = 0; i < _workers.Length; i++)
            if (_workers[i] is { IsCompleted: true } task)
            { _ = task.Exception; _workers[i] = null; }
        List<TileKey>? released = null;
        foreach (Entry entry in _entries.Values)
        {
            if (entry.References == 0 && entry.Work.State == GroundWorkState.Retiring) entry.Work.Retire();
            if (entry.References == 0 && entry.Work.State == GroundWorkState.Released)
                (released ??= new()).Add(entry.Key);
        }
        if (released != null)
            foreach (TileKey key in released) { _entries[key].Cancellation.Dispose(); _entries.Remove(key); }
    }

    internal int ResidentCount
    { get { CheckThread(); int count = 0; foreach (Entry entry in _entries.Values) if (entry.Work.Ready) count++; return count; } }
    internal bool HasTile(TileKey key) { CheckThread(); return !_closed && _entries.TryGetValue(key, out Entry? entry) && entry.Work.Ready; }
    internal IEnumerable<TileKey> Tiles
    { get { CheckThread(); foreach (Entry entry in _entries.Values) if (entry.Work.Ready) yield return entry.Key; } }

    internal void StopAndDrain()
    {
        CheckThread(); _closed = true;
        foreach (GroundAreaTicket ticket in _tickets) ticket.Detach();
        _tickets.Clear();
        foreach (Entry entry in _entries.Values) { entry.References = 0; entry.Cancellation.Cancel(); }
        List<Exception>? errors = null;
        void Attempt(Action action) { try { action(); } catch (Exception error) { (errors ??= new()).Add(error); } }
        Attempt(_owner.BeginRetirement);
        for (int i = 0; i < _workers.Length; i++)
        {
            if (_workers[i] is not { } task) continue;
            Attempt(() => task.GetAwaiter().GetResult()); _workers[i] = null;
        }
        Attempt(_owner.BeginRetirement);
        Reap();
        if (_entries.Count != 0)
            throw new AggregateException("Selected ground native owners remain charged; retry retirement.", errors ?? new List<Exception>());
        _entries.TrimExcess(); _tickets.TrimExcess();
        _source = null; _build = null; _publish = null;
        // A recovered earlier cleanup fault is observable, but does not keep already-released ownership alive.
        if (errors != null) throw new AggregateException("Selected ground drained after a worker/cleanup failure.", errors);
    }

    internal void RequireOwnerThread() => CheckThread();

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Ground requests, publication and retirement belong to the physics owner thread.");
    }
    private static string Detail(Exception error) => error.Message.Length <= 512 ? error.Message : error.Message[..512];
}
