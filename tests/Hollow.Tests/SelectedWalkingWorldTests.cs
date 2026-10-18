using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Inactive selected-construction ownership tests; the synthetic catalog is not playable asset admission.</summary>
public static class SelectedWalkingWorldTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);
    private const string RawProfile = "selected-fixture-raw";

    [Test]
    public static void SelectedWalkingInputIdentityBindsPhysicalGeneratorInputsWithoutSelectionCircularity()
    {
        PlanetEnvironmentDescriptor plain = Earth();
        string identity = WalkingGradedTerrain.InputIdentity(plain, RawProfile);
        Assert.Equal(identity, WalkingGradedTerrain.InputIdentity(Earth(), RawProfile), "independent metadata resolution has one input identity");
        foreach (PlanetEnvironmentDescriptor changed in new[]
        {
            Earth(radius: 260), Earth(parameters: Parameters() with { LayoutVariant = 3 }),
            Earth(parameters: Parameters() with { MonumentChance = 0.1 }),
            Earth(parameters: Parameters() with { MonumentSpacing = 140 }),
            Earth(parameters: Parameters() with { TerrainRevision = 99 }),
        })
            Assert.True(identity != WalkingGradedTerrain.InputIdentity(changed, RawProfile), "physical terrain/site inputs change finalized input identity");
        Assert.True(identity != WalkingGradedTerrain.InputIdentity(plain, "other-raw-profile"), "raw substrate profile is part of the field input contract");
        PlanetEnvironmentDescriptor selected = Selected();
        PlanetEnvironmentDescriptor appearance = Selected(catalog: Catalog(alternateLodKeys: true));
        Assert.Equal(identity, WalkingGradedTerrain.InputIdentity(selected, RawProfile), "selected descriptor computes the same noncircular terrain identity");
        Assert.Equal(identity, WalkingGradedTerrain.InputIdentity(appearance, RawProfile), "LOD qualification does not grade the terrain");
        Assert.Equal(selected.CanonicalIdentity, appearance.CanonicalIdentity, "appearance-only evidence preserves physical world identity");
        Assert.True(selected.CacheIdentity != appearance.CacheIdentity, "resident qualification variants remain distinct");
        Assert.Equal(plain.SurfaceSeed, selected.SurfaceSeed, "selected identity does not perturb the address-derived surface seed");
        Assert.Equal(plain.TerrainSeed, selected.TerrainSeed, "selected identity does not perturb the walking terrain seed");
        string generation = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(selected.Formations!.Identity.GenerationCanonical));
        Assert.True(generation.Contains(TerrainPinSet.Contract, StringComparison.Ordinal) &&
            generation.Contains(SurfaceGrid.CanonicalCollisionContract, StringComparison.Ordinal), "physical generation binds fixed pins and canonical collision topology");
    }

    [Test]
    public static void SelectedWalkingRejectsWrongTerrainIdentityBeforeInjectedEstimatorOrFactory()
    {
        int estimates = 0, builds = 0;
        var wrong = Selected(terrainIdentity: new string('A', 64));
        using var store = new WorldStore(Options(), (descriptor, _) => { builds++; return new SphereSurface(descriptor.Radius, 1); },
            _ => { estimates++; return new WorldMemoryEstimate(0, 0); });
        Reject<FormationValidationException>(() => { using WorldLease lease = store.Acquire(wrong); });
        Reject<FormationValidationException>(() => EnvironmentWorldFactory.CreateSelected(wrong, Limits()));
        Assert.Equal(0, estimates, "untrusted terrain key is rejected before injected memory estimation");
        Assert.Equal(0, builds, "untrusted terrain key is rejected before factory construction");
        Assert.Equal(0, store.Diagnostics.BuildingWorlds, "identity rejection reserves no build");
        Assert.Equal(0L, store.Diagnostics.ReservedResidentBytes, "identity rejection reserves no output");
        Reject<FormationValidationException>(() => Limits().Prepare(Selected(contract: "other-grading-contract")));
    }

    [Test]
    public static void SelectedWalkingDefaultFactoryAndStoreRemainDisabledEvenWithInjectedFactory()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        Reject<NotSupportedException>(() => EnvironmentWorldFactory.Create(descriptor));
        int builds = 0, estimates = 0;
        using var store = new WorldStore(new WorldStoreOptions(),
            (d, _) => { builds++; return new SphereSurface(d.Radius, 1); },
            _ => { estimates++; return new WorldMemoryEstimate(0, 0); });
        Reject<NotSupportedException>(() => { using WorldLease lease = store.Acquire(descriptor); });
        Assert.Equal(0, builds, "custom factory cannot bypass the absent selected capability");
        Assert.Equal(0, estimates, "default-disabled selection fails before custom estimates");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => EnvironmentWorldFactory.CreateSelected(descriptor, Limits(), cancelled.Token));
    }

    [Test]
    public static void SelectedWalkingPublishesOneCoherentBundleAndPreservesLegacyTerrainOutputs()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        WalkingWorld world = EnvironmentWorldFactory.CreateSelected(descriptor, Limits());
        TerrainLattice lattice = world.TerrainLattice ?? throw new TestFailure("Selected lattice is required.");
        FormationField field = world.Formations ?? throw new TestFailure("Selected field is required.");
        TerrainPinSet pins = TerrainPinSet.Of(world) ?? throw new TestFailure("Selected pins are required.");
        Assert.True(ReferenceEquals(lattice, pins.Lattice), "field consumers share the exact immutable lattice descriptor");
        Assert.Equal(field.GenerationDigest, pins.GenerationDigest, "pins carry the admitted field generation");
        Assert.Equal(WalkingGradedTerrain.InputIdentity(descriptor, RawProfile), pins.TerrainIdentity, "pins bind independently reconstructed graded inputs");
        Assert.True(world.MatchesSelected(descriptor), "private bundle is stamped with complete owning descriptor and qualification");
        Assert.True(!world.MatchesSelected(Selected(catalog: Catalog(alternateLodKeys: true))), "same physical generation cannot authenticate another LOD qualification");
        Assert.Equal(0, field.Pieces.Length, "high-threshold synthetic survey produces a complete empty field, not an actual asset admission");
        Assert.Equal(0, field.Rejections.Length, "empty planning is not a failed partial seal");
        Assert.Equal(0, pins.Vertices.Length, "complete empty field has complete empty pins");
        Reject<NotSupportedException>(() => { _ = world.Cliffs; });
        Reject<NotSupportedException>(() => world.GroundCollision(ZoneFrame.World, new TileKey(0, 0, 0, 0), 0));
        var legacy = (WalkingWorld)EnvironmentWorldFactory.Create(Earth());
        Assert.True(legacy.TerrainLattice == null && legacy.Formations == null && legacy.TerrainPins == null, "legacy worlds opt out of all selected capabilities");
        Assert.True(legacy.Cliffs != null, "legacy cliff owner remains available");
        for (int face = 0; face < 6; face++)
        for (int point = 0; point < 3; point++)
        {
            Vector3D direction = CubeSphere.Direction(face, -0.6 + point * 0.6, 0.35);
            Assert.Equal(legacy.HeightAt(direction), world.HeightAt(direction), "selected construction preserves canonical graded terrain samples");
            Assert.Equal(legacy.WaterLevelAt(direction), world.WaterLevelAt(direction), "selected construction preserves water samples");
            Assert.Equal(legacy.Fields(direction), world.Fields(direction), "selected construction preserves raw field samples");
        }
        Assert.True(world.Monuments.All is not MonumentPlacement[], "public site list cannot cast to its backing array");
        Assert.True(((ICollection<MonumentPlacement>)world.Monuments.All).IsReadOnly, "public site list rejects edits");
        Reject<NotSupportedException>(() => ((IList<MonumentPlacement>)world.Monuments.All).Add(default));
        Assert.True(MonumentDefinition.All is not MonumentDefinition[], "public physical catalog cannot cast to its backing array");
        MonumentDefinition first = MonumentDefinition.All[0];
        Reject<NotSupportedException>(() => ((IList<MonumentDefinition>)MonumentDefinition.All)[0] = first);
        Assert.True(world.Lakes is not List<WalkingLake>, "public lake view cannot cast to the sampler's mutable list");
        Assert.True(world.Drainage is not DrainageSegment[], "public drainage view cannot cast to the sampler's backing array");
        Reject<NotSupportedException>(() => ((IList<WalkingLake>)world.Lakes).Add(default));
        Reject<NotSupportedException>(() => ((IList<DrainageSegment>)world.Drainage).Add(default));
    }

    [Test]
    public static void SelectedWalkingMandatoryReservationsCannotBeUnderchargedByCustomEstimators()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        SelectedWalkingWorldLimits limits = Limits();
        SelectedWalkingWorldPlan plan = limits.Prepare(descriptor);
        (long fieldResident, long fieldScratch) = limits.Field.Reserve();
        (long pinResident, long pinScratch) = TerrainPinSet.Reserve(limits.Pins);
        Assert.True(plan.AdditionalEstimate.EstimatedResidentBytes > fieldResident + pinResident, "selected output reserves eager sites/index as well as field and pins");
        Assert.True(plan.AdditionalEstimate.ReservedBuildBytes > fieldScratch + pinScratch, "selected scratch adds grading and pin work to the entire field reservation");
        WorldStoreOptions options = Options();
        WorldMemoryEstimate baseline = WorldStore.EstimateDefault(descriptor, options);
        long resident = baseline.EstimatedResidentBytes + plan.AdditionalEstimate.EstimatedResidentBytes + descriptor.EstimatedSelectionBytes;
        long scratch = baseline.ReservedBuildBytes + plan.AdditionalEstimate.ReservedBuildBytes;
        int builds = 0;
        foreach (WorldStoreOptions tooSmall in new[]
        {
            options with { MaxEstimatedResidentBytes = resident - 1 }, options with { MaxReservedBuildBytes = scratch - 1 }
        })
        {
            using var store = new WorldStore(tooSmall,
                (d, _) => { builds++; return new SphereSurface(d.Radius, 1); }, _ => new WorldMemoryEstimate(0, 0));
            Reject<WorldStoreCapacityException>(() => { using WorldLease lease = store.Acquire(descriptor); });
            Assert.Equal(0L, store.Diagnostics.ReservedBuildBytes, "capacity refusal publishes no partial reservation");
        }
        Assert.Equal(0, builds, "a zero estimator cannot admit selected construction below its mandatory floors");
        Reject<FormationCapacityException>(() => (limits with { MaxEstimatedGradingResidentBytes = 0 }).Prepare(descriptor));
        Reject<FormationCapacityException>(() => (limits with { Pins = limits.Pins with { MaxEstimatedScratchBytes = 0 } }).Prepare(descriptor));
        Reject<FormationCapacityException>(() => (limits with { Field = limits.Field with { MaxEstimatedResidentBytes = 0 } }).Prepare(descriptor));
        var larger = limits with { Pins = limits.Pins with { MaxVertices = 64 } };
        Assert.True(larger.Prepare(descriptor).AdditionalEstimate.EstimatedResidentBytes > plan.AdditionalEstimate.EstimatedResidentBytes,
            "operational pin capacity changes reservation without changing descriptor or seed");
    }

    [Test]
    public static void SelectedWalkingRejectsSpoofedFactoryOutputAndRetiresItsOwnedResources()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        var output = new OwnedSurface(descriptor.Radius);
        using var store = new WorldStore(Options(), (_, _) => output, _ => new WorldMemoryEstimate(0, 0));
        Reject<InvalidOperationException>(() => { using WorldLease lease = store.Acquire(descriptor); });
        Complete(store.BeginRetirement());
        Assert.Equal(1, output.DisposeCalls, "unqualified factory output is disposed exactly once");
        Assert.Equal(0, store.Diagnostics.ResidentWorlds, "no surface without the private completed bundle is published");
        Assert.Equal(0L, store.Diagnostics.ReservedResidentBytes, "rejected completed output releases reservation after cleanup");
        Assert.Equal(0L, store.Diagnostics.ReservedBuildBytes, "rejected factory has actually exited before scratch is released");
    }

    [Test]
    public static void SelectedWalkingCancellationKeepsFullReservationsUntilFactoryActuallyExits()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        WorldStoreOptions options = Options();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim(); using var cancelled = new CancellationTokenSource();
        var output = new OwnedSurface(descriptor.Radius);
        using var store = new WorldStore(options, (_, token) =>
        {
            using var registration = token.Register(stopped.Set);
            entered.Set();
            if (!release.Wait(Guard)) throw new TimeoutException("Selected factory barrier was not released.");
            return output; // Deliberately ignore cancellation to test ownership until the work really exits.
        }, _ => new WorldMemoryEstimate(0, 0));
        Task<WorldLease>? acquisition = null;
        try
        {
            acquisition = store.AcquireAsync(descriptor, cancelled.Token);
            Assert.True(entered.Wait(Guard), "controlled selected factory starts after mandatory admission");
            WorldStoreDiagnostics admitted = store.Diagnostics;
            WorldMemoryEstimate baseline = WorldStore.EstimateDefault(descriptor, options);
            WorldMemoryEstimate extra = options.SelectedWalkingLimits!.Prepare(descriptor).AdditionalEstimate;
            Assert.Equal(baseline.EstimatedResidentBytes + extra.EstimatedResidentBytes + descriptor.EstimatedSelectionBytes,
                admitted.ReservedResidentBytes, "admission holds the full selected output plus retained input catalog");
            Assert.Equal(baseline.ReservedBuildBytes + extra.ReservedBuildBytes, admitted.ReservedBuildBytes, "all simultaneous selected scratch is reserved");
            cancelled.Cancel();
            Reject<OperationCanceledException>(() => Complete(acquisition!));
            Assert.True(stopped.Wait(Guard), "last waiter cancellation reaches the actual factory token");
            Assert.Equal(admitted.ReservedResidentBytes, store.Diagnostics.ReservedResidentBytes, "cancellation cannot release output while work is running");
            Assert.Equal(admitted.ReservedBuildBytes, store.Diagnostics.ReservedBuildBytes, "cancellation cannot release scratch while work is running");
            Assert.Equal(0, output.DisposeCalls, "the store has not received output ownership before factory exit");
        }
        finally
        {
            release.Set();
            Complete(store.BeginRetirement());
            if (acquisition != null)
                try { Complete(acquisition).Dispose(); } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        }
        Assert.Equal(1, output.DisposeCalls, "cancelled late output is disposed after actual factory exit");
        Assert.Equal(0L, store.Diagnostics.ReservedBuildBytes, "retirement releases scratch only after all work exits");
        Assert.Equal(0L, store.Diagnostics.ReservedResidentBytes, "retirement releases selected output reservation");
    }

    [Test]
    public static void SelectedWalkingBuilderFailurePublishesNothingAndDefaultOptInStoreCanRecover()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        var limits = Limits();
        var failing = limits with { Field = limits.Field with { Planner = limits.Field.Planner with { MaxTerrainSamples = 0 } } };
        using (var store = new WorldStore(Options() with { SelectedWalkingLimits = failing }))
        {
            Reject<FormationCapacityException>(() => { using WorldLease lease = store.Acquire(descriptor); });
            Complete(store.BeginRetirement());
            Assert.Equal(0, store.Diagnostics.ResidentWorlds, "planner failure cannot publish a lattice-only world");
            Assert.Equal(0L, store.Diagnostics.ReservedBuildBytes, "failed construction releases scratch after its stack exits");
        }
        using var recovered = new WorldStore(Options());
        using (WorldLease first = recovered.Acquire(descriptor))
        using (WorldLease same = recovered.Acquire(Selected()))
        {
            Assert.True(ReferenceEquals(first.Surface, same.Surface), "default explicitly enabled factory coalesces complete selected worlds");
            Assert.True(first.Surface is WalkingWorld world && world.MatchesSelected(descriptor), "store publishes the authenticated atomic bundle");
        }
        Assert.Equal(1, recovered.Trim(), "idle selected world can be evicted through the existing ownership ledger");
        Complete(recovered.BeginRetirement());
        Assert.Equal(0L, recovered.Diagnostics.EstimatedResidentBytes, "eviction retires selected field and pin accounting");
    }

    [Test]
    public static void SelectedWalkingPrivateAdmissionStampRejectsGenuineOutputBuiltUnderDifferentLimits()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        SelectedWalkingWorldLimits admitted = Limits();
        SelectedWalkingWorldLimits larger = admitted with { Pins = admitted.Pins with { MaxVertices = 64 } };
        WalkingWorld world = EnvironmentWorldFactory.CreateSelected(descriptor, larger);
        Assert.True(world.MatchesSelected(descriptor), "genuine output retains the same physical and qualification identity");
        Assert.True(world.MatchesSelected(descriptor, larger with { Pins = larger.Pins with { } }),
            "independently reconstructed equal immutable allowances authenticate admission");
        Assert.True(!world.MatchesSelected(descriptor, admitted), "physical identity does not authorize a different construction reservation");
        using var store = new WorldStore(Options() with { SelectedWalkingLimits = admitted }, (_, _) => world,
            _ => new WorldMemoryEstimate(0, 0));
        Reject<InvalidOperationException>(() => { using WorldLease lease = store.Acquire(descriptor); });
        Complete(store.BeginRetirement());
        Assert.Equal(0, store.Diagnostics.ResidentWorlds, "an injected factory cannot publish a world built under larger allowances");
        Assert.Equal(0L, store.Diagnostics.ReservedBuildBytes, "rejected output finishes retirement without a leaked build reservation");
    }

    [Test]
    public static void SelectedWalkingCollisionOptInPublishesTheCanonicalBundleWithoutLegacyGeology()
    {
        PlanetEnvironmentDescriptor descriptor = Selected();
        SelectedWalkingWorldLimits inactive = Limits();
        SelectedWalkingWorldLimits enabled = inactive with { Collision = SelectedGroundCollisionTests.Limits() };
        SelectedWalkingWorldPlan prior = inactive.Prepare(descriptor), plan = enabled.Prepare(descriptor);
        Assert.Equal(8192L, plan.AdditionalEstimate.EstimatedResidentBytes - prior.AdditionalEstimate.EstimatedResidentBytes,
            "zero-site collision snapshot retains an explicit metadata allowance");
        Assert.Equal(8192L, plan.AdditionalEstimate.ReservedBuildBytes - prior.AdditionalEstimate.ReservedBuildBytes,
            "collision snapshot construction is included in simultaneous build scratch");
        WalkingWorld world = EnvironmentWorldFactory.CreateSelected(descriptor, enabled);
        var tile = new TileKey(4, 2, 1, 1);
        TriangleMesh expected = SurfaceGrid.Collision(world, ZoneFrame.World, tile, 2, 1, enabled.Collision!.Terrain);
        TriangleMesh actual = world.GroundCollision(ZoneFrame.World, tile, 2);
        Assert.True(expected.Positions.AsSpan().SequenceEqual(actual.Positions) && expected.Indices.AsSpan().SequenceEqual(actual.Indices),
            "the real selected WalkingWorld routes its complete empty field through canonical collision");
        Assert.True(world.MatchesSelected(descriptor, enabled) && !world.MatchesSelected(descriptor, inactive),
            "collision allowances participate in the private admission stamp without changing physical identity");
        Reject<NotSupportedException>(() => _ = world.Cliffs);
    }

    private static WalkingParameters Parameters() => WalkingParameters.Default with
    { Provinces = 8, DrainageCell = 1000, MonumentChance = 0, OceanCover = 0.25 };
    private static PlanetEnvironmentDescriptor Earth(FormationWorldSelection? selection = null, double radius = 250,
        WalkingParameters? parameters = null) => EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath,
            radius, walkingParameters: parameters ?? Parameters(), formationSelection: selection) ?? throw new TestFailure("Earth metadata is required.");
    private static PlanetEnvironmentDescriptor Selected(string? terrainIdentity = null, FormationCatalog? catalog = null,
        string contract = WalkingGradedTerrain.Contract)
    {
        PlanetEnvironmentDescriptor plain = Earth();
        var field = new FormationFieldContract(terrainIdentity ?? WalkingGradedTerrain.InputIdentity(plain, RawProfile), plain.Radius, 0,
            FormationFieldAdmissionPolicy.IndependentPieces);
        return Earth(FormationWorldSelection.Create("formation-selected-fixture", contract, catalog ?? Catalog(), Settings(), fieldContract: field));
    }
    private static FormationPlannerSettings Settings() =>
        new("recipe", 1, RawProfile, 1, 1000000, 1, 2, 1, 1, 2, Vector3D.UnitY, "toe", surveyLevel: 0);
    private static FormationCatalog Catalog(bool alternateLodKeys = false)
    {
        string digest = new('A', 64);
        var geometry = new FormationGeometry("stone", 1, "test-family",
            new[] { Vector3D.Zero, new Vector3D(0, 0, 2), new Vector3D(2, 0, 0) },
            new[] { new GeometryTriangle(0, 1, 2, 0) }, new[] { new SupportPatch("top", "stone", SupportCapabilities.None) });
        var bindings = new[] { new FormationContactLoopBinding("ring", new[] { 0, 1, 2 }) };
        var maps = new[] { new CliffLodTriangleMap(0, 0) };
        var definition = new CliffSupportDefinition(geometry, digest, digest, 1, 1,
            new[] { new CliffPatchDomain("top", new[] { new CliffPatchBoundary(new[] { 0, 1, 2 }) }) },
            new[] { new CliffContactDomain("toe", "toe", "top", new[] { 0, 1 }, 0, 1, 1) },
            new[] { new CliffLodBinding(alternateLodKeys ? "near-preview" : "near", digest, geometry, maps, 0, 1, bindings),
                new CliffLodBinding(alternateLodKeys ? "far-preview" : "far", digest, geometry, maps, 0, 1, bindings) },
            contactLoops: new[] { new FormationContactLoop("ring", new[] { 0, 1, 2 }) });
        var recipe = new FormationRecipe("recipe", 1, 17, "test-family", new[] { new FormationRecipeSlot(0, "crest", true,
            new[] { new FormationGeometryChoice("stone", 1, new[] { "toe" }, 1, 1) }) }, 10, 20, 0.5, 2, 1);
        return new FormationCatalog(new[] { definition }, new[] { new SubstrateProfile("stone", 1, 0, 0, 0, 0, 0, 0, 0) }, new[] { recipe });
    }
    private static SelectedWalkingWorldLimits Limits() => new()
    {
        MaxProtectedSites = 0, MaxEstimatedGradingResidentBytes = 1024 * 1024, MaxEstimatedGradingScratchBytes = 1024 * 1024,
        Pins = new TerrainPinSetLimits { MaxVertices = 32, MaxInputPins = 128, MaxNormalTriangles = 256,
            MaxAdditionalSamples = 256, MaxEstimatedResidentBytes = 1024 * 1024, MaxEstimatedScratchBytes = 1024 * 1024 },
        Field = new FormationFieldLimits
        {
            Planner = new FormationPlannerLimits { MaxRecords = 0, MaxContourSegments = 0, MaxProtectedSites = 0,
                MaxEstimatedOwnedBytes = 1024 * 1024, MaxEstimatedScratchBytes = 1024 * 1024 },
            Support = new FormationSupportIndexLimits { MaxPieces = 0, MaxTriangles = 1, MaxNodes = 1,
                MaxUniqueGeometries = 1, MaxUniqueTriangles = 1, MaxEstimatedResidentBytes = 65536, MaxEstimatedScratchBytes = 65536 },
            Seal = new FormationTerrainSealLimits { MaxTerrainTriangles = 16, MaxDependencyTriangles = 16,
                MaxEstimatedScratchBytes = 1024 * 1024, MaxEstimatedResultBytes = 65536 },
            MaxPieces = 0, MaxLoopChecks = 0, MaxReceipts = 0, MaxCohorts = 0, MaxRejections = 0,
            MaxCandidateTrianglesPerLoop = 0, MaxDependencyTriangles = 0, MaxPinnedVertices = 0, MaxDependencyTiles = 0,
            MaxCandidateCellVisits = 0, MaxTerrainVertexSamples = 0, MaxCandidateTriangleTests = 0,
            MaxSegmentTriangleAttempts = 0, MaxExactArithmeticOperations = 0,
            MaxEstimatedResidentBytes = 1024 * 1024, MaxEstimatedScratchBytes = 8 * 1024 * 1024,
        }
    };
    private static WorldStoreOptions Options() => new()
    {
        SelectedWalkingLimits = Limits(), MaxResidentWorlds = 2, MaxConcurrentBuilds = 1,
        DefaultEstimatedResidentBytes = 0, DefaultReservedBuildBytes = 0,
        MaxEstimatedResidentBytes = 256 * 1024 * 1024, MaxReservedBuildBytes = 64 * 1024 * 1024,
    };
    private sealed class OwnedSurface(double radius) : ISurface, IDisposable
    {
        private int _disposeCalls;
        public int DisposeCalls => Volatile.Read(ref _disposeCalls);
        public double Radius => radius;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0;
        public void Dispose() => Interlocked.Increment(ref _disposeCalls);
    }
    private static T Complete<T>(Task<T> task) => task.WaitAsync(Guard).GetAwaiter().GetResult();
    private static void Complete(Task task) => task.WaitAsync(Guard).GetAwaiter().GetResult();
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new TestFailure("Expected " + typeof(T).Name + "."); }
}
