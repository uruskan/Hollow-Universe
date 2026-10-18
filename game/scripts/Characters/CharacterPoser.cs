using System;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// Procedural layer on top of the animation clips, run as the skeleton's modifier (GROUND.md §4.8):
/// <list type="bullet">
/// <item>hips turned towards the direction of travel, chest turned back towards the aim (directional movement);</item>
/// <item>view pitch spread over the spine, neck and head, so others see where a player looks;</item>
/// <item>two-bone arm IK onto the grips of a held rifle or a first-person item;</item>
/// <item>foot placement on uneven ground: the hips drop and each leg reaches the ground under its foot.</item>
/// </list>
/// Works in skeleton space, where the character faces +Z and +Y is its up (the view orients the skeleton), so it never
/// assumes a world up.
/// </summary>
public partial class CharacterPoser : SkeletonModifier3D
{
    /// <summary>Hip turn towards the direction of travel, radians, anticlockwise from above.</summary>
    public float LegYaw;

    /// <summary>View pitch, radians, positive up.</summary>
    public float Pitch;

    /// <summary>How much of the pitch the spine takes (the rest goes to neck and head): more when aiming a gun.</summary>
    public float SpinePitchShare = 0.45f;

    /// <summary>
    /// Free look: the neck and head turned further than the view, radians (yaw about the body's up, to the left; pitch
    /// up), on top of everything else; the neck takes two fifths, the head the rest (RustMVP's split).
    /// </summary>
    public float LookYaw;

    public float LookPitch;

    /// <summary>Hand targets in skeleton space with their weights (0 = leave the clip alone).</summary>
    public Transform3D RightHand;
    public Transform3D LeftHand;
    public float RightHandWeight;
    public float LeftHandWeight;

    /// <summary>Foot placement: casts down from each foot (needs a physics world); off when null.</summary>
    public Func<Vector3, Vector3, (bool Hit, Vector3 Point, Vector3 Normal)>? GroundProbe;

    /// <summary>Extra rotation applied to the chest after pitch (recoil, flinches), skeleton space.</summary>
    public Quaternion ChestKick = Quaternion.Identity;

    /// <summary>Where the elbows point while the hands reach their targets (relative to the shoulders, skeleton space).</summary>
    public static readonly Vector3 DefaultRightPole = new(-0.6f, -0.8f, -0.4f), DefaultLeftPole = new(0.6f, -0.8f, -0.4f);

    public Vector3 RightPole = DefaultRightPole;
    public Vector3 LeftPole = DefaultLeftPole;

    /// <summary>The chest turned about the body's up (radians, anticlockwise from above) over the spine, with the neck
    /// and head turning back by as much, so the eyes stay on the aim (a bow's stance: the left shoulder leads).</summary>
    public float TorsoTwist;

    /// <summary>The chest leaned forward (radians) over the spine (a spear's thrust).</summary>
    public float TorsoLean;

    /// <summary>The right hand as the IK left it (skeleton space), for debugging and tests.</summary>
    public Transform3D LastRightHand;

    /// <summary>Last foot placement offsets (metres along up), for debugging and tests.</summary>
    public float FootDropLeft;
    public float FootDropRight;

