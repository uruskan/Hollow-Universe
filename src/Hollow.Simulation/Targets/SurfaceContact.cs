using System.Numerics;

namespace Hollow.Simulation.Targets;

/// <summary>Immutable authoritative collision evidence, captured before damage can remove its target.</summary>
public readonly record struct SurfaceContact(TargetRef Target, byte Surface, Vector3 Point, Vector3 Normal,
    TargetRef Anchor = default, Vector3 LocalPoint = default, Vector3 LocalNormal = default)
{
    public bool HasSurface => Surface != 0;
    public bool Anchored => !Anchor.IsNone;
    public Vector3 PointAt(in TargetPose pose) => Anchored ? pose.ToZone(LocalPoint) : Point;
    public Vector3 NormalAt(in TargetPose pose) => Anchored ? Vector3.Transform(LocalNormal, pose.Rotation) : Normal;

    public static SurfaceContact At(TargetRef target, byte surface, Vector3 point, Vector3 normal, TargetPose? pose = null)
    {
        normal = float.IsFinite(normal.LengthSquared()) && normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
        return pose is { } p
            ? new SurfaceContact(target, surface, point, normal, target, p.ToLocal(point), Vector3.Transform(normal, Quaternion.Inverse(p.Rotation)))
            : new SurfaceContact(target, surface, point, normal);
    }
}
