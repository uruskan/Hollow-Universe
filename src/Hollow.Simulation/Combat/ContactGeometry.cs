using System;
using System.Numerics;

namespace Hollow.Simulation.Combat;

/// <summary>Normals of the same analytic shapes used by weapon traces.</summary>
public static class ContactGeometry
{
    public static Vector3 CapsuleNormal(Vector3 point, Vector3 a, Vector3 b, Vector3 fallback)
    {
        Vector3 axis = b - a;
        float t = axis.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(point - a, axis) / axis.LengthSquared(), 0, 1) : 0;
        Vector3 normal = point - (a + axis * t);
        return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : fallback;
    }

    public static Vector3 BoxNormal(Vector3 point, Vector3 centre, Vector3 half, Vector3 right, Vector3 up, Vector3 forward)
    {
        Vector3 offset = point - centre;
        float x = Vector3.Dot(offset, right) / half.X, y = Vector3.Dot(offset, up) / half.Y, z = Vector3.Dot(offset, forward) / half.Z;
        return MathF.Abs(x) >= MathF.Abs(y) && MathF.Abs(x) >= MathF.Abs(z) ? right * MathF.Sign(x)
            : MathF.Abs(y) >= MathF.Abs(z) ? up * MathF.Sign(y) : forward * MathF.Sign(z);
    }
}