    private int _pelvis = -1, _spine1, _spine2, _spine3, _neck, _head;
    private int _upperR, _lowerR, _handR, _upperL, _lowerL, _handL;
    private int _thighL, _calfL, _footL, _thighR, _calfR, _footR;
    private float _hipDrop;

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D s = GetSkeleton();
        if (s == null)
        {
            return;
        }
        if (_pelvis < 0)
        {
            Cache(s);
        }
        if (MathF.Abs(LegYaw) > 1e-4f)
        {
            // Hips towards the travel direction; the spine takes it back so the chest faces the aim.
            Turn(s, _pelvis, new Quaternion(Vector3.Up, LegYaw));
            Turn(s, _spine1, new Quaternion(Vector3.Up, -LegYaw * 0.34f));
            Turn(s, _spine2, new Quaternion(Vector3.Up, -LegYaw * 0.33f));
            Turn(s, _spine3, new Quaternion(Vector3.Up, -LegYaw * 0.33f));
        }
        if (MathF.Abs(TorsoTwist) > 1e-4f)
        {
            Turn(s, _spine1, new Quaternion(Vector3.Up, TorsoTwist * 0.25f));
            Turn(s, _spine2, new Quaternion(Vector3.Up, TorsoTwist * 0.35f));
            Turn(s, _spine3, new Quaternion(Vector3.Up, TorsoTwist * 0.4f));
            Turn(s, _neck, new Quaternion(Vector3.Up, -TorsoTwist * 0.45f));
            Turn(s, _head, new Quaternion(Vector3.Up, -TorsoTwist * 0.55f));
        }
        if (MathF.Abs(TorsoLean) > 1e-4f)
        {
            // Leaning forward turns the up (+Y) towards the front (+Z): a rotation about +X (the character's left).
            Turn(s, _spine1, new Quaternion(Vector3.Right, TorsoLean * 0.3f));
            Turn(s, _spine2, new Quaternion(Vector3.Right, TorsoLean * 0.35f));
            Turn(s, _spine3, new Quaternion(Vector3.Right, TorsoLean * 0.35f));
            Turn(s, _head, new Quaternion(Vector3.Right, -TorsoLean * 0.5f));
        }
        if (MathF.Abs(Pitch) > 1e-4f)
        {
            // Looking up turns the forward (+Z) towards +Y: a rotation about −X (the character's right).
            Vector3 right = Vector3.Left;
            float spine = SpinePitchShare;
            Turn(s, _spine1, new Quaternion(right, Pitch * spine * 0.28f));
            Turn(s, _spine2, new Quaternion(right, Pitch * spine * 0.34f));
            Turn(s, _spine3, new Quaternion(right, Pitch * spine * 0.38f));
            Turn(s, _neck, new Quaternion(right, Pitch * (1 - spine) * 0.4f));
            Turn(s, _head, new Quaternion(right, Pitch * (1 - spine) * 0.6f));
        }
        if (MathF.Abs(LookYaw) > 1e-4f || MathF.Abs(LookPitch) > 1e-4f)
        {
            // Free look: the head pitched, then turned about the body's up, so it looks along the turned view.
            Turn(s, _neck, new Quaternion(Vector3.Up, LookYaw * 0.4f) * new Quaternion(Vector3.Left, LookPitch * 0.4f));
            Turn(s, _head, new Quaternion(Vector3.Up, LookYaw * 0.6f) * new Quaternion(Vector3.Left, LookPitch * 0.6f));
        }
        if (!ChestKick.IsEqualApprox(Quaternion.Identity))
        {
            Turn(s, _spine3, ChestKick);
        }
        if (GroundProbe != null)
        {
            PlaceFeet(s, (float)delta);
        }
        if (RightHandWeight > 0.001f)
        {
            ReachArm(s, _upperR, _lowerR, _handR, RightHand, RightHandWeight, RightPole);
            LastRightHand = s.GetBoneGlobalPose(_handR);
        }
        if (LeftHandWeight > 0.001f)
        {
            ReachArm(s, _upperL, _lowerL, _handL, LeftHand, LeftHandWeight, LeftPole);
        }
    }

    private void Cache(Skeleton3D s)
    {
        _pelvis = s.FindBone("pelvis");
        _spine1 = s.FindBone("spine_01");
        _spine2 = s.FindBone("spine_02");
        _spine3 = s.FindBone("spine_03");
        _neck = s.FindBone("neck_01");
        _head = s.FindBone("head");
        _upperR = s.FindBone("upperarm_r");
        _lowerR = s.FindBone("lowerarm_r");
        _handR = s.FindBone("hand_r");
        _upperL = s.FindBone("upperarm_l");
        _lowerL = s.FindBone("lowerarm_l");
        _handL = s.FindBone("hand_l");
        _thighL = s.FindBone("thigh_l");
        _calfL = s.FindBone("calf_l");
        _footL = s.FindBone("foot_l");
        _thighR = s.FindBone("thigh_r");
        _calfR = s.FindBone("calf_r");
        _footR = s.FindBone("foot_r");
    }

    /// <summary>Rotates a bone by <paramref name="extra"/> in skeleton space, about its own head.</summary>
    public static void Turn(Skeleton3D s, int bone, Quaternion extra, float weight = 1f)
    {
        if (weight < 1f)
        {
            extra = Quaternion.Identity.Slerp(extra, weight);
        }
        Basis global = s.GetBoneGlobalPose(bone).Basis;
        SetGlobalBasis(s, bone, new Basis(extra) * global);
    }

    public static void SetGlobalBasis(Skeleton3D s, int bone, Basis wanted)
    {
        int parent = s.GetBoneParent(bone);
        Basis parentBasis = parent >= 0 ? s.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        s.SetBonePoseRotation(bone, (parentBasis.Inverse() * wanted).Orthonormalized().GetRotationQuaternion());
    }

    /// <summary>
    /// Two-bone IK: the hand reaches <paramref name="target"/> (skeleton space) with the elbow bent towards
    /// <paramref name="poleDirection"/> (relative to the shoulder), and takes the target's orientation.
    /// Zero wristTwist preserves the base forearm twist for an additive pose such as item inspection.
    /// </summary>
    public static void ReachArm(Skeleton3D s, int upper, int lower, int hand, Transform3D target, float weight, Vector3 poleDirection, float wristTwist = 0.65f)
    {
        Vector3 shoulder = s.GetBoneGlobalPose(upper).Origin;
        Vector3 elbow = s.GetBoneGlobalPose(lower).Origin;
        Vector3 wrist = s.GetBoneGlobalPose(hand).Origin;
        Vector3 goal = wrist.Lerp(target.Origin, weight);
        SolveTwoBone(s, upper, lower, hand, shoulder, elbow, wrist, goal, shoulder + poleDirection);
        Basis current = s.GetBoneGlobalPose(hand).Basis;
        Quaternion from = current.GetRotationQuaternion();
        Quaternion to = from.Slerp(target.Basis.GetRotationQuaternion(), weight);
        // Most of the wrist's roll goes into the forearm, as a real forearm turns, so the wrist does not twist flat.
        Transform3D fore = s.GetBoneGlobalPose(lower);
        Vector3 axis = (s.GetBoneGlobalPose(hand).Origin - fore.Origin).Normalized();
        Quaternion restRelative = (s.GetBoneGlobalRest(lower).Basis.Inverse() * s.GetBoneGlobalRest(hand).Basis).GetRotationQuaternion();
        Quaternion neutralFore = to * restRelative.Inverse();
        Quaternion delta = neutralFore * fore.Basis.GetRotationQuaternion().Inverse();
        float twist = 2f * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Dot(axis), delta.W);
        Turn(s, lower, new Quaternion(axis, Wrap(twist) * wristTwist));
        SetGlobalBasis(s, hand, new Basis(to));
    }

    private static float Wrap(float a)
    {
        while (a > MathF.PI)
        {
            a -= MathF.Tau;
        }
        while (a < -MathF.PI)
        {
            a += MathF.Tau;
        }
        return a;
    }

    private static void SolveTwoBone(Skeleton3D s, int upper, int lower, int end, Vector3 root, Vector3 mid, Vector3 tip,
        Vector3 goal, Vector3 pole)
    {
        float a = (mid - root).Length();
        float b = (tip - mid).Length();
        Vector3 toGoal = goal - root;
        float d = Math.Clamp(toGoal.Length(), MathF.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        Vector3 dir = toGoal.Normalized();
        Vector3 bend = pole - root;
        bend -= dir * bend.Dot(dir);
        if (bend.LengthSquared() < 1e-8f)
        {
            bend = (mid - root) - dir * (mid - root).Dot(dir);
        }
        bend = bend.Normalized();
        float cos = Math.Clamp((a * a + d * d - b * b) / (2 * a * d), -1f, 1f);
        float sin = MathF.Sqrt(1 - cos * cos);
        Vector3 newMid = root + (dir * cos + bend * sin) * a;
        Turn(s, upper, Arc((mid - root).Normalized(), (newMid - root).Normalized()));
        Vector3 midNow = s.GetBoneGlobalPose(lower).Origin;
        Vector3 tipNow = s.GetBoneGlobalPose(end).Origin;
        Turn(s, lower, Arc((tipNow - midNow).Normalized(), (root + dir * d - midNow).Normalized()));
    }

    /// <summary>The shortest rotation taking one unit vector to another.</summary>
    public static Quaternion Arc(Vector3 from, Vector3 to)
    {
        float dot = Math.Clamp(from.Dot(to), -1f, 1f);
        if (dot > 0.99999f)
        {
            return Quaternion.Identity;
        }
        Vector3 axis = from.Cross(to);
        if (axis.LengthSquared() < 1e-10f)
        {
            axis = from.Cross(Vector3.Right).LengthSquared() > 1e-6f ? from.Cross(Vector3.Right) : from.Cross(Vector3.Up);
        }
        return new Quaternion(axis.Normalized(), MathF.Acos(dot));
    }

    // ---------------------------------------------------------------- feet

    private void PlaceFeet(Skeleton3D s, float delta)
    {
        Transform3D toWorld = s.GlobalTransform;
        Vector3 up = toWorld.Basis.Y.Normalized();
        float dropL = FootOffset(s, toWorld, up, _footL, out Vector3 normalL);
        float dropR = FootOffset(s, toWorld, up, _footR, out Vector3 normalR);
        // The hips go down as far as the lower foot needs (never up), smoothly.
        float wantDrop = Math.Min(0f, Math.Min(dropL, dropR));
        _hipDrop += (wantDrop - _hipDrop) * Math.Min(1f, delta * 12f);
        FootDropLeft = dropL;
        FootDropRight = dropR;
        if (MathF.Abs(_hipDrop) > 1e-4f)
        {
            Transform3D pelvis = s.GetBonePose(_pelvis);
            int parent = s.GetBoneParent(_pelvis);
            Basis parentBasis = parent >= 0 ? s.GetBoneGlobalPose(parent).Basis : Basis.Identity;
            s.SetBonePosePosition(_pelvis, pelvis.Origin + parentBasis.Inverse() * (Vector3.Up * _hipDrop));
        }
        PlantLeg(s, _thighL, _calfL, _footL, dropL - _hipDrop, toWorld, normalL);
        PlantLeg(s, _thighR, _calfR, _footR, dropR - _hipDrop, toWorld, normalR);
    }

    /// <summary>How far (along up) the foot must move to stand on the ground under it; 0 when nothing is found.</summary>
    private float FootOffset(Skeleton3D s, Transform3D toWorld, Vector3 up, int foot, out Vector3 normal)
    {
        normal = Vector3.Up;
        Vector3 ankle = s.GetBoneGlobalPose(foot).Origin;
        Vector3 rest = s.GetBoneGlobalRest(foot).Origin;
        // Only feet near the ground in the clip are planted; a raised foot keeps its height above whatever is below.
        Vector3 world = toWorld * new Vector3(ankle.X, 0f, ankle.Z);
        (bool hit, Vector3 point, Vector3 n) = GroundProbe!(world + up * 0.55f, -up * 1.3f);
        if (!hit)
        {
            return 0f;
        }
        normal = toWorld.Basis.Inverse() * n;
        float ground = up.Dot(point - toWorld.Origin);
        return Math.Clamp(ground, -0.45f, 0.45f) * (ankle.Y < rest.Y + 0.25f ? 1f : 0.5f);
    }

    private void PlantLeg(Skeleton3D s, int thigh, int calf, int foot, float lift, Transform3D toWorld, Vector3 groundNormal)
    {
        if (MathF.Abs(lift) < 1e-4f)
        {
            return;
        }
        Vector3 hip = s.GetBoneGlobalPose(thigh).Origin;
        Vector3 knee = s.GetBoneGlobalPose(calf).Origin;
        Basis footBasis = s.GetBoneGlobalPose(foot).Basis;
        Vector3 ankle = s.GetBoneGlobalPose(foot).Origin;
        SolveTwoBone(s, thigh, calf, foot, hip, knee, ankle, ankle + Vector3.Up * lift, knee + (knee - hip).Normalized() * 0.1f + Vector3.Back * 0.5f);
        // Keep the foot's own orientation, tilted onto the ground (at most 25 degrees).
        Quaternion tilt = Arc(Vector3.Up, groundNormal.Normalized());
        float angle = tilt.GetAngle();
        if (angle > 0.44f)
        {
            tilt = Quaternion.Identity.Slerp(tilt, 0.44f / angle);
        }
        SetGlobalBasis(s, foot, new Basis(tilt) * footBasis);
    }
}
