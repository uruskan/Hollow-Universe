using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Physical query fixtures, not an asset/profile activation or native collision latency qualification.</summary>
public static class SelectedGroundTraceTests
{
    private const string Identity = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Generation = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public static void SelectedTraceMatchesActualCanonicalCollisionTrianglesAndBothDiagonals()
    {
        var surface = new Terrain();
        var limits = Limits(); var workspace = new SelectedGroundTraceWorkspace(limits);
        for (int face = 0; face < 6; face++)
        foreach ((int x, int y) in new[] { (0, 0), (0, 15), (31, 16), (15, 0), (16, 31), (31, 31), (13, 19) })
        for (int half = 0; half < 2; half++)
        {
            var cell = new TileKey(face, surface.TerrainLattice.VertexLevel, x, y);
            TerrainLatticeTriangle triangle = surface.TerrainLattice.Triangle(cell, half);
            Vector3D a = surface.Point(triangle.A), b = surface.Point(triangle.B), c = surface.Point(triangle.C);
            Vector3D centre = a * 0.23 + b * 0.31 + c * 0.46;
            Vector3D normal = Vector3D.Cross(b - a, c - a).Normalized;
            var ray = new SelectedGroundTraceRequest(centre + normal * 0.5, -normal, 1);
            SelectedGroundTraceResult result = Trace(surface, ray, workspace: workspace);
            SelectedGroundHit hit = Hit(result);
            Assert.Equal(GroundPrimitiveKind.Terrain, hit.Primitive.Kind, "the base mesh retains terrain provenance");
            Assert.Equal(cell, hit.Primitive.Cell, "the exact canonical cell is selected, including seam-adjacent cells");
            Assert.Equal(half, hit.Primitive.Triangle, "both canonical diagonal halves retain their original ordinals");
            Assert.True((hit.Normal - normal).Length < 1e-12 && hit.FrontFace, "normal is the physical winding normal");
            TriangleMesh mesh = SurfaceGrid.Collision(surface, ZoneFrame.World, cell, 0, 1);
            double oracle = MeshDistance(mesh, ray, out _);
            Assert.True(oracle >= 0 && Math.Abs(oracle - hit.Distance) < 0.00001,
                "independent ray versus actual float collision output agrees within its position quantization");
            Assert.True((hit.Point - centre).Length < 1e-12, "body-space point is on the canonical triangle");
            Assert.Equal(Generation, result.GenerationDigest!, "physical evidence carries the immutable generation");
        }
    }

    [Test]
    public static void SelectedTraceClosedCubeSeamsAndCornersNeverBecomeAClearRay()
    {
        var surface = new Terrain(); var workspace = new SelectedGroundTraceWorkspace(Limits());
        for (int face = 0; face < 6; face++)
        foreach ((int x, int y) in new[] { (0, 0), (32, 0), (0, 32), (32, 32), (0, 16), (32, 16), (16, 0), (16, 32) })
        {
            CubeVertexKey vertex = surface.TerrainLattice.VertexKey(face, x, y);
            Vector3D direction = surface.TerrainLattice.Direction(vertex);
            var ray = new SelectedGroundTraceRequest(direction * 12, -direction, 4);
            SelectedGroundHit a = Hit(Trace(surface, ray, workspace: workspace));
            SelectedGroundHit b = Hit(Trace(surface, ray, workspace: workspace));
            Assert.Equal(a, b, "closed seam and corner queries keep stable physical evidence across workspace epochs");
            Assert.True(Math.Abs(a.Distance - 2) < 1e-12, "the shared vertex is not lost by angular culling or edge predicates");
        }
    }

    [Test]
    public static void SelectedTraceNearestFormationPreservesWallAndUndersideNormalsWithoutHitListTruncation()
    {
        FormationPiece wall = Quad(4, 14);
        var surface = new Terrain(wall);
        var ray = new SelectedGroundTraceRequest(new(0, 0, 16), -Vector3D.UnitZ, 10);
        FormationRayResult all = surface.Formations.Raycast(new FormationRayRequest(ray.Origin, ray.Direction, ray.Length,
            Backfaces: FormationBackfacePolicy.TwoSided), new FormationSupportQueryLimits { MaxHits = 1 });
        Assert.Equal(FormationQueryStatus.CapacityExceeded, all.Status, "the closed diagonal has multiple hits and cannot use an all-hit cap of one");
        SelectedGroundHit hit = Hit(Trace(surface, ray));
        Assert.Equal(GroundPrimitiveKind.Formation, hit.Primitive.Kind, "the physical wall wins before radial base terrain");
        Assert.Equal(0, hit.Primitive.Triangle, "coincident closed diagonal chooses the stable lower ordinal");
        Assert.Equal(2.0, hit.Distance, "nearest accumulator handles all candidates without a hit-list allowance");
        Assert.Equal(Vector3D.UnitZ, hit.Normal, "wall winding is preserved");
        Assert.True(hit.FormationSupport is { Capabilities: SupportCapabilities.None }, "a physical obstruction does not acquire plant/build permission");
        var underside = new SelectedGroundTraceRequest(new(0.2, 0.1, 12), Vector3D.UnitZ, 3);
        hit = Hit(Trace(surface, underside));
        Assert.True(!hit.FrontFace && hit.Normal == Vector3D.UnitZ, "two-sided hit does not silently turn the winding normal toward the ray");
        AssertClear(Trace(surface, underside with { Backfaces = FormationBackfacePolicy.FrontFacesOnly }));
    }

