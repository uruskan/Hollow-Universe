using System;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationWorldSelectionTests
{
    private const string Contract = "walking-finalized-grading-raw-fields-sites-v1";

    [Test]
    public static void ExplicitSelectionBindsPhysicalInputsWithoutReseedingLegacyWorld()
    {
        PlanetEnvironmentDescriptor legacy = Earth();
        FormationWorldSelection selection = Selection();
        PlanetEnvironmentDescriptor selected = Earth(selection);
        Assert.Equal(1, legacy.CanonicalSchema, "null selection keeps the original descriptor schema");
        Assert.Equal(2, selected.CanonicalSchema, "an explicit selection uses the extended schema");
        Assert.Equal(legacy.SurfaceSeed, selected.SurfaceSeed, "selection does not create a procedural seed");
        Assert.Equal(legacy.TerrainSeed, selected.TerrainSeed, "raw terrain seed stays explicit and unchanged");
        Assert.Equal(legacy.WalkingParameters, selected.WalkingParameters, "raw grading inputs stay unchanged");
        Assert.True(selected.CanonicalIdentity != legacy.CanonicalIdentity && !selected.IsLegacyMultiplayerCompatible,
            "selection cannot masquerade as the legacy shared-build profile");
        Assert.Equal(selection.Identity.PhysicalContentDigest, selected.RequiredPhysicalContentFingerprint,
            "selected decoded physical content is advertised");
        Assert.Equal(Earth().CanonicalIdentity, legacy.CanonicalIdentity, "selection creation cannot mutate the default descriptor");
        Assert.True(selected.CanonicalIdentity != Earth(Selection(contract: "walking-test-contract-v2")).CanonicalIdentity,
            "final grading/site contract affects physical save identity");
        Assert.True(selected.CanonicalIdentity != Earth(Selection(profile: "formation-second-profile")).CanonicalIdentity,
            "explicit profile identity participates in compatibility");
        Assert.True(selected.CanonicalIdentity != Earth(selection, radius: 1300).CanonicalIdentity,
            "same formation catalog cannot alias a different world radius");
        var region = new FormationSurveyRegion(1, new[] { new TileKey(0, 1, 0, 0) }, 1);
        FormationWorldSelection regional = FormationWorldSelection.Create("formation-regional", Contract, Catalog(), Settings(region));
        Assert.True(regional.EstimatedResidentBytes >= regional.Catalog.EstimatedResidentBytes +
            regional.Identity.EstimatedRetainedBytes + region.Diagnostics.EstimatedOwnedBytes,
            "the retained survey halo is admitted independently of its compact canonical policy");
        Reject<NotSupportedException>(() => EnvironmentDescriptors.Resolve(StartingWorld.System.Value,
            StartingWorld.BodyPath, 1200, EnvironmentSurfaceKind.StandIn, landing: Vector3D.UnitX,
            formationSelection: selection));
    }

    [Test]
    public static void QualificationOnlyChangePreservesPhysicalSaveIdentityButCannotAliasResidentCatalog()
    {
        PlanetEnvironmentDescriptor a = Earth(Selection()), b = Earth(Selection(lodOffset: 0.125));
        Assert.Equal(a.CanonicalIdentity, b.CanonicalIdentity, "visual LOD changes do not invalidate physical saves");
        Assert.Equal(a.Identity, b.Identity, "physical digest excludes LOD evidence");
        Assert.True(a.QualificationIdentity != b.QualificationIdentity && !a.Equals(b),
            "peer admission and retained resource equality include the changed qualification input");
        int builds = 0;
        var options = new WorldStoreOptions { MaxResidentWorlds = 2, MaxEstimatedResidentBytes = 4 * 1024 * 1024 };
        using var store = new WorldStore(options,
            (descriptor, _) => { builds++; return new SphereSurface(descriptor.Radius, 71); },
            _ => new WorldMemoryEstimate(1, 1));
        using (WorldLease first = store.Acquire(a))
        using (WorldLease same = store.Acquire(Earth(Selection())))
        using (WorldLease second = store.Acquire(b))
        {
            Assert.Equal(2, builds, "full qualification inputs distinguish resident catalogs while equivalent selection coalesces");
            Assert.True(ReferenceEquals(first.Surface, same.Surface) && !ReferenceEquals(first.Surface, second.Surface),
                "each lease owns the correct selected resource variant");
            Assert.Equal(b.QualificationIdentity, second.Descriptor.QualificationIdentity, "lease carries actual acquired qualification");
            Assert.True(store.Diagnostics.EstimatedResidentBytes >= a.EstimatedSelectionBytes + b.EstimatedSelectionBytes,
                "selection accounting cannot be bypassed by an injected cheap world estimator");
        }
        store.BeginRetirement().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    [Test]
    public static void SelectionRejectsUnknownImplementationOversizeKeysAndUnadmittedRetainedInputs()
    {
        Reject<FormationValidationException>(() => Selection(profile: "legacy-walking-earth"));
        Reject<FormationValidationException>(() => Selection(profile: "formation-" + new string('a', 55)));
        Reject<FormationValidationException>(() => Selection(contract: "unnegotiated space"));
        Reject<NotSupportedException>(() => FormationWorldSelection.Create("formation-test", Contract, Catalog(), Settings(),
            new FormationAlgorithmRevisions { Planner = 1 }));
        Reject<FormationValidationException>(() => FormationWorldSelection.Create("formation-test", Contract, Catalog(), Settings(),
            maximumCanonicalBytes: 4 * 1024 * 1024 + 1));
        Reject<FormationCapacityException>(() => FormationWorldSelection.Create("formation-test", Contract, Catalog(), Settings(),
            maximumCanonicalBytes: 1));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => FormationWorldSelection.Create("formation-test", Contract, Catalog(), Settings(),
            cancellation: cancelled.Token));
        int builds = 0;
        using var store = new WorldStore(new WorldStoreOptions { MaxEstimatedResidentBytes = 1 },
            (descriptor, _) => { builds++; return new SphereSurface(descriptor.Radius, 71); },
            _ => new WorldMemoryEstimate(0, 0));
        Reject<WorldStoreCapacityException>(() => { using WorldLease lease = store.Acquire(Earth(Selection())); });
        Assert.Equal(0, builds, "retained selection admission precedes factory construction");
    }

    private static PlanetEnvironmentDescriptor Earth(FormationWorldSelection? selection = null, double radius = 1200) =>
        EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath, radius,
            formationSelection: selection) ?? throw new TestFailure("Earth descriptor is required");

    private static FormationWorldSelection Selection(double lodOffset = 0, string profile = "formation-test",
        string contract = Contract) => FormationWorldSelection.Create(profile, contract, Catalog(lodOffset), Settings());

    private static FormationPlannerSettings Settings(FormationSurveyRegion? region = null) => new("recipe", 1, "raw-test", 1, 0.5, 1, 2, 1, 1, 2,
        Vector3D.UnitY, "toe", surveyLevel: 0, region: region);

    private static FormationCatalog Catalog(double lodOffset = 0)
    {
        string digest = new('A', 64);
        var vertices = new[] { Vector3D.Zero, new Vector3D(0, 0, 2), new Vector3D(2, 0, 0) };
        var triangles = new[] { new GeometryTriangle(0, 1, 2, 0) };
        var patches = new[] { new SupportPatch("top", "stone", SupportCapabilities.Walkable) };
        var geometry = new FormationGeometry("stone", 1, "test-family", vertices, triangles, patches);
        var visual = new FormationGeometry("visual", 1, "test-family",
            new[] { vertices[0] + Vector3D.UnitY * lodOffset, vertices[1] + Vector3D.UnitY * lodOffset,
                vertices[2] + Vector3D.UnitY * lodOffset }, triangles, patches);
        var mappings = new[] { new CliffLodTriangleMap(0, 0) };
        var definition = new CliffSupportDefinition(geometry, digest, digest, 1, 1,
            new[] { new CliffPatchDomain("top", new[] { new CliffPatchBoundary(new[] { 0, 1, 2 }) }) },
            new[] { new CliffContactDomain("toe", "toe", "top", new[] { 0, 1 }, 0, 1, 1) },
            new[] { new CliffLodBinding("near", digest, visual, mappings, 0.25, 1),
                new CliffLodBinding("far", digest, visual, mappings, 0.25, 1) });
        var profile = new SubstrateProfile("stone", 1, 0, 0, 0, 0, 0, 0, 0);
        var choice = new FormationGeometryChoice("stone", 1, new[] { "toe" }, 1, 1);
        var recipe = new FormationRecipe("recipe", 1, 17, "test-family",
            new[] { new FormationRecipeSlot(0, "crest", true, new[] { choice }) }, 10, 20, 0.5);
        return new FormationCatalog(new[] { definition }, new[] { profile }, new[] { recipe });
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
