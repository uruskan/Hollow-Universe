using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// The cube-sphere of a planet surface (GROUND.md §4.6): six faces, each a square of face coordinates
/// (u, v) ∈ [−1, 1]², mapped to the sphere through a tangent warp so tiles stay nearly the same size everywhere
/// (largest to smallest tile area about 1.4, against 5.2 for the plain gnomonic cube). Deterministic: DMath only.
/// </summary>
/// <remarks>
/// Faces: 0 +X, 1 −X, 2 +Y (north pole), 3 −Y, 4 +Z, 5 −Z, in the body frame of <see cref="ISurface"/>. Each face has
/// an outward normal N and axes U, V with U × V = N, so a quad that runs counter-clockwise in (u, v) runs
/// counter-clockwise seen from outside the planet.
/// </remarks>
public static class CubeSphere
{
    public const int FaceCount = 6;

    private static readonly Vector3D[] Normals =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private static readonly Vector3D[] AxesU =
    {
        new(0, 0, -1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(-1, 0, 0),
    };

    private static readonly Vector3D[] AxesV =
    {
        new(0, 1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0),
    };

    public static Vector3D Normal(int face) => Normals[face];
    public static Vector3D AxisU(int face) => AxesU[face];
    public static Vector3D AxisV(int face) => AxesV[face];

    /// <summary>The unit direction of face coordinates (u, v) ∈ [−1, 1]² on a face.</summary>
    public static Vector3D Direction(int face, double u, double v)
    {
        double a = Warp(u), b = Warp(v);
        Vector3D n = Normals[face], au = AxesU[face], av = AxesV[face];
        double x = n.X + a * au.X + b * av.X;
        double y = n.Y + a * au.Y + b * av.Y;
        double z = n.Z + a * au.Z + b * av.Z;
        double inv = 1.0 / Math.Sqrt(x * x + y * y + z * z);
        return new Vector3D(x * inv, y * inv, z * inv);
    }

    /// <summary>The face and face coordinates of a direction (need not be unit length; must not be zero).</summary>
    public static (int Face, double U, double V) FromDirection(Vector3D d)
    {
        double ax = Math.Abs(d.X), ay = Math.Abs(d.Y), az = Math.Abs(d.Z);
        int face;
        if (ax >= ay && ax >= az)
        {
            face = d.X >= 0 ? 0 : 1;
        }
        else if (ay >= az)
        {
            face = d.Y >= 0 ? 2 : 3;
        }
        else
        {
            face = d.Z >= 0 ? 4 : 5;
        }
        double n = Vector3D.Dot(d, Normals[face]);
        double a = Vector3D.Dot(d, AxesU[face]) / n;
        double b = Vector3D.Dot(d, AxesV[face]) / n;
        return (face, Unwarp(a), Unwarp(b));
    }

    /// <summary>Face coordinate to cube coordinate: tan(t·π/4), odd by construction so opposite edges match exactly.</summary>
    public static double Warp(double t)
    {
        double s = Math.Abs(t) * (DMath.Pi / 4);
        double w = DMath.Sin(s) / DMath.Cos(s);
        return t < 0 ? -w : w;
    }

    /// <summary>Cube coordinate to face coordinate: the inverse of <see cref="Warp"/>.</summary>
    public static double Unwarp(double a)
    {
        double t = DMath.Atan(Math.Abs(a)) * (4 / DMath.Pi);
        return a < 0 ? -t : t;
    }
}

/// <summary>
/// A surface tile: a square of one cube face at a quadtree level (ARCHITECTURE.md §7: with the body's ID it names the
/// tile for ever). Level L splits the face into 2^L × 2^L tiles; tile (X, Y) covers u from −1 + 2X / 2^L to
/// −1 + 2(X + 1) / 2^L, and v likewise. Every face coordinate a tile grid uses is a dyadic fraction, so it is exact
/// in double precision, and a parent's vertices are bit-for-bit its children's.
/// </summary>
public readonly struct TileKey : IEquatable<TileKey>
{
    /// <summary>Deepest level a key can name (X and Y have 28 bits).</summary>
    public const int MaxLevel = 28;

    public readonly byte Face;
    public readonly byte Level;
    public readonly int X;
    public readonly int Y;

    public TileKey(int face, int level, int x, int y)
    {
        if ((uint)face >= CubeSphere.FaceCount || (uint)level > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Face 0–5 and level 0–28.");
        }
        int n = 1 << level;
        if ((uint)x >= (uint)n || (uint)y >= (uint)n)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Tile coordinates outside the face.");
        }
        Face = (byte)face;
        Level = (byte)level;
        X = x;
        Y = y;
    }

    /// <summary>3 bits face, 5 bits level, 28 bits X, 28 bits Y.</summary>
    public ulong Packed => (ulong)Face << 61 | (ulong)Level << 56 | (ulong)(uint)X << 28 | (uint)Y;

    /// <summary>A key from <see cref="Packed"/>, or null if the bits don't name a tile (untrusted input, ARCHITECTURE.md §8.7).</summary>
    public static TileKey? FromPacked(ulong packed)
    {
        int face = (int)(packed >> 61), level = (int)((packed >> 56) & 31);
        long x = (long)((packed >> 28) & 0xFFFFFFF), y = (long)(packed & 0xFFFFFFF);
        if (face >= CubeSphere.FaceCount || level > MaxLevel || x >= 1L << level || y >= 1L << level)
        {
            return null;
        }
        return new TileKey(face, level, (int)x, (int)y);
    }

    /// <summary>The tile at a level that holds a direction.</summary>
    public static TileKey At(Vector3D direction, int level)
    {
        (int face, double u, double v) = CubeSphere.FromDirection(direction);
        return At(face, u, v, level);
    }

