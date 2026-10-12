using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Simulation.Building;

/// <summary>
/// A piece's place in its structure's frame: the pivot in metres (Y up) and the yaw in steps of 30°. Every yaw in a
/// structure is a multiple of 30° from its first foundation (square edges turn by 90°, triangle edges by 120°), so
/// yaws are whole steps and their sines and cosines exact constants.
/// </summary>
public readonly record struct Pose(Vector3D Position, int Yaw)
{
    /// <summary>The yaw in degrees, for display.</summary>
    public double YawDegrees => Yaw * 30.0;
}

/// <summary>A socket of a piece: where it is and, for edges, which edge.</summary>
public readonly record struct SocketId(SocketKind Kind, int Edge)
{
    /// <summary>A small number for the wire and saves (RustMVP's numbering: edges 0–3, centre 16, top 32, up 64+i, down 68+i).</summary>
    public int Code => Kind switch
    {
        SocketKind.Edge => Edge,
        SocketKind.Centre => 16,
        SocketKind.Top => 32,
        SocketKind.EdgeUp => 64 + Edge,
        _ => 68 + Edge,
    };

    /// <summary>The socket for a code, or null for a code that names none (codes from the wire are untrusted).</summary>
    public static SocketId? FromCode(int code) => code switch
    {
        >= 0 and <= 3 => new SocketId(SocketKind.Edge, code),
        16 => new SocketId(SocketKind.Centre, 0),
        32 => new SocketId(SocketKind.Top, 0),
        >= 64 and <= 67 => new SocketId(SocketKind.EdgeUp, code - 64),
        >= 68 and <= 71 => new SocketId(SocketKind.EdgeDown, code - 68),
        _ => null,
    };

    public override string ToString() => Kind is SocketKind.Centre or SocketKind.Top ? Kind.ToString() : $"{Kind} {Edge}";
}

/// <summary>
/// The logical geometry of building pieces (GROUND.md §6.3), ported from RustMVP's engine-free rules
/// (<c>building_rules.hpp</c>, <c>building_geometry.hpp</c>) into this project's frame: metres, Y up, a piece's local
/// X along its edge or wall, a wall's outside (hard side) toward +Z. RustMVP's (x, y, z-up) maps to (x, z, −y) here,
/// which keeps its yaw angles and their sense. Pivots: slabs at the footprint's centroid on their top plane; walls at
/// the middle of their bottom edge; stairs and roofs at the footprint's centroid on their bottom plane.
/// </summary>
public static class PieceGeometry
{
    private const double Root3 = 1.7320508075688772;

    private static readonly double[] CosTable =
    {
        1, Root3 / 2, 0.5, 0, -0.5, -Root3 / 2, -1, -Root3 / 2, -0.5, 0, 0.5, Root3 / 2,
    };

    private static readonly double[] SinTable =
    {
        0, 0.5, Root3 / 2, 1, Root3 / 2, 0.5, 0, -0.5, -Root3 / 2, -1, -Root3 / 2, -0.5,
    };

    /// <summary>A yaw in steps of 30°, wrapped to 0–11.</summary>
    public static int Wrap(int steps) => ((steps % 12) + 12) % 12;

    public static double Cos(int steps) => CosTable[Wrap(steps)];

    public static double Sin(int steps) => SinTable[Wrap(steps)];

