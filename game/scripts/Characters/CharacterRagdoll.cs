using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// The limp body after death (GROUND.md §4.8): fourteen capsules on the main bones under a
/// <see cref="PhysicalBoneSimulator3D"/>, joined within what a body can do: hinges at the knees and elbows (they bend
/// one way, and hardly past straight), six-axis joints with separate flexion, abduction and twist limits at the hips
/// and shoulders (a leg swings far forward but little back; an arm lifts high but not through the chest), cones along
/// the spine, neck and ankles. Built on first use. Gravity follows the character's up at the moment of death, not the
/// world's −Y. Ragdolls collide with the world (layer 1) but not with each other or themselves.
/// </summary>
public sealed class CharacterRagdoll
{
    /// <summary>Physics layer of ragdoll bodies (layer 4).</summary>
    public const uint Layer = 1u << 3;

    private readonly Skeleton3D _skeleton;
    private readonly PhysicalBoneSimulator3D _simulator;
    private readonly List<PhysicalBone3D> _bodies = new();

    private enum Joint { Root, Cone, Knee, Elbow, Hip, Shoulder }

    // Bone, the bone whose head ends its capsule (or a length), radius, mass, joint, and its limits (degrees): a cone's
    // swing and twist; a hinge's lower and upper bend.
    private static readonly (string Bone, string? End, float Length, float Radius, float Mass, Joint Joint, float A, float B)[] Parts =
    {
        ("pelvis", "spine_01", 0f, 0.125f, 14f, Joint.Root, 0, 0),
        ("spine_01", "spine_03", 0f, 0.13f, 10f, Joint.Cone, 22, 12),
        ("spine_03", "neck_01", 0f, 0.15f, 14f, Joint.Cone, 18, 10),
        ("head", null, 0.2f, 0.1f, 5f, Joint.Cone, 35, 40),
        ("upperarm_l", "lowerarm_l", 0f, 0.055f, 2.5f, Joint.Shoulder, 0, 0),
        ("upperarm_r", "lowerarm_r", 0f, 0.055f, 2.5f, Joint.Shoulder, 0, 0),
        ("lowerarm_l", "hand_l", 0f, 0.045f, 2.0f, Joint.Elbow, -5, 145),
        ("lowerarm_r", "hand_r", 0f, 0.045f, 2.0f, Joint.Elbow, -5, 145),
        ("thigh_l", "calf_l", 0f, 0.08f, 9f, Joint.Hip, 0, 0),
        ("thigh_r", "calf_r", 0f, 0.08f, 9f, Joint.Hip, 0, 0),
        ("calf_l", "foot_l", 0f, 0.06f, 4f, Joint.Knee, -2, 140),
        ("calf_r", "foot_r", 0f, 0.06f, 4f, Joint.Knee, -2, 140),
        ("foot_l", "ball_l", 0f, 0.045f, 1.2f, Joint.Cone, 30, 12),
        ("foot_r", "ball_r", 0f, 0.045f, 1.2f, Joint.Cone, 30, 12),
    };

    // Six-axis limits (degrees): flexion (x: −extension … +flexion, forward), twist (y), abduction (z: −adduction …
    // +abduction, away from the body; mirrored on the right).
    private static readonly (float ExtendBack, float FlexForward, float Twist, float Adduct, float Abduct) HipLimits = (20f, 115f, 30f, 20f, 45f);
    private static readonly (float ExtendBack, float FlexForward, float Twist, float Adduct, float Abduct) ShoulderLimits = (45f, 100f, 45f, 35f, 85f);

    public bool Active { get; private set; }

