using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Simulation.Building;

/// <summary>
/// One convex part of a piece's collision, in the piece's local metres: its corner points (what Jolt builds a hull
/// from) and its face planes (for the quick test against character capsules).
/// </summary>
public sealed class ConvexPart
{
    public ConvexPart(Vector3[] points)
    {
        Points = points;
        Planes = HullPlanes(points);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (Vector3 p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        Min = min;
        Max = max;
    }

    public Vector3[] Points { get; }

    /// <summary>Outward face planes: a point p is inside when Dot(Normal, p) ≤ D for every plane.</summary>
    public (Vector3 Normal, float D)[] Planes { get; }

    public Vector3 Min { get; }

    public Vector3 Max { get; }

    /// <summary>
    /// How far outside the part a point is, at least (exact inside and facing a face; near an edge or corner it reads
    /// a little short, so a sphere test with it errs toward "touching").
    /// </summary>
    public float Outside(Vector3 p)
    {
        float worst = float.MinValue;
        foreach ((Vector3 n, float d) in Planes)
        {
            worst = Math.Max(worst, Vector3.Dot(n, p) - d);
        }
        return worst;
    }

    public static ConvexPart Box(float x0, float x1, float y0, float y1, float z0, float z1) => new(new[]
    {
        new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1),
        new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1),
    });

    /// <summary>A vertical prism over a convex polygon given in (x, z).</summary>
    public static ConvexPart Prism(IReadOnlyList<(double X, double Z)> polygon, double y0, double y1)
    {
        var points = new Vector3[polygon.Count * 2];
        for (int i = 0; i < polygon.Count; i++)
        {
            points[i] = new Vector3((float)polygon[i].X, (float)y0, (float)polygon[i].Z);
            points[polygon.Count + i] = new Vector3((float)polygon[i].X, (float)y1, (float)polygon[i].Z);
        }
        return new ConvexPart(points);
    }

    // The faces of the hull of a few points: every plane through three of them with all the others on its inner side.
    private static (Vector3, float)[] HullPlanes(Vector3[] points)
    {
        var planes = new List<(Vector3 Normal, float D)>();
        int n = points.Length;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                for (int k = j + 1; k < n; k++)
                {
                    Vector3 normal = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                    float length = normal.Length();
                    if (length < 1e-6f)
                    {
                        continue;
                    }
                    normal /= length;
                    float d = Vector3.Dot(normal, points[i]);
                    bool above = false, below = false;
                    foreach (Vector3 p in points)
                    {
                        float s = Vector3.Dot(normal, p) - d;
                        above |= s > 1e-4f;
                        below |= s < -1e-4f;
                    }
                    if (above && below)
                    {
                        continue;
                    }
                    if (above)
                    {
                        normal = -normal;
                        d = -d;
                    }
                    bool known = false;
                    foreach ((Vector3 m, float e) in planes)
                    {
                        known |= Vector3.Dot(m, normal) > 0.9999f && Math.Abs(e - d) < 1e-4f;
                    }
                    if (!known)
                    {
                        planes.Add((normal, d));
                    }
                }
            }
        }
        return planes.ToArray();
    }
}

/// <summary>
/// Pieces' collision (GROUND.md §6.3): convex parts built from the module sizes in <c>building.json</c>, the same on the
/// server and every client, never from the art. They follow the kit's own hulls: one box or prism for slabs and
/// walls, the jambs and lintel around an opening, a frame's border strips, a spiral's twelve treads and column.
/// </summary>
public static class PieceShapes
{
    private const double Root3 = 1.7320508075688772;

