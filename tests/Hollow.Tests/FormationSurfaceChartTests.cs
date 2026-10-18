using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationSurfaceChartTests
{
    private static readonly FormationChartBasis Basis = new(Vector3D.Zero, Vector3D.UnitX, -Vector3D.UnitZ);
    private static readonly string SyntheticDigest = new('a', 64);

    [Test]
    public static void SurfaceChartQualifiesAnActualNonplanarCrownWithoutFlatteningIt()
    {
        CliffSupportDefinition definition = Crown();
        FormationChartQualificationResult result = FormationSurfaceChart.Qualify(definition, "crown", Basis);
        Assert.Equal(FormationChartQualificationStatus.Qualified, result.Status, "positive injective graph qualifies");
        FormationSurfaceChart chart = result.Chart!;
        Assert.Equal(definition, chart.Definition, "chart retains the qualified physical definition");
        Assert.Equal(5, chart.ProjectedVertices.Length, "all source vertex ordinals are preserved");
        Assert.True(chart.Triangles.SequenceEqual(new[] { 0, 1, 2, 3 }), "source triangle ordinals remain stable");
        Assert.Equal(4, chart.BoundaryEdges.Length, "the real outer boundary is retained");
        Assert.Equal(new Vector3D(0, 0.5, 0), chart.Geometry.Vertices[4], "qualification does not flatten the raised centre");
        Assert.Equal(new FormationChartPoint(0, 0), chart.ProjectedVertices[4], "the raised centre has an explicit chart projection");
        Assert.True(chart.EstimatedResidentBytes >= definition.EstimatedResidentBytes, "retained physical and LOD definitions count toward memory");
        Assert.True(result.Diagnostics.PairChecks > 0 && result.Diagnostics.PredicateTests > 0, "qualification actually checks the domain");
        var (index, _, anchor) = Instance(definition, Vector3D.Zero);
        FormationChartDiskResult supported = Query(index, chart, anchor, 0.4);
        Assert.True(supported.Supported, "the continuous disk crosses the crown's noncoplanar triangles");
        Assert.Equal(4, supported.Diagnostics.OverlappingTriangles, "all four noncoplanar crown facets contribute to the disk");
        Assert.Equal(anchor, supported.Anchor!.Value, "successful query preserves its authoritative anchor");
        // The old planar API requires exact membership of the represented world plane. Its triangle's
        // first authored vertex makes point - a exactly zero, isolating the patch-planarity check.
        GeometryTriangle selected = definition.Geometry.Triangles[anchor.Primitive.Triangle];
        SupportHit vertexAnchor = Copy(anchor, point: definition.Geometry.Vertices[selected.A], barycentric: new Vector3D(1, 0, 0));
        FormationDiskResult planar = FormationSupportQueries.ContainsDisk(index, definition, new FormationDiskRequest(vertexAnchor, 0.4));
        Assert.Equal(FormationDiskQualification.NonPlanarPatch, planar.Qualification, "the old planar contract remains strict");
        Assert.True(planar.Status == FormationQueryStatus.Complete && !planar.Supported && planar.Anchor == null,
            "the completed planar refusal cannot publish support");
    }

    [Test]
    public static void SurfaceChartRetainsHoleBoundariesAndRejectsDiskContactWithThem()
    {
        CliffSupportDefinition definition = Ring();
        FormationSurfaceChart chart = Chart(definition);
        Assert.Equal(8, chart.BoundaryEdges.Length, "outer boundary and inner hole both survive qualification");
        var (index, _, anchor) = Instance(definition, new Vector3D(-1.5, 0, 0));
        Assert.True(Query(index, chart, anchor, 0.25).Supported, "a disk inside the surrounding shelf is supported");
        foreach (double radius in new[] { 0.5, 0.6 })
        {
            FormationChartDiskResult result = Query(index, chart, anchor, radius);
            Assert.Equal(FormationChartDiskQualification.BoundaryCrossing, result.Qualification, "touching or crossing the hole is rejected");
            Assert.True(!result.Supported && result.Anchor == null, "a hole rejection publishes no successful anchor");
        }
        FormationRayResult empty = FormationSupportQueries.Raycast(index, new FormationRayRequest(new Vector3D(0, 4, 0), -Vector3D.UnitY, 8));
        Assert.Equal(0, empty.Hits.Length, "the physical hole was not filled to make the chart pass");
    }

    [Test]
    public static void SurfaceChartRetainsSplitCoincidentVertexFansAsExclusions()
    {
        CliffSupportDefinition definition = TouchingFans();
        FormationSurfaceChart chart = Chart(definition);
        Assert.Equal(definition.Geometry.Vertices[2], definition.Geometry.Vertices[4], "source pinch duplicates only the vertex identity");
        Assert.Equal(chart.ProjectedVertices[2], chart.ProjectedVertices[4], "the pinch remains at the same projected location");
        Assert.Equal(8, chart.ProjectedVertices.Length, "qualification does not weld split source fans");
        Assert.Equal(8, chart.BoundaryEdges.Length, "both fan boundaries remain query exclusions");
        var (index, _, safe) = Instance(definition, new Vector3D(-1, 0, -1));
        Assert.True(Query(index, chart, safe, 0.25).Supported, "a disk inside one fan is permitted");
        SupportHit nearPinch = Anchor(index, FormationTransform.Identity, new Vector3D(-0.1, 0, -0.1));
        Assert.Equal(FormationChartDiskQualification.BoundaryCrossing, Query(index, chart, nearPinch, 0.15).Qualification,
            "a disk cannot bridge the excluded pinch sectors");
        FormationChartQualificationResult stacked = FormationSurfaceChart.Qualify(TouchingFans(secondHeight: 1), "crown", Basis);
        Assert.Equal(FormationChartQualificationStatus.NonInjectiveProjection, stacked.Status,
            "a shared projection at different physical heights is not a split source pinch");
        Assert.True(stacked.Chart == null, "noninjective qualification publishes no chart");
    }

    [Test]
    public static void SurfaceChartRejectsReversedAndEdgeOnProjection()
    {
        CliffSupportDefinition definition = Crown();
        FormationChartQualificationResult reversed = FormationSurfaceChart.Qualify(definition, "crown",
            new FormationChartBasis(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitZ));
        Assert.Equal(FormationChartQualificationStatus.NonPositiveProjection, reversed.Status, "back-facing projection is not graph support");
        var wall = Definition(new[] { new Vector3D(0, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1) },
            new[] { new GeometryTriangle(0, 1, 2, 0) }, new[] { new[] { 0, 1, 2 } });
        FormationChartQualificationResult edgeOn = FormationSurfaceChart.Qualify(wall, "crown", Basis);
        Assert.Equal(FormationChartQualificationStatus.DegenerateProjection, edgeOn.Status, "a vertical physical face has no positive projected area");
        Assert.True(reversed.Chart == null && edgeOn.Chart == null, "invalid projections publish no chart");
    }

    [Test]
    public static void SurfaceChartRejectsInteriorOverlapAndCrossedProjectedLoops()
    {
        var nested = Definition(new[] { new Vector3D(-3, 0, -3), new Vector3D(-3, 0, 3), new Vector3D(3, 0, -3),
            new Vector3D(-2, 1, -2), new Vector3D(-2, 1, -1), new Vector3D(-1, 1, -2) },
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(3, 4, 5, 0) },
            new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 } });
        FormationChartQualificationResult interior = FormationSurfaceChart.Qualify(nested, "crown", Basis);
        Assert.Equal(FormationChartQualificationStatus.NonInjectiveProjection, interior.Status,
            "strict triangle-interior overlap is caught even without intersecting boundary edges");
        Vector3D[] crossedVertices = Quad(-2, 1, -1, 1, 0).Concat(Quad(-1, 2, -2, 2, 1)).ToArray();
        var crossed = Definition(crossedVertices, TwoQuads(), new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 } });
        FormationChartQualificationResult crossing = FormationSurfaceChart.Qualify(crossed, "crown", Basis);
        Assert.Equal(FormationChartQualificationStatus.BoundaryCrossing, crossing.Status, "projected boundary loops cannot cross");
        Assert.True(interior.Chart == null && crossing.Chart == null, "ambiguous surfaces never publish partial charts");
    }

    [Test]
    public static void SurfaceChartPreservesExactProjectionSignWhenDoubleProductsUnderflow()
    {
        // Physical cross magnitude is 1e-100, while the positive projected determinant is 1e-400.
        // Qualification concerns represented-coordinate topology; its huge guard precludes a useful disk claim.
        var definition = Definition(new[] { Vector3D.Zero, new Vector3D(0, 1e100, 1e-200), new Vector3D(1e-200, 0, 0) },
            new[] { new GeometryTriangle(0, 1, 2, 0) }, new[] { new[] { 0, 1, 2 } });
        FormationChartQualificationResult positive = FormationSurfaceChart.Qualify(definition, "crown", Basis);
        Assert.Equal(FormationChartQualificationStatus.Qualified, positive.Status, "exact projected area retains its positive sign below floating-product range");
        Assert.True(positive.Chart!.NumericalGuardLocal > 1, "qualification keeps the large numerical uncertainty explicit");
        FormationChartQualificationResult negative = FormationSurfaceChart.Qualify(definition, "crown",
            new FormationChartBasis(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitZ));
        Assert.Equal(FormationChartQualificationStatus.NonPositiveProjection, negative.Status, "reversing the same tiny determinant retains its negative sign");
        Assert.True(negative.Chart == null, "an underflowed negative projection is never promoted to support");
    }

    [Test]
    public static void SurfaceChartQualificationHonoursAggregateBudgetsAndCancellation()
    {
        CliffSupportDefinition definition = Crown();
        foreach (FormationChartQualificationLimits limits in new[]
        {
            new FormationChartQualificationLimits { MaxVertices = 0 },
            new FormationChartQualificationLimits { MaxSourceTriangles = 0 },
            new FormationChartQualificationLimits { MaxSourcePatches = 0 },
            new FormationChartQualificationLimits { MaxPatchTriangles = 0 },
            new FormationChartQualificationLimits { MaxBoundaryEdges = 0 },
            new FormationChartQualificationLimits { MaxPairChecks = 0 },
            new FormationChartQualificationLimits { MaxPredicateTests = 0 },
            new FormationChartQualificationLimits { MaxEstimatedResidentBytes = 0 },
            new FormationChartQualificationLimits { MaxEstimatedScratchBytes = 0 },
        })
        {
            FormationChartQualificationResult result = FormationSurfaceChart.Qualify(definition, "crown", Basis, limits);
            Assert.Equal(FormationChartQualificationStatus.CapacityExceeded, result.Status, "qualification capacity refusal is explicit");
            Assert.True(result.Chart == null, "capacity failure publishes no chart");
        }
        FormationChartQualificationResult complete = FormationSurfaceChart.Qualify(definition, "crown", Basis);
        Assert.True(complete.Diagnostics.PairChecks > 1, "fixture has several boundary/triangle pairs");
        FormationChartQualificationResult limited = FormationSurfaceChart.Qualify(definition, "crown", Basis,
            new FormationChartQualificationLimits { MaxPairChecks = complete.Diagnostics.PairChecks - 1 });
        Assert.Equal(FormationChartQualificationStatus.CapacityExceeded, limited.Status, "one shared pair budget covers the entire qualification");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        FormationChartQualificationResult cancelled = FormationSurfaceChart.Qualify(definition, "crown", Basis, cancellation: cancellation.Token);
        Assert.Equal(FormationChartQualificationStatus.Cancelled, cancelled.Status, "pre-cancelled qualification does not start publishing");
        Assert.True(cancelled.Chart == null, "cancelled chart is absent");
    }

    [Test]
    public static void SurfaceChartQueryValidatesAnchorDefinitionAndQualifiedScale()
    {
        CliffSupportDefinition definition = Crown();
        FormationSurfaceChart chart = Chart(definition);
        var (index, _, anchor) = Instance(definition, Vector3D.Zero);
        foreach (SupportHit spoofed in new[]
        {
            Copy(anchor, point: anchor.Point + Vector3D.UnitY * 0.1),
            Copy(anchor, normal: Vector3D.UnitY), Copy(anchor, barycentric: new Vector3D(1, 0, 0)),
            Copy(anchor, profile: "wrong"), Copy(anchor, patch: "wrong"), Copy(anchor, family: "wrong"),
            Copy(anchor, primitive: new PhysicalPrimitiveId(new FormationPieceId(new FormationId(999, 0), 0), "crown-mesh", 1, 0)),
        })
        {
            FormationChartDiskResult result = Query(index, chart, spoofed, 0.4);
            Assert.Equal(FormationChartDiskQualification.InvalidAnchor, result.Qualification, "spoofed support is not authoritative");
            Assert.True(result.Anchor == null, "invalid anchor is not echoed as a success");
        }
        var (changedIndex, _, changedAnchor) = Instance(Crown(height: 0.75), Vector3D.Zero);
        Assert.Equal(FormationChartDiskQualification.DefinitionMismatch, Query(changedIndex, chart, changedAnchor, 0.4).Qualification,
            "same geometry key/revision cannot conceal changed physical triangles");
        var oversized = new FormationTransform(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 3);
        var (scaledIndex, _, scaledAnchor) = Instance(definition, Vector3D.Zero, oversized);
        Assert.Equal(FormationChartDiskQualification.ScaleOutsideQualification, Query(scaledIndex, chart, scaledAnchor, 0.4).Qualification,
            "a valid physical transform still needs asset qualification at that scale");
    }

    [Test]
    public static void SurfaceChartQueryChecksPatchCapabilitiesSlopeAndWholeTriangleHeight()
    {
        CliffSupportDefinition definition = Crown();
        FormationSurfaceChart chart = Chart(definition);
        var (index, _, anchor) = Instance(definition, Vector3D.Zero);
        CliffSupportDefinition uneven = Crown(raisedCorner: 2);
        var (unevenIndex, _, gentleAnchor) = Instance(uneven, Vector3D.Zero);
        Assert.True(Vector3D.Dot(gentleAnchor.Normal, Vector3D.UnitY) > 0.9, "the selected anchor alone passes the slope policy");
        FormationChartDiskResult slope = FormationSurfaceChartQueries.ContainsDisk(unevenIndex, Chart(uneven),
            new FormationChartDiskRequest(gentleAnchor, 0.4, Vector3D.UnitY, MinimumNormalDot: 0.9, MaximumHeightSpread: 5));
        Assert.Equal(FormationChartDiskQualification.SlopeExceeded, slope.Qualification, "a steeper neighbouring facet rejects the disk despite its valid anchor");
        FormationChartDiskResult height = FormationSurfaceChartQueries.ContainsDisk(index, chart,
            new FormationChartDiskRequest(anchor, 0.01, Vector3D.UnitY, MinimumNormalDot: 0.9, MaximumHeightSpread: 0.49));
        Assert.Equal(FormationChartDiskQualification.HeightSpreadExceeded, height.Qualification,
            "height bounds conservatively include every vertex of an overlapping triangle");
        CliffSupportDefinition bare = Crown(capabilities: SupportCapabilities.Walkable);
        var (bareIndex, _, bareAnchor) = Instance(bare, Vector3D.Zero);
        FormationChartDiskResult missing = FormationSurfaceChartQueries.ContainsDisk(bareIndex, Chart(bare),
            new FormationChartDiskRequest(bareAnchor, 0.4, Vector3D.UnitY, MaximumHeightSpread: 1, RequiredCapabilities: SupportCapabilities.Soil));
        Assert.Equal(FormationChartDiskQualification.MissingCapabilities, missing.Qualification, "slope does not grant soil capability");
        var split = Definition(Quad(-2, 2, -2, 2, 0), new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 1) },
            new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, splitPatches: true);
        var (splitIndex, _, otherPatchAnchor) = Instance(split, new Vector3D(1, 0, 0));
        Assert.Equal(FormationChartDiskQualification.PatchMismatch, Query(splitIndex, Chart(split), otherPatchAnchor, 0.1).Qualification,
            "a valid hit on another patch cannot use this chart");
    }

    [Test]
    public static void SurfaceChartQueryUsesWorldMetresAndProperTransformedUp()
    {
        CliffSupportDefinition definition = Crown();
        FormationSurfaceChart chart = Chart(definition);
        var transform = new FormationTransform(new Vector3D(8, 16, 32), Vector3D.UnitY, -Vector3D.UnitX, Vector3D.UnitZ, 2);
        var (index, _, anchor) = Instance(definition, Vector3D.Zero, transform);
        FormationChartDiskResult valid = Query(index, chart, anchor, 0.8, up: -Vector3D.UnitX, maximumHeight: 2);
        Assert.True(valid.Supported, "rotated/scaled crown supports the same local disk");
        Assert.Equal(anchor, valid.Anchor!.Value, "world-space input anchor is preserved exactly");
        // Allow 64 machine-epsilon units at this fixture's maximum coordinate magnitude (32 m)
        // for the ray intersection, independently of exact returned-anchor preservation.
        Assert.Near(0, (valid.Anchor!.Value.Point - transform.Transform(new Vector3D(0, 0.5, 0))).Length,
            64 * 2.2204460492503131e-16 * 32, "ray anchor agrees with the theoretical transformed crown vertex");
        FormationChartDiskResult outerEdge = Query(index, chart, anchor, 4, up: -Vector3D.UnitX, maximumHeight: 2);
        Assert.Equal(FormationChartDiskQualification.BoundaryCrossing, outerEdge.Qualification, "world radius converts to local scale before boundary checks");
        FormationChartDiskResult height = Query(index, chart, anchor, 0.8, up: -Vector3D.UnitX, maximumHeight: 0.99);
        Assert.Equal(FormationChartDiskQualification.HeightSpreadExceeded, height.Qualification, "height limits remain world metres after scaling");
    }

    [Test]
    public static void SurfaceChartQueriesDoNotPublishPartialResultsOnBudgetOrCancellation()
    {
        CliffSupportDefinition definition = Crown();
        FormationSurfaceChart chart = Chart(definition);
        var (index, _, anchor) = Instance(definition, Vector3D.Zero);
        var request = new FormationChartDiskRequest(anchor, 0.4, Vector3D.UnitY, MaximumHeightSpread: 1);
        foreach (FormationChartQueryLimits limits in new[]
        {
            new FormationChartQueryLimits { MaxVisitedNodes = 0 },
            new FormationChartQueryLimits { MaxTestedTriangles = 0 },
            new FormationChartQueryLimits { MaxBoundaryEdges = 0 },
            new FormationChartQueryLimits { MaxPredicateTests = 0 },
            new FormationChartQueryLimits { MaxEstimatedScratchBytes = 0 },
            new FormationChartQueryLimits { MaxEstimatedResultBytes = 0 },
        })
        {
            FormationChartDiskResult result = FormationSurfaceChartQueries.ContainsDisk(index, chart, request, limits);
            Assert.Equal(FormationQueryStatus.CapacityExceeded, result.Status, "query capacity refusal is explicit");
            Assert.True(!result.Supported && result.Anchor == null, "capacity failure cannot look like supported geometry");
        }
        var (independentIndex, _, independentAnchor) = Instance(Crown(), Vector3D.Zero);
        foreach (FormationChartQueryLimits limits in new[]
        {
            new FormationChartQueryLimits { MaxComparedVertices = 0 },
            new FormationChartQueryLimits { MaxComparedTriangles = 0 },
            new FormationChartQueryLimits { MaxComparedPatches = 0 },
        })
        {
            FormationChartDiskResult result = FormationSurfaceChartQueries.ContainsDisk(independentIndex, chart,
                request with { Anchor = independentAnchor }, limits);
            Assert.Equal(FormationQueryStatus.CapacityExceeded, result.Status, "independently allocated definitions need bounded full comparison");
            Assert.True(result.Anchor == null, "incomplete definition comparison publishes no successful anchor");
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        FormationChartDiskResult cancelled = FormationSurfaceChartQueries.ContainsDisk(index, chart, request, cancellation: cancellation.Token);
        Assert.Equal(FormationQueryStatus.Cancelled, cancelled.Status, "query cancellation is explicit");
        Assert.True(!cancelled.Supported && cancelled.Anchor == null, "cancelled query has no successful anchor");
    }

    [Test]
    public static void SurfaceChartAndQueriesAreImmutableAcrossConcurrentReads()
    {
        CliffSupportDefinition definition = Crown();
        FormationSurfaceChart chart = Chart(definition);
        var (index, _, anchor) = Instance(definition, Vector3D.Zero);
        FormationChartPoint[] points = chart.ProjectedVertices.ToArray();
        FormationChartEdge[] edges = chart.BoundaryEdges.ToArray();
        FormationChartDiskResult expected = Query(index, chart, anchor, 0.4);
        Parallel.For(0, 32, i =>
        {
            FormationChartDiskResult result = Query(index, chart, anchor, (i & 1) == 0 ? 0.4 : 2);
            Assert.Equal((i & 1) == 0 ? FormationChartDiskQualification.Supported : FormationChartDiskQualification.BoundaryCrossing,
                result.Qualification, "alternating query order cannot mutate the chart");
            if ((i & 1) == 0)
            {
                Assert.Equal(expected.Anchor, result.Anchor, "concurrent support retains exact identity and values");
                Assert.Equal(expected.Diagnostics, result.Diagnostics, "query work counters are local");
            }
        });
        Assert.True(chart.ProjectedVertices.SequenceEqual(points) && chart.BoundaryEdges.SequenceEqual(edges), "shared projected topology is unchanged");
        Assert.True(typeof(FormationSurfaceChart).GetProperties().All(p => p.SetMethod == null), "chart properties are get-only");
    }

    [Test]
    public static void SurfaceChartRejectsMalformedBasesRequestsAndUnknownPatches()
    {
        Reject<FormationValidationException>(() => new FormationChartBasis(Vector3D.Zero, Vector3D.Zero, -Vector3D.UnitZ));
        Reject<FormationValidationException>(() => new FormationChartBasis(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitX));
        Reject<FormationValidationException>(() => new FormationChartBasis(new Vector3D(double.NaN, 0, 0), Vector3D.UnitX, -Vector3D.UnitZ));
        CliffSupportDefinition definition = Crown();
        FormationChartQualificationResult missing = FormationSurfaceChart.Qualify(definition, "missing", Basis);
        Assert.Equal(FormationChartQualificationStatus.MissingPatch, missing.Status, "unknown physical patch is explicit");
        Assert.True(missing.Chart == null, "unknown patch has no chart");
        FormationSurfaceChart chart = Chart(definition);
        var (index, _, anchor) = Instance(definition, Vector3D.Zero);
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1.0, 0.0, double.Epsilon })
            Reject<FormationValidationException>(() => Query(index, chart, anchor, invalid));
        Reject<FormationValidationException>(() => FormationSurfaceChartQueries.ContainsDisk(index, chart,
            new FormationChartDiskRequest(anchor, 0.4, Vector3D.Zero)));
        Reject<FormationValidationException>(() => FormationSurfaceChartQueries.ContainsDisk(index, chart,
            new FormationChartDiskRequest(anchor, 0.4, Vector3D.UnitY, MinimumNormalDot: 1.1)));
        Reject<FormationValidationException>(() => FormationSurfaceChartQueries.ContainsDisk(index, chart,
            new FormationChartDiskRequest(anchor, 0.4, Vector3D.UnitY, MaximumHeightSpread: -1)));
        Reject<FormationValidationException>(() => FormationSurfaceChartQueries.ContainsDisk(index, chart, default));
    }

    private static FormationChartDiskResult Query(FormationSupportIndex index, FormationSurfaceChart chart, SupportHit anchor,
        double radius, Vector3D? up = null, double maximumHeight = 1) => FormationSurfaceChartQueries.ContainsDisk(index, chart,
            new FormationChartDiskRequest(anchor, radius, up ?? Vector3D.UnitY, MinimumNormalDot: 0.9, MaximumHeightSpread: maximumHeight));
    private static FormationSurfaceChart Chart(CliffSupportDefinition definition)
    {
        FormationChartQualificationResult result = FormationSurfaceChart.Qualify(definition, "crown", Basis);
        Assert.Equal(FormationChartQualificationStatus.Qualified, result.Status, "fixture graph must qualify");
        return result.Chart!;
    }
    private static (FormationSupportIndex Index, FormationPiece Piece, SupportHit Anchor) Instance(CliffSupportDefinition definition,
        Vector3D localPoint, FormationTransform? transform = null)
    {
        FormationTransform pose = transform ?? FormationTransform.Identity;
        var piece = new FormationPiece(new FormationPieceId(new FormationId(101, 2), 3), "crown", definition.Geometry, pose);
        var index = new FormationSupportIndex(new[] { piece });
        return (index, piece, Anchor(index, pose, localPoint));
    }
    private static SupportHit Anchor(FormationSupportIndex index, FormationTransform transform, Vector3D localPoint)
    {
        FormationRayResult result = FormationSupportQueries.Raycast(index, new FormationRayRequest(
            transform.Transform(localPoint + Vector3D.UnitY * 4), -transform.Up, 8 * transform.Scale));
        Assert.True(result.Status == FormationQueryStatus.Complete && result.Hits.Length > 0, "fixture must produce an actual physical support hit");
        return result.Hits[0];
    }
    private static SupportHit Copy(SupportHit source, PhysicalPrimitiveId? primitive = null, Vector3D? point = null,
        Vector3D? normal = null, Vector3D? barycentric = null, string? patch = null, string? profile = null, string? family = null) =>
        new(primitive ?? source.Primitive, point ?? source.Point, normal ?? source.Normal, barycentric ?? source.Barycentric, source.Distance,
            patch ?? source.PatchKey, family ?? source.FamilyKey, profile ?? source.SubstrateProfileKey, source.Capabilities);
    private static CliffSupportDefinition Crown(double height = 0.5,
        SupportCapabilities capabilities = SupportCapabilities.Walkable | SupportCapabilities.Plantable | SupportCapabilities.Soil,
        double raisedCorner = 0)
    {
        Vector3D[] vertices = Quad(-2, 2, -2, 2, 0).Concat(new[] { new Vector3D(0, height, 0) }).ToArray();
        vertices[2] += Vector3D.UnitY * raisedCorner;
        return Definition(vertices,
            new[] { new GeometryTriangle(0, 1, 4, 0), new GeometryTriangle(1, 2, 4, 0),
                new GeometryTriangle(2, 3, 4, 0), new GeometryTriangle(3, 0, 4, 0) }, new[] { new[] { 0, 1, 2, 3 } }, capabilities);
    }
    private static CliffSupportDefinition Ring() => Definition(Quad(-3, 3, -3, 3, 0).Concat(Quad(-1, 1, -1, 1, 0)).ToArray(),
        new[] { new GeometryTriangle(0, 1, 5, 0), new GeometryTriangle(0, 5, 4, 0), new GeometryTriangle(1, 2, 6, 0), new GeometryTriangle(1, 6, 5, 0),
            new GeometryTriangle(2, 3, 7, 0), new GeometryTriangle(2, 7, 6, 0), new GeometryTriangle(3, 0, 4, 0), new GeometryTriangle(3, 4, 7, 0) },
        new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 7, 6, 5 } });
    private static CliffSupportDefinition TouchingFans(double secondHeight = 0) => Definition(
        Quad(-2, 0, -2, 0, 0).Concat(Quad(0, 2, 0, 2, secondHeight)).ToArray(), TwoQuads(),
        new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 } });
    private static Vector3D[] Quad(double x0, double x1, double z0, double z1, double height) =>
        new[] { new Vector3D(x0, height, z0), new Vector3D(x0, height, z1), new Vector3D(x1, height, z1), new Vector3D(x1, height, z0) };
    private static GeometryTriangle[] TwoQuads() => new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0),
        new GeometryTriangle(4, 5, 6, 0), new GeometryTriangle(4, 6, 7, 0) };
    private static CliffSupportDefinition Definition(Vector3D[] vertices, GeometryTriangle[] triangles, int[][] loops,
        SupportCapabilities capabilities = SupportCapabilities.Walkable | SupportCapabilities.Plantable | SupportCapabilities.Soil,
        bool splitPatches = false)
    {
        SupportPatch[] patches = splitPatches ? new[] { new SupportPatch("crown", "soil", capabilities), new SupportPatch("other", "soil", capabilities) }
            : new[] { new SupportPatch("crown", "soil", capabilities) };
        var mesh = new FormationGeometry("crown-mesh", 1, "rock", vertices, triangles, patches);
        CliffPatchDomain[] domains = splitPatches
            ? new[] { new CliffPatchDomain("crown", new[] { new CliffPatchBoundary(loops[0]) }), new CliffPatchDomain("other", new[] { new CliffPatchBoundary(loops[1]) }) }
            : new[] { new CliffPatchDomain("crown", loops.Select(loop => new CliffPatchBoundary(loop))) };
        CliffLodTriangleMap[] maps = Enumerable.Range(0, triangles.Length).Select(i => new CliffLodTriangleMap(i, i)).ToArray();
        return new CliffSupportDefinition(mesh, SyntheticDigest, SyntheticDigest, 0.5, 2, domains, Array.Empty<CliffContactDomain>(),
            new[] { new CliffLodBinding("near", SyntheticDigest, mesh, maps, 0, 0.999999999), new CliffLodBinding("far", SyntheticDigest, mesh, maps, 0, 0.999999999) });
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
