using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationGenerationIdentityTests
{
    [Test]
    public static void FormationIdentityCanonicalInputsIgnoreAllocationCultureAndKeyedEnumerationOrder()
    {
        FormationCatalog a = Catalog(), b = Catalog(new FixtureOptions { Permute = true, RotateLoops = true });
        FormationGenerationIdentity expected = Identity(a);
        Same(expected, Identity(b));
        CultureInfo prior = CultureInfo.CurrentCulture, priorUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string name in new[] { "tr-TR", "fr-FR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                Same(expected, Identity(b));
            }
        }
        finally { CultureInfo.CurrentCulture = prior; CultureInfo.CurrentUICulture = priorUi; }
        Parallel.For(0, 4, _ => Same(expected, Identity(b)));
        AssertDigest(expected.PhysicalContentCanonical, expected.PhysicalContentDigest);
        AssertDigest(expected.GenerationCanonical, expected.GenerationDigest);
        AssertDigest(expected.QualificationCanonical, expected.QualificationDigest);
    }

    [Test]
    public static void FormationIdentityChangesForPhysicalOrdinalsPermissionsDomainsContactsAndScale()
    {
        var baseline = new FixtureOptions();
        FormationGenerationIdentity original = Identity(Catalog(baseline));
        FixtureOptions[] changes =
        {
            baseline with { GeometryKey = "changed-a" }, baseline with { GeometryRevision = 2 },
            baseline with { Family = "other-family" }, baseline with { VertexOffset = 0.125 },
            baseline with { ReorderVertices = true }, baseline with { ReorderTriangles = true },
            baseline with { ReorderPatches = true }, baseline with { ReverseWinding = true },
            baseline with { Capabilities = SupportCapabilities.Walkable | SupportCapabilities.Plantable },
            baseline with { ProfileKey = "changed-profile" }, baseline with { PatchKey = "changed-patch" },
            baseline with { DefinitionMinimumScale = 0.25 }, baseline with { DefinitionMaximumScale = 3 },
            baseline with { ContactKey = "changed-contact" }, baseline with { ContactRole = "other-role" },
            baseline with { ReverseContact = true }, baseline with { ContactMinimumBurial = 0.01 },
            baseline with { ContactMaximumBurial = 0.3 }, baseline with { ContactMaximumGap = 0.2 }
        };
        foreach (FixtureOptions change in changes) PhysicalChanged(original, Identity(Catalog(change)));
        Same(original, Identity(Catalog(baseline with { SignedZero = true })));
    }

    [Test]
    public static void FormationIdentityChangesForRecipeRoleChoiceCoverageAndFitPolicy()
    {
        var baseline = new FixtureOptions();
        FormationGenerationIdentity original = Identity(Catalog(baseline));
        FixtureOptions[] changes =
        {
            baseline with { RecipeDomain = 23 }, baseline with { RecipeSection = 13 },
            baseline with { RecipeExtent = 30 }, baseline with { RecipeCoverage = 0.6 },
            baseline with { RecipeGuideStations = 4 }, baseline with { RecipeFitProbes = 3 },
            baseline with { Slot = 3 }, baseline with { SlotRole = "changed-role" },
            baseline with { SlotRequired = false }, baseline with { IncludeSecondChoice = false },
            baseline with { ChoiceMinimumScale = 0.75 }, baseline with { ChoiceMaximumScale = 1.5 },
            baseline with { RequireSecondContact = false }
        };
        foreach (FixtureOptions change in changes) PhysicalChanged(original, Identity(Catalog(change)));
    }

    [Test]
    public static void FormationIdentityChangesForEverySubstrateScalar()
    {
        var baseline = new ProfileOptions();
        FormationGenerationIdentity original = Identity(Catalog(profile: baseline));
        ProfileOptions[] changes =
        {
            baseline with { Bedrock = 0.1 }, baseline with { LooseStone = 0.1 }, baseline with { Soil = 0.6 },
            baseline with { Depth = 0.3 }, baseline with { Plant = 0.6 }, baseline with { Grass = 0.5 },
            baseline with { Shrubs = 0.04 }, baseline with { Trees = 0.02 }, baseline with { Litter = 0.3 },
            baseline with { MinimumTemperature = -10 }, baseline with { MaximumTemperature = 45 },
            baseline with { MinimumUp = 0.7 }
        };
        foreach (ProfileOptions change in changes) PhysicalChanged(original, Identity(Catalog(profile: change)));
    }

    [Test]
    public static void FormationIdentityBindsEveryPlannerSettingWithoutReseedingOrChangingCatalogContent()
    {
        FormationCatalog catalog = Catalog();
        var baseline = new SettingsOptions();
        FormationGenerationIdentity original = Identity(catalog, Settings(baseline));
        SettingsOptions[] changes =
        {
            baseline with { RecipeKey = "recipe-b" }, baseline with { RecipeRevision = 2 },
            baseline with { TerrainProfileKey = "other-ground" }, baseline with { SurveyLevel = 2 },
            baseline with { GradientProbe = 3 }, baseline with { MinimumRiseRun = 0.6 },
            baseline with { CrossStep = 11 }, baseline with { CrossSteps = 11 },
            baseline with { Along = 3 }, baseline with { Down = 3 }, baseline with { Reach = 90 },
            baseline with { Up = Vector3D.UnitZ }, baseline with { AnchorRole = "other-anchor" }
        };
        foreach (SettingsOptions change in changes)
        {
            FormationGenerationIdentity actual = Identity(catalog, Settings(change));
            Assert.Equal(original.PhysicalContentCanonical, actual.PhysicalContentCanonical, "Planner selection does not change decoded catalog content.");
            Assert.Equal(original.QualificationCanonical, actual.QualificationCanonical, "Planner selection does not change decoded qualification evidence.");
            Assert.True(original.GenerationCanonical != actual.GenerationCanonical && original.GenerationDigest != actual.GenerationDigest,
                "Each output-affecting planner setting changes generation identity.");
        }
        // This encoder has no universe/world/terrain seed argument and creates no terrain. Actual world seed
        // preservation remains a descriptor/factory integration test, not something a catalog hash can prove.
    }

    [Test]
    public static void FormationIdentityBindsCanonicalCoreCellsHaloAndWholeVersusRegionalPolicy()
    {
        var cells = new[] { new TileKey(4, 3, 3, 3), new TileKey(4, 3, 4, 3) };
        var first = new FormationSurveyRegion(3, cells, 1);
        Array.Reverse(cells);
        var second = new FormationSurveyRegion(3, cells, 1,
            new FormationSurveyRegionLimits { MaxCoreCells = 16, MaxSurveyCells = 128, MaxInputVisits = 16 });
        FormationCatalog catalog = Catalog();
        FormationGenerationIdentity expected = Identity(catalog, Settings(region: first));
        Same(expected, Identity(catalog, Settings(region: second)));
        FormationSurveyRegion[] changes =
        {
            new(3, cells, 2), new(3, new[] { cells[0] }, 1),
            new(3, new[] { new TileKey(4, 3, 3, 4), cells[0] }, 1),
            new(4, new[] { new TileKey(4, 4, 6, 6), new TileKey(4, 4, 7, 6) }, 1)
        };
        foreach (FormationSurveyRegion region in changes)
        {
            FormationGenerationIdentity actual = Identity(catalog, Settings(region: region));
            Assert.Equal(expected.PhysicalContentDigest, actual.PhysicalContentDigest, "Region policies do not alter the physical catalog.");
            Assert.True(expected.GenerationDigest != actual.GenerationDigest, "Core membership, actual grid and halo affect generation identity.");
        }
        TileKey[] whole = (from face in Enumerable.Range(0, 6) from y in Enumerable.Range(0, 2)
            from x in Enumerable.Range(0, 2) select new TileKey(face, 1, x, y)).ToArray();
        FormationGenerationIdentity explicitWhole = Identity(catalog, Settings(region: new FormationSurveyRegion(1, whole, 1)));
        FormationGenerationIdentity implicitWhole = Identity(catalog, Settings(new SettingsOptions { SurveyLevel = 0 }));
        Assert.True(explicitWhole.GenerationDigest != implicitWhole.GenerationDigest, "Explicit core ownership is never inferred from equivalent current output.");
    }

    [Test]
    public static void FormationIdentitySeparatesLodQualificationFromPhysicalGenerationAndByteReceipts()
    {
        var baseline = new FixtureOptions();
        FormationGenerationIdentity original = Identity(Catalog(baseline));
        foreach (FixtureOptions change in new[]
        {
            baseline with { LodOffset = 0.01 }, baseline with { LodError = 0.3 },
            baseline with { LodNormal = 0.8 }, baseline with { ReverseVisualOrdinals = true },
            baseline with { NearLodKey = "different-near" }
        })
        {
            FormationGenerationIdentity actual = Identity(Catalog(change));
            Assert.Equal(original.PhysicalContentCanonical, actual.PhysicalContentCanonical, "LOD proof is outside physical content identity.");
            Assert.Equal(original.GenerationCanonical, actual.GenerationCanonical, "LOD proof does not reroll or reidentify physical generation.");
            Assert.True(original.QualificationCanonical != actual.QualificationCanonical && original.QualificationDigest != actual.QualificationDigest,
                "Decoded qualification geometry, correspondence and tolerances have separate admission identity.");
        }
        Same(original, Identity(Catalog(baseline with { RawDigest = new string('A', 64) })));
        // Raw receipt/source/physical/LOD file digests can change after cosmetic or packaging edits while decoded
        // physical/proof data remain equivalent. Authenticity is still required separately by the package loader.
    }

    [Test]
    public static void FormationIdentityBindsExplicitAlgorithmRevisionsToTheCorrectContract()
    {
        FormationCatalog catalog = Catalog();
        var revisions = new FormationAlgorithmRevisions();
        FormationGenerationIdentity original = Identity(catalog, revisions: revisions);
        foreach (FormationAlgorithmRevisions change in new[]
        {
            revisions with { Planner = revisions.Planner + 1 }, revisions with { Topology = revisions.Topology + 1 },
            revisions with { RegionalContainment = revisions.RegionalContainment + 1 },
            revisions with { ConflictAdmission = revisions.ConflictAdmission + 1 },
            revisions with { Transitions = 2 }, revisions with { Support = 2 }, revisions with { SurfaceChart = 2 },
            revisions with { Substrate = 2 }
        })
        {
            FormationGenerationIdentity actual = Identity(catalog, revisions: change);
            Assert.Equal(original.PhysicalContentDigest, actual.PhysicalContentDigest, "Algorithm versions do not change decoded physical input.");
            Assert.Equal(original.QualificationDigest, actual.QualificationDigest, "Simulation versions do not change qualification evidence.");
            Assert.True(original.GenerationDigest != actual.GenerationDigest, "Successful-output algorithm changes fence generation compatibility.");
        }
        FormationGenerationIdentity qualification = Identity(catalog, revisions: revisions with { Qualification = 2 });
        Assert.Equal(original.GenerationCanonical, qualification.GenerationCanonical, "Qualification algorithm changes use a separate admission hook.");
        Assert.True(original.QualificationDigest != qualification.QualificationDigest, "Qualification revision participates in its own canonical contract.");
    }

    [Test]
    public static void FormationIdentityBindsR2IndependentConflictAndSourceEvidencePoliciesInEveryScope()
    {
        var revisions = new FormationAlgorithmRevisions();
        Assert.Equal(2, FormationPlanner.GenerationRevision, "Re-review identity policies when the supported planner changes.");
        Assert.Equal(FormationPlanner.GenerationRevision, revisions.Planner, "Default declaration matches the supported planner.");
        Assert.Equal(2, revisions.RegionalContainment, "Regional evidence now includes all components, fits and source shadow closure.");
        Assert.Equal(1, revisions.ConflictAdmission, "Independent proposal conflict admission has an explicit revision.");
        Assert.Equal("independent-proposal-lower-id-v1", FormationPlanner.ConflictPolicy, "Conflict policy is an implementation contract.");
        FormationCatalog catalog = Catalog();
        var region = new FormationSurveyRegion(3, new[] { new TileKey(4, 3, 3, 3) }, 1);
        foreach (FormationPlannerSettings selected in new[] { Settings(), Settings(region: region) })
        {
            FormationGenerationIdentity identity = Identity(catalog, selected);
            byte[] generation = Convert.FromBase64String(identity.GenerationCanonical);
            byte[] physical = Convert.FromBase64String(identity.PhysicalContentCanonical);
            byte[] qualification = Convert.FromBase64String(identity.QualificationCanonical);
            foreach (string policy in new[]
            {
                FormationPlanner.ConflictPolicy, "outward-coordinate-spans-plus-diagonal-v1",
                "outward-physical-aabb-plus-extent-two-edge-ring-shadow-v1",
                "canonical-contour-core-publication-only-v1", "all-survey-components-and-fits-complete-or-refuse-v1",
                "paired-last-crest-source-segment-shadow-v1", "whole-and-regional-v1", "SourceShadowEdgeRings"
            })
            {
                byte[] encoded = Encoding.UTF8.GetBytes(policy);
                Assert.True(generation.AsSpan().IndexOf(encoded) >= 0, "Generation binds policy: " + policy);
                Assert.True(physical.AsSpan().IndexOf(encoded) < 0 && qualification.AsSpan().IndexOf(encoded) < 0,
                    "Planner policy does not contaminate decoded physical or qualification identity: " + policy);
            }
            FormationGenerationIdentity priorDeclaration = Identity(catalog,
                selected, revisions with { Planner = 1, RegionalContainment = 1 });
            Assert.True(identity.GenerationDigest != priorDeclaration.GenerationDigest,
                "Earlier planner/evidence declarations cannot alias the current default identity.");
        }
    }

    [Test]
    public static void FormationIdentityRejectsMissingRequiredInputsAndBoundedEncodingFailures()
    {
        FormationCatalog catalog = Catalog(); FormationPlannerSettings settings = Settings();
        var revisions = new FormationAlgorithmRevisions();
        FormationGenerationIdentity expected = Identity(catalog);
        int exact = new[] { expected.PhysicalContentCanonical, expected.GenerationCanonical, expected.QualificationCanonical }
            .Max(text => Convert.FromBase64String(text).Length);
        Same(expected, FormationGenerationIdentity.Create(catalog, settings, revisions, exact));
        Reject<FormationCapacityException>(() => FormationGenerationIdentity.Create(catalog, settings, revisions, exact - 1));
        Assert.Equal(Convert.FromBase64String(expected.PhysicalContentCanonical).Length, expected.PhysicalContentByteLength, "Physical byte count describes its exported canonical bytes.");
        Assert.Equal(Convert.FromBase64String(expected.GenerationCanonical).Length, expected.GenerationByteLength, "Generation byte count describes its exported canonical bytes.");
        Assert.Equal(Convert.FromBase64String(expected.QualificationCanonical).Length, expected.QualificationByteLength, "Qualification byte count describes its exported canonical bytes.");
        long retainedCharacters = (long)expected.PhysicalContentCanonical.Length + expected.GenerationCanonical.Length +
            expected.QualificationCanonical.Length + expected.PhysicalContentDigest.Length + expected.GenerationDigest.Length + expected.QualificationDigest.Length;
        Assert.True(expected.EstimatedRetainedBytes > 2 * retainedCharacters, "Retention includes all six UTF-16 strings plus object overhead.");
        Assert.True(expected.EstimatedPeakBytes > expected.EstimatedRetainedBytes + expected.PhysicalContentByteLength +
            expected.GenerationByteLength + expected.QualificationByteLength, "Peak admission also includes every source buffer and encoding workspace.");
        Same(expected, FormationGenerationIdentity.Create(catalog, settings, revisions, exact,
            maximumEstimatedBytes: expected.EstimatedPeakBytes));
        Reject<FormationCapacityException>(() => FormationGenerationIdentity.Create(catalog, settings, revisions, exact,
            maximumEstimatedBytes: expected.EstimatedPeakBytes - 1));
        Reject<FormationValidationException>(() => FormationGenerationIdentity.Create(catalog, settings, revisions, maximumEstimatedBytes: 0));
        Reject<FormationValidationException>(() => FormationGenerationIdentity.Create(catalog, settings, revisions, maximumEstimatedBytes: 1024L * 1024 * 1024 + 1));
        Reject<FormationValidationException>(() => FormationGenerationIdentity.Create(catalog, settings, revisions, 0));
        Reject<FormationValidationException>(() => FormationGenerationIdentity.Create(catalog, settings, revisions, 64 * 1024 * 1024 + 1));
        Reject<FormationValidationException>(() => Identity(catalog, revisions: revisions with { Planner = 0 }));
        Reject<FormationValidationException>(() => Identity(catalog, revisions: revisions with { RegionalContainment = 0 }));
        Reject<FormationValidationException>(() => Identity(catalog, revisions: revisions with { ConflictAdmission = 0 }));
        Reject<FormationValidationException>(() => Identity(catalog, revisions: revisions with { Qualification = 0 }));
        Reject<FormationValidationException>(() => Identity(catalog, Settings(new SettingsOptions { RecipeKey = "missing" })));
        Reject<ArgumentException>(() => Identity(Catalog(new FixtureOptions { GeometryKey = "bad-\uD800" })));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => FormationGenerationIdentity.Create(catalog, settings, revisions, cancellation: cancelled.Token));
    }

    private static FormationGenerationIdentity Identity(FormationCatalog catalog, FormationPlannerSettings? settings = null,
        FormationAlgorithmRevisions? revisions = null) => FormationGenerationIdentity.Create(catalog, settings ?? Settings(), revisions ?? new());
    private static FormationPlannerSettings Settings(SettingsOptions? o = null, FormationSurveyRegion? region = null)
    {
        o ??= new();
        return new(o.RecipeKey, o.RecipeRevision, o.TerrainProfileKey, o.GradientProbe, o.MinimumRiseRun, o.CrossStep,
            o.CrossSteps, o.Along, o.Down, o.Reach, o.Up, o.AnchorRole, region?.GridLevel - 1 ?? o.SurveyLevel, region);
    }
    private static FormationCatalog Catalog(FixtureOptions? o = null, ProfileOptions? profile = null)
    {
        o ??= new(); profile ??= new();
        CliffSupportDefinition a = Definition(o.GeometryKey, o), b = Definition("geometry-b", o);
        var definitions = new[] { a, b };
        var profiles = new[] { Profile(o.ProfileKey, profile), Profile("other-profile", profile) };
        var recipes = new[] { Recipe("recipe-a", 1, o.RecipeDomain, o), Recipe("recipe-a", 2, o.RecipeDomain, o), Recipe("recipe-b", 1, 53, o) };
        if (o.Permute) { Array.Reverse(definitions); Array.Reverse(profiles); Array.Reverse(recipes); }
        return new(definitions, profiles, recipes);
    }
    private static CliffSupportDefinition Definition(string key, FixtureOptions o)
    {
        double zero = o.SignedZero ? -0.0 : 0.0;
        var original = new[] { new Vector3D(zero, zero, zero), new Vector3D(0, 0, 2), new Vector3D(2, 0, 2), new Vector3D(2 + o.VertexOffset, 0, 0) };
        int Map(int old) => o.ReorderVertices ? (old + 3) % 4 : old;
        var vertices = new Vector3D[4];
        for (int i = 0; i < 4; i++) vertices[Map(i)] = original[i];
        int patchA = o.ReorderPatches ? 1 : 0, patchB = 1 - patchA;
        int[] triangleA = o.ReverseWinding ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };
        int[] triangleB = o.ReverseWinding ? new[] { 0, 3, 2 } : new[] { 0, 2, 3 };
        GeometryTriangle Triangle(int[] indices, int patch) => new(Map(indices[0]), Map(indices[1]), Map(indices[2]), patch);
        var triangles = new[] { Triangle(triangleA, patchA), Triangle(triangleB, patchB) };
        if (o.ReorderTriangles) Array.Reverse(triangles);
        var patches = new[] { new SupportPatch(o.PatchKey, o.ProfileKey, o.Capabilities), new SupportPatch("patch-b", o.ProfileKey, SupportCapabilities.Walkable) };
        if (o.ReorderPatches) Array.Reverse(patches);
        var geometry = new FormationGeometry(key, o.GeometryRevision, o.Family, vertices, triangles, patches);
        CliffPatchDomain Domain(string patch, int[] triangle)
        {
            int[] loop = triangle.Select(Map).ToArray();
            if (o.RotateLoops) loop = new[] { loop[1], loop[2], loop[0] };
            return new(patch, new[] { new CliffPatchBoundary(loop) });
        }
        var domains = new[] { Domain(o.PatchKey, triangleA), Domain("patch-b", triangleB) };
        var contacts = new[]
        {
            new CliffContactDomain(o.ContactKey, o.ContactRole, o.PatchKey,
                (o.ReverseContact ? new[] { 1, 0 } : new[] { 0, 1 }).Select(Map),
                o.ContactMinimumBurial, o.ContactMaximumBurial, o.ContactMaximumGap),
            new CliffContactDomain("contact-b", "toe", "patch-b", new[] { Map(2), Map(3) }, 0, 0.2, 0.1)
        };
        var visualTriangles = (GeometryTriangle[])triangles.Clone();
        if (o.ReverseVisualOrdinals) Array.Reverse(visualTriangles);
        var visual = new FormationGeometry(key + "-lod", o.GeometryRevision, o.Family,
            vertices.Select(p => p + Vector3D.UnitY * o.LodOffset), visualTriangles, patches);
        var maps = new[] { new CliffLodTriangleMap(0, o.ReverseVisualOrdinals ? 1 : 0), new CliffLodTriangleMap(1, o.ReverseVisualOrdinals ? 0 : 1) };
        if (o.Permute) Array.Reverse(maps);
        var lods = new[] { new CliffLodBinding(o.NearLodKey, o.RawDigest, visual, maps, o.LodError, o.LodNormal),
            new CliffLodBinding("far", o.RawDigest, visual, maps, o.LodError, o.LodNormal) };
        if (o.Permute) { Array.Reverse(domains); Array.Reverse(contacts); Array.Reverse(lods); }
        return new(geometry, o.RawDigest, o.RawDigest, o.DefinitionMinimumScale, o.DefinitionMaximumScale, domains, contacts, lods);
    }
    private static FormationRecipe Recipe(string key, int revision, ushort domain, FixtureOptions o)
    {
        string[] contacts = o.RequireSecondContact ? new[] { o.ContactKey, "contact-b" } : new[] { o.ContactKey };
        if (o.Permute) Array.Reverse(contacts);
        FormationGeometryChoice Choice(string geometry) => new(geometry, o.GeometryRevision, contacts, o.ChoiceMinimumScale, o.ChoiceMaximumScale);
        FormationGeometryChoice[] alternatives = o.IncludeSecondChoice ? new[] { Choice(o.GeometryKey), Choice("geometry-b") } : new[] { Choice(o.GeometryKey) };
        if (o.Permute) Array.Reverse(alternatives);
        var slots = new[] { new FormationRecipeSlot(o.Slot, o.SlotRole, o.SlotRequired, alternatives), new FormationRecipeSlot(9, "optional", false, alternatives) };
        if (o.Permute) Array.Reverse(slots);
        return new(key, revision, domain, o.Family, slots, o.RecipeSection, o.RecipeExtent, o.RecipeCoverage, o.RecipeGuideStations, o.RecipeFitProbes);
    }
    private static SubstrateProfile Profile(string key, ProfileOptions p) => new(key, p.Bedrock, p.LooseStone, p.Soil,
        p.Depth, p.Plant, p.Grass, p.Shrubs, p.Trees, p.Litter, p.MinimumTemperature, p.MaximumTemperature, p.MinimumUp);

    private sealed record FixtureOptions
    {
        public string GeometryKey { get; init; } = "geometry-a";
        public int GeometryRevision { get; init; } = 1;
        public string Family { get; init; } = "family";
        public string PatchKey { get; init; } = "patch-a";
        public string ProfileKey { get; init; } = "profile";
        public SupportCapabilities Capabilities { get; init; } = SupportCapabilities.Walkable;
        public double VertexOffset { get; init; }
        public bool ReorderVertices { get; init; }
        public bool ReorderTriangles { get; init; }
        public bool ReorderPatches { get; init; }
        public bool ReverseWinding { get; init; }
        public bool ReverseContact { get; init; }
        public bool SignedZero { get; init; }
        public bool Permute { get; init; }
        public bool RotateLoops { get; init; }
        public double DefinitionMinimumScale { get; init; } = 0.5;
        public double DefinitionMaximumScale { get; init; } = 2;
        public string ContactKey { get; init; } = "contact-a";
        public string ContactRole { get; init; } = "crest";
        public double ContactMinimumBurial { get; init; }
        public double ContactMaximumBurial { get; init; } = 0.2;
        public double ContactMaximumGap { get; init; } = 0.1;
        public ushort RecipeDomain { get; init; } = 17;
        public double RecipeSection { get; init; } = 12;
        public double RecipeExtent { get; init; } = 20;
        public double RecipeCoverage { get; init; } = 0.5;
        public int RecipeGuideStations { get; init; } = 3;
        public int RecipeFitProbes { get; init; } = 2;
        public int Slot { get; init; }
        public string SlotRole { get; init; } = "wall";
        public bool SlotRequired { get; init; } = true;
        public bool IncludeSecondChoice { get; init; } = true;
        public bool RequireSecondContact { get; init; } = true;
        public double ChoiceMinimumScale { get; init; } = 0.5;
        public double ChoiceMaximumScale { get; init; } = 2;
        public double LodOffset { get; init; }
        public double LodError { get; init; } = 0.2;
        public double LodNormal { get; init; } = 0.9;
        public bool ReverseVisualOrdinals { get; init; }
        public string NearLodKey { get; init; } = "near";
        public string RawDigest { get; init; } = new('0', 64);
    }
    private sealed record ProfileOptions
    {
        public double Bedrock { get; init; } = 0.2;
        public double LooseStone { get; init; } = 0.3;
        public double Soil { get; init; } = 0.5;
        public double Depth { get; init; } = 0.2;
        public double Plant { get; init; } = 0.8;
        public double Grass { get; init; } = 0.7;
        public double Shrubs { get; init; } = 0.03;
        public double Trees { get; init; } = 0.01;
        public double Litter { get; init; } = 0.2;
        public double MinimumTemperature { get; init; }
        public double MaximumTemperature { get; init; } = 40;
        public double MinimumUp { get; init; } = 0.6;
    }
    private sealed record SettingsOptions
    {
        public string RecipeKey { get; init; } = "recipe-a";
        public int RecipeRevision { get; init; } = 1;
        public string TerrainProfileKey { get; init; } = "ground";
        public int SurveyLevel { get; init; } = 3;
        public double GradientProbe { get; init; } = 2;
        public double MinimumRiseRun { get; init; } = 0.5;
        public double CrossStep { get; init; } = 10;
        public int CrossSteps { get; init; } = 12;
        public int Along { get; init; } = 2;
        public int Down { get; init; } = 2;
        public double Reach { get; init; } = 100;
        public Vector3D Up { get; init; } = Vector3D.UnitY;
        public string AnchorRole { get; init; } = "crest";
    }
    private static void PhysicalChanged(FormationGenerationIdentity a, FormationGenerationIdentity b)
    {
        Assert.True(a.PhysicalContentCanonical != b.PhysicalContentCanonical && a.PhysicalContentDigest != b.PhysicalContentDigest,
            "Changed consumed physical inputs change full canonical content and its fingerprint.");
        Assert.True(a.GenerationCanonical != b.GenerationCanonical && a.GenerationDigest != b.GenerationDigest,
            "Generation binds the full physical-content contract.");
    }
    private static void Same(FormationGenerationIdentity a, FormationGenerationIdentity b)
    {
        Assert.Equal(a.PhysicalContentCanonical, b.PhysicalContentCanonical, "Canonical physical content agrees.");
        Assert.Equal(a.PhysicalContentDigest, b.PhysicalContentDigest, "Physical digests agree.");
        Assert.Equal(a.GenerationCanonical, b.GenerationCanonical, "Canonical generation inputs agree.");
        Assert.Equal(a.GenerationDigest, b.GenerationDigest, "Generation digests agree.");
        Assert.Equal(a.QualificationCanonical, b.QualificationCanonical, "Canonical qualification evidence agrees.");
        Assert.Equal(a.QualificationDigest, b.QualificationDigest, "Qualification digests agree.");
    }
    private static void AssertDigest(string canonical, string digest) =>
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(canonical))), digest, "Digest hashes the complete exported canonical bytes.");
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