    [Test]
    public static void SelectedTraceNearestOrderingIsExactStableAndDoesNotMergeNearbyDistances()
    {
        var first = new Terrain(Quad(9, 14), Quad(2, 14));
        var reverse = new Terrain(Quad(2, 14), Quad(9, 14));
        var ray = new SelectedGroundTraceRequest(new(0.2, 0.1, 16), -Vector3D.UnitZ, 10);
        SelectedGroundHit a = Hit(Trace(first, ray)), b = Hit(Trace(reverse, ray));
        Assert.Equal(a, b, "input order cannot resolve coincident physical faces differently");
        Assert.Equal(new FormationId(2, 0), a.Primitive.Formation.Piece.Formation, "exact equal distance uses stable primitive identity");
        var close = new Terrain(Quad(1, 14), Quad(8, Math.BitIncrement(14)));
        Assert.Equal(new FormationId(8, 0), Hit(Trace(close, ray)).Primitive.Formation.Piece.Formation,
            "one-representable-step nearer face wins even when its identity sorts later");
    }

    [Test]
    public static void SelectedTraceCoincidentPhysicalObjectDoesNotAcquireBaseTerrainProvenance()
    {
        var surface = new Terrain();
        var cell = new TileKey(4, 5, 17, 17);
        TerrainLatticeTriangle triangle = surface.TerrainLattice.Triangle(cell, 0);
        Vector3D a = surface.Point(triangle.A), b = surface.Point(triangle.B), c = surface.Point(triangle.C);
        var geometry = new FormationGeometry("coincident", 0, "trace-fixture", new[] { a, b, c },
            new[] { new GeometryTriangle(0, 1, 2, 0) }, new[] { new SupportPatch("face", "rock", SupportCapabilities.None) });
        var piece = new FormationPiece(new(new FormationId(1, 0), 0), "face", geometry,
            new FormationTransform(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 1));
        surface = new Terrain(piece);
        Vector3D normal = Vector3D.Cross(b - a, c - a).Normalized, centre = a * 0.23 + b * 0.31 + c * 0.46;
        SelectedGroundHit hit = Hit(Trace(surface, new(centre + normal, -normal, 2)));
        Assert.Equal(GroundPrimitiveKind.Formation, hit.Primitive.Kind, "exact coincident obstruction uses explicit physical provenance before base terrain");
        Assert.True(hit.FormationSupport is { Capabilities: SupportCapabilities.None }, "coincident wall cannot silently become soil");
    }

    [Test]
    public static void SelectedTracePinnedPhysicalVerticesMatchCollisionAndRejectUncertifiedPublicPins()
    {
        var surface = new Terrain();
        CubeVertexKey key = surface.TerrainLattice.VertexKey(4, 17, 17);
        Vector3D direction = surface.TerrainLattice.Direction(key);
        surface.InstallPin(new(key, direction * 10.2));
        Assert.True(surface.TerrainPins!.SupportsCanonicalRadialTrace && surface.TerrainPins.Vertices.Length == 1,
            "nonempty frozen canonical pins are certified during their actual construction");
        var cell = new TileKey(4, 5, 17, 17);
        TerrainLatticeTriangle triangle = surface.TerrainLattice.Triangle(cell, 0);
        Vector3D a = surface.Point(triangle.A), b = surface.Point(triangle.B), c = surface.Point(triangle.C);
        Vector3D at = a * 0.4 + b * 0.2 + c * 0.4, normal = Vector3D.Cross(b - a, c - a).Normalized;
        var ray = new SelectedGroundTraceRequest(at + normal, -normal, 2);
        SelectedGroundHit hit = Hit(Trace(surface, ray));
        Assert.True(Math.Abs(hit.Distance - 1) < 1e-12, "the trace uses the frozen physical pin, not resampled HeightAt");
        Assert.True(Math.Abs(MeshDistance(SurfaceGrid.Collision(surface, ZoneFrame.World, cell, 0, 1), ray, out _) - hit.Distance) < 0.00001,
            "actual canonical collision has the same raised pinned triangle");
        var displaced = new Terrain(); displaced.InstallPin(new(key, direction * 10 + Vector3D.UnitX * 0.001));
        Assert.True(!displaced.TerrainPins!.SupportsCanonicalRadialTrace, "valid public displaced pins retain their independent domain");
        AssertIncomplete(Trace(displaced, ray), SelectedGroundTraceStatus.Unsupported);
    }

