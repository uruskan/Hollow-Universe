using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hollow.Core;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class ZoneEnvironmentLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Test]
    public static void ZonePinsItsEnvironmentUntilTheLastPhysicsReaderRetires() => CheckRetirement(false);

    [Test]
    public static void FailedSystemAndTileStillReleaseEveryOwnerBeforeTheEnvironment() => CheckRetirement(true);

    private static void CheckRetirement(bool fault)
    {
        ZoneSpec spec = Spec();
        using var barriers = new Barriers();
        var surface = new ControlledSurface(spec.Radius, barriers) { FailBuild = fault };
        using var store = Store(surface);
        var first = new TrackingSystem();
        var last = new FailingSystem(fault);
        var zone = new Zone(store.Acquire(spec.EnvironmentDescriptor()), spec, systems: new ZoneSystem[] { first, last });
        PhysicsWorld physics = zone.Physics;
        surface.OnDispose = () => Assert.Equal(IntPtr.Zero, physics.Jolt.Handle, "native physics retires before its generated source");
        Exception? failure = null;
        Thread? disposer = null;
        using var begun = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        try
        {
            physics.MakePropBodiesAhead = false;
            physics.PrefetchWithin(surface.BlockedPoint, 0);
            Assert.True(barriers.Entered.Wait(Bound), "the production prefetch reader enters its source");
            _ = store.BeginRetirement();
            disposer = new Thread(() =>
            {
                begun.Set();
                try { zone.Dispose(); }
                catch (Exception error) { failure = error; }
                finally { done.Set(); }
            }) { IsBackground = true, Name = "Zone environment retirement" };
            disposer.Start();
            Assert.True(begun.Wait(Bound), "zone retirement starts");
            Assert.True(!done.Wait(TimeSpan.FromMilliseconds(100)), "the zone waits for its already admitted reader");
            Assert.Equal(1, store.Diagnostics.ActiveLeases, "the zone keeps its pin throughout the blocked read");
            Assert.Equal(0, surface.Disposals, "the store cannot dispose a pinned source");
            Assert.True(!store.Retirement.IsCompleted, "store retirement waits for the zone pin");
            bool gateAvailable = JoltRuntime.Gate.TryEnter(Bound);
            if (gateAvailable) JoltRuntime.Gate.Exit();
            Assert.True(gateAvailable, "zone waits never hold the Jolt gate");
            barriers.Release.Set();
            Assert.True(disposer.Join(Bound), "zone retirement completes after the source returns");
            Assert.True(store.Retirement.Wait(Bound), "the store retires after the final lease");
            Assert.Equal(1, first.Disposals, "an earlier system is retired even when a later one fails");
            Assert.Equal(1, last.Disposals, "the failing system is retired once");
            Assert.Equal(1, surface.Disposals, "the generated source is disposed once");
            Assert.Equal(0, store.Diagnostics.ActiveLeases, "no zone pin remains");
            Assert.Equal(IntPtr.Zero, physics.Jolt.Handle, "native world retired after a worker fault too");
            if (fault)
            {
                Assert.True(failure is AggregateException, "cleanup failures are reported together after draining");
                Exception[] errors = ((AggregateException)failure!).Flatten().InnerExceptions.ToArray();
                Assert.Equal(2, errors.Length, "the system and worker failures are each reported once");
                Assert.True(errors.Contains(last.Failure) && errors.Contains(surface.BuildFailure), "original failures are retained");
            }
            else Assert.True(failure == null, $"normal retirement succeeds: {failure}");
            zone.Dispose();
            Assert.Equal(1, first.Disposals, "second disposal is idempotent after a reported failure too");
        }
        finally
        {
            barriers.Release.Set();
            Assert.True(disposer == null || disposer.Join(Bound), "the disposal thread cannot escape the fixture");
            try { zone.Dispose(); } catch { }
            store.Dispose();
            Assert.True(store.Retirement.Wait(Bound), "fixture world retirement completes");
        }
    }

    [Test]
    public static void FailedZoneConstructionDrainsStartedWorkAndReturnsItsTransferredLease()
    {
        ZoneSpec spec = Spec();
        using var barriers = new Barriers();
        var surface = new ControlledSurface(spec.Radius, barriers);
        using var store = Store(surface);
        WorldLease lease = store.Acquire(spec.EnvironmentDescriptor());
        var system = new ConstructionFailureSystem(surface);
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        var constructor = new Thread(() =>
        {
            try { _ = new Zone(lease, spec, systems: new ZoneSystem[] { system }); }
            catch (Exception error) { failure = error; }
            finally { done.Set(); }
        }) { IsBackground = true, Name = "Zone constructor rollback" };
        constructor.Start();
        try
        {
            Assert.True(barriers.Entered.Wait(Bound), "Init dispatched a real terrain reader before failing");
            Assert.True(system.RollingBack.Wait(Bound), "the failed constructor began system cleanup");
            _ = store.BeginRetirement();
            Assert.True(!done.Wait(TimeSpan.FromMilliseconds(100)), "constructor rollback waits for the reader");
            Assert.Equal(1, store.Diagnostics.ActiveLeases, "failed construction still pins its running reader");
            Assert.Equal(0, surface.Disposals, "failed construction does not dispose an in-use source");
            barriers.Release.Set();
            Assert.True(constructor.Join(Bound), "constructor rollback completes");
            Assert.True(store.Retirement.Wait(Bound), "the transferred lease is returned after rollback");
            Assert.True(ReferenceEquals(system.Failure, failure), "a clean rollback preserves the construction error");
            Assert.Equal(IntPtr.Zero, system.Physics!.Jolt.Handle, "failed construction releases its native world");
            Assert.Equal(1, system.Disposals, "the partially initialized system is cleaned once");
            Assert.Equal(1, surface.Disposals, "the store disposes the source after rollback");
            lease.Dispose();
        }
        finally
        {
            barriers.Release.Set();
            Assert.True(constructor.Join(Bound), "the constructor thread cannot escape the fixture");
            lease.Dispose();
            store.Dispose();
            Assert.True(store.Retirement.Wait(Bound), "fixture world retirement completes");
            system.RollingBack.Dispose();
        }
    }

    [Test]
    public static void InjectedSurfaceRemainsBorrowedAndMismatchedLeaseIsRolledBack()
    {
        ZoneSpec spec = Spec();
        using var barriers = new Barriers();
        var surface = new ControlledSurface(spec.Radius, barriers);
        var borrowed = new Zone(surface, spec, systems: Array.Empty<ZoneSystem>());
        borrowed.Dispose();
        borrowed.Dispose();
        Assert.Equal(0, surface.Disposals, "a fixture surface remains caller-owned");
        using var store = Store(surface);
        WorldLease lease = store.Acquire(spec.EnvironmentDescriptor());
        bool rejected = false;
        try { _ = new Zone(lease, spec with { SurfaceSeed = spec.SurfaceSeed + 1 }, systems: Array.Empty<ZoneSystem>()); }
        catch (ArgumentException) { rejected = true; }
        Assert.True(rejected, "lease/spec identity disagreement is refused before physics construction");
        Assert.Equal(0, store.Diagnostics.ActiveLeases, "a rejected transferred lease is returned");
        Assert.Equal(1, store.Trim(), "the caller can evict the unpinned source");
        Assert.Equal(1, surface.Disposals, "the actual store owner disposes it once");
    }

    [Test]
    public static void ZoneDescriptorIsMetadataOnlyAndRejectsUntrustedInputs()
    {
        ZoneSpec spec = Spec() with { Ground = GroundKind.Walking };
        PlanetEnvironmentDescriptor descriptor = spec.EnvironmentDescriptor();
        Assert.Equal(spec.SurfaceSeed, descriptor.SurfaceSeed, "legacy zone seed is preserved");
        Assert.Equal(WalkingWorld.SeedFor(spec.SurfaceSeed), descriptor.TerrainSeed, "legacy terrain seed is preserved");
        foreach (ZoneSpec invalid in new[]
        {
            spec with { Ground = (GroundKind)255 }, spec with { Radius = double.NaN },
            spec with { Radius = ZoneSpec.FirstWorldRadius }, spec with { LatitudeDegrees = double.PositiveInfinity },
            spec with { LatitudeDegrees = 91 }, spec with { LongitudeDegrees = 181 },
            spec with { SurfaceGravity = -1 }, spec with { BodyPath = uint.MaxValue },
        })
        {
            bool refused = false;
            try { _ = invalid.EnvironmentDescriptor(); } catch (ArgumentException) { refused = true; }
            Assert.True(refused, "invalid zone input is refused at descriptor resolution");
        }
        Assert.True((spec with { Radius = ZoneSpec.FirstWorldRadius }).EnvironmentDescriptor(diagnostic: true).IsDiagnostic,
            "a trusted historical fixture opts into diagnostic scale explicitly");
    }

    [Test]
    public static void ServerReleasesItsLeaseWithoutRetiringTheCallersStore()
    {
        ZoneSpec spec = Spec();
        using var barriers = new Barriers();
        barriers.Release.Set();
        var surface = new ControlledSurface(spec.Radius, barriers);
        using var store = Store(surface);
        var server = new GameServer(Options(store));
        PhysicsWorld physics = server.Zone.Physics;
        try
        {
            Assert.True(ReferenceEquals(surface, server.Zone.Surface), "server bootstrap transfers the acquired source to its zone");
            Assert.Equal(1, store.Diagnostics.ActiveLeases, "the server owns one zone pin after bootstrap");
            server.Dispose();
            server.Dispose();
            Assert.Equal(IntPtr.Zero, physics.Jolt.Handle, "server disposal retires zone physics");
            Assert.Equal(0, store.Diagnostics.ActiveLeases, "server disposal returns its pin");
            Assert.True(!store.Diagnostics.IsRetiring, "a provided store remains caller-owned");
            Assert.Equal(0, surface.Disposals, "idle source retention belongs to the caller's store policy");
            Assert.Equal(1, store.Trim(), "caller can trim after server retirement");
        }
        finally { server.Dispose(); }
    }

    [Test]
    public static void FailedServerBootstrapReturnsItsLeaseAndPreservesTheCallersStore()
    {
        ZoneSpec spec = Spec();
        using var barriers = new Barriers();
        var surface = new ControlledSurface(spec.Radius, barriers);
        using var store = Store(surface);
        var expected = new InvalidOperationException("Controlled bootstrap failure after zone and network creation.");
        Exception? failure = null;
        try { _ = new GameServer(Options(store) with { Log = _ => throw expected }); }
        catch (Exception error) { failure = error; }
        Assert.True(ReferenceEquals(expected, failure), $"bootstrap preserves its original error after clean rollback: {failure}");
        Assert.Equal(0, store.Diagnostics.ActiveLeases, "bootstrap failure returns the zone pin");
        Assert.True(!store.Diagnostics.IsRetiring, "bootstrap failure does not retire a caller-owned store");
        Assert.Equal(1, store.Trim(), "no failed-server lease prevents eviction");
        Assert.Equal(1, surface.Disposals, "failed-server source is owned and disposed by the store");
    }

    private static ZoneSpec Spec() => ZoneSpec.StartingWorld(StartingWorld.LandingLatitude, StartingWorld.LandingLongitude);
    private static ServerOptions Options(WorldStore store) => new()
    {
        WorldStore = store, Port = 0, DatabasePath = null, World = false, Animals = false,
        SpawnAtOrigin = true, Log = _ => { }, Latitude = StartingWorld.LandingLatitude, Longitude = StartingWorld.LandingLongitude,
    };
    private static WorldStore Store(ISurface surface) => new(new WorldStoreOptions(), (_, _) => surface, _ => new WorldMemoryEstimate(1, 1));

    private sealed class TrackingSystem : ZoneSystem
    {
        public int Disposals;
        public override void Dispose() => Disposals++;
    }
    private sealed class FailingSystem(bool fault) : ZoneSystem
    {
        public int Disposals;
        public InvalidOperationException Failure { get; } = new("Controlled system retirement failure.");
        public override void Dispose()
        {
            Disposals++;
            if (fault) throw Failure;
        }
    }
    private sealed class ConstructionFailureSystem(ControlledSurface surface) : ZoneSystem
    {
        public new PhysicsWorld? Physics { get; private set; }
        public int Disposals;
        public ManualResetEventSlim RollingBack { get; } = new();
        public InvalidOperationException Failure { get; } = new("Controlled zone initialization failure.");
        protected internal override void Init()
        {
            Physics = Zone.Physics;
            Physics.MakePropBodiesAhead = false;
            Physics.PrefetchWithin(surface.BlockedPoint, 0);
            Assert.True(surface.Barriers.Entered.Wait(Bound), "construction starts its reader before failing");
            throw Failure;
        }
        public override void Dispose()
        {
            Disposals++;
            RollingBack.Set();
        }
    }
    private sealed class ControlledSurface(double radius, Barriers barriers) : ISurface, IDisposable
    {
        private int _disposals;
        public double Radius => radius;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0;
        public Barriers Barriers => barriers;
        public bool FailBuild { get; init; }
        public int Disposals => Volatile.Read(ref _disposals);
        public Action? OnDispose { get; set; }
        public InvalidOperationException BuildFailure { get; } = new("Controlled zone terrain reader failure.");
        private TileKey BlockedTile
        {
            get
            {
                int level = PhysicsWorld.LevelOn(Radius), middle = (1 << level) / 2;
                return new TileKey(2, level, middle, middle);
            }
        }
        public Vector3 BlockedPoint => ZoneFrame.World.ToLocal(BlockedTile.Centre * Radius);
        public TriangleMesh GroundCollision(in ZoneFrame frame, TileKey tile, double margin)
        {
            if (tile == BlockedTile)
            {
                barriers.Entered.Set();
                if (!barriers.Release.Wait(Bound)) throw new TimeoutException("The zone fixture did not release its source reader.");
                if (FailBuild) throw BuildFailure;
            }
            Assert.Equal(0, Disposals, "no source read can outlive environment ownership");
            return SurfaceGrid.Collision(this, frame, tile, margin, spacing: Radius);
        }
        public void Dispose()
        {
            OnDispose?.Invoke();
            Interlocked.Increment(ref _disposals);
        }
    }
    private sealed class Barriers : IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