    /// <summary>Turns a point about +Y by a yaw: (1, 0, 0) at 90° goes to (0, 0, −1), as Godot's basis does.</summary>
    public static Vector3D Rotate(Vector3D v, int yaw)
    {
        double c = Cos(yaw), s = Sin(yaw);
        return new Vector3D(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }

    /// <summary>A pose's point in its structure's frame.</summary>
    public static Vector3D ToFrame(in Pose pose, Vector3D local) => pose.Position + Rotate(local, pose.Yaw);

    /// <summary>A frame point in a pose's local coordinates.</summary>
    public static Vector3D ToLocal(in Pose pose, Vector3D frame) => Rotate(frame - pose.Position, -pose.Yaw);

    // ---------------------------------------------------------------- footprints

    /// <summary>
    /// The footprint's corners, counter-clockwise seen from above, at y = 0: a square of side <paramref name="width"/>, or an
    /// equilateral triangle with its centroid at the origin and its first edge (the base) toward +Z.
    /// </summary>
    public static Vector3D[] Footprint(PieceShape shape, double width)
    {
        double half = width / 2;
        if (shape == PieceShape.Triangle)
        {
            double altitude = width * Root3 / 2;
            return new[]
            {
                new Vector3D(-half, 0, altitude / 3), new Vector3D(half, 0, altitude / 3), new Vector3D(0, 0, -2 * altitude / 3),
            };
        }
        if (shape == PieceShape.Square)
        {
            return new[]
            {
                new Vector3D(-half, 0, half), new Vector3D(half, 0, half), new Vector3D(half, 0, -half), new Vector3D(-half, 0, -half),
            };
        }
        return Array.Empty<Vector3D>();
    }

    /// <summary>An edge's middle and its direction as a yaw (the edge runs from corner i to corner i + 1).</summary>
    public static (Vector3D Middle, int Yaw) Edge(PieceShape shape, int index, double width)
    {
        Vector3D[] corners = Footprint(shape, width);
        Vector3D a = corners[index], b = corners[(index + 1) % corners.Length];
        int yaw = shape == PieceShape.Triangle ? index * 4 : index * 3;
        return ((a + b) * 0.5, yaw);
    }

    // ---------------------------------------------------------------- sockets

    /// <summary>The sockets of <paramref name="parent"/> that take <paramref name="child"/> (each footprint edge counts as one).</summary>
    public static void Sockets(PieceDef parent, PieceDef child, List<SocketId> into)
    {
        into.Clear();
        foreach (SocketDef socket in parent.Sockets)
        {
            if (!socket.Accepts(parent, child))
            {
                continue;
            }
            if (socket.At is SocketKind.Edge or SocketKind.EdgeUp or SocketKind.EdgeDown)
            {
                for (int i = 0; i < parent.EdgeCount; i++)
                {
                    into.Add(new SocketId(socket.At, i));
                }
            }
            else
            {
                into.Add(new SocketId(socket.At, 0));
            }
        }
    }

    /// <summary>Whether a parent has the socket and it takes the child.</summary>
    public static bool Accepts(PieceDef parent, SocketId socket, PieceDef child)
    {
        foreach (SocketDef s in parent.Sockets)
        {
            if (s.At == socket.Kind && s.Accepts(parent, child))
            {
                bool edge = socket.Kind is SocketKind.Edge or SocketKind.EdgeUp or SocketKind.EdgeDown;
                return edge ? socket.Edge >= 0 && socket.Edge < parent.EdgeCount : socket.Edge == 0;
            }
        }
        return false;
    }

    /// <summary>
    /// How many ways the child fits a socket (RustMVP's <c>orientation_count</c>, in its order): a spiral on the same
    /// spiral 1; a wall or roof 2 (flipped); stairs 4 or 3; a floor on a wall or roof top its edges × 2 sides; on stairs
    /// its edges; a slab on a slab edge its edges (which of them meets the parent's).
    /// </summary>
    public static int Orientations(PieceDef parent, SocketId socket, PieceDef child)
    {
        if (!Accepts(parent, socket, child))
        {
            return 0;
        }
        if (parent.IsSpiral && child == parent)
        {
            return 1;
        }
        if (child.Family is PieceFamily.Wall or PieceFamily.Roof)
        {
            return 2;
        }
        if (child.Family == PieceFamily.Stairs)
        {
            return child.IsTriangle ? 3 : 4;
        }
        if (parent.Family is PieceFamily.Wall or PieceFamily.Roof)
        {
            return child.EdgeCount * 2;
        }
        return child.EdgeCount;
    }

    /// <summary>
    /// Where a child goes on a parent's socket in one of its orientations (RustMVP's <c>snap</c>), in the parent's
    /// frame. False when the child doesn't fit that socket that way.
    /// </summary>
    public static bool Snap(BuildingData data, PieceDef parent, in Pose parentPose, SocketId socket, PieceDef child, int orientation,
        out Pose pose)
    {
        pose = default;
        if (orientation < 0 || orientation >= Orientations(parent, socket, child))
        {
            return false;
        }
        double w = data.Width;
        Vector3D position = Vector3D.Zero;
        int yaw;
        if (parent.IsSlab)
        {
            if (socket.Kind == SocketKind.Centre)
            {
                yaw = orientation * (child.IsTriangle ? 4 : 3);
            }
            else
            {
                (Vector3D middle, int edgeYaw) = Edge(parent.Shape, socket.Edge, w);
                position = middle;
                if (child.Family == PieceFamily.Wall)
                {
                    yaw = edgeYaw + 6 * orientation;
                }
                else
                {
                    int receiving = child.Family == PieceFamily.Roof ? 0 : orientation;
                    (Vector3D childMiddle, int childYaw) = Edge(child.Shape, receiving, w);
                    // Neighbouring slabs face the shared edge from opposite sides; a roof starts over its support (0)
                    // or slopes outward (1).
                    yaw = edgeYaw - childYaw + (child.Family == PieceFamily.Roof ? 6 * orientation : 6);
                    position = middle - Rotate(childMiddle, yaw);
                }
                if (socket.Kind == SocketKind.EdgeUp)
                {
                    position += new Vector3D(0, data.Storey / 2, 0);
                }
                else if (socket.Kind == SocketKind.EdgeDown)
                {
                    position -= new Vector3D(0, data.Storey / 2, 0);
                }
            }
        }
        else if (parent.Family == PieceFamily.Wall || parent.Family == PieceFamily.Roof)
        {
            // A wall's top, or a square roof's high edge.
            Vector3D top = parent.Family == PieceFamily.Wall
                ? new Vector3D(0, parent.Height, 0)
                : new Vector3D(0, parent.Height, -w / 2);
            position = top;
            if (child.Family == PieceFamily.Wall)
            {
                yaw = 6 * orientation;
            }
            else
            {
                int count = child.EdgeCount;
                int receiving = child.Family == PieceFamily.Roof ? 0 : orientation % count;
                int side = child.Family == PieceFamily.Roof ? orientation : orientation / count;
                (Vector3D childMiddle, int childYaw) = Edge(child.Shape, receiving, w);
                // Side 0 puts the child on the wall's inside (−Z).
                yaw = 6 * side - childYaw;
                position = top - Rotate(childMiddle, yaw);
            }
        }
        else
        {
            // Stairs: the top.
            position = new Vector3D(0, parent.Height, 0);
            if (parent.IsSpiral && child == parent)
            {
                // The next half-storey module turns half a circle; a triangle spiral moves into the neighbouring
                // triangle across its base, so two modules come back to the same column a storey up.
                yaw = 6;
                if (parent.IsTriangle)
                {
                    position = new Vector3D(0, parent.Height, w / Root3);
                }
            }
            else
            {
                yaw = orientation * (child.IsTriangle ? 4 : 3);
            }
        }
        pose = new Pose(ToFrame(parentPose, position), Wrap(parentPose.Yaw + yaw));
        return true;
    }

    /// <summary>The socket's point in the frame, where the cursor aims at it (RustMVP's <c>socket_anchor</c>).</summary>
    public static Vector3D Anchor(BuildingData data, PieceDef parent, in Pose parentPose, SocketId socket)
    {
        Vector3D local = Vector3D.Zero;
        if (parent.IsSlab)
        {
            if (socket.Kind != SocketKind.Centre)
            {
                local = Edge(parent.Shape, socket.Edge, data.Width).Middle;
                if (socket.Kind == SocketKind.EdgeUp)
                {
                    local += new Vector3D(0, data.Storey / 2, 0);
                }
                else if (socket.Kind == SocketKind.EdgeDown)
                {
                    local -= new Vector3D(0, data.Storey / 2, 0);
                }
            }
        }
        else
        {
            local = new Vector3D(0, parent.Height, parent.Family == PieceFamily.Roof ? -data.Width / 2 : 0);
        }
        return ToFrame(parentPose, local);
    }

    /// <summary>
    /// How much stability a link from parent to child costs, in thousandths (RustMVP's <c>support_loss</c>): foundation to
    /// foundation and anything to a wall nothing; the centre socket, a wall or stairs below, or a roof above 100; any
    /// other slab edge 200.
    /// </summary>
    public static int SupportLoss(BuildingData data, PieceDef parent, PieceDef child, SocketId socket)
    {
        if (parent.Family == PieceFamily.Foundation && child.Family == PieceFamily.Foundation)
        {
            return data.LossFoundationToFoundation;
        }
        if (child.Family == PieceFamily.Wall)
        {
            return data.LossToWall;
        }
        if (socket.Kind == SocketKind.Centre)
        {
            return data.LossCentre;
        }
        if (parent.Family == PieceFamily.Wall)
        {
            return data.LossFromWall;
        }
        if (parent.Family == PieceFamily.Stairs)
        {
            return data.LossFromStairs;
        }
        if (child.Family == PieceFamily.Roof)
        {
            return data.LossToRoof;
        }
        return data.LossSlabEdge;
    }

    /// <summary>
    /// Pieces in the same place compete for it (RustMVP's placement families): slabs and frames, walls, stairs, roofs.
    /// </summary>
    public static int PlaceFamily(PieceDef piece) => piece.Family switch
    {
        PieceFamily.Foundation or PieceFamily.Floor => 0,
        PieceFamily.Wall => 1,
        PieceFamily.Roof => 3,
        _ => 2,
    };

    /// <summary>Same place: positions within a centimetre and the same yaw (the same piece kind at the same pose).</summary>
    public static bool SamePose(in Pose a, in Pose b) =>
        (a.Position - b.Position).LengthSquared <= 1e-4 && Wrap(a.Yaw - b.Yaw) == 0;

    // ---------------------------------------------------------------- reach and aim

    /// <summary>
    /// The point of a piece's logical surface nearest to a frame point (RustMVP's <c>closest_placement_point</c>): a
    /// wall's box, a slab's top within its thickness, a roof's slope, a staircase's footprint up to its height.
    /// </summary>
    public static Vector3D ClosestPoint(BuildingData data, PieceDef piece, in Pose pose, Vector3D from)
    {
        Vector3D local = ToLocal(pose, from);
        Vector3D result;
        if (piece.Family == PieceFamily.Wall)
        {
            double half = data.Width / 2, t = data.WallThickness / 2;
            result = new Vector3D(Math.Clamp(local.X, -half, half), Math.Clamp(local.Y, 0, piece.Height), Math.Clamp(local.Z, -t, t));
        }
        else
        {
            result = ClosestOnFootprint(piece.Shape, data.Width, local);
            double y;
            if (piece.Family == PieceFamily.Foundation)
            {
                y = Math.Clamp(local.Y, -data.FoundationDepth, 0);
            }
            else if (piece.Family == PieceFamily.Floor)
            {
                y = Math.Clamp(local.Y, -data.FloorThickness, 0);
            }
            else if (piece.Family == PieceFamily.Roof)
            {
                (double low, double span) = RoofSpan(piece.Shape, data.Width);
                y = piece.Height * Math.Clamp((low - result.Z) / span, 0, 1);
            }
            else
            {
                y = Math.Clamp(local.Y, 0, piece.Height);
            }
            result = new Vector3D(result.X, y, result.Z);
        }
        return ToFrame(pose, result);
    }

    /// <summary>A roof's low edge (z of edge 0) and the horizontal run to its high side.</summary>
    public static (double Low, double Span) RoofSpan(PieceShape shape, double width) =>
        shape == PieceShape.Triangle ? (width * Root3 / 6, width * Root3 / 2) : (width / 2, width);

    /// <summary>The point of a footprint (on y = 0) nearest to a local point: the point itself when inside.</summary>
    public static Vector3D ClosestOnFootprint(PieceShape shape, double width, Vector3D point)
    {
        Vector3D[] corners = Footprint(shape, width);
        var p = new Vector3D(point.X, 0, point.Z);
        bool inside = true;
        double best = double.MaxValue;
        Vector3D nearest = p;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3D a = corners[i], b = corners[(i + 1) % corners.Length];
            if (Cross2(a, b, p) < -1e-9)
            {
                inside = false;
            }
            Vector3D c = ClosestOnSegment(p, a, b);
            double d = (c - p).LengthSquared;
            if (d < best)
            {
                best = d;
                nearest = c;
            }
        }
        return inside ? p : nearest;
    }