    [Test]
    public static void SelectedTraceRadialCertificateRequiresExactReconstructionAcrossScalesAndFaces()
    {
        var lattice = new TerrainLattice(1354, 5);
        for (int face = 0; face < 6; face++)
        foreach ((int x, int y) in new[] { (0, 0), (3, 901), (512, 512), (1024, 1024), (1024, 137) })
        foreach (double radial in new[] { 0.001, 10.2, 1354.137, 1000000.25 })
        {
            Vector3D direction = lattice.Direction(lattice.VertexKey(face, x, y));
            Vector3D point = direction * radial;
            Assert.True(TerrainPinSet.IsCanonicalRadialProduct(direction, point), "exact generated multiplication is recognized at canonical seams and scales");
            Assert.True(!TerrainPinSet.IsCanonicalRadialProduct(direction, -point), "an antipodal pin cannot acquire a positive radial certificate");
        }
    }

    [Test]
    public static void SelectedTraceMonumentsUseTheAdmittedPhysicalSnapshotAndPreserveTriangleIdentity()
    {
        var surface = new Terrain();
        var site = new MonumentPlacement(new TileKey(4, 1, 0, 0), 0, new(0, 0, 1000), Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 0);
        var monuments = SelectedGroundCollision.Snapshot(new[] { site }, 1, default);
        int[] indices = site.Definition.Indices;
        Vector3D a = site.Transform(site.Definition.Positions[indices[0]]), b = site.Transform(site.Definition.Positions[indices[1]]),
            c = site.Transform(site.Definition.Positions[indices[2]]);
        Vector3D normal = Vector3D.Cross(b - a, c - a).Normalized;
        var ray = new SelectedGroundTraceRequest(a * 0.23 + b * 0.31 + c * 0.46 + normal * 0.1, -normal, 0.2);
        SelectedGroundHit hit = Hit(SelectedGroundTrace.Trace(surface, surface.Formations, monuments, ray, Limits()));
        Assert.Equal(GroundPrimitiveKind.Monument, hit.Primitive.Kind, "snapshot monument physical faces participate alongside formations and terrain");
        Assert.Equal(site.Tile, hit.Primitive.Cell, "stable monument site is retained");
        Assert.Equal(site.Definition.Key, hit.Primitive.MonumentKey!, "physical definition is retained");
        var positions = new float[site.Definition.Positions.Length * 3];
        for (int i = 0; i < site.Definition.Positions.Length; i++)
        {
            Vector3D point = site.Transform(site.Definition.Positions[i]);
            positions[i * 3] = (float)point.X; positions[i * 3 + 1] = (float)point.Y; positions[i * 3 + 2] = (float)point.Z;
        }
        double oracle = MeshDistance(new TriangleMesh(positions, indices), ray, out _);
        Assert.True(hit.Primitive.Triangle >= 0 && hit.Primitive.Triangle < indices.Length / 3 && oracle >= 0 && Math.Abs(hit.Distance - oracle) < 0.0001,
            "triangle ordinal and nearest distance agree with the complete monument collision mesh within its float quantization");
        AssertIncomplete(SelectedGroundTrace.Trace(surface, surface.Formations, monuments, ray, Limits() with { MaxMonuments = 0 }),
            SelectedGroundTraceStatus.CapacityExceeded);
    }

    [Test]
    public static void SelectedTraceCompleteMissIntervalsBackfacesAndUnsupportedSweepsAreDistinct()
    {
        var surface = new Terrain(Quad(4, 14));
        var ray = new SelectedGroundTraceRequest(new(0.2, 0.1, 16), -Vector3D.UnitZ, 2);
        Assert.Equal(2.0, Hit(Trace(surface, ray)).Distance, "closed interval retains an exact endpoint hit");
        AssertClear(Trace(surface, ray with { MinDistance = 2.01, Length = 3 }));
        AssertClear(Trace(surface, new(new(0, 0, 20), Vector3D.UnitX, 3)));
        var inside = new SelectedGroundTraceRequest(new(0.1, 0.2, 9), Vector3D.UnitZ, 2);
        Assert.True(!Hit(Trace(surface, inside)).FrontFace, "starting below terrain still finds an outward crossing");
        AssertClear(Trace(surface, inside with { Backfaces = FormationBackfacePolicy.FrontFacesOnly }));
        AssertIncomplete(Trace(surface, ray with { Radius = 0.1 }), SelectedGroundTraceStatus.Unsupported);
        AssertIncomplete(Trace(surface, new(new(-4, 0, 14), Vector3D.UnitX, 8)), SelectedGroundTraceStatus.NumericallyUnresolved);
    }

