using System;
using System.Linq;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class SelectedGroundCollisionTests
{
    [Test]
    public static void SelectedCollisionAppendsSealedRaisedFacesAndRejectsTheOppositePlanetSide()
    {
        var fixture = new Fixture();
        FormationPiece near = fixture.Piece(3, new Vector3D(0.4, 0.4, 9));
        FormationPiece far = fixture.Piece(7, new Vector3D(0.4, 0.4, -9), opposite: true);
        fixture.Build(far, near);
        Assert.Equal(2, fixture.Field.Pieces.Length, "both real buried-loop fixtures qualify before collision selection");
        TileKey tile = TileKey.At(near.Transform.Position, 2);
        TerrainCollisionMesh ground = SurfaceGrid.CollisionWithFootprint(fixture.Terrain, ZoneFrame.World, tile, 0, Limits().Terrain);
        TriangleMesh mesh = fixture.Collision(tile);
        Assert.Equal(ground.Mesh.VertexCount + near.Geometry.VertexCount, mesh.VertexCount, "only the nearby physical piece is appended");
        Assert.Equal(ground.Mesh.TriangleCount + near.Geometry.TriangleCount, mesh.TriangleCount, "complete physical topology is retained");
        Assert.True(mesh.Positions.AsSpan(0, ground.Mesh.Positions.Length).SequenceEqual(ground.Mesh.Positions), "the canonical base positions remain exact");
        Assert.True(mesh.Indices.AsSpan(0, ground.Mesh.Indices.Length).SequenceEqual(ground.Mesh.Indices), "the canonical base triangles retain their order");
        Assert.True(near.Bounds.Max.Z > fixture.Terrain.Radius, "the raised peak is outside the base terrain sample bounds");
        CheckPiece(mesh, ground.Mesh.VertexCount, near, ZoneFrame.World);
        Assert.True(ground.Footprint.MayIntersect(near.Bounds), "radial footprint keeps raised wall and shelf geometry");
        Assert.True(!ground.Footprint.MayIntersect(far.Bounds), "opposite planet geometry is conservatively excluded");
    }

    [Test]
    public static void SelectedCollisionOrderAndRebasedPhysicalVerticesAreDeterministic()
    {
        var first = new Fixture();
        FormationPiece a = first.Piece(3, new Vector3D(0.3, 0.3, 9));
        FormationPiece b = first.Piece(7, new Vector3D(0.7, 0.3, 9));
        first.Build(b, a);
        var second = new Fixture(); second.Build(a, b);
        TileKey tile = TileKey.At(a.Transform.Position, 2);
        TriangleMesh one = first.Collision(tile), two = second.Collision(tile);
        Assert.True(one.Positions.AsSpan().SequenceEqual(two.Positions) && one.Indices.AsSpan().SequenceEqual(two.Indices),
            "fresh fields and shuffled input preserve canonical physical output order");
        ZoneFrame frame = new(new Vector3D(3, 4, 8));
        TriangleMesh rebased = first.Collision(tile, frame: frame);
        int baseVertices = SurfaceGrid.CollisionWithFootprint(first.Terrain, frame, tile, 0, Limits().Terrain).Mesh.VertexCount;
        CheckPiece(rebased, baseVertices, a, frame);
        CheckPiece(rebased, baseVertices + a.Geometry.VertexCount, b, frame);
        Assert.True(rebased.Indices.AsSpan().SequenceEqual(one.Indices), "rebasing never changes composite topology or primitive order");
    }

    [Test]
    public static void CanonicalFootprintsCoverEveryDirectedSeamCornerAndTallBoundaryPoint()
    {
        Vector3D unresolved = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Reject<FormationValidationException>(() => _ = new FormationBounds(unresolved, unresolved));
        foreach (double radius in new[] { 2.0, 1354.0 })
        {
            var terrain = new Terrain(radius, 0);
            TerrainLattice lattice = terrain.TerrainLattice;
            int side = 1 << lattice.VertexLevel;
            for (int face = 0; face < 6; face++)
            foreach ((int x, int y) in new[] { (0, side / 2), (side - 1, side / 2), (side / 2, 0), (side / 2, side - 1),
                (0, 0), (side - 1, 0), (0, side - 1), (side - 1, side - 1) })
            {
                var core = new TileKey(face, lattice.VertexLevel, x, y);
                double margin = TileKey.EdgeLength(lattice.VertexLevel, radius) * 0.5;
                TerrainCollisionMesh result = SurfaceGrid.CollisionWithFootprint(terrain, ZoneFrame.World, core, margin, Limits().Terrain);
                TriangleMesh unchanged = SurfaceGrid.Collision(terrain, ZoneFrame.World, core, margin, 1, Limits().Terrain);
                Assert.True(result.Mesh.Positions.AsSpan().SequenceEqual(unchanged.Positions) && result.Mesh.Indices.AsSpan().SequenceEqual(unchanged.Indices),
                    "requesting the footprint preserves the exact canonical margin mesh");
                Assert.True(result.Footprint.Ranges.Length >= 2, "the actual incident faces enter the footprint");
                for (int i = 0; i < result.Mesh.Positions.Length; i += 3)
                {
                    Vector3D point = new(result.Mesh.Positions[i], result.Mesh.Positions[i + 1], result.Mesh.Positions[i + 2]);
                    point *= 3;
                    Assert.True(result.Footprint.MayIntersect(new FormationBounds(point, point)), "every actual margin vertex remains covered at arbitrary positive height");
                }
                Assert.True(result.Footprint.MayIntersect(new FormationBounds(new(-1, -1, -1), new(1, 1, 1))),
                    "an origin-containing physical bound is never misclassified as empty");
            }
        }
    }

    [Test]
    public static void CanonicalFootprintSaturatesWithTheExactTinyWorldCollisionSphere()
    {
        var terrain = new Terrain(2, 0);
        int level = terrain.TerrainLattice.VertexLevel, side = 1 << level;
        double margin = TerrainLattice.MaximumMarginCells * TileKey.EdgeLength(level, terrain.Radius);
        TerrainCollisionMesh result = SurfaceGrid.CollisionWithFootprint(terrain, ZoneFrame.World, new TileKey(0, 0, 0, 0), margin, Limits().Terrain);
        Assert.Equal(6, result.Footprint.Ranges.Length, "saturated collision has all six face ranges");
        Assert.Equal(2 * 6 * side * side, result.Mesh.TriangleCount, "saturation retains each canonical sphere triangle exactly once");
        foreach (TerrainFaceCellRange range in result.Footprint.Ranges)
        {
            Assert.Equal(0, range.X0, "full-face footprint begins at the first canonical column");
            Assert.Equal(0, range.Y0, "full-face footprint begins at the first canonical row");
            Assert.Equal(side - 1, range.X1, "full-face footprint ends at the last canonical column");
            Assert.Equal(side - 1, range.Y1, "full-face footprint ends at the last canonical row");
        }
    }

    [Test]
    public static void SealedModuleAtACubeSeamAppearsOnBothIncidentCollisionTiles()
    {
        var fixture = new Fixture();
        FormationPiece piece = fixture.Piece(3, new Vector3D(6.3, 0, 6.3)); fixture.Build(piece);
        Assert.Equal(1, fixture.Field.Pieces.Length, "the seam module has a complete terrain seal");
        foreach (TileKey tile in new[] { new TileKey(0, 2, 0, 2), new TileKey(4, 2, 3, 2) })
        {
            TerrainCollisionMesh ground = SurfaceGrid.CollisionWithFootprint(fixture.Terrain, ZoneFrame.World, tile, 0.1, Limits().Terrain);
            TriangleMesh complete = fixture.Collision(tile, margin: 0.1);
            Assert.Equal(ground.Mesh.TriangleCount + piece.Geometry.TriangleCount, complete.TriangleCount,
                "both incident tiles retain the complete seam piece without extrapolated cells");
        }
    }

    [Test]
    public static void PhysicalMonumentsUseTheSameRadialFootprintAndStableCompositeOrder()
    {
        var fixture = new Fixture();
        FormationPiece piece = fixture.Piece(3, new Vector3D(0.4, 0.4, 9)); fixture.Build(piece);
        TileKey tile = TileKey.At(piece.Transform.Position, 1);
        var near = new MonumentPlacement(tile, 0, new Vector3D(20, 20, 1000), Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 0);
        var far = near with { Position = new Vector3D(20, 20, -1000) };
        SelectedGroundCollision.Monument[] monuments = SelectedGroundCollision.Snapshot(new[] { near, far }, 2, default);
        TerrainCollisionMesh ground = SurfaceGrid.CollisionWithFootprint(fixture.Terrain, ZoneFrame.World, tile, 0, Limits().Terrain);
        TriangleMesh mesh = SelectedGroundCollision.Build(fixture.Terrain, ZoneFrame.World, tile, 0, fixture.Field, monuments, Limits());
        Assert.Equal(ground.Mesh.VertexCount + piece.Geometry.VertexCount + near.Definition.Positions.Length, mesh.VertexCount,
            "the tall nearby monument is selected and the opposite-side monument is rejected");
        int offset = ground.Mesh.VertexCount + piece.Geometry.VertexCount;
        for (int i = 0; i < near.Definition.Positions.Length; i++)
        {
            var expected = ZoneFrame.World.ToLocal(near.Transform(near.Definition.Positions[i]));
            Assert.Equal(expected.X, mesh.Positions[(offset + i) * 3], "monument physical X is appended after formations");
            Assert.Equal(expected.Y, mesh.Positions[(offset + i) * 3 + 1], "monument physical Y is appended after formations");
            Assert.Equal(expected.Z, mesh.Positions[(offset + i) * 3 + 2], "monument physical Z is appended after formations");
        }
        Reject<FormationCapacityException>(() => SelectedGroundCollision.Build(fixture.Terrain, ZoneFrame.World, tile, 0,
            fixture.Field, monuments, Limits() with { MaxMonuments = 1 }));
    }

    [Test]
    public static void SelectedCollisionRefusesIncompleteQueriesAndCompleteOutputCapacity()
    {
        var fixture = new Fixture(); fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        TileKey tile = TileKey.At(new Vector3D(0.4, 0.4, 9), 2);
        TerrainCollisionMesh ground = SurfaceGrid.CollisionWithFootprint(fixture.Terrain, ZoneFrame.World, tile, 0, Limits().Terrain);
        foreach (SelectedGroundCollisionLimits limits in new[]
        {
            Limits() with { Query = Limits().Query with { MaxPieces = 0 } },
            Limits() with { Query = Limits().Query with { MaxVisitedNodes = 0 } },
            Limits() with { Query = Limits().Query with { MaxEstimatedBytes = 0 } },
            Limits() with { MaxVertices = ground.Mesh.VertexCount },
            Limits() with { MaxTriangles = ground.Mesh.TriangleCount },
        }) Reject<FormationCapacityException>(() => fixture.Collision(tile, limits: limits));
        TerrainCollisionMesh wrong = SurfaceGrid.CollisionWithFootprint(new Terrain(11, 0), ZoneFrame.World, tile, 0, Limits().Terrain);
        Reject<FormationValidationException>(() => fixture.Field.QueryPieces(wrong.Footprint, Limits().Query));
    }

    [Test]
    public static void SelectedCollisionAggregateReservationsRefuseBeforeSampling()
    {
        var fixture = new Fixture(); fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        TileKey tile = TileKey.At(new Vector3D(0.4, 0.4, 9), 2);
        var limits = Limits(); var reserved = limits.Reserve();
        int calls = fixture.Terrain.Calls;
        Reject<FormationCapacityException>(() => fixture.Collision(tile, limits: limits with { MaxEstimatedScratchBytes = reserved.Scratch - 1 }));
        Reject<FormationCapacityException>(() => fixture.Collision(tile, limits: limits with { MaxEstimatedMeshBytes = reserved.Mesh - 1 }));
        Assert.Equal(calls, fixture.Terrain.Calls, "both full-operation byte checks precede terrain sampling");
        TriangleMesh mesh = fixture.Collision(tile, limits: limits with { MaxEstimatedScratchBytes = reserved.Scratch, MaxEstimatedMeshBytes = reserved.Mesh });
        Assert.True(mesh.TriangleCount > 0, "the exact aggregate allowance admits the complete result");
    }

    [Test]
    public static void SelectedCollisionCancellationAndInvalidRadialTerrainCannotPublishABaseOnlyMesh()
    {
        var fixture = new Fixture(); fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        TileKey away = new(0, fixture.Terrain.TerrainLattice.VertexLevel, 0, 0);
        using var cancellation = new CancellationTokenSource();
        int samples = 0; fixture.Terrain.Sample = () => { if (++samples == 4) cancellation.Cancel(); return 0; };
        Reject<OperationCanceledException>(() => fixture.Collision(away, cancellation: cancellation.Token));
        Assert.Equal(4, samples, "final canonical vertex cancellation is observed before composite publication");
        fixture.Terrain.Sample = () => -fixture.Terrain.Radius;
        Reject<FormationValidationException>(() => fixture.Collision(away));
        fixture.Terrain.Sample = () => double.NaN;
        Reject<FormationValidationException>(() => fixture.Collision(away));
    }

    internal static SelectedGroundCollisionLimits Limits() => new()
    {
        Terrain = new TerrainCollisionLimits { MaxCells = 20000, MaxVertices = 30000,
            MaxEstimatedScratchBytes = 16 * 1024 * 1024, MaxEstimatedMeshBytes = 2 * 1024 * 1024 },
        Query = new FormationFieldQueryLimits { MaxVisitedNodes = 100, MaxPieces = 8, MaxEstimatedBytes = 65536 },
        MaxMonuments = 8, MaxVertices = 65536, MaxTriangles = 131072,
        MaxEstimatedScratchBytes = 32 * 1024 * 1024, MaxEstimatedMeshBytes = 4 * 1024 * 1024,
    };

    private static void CheckPiece(TriangleMesh mesh, int offset, FormationPiece piece, ZoneFrame frame)
    {
        for (int i = 0; i < piece.Geometry.VertexCount; i++)
        {
            var expected = frame.ToLocal(piece.Transform.Transform(piece.Geometry.Vertices[i]));
            Assert.Equal(expected.X, mesh.Positions[(offset + i) * 3], "physical X matches the frozen module transform");
            Assert.Equal(expected.Y, mesh.Positions[(offset + i) * 3 + 1], "physical Y matches the frozen module transform");
            Assert.Equal(expected.Z, mesh.Positions[(offset + i) * 3 + 2], "physical Z matches the frozen module transform");
        }
    }

    internal sealed class Fixture
    {
        private const string Digest = "0000000000000000000000000000000000000000000000000000000000000000";
        internal Terrain Terrain { get; } = new(10, 0);
        internal FormationGeometry Geometry { get; }
        internal FormationWorldSelection Selection { get; }
        internal FormationField Field { get; private set; } = null!;
        private FormationContourIdentity Identity => new(4, 1, 0, 0, 0, 1, 33, 0, 0);
        internal Fixture()
        {
            int[] ring = { 0, 1, 2, 3 };
            Geometry = new FormationGeometry("raised", 0, "test", new[] { new Vector3D(-0.15, -0.15, 0), new Vector3D(0.15, -0.15, 0),
                new Vector3D(0.15, 0.15, 0), new Vector3D(-0.15, 0.15, 0), new Vector3D(0, 0, 3) },
                Enumerable.Range(0, 4).Select(i => new GeometryTriangle(i, (i + 1) % 4, 4, 0)),
                new[] { new SupportPatch("face", "rock", SupportCapabilities.None) });
            var contacts = new[] { new FormationContactLoop("ring", ring) };
            var bindings = new[] { new FormationContactLoopBinding("ring", ring) };
            var maps = Enumerable.Range(0, 4).Select(i => new CliffLodTriangleMap(i, i)).ToArray();
            var definition = new CliffSupportDefinition(Geometry, Digest, Digest, 1, 1,
                new[] { new CliffPatchDomain("face", new[] { new CliffPatchBoundary(ring) }) },
                new[] { new CliffContactDomain("anchor", "crest", "face", new[] { 0, 1 }, 0, 1, 0) },
                // The meshes and vertices are identical. The steep fan's normalized double normals have a
                // self-dot slightly below one, so metadata admits only roundoff rather than demanding exact 1.
                new[] { new CliffLodBinding("near", Digest, Geometry, maps, 0, 1 - 1e-12, bindings),
                    new CliffLodBinding("far", Digest, Geometry, maps, 0, 1 - 1e-12, bindings) },
                contactLoops: contacts);
            var choice = new FormationGeometryChoice(Geometry.Key, 0, new[] { "anchor" }, 1, 1);
            var recipe = new FormationRecipe("recipe", 0, 33, "test", new[] { new FormationRecipeSlot(3, "wall", true, new[] { choice }),
                new FormationRecipeSlot(7, "wall", false, new[] { choice }) }, 10, 100, 0, 2, 1);
            var catalog = new FormationCatalog(new[] { definition }, new[] { new SubstrateProfile("rock", 1, 0, 0, 0, 0, 0, 0, 0) }, new[] { recipe });
            var settings = new FormationPlannerSettings("recipe", 0, "flat", 1, 0.5, 1, 2, 1, 1, 2, Vector3D.UnitY, "crest", 0);
            Selection = FormationWorldSelection.Create("formation-collision-fixture", "test-grading", catalog, settings,
                fieldContract: new FormationFieldContract(Digest, Terrain.Radius, 0, FormationFieldAdmissionPolicy.IndependentPieces));
        }
        internal FormationPiece Piece(int slot, Vector3D position, bool opposite = false) => new(new FormationPieceId(Identity.Id, slot), "wall", Geometry,
            new FormationTransform(position, opposite ? -Vector3D.UnitX : Vector3D.UnitX, Vector3D.UnitY,
                opposite ? -Vector3D.UnitZ : Vector3D.UnitZ, 1));
        internal void Build(params FormationPiece[] pieces)
        {
            FormationBounds bounds = pieces[0].Bounds;
            foreach (FormationPiece piece in pieces) bounds = bounds.Union(piece.Bounds);
            var guides = new[] { bounds.Min, bounds.Max };
            var record = new FormationRecord(Identity.Id, "recipe", 0, "test", guides, guides, guides, pieces,
                Array.Empty<FormationRejection>(), bounds, new FormationCoverage(100, 100, 0));
            var plan = new FormationPlan(new[] { new FormationPlanEntry(Identity, record) },
                new FormationPlanDiagnostics { Records = 1, AcceptedPieces = pieces.Length }, default);
            var limits = new FormationFieldLimits
            {
                Planner = new FormationPlannerLimits { MaxRecords = 8, MaxContourSegments = 32, MaxProtectedSites = 0 },
                Support = new FormationSupportIndexLimits { MaxPieces = 8, MaxTriangles = 128, MaxUniqueGeometries = 2, MaxUniqueTriangles = 32, MaxNodes = 256 },
                Seal = new FormationTerrainSealLimits { MaxTerrainTriangles = 256, MaxDependencyTriangles = 256 },
                MaxPieces = 8, MaxLoopChecks = 32, MaxReceipts = 32, MaxCohorts = 8, MaxRejections = 8,
                MaxCandidateTrianglesPerLoop = 2048, MaxDependencyTriangles = 2048, MaxPinnedVertices = 4096, MaxDependencyTiles = 1024,
                MaxCandidateCellVisits = 100000, MaxTerrainVertexSamples = 100000, MaxCandidateTriangleTests = 1000000,
                MaxSegmentTriangleAttempts = 1000000, MaxExactArithmeticOperations = 100000000,
                MaxEstimatedResidentBytes = 256L * 1024 * 1024, MaxEstimatedScratchBytes = 256L * 1024 * 1024,
            };
            Field = FormationFieldBuilder.BuildPlanned(Terrain, Terrain.TerrainLattice, Selection, limits, plan);
            Terrain.TerrainPins = TerrainPinSet.Build(Terrain, Terrain.TerrainLattice, Digest, Field, new TerrainPinSetLimits
            {
                MaxVertices = 4096, MaxInputPins = 8192, MaxNormalTriangles = 32768, MaxAdditionalSamples = 32768,
                MaxEstimatedResidentBytes = 16 * 1024 * 1024, MaxEstimatedScratchBytes = 16 * 1024 * 1024,
            });
            Assert.True(Terrain.TerrainPins.SupportsCanonicalRadialTrace,
                "actual sealed canonical pins support the selected angular trace domain");
            if (Field.Pieces.Length != 0)
                Assert.True(Terrain.TerrainPins.Vertices.Length != 0,
                    "nonempty sealed collision fixtures exercise real fixed contact vertices");
            Terrain.Formations = Field;
        }
        internal TriangleMesh Collision(TileKey tile, double margin = 0, ZoneFrame? frame = null,
            SelectedGroundCollisionLimits? limits = null, CancellationToken cancellation = default) =>
            SelectedGroundCollision.Build(Terrain, frame ?? ZoneFrame.World, tile, margin, Field,
                Array.Empty<SelectedGroundCollision.Monument>(), limits ?? Limits(), cancellation);
    }
    internal sealed class Terrain(double radius, int level) : ISurface, IGradedTerrain, ITerrainLatticeSurface, ITerrainPinnedSurface, IFormationSurface
    {
        public TerrainLattice TerrainLattice { get; } = new(radius, level);
        public TerrainPinSet? TerrainPins { get; internal set; }
        public FormationField? Formations { get; internal set; }
        internal Func<double>? Sample { get; set; }
        internal int Calls { get; private set; }
        public bool IsFinalized => true;
        public double Radius => radius;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) { Calls++; return Sample?.Invoke() ?? 0; }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new("flat", 15, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
    }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new TestFailure("Expected " + typeof(T).Name + "."); }
}
