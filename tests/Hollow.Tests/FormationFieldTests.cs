using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationFieldTests
{
    [Test]
    public static void FormationFieldPublishesOnlySealedStableSlotsAndSeparateProposalCounts()
    {
        var fixture = new Fixture();
        FormationPiece rejected = fixture.Piece(3, new Vector3D(0.4, 0.4, 11));
        FormationPiece accepted = fixture.Piece(7, new Vector3D(0.4, 0.4, 9));
        FormationField field = fixture.Build(rejected, accepted);
        Assert.Equal(1, field.Pieces.Length, "only the buried piece survives");
        Assert.Equal(7, field.Pieces[0].Id.Slot, "rejection never renumbers the original slot");
        Assert.Equal(3, field.Rejections[0].Piece.Slot, "the rejected original slot remains explicit");
        Assert.Equal(FormationFieldRejectionReason.IncompleteSeal, field.Rejections[0].Reason, "complete uncovered geometry is a refusal");
        Assert.Equal(2, field.ProposalDiagnostics.AcceptedPieces, "planner accepted count remains explicitly pre-seal");
        Assert.Equal(1, field.Diagnostics.AcceptedPieces, "field diagnostic reports the final physical count");
        FormationCohort cohort = field.Cohort(fixture.Id) ?? throw new TestFailure("A sealed cohort is required.");
        Assert.Equal(1, cohort.Pieces.Length, "cohort contains only final pieces");
        Assert.Equal(1, cohort.Receipts.Length, "rejected geometry publishes no successful receipt");
        FormationContactSealReceipt receipt = cohort.Receipts[0];
        Assert.True(ReferenceEquals(accepted, receipt.Piece), "receipt binds the exact unchanged physical placement");
        Assert.Equal(fixture.Selection.Identity.GenerationDigest, receipt.GenerationDigest, "receipt binds selected generation identity");
        Assert.Equal(fixture.Selection.Identity.PhysicalContentDigest, receipt.PhysicalContentDigest, "receipt binds physical content identity");
        Assert.Equal(fixture.Selection.FieldContract!.TerrainIdentity, receipt.Contract.TerrainIdentity, "receipt binds finalized terrain identity");
        Assert.True(receipt.Witnesses.Length > 0 && receipt.Pins.Length > 0, "successful receipt carries real terrain evidence");
        VerifyPins(fixture, receipt);
    }

    [Test]
    public static void FormationFieldWholeFormationPolicyRejectsAllWithoutPromotingOrRefitting()
    {
        var fixture = new Fixture(policy: FormationFieldAdmissionPolicy.WholePlannedFormation);
        FormationField field = fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 11)), fixture.Piece(7, new Vector3D(0.4, 0.4, 9)));
        Assert.Equal(0, field.Pieces.Length, "explicit whole-formation policy drops the sealed sibling too");
        Assert.Equal(0, field.Cohorts.Length, "rejected formation has no live dependency cohort");
        Assert.Equal(2, field.Rejections.Length, "both original slots retain reasons");
        Assert.Equal(7, field.Rejections[1].Piece.Slot, "policy rejection retains its slot");
        Assert.Equal(FormationFieldRejectionReason.FormationPolicy, field.Rejections[1].Reason, "policy refusal differs from failed geometry");
    }

    [Test]
    public static void FormationFieldUsesCanonicalTrianglesAndGuardPinsAcrossFaceSeams()
    {
        var fixture = new Fixture();
        FormationField field = fixture.Build(fixture.Piece(3, new Vector3D(6.5, 0, 6.5)));
        Assert.Equal(1, field.Pieces.Length, "a deeply buried seam-crossing ring is admitted");
        FormationContactSealReceipt receipt = field.Cohorts[0].Receipts[0];
        var faces = new HashSet<int>();
        foreach (TerrainLatticeTriangle triangle in receipt.Witnesses)
        {
            faces.Add(triangle.Cell.Face);
            Assert.Equal(fixture.Lattice.Triangle(triangle.Cell, triangle.Half), triangle, "witness uses actual canonical topology");
        }
        Assert.True(faces.Contains(0) && faces.Contains(4), "the seal uses both real incident faces");
        Assert.True(receipt.DependencyTiles.ToArray().Any(t => t.Face == 0) && receipt.DependencyTiles.ToArray().Any(t => t.Face == 4),
            "dependency cohort includes both sides of the cube seam");
        VerifyPins(fixture, receipt);
        // At finest level zero every pin's incident face is also its incident render tile.
        foreach (FormationTerrainPin pin in receipt.Pins)
        {
            int n = 1 << pin.Vertex.Level;
            foreach (int face in Enumerable.Range(0, 6))
            {
                Vector3D normal = CubeSphere.Normal(face);
                int dot = (int)normal.X * pin.Vertex.X + (int)normal.Y * pin.Vertex.Y + (int)normal.Z * pin.Vertex.Z;
                if (dot == n) Assert.True(receipt.DependencyTiles.ToArray().Contains(new TileKey(face, 0, 0, 0)), "every face incident on a pin is a dependency");
            }
        }
    }

    [Test]
    public static void FormationFieldGuardDependenciesIncludeTilesSharingOuterGuardVertices()
    {
        var fixture = new Fixture(level: 1);
        FormationField field = fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        FormationContactSealReceipt receipt = field.Cohorts[0].Receipts[0];
        var tiles = new HashSet<TileKey>(receipt.DependencyTiles.ToArray());
        foreach (FormationTerrainPin pin in receipt.Pins)
        foreach (TileKey tile in Enumerable.Range(0, 6).SelectMany(face => Enumerable.Range(0, 2).SelectMany(y =>
            Enumerable.Range(0, 2).Select(x => new TileKey(face, 1, x, y)))))
        {
            if (fixture.Lattice.EnumerateVertices(tile).Contains(pin.Vertex))
                Assert.True(tiles.Contains(tile), "every finest tile sharing a guard pin participates in the cohort");
        }
    }

    [Test]
    public static void FormationFieldAggregatesSealAttemptsAndArithmeticAcrossPieces()
    {
        var fixture = new Fixture(); Vector3D position = new(0.4, 0.4, 9);
        FormationPiece first = fixture.Piece(3, position), second = fixture.Piece(7, position);
        FormationField one = fixture.Build(first), two = fixture.Build(first, second);
        Assert.True(one.Diagnostics.SegmentTriangleAttempts > 0 && one.Diagnostics.ExactArithmeticOperations > 0, "one seal consumes real work");
        Assert.Equal(2 * one.Diagnostics.SegmentTriangleAttempts, two.Diagnostics.SegmentTriangleAttempts, "same geometry in another slot consumes a second call's work");
        Reject<FormationCapacityException>(() => fixture.Build(Limits() with { MaxSegmentTriangleAttempts = one.Diagnostics.SegmentTriangleAttempts }, first, second));
        Reject<FormationCapacityException>(() => fixture.Build(Limits() with { MaxExactArithmeticOperations = one.Diagnostics.ExactArithmeticOperations }, first, second));
        FormationField exact = fixture.Build(Limits() with
        {
            MaxSegmentTriangleAttempts = two.Diagnostics.SegmentTriangleAttempts,
            MaxExactArithmeticOperations = two.Diagnostics.ExactArithmeticOperations
        }, first, second);
        Assert.Equal(2, exact.Pieces.Length, "exact aggregate work allowance admits the same complete physical result");
        Assert.Equal(one.Cohorts[0].Pins.Length, two.Cohorts[0].Pins.Length, "cohort union deduplicates shared pins");
    }

    [Test]
    public static void FormationFieldNarrowsDenseAuthoredLoopsBeforeTheExactPairBudget()
    {
        var fixture = new Fixture(subdivisions: 80);
        FormationFieldLimits limits = Limits();
        limits = limits with
        {
            Seal = limits.Seal with { MaxTerrainTriangles = 16 },
            Support = limits.Support with { MaxTriangles = 1024, MaxUniqueTriangles = 512, MaxNodes = 2048 }
        };
        FormationField field = fixture.Build(limits, fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        Assert.Equal(1, field.Pieces.Length, "a 320-vertex authored ring fits the bounded narrowed candidate gate");
        FormationContactSealReceipt receipt = field.Cohorts[0].Receipts[0];
        Assert.Equal(320, receipt.Loop.VertexIndices.Length, "no contact-loop simplification changes the authored points");
        Assert.True(receipt.Diagnostics.TerrainTriangles <= 16, "only conservatively relevant radial triangles enter the exact gate");
        Assert.True(receipt.Diagnostics.SegmentTriangleAttempts <= FormationTerrainSeal.MaximumAttempts, "one aggregate exact pair budget covers the complete loop");
        VerifyPins(fixture, receipt);
    }

    [Test]
    public static void FormationFieldCapacityAndCancellationNeverPublishPartialGeometry()
    {
        var fixture = new Fixture(); FormationPiece piece = fixture.Piece(3, new Vector3D(0.4, 0.4, 9));
        FormationFieldLimits normal = Limits();
        FormationFieldLimits[] refused =
        {
            normal with { MaxPieces = 0 }, normal with { MaxLoopChecks = 0 }, normal with { MaxReceipts = 0 },
            normal with { MaxCohorts = 0 }, normal with { MaxDependencyTriangles = 0 }, normal with { MaxPinnedVertices = 0 },
            normal with { MaxDependencyTiles = 0 }, normal with { MaxCandidateTrianglesPerLoop = 0 },
            normal with { MaxCandidateCellVisits = 0 }, normal with { MaxTerrainVertexSamples = 0 },
            normal with { MaxCandidateTriangleTests = 0 }, normal with { MaxSegmentTriangleAttempts = 0 },
            normal with { MaxExactArithmeticOperations = 0 }, normal with { MaxEstimatedResidentBytes = 0 },
            normal with { MaxEstimatedScratchBytes = 0 }, normal with { Seal = normal.Seal with { MaxTerrainTriangles = 0 } },
            normal with { Seal = normal.Seal with { MaxEstimatedScratchBytes = long.MaxValue } },
            normal with { Support = normal.Support with { MaxTriangles = 0 } }
        };
        foreach (FormationFieldLimits limits in refused) Reject<FormationCapacityException>(() => fixture.Build(limits, piece));
        Reject<FormationCapacityException>(() => fixture.Build(normal with { MaxRejections = 0 }, fixture.Piece(3, new Vector3D(0.4, 0.4, 11))));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Reject<OperationCanceledException>(() => fixture.Build(normal, cancellation.Token, piece));
        using var late = new CancellationTokenSource();
        var cancelling = new Fixture(terrain: new FlatTerrain(late));
        Reject<OperationCanceledException>(() => cancelling.Build(normal, late.Token, cancelling.Piece(3, new Vector3D(0.4, 0.4, 9))));
    }

    [Test]
    public static void FormationFieldPhysicalQueriesAndCollisionRetainCapabilityNoneAndWholePieces()
    {
        var fixture = new Fixture(); FormationPiece piece = fixture.Piece(7, new Vector3D(0.4, 0.4, 9));
        FormationField field = fixture.Build(piece);
        FormationBounds pointVolume = new(new Vector3D(0.4, 0.4, 9), new Vector3D(0.4, 0.4, 9));
        FormationFieldPieceQuery query = field.QueryPieces(pointVolume, QueryLimits());
        Assert.Equal(FormationQueryStatus.Complete, query.Status, "bounded physical query completes");
        Assert.Equal(7, query.Pieces[0].Id.Slot, "query preserves physical identity");
        var ray = new FormationRayRequest(new Vector3D(0.43, 0.4, 12), -Vector3D.UnitZ, 5);
        FormationRayResult hit = field.Raycast(ray, new FormationSupportQueryLimits());
        Assert.True(hit.Status == FormationQueryStatus.Complete && hit.Hits.Length > 0, "capability-None physical wall still obstructs a ray");
        Assert.Equal(SupportCapabilities.None, hit.Hits[0].Capabilities, "ray does not invent support permission");
        Reject<FormationValidationException>(() => field.Raycast(ray with { RequiredCapabilities = SupportCapabilities.Plantable }, new FormationSupportQueryLimits()));
        var terrain = new TriangleMesh(new float[] { -1, -1, 0, 1, -1, 0, 0, 1, 0 }, new[] { 0, 1, 2 });
        var limits = new FormationFieldCollisionLimits { Query = QueryLimits(), MaxVertices = 100, MaxTriangles = 100, MaxEstimatedOutputBytes = 16384 };
        FormationFieldCollisionResult result = field.AppendCollision(terrain, ZoneFrame.World, pointVolume, limits);
        Assert.Equal(FormationQueryStatus.Complete, result.Status, "collision completes within explicit output limits");
        TriangleMesh mesh = result.Mesh ?? throw new TestFailure("Complete collision needs a mesh.");
        Assert.Equal(terrain.VertexCount + piece.Geometry.VertexCount, mesh.VertexCount, "small query volume still appends the whole intersecting piece");
        Assert.Equal(terrain.TriangleCount + piece.Geometry.TriangleCount, mesh.TriangleCount, "all physical faces are collision faces");
        Assert.True(mesh.Positions.AsSpan(0, terrain.Positions.Length).SequenceEqual(terrain.Positions), "base vertices are unchanged");
        Assert.True(mesh.Indices.AsSpan(0, terrain.Indices.Length).SequenceEqual(terrain.Indices), "base indices are unchanged");
        FormationFieldCollisionResult small = field.AppendCollision(terrain, ZoneFrame.World, pointVolume, limits with { MaxVertices = terrain.VertexCount });
        Assert.True(small.Status == FormationQueryStatus.CapacityExceeded && small.Mesh == null, "no partial base mesh escapes output exhaustion");
        FormationFieldPieceQuery incomplete = field.QueryPieces(pointVolume, QueryLimits() with { MaxPieces = 0 });
        Assert.True(incomplete.Status == FormationQueryStatus.CapacityExceeded && incomplete.Pieces.Length == 0, "incomplete query is distinct from complete empty");
        FormationFieldPieceQuery empty = field.QueryPieces(new FormationBounds(new Vector3D(-5, -5, -5), new Vector3D(-4, -4, -4)), QueryLimits());
        Assert.True(empty.Status == FormationQueryStatus.Complete && empty.Pieces.Length == 0, "empty spatial result is explicitly complete");
    }

    [Test]
    public static void FormationFieldValidatesSelectionBeforeSurveyAndPublicFlatBuildIsCompleteEmpty()
    {
        var fixture = new Fixture();
        FormationField field = FormationField.Build(fixture.Terrain, fixture.Lattice, fixture.Selection, Limits());
        Assert.Equal(0, field.Pieces.Length, "a flat finalized world has a complete empty field");
        Assert.Equal(0, field.Rejections.Length, "empty planning is not reported as a geometric failure");
        var legacy = new Fixture(loops: false);
        Reject<FormationValidationException>(() => FormationField.Build(legacy.Terrain, legacy.Lattice, legacy.Selection, Limits()));
        Assert.Equal(0, legacy.Terrain.Calls, "missing closed loops are rejected before survey");
        FormationWorldSelection missing = FormationWorldSelection.Create("formation-no-contract", "test-grading", fixture.Catalog, Settings());
        Reject<FormationValidationException>(() => FormationField.Build(fixture.Terrain, fixture.Lattice, missing, Limits()));
        Reject<FormationValidationException>(() => FormationField.Build(fixture.Terrain, new TerrainLattice(11, 0), fixture.Selection, Limits()));
        var region = new FormationSurveyRegion(1, new[] { new TileKey(4, 1, 0, 0) }, 1);
        FormationWorldSelection regional = FormationWorldSelection.Create("formation-regional", "test-grading", fixture.Catalog,
            Settings(region), fieldContract: fixture.Selection.FieldContract);
        Reject<FormationValidationException>(() => FormationField.Build(fixture.Terrain, fixture.Lattice, regional, Limits()));
    }

    private static void VerifyPins(Fixture fixture, FormationContactSealReceipt receipt)
    {
        var pins = receipt.Pins.ToArray().ToDictionary(p => p.Vertex, p => p.Position);
        foreach (FormationTerrainPin pin in receipt.Pins)
            Assert.Equal(fixture.Lattice.Direction(pin.Vertex) * fixture.Terrain.Radius, pin.Position, "pin freezes the actual canonical terrain position");
        foreach (TerrainLatticeTriangle triangle in receipt.Witnesses)
            Assert.True(pins.ContainsKey(triangle.A) && pins.ContainsKey(triangle.B) && pins.ContainsKey(triangle.C), "every selected witness vertex is pinned");
        Vector3D[] loop = receipt.Loop.VertexIndices.ToArray().Select(i => receipt.Piece.Transform.Transform(receipt.Piece.Geometry.Vertices[i])).ToArray();
        TerrainSealTriangle[] terrain = receipt.Witnesses.ToArray().Select(t => new TerrainSealTriangle(t, pins[t.A], pins[t.B], pins[t.C])).ToArray();
        FormationTerrainSealResult replay = FormationTerrainSeal.Evaluate(loop, terrain, FormationFieldContract.MinimumPlaneBurialMetres);
        Assert.True(replay.IsSealed, "the published witness subset independently proves every segment including closure");
    }

    private static FormationFieldQueryLimits QueryLimits() => new() { MaxVisitedNodes = 100, MaxPieces = 8, MaxEstimatedBytes = 65536 };
    private static FormationPlannerSettings Settings(FormationSurveyRegion? region = null) =>
        new("recipe", 0, "flat", 1, 0.5, 1, 2, 1, 1, 2, Vector3D.UnitY, "crest", 0, region);
    private static FormationFieldLimits Limits() => new()
    {
        Planner = new FormationPlannerLimits { MaxRecords = 8, MaxContourSegments = 32, MaxProtectedSites = 0 },
        Support = new FormationSupportIndexLimits { MaxPieces = 8, MaxTriangles = 128, MaxUniqueGeometries = 2, MaxUniqueTriangles = 32, MaxNodes = 256 },
        Seal = new FormationTerrainSealLimits { MaxTerrainTriangles = 256, MaxDependencyTriangles = 256 },
        MaxPieces = 8, MaxLoopChecks = 32, MaxReceipts = 32, MaxCohorts = 8, MaxRejections = 8,
        MaxCandidateTrianglesPerLoop = 2048, MaxDependencyTriangles = 2048, MaxPinnedVertices = 4096, MaxDependencyTiles = 1024,
        MaxCandidateCellVisits = 100000, MaxTerrainVertexSamples = 100000, MaxCandidateTriangleTests = 1000000,
        MaxSegmentTriangleAttempts = 1000000, MaxExactArithmeticOperations = 100000000,
        MaxEstimatedResidentBytes = 256L * 1024 * 1024, MaxEstimatedScratchBytes = 256L * 1024 * 1024
    };
    private sealed class Fixture
    {
        internal Fixture(bool loops = true, FormationFieldAdmissionPolicy policy = FormationFieldAdmissionPolicy.IndependentPieces,
            int level = 0, FlatTerrain? terrain = null, int subdivisions = 1)
        {
            Terrain = terrain ?? new FlatTerrain(); Lattice = new TerrainLattice(Terrain.Radius, level);
            var patch = new SupportPatch("face", "rock", SupportCapabilities.None);
            int count = 4 * subdivisions;
            var vertices = new Vector3D[count + 1]; var triangles = new GeometryTriangle[count];
            for (int i = 0; i < count; i++)
            {
                double t = (double)(i % subdivisions) / subdivisions;
                vertices[i] = (i / subdivisions) switch
                {
                    0 => new Vector3D(-0.15 + 0.3 * t, -0.15, 0), 1 => new Vector3D(0.15, -0.15 + 0.3 * t, 0),
                    2 => new Vector3D(0.15 - 0.3 * t, 0.15, 0), _ => new Vector3D(-0.15, 0.15 - 0.3 * t, 0)
                };
                triangles[i] = new GeometryTriangle(i, (i + 1) % count, count, 0);
            }
            Geometry = new FormationGeometry("quad", 0, "test", vertices, triangles, new[] { patch });
            const string digest = "0000000000000000000000000000000000000000000000000000000000000000";
            int[] ring = Enumerable.Range(0, count).ToArray();
            FormationContactLoop[] contacts = loops ? new[] { new FormationContactLoop("ring", ring) } : Array.Empty<FormationContactLoop>();
            FormationContactLoopBinding[] bindings = loops ? new[] { new FormationContactLoopBinding("ring", ring) } : Array.Empty<FormationContactLoopBinding>();
            CliffLodTriangleMap[] maps = Enumerable.Range(0, count).Select(i => new CliffLodTriangleMap(i, i)).ToArray();
            var definition = new CliffSupportDefinition(Geometry, digest, digest, 1, 1,
                new[] { new CliffPatchDomain("face", new[] { new CliffPatchBoundary(ring) }) },
                new[] { new CliffContactDomain("anchor", "crest", "face", new[] { 0, 1 }, 0, 1, 0) },
                new[] { new CliffLodBinding("near", digest, Geometry, maps, 0, 1, bindings), new CliffLodBinding("far", digest, Geometry, maps, 0, 1, bindings) },
                contactLoops: contacts);
            var choice = new FormationGeometryChoice(Geometry.Key, 0, new[] { "anchor" }, 1, 1);
            var recipe = new FormationRecipe("recipe", 0, 33, "test", new[]
            { new FormationRecipeSlot(3, "wall", true, new[] { choice }), new FormationRecipeSlot(7, "wall", false, new[] { choice }) }, 10, 100, 0, 2, 1);
            Catalog = new FormationCatalog(new[] { definition }, new[] { new SubstrateProfile("rock", 1, 0, 0, 0, 0, 0, 0, 0) }, new[] { recipe });
            Selection = FormationWorldSelection.Create("formation-field-test", "test-grading", Catalog, Settings(),
                fieldContract: new FormationFieldContract(digest, Terrain.Radius, level, policy));
        }
        internal FlatTerrain Terrain { get; }
        internal TerrainLattice Lattice { get; }
        internal FormationCatalog Catalog { get; }
        internal FormationGeometry Geometry { get; }
        internal FormationWorldSelection Selection { get; }
        private FormationContourIdentity Identity => new(4, 1, 0, 0, 0, 1, 33, 0, 0);
        internal FormationId Id => Identity.Id;
        internal FormationPiece Piece(int slot, Vector3D position) => new(new FormationPieceId(Id, slot), "wall", Geometry,
            new FormationTransform(position, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 1));
        internal FormationField Build(params FormationPiece[] pieces) => Build(Limits(), default, pieces);
        internal FormationField Build(FormationFieldLimits limits, params FormationPiece[] pieces) => Build(limits, default, pieces);
        internal FormationField Build(FormationFieldLimits limits, CancellationToken cancellation, params FormationPiece[] pieces)
        {
            FormationBounds bounds = pieces[0].Bounds;
            foreach (FormationPiece piece in pieces) bounds = bounds.Union(piece.Bounds);
            var guides = new[] { bounds.Min, bounds.Max };
            var record = new FormationRecord(Id, "recipe", 0, "test", guides, guides, guides, pieces,
                Array.Empty<FormationRejection>(), bounds, new FormationCoverage(100, 100, 0));
            FormationPlanDiagnostics diagnostics = default;
            var plan = new FormationPlan(new[] { new FormationPlanEntry(Identity, record) },
                diagnostics with { Records = 1, AcceptedPieces = pieces.Length }, cancellation);
            return FormationFieldBuilder.BuildPlanned(Terrain, Lattice, Selection, limits, plan, cancellation);
        }
    }
    private sealed class FlatTerrain(CancellationTokenSource? cancellation = null) : IGradedTerrain
    {
        internal int Calls { get; private set; }
        public bool IsFinalized => true;
        public double Radius => 10;
        public double HeightAt(Vector3D direction) { Calls++; if (Calls == 3) cancellation?.Cancel(); return 0; }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new("flat", 15, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
    }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new TestFailure("Expected " + typeof(T).Name + "."); }
}
