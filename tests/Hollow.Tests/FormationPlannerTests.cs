using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationPlannerTests
{
    [Test]
    public static void FormationPlannerIdentityPackingRoundTripsWithoutHashOrAcceptanceOrdinal()
    {
        var ids = new HashSet<FormationId>();
        for (int face = 0; face < 6; face++)
        for (int level = 1; level <= 7; level++)
        for (int a = 0; a < 4; a++)
        for (int b = a + 1; b < 4; b++)
        foreach (int revision in new[] { 0, int.MaxValue })
        foreach (ushort domain in new ushort[] { 0, ushort.MaxValue })
        {
            var identity = new FormationContourIdentity(face, level, (1 << level) - 1, 0, a, b, domain, revision, 65535);
            Assert.True(ids.Add(identity.Id), "Full bounded tuples have distinct packed identities.");
            Assert.Equal(identity, FormationContourIdentity.FromId(identity.Id), "Identity reverses exactly.");
            Assert.True(identity.FirstEdge.IsValid && identity.SecondEdge.IsValid, "Identity retains exact topology edges.");
        }
        var canonical = new FormationContourIdentity(0, 1, 0, 0, 0, 1, 0, 0, 0).Id;
        var swapped = new FormationId((canonical.Owner & ~((3UL << 20) | (3UL << 22))) | (1UL << 20), canonical.Section);
        Reject<FormationValidationException>(() => FormationContourIdentity.FromId(swapped));
        Reject<FormationValidationException>(() => FormationContourIdentity.FromId(new FormationId(canonical.Owner | (1UL << 63), 0)));
        Reject<FormationValidationException>(() => new FormationContourIdentity(0, 7, 128, 0, 0, 1, 0, 0, 0));
    }

    [Test]
    public static void FormationPlannerFlatSurveyOwnsSharedSeamVerticesAndAcceptsDrySentinel()
    {
        var terrain = new SyntheticTerrain(flat: true);
        FormationPlan plan = FormationPlanner.Build(terrain, Catalog(), Settings());
        Assert.Equal(0, plan.Entries.Length, "Flat terrain has no gradient contours.");
        Assert.Equal(6 * 16 * 16, plan.Diagnostics.SurveyCells, "Survey visits every 2x2 coarse subcell.");
        Assert.Equal(6 * 16 * 16 + 2, plan.Diagnostics.SurveyVertices, "Cube edges and corners have one canonical vertex owner.");
        // Observe the terrain's actual requests. The first level1 cell visits corner, edge centre, face centre.
        var observed = new SyntheticTerrain(flat: true);
        var coarse = new FormationPlannerSettings("recipe", 0, "test-ground", 2, 0.5, 10, 12, 1, 1, 100, Vector3D.UnitY, "crest", 0);
        FormationPlan coarsePlan = FormationPlanner.Build(observed, Catalog(), coarse);
        Assert.Equal(CubeSphere.Direction(0, 0, -1), observed.SurveyDirections[1], "Second survey request preserves the edge centre.");
        Assert.Equal(CubeSphere.Normal(0), observed.SurveyDirections[2], "Third survey request preserves the face centre.");
        for (int face = 0; face < 6; face++)
            Assert.True(observed.SurveyDirections.Contains(CubeSphere.Normal(face)), "Every face centre reaches terrain at its exact normal.");
        var expected = new Dictionary<CubeVertexKey, Vector3D>();
        for (int face = 0; face < 6; face++)
        for (int y = 0; y <= 2; y++)
        for (int x = 0; x <= 2; x++)
        {
            CubeVertexKey key = CubeTopology.VertexKey(face, 1, x, y);
            if (expected.ContainsKey(key)) continue; // First face is the canonical owner; other faces share its key.
            Vector3D direction = CubeSphere.Direction(face, -1 + x, -1 + y);
            expected.Add(key, direction);
            Assert.True(observed.SurveyDirections.Contains(direction), "Every canonical grid vertex reaches its physical location.");
        }
        Assert.Equal(26, expected.Count, "Six face centres, twelve edge centres and eight corners are distinct.");
        Assert.Equal(expected.Count, coarsePlan.Diagnostics.SurveyVertices, "Shared cube points are sampled once.");
        Assert.Equal(expected.Count, new HashSet<Vector3D>(observed.SurveyDirections).Count, "Distinct canonical points do not collapse physically.");
        Assert.True(plan.Diagnostics.ReservedScratchBytes <= 32L * 1024 * 1024, "Experimental scratch ceiling is explicit.");
        Assert.Equal(0, plan.QueryBounds(WorldBounds()).Records.Length, "Empty plan has an empty bounded query.");
        Reject<FormationValidationException>(() => FormationPlanner.Build(new SyntheticTerrain(flat: true, water: double.NaN), Catalog(), Settings()));
        Reject<FormationValidationException>(() => FormationPlanner.Build(new SyntheticTerrain(flat: true, water: double.PositiveInfinity), Catalog(), Settings()));
        Reject<FormationValidationException>(() => FormationPlanner.Build(new SyntheticTerrain(flat: true, finalized: false), Catalog(), Settings()));
    }

    [Test]
    public static void FormationPlannerFitsQualifiedChainsAndMeasuresPhysicalCoverage()
    {
        FormationPlan plan = FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings());
        Assert.True(plan.Entries.Length > 0, "Finite survey extracts actual gradient contours.");
        Assert.True(plan.Diagnostics.AcceptedPieces > 0, "Qualified synthetic wall fits a measured terrain band.");
        Assert.True(plan.Diagnostics.ContactSamples > 0 && plan.Diagnostics.CoverageSamples > 0, "Contact and coverage measurements are reported.");
        Assert.True(plan.Diagnostics.GeometryVertexVisits > 0 && plan.Diagnostics.TriangleTests > 0, "Candidate validation and physical coverage work are charged.");
        int physical = 0;
        foreach (FormationPlanEntry entry in plan.Entries)
        {
            Assert.Equal(entry.Identity.Id, entry.Record.Id, "Record uses its frozen full planning identity.");
            if (entry.Record.Pieces.Length == 0) continue;
            physical++;
            Assert.True(entry.Record.Coverage.IntendedArea > 0 && entry.Record.Coverage.CoveredArea / entry.Record.Coverage.IntendedArea >= 0.5,
                "Accepted geometry meets the recipe's measured coverage target.");
            Assert.Equal(0.0, entry.Record.Coverage.AllowedSoilArea, "No undeclared soil fill replaces missing physical coverage.");
        }
        Assert.Equal(physical, plan.QueryBounds(WorldBounds(), physicalOnly: true).Records.Length, "Plan query resolves physical owners.");
        Reject<FormationCapacityException>(() => plan.QueryBounds(WorldBounds(), limits: new FormationPlanQueryLimits { MaxResults = 0 }));
        Reject<FormationCapacityException>(() => plan.QueryBounds(WorldBounds(), limits: new FormationPlanQueryLimits { MaxVisitedNodes = 0 }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => plan.QueryBounds(WorldBounds(), cancellation: cancelled.Token));
    }

    [Test]
    public static void FormationPlannerRejectionsPreserveIdsAndNeverUseBoundsAsCoverage()
    {
        FormationPlan baseline = FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings());
        var sites = new[] { new FormationProtectedSite("protected", WorldBounds()) };
        FormationPlan protectedPlan = FormationPlanner.Build(new SyntheticTerrain(sites: sites), Catalog(), Settings());
        SameIds(baseline, protectedPlan);
        Assert.Equal(0, protectedPlan.Diagnostics.AcceptedPieces, "Conservative protected boxes prohibit physical pieces.");
        Assert.True(HasRejection(protectedPlan, FormationRejectionReason.ProtectedSite), "Protected rejection remains explicit.");
        sites[0] = new FormationProtectedSite("moved", new FormationBounds(new Vector3D(10000, 10000, 10000), new Vector3D(10001, 10001, 10001)));
        Assert.Equal(0, protectedPlan.QueryBounds(WorldBounds(), physicalOnly: true).Records.Length, "Published plan retains no live site callback/array.");

        FormationPlan unsupported = FormationPlanner.Build(new SyntheticTerrain(), Catalog(unmatchedContact: true), Settings());
        SameIds(baseline, unsupported);
        Assert.Equal(0, unsupported.Diagnostics.AcceptedPieces, "An unmatched required authored contact cannot be ignored.");
        Assert.True(HasRejection(unsupported, FormationRejectionReason.Contact), "Unmatched contact is recorded.");
        FormationPlan uncovered = FormationPlanner.Build(new SyntheticTerrain(), Catalog(thin: true), Settings());
        SameIds(baseline, uncovered);
        Assert.Equal(0, uncovered.Diagnostics.AcceptedPieces, "A narrow top strip does not cover the intended cliff face.");
        Assert.True(HasRejection(uncovered, FormationRejectionReason.Coverage), "Actual triangle misses produce coverage rejection.");
    }

    [Test]
    public static void FormationPlannerCanonicalSlotsRejectOverlapsAndExplicitAnchorRoleWins()
    {
        FormationPlan single = FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings());
        FormationPlan duplicate = FormationPlanner.Build(new SyntheticTerrain(), Catalog(duplicateSlot: true), Settings());
        SameIds(single, duplicate);
        Assert.True(duplicate.Diagnostics.AcceptedPieces > 0, "Earlier required canonical slots remain usable.");
        bool rejectedSecond = false;
        foreach (FormationPlanEntry entry in duplicate.Entries)
        {
            Assert.True(entry.Record.Pieces.Length <= 1, "Identical physical triangles are never accepted in two slots.");
            foreach (FormationRejection rejection in entry.Record.Rejections)
                rejectedSecond |= rejection.Slot == 7 && rejection.Reason == FormationRejectionReason.Clearance;
        }
        Assert.True(rejectedSecond, "Later optional duplicate slot is conservatively rejected.");
        FormationPlan renamed = FormationPlanner.Build(new SyntheticTerrain(), Catalog(anchorKey: "zz-anchor", extraValidContact: true), Settings());
        SameIds(single, renamed);
        Assert.Equal(single.Diagnostics.AcceptedPieces, renamed.Diagnostics.AcceptedPieces, "Authored anchor role wins over an earlier-keyed valid toe contact.");
        FormationPlan ambiguous = FormationPlanner.Build(new SyntheticTerrain(), Catalog(ambiguousAnchor: true), Settings());
        Assert.Equal(0, ambiguous.Diagnostics.AcceptedPieces, "Two contacts claiming the anchor role are refused explicitly.");
    }

    [Test]
    public static void FormationPlannerAdmissionCountsRejectedGeometryAndWholeRecordSamples()
    {
        var terrain = new SyntheticTerrain();
        Reject<FormationCapacityException>(() => FormationPlanner.Build(terrain, Catalog(), Settings(), new FormationPlannerLimits { MaxEstimatedScratchBytes = 0 }));
        Assert.Equal(0, terrain.HeightCalls, "Scratch reservation fails before terrain work.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => FormationPlanner.Build(terrain, Catalog(), Settings(), cancellation: cancelled.Token));
        Assert.Equal(0, terrain.HeightCalls, "Precancellation dispatches no terrain queries.");
        Reject<FormationCapacityException>(() => FormationPlanner.Build(terrain, Catalog(), Settings(), new FormationPlannerLimits { MaxTerrainSamples = 0 }));
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings(), new FormationPlannerLimits { MaxTriangleTests = 0 }));
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings(), new FormationPlannerLimits { MaxGeometryVertexVisits = 0 }));
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings(coverageAlong: 1, coverageDown: 1),
            new FormationPlannerLimits { MaxContactAndCoverageSamplesPerRecord = 3 }));
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings(), new FormationPlannerLimits { MaxRecords = 0 }));
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings(), new FormationPlannerLimits { MaxContourSegments = 0 }));
    }

    [Test]
    public static void FormationPlannerClosedContoursSplitBeforeFittingAndExtentIsFormationWide()
    {
        var settings = new FormationPlannerSettings("recipe", 0, "test-ground", 2, 0.2, 10, 12, 1, 1, 100, Vector3D.UnitY, "crest", 0);
        FormationPlan loops = FormationPlanner.Build(new SyntheticTerrain(cap: true), Catalog(section: 10000, extent: 10000, stations: 9), settings);
        Assert.True(loops.Diagnostics.Contours > 0, "Synthetic cap exposes a closed gradient contour.");
        foreach (FormationPlanEntry entry in loops.Entries)
            Assert.True(!entry.Record.Crest[0].Equals(entry.Record.Crest[entry.Record.Crest.Length - 1]), "Closed contour never becomes a zero-chord fit.");
        FormationPlan extent = FormationPlanner.Build(new SyntheticTerrain(), Catalog(extent: 20), Settings());
        Assert.Equal(0, extent.Diagnostics.AcceptedPieces, "Intended/combined formation extent is bounded.");
        Assert.True(HasRejection(extent, FormationRejectionReason.Extent), "Spatial extent failure is explicit.");
    }

    [Test]
    public static void FormationPlannerSparseLevelSevenSurveyChargesOnlyAdmittedTopology()
    {
        var core = new TileKey(4, 7, 64, 64);
        var region = SmallRegion(new[] { core });
        var expectedCells = OneRing(new[] { core });
        Assert.Equal(5, expectedCells.Count, "An interior one-ring survey is the core and four edge neighbors.");
        Assert.True(!region.ContainsCell(new TileKey(4, 7, 63, 63)), "One ring does not silently admit diagonal cells.");
        var terrain = new SyntheticTerrain(flat: true);
        var limits = new FormationPlannerLimits
        {
            MaxRecords = 16, MaxContourSegments = 128, MaxProtectedSites = 0,
            MaxTerrainSamples = 84, MaxEstimatedScratchBytes = 1024 * 1024
        };
        FormationPlan plan = FormationPlanner.Build(terrain, Catalog(), Settings(region: region), limits);
        AssertSurvey(plan, terrain, expectedCells);
        Assert.Equal(12, plan.Diagnostics.SurveyVertices, "The sparse cross has twelve shared grid vertices.");
        Assert.Equal(0, plan.Entries.Length, "Flat regional terrain publishes no invented formation.");
        Assert.True(plan.Diagnostics.ReservedScratchBytes <= limits.MaxEstimatedScratchBytes,
            "Level seven uses a sparse reservation below the dense six-face survey requirement.");
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(flat: true), Catalog(),
            Settings(region: region), limits with { MaxTerrainSamples = 83 }));
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(flat: true), Catalog(),
            Settings(region: region), limits with { MaxEstimatedScratchBytes = plan.Diagnostics.ReservedScratchBytes - 1 }));
    }

    [Test]
    public static void FormationPlannerRegionalSeamVerticesHaveOnePhysicalTerrainObservation()
    {
        var first = new TileKey(0, 7, 127, 64);
        TileKey second = CubeTopology.Neighbor(first, CubeEdge.UMax);
        Assert.True(first.Face != second.Face, "The fixture crosses an actual cube-face seam.");
        var source = new[] { second, first };
        var region = SmallRegion(source);
        var expected = OneRing(source);
        source[0] = new TileKey(5, 7, 0, 0);
        Assert.True(region.HasCore(second), "Region construction owns its input cell snapshot.");
        var terrain = new SyntheticTerrain(flat: true);
        FormationPlan plan = FormationPlanner.Build(terrain, Catalog(), Settings(region: region));
        AssertSurvey(plan, terrain, expected);
        Assert.True(!region.IsCoreBoundaryEdge(CubeTopology.EdgeKey(first, CubeEdge.UMax)),
            "The shared seam is interior to the two-cell core.");
        var edges = new HashSet<CubeEdgeKey>();
        foreach (TileKey cell in expected)
        for (int e = 0; e < 4; e++)
            if (!expected.Contains(CubeTopology.Neighbor(cell, (CubeEdge)e)))
                edges.Add(CubeTopology.EdgeKey(cell, (CubeEdge)e));
        Assert.Equal(edges.Count, region.BoundaryEdges.Length, "Outer edges match the independent neighbor-set oracle.");
        foreach (CubeEdgeKey edge in region.BoundaryEdges)
            Assert.True(edges.Contains(edge), "Every reported boundary separates admitted and absent cells.");
    }

    [Test]
    public static void FormationPlannerRegionValidationAndAdmissionRefuseBeforeSurvey()
    {
        var cell = new TileKey(4, 7, 64, 64);
        Reject<FormationValidationException>(() => SmallRegion(new[] { cell, cell }));
        Reject<FormationValidationException>(() => new FormationSurveyRegion(7, Array.Empty<TileKey>(), 1));
        Reject<FormationValidationException>(() => new FormationSurveyRegion(6, new[] { cell }, 1));
        Reject<FormationValidationException>(() => new FormationSurveyRegion(7, new[] { cell }, 0));
        var small = new FormationSurveyRegionLimits { MaxCoreCells = 1, MaxSurveyCells = 5, MaxInputVisits = 1 };
        var region = new FormationSurveyRegion(7, new[] { cell }, 1, small);
        Assert.Equal(28L, region.Diagnostics.NeighborVisits, "Four expansion, four core boundary and twenty survey boundary visits are charged.");
        Reject<FormationCapacityException>(() => new FormationSurveyRegion(7, new[] { cell }, 1, small with { MaxSurveyCells = 4 }));
        Reject<FormationCapacityException>(() => new FormationSurveyRegion(7, new[] { cell }, 1, small with { MaxInputVisits = 0 }));
        Reject<FormationCapacityException>(() => new FormationSurveyRegion(7, new[] { cell }, 1, small with { MaxNeighborVisits = 27 }));
        Reject<FormationCapacityException>(() => new FormationSurveyRegion(7, new[] { cell }, 1, small with { MaxEstimatedOwnedBytes = 0 }));
        Reject<FormationCapacityException>(() => new FormationSurveyRegion(7, new[] { cell }, 1, small with { MaxEstimatedScratchBytes = 0 }));
        Reject<FormationCapacityException>(() => new FormationSurveyRegion(7, new[] { cell }, 2, small with { MaxHaloRings = 1 }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => new FormationSurveyRegion(7, new[] { cell }, 1, cancellationToken: cancelled.Token));
        var terrain = new SyntheticTerrain(flat: true);
        Reject<FormationValidationException>(() => new FormationPlannerSettings("recipe", 0, "test-ground", 2, 0.5, 10, 12,
            2, 2, 100, Vector3D.UnitY, "crest", 3, region));
        Reject<OperationCanceledException>(() => FormationPlanner.Build(terrain, Catalog(), Settings(region: region), cancellation: cancelled.Token));
        Reject<FormationCapacityException>(() => FormationPlanner.Build(terrain, Catalog(), Settings(region: region),
            new FormationPlannerLimits { MaxEstimatedScratchBytes = 0 }));
        Assert.Equal(0, terrain.HeightCalls, "Regional admission and precancellation make no terrain calls.");
    }

    [Test]
    public static void FormationPlannerCompleteRegionalPermutationPreservesFullPhysicalRecords()
    {
        TileKey[] cells = AllCells(4);
        var limits = new FormationSurveyRegionLimits { MaxCoreCells = cells.Length, MaxSurveyCells = cells.Length, MaxInputVisits = cells.Length };
        var forward = new FormationSurveyRegion(4, cells, 1, limits);
        Array.Reverse(cells);
        var reverse = new FormationSurveyRegion(4, cells, 1, limits);
        Assert.Equal(0, forward.BoundaryEdges.Length, "The complete six-face control has no artificial boundary.");
        for (int i = 0; i < forward.Cells.Length; i++)
            Assert.Equal(forward.Cells[i], reverse.Cells[i], "Input order cannot change canonical cell traversal.");
        FormationCatalog catalog = Catalog();
        FormationPlan whole = FormationPlanner.Build(new SyntheticTerrain(), catalog, Settings());
        FormationPlan a = FormationPlanner.Build(new SyntheticTerrain(), catalog, Settings(region: forward));
        FormationPlan b = FormationPlanner.Build(new SyntheticTerrain(), catalog, Settings(region: reverse));
        Assert.True(whole.Diagnostics.AcceptedPieces > 0, "The independent full-survey control has actual fitted physical output.");
        SamePlan(whole, a);
        SamePlan(a, b);
    }

    [Test]
    public static void FormationPlannerCoreContourCutByHaloRefusesTheWholeBuild()
    {
        FormationPlan control = FormationPlanner.Build(new SyntheticTerrain(), Catalog(), Settings());
        Assert.True(control.Entries.Length > 0 && control.Diagnostics.AcceptedPieces > 0,
            "The unrestricted fixture has complete contours and physical output before clipping.");
        FormationContourIdentity owner = control.Entries[0].Identity;
        var region = SmallRegion(new[] { new TileKey(owner.Face, owner.GridLevel, owner.X, owner.Y) });
        var terrain = new SyntheticTerrain();
        // This planar-height band wraps round the sphere. A five-cell patch cuts its core-relevant component,
        // even when an initial traced section would already fit within MaximumSectionLengthMetres.
        Reject<FormationIncompleteRegionException>(() => FormationPlanner.Build(terrain, Catalog(section: 4), Settings(region: region)));
        Assert.True(terrain.HeightCalls > 0, "Refusal follows actual boundary evidence rather than empty admission.");
    }

    [Test]
    public static void FormationPlannerCompleteMesaRefusesUnadmittedPhysicalEvidence()
    {
        var core = new TileKey[16 * 16];
        int index = 0;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++) core[index++] = new TileKey(4, 4, x, y);
        var region = new FormationSurveyRegion(4, core, 1);
        var terrain = new SyntheticTerrain(mesa: true);
        FormationCatalog catalog = Catalog();
        // The complete circular contour is not sufficient evidence for the wall's out-of-face footprint.
        // This is an incomplete request, not a final Extent rejection that could change after a wider request.
        Reject<FormationIncompleteRegionException>(() => FormationPlanner.Build(terrain, catalog, Settings(region: region)));
        Assert.True(terrain.HeightCalls > 0, "The bounded request performs terrain work before refusing incomplete physical evidence.");
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new SyntheticTerrain(mesa: true), catalog, Settings(region: region),
            new FormationPlannerLimits { MaxRegionCellTests = 0 }));
    }

    [Test]
    public static void FormationPlannerFiniteRegionalProposalsPreserveRecordsAcrossCorePartitions()
    {
        Vector3D centre = Vector3D.UnitZ;
        TileKey[] discoveryCore = CoreAround(TileKey.At(centre, 7), 16);
        FormationSurveyRegion discoveryRegion = LocalRegion(discoveryCore);
        FormationCatalog catalog = LocalCatalog();
        FormationPlan discovery = FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(discoveryRegion), LocalLimits());
        Assert.True(discovery.Diagnostics.AcceptedPieces > 0, "The finite evidence fixture has actual accepted physical pieces.");
        Assert.True(discovery.Diagnostics.SurveyCells < 6 * 128 * 128 / 4, "The positive fixture remains a finite minority of the level-seven world.");
        TileKey? acceptedOwner = null, suppressedOwner = null;
        foreach (FormationPlanEntry entry in discovery.Entries)
            if (entry.Record.Pieces.Length > 0) { acceptedOwner = Owner(entry.Identity); break; }
        Assert.True(acceptedOwner.HasValue, "The diagnostic acceptance count is backed by a published physical owner.");
        foreach (FormationPlanEntry entry in discovery.Entries)
            if (entry.Identity.Section == 0 && !Owner(entry.Identity).Equals(acceptedOwner!.Value) &&
                HasRejection(entry.Record, FormationRejectionReason.Clearance))
            { suppressedOwner = Owner(entry.Identity); break; }
        Assert.True(suppressedOwner.HasValue,
            "A different contour owner's first section is suppressed by a lower owner's independently fitted proposal.");
        TileKey a = acceptedOwner!.Value, b = suppressedOwner!.Value;
        FormationPlan separateA = FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(LocalRegion(new[] { a })), LocalLimits());
        FormationPlan separateB = FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(LocalRegion(new[] { b })), LocalLimits());
        FormationPlan combined = FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(LocalRegion(new[] { a, b })), LocalLimits());
        FormationPlan reversed = FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(LocalRegion(new[] { b, a })), LocalLimits());
        Assert.True(separateA.Diagnostics.AcceptedPieces > 0, "A single core owner still publishes real physical output with complete halo evidence.");
        SameOwnedRecords(discovery, separateA, a);
        SameOwnedRecords(discovery, separateB, b);
        SameOwnedRecords(combined, separateA, a);
        SameOwnedRecords(combined, separateB, b);
        SamePlan(combined, reversed);
        bool firstSuppressed = false;
        foreach (FormationPlanEntry entry in separateB.Entries)
            if (entry.Identity.Section == 0 && HasRejection(entry.Record, FormationRejectionReason.Clearance))
            {
                firstSuppressed = true;
                Assert.Equal(0, entry.Record.Pieces.Length, "A halo owner's conflict suppresses the whole final record.");
                Assert.Equal(0.0, entry.Record.Coverage.CoveredArea, "Suppressed proposals cannot retain physical coverage.");
                Assert.Equal(0.0, entry.Record.Coverage.AllowedSoilArea, "Suppressed proposals cannot substitute soil coverage.");
            }
        Assert.True(firstSuppressed, "Publishing only the later core owner must not erase the earlier halo proposal's conflict.");
        // Both circular contours fit inside this discovery core, but one additional ring cannot establish
        // the full outward physical-box-plus-recipe-extent conflict neighborhood.
        Reject<FormationIncompleteRegionException>(() => FormationPlanner.Build(new LocalTerraces(centre), catalog,
            LocalSettings(LocalRegion(discoveryCore, haloRings: 1)), LocalLimits()));
    }

    [Test]
    public static void FormationPlannerFitsCrossSeamContoursAndReportsExactRegionWorkLimit()
    {
        Vector3D centre = (Vector3D.UnitX + Vector3D.UnitZ).Normalized;
        FormationSurveyRegion region = LocalRegion(CoreAround(TileKey.At(centre, 7), 16));
        FormationCatalog catalog = LocalCatalog();
        FormationPlan plan = FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(region), LocalLimits());
        Assert.True(plan.Diagnostics.AcceptedPieces > 0 && plan.Diagnostics.GeometryVertexVisits > 0,
            "The rotated finite contour produces actual fitted geometry across a cube-face seam.");
        bool fittedAcrossSeam = false, fittedFarFromOwner = false;
        foreach (FormationPlanEntry entry in plan.Entries)
        {
            int firstFace = TileKey.At(entry.Record.Crest[0].Normalized, 7).Face;
            bool crosses = false;
            foreach (Vector3D point in entry.Record.Crest)
                crosses |= TileKey.At(point.Normalized, 7).Face != firstFace;
            bool fitted = entry.Record.Pieces.Length > 0 || HasRejection(entry.Record, FormationRejectionReason.Clearance);
            fittedAcrossSeam |= crosses && fitted;
            if (fitted)
            {
                TileKey lastCrestCell = TileKey.At(entry.Record.Crest[entry.Record.Crest.Length - 1].Normalized, 7);
                bool nearOwner = false;
                // Four rings give a clear margin around inverse-projection boundary rounding. The actual
                // provenance rule uses the traced segment's cell, which can be far from its contour owner.
                foreach (TileKey cell in CoreAround(Owner(entry.Identity), 4)) nearOwner |= cell.Equals(lastCrestCell);
                fittedFarFromOwner |= !nearOwner;
            }
        }
        Assert.True(fittedAcrossSeam, "A cross-face guide reaches a fitted physical proposal, including a later conflict-suppressed proposal.");
        Assert.True(fittedFarFromOwner, "A distant contour section fits using its actual segment source, not the canonical contour owner's cell.");
        Assert.True(plan.Diagnostics.RegionCellTests > 0, "Continuous fitting and conflict-evidence domain work is visible.");
        var exact = LocalLimits() with { MaxRegionCellTests = plan.Diagnostics.RegionCellTests };
        FormationPlan repeated = FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(region), exact);
        SamePlan(plan, repeated);
        Assert.Equal(plan.Diagnostics.RegionCellTests, repeated.Diagnostics.RegionCellTests, "The exact successful region-work budget is deterministic.");
        Reject<FormationCapacityException>(() => FormationPlanner.Build(new LocalTerraces(centre), catalog, LocalSettings(region),
            exact with { MaxRegionCellTests = plan.Diagnostics.RegionCellTests - 1 }));
    }

    [Test]
    public static void FormationPlannerIncompleteHaloOnlyContourCannotBeIgnored()
    {
        var terrain = new LocalTerraces(Vector3D.UnitZ);
        var core = new TileKey(4, 7, 64, 80);
        Assert.Equal(0.0, terrain.HeightAt(core.Centre), "The sole core cell lies on the flat terrain outside both local terraces.");
        FormationSurveyRegion region = LocalRegion(new[] { core }, haloRings: 12);
        // The halo reaches part of the circular outer terrace while its far arc remains outside the region.
        // These are relevant competing proposals even though no contour is owned by the flat core cell.
        Reject<FormationIncompleteRegionException>(() => FormationPlanner.Build(terrain, LocalCatalog(), LocalSettings(region), LocalLimits()));
    }

    private static FormationPlannerSettings Settings(int coverageAlong = 2, int coverageDown = 2, FormationSurveyRegion? region = null) =>
        new("recipe", 0, "test-ground", 2, 0.5, 10, 12, coverageAlong, coverageDown, 100, Vector3D.UnitY, "crest", region == null ? 3 : region.GridLevel - 1, region);

    private static FormationSurveyRegion SmallRegion(TileKey[] cells) => new(cells[0].Level, cells, 1,
        new FormationSurveyRegionLimits { MaxCoreCells = 16, MaxSurveyCells = 64, MaxInputVisits = 16 });

    private static FormationPlannerSettings LocalSettings(FormationSurveyRegion region) =>
        new("recipe", 0, "test-ground", 1, 0.5, 2, 12, 1, 2, 8, Vector3D.UnitY, "crest", 6, region);
    private static FormationPlannerLimits LocalLimits() => new() { MaxEstimatedScratchBytes = 64L * 1024 * 1024 };
    private static FormationCatalog LocalCatalog() =>
        Catalog(section: 6, extent: 56, halfWidth: 8, depth: 24, reach: 24, burial: 4, gap: 8);
    private static FormationSurveyRegion LocalRegion(TileKey[] core, int haloRings = 56) => new(7, core, haloRings,
        new FormationSurveyRegionLimits
        {
            MaxCoreCells = 1024, MaxSurveyCells = 16384, MaxInputVisits = 1024, MaxHaloRings = 56,
            MaxNeighborVisits = 200000, MaxEstimatedOwnedBytes = 8 * 1024 * 1024,
            MaxEstimatedScratchBytes = 16 * 1024 * 1024
        });
    private static TileKey[] CoreAround(TileKey centre, int rings)
    {
        var cells = new HashSet<TileKey> { centre };
        var frontier = new List<TileKey> { centre };
        for (int ring = 0; ring < rings; ring++)
        {
            var next = new List<TileKey>();
            foreach (TileKey cell in frontier)
            for (int edge = 0; edge < 4; edge++)
            {
                TileKey neighbor = CubeTopology.Neighbor(cell, (CubeEdge)edge);
                if (cells.Add(neighbor)) next.Add(neighbor);
            }
            frontier = next;
        }
        var result = new TileKey[cells.Count]; cells.CopyTo(result); return result;
    }

    private static TileKey[] AllCells(int level)
    {
        int n = 1 << level, index = 0;
        var result = new TileKey[6 * n * n];
        for (int face = 0; face < 6; face++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++) result[index++] = new TileKey(face, level, x, y);
        return result;
    }

    private static HashSet<TileKey> OneRing(TileKey[] core)
    {
        var result = new HashSet<TileKey>(core);
        foreach (TileKey cell in core)
        for (int edge = 0; edge < 4; edge++) result.Add(CubeTopology.Neighbor(cell, (CubeEdge)edge));
        return result;
    }

    private static void AssertSurvey(FormationPlan plan, SyntheticTerrain terrain, HashSet<TileKey> cells)
    {
        var expected = new Dictionary<CubeVertexKey, Vector3D>();
        foreach (TileKey cell in cells)
        for (int dy = 0; dy <= 1; dy++)
        for (int dx = 0; dx <= 1; dx++)
        {
            CubeVertexKey key = CubeTopology.VertexKey(cell.Face, cell.Level, cell.X + dx, cell.Y + dy);
            if (!expected.ContainsKey(key)) expected.Add(key, CubeSphere.Direction(cell.Face,
                cell.U0 + dx * cell.Span, cell.V0 + dy * cell.Span));
        }
        Assert.Equal(cells.Count, plan.Diagnostics.SurveyCells, "Only independently enumerated admitted cells are surveyed.");
        Assert.Equal(expected.Count, plan.Diagnostics.SurveyVertices, "Topology keys own shared samples exactly once.");
        Assert.Equal(expected.Count, terrain.SurveyDirections.Count, "Every flat survey vertex reaches RawFieldsAt once.");
        Assert.Equal(expected.Count, new HashSet<Vector3D>(terrain.SurveyDirections).Count, "Distinct physical vertices do not collapse.");
        foreach (Vector3D point in expected.Values)
        {
            bool found = false;
            foreach (Vector3D observed in terrain.SurveyDirections)
                // Alternate face spellings of a seam may differ by a few ULPs in CubeSphere.Warp.
                found |= (point - observed).Length <= 1e-14;
            Assert.True(found, "Every independently located grid vertex is observed at its physical direction.");
        }
        Assert.Equal(5 * expected.Count, terrain.HeightCalls, "One height and four gradient probes are charged for each flat vertex.");
        Assert.Equal(7L * expected.Count, plan.Diagnostics.TerrainSamples, "Height, water and raw-field reads all consume the terrain budget.");
    }

    private static FormationCatalog Catalog(bool thin = false, bool unmatchedContact = false, bool duplicateSlot = false,
        bool ambiguousAnchor = false, bool extraValidContact = false, string anchorKey = "anchor", double section = 24, double extent = 300, int stations = 2,
        double halfWidth = 32, double depth = 120, double reach = 120, double burial = 8, double gap = 8)
    {
        double meshDepth = thin ? 0 : depth, meshReach = thin ? 1 : reach;
        var geometry = new FormationGeometry("synthetic-wall", 0, "test-family",
            new[] { new Vector3D(-halfWidth, 0, 0), new Vector3D(halfWidth, 0, 0), new Vector3D(halfWidth, -meshDepth, meshReach), new Vector3D(-halfWidth, -meshDepth, meshReach) },
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) },
            new[] { new SupportPatch("face", "rock", SupportCapabilities.None) });
        var contacts = new List<CliffContactDomain> { new(anchorKey, "crest", "face", new[] { 0, 1 }, 0, burial, gap) };
        var contactKeys = new List<string> { anchorKey };
        if (unmatchedContact || ambiguousAnchor || extraValidContact)
        {
            contacts.Add(new CliffContactDomain("aaa-other", ambiguousAnchor ? "crest" : "toe", "face", new[] { 2, 3 }, 0,
                extraValidContact ? 1000 : 0, extraValidContact ? 1000 : 0));
            contactKeys.Add("aaa-other");
        }
        const string digest = "0000000000000000000000000000000000000000000000000000000000000000";
        var maps = new[] { new CliffLodTriangleMap(0, 0), new CliffLodTriangleMap(1, 1) };
        var definition = new CliffSupportDefinition(geometry, digest, digest, 1, 1,
            new[] { new CliffPatchDomain("face", new[] { new CliffPatchBoundary(new[] { 0, 1, 2, 3 }) }) }, contacts,
            new[] { new CliffLodBinding("near", digest, geometry, maps, 0, 0.99), new CliffLodBinding("far", digest, geometry, maps, 0, 0.99) });
        var choice = new FormationGeometryChoice(definition.Key, definition.Revision, contactKeys, 1, 1);
        var slots = new List<FormationRecipeSlot> { new(0, "wall", true, new[] { choice }) };
        if (duplicateSlot) slots.Add(new FormationRecipeSlot(7, "optional-wall", false, new[] { choice }));
        var recipe = new FormationRecipe("recipe", 0, 17, "test-family", slots, section, extent, 0.5, stations, 1);
        var profile = new SubstrateProfile("rock", 1, 0, 0, 0, 0, 0, 0, 0);
        return new FormationCatalog(new[] { definition }, new[] { profile }, new[] { recipe });
    }

    private sealed class SyntheticTerrain(bool flat = false, bool cap = false, bool mesa = false, double water = double.NegativeInfinity,
        bool finalized = true, FormationProtectedSite[]? sites = null) : IGradedTerrain
    {
        private readonly FormationProtectedSite[] _sites = sites ?? Array.Empty<FormationProtectedSite>();
        public int HeightCalls { get; private set; }
        public List<Vector3D> SurveyDirections { get; } = new();
        public bool IsFinalized => finalized;
        public double Radius => 128;
        public double HeightAt(Vector3D direction)
        {
            HeightCalls++;
            // Two closed circular slope transitions at chord radii 40 and 60 m, well inside the +Z face.
            return flat ? 0 : mesa ? Math.Clamp(60 - (direction - Vector3D.UnitZ).Length * Radius, 0, 20)
                : cap ? 40 * Math.Max(0, direction.Z) : Math.Clamp(direction.X * Radius + 40, 0, 80);
        }
        public double WaterLevelAt(Vector3D direction) => water;
        public GradedTerrainFields RawFieldsAt(Vector3D direction)
        {
            if (flat) SurveyDirections.Add(direction);
            return new(cap || flat || mesa || direction.Z > 0.85 ? "test-ground" : "other", 15, 0.5, 0.5);
        }
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => _sites;
    }

    private sealed class LocalTerraces(Vector3D centre) : IGradedTerrain
    {
        public bool IsFinalized => true;
        public double Radius => 512;
        public double HeightAt(Vector3D direction)
        {
            double distance = (direction - centre).Length * Radius;
            return Math.Clamp(28 - distance, 0, 8) + Math.Clamp(48 - distance, 0, 8);
        }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new("test-ground", 15, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
    }

    private static FormationBounds WorldBounds() => new(new Vector3D(-1000, -1000, -1000), new Vector3D(1000, 1000, 1000));
    private static bool HasRejection(FormationPlan plan, FormationRejectionReason reason)
    {
        foreach (FormationPlanEntry entry in plan.Entries)
            foreach (FormationRejection rejection in entry.Record.Rejections) if (rejection.Reason == reason) return true;
        return false;
    }
    private static bool HasRejection(FormationRecord record, FormationRejectionReason reason)
    {
        foreach (FormationRejection rejection in record.Rejections) if (rejection.Reason == reason) return true;
        return false;
    }
    private static TileKey Owner(FormationContourIdentity identity) =>
        new(identity.Face, identity.GridLevel, identity.X, identity.Y);
    private static void SameOwnedRecords(FormationPlan complete, FormationPlan requested, TileKey owner)
    {
        int matched = 0;
        foreach (FormationPlanEntry entry in complete.Entries)
        {
            if (!Owner(entry.Identity).Equals(owner)) continue;
            Assert.True(matched < requested.Entries.Length, "A separate core request cannot lose owned records.");
            Assert.Equal(entry.Identity, requested.Entries[matched].Identity, "Owned section identity does not depend on neighboring published cores.");
            SameRecord(entry.Record, requested.Entries[matched].Record);
            matched++;
        }
        Assert.True(matched > 0, "The compared core owner publishes nonempty records.");
        Assert.Equal(matched, requested.Entries.Length, "Halo evidence is not published as extra core-owned records.");
    }
    private static void SameIds(FormationPlan a, FormationPlan b)
    {
        Assert.Equal(a.Entries.Length, b.Entries.Length, "Acceptance/rejection does not change preassigned section count.");
        for (int i = 0; i < a.Entries.Length; i++) Assert.Equal(a.Entries[i].Identity, b.Entries[i].Identity, "Full section identity survives rejection.");
    }
    private static void SamePlan(FormationPlan a, FormationPlan b)
    {
        SameIds(a, b);
        Assert.Equal(a.Diagnostics.AcceptedPieces, b.Diagnostics.AcceptedPieces, "Physical acceptance is independent of core enumeration.");
        for (int i = 0; i < a.Entries.Length; i++)
            SameRecord(a.Entries[i].Record, b.Entries[i].Record);
    }
    private static void SameRecord(FormationRecord x, FormationRecord y)
    {
        Assert.Equal(x.Id, y.Id, "Full physical record identity is stable.");
        Assert.Equal(x.RecipeKey, y.RecipeKey, "Selected recipe is stable.");
        Assert.Equal(x.RecipeRevision, y.RecipeRevision, "Selected recipe revision is stable.");
        Assert.Equal(x.FamilyKey, y.FamilyKey, "Physical family is stable.");
        Assert.True(x.Crest.SequenceEqual(y.Crest) && x.Face.SequenceEqual(y.Face) && x.Toe.SequenceEqual(y.Toe),
                "Canonical contour and fit guides are exactly stable.");
        Assert.Equal(x.Coverage, y.Coverage, "Measured physical and allowed-soil coverage is exactly stable.");
        Assert.Equal(x.InfluenceBounds, y.InfluenceBounds, "The complete formation influence is stable.");
        Assert.Equal(x.Pieces.Length, y.Pieces.Length, "Physical slot counts are stable.");
        for (int j = 0; j < x.Pieces.Length; j++)
        {
            Assert.Equal(x.Pieces[j].Id, y.Pieces[j].Id, "Physical piece identity is stable.");
            Assert.Equal(x.Pieces[j].Role, y.Pieces[j].Role, "Physical piece role is stable.");
            Assert.Equal(x.Pieces[j].Geometry.Key, y.Pieces[j].Geometry.Key, "The selected qualified geometry is stable.");
            Assert.Equal(x.Pieces[j].Geometry.Revision, y.Pieces[j].Geometry.Revision, "The selected geometry revision is stable.");
            Assert.Equal(x.Pieces[j].Transform, y.Pieces[j].Transform, "Full physical transforms are stable.");
        }
        Assert.Equal(x.Rejections.Length, y.Rejections.Length, "Rejected slots remain explicit and stable.");
        for (int j = 0; j < x.Rejections.Length; j++)
        {
            Assert.Equal(x.Rejections[j].Slot, y.Rejections[j].Slot, "Rejected slot identity is stable.");
            Assert.Equal(x.Rejections[j].Role, y.Rejections[j].Role, "Rejected slot role is stable.");
            Assert.Equal(x.Rejections[j].Reason, y.Rejections[j].Reason, "Rejection reason is stable.");
            Assert.Equal(x.Rejections[j].IntendedRegion, y.Rejections[j].IntendedRegion, "Rejected footprint is stable.");
        }
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
