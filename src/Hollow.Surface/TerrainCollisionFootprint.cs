using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Inclusive canonical cell rectangle on one cube face. A footprint contains at most six.</summary>
public readonly record struct TerrainFaceCellRange(int Face, int X0, int X1, int Y0, int Y1)
{
    internal bool Intersects(TerrainFaceCellRange other) => Face == other.Face && X0 <= other.X1 && X1 >= other.X0 &&
        Y0 <= other.Y1 && Y1 >= other.Y0;
}

/// <summary>
/// Conservative radial footprint of the exact cells used by a canonical collision build. Per-face rectangles can
/// contain extra cells. Physical bounds are projected through all incident faces, including edges and corners;
/// there is no height cutoff, sampled surface AABB or guessed module extent. Retains no surface or callbacks.
/// </summary>
public sealed class TerrainCollisionFootprint
{
    private readonly TerrainFaceCellRange[] _ranges;
    private TerrainCollisionFootprint(TerrainLattice lattice, TerrainFaceCellRange[] ranges)
    { Radius = lattice.Radius; VertexLevel = lattice.VertexLevel; _ranges = ranges; }
    public double Radius { get; }
    public int VertexLevel { get; }
    public ReadOnlySpan<TerrainFaceCellRange> Ranges => _ranges;

    public bool MayIntersect(FormationBounds bounds)
    {
        foreach (TerrainFaceCellRange range in _ranges)
            if (CanonicalRadialShadow.Range(bounds, range.Face, VertexLevel) is { } shadow && range.Intersects(shadow))
                return true;
        return false;
    }

    internal static TerrainCollisionFootprint Core(TerrainLattice lattice, TileKey tile, int cells) =>
        new(lattice, new[] { new TerrainFaceCellRange(tile.Face, tile.X * cells, (tile.X + 1) * cells - 1,
            tile.Y * cells, (tile.Y + 1) * cells - 1) });

    internal static TerrainCollisionFootprint Of(TerrainLattice lattice, List<TileKey> cells, CancellationToken cancellation)
    {
        Span<TerrainFaceCellRange> ranges = stackalloc TerrainFaceCellRange[CubeSphere.FaceCount];
        int count = 0;
        // SurfaceGrid supplies canonical cells in packed order, hence face order.
        foreach (TileKey cell in cells)
        {
            cancellation.ThrowIfCancellationRequested();
            if (count == 0 || ranges[count - 1].Face != cell.Face)
                ranges[count++] = new TerrainFaceCellRange(cell.Face, cell.X, cell.X, cell.Y, cell.Y);
            else
            {
                TerrainFaceCellRange prior = ranges[count - 1];
                ranges[count - 1] = prior with { X0 = Math.Min(prior.X0, cell.X), X1 = Math.Max(prior.X1, cell.X),
                    Y0 = Math.Min(prior.Y0, cell.Y), Y1 = Math.Max(prior.Y1, cell.Y) };
            }
        }
        return new TerrainCollisionFootprint(lattice, ranges[..count].ToArray());
    }
}

/// <summary>Caller-owned mesh plus immutable footprint from the same canonical cell enumeration.</summary>
public sealed record TerrainCollisionMesh(TriangleMesh Mesh, TerrainCollisionFootprint Footprint);

/// <summary>The shared conservative range calculation used by seal candidates and collision broadphase.</summary>
internal static class CanonicalRadialShadow
{
    internal static TerrainFaceCellRange? Range(FormationBounds bounds, int face, int level)
    {
        int n = 1 << level;
        (double n0, double n1) = AxisInterval(bounds, CubeSphere.Normal(face));
        (double u0, double u1) = AxisInterval(bounds, CubeSphere.AxisU(face));
        (double v0, double v1) = AxisInterval(bounds, CubeSphere.AxisV(face));
        // Public bounds may reach the finite double endpoint. If outward rounding becomes infinite, retain the
        // whole face rather than let an inf/inf ratio produce NaN and accidentally narrow its integer range.
        // Seal body coordinates are already bounded, so this does not change its admitted candidate enumeration.
        if (!double.IsFinite(n0) || !double.IsFinite(n1) || !double.IsFinite(u0) || !double.IsFinite(u1) ||
            !double.IsFinite(v0) || !double.IsFinite(v1)) return new TerrainFaceCellRange(face, 0, n - 1, 0, n - 1);
        double uAbs = MinAbs(u0, u1), vAbs = MinAbs(v0, v1);
        if (n1 <= 0 || n1 < uAbs || n1 < vAbs) return null;
        double denominator = Math.Max(n0, Math.Max(uAbs, vAbs));
        double uLow = -1, uHigh = 1, vLow = -1, vHigh = 1;
        // An origin-containing box conservatively covers a face, never an empty candidate set.
        if (denominator > 0)
        {
            (uLow, uHigh) = Ratios(u0, u1, denominator, n1);
            (vLow, vHigh) = Ratios(v0, v1, denominator, n1);
        }
        uLow = Math.Max(-1, uLow); uHigh = Math.Min(1, uHigh);
        vLow = Math.Max(-1, vLow); vHigh = Math.Min(1, vHigh);
        if (uLow > uHigh || vLow > vHigh) return null;
        (int x0, int x1) = Cells(uLow, uHigh, n);
        (int y0, int y1) = Cells(vLow, vHigh, n);
        // Exactly the prior seal candidate guard: represented normalization/radial multiplication roundoff.
        return new TerrainFaceCellRange(face, Math.Max(0, x0 - 1), Math.Min(n - 1, x1 + 1),
            Math.Max(0, y0 - 1), Math.Min(n - 1, y1 + 1));
    }

    private static (double Low, double High) AxisInterval(FormationBounds bounds, Vector3D axis)
    {
        double sign = axis.X != 0 ? axis.X : axis.Y != 0 ? axis.Y : axis.Z;
        double low = axis.X != 0 ? bounds.Min.X : axis.Y != 0 ? bounds.Min.Y : bounds.Min.Z;
        double high = axis.X != 0 ? bounds.Max.X : axis.Y != 0 ? bounds.Max.Y : bounds.Max.Z;
        return sign > 0 ? (Math.BitDecrement(low), Math.BitIncrement(high)) : (Math.BitDecrement(-high), Math.BitIncrement(-low));
    }
    private static double MinAbs(double low, double high) => low <= 0 && high >= 0 ? 0 : Math.Min(Math.Abs(low), Math.Abs(high));
    private static (double Low, double High) Ratios(double a, double b, double lowDenominator, double highDenominator)
    {
        double aa = a / lowDenominator, ab = a / highDenominator, ba = b / lowDenominator, bb = b / highDenominator;
        return (Math.BitDecrement(Math.Min(Math.Min(aa, ab), Math.Min(ba, bb))),
            Math.BitIncrement(Math.Max(Math.Max(aa, ab), Math.Max(ba, bb))));
    }
    private static (int First, int Last) Cells(double low, double high, int n)
    {
        double Warp(int index) => index == 0 ? -1 : index == n ? 1 : CubeSphere.Warp(-1 + 2.0 * index / n);
        int left = 0, right = n + 1;
        while (left < right)
        { int middle = left + (right - left) / 2; if (Warp(middle) < low) left = middle + 1; else right = middle; }
        int first = Math.Clamp(left - 1, 0, n - 1);
        left = 0; right = n + 1;
        while (left < right)
        { int middle = left + (right - left) / 2; if (Warp(middle) <= high) left = middle + 1; else right = middle; }
        return (first, Math.Clamp(left - 1, 0, n - 1));
    }
}
