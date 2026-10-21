using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class TerrainPinSetTests
{
    private static readonly string TerrainIdentity = new('a', 64);
    private static readonly string GenerationDigest = new('b', 64);

    [Test]
    public static void IncidentCellsIncludeAllFaceAndCornerCopiesWithoutHalfSteps()
    {
        var lattice = new TerrainLattice(1200, 1);
        Span<TileKey> cells = stackalloc TileKey[4];
        foreach (int face in Enumerable.Range(0, 6))
        foreach (int x in new[] { 0, 1, 31, 32, 33, 63, 64 })
        foreach (int y in new[] { 0, 1, 31, 32, 33, 63, 64 })
        {
            CubeVertexKey key = lattice.VertexKey(face, x, y);
            int count = lattice.IncidentCells(key, cells);
            bool corner = (x == 0 || x == 64) && (y == 0 || y == 64);
            Assert.Equal(corner ? 3 : 4, count, "All incident face cells are returned, including cube corners.");
            var distinctFaces = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                TileKey cell = cells[i];
                distinctFaces.Add(cell.Face);
                Assert.Equal(lattice.VertexLevel, (int)cell.Level, "Incident cells belong to the canonical finest topology.");
                if (i > 0) Assert.True(cells[i - 1].Packed < cell.Packed, "Incident cells are unique and canonically ordered.");
                TerrainLatticeTriangle a = lattice.Triangle(cell, 0), b = lattice.Triangle(cell, 1);
                Assert.True(a.A == key || a.B == key || a.C == key || b.C == key, "Each returned cell actually contains the vertex.");
            }
            int expectedFaces = corner ? 3 : x == 0 || x == 64 || y == 0 || y == 64 ? 2 : 1;
            Assert.Equal(expectedFaces, distinctFaces.Count, "Shared face and corner copies are not assigned to only one face.");
        }
        Throws<ArgumentException>(() => lattice.IncidentCells(CubeTopology.VertexKey(0, 7, 1, 1), new TileKey[4]));
        Throws<ArgumentException>(() => lattice.IncidentCells(lattice.VertexKey(0, 0, 0), new TileKey[3]));
        Assert.True(!lattice.TryGridVertex(new TileKey(0, 1, 0, 0), 32, -1, 0, out _),
            "An extrapolated face margin never becomes a canonical pin identity.");
        Assert.True(lattice.TryGridVertex(new TileKey(0, 1, 0, 0), 32, 32, 0, out CubeVertexKey edge) &&
            edge == lattice.VertexKey(0, 32, 0), "Inclusive tile edges retain their canonical identity.");
    }

    [Test]
    public static void FrozenPinsMergeDeterministicallyAndRefuseConflictingOwnership()
    {
        var source = new Terrain(1200);
        FormationTerrainPin pin = Pin(source, 0, 13, 17);
        TerrainPinSet a = Build(source, pin, pin);
        Assert.Equal(1, a.Vertices.Length, "Duplicate identical pins share one stored entry.");
        Assert.Equal(2, a.Diagnostics.InputPins, "Repeated input still consumes the aggregate input budget.");
        FormationTerrainPin other = Pin(source, 0, 17, 13);
        TerrainPinSet first = Build(source, pin, other), reverse = Build(source, other, pin);
        Assert.True(first.Vertices.SequenceEqual(reverse.Vertices), "Input order cannot change positions, normals or key order.");
        Assert.Equal(first.Diagnostics, reverse.Diagnostics, "Canonical normal work is deterministic after merging.");
        Throws<FormationValidationException>(() => Build(source, pin, pin with { Position = pin.Position * 1.001 }));
        Throws<FormationValidationException>(() => Build(source, pin with { Vertex = CubeTopology.VertexKey(0, 7, 1, 1) }));
        FormationField field = Field(source.TerrainLattice, new[] { pin });
        Throws<FormationValidationException>(() => TerrainPinSet.Build(source, source.TerrainLattice, new string('c', 64), field, Limits()));
        Throws<FormationValidationException>(() => TerrainPinSet.Build(new Terrain(1000), source.TerrainLattice, TerrainIdentity, field, Limits()));
        source.Formations = field; source.TerrainPins = a;
        Assert.True(ReferenceEquals(a, TerrainPinSet.Of(source)), "A correctly owned optional capability resolves without copying.");
        source.Formations = Field(source.TerrainLattice, new[] { pin }, new string('d', 64));
        Throws<FormationValidationException>(() => TileMesh.Build(source, new TileKey(0, 1, 0, 0)));
    }

    [Test]
    public static void PinnedVerticesKeepBothFineAndMorphGeometryAndNormals()
    {
        var source = new Terrain(1200);
        FormationTerrainPin pin = Pin(source, 0, 13, 17);
        Install(source, pin);
        var tile = new TileKey(0, 1, 0, 0);
        TileGrid grid = TileMesh.Build(source, tile);
        int at = 17 * 33 + 13;
        TerrainPinnedVertex fixedVertex = source.TerrainPins!.Vertices[0];
        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(grid.Positions[3 * at + c], grid.MorphPositions[3 * at + c], "Pinned position ignores camera morph.");
            Assert.Equal(grid.Normals[3 * at + c], grid.MorphNormals[3 * at + c], "Pinned normal ignores camera morph.");
        }
        Assert.Equal((float)fixedVertex.Normal.X, grid.Normals[3 * at], "Rendered normal uses the frozen canonical stencil.");
        Assert.True((grid.Vertex(at) - pin.Position).Length < 0.0001, "Stored tile-relative position represents the frozen pin.");
        Assert.Equal(pin.Position, TileMesh.Positions(source, tile, 32)[at], "The shared position consumer does not resample a pin.");
        int unpinned = 7 * 33 + 5, before = 6 * 33 + 4, after = 8 * 33 + 6;
        for (int c = 0; c < 3; c++)
            Assert.Equal((grid.Positions[3 * before + c] + grid.Positions[3 * after + c]) * 0.5f,
                grid.MorphPositions[3 * unpinned + c], "Unpinned vertices retain the existing parent-diagonal morph.");
        Assert.True(source.TerrainPins.Diagnostics.AdditionalSamples > 0, "The isolated pin exercises bounded normal-only source samples.");
        Assert.Equal(1, source.TerrainPins.Vertices.Length, "Normal-only stencil samples never expand the pinned guard.");
    }

    [Test]
    public static void CrossFaceAndCornerCopiesUseOneFrozenNormalAndPosition()
    {
        var source = new Terrain(1200);
        var pins = new List<FormationTerrainPin>();
        for (int face = 0; face < 6; face++)
        foreach (int x in new[] { 0, 32, 64 })
        foreach (int y in new[] { 0, 32, 64 })
            if (x != 32 || y != 32) pins.Add(Pin(source, face, x, y));
        Install(source, pins.ToArray());
        var copies = new Dictionary<CubeVertexKey, int>();
        for (int face = 0; face < 6; face++)
        for (int ty = 0; ty < 2; ty++)
        for (int tx = 0; tx < 2; tx++)
        {
            var tile = new TileKey(face, 1, tx, ty);
            TileGrid grid = TileMesh.Build(source, tile);
            foreach (int j in new[] { 0, 32 })
            foreach (int i in new[] { 0, 32 })
            {
                CubeVertexKey key = source.TerrainLattice.VertexKey(face, tx * 32 + i, ty * 32 + j);
                if (!source.TerrainPins!.TryGet(key, out TerrainPinnedVertex pin)) continue;
                int at = j * 33 + i;
                copies[key] = copies.GetValueOrDefault(key) + 1;
                Assert.Equal((float)pin.Normal.X, grid.Normals[3 * at], "Every copy has the same canonical normal X.");
                Assert.Equal((float)pin.Normal.Y, grid.Normals[3 * at + 1], "Every copy has the same canonical normal Y.");
                Assert.Equal((float)pin.Normal.Z, grid.Normals[3 * at + 2], "Every copy has the same canonical normal Z.");
                Assert.True((grid.Vertex(at) - pin.Position).Length < 0.0001, "All face/corner copies represent the same frozen body point.");
            }
        }
        Assert.True(copies.Values.All(count => count >= 3), "The fixture checks shared tile/cube-face copies rather than isolated vertices.");
    }

    [Test]
    public static void PinFloatConversionStaysBelowOneMillimetreAtApprovedPlanetSizes()
    {
        foreach (double radius in new[] { 2.0, 30.0, 1200.0, 1354.0 })
        {
            var source = new Terrain(radius);
            FormationTerrainPin pin = Pin(source, 5, 31, 29);
            Install(source, pin);
            var tile = new TileKey(5, 1, 0, 0);
            TileGrid render = TileMesh.Build(source, tile);
            TriangleMesh physics = SurfaceGrid.Collision(source, ZoneFrame.World, tile, 0, 7);
            int at = 29 * 33 + 31;
            var physical = new Vector3D(physics.Positions[3 * at], physics.Positions[3 * at + 1], physics.Positions[3 * at + 2]);
            Assert.True((physical - pin.Position).Length < 0.001, "World-frame collision float error stays below one millimetre.");
            Assert.True((render.Vertex(at) - pin.Position).Length < 0.001, "Tile-relative render float error stays below one millimetre.");
            Assert.True((render.Vertex(at) - physical).Length < 0.001, "The two consumers' CPU float conversions agree within one millimetre.");
        }
        // This is a CPU precision bound for these fixtures, not a substitute for native GPU/reanchor captures.
    }

    [Test]
    public static void PinLimitsCancellationAndBadNormalsPublishNoPartialSet()
    {
        var source = new Terrain(1200);
        FormationTerrainPin pin = Pin(source, 0, 13, 17);
        FormationField field = Field(source.TerrainLattice, new[] { pin });
        TerrainPinSetLimits normal = Limits();
        foreach (TerrainPinSetLimits limit in new[]
        {
            normal with { MaxVertices = 0 }, normal with { MaxInputPins = 0 }, normal with { MaxNormalTriangles = 0 },
            normal with { MaxAdditionalSamples = 0 }, normal with { MaxEstimatedResidentBytes = 0 },
            normal with { MaxEstimatedScratchBytes = 0 },
        })
            Throws<FormationCapacityException>(() => TerrainPinSet.Build(source, source.TerrainLattice, TerrainIdentity, field, limit));
        Throws<FormationValidationException>(() => TerrainPinSet.Build(source, source.TerrainLattice, TerrainIdentity, field,
            normal with { MaxVertices = TerrainPinSet.MaximumVertices + 1 }));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Throws<OperationCanceledException>(() => TerrainPinSet.Build(source, source.TerrainLattice, TerrainIdentity, field, normal, stop.Token));
        using var during = new CancellationTokenSource();
        source.SampleAction = during.Cancel;
        Throws<OperationCanceledException>(() => TerrainPinSet.Build(source, source.TerrainLattice, TerrainIdentity, field, normal, during.Token));
        source.SampleAction = null; source.BadHeight = true;
        Throws<FormationValidationException>(() => TerrainPinSet.Build(source, source.TerrainLattice, TerrainIdentity, field, normal));
    }

    [Test]
    public static void NullPinCapabilityPreservesCanonicalAndLegacyOutput()
    {
        var source = new Terrain(1200);
        ISurface baseline = new Unpinned(source);
        var tile = new TileKey(0, 1, 0, 0);
        TileGrid a = TileMesh.Build(baseline, tile), b = TileMesh.Build(source, tile);
        Assert.True(a.Positions.AsSpan().SequenceEqual(b.Positions), "Null pin capability preserves canonical positions exactly.");
        Assert.True(a.Normals.AsSpan().SequenceEqual(b.Normals), "Null pin capability preserves canonical normals exactly.");
        Assert.True(a.MorphPositions.AsSpan().SequenceEqual(b.MorphPositions), "Null pin capability preserves canonical morph positions exactly.");
        Assert.True(a.MorphNormals.AsSpan().SequenceEqual(b.MorphNormals), "Null pin capability preserves canonical morph normals exactly.");
        Assert.True(SurfaceGrid.Collision(baseline, ZoneFrame.World, tile, 2, 1).Positions.AsSpan().SequenceEqual(
            SurfaceGrid.Collision(source, ZoneFrame.World, tile, 2, 1).Positions), "Null pins preserve collision and margin samples exactly.");
        // TerrainLatticeTests.NullOptInPreservesAllLegacyGridOutputs separately covers absence/null lattice parity.
    }

    private static TerrainPinSetLimits Limits() => new()
    {
        MaxVertices = 128, MaxInputPins = 512, MaxNormalTriangles = 2048, MaxAdditionalSamples = 2048,
        MaxEstimatedResidentBytes = 32768, MaxEstimatedScratchBytes = 65536,
    };
    private static FormationTerrainPin Pin(Terrain source, int face, int x, int y)
    {
        CubeVertexKey key = source.TerrainLattice.VertexKey(face, x, y);
        Vector3D direction = source.TerrainLattice.Direction(key);
        return new FormationTerrainPin(key, direction * (source.Radius + source.HeightAt(direction)));
    }
    private static TerrainPinSet Build(Terrain source, params FormationTerrainPin[] pins) =>
        TerrainPinSet.Build(source, source.TerrainLattice, TerrainIdentity, Field(source.TerrainLattice, pins), Limits());
    private static void Install(Terrain source, params FormationTerrainPin[] pins)
    {
        source.Formations = Field(source.TerrainLattice, pins);
        source.TerrainPins = TerrainPinSet.Build(source, source.TerrainLattice, TerrainIdentity, source.Formations, Limits());
    }
    // Internal synthetic field fixtures isolate pin consumers; this does not pretend to qualify physical assets.
    private static FormationField Field(TerrainLattice lattice, FormationTerrainPin[] pins, string? digest = null)
    {
        var cohort = new FormationCohort(new FormationId(1, 0), Array.Empty<FormationPiece>(), Array.Empty<FormationContactSealReceipt>(),
            Array.Empty<TerrainLatticeTriangle>(), pins, Array.Empty<TileKey>());
        var contract = new FormationFieldContract(TerrainIdentity, lattice.Radius, lattice.FinestTileLevel,
            FormationFieldAdmissionPolicy.IndependentPieces);
        return new FormationField(contract, digest ?? GenerationDigest, Array.Empty<FormationPiece>(), new[] { cohort },
            Array.Empty<FormationFieldRejection>(), new FormationSupportIndex(Array.Empty<FormationPiece>()), default, default);
    }
    private sealed class Terrain(double radius) : ISurface, IGradedTerrain, ITerrainLatticeSurface, IFormationSurface, ITerrainPinnedSurface
    {
        public double Radius { get; } = radius;
        public double SeaRadius => 0;
        public bool IsFinalized => true;
        public TerrainLattice TerrainLattice { get; } = new(radius, 1);
        public FormationField? Formations { get; set; }
        public TerrainPinSet? TerrainPins { get; set; }
        public Action? SampleAction { get; set; }
        public bool BadHeight { get; set; }
        public double HeightAt(Vector3D d)
        { SampleAction?.Invoke(); return BadHeight ? double.NaN : 0.02 * (2 * d.X - 3 * d.Y + d.Z * d.Z); }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new("fixture", 10, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
    }
    private sealed class Unpinned(Terrain source) : ISurface, ITerrainLatticeSurface
    {
        public double Radius => source.Radius;
        public double SeaRadius => source.SeaRadius;
        public double HeightAt(Vector3D direction) => source.HeightAt(direction);
        public TerrainLattice TerrainLattice => source.TerrainLattice;
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
