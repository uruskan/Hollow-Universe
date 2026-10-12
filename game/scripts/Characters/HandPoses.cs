using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// Finger poses for holding things, made by curling each finger joint towards the palm from the rest pose: a fist
/// round a handle, a pistol grip (index finger along the trigger guard) and a support hand under a barrel.
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

    // Curl of the three joints of each finger (degrees), per grip; the thumb has its own.
    private static readonly Dictionary<Grip, (float[] Finger, float[] Index, float[] Thumb)> Curls = new()
    {
        [Grip.Relaxed] = (new[] { 12f, 15f, 10f }, new[] { 8f, 10f, 8f }, new[] { 5f, 8f, 5f }),
        [Grip.Fist] = (new[] { 62f, 78f, 48f }, new[] { 58f, 72f, 44f }, new[] { 25f, 30f, 28f }),
        [Grip.Pistol] = (new[] { 66f, 80f, 50f }, new[] { 22f, 28f, 12f }, new[] { 28f, 26f, 22f }),
        [Grip.Support] = (new[] { 40f, 52f, 34f }, new[] { 36f, 46f, 30f }, new[] { 12f, 16f, 10f }),
    };

    // Every character skeleton comes from the same template, so one table per hand serves them all.
    private static readonly Dictionary<bool, List<(int Bone, Quaternion RestRotation, Vector3 Axis, int Joint, int Kind)>> Rigs = new();

    /// <summary>Sets the finger bones of one hand to a grip (from their rest pose).</summary>
    public static void Apply(Skeleton3D s, Grip grip, bool right, float weight = 1f)
    {
        (float[] finger, float[] index, float[] thumb) = Curls[grip];
        foreach ((int bone, Quaternion rest, Vector3 axis, int joint, int kind) in Rig(s, right))
        {
            float degrees = kind switch { 0 => thumb[joint], 1 => index[joint], _ => finger[joint] } * weight;
            s.SetBonePoseRotation(bone, rest * new Quaternion(axis, Mathf.DegToRad(degrees)));
        }
    }

    /// <summary>For each finger bone: its rest rotation and the local axis that bends it towards the palm.</summary>
    private static List<(int, Quaternion, Vector3, int, int)> Rig(Skeleton3D s, bool right)
    {
        if (Rigs.TryGetValue(right, out var rig))
        {
            return rig;
        }
        rig = new List<(int, Quaternion, Vector3, int, int)>();
        string side = right ? "_r" : "_l";
        Transform3D hand = s.GetBoneGlobalRest(s.FindBone("hand" + side));
        Vector3 wrist = hand.Origin;
        Vector3 middle = s.GetBoneGlobalRest(s.FindBone("middle_01" + side)).Origin;
        Vector3 index = s.GetBoneGlobalRest(s.FindBone("index_01" + side)).Origin;
        Vector3 pinky = s.GetBoneGlobalRest(s.FindBone("pinky_01" + side)).Origin;
        Vector3 f = (middle - wrist).Normalized();
        Vector3 t = index - pinky;
        t = (t - f * t.Dot(f)).Normalized();
        Vector3 z = f.Cross(t).Normalized();
        Vector3 palm = right ? -z : z;
        foreach (string name in new[] { "thumb", "index", "middle", "ring", "pinky" })
        {
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
                    name == "thumb" ? 0 : name == "index" ? 1 : 2));
            }
        }
        Rigs[right] = rig;
        return rig;
    }
}