    public static ConvexPart[] For(BuildingData data, PieceDef piece)
    {
        double w = data.Width, half = w / 2, t = data.WallThickness / 2;
        var parts = new List<ConvexPart>();
        switch (piece.Family)
        {
            case PieceFamily.Foundation:
                parts.Add(ConvexPart.Prism(Polygon(piece.Shape, w), -data.FoundationDepth, 0));
                break;
            case PieceFamily.Floor when piece.Frame:
            {
                (double X, double Z)[] outer = Polygon(piece.Shape, w);
                (double X, double Z)[] inner = Inset(outer, piece.Shape, w, data.FrameBorder);
                for (int i = 0; i < outer.Length; i++)
                {
                    int j = (i + 1) % outer.Length;
                    parts.Add(ConvexPart.Prism(new[] { outer[i], outer[j], inner[j], inner[i] }, -data.FloorThickness, 0));
                }
                break;
            }
            case PieceFamily.Floor:
                parts.Add(ConvexPart.Prism(Polygon(piece.Shape, w), -data.FloorThickness, 0));
                break;
            case PieceFamily.Wall:
                foreach ((double x0, double x1, double y0, double y1) in WallRegions(data, piece))
                {
                    parts.Add(ConvexPart.Box((float)x0, (float)x1, (float)y0, (float)y1, (float)-t, (float)t));
                }
                break;
            case PieceFamily.Stairs:
                Stairs(data, piece, parts);
                break;
            case PieceFamily.Roof:
                parts.Add(Roof(data, piece));
                parts.AddRange(Gables(data, piece));
                break;
        }
        return parts.ToArray();
    }

    /// <summary>The solid parts of a wall piece, as (x0, x1, y0, y1) rectangles in its plane (the kit's wall regions).</summary>
    public static IReadOnlyList<(double X0, double X1, double Y0, double Y1)> WallRegions(BuildingData data, PieceDef piece)
    {
        double half = data.Width / 2, h = piece.Height;
        switch (piece.Opening)
        {
            case Opening.Window:
            {
                double ow = data.WindowOpening.Width / 2, bottom = data.WindowSill, top = data.WindowSill + data.WindowOpening.Height;
                return new[] { (-half, -ow, 0.0, h), (ow, half, 0.0, h), (-ow, ow, 0.0, bottom), (-ow, ow, top, h) };
            }
            case Opening.Door:
            {
                double ow = data.DoorOpening.Width / 2, top = data.DoorOpening.Height;
                return new[] { (-half, -ow, 0.0, h), (ow, half, 0.0, h), (-ow, ow, top, h) };
            }
            case Opening.Double:
            {
                double ow = data.DoubleOpening.Width / 2, top = data.DoubleOpening.Height;
                return new[] { (-half, -ow, 0.0, h), (ow, half, 0.0, h), (-ow, ow, top, h) };
            }
            default:
                return new[] { (-half, half, 0.0, h) };
        }
    }

    /// <summary>The footprint as (x, z) corners, counter-clockwise seen from above.</summary>
    public static (double X, double Z)[] Polygon(PieceShape shape, double width)
    {
        Hollow.Core.Vector3D[] corners = PieceGeometry.Footprint(shape, width);
        var polygon = new (double, double)[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            polygon[i] = (corners[i].X, corners[i].Z);
        }
        return polygon;
    }

    // The footprint with every edge moved inward by a border (the kit's frame inset: a scale about the centroid).
    private static (double X, double Z)[] Inset((double X, double Z)[] polygon, PieceShape shape, double width, double border)
    {
        double inradius = shape == PieceShape.Triangle ? width * Root3 / 6 : width / 2;
        double scale = 1 - border / inradius;
        var inner = new (double, double)[polygon.Length];
        for (int i = 0; i < polygon.Length; i++)
        {
            inner[i] = (polygon[i].X * scale, polygon[i].Z * scale);
        }
        return inner;
    }

    private static void Stairs(BuildingData data, PieceDef piece, List<ConvexPart> parts)
    {
        double w = data.Width, half = w / 2, h = piece.Height;
        switch (piece.Stairs)
        {
            case StairsKind.Spiral:
                Spiral(data, piece, parts);
                return;
            case StairsKind.U:
            {
                // hollow-orbit's U stairs: up the east half toward −Z, a landing across the north, back down... up the west half.
                double landing = h / 2, turn = -half / 3;
                parts.Add(Wedge(0, half, half, turn, 0, landing));
                parts.Add(ConvexPart.Box(-(float)half, (float)half, 0, (float)landing, -(float)half, (float)turn));
                parts.Add(SlopedSlab(-half, 0, turn, half, landing, h, 0.25, alongZ: true));
                return;
            }
            case StairsKind.L:
            {
                // Up the east strip toward −Z, a landing in the north-east corner, then west along the north strip.
                double landing = h / 2, inner = half - 1.2, edge = -half + 1.2;
                parts.Add(Wedge(inner, half, half, edge, 0, landing));
                parts.Add(ConvexPart.Box((float)inner, (float)half, 0, (float)landing, -(float)half, (float)edge));
                parts.Add(SlopedSlab(-half, edge, inner, -half, landing, h, 0.25, alongZ: false));
                return;
            }
            case StairsKind.Ramp:
            case StairsKind.Steps:
                // One slope over the whole footprint, from its +Z edge up to its −Z edge (the art shows the steps).
                parts.Add(Wedge(-half, half, half, -half, 0, h));
                return;
        }
    }

