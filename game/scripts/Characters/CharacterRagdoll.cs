using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// The limp body after death (GROUND.md §4.8): fourteen capsules on the main bones, joined with cones and hinges, under
/// a <see cref="PhysicalBoneSimulator3D"/>. Built on first use. Gravity follows the character's up at the moment of
/// death, not the world's −Y. Ragdolls collide with the world (layer 1) but not with each other or themselves.
/// </summary>
public sealed class CharacterRagdoll
{
    /// <summary>Physics layer of ragdoll bodies (layer 4).</summary>
    public const uint Layer = 1u << 3;

    private readonly Skeleton3D _skeleton;
    private readonly PhysicalBoneSimulator3D _simulator;
    private readonly List<PhysicalBone3D> _bodies = new();

    private enum Joint { Root, Cone, Knee, Elbow }

    // Bone, the bone whose head ends its capsule (or a length), radius, mass, joint, swing or lower limit, twist or upper.
    private static readonly (string Bone, string? End, float Length, float Radius, float Mass, Joint Joint, float A, float B)[] Parts =
    {
        ("pelvis", "spine_01", 0f, 0.125f, 14f, Joint.Root, 0, 0),
        ("spine_01", "spine_03", 0f, 0.13f, 10f, Joint.Cone, 18, 12),
        ("spine_03", "neck_01", 0f, 0.15f, 14f, Joint.Cone, 20, 14),
        ("head", null, 0.2f, 0.1f, 5f, Joint.Cone, 40, 35),
        ("upperarm_l", "lowerarm_l", 0f, 0.055f, 2.5f, Joint.Cone, 75, 35),
        ("upperarm_r", "lowerarm_r", 0f, 0.055f, 2.5f, Joint.Cone, 75, 35),
        ("lowerarm_l", "hand_l", 0f, 0.045f, 2.0f, Joint.Elbow, -40, 100),
        ("lowerarm_r", "hand_r", 0f, 0.045f, 2.0f, Joint.Elbow, -40, 100),
        ("thigh_l", "calf_l", 0f, 0.08f, 9f, Joint.Cone, 55, 15),
        ("thigh_r", "calf_r", 0f, 0.08f, 9f, Joint.Cone, 55, 15),
        ("calf_l", "foot_l", 0f, 0.06f, 4f, Joint.Knee, 0, 135),
        ("calf_r", "foot_r", 0f, 0.06f, 4f, Joint.Knee, 0, 135),
        ("foot_l", "ball_l", 0f, 0.045f, 1.2f, Joint.Cone, 25, 10),
        ("foot_r", "ball_r", 0f, 0.045f, 1.2f, Joint.Cone, 25, 10),
    };

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

    /// <summary>The flexion axis of a knee or an elbow, in the bone's own frame.</summary>
    private static Vector3 HingeAxis(Skeleton3D s, int bone, bool knee)
    {
        Basis basis = s.GetBoneGlobalRest(bone).Basis;
        if (knee)
        {
            // Skeleton +X: with the character facing +Z, bending the knee about it swings the foot backwards.
            return basis.Inverse() * Vector3.Right;
        }
        int parent = s.GetBoneParent(bone);
        Vector3 upper = (s.GetBoneGlobalRest(bone).Origin - s.GetBoneGlobalRest(parent).Origin).Normalized();
        Vector3 fore = basis * Vector3.Up;
        Vector3 axis = upper.Cross(fore);
        if (axis.LengthSquared() < 1e-6f)
        {
            axis = Vector3.Up;
        }
        return basis.Inverse() * axis.Normalized();
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
