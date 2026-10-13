using System;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Animals;

/// <summary>
/// An animal's hit shapes (GROUND.md §6.9), from its species' size and a pose: a capsule for the body along its length,
/// a sphere for the head (it counts double, as on people), capsules for the legs; a corpse lies on its side. The
/// melee sweep, arrows and bullets are tested against them where the attacker saw the animal.
/// </summary>
public static class AnimalHitboxes
{
    /// <summary>
    /// An animal's shapes at a pose, in a fixed order (standing: body, head, front right, front left, hind right, hind
    /// left leg; lying: body, head, legs): how many went into <paramref name="into"/>.
    /// </summary>
    public static int Shapes(AnimalSpecies s, in HitPose pose, Span<HitShape> into)
    {
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(pose.Yaw, pose.Up);
        Vector3 up = pose.Up, f = pose.Feet;
        float l = s.Length, h = s.Height, rad = s.Radius;
        if (pose.Pose == Pose.Lying)
        {
            into[0] = new HitShape(f + up * (rad * 0.85f) - forward * (l * 0.3f), f + up * (rad * 0.85f) + forward * (l * 0.25f), rad, HitRegion.Body);
            Vector3 head = f + forward * (l * 0.45f) + up * s.HeadRadius;
            into[1] = new HitShape(head, head, s.HeadRadius, HitRegion.Head);
            into[2] = new HitShape(f + up * (rad * 0.4f) + right * (rad * 0.8f), f + up * (rad * 0.3f) + right * (rad * 0.8f + h * 0.45f), rad * 0.22f,
                HitRegion.Limb);
            return 3;
        }
        float bodyHeight = h - rad * 0.9f;
        into[0] = new HitShape(f + up * bodyHeight - forward * (l * 0.3f), f + up * bodyHeight + forward * (l * 0.22f), rad, HitRegion.Body);
        Vector3 top = f + up * s.HeadUp + forward * s.HeadAhead;
        into[1] = new HitShape(top, top, s.HeadRadius, HitRegion.Head);
        float legTop = bodyHeight - rad * 0.5f, legRadius = rad * 0.24f;
        int n = 2;
        foreach (float along in stackalloc[] { l * 0.24f, -l * 0.26f })
        {
            foreach (float side in stackalloc[] { rad * 0.55f, -rad * 0.55f })
            {
                Vector3 hip = f + forward * along + right * side;
                into[n++] = new HitShape(hip + up * legTop, hip + up * (legRadius + 0.02f), legRadius, HitRegion.Limb);
            }
        }
        return n;
    }

    /// <summary>The nearest contact of a thickened ray with an animal at a pose: the distance along it (negative for a miss), the region and the shape.</summary>
    public static float Raycast(AnimalSpecies s, in HitPose pose, Vector3 origin, Vector3 direction, float length, float thickness, out HitRegion region,
        out int shape)
    {
        Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
        return Hitboxes.Nearest(shapes[..Shapes(s, pose, shapes)], origin, direction, length, thickness, out region, out shape);
    }

    public static float Raycast(AnimalSpecies s, in HitPose pose, Vector3 origin, Vector3 direction, float length, float thickness, out HitRegion region) =>
        Raycast(s, pose, origin, direction, length, thickness, out region, out _);

    /// <summary>The middle of an animal's body at a pose (what blasts and bites aim at).</summary>
    public static Vector3 Middle(AnimalSpecies s, in HitPose pose) =>
        pose.Pose == Pose.Lying ? pose.Feet + pose.Up * s.Radius : pose.Feet + pose.Up * (s.Height - s.Radius * 0.9f);

    /// <summary>Where its head is at a pose.</summary>
    public static Vector3 Head(AnimalSpecies s, in HitPose pose)
    {
        (Vector3 forward, _) = CharacterMotor.HorizontalBasis(pose.Yaw, pose.Up);
        return pose.Pose == Pose.Lying
            ? pose.Feet + forward * (s.Length * 0.45f) + pose.Up * s.HeadRadius
            : pose.Feet + pose.Up * s.HeadUp + forward * s.HeadAhead;
    }
}
