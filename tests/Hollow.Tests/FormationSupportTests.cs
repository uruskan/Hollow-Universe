using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationSupportTests
{
    [Test]
    public static void FormationSupportRepeatedInstancesShareCatalogStorageAcrossWorlds()
    {
        var local = new FormationGeometryIndex(Quad());
        var pieces = new FormationPiece[16];
        for (int i = 0; i < pieces.Length; i++)
            pieces[i] = new FormationPiece(new FormationPieceId(new FormationId((ulong)i, 0), 0), "face", Quad(),
                new FormationTransform(new Vector3D(i * 10, 0, 0), Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 1));
        var limits = new FormationSupportIndexLimits { MaxPieces = 16, MaxTriangles = 32, MaxUniqueTriangles = 2, MaxUniqueGeometries = 1, MaxNodes = 34 };
        var first = new FormationSupportIndex(pieces, new[] { local }, limits);
        Array.Reverse(pieces);
        var second = new FormationSupportIndex(pieces, new[] { local }, limits);
        Assert.True(ReferenceEquals(first.GeometryIndexes[0], local) && ReferenceEquals(first.GeometryIndexes[0], second.GeometryIndexes[0]),
            "independent equal piece geometry uses the explicitly shared immutable catalog BVH");
        Assert.Equal(32, first.Diagnostics.Triangles);
        Assert.Equal(2, first.Diagnostics.LocalTriangles);
        Assert.Equal(1, first.Diagnostics.UniqueGeometries);
        Assert.Equal(31, first.Diagnostics.WorldNodes);
        Assert.Equal(3, first.Diagnostics.LocalNodes);
        Assert.Equal(local.EstimatedResidentBytes, first.Diagnostics.SharedGeometryEstimatedResidentBytes);
        Assert.Equal(first.Diagnostics.WorldEstimatedResidentBytes + local.EstimatedResidentBytes, first.Diagnostics.EstimatedResidentBytes);
        for (int i = 0; i < pieces.Length; i++)
        {
            FormationRayRequest ray = Down() with { Origin = new Vector3D(i * 10 + 0.25, 10, 0) };
            FormationRayResult hits = FormationSupportQueries.Raycast(first, ray);
            Assert.Equal(1, hits.Hits.Length);
            Assert.Equal((ulong)i, hits.Hits[0].Primitive.Piece.Formation.Owner);
            Assert.True(hits.Hits.SequenceEqual(FormationSupportQueries.Raycast(second, ray).Hits));
        }
        Throws<FormationCapacityException>(() => _ = new FormationSupportIndex(pieces, new[] { local }, limits with { MaxUniqueTriangles = 1 }));
        Throws<FormationCapacityException>(() => _ = new FormationSupportIndex(pieces, new[] { local }, limits with { MaxTriangles = 31 }));
        Throws<FormationCapacityException>(() => _ = new FormationSupportIndex(pieces, new[] { local }, limits with { MaxNodes = 33 }));
        Throws<FormationCapacityException>(() => _ = new FormationGeometryIndex(Quad(), new FormationGeometryIndexLimits { MaxTriangles = 1 }));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Throws<OperationCanceledException>(() => _ = new FormationGeometryIndex(Quad(), cancellation: cancelled.Token));
    }

    [Test]
    public static void FormationSupportWorldAndLocalTraversalShareEveryQueryBudget()
    {
        var index = new FormationSupportIndex(new[] { Piece(1, Quad(), 0) });
        FormationRayResult full = FormationSupportQueries.Raycast(index, Down(), new FormationSupportQueryLimits { MaxVisitedNodes = 4 });
        Assert.Equal(FormationQueryStatus.Complete, full.Status);
        Assert.Equal(4, full.Diagnostics.VisitedNodes, "one world leaf and three local BVH nodes");
        Assert.Equal(2, full.Diagnostics.TestedTriangles);
        FormationRayResult shortNodes = FormationSupportQueries.Raycast(index, Down(), new FormationSupportQueryLimits { MaxVisitedNodes = 3 });
        Assert.Equal(FormationQueryStatus.CapacityExceeded, shortNodes.Status);
        Assert.Equal(3, shortNodes.Diagnostics.VisitedNodes);
        Assert.Equal(0, shortNodes.Hits.Length);
        FormationRayResult miss = FormationSupportQueries.Raycast(index, Down() with { Origin = new Vector3D(20, 10, 0) });
        Assert.Equal(1, miss.Diagnostics.VisitedNodes);
        Assert.Equal(0, miss.Diagnostics.TestedTriangles, "world bounds reject without local traversal");
        FormationFootprintResult footprint = FormationSupportQueries.Footprint(index,
            new FormationFootprintRequest(new[] { Down(), Down() }, Vector3D.UnitY), new FormationSupportQueryLimits { MaxVisitedNodes = 7 });
        Assert.Equal(FormationQueryStatus.CapacityExceeded, footprint.Status);
        Assert.Equal(7, footprint.Diagnostics.VisitedNodes);
        Assert.True(!footprint.Supported && footprint.Hits.Length == 0);
    }

    [Test]
    public static void FormationDiskContainsContinuousAreaAcrossTriangleEdgesButRejectsPatchBoundary()
    {
        FormationGeometry geometry = Quad();
        var index = new FormationSupportIndex(new[] { Piece(1, geometry, 0) });
        CliffSupportDefinition definition = Definition(geometry, new[] { 0, 1, 2, 3 });
        SupportHit anchor = FormationSupportQueries.Raycast(index, Down()).Hits[0];
        FormationDiskResult inside = FormationSupportQueries.ContainsDisk(index, definition, new(anchor, 1));
        Assert.True(inside.Supported, "the disk crosses the internal diagonal but stays inside the entire physical patch");
        Assert.Equal(anchor, inside.Anchor!.Value);
        Assert.Equal(2, inside.Diagnostics.DomainTriangles);
        Assert.Equal(4, inside.Diagnostics.DomainEdges);
        foreach (double radius in new[] { 1.75, 1.8 })
        {
            FormationDiskResult outside = FormationSupportQueries.ContainsDisk(index, definition, new(anchor, radius));
            Assert.Equal(FormationQueryStatus.Complete, outside.Status);
            Assert.Equal(FormationDiskQualification.BoundaryCrossing, outside.Qualification);
            Assert.True(!outside.Supported && outside.Anchor == null, "touching or crossing is conservatively rejected");
        }
        // Equal, separately allocated definitions are admitted by exact bounded content comparison.
        CliffSupportDefinition equal = Definition(Quad(), new[] { 0, 1, 2, 3 });
        FormationDiskResult copy = FormationSupportQueries.ContainsDisk(index, equal, new(anchor, 1));
        Assert.True(copy.Supported);
        Assert.Equal(10, copy.Diagnostics.DomainVertices);
        Assert.Equal(4, copy.Diagnostics.DomainTriangles);
        Assert.Equal(2, copy.Diagnostics.DomainPatches);
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 2 }, _ =>
            Assert.True(FormationSupportQueries.ContainsDisk(index, definition, new(anchor, 1)).Supported));
    }

    [Test]
    public static void FormationDiskCannotBridgeAHoleEvenWhenExplicitFootprintSamplesPass()
    {
        var vertices = new[] {
            new Vector3D(-2, 0, -2), new Vector3D(-2, 0, 2), new Vector3D(2, 0, 2), new Vector3D(2, 0, -2),
            new Vector3D(-0.5, 0, -0.5), new Vector3D(-0.5, 0, 0.5), new Vector3D(0.5, 0, 0.5), new Vector3D(0.5, 0, -0.5) };
        var triangles = new List<GeometryTriangle>();
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            triangles.Add(new GeometryTriangle(i, next, next + 4, 0));
            triangles.Add(new GeometryTriangle(i, next + 4, i + 4, 0));
        }
        var ring = new FormationGeometry("ring", 1, "rock", vertices, triangles, new[] { Patch() });
        var index = new FormationSupportIndex(new[] { Piece(1, ring, 0) });
        CliffSupportDefinition definition = Definition(ring, new[] { 0, 1, 2, 3 }, new[] { 4, 7, 6, 5 });
        FormationRayRequest ray = Down() with { Origin = new Vector3D(1, 10, 0) };
        SupportHit anchor = FormationSupportQueries.Raycast(index, ray).Hits[0];
        Assert.True(FormationSupportQueries.ContainsDisk(index, definition, new(anchor, 0.25)).Supported);
        FormationFootprintResult samples = FormationSupportQueries.Footprint(index, new FormationFootprintRequest(new[] {
            ray, ray with { Origin = new Vector3D(1, 10, 0.6) }, ray with { Origin = new Vector3D(1, 10, -0.6) } }, Vector3D.UnitY));
        Assert.True(samples.Supported, "these supplied samples do not inspect the hole");
        FormationDiskResult disk = FormationSupportQueries.ContainsDisk(index, definition, new(anchor, 0.6));
        Assert.Equal(FormationDiskQualification.BoundaryCrossing, disk.Qualification);
        Assert.True(!disk.Supported, "continuous disk containment examines the inner boundary too");
    }

    [Test]
    public static void FormationDiskRejectsNonplanarPatchesAndUnqualifiedOrForgedAnchors()
    {
        Vector3D[] vertices = Vertices(); vertices[3] = new Vector3D(2, 1, -2);
        var bent = new FormationGeometry("bent", 1, "rock", vertices,
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) }, new[] { Patch() });
        var bentIndex = new FormationSupportIndex(new[] { Piece(1, bent, 0) });
        SupportHit flatAnchor = FormationSupportQueries.Raycast(bentIndex, Down() with { Origin = new Vector3D(-0.25, 10, 0) }).Hits[0];
        FormationDiskResult nonplanar = FormationSupportQueries.ContainsDisk(bentIndex, Definition(bent, new[] { 0, 1, 2, 3 }), new(flatAnchor, 0.1));
        Assert.Equal(FormationQueryStatus.Complete, nonplanar.Status);
        Assert.Equal(FormationDiskQualification.NonPlanarPatch, nonplanar.Qualification);
        Assert.True(!nonplanar.Supported);
        vertices[3] = new Vector3D(-1, 0, 1);
        var folded = new FormationGeometry("folded", 1, "rock", vertices,
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) }, new[] { Patch() });
        var foldedIndex = new FormationSupportIndex(new[] { Piece(1, folded, 0) });
        SupportHit foldedAnchor = FormationSupportQueries.Raycast(foldedIndex, Down() with { Origin = new Vector3D(-0.25, 10, 0) }).Hits[0];
        Assert.Equal(FormationDiskQualification.UnsupportedDomain,
            FormationSupportQueries.ContainsDisk(foldedIndex, Definition(folded, new[] { 0, 1, 2, 3 }), new(foldedAnchor, 0.1)).Qualification,
            "a topologically manifold sheet folded flat cannot grant upward support through reversed triangles");
        FormationGeometry geometry = Quad();
        var index = new FormationSupportIndex(new[] { Piece(1, geometry, 0) });
        CliffSupportDefinition definition = Definition(geometry, new[] { 0, 1, 2, 3 });
        SupportHit anchor = FormationSupportQueries.Raycast(index, Down()).Hits[0];
        SupportHit forged = new(anchor.Primitive, new Vector3D(20, 0, 0), anchor.Normal, anchor.Barycentric,
            anchor.Distance, anchor.PatchKey, anchor.FamilyKey, anchor.SubstrateProfileKey, anchor.Capabilities);
        Assert.Equal(FormationDiskQualification.InvalidAnchor, FormationSupportQueries.ContainsDisk(index, definition, new(forged, 0.1)).Qualification);
        Assert.Equal(FormationDiskQualification.InvalidAnchor,
            FormationSupportQueries.ContainsDisk(new FormationSupportIndex(new[] { Piece(2, geometry, 0) }), definition, new(anchor, 0.1)).Qualification);
        Assert.Equal(FormationDiskQualification.DefinitionMismatch,
            FormationSupportQueries.ContainsDisk(index, Definition(Quad("different"), new[] { 0, 1, 2, 3 }), new(anchor, 0.1)).Qualification);
        var scaled = new FormationPiece(new FormationPieceId(new FormationId(1, 0), 0), "face", geometry,
            new FormationTransform(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 3));
        var scaledIndex = new FormationSupportIndex(new[] { scaled });
        SupportHit scaledAnchor = FormationSupportQueries.Raycast(scaledIndex, Down()).Hits[0];
        Assert.Equal(FormationDiskQualification.ScaleOutsideQualification,
            FormationSupportQueries.ContainsDisk(scaledIndex, definition, new(scaledAnchor, 0.1)).Qualification);
        foreach (double radius in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.Epsilon, double.MaxValue })
            Throws<FormationValidationException>(() => FormationSupportQueries.ContainsDisk(index, definition, new(anchor, radius)));
        Throws<FormationValidationException>(() => FormationSupportQueries.ContainsDisk(index, definition, default));
    }

    [Test]
    public static void FormationDiskBoundsEveryDomainScanAndPublishesNoPartialSupport()
    {
        FormationGeometry geometry = Quad();
        var index = new FormationSupportIndex(new[] { Piece(1, geometry, 0) });
        CliffSupportDefinition definition = Definition(geometry, new[] { 0, 1, 2, 3 });
        var request = new FormationDiskRequest(FormationSupportQueries.Raycast(index, Down()).Hits[0], 1);
        foreach (FormationSupportQueryLimits limits in new[] {
            new FormationSupportQueryLimits { MaxDomainTriangles = 1 }, new FormationSupportQueryLimits { MaxDomainVertices = 5 },
            new FormationSupportQueryLimits { MaxDomainEdges = 3 }, new FormationSupportQueryLimits { MaxDomainPatches = 0 },
            new FormationSupportQueryLimits { MaxTestedTriangles = 2 }, new FormationSupportQueryLimits { MaxVisitedNodes = 0 },
            new FormationSupportQueryLimits { MaxHits = 0 }, new FormationSupportQueryLimits { MaxEstimatedScratchBytes = 0 },
            new FormationSupportQueryLimits { MaxEstimatedResultBytes = 0 } })
        {
            FormationDiskResult result = FormationSupportQueries.ContainsDisk(index, definition, request, limits);
            Assert.Equal(FormationQueryStatus.CapacityExceeded, result.Status);
            Assert.Equal(FormationDiskQualification.NotEvaluated, result.Qualification);
            Assert.True(!result.Supported && result.Anchor == null);
        }
        FormationDiskResult equalityBudget = FormationSupportQueries.ContainsDisk(index, Definition(Quad(), new[] { 0, 1, 2, 3 }), request,
            new FormationSupportQueryLimits { MaxDomainVertices = 2 });
        Assert.Equal(FormationQueryStatus.CapacityExceeded, equalityBudget.Status, "independent-definition equality checks have finite work budgets too");
        Assert.Equal(2, equalityBudget.Diagnostics.DomainVertices);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        FormationDiskResult cancellation = FormationSupportQueries.ContainsDisk(index, definition, request, cancellation: cancelled.Token);
        Assert.Equal(FormationQueryStatus.Cancelled, cancellation.Status);
        Assert.True(!cancellation.Supported && cancellation.Anchor == null);
        Assert.Equal(0, cancellation.Diagnostics.VisitedNodes);
    }

    [Test]
    public static void FormationSupportFindsShelvesAndExplicitlySelectsGroundBeneathThem()
    {
        FormationGeometry geometry = Quad();
        var index = new FormationSupportIndex(new[] { Piece(8, geometry, 0), Piece(2, geometry, 3) });
        FormationRayResult all = FormationSupportQueries.Raycast(index, Down());
        Assert.Equal(FormationQueryStatus.Complete, all.Status);
        Assert.Equal(2, all.Hits.Length, "both shelf and underlying ground are returned");
        Assert.Near(7, all.Hits[0].Distance, 1e-12);
        Assert.Near(10, all.Hits[1].Distance, 1e-12);
        Assert.Equal(2UL, all.Hits[0].Primitive.Piece.Formation.Owner);
        Assert.Equal(Vector3D.UnitY, all.Hits[0].Normal);
        Assert.Equal("soil", all.Hits[0].SubstrateProfileKey);
        Assert.Near(1, all.Hits[0].Barycentric.X + all.Hits[0].Barycentric.Y + all.Hits[0].Barycentric.Z, 1e-12);
        FormationRayResult ground = FormationSupportQueries.Raycast(index, Down() with { MinDistance = 8 });
        Assert.Equal(1, ground.Hits.Length);
        Assert.Equal(8UL, ground.Hits[0].Primitive.Piece.Formation.Owner);
    }

    [Test]
    public static void FormationSupportBackfacesPreserveWindingAndCapabilitiesAreExplicit()
    {
        var index = new FormationSupportIndex(new[] { Piece(1, Quad(reverse: true), 0) });
        Assert.Equal(0, FormationSupportQueries.Raycast(index, Down()).Hits.Length);
        FormationRayResult twoSided = FormationSupportQueries.Raycast(index,
            Down() with { Backfaces = FormationBackfacePolicy.TwoSided });
        Assert.Equal(1, twoSided.Hits.Length);
        Assert.Equal(-Vector3D.UnitY, twoSided.Hits[0].Normal, "two-sided queries never flip physical winding normals");
        FormationRayResult excluded = FormationSupportQueries.Raycast(index,
            Down() with { Backfaces = FormationBackfacePolicy.TwoSided, RequiredCapabilities = SupportCapabilities.Soil });
        Assert.Equal(0, excluded.Hits.Length, "substrate naming cannot invent an absent physical capability");
    }

    [Test]
    public static void FormationSupportCanonicalTiesAreIndependentOfInputOrderAndFaceLocation()
    {
        FormationGeometry geometry = Quad();
        FormationPiece a = Piece(9, geometry, 0), b = Piece(3, geometry, 0);
        var first = new FormationSupportIndex(new[] { a, b });
        var second = new FormationSupportIndex(new[] { b, a });
        FormationRayRequest edge = Down() with { Origin = new Vector3D(0, 10, 0) };
        FormationRayResult hits = FormationSupportQueries.Raycast(first, edge);
        Assert.True(hits.Hits.SequenceEqual(FormationSupportQueries.Raycast(second, edge).Hits), "permutation preserves all hit values and ordering");
        Assert.Equal(4, hits.Hits.Length, "the shared diagonal keeps both primitive identities for each piece");
        for (int i = 1; i < hits.Hits.Length; i++)
            Assert.True(hits.Hits[i - 1].Primitive.CompareTo(hits.Hits[i].Primitive) < 0, "exact distance ties use canonical primitive order");
        SupportHit[] expected = hits.Hits.ToArray();
        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 2 }, _ =>
        {
            FormationRayResult concurrent = FormationSupportQueries.Raycast(first, edge);
            Assert.True(concurrent.Status == FormationQueryStatus.Complete && concurrent.Hits.SequenceEqual(expected),
                "concurrent immutable reads retain identical canonical hits");
        });
        var rotated = new FormationPiece(new FormationPieceId(new FormationId(5, 0), 0), "face", geometry,
            new FormationTransform(new Vector3D(100, 100, 100), Vector3D.UnitY, Vector3D.UnitZ, Vector3D.UnitX, 2));
        FormationRayResult seam = FormationSupportQueries.Raycast(new FormationSupportIndex(new[] { rotated }),
            new FormationRayRequest(new Vector3D(100, 100.5, 110), -Vector3D.UnitZ, 20));
        Assert.Equal(1, seam.Hits.Length, "body-space discovery does not depend on a cube face or owner position");
        Assert.Near(10, seam.Hits[0].Distance, 1e-12);
        Assert.Equal(Vector3D.UnitZ, seam.Hits[0].Normal);
    }

    [Test]
    public static void FormationSupportSnapshotAndFootprintInputsCannotBeMutatedByCallers()
    {
        Vector3D[] vertices = Vertices();
        GeometryTriangle[] triangles = { new(0, 1, 2, 0), new(0, 2, 3, 0) };
        SupportPatch[] patches = { Patch() };
        var geometry = new FormationGeometry("copied", 1, "rock", vertices, triangles, patches);
        FormationPiece[] pieces = { Piece(1, geometry, 0) };
        var index = new FormationSupportIndex(pieces);
        FormationRayRequest[] probes = { Down() };
        var footprint = new FormationFootprintRequest(probes, Vector3D.UnitY);
        vertices[0] = new Vector3D(100, 100, 100); triangles[0] = default; patches[0] = new SupportPatch("other", "other", SupportCapabilities.None);
        pieces[0] = Piece(2, Quad(), 100); probes[0] = default;
        FormationFootprintResult result = FormationSupportQueries.Footprint(index, footprint);
        Assert.True(result.Supported, "every caller-owned input was snapshotted");
        Assert.Equal(1UL, result.Hits[0].Primitive.Piece.Formation.Owner);
        Assert.Equal("ground", result.Hits[0].PatchKey);
        Assert.Equal(new Vector3D(-2, 0, -2), geometry.Vertices[0]);
    }

    [Test]
    public static void FormationSupportFootprintRejectsMissingAndIncompatibleSamples()
    {
        var index = new FormationSupportIndex(new[] { Piece(1, Quad(), 0) });
        FormationRayRequest[] rays = { Down(), Down() with { Origin = new Vector3D(-0.5, 10, 0) } };
        FormationFootprintResult supported = FormationSupportQueries.Footprint(index, new FormationFootprintRequest(rays, Vector3D.UnitY));
        Assert.True(supported.Supported);
        Assert.Equal(2, supported.Hits.Length);
        rays[1] = Down() with { Origin = new Vector3D(5, 10, 0) };
        FormationFootprintResult missing = FormationSupportQueries.Footprint(index, new FormationFootprintRequest(rays, Vector3D.UnitY));
        Assert.Equal(FormationQueryStatus.Complete, missing.Status);
        Assert.True(!missing.Supported && missing.Hits.Length == 0, "one successful point cannot support a wide footprint");
        var underneath = new FormationSupportIndex(new[] { Piece(1, Quad(reverse: true), 0) });
        FormationRayRequest[] twoSided = { Down() with { Backfaces = FormationBackfacePolicy.TwoSided } };
        Assert.True(!FormationSupportQueries.Footprint(underneath, new FormationFootprintRequest(twoSided, Vector3D.UnitY)).Supported,
            "an underside is not a support surface merely because the ray is two-sided");
        var raised = new FormationPiece(new FormationPieceId(new FormationId(2, 0), 0), "shelf", Quad("small"),
            new FormationTransform(new Vector3D(-1, 1, 0), Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 0.2));
        var uneven = new FormationSupportIndex(new[] { Piece(1, Quad(), 0), raised });
        rays[1] = Down() with { Origin = new Vector3D(-1, 10, 0) };
        Assert.True(!FormationSupportQueries.Footprint(uneven, new FormationFootprintRequest(rays, Vector3D.UnitY, maximumHeightSpread: 0.5)).Supported,
            "support rejects a footprint split across incompatible shelf heights");
    }

    [Test]
    public static void FormationSupportQueryBudgetsNeverPublishPartialHitsAndAreSharedAcrossProbes()
    {
        var index = new FormationSupportIndex(new[] { Piece(1, Quad(), 0), Piece(2, Quad(), 3) });
        foreach (FormationSupportQueryLimits limits in new[] {
            new FormationSupportQueryLimits { MaxHits = 1 },
            new FormationSupportQueryLimits { MaxVisitedNodes = 1 },
            new FormationSupportQueryLimits { MaxTestedTriangles = 0 },
            new FormationSupportQueryLimits { MaxEstimatedScratchBytes = 0 },
            new FormationSupportQueryLimits { MaxEstimatedResultBytes = 0 } })
        {
            FormationRayResult result = FormationSupportQueries.Raycast(index, Down(), limits);
            Assert.Equal(FormationQueryStatus.CapacityExceeded, result.Status);
            Assert.Equal(0, result.Hits.Length, "capacity exhaustion cannot impersonate a complete nearest-hit answer");
        }
        var one = new FormationSupportIndex(new[] { Piece(1, Quad(), 0) });
        var request = new FormationFootprintRequest(new[] { Down(), Down() }, Vector3D.UnitY);
        FormationFootprintResult aggregate = FormationSupportQueries.Footprint(one, request, new FormationSupportQueryLimits { MaxHits = 1 });
        Assert.Equal(FormationQueryStatus.CapacityExceeded, aggregate.Status, "probe two cannot reset the footprint hit budget");
        Assert.True(!aggregate.Supported && aggregate.Hits.Length == 0);
        Assert.Equal(1, aggregate.Diagnostics.Hits);
        FormationFootprintResult tooManyProbes = FormationSupportQueries.Footprint(one, request,
            new FormationSupportQueryLimits { MaxProbes = 1 });
        Assert.Equal(FormationQueryStatus.CapacityExceeded, tooManyProbes.Status);
        Assert.Equal(0, tooManyProbes.Diagnostics.VisitedNodes, "probe count is refused before traversal");
        Assert.True(!tooManyProbes.Supported && tooManyProbes.Hits.Length == 0);
    }

    [Test]
    public static void FormationSupportBuildBudgetsAndCancellationFailExplicitly()
    {
        FormationPiece piece = Piece(1, Quad(), 0);
        foreach (FormationSupportIndexLimits limits in new[] {
            new FormationSupportIndexLimits { MaxPieces = 0 }, new FormationSupportIndexLimits { MaxTriangles = 1 },
            new FormationSupportIndexLimits { MaxNodes = 1 }, new FormationSupportIndexLimits { MaxEstimatedResidentBytes = 0 },
            new FormationSupportIndexLimits { MaxEstimatedScratchBytes = 0 } })
            Throws<FormationCapacityException>(() => _ = new FormationSupportIndex(new[] { piece }, limits));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => _ = new FormationSupportIndex(new[] { piece }, cancellation: cancellation.Token));
        var index = new FormationSupportIndex(new[] { piece });
        FormationRayResult result = FormationSupportQueries.Raycast(index, Down(), cancellation: cancellation.Token);
        Assert.Equal(FormationQueryStatus.Cancelled, result.Status);
        Assert.Equal(0, result.Hits.Length);
        Assert.Equal(0, result.Diagnostics.VisitedNodes);
        Assert.True(index.Diagnostics.EstimatedResidentBytes <= index.Diagnostics.ReservedResidentBytes);
    }

    [Test]
    public static void FormationSupportRejectsAmbiguousIdentitiesButAcceptsEqualIndependentGeometry()
    {
        FormationPiece first = Piece(1, Quad(), 0);
        Throws<FormationValidationException>(() => _ = new FormationSupportIndex(new[] { first, Piece(1, Quad("other"), 3) }));
        Throws<FormationValidationException>(() => _ = new FormationSupportIndex(new[] { first, Piece(2, Quad(reverse: true), 3) }));
        var valid = new FormationSupportIndex(new[] { first, Piece(2, Quad(), 3) });
        Assert.Equal(2, FormationSupportQueries.Raycast(valid, Down()).Hits.Length, "equal separate immutable catalog allocations are valid");
    }

    [Test]
    public static void FormationPhysicalDtosRejectMalformedGeometryAndDefaultTransforms()
    {
        Throws<FormationValidationException>(() => _ = new FormationGeometry("bad", 0, "rock", Vertices(),
            new[] { new GeometryTriangle(0, 0, 1, 0) }, new[] { Patch() }));
        Throws<FormationValidationException>(() => _ = new FormationGeometry("bad", 0, "rock", Vertices(),
            new[] { new GeometryTriangle(0, 1, 99, 0) }, new[] { Patch() }));
        Throws<FormationValidationException>(() => _ = new FormationGeometry("bad", 0, "rock", Vertices(),
            new[] { new GeometryTriangle(0, 1, 2, 0) }, new[] { Patch(), Patch() }));
        Throws<FormationValidationException>(() => _ = new FormationPiece(new FormationPieceId(new FormationId(1, 0), 0), "face", Quad(), default));
        var index = new FormationSupportIndex(Array.Empty<FormationPiece>());
        Throws<FormationValidationException>(() => FormationSupportQueries.Raycast(index, default));
        Throws<FormationValidationException>(() => FormationSupportQueries.Raycast(index, Down() with { MinDistance = 21 }));
        Assert.Equal(FormationQueryStatus.Complete, FormationSupportQueries.Raycast(index, Down()).Status);
        var tiny = new FormationGeometry("tiny", 0, "rock",
            new[] { Vector3D.Zero, new Vector3D(0, 0, 1e-80), new Vector3D(1e-80, 0, 0) },
            new[] { new GeometryTriangle(0, 1, 2, 0) }, new[] { Patch() });
        FormationRayResult tinyHit = FormationSupportQueries.Raycast(new FormationSupportIndex(new[] { Piece(1, tiny, 0) }),
            new FormationRayRequest(new Vector3D(2e-81, 1, 2e-81), -Vector3D.UnitY, 2));
        Assert.Equal(1, tinyHit.Hits.Length, "a positive subnormal squared area still yields a valid support hit");
        Assert.Equal(Vector3D.UnitY, tinyHit.Hits[0].Normal, "normalization scales before squaring tiny cross products");
    }

    [Test]
    public static void FormationRecordsCopyGuidesAndPreserveRejectedSlotIdentity()
    {
        FormationId id = new(1, 0);
        Vector3D[] guide = { new(-1, 0, 0), new(1, 0, 0) };
        FormationPiece[] pieces = { Piece(1, Quad(), 0, slot: 4), Piece(1, Quad(), 0, slot: 0) };
        var bounds = new FormationBounds(new Vector3D(-10, -10, -10), new Vector3D(10, 10, 10));
        FormationRejection[] rejected = { new(2, "cap", FormationRejectionReason.Contact, bounds) };
        var record = new FormationRecord(id, "synthetic", 0, "rock", guide, guide, guide, pieces, rejected, bounds, new FormationCoverage(10, 6, 1));
        guide[0] = Vector3D.Zero; pieces[0] = Piece(1, Quad(), 0, slot: 9); rejected[0] = new(7, "base", FormationRejectionReason.Coverage, bounds);
        Assert.Equal(new Vector3D(-1, 0, 0), record.Crest[0]);
        Assert.Equal(0, record.Pieces[0].Id.Slot);
        Assert.Equal(4, record.Pieces[1].Id.Slot, "accepted slots are sorted, never renumbered after rejection");
        Assert.Equal(2, record.Rejections[0].Slot);
        Assert.Near(3, record.Coverage.UncoveredArea, 0);
        Throws<FormationValidationException>(() => _ = new FormationRecord(id, "bad", 0, "rock", guide, guide, guide,
            new[] { Piece(1, Quad(), 0, slot: 2) }, new[] { new FormationRejection(2, "cap", FormationRejectionReason.Contact, bounds) }, bounds, default));
    }

    private static FormationRayRequest Down() => new(new Vector3D(0.25, 10, 0), -Vector3D.UnitY, 20);
    private static CliffSupportDefinition Definition(FormationGeometry geometry, params int[][] loops)
    {
        var boundaries = new CliffPatchBoundary[loops.Length];
        for (int i = 0; i < loops.Length; i++) boundaries[i] = new CliffPatchBoundary(loops[i]);
        var maps = new CliffLodTriangleMap[geometry.TriangleCount];
        for (int i = 0; i < maps.Length; i++) maps[i] = new CliffLodTriangleMap(i, i);
        string digest = new('A', 64);
        return new CliffSupportDefinition(geometry, digest, digest, 0.5, 2,
            new[] { new CliffPatchDomain("ground", boundaries) }, Array.Empty<CliffContactDomain>(), new[] {
                new CliffLodBinding("lod0", digest, geometry, maps, 0, 0.99),
                new CliffLodBinding("lod1", digest, geometry, maps, 0, 0.99) });
    }
    private static Vector3D[] Vertices() => new[] { new Vector3D(-2, 0, -2), new Vector3D(-2, 0, 2), new Vector3D(2, 0, 2), new Vector3D(2, 0, -2) };
    private static SupportPatch Patch() => new("ground", "soil", SupportCapabilities.Walkable | SupportCapabilities.Plantable);
    private static FormationGeometry Quad(string key = "quad", bool reverse = false) => new(key, 1, "rock", Vertices(),
        reverse ? new[] { new GeometryTriangle(0, 2, 1, 0), new GeometryTriangle(0, 3, 2, 0) }
            : new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) }, new[] { Patch() });
    private static FormationPiece Piece(ulong owner, FormationGeometry geometry, double height, int slot = 0) =>
        new(new FormationPieceId(new FormationId(owner, 0), slot), "face", geometry,
            new FormationTransform(new Vector3D(0, height, 0), Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 1));
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }

    // Preserve the repository's assertions while supplying useful source expressions for simple comparisons.
    private static class Assert
    {
        public static void True(bool condition, string? message = null, [CallerArgumentExpression(nameof(condition))] string expression = "") =>
            global::Hollow.Tests.Assert.True(condition, message ?? expression);
        public static void Equal<T>(T expected, T actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string expression = "") =>
            global::Hollow.Tests.Assert.Equal(expected, actual, message ?? expression);
        public static void Near(double expected, double actual, double tolerance, string? message = null,
            [CallerArgumentExpression(nameof(actual))] string expression = "") =>
            global::Hollow.Tests.Assert.Near(expected, actual, tolerance, message ?? expression);
    }
}
