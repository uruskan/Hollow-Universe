using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Client;
using Hollow.Client.Map;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Net.Bits;
using Hollow.Net.Replication;
using Hollow.Simulation;
using Hollow.Surface;

namespace Hollow.Tests;

public static class ClientEnvironmentLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    [Test]
    public static void MapRetirementPinsAnAdmittedPixelReaderUntilCancellationDrainsIt()
    {
        ZoneSpec spec = Spec(1000);
        var surface = new ReaderSurface(spec.Radius) { Block = true };
        using var store = Store((_, _) => surface);
        var map = new ZoneMap(spec, store.Acquire(spec.EnvironmentDescriptor()), ZoneFrame.World, ContentFixture.Content.World);
        using var stop = new CancellationTokenSource();
        Task<MapPicture> render = Task.Run(() => map.Render(16, stop.Token));
        try
        {
            Assert.True(surface.Entered.Wait(Bound), "the map is inside a real pixel source query");
            map.Dispose();
            Assert.Equal(1, store.Diagnostics.ActiveLeases, "only the admitted pixel reader retains the world");
            Assert.Equal(0, store.Trim(), "trim cannot evict a disposed map's active worker");
            stop.Cancel();
            Assert.True(!render.IsCompleted, "cancellation cannot free a world while its query is still executing");
        }
        finally
        {
            surface.Release.Set();
            try { render.WaitAsync(Bound).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            map.Dispose();
        }
        Assert.Equal(0, store.Diagnostics.ActiveLeases, "all pixel workers returned before their pin was released");
        Assert.Equal(1, store.Trim(), "the retired map is now evictable");
        Assert.Equal(1, surface.Disposals, "factory-owned output is disposed exactly once");
        Refuses<ObjectDisposedException>(() => map.Render(16));
        Refuses<ObjectDisposedException>(() => map.Ground(Vector3D.UnitX));
    }

    [Test]
    public static void RepeatedWelcomeRetiresMapsAndPhysicsBeforeReplacingTheWorld()
    {
        var created = new List<ReaderSurface>();
        using var store = Store((descriptor, _) =>
        {
            var surface = new ReaderSurface(descriptor.Radius);
            created.Add(surface);
            return surface;
        });
        using var client = new GameClient(new ClientOptions { WorldStore = store, Content = ContentFixture.Content });
        var viewPins = new List<WorldLease>();
        client.WorldRetiring += () =>
        {
            Assert.True(client.Surface != null && client.Prediction != null, "presentation retirement sees the old live physics/world");
            viewPins.Add(client.ForkWorldLease());
        };
        try
        {
            Welcome(client, Spec(1000));
            ZoneMap oldMap = client.World().Map!;
            var oldPhysics = client.Physics;
            Welcome(client, Spec(1100));
            Assert.Equal(1, viewPins.Count, "re-welcome retires the previous presentation once");
            Assert.True(!ReferenceEquals(oldPhysics, client.Physics), "the replacement uses new physics");
            Assert.Equal(1100.0, client.Surface!.Radius, "new surface is published after retirement");
            Assert.True(!ReferenceEquals(oldMap, client.World().Map), "the module no longer retains the prior map");
            Refuses<ObjectDisposedException>(() => oldMap.Render(8));
            client.Dispose();
            client.Dispose();
            Assert.Equal(2, viewPins.Count, "final disposal retires the second presentation once");
            Assert.True(client.Surface == null && client.Zone == null && client.Physics == null, "disposed client clears its world references");
            Assert.Equal(2, store.Diagnostics.ActiveLeases, "only asynchronous presentation readers remain");
            Assert.Equal(0, store.Trim(), "both old views still protect their own world");
        }
        finally { foreach (WorldLease lease in viewPins) lease.Dispose(); }
        Assert.Equal(2, store.Trim(), "both worlds retire when their final views finish");
        Assert.True(created.TrueForAll(surface => surface.Disposals == 1), "each world is released exactly once");
    }

    [Test]
    public static void UnknownWelcomeKindIsRejectedWithoutAllocatingAWorld()
    {
        int builds = 0;
        using var store = Store((descriptor, _) => { builds++; return new ReaderSurface(descriptor.Radius); });
        using var client = new GameClient(new ClientOptions { WorldStore = store, Content = ContentFixture.Content });
        ZoneSpec spec = Spec(1000);
        ZoneInfo zone = NetConvert.ToInfo(spec);
        zone.Ground = byte.MaxValue;
        var offer = new WorldOffer { OfferId = 1, Zone = zone, Environment = NetConvert.ToEnvironment(spec.EnvironmentDescriptor()) };
        InvokeOffer(client, offer);
        Assert.Equal(ClientState.Disconnected, client.State, "unknown generation kinds are refused, not mapped to stand-in terrain");
        Assert.Equal(0, builds, "untrusted descriptors never enter construction");
    }

    [Test]
    public static void DisposalFromARetirementSubscriberCannotPublishTheNextWelcome()
    {
        using var store = Store((descriptor, _) => new ReaderSurface(descriptor.Radius));
        using var client = new GameClient(new ClientOptions { WorldStore = store, Content = ContentFixture.Content });
        Welcome(client, Spec(1000));
        ZoneMap oldMap = client.World().Map!;
        var oldPhysics = client.Physics!;
        client.WorldRetiring += client.Dispose;
        Welcome(client, Spec(1100));
        Assert.Equal(ClientState.Disconnected, client.State, "reentrant disposal closes replacement admission");
        Assert.True(client.Surface == null && client.Physics == null, "a disposed client never publishes the staged replacement");
        Refuses<ObjectDisposedException>(() => oldMap.Render(8));
        Assert.Equal(IntPtr.Zero, oldPhysics.Jolt.Handle, "reentrant disposal still retires the old module and physics readers");
        Assert.Equal(0, store.Diagnostics.ActiveLeases, "both old and staged replacement pins are released");
        Assert.Equal(2, store.Trim(), "neither world is stranded by reentrant disposal");
    }

    [Test]
    public static void FailingRetirementSubscriberDoesNotSkipOtherReadersOrTheOwnedWorld()
    {
        using var store = Store((descriptor, _) => new ReaderSurface(descriptor.Radius));
        using var client = new GameClient(new ClientOptions { WorldStore = store, Content = ContentFixture.Content });
        Welcome(client, Spec(1000));
        var physics = client.Physics!;
        ZoneMap map = client.World().Map!;
        var expected = new InvalidOperationException("Controlled presentation retirement failure.");
        int lastReader = 0;
        client.WorldRetiring += () => throw expected;
        client.WorldRetiring += () => lastReader++;
        AggregateException? failure = null;
        try { client.Dispose(); } catch (AggregateException error) { failure = error; }
        Assert.True(failure != null && failure.Flatten().InnerExceptions.Contains(expected), "the original callback failure reaches the owner");
        Assert.Equal(1, lastReader, "a failing reader cannot skip the remaining readers");
        Assert.Equal(IntPtr.Zero, physics.Jolt.Handle, "native physics is retired despite callback failure");
        Refuses<ObjectDisposedException>(() => map.Render(8));
        Assert.Equal(0, store.Diagnostics.ActiveLeases, "all world pins are returned before disposal reports failure");
        Assert.Equal(1, store.Trim(), "a failed disposal does not strand its generated world");
    }

    [Test]
    public static void ReplacementWelcomeAcceptsTheNewServersInitialSnapshotSequence()
    {
        using var store = Store((descriptor, _) => new ReaderSurface(descriptor.Radius));
        using var client = new GameClient(new ClientOptions { WorldStore = store, Content = ContentFixture.Content });
        Welcome(client, Spec(1000));
        var oldServer = new SnapshotSender();
        ReceiveSnapshot(client, oldServer);
        ReceiveSnapshot(client, oldServer);
        Assert.Equal(2L, client.Stats.Snapshots, "both original snapshots were decoded");
        Welcome(client, Spec(1100));
        ReceiveSnapshot(client, new SnapshotSender());
        Assert.Equal(3L, client.Stats.Snapshots, "sequence one belongs to the replacement session and is not stale");
    }

    private static void ReceiveSnapshot(GameClient client, SnapshotSender sender)
    {
        var writer = new BitWriter(512);
        sender.Write(writer, new SnapshotHeader { ServerTick = 30, InputMargin = sbyte.MinValue }, ReadOnlySpan<SnapshotCandidate>.Empty, 4096);
        byte[] packet = writer.ToArray();
        var reader = (BitReader)typeof(GameClient).GetField("_reader", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        reader.Reset(packet, 0, packet.Length);
        _ = reader.ReadBits(8);
        typeof(GameClient).GetMethod("OnSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(client, null);
    }

    private static ZoneSpec Spec(double radius) => ZoneSpec.StartingWorld(15, 30) with { Radius = radius, Ground = GroundKind.StandIn };
    private static WorldStore Store(Func<PlanetEnvironmentDescriptor, CancellationToken, ISurface> factory) =>
        new(new WorldStoreOptions(), factory);
    private static void Welcome(GameClient client, ZoneSpec spec)
    {
        uint offerId = (uint)spec.Radius;
        EnvironmentIdentity identity = NetConvert.ToEnvironment(spec.EnvironmentDescriptor());
        ZoneInfo info = NetConvert.ToInfo(spec);
        InvokeOffer(client, new WorldOffer { OfferId = offerId, Zone = info, Environment = identity });
        InvokeWelcome(client, new Welcome { OfferId = offerId, Environment = identity, Zone = info,
            AttackSession = 1, NetId = 1, EntityId = 1, ServerName = "lifetime fixture" });
    }
    private static void InvokeOffer(GameClient client, WorldOffer offer) =>
        typeof(GameClient).GetMethod("OnWorldOffer", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(client, new object[] { offer });
    private static void InvokeWelcome(GameClient client, Welcome welcome) =>
        typeof(GameClient).GetMethod("OnWelcome", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(client, new object[] { welcome });
    private static void Refuses<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }

    private sealed class ReaderSurface(double radius) : ISurface, IDisposable
    {
        public double Radius => radius;
        public double SeaRadius => 0;
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public bool Block;
        public int Disposals;
        public double HeightAt(Vector3D direction)
        {
            if (Disposals != 0) throw new ObjectDisposedException(nameof(ReaderSurface));
            Entered.Set();
            if (Block && !Release.Wait(Bound)) throw new TimeoutException("Map fixture reader was never released.");
            return 1;
        }
        public void Dispose() { Interlocked.Increment(ref Disposals); Entered.Dispose(); Release.Dispose(); }
    }
}
