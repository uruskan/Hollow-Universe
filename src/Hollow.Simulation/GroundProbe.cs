using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>
/// The surface's own ground (its heights, not the collision tiles' triangles), asked the same way by the zone and by a
/// client: the ground under a point, how far a point is above it, its normal and slope, and where a ray first goes under
/// it. One copy of these for both sides (the client's placement had its own).
/// </summary>
public readonly struct GroundProbe(ISurface surface, ZoneFrame frame)
{
    public ISurface Surface { get; } = surface;

    public ZoneFrame Frame { get; } = frame;

    /// <summary>The ground under a point of the zone's tangent plane, in the zone frame.</summary>
    public Vector3 At(float x, float z)
    {
        Vector3D direction = Frame.ToBody(x, 0, z).Normalized;
        return Frame.ToLocal(direction * (Surface.Radius + Surface.HeightAt(direction)));
    }

    /// <summary>The point on the ground below (or above) a point.</summary>
    public Vector3 Settle(Vector3 point) => At(point.X, point.Z);

    /// <summary>How far a point is above the ground, in metres (negative below).</summary>
    public float HeightAbove(Vector3 point)
    {
        Vector3D body = Frame.ToBody(point);
        double r = body.Length;
        return (float)(r - (Surface.Radius + Surface.HeightAt(body / r)));
    }

    /// <summary>The ground's up and its slope (radians) under a point, by finite differences.</summary>
    public (Vector3 Normal, float Slope) Normal(Vector3 point)
    {
        const float d = 0.35f;
        Vector3 a = At(point.X - d, point.Z), b = At(point.X + d, point.Z);
        Vector3 c = At(point.X, point.Z - d), e = At(point.X, point.Z + d);
        Vector3 normal = Vector3.Normalize(Vector3.Cross(e - c, b - a));
        Vector3 up = Frame.UpAt(point);
        if (Vector3.Dot(normal, up) < 0)
        {
            normal = -normal;
        }
        float cos = Math.Clamp(Vector3.Dot(normal, up), -1, 1);
        return (normal, MathF.Acos(cos));
    }

    /// <summary>Where a ray first goes below the ground (within <paramref name="length"/>), or −1.</summary>
    public float Crossing(Vector3 origin, Vector3 direction, float length)
    {
        float previous = HeightAbove(origin);
        if (previous < 0)
        {
            return -1; // starting under the ground: the surface doesn't block (it would block everything)
        }
        int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.5f));
        float t0 = 0;
        for (int i = 1; i <= steps; i++)
        {
            float t1 = length * i / steps;
            if (HeightAbove(origin + direction * t1) < 0)
            {
                // Bisect between the last point above and this one.
                float lo = t0, hi = t1;
                for (int k = 0; k < 8; k++)
                {
                    float mid = (lo + hi) / 2;
                    if (HeightAbove(origin + direction * mid) < 0)
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

    /// <summary>Whether a point lies under the sea's surface, or less than <paramref name="clearance"/> above it.</summary>
    public bool InWater(Vector3 point, float clearance = 0.05f) =>
        Surface.SeaRadius > 0 && Frame.ToBody(point).Length < Surface.SeaRadius + clearance;
}
