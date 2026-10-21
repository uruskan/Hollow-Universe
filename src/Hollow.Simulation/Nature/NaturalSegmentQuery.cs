using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>
/// A segment broadphase over the six cube-face quadtrees. Every leaf is visited at most once; no generation happens
/// until intersected leaves have been sorted in the same order as radius queries. Work depends on intersected world
/// cells, not the length of the ray outside the planet. The caller supplies a root-relative bound for its hit shapes.
/// </summary>
public static class NaturalSegmentQuery
{
    public static bool Valid(Vector3 from, Vector3 to, float reach) =>
        Finite(from) && Finite(to) && float.IsFinite(reach) && reach >= 0;

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    /// <summary>Fallback for providers without surface bounds; refuses an unrepresentable float query.</summary>
    public static bool EnclosingSphere(Vector3 from, Vector3 to, float reach, out Vector3 centre, out float radius)
    {
        centre = default;
        radius = 0;
        if (!Valid(from, to, reach)) return false;
        double x = ((double)to.X - from.X) * 0.5, y = ((double)to.Y - from.Y) * 0.5,
            z = ((double)to.Z - from.Z) * 0.5;
        centre = new Vector3((float)(from.X + x), (float)(from.Y + y), (float)(from.Z + z));
        // Include the midpoint's conversion to float; round the radius outward as well.
        double ex = from.X + x - centre.X, ey = from.Y + y - centre.Y, ez = from.Z + z - centre.Z;
        double bound = Math.Sqrt(x * x + y * y + z * z) + reach + Math.Sqrt(ex * ex + ey * ey + ez * ez);
        radius = MathF.BitIncrement((float)bound);
        return float.IsFinite(radius);
    }

    public static void Query(INaturalObjects source, ISurface surface, in ZoneFrame frame,
        Vector3 from, Vector3 to, float rootReach, List<NaturalObject> into)
    {
        if (!Valid(from, to, rootReach)) return;
        if (source.Level < 0 || source.Level > 28)
            throw new InvalidOperationException("Natural segment queries require a valid cube-sphere tile level.");
        (double low, double high) = surface.HeightBounds;
        double r0 = surface.Radius + low, r1 = surface.Radius + high;
        if (!double.IsFinite(r0) || !double.IsFinite(r1) || r0 > r1)
            throw new InvalidOperationException("Natural segment queries require finite ordered surface height bounds.");
        double outer = Math.Max(Math.Abs(r0), Math.Abs(r1));
        // Roots are stored as floats in the zone frame. Include their round trip through that frame in the bound.
        double padding = rootReach + 2e-6 * (outer + frame.Origin.Length + 1);
        Vector3D a = frame.ToBody(from), b = frame.ToBody(to);
        var tiles = new List<TileKey>();
        if (!Intersects(a, b, new Vector3D(-outer - padding, -outer - padding, -outer - padding),
                new Vector3D(outer + padding, outer + padding, outer + padding))) return;
        for (int face = 0; face < CubeSphere.FaceCount; face++) Collect(new TileKey(face, 0, 0, 0));
        tiles.Sort((left, right) => left.Packed.CompareTo(right.Packed));
        foreach (TileKey tile in tiles)
            foreach (NaturalObject item in source.InTile(tile)) into.Add(item);

        void Collect(TileKey tile)
        {
            Bounds(tile, r0, r1, padding, out Vector3D min, out Vector3D max);
            if (!Intersects(a, b, min, max)) return;
            if (tile.Level == source.Level)
            {
                tiles.Add(tile);
                return;
            }
            for (int child = 0; child < 4; child++) Collect(tile.Child(child));
        }
    }

    // CubeSphere.Direction = (N + Warp(u) U + Warp(v) V) / sqrt(1 + Warp(u)^2 + Warp(v)^2).
    // Monotone Warp endpoints and interval arithmetic enclose the entire patch, including edge extrema that are
    // not necessarily at corners. Multiplication by the full radial interval includes all declared terrain relief.
    private static void Bounds(TileKey tile, double radiusLow, double radiusHigh, double padding,
        out Vector3D min, out Vector3D max)
    {
        double u0 = CubeSphere.Warp(tile.U0), u1 = CubeSphere.Warp(tile.U0 + tile.Span);
        double v0 = CubeSphere.Warp(tile.V0), v1 = CubeSphere.Warp(tile.V0 + tile.Span);
        double denominatorLow = Math.Sqrt(1 + NearestSquare(u0, u1) + NearestSquare(v0, v1));
        double denominatorHigh = Math.Sqrt(1 + Math.Max(u0 * u0, u1 * u1) + Math.Max(v0 * v0, v1 * v1));
        Vector3D n = CubeSphere.Normal(tile.Face), u = CubeSphere.AxisU(tile.Face), v = CubeSphere.AxisV(tile.Face);
        (double x0, double x1) = Axis(n.X, u.X, v.X);
        (double y0, double y1) = Axis(n.Y, u.Y, v.Y);
        (double z0, double z1) = Axis(n.Z, u.Z, v.Z);
        min = new Vector3D(x0 - padding, y0 - padding, z0 - padding);
        max = new Vector3D(x1 + padding, y1 + padding, z1 + padding);

        (double Low, double High) Axis(double normal, double alongU, double alongV)
        {
            double c0 = normal + Math.Min(u0 * alongU, u1 * alongU) + Math.Min(v0 * alongV, v1 * alongV);
            double c1 = normal + Math.Max(u0 * alongU, u1 * alongU) + Math.Max(v0 * alongV, v1 * alongV);
            double low = Math.Min(c0 / denominatorLow, c0 / denominatorHigh);
            double high = Math.Max(c1 / denominatorLow, c1 / denominatorHigh);
            return (Math.Min(Math.Min(low * radiusLow, low * radiusHigh), Math.Min(high * radiusLow, high * radiusHigh)),
                Math.Max(Math.Max(low * radiusLow, low * radiusHigh), Math.Max(high * radiusLow, high * radiusHigh)));
        }
    }

    private static double NearestSquare(double low, double high) => low <= 0 && high >= 0 ? 0 : Math.Min(low * low, high * high);

    private static bool Intersects(Vector3D a, Vector3D b, Vector3D min, Vector3D max)
    {
        double enter = 0, leave = 1;
        return Axis(a.X, b.X, min.X, max.X) && Axis(a.Y, b.Y, min.Y, max.Y) && Axis(a.Z, b.Z, min.Z, max.Z);

        bool Axis(double start, double end, double low, double high)
        {
            double direction = end - start;
            if (direction == 0) return start >= low && start <= high;
            double t0 = (low - start) / direction, t1 = (high - start) / direction;
            enter = Math.Max(enter, Math.Min(t0, t1));
            leave = Math.Min(leave, Math.Max(t0, t1));
            return enter <= leave;
        }
    }
}
