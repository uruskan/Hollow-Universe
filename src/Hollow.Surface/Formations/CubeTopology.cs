using System;
using System.Globalization;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Face-grid edges. Edge parameters increase in V for U edges, and in U for V edges.</summary>
public enum CubeEdge : byte { UMin, UMax, VMin, VMax }

/// <summary>The adjoining face, its shared edge, and whether the increasing edge parameter reverses.</summary>
public readonly struct CubeEdgeTransition
{
    private CubeEdgeTransition(int face, CubeEdge edge, bool reversed) =>
        (Face, Edge, Reversed, IsValid) = (face, edge, reversed, true);

    public int Face { get; }
    public CubeEdge Edge { get; }
    public bool Reversed { get; }
    /// <summary>The default value is not a transition.</summary>
    public bool IsValid { get; }

    internal static CubeEdgeTransition Create(int face, CubeEdge edge, bool reversed)
    {
        if ((uint)face >= CubeSphere.FaceCount) throw new ArgumentOutOfRangeException(nameof(face));
        if ((uint)edge > (uint)CubeEdge.VMax) throw new ArgumentOutOfRangeException(nameof(edge));
        return new CubeEdgeTransition(face, edge, reversed);
    }
}

/// <summary>
/// Canonical dyadic face-grid vertex, shared across faces and refinement levels. X/Y/Z are integer coordinates
/// on the unwarped cube, divided by 2^Level; they are an identity, not a unit direction on the warped sphere.
/// Common factors of two are removed. Ordering is validity, Level, X, Y, Z, using integer comparisons only.
/// </summary>
public readonly struct CubeVertexKey : IEquatable<CubeVertexKey>, IComparable<CubeVertexKey>
{
    private CubeVertexKey(int x, int y, int z, int level) =>
        (X, Y, Z, Level, IsValid) = (x, y, z, level, true);

    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public int Level { get; }
    /// <summary>The default value is invalid and cannot be used as an edge endpoint.</summary>
    public bool IsValid { get; }

    internal static CubeVertexKey Create(int x, int y, int z, int level)
    {
        if ((uint)level > TileKey.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        int n = 1 << level;
        if (x < -n || x > n || y < -n || y > n || z < -n || z > n ||
            (Math.Abs(x) != n && Math.Abs(y) != n && Math.Abs(z) != n))
            throw new ArgumentException("A vertex must lie on the integer cube surface.");
        while (level > 0 && ((x | y | z) & 1) == 0)
        {
            x /= 2; y /= 2; z /= 2; level--;
        }
        return new CubeVertexKey(x, y, z, level);
    }

    public int CompareTo(CubeVertexKey other)
    {
        int order = IsValid.CompareTo(other.IsValid);
        if (order == 0) order = Level.CompareTo(other.Level);
        if (order == 0) order = X.CompareTo(other.X);
        if (order == 0) order = Y.CompareTo(other.Y);
        return order == 0 ? Z.CompareTo(other.Z) : order;
    }

    public bool Equals(CubeVertexKey other) => IsValid == other.IsValid && Level == other.Level &&
        X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is CubeVertexKey other && Equals(other);
    public override int GetHashCode() => unchecked((((Level * 397) ^ X) * 397 ^ Y) * 397 ^ Z);
    public static bool operator ==(CubeVertexKey left, CubeVertexKey right) => left.Equals(right);
    public static bool operator !=(CubeVertexKey left, CubeVertexKey right) => !left.Equals(right);
    public override string ToString() => IsValid
        ? string.Create(CultureInfo.InvariantCulture, $"cube {Level}:{X},{Y},{Z}") : "invalid cube vertex";
}

/// <summary>
/// Canonical undirected segment between two distinct valid vertices. Endpoint ordering is independent of face
/// and caller order. This key alone does not assert that arbitrary endpoints form one tile's edge.
/// </summary>
public readonly struct CubeEdgeKey : IEquatable<CubeEdgeKey>, IComparable<CubeEdgeKey>
{
    private CubeEdgeKey(CubeVertexKey first, CubeVertexKey second) => (First, Second) = (first, second);
    public CubeVertexKey First { get; }
    public CubeVertexKey Second { get; }
    public bool IsValid => First.IsValid && Second.IsValid;

    internal static CubeEdgeKey Create(CubeVertexKey first, CubeVertexKey second)
    {
        if (!first.IsValid || !second.IsValid) throw new ArgumentException("Edge endpoints must be valid cube vertices.");
        int order = first.CompareTo(second);
        if (order == 0) throw new ArgumentException("An edge needs two distinct endpoints.");
        return order < 0 ? new CubeEdgeKey(first, second) : new CubeEdgeKey(second, first);
    }

    public int CompareTo(CubeEdgeKey other)
    {
        int order = First.CompareTo(other.First);
        return order == 0 ? Second.CompareTo(other.Second) : order;
    }

    public bool Equals(CubeEdgeKey other) => First == other.First && Second == other.Second;
    public override bool Equals(object? obj) => obj is CubeEdgeKey other && Equals(other);
    public override int GetHashCode() => unchecked(First.GetHashCode() * 397 ^ Second.GetHashCode());
    public static bool operator ==(CubeEdgeKey left, CubeEdgeKey right) => left.Equals(right);
    public static bool operator !=(CubeEdgeKey left, CubeEdgeKey right) => !left.Equals(right);
}

/// <summary>
/// Exact combinatorial topology for the existing cube-face grids. Transitions are derived from CubeSphere's
/// signed-axis bases, never from floating-point directions, probes, warping or dominant-axis tie breaking.
/// This utility does not activate formation generation or change the legacy terrain/streaming paths.
/// </summary>
public static class CubeTopology
{
    private static readonly FaceBasis[] Bases = ReadBases();
    private static readonly CubeEdgeTransition[] Transitions = BuildTransitions();

    public static CubeEdgeTransition Transition(int face, CubeEdge edge)
    {
        ValidateFaceLevel(face, 0);
        ValidateEdge(edge);
        return Transitions[face * 4 + (int)edge];
    }

    /// <summary>One same-level tile sharing the requested edge, including exact cross-face transitions.</summary>
    public static TileKey Neighbor(TileKey tile, CubeEdge edge)
    {
        int n = ValidateTile(tile);
        ValidateEdge(edge);
        switch (edge)
        {
            case CubeEdge.UMin when tile.X > 0: return new TileKey(tile.Face, tile.Level, tile.X - 1, tile.Y);
            case CubeEdge.UMax when tile.X < n - 1: return new TileKey(tile.Face, tile.Level, tile.X + 1, tile.Y);
            case CubeEdge.VMin when tile.Y > 0: return new TileKey(tile.Face, tile.Level, tile.X, tile.Y - 1);
            case CubeEdge.VMax when tile.Y < n - 1: return new TileKey(tile.Face, tile.Level, tile.X, tile.Y + 1);
        }
        CubeEdgeTransition transition = Transitions[tile.Face * 4 + (int)edge];
        int along = edge is CubeEdge.UMin or CubeEdge.UMax ? tile.Y : tile.X;
        if (transition.Reversed) along = n - 1 - along;
        return transition.Edge switch
        {
            CubeEdge.UMin => new TileKey(transition.Face, tile.Level, 0, along),
            CubeEdge.UMax => new TileKey(transition.Face, tile.Level, n - 1, along),
            CubeEdge.VMin => new TileKey(transition.Face, tile.Level, along, 0),
            CubeEdge.VMax => new TileKey(transition.Face, tile.Level, along, n - 1),
            _ => throw new InvalidOperationException("Invalid cube transition table."),
        };
    }

    /// <summary>Grid vertex coordinates are inclusive 0..2^level; unlike tile coordinates, the upper boundary is valid.</summary>
    public static CubeVertexKey VertexKey(int face, int level, int x, int y)
    {
        int n = ValidateFaceLevel(face, level);
        if ((uint)x > (uint)n) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y > (uint)n) throw new ArgumentOutOfRangeException(nameof(y));
        FaceBasis basis = Bases[face];
        int u = 2 * x - n, v = 2 * y - n;
        return CubeVertexKey.Create(n * basis.N.X + u * basis.U.X + v * basis.V.X,
            n * basis.N.Y + u * basis.U.Y + v * basis.V.Y,
            n * basis.N.Z + u * basis.U.Z + v * basis.V.Z, level);
    }

    public static CubeEdgeKey EdgeKey(CubeVertexKey first, CubeVertexKey second) => CubeEdgeKey.Create(first, second);

    /// <summary>The canonical endpoints of one tile edge; coarse edges remain distinct from their child segments.</summary>
    public static CubeEdgeKey EdgeKey(TileKey tile, CubeEdge edge)
    {
        ValidateTile(tile);
        ValidateEdge(edge);
        int x = tile.X, y = tile.Y;
        return edge switch
        {
            CubeEdge.UMin => EdgeKey(VertexKey(tile.Face, tile.Level, x, y), VertexKey(tile.Face, tile.Level, x, y + 1)),
            CubeEdge.UMax => EdgeKey(VertexKey(tile.Face, tile.Level, x + 1, y), VertexKey(tile.Face, tile.Level, x + 1, y + 1)),
            CubeEdge.VMin => EdgeKey(VertexKey(tile.Face, tile.Level, x, y), VertexKey(tile.Face, tile.Level, x + 1, y)),
            CubeEdge.VMax => EdgeKey(VertexKey(tile.Face, tile.Level, x, y + 1), VertexKey(tile.Face, tile.Level, x + 1, y + 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
    }

    private static int ValidateFaceLevel(int face, int level)
    {
        if ((uint)face >= CubeSphere.FaceCount) throw new ArgumentOutOfRangeException(nameof(face));
        if ((uint)level > TileKey.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        return 1 << level;
    }

    private static int ValidateTile(TileKey tile)
    {
        int n = ValidateFaceLevel(tile.Face, tile.Level);
        if ((uint)tile.X >= (uint)n || (uint)tile.Y >= (uint)n) throw new ArgumentException("Invalid cube tile.", nameof(tile));
        return n;
    }

    private static void ValidateEdge(CubeEdge edge)
    {
        if ((uint)edge > (uint)CubeEdge.VMax) throw new ArgumentOutOfRangeException(nameof(edge));
    }

    private static FaceBasis[] ReadBases()
    {
        var result = new FaceBasis[CubeSphere.FaceCount];
        for (int face = 0; face < result.Length; face++)
        {
            var basis = new FaceBasis(Axis.Read(CubeSphere.Normal(face)), Axis.Read(CubeSphere.AxisU(face)),
                Axis.Read(CubeSphere.AxisV(face)));
            if (basis.N.Dot(basis.U) != 0 || basis.N.Dot(basis.V) != 0 || basis.U.Dot(basis.V) != 0 ||
                basis.U.Cross(basis.V) != basis.N)
                throw new InvalidOperationException("CubeSphere bases must be right-handed signed coordinate axes.");
            for (int previous = 0; previous < face; previous++)
                if (result[previous].N == basis.N) throw new InvalidOperationException("CubeSphere face normals must be distinct.");
            result[face] = basis;
        }
        return result;
    }

    private static CubeEdgeTransition[] BuildTransitions()
    {
        var result = new CubeEdgeTransition[CubeSphere.FaceCount * 4];
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
        {
            CubeEdge edge = (CubeEdge)edgeIndex;
            FaceBasis source = Bases[face];
            Axis outward = edge is CubeEdge.UMin or CubeEdge.UMax ? source.U : source.V;
            if (edge is CubeEdge.UMin or CubeEdge.VMin) outward = outward.Negated;
            int destinationFace = -1;
            for (int candidate = 0; candidate < Bases.Length; candidate++)
                if (Bases[candidate].N == outward) { destinationFace = candidate; break; }
            if (destinationFace < 0) throw new InvalidOperationException("CubeSphere is missing a neighboring face.");
            FaceBasis destination = Bases[destinationFace];
            int fixedU = source.N.Dot(destination.U), fixedV = source.N.Dot(destination.V);
            CubeEdge destinationEdge = fixedU != 0 ? (fixedU < 0 ? CubeEdge.UMin : CubeEdge.UMax)
                : fixedV < 0 ? CubeEdge.VMin : CubeEdge.VMax;
            Axis sourceTangent = edge is CubeEdge.UMin or CubeEdge.UMax ? source.V : source.U;
            Axis destinationTangent = destinationEdge is CubeEdge.UMin or CubeEdge.UMax ? destination.V : destination.U;
            int orientation = sourceTangent.Dot(destinationTangent);
            if (Math.Abs(orientation) != 1) throw new InvalidOperationException("CubeSphere edge axes do not align.");
            result[face * 4 + edgeIndex] = CubeEdgeTransition.Create(destinationFace, destinationEdge, orientation < 0);
        }
        return result;
    }

    private readonly record struct FaceBasis(Axis N, Axis U, Axis V);

    private readonly record struct Axis(int X, int Y, int Z)
    {
        public Axis Negated => new(-X, -Y, -Z);
        public int Dot(Axis other) => X * other.X + Y * other.Y + Z * other.Z;
        public Axis Cross(Axis other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
        public static Axis Read(Vector3D value)
        {
            static int Component(double number) => number is -1 or 0 or 1 ? (int)number
                : throw new InvalidOperationException("CubeSphere basis components must be exactly -1, 0 or 1.");
            var axis = new Axis(Component(value.X), Component(value.Y), Component(value.Z));
            if (axis.Dot(axis) != 1) throw new InvalidOperationException("CubeSphere basis must contain unit coordinate axes.");
            return axis;
        }
    }
}
