using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Store ownership/admission checks with metadata-only descriptors and tiny injected worlds.</summary>
public static class WorldStoreTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);
    // Deliberately tiny accounting units exercise exact boundaries; these are not measured terrain budgets.
    private const long ResidentBytes = 8, BuildBytes = 4;

    [Test]
    public static void ConcurrentEquivalentDescriptorsCoalesceAndOneCancelledWaiterDoesNotCancelTheBuild()
    {
        using var factory = new BlockedFactory();
        using var store = Store(factory.Build);
        using var cancelled = new CancellationTokenSource();
        var tasks = new Task<WorldLease>?[4];
        try
        {
            tasks[0] = store.AcquireAsync(Earth());
            factory.WaitForEntry();
            tasks[1] = store.AcquireAsync(Earth(), cancelled.Token);
            tasks[2] = store.AcquireAsync(Earth());
            tasks[3] = store.AcquireAsync(Earth());
            Assert.Equal(1, factory.Calls, "independently resolved equivalent descriptors share one factory");
            Check(store, residents: 0, building: 1, leases: 0, waiters: 4);
            cancelled.Cancel();
            Refuse<OperationCanceledException>(() => Complete(tasks[1]!));
            Check(store, residents: 0, building: 1, leases: 0, waiters: 3);
            Assert.True(!factory.Cancelled.IsSet, "one waiter cannot cancel work still required by other waiters");
            factory.Release.Set();
            WorldLease first = Complete(tasks[0]!);
            WorldLease second = Complete(tasks[2]!);
            WorldLease third = Complete(tasks[3]!);
            Assert.True(ReferenceEquals(first.Surface, second.Surface) && ReferenceEquals(first.Surface, third.Surface),
                "all successful waiters receive the same resident world");
            Assert.Equal(Earth(), first.Descriptor, "the lease exposes the resolved descriptor for its world");
            Check(store, residents: 1, building: 0, leases: 3, waiters: 0);
        }
        finally
        {
            factory.Release.Set();
            Cleanup(store, tasks);
        }
    }

    [Test]
    public static void AlreadyCancelledRequestsNeverReserveCapacityOrCallTheFactory()
    {
        int calls = 0;
        using var store = Store((descriptor, _) => { calls++; return Sphere(descriptor); });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            Refuse<OperationCanceledException>(() => Complete(store.AcquireAsync(Earth(), cancelled.Token)));
            Refuse<OperationCanceledException>(() => store.Acquire(Earth(), cancelled.Token));
            Assert.Equal(0, calls, "pre-cancelled acquisition cannot dispatch generation");
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0);
            using WorldLease admitted = store.Acquire(Earth());
            Assert.Equal(1, calls, "a later uncancelled request can use all capacity");
        }
        finally { Complete(store.BeginRetirement()); }
    }

    [Test]
    public static void BuildAndWaiterLimitsRejectWithoutLeakingReservations()
    {
        using var factory = new BlockedFactory();
        using var store = Store(factory.Build, Options(builds: 1, waiters: 3));
        using var cancelled = new CancellationTokenSource();
        var tasks = new Task<WorldLease>?[3];
        try
        {
            tasks[0] = store.AcquireAsync(Earth());
            factory.WaitForEntry();
            Refuse<WorldStoreCapacityException>(() => Complete(store.AcquireAsync(Earth(1100))));
            Check(store, residents: 0, building: 1, leases: 0, waiters: 1);
            tasks[1] = store.AcquireAsync(Earth(), cancelled.Token);
            tasks[2] = store.AcquireAsync(Earth());
            Refuse<WorldStoreCapacityException>(() => Complete(store.AcquireAsync(Earth())));
            Check(store, residents: 0, building: 1, leases: 0, waiters: 3);
            Assert.Equal(1, factory.Calls, "build-slot and waiter refusals dispatch no extra factories");
            cancelled.Cancel();
            Refuse<OperationCanceledException>(() => Complete(tasks[1]!));
            Check(store, residents: 0, building: 1, leases: 0, waiters: 2);
            factory.Release.Set();
            _ = Complete(tasks[0]!);
            _ = Complete(tasks[2]!);
            Check(store, residents: 1, building: 0, leases: 2, waiters: 0);
        }
        finally
        {
            factory.Release.Set();
            Cleanup(store, tasks);
        }
    }

    [Test]
    public static void WorldResidentAndScratchBudgetsEachRefuseBeforeDispatch()
    {
        CheckBudget(Options(worlds: 1), "world slots");
        CheckBudget(Options(residentBytes: 2 * ResidentBytes - 1), "resident bytes, including build reservations");
        CheckBudget(Options(buildBytes: 2 * BuildBytes - 1), "build scratch bytes");

        // A reservation exactly equal to each limit must be admitted.
        using var store = Store((descriptor, _) => Sphere(descriptor),
            Options(worlds: 1, residentBytes: ResidentBytes, buildBytes: BuildBytes));
        WorldLease lease = store.Acquire(Earth());
        try { Check(store, residents: 1, building: 0, leases: 1, waiters: 0); }
        finally { lease.Dispose(); Complete(store.BeginRetirement()); }
    }

    [Test]
    public static void DefaultEstimatorRefusesAnOversizedDrainageGridBeforeFactoryDispatch()
    {
        int calls = 0;
        PlanetEnvironmentDescriptor descriptor = Earth(parameters: WalkingParameters.Earth with { DrainageCell = 1e-6 });
        // Keep the production default estimator: overriding it with our tiny accounting fixture would bypass
        // the contract under test. Resolving this descriptor must not allocate the implied drainage grid.
        using var store = new WorldStore(new WorldStoreOptions(), (input, _) =>
        {
            Interlocked.Increment(ref calls);
            return Sphere(input);
        });
        try
        {
            Refuse<WorldStoreCapacityException>(() => store.Acquire(descriptor));
            Assert.Equal(0, calls, "a tiny positive drainage cell cannot bypass default construction budgets");
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0);
        }
        finally { Complete(store.BeginRetirement()); }
    }

    [Test]
    public static void FactoryFailureRollsBackEveryReservationAndAllowsSameIdentityRetry()
    {
        int calls = 0;
        var expected = new InvalidOperationException("controlled world factory failure");
        using var store = Store((descriptor, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw expected;
            return Sphere(descriptor);
        }, Options(worlds: 1, residentBytes: ResidentBytes, buildBytes: BuildBytes, builds: 1));
        try
        {
            Exception actual = Refuse<InvalidOperationException>(() => store.Acquire(Earth()));
            Assert.True(ReferenceEquals(expected, actual), "the caller receives the original generation failure");
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0);
            using WorldLease retried = store.Acquire(Earth());
            Assert.Equal(2, calls, "failed identity is not negatively cached");
            Check(store, residents: 1, building: 0, leases: 1, waiters: 0);
        }
        finally { Complete(store.BeginRetirement()); }
    }

    [Test]
    public static void RetirementDisposesEveryOwnedWorldAndAggregatesAllCleanupFailures()
    {
        var first = new FailingDisposalSurface(1200, "first controlled disposal failure");
        var second = new FailingDisposalSurface(1100, "second controlled disposal failure");
        using var store = Store((descriptor, _) => descriptor.Radius == 1200 ? first : second);
        WorldLease? firstLease = null, secondLease = null;
        try
        {
            firstLease = store.Acquire(Earth());
            secondLease = store.Acquire(Earth(1100));
            firstLease.Dispose();
            secondLease.Dispose();
            Assert.Equal(0, first.DisposeCalls + second.DisposeCalls, "inactive cached worlds remain owned until eviction or retirement");
            Task retirement = store.BeginRetirement();
            AggregateException failure = Expect<AggregateException>(() => Complete(retirement)).Flatten();
            Assert.Equal(2, failure.InnerExceptions.Count, "retirement reports both failures after attempting every cleanup");
            Assert.True(failure.InnerExceptions.Contains(first.Failure) && failure.InnerExceptions.Contains(second.Failure),
                "cleanup aggregation preserves each original exception");
            Assert.Equal(1, first.DisposeCalls, "the first world is disposed exactly once");
            Assert.Equal(1, second.DisposeCalls, "the second world is disposed despite another world's failure");
            Assert.True(store.Diagnostics.IsRetired, "failed cleanup still finishes retirement accounting");
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0, cleanupFailures: 2);
            _ = Expect<AggregateException>(() => Complete(store.BeginRetirement()));
            Assert.Equal(2, first.DisposeCalls + second.DisposeCalls, "repeated retirement never retries disposed outputs");
        }
        finally
        {
            firstLease?.Dispose();
            secondLease?.Dispose();
            try { Complete(store.BeginRetirement()); }
            catch (AggregateException) { } // Expected failures were asserted above; still await all cleanup on an assertion failure.
        }
    }

    [Test]
    public static void CleanupFailureHistoryStaysBoundedWhileEveryFailureIsCounted()
    {
        var worlds = new FailingDisposalSurface[5];
        for (int i = 0; i < worlds.Length; i++)
            worlds[i] = new FailingDisposalSurface(1200 - i * 100, "controlled disposal failure " + i);
        int calls = 0;
        using var store = Store((_, _) => worlds[Interlocked.Increment(ref calls) - 1],
            Options() with { MaxRetainedCleanupFailures = 2 });
        try
        {
            for (int i = 0; i < worlds.Length; i++)
            {
                using WorldLease lease = store.Acquire(Earth(worlds[i].Radius));
                lease.Dispose();
                AggregateException failure = Expect<AggregateException>(() => store.Trim()).Flatten();
                Assert.True(failure.InnerExceptions.Contains(worlds[i].Failure), "each trim exposes its original cleanup failure");
                Assert.Equal(1, worlds[i].DisposeCalls, "each failed disposal is attempted exactly once");
                Check(store, residents: 0, building: 0, leases: 0, waiters: 0, cleanupFailures: i + 1);
            }

            AggregateException retirement = Expect<AggregateException>(() => Complete(store.BeginRetirement())).Flatten();
            Assert.True(retirement.InnerExceptions.Count <= 3, "retirement retains at most two original failures plus one omitted-count summary");
            Assert.True(retirement.InnerExceptions.Contains(worlds[0].Failure) && retirement.InnerExceptions.Contains(worlds[1].Failure),
                "the first failures remain observable after the history cap is reached");
            Assert.Equal(worlds.Length, calls, "all five worlds were admitted and retired independently");
            Assert.True(store.Diagnostics.IsRetired, "bounded failure reporting still completes retirement");
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0, cleanupFailures: worlds.Length);
            foreach (FailingDisposalSurface world in worlds)
                Assert.Equal(1, world.DisposeCalls, "retirement does not retry previously failed disposals");
        }
        finally
        {
            try { Complete(store.BeginRetirement()); }
            catch (AggregateException) { } // Await cleanup even when an assertion fails; expected failures are checked above.
        }
    }

    [Test]
    public static void ForkPinsEvictionAndEvictedWorldRegeneratesWithoutChangingItsSamples()
    {
        int calls = 0;
        using var store = Store((descriptor, _) => { calls++; return Sphere(descriptor); }, Options(worlds: 1));
        WorldLease original = store.Acquire(Earth());
        WorldLease fork = original.Fork();
        try
        {
            double expected = original.Surface.HeightAt(Vector3D.UnitX);
            Assert.True(ReferenceEquals(original.Surface, fork.Surface), "fork owns the same world independently");
            original.Dispose();
            Check(store, residents: 1, building: 0, leases: 1, waiters: 0);
            Assert.Equal(0, store.Trim(), "a fork keeps the world resident");
            Refuse<WorldStoreCapacityException>(() => store.Acquire(Earth(1100)));
            Assert.Equal(expected, fork.Surface.HeightAt(Vector3D.UnitX), "refused admission cannot disturb active world access");
            fork.Dispose();
            Assert.Equal(1, store.Trim(), "the last release makes the world evictable");
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0);
            using WorldLease other = store.Acquire(Earth(1100));
            other.Dispose();
            Assert.Equal(1, store.Trim(), "the second inactive identity can also be evicted");
            using WorldLease revisited = store.Acquire(Earth());
            Assert.Equal(expected, revisited.Surface.HeightAt(Vector3D.UnitX), "eviction and revisit preserve deterministic output");
            Assert.Equal(3, calls, "each evicted identity is reconstructed only when requested again");
        }
        finally { original.Dispose(); fork.Dispose(); Complete(store.BeginRetirement()); }
    }

    [Test]
    public static void DisposedLeaseCannotAccessOrRetainAnEvictedWorld()
    {
        using var store = Store(static (descriptor, _) => Sphere(descriptor));
        try
        {
            (WorldLease disposed, WeakReference<ISurface> weak) = EvictAndKeepDisposedHandle(store);
            Expect<ObjectDisposedException>(() => _ = disposed.Surface);
            Expect<ObjectDisposedException>(() => _ = disposed.Descriptor);
            Refuse<ObjectDisposedException>(() => disposed.Fork());
            disposed.Dispose();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            Assert.True(!weak.TryGetTarget(out _), "a retained disposed handle and live empty store do not retain generated data");
            GC.KeepAlive(disposed);
            GC.KeepAlive(store);
        }
        finally { Complete(store.BeginRetirement()); }
    }

    [Test]
    public static void RetirementRejectsAdmissionAndForksWhileWaitingForAllExistingLeases()
    {
        using var store = Store(static (descriptor, _) => Sphere(descriptor));
        WorldLease original = store.Acquire(Earth());
        WorldLease fork = original.Fork();
        try
        {
            Task retirement = store.BeginRetirement();
            Assert.True(store.Diagnostics.IsRetiring && !store.Diagnostics.IsRetired, "retirement is explicitly visible");
            Assert.True(!retirement.IsCompleted, "two active owners prevent retirement completion");
            Refuse<ObjectDisposedException>(() => store.Acquire(Earth()));
            Refuse<ObjectDisposedException>(() => Complete(store.AcquireAsync(Earth(1100))));
            Refuse<ObjectDisposedException>(() => fork.Fork());
            Assert.Near(1200, fork.Surface.Radius, 0, "existing leases remain usable during retirement");
            original.Dispose();
            Assert.True(!retirement.IsCompleted, "the final fork still pins retirement");
            fork.Dispose();
            Complete(retirement);
            Complete(store.Retirement);
            Complete(store.BeginRetirement());
            Assert.True(store.Diagnostics.IsRetired, "retirement finishes once the last owner releases");
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0);
        }
        finally { original.Dispose(); fork.Dispose(); Complete(store.BeginRetirement()); }
    }

    [Test]
    public static void RetirementFailsWaitersImmediatelyButWaitsForAFactoryIgnoringCancellation()
    {
        using var factory = new BlockedFactory();
        using var store = Store(factory.Build);
        Task<WorldLease>? acquisition = null;
        try
        {
            acquisition = store.AcquireAsync(Earth());
            factory.WaitForEntry();
            Task retirement = store.BeginRetirement();
            Refuse<ObjectDisposedException>(() => Complete(acquisition));
            Assert.True(factory.Cancelled.Wait(Guard), "retirement cancels generation's own token");
            Assert.True(!retirement.IsCompleted, "a still-running factory prevents retirement completion");
            Check(store, residents: 0, building: 1, leases: 0, waiters: 0);
            factory.Release.Set();
            Complete(retirement);
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0);
            Assert.Equal(1, factory.Calls, "the ignored-token result is discarded without a replacement build");
        }
        finally
        {
            factory.Release.Set();
            Cleanup(store, acquisition);
        }
    }

    [Test]
    public static void CancellingAllWaitersKeepsReservationsUntilTheFactoryActuallyStops()
    {
        using var factory = new BlockedFactory();
        using var store = Store(factory.Build);
        using var firstCancel = new CancellationTokenSource();
        using var secondCancel = new CancellationTokenSource();
        var tasks = new Task<WorldLease>?[2];
        try
        {
            tasks[0] = store.AcquireAsync(Earth(), firstCancel.Token);
            factory.WaitForEntry();
            tasks[1] = store.AcquireAsync(Earth(), secondCancel.Token);
            firstCancel.Cancel();
            secondCancel.Cancel();
            Refuse<OperationCanceledException>(() => Complete(tasks[0]!));
            Refuse<OperationCanceledException>(() => Complete(tasks[1]!));
            Assert.True(factory.Cancelled.Wait(Guard), "no remaining waiters cancels the shared factory token");
            Check(store, residents: 0, building: 1, leases: 0, waiters: 0);
            Task retirement = store.BeginRetirement();
            Assert.True(!retirement.IsCompleted, "cancelled callers do not make live factory memory disappear");
            factory.Release.Set();
            Complete(retirement);
            Check(store, residents: 0, building: 0, leases: 0, waiters: 0);
        }
        finally
        {
            factory.Release.Set();
            Cleanup(store, tasks);
        }
    }

    [Test]
    public static void DistinctProfilesRadiiAndStoresHaveIndependentFactoriesAndWorlds()
    {
        int storeCalls = 0, independentCalls = 0;
        using var store = Store((descriptor, _) => { Interlocked.Increment(ref storeCalls); return Sphere(descriptor); });
        using var independent = Store((descriptor, _) => { Interlocked.Increment(ref independentCalls); return Sphere(descriptor); });
        try
        {
            using WorldLease layoutFive = store.Acquire(Earth());
            using WorldLease equivalent = store.Acquire(Earth());
            using WorldLease layoutZero = store.Acquire(Earth(parameters: WalkingParameters.Earth with { LayoutVariant = 0 }));
            using WorldLease smaller = store.Acquire(Earth(1100));
            using WorldLease otherStore = independent.Acquire(Earth());
            Assert.True(ReferenceEquals(layoutFive.Surface, equivalent.Surface), "equivalent metadata shares within its store");
            Assert.True(!ReferenceEquals(layoutFive.Surface, layoutZero.Surface), "explicit layout zero cannot borrow layout five's world");
            Assert.True(!ReferenceEquals(layoutFive.Surface, smaller.Surface), "different game radii are different resident identities");
            Assert.True(!ReferenceEquals(layoutFive.Surface, otherStore.Surface), "independent stores do not secretly share a static world cache");
            Assert.Equal(5, layoutFive.Descriptor.LayoutVariant, "production Earth keeps layout five");
            Assert.Equal(0, layoutZero.Descriptor.LayoutVariant, "the explicit zero override survives acquisition");
            Assert.Near(1100, smaller.Surface.Radius, 0, "factory receives the smaller descriptor radius");
            Assert.Equal(3, storeCalls, "the first store builds each distinct identity once");
            Assert.Equal(1, independentCalls, "the independent store constructs its own world through its own factory");
            Check(store, residents: 3, building: 0, leases: 4, waiters: 0);
            Check(independent, residents: 1, building: 0, leases: 1, waiters: 0);
        }
        finally { Complete(store.BeginRetirement()); Complete(independent.BeginRetirement()); }
    }

    private static PlanetEnvironmentDescriptor Earth(double radius = 1200, WalkingParameters? parameters = null) =>
        EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath, radius,
            walkingParameters: parameters, diagnostic: parameters != null)
        ?? throw new TestFailure("Earth metadata must resolve without constructing its terrain");

    private static ISurface Sphere(PlanetEnvironmentDescriptor descriptor) => new SphereSurface(descriptor.Radius, 71);

    private static WorldStoreOptions Options(int worlds = 4, long residentBytes = 64, long buildBytes = 32,
        int builds = 4, int waiters = 8) => new()
    {
        MaxResidentWorlds = worlds,
        MaxEstimatedResidentBytes = residentBytes,
        MaxReservedBuildBytes = buildBytes,
        MaxConcurrentBuilds = builds,
        MaxPendingAcquisitions = waiters,
        DefaultEstimatedResidentBytes = ResidentBytes,
        DefaultReservedBuildBytes = BuildBytes,
    };

    private static WorldStore Store(Func<PlanetEnvironmentDescriptor, CancellationToken, ISurface> factory,
        WorldStoreOptions? options = null) => new(options ?? Options(), factory,
            static _ => new WorldMemoryEstimate(ResidentBytes, BuildBytes));

    private static void Check(WorldStore store, int residents, int building, int leases, int waiters, int cleanupFailures = 0)
    {
        WorldStoreDiagnostics state = store.Diagnostics;
        Assert.Equal(residents, state.ResidentWorlds, "resident worlds");
        Assert.Equal(building, state.BuildingWorlds, "scheduled or running factories");
        Assert.Equal(leases, state.ActiveLeases, "active lease owners");
        Assert.Equal(waiters, state.PendingAcquisitions, "pending acquisition claims");
        Assert.Equal(residents * ResidentBytes, state.EstimatedResidentBytes, "resident byte accounting");
        Assert.Equal(building * ResidentBytes, state.ReservedResidentBytes, "future resident byte reservations");
        Assert.Equal(building * BuildBytes, state.ReservedBuildBytes, "running factory scratch reservations");
        Assert.Equal(0, state.PendingDisposals, "no unaccounted disposal remains at this controlled observation point");
        Assert.Equal(0L, state.PendingDisposalBytes, "no disposal byte reservation remains at this controlled observation point");
        Assert.Equal(cleanupFailures, state.CleanupFailures, "all output-cleanup failures remain observable");
    }

    private static void CheckBudget(WorldStoreOptions options, string budget)
    {
        using var factory = new BlockedFactory();
        using var store = Store(factory.Build, options);
        Task<WorldLease>? acquisition = null;
        try
        {
            acquisition = store.AcquireAsync(Earth());
            factory.WaitForEntry();
            Refuse<WorldStoreCapacityException>(() => Complete(store.AcquireAsync(Earth(1100))));
            Assert.Equal(1, factory.Calls, budget + " refusal happens before dispatch");
            Check(store, residents: 0, building: 1, leases: 0, waiters: 1);
            factory.Release.Set();
            _ = Complete(acquisition);
            Check(store, residents: 1, building: 0, leases: 1, waiters: 0);
        }
        finally
        {
            factory.Release.Set();
            Cleanup(store, acquisition);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WorldLease, WeakReference<ISurface>) EvictAndKeepDisposedHandle(WorldStore store)
    {
        WorldLease lease = store.Acquire(Earth());
        var weak = new WeakReference<ISurface>(lease.Surface);
        lease.Dispose();
        Assert.Equal(1, store.Trim(), "the weak-reference fixture has evicted its world");
        return (lease, weak);
    }

    private static T Complete<T>(Task<T> task) => task.WaitAsync(Guard).GetAwaiter().GetResult();
    private static void Complete(Task task) => task.WaitAsync(Guard).GetAwaiter().GetResult();

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T expected) { return expected; }
        throw new TestFailure("Expected " + typeof(T).Name + ".");
    }

    private static T Refuse<T>(Func<WorldLease> acquire) where T : Exception => Expect<T>(() =>
    {
        // Even a regression which unexpectedly admits the request must not leak its lease during cleanup.
        using WorldLease unexpected = acquire();
    });

    private static void Cleanup(WorldStore store, params Task<WorldLease>?[] acquisitions)
    {
        // Begin first so an unexpected pending waiter cannot prolong cleanup. Barriers are released by callers.
        Task retirement = store.BeginRetirement();
        Exception? failure = null;
        foreach (Task<WorldLease>? acquisition in acquisitions)
        {
            if (acquisition == null) continue;
            try { Complete(acquisition).Dispose(); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception error) { failure ??= error; }
        }
        try { Complete(retirement); }
        catch (Exception error) { failure ??= error; }
        if (failure != null) throw new TestFailure("Store-test cleanup failed: " + failure);
    }

    private sealed class FailingDisposalSurface : ISurface, IDisposable
    {
        private readonly SphereSurface _sphere;
        private int _disposeCalls;

        public FailingDisposalSurface(double radius, string message)
        {
            _sphere = new SphereSurface(radius, 71);
            Failure = new InvalidOperationException(message);
        }

        public InvalidOperationException Failure { get; }
        public int DisposeCalls => Volatile.Read(ref _disposeCalls);
        public double Radius => _sphere.Radius;
        public double SeaRadius => _sphere.SeaRadius;
        public double HeightAt(Vector3D direction) => _sphere.HeightAt(direction);
        public void Dispose() { Interlocked.Increment(ref _disposeCalls); throw Failure; }
    }

    private sealed class BlockedFactory : IDisposable
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public ManualResetEventSlim Cancelled { get; } = new();

        public ISurface Build(PlanetEnvironmentDescriptor descriptor, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _calls);
            using CancellationTokenRegistration registration = cancellation.Register(() => Cancelled.Set());
            Entered.Set();
            if (!Release.Wait(Guard)) throw new TimeoutException("The test did not release its world factory barrier.");
            // Deliberately ignore cancellation so tests can distinguish signalling from actual retirement.
            return Sphere(descriptor);
        }

        public void WaitForEntry() => Assert.True(Entered.Wait(Guard), "the controlled factory starts");
        public void Dispose() { Entered.Dispose(); Release.Dispose(); Cancelled.Dispose(); }
    }
}
