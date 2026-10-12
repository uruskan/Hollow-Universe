using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// Finger poses for holding things, taken from single frames of the animation clips: a closed fist round a handle
/// (from the sword idle), a pistol grip, and a support hand (from the pistol idle).
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

    private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };
    private static readonly Dictionary<(Grip, bool), Dictionary<string, Quaternion>> Cache = new();

    /// <summary>Sets the finger bones of one hand to a grip (local rotations from the clip frame).</summary>
    public static void Apply(Skeleton3D s, Grip grip, bool right, float weight = 1f)
    {
        Dictionary<string, Quaternion> pose = Get(grip, right);
        foreach ((string bone, Quaternion q) in pose)
        {
            int i = s.FindBone(bone);
            if (i < 0)
            {
                continue;
            }
            Quaternion now = s.GetBonePoseRotation(i);
            s.SetBonePoseRotation(i, weight >= 1f ? q : now.Slerp(q, weight));
        }
    }

    private static Dictionary<string, Quaternion> Get(Grip grip, bool right)
    {
        if (Cache.TryGetValue((grip, right), out Dictionary<string, Quaternion>? pose))
        {
            return pose;
        }
        (string clip, float time) = grip switch
        {
            Grip.Fist => ("tool_idle", 0.5f),
            Grip.Pistol => ("pistol_idle", 0.5f),
            Grip.Support => ("pistol_idle", 0.5f),
            _ => ("idle", 0.5f),
        };
        pose = new Dictionary<string, Quaternion>();
        Animation animation = CharacterAssets.Library.GetAnimation(clip);
        string side = right ? "_r" : "_l";
        foreach (string finger in FingerNames)
        {
            for (int k = 1; k <= 3; k++)
            {
                string bone = $"{finger}_0{k}{side}";
                int track = animation.FindTrack(new NodePath("Skeleton:" + bone), Animation.TrackType.Rotation3D);
                if (track >= 0)
                {
                    pose[bone] = animation.RotationTrackInterpolate(track, time);
                }
            }
        }
        Cache[(grip, right)] = pose;
        return pose;
    }
}
