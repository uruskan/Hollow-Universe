using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Per-loop temporary canonical geometry. Builds segment-shadow candidates on all six faces, then conservatively
/// removes triangles whose radial cone excludes every complete segment. Outward arithmetic can retain false
/// positives but never turns an uncertain test into exclusion. Only the exact gate can certify a seal.
/// </summary>
internal sealed class FormationFieldTerrain(IGradedTerrain terrain, TerrainLattice lattice, FormationFieldWork work)
{
    private readonly Dictionary<CubeVertexKey, Vector3D> _positions = new();

    internal TerrainSealTriangle[] Candidates(ReadOnlySpan<Vector3D> loop)
    {
        var cells = new HashSet<TileKey>();
        for (int i = 0; i < loop.Length; i++)
        {
            work.Check();
            Vector3D a = loop[i], b = loop[(i + 1) % loop.Length];
            Shadow(new FormationBounds(FormationBounds.Lower(a, b), FormationBounds.Upper(a, b)), cells);
        }
        TileKey[] ordered = SortedTiles(cells);
        var candidates = new List<TerrainSealTriangle>();
        foreach (TileKey cell in ordered)
        for (int half = 0; half < 2; half++)
        {
            work.Check(); TerrainLatticeTriangle primitive = lattice.Triangle(cell, half);
            var triangle = new TerrainSealTriangle(primitive, Position(primitive.A), Position(primitive.B), Position(primitive.C));
            if (!MayTouch(triangle, loop)) continue;
            if (candidates.Count == work.Limits.Seal.MaxTerrainTriangles)
                throw new FormationCapacityException("Relevant seal triangles exceed the per-loop allowance.");
            candidates.Add(triangle);
        }
        return candidates.ToArray();
    }

    private bool MayTouch(TerrainSealTriangle triangle, ReadOnlySpan<Vector3D> loop)
    {
        IntervalVector ab = Cross(triangle.A, triangle.B), bc = Cross(triangle.B, triangle.C), ca = Cross(triangle.C, triangle.A);
        for (int i = 0; i < loop.Length; i++)
        {
            work.CandidateTest(); Vector3D first = loop[i], second = loop[(i + 1) % loop.Length];
            // A linear half-space excludes the whole segment only when both endpoint upper bounds are negative.
            if (Outside(ab, first, second) || Outside(bc, first, second) || Outside(ca, first, second)) continue;
            return true;
        }
        return false;
    }