    [Test]
    public static void SelectedTraceEveryWorkAllowanceCanRefuseWithoutPublishingAPartialHit()
    {
        var surface = new Terrain(Quad(4, 14));
        var ray = new SelectedGroundTraceRequest(new(0.2, 0.1, 16), -Vector3D.UnitZ, 10);
        SelectedGroundTraceLimits full = Limits();
        foreach (SelectedGroundTraceLimits limits in new[]
        {
            full with { MaxTerrainNodes = 0 }, full with { MaxTerrainCells = 0 }, full with { MaxVertices = 0 },
            full with { MaxVertexLookups = 0 }, full with { MaxFormationNodes = 0 }, full with { MaxTriangles = 0 },
            full with { MaxEstimatedScratchBytes = 0 },
        }) AssertIncomplete(Trace(surface, ray, limits), SelectedGroundTraceStatus.CapacityExceeded);
        AssertIncomplete(Trace(surface, ray, full with { MaxExactTests = 0 }, mode: SelectedGroundTrace.PredicateMode.ExactOracle),
            SelectedGroundTraceStatus.CapacityExceeded);
        AssertIncomplete(Trace(surface, ray with { Origin = new(0, 0, 16) }, full with { MaxExactTests = 0 }),
            SelectedGroundTraceStatus.CapacityExceeded); // Closed card diagonal requires exact edge decisions.
        // Terrain is visited first and has a real hit. Refusing the later formation walk must erase that hit.
        SelectedGroundTraceResult late = Trace(surface, ray, full with { MaxFormationNodes = 0 });
        Assert.True(late.Diagnostics.ExactTests + late.Diagnostics.FilteredTests > 0, "late refusal occurs after real terrain triangle tests");
        Assert.True(late.Hit == null, "provisional terrain cannot be exposed through a missing formation query");
        SelectedGroundTraceResult bounded = Trace(surface, new(new(0.1, 0.2, 15), -Vector3D.UnitZ, 30), full with { MaxTerrainNodes = 32 });
        AssertIncomplete(bounded, SelectedGroundTraceStatus.CapacityExceeded);
        Assert.Equal(32, bounded.Diagnostics.TerrainNodes, "origin-crossing traversal stops at its exact node bound");
    }

    [Test]
    public static void SelectedTraceCancellationAndScratchReuseDoNotRetainOldWorldData()
    {
        var surface = new Terrain(); var limits = Limits(); var workspace = new SelectedGroundTraceWorkspace(limits);
        var ray = new SelectedGroundTraceRequest(new(0.2, 0.1, 12), -Vector3D.UnitZ, 4);
        SelectedGroundHit original = Hit(Trace(surface, ray, workspace: workspace));
        using var stop = new CancellationTokenSource();
        surface.SampleAction = stop.Cancel;
        AssertIncomplete(Trace(surface, ray, cancellation: stop.Token, workspace: workspace), SelectedGroundTraceStatus.Cancelled);
        surface.SampleAction = null;
        Assert.Equal(original, Hit(Trace(surface, ray, workspace: workspace)), "cancelled operation releases exclusive scratch ownership");
        var raised = new Terrain { Height = 0.2 };
        Assert.True(Hit(Trace(raised, ray, workspace: workspace)).Distance < original.Distance - 0.19,
            "another world cannot see prior epoch's cached vertex positions");
        using (workspace.Acquire(limits.Reserve().Slots))
            Reject<InvalidOperationException>(() => Trace(surface, ray, workspace: workspace));
        surface.Height = double.NaN;
        AssertIncomplete(Trace(surface, ray, workspace: workspace), SelectedGroundTraceStatus.NumericallyUnresolved);
    }

    [Test]
    public static void SelectedTraceSharedBvhExtractionPreservesExistingAllHitAndFootprintQueries()
    {
        var surface = new Terrain(Quad(8, 14), Quad(2, 12));
        var request = new FormationRayRequest(new(0, 0, 16), -Vector3D.UnitZ, 10, Backfaces: FormationBackfacePolicy.TwoSided);
        FormationRayResult result = surface.Formations.Raycast(request, new FormationSupportQueryLimits());
        Assert.Equal(FormationQueryStatus.Complete, result.Status, "legacy all-hit request still completes");
        Assert.Equal(4, result.Hits.Length, "both closed diagonal hits on both planes remain present");
        Assert.Equal(2.0, result.Hits[0].Distance, "legacy list remains sorted by distance");
        Assert.Equal(0, result.Hits[0].Primitive.Triangle, "legacy equal-distance tie remains ordinal");
        Assert.Equal(4.0, result.Hits[3].Distance, "far plane remains in all-hit output");
        var footprint = new FormationFootprintRequest(new[] { request }, Vector3D.UnitZ);
        FormationFootprintResult supported = FormationSupportQueries.Footprint(surface.Formations.SupportIndex, footprint);
        Assert.True(supported.Supported && supported.Hits.Length == 1 && supported.Hits[0] == result.Hits[0],
            "the existing footprint caller consumes the same all-hit traversal semantics");
    }

