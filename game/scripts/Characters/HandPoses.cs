using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// Finger poses for holding things (first person; third person takes its fingers from the clips). The fingers of a
/// fist and of a pistol grip are wrapped round the handle: each joint is bent just enough for the next one to lie on a
/// circle round the handle's axis (its radius plus a finger's half thickness), so the fingers close on the handle
/// without passing through it. The thumb, the trigger finger and the support hand use fixed curls.
/// </summary>
public static class HandPoses
{
    public enum Grip
    {
        Relaxed,
        Fist,
        Pistol,
        Support,
    }

    /// <summary>Where a handle sits in a closed fist: this far from the wrist towards the knuckles (a fraction of the
    /// hand's length) and this far out from the palm's bones (metres). <see cref="HandFrame.Measure"/> takes them.</summary>
    public const float FistAlong = 0.7f, FistLift = 0.03f;

    // Handle radius (metres) each wrapped grip closes on.
    private static readonly Dictionary<Grip, float> HandleRadius = new() { [Grip.Fist] = 0.018f, [Grip.Pistol] = 0.019f };

    private const float FingerHalfThickness = 0.0085f;

    // Fixed curls of the three joints of each finger (degrees): fingers, index, thumb.
    private static readonly Dictionary<Grip, (float[] Finger, float[] Index, float[] Thumb)> Curls = new()
    {
        [Grip.Relaxed] = (new[] { 12f, 15f, 10f }, new[] { 8f, 10f, 8f }, new[] { 5f, 8f, 5f }),
        [Grip.Fist] = (new[] { 62f, 78f, 48f }, new[] { 58f, 72f, 44f }, new[] { 30f, 40f, 30f }),
        [Grip.Pistol] = (new[] { 66f, 80f, 50f }, new[] { 22f, 28f, 12f }, new[] { 28f, 26f, 22f }),
        [Grip.Support] = (new[] { 40f, 52f, 34f }, new[] { 36f, 46f, 30f }, new[] { 12f, 16f, 10f }),
    };

    private static readonly string[] Fingers = { "index", "middle", "ring", "pinky" };

    // Every character skeleton comes from the same template, so one table per hand serves them all.
    private static readonly Dictionary<bool, List<(int Bone, Quaternion RestRotation, Vector3 Axis, int Joint, int Kind, int Finger)>> Rigs = new();
    private static readonly Dictionary<(bool Right, Grip Grip), float[][]> Wraps = new();

    /// <summary>Sets the finger bones of one hand to a grip (from their rest pose).</summary>
    public static void Apply(Skeleton3D s, Grip grip, bool right, float weight = 1f)
    {
        (float[] finger, float[] index, float[] thumb) = Curls[grip];
        float[][]? wrap = HandleRadius.ContainsKey(grip) ? Wrap(s, grip, right) : null;
        foreach ((int bone, Quaternion rest, Vector3 axis, int joint, int kind, int f) in Rig(s, right))
        {
            float degrees = kind switch
            {
                0 => thumb[joint],
                1 when grip == Grip.Pistol => index[joint],
                _ when wrap != null => wrap[f][joint],
                1 => index[joint],
                _ => finger[joint],
            } * weight;
            s.SetBonePoseRotation(bone, rest * new Quaternion(axis, Mathf.DegToRad(degrees)));
        }
    }

    /// <summary>For each finger (index … pinky), the bend of its three joints (degrees) that wraps it round the grip's
    /// handle, which lies across the palm where <see cref="FistAlong"/> and <see cref="FistLift"/> put it.</summary>
    private static float[][] Wrap(Skeleton3D s, Grip grip, bool right)
    {
        if (Wraps.TryGetValue((right, grip), out float[][]? cached))
        {
            return cached;
        }
        string side = right ? "_r" : "_l";
        (Vector3 wrist, Vector3 f, _, Vector3 palm) = Frame(s, side);
        float length = (s.GetBoneGlobalRest(s.FindBone("middle_01" + side)).Origin - wrist).Length();
        // The bending plane: x along the hand, y towards the palm. The handle's axis runs across it.
        Vector2 Flat(Vector3 p) => new((p - wrist).Dot(f), (p - wrist).Dot(palm));
        Vector2 centre = new(length * FistAlong, FistLift);
        float radius = HandleRadius[grip] + FingerHalfThickness;
        var result = new float[Fingers.Length][];
        for (int i = 0; i < Fingers.Length; i++)
        {
            var joints = new Vector2[4];
            Transform3D last = Transform3D.Identity;
            for (int k = 0; k < 3; k++)
            {
                last = s.GetBoneGlobalRest(s.FindBone($"{Fingers[i]}_0{k + 1}{side}"));
                joints[k] = Flat(last.Origin);
            }
            // The last bone has no child: its tip lies along its own axis, a little shorter than the middle bone.
            Vector2 tipDir = Flat(last.Origin + last.Basis.Y.Normalized()) - joints[2];
            joints[3] = joints[2] + tipDir.Normalized() * (joints[2] - joints[1]).Length() * 0.85f;

            Vector2 at = joints[0];
            var bend = new float[3];
            float turned = 0f;
            for (int k = 0; k < 3; k++)
            {
                float segment = (joints[k + 1] - joints[k]).Length();
                float rest = (joints[k + 1] - joints[k]).Angle();
                Vector2 next = NextOnCircle(at, segment, centre, radius, rest + turned);
                float total = Mathf.Wrap((next - at).Angle() - rest, -MathF.PI, MathF.PI);
                bend[k] = Math.Clamp(Mathf.RadToDeg(total - turned), -10f, 115f);
                turned += Mathf.DegToRad(bend[k]);
                at = next;
            }
            result[i] = bend;
        }
        Wraps[(right, grip)] = result;
        return result;
    }

