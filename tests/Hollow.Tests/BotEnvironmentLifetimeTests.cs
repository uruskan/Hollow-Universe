using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hollow.Bots;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class BotEnvironmentLifetimeTests
{
    [Test]
    public static void SharedBotContextsRespectDescriptorAndSessionAndRetireNativeUsersBeforeTheWorld()
    {
        GameContent content = GameContent.LoadDefault();
        using var first = new BotWorlds(content, Store());
        using var other = new BotWorlds(content, Store());
        ZoneSpec aSpec = Spec(1200), bSpec = Spec(1100);
        PlanetEnvironmentDescriptor aDescriptor = aSpec.EnvironmentDescriptor(), bDescriptor = bSpec.EnvironmentDescriptor();
        using WorldLease a = first.Store.Acquire(aDescriptor);
        using WorldLease repeated = first.Store.Acquire(aSpec.EnvironmentDescriptor());
        using WorldLease b = first.Store.Acquire(bDescriptor);
        using WorldLease elsewhere = other.Store.Acquire(aDescriptor);
        BotWorldContext aContext = first.Get(aSpec, a);
        BotWorldContext same = first.Get(aSpec, repeated);
        BotWorldContext bContext = first.Get(bSpec, b);
        BotWorldContext otherContext = other.Get(aSpec, elsewhere);
        Assert.True(ReferenceEquals(aContext, same), "equivalent descriptors share one mutable context within a swarm");
        Assert.True(!ReferenceEquals(aContext.Physics, bContext.Physics), "another radius has independent collision");
        Assert.True(!ReferenceEquals(aContext.Nature, bContext.Nature), "another radius has independent natural changes");
        Assert.True(!ReferenceEquals(aContext.Building, bContext.Building), "another radius has independent building state");
        Assert.True(!ReferenceEquals(aContext.Physics, otherContext.Physics), "separate sessions do not share mutable physics");
        Assert.True(!ReferenceEquals(aContext.Surface, otherContext.Surface), "separate stores do not borrow each other's terrain");
        Assert.True(aContext.Physics.Listener is NaturalBodies, "the shared natural collision listener stays owned");
        aContext.Nature.Apply(123, new NaturalOverride { Depleted = true });
        Assert.True(same.Nature.IsDepleted(123), "one client's depletion reaches the shared replica");
        Assert.True(!bContext.Nature.IsDepleted(123) && !otherContext.Nature.IsDepleted(123), "depletion does not cross world or session identity");

        PhysicsWorld aPhysics = aContext.Physics, bPhysics = bContext.Physics, otherPhysics = otherContext.Physics;
        var aSurface = (DisposalProbe)a.Surface;
        var bSurface = (DisposalProbe)b.Surface;
        var otherSurface = (DisposalProbe)elsewhere.Surface;
        aSurface.BeforeDispose = () => Assert.Equal(IntPtr.Zero, aPhysics.Jolt.Handle, "A native users are gone before surface disposal");
        bSurface.BeforeDispose = () => Assert.Equal(IntPtr.Zero, bPhysics.Jolt.Handle, "B native users are gone before surface disposal");
        otherSurface.BeforeDispose = () => Assert.Equal(IntPtr.Zero, otherPhysics.Jolt.Handle, "other session native users are gone before surface disposal");
        a.Dispose(); repeated.Dispose(); b.Dispose(); elsewhere.Dispose();
        Assert.Equal(2, first.Store.Diagnostics.ActiveLeases, "shared contexts retain one lease each after clients leave");
        first.RetireExcept(new HashSet<PlanetEnvironmentDescriptor> { aDescriptor });
        Assert.Equal(IntPtr.Zero, bPhysics.Jolt.Handle, "unused context releases native collision");
        Assert.True(bPhysics.Listener == null, "unused context detaches the natural listener");
        Assert.True(aPhysics.Jolt.Handle != IntPtr.Zero && otherPhysics.Jolt.Handle != IntPtr.Zero, "retirement preserves the other active contexts");
        Assert.Equal(1, first.Store.Diagnostics.ActiveLeases, "unused context releases its terrain pin after native cleanup");
        first.Dispose();
        Assert.True(first.Store.Retirement.Wait(TimeSpan.FromSeconds(5)), "first session retires completely");
        first.Store.Retirement.GetAwaiter().GetResult();
        Assert.True(aSurface.Disposed && bSurface.Disposed, "session store releases both generated worlds");
        Assert.True(!otherSurface.Disposed, "retiring one swarm does not retire another");
        other.Dispose();
        Assert.True(other.Store.Retirement.Wait(TimeSpan.FromSeconds(5)), "second session retires completely");
        other.Store.Retirement.GetAwaiter().GetResult();
        Assert.True(otherSurface.Disposed, "second store releases its own world");
    }

    [Test]
    public static void HomesAndLandingSitesArePerClientAndResetWhenItsWorldChanges()
    {
        GameContent content = GameContent.LoadDefault();
        using var store = Store();
        using var a = new GameClient(new ClientOptions { Content = content, WorldStore = store });
        using var b = new GameClient(new ClientOptions { Content = content, WorldStore = store });
        using var homes = new BotGround();
        using var otherHomes = new BotGround();
        ZoneSpec aSpec = Spec(1200), bSpec = Spec(1100);
        PlanetEnvironmentDescriptor aDescriptor = aSpec.EnvironmentDescriptor(), bDescriptor = bSpec.EnvironmentDescriptor();
        Vector3 aLanding = new(1200, 0, 0), bLanding = new(0, 1100, 0), aMoved = aLanding + new Vector3(0, 0, 150);
        homes.Bind(a, aDescriptor, aSpec);
        homes.Bind(b, bDescriptor, bSpec);
        homes.Observe(a, aLanding, aLanding);
        homes.Observe(b, bLanding, bLanding);
        otherHomes.Bind(b, bDescriptor, bSpec);
        otherHomes.Observe(b, bLanding + new Vector3(0, 0, 50), bLanding);
        Assert.Equal(aLanding, homes.Landing(a), "first client's own landing");
        Assert.Equal(bLanding, homes.Landing(b), "second client does not inherit the first landing");
        Assert.True(homes.ArrivedAtLanding(b) && !otherHomes.ArrivedAtLanding(b), "arrival state belongs to its session");
        homes.Observe(a, aMoved, aLanding);
        Assert.Equal(aMoved, homes.Home(a), "teleport moves only this bot's home");
        Assert.Equal(aLanding, homes.FirstArrival(a), "teleport preserves its initial arrival for the raid behavior");
        Assert.Equal(bLanding, homes.Home(b), "another bot's home stays unchanged");
        homes.Bind(a, bDescriptor, bSpec);
        Assert.True(!homes.TryLanding(a, out _), "world replacement invalidates the old landing");
        homes.Observe(a, bLanding, bLanding);
        Assert.Equal(bLanding, homes.FirstArrival(a), "new world starts a fresh arrival history");
        homes.Forget(a);
        Assert.True(!homes.TryLanding(a, out _), "retirement removes that client from home tracking");
    }

    [Test]
    public static void RetainingADisposedHomeTrackerDoesNotRootItsClients()
    {
        (BotGround tracker, WeakReference<GameClient> weak) = ReleasedClient();
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.True(!weak.TryGetTarget(out _), "disposed session tracking must not retain the client or its callbacks");
        GC.KeepAlive(tracker);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (BotGround, WeakReference<GameClient>) ReleasedClient()
    {
        using var store = Store();
        var client = new GameClient(new ClientOptions { Content = GameContent.LoadDefault(), WorldStore = store });
        var homes = new BotGround();
        ZoneSpec spec = Spec(1200);
        homes.Bind(client, spec.EnvironmentDescriptor(), spec);
        homes.Observe(client, new Vector3(1200, 0, 0), new Vector3(1200, 0, 0));
        var weak = new WeakReference<GameClient>(client);
        client.Dispose();
        homes.Dispose();
        return (homes, weak);
    }

    private static ZoneSpec Spec(double radius) => new()
    {
        SystemAddress = StartingWorld.System.Value, BodyPath = StartingWorld.BodyPath, Radius = radius,
        SurfaceSeed = Hash64.Of(StartingWorld.System.Value, StartingWorld.BodyPath), Ground = GroundKind.Walking,
        LatitudeDegrees = 0, LongitudeDegrees = 0,
    };

    private static WorldStore Store() => new(new WorldStoreOptions(), static (descriptor, _) => new DisposalProbe(descriptor.Radius));

    private sealed class DisposalProbe(double radius) : ISurface, IDisposable
    {
        public double Radius => radius;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0;
        public Action? BeforeDispose;
        public bool Disposed;
        public void Dispose() { BeforeDispose?.Invoke(); Disposed = true; }
    }
}