    [Test]
    public static void SelectedTraceFilteredPathAgreesWithExactOracleAcrossScalesEdgesAndGrazing()
    {
        var limits = Limits(); var workspace = new SelectedGroundTraceWorkspace(limits);
        int cases = 0, filteredHits = 0, exactFallbacks = 0;
        foreach (double radius in new[] { 0.001, 10.0, 1200.0, 100000.0 })
        {
            var surface = new Terrain { RadiusValue = radius, TerrainLattice = new TerrainLattice(radius, 0) };
            surface.RebuildField();
            for (int face = 0; face < 6; face++)
            foreach ((int x, int y) in new[] { (0, 0), (0, 15), (31, 16), (31, 31), (13, 19) })
            for (int half = 0; half < 2; half++)
            {
                var cell = new TileKey(face, surface.TerrainLattice.VertexLevel, x, y);
                TerrainLatticeTriangle triangle = surface.TerrainLattice.Triangle(cell, half);
                Vector3D a = surface.Point(triangle.A), b = surface.Point(triangle.B), c = surface.Point(triangle.C);
                Vector3D normal = Vector3D.Cross(b - a, c - a).Normalized;
                double length = radius * 0.02;
                foreach (Vector3D target in new[] { a * 0.23 + b * 0.31 + c * 0.46, (a + b) * 0.5, a })
                    Compare(new(target + normal * (length * 0.5), -normal, length));
                // Endpoints and a finite near-tangent crossing exercise uncertainty without an angular
                // epsilon or a random-data dependency. Both paths see the exact same represented ray.
                Vector3D centre = a * 0.23 + b * 0.31 + c * 0.46;
                Compare(new(centre + normal * length, -normal, length));
                Vector3D grazing = ((b - a).Normalized - normal * 1e-10).Normalized;
                Compare(new(centre - grazing * (length * 0.25), grazing, length * 0.5));
            }

            void Compare(SelectedGroundTraceRequest request)
            {
                SelectedGroundTraceResult actual = Trace(surface, request, limits, workspace: workspace);
                SelectedGroundTraceResult oracle = Trace(surface, request, limits, workspace: workspace, mode: SelectedGroundTrace.PredicateMode.ExactOracle);
                SameEvidence(actual, oracle);
                cases++; filteredHits += actual.Diagnostics.FilteredHits; exactFallbacks += actual.Diagnostics.ExactTests;
                Assert.True(actual.Diagnostics.FilteredTests <= actual.Diagnostics.Triangles &&
                    actual.Diagnostics.ExactTests <= limits.MaxExactTests, "every filter and exact fallback stays inside its explicit work charge");
            }
        }
        Assert.Equal(1200, cases, "fixed differential corpus is bounded and includes all six cube faces");
        Assert.True(filteredHits > 0 && exactFallbacks > 0, "the corpus actually exercises both proof-only hits and exact uncertainty fallbacks");
    }

