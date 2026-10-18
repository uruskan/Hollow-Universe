using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;
using JoltPhysicsSharp;

namespace Hollow.Tests;

/// <summary>Isolated native qualification fixtures. Synthetic physical pieces are not production asset admission.</summary>
public static class SelectedPhysicsGroundTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly Vector3 Near = new(.4f, .35f, 12);

    [Test]
    public static void SelectedJoltFailedTicketsCannotGrowRetainedEntriesBeyondTheOwnerAllowance()
    {
        using var fixture = new Fixture();
        var budget = new GroundStreamingBudget(Limits() with
        { MaxTiles = 1, MaxOutstanding = 1, MaxExecuting = 1, MaxCompletionSlots = 1, MaxBodies = 1 });
        var hooks = new GroundPhysicsHooks { At = at =>
        { if (at == GroundNativeStage.BeforeMesh) throw new InvalidOperationException("injected failed retained entry"); } };
        using PhysicsWorld world = Open(fixture, budget, hooks,
            new GroundPhysicsOptions { MaxRequestTiles = 1, MaxWorkers = 1, MaxAdoptionsPerPump = 1 });
        using GroundAreaTicket failed = Request(world, Near); world.PumpSelectedGround(synchronous: true);
        Assert.True(failed.State == GroundAreaStatus.Failed && budget.Diagnostics.Tiles == 0,
            "failed ticket retains its entry after the work reservation retires");
        hooks.At = null;
        GroundAreaRequest refused = world.RequestGround(new Vector3(12, 0, 0), 0);
        Assert.True(refused.Status == GroundAreaStatus.Capacity && refused.Ticket == null,
            "a still-live failed entry cannot be hidden by the now-empty work ledger");
        failed.Dispose();
        using GroundAreaTicket next = Request(world, new Vector3(12, 0, 0)); Ready(world, next);
        next.Dispose(); world.Dispose(); Empty(budget);
    }

    [Test]
    public static void SelectedJoltRetainedBackingCapacityIsReservedBeforeNativeSetupAndUntilOwnerRetires()
    {
        using var fixture = new Fixture(); var hooks = new GroundPhysicsHooks();
        var large = Limits() with { MaxTiles = GroundCollisionLayout.TileIdCount,
            MaxBodies = GroundCollisionLayout.TileIdCount, MaxAreaTickets = 4096 };
        var refused = new GroundStreamingBudget(large);
        Assert.True(!PhysicsWorld.TryCreateSelected(fixture.Lease, ZoneFrame.World, refused, Options(), hooks,
            out PhysicsWorld? absent, out GroundPhysicsFailure? failure), "full high-water backing storage exceeds the four MiB fixture allowance");
        using (absent) using (failure)
        {
            Assert.True(failure is { Status: GroundAreaStatus.Capacity, RetirementPending: false }, "backing capacity refusal is explicit");
            Assert.Equal(0, hooks.SetupResources, "backing capacity is admitted before any native setup");
            Assert.Equal(1, fixture.Store.Diagnostics.ActiveLeases, "capacity refusal releases the speculative source pin");
        }
        Empty(refused);

        GroundStreamingLimits limits = Limits();
        long retained = checked(GroundStreamingBudget.OwnerMetadataBytes(limits) + PhysicsWorld.SelectedOwnerManagedBytes(limits));
        var budget = new GroundStreamingBudget(limits with { MaxMetadataBytes = retained + GroundStreamingBudget.RecordBytes });
        using PhysicsWorld world = Open(fixture, budget, hooks);
        Assert.Equal(retained, budget.Diagnostics.MetadataBytes, "owner reserves both sets of retained backing arrays upfront");
        GroundAreaTicket ticket = Request(world, Near); Ready(world, ticket); ticket.Dispose();
        Assert.Equal(0, budget.Diagnostics.Tiles, "all live tile records have retired");
        Assert.Equal(retained, budget.Diagnostics.MetadataBytes, "empty collection backing storage stays charged");
        Assert.True(!budget.TryOpenOwner(out _), "another owner cannot spend retained high-water metadata capacity");
        world.Dispose(); Empty(budget);
    }

    [Test]
    public static void SelectedJoltPartialSetupFailuresReleaseEveryNativeOwnerAndSourcePin()
    {
        using var fixture = new Fixture();
        foreach (GroundNativeStage stage in new[] { GroundNativeStage.SetupParked, GroundNativeStage.SetupLayers,
            GroundNativeStage.SetupBroadPhase, GroundNativeStage.SetupFilters, GroundNativeStage.SetupSystem })
        {
            var hooks = new GroundPhysicsHooks { At = at => { if (at == stage) throw new InvalidOperationException("injected setup failure"); } };
            var budget = new GroundStreamingBudget(Limits());
            bool opened = PhysicsWorld.TryCreateSelected(fixture.Lease, ZoneFrame.World, budget, Options(), hooks, out PhysicsWorld? world,
                out GroundPhysicsFailure? failure);
            using PhysicsWorld? cleanupWorld = world; using GroundPhysicsFailure? cleanupFailure = failure;
            Assert.True(!opened, stage + " refuses publication");
            Assert.True(world == null && failure is { Status: GroundAreaStatus.Failed, RetirementPending: false }, "failed setup fully rolls back");
            Assert.Equal(0, hooks.SetupResources, "every partially created setup resource is disposed");
            Assert.Equal(1, fixture.Store.Diagnostics.ActiveLeases, "failed construction releases its private source fork");
            Empty(budget); failure!.Dispose();
        }
    }

    [Test]
    public static void SelectedJoltFailedSetupRollbackReturnsAnOwnedRetryHandle()
    {
        using var fixture = new Fixture(); var budget = new GroundStreamingBudget(Limits());
        var hooks = new GroundPhysicsHooks { At = at =>
        {
            if (at is GroundNativeStage.SetupFilters or GroundNativeStage.BeforeSetupDispose)
                throw new InvalidOperationException("injected setup rollback failure");
        } };
        bool opened = PhysicsWorld.TryCreateSelected(fixture.Lease, ZoneFrame.World, budget, Options(), hooks,
            out PhysicsWorld? world, out GroundPhysicsFailure? failure);
        try
        {
            Assert.True(!opened, "failed setup does not publish a physics world");
            Assert.True(failure!.RetirementPending && hooks.SetupResources == 4, "failure owns every unretired native resource");
            Assert.Equal(1, budget.Diagnostics.Owners, "failed native teardown keeps its setup reservation");
            Assert.Equal(2, fixture.Store.Diagnostics.ActiveLeases, "failed native teardown keeps its source pin");
        }
        finally { hooks.At = null; world?.Dispose(); failure?.Dispose(); }
        Assert.True(!failure!.RetirementPending && hooks.SetupResources == 0, "explicit retry completes exact native ownership");
        Assert.Equal(1, fixture.Store.Diagnostics.ActiveLeases, "retry releases the construction source fork last"); Empty(budget);
    }

    [Test]
    public static void SelectedJoltNonemptyFormationAndTerrainHitThroughSharedReadyTickets()
    {
        using var fixture = new Fixture(); var hooks = new GroundPhysicsHooks(); var budget = new GroundStreamingBudget(Limits());
        using PhysicsWorld world = Open(fixture, budget, hooks);
        GroundAreaTicket first = Request(world, Near), second = Request(world, Near);
        Assert.Equal(GroundAreaStatus.Pending, first.State, "an admitted unbuilt area is pending");
        Assert.Equal(1, budget.Diagnostics.Tiles, "overlapping requests share one future physical tile");
        Ready(world, first);
        Assert.Equal(GroundAreaStatus.Ready, second.State, "both requests observe the same adopted body");
        Assert.Equal(1, hooks.Bodies, "only one native body is published");
        var formation = Ray(world, new Vector3(.4f, .35f, 15), -Vector3.UnitZ, 8);
        Assert.True(formation.Point.Z > 10.8f && formation.Point.Z < 11.2f, "native ray hits the raised sealed module above the radial terrain");
        Assert.True(float.IsFinite(formation.Normal.X) && MathF.Abs(formation.Normal.Length() - 1) < .0001f,
            "native formation hit supplies a finite unit face normal");
        var terrain = Ray(world, new Vector3(3, 2, 15), -Vector3.UnitZ, 8);
        Assert.True(terrain.Point.Z > 9 && terrain.Point.Z < 10, "the same native tile includes canonical ground away from the formation");
        first.Dispose();
        Assert.True(second.State == GroundAreaStatus.Ready && hooks.Bodies == 1, "releasing one request cannot evict the other's collision");
        second.Dispose();
        Assert.True(hooks.Bodies == 0 && hooks.Shapes == 0 && world.TileCount == 0, "last reference destroys the selected body and shape");
        using GroundAreaTicket again = Request(world, Near); Ready(world, again);
        Assert.Equal(formation.Body, Ray(world, new Vector3(.4f, .35f, 15), -Vector3.UnitZ, 8).Body, "recreated tile retains its canonical body ID");
        Assert.Equal(2, world.BodiesMade, "destroyed ground body is recreated rather than retained in an uncharged pool");
        again.Dispose(); world.Dispose(); Empty(budget);
    }

    [Test]
    public static void SelectedJoltCompleteAreaCapacityRefusesWithoutChangingCurrentGround()
    {
        using var fixture = new Fixture();
        var limits = Limits() with { MaxTiles = 1, MaxOutstanding = 1, MaxExecuting = 1, MaxCompletionSlots = 1, MaxBodies = 1 };
        var budget = new GroundStreamingBudget(limits); var hooks = new GroundPhysicsHooks();
        using PhysicsWorld world = Open(fixture, budget, hooks, new GroundPhysicsOptions { MaxRequestTiles = 1, MaxWorkers = 1, MaxAdoptionsPerPump = 1 });
        using GroundAreaTicket current = Request(world, Near); Ready(world, current);
        GroundStreamingDiagnostics before = budget.Diagnostics;
        GroundAreaRequest refused = world.RequestGround(new Vector3(12, 0, 0), 0);
        Assert.True(refused.Status == GroundAreaStatus.Capacity && refused.Ticket == null, "another complete face cannot fit one shared tile");
        Assert.Equal(before, budget.Diagnostics, "capacity rollback leaves every retained reservation unchanged");
        Assert.True(current.State == GroundAreaStatus.Ready && hooks.Bodies == 1, "current coherent collision survives refusal");
        GroundAreaRequest corridor = world.RequestGround(Near, new Vector3(12, 0, 0), 0);
        Assert.Equal(GroundAreaStatus.Capacity, corridor.Status, "a long swept request is counted completely before any partial publication");
        Assert.Equal(GroundAreaStatus.Cancelled, world.RequestGround(Near, 0, new CancellationToken(true)).Status, "cancelled request publishes no ticket");
        current.Dispose(); world.Dispose(); Empty(budget);
    }

    [Test]
    public static void SelectedJoltShapeAndPartialBodyFailuresCleanTheirActualNativeHandles()
    {
        using var fixture = new Fixture();
        foreach (GroundNativeStage stage in new[] { GroundNativeStage.BeforeMesh, GroundNativeStage.MeshSettings,
            GroundNativeStage.ShapeCreated, GroundNativeStage.BeforePublication, GroundNativeStage.BodyCreated, GroundNativeStage.BodyAdded })
        {
            var budget = new GroundStreamingBudget(Limits());
            bool reached = false;
            var hooks = new GroundPhysicsHooks { At = at =>
            { if (at == stage) { reached = true; throw new InvalidOperationException("injected tile stage failure"); } } };
            using PhysicsWorld world = Open(fixture, budget, hooks);
            using GroundAreaTicket ticket = Request(world, Near);
            world.PumpSelectedGround(synchronous: true);
            Assert.True(reached, stage + " was actually reached before the controlled native failure");
            Assert.Equal(GroundAreaStatus.Failed, ticket.State, stage + " cannot report ready");
            Assert.True(hooks.MeshSettings == 0 && hooks.Shapes == 0 && hooks.Bodies == 0,
                stage + " cleans settings, shape and any partially published body");
            Assert.Equal(0, budget.Diagnostics.Tiles, "failed work releases future output only after native cleanup");
            ticket.Dispose(); world.Dispose(); Empty(budget);
            Assert.Equal(1, fixture.Store.Diagnostics.ActiveLeases, "failed tile owner releases the source only after setup retires");
        }
    }

    [Test]
    public static void SelectedJoltPreReturnCleanupFailureKeepsItsResourceAndReservation()
    {
        using var fixture = new Fixture(); var budget = new GroundStreamingBudget(Limits());
        var hooks = new GroundPhysicsHooks { At = at =>
        { if (at is GroundNativeStage.ShapeCreated or GroundNativeStage.BeforeTileDispose) throw new InvalidOperationException("injected persistent rollback failure"); } };
        using PhysicsWorld world = Open(fixture, budget, hooks);
        using GroundAreaTicket ticket = Request(world, Near);
        try
        {
            GroundCollisionPlanTests.Reject<InvalidOperationException>(() => world.PumpSelectedGround(synchronous: true));
            Assert.True(hooks.Shapes == 1 && hooks.MeshSettings == 1, "failed pre-return rollback remains a concrete owned native resource");
            Assert.True(budget.Diagnostics.Tiles == 1 && budget.Diagnostics.Bodies == 1 && budget.Diagnostics.NativeShapeBytes > 0 &&
                budget.Diagnostics.NativeBuildBytes > 0 && budget.Diagnostics.ManagedBuildBytes > 0 && budget.Diagnostics.Executing == 0,
                "failed rollback keeps output/body and retained settings/build reservations after the callback stops executing");
            Assert.True(ticket.State == GroundAreaStatus.Failed && fixture.Store.Diagnostics.ActiveLeases == 2,
                "failed work cannot authorize queries or release its source");
        }
        finally { hooks.At = null; world.Dispose(); }
        Assert.True(hooks.Shapes == 0 && hooks.MeshSettings == 0 && hooks.SetupResources == 0, "retry retires every actual native handle"); Empty(budget);
    }

    [Test]
    public static void SelectedJoltCancellationKeepsExecutingWorkAndSourceUntilActualExit()
    {
        using var fixture = new Fixture(); var budget = new GroundStreamingBudget(Limits());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var disposing = new ManualResetEventSlim();
        var hooks = new GroundPhysicsHooks { At = stage =>
        {
            if (stage != GroundNativeStage.BeforeMesh) return;
            entered.Set(); if (!release.Wait(Guard)) throw new TimeoutException("Native fixture worker was not released.");
        } };
        using PhysicsWorld world = Open(fixture, budget, hooks);
        GroundAreaTicket ticket = Request(world, Near); world.PumpSelectedGround();
        try
        {
            Assert.True(entered.Wait(Guard), "bounded worker reaches its controlled barrier");
            fixture.Lease.Dispose(); ticket.Dispose();
            Assert.True(budget.Diagnostics.Executing == 1 && budget.Diagnostics.ManagedBuildBytes > 0 && budget.Diagnostics.Bodies == 1,
                "cancelling a running request keeps its execution and future output charged");
            Assert.True(fixture.Store.Diagnostics.ActiveLeases == 1 && !fixture.Source.Disposed, "the native reader owns its independent source pin");
            Task observe = Task.Run(() =>
            {
                try
                {
                    Assert.True(disposing.Wait(Guard), "owner starts retirement");
                    bool available = JoltRuntime.Gate.TryEnter(Guard);
                    if (available) JoltRuntime.Gate.Exit();
                    Assert.True(available, "retirement never waits for a worker while holding the Jolt gate");
                    Assert.Equal(1, fixture.Store.Diagnostics.ActiveLeases, "source remains pinned while the running callback is blocked");
                }
                finally { release.Set(); }
            });
            disposing.Set(); world.Dispose(); observe.WaitAsync(Guard).GetAwaiter().GetResult();
            Assert.Equal(0, fixture.Store.Diagnostics.ActiveLeases, "source fork releases after worker and native setup exit");
            Assert.True(ticket.State == GroundAreaStatus.Retired && hooks.Shapes == 0 && hooks.Bodies == 0, "late cancelled output is never published"); Empty(budget);
        }
        finally { release.Set(); }
    }

    [Test]
    public static void SelectedJoltActiveAndRetiringWorldsShareOneFiniteAllowance()
    {
        using var fixture = new Fixture();
        var budget = new GroundStreamingBudget(Limits() with
        { MaxTiles = 1, MaxOutstanding = 1, MaxExecuting = 1, MaxCompletionSlots = 1, MaxBodies = 1 });
        var options = new GroundPhysicsOptions { MaxRequestTiles = 1, MaxWorkers = 1, MaxAdoptionsPerPump = 1 };
        using PhysicsWorld a = Open(fixture, budget, new GroundPhysicsHooks(), options);
        using PhysicsWorld b = Open(fixture, budget, new GroundPhysicsHooks(), options);
        using GroundAreaTicket first = Request(a, Near); Ready(a, first);
        Assert.Equal(GroundAreaStatus.Retired, b.GroundState(first), "a live ready ticket from A cannot authorize another physics world");
        Assert.Equal(GroundAreaStatus.Capacity, b.RequestGround(Near, 0).Status, "world B cannot spend world A's physical output allowance");
        a.Dispose(); Assert.Equal(GroundAreaStatus.Retired, first.State, "old tickets cannot remain ready across retirement");
        using GroundAreaTicket second = Request(b, Near); Ready(b, second);
        Assert.Equal(1, budget.Diagnostics.Owners, "retiring A releases only its own setup");
        second.Dispose(); b.Dispose(); Empty(budget);
    }

    [Test]
    public static void SelectedJoltCompletedShapesRemainPendingUntilAdoptionAndShareSynchronousLimits()
    {
        using var fixture = new Fixture();
        var budget = new GroundStreamingBudget(Limits() with { MaxExecuting = 1, MaxCompletionSlots = 1 });
        var options = new GroundPhysicsOptions { MaxRequestTiles = 8, MaxWorkers = 1, MaxAdoptionsPerPump = 1 };
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int builds = 0;
        var hooks = new GroundPhysicsHooks { At = stage =>
        {
            if (stage != GroundNativeStage.BeforeMesh) return;
            Interlocked.Increment(ref builds); entered.Set();
            if (!release.Wait(Guard)) throw new TimeoutException("Completion fixture was not released.");
        } };
        using PhysicsWorld world = Open(fixture, budget, hooks, options);
        using GroundAreaTicket first = Request(world, Near), second = Request(world, new Vector3(12, 0, 0));
        try
        {
            world.PumpSelectedGround(); Assert.True(entered.Wait(Guard), "one native worker enters its reserved slot");
            world.PumpSelectedGround(synchronous: true);
            Assert.True(builds == 1 && budget.Diagnostics.Executing == 1, "synchronous work cannot bypass the already occupied execution allowance");
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => budget.Diagnostics.Executing == 0 && budget.Diagnostics.CompletionSlots == 1, Guard),
                "completed output retains its reserved completion slot");
            Assert.True(first.State == GroundAreaStatus.Pending && hooks.Bodies == 0, "a built but unpublished native shape cannot authorize queries");
            world.PumpSelectedGround(synchronous: true);
            Assert.True(first.State == GroundAreaStatus.Ready && second.State == GroundAreaStatus.Ready,
                "adoption frees completion capacity for the next bounded synchronous build");
        }
        finally { release.Set(); }
        first.Dispose(); second.Dispose(); world.Dispose(); Empty(budget);
    }

    [Test]
    public static void SelectedJoltCanonicalSeamsAndCornersWorkAcrossTheSupportedRadiusRange()
    {
        foreach (double radius in new[] { .4, 250, 1354.055000514614 })
        {
            using var fixture = new Fixture(radius); var budget = new GroundStreamingBudget(Limits());
            using PhysicsWorld world = Open(fixture, budget, new GroundPhysicsHooks());
            foreach (Vector3 direction in new[] { Vector3.UnitZ, Vector3.Normalize(new Vector3(1, 0, 1)), Vector3.Normalize(Vector3.One) })
            {
                using GroundAreaTicket ticket = Request(world, direction * (float)(radius + .5)); Ready(world, ticket);
                var hit = Ray(world, direction * (float)(radius + 2), -direction, 4);
                Assert.True(Math.Abs(hit.Point.Length() - radius) < .05, "actual native canonical triangles cover the requested face/seam/corner");
                ticket.Dispose(); Assert.Equal(0, world.TileCount, "each bounded native area retires before the next");
            }
            world.Dispose(); Empty(budget);
        }
    }

    [Test]
    public static void SelectedJoltCannotBypassAdmissionThroughLegacyEntrypoints()
    {
        using var fixture = new Fixture();
        GroundCollisionPlanTests.Reject<NotSupportedException>(() => new PhysicsWorld(fixture.Source, ZoneFrame.World));
        var budget = new GroundStreamingBudget(Limits()); var hooks = new GroundPhysicsHooks();
        using PhysicsWorld world = Open(fixture, budget, hooks);
        GroundCollisionPlanTests.Reject<NotSupportedException>(() => world.EnsureGround(Near));
        GroundCollisionPlanTests.Reject<NotSupportedException>(() => world.Prefetch(Near));
        GroundCollisionPlanTests.Reject<NotSupportedException>(() => world.GroundReady(Near));
        GroundCollisionPlanTests.Reject<NotSupportedException>(() => world.Listener = new Listener());
        GroundCollisionPlanTests.Reject<NotSupportedException>(() => world.AddProp(PropLayer.Nature, 1, PropShape.Sphere(Near, 1)));
        Assert.True(hooks.Bodies == 0 && budget.Diagnostics.Tiles == 0, "rejected legacy entrypoints perform no ground or prop work");
        world.Dispose(); Empty(budget);
        var noSetup = new GroundStreamingBudget(Limits() with { MaxNativeSetupBytes = 1 });
        Assert.True(!PhysicsWorld.TryCreateSelected(fixture.Lease, ZoneFrame.World, noSetup, Options(), hooks, out _, out GroundPhysicsFailure? refusal),
            "setup capacity refuses before native allocation");
        Assert.Equal(GroundAreaStatus.Capacity, refusal!.Status, "capacity is explicit");
        Assert.True(hooks.SetupResources == 0 && fixture.Store.Diagnostics.ActiveLeases == 1, "setup refusal owns no native or source residue"); Empty(noSetup);
    }

    private static GroundPhysicsOptions Options() => new() { MaxRequestTiles = 64, MaxWorkers = 2, MaxAdoptionsPerPump = 2 };
    private static GroundStreamingLimits Limits() => new()
    {
        Native = new GroundNativeEstimates { Contract = "isolated-native-fixture-estimates-not-measurements", ShapeFixed = 65536,
            ShapePerVertex = 32, ShapePerTriangle = 96, BuildFixed = 131072, BuildPerVertex = 64, BuildPerTriangle = 192,
            BodyBytes = 2048, SetupBytes = 16 * 1024 * 1024 },
        MaxOwners = 2, MaxTiles = 64, MaxOutstanding = 64, MaxExecuting = 2, MaxCompletionSlots = 2, MaxBodies = 64, MaxParked = 0,
        MaxAreaTickets = 8, MaxPlanning = 1, MaxPlanBytes = 8 * 1024 * 1024, MaxAreaBytes = 65536,
        MaxPlanningBytes = 64 * 1024 * 1024, MaxManagedBuildBytes = 64 * 1024 * 1024, MaxNativeBuildBytes = 64 * 1024 * 1024,
        MaxNativeShapeBytes = 64 * 1024 * 1024, MaxNativeBodyBytes = 256 * 1024, MaxNativeSetupBytes = 32 * 1024 * 1024,
        MaxMetadataBytes = 4 * 1024 * 1024,
    };
    private static PhysicsWorld Open(Fixture fixture, GroundStreamingBudget budget, GroundPhysicsHooks hooks, GroundPhysicsOptions? options = null)
    {
        bool opened = PhysicsWorld.TryCreateSelected(fixture.Lease, ZoneFrame.World, budget, options ?? Options(), hooks,
            out PhysicsWorld? world, out GroundPhysicsFailure? failure);
        if (!opened) { hooks.At = null; failure?.Dispose(); }
        Assert.True(opened, "selected native fixture opens: " + failure?.Message);
        return world!;
    }
    private static GroundAreaTicket Request(PhysicsWorld world, Vector3 point)
    {
        GroundAreaRequest result = world.RequestGround(point, 0);
        Assert.True(result.Ticket != null && result.Status is GroundAreaStatus.Pending or GroundAreaStatus.Ready,
            "complete native area is admitted: " + result.Reason);
        return result.Ticket!;
    }
    private static void Ready(PhysicsWorld world, GroundAreaTicket ticket)
    {
        for (int i = 0; i < 64 && ticket.State == GroundAreaStatus.Pending; i++) world.PumpSelectedGround(synchronous: true);
        Assert.Equal(GroundAreaStatus.Ready, ticket.State, "all requested native bodies are adopted: " + ticket.Failure);
    }
    private static (Vector3 Point, Vector3 Normal, uint Body) Ray(PhysicsWorld world, Vector3 origin, Vector3 direction, float length)
    {
        lock (JoltRuntime.Gate)
        {
            world.OptimizeBroadPhase(); var ray = new Ray(origin, direction * length);
            Assert.True(world.Jolt.NarrowPhaseQuery.CastRay(ray, out RayCastResult result), "actual selected Jolt geometry intersects the qualification ray");
            Vector3 point = ray.GetPointOnRay(result.Fraction), normal = Vector3.Zero;
            BodyLockInterface locks = world.Jolt.BodyLockInterfaceNoLock; locks.LockRead(result.BodyID, out BodyLockRead read);
            try
            {
                Assert.True(read.Succeeded && read.Body != null, "native body remains owned during the hit query");
                normal = read.Body!.GetWorldSpaceSurfaceNormal((SubShapeID)result.subShapeID2, point);
            }
            finally { locks.UnlockRead(read); }
            return (point, normal, result.BodyID.ID);
        }
    }
    private static void Empty(GroundStreamingBudget budget) => Assert.Equal(default(GroundStreamingDiagnostics), budget.Diagnostics,
        "all native, queued, area and setup reservations return to zero");
    private sealed class Listener : ITileListener
    { public void TileAdded(TileKey tile) { } public void TileRemoved(TileKey tile) { } }

    private sealed class Fixture : IDisposable
    {
        internal PlannedSource Source { get; }
        internal WorldStore Store { get; }
        internal WorldLease Lease { get; }
        internal Fixture(double? emptyRadius = null)
        {
            Source = new PlannedSource(emptyRadius);
            // An injected diagnostic source exercises real WorldLease ownership without qualifying an actual catalog.
            PlanetEnvironmentDescriptor descriptor = EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath,
                Source.Radius, EnvironmentSurfaceKind.StandIn, diagnostic: true) ?? throw new TestFailure("Diagnostic metadata did not resolve.");
            Store = new WorldStore(new WorldStoreOptions(), (_, _) => Source, _ => new WorldMemoryEstimate(1024 * 1024, 1024 * 1024));
            Lease = Store.Acquire(descriptor);
        }
        public void Dispose() { Lease.Dispose(); Store.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class PlannedSource : IPlannedGroundCollision, ITerrainLatticeSurface, ITerrainPinnedSurface, IFormationSurface, IDisposable
    {
        private readonly object _identity = new();
        private readonly FormationField _field;
        private readonly SelectedGroundCollisionLimits _limits = SelectedGroundCollisionTests.Limits();
        public TerrainLattice TerrainLattice { get; }
        public TerrainPinSet? TerrainPins { get; }
        public FormationField? Formations => _field;
        internal bool Disposed;
        internal PlannedSource(double? emptyRadius)
        {
            if (emptyRadius is { } radius)
            {
                TerrainLattice = TerrainLattice.ForRadius(radius);
                var contract = new FormationFieldContract(new string('0', 64), radius, TerrainLattice.FinestTileLevel,
                    FormationFieldAdmissionPolicy.IndependentPieces);
                _field = new FormationField(contract, new string('0', 64), Array.Empty<FormationPiece>(), Array.Empty<FormationCohort>(),
                    Array.Empty<FormationFieldRejection>(), new FormationSupportIndex(Array.Empty<FormationPiece>()), new FormationPlanDiagnostics(), default);
            }
            else
            {
                var fixture = new SelectedGroundCollisionTests.Fixture(); fixture.Build(fixture.Piece(3, new Vector3D(.4, .4, 9)));
                Assert.True(fixture.Field.Pieces.Length == 1 && fixture.Field.Cohorts.Length == 1, "native fixture contains one continuously sealed physical module");
                TerrainLattice = fixture.Terrain.TerrainLattice; TerrainPins = fixture.Terrain.TerrainPins; _field = fixture.Field;
            }
            GroundCollisionLayout.Validate(TerrainLattice, _limits);
            Assert.True(ReferenceEquals(TerrainPins, TerrainPinSet.Of(this)), "native fixture exposes the authenticated field owning its pins");
        }
        public bool HasPlannedGroundCollision => true;
        public GroundCollisionPreparationBounds CollisionPreparationBounds => GroundCollisionPreparationBounds.From(_limits);
        public double Radius => TerrainLattice.Radius;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) { ObjectDisposedException.ThrowIf(Disposed, this); return 0; }
        public SelectedGroundCollisionPlan PrepareGroundCollision(in ZoneFrame frame, TileKey tile, double margin, CancellationToken cancellation = default) =>
            SelectedGroundCollision.Prepare(_identity, TerrainLattice, frame, tile, margin, _field,
                Array.Empty<SelectedGroundCollision.Monument>(), _limits, cancellation);
        public TriangleMesh BuildGroundCollision(SelectedGroundCollisionPlan plan, CancellationToken cancellation = default) =>
            SelectedGroundCollision.Build(this, _identity, plan, cancellation);
        public void Dispose() => Disposed = true;
    }
}
