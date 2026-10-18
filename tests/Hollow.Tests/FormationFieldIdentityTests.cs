using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationFieldIdentityTests
{
    private static readonly string TerrainIdentity = new('A', 64);
    private const string TerrainContract = "walking-finalized-grading-raw-fields-sites-v1";

    [Test]
    public static void FormationFieldIdentityPreservesAllPriorCanonicalBytesWhenExtensionsAreAbsent()
    {
        FormationGenerationIdentity actual = Identity(Catalog(loops: false));
        (byte[] physical, byte[] generation, byte[] qualification) = PriorFixtureDocuments();
        Assert.True(Convert.FromBase64String(actual.PhysicalContentCanonical).AsSpan().SequenceEqual(physical),
            "legacy physical bytes match the independently specified pre-field fixture format");
        Assert.True(Convert.FromBase64String(actual.GenerationCanonical).AsSpan().SequenceEqual(generation),
            "legacy generation bytes omit any empty closed-loop or field extension");
        Assert.True(Convert.FromBase64String(actual.QualificationCanonical).AsSpan().SequenceEqual(qualification),
            "legacy qualification bytes omit empty LOD loop metadata");
        FormationWorldSelection selection = Select(Catalog(loops: false));
        Assert.Equal(actual.GenerationCanonical, selection.Identity.GenerationCanonical, "an omitted selected field contract preserves the prior encoding");
        Assert.True(selection.FieldContract == null, "legacy selection has no implicit field policy");
    }

    [Test]
    public static void FormationFieldIdentityBindsClosedLoopPresenceKeyAndOrderedPhysicalIndices()
    {
        FormationGenerationIdentity original = Identity(Catalog());
        foreach (FormationCatalog changed in new[] { Catalog(loops: false), Catalog(loopKey: "other-ring"), Catalog(reverse: true) })
        {
            FormationGenerationIdentity identity = Identity(changed);
            Assert.True(original.PhysicalContentCanonical != identity.PhysicalContentCanonical, "closed loop changes affect physical canonical content");
            Assert.True(original.GenerationDigest != identity.GenerationDigest, "generation binds the complete changed physical contract");
            Assert.True(original.QualificationDigest != identity.QualificationDigest, "qualification binds the changed physical owner");
        }
        string text = Encoding.UTF8.GetString(Convert.FromBase64String(original.PhysicalContentCanonical));
        Assert.True(text.Contains("ClosedContactLoops", StringComparison.Ordinal) && text.Contains("implicit-last-to-first", StringComparison.Ordinal),
            "physical encoding binds explicit closed-loop semantics");
    }

    [Test]
    public static void FormationFieldIdentityKeepsExactLodLoopOrdinalsInQualificationOnly()
    {
        // Both LOD meshes contain two identical triangle copies. Only the authored binding chooses another copy.
        FormationGenerationIdentity a = Identity(Catalog()), b = Identity(Catalog(alternateVisualBinding: true));
        Assert.Equal(a.PhysicalContentCanonical, b.PhysicalContentCanonical, "visual loop ordinals do not change physical geometry or loops");
        Assert.Equal(a.GenerationCanonical, b.GenerationCanonical, "visual binding changes do not invalidate physical saves");
        Assert.True(a.QualificationCanonical != b.QualificationCanonical && a.QualificationDigest != b.QualificationDigest,
            "distinct exact LOD bindings cannot alias the same admitted render resource");
    }

    [Test]
    public static void FormationFieldIdentityBindsExplicitSeamEdgesOnlyToQualification()
    {
        FormationGenerationIdentity plain = Identity(Catalog()), first = Identity(Catalog(explicitEdges: true)),
            alternate = Identity(Catalog(explicitEdges: true, alternateEdges: true));
        Assert.Equal(plain.GenerationCanonical, first.GenerationCanonical, "visual endpoint evidence does not alter physical placement");
        Assert.Equal(first.PhysicalContentCanonical, alternate.PhysicalContentCanonical, "equal-coordinate edge copies do not alter physics");
        Assert.True(plain.QualificationCanonical != first.QualificationCanonical && first.QualificationCanonical != alternate.QualificationCanonical,
            "presence and exact authored endpoint ordinals both bind qualification");
    }

    [Test]
    public static void FormationFieldIdentityBindsTerrainLatticeAndAdmissionOnlyToGeneration()
    {
        FormationCatalog catalog = Catalog();
        FormationGenerationIdentity baseline = Identity(catalog, Contract());
        foreach (FormationFieldContract changed in new[]
        {
            Contract(terrain: new string('B', 64)), Contract(radius: 1300), Contract(level: 5),
            Contract(policy: FormationFieldAdmissionPolicy.WholePlannedFormation),
        })
        {
            FormationGenerationIdentity identity = Identity(catalog, changed);
            Assert.Equal(baseline.PhysicalContentCanonical, identity.PhysicalContentCanonical, "field policy leaves authored physical inputs unchanged");
            Assert.Equal(baseline.QualificationCanonical, identity.QualificationCanonical, "terrain field policy leaves LOD qualification unchanged");
            Assert.True(baseline.GenerationCanonical != identity.GenerationCanonical && baseline.GenerationDigest != identity.GenerationDigest,
                "every terrain, lattice and accepted-output policy change fences generated field compatibility");
        }
        FormationGenerationIdentity absent = Identity(catalog);
        Assert.True(baseline.GenerationDigest != absent.GenerationDigest, "a sealed field cannot alias a selection without its field policy");
        Assert.Equal(baseline.GenerationCanonical, Identity(catalog, Contract(terrain: TerrainIdentity.ToLowerInvariant())).GenerationCanonical,
            "terrain digest spelling is canonical");
        string generation = Encoding.UTF8.GetString(Convert.FromBase64String(baseline.GenerationCanonical));
        foreach (string policy in new[] { "sealed-formation-field-v1", "segment-shadow-one-cell-guard-outward-radial-cull-v1",
            "witness-two-edge-rings-incident-finest-tiles-v1", "canonical-cube-terrain-lattice-v1", "fixed-radial-terrain-loop-seal-l1-v1",
            "MinimumPlaneBurialMetres", "GuardRings", "TerrainIdentity" })
            Assert.True(generation.Contains(policy, StringComparison.Ordinal), "generation records field policy: " + policy);
        Assert.Equal(0.05, FormationFieldContract.MinimumPlaneBurialMetres, "seal offset is the explicit five-centimetre world-space policy");
        Assert.Equal(2, FormationFieldContract.GuardRings, "witness dependency guard uses the declared two rings");
    }

    [Test]
    public static void FormationFieldSelectionRetainsItsContractWithoutChangingProceduralSeeds()
    {
        FormationCatalog catalog = Catalog();
        FormationFieldContract contract = Contract();
        FormationWorldSelection plain = Select(catalog), selected = Select(catalog, contract);
        Assert.True(ReferenceEquals(contract, selected.FieldContract), "selection retains the exact immutable field contract");
        Assert.Equal(Identity(catalog, contract).GenerationCanonical, selected.Identity.GenerationCanonical, "selection uses the full field-aware canonical identity");
        Assert.Equal(1024L + selected.Identity.EstimatedRetainedBytes - plain.Identity.EstimatedRetainedBytes,
            selected.EstimatedResidentBytes - plain.EstimatedResidentBytes, "selected input admission accounts for field policy and extended canonical strings");
        PlanetEnvironmentDescriptor a = Earth(selected);
        PlanetEnvironmentDescriptor b = Earth(Select(catalog, Contract(terrain: new string('B', 64), policy: FormationFieldAdmissionPolicy.WholePlannedFormation)));
        PlanetEnvironmentDescriptor legacy = Earth(null);
        Assert.True(a.CanonicalIdentity != b.CanonicalIdentity, "descriptor identity includes the selected field contract");
        Assert.Equal(a.SurfaceSeed, b.SurfaceSeed, "field policy is never hashed into the surface seed");
        Assert.Equal(a.TerrainSeed, b.TerrainSeed, "field policy is never hashed into the terrain seed");
        Assert.Equal(legacy.SurfaceSeed, a.SurfaceSeed, "explicit formation selection preserves the default surface seed");
        Assert.Equal(legacy.TerrainSeed, a.TerrainSeed, "explicit formation selection preserves the default terrain seed");
    }

    [Test]
    public static void FormationFieldIdentityAdmitsExtendedBytesAndRejectsMalformedContracts()
    {
        FormationCatalog catalog = Catalog(); FormationFieldContract contract = Contract();
        FormationGenerationIdentity expected = Identity(catalog, contract);
        int exact = Math.Max(expected.PhysicalContentByteLength, Math.Max(expected.GenerationByteLength, expected.QualificationByteLength));
        FormationGenerationIdentity bounded = FormationGenerationIdentity.Create(catalog, Settings(), new(), exact,
            maximumEstimatedBytes: expected.EstimatedPeakBytes, fieldContract: contract);
        Assert.Equal(expected.GenerationCanonical, bounded.GenerationCanonical, "operational identity capacity does not alter successful field inputs");
        Reject<FormationCapacityException>(() => FormationGenerationIdentity.Create(catalog, Settings(), new(), exact - 1, fieldContract: contract));
        Reject<FormationCapacityException>(() => FormationGenerationIdentity.Create(catalog, Settings(), new(), exact,
            maximumEstimatedBytes: expected.EstimatedPeakBytes - 1, fieldContract: contract));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => FormationGenerationIdentity.Create(catalog, Settings(), new(), cancellation: cancelled.Token, fieldContract: contract));
        Reject<FormationValidationException>(() => Contract(terrain: new string('g', 64)));
        Reject<FormationValidationException>(() => Contract(policy: (FormationFieldAdmissionPolicy)99));
        Reject<FormationValidationException>(() => Contract(radius: FormationTerrainSeal.MaximumCoordinateMetres + 1));
        Reject<ArgumentException>(() => Contract(radius: 0));
        Reject<ArgumentException>(() => Contract(radius: double.NaN));
        Reject<ArgumentException>(() => Contract(level: -1));
        Reject<ArgumentException>(() => Contract(level: TileKey.MaxLevel));
    }

    private static FormationFieldContract Contract(string? terrain = null, double radius = 1200, int level = 6,
        FormationFieldAdmissionPolicy policy = FormationFieldAdmissionPolicy.IndependentPieces) => new(terrain ?? TerrainIdentity, radius, level, policy);
    private static FormationGenerationIdentity Identity(FormationCatalog catalog, FormationFieldContract? contract = null) =>
        FormationGenerationIdentity.Create(catalog, Settings(), new(), fieldContract: contract);
    private static FormationWorldSelection Select(FormationCatalog catalog, FormationFieldContract? contract = null) =>
        FormationWorldSelection.Create("formation-field-test", TerrainContract, catalog, Settings(), fieldContract: contract);
    private static PlanetEnvironmentDescriptor Earth(FormationWorldSelection? selection) =>
        EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath, 1200, formationSelection: selection)
            ?? throw new TestFailure("Earth descriptor is required.");
    private static FormationPlannerSettings Settings() => new("recipe", 1, "raw-test", 1, 0.5, 1, 2, 1, 1, 2, Vector3D.UnitY, "toe", surveyLevel: 0);
    private static Vector3D[] Points() => new[] { Vector3D.Zero, new Vector3D(0, 0, 2), new Vector3D(2, 0, 0) };
    private static FormationCatalog Catalog(bool loops = true, string loopKey = "ring", bool reverse = false, bool alternateVisualBinding = false,
        bool explicitEdges = false, bool alternateEdges = false)
    {
        string digest = new('A', 64);
        var patches = new[] { new SupportPatch("top", "stone", SupportCapabilities.None) };
        var physical = new FormationGeometry("stone", 1, "test-family", Points(), new[] { new GeometryTriangle(0, 1, 2, 0) }, patches);
        var visual = new FormationGeometry("visual", 1, "test-family", Points().Concat(Points()),
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(3, 4, 5, 0) }, patches);
        int[] indices = reverse ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };
        FormationContactLoopBinding[] bindings = loops
            ? new[] { new FormationContactLoopBinding(loopKey, indices.Select(i => i + (alternateVisualBinding ? 3 : 0)),
                explicitEdges ? new[] { 0, 1, 1, 2, 2, 0 }.Select(i => i + (alternateEdges ? 3 : 0)) : null) }
            : Array.Empty<FormationContactLoopBinding>();
        var definition = new CliffSupportDefinition(physical, digest, digest, 1, 1,
            new[] { new CliffPatchDomain("top", new[] { new CliffPatchBoundary(new[] { 0, 1, 2 }) }) },
            new[] { new CliffContactDomain("toe", "toe", "top", new[] { 0, 1 }, 0, 1, 1) },
            new[] { new CliffLodBinding("near", digest, visual, Array.Empty<CliffLodTriangleMap>(), 0, 1, bindings),
                new CliffLodBinding("far", digest, visual, Array.Empty<CliffLodTriangleMap>(), 0, 1, bindings) },
            contactLoops: loops ? new[] { new FormationContactLoop(loopKey, indices) } : Array.Empty<FormationContactLoop>());
        var profile = new SubstrateProfile("stone", 1, 0, 0, 0, 0, 0, 0, 0);
        var recipe = new FormationRecipe("recipe", 1, 17, "test-family", new[] { new FormationRecipeSlot(0, "crest", true,
            new[] { new FormationGeometryChoice("stone", 1, new[] { "toe" }, 1, 1) }) }, 10, 20, 0.5, 2, 1);
        return new FormationCatalog(new[] { definition }, new[] { profile }, new[] { recipe });
    }

    // Independent, fixed-fixture specification of the pre-field schema, read from the field-r1 source snapshot.
    // These bytes are written from literals, never from current identity helpers, reflected production fields,
    // current implementation version constants, or the generated result. No runtime artifact file is required.
    private static (byte[] Physical, byte[] Generation, byte[] Qualification) PriorFixtureDocuments()
    {
        byte[] physical = Document(w =>
        {
            Header(w, "Hollow.Formation.PhysicalContent"); ArrayHeader(w, "Qualified", 1); Geometry(w, "stone", false);
            Record(w, "QualifiedScales", ("MaximumScale", 1.0), ("MinimumScale", 1.0));
            ArrayHeader(w, "PatchDomains", 1); Record(w, "PatchDomain", ("PatchKey", "top"));
            ArrayHeader(w, "Boundaries", 1); Indices(w, 0, 1, 2);
            ArrayHeader(w, "Contacts", 1); Record(w, "Contact", ("Key", "toe"), ("MaximumBurialMetres", 1.0),
                ("MaximumGapMetres", 1.0), ("MinimumBurialMetres", 0.0), ("PatchKey", "top"), ("Role", "toe")); Indices(w, 0, 1);
            ArrayHeader(w, "Profiles", 1); Record(w, "SubstrateProfile", ("Bedrock", 1.0), ("GrassCoverage", 0.0), ("Key", "stone"),
                ("Litter", 0.0), ("LooseStone", 0.0), ("MaximumTemperatureC", 40.0), ("MinimumSoilUpDot", 0.6),
                ("MinimumTemperatureC", 0.0), ("PlantSuitability", 0.0), ("ShrubsPerSquareMetre", 0.0), ("Soil", 0.0),
                ("SoilDepthMetres", 0.0), ("TreesPerSquareMetre", 0.0));
            ArrayHeader(w, "Recipes", 1); Record(w, "Recipe", ("FamilyKey", "test-family"), ("IdentityDomain", 17u), ("Key", "recipe"),
                ("MaximumExtentMetres", 20.0), ("MaximumFitProbes", 1), ("MaximumGuideStations", 2),
                ("MaximumSectionLengthMetres", 10.0), ("MinimumCoverage", 0.5), ("Revision", 1));
            ArrayHeader(w, "Slots", 1); Record(w, "Slot", ("Required", true), ("Role", "crest"), ("Slot", 0));
            ArrayHeader(w, "Alternatives", 1); Record(w, "Choice", ("Key", "stone"), ("MaximumScale", 1.0), ("MinimumScale", 1.0), ("Revision", 1));
            ArrayHeader(w, "ContactKeys", 1); Value(w, "toe");
        });
        byte[] generation = Document(w =>
        {
            Header(w, "Hollow.Formation.Generation"); w.Write(physical.Length); w.Write(physical);
            Record(w, "SimulationAlgorithms", ("ConflictAdmission", 1), ("Planner", 2), ("RegionalContainment", 2), ("Substrate", 1),
                ("Support", 1), ("SurfaceChart", 1), ("Topology", 1), ("Transitions", 1));
            Record(w, "ImplementedPlannerPolicies", ("ConflictPolicy", "independent-proposal-lower-id-v1"),
                ("ExtentPolicy", "outward-coordinate-spans-plus-diagonal-v1"), ("GenerationRevision", 2),
                ("NeighborEvidencePolicy", "outward-physical-aabb-plus-extent-two-edge-ring-shadow-v1"),
                ("OwnershipPolicy", "canonical-contour-core-publication-only-v1"),
                ("RegionalEvidencePolicy", "all-survey-components-and-fits-complete-or-refuse-v1"),
                ("SourceAdmissionPolicy", "paired-last-crest-source-segment-shadow-v1"),
                ("SourceAdmissionScope", "whole-and-regional-v1"), ("SourceShadowEdgeRings", 2));
            Record(w, "PlannerSettings", ("AnchorContactRole", "toe"), ("CoverageAlong", 1), ("CoverageDown", 1), ("CoverageReachMetres", 2.0),
                ("CrossSectionStepMetres", 1.0), ("CrossSectionSteps", 2), ("GeometryUp", new Vector3D(0, 1, 0)), ("GradientProbeMetres", 1.0),
                ("MinimumRiseRun", 0.5), ("RecipeKey", "recipe"), ("RecipeRevision", 1), ("SurveyLevel", 0), ("TerrainProfileKey", "raw-test"));
            w.Write(false);
        });
        byte[] qualification = Document(w =>
        {
            Header(w, "Hollow.Formation.Qualification"); w.Write(physical.Length); w.Write(physical);
            Record(w, "QualificationAlgorithms", ("Qualification", 1)); ArrayHeader(w, "QualifiedLods", 1);
            Record(w, "QualifiedOwner", ("Key", "stone"), ("Revision", 1)); ArrayHeader(w, "Lods", 2);
            foreach (string key in new[] { "far", "near" })
            {
                Record(w, "Lod", ("Key", key), ("MaximumPositionErrorMetres", 0.0), ("MinimumNormalDot", 1.0));
                Geometry(w, "visual", true); ArrayHeader(w, "TriangleMaps", 0);
            }
        });
        return (physical, generation, qualification);

        static void Geometry(BinaryWriter w, string key, bool duplicate)
        {
            Record(w, "Geometry", ("FamilyKey", "test-family"), ("Key", key), ("Revision", 1));
            ArrayHeader(w, "Vertices", duplicate ? 6 : 3);
            for (int copy = 0; copy < (duplicate ? 2 : 1); copy++)
            { Value(w, new Vector3D(0, 0, 0)); Value(w, new Vector3D(0, 0, 2)); Value(w, new Vector3D(2, 0, 0)); }
            ArrayHeader(w, "Triangles", duplicate ? 2 : 1); w.Write(0); w.Write(1); w.Write(2); w.Write(0);
            if (duplicate) { w.Write(3); w.Write(4); w.Write(5); w.Write(0); }
            ArrayHeader(w, "Patches", 1); Record(w, "Patch", ("Capabilities", 0u), ("Key", "top"), ("SubstrateProfileKey", "stone"));
        }
    }
    private static byte[] Document(Action<BinaryWriter> write)
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, new UTF8Encoding(false, true), leaveOpen: true)) write(writer);
        return memory.ToArray();
    }
    private static void Header(BinaryWriter writer, string domain) { writer.Write(domain); writer.Write(1); }
    private static void ArrayHeader(BinaryWriter writer, string name, int length) { writer.Write((byte)11); writer.Write(name); writer.Write(length); }
    private static void Indices(BinaryWriter writer, params int[] indices)
    { ArrayHeader(writer, "Vertices", indices.Length); foreach (int index in indices) writer.Write(index); }
    private static void Record(BinaryWriter writer, string type, params (string Name, object Value)[] fields)
    {
        writer.Write((byte)10); writer.Write(type); writer.Write(fields.Length);
        foreach (var field in fields.OrderBy(field => field.Name, StringComparer.Ordinal)) { writer.Write(field.Name); Value(writer, field.Value); }
    }
    private static void Value(BinaryWriter writer, object value)
    {
        switch (value)
        {
            case bool n: writer.Write((byte)1); writer.Write(n); break;
            case int n: writer.Write((byte)2); writer.Write(n); break;
            case uint n: writer.Write((byte)3); writer.Write(n); break;
            case double n: writer.Write((byte)5); writer.Write(n); break;
            case string n: writer.Write((byte)7); writer.Write(n); break;
            case Vector3D n: writer.Write((byte)9); Value(writer, n.X); Value(writer, n.Y); Value(writer, n.Z); break;
            default: throw new TestFailure("Unsupported prior-fixture literal.");
        }
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