    private void Shadow(FormationBounds bounds, HashSet<TileKey> cells)
    {
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            work.Check();
            if (CanonicalRadialShadow.Range(bounds, face, lattice.VertexLevel) is not { } range) continue;
            for (int y = range.Y0; y <= range.Y1; y++)
            for (int x = range.X0; x <= range.X1; x++)
            {
                work.Cell(); var cell = new TileKey(face, lattice.VertexLevel, x, y);
                if (cells.Contains(cell)) continue;
                if (cells.Count >= work.Limits.MaxCandidateTrianglesPerLoop / 2)
                    throw new FormationCapacityException("Candidate terrain enumeration exceeds its explicit cell allowance.");
                cells.Add(cell);
            }
        }
    }

    internal (FormationTerrainPin[] Pins, TileKey[] Tiles) Guards(ReadOnlySpan<TerrainLatticeTriangle> witnesses)
    {
        var cells = new HashSet<TileKey>();
        foreach (TerrainLatticeTriangle witness in witnesses) { work.Cell(); cells.Add(witness.Cell); }
        for (int ring = 0; ring < FormationFieldContract.GuardRings; ring++)
        {
            TileKey[] prior = SortedTiles(cells);
            foreach (TileKey cell in prior)
            for (int edge = 0; edge < 4; edge++)
            { work.Cell(); cells.Add(CubeTopology.Neighbor(cell, (CubeEdge)edge)); }
        }
        var pins = new Dictionary<CubeVertexKey, Vector3D>(); var tiles = new HashSet<TileKey>();
        foreach (TileKey cell in SortedTiles(cells))
        {
            work.Check(); TerrainLatticeTriangle first = lattice.Triangle(cell, 0), second = lattice.Triangle(cell, 1);
            Pin(first.A); Pin(first.B); Pin(first.C); Pin(second.C);
        }
        return (SortedPins(pins), SortedTiles(tiles));

        void Pin(CubeVertexKey vertex)
        {
            if (pins.ContainsKey(vertex)) return;
            work.Pin(); pins.Add(vertex, Position(vertex));
            IncidentTiles(vertex, tiles);
        }
    }

    private void IncidentTiles(CubeVertexKey vertex, HashSet<TileKey> tiles)
    {
        int shift = lattice.VertexLevel - vertex.Level, n = 1 << lattice.VertexLevel;
        int x = vertex.X << shift, y = vertex.Y << shift, z = vertex.Z << shift;
        int renderN = 1 << lattice.FinestTileLevel, stride = TerrainLattice.RenderCells;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            if (Dot(CubeSphere.Normal(face)) != n) continue;
            int u = (Dot(CubeSphere.AxisU(face)) + n) / 2, v = (Dot(CubeSphere.AxisV(face)) + n) / 2;
            int firstX = Math.Max(0, u / stride - (u % stride == 0 ? 1 : 0)), lastX = Math.Min(renderN - 1, u / stride);
            int firstY = Math.Max(0, v / stride - (v % stride == 0 ? 1 : 0)), lastY = Math.Min(renderN - 1, v / stride);
            for (int tileY = firstY; tileY <= lastY; tileY++)
            for (int tileX = firstX; tileX <= lastX; tileX++)
            {
                work.Check(); var tile = new TileKey(face, lattice.FinestTileLevel, tileX, tileY);
                if (tiles.Contains(tile)) continue;
                work.Tile(); tiles.Add(tile);
            }
        }
        int Dot(Vector3D axis) => (int)axis.X * x + (int)axis.Y * y + (int)axis.Z * z;
    }

    private Vector3D Position(CubeVertexKey vertex)
    {
        if (_positions.TryGetValue(vertex, out Vector3D point)) return point;
        work.Sample(); Vector3D direction = lattice.Direction(vertex);
        double radial = terrain.Radius + terrain.HeightAt(direction);
        if (!double.IsFinite(radial) || radial <= 0)
            throw new FormationValidationException("Canonical terrain requires a finite positive radial height.");
        point = direction * radial; ValidatePoint(point); _positions.Add(vertex, point); return point;
    }

    internal static void ValidatePoint(Vector3D point)
    {
        FormationInput.Finite(point, nameof(point));
        if (point == Vector3D.Zero || Math.Abs(point.X) > FormationTerrainSeal.MaximumCoordinateMetres ||
            Math.Abs(point.Y) > FormationTerrainSeal.MaximumCoordinateMetres || Math.Abs(point.Z) > FormationTerrainSeal.MaximumCoordinateMetres)
            throw new FormationValidationException("Field coordinates exceed the exact seal's body-space domain.");
    }
    internal static TileKey[] SortedTiles(HashSet<TileKey> tiles)
    { var result = new TileKey[tiles.Count]; tiles.CopyTo(result); Array.Sort(result, static (a, b) => a.Packed.CompareTo(b.Packed)); return result; }
    internal static FormationTerrainPin[] SortedPins(Dictionary<CubeVertexKey, Vector3D> pins)
    {
        var result = new FormationTerrainPin[pins.Count]; int next = 0;
        foreach (var pair in pins) result[next++] = new FormationTerrainPin(pair.Key, pair.Value);
        Array.Sort(result, static (a, b) => a.Vertex.CompareTo(b.Vertex)); return result;
    }
    internal static int ComparePrimitive(TerrainLatticeTriangle a, TerrainLatticeTriangle b)
    { int order = a.Cell.Packed.CompareTo(b.Cell.Packed); return order != 0 ? order : a.Half.CompareTo(b.Half); }

    private readonly record struct Interval(double Low, double High)
    {
        internal static Interval Product(double a, double b) => new(Math.BitDecrement(a * b), Math.BitIncrement(a * b));
        internal Interval Times(double value) => value >= 0
            ? new(Math.BitDecrement(Low * value), Math.BitIncrement(High * value))
            : new(Math.BitDecrement(High * value), Math.BitIncrement(Low * value));
        internal Interval Plus(Interval other) => new(Math.BitDecrement(Low + other.Low), Math.BitIncrement(High + other.High));
        internal Interval Minus(Interval other) => new(Math.BitDecrement(Low - other.High), Math.BitIncrement(High - other.Low));
    }
    private readonly record struct IntervalVector(Interval X, Interval Y, Interval Z)
    { internal double DotUpper(Vector3D point) => X.Times(point.X).Plus(Y.Times(point.Y)).Plus(Z.Times(point.Z)).High; }
    private static IntervalVector Cross(Vector3D a, Vector3D b) => new(
        Interval.Product(a.Y, b.Z).Minus(Interval.Product(a.Z, b.Y)),
        Interval.Product(a.Z, b.X).Minus(Interval.Product(a.X, b.Z)),
        Interval.Product(a.X, b.Y).Minus(Interval.Product(a.Y, b.X)));
    private static bool Outside(IntervalVector plane, Vector3D a, Vector3D b) => plane.DotUpper(a) < 0 && plane.DotUpper(b) < 0;
}
