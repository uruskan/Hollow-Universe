using System;
using System.Numerics;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class PhysicsLifetimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// T0.3: disposing a world or a ship space frees its native physics system, bodies and shapes. JoltPhysicsSharp
    /// 2.23.0 left them all behind (about 16 MiB a world with resident ground), which grew the test process to many GiB
    /// until Jolt crashed on a failed native allocation.
    /// </summary>
    [Test]
    public static void DisposedWorldsAndShipSpacesReturnTheirNativeMemory()
    {
        var surface = new LevelSurface(1200);
        var point = new Vector3(0, 1200, 0);
        Hollow.Simulation.Ships.ShipInterior interior = ContentFixture.Content.Ship("iron-wake")!.Interior!;
        var states = new float[interior.Parts.Count];
        void Cycle(int count)
        {
            for (int i = 0; i < count; i++)
            {
                using (var world = new PhysicsWorld(surface, ZoneFrame.World) { MakePropBodiesAhead = false })
                {
                    world.EnsureGroundWithin(point, 40);
                    world.AddProp(PropLayer.Nature, (ulong)i + 1, PropShape.Sphere(point, 0.5f));
                }
                using (new ShipSpace(interior, states, Hollow.Simulation.Movement.MovementTuning.Default)) { }
            }
        }
        Cycle(4); // the runtime, pools and shared shape caches come first
        long before = PrivateBytes();
        Cycle(24);
        double grown = (PrivateBytes() - before) / 1048576.0;
        Fixtures.Report($"24 worlds with ground and 24 ship spaces created and disposed: private memory grew {grown:0.0} MiB");
        // Before the fix, 24 cycles grew about 400 MiB; even leaking only the shape settings grew about 40 MiB.
        Assert.True(grown < 16, "disposed worlds and ship spaces return their native memory (it was about 16 MiB a world)");
    }

    private static long PrivateBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    [Test]
    public static void DisposalDrainsAnAlreadyStartedTileWithoutHoldingTheJoltGate() => CheckDisposal(failBuild: false);

    [Test]
    public static void AFailedTileDoesNotAbandonOtherWorkersOrNativeResources() => CheckDisposal(failBuild: true);

    private static void CheckDisposal(bool failBuild)
    {
        using var surface = new ControlledSurface(failBuild);
        var world = new PhysicsWorld(surface, ZoneFrame.World) { MakePropBodiesAhead = false };
        using var disposing = new ManualResetEventSlim();
        using var disposed = new ManualResetEventSlim();
        Thread? disposer = null;
        Exception? failure = null;
        try
        {
            // Resident bodies and their shapes must be freed too, even when a later prefetch fails.
            Vector3 resident = surface.Point(surface.ResidentTile);
            world.EnsureGroundWithin(resident, 0);
            world.AddProp(PropLayer.Nature, 1, PropShape.Sphere(resident, 0.5f));
            int residentBuilds = world.TilesBuilt;
            Assert.True(world.TileCount > 0 && world.PropCount == 1, "the fixture owns resident ground and a prop");
            Assert.True(world.Jolt.Handle != IntPtr.Zero, "the fixture owns a native physics world");

            if (failBuild)
            {
                world.PrefetchWithin(surface.Point(surface.FailedTile), 0);
                Assert.True(surface.Failed.Wait(Timeout), "the first prefetch reaches its controlled failure");
            }
            world.PrefetchWithin(surface.Point(surface.BlockedTile), 0);
            Assert.True(surface.Entered.Wait(Timeout), "another prefetch starts before disposal");

            // Transfer ownership to this thread after all setup mutations. The test thread only controls the
            // surface's barriers until disposal has joined; it never accesses the world's dictionaries meanwhile.
            disposer = new Thread(() =>
            {
                disposing.Set();
                try
                {
                    world.Dispose();
                }
                catch (Exception error)
                {
                    failure = error;
                }
                finally
                {
                    disposed.Set();
                }
            }) { IsBackground = true, Name = "Physics lifetime test disposal" };
            disposer.Start();
            Assert.True(disposing.Wait(Timeout), "disposal starts");
            bool waitedForBuild = !disposed.Wait(TimeSpan.FromMilliseconds(100));

            bool gateAvailable = JoltRuntime.Gate.TryEnter(Timeout);
            if (gateAvailable)
            {
                JoltRuntime.Gate.Exit();
            }
            surface.Release.Set();
            Assert.True(disposer.Join(Timeout), "disposal completes after the build is released");
            Assert.True(surface.Returned.Wait(Timeout), "the controlled build completed its collision mesh");
            Assert.True(waitedForBuild, "disposal waits for the outstanding build");
            Assert.True(gateAvailable, "waiting for a tile does not hold the process-wide Jolt gate");
            Assert.True(world.TilesBuilt > residentBuilds, "a pending mesh reached native shape creation before cleanup");
            Assert.Equal(0, world.TileCount, "all resident tiles released");
            Assert.Equal(0, world.PropCount, "all resident props released");
            Assert.Equal(IntPtr.Zero, world.Jolt.Handle, "the native physics world was disposed");
            if (failBuild)
            {
                Assert.True(failure is AggregateException, "the build failure is reported after cleanup");
                AggregateException aggregate = ((AggregateException)failure!).Flatten();
                Assert.Equal(1, aggregate.InnerExceptions.Count, "one failed tile is reported once");
                Assert.True(ReferenceEquals(surface.BuildFailure, aggregate.InnerExceptions[0]), "the original build failure is preserved");
            }
            else
            {
                Assert.True(failure == null, $"normal disposal succeeds: {failure}");
            }
            world.Dispose(); // Idempotent after both successful disposal and a reported build failure.
        }
        finally
        {
            surface.Release.Set();
            bool joined = disposer == null || disposer.Join(Timeout);
            if (joined)
            {
                try
                {
                    world.Dispose();
                }
                catch (Exception)
                {
                    // Preserve the assertion that exposed a regression; the explicit second Dispose above is
                    // what verifies idempotence. No worker is left deliberately blocked after a failed assertion.
                }
            }
            Assert.True(joined, "the disposal thread exits within the test's bound");
        }
    }

    private sealed class ControlledSurface : ISurface, IDisposable
    {
        private readonly bool _failBuild;

        public ControlledSurface(bool failBuild)
        {
            _failBuild = failBuild;
            int level = PhysicsWorld.LevelOn(Radius), middle = (1 << level) / 2;
            ResidentTile = new TileKey(0, level, middle, middle);
            FailedTile = new TileKey(1, level, middle, middle);
            BlockedTile = new TileKey(2, level, middle, middle);
        }

        public double Radius => 256;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0;
        public TileKey ResidentTile { get; }
        public TileKey FailedTile { get; }
        public TileKey BlockedTile { get; }
        public InvalidOperationException BuildFailure { get; } = new("Controlled tile build failure.");
        public ManualResetEventSlim Failed { get; } = new();
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public ManualResetEventSlim Returned { get; } = new();

        public Vector3 Point(TileKey tile) => ZoneFrame.World.ToLocal(tile.Centre * Radius);

        public TriangleMesh GroundCollision(in ZoneFrame frame, TileKey tile, double margin)
        {
            if (_failBuild && tile == FailedTile)
            {
                Failed.Set();
                throw BuildFailure;
            }
            if (tile == BlockedTile)
            {
                Entered.Set();
                if (!Release.Wait(Timeout))
                {
                    throw new TimeoutException("The lifetime test did not release its tile build.");
                }
            }
            // Real valid sphere triangles, deliberately coarse: the production path still creates a native
            // mesh shape, and the test spends its time at the controlled barrier rather than generating terrain.
            TriangleMesh mesh = SurfaceGrid.Collision(this, frame, tile, margin, spacing: Radius);
            if (tile == BlockedTile)
            {
                Returned.Set();
            }
            return mesh;
        }

        public void Dispose()
        {
            Failed.Dispose();
            Entered.Dispose();
            Release.Dispose();
            Returned.Dispose();
        }
    }
}