    [Test]
    public static void SelectedTraceFilteredNearestTiesPinsMonumentsAndFailuresMatchOracle()
    {
        var limits = Limits(); var workspace = new SelectedGroundTraceWorkspace(limits);
        var request = new SelectedGroundTraceRequest(new(0.2, 0.1, 16), -Vector3D.UnitZ, 10);
        foreach (Terrain surface in new[]
        {
            new Terrain(Quad(9, 14), Quad(2, 14)), new Terrain(Quad(2, 14), Quad(9, 14)),
            new Terrain(Quad(1, 14), Quad(8, Math.BitIncrement(14))),
            new Terrain(Quad(8, Math.BitIncrement(14)), Quad(1, 14)),
        })
        foreach (SelectedGroundTraceRequest ray in new[]
        {
            request, request with { Origin = new(0, 0, 16) }, request with { Length = 2 },
            request with { MinDistance = 2 }, request with { MinDistance = Math.BitIncrement(2) },
            new SelectedGroundTraceRequest(new(-4, 0, 14), Vector3D.UnitX, 8),
            new SelectedGroundTraceRequest(new(0.2, 0.1, 12), Vector3D.UnitZ, 3, Backfaces: FormationBackfacePolicy.FrontFacesOnly),
        }) SameEvidence(Trace(surface, ray, limits, workspace: workspace),
            Trace(surface, ray, limits, workspace: workspace, mode: SelectedGroundTrace.PredicateMode.ExactOracle));

        var pinned = new Terrain();
        CubeVertexKey key = pinned.TerrainLattice.VertexKey(4, 17, 17);
        pinned.InstallPin(new(key, pinned.TerrainLattice.Direction(key) * 10.2));
        SameEvidence(Trace(pinned, request, limits, workspace: workspace),
            Trace(pinned, request, limits, workspace: workspace, mode: SelectedGroundTrace.PredicateMode.ExactOracle));

        var site = new MonumentPlacement(new TileKey(4, 1, 0, 0), 0, new(0, 0, 1000), Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 0);
        var monuments = SelectedGroundCollision.Snapshot(new[] { site }, 1, default);
        for (int t = 0; t < Math.Min(12, site.Definition.Indices.Length); t += 3)
        {
            Vector3D a = site.Transform(site.Definition.Positions[site.Definition.Indices[t]]);
            Vector3D b = site.Transform(site.Definition.Positions[site.Definition.Indices[t + 1]]);
            Vector3D c = site.Transform(site.Definition.Positions[site.Definition.Indices[t + 2]]);
            Vector3D normal = Vector3D.Cross(b - a, c - a).Normalized;
            var ray = new SelectedGroundTraceRequest(a * 0.23 + b * 0.31 + c * 0.46 + normal * 0.1, -normal, 0.2);
            SameEvidence(SelectedGroundTrace.Trace(pinned, pinned.Formations, monuments, ray, limits, workspace: workspace),
                SelectedGroundTrace.Trace(pinned, pinned.Formations, monuments, ray, limits, workspace: workspace,
                    mode: SelectedGroundTrace.PredicateMode.ExactOracle));
        }
        foreach (SelectedGroundTraceLimits bounded in new[] { limits with { MaxFormationNodes = 0 }, limits with { MaxTerrainNodes = 4 } })
        {
            var surface = new Terrain(Quad(4, 14));
            SameEvidence(Trace(surface, request, bounded, workspace: workspace),
                Trace(surface, request, bounded, workspace: workspace, mode: SelectedGroundTrace.PredicateMode.ExactOracle));
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        SameEvidence(Trace(pinned, request, limits, cancelled.Token, workspace),
            Trace(pinned, request, limits, cancelled.Token, workspace, SelectedGroundTrace.PredicateMode.ExactOracle));
    }

    [Test]
    public static void SelectedTraceProvenInteriorHitAndMissUseNoExactBudgetOrQueryHeapOwner()
    {
        var surface = new Terrain(Quad(4, 14));
        var limits = Limits() with { MaxExactTests = 0 };
        var workspace = new SelectedGroundTraceWorkspace(limits);
        var ray = new SelectedGroundTraceRequest(new(0.25, 0.125, 16), -Vector3D.UnitZ, 3);
        var miss = new SelectedGroundTraceRequest(new(0, 0, 20), Vector3D.UnitX, 3);
        for (int i = 0; i < 16; i++) { Trace(surface, ray, limits, workspace: workspace); Trace(surface, miss, limits, workspace: workspace); }
        SelectedGroundTraceResult result = default, clear = default;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        { result = Trace(surface, ray, limits, workspace: workspace); clear = Trace(surface, miss, limits, workspace: workspace); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(GroundPrimitiveKind.Formation, Hit(result).Primitive.Kind, "interior proof supplies the actual physical wall hit");
        AssertClear(clear);
        Assert.True(result.Diagnostics.FilteredHits > 0 && result.Diagnostics.ExactTests == 0,
            "the test cannot pass by silently taking the exact predicate");
        Assert.Equal(0L, allocated, "caller-owned workspace plus ref visitor has no routine per-query heap allocation");
        SameEvidence(result, Trace(surface, ray, Limits(), workspace: workspace, mode: SelectedGroundTrace.PredicateMode.ExactOracle));
    }

    private static void SameEvidence(SelectedGroundTraceResult actual, SelectedGroundTraceResult oracle)
    {
        Assert.Equal(oracle.Status, actual.Status, "filtered and exact traversal agree on completeness");
        Assert.Equal(oracle.GenerationDigest!, actual.GenerationDigest!, "the filter cannot change immutable generation ownership");
        Assert.Equal(oracle.TerrainIdentity!, actual.TerrainIdentity!, "the filter cannot change physical terrain identity");
        Assert.Equal(oracle.Hit.HasValue, actual.Hit.HasValue, "a proof filter cannot invent or lose a physical crossing");
        if (oracle.Hit is not { } expected || actual.Hit is not { } hit) return;
        Assert.Equal(expected.Primitive, hit.Primitive, "exact nearest identity, closed-edge tie and source triangle ordinal are unchanged");
        Assert.Equal(expected.Normal, hit.Normal, "normal remains the exact same original winding computation");
        Assert.Equal(expected.FrontFace, hit.FrontFace, "back-face policy and provenance are unchanged");
        const double epsilon = 2.2204460492503131e-16;
        double tolerance = 128 * epsilon * Math.Max(1, Math.Abs(expected.Distance));
        Assert.True(Math.Abs(expected.Distance - hit.Distance) <= tolerance, "returned double distance stays inside the filter's documented representation bound");
        double pointTolerance = tolerance + 4 * epsilon * Math.Max(1, expected.Point.Length);
        Assert.True((expected.Point - hit.Point).Length <= pointTolerance && (expected.Barycentric - hit.Barycentric).Length <= 256 * epsilon,
            "floating point/barycentric representation agrees while identity uses exact ordering");
        Assert.Equal(expected.FormationSupport.HasValue, hit.FormationSupport.HasValue, "formation permission evidence is neither added nor removed");
        if (expected.FormationSupport is { } support && hit.FormationSupport is { } actualSupport)
        {
            Assert.Equal(support.Primitive, actualSupport.Primitive, "support identity is the selected face");
            Assert.Equal(support.Capabilities, actualSupport.Capabilities, "physical contact does not acquire new permissions");
            Assert.Equal(support.PatchKey, actualSupport.PatchKey, "patch provenance is preserved");
            Assert.Equal(support.SubstrateProfileKey, actualSupport.SubstrateProfileKey, "substrate provenance is preserved");
        }
    }

    // Root runs this separately by name. No latency threshold is invented: emit reproducible per-scenario
    // work, managed allocation and elapsed time before choosing a fast predicate or production allowances.
    [Test]
    public static void SelectedTraceBoundedTimingReport()
    {
        var surface = new Terrain(Quad(4, 1210)) { RadiusValue = 1200, TerrainLattice = new TerrainLattice(1200, 5) };
        surface.RebuildField();
        var limits = Limits() with { MaxTerrainNodes = 8192, MaxTerrainCells = 2048, MaxVertices = 8192 };
        var workspace = new SelectedGroundTraceWorkspace(limits);
        var seam = new Vector3D(1, 0, 1).Normalized;
        var cases = new[]
        {
            ("short-terrain", new SelectedGroundTraceRequest(new(10, 5, 1202), -Vector3D.UnitZ, 5)),
            ("wall", new SelectedGroundTraceRequest(new(0.2, 0.1, 1212), -Vector3D.UnitZ, 20)),
            ("seam", new SelectedGroundTraceRequest(seam * 1202, -seam, 5)),
            ("long", new SelectedGroundTraceRequest(new(-80, 5, 1202), new Vector3D(1, 0, -0.04).Normalized, 160)),
            ("miss", new SelectedGroundTraceRequest(new(0, 0, 1220), Vector3D.UnitX, 20)),
        };
        const int iterations = 24;
        foreach (var scenario in cases)
        foreach (SelectedGroundTrace.PredicateMode mode in new[] { SelectedGroundTrace.PredicateMode.Filtered, SelectedGroundTrace.PredicateMode.ExactOracle })
        {
            SelectedGroundTraceResult warm = Trace(surface, scenario.Item2, limits, workspace: workspace, mode: mode);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            SelectedGroundTraceResult result = default;
            for (int i = 0; i < iterations; i++) result = Trace(surface, scenario.Item2, limits, workspace: workspace, mode: mode);
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(warm, result, "reused workspace does not alter deterministic output or work counters");
            Console.WriteLine($"selected-trace-bench scenario={scenario.Item1} mode={mode} iterations={iterations} status={result.Status} " +
                $"us_per_query={watch.Elapsed.TotalMicroseconds / iterations:F3} allocated_bytes_per_query={allocated / iterations} " +
                $"nodes={result.Diagnostics.TerrainNodes + result.Diagnostics.FormationNodes} cells={result.Diagnostics.TerrainCells} " +
                $"vertices={result.Diagnostics.Vertices} triangles={result.Diagnostics.Triangles} filtered={result.Diagnostics.FilteredTests} " +
                $"filtered_hits={result.Diagnostics.FilteredHits} exact={result.Diagnostics.ExactTests} " +
                $"exact_step_charge={result.Diagnostics.ReservedExactArithmeticSteps} max_integer_bits={result.Diagnostics.MaximumExactIntegerBits} " +
                $"workspace_reserved={workspace.ReservedScratchBytes}");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "bounded benchmark scenario exceeded its diagnostic guard");
        }
    }

    private static SelectedGroundTraceLimits Limits() => new()
    {
        MaxTerrainNodes = 8192, MaxTerrainCells = 2048, MaxVertices = 8192, MaxVertexLookups = 131072,
        MaxFormationNodes = 4096, MaxMonuments = 32, MaxTriangles = 16384, MaxExactTests = 4096,
        MaxEstimatedScratchBytes = 4 * 1024 * 1024,
    };
    private static SelectedGroundTraceResult Trace(Terrain surface, SelectedGroundTraceRequest request,
        SelectedGroundTraceLimits? limits = null, CancellationToken cancellation = default, SelectedGroundTraceWorkspace? workspace = null,
        SelectedGroundTrace.PredicateMode mode = SelectedGroundTrace.PredicateMode.Filtered) =>
        SelectedGroundTrace.Trace(surface, surface.Formations, ReadOnlySpan<SelectedGroundCollision.Monument>.Empty,
            request, limits ?? Limits(), cancellation, workspace, mode);
    private static SelectedGroundHit Hit(SelectedGroundTraceResult result)
    {
        Assert.Equal(SelectedGroundTraceStatus.Complete, result.Status, "query must complete before its hit is used");
        return result.Hit ?? throw new TestFailure("Expected a selected physical hit.");
    }
    private static void AssertClear(SelectedGroundTraceResult result)
    { Assert.Equal(SelectedGroundTraceStatus.Complete, result.Status, "only completed queries may report clear"); Assert.True(result.Hit == null, "completed query is clear"); }
    private static void AssertIncomplete(SelectedGroundTraceResult result, SelectedGroundTraceStatus status)
    { Assert.Equal(status, result.Status, "incomplete reason is explicit"); Assert.True(result.Hit == null, "incomplete query never publishes provisional evidence"); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new TestFailure("Expected " + typeof(T).Name); }

    private static FormationPiece Quad(ulong id, double height)
    {
        var geometry = new FormationGeometry("trace-quad", 0, "trace-fixture",
            new[] { new Vector3D(-2, -2, 0), new Vector3D(2, -2, 0), new Vector3D(2, 2, 0), new Vector3D(-2, 2, 0) },
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) },
            new[] { new SupportPatch("face", "rock", SupportCapabilities.None) });
        return new FormationPiece(new(new FormationId(id, 0), 0), "face", geometry,
            new FormationTransform(new(0, 0, height), Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 1));
    }

    // Independent ordinary floating Moller test against the actual float collision output; it does not call
    // the production predicate/cone/lattice triangle assembly. Tests aim inside faces, away from its edge errors.
    private static double MeshDistance(TriangleMesh mesh, SelectedGroundTraceRequest ray, out int ordinal)
    {
        double nearest = double.PositiveInfinity; ordinal = -1;
        Vector3D Vertex(int index) => new(mesh.Positions[index * 3], mesh.Positions[index * 3 + 1], mesh.Positions[index * 3 + 2]);
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3D a = Vertex(mesh.Indices[i]), b = Vertex(mesh.Indices[i + 1]), c = Vertex(mesh.Indices[i + 2]);
            Vector3D e1 = b - a, e2 = c - a, p = Vector3D.Cross(ray.Direction, e2), t = ray.Origin - a;
            double det = Vector3D.Dot(e1, p);
            if (Math.Abs(det) < 1e-15) continue;
            double u = Vector3D.Dot(t, p) / det;
            Vector3D q = Vector3D.Cross(t, e1);
            double v = Vector3D.Dot(ray.Direction, q) / det, distance = Vector3D.Dot(e2, q) / det;
            if (u < 0 || v < 0 || u + v > 1 || distance < ray.MinDistance || distance > ray.Length || distance >= nearest) continue;
            nearest = distance; ordinal = i / 3;
        }
        return ordinal < 0 ? -1 : nearest;
    }

    private sealed class Terrain : ISurface, IGradedTerrain, ITerrainLatticeSurface, IFormationSurface, ITerrainPinnedSurface
    {
        private readonly FormationPiece[] _pieces;
        internal Terrain(params FormationPiece[] pieces) { _pieces = pieces; RebuildField(); }
        public double RadiusValue { get; set; } = 10;
        public TerrainLattice TerrainLattice { get; set; } = new(10, 0);
        public double Radius => RadiusValue;
        public double SeaRadius => 0;
        public double Height { get; set; }
        public bool IsFinalized => true;
        public Action? SampleAction { get; set; }
        public FormationField Formations { get; private set; } = null!;
        public TerrainPinSet? TerrainPins { get; private set; }
        public double HeightAt(Vector3D direction) { SampleAction?.Invoke(); return Height; }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new("trace-fixture", 10, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
        internal Vector3D Point(CubeVertexKey key) => TerrainPins != null && TerrainPins.TryGet(key, out TerrainPinnedVertex pin)
            ? pin.Position : TerrainLattice.Direction(key) * (Radius + Height);
        internal void RebuildField(params FormationTerrainPin[] pins)
        {
            var cohort = new FormationCohort(new FormationId(1, 0), Array.Empty<FormationPiece>(), Array.Empty<FormationContactSealReceipt>(),
                Array.Empty<TerrainLatticeTriangle>(), pins, Array.Empty<TileKey>());
            var contract = new FormationFieldContract(Identity, Radius, TerrainLattice.FinestTileLevel, FormationFieldAdmissionPolicy.IndependentPieces);
            Formations = new FormationField(contract, Generation, _pieces, new[] { cohort }, Array.Empty<FormationFieldRejection>(),
                new FormationSupportIndex(_pieces), default, default);
        }
        internal void InstallPin(FormationTerrainPin pin)
        {
            RebuildField(pin);
            TerrainPins = TerrainPinSet.Build(this, TerrainLattice, Identity, Formations, new TerrainPinSetLimits
            {
                MaxVertices = 16, MaxInputPins = 16, MaxNormalTriangles = 128, MaxAdditionalSamples = 128,
                MaxEstimatedResidentBytes = 65536, MaxEstimatedScratchBytes = 65536,
            });
        }
    }
}
