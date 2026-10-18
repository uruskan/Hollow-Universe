using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class CliffSupportQualificationTests
{
    // These are synthetic receipt-shaped tokens. File-byte authentication belongs to the external loader.
    private static readonly string SourceDigest = new('a', 64), PhysicalDigest = new('b', 64), LodDigest = new('c', 64);

    [Test]
    public static void CliffSupportQualificationPreservesExactPatchAndContactIdentity()
    {
        CliffSupportDefinition definition = Qualified();
        Assert.Equal(SourceDigest.ToUpperInvariant(), definition.SourceSha256, "digest spelling is canonical");
        Assert.Equal(PhysicalDigest.ToUpperInvariant(), definition.PhysicalSha256, "physical receipt token is retained");
        Assert.Equal("shelf", definition.RequirePatch("shelf").PatchKey, "authored patch is addressed by key");
        Assert.True(definition.RequirePatch("shelf").Boundaries[0].VertexIndices.SequenceEqual(new[] { 0, 1, 2, 3 }),
            "exact directed boundary survives qualification");
        Assert.Equal("shelf", definition.RequireContact("toe").PatchKey, "contact names its physical patch");
        Assert.Equal(2, definition.Lods.Length, "both explicit visual LODs qualify");
        Assert.Equal("far", definition.Lods[0].Key, "LOD order is canonical");
        var piece = new FormationPiece(new FormationPieceId(new FormationId(17, 1), 2), "crown", definition.Geometry, FormationTransform.Identity);
        FormationRayResult hit = FormationSupportQueries.Raycast(new FormationSupportIndex(new[] { piece }),
            new FormationRayRequest(new Vector3D(-1, 1, 0), -Vector3D.UnitY, 2, RequiredCapabilities: SupportCapabilities.Soil));
        Assert.Equal(FormationQueryStatus.Complete, hit.Status, "bounded support query completes");
        Assert.Equal(1, hit.Hits.Length, "fixture hits one triangle away from the diagonal");
        Assert.Equal("shelf", hit.Hits[0].PatchKey, "support uses the qualified triangle patch");
        Assert.Equal("soil", hit.Hits[0].SubstrateProfileKey, "support preserves the intrinsic profile reference");
        Assert.Equal(piece.PrimitiveId(0), hit.Hits[0].Primitive, "support retains the physical triangle identity");
        Reject<FormationValidationException>(() => definition.RequirePatch("missing"));
        Reject<FormationValidationException>(() => definition.RequireContact("missing"));
    }

    [Test]
    public static void CliffSupportBoundariesMustCoverEveryActualPatchEdgeWithWinding()
    {
        FormationGeometry mesh = Mesh();
        foreach (CliffPatchDomain invalid in new[]
        {
            new CliffPatchDomain("shelf", Array.Empty<CliffPatchBoundary>()),
            Domain(new[] { 0, 3, 2, 1 }), // Reversed loop.
            Domain(new[] { 0, 1, 2 }), // Internal diagonal substitutes for two real edges.
            Domain(new[] { 0, 1, 2, 99 }),
            new CliffPatchDomain("unknown", new[] { new CliffPatchBoundary(new[] { 0, 1, 2, 3 }) }),
            new CliffPatchDomain("shelf", new[] { new CliffPatchBoundary(new[] { 0, 1, 2, 3 }), new CliffPatchBoundary(new[] { 0, 1, 2, 3 }) }),
        }) Reject<FormationValidationException>(() => Definition(mesh, patches: new[] { invalid }));
        Reject<FormationValidationException>(() => new CliffPatchBoundary(new[] { 0, 1, 2, 0 }));
        Reject<FormationValidationException>(() => new CliffPatchBoundary(new[] { 0, 1 }));
        Reject<FormationValidationException>(() => Definition(mesh, patches: Array.Empty<CliffPatchDomain>()));
        var split = new FormationGeometry("split", 1, "rock", Vertices(),
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 1) },
            new[] { Patch(), new SupportPatch("bare", "soil", SupportCapabilities.None) });
        var domains = new[] { Domain(new[] { 0, 1, 2 }), new CliffPatchDomain("bare", new[] { new CliffPatchBoundary(new[] { 0, 2, 3 }) }) };
        CliffSupportDefinition qualified = Definition(split, patches: domains);
        Assert.Equal(2, qualified.Patches.Length, "physical patches have separate exact domains even where they share an edge");
        Reject<FormationValidationException>(() => Definition(split, patches: new[] { domains[0], domains[0] }));
    }

    [Test]
    public static void CliffSupportHoleBoundariesCannotBeOmittedOrTurnedIntoSupport()
    {
        Vector3D[] vertices = Vertices().Concat(new[] { new Vector3D(-1, 0, -1), new Vector3D(-1, 0, 1),
            new Vector3D(1, 0, 1), new Vector3D(1, 0, -1) }).ToArray();
        GeometryTriangle[] triangles = { new(0, 1, 5, 0), new(0, 5, 4, 0), new(1, 2, 6, 0), new(1, 6, 5, 0),
            new(2, 3, 7, 0), new(2, 7, 6, 0), new(3, 0, 4, 0), new(3, 4, 7, 0) };
        var ring = new FormationGeometry("ring", 1, "rock", vertices, triangles, new[] { Patch() });
        var outer = new CliffPatchBoundary(new[] { 0, 1, 2, 3 });
        var inner = new CliffPatchBoundary(new[] { 4, 7, 6, 5 });
        CliffSupportDefinition definition = Definition(ring, patches: new[] { new CliffPatchDomain("shelf", new[] { outer, inner }) });
        var piece = new FormationPiece(new FormationPieceId(new FormationId(17, 1), 2), "crown", definition.Geometry, FormationTransform.Identity);
        var index = new FormationSupportIndex(new[] { piece });
        FormationRayResult missing = FormationSupportQueries.Raycast(index, new FormationRayRequest(new Vector3D(0, 1, 0), -Vector3D.UnitY, 2));
        Assert.Equal(0, missing.Hits.Length, "qualified boundary holes retain their missing geometry");
        FormationRayResult present = FormationSupportQueries.Raycast(index, new FormationRayRequest(new Vector3D(-1.5, 1, 0), -Vector3D.UnitY, 2));
        Assert.True(present.Hits.Length > 0, "the surrounding qualified shelf is real");
        Reject<FormationValidationException>(() => Definition(ring, patches: new[] { new CliffPatchDomain("shelf", new[] { outer }) }));
        Reject<FormationValidationException>(() => Definition(ring, patches: new[] { new CliffPatchDomain("shelf",
            new[] { outer, new CliffPatchBoundary(new[] { 4, 5, 6, 7 }) }) }));
    }

    [Test]
    public static void CliffContactsFollowNamedBoundaryChainsAndFiniteIntervals()
    {
        FormationGeometry mesh = Mesh();
        foreach (CliffContactDomain invalid in new[]
        {
            Contact(vertices: new[] { 0, 2 }), // The interior diagonal is not a contact boundary.
            Contact(vertices: new[] { 0, 99 }), Contact(patch: "missing"),
        }) Reject<FormationValidationException>(() => Definition(mesh, contacts: new[] { invalid }));
        Reject<FormationValidationException>(() => Definition(mesh, contacts: new[] { Contact(), Contact() }));
        Reject<FormationValidationException>(() => Contact(vertices: new[] { 0, 1, 0 }));
        Reject<FormationValidationException>(() => new CliffContactDomain("toe", "base", "shelf", new[] { 0, 1 }, 1, 0.5, 0.1));
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.1 })
            Reject<FormationValidationException>(() => new CliffContactDomain("toe", "base", "shelf", new[] { 0, 1 }, 0, 0.2, invalid));
        CliffSupportDefinition reverse = Definition(mesh, contacts: new[] { Contact(vertices: new[] { 2, 1, 0 }) });
        Assert.True(reverse.RequireContact("toe").VertexIndices.SequenceEqual(new[] { 2, 1, 0 }),
            "contact traversal can run either way along actual boundary edges");
    }

    [Test]
    public static void CliffLodMappingsCoverSupportAndPreserveCornerAndNormalCorrespondence()
    {
        FormationGeometry mesh = Mesh();
        var complete = Maps(mesh);
        var partial = new[] { new CliffLodTriangleMap(0, 0) };
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, complete) }));
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, complete), Lod("far", mesh, partial) }));
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, complete), Lod("near", mesh, complete) }));
        Reject<FormationValidationException>(() => Lod("far", mesh, new[] { new CliffLodTriangleMap(0, 0), new CliffLodTriangleMap(0, 1) }));
        Reject<FormationValidationException>(() => Lod("far", mesh, new[] { new CliffLodTriangleMap(0, 0), new CliffLodTriangleMap(1, 0) }));
        Reject<FormationValidationException>(() => Lod("far", mesh, new[] { new CliffLodTriangleMap(0, 99) }));
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, complete),
            Lod("far", mesh, new[] { new CliffLodTriangleMap(99, 0) }) }));
        FormationGeometry reverse = Mesh(reverse: true);
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, complete),
            Lod("far", reverse, complete, error: 20, normalDot: 0) }));
        var swapped = new[] { new CliffLodTriangleMap(0, 1), new CliffLodTriangleMap(1, 0) };
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, complete), Lod("far", mesh, swapped) }));
    }

    [Test]
    public static void CliffLodDeviationIsBoundedAtMaximumScaleForEveryCorner()
    {
        FormationGeometry mesh = Mesh(), shifted = Mesh(offset: new Vector3D(0, 0.125, 0));
        CliffLodTriangleMap[] mappings = Maps(mesh);
        CliffSupportDefinition boundary = Definition(mesh, lods: new[] { Lod("near", mesh, mappings),
            Lod("far", shifted, mappings, error: 0.25) });
        Assert.Equal(2.0, boundary.MaximumScale, "fixture reaches the declared maximum scale");
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, mappings),
            Lod("far", shifted, mappings, error: 0.249) }));
        // Only one corner moves: a centroid test could dilute this error by a factor of three.
        Vector3D[] changed = Vertices(); changed[0] += new Vector3D(0, 0.125, 0);
        var tipped = new FormationGeometry("visual", 1, "rock", changed, mesh.Triangles.ToArray(), new[] { Patch() });
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh, mappings),
            Lod("far", tipped, mappings, error: 0.1, normalDot: 0) }));
    }

    [Test]
    public static void CliffQualificationCopiesNestedInputsAndRejectsMalformedReceiptTokens()
    {
        FormationGeometry mesh = Mesh();
        int[] boundary = { 0, 1, 2, 3 }, contact = { 0, 1 };
        CliffPatchBoundary[] loops = { new(boundary) };
        CliffPatchDomain[] patches = { new("shelf", loops) };
        CliffContactDomain[] contacts = { Contact(vertices: contact) };
        CliffLodTriangleMap[] maps = Maps(mesh);
        CliffLodBinding[] lods = { Lod("near", mesh, maps), Lod("far", mesh, maps) };
        CliffSupportDefinition definition = Definition(mesh, patches, contacts, lods);
        boundary[0] = 99; contact[0] = 99; loops[0] = new CliffPatchBoundary(new[] { 7, 8, 9 });
        patches[0] = new CliffPatchDomain("wrong", loops); contacts[0] = Contact(patch: "wrong");
        maps[0] = new CliffLodTriangleMap(99, 99); lods[0] = Lod("wrong", mesh, Maps(mesh));
        Assert.Equal(0, definition.RequirePatch("shelf").Boundaries[0].VertexIndices[0], "nested boundary storage is copied");
        Assert.Equal(0, definition.RequireContact("toe").VertexIndices[0], "contact vertices are copied");
        Assert.Equal(0, definition.Lods[0].TriangleMaps[0].PhysicalTriangle, "LOD mappings are copied");
        foreach (string invalid in new[] { "", new string('a', 63), new string('g', 64), " " + new string('a', 63) })
        {
            Reject<FormationValidationException>(() => Definition(mesh, source: invalid));
            Reject<FormationValidationException>(() => Definition(mesh, physical: invalid));
            Reject<FormationValidationException>(() => new CliffLodBinding("near", invalid, mesh, Maps(mesh), 0, 1));
        }
        foreach (Type type in new[] { typeof(CliffSupportDefinition), typeof(CliffPatchDomain), typeof(CliffPatchBoundary), typeof(CliffContactDomain), typeof(CliffLodBinding) })
            Assert.True(type.GetProperties().All(p => p.SetMethod == null), "published qualification inputs are get-only: " + type.Name);
    }

    [Test]
    public static void CliffQualificationBudgetsAndRecipeShapeFailuresAreExplicit()
    {
        FormationGeometry mesh = Mesh();
        Reject<FormationCapacityException>(() => Definition(mesh, limits: new CliffSupportLimits { MaxBoundaryVertices = 3 }));
        Reject<FormationCapacityException>(() => Definition(mesh, limits: new CliffSupportLimits { MaxMappedTriangles = 3 }));
        Reject<FormationCapacityException>(() => Definition(mesh, limits: new CliffSupportLimits { MaxEstimatedResidentBytes = 0 }));
        Reject<FormationCapacityException>(() => Definition(mesh, limits: new CliffSupportLimits { MaxEstimatedScratchBytes = 0 }));
        Reject<FormationValidationException>(() => Definition(mesh, limits: new CliffSupportLimits { MaxLods = 1 }));
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1.0, 0.0 })
            Reject<FormationValidationException>(() => Definition(mesh, minimumScale: invalid));
        var choice = new FormationGeometryChoice("shelf", 1, new[] { "toe" }, 0.5, 2);
        var slot = new FormationRecipeSlot(0, "crown", true, new[] { choice });
        Reject<FormationValidationException>(() => new FormationGeometryChoice("shelf", 1, new[] { "toe", "toe" }, 0.5, 2));
        Reject<FormationValidationException>(() => new FormationGeometryChoice("shelf", 1, Array.Empty<string>(), 0.5, 2));
        Reject<FormationValidationException>(() => new FormationRecipeSlot(0, "crown", true, new[] { choice, choice }));
        Reject<FormationValidationException>(() => new FormationRecipeSlot(-1, "crown", true, new[] { choice }));
        Reject<FormationValidationException>(() => new FormationRecipe("bad", 1, 3, "rock", new[] { slot, slot }, 12, 20, 0.5));
        Reject<FormationValidationException>(() => new FormationRecipe("bad", 1, 3, "rock", new[] { slot }, 12, 20, double.NaN));
        Reject<FormationValidationException>(() => new FormationRecipe("bad", 1, 3, "rock", new[] { slot }, 12, 20, 0.5, maximumFitProbes: 0));
        Reject<FormationValidationException>(() => new FormationRecipe("bad", 1, 3, "rock", new[] { slot }, 12, 20, 0.5, maximumGuideStations: 1));
        Reject<FormationCapacityException>(() => new FormationRecipeSlot(0, "crown", true,
            Enumerable.Range(0, 6).Select(i => new FormationGeometryChoice("g" + i, 1, new[] { "toe" }, 0.5, 2))));
    }

    [Test]
    public static void FormationCatalogUsesCanonicalExactRevisionLookups()
    {
        CliffSupportDefinition a1 = Qualified("a", 1), a2 = Qualified("a", 2), z = Qualified("z", 1);
        FormationRecipe recipe1 = Recipe("terrace", 1, 3, "a", 1), recipe2 = Recipe("terrace", 2, 3, "a", 2);
        var catalog = new FormationCatalog(new[] { z, a2, a1 }, new[] { Profile("z-soil"), Profile() }, new[] { recipe2, recipe1 });
        Assert.Equal(a1, catalog.RequireQualified("a", 1), "first revision resolves exactly");
        Assert.Equal(a2, catalog.RequireQualified("a", 2), "later revision does not replace the first");
        Assert.Equal(z, catalog.RequireQualified("z", 1), "last key is searchable");
        Assert.Equal("a", catalog.Qualified[0].Key, "definitions use ordinal key ordering");
        Assert.Equal(1, catalog.Qualified[0].Revision, "revision ordering is numeric");
        Assert.Equal("soil", catalog.Profiles[0].Key, "profiles use ordinal key ordering");
        Assert.Equal(recipe1, catalog.Recipes[0], "recipes use key/revision ordering");
        Assert.Equal(recipe2, catalog.RequireRecipe("terrace", 2), "same-key revisions share their stable identity domain");
        Parallel.For(0, 24, _ =>
        {
            Assert.Equal(a2, catalog.RequireQualified("a", 2), "concurrent immutable lookup");
            Assert.Equal("soil", catalog.RequireProfile("soil").Key, "concurrent profile lookup");
        });
        Reject<FormationValidationException>(() => catalog.RequireQualified("a", 0));
        Reject<FormationValidationException>(() => catalog.RequireQualified("A", 1));
        Reject<FormationValidationException>(() => catalog.RequireQualified("a", -1));
        Reject<FormationValidationException>(() => catalog.RequireRecipe("terrace", 0));
        Reject<FormationValidationException>(() => catalog.RequireProfile("missing"));
    }

    [Test]
    public static void FormationCatalogRejectsAmbiguousKeysAndIdentityDomains()
    {
        CliffSupportDefinition definition = Qualified();
        SubstrateProfile profile = Profile();
        FormationRecipe recipe = Recipe();
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition, Qualified() }, new[] { profile }, new[] { recipe }));
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition }, new[] { profile, Profile() }, new[] { recipe }));
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition }, new[] { profile }, new[] { recipe, Recipe() }));
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition }, new[] { profile },
            new[] { recipe, Recipe("different-key", domain: 3) }));
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition }, new[] { profile },
            new[] { recipe, Recipe(revision: 2, domain: 4) }));
        Reject<FormationValidationException>(() => new FormationCatalog(new CliffSupportDefinition[] { null! }, new[] { profile }, Array.Empty<FormationRecipe>()));
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition }, new SubstrateProfile[] { null! }, Array.Empty<FormationRecipe>()));
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition }, new[] { profile }, new FormationRecipe[] { null! }));
    }

    [Test]
    public static void FormationCatalogResolvesEveryProfileGeometryContactFamilyAndScale()
    {
        CliffSupportDefinition definition = Qualified();
        Reject<FormationValidationException>(() => new FormationCatalog(new[] { definition }, Array.Empty<SubstrateProfile>(), Array.Empty<FormationRecipe>()));
        Reject<FormationValidationException>(() => Catalog(definition, Recipe(geometry: "missing")));
        Reject<FormationValidationException>(() => Catalog(definition, Recipe(geometryRevision: 2)));
        Reject<FormationValidationException>(() => Catalog(definition, Recipe(family: "unrelated")));
        Reject<FormationValidationException>(() => Catalog(definition, Recipe(contact: "missing")));
        Reject<FormationValidationException>(() => Catalog(definition, Recipe(minimumScale: 0.25)));
        Reject<FormationValidationException>(() => Catalog(definition, Recipe(maximumScale: 3)));
        var catalog = Catalog(definition, Recipe(minimumScale: 0.5, maximumScale: 2));
        Assert.Equal(1, catalog.Recipes.Length, "qualification interval endpoints are permitted");
        Assert.Equal(definition, catalog.RequireQualified("shelf", 1), "validation resolves the supplied geometry without a fallback");
    }

    [Test]
    public static void FormationCatalogAndRecipeSnapshotsIgnoreSourceMutation()
    {
        CliffSupportDefinition definition = Qualified();
        string[] contacts = { "toe" };
        var choice = new FormationGeometryChoice("shelf", 1, contacts, 0.5, 2);
        FormationGeometryChoice[] choices = { choice };
        var slot = new FormationRecipeSlot(4, "crown", true, choices);
        FormationRecipeSlot[] slots = { slot };
        var recipe = new FormationRecipe("terrace", 1, 3, "rock", slots, 12, 20, 0.5);
        var definitions = new List<CliffSupportDefinition> { definition };
        var profiles = new List<SubstrateProfile> { Profile() };
        var recipes = new List<FormationRecipe> { recipe };
        var catalog = new FormationCatalog(definitions, profiles, recipes);
        contacts[0] = "missing"; choices[0] = new FormationGeometryChoice("missing", 1, new[] { "missing" }, 0.5, 2);
        slots[0] = new FormationRecipeSlot(9, "other", false, choices);
        definitions.Clear(); profiles.Clear(); recipes.Clear();
        Assert.Equal("toe", recipe.Slots[0].Alternatives[0].ContactKeys[0], "contact keys were copied");
        Assert.Equal("shelf", recipe.Slots[0].Alternatives[0].Key, "alternatives were copied");
        Assert.Equal(4, recipe.Slots[0].Slot, "slot identity survives source replacement");
        Assert.Equal(recipe, catalog.RequireRecipe("terrace", 1), "catalog retains its snapshot");
        Assert.Equal(definition, catalog.RequireQualified("shelf", 1), "qualified source collection is not retained");
        foreach (Type type in new[] { typeof(FormationCatalog), typeof(FormationRecipe), typeof(FormationRecipeSlot), typeof(FormationGeometryChoice) })
            Assert.True(type.GetProperties().All(p => p.SetMethod == null), "published values are get-only: " + type.Name);
        foreach (FieldInfo field in typeof(FormationCatalog).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Assert.True(!typeof(Delegate).IsAssignableFrom(field.FieldType) && !typeof(ISurface).IsAssignableFrom(field.FieldType),
                "catalog does not retain a world or loading callback: " + field.Name);
    }

    [Test]
    public static void FormationCatalogAccountsRetainedContentAndRefusesCapacityExplicitly()
    {
        CliffSupportDefinition definition = Qualified();
        FormationRecipe recipe = Recipe();
        var limits = new FormationCatalogLimits { MaxDefinitions = 1, MaxProfiles = 1, MaxRecipes = 1 };
        var catalog = new FormationCatalog(new[] { definition }, new[] { Profile() }, new[] { recipe }, limits);
        Assert.True(catalog.EstimatedResidentBytes >= definition.EstimatedResidentBytes + recipe.EstimatedResidentBytes,
            "retained geometry and recipe data count toward catalog admission");
        var exact = limits with { MaxEstimatedResidentBytes = catalog.EstimatedResidentBytes, MaxConstructionScratchBytes = catalog.ReservedConstructionScratchBytes };
        _ = new FormationCatalog(new[] { definition }, new[] { Profile() }, new[] { recipe }, exact);
        Reject<FormationCapacityException>(() => new FormationCatalog(new[] { definition }, new[] { Profile() }, new[] { recipe },
            exact with { MaxEstimatedResidentBytes = exact.MaxEstimatedResidentBytes - 1 }));
        int enumerated = 0;
        IEnumerable<CliffSupportDefinition> Source()
        {
            enumerated++;
            yield return definition;
        }
        Reject<FormationCapacityException>(() => new FormationCatalog(Source(), new[] { Profile() }, new[] { recipe },
            exact with { MaxConstructionScratchBytes = exact.MaxConstructionScratchBytes - 1 }));
        Assert.Equal(0, enumerated, "copy/sort admission precedes enumeration");
        Reject<FormationCapacityException>(() => new FormationCatalog(new[] { definition, Qualified("other") }, new[] { Profile() }, new[] { recipe }, limits));
        Reject<FormationValidationException>(() => new FormationCatalog(Source(), new[] { Profile() }, new[] { recipe }, limits with { MaxRecipes = -1 }));
        Reject<FormationValidationException>(() => new FormationCatalog(Source(), new[] { Profile() }, new[] { recipe }, limits with { MaxEstimatedResidentBytes = -1 }));
        var empty = new FormationCatalog(Array.Empty<CliffSupportDefinition>(), Array.Empty<SubstrateProfile>(), Array.Empty<FormationRecipe>(),
            new FormationCatalogLimits { MaxDefinitions = 0, MaxProfiles = 0, MaxRecipes = 0 });
        Assert.Equal(0, empty.Qualified.Length, "an explicitly empty catalog is supported");
        Reject<FormationValidationException>(() => empty.RequireQualified("shelf", 1));
    }

    private static FormationCatalog Catalog(CliffSupportDefinition definition, FormationRecipe recipe) =>
        new(new[] { definition }, new[] { Profile() }, new[] { recipe });
    private static Vector3D[] Vertices() => new[] { new Vector3D(-2, 0, -2), new Vector3D(-2, 0, 2),
        new Vector3D(2, 0, 2), new Vector3D(2, 0, -2) };
    private static SupportPatch Patch() => new("shelf", "soil", SupportCapabilities.Walkable | SupportCapabilities.Plantable | SupportCapabilities.Soil);
    private static FormationGeometry Mesh(string key = "shelf", int revision = 1, bool reverse = false, Vector3D offset = default) =>
        new(key, revision, "rock", Vertices().Select(v => v + offset), reverse
            ? new[] { new GeometryTriangle(0, 2, 1, 0), new GeometryTriangle(0, 3, 2, 0) }
            : new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) }, new[] { Patch() });
    private static CliffPatchDomain Domain(int[]? vertices = null) => new("shelf",
        new[] { new CliffPatchBoundary(vertices ?? new[] { 0, 1, 2, 3 }) });
    private static CliffContactDomain Contact(string patch = "shelf", int[]? vertices = null) =>
        new("toe", "base", patch, vertices ?? new[] { 0, 1 }, 0, 0.2, 0.1);
    private static CliffLodTriangleMap[] Maps(FormationGeometry geometry) =>
        Enumerable.Range(0, geometry.TriangleCount).Select(i => new CliffLodTriangleMap(i, i)).ToArray();
    private static CliffLodBinding Lod(string key, FormationGeometry mesh, IEnumerable<CliffLodTriangleMap> maps,
        double error = 0, double normalDot = 1) => new(key, LodDigest, mesh, maps, error, normalDot);
    private static CliffSupportDefinition Definition(FormationGeometry mesh, IEnumerable<CliffPatchDomain>? patches = null,
        IEnumerable<CliffContactDomain>? contacts = null, IEnumerable<CliffLodBinding>? lods = null, string? source = null,
        string? physical = null, double minimumScale = 0.5, CliffSupportLimits? limits = null) =>
        new(mesh, source ?? SourceDigest, physical ?? PhysicalDigest, minimumScale, 2,
            patches ?? new[] { Domain() }, contacts ?? new[] { Contact() },
            lods ?? new[] { Lod("near", mesh, Maps(mesh)), Lod("far", mesh, Maps(mesh)) }, limits);
    private static CliffSupportDefinition Qualified(string key = "shelf", int revision = 1) => Definition(Mesh(key, revision));
    private static SubstrateProfile Profile(string key = "soil") => new(key, 0.2, 0.1, 0.7, 0.2, 1, 0.8, 0.03, 0.01);
    private static FormationRecipe Recipe(string key = "terrace", int revision = 1, ushort domain = 3, string geometry = "shelf",
        int geometryRevision = 1, string family = "rock", string contact = "toe", double minimumScale = 0.5, double maximumScale = 2) =>
        new(key, revision, domain, family, new[] { new FormationRecipeSlot(0, "crown", true,
            new[] { new FormationGeometryChoice(geometry, geometryRevision, new[] { contact }, minimumScale, maximumScale) }) }, 12, 20, 0.5);

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
