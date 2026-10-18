using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationContactLoopTests
{
    private static readonly string Digest = new('a', 64);

    [Test]
    public static void FormationContactLoopCopiesOrderedIndicesAndBoundsItsImplicitClosure()
    {
        int[] indices = { 2, 3, 0, 1 };
        var loop = new FormationContactLoop("buried-ring", indices);
        var binding = new FormationContactLoopBinding("buried-ring", indices);
        indices[0] = 99;
        Assert.True(loop.VertexIndices.SequenceEqual(new[] { 2, 3, 0, 1 }), "physical order is copied without rotation");
        Assert.True(binding.VertexIndices.SequenceEqual(loop.VertexIndices), "visual order is copied independently");
        Assert.Equal(512, new FormationContactLoop("maximum", Enumerable.Range(0, 512)).VertexIndices.Length, "512 vertices are admitted");
        Assert.Equal(3, new FormationContactLoop("minimum", new[] { 0, 1, 2 }).VertexIndices.Length, "three vertices are admitted");
        foreach (int[] invalid in new[] { new[] { 0, 1 }, new[] { 0, 1, 2, 0 }, new[] { -1, 0, 1 } })
        {
            Reject<FormationValidationException>(() => new FormationContactLoop("ring", invalid));
            Reject<FormationValidationException>(() => new FormationContactLoopBinding("ring", invalid));
        }
        Reject<FormationCapacityException>(() => new FormationContactLoop("too-many", Enumerable.Range(0, 513)));
        Reject<FormationCapacityException>(() => new FormationContactLoopBinding("too-many", Enumerable.Range(0, 513)));
        Assert.Equal(2, new CliffContactDomain("open", "toe", "bare", new[] { 0, 1 }, 0, 1, 0).VertexIndices.Length,
            "legacy fitting chains remain open and accept two vertices");
        Reject<FormationCapacityException>(() => new CliffContactDomain("open", "toe", "bare", Enumerable.Range(0, 65), 0, 1, 0));
    }

    [Test]
    public static void FormationContactLoopQualifiesExactPhysicalAndEveryLodClosingEdge()
    {
        FormationGeometry mesh = Mesh();
        CliffSupportDefinition definition = Definition(mesh);
        Assert.Equal("ring", definition.ContactLoops[0].Key, "physical loop is exposed independently of open contacts");
        foreach (CliffLodBinding lod in definition.Lods)
            Assert.True(lod.RequireContactLoop("ring").VertexIndices.SequenceEqual(new[] { 0, 1, 2, 3 }), "each LOD retains exact ordered binding");
        Reject<FormationValidationException>(() => definition.RequireContactLoop("missing"));
        Reject<FormationValidationException>(() => definition.Lods[0].RequireContactLoop("missing"));

        var missingPhysicalClosure = new[] { new FormationContactLoop("ring", new[] { 1, 2, 3 }) };
        Reject<FormationValidationException>(() => Definition(mesh, loops: missingPhysicalClosure), "closing segment");
        // All coordinates agree, but the other diagonal removes visual edge 2 -> 0.
        FormationGeometry otherDiagonal = Mesh(triangles: new[] { new GeometryTriangle(0, 1, 3, 0), new GeometryTriangle(1, 2, 3, 0) });
        int[] triangleLoop = { 0, 1, 2 };
        Reject<FormationValidationException>(() => Definition(mesh,
            loops: new[] { new FormationContactLoop("ring", triangleLoop) },
            lods: new[] { Lod("near", mesh, triangleLoop), Lod("far", otherDiagonal, triangleLoop) }), "closing segment");
    }

    [Test]
    public static void FormationContactLoopRequiresExactCoordinatesBeyondOrdinaryLodTolerance()
    {
        FormationGeometry mesh = Mesh();
        Vector3D[] changed = Points(); changed[0] += new Vector3D(0, 0.000001, 0);
        FormationGeometry shifted = Mesh(changed);
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh), Lod("far", shifted) }), "exactly");
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh), Lod("far", mesh, new[] { 1, 2, 3, 0 }) }), "exactly");
        // Duplicate coordinates at distinct indices cannot be disguised as unique loop vertices.
        Vector3D[] duplicates = Points().Concat(new[] { Points()[0] }).ToArray();
        FormationGeometry visualDuplicate = Mesh(duplicates, new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(4, 2, 3, 0) });
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh), Lod("far", visualDuplicate, new[] { 0, 1, 2, 4 }) }), "distinct local coordinates");

        var physicalDuplicate = new FormationGeometry("duplicate", 1, "rock", duplicates,
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(4, 2, 3, 1) },
            new[] { Patch("first"), Patch("second") });
        CliffPatchDomain[] domains = { Domain("first", new[] { 0, 1, 2 }), Domain("second", new[] { 4, 2, 3 }) };
        Reject<FormationValidationException>(() => new CliffSupportDefinition(physicalDuplicate, Digest, Digest, 1, 1,
            domains, Array.Empty<CliffContactDomain>(), new[] { Lod("near", physicalDuplicate, new[] { 0, 1, 2, 3, 4 }), Lod("far", physicalDuplicate, new[] { 0, 1, 2, 3, 4 }) },
            contactLoops: new[] { new FormationContactLoop("ring", new[] { 0, 1, 2, 3, 4 }) }), "distinct local coordinates");
    }

    [Test]
    public static void FormationContactLoopBindingsMustMatchEveryLodAndSortByKey()
    {
        FormationGeometry mesh = Mesh();
        FormationContactLoop[] loops = { new("z-ring", new[] { 0, 1, 2, 3 }), new("a-ring", new[] { 0, 1, 2, 3 }) };
        FormationContactLoopBinding[] bindings = { new("z-ring", new[] { 0, 1, 2, 3 }), new("a-ring", new[] { 0, 1, 2, 3 }) };
        var lods = new[] { Lod("near", mesh, bindings: bindings), Lod("far", mesh, bindings: bindings) };
        CliffSupportDefinition definition = Definition(mesh, loops, lods);
        loops[0] = new FormationContactLoop("changed", new[] { 0, 1, 2 });
        bindings[0] = new FormationContactLoopBinding("changed", new[] { 0, 1, 2 });
        Assert.Equal("a-ring", definition.ContactLoops[0].Key, "physical loops sort by ordinal key");
        Assert.Equal("a-ring", definition.Lods[0].ContactLoops[0].LoopKey, "visual bindings sort by the same ordinal key");
        Assert.Equal("z-ring", definition.ContactLoops[1].Key, "physical array is copied");
        Assert.Equal("z-ring", definition.Lods[0].ContactLoops[1].LoopKey, "visual array is copied");
        Reject<FormationValidationException>(() => Definition(mesh, loops: new[] { definition.ContactLoops[0], definition.ContactLoops[0] }));
        Reject<FormationValidationException>(() => Lod("far", mesh, bindings: new[] { new FormationContactLoopBinding("ring", new[] { 0, 1, 2 }), new FormationContactLoopBinding("ring", new[] { 0, 1, 2 }) }));
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh), Lod("far", mesh, bindings: Array.Empty<FormationContactLoopBinding>()) }));
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh), Lod("far", mesh, bindings: new[] { new FormationContactLoopBinding("unknown", new[] { 0, 1, 2, 3 }) }) }));
        Reject<FormationValidationException>(() => Definition(mesh, lods: new[] { Lod("near", mesh), Lod("far", mesh, new[] { 0, 1, 2 }) }));
        Reject<FormationValidationException>(() => Definition(mesh, loops: Array.Empty<FormationContactLoop>()));
        Reject<FormationValidationException>(() => Definition(mesh, loops: new[] { new FormationContactLoop("ring", new[] { 0, 1, 99 }) }));
    }

    [Test]
    public static void FormationContactLoopChargesAggregateReferencesAndPreservesLegacyAccounting()
    {
        FormationGeometry mesh = Mesh();
        CliffSupportDefinition definition = Definition(mesh);
        var emptyBindings = Array.Empty<FormationContactLoopBinding>();
        CliffSupportDefinition legacy = Definition(mesh, Array.Empty<FormationContactLoop>(),
            new[] { Lod("near", mesh, bindings: emptyBindings), Lod("far", mesh, bindings: emptyBindings) });
        Assert.Equal(0, legacy.ContactLoops.Length, "legacy definitions need no closed contact loops");
        Assert.Equal(864L, definition.EstimatedResidentBytes - legacy.EstimatedResidentBytes, "three four-vertex loops account for all retained metadata");
        Assert.Equal(33536L, definition.ReservedConstructionScratchBytes - legacy.ReservedConstructionScratchBytes, "validation reserves bounded edge and coordinate storage");
        var exact = new CliffSupportLimits { MaxContactLoops = 1, MaxLoopVertices = 12,
            MaxEstimatedResidentBytes = definition.EstimatedResidentBytes, MaxEstimatedScratchBytes = definition.ReservedConstructionScratchBytes };
        _ = Definition(mesh, limits: exact);
        Reject<FormationCapacityException>(() => Definition(mesh, limits: exact with { MaxContactLoops = 0 }));
        Reject<FormationCapacityException>(() => Definition(mesh, limits: exact with { MaxLoopVertices = 11 }));
        Reject<FormationCapacityException>(() => Definition(mesh, limits: exact with { MaxEstimatedResidentBytes = exact.MaxEstimatedResidentBytes - 1 }));
        Reject<FormationCapacityException>(() => Definition(mesh, limits: exact with { MaxEstimatedScratchBytes = exact.MaxEstimatedScratchBytes - 1 }));
        Reject<FormationValidationException>(() => Definition(mesh, limits: exact with { MaxContactLoops = 129 }));
        Reject<FormationValidationException>(() => Definition(mesh, limits: exact with { MaxLoopVertices = -1 }));
    }

    [Test]
    public static void FormationContactLoopExplicitSeamsRequireExactEndpointsAndActualEdges()
    {
        Vector3D[] p = Points();
        // Two render triangles have distinct corners at their material/normal/UV seam.
        FormationGeometry visual = Mesh(new[] { p[0], p[1], p[2], p[0], p[2], p[3] },
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(3, 4, 5, 0) });
        int[] representatives = { 0, 1, 2, 5 }, edges = { 0, 1, 1, 2, 4, 5, 5, 3 };
        var binding = new FormationContactLoopBinding("ring", representatives, edges);
        var lods = new[] { Lod("near", visual, bindings: new[] { binding }), Lod("far", visual, bindings: new[] { binding }) };
        CliffSupportDefinition admitted = Definition(Mesh(), lods: lods);
        edges[0] = 99; representatives[0] = 99;
        Assert.Equal(0, binding.EdgeVertexIndices[0], "explicit edge storage is privately copied");
        Assert.Equal(0, binding.VertexIndices[0], "representative storage is privately copied");
        Assert.Equal(8, admitted.Lods[0].ContactLoops[0].EdgeVertexIndices.Length, "every closing edge is retained");
        Reject<FormationValidationException>(() => Definition(Mesh(), lods: new[] { Lod("near", visual, new[] { 0, 1, 2, 5 }), Lod("far", visual, new[] { 0, 1, 2, 5 }) }));
        foreach (int[] invalid in new[] { new[] { 1, 0, 1, 2, 4, 5, 5, 3 }, new[] { 0, 1, 1, 2, 2, 5, 5, 3 }, new[] { 0, 1, 1, 2, 4, 5, 5, 99 } })
        {
            var bad = new FormationContactLoopBinding("ring", new[] { 0, 1, 2, 5 }, invalid);
            Reject<FormationValidationException>(() => Definition(Mesh(), lods: new[] { Lod("near", visual, bindings: new[] { bad }), lods[1] }));
        }
        Vector3D[] shifted = { p[0], p[1], p[2], p[0] + new Vector3D(0, double.Epsilon, 0), p[2], p[3] };
        FormationGeometry cracked = Mesh(shifted, new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(3, 4, 5, 0) });
        Reject<FormationValidationException>(() => Definition(Mesh(), lods: new[] { Lod("near", cracked, bindings: new[] { binding }), lods[1] }));
        Reject<FormationValidationException>(() => new FormationContactLoopBinding("ring", new[] { 0, 1, 2 }, new[] { 0, 1 }));
        Reject<FormationValidationException>(() => new FormationContactLoopBinding("ring", new[] { 0, 1, 2 }, new[] { 0, 1, 1, 2, 2, -1 }));
        // Four physical + two sets of (four representatives + eight actual endpoint references).
        var exact = new CliffSupportLimits { MaxLoopVertices = 28, MaxEstimatedResidentBytes = admitted.EstimatedResidentBytes,
            MaxEstimatedScratchBytes = admitted.ReservedConstructionScratchBytes };
        _ = Definition(Mesh(), lods: lods, limits: exact);
        Reject<FormationCapacityException>(() => Definition(Mesh(), lods: lods, limits: exact with { MaxLoopVertices = 27 }));
    }

    private static Vector3D[] Points() => new[] { new Vector3D(-2, 0, -2), new Vector3D(-2, 0, 2), new Vector3D(2, 0, 2), new Vector3D(2, 0, -2) };
    private static SupportPatch Patch(string key = "bare") => new(key, "rock", SupportCapabilities.None);
    private static FormationGeometry Mesh(Vector3D[]? points = null, GeometryTriangle[]? triangles = null) => new("rock", 1, "rock", points ?? Points(),
        triangles ?? new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) }, new[] { Patch() });
    private static CliffPatchDomain Domain(string key = "bare", int[]? vertices = null) => new(key,
        new[] { new CliffPatchBoundary(vertices ?? new[] { 0, 1, 2, 3 }) });
    private static CliffLodBinding Lod(string key, FormationGeometry mesh, int[]? vertices = null, FormationContactLoopBinding[]? bindings = null) =>
        new(key, Digest, mesh, Array.Empty<CliffLodTriangleMap>(), 100, 0,
            bindings ?? new[] { new FormationContactLoopBinding("ring", vertices ?? new[] { 0, 1, 2, 3 }) });
    private static CliffSupportDefinition Definition(FormationGeometry mesh, FormationContactLoop[]? loops = null, CliffLodBinding[]? lods = null, CliffSupportLimits? limits = null) =>
        new(mesh, Digest, Digest, 1, 1, new[] { Domain() }, new[] { new CliffContactDomain("toe", "toe", "bare", new[] { 0, 1 }, 0, 1, 0) },
            lods ?? new[] { Lod("near", mesh), Lod("far", mesh) }, limits,
            loops ?? new[] { new FormationContactLoop("ring", new[] { 0, 1, 2, 3 }) });
    private static void Reject<T>(Action action, string? message = null) where T : Exception
    {
        try { action(); }
        catch (T error)
        {
            Assert.True(message == null || error.Message.Contains(message, StringComparison.Ordinal), "failure identifies the violated closed-loop contract: " + error.Message);
            return;
        }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