    /// <summary>The next joint: `segment` away from `from` and on the circle. The bone, pointing at `heading` before
    /// it bends, curls towards the palm (anticlockwise in the bending plane) until it first touches the circle.</summary>
    private static Vector2 NextOnCircle(Vector2 from, float segment, Vector2 centre, float radius, float heading)
    {
        Vector2 d = centre - from;
        float dist = d.Length();
        if (dist < 1e-6f)
        {
            return from + new Vector2(segment, 0f);
        }
        // Circle–circle intersection (centre `from`, radius `segment`; centre `centre`, radius `radius`).
        float a = (segment * segment - radius * radius + dist * dist) / (2f * dist);
        float h2 = segment * segment - a * a;
        if (h2 < 0f)
        {
            // Out of reach (or too long to wrap): point straight at the handle's axis, or across it.
            return from + d.Normalized() * segment;
        }
        float h = MathF.Sqrt(h2);
        Vector2 mid = from + d * (a / dist);
        Vector2 perp = new Vector2(-d.Y, d.X) / dist;
        Vector2 p1 = mid + perp * h, p2 = mid - perp * h;
        float Sweep(Vector2 p) => Mathf.Wrap((p - from).Angle() - heading, -0.35f, MathF.Tau - 0.35f);
        return Sweep(p1) <= Sweep(p2) ? p1 : p2;
    }

    private static (Vector3 Wrist, Vector3 F, Vector3 T, Vector3 Palm) Frame(Skeleton3D s, string side)
    {
        Vector3 wrist = s.GetBoneGlobalRest(s.FindBone("hand" + side)).Origin;
        Vector3 middle = s.GetBoneGlobalRest(s.FindBone("middle_01" + side)).Origin;
        Vector3 index = s.GetBoneGlobalRest(s.FindBone("index_01" + side)).Origin;
        Vector3 pinky = s.GetBoneGlobalRest(s.FindBone("pinky_01" + side)).Origin;
        Vector3 f = (middle - wrist).Normalized();
        Vector3 t = index - pinky;
        t = (t - f * t.Dot(f)).Normalized();
        Vector3 z = f.Cross(t).Normalized();
        return (wrist, f, t, side == "_r" ? -z : z);
    }

    /// <summary>For each finger bone: its rest rotation and the local axis that bends it towards the palm.</summary>
    private static List<(int, Quaternion, Vector3, int, int, int)> Rig(Skeleton3D s, bool right)
    {
        if (Rigs.TryGetValue(right, out var rig))
        {
            return rig;
        }
        rig = new List<(int, Quaternion, Vector3, int, int, int)>();
        string side = right ? "_r" : "_l";
        (_, _, Vector3 t, Vector3 palm) = Frame(s, side);
        string[] names = { "thumb", "index", "middle", "ring", "pinky" };
        for (int n = 0; n < names.Length; n++)
        {
            string name = names[n];
            for (int k = 1; k <= 3; k++)
            {
                int bone = s.FindBone($"{name}_0{k}{side}");
                if (bone < 0)
                {
                    continue;
                }
                Transform3D g = s.GetBoneGlobalRest(bone);
                int child = k < 3 ? s.FindBone($"{name}_0{k + 1}{side}") : -1;
                Vector3 dir = child >= 0 ? (s.GetBoneGlobalRest(child).Origin - g.Origin).Normalized() : g.Basis.Y.Normalized();
                // The thumb closes across the palm, towards the little finger's side.
                Vector3 towards = name == "thumb" ? (palm * 0.6f - t * 0.8f).Normalized() : palm;
                Vector3 axis = dir.Cross(towards);
                if (axis.LengthSquared() < 1e-8f)
                {
                    continue;
                }
                Vector3 local = (g.Basis.Inverse() * axis.Normalized()).Normalized();
                rig.Add((bone, s.GetBoneRest(bone).Basis.GetRotationQuaternion(), local, k - 1,
                    name == "thumb" ? 0 : name == "index" ? 1 : 2, n - 1));
            }
        }
        Rigs[right] = rig;
        return rig;
    }
}
