using System;
using System.Numerics;

namespace Hollow.Simulation.Combat;

/// <summary>
/// Ray tests for hits (melee sweeps, arrows, later bullets): against spheres, capsules and oriented boxes, all in the
/// zone frame. A ray is an origin, a unit direction and a length; the result is the distance along it to the first
/// contact, or a negative number for a miss. A ray starting inside a shape hits it at 0.
/// </summary>
public static class Geometry
{
    public static float RaySphere(Vector3 origin, Vector3 direction, float length, Vector3 centre, float radius)
    {
        Vector3 oc = origin - centre;
        float c = Vector3.Dot(oc, oc) - radius * radius;
        if (c <= 0)
        {
            return 0;
        }
        float b = Vector3.Dot(oc, direction);
        if (b > 0)
        {
            return -1;
        }
        float h = b * b - c;
        if (h < 0)
        {
            return -1;
        }
        float t = -b - MathF.Sqrt(h);
        return t <= length ? Math.Max(0, t) : -1;
    }

    /// <summary>A capsule: the segment <paramref name="a"/>–<paramref name="b"/> grown by <paramref name="radius"/>.</summary>
    public static float RayCapsule(Vector3 origin, Vector3 direction, float length, Vector3 a, Vector3 b, float radius)
    {
        if (DistanceSquaredToSegment(origin, a, b) <= radius * radius)
        {
            return 0;
        }
        Vector3 ba = b - a, oa = origin - a;
        float baba = Vector3.Dot(ba, ba);
        if (baba < 1e-12f)
        {
            return RaySphere(origin, direction, length, a, radius);
        }
        float bard = Vector3.Dot(ba, direction);
        float baoa = Vector3.Dot(ba, oa);
        float rdoa = Vector3.Dot(direction, oa);
        float oaoa = Vector3.Dot(oa, oa);
        float qa = baba - bard * bard;
        float t = -1;
        if (qa > 1e-9f)
        {
            float qb = baba * rdoa - baoa * bard;
            float qc = baba * oaoa - baoa * baoa - radius * radius * baba;
            float h = qb * qb - qa * qc;
            if (h < 0)
            {
                return -1;
            }
            float tc = (-qb - MathF.Sqrt(h)) / qa;
            float y = baoa + tc * bard;
            if (y > 0 && y < baba)
            {
                t = tc;
            }
        }
        if (t < 0)
        {
            // The caps: the nearer of the two spheres the ray meets.
            float ta = RaySphere(origin, direction, length, a, radius);
            float tb = RaySphere(origin, direction, length, b, radius);
            t = ta < 0 ? tb : tb < 0 ? ta : Math.Min(ta, tb);
            return t;
        }
        return t <= length && t >= 0 ? t : -1;
    }

