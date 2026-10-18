using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class NavigationLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Test]
    public static void NavigationStopClosesAdmissionAndDrainsItsBlockedReader() => CheckNavigation(fault: false);

    [Test]
    public static void NavigationFailureCannotAbandonAnotherReaderOrResidentAreas() => CheckNavigation(fault: true);

    [Test]
    public static void NavigationAdoptionObservesAndReportsFailedBuilds()
    {
        var surface = new TestSurface();
        using var nature = new ControlledNature(surface.Radius, fault: true);
        using var nav = new NavGrid(surface, ZoneFrame.World, nature, ContentFixture.Content);
        nav.Prefetch(nature.Point(nature.FailedTile), 0);
        Assert.True(nature.Failed.Wait(Bound), "the navigation source throws before adoption");
        Exception? failure = null;
        bool observed = SpinWait.SpinUntil(() =>
        {
            try { nav.Adopt(); }
            catch (AggregateException error) { failure = error; }
            return failure != null;
        }, Bound);
        Assert.True(observed, "adoption reports the completed task's failure rather than silently forgetting it");
        OriginalFailure(failure, nature.BuildFailure);
        nav.Dispose(); // The already-reported task is no longer pending and is not reported a second time.
    }

    private static void CheckNavigation(bool fault)
    {
        var surface = new TestSurface();
        using var nature = new ControlledNature(surface.Radius, fault);
        using var nav = new NavGrid(surface, ZoneFrame.World, nature, ContentFixture.Content);
        nav.Ensure(nature.Point(nature.ResidentTile));
        int published = nav.Built;
        Assert.True(nav.Count > 0, "the fixture starts with a resident navigation area");
        try
        {
            if (fault)
            {
                nav.Prefetch(nature.Point(nature.FailedTile), 0);
                Assert.True(nature.Failed.Wait(Bound), "one admitted navigation build fails");
            }
            nav.Prefetch(nature.Point(nature.BlockedTile), 0);
            Assert.True(nature.Entered.Wait(Bound), "another admitted navigation build is inside its nature query");
            nav.Stop();
            nav.Stop();
            Assert.True(nav.IsStopped && !nature.Exited.IsSet, "stop closes admission without waiting for the blocked source");
            Refuses(() => nav.Prefetch(nature.Point(nature.ResidentTile), 0));
            Refuses(() => nav.Ensure(nature.Point(nature.ResidentTile)));
            nav.Adopt();
            Assert.Equal(published, nav.Built, "retired builds cannot publish");

            Exception? failure = DrainOnThread(nav.Dispose, () => nature.Release.Set());
            Assert.True(nature.Exited.IsSet, "the source reader exited before disposal completed");
            Assert.Equal(0, nav.Pending, "every admitted navigation task was drained");
            Assert.Equal(0, nav.Count, "resident and unadopted navigation areas were released");
            Assert.Equal(published, nav.Built, "draining does not publish cancelled results");
            nav.Adopt();
            nav.Release(0);
            Assert.True(!nav.IsReady(nature.Point(nature.ResidentTile)), "retired areas are never reported ready");
            if (fault) OriginalFailure(failure, nature.BuildFailure);
            else Assert.True(failure == null, $"cooperative cancellation is normal retirement: {failure}");
            nav.Dispose();
        }
        finally
        {
            nature.Release.Set();
            // A failed assertion still drains the real work before the fixture's wait handles are disposed.
            try { nav.Dispose(); } catch (AggregateException) { }
        }
    }

    [Test]
    public static void AnimalSystemDisposalRetiresItsNavigationWorld()
    {
        using var rig = new GameRig();
        AnimalSystem system = rig.Zone.Animals();
        AnimalWorld animals = system.World;
        animals.Nav.Ensure(rig.Zone.Landing);
        Assert.True(animals.Nav.Count > 0, "animal navigation is resident before system teardown");
        system.Dispose();
        Assert.True(system.World == null && animals.Nav.IsStopped && animals.Nav.Count == 0,
            "system teardown retires and drops the animal world");
        Refuses(animals.Step);
        Refuses(() => animals.Nav.Prefetch(rig.Zone.Landing, 0));
        system.Dispose();
        animals.Dispose();
    }

    [Test]
    public static void NaturalBodyRetirementDetachesAndDrainsAFaultingCapturedCallback()
    {
        var surface = new TestSurface();
        using var nature = new ControlledNature(surface.Radius, fault: false) { FailBlockedQuery = true };
        using var physics = new PhysicsWorld(surface, ZoneFrame.World) { MakePropBodiesAhead = false };
        var state = new NaturalState(ContentFixture.Content, nature);
        using var listener = new NaturalBodies(physics, state);
        // Prove the original event subscription is live before retiring it.
        state.Apply(42, new NaturalOverride { Depleted = true });
        state.Apply(42, null);
        Assert.Equal(1, nature.Lookups, "presence changes initially reach the listener");
        try
        {
            physics.PrefetchWithin(nature.Point(nature.BlockedTile), 0);
            Assert.True(nature.Entered.Wait(Bound), "physics captured and entered the natural-object listener");
            listener.Stop();
            listener.Stop();
            Assert.True(physics.Listener == null, "the listener detaches from physics immediately");
            state.Apply(42, new NaturalOverride { Depleted = true });
            state.Apply(42, null);
            Assert.Equal(1, nature.Lookups, "presence changes no longer query the retired source");
            listener.TilePreparing(nature.BlockedTile); // A callback captured before Stop may still arrive later.
            Assert.Equal(1, nature.BlockedCalls, "late callbacks are rejected before reading their source");

            Exception? listenerFailure = DrainOnThread(listener.Dispose, () => nature.Release.Set());
            Assert.True(listenerFailure == null && nature.Exited.IsSet, "callback failure still leaves the lifetime barrier");
            listener.Dispose();
            Exception? physicsFailure = null;
            try { physics.Dispose(); } catch (Exception error) { physicsFailure = error; }
            OriginalFailure(physicsFailure, nature.BuildFailure);
            Assert.Equal(IntPtr.Zero, physics.Jolt.Handle, "physics observes the callback fault and still releases its native world");
        }
        finally
        {
            nature.Release.Set();
            listener.Dispose();
            try { physics.Dispose(); } catch (AggregateException) { }
        }
    }

    [Test]
    public static void DisposedNaturalListenersAreNotRetainedByTheirPublishers()
    {
        using var physics = new PhysicsWorld(new TestSurface(), ZoneFrame.World);
        using var nature = new ControlledNature(physics.Surface.Radius, fault: false);
        var state = new NaturalState(ContentFixture.Content, nature);
        WeakReference retired = RetireListener(physics, state);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(!retired.IsAlive, "neither physics nor the presence event retains a disposed listener");
        using var replacement = new NaturalBodies(physics, state);
        WeakReference superseded = RetireListenerWithoutRemovingReplacement(physics, state, replacement);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(!superseded.IsAlive && ReferenceEquals(physics.Listener, replacement),
            "retiring an older listener preserves its replacement");
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetireListener(PhysicsWorld physics, NaturalState state)
    {
        var listener = new NaturalBodies(physics, state);
        listener.Dispose();
        listener.Dispose();
        return new WeakReference(listener);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetireListenerWithoutRemovingReplacement(PhysicsWorld physics, NaturalState state, NaturalBodies replacement)
    {
        var older = new NaturalBodies(physics, state);
        physics.Listener = replacement;
        older.Dispose();
        return new WeakReference(older);
    }

    private static Exception? DrainOnThread(Action dispose, Action release)
    {
        using var entered = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            entered.Set();
            try { dispose(); }
            catch (Exception error) { failure = error; }
            finally { finished.Set(); }
        }) { IsBackground = true, Name = "Navigation lifetime retirement" };
        thread.Start();
        try
        {
            Assert.True(entered.Wait(Bound), "retirement starts");
            bool waited = !finished.Wait(TimeSpan.FromMilliseconds(100));
            bool gateAvailable = JoltRuntime.Gate.TryEnter(Bound);
            if (gateAvailable) JoltRuntime.Gate.Exit();
            release();
            Assert.True(thread.Join(Bound), "retirement finishes after the admitted source returns");
            Assert.True(waited, "retirement waits for the admitted source reader");
            Assert.True(gateAvailable, "retirement does not hold the Jolt gate while waiting");
            return failure;
        }
        finally
        {
            release();
            Assert.True(thread.Join(Bound), "the retirement thread exits within the fixture bound");
        }
    }

    private static void Refuses(Action action)
    {
        bool refused = false;
        try { action(); } catch (ObjectDisposedException) { refused = true; }
        Assert.True(refused, "retired navigation refuses new queries and admission");
    }

    private static void OriginalFailure(Exception? actual, Exception expected)
    {
        Assert.True(actual is AggregateException, $"retirement reports aggregated failure: {actual}");
        AggregateException aggregate = ((AggregateException)actual!).Flatten();
        Assert.Equal(1, aggregate.InnerExceptions.Count, "the one failed build is reported once");
        Assert.True(ReferenceEquals(expected, aggregate.InnerExceptions[0]), "the original source exception is preserved");
    }

    private sealed class TestSurface : ISurface
    {
        public double Radius => 256;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0;
        public TriangleMesh GroundCollision(in ZoneFrame frame, TileKey tile, double margin) =>
            SurfaceGrid.Collision(this, frame, tile, margin, spacing: Radius);
    }

    private sealed class ControlledNature : INaturalObjects, IDisposable
    {
        private readonly double _radius;
        private readonly bool _fault;
        private int _lookups, _blockedCalls;
        public ControlledNature(double radius, bool fault)
        {
            _radius = radius;
            _fault = fault;
            Level = Math.Min(TileKey.LevelFor(radius, NavGrid.AreaSize), 20);
            int middle = (1 << Level) / 2;
            ResidentTile = new TileKey(0, Level, middle, middle);
            FailedTile = new TileKey(1, Level, middle, middle);
            BlockedTile = new TileKey(2, Level, middle, middle);
        }
        public int Level { get; }
        public TileKey ResidentTile { get; }
        public TileKey FailedTile { get; }
        public TileKey BlockedTile { get; }
        public bool FailBlockedQuery { get; init; }
        public int Lookups => Volatile.Read(ref _lookups);
        public int BlockedCalls => Volatile.Read(ref _blockedCalls);
        public InvalidOperationException BuildFailure { get; } = new("Controlled navigation/nature source failure.");
        public ManualResetEventSlim Failed { get; } = new();
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public ManualResetEventSlim Exited { get; } = new();
        public Vector3 Point(TileKey tile) => ZoneFrame.World.ToLocal(tile.Centre * _radius);
        public void Query(Vector3 centre, float radius, List<NaturalObject> into)
        {
            TileKey tile = TileKey.At(ZoneFrame.World.ToBody(centre), Level);
            if (_fault && tile == FailedTile)
            {
                Failed.Set();
                throw BuildFailure;
            }
            if (tile != BlockedTile) return;
            Interlocked.Increment(ref _blockedCalls);
            Entered.Set();
            try
            {
                if (!Release.Wait(Bound)) throw new TimeoutException("The lifetime fixture did not release its source query.");
                if (FailBlockedQuery) throw BuildFailure;
            }
            finally { Exited.Set(); }
        }
        public bool TryGet(ulong id, out NaturalObject found)
        {
            Interlocked.Increment(ref _lookups);
            found = default;
            return false;
        }
        public IReadOnlyList<NaturalObject> InTile(TileKey tile) => Array.Empty<NaturalObject>();
        public TileKey? TileOf(ulong id) => null;
        public void Dispose()
        {
            Failed.Dispose();
            Entered.Dispose();
            Release.Dispose();
            Exited.Dispose();
        }
    }
}
