using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Movement;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>
/// The surface's own ground (its heights, not the collision tiles' triangles), asked the same way by the zone and by a
/// client: the ground under a point, how far a point is above it, its normal and slope, and where a ray first goes under
/// it. One copy of these for both sides (the client's placement had its own). Everything is radial: the ground under a
/// point lies along its direction from the world's centre (docs/WHOLE_WORLD.md §6.1).
/// </summary>
public readonly struct GroundProbe(ISurface surface, ZoneFrame frame)
{
    public ISurface Surface { get; } = surface;

    public ZoneFrame Frame { get; } = frame;

    /// <summary>The point on the ground below (or above) a point: along its direction from the centre.</summary>
    public Vector3 Settle(Vector3 point)
    {
        Vector3D body = Frame.ToBody(point);
        double r = body.Length;
        return r > 0 ? On(body / r) : point;
    }

    /// <summary>The ground at a unit body-frame direction, in the zone frame.</summary>
    public Vector3 On(Vector3D direction) => Frame.ToLocal(direction * (Surface.Radius + Surface.HeightAt(direction)));

    /// <summary>
    /// The ground <paramref name="east"/> metres east and <paramref name="south"/> metres south of a point, measured
    /// along the ground (a turn about the centre by that arc), so layouts keep their sizes on a small world: rings,
    /// plots, a test's places. At a pole east is the fixed fallback <see cref="Facing.Compass"/> gives.
    /// </summary>
    public Vector3 Offset(Vector3 from, float east, float south)
    {
        Vector3 up = Frame.UpAt(from);
        (Vector3 north, Vector3 e) = Facing.Compass(up);
        Vector3 along = e * east - north * south;
        float distance = along.Length();
        if (distance < 1e-6f)
        {
            return Settle(from);
        }
        double angle = distance / Surface.Radius, cos = DMath.Cos(angle), sin = DMath.Sin(angle);
        Vector3 t = along / distance;
        // The turned direction in the frame's axes, then in the body's (the frame's own rotation, without its origin).
        Vector3D local = new(up.X * cos + t.X * sin, up.Y * cos + t.Y * sin, up.Z * cos + t.Z * sin);
        return On((Frame.ToBody(local.X, local.Y, local.Z) - Frame.Origin).Normalized);
    }

    /// <summary>
    /// How far a point is east and south of another, along the ground (<see cref="Offset"/>'s inverse): the arc between
    /// their directions from the centre, split by the first point's compass.
    /// </summary>
    public (float East, float South) OffsetOf(Vector3 from, Vector3 point)
    {
        Vector3 up = Frame.UpAt(from), to = Frame.UpAt(point);
        (Vector3 north, Vector3 east) = Facing.Compass(up);
        Vector3 along = to - up * Vector3.Dot(to, up);
        float length = along.Length();
        if (length < 1e-9f)
        {
            return (0, 0);
        }
        float arc = (float)(DMath.Atan2(length, Vector3.Dot(to, up)) * Surface.Radius);
        along /= length;
        return (Vector3.Dot(along, east) * arc, -Vector3.Dot(along, north) * arc);
    }

    /// <summary>How far a point is above the ground, in metres (negative below).</summary>
    public float HeightAbove(Vector3 point)
    {
        Vector3D body = Frame.ToBody(point);
        double r = body.Length;
        return r > 0 ? (float)(r - (Surface.Radius + Surface.HeightAt(body / r))) : float.NegativeInfinity;
    }

    /// <summary>The ground's up and its slope (radians) under a point, by finite differences along the local ground.</summary>
    public (Vector3 Normal, float Slope) Normal(Vector3 point)
    {
        const float d = 0.35f;
        Vector3 up = Frame.UpAt(point);
        (Vector3 north, Vector3 east) = Facing.Compass(up);
        Vector3 a = Settle(point - east * d), b = Settle(point + east * d);
        Vector3 c = Settle(point - north * d), e = Settle(point + north * d);
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, e - c));
        if (Vector3.Dot(normal, up) < 0)
        {
            normal = -normal;
        }
        float cos = Math.Clamp(Vector3.Dot(normal, up), -1, 1);
        return (normal, MathF.Acos(cos));
    }

    /// <summary>Where a ray first goes below the ground (within <paramref name="length"/>), or −1.</summary>
    public float Crossing(Vector3 origin, Vector3 direction, float length) => March(origin, direction, length, water: false);

    /// <summary>
    /// Where a ray first goes below the water's surface (the sea, a lake, a stream; within <paramref name="length"/>), or
    /// −1: what a boat is put on (docs/MOTORBOAT.md §4).
    /// </summary>
    public float WaterCrossing(Vector3 origin, Vector3 direction, float length) => March(origin, direction, length, water: true);

    /// <summary>How far a point is above the water's surface (negative: under it), +∞ where no water lies.</summary>
    public float HeightAboveWater(Vector3 point)
    {
        Core.Vector3D body = Frame.ToBody(point);
        double length = body.Length;
        double level = length > 0 ? Surface.WaterLevelAt(body / length) : double.NegativeInfinity;
        return double.IsFinite(level) ? (float)(length - (Surface.Radius + level)) : float.PositiveInfinity;
    }

    private float Height(Vector3 point, bool water) => water ? HeightAboveWater(point) : HeightAbove(point);

    // Half-metre steps along the ray until a point is below the surface, then halving between it and the last above.
    private float March(Vector3 origin, Vector3 direction, float length, bool water)
    {
        float previous = Height(origin, water);
        if (previous < 0)
        {
            return -1; // starting under the ground: the surface doesn't block (it would block everything)
        }
        int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.5f));
        float t0 = 0;
        for (int i = 1; i <= steps; i++)
        {
            float t1 = length * i / steps;
            if (Height(origin + direction * t1, water) < 0)
            {
                // Bisect between the last point above and this one.
                float lo = t0, hi = t1;
                for (int k = 0; k < 8; k++)
                {
                    float mid = (lo + hi) / 2;
                    if (Height(origin + direction * mid, water) < 0)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }
                return hi;
            }
            t0 = t1;
        }
        return -1;
    }

    /// <summary>
    /// Whether a point lies under the water's surface (the sea's; on the walking-scale ground a lake's or a stream's too,
    /// <see cref="ISurface.WaterLevelAt"/>), or less than <paramref name="clearance"/> above it.
    /// </summary>
    public bool InWater(Vector3 point, float clearance = 0.05f)
    {
        Core.Vector3D body = Frame.ToBody(point);
        double length = body.Length;
        if (!(length > 0))
        {
            return false;
        }
        double level = Surface.WaterLevelAt(body / length);
        return level > double.NegativeInfinity && length < Surface.Radius + level + clearance;
    }
}