    /// <summary>
    /// An oriented box: <paramref name="centre"/>, half extents along its own axes, which are <paramref name="right"/>,
    /// <paramref name="up"/> and <paramref name="forward"/> (unit, perpendicular).
    /// </summary>
    public static float RayBox(Vector3 origin, Vector3 direction, float length, Vector3 centre, Vector3 halfExtents,
        Vector3 right, Vector3 up, Vector3 forward)
    {
        Vector3 d = origin - centre;
        Span<float> o = stackalloc float[] { Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, forward) };
        Span<float> v = stackalloc float[] { Vector3.Dot(direction, right), Vector3.Dot(direction, up), Vector3.Dot(direction, forward) };
        Span<float> h = stackalloc float[] { halfExtents.X, halfExtents.Y, halfExtents.Z };
        float near = 0, far = length;
        for (int i = 0; i < 3; i++)
        {
            if (MathF.Abs(v[i]) < 1e-9f)
            {
                if (o[i] < -h[i] || o[i] > h[i])
                {
                    return -1;
                }
                continue;
            }
            float t1 = (-h[i] - o[i]) / v[i], t2 = (h[i] - o[i]) / v[i];
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }
            near = Math.Max(near, t1);
            far = Math.Min(far, t2);
            if (near > far)
            {
                return -1;
            }
        }
        return near;
    }

    public static float DistanceSquaredToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared < 1e-12f ? 0 : Math.Clamp(Vector3.Dot(p - a, ab) / lengthSquared, 0, 1);
        return Vector3.DistanceSquared(p, a + ab * t);
    }

    /// <summary>
    /// The point of a box standing on <paramref name="basePoint"/> (its bottom's middle) along <paramref name="up"/>, turned by
    /// <paramref name="yaw"/>, nearest to a point (the point itself when inside): a deployable's bounds, on the server and the
    /// client alike.
    /// </summary>
    public static Vector3 NearestOnBox(Vector3 basePoint, Vector3 up, float yaw, Vector3 half, Vector3 point)
    {
        (Vector3 right, Vector3 forward) = Axes(up, yaw);
        Vector3 centre = basePoint + up * half.Y;
        Vector3 local = point - centre;
        float x = Math.Clamp(Vector3.Dot(local, right), -half.X, half.X);
        float y = Math.Clamp(Vector3.Dot(local, up), -half.Y, half.Y);
        float z = Math.Clamp(Vector3.Dot(local, forward), -half.Z, half.Z);
        return centre + right * x + up * y + forward * z;
    }

    /// <summary>The axes of something standing along <paramref name="up"/> and turned by <paramref name="yaw"/> about it.</summary>
    public static (Vector3 Right, Vector3 Forward) Axes(Vector3 up, float yaw)
    {
        (Vector3 forward, Vector3 right) = Movement.CharacterMotor.HorizontalBasis(yaw, up);
        return (right, forward);
    }

    /// <summary>
    /// The turn a thing standing along <paramref name="up"/> (a unit vector) at <paramref name="yaw"/> is drawn with, as
    /// the client draws deployables and props: X to its right, Y along up, −Z along its forward.
    /// </summary>
    public static Quaternion Upright(Vector3 up, float yaw)
    {
        (Vector3 forward, Vector3 right) = Movement.CharacterMotor.HorizontalBasis(yaw, up);
        var rows = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rows));
    }

    /// <summary>Whether two boxes standing on the same "up" overlap, by separating axes in the horizontal plane and along up.</summary>
    public static bool BoxesOverlap(Vector3 centreA, Vector3 halfA, float yawA, Vector3 centreB, Vector3 halfB, float yawB, Vector3 up)
    {
        Vector3 d = centreB - centreA;
        if (MathF.Abs(Vector3.Dot(d, up)) > halfA.Y + halfB.Y)
        {
            return false;
        }
        (Vector3 ra, Vector3 fa) = Axes(up, yawA);
        (Vector3 rb, Vector3 fb) = Axes(up, yawB);
        foreach (Vector3 axis in new[] { ra, fa, rb, fb })
        {
            float projectedA = halfA.X * MathF.Abs(Vector3.Dot(ra, axis)) + halfA.Z * MathF.Abs(Vector3.Dot(fa, axis));
            float projectedB = halfB.X * MathF.Abs(Vector3.Dot(rb, axis)) + halfB.Z * MathF.Abs(Vector3.Dot(fb, axis));
            if (MathF.Abs(Vector3.Dot(d, axis)) > projectedA + projectedB)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether a box (standing on <paramref name="up"/>) and a vertical cylinder overlap, near enough.</summary>
    public static bool BoxCylinderOverlap(Vector3 centre, Vector3 half, float yaw, Vector3 baseOfCylinder, float radius, float height, Vector3 up)
    {
        Vector3 d = baseOfCylinder - centre;
        float along = Vector3.Dot(d, up);
        if (along > half.Y || along + height < -half.Y)
        {
            return false;
        }
        (Vector3 right, Vector3 forward) = Axes(up, yaw);
        float x = Math.Clamp(Vector3.Dot(d, right), -half.X, half.X);
        float z = Math.Clamp(Vector3.Dot(d, forward), -half.Z, half.Z);
        float dx = Vector3.Dot(d, right) - x, dz = Vector3.Dot(d, forward) - z;
        return dx * dx + dz * dz <= radius * radius;
    }
}
