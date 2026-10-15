using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Capabilities;

namespace Hollow.Simulation.Targets;

/// <summary>Optional health advertised by a target adapter. Absence is distinct from a destroyed target.</summary>
public readonly record struct TargetVitality(double Current, double Maximum)
{
    public bool Damaged => double.IsFinite(Current) && double.IsFinite(Maximum) && Current > 0 && Maximum > 0 && Current < Maximum;
    public float Fraction => Maximum > 0 && double.IsFinite(Current) && double.IsFinite(Maximum)
        ? (float)Math.Clamp(Current / Maximum, 0, 1) : 0;
}

public readonly record struct AimedVitality(TargetRef Target, string Name, TargetVitality Vitality, float Distance);

/// <summary>A nearby damaged object at the crosshair. A healthy object or ground in front hides anything behind it.</summary>
public static class TargetHealthQuery
{
    public const float Reach = 8;

    public static AimedVitality? Find(IReadOnlyList<ITargetView> families, Vector3 eye, Vector3 direction, in Viewer viewer)
    {
        float length = direction.LengthSquared();
        if (!float.IsFinite(length) || length < 1e-8f) return null;
        // A zero-radius attack ray meets solid geometry rather than an open doorway's interaction plane.
        AimHit hit = Targeting.Aim(families, new AimRay(eye, Vector3.Normalize(direction), Reach, viewer.Id.Value) { AttackRadius = 0 });
        if (!hit.Hit) return null;
        foreach (ITargetView family in families)
            if (family.Family == hit.Target.Family && family.TryFacts(hit.Target, viewer, out TargetFacts facts) &&
                facts.Vitality is { Damaged: true } vitality)
                return new AimedVitality(hit.Target, facts.Name, vitality, hit.Distance);
        return null;
    }
}