    // A solid ramp over x0..x1, rising from (z = zLow, y = y0) to (z = zHigh, y = y1), standing on y0 at both ends.
    private static ConvexPart Wedge(double x0, double x1, double zLow, double zHigh, double y0, double y1) => new(new[]
    {
        new Vector3((float)x0, (float)y0, (float)zLow), new Vector3((float)x1, (float)y0, (float)zLow),
        new Vector3((float)x0, (float)y0, (float)zHigh), new Vector3((float)x1, (float)y0, (float)zHigh),
        new Vector3((float)x0, (float)y1, (float)zHigh), new Vector3((float)x1, (float)y1, (float)zHigh),
    });

    // A sloped slab of a vertical thickness: alongZ, it spans x from a0 to a1 and rises along z from b0 (y0) to b1 (y1);
    // otherwise it spans z from a0 to a1 and rises along x from b0 to b1.
    private static ConvexPart SlopedSlab(double a0, double a1, double b0, double b1, double y0, double y1, double thickness, bool alongZ)
    {
        Vector3 P(double a, double b, double y) => alongZ
            ? new Vector3((float)a, (float)y, (float)b)
            : new Vector3((float)b, (float)y, (float)a);
        return new ConvexPart(new[]
        {
            P(a0, b0, y0), P(a1, b0, y0), P(a0, b1, y1), P(a1, b1, y1),
            P(a0, b0, y0 - thickness), P(a1, b0, y0 - thickness), P(a0, b1, y1 - thickness), P(a1, b1, y1 - thickness),
        });
    }

    /// <summary>
    /// The kit's spiral: twelve treads of 15° turning half a circle about a column (on the square's centre, or on the
    /// middle of a triangle's base), each tread 10 cm deep with open space below, rising a half storey.
    /// </summary>
    private static void Spiral(BuildingData data, PieceDef piece, List<ConvexPart> parts)
    {
        (double X, double Z)[] polygon = Polygon(piece.Shape, data.Width);
        (double X, double Z) c = piece.IsTriangle ? (0, data.Width * Root3 / 6) : (0, 0);
        const int steps = 12;
        const double inner = 0.115;
        double rise = piece.Height / steps;
        for (int i = 0; i < steps; i++)
        {
            double a = i * Math.PI / steps + 1e-5, b = (i + 1) * Math.PI / steps - 1e-5, mid = (a + b) / 2;
            var tread = new List<(double, double)> { Along(c, a, inner) };
            foreach (double angle in new[] { a, mid, b })
            {
                tread.Add(Along(c, angle, Math.Max(inner + 0.03, RayToEdge(c, angle, polygon) - 0.018)));
            }
            tread.Add(Along(c, b, inner));
            double top = (i + 1) * rise;
            parts.Add(ConvexPart.Prism(tread, top - 0.10, top));
        }
        var column = new (double, double)[12];
        for (int i = 0; i < column.Length; i++)
        {
            column[i] = Along(c, i * 2 * Math.PI / column.Length, 0.104);
        }
        parts.Add(ConvexPart.Prism(column, 0, piece.Height));
    }

    // The kit measures angles in Blender's x-y plane, from +X toward +Y; Blender's +Y is −Z here.
    private static (double X, double Z) Along((double X, double Z) c, double angle, double distance) =>
        (c.X + Math.Cos(angle) * distance, c.Z - Math.Sin(angle) * distance);