    /// <summary>The tile at a level that holds face coordinates (<see cref="CubeSphere.FromDirection"/>'s), for callers that need them too.</summary>
    public static TileKey At(int face, double u, double v, int level)
    {
        int n = 1 << level;
        int x = Math.Clamp((int)Math.Floor((u + 1) * 0.5 * n), 0, n - 1);
        int y = Math.Clamp((int)Math.Floor((v + 1) * 0.5 * n), 0, n - 1);
        return new TileKey(face, level, x, y);
    }

    public TileKey Parent => Level == 0 ? this : new TileKey(Face, Level - 1, X >> 1, Y >> 1);

    /// <summary>Child 0–3: (0, 0), (1, 0), (0, 1), (1, 1) in (u, v).</summary>
    public TileKey Child(int i) => new(Face, Level + 1, X * 2 + (i & 1), Y * 2 + (i >> 1));

    /// <summary>Face coordinate of the tile's lower edge in u.</summary>
    public double U0 => -1.0 + 2.0 * X / (1L << Level);

    /// <summary>Face coordinate of the tile's lower edge in v.</summary>
    public double V0 => -1.0 + 2.0 * Y / (1L << Level);

    /// <summary>Width of the tile in face coordinates.</summary>
    public double Span => 2.0 / (1L << Level);

    /// <summary>
    /// Face coordinate of grid line <paramref name="i"/> of a grid with <paramref name="cells"/> cells per edge (a power
    /// of two), exactly: neighbouring tiles and parents share their vertices bit for bit.
    /// </summary>
    public static double GridCoordinate(int tileIndex, int level, int i, int cells) =>
        -1.0 + 2.0 * ((long)tileIndex * cells + i) / ((double)cells * (1L << level));

    /// <summary>Unit direction of the tile's centre.</summary>
    public Vector3D Centre => CubeSphere.Direction(Face, U0 + Span / 2, V0 + Span / 2);

    /// <summary>Approximate edge length of tiles at a level, in metres, on a sphere of this radius.</summary>
    public static double EdgeLength(int level, double radius) => DMath.HalfPi * radius / (1L << level);

    /// <summary>
    /// The level whose tiles' edge (<see cref="EdgeLength"/>) is nearest <paramref name="metres"/> on a sphere of this
    /// radius, nearest by ratio, within <paramref name="most"/> (docs/WHOLE_WORLD.md §3.2: lengths by size, not by level
    /// number). Only + − × ÷, so the server and every client agree.
    /// </summary>
    public static int LevelFor(double radius, double metres, int most = MaxLevel)
    {
        int best = 0;
        double bestRatio = double.MaxValue;
        for (int level = 0; level <= most; level++)
        {
            double edge = EdgeLength(level, radius);
            double ratio = edge >= metres ? edge / metres : metres / edge;
            if (ratio < bestRatio)
            {
                (best, bestRatio) = (level, ratio);
            }
        }
        return best;
    }

    /// <summary>How many tiles a level has on the whole sphere.</summary>
    public static long CountAt(int level) => 6L << (2 * level);

    /// <summary>The tile's place among its level's tiles, 0 … <see cref="CountAt"/> − 1: face, then row, then column.</summary>
    public long Index => ((long)Face << (2 * Level)) + ((long)Y << Level) + X;

    /// <summary>The tile at a place among a level's tiles, or null outside them (untrusted input).</summary>
    public static TileKey? FromIndex(int level, long index)
    {
        if ((uint)level > MaxLevel || index < 0 || index >= CountAt(level))
        {
            return null;
        }
        long perFace = 1L << (2 * level);
        long inFace = index % perFace;
        return new TileKey((int)(index / perFace), level, (int)(inFace & ((1L << level) - 1)), (int)(inFace >> level));
    }

    /// <summary>The tile with these coordinates, or null if they name none (untrusted input: never an exception).</summary>
    public static TileKey? Checked(int face, int level, long x, long y) =>
        (uint)face < CubeSphere.FaceCount && (uint)level <= MaxLevel && x >= 0 && y >= 0 && x < 1L << level && y < 1L << level
            ? new TileKey(face, level, (int)x, (int)y)
            : null;

    /// <summary>
    /// Every tile of a level within about <paramref name="reach"/> metres of a direction on a sphere of radius
    /// <paramref name="radius"/>, found by sampling the tangent plane at that point a third of a tile apart, so face
    /// seams and the cube's corners need nothing special. Adds each tile once, in a fixed order.
    /// </summary>
    public static void Around(Vector3D direction, double reach, double radius, int level, System.Collections.Generic.ICollection<TileKey> into)
    {
        Vector3D up = direction.Normalized;
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-18 ? east.Normalized : new Vector3D(0, 0, -1);
        Vector3D north = Vector3D.Cross(up, east);
        double step = EdgeLength(level, radius) / 3;
        int n = Math.Max(1, (int)Math.Ceiling(reach / step));
        TileKey last = At(up, level);
        into.Add(last);
        for (int j = -n; j <= n; j++)
        {
            for (int i = -n; i <= n; i++)
            {
                Vector3D p = up * radius + east * (i * step) + north * (j * step);
                TileKey key = At(p, level);
                if (key != last && !into.Contains(key))
                {
                    into.Add(key);
                }
                last = key;
            }
        }
    }

    public bool Equals(TileKey other) => Packed == other.Packed;
    public override bool Equals(object? obj) => obj is TileKey k && Equals(k);
    public override int GetHashCode() => Packed.GetHashCode();
    public static bool operator ==(TileKey a, TileKey b) => a.Equals(b);
    public static bool operator !=(TileKey a, TileKey b) => !a.Equals(b);
    public override string ToString() => $"tile {Face}/{Level}/{X}/{Y}";
}