    // > 0 when p is left of a→b seen from above (inside a counter-clockwise footprint). Here "left" with +X right and
    // +Z toward the viewer is the −Z side, so the sign is flipped from the usual x-y cross product.
    private static double Cross2(Vector3D a, Vector3D b, Vector3D p) => -((b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X));

    public static Vector3D ClosestOnSegment(Vector3D p, Vector3D a, Vector3D b)
    {
        Vector3D ab = b - a;
        double length = ab.LengthSquared;
        double t = length > 1e-12 ? Math.Clamp(Vector3D.Dot(p - a, ab) / length, 0, 1) : 0;
        return a + ab * t;
    }

    /// <summary>Squared distance from a point to a segment.</summary>
    public static double DistanceToSegmentSquared(Vector3D p, Vector3D a, Vector3D b) => (ClosestOnSegment(p, a, b) - p).LengthSquared;

    /// <summary>
    /// How near an aim segment passes to a slab's whole footprint (RustMVP's <c>aim_footprint</c>), so a ceiling can be
    /// chosen by aiming anywhere on it, its open frame included. Returns the squared miss distance.
    /// </summary>
    public static double AimFootprint(BuildingData data, PieceDef piece, in Pose pose, Vector3D start, Vector3D end)
    {
        Vector3D a = ToLocal(pose, start), b = ToLocal(pose, end), d = b - a;
        double best = (ClosestOnFootprint(piece.Shape, data.Width, a) - a).LengthSquared;
        void Consider(Vector3D footprintPoint, Vector3D rayPoint)
        {
            double distance = (footprintPoint - rayPoint).LengthSquared;
            if (distance < best)
            {
                best = distance;
            }
        }
        Consider(ClosestOnFootprint(piece.Shape, data.Width, b), b);
        if (Math.Abs(d.Y) > 1e-8)
        {
            double fraction = -a.Y / d.Y;
            if (fraction is >= 0 and <= 1)
            {
                Vector3D crossing = a + d * fraction;
                Consider(ClosestOnFootprint(piece.Shape, data.Width, crossing), crossing);
            }
        }
        Vector3D[] corners = Footprint(piece.Shape, data.Width);
        double rayLength = d.LengthSquared;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3D p = corners[i], edge = corners[(i + 1) % corners.Length] - p, offset = a - p;
            double edgeLength = edge.LengthSquared, cross = Vector3D.Dot(d, edge);
            double along = Vector3D.Dot(d, offset), edgeAlong = Vector3D.Dot(edge, offset);
            double denominator = rayLength * edgeLength - cross * cross;
            double rayFraction = denominator > 1e-8 ? Math.Clamp((cross * edgeAlong - along * edgeLength) / denominator, 0, 1) : 0;
            double edgeFraction = edgeLength > 1e-8 ? (cross * rayFraction + edgeAlong) / edgeLength : 0;
            if (edgeFraction < 0)
            {
                edgeFraction = 0;
                rayFraction = rayLength > 1e-8 ? Math.Clamp(-along / rayLength, 0, 1) : 0;
            }
            else if (edgeFraction > 1)
            {
                edgeFraction = 1;
                rayFraction = rayLength > 1e-8 ? Math.Clamp((cross - along) / rayLength, 0, 1) : 0;
            }
            Consider(p + edge * edgeFraction, a + d * rayFraction);
        }
        return best;
    }

    /// <summary>
    /// Where an aim segment crosses a wall opening's plane inside the opening (RustMVP's <c>ray_opening</c>): aiming into
    /// an empty doorway selects it.
    /// </summary>
    public static bool RayOpening(in Pose pose, double width, double height, Vector3D start, Vector3D end, out Vector3D point)
    {
        point = default;
        Vector3D a = ToLocal(pose, start), b = ToLocal(pose, end);
        double dz = b.Z - a.Z;
        if (Math.Abs(dz) < 1e-8)
        {
            return false;
        }
        double fraction = -a.Z / dz;
        if (fraction is < 0 or > 1)
        {
            return false;
        }
        Vector3D hit = a + (b - a) * fraction;
        if (Math.Abs(hit.X) > width / 2 || hit.Y < 0 || hit.Y > height)
        {
            return false;
        }
        point = ToFrame(pose, hit);
        return true;
    }

    // ---------------------------------------------------------------- seams (collision exemptions)

    /// <summary>
    /// A wall standing on or beside a slab's border, at any height through its span, with the whole wall on or outside
    /// one footprint edge (RustMVP's <c>wall_tile_seam</c>): their small overlap is the joint, not an intersection.
    /// </summary>
    public static bool WallTileSeam(BuildingData data, PieceDef wall, in Pose wallPose, PieceDef tile, in Pose tilePose)
    {
        if (wall.Family != PieceFamily.Wall || !(tile.IsSlab || tile.Family == PieceFamily.Stairs))
        {
            return false;
        }
        double dy = tilePose.Position.Y - wallPose.Position.Y;
        double tileHeight = tile.Family == PieceFamily.Stairs ? tile.Height : 0;
        if (dy > wall.Height + 0.005 || dy + tileHeight < -0.005)
        {
            return false;
        }
        double half = data.Width / 2;
        Vector3D a = ToLocal(tilePose, ToFrame(wallPose, new Vector3D(-half, 0, 0)));
        Vector3D b = ToLocal(tilePose, ToFrame(wallPose, new Vector3D(half, 0, 0)));
        a = new Vector3D(a.X, 0, a.Z);
        b = new Vector3D(b.X, 0, b.Z);
        Vector3D[] corners = Footprint(tile.Shape, data.Width);
        bool separated = false, touch = false;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3D p = corners[i], q = corners[(i + 1) % corners.Length];
            double length = (q - p).Length;
            double Side(Vector3D v) => Cross2(p, q, v) / length;
            if (Side(a) <= 0.005 && Side(b) <= 0.005)
            {
                separated = true;
            }
            if ((ClosestOnSegment(a, p, q) - a).LengthSquared <= 2.5e-5 || (ClosestOnSegment(b, p, q) - b).LengthSquared <= 2.5e-5 ||
                (ClosestOnSegment(p, a, b) - p).LengthSquared <= 2.5e-5)
            {
                touch = true;
            }
        }
        return separated && touch;
    }

    /// <summary>
    /// Two slabs at the same height whose footprints share an edge or a corner without overlapping (RustMVP's
    /// <c>tile_tile_seam</c>), such as the diagonal pair of a 2 × 2 base.
    /// </summary>
    public static bool TileTileSeam(BuildingData data, PieceDef left, in Pose leftPose, PieceDef right, in Pose rightPose)
    {
        if (!left.IsSlab || !right.IsSlab || Math.Abs(leftPose.Position.Y - rightPose.Position.Y) > 0.005)
        {
            return false;
        }
        Vector3D[] a = Footprint(left.Shape, data.Width), b = Footprint(right.Shape, data.Width);
        for (int i = 0; i < a.Length; i++)
        {
            Vector3D p = ToFrame(leftPose, a[i]);
            a[i] = new Vector3D(p.X, 0, p.Z);
        }
        for (int i = 0; i < b.Length; i++)
        {
            Vector3D p = ToFrame(rightPose, b[i]);
            b[i] = new Vector3D(p.X, 0, p.Z);
        }
        bool boundary = false;
        bool Axes(Vector3D[] shape)
        {
            for (int i = 0; i < shape.Length; i++)
            {
                Vector3D edge = shape[(i + 1) % shape.Length] - shape[i];
                double length = edge.Length;
                if (length < 1e-8)
                {
                    return false;
                }
                var normal = new Vector3D(-edge.Z / length, 0, edge.X / length);
                double amin = double.MaxValue, amax = double.MinValue, bmin = double.MaxValue, bmax = double.MinValue;
                foreach (Vector3D p in a)
                {
                    double v = Vector3D.Dot(p, normal);
                    amin = Math.Min(amin, v);
                    amax = Math.Max(amax, v);
                }
                foreach (Vector3D p in b)
                {
                    double v = Vector3D.Dot(p, normal);
                    bmin = Math.Min(bmin, v);
                    bmax = Math.Max(bmax, v);
                }
                double gap = Math.Max(bmin - amax, amin - bmax);
                if (gap > 0.005)
                {
                    return false;
                }
                if (gap >= -0.005)
                {
                    boundary = true;
                }
            }
            return true;
        }
        return Axes(a) && Axes(b) && boundary;
    }

    /// <summary>
    /// Two walls meeting at a corner with overlapping heights (RustMVP's <c>wall_wall_seam</c>): only their ends may
    /// meet; walls running into each other along the same line still block.
    /// </summary>
    public static bool WallWallSeam(BuildingData data, PieceDef left, in Pose leftPose, PieceDef right, in Pose rightPose)
    {
        if (left.Family != PieceFamily.Wall || right.Family != PieceFamily.Wall)
        {
            return false;
        }
        if (leftPose.Position.Y > rightPose.Position.Y + right.Height + 0.005 ||
            rightPose.Position.Y > leftPose.Position.Y + left.Height + 0.005)
        {
            return false;
        }
        double half = data.Width / 2;
        foreach (double ls in new[] { -1.0, 1.0 })
        {
            foreach (double rs in new[] { -1.0, 1.0 })
            {
                Vector3D a = ToFrame(leftPose, new Vector3D(ls * half, 0, 0)), b = ToFrame(rightPose, new Vector3D(rs * half, 0, 0));
                a = new Vector3D(a.X, 0, a.Z);
                b = new Vector3D(b.X, 0, b.Z);
                if ((a - b).LengthSquared > 2.5e-5)
                {
                    continue;
                }
                Vector3D av = Rotate(new Vector3D(-ls, 0, 0), leftPose.Yaw), bv = Rotate(new Vector3D(-rs, 0, 0), rightPose.Yaw);
                if (Math.Abs(av.X * bv.Z - av.Z * bv.X) < 1e-4 && Vector3D.Dot(av, bv) > 0)
                {
                    continue;
                }
                return true;
            }
        }
        return false;
    }
}