    public CharacterRagdoll(Skeleton3D skeleton)
    {
        _skeleton = skeleton;
        _simulator = new PhysicalBoneSimulator3D { Name = "Ragdoll" };
        skeleton.AddChild(_simulator);
        foreach (var p in Parts)
        {
            int bone = skeleton.FindBone(p.Bone);
            if (bone < 0)
            {
                continue;
            }
            Transform3D rest = skeleton.GetBoneGlobalRest(bone);
            Vector3 along;
            if (p.End != null)
            {
                Vector3 end = skeleton.GetBoneGlobalRest(skeleton.FindBone(p.End)).Origin;
                along = rest.Basis.Inverse() * (end - rest.Origin);
            }
            else
            {
                along = Vector3.Up * p.Length;
            }
            float length = along.Length();
            Vector3 dir = along / length;
            var body = new PhysicalBone3D
            {
                Name = "Body_" + p.Bone,
                Mass = p.Mass,
                Friction = 0.9f,
                LinearDamp = 0.15f,
                AngularDamp = 1.2f,
                GravityScale = 0f,
                CollisionLayer = Layer,
                CollisionMask = 1,
                BodyOffset = new Transform3D(new Basis(CharacterPoser.Arc(Vector3.Up, dir)), along * 0.5f),
            };
            body.Set("bone_name", p.Bone);
            float shapeLength = Math.Max(p.Radius * 2.05f, length + p.Radius * 0.6f);
            body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = p.Radius, Height = shapeLength } });
            // The joint sits at the bone's head; its frame decides the hinge axis.
            Transform3D toBody = body.BodyOffset.AffineInverse();
            switch (p.Joint)
            {
                case Joint.Root:
                    body.JointType = PhysicalBone3D.JointTypeEnum.None;
                    break;
                case Joint.Cone:
                    body.JointType = PhysicalBone3D.JointTypeEnum.Cone;
                    body.JointOffset = toBody;
                    body.Set("joint_constraints/swing_span", p.A);
                    body.Set("joint_constraints/twist_span", p.B);
                    body.Set("joint_constraints/bias", 0.3f);
                    body.Set("joint_constraints/softness", 0.8f);
                    body.Set("joint_constraints/relaxation", 1.0f);
                    break;
                case Joint.Hip:
                case Joint.Shoulder:
                    SixAxes(body, skeleton, bone, toBody, p.Joint == Joint.Hip ? HipLimits : ShoulderLimits,
                        left: p.Bone.EndsWith("_l", StringComparison.Ordinal));
                    break;
                default:
                    body.JointType = PhysicalBone3D.JointTypeEnum.Hinge;
                    Vector3 axis = HingeAxis(skeleton, bone, p.Joint == Joint.Knee);
                    // Hinge about the joint frame's Z axis.
                    Vector3 axisInBody = toBody.Basis * axis;
                    Basis frame = new Basis(CharacterPoser.Arc(Vector3.Back, axisInBody.Normalized()));
                    body.JointOffset = new Transform3D(frame, toBody.Origin);
                    body.Set("joint_constraints/angular_limit_enabled", true);
                    body.Set("joint_constraints/angular_limit_lower", p.A);
                    body.Set("joint_constraints/angular_limit_upper", p.B);
                    body.Set("joint_constraints/angular_limit_bias", 0.3f);
                    body.Set("joint_constraints/angular_limit_softness", 0.9f);
                    break;
            }
            _simulator.AddChild(body);
            _bodies.Add(body);
        }
        _simulator.Active = false;
    }

    /// <summary>The flexion axis of a knee or an elbow, in the bone's own frame: turning about it by a positive angle
    /// bends the joint (the foot goes back, the hand comes forward).</summary>
    private static Vector3 HingeAxis(Skeleton3D s, int bone, bool knee)
    {
        Basis basis = s.GetBoneGlobalRest(bone).Basis;
        if (knee)
        {
            // Skeleton +X: with the character facing +Z, bending the knee about it swings the foot backwards.
            return basis.Inverse() * Vector3.Right;
        }
        // The upper arm's direction crossed with forward (+Z): turning about it brings the forearm forward. (The rest
        // pose's arm is nearly straight, so the two arm bones alone don't give the axis.)
        int parent = s.GetBoneParent(bone);
        Vector3 upper = (s.GetBoneGlobalRest(bone).Origin - s.GetBoneGlobalRest(parent).Origin).Normalized();
        return basis.Inverse() * upper.Cross(new Vector3(0, 0, 1)).Normalized();
    }

    /// <summary>
    /// A hip or a shoulder: a six-axis joint whose frame has Y along the bone, X across it so that turning about +X
    /// swings the limb forward, and Z = X × Y, about which a positive turn swings a left limb out (a right limb in).
    /// </summary>
    private static void SixAxes(PhysicalBone3D body, Skeleton3D s, int bone, Transform3D toBody,
        (float ExtendBack, float FlexForward, float Twist, float Adduct, float Abduct) limits, bool left)
    {
        Transform3D rest = s.GetBoneGlobalRest(bone);
        int child = s.FindBone(s.GetBoneName(bone).Replace("thigh", "calf").Replace("upperarm", "lowerarm"));
        Vector3 y = (s.GetBoneGlobalRest(child).Origin - rest.Origin).Normalized();
        Vector3 x = y.Cross(new Vector3(0, 0, 1)).Normalized();
        Vector3 z = x.Cross(y).Normalized();
        Basis inBody = toBody.Basis * rest.Basis.Inverse() * new Basis(x, y, z);
        body.JointType = PhysicalBone3D.JointTypeEnum.Type6Dof;
        body.JointOffset = new Transform3D(inBody.Orthonormalized(), toBody.Origin);
        float abductLow = left ? -limits.Adduct : -limits.Abduct, abductHigh = left ? limits.Abduct : limits.Adduct;
        (string Axis, float Lower, float Upper)[] axes =
        {
            ("x", -limits.ExtendBack, limits.FlexForward),
            ("y", -limits.Twist, limits.Twist),
            ("z", abductLow, abductHigh),
        };
        foreach ((string axis, float lower, float upper) in axes)
        {
            body.Set($"joint_constraints/{axis}/angular_limit_enabled", true);
            body.Set($"joint_constraints/{axis}/angular_limit_lower", lower);
            body.Set($"joint_constraints/{axis}/angular_limit_upper", upper);
            body.Set($"joint_constraints/{axis}/angular_limit_softness", 0.8f);
            body.Set($"joint_constraints/{axis}/angular_damping", 1.0f);
            body.Set($"joint_constraints/{axis}/linear_limit_enabled", true);
            body.Set($"joint_constraints/{axis}/linear_limit_lower", 0f);
            body.Set($"joint_constraints/{axis}/linear_limit_upper", 0f);
        }
    }

    /// <summary>Goes limp, moving at <paramref name="velocity"/> (world space), falling along −<paramref name="up"/>.</summary>
    public void Start(Vector3 velocity, Vector3 up, float gravity = 9.0f)
    {
        _simulator.Active = true;
        _simulator.PhysicalBonesStartSimulation();
        foreach (PhysicalBone3D body in _bodies)
        {
            body.LinearVelocity = velocity;
            PhysicsServer3D.BodySetConstantForce(body.GetRid(), -up * gravity * body.Mass);
        }
        Active = true;
    }

    /// <summary>A push on the chest (world space), e.g. from the killing shot.</summary>
    public void Push(Vector3 impulse)
    {
        foreach (PhysicalBone3D body in _bodies)
        {
            if (body.Name == "Body_spine_03")
            {
                body.ApplyCentralImpulse(impulse);
            }
        }
    }

    public void Stop()
    {
        _simulator.PhysicalBonesStopSimulation();
        _simulator.Active = false;
        Active = false;
        _skeleton.ResetBonePoses();
    }
}
