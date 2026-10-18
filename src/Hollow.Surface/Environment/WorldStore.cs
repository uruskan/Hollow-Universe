using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hollow.Surface;

/// <summary>
/// A bounded, session-owned cache of generated worlds. Identity uses the complete canonical descriptor, not only its
/// digest. Construction is coalesced; active leases and admitted builds are never evicted. IDisposable begins
/// retirement without waiting; await Retirement or DisposeAsync to observe completion and cleanup failures.
/// </summary>
public sealed class WorldStore : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly WorldStoreOptions _options;
    private readonly Func<PlanetEnvironmentDescriptor, CancellationToken, ISurface> _factory;
    private readonly Func<PlanetEnvironmentDescriptor, WorldMemoryEstimate> _estimate;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<Entry> _builds = new();
    private readonly TaskCompletionSource _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> _cleanupFailures = new();
    private int _residentWorlds, _activeLeases, _pendingAcquisitions, _pendingDisposals, _cleanupFailureCount;
    private long _residentBytes, _reservedResidentBytes, _reservedBuildBytes, _pendingDisposalBytes;
    private ulong _sequence;
    private bool _retiring;

    /// <param name="factory">
    /// Transfers ownership of each successfully returned surface to this store. A factory must honor descriptor.Radius,
    /// clean up its own failed construction, and not return the same independently owned object for distinct entries.
    /// It runs outside the store lock. Cancellation is cooperative; ignored cancellation does not release reservations.
    /// For sealed walking worlds it must construct under options.SelectedWalkingLimits, including scratch while
    /// running. The private output stamp is checked on return; rejection cannot retroactively bound arbitrary
    /// allocations made by caller-supplied code during construction.
    /// </param>
    /// <param name="estimator">A cheap, deterministic estimate of output plus additional scratch; runs outside the lock.</param>
    public WorldStore(WorldStoreOptions options,
        Func<PlanetEnvironmentDescriptor, CancellationToken, ISurface>? factory = null,
        Func<PlanetEnvironmentDescriptor, WorldMemoryEstimate>? estimator = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _factory = factory ?? ((descriptor, cancellation) =>
            descriptor.Formations?.FieldContract != null && options.SelectedWalkingLimits is { } selected
                ? EnvironmentWorldFactory.CreateSelected(descriptor, selected, cancellation)
                : EnvironmentWorldFactory.Create(descriptor, cancellation));
        _estimate = estimator ?? (descriptor => EstimateDefault(descriptor, options));
    }

    public WorldStoreDiagnostics Diagnostics
    {
        get
        {
            lock (_gate)
                return new WorldStoreDiagnostics(_residentWorlds, _builds.Count, _activeLeases, _pendingAcquisitions,
                    _residentBytes, _reservedResidentBytes, _reservedBuildBytes, _retiring, _retirement.Task.IsCompleted,
                    _pendingDisposals, _pendingDisposalBytes, _cleanupFailureCount);
        }
    }

    public Task Retirement => _retirement.Task;

    /// <summary>
    /// Conservative admission estimates, not measured allocation totals or a process-memory guarantee. The drainage
    /// grid is the dominant eagerly allocated walking structure (Drainage constructor: direction/scalar/parent arrays,
    /// flood work, lake indices and segment buckets). Follow its actual level selection so diagnostic tiny cells cannot
    /// obtain a fixed cheap reservation for a huge grid. Lazy population, render and physics caches have separate owners.
    /// Configured defaults are floors; injected estimators can account for a different factory's real requirements.
    /// </summary>
    internal static WorldMemoryEstimate EstimateDefault(PlanetEnvironmentDescriptor descriptor, WorldStoreOptions options)
    {
        long resident = options.DefaultEstimatedResidentBytes, scratch = options.DefaultReservedBuildBytes;
        if (descriptor.WalkingParameters is { } parameters)
        {
            int level = TileKey.LevelFor(descriptor.Radius, parameters.DrainageCell, 12);
            long side = 1L << level;
            long cells = SaturatingMultiply(6, SaturatingMultiply(side, side));
            // Drainage's segment index has an independent 120 m bucket grid, even with very coarse drainage cells.
            // Keep this structural estimate in step with Drainage.BucketMetres and its maximum grid level.
            long bucketSide = 1L << TileKey.LevelFor(descriptor.Radius, 120, 12);
            long buckets = SaturatingMultiply(6, SaturatingMultiply(bucketSide, bucketSide));
            long baseline = SaturatingAdd(4L * 1024 * 1024, SaturatingMultiply(parameters.Provinces, 512));
            resident = Math.Max(resident, SaturatingAdd(baseline,
                SaturatingAdd(SaturatingMultiply(cells, 256), SaturatingMultiply(buckets, 64))));
            scratch = Math.Max(scratch, SaturatingAdd(baseline,
                SaturatingAdd(SaturatingMultiply(cells, 512), SaturatingMultiply(buckets, 128))));
        }
        return new WorldMemoryEstimate(resident, scratch);
    }

    private static long SaturatingMultiply(long a, long b) => a != 0 && b > long.MaxValue / a ? long.MaxValue : a * b;
    private static long SaturatingAdd(long a, long b) => b > long.MaxValue - a ? long.MaxValue : a + b;

    public WorldLease Acquire(PlanetEnvironmentDescriptor descriptor, CancellationToken cancellationToken = default) =>
        AcquireAsync(descriptor, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Joins an admitted construction or starts one. Capacity/closed-admission errors are immediate; a precanceled token
    /// returns a canceled task. Pending acquisitions reserve ownership before dispatch, and each cancellation releases
    /// only its own claim. There is no unbounded admission queue.
    /// </summary>
    public Task<WorldLease> AcquireAsync(PlanetEnvironmentDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<WorldLease>(cancellationToken);
        WorldMemoryEstimate? estimate = null;
        while (true)
        {
            Entry? dispatch = null;
            Waiter? waiter = null;
            List<Disposal>? evicted = null;
            bool estimateNeeded = false;
            lock (_gate)
            {
                ThrowIfRetiring();
                if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<WorldLease>(cancellationToken);
                if (_entries.TryGetValue(descriptor.CacheIdentity, out Entry? existing))
                {
                    if (existing.Surface != null) return Task.FromResult(NewLeaseLocked(existing));
                    waiter = AddWaiterLocked(existing, cancellationToken);
                }
                else if (estimate == null)
                {
                    estimateNeeded = true;
                }
                else
                {
                    WorldMemoryEstimate bytes = estimate.Value;
                    CheckBuildAdmissionLocked(bytes);
                    evicted = MakeRoomLocked(bytes);
                    if (evicted == null)
                    {
                        dispatch = new Entry(descriptor, bytes);
                        _entries.Add(dispatch.Key, dispatch);
                        _builds.Add(dispatch);
                        _reservedResidentBytes += bytes.EstimatedResidentBytes;
                        _reservedBuildBytes += bytes.ReservedBuildBytes;
                        waiter = AddWaiterLocked(dispatch, cancellationToken);
                    }
                }
            }
            if (estimateNeeded)
            {
                // Mandatory metadata authentication and allowances precede any caller-injected estimator/factory.
                SelectedWalkingWorldPlan? selected = descriptor.Formations?.FieldContract == null ? null :
                    (_options.SelectedWalkingLimits ?? throw new NotSupportedException("Selected walking construction is not enabled for this store."))
                    .Prepare(descriptor, cancellationToken);
                estimate = _estimate(descriptor);
                if (estimate.Value.EstimatedResidentBytes < 0 || estimate.Value.ReservedBuildBytes < 0)
                    throw new ArgumentOutOfRangeException(nameof(descriptor), "World memory estimates must be nonnegative.");
                if (selected != null)
                {
                    WorldMemoryEstimate baseline = EstimateDefault(descriptor, _options);
                    estimate = new WorldMemoryEstimate(
                        SaturatingAdd(Math.Max(estimate.Value.EstimatedResidentBytes, baseline.EstimatedResidentBytes),
                            selected.AdditionalEstimate.EstimatedResidentBytes),
                        SaturatingAdd(Math.Max(estimate.Value.ReservedBuildBytes, baseline.ReservedBuildBytes),
                            selected.AdditionalEstimate.ReservedBuildBytes));
                }
                // Selected catalogs/canonical strings remain owned through the entry even with an injected factory.
                estimate = estimate.Value with { EstimatedResidentBytes = SaturatingAdd(
                    estimate.Value.EstimatedResidentBytes, descriptor.EstimatedSelectionBytes) };
                continue;
            }
            if (evicted != null)
            {
                DisposeAll(evicted);
                // Another caller may have acquired the same identity while disposal ran. Resolve again under the lock.
                continue;
            }
            try { RegisterCancellation(waiter!); }
            catch (Exception error) { RejectWaiter(waiter!, error); }
            if (dispatch != null) Dispatch(dispatch);
            return waiter!.Completion.Task;
        }
    }

    private Waiter AddWaiterLocked(Entry entry, CancellationToken token)
    {
        CheckWaiterAdmissionLocked();
        var waiter = new Waiter(this, entry, token);
        entry.Waiters.Add(waiter);
        _pendingAcquisitions++;
        return waiter;
    }

    private void CheckBuildAdmissionLocked(WorldMemoryEstimate bytes)
    {
        CheckWaiterAdmissionLocked();
        if (_builds.Count >= _options.MaxConcurrentBuilds)
            throw new WorldStoreCapacityException("The session's concurrent world-build limit is reached.");
        if (bytes.EstimatedResidentBytes > _options.MaxEstimatedResidentBytes)
            throw new WorldStoreCapacityException("This world's estimated output exceeds the session resident budget.");
        if (bytes.ReservedBuildBytes > _options.MaxReservedBuildBytes - _reservedBuildBytes)
            throw new WorldStoreCapacityException("This world's scratch reservation exceeds the available build budget.");
    }

    private void CheckWaiterAdmissionLocked()
    {
        if (_pendingAcquisitions >= _options.MaxPendingAcquisitions)
            throw new WorldStoreCapacityException("The session's pending world-acquisition limit is reached.");
        if (_activeLeases >= int.MaxValue - _pendingAcquisitions)
            throw new WorldStoreCapacityException("The session lease count is exhausted.");
    }

    // Select before detaching: a request which cannot fit even after eviction must not destroy useful idle entries.
    private List<Disposal>? MakeRoomLocked(WorldMemoryEstimate bytes)
    {
        long used = _residentBytes + _reservedResidentBytes + _pendingDisposalBytes;
        int worlds = _residentWorlds + _builds.Count + _pendingDisposals;
        bool Fits() => worlds < _options.MaxResidentWorlds && bytes.EstimatedResidentBytes <= _options.MaxEstimatedResidentBytes - used;
        if (Fits()) return null;
        var candidates = new List<Entry>();
        foreach (Entry entry in _entries.Values.Where(e => e.Surface != null && e.Leases == 0).OrderBy(e => e.LastUsed))
        {
            candidates.Add(entry);
            worlds--;
            used -= entry.Estimate.EstimatedResidentBytes;
            if (Fits()) break;
        }
        if (!Fits()) throw new WorldStoreCapacityException("Pinned worlds or outstanding work leave insufficient resident capacity.");
        var disposals = new List<Disposal>(candidates.Count);
        foreach (Entry entry in candidates) disposals.Add(DetachResidentLocked(entry));
        return disposals;
    }

    private static void RegisterCancellation(Waiter waiter)
    {
        if (!waiter.Token.CanBeCanceled) return;
        CancellationTokenRegistration registration = waiter.Token.UnsafeRegister(static state =>
        {
            var pending = (Waiter)state!;
            Volatile.Read(ref pending.Owner)?.CancelWaiter(pending);
        }, waiter);
        WorldStore? owner = Volatile.Read(ref waiter.Owner);
        if (owner == null)
        {
            registration.Unregister();
            return;
        }
        lock (owner._gate)
        {
            if (waiter.Finished) registration.Unregister();
            else
            {
                waiter.Registration = registration;
                waiter.HasRegistration = true;
            }
        }
    }

    private void CancelWaiter(Waiter waiter)
    {
        lock (_gate)
        {
            if (waiter.Finished) return;
            Entry entry = waiter.Entry!;
            FinishWaiterLocked(waiter);
            waiter.Completion.TrySetCanceled(waiter.Token);
            if (entry.Waiters.Count == 0)
            {
                entry.Abandoned = true;
                RemoveEntryLocked(entry);
                CancelBuildLocked(entry);
            }
        }
    }

    private void RejectWaiter(Waiter waiter, Exception error)
    {
        lock (_gate)
        {
            if (waiter.Finished) return;
            Entry entry = waiter.Entry!;
            FinishWaiterLocked(waiter);
            waiter.Completion.TrySetException(error);
            if (entry.Waiters.Count == 0)
            {
                entry.Abandoned = true;
                RemoveEntryLocked(entry);
                CancelBuildLocked(entry);
            }
        }
    }

    private void FinishWaiterLocked(Waiter waiter)
    {
        waiter.Finished = true;
        waiter.Entry!.Waiters.Remove(waiter);
        _pendingAcquisitions--;
        if (waiter.HasRegistration)
        {
            // Nonblocking: an executing callback may be waiting for _gate. Dispose() here would deadlock it.
            waiter.Registration.Unregister();
            waiter.Registration = default;
            waiter.HasRegistration = false;
        }
        waiter.Entry = null;
        Volatile.Write(ref waiter.Owner, null);
    }

    private static void CancelBuildLocked(Entry entry)
    {
        if (entry.FactoryFinished || entry.Cancellation != null) return;
        // CancelAsync marks the token now but runs user registrations asynchronously, outside the store lock.
        entry.Cancellation = entry.Stop.CancelAsync();
    }

    private void Dispatch(Entry entry)
    {
        try
        {
            // Do not pass a waiter token: even a canceled-before-start job must execute the bookkeeping path below.
            _ = Task.Run(() => BuildAsync(entry));
        }
        catch (Exception error)
        {
            _ = FinishBuildAsync(entry, error);
        }
    }

    private async Task BuildAsync(Entry entry)
    {
        ISurface? result = null;
        Exception? failure = null;
        try
        {
            entry.Stop.Token.ThrowIfCancellationRequested();
            result = _factory(entry.Descriptor!, entry.Stop.Token);
            if (result == null) throw new InvalidOperationException("The world factory returned no surface.");
            if (result.Radius != entry.Descriptor!.Radius)
                throw new InvalidOperationException("The world factory returned a surface with a different radius from its descriptor.");
            if (entry.Descriptor.Formations?.FieldContract != null &&
                (result is not WalkingWorld selected || !selected.MatchesSelected(entry.Descriptor, _options.SelectedWalkingLimits!)))
                throw new InvalidOperationException("The world factory returned no authenticated complete selected walking bundle.");
        }
        catch (Exception error)
        {
            failure = error;
        }
        // Clear the factory wrapper's direct reference before publishing to callers. A completed wrapper can remain
        // briefly reachable from the thread pool; after Trim it must not keep an otherwise evicted world alive.
        entry.BuiltSurface = result;
        result = null;
        await FinishBuildAsync(entry, failure).ConfigureAwait(false);
    }

    private async Task FinishBuildAsync(Entry entry, Exception? failure)
    {
        Task? cancellation;
        lock (_gate)
        {
            entry.FactoryFinished = true;
            cancellation = entry.Cancellation;
        }
        // No new cancellation is started after FactoryFinished. Retire any callback already dispatched before
        // disposing its source or the factory output; these callbacks can also hold construction resources.
        if (cancellation != null)
        {
            try { await cancellation.ConfigureAwait(false); }
            catch (Exception error) { lock (_gate) RecordCleanupFailureLocked(error); }
        }
        entry.Stop.Dispose();
        Disposal? discarded = null;
        lock (_gate)
        {
            ISurface? surface = entry.BuiltSurface;
            entry.BuiltSurface = null;
            _builds.Remove(entry);
            _reservedResidentBytes -= entry.Estimate.EstimatedResidentBytes;
            _reservedBuildBytes -= entry.Estimate.ReservedBuildBytes;
            // Token callbacks need not have acquired our lock yet. Check tokens before deciding that somebody still
            // wants this world, so an ignored factory cancellation cannot leave an unrequested resident entry.
            foreach (Waiter waiter in entry.Waiters.Where(w => w.Token.IsCancellationRequested).ToArray())
            {
                FinishWaiterLocked(waiter);
                waiter.Completion.TrySetCanceled(waiter.Token);
            }
            bool publish = failure == null && !_retiring && !entry.Abandoned && entry.Waiters.Count > 0;
            if (publish)
            {
                entry.Surface = surface!;
                surface = null;
                entry.LastUsed = ++_sequence;
                _residentWorlds++;
                _residentBytes += entry.Estimate.EstimatedResidentBytes;
                foreach (Waiter waiter in entry.Waiters.ToArray())
                {
                    FinishWaiterLocked(waiter);
                    WorldLease lease = NewLeaseLocked(entry);
                    if (!waiter.Completion.TrySetResult(lease)) lease.Dispose();
                }
            }
            else
            {
                RemoveEntryLocked(entry);
                foreach (Waiter waiter in entry.Waiters.ToArray())
                {
                    FinishWaiterLocked(waiter);
                    if (failure is OperationCanceledException canceled)
                        waiter.Completion.TrySetCanceled(canceled.CancellationToken.IsCancellationRequested
                            ? canceled.CancellationToken : new CancellationToken(true));
                    else waiter.Completion.TrySetException(failure ?? new ObjectDisposedException(nameof(WorldStore)));
                }
                if (surface != null)
                {
                    discarded = TrackDisposalLocked(surface, entry.Estimate.EstimatedResidentBytes);
                    surface = null;
                }
                entry.Descriptor = null;
            }
            entry.Cancellation = null;
            CompleteRetirementLocked();
        }
        if (discarded != null) DisposeOne(discarded);
    }

    internal WorldLease Fork(Entry entry)
    {
        lock (_gate)
        {
            ThrowIfRetiring();
            return NewLeaseLocked(entry);
        }
    }

    private WorldLease NewLeaseLocked(Entry entry)
    {
        if (_activeLeases >= int.MaxValue - _pendingAcquisitions)
            throw new WorldStoreCapacityException("The session lease count is exhausted.");
        var lease = new WorldLease(this, entry, entry.Surface!, entry.Descriptor!);
        entry.Leases++;
        _activeLeases++;
        entry.LastUsed = ++_sequence;
        return lease;
    }

    internal void Release(Entry entry)
    {
        Disposal? disposal = null;
        lock (_gate)
        {
            entry.Leases--;
            _activeLeases--;
            entry.LastUsed = ++_sequence;
            if (_retiring && entry.Leases == 0) disposal = DetachResidentLocked(entry);
            CompleteRetirementLocked();
        }
        if (disposal != null) QueueDisposal(disposal);
    }

    /// <summary>Evicts every zero-lease resident, disposing factory-owned IDisposable output outside the lock.</summary>
    public int Trim()
    {
        List<Disposal> disposals;
        lock (_gate)
        {
            disposals = _entries.Values.Where(e => e.Surface != null && e.Leases == 0).ToArray()
                .Select(DetachResidentLocked).ToList();
        }
        DisposeAll(disposals);
        return disposals.Count;
    }

    /// <summary>
    /// Closes admission/forking, rejects pending acquisitions, requests cooperative cancellation and retires idle
    /// output. Nonblocking: existing leases remain usable, and construction/cleanup must finish before Retirement.
    /// </summary>
    public Task BeginRetirement()
    {
        List<Disposal> disposals;
        lock (_gate)
        {
            if (_retiring) return _retirement.Task;
            _retiring = true;
            foreach (Entry entry in _builds)
            {
                entry.Abandoned = true;
                RemoveEntryLocked(entry);
                foreach (Waiter waiter in entry.Waiters.ToArray())
                {
                    FinishWaiterLocked(waiter);
                    waiter.Completion.TrySetException(new ObjectDisposedException(nameof(WorldStore)));
                }
                CancelBuildLocked(entry);
            }
            disposals = _entries.Values.Where(e => e.Surface != null && e.Leases == 0).ToArray()
                .Select(DetachResidentLocked).ToList();
            CompleteRetirementLocked();
        }
        foreach (Disposal disposal in disposals) QueueDisposal(disposal);
        return _retirement.Task;
    }

    public void Dispose() => _ = BeginRetirement();

    public async ValueTask DisposeAsync() => await BeginRetirement().ConfigureAwait(false);

    private void ThrowIfRetiring()
    {
        if (_retiring) throw new ObjectDisposedException(nameof(WorldStore), "The environment session is retiring.");
    }

    private void RemoveEntryLocked(Entry entry)
    {
        if (_entries.TryGetValue(entry.Key, out Entry? current) && ReferenceEquals(current, entry)) _entries.Remove(entry.Key);
    }

    private Disposal DetachResidentLocked(Entry entry)
    {
        ISurface surface = entry.Surface!;
        entry.Surface = null;
        entry.Descriptor = null;
        RemoveEntryLocked(entry);
        _residentWorlds--;
        _residentBytes -= entry.Estimate.EstimatedResidentBytes;
        return TrackDisposalLocked(surface, entry.Estimate.EstimatedResidentBytes);
    }

    private Disposal TrackDisposalLocked(ISurface surface, long estimatedBytes)
    {
        _pendingDisposals++;
        _pendingDisposalBytes += estimatedBytes;
        return new Disposal(surface, estimatedBytes);
    }

    private void QueueDisposal(Disposal disposal)
    {
        try { _ = Task.Run(() => DisposeOne(disposal)); }
        catch (Exception error)
        {
            lock (_gate) RecordCleanupFailureLocked(error);
            // Scheduling rejection must not strand ownership. Only this exceptional path performs inline cleanup.
            DisposeOne(disposal);
        }
    }

    private void DisposeAll(List<Disposal> disposals)
    {
        List<Exception>? failures = null;
        foreach (Disposal disposal in disposals)
            if (DisposeOne(disposal) is { } failure) (failures ??= new()).Add(failure);
        if (failures != null) throw new AggregateException("World output cleanup failed.", failures).Flatten();
    }

    private Exception? DisposeOne(Disposal disposal)
    {
        Exception? failure = null;
        try { (disposal.Surface as IDisposable)?.Dispose(); }
        catch (Exception error) { failure = error; }
        finally
        {
            // Remove the strong reference even when external disposal throws; failures remain observable on Retirement.
            disposal.Surface = null;
            lock (_gate)
            {
                _pendingDisposals--;
                _pendingDisposalBytes -= disposal.EstimatedBytes;
                if (failure != null) RecordCleanupFailureLocked(failure);
                CompleteRetirementLocked();
            }
        }
        return failure;
    }

    private void CompleteRetirementLocked()
    {
        if (!_retiring || _builds.Count != 0 || _activeLeases != 0 || _pendingDisposals != 0) return;
        if (_cleanupFailureCount == 0) _retirement.TrySetResult();
        else
        {
            var failures = new List<Exception>(_cleanupFailures);
            int omitted = _cleanupFailureCount - _cleanupFailures.Count;
            if (omitted > 0)
                failures.Add(new InvalidOperationException($"At least {omitted} additional cleanup failures were omitted from the bounded exception history."));
            _retirement.TrySetException(new AggregateException("World session cleanup failed.", failures).Flatten());
        }
    }

    private void RecordCleanupFailureLocked(Exception error)
    {
        if (_cleanupFailureCount < int.MaxValue) _cleanupFailureCount++;
        if (_cleanupFailures.Count < _options.MaxRetainedCleanupFailures) _cleanupFailures.Add(error);
    }

    internal sealed class Entry(PlanetEnvironmentDescriptor descriptor, WorldMemoryEstimate estimate)
    {
        internal readonly string Key = descriptor.CacheIdentity;
        internal PlanetEnvironmentDescriptor? Descriptor = descriptor;
        internal readonly WorldMemoryEstimate Estimate = estimate;
        internal readonly CancellationTokenSource Stop = new();
        internal readonly HashSet<Waiter> Waiters = new();
        internal ISurface? Surface;
        internal ISurface? BuiltSurface;
        internal Task? Cancellation;
        internal bool Abandoned, FactoryFinished;
        internal int Leases;
        internal ulong LastUsed;
    }

    internal sealed class Waiter(WorldStore owner, Entry entry, CancellationToken token)
    {
        internal WorldStore? Owner = owner;
        internal Entry? Entry = entry;
        internal readonly CancellationToken Token = token;
        internal readonly TaskCompletionSource<WorldLease> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Registration;
        internal bool HasRegistration, Finished;
    }

    private sealed class Disposal(ISurface surface, long estimatedBytes)
    {
        internal ISurface? Surface = surface;
        internal readonly long EstimatedBytes = estimatedBytes;
    }
}