    private static double RayToEdge((double X, double Z) c, double angle, (double X, double Z)[] polygon)
    {
        double dx = Math.Cos(angle), dz = -Math.Sin(angle), best = double.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            (double ax, double az) = polygon[i];
            (double bx, double bz) = polygon[(i + 1) % polygon.Length];
            double ex = bx - ax, ez = bz - az;
            double den = dx * ez - dz * ex;
            if (Math.Abs(den) < 1e-8)
            {
                continue;
            }
            double acx = ax - c.X, acz = az - c.Z;
            double t = (acx * ez - acz * ex) / den;
            double s = (acx * dz - acz * dx) / den;
            if (t > 1e-6 && s is >= -1e-7 and <= 1 + 1e-7 && t < best)
            {
                best = t;
            }
        }
        return best == double.MaxValue ? 0.5 : best;
    }

    /// <summary>A roof: a slab whose underside rises from its low edge (edge 0, +Z) to its full height on the far side.</summary>
    private static ConvexPart Roof(BuildingData data, PieceDef piece)
    {
        (double X, double Z)[] polygon = Polygon(piece.Shape, data.Width);
        (double low, double span) = PieceGeometry.RoofSpan(piece.Shape, data.Width);
        double h = piece.Height;
        // The underside's upward normal, and the slab thickness along it.
        var normal = Vector3.Normalize(new Vector3(0, (float)span, (float)h));
        Vector3 offset = normal * (float)data.FloorThickness;
        var points = new List<Vector3>();
        foreach ((double x, double z) in polygon)
        {
            var bottom = new Vector3((float)x, (float)(h * (low - z) / span), (float)z);
            points.Add(bottom);
            points.Add(bottom + offset);
        }
        return new ConvexPart(points.ToArray());
    }

    /// <summary>The thickness of a roof's gable panels.</summary>
    public const double GableThickness = 0.06;

    /// <summary>
    /// A roof's gables: the upright triangles closing the space under its slope above its sides (a square roof's two
    /// sides, a triangle roof's two slanting edges), so two roofs meeting in a ridge close the gable end. Each is a thin
    /// panel just inside the footprint.
    /// </summary>
    public static IEnumerable<ConvexPart> Gables(BuildingData data, PieceDef piece)
    {
        (double X, double Z)[] polygon = Polygon(piece.Shape, data.Width);
        (double low, double span) = PieceGeometry.RoofSpan(piece.Shape, data.Width);
        double h = piece.Height, t = GableThickness;
        double Rise(double z) => h * (low - z) / span;
        // The footprint's edges other than the low one (edge 0) carry a gable.
        for (int i = 1; i < polygon.Length; i++)
        {
            (double ax, double az) = polygon[i];
            (double bx, double bz) = polygon[(i + 1) % polygon.Length];
            if (piece.Shape == PieceShape.Square && i == 2)
            {
                continue; // the high edge: the next roof or a wall goes there
            }
            // Inward normal of the edge (the footprint runs counter-clockwise seen from above: inside is on the left).
            double ex = bx - ax, ez = bz - az, length = Math.Sqrt(ex * ex + ez * ez);
            double nx = ez / length, nz = -ex / length;
            var points = new List<Vector3>();
            foreach ((double x, double z) in new[] { (ax, az), (bx, bz) })
            {
                foreach (double inset in new[] { 0.0, t })
                {
                    double px = x + nx * inset, pz = z + nz * inset;
                    points.Add(new Vector3((float)px, 0, (float)pz));
                    if (Rise(z) > 1e-6)
                    {
                        points.Add(new Vector3((float)px, (float)Rise(z), (float)pz));
                    }
                }
            }
            yield return new ConvexPart(points.ToArray());
        }
    }

    /// <summary>A door leaf's box in its hinge's frame: from the hinge across its width, up its height, centred on its thickness.</summary>
    public static ConvexPart Leaf(LeafDef leaf)
    {
        float x0 = Math.Min(0, leaf.Size.X), x1 = Math.Max(0, leaf.Size.X), t = leaf.Size.Z / 2;
        return ConvexPart.Box(x0, x1, 0, leaf.Size.Y, -t, t);
    }

    /// <summary>The claim device's box, standing on its origin.</summary>
    public static ConvexPart Claim(BuildingData data)
    {
        Vector3 s = data.ClaimSize;
        return ConvexPart.Box(-s.X / 2, s.X / 2, 0, s.Y, -s.Z / 2, s.Z / 2);
    }
}
