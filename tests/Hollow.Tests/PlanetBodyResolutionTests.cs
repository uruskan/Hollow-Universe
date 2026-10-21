using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.Tests;

public static class PlanetBodyResolutionTests
{
    [Test]
    public static void MetadataMatchesKnownBodiesAndHasNoTerrainInItsTypedStorage()
    {
        var (solar, earth) = StartingWorld.Find();
        Check(solar, earth, PlanetBodies.Find(solar.Address.Value, earth.Path));

        var universe = new Galaxy.Galaxy(null);
        var (system, planet) = FirstWorld.Find(universe)
            ?? throw new TestFailure("the inherited procedural fixture still resolves without a catalogue");
        Check(system, planet, PlanetBodies.Find(system.Address.Value, planet.Path));
        Check(system, planet, PlanetBodies.Find(universe, system.Address.Value, planet.Path));

        var visited = new HashSet<Type>();
        AssertTerrainFreeShape(typeof(PlanetBodies.Entry), visited);
        foreach (FieldInfo field in typeof(PlanetBodies).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            AssertTerrainFreeShape(field.FieldType, visited);
    }

    [Test]
    public static void UnknownAddressesAndInvalidRadiiAreRejectedBeforeConstruction()
    {
        var (system, _) = StartingWorld.Find();
        uint starPath = system.Bodies.First(body => body.State == null).Path;
        Assert.True(PlanetBodies.Find(system.Address.Value, starPath) == null, "a star has no planet state");
        Assert.True(EnvironmentDescriptors.Resolve(system.Address.Value, starPath, 1200) == null,
            "a star cannot supply a walking descriptor");
        for (uint i = 0; i < 32; i++)
        {
            ulong address = ulong.MaxValue - i;
            uint path = uint.MaxValue - i;
            Assert.True(PlanetBodies.Find(address, path) == null, "unknown systems return no metadata");
            Assert.True(PlanetBodies.Find(system.Address.Value, path) == null, "unknown body paths return no metadata");
            Assert.True(EnvironmentDescriptors.Resolve(address, path, Units.EarthRadius,
                EnvironmentSurfaceKind.RealRadius, diagnostic: true) == null, "unknown systems produce no real-surface descriptor");
            Assert.True(EnvironmentDescriptors.Resolve(address, path, 1200) == null, "unknown systems produce no walking descriptor");
            Assert.True(EnvironmentDescriptors.Resolve(system.Address.Value, path, 1200) == null,
                "unknown body paths produce no walking descriptor");
        }
        foreach (double radius in new[] { 0, -1, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            bool rejected = false;
            try { _ = EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath, radius); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert.True(rejected, "an invalid descriptor radius throws before world construction");
        }
    }

    [Test]
    public static void ExplicitUniverseSeedsResolveIndependently()
    {
        PlanetBodies.Entry baseline = PlanetBodies.Find(FirstWorld.System.Value, FirstWorld.BodyPath)
            ?? throw new TestFailure("default procedural body is available");
        bool different = false;
        foreach (ulong seed in new[] { 11UL, 29UL, 101UL })
        {
            var universe = new Galaxy.Galaxy(null, seed);
            StarSystem? expectedSystem = universe.GetSystem(FirstWorld.System);
            Body? expectedBody = expectedSystem?.Bodies.FirstOrDefault(body => body.Path == FirstWorld.BodyPath && body.State != null);
            PlanetBodies.Entry? actual = PlanetBodies.Find(FirstWorld.System.Value, FirstWorld.BodyPath, seed);
            PlanetBodies.Entry? owned = PlanetBodies.Find(universe, FirstWorld.System.Value, FirstWorld.BodyPath);
            if (expectedBody == null)
            {
                Assert.True(actual == null && owned == null, "a body missing from this seed is not borrowed from another universe");
                different = true;
            }
            else
            {
                Check(expectedSystem!, expectedBody, actual);
                Check(expectedSystem!, expectedBody, owned);
                different |= expectedBody.Radius != baseline.Body.Radius || expectedBody.State != baseline.Body.State;
            }
            Check(baseline.System, baseline.Body, PlanetBodies.Find(FirstWorld.System.Value, FirstWorld.BodyPath));
        }
        Assert.True(different, "the test exercises universe seeds with different physical output or body availability");
        Check(baseline.System, baseline.Body,
            PlanetBodies.Find(FirstWorld.System.Value, FirstWorld.BodyPath, Galaxy.Galaxy.DefaultSeed));
    }

    [Test]
    public static void WalkingLeaseBuildsOnlyPlayableTerrainAndPreservesSamples()
    {
        var (system, earth) = StartingWorld.Find();
        double radius = GameScale.PlanetRadius(earth);
        int builds = 0;
        var store = new WorldStore(new WorldStoreOptions(), (descriptor, token) =>
        {
            Assert.Equal(EnvironmentSurfaceKind.Walking, descriptor.Kind, "only the requested playable generator is dispatched");
            System.Threading.Interlocked.Increment(ref builds);
            return EnvironmentWorldFactory.Create(descriptor, token);
        });
        try
        {
            using WorldLease first = store.Acquire(EnvironmentDescriptors.Resolve(system.Address.Value, earth.Path, radius)!);
            using WorldLease second = store.Acquire(EnvironmentDescriptors.Resolve(system.Address.Value, earth.Path, radius)!);
            var shared = (WalkingWorld)first.Surface;
            WalkingWorld independent = WalkingWorld.For(system, earth, radius);
            Assert.True(ReferenceEquals(shared, second.Surface), "equivalent descriptors share a world within their owning store");
            Assert.Equal(1, builds, "two leases build exactly one playable world and no real-radius terrain");
            Assert.Equal(radius, shared.Radius, "the leased world uses the playable radius");
            Assert.Equal(independent.Parameters, shared.Parameters, "production profile and layout selection are unchanged");
            foreach (Vector3D direction in PlanetSurface.FibonacciSphere(64))
            {
                Assert.Equal(independent.Fields(direction), shared.Fields(direction), "descriptor construction preserves complete samples");
                Assert.Equal(independent.HeightAt(direction), shared.HeightAt(direction), "monument-modified heights still agree");
            }
        }
        finally { store.DisposeAsync().GetAwaiter().GetResult(); }
    }

    [Test]
    public static void AThreadsLastSampleDoesNotKeepItsWorldAlive()
    {
        WeakReference<WalkingWorld> sampled = SampleTransientWorld();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.True(!sampled.TryGetTarget(out _), "a thread's cached sample retains only an identity token, not terrain and drainage arrays");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<WalkingWorld> SampleTransientWorld()
    {
        var world = new WalkingWorld(0x504C414E4554, 250, 0, 0, 285);
        _ = world.Fields(Vector3D.UnitX);
        return new WeakReference<WalkingWorld>(world);
    }

    // Inspect storage types without invoking getters or creating terrain. Framework containers contribute their
    // generic/element types; application metadata contributes all instance fields, including private backing fields.
    private static void AssertTerrainFreeShape(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type)) return;
        Assert.True(!typeof(ISurface).IsAssignableFrom(type) && type != typeof(WorldLease) && type != typeof(WorldStore),
            "metadata result and resolver storage must not expose or own terrain: " + type.FullName);
        if (type.HasElementType) AssertTerrainFreeShape(type.GetElementType()!, visited);
        foreach (Type argument in type.GetGenericArguments()) AssertTerrainFreeShape(argument, visited);
        if (type.Namespace?.StartsWith("Hollow.", StringComparison.Ordinal) != true) return;
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            AssertTerrainFreeShape(field.FieldType, visited);
    }

    private static void Check(StarSystem expectedSystem, Body expectedBody, PlanetBodies.Entry? actual)
    {
        PlanetBodies.Entry resolved = actual ?? throw new TestFailure("the requested body resolves");
        Assert.Equal(expectedSystem.Address, resolved.System.Address, "same system identity");
        Assert.Equal(expectedSystem.Name, resolved.System.Name, "same system name");
        Assert.Equal(expectedBody.Path, resolved.Body.Path, "same body path");
        Assert.Equal(expectedBody.Name, resolved.Body.Name, "same body name");
        Assert.Equal(expectedBody.Radius, resolved.Body.Radius, "same physical radius");
        Assert.Equal(expectedBody.Mass, resolved.Body.Mass, "same physical mass");
        Assert.Equal(expectedBody.Temperature, resolved.Body.Temperature, "same temperature");
        Assert.Equal(expectedBody.SurfacePressure, resolved.Body.SurfacePressure, "same pressure");
        Assert.Equal(expectedBody.State, resolved.Body.State, "same composition, surface, liquid and biosphere");
    }
}
