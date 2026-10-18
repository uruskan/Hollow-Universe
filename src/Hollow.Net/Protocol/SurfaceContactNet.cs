using System;
using System.Numerics;
using Hollow.Simulation.Targets;

namespace Hollow.Net.Protocol;

public partial struct SurfaceContactNet
{
    // Twelve-bit [-1,1] quantization cannot encode exact zero: all-zero input decodes to about 0.000244 per axis.
    private const float MinimumNormalLengthSquared = 1e-6f;
    public static SurfaceContactNet Of(in SurfaceContact contact) => new()
    {
        Target = new TargetNet { Family = contact.Target.Family, Id = contact.Target.Id, Part = contact.Target.Part },
        Surface = contact.Surface,
        NormalX = contact.Normal.X, NormalY = contact.Normal.Y, NormalZ = contact.Normal.Z,
        Anchor = new TargetNet { Family = contact.Anchor.Family, Id = contact.Anchor.Id, Part = contact.Anchor.Part },
        LocalX = contact.LocalPoint.X, LocalY = contact.LocalPoint.Y, LocalZ = contact.LocalPoint.Z,
        LocalNormalX = contact.LocalNormal.X, LocalNormalY = contact.LocalNormal.Y, LocalNormalZ = contact.LocalNormal.Z,
    };

    public readonly SurfaceContact At(Vector3 point)
    {
        if (!Finite(point)) return default;
        TargetRef target = TargetOf(Target);
        Vector3 normal = Unit(new Vector3(NormalX, NormalY, NormalZ));
        if (target.IsNone) return new SurfaceContact(TargetRef.None, 0, point, normal);
        TargetRef anchor = TargetOf(Anchor);
        Vector3 local = new(LocalX, LocalY, LocalZ);
        Vector3 localNormal = new(LocalNormalX, LocalNormalY, LocalNormalZ);
        if (anchor != target || anchor.Family == TargetFamily.Ground || !Finite(local) || !float.IsFinite(local.LengthSquared()) ||
            !Finite(localNormal) || !float.IsFinite(localNormal.LengthSquared()) || localNormal.LengthSquared() < MinimumNormalLengthSquared)
            anchor = TargetRef.None;
        return new SurfaceContact(target, Surface, point, normal, anchor, anchor.IsNone ? default : local, anchor.IsNone ? default : Unit(localNormal));
    }

    private static TargetRef TargetOf(TargetNet target) => Enum.IsDefined(target.Family)
        ? new TargetRef(target.Family, target.Id, target.Part) : TargetRef.None;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static Vector3 Unit(Vector3 v) => Finite(v) && float.IsFinite(v.LengthSquared()) && v.LengthSquared() >= MinimumNormalLengthSquared ? Vector3.Normalize(v) : Vector3.UnitY;
}
