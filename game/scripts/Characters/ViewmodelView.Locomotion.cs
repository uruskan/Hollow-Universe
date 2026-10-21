using System;
using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Characters;

public partial class ViewmodelView
{
    private HeldLocomotionProfile? _locomotionProfile;
    private double _locomotionPhase;
    private float _locomotionGait, _locomotionSprint, _locomotionCrouch, _locomotionAction = 1;
    private Vector2 _locomotionLook;

    /// <summary>The complete camera-space composition used this frame. Identity is the exact stationary baseline.</summary>
    public Transform3D SkeletalLocomotionTransform { get; private set; } = Transform3D.Identity;
    public float SkeletalLocomotionWeight => Smooth(_locomotionAction);
    public float SkeletalLocomotionGait => _locomotionGait;
    public float SkeletalLocomotionSprint => _locomotionSprint;
    public double SkeletalLocomotionPhase => _locomotionPhase;
    public string? SkeletalLocomotionProfile => _locomotionProfile?.Name;

    private void ResetSkeletalLocomotion()
    {
        _locomotionProfile = null;
        _locomotionPhase = 0;
        _locomotionGait = _locomotionSprint = _locomotionCrouch = 0;
        _locomotionAction = 1;
        _locomotionLook = Vector2.Zero;
        SkeletalLocomotionTransform = Transform3D.Identity;
    }

    private void AdvanceSkeletalLocomotion(float elapsed)
    {
        // Authored-pose scrubbing stays exact and does not inherit motion from prior live playback.
        if (_previewAction != null || _model == null) { ResetSkeletalLocomotion(); return; }
        HeldLocomotionProfile? profile = HeldLocomotion.ForFamily(_model.Presentation.Family);
        if (profile == null || !_model.Presentation.Actions.TryGetValue("idle", out HeldPresentationClip? idle) || idle.Skeletal == null)
        { ResetSkeletalLocomotion(); return; }
        _locomotionProfile = profile;
        // Bound a presentation-only hitch. Ordinary frame rates integrate travelled distance, not a render clock.
        float dt = float.IsFinite(elapsed) ? Math.Clamp(elapsed, 0, 0.25f) : 0;
        // Camera pitch moves forward velocity into camera Y. Gameplay supplies gravity-tangent speed;
        // legacy synthetic/editor frames can still use their deliberately level XZ input.
        float speed = _frame.GroundSpeed ?? new Vector2(_frame.LocalVelocity.X, _frame.LocalVelocity.Z).Length();
        if (!float.IsFinite(speed) || speed < 0) speed = 0;
        bool moving = _frame.Grounded && speed >= profile.MinimumSpeed;
        if (moving)
            _locomotionPhase = (_locomotionPhase + Math.PI * speed * dt / Footsteps.Stride(speed, _frame.Crouching)) % (2 * Math.PI);
        _locomotionGait = SettleMotion(_locomotionGait, moving ? Math.Clamp(speed / profile.ReferenceSpeed, 0, 1) : 0,
            dt, profile.GaitResponseSeconds);
        _locomotionSprint = SettleMotion(_locomotionSprint,
            moving && _frame.Sprinting && !_frame.Crouching && speed >= profile.SprintMinimumSpeed ? 1 : 0,
            dt, profile.SprintResponseSeconds);
        _locomotionCrouch = SettleMotion(_locomotionCrouch, _frame.Crouching ? 1 : 0, dt, profile.GaitResponseSeconds);
        Vector2 look = _frame.LookRate;
        if (!float.IsFinite(look.X) || !float.IsFinite(look.Y)) look = Vector2.Zero;
        look = look.LimitLength(profile.MaximumLookRate);
        _locomotionLook = new Vector2(SettleMotion(_locomotionLook.X, look.X, dt, profile.LookResponseSeconds),
            SettleMotion(_locomotionLook.Y, look.Y, dt, profile.LookResponseSeconds));
        float target = _equip >= 1 && profile.ActionWeights.TryGetValue(ActiveAction, out float weight) ? weight : 0;
        float fade = target < _locomotionAction ? profile.ActionFadeOutSeconds : profile.ActionFadeInSeconds;
        // A finite fade reaches exactly zero during a strike, equip or inspection, then eases back after cancellation.
        _locomotionAction = Mathf.MoveToward(_locomotionAction, target, dt / fade);
    }

    private static float SettleMotion(float value, float target, float dt, float seconds)
    {
        if (dt <= 0) return value;
        float result = target + (value - target) * MathF.Exp(-dt / seconds);
        return MathF.Abs(result - target) < 0.00001f ? target : result;
    }

    private Transform3D ComposeSkeletalLocomotion()
    {
        HeldLocomotionProfile? profile = _locomotionProfile;
        if (profile == null || _previewAction != null) return Transform3D.Identity;
        float gate = Smooth(_locomotionAction);
        if (gate == 0 || _locomotionGait == 0 && _locomotionSprint == 0 && _locomotionLook == Vector2.Zero)
            return Transform3D.Identity;
        float side = (float)Math.Sin(_locomotionPhase), step = (float)Math.Cos(2 * _locomotionPhase);
        float gait = _locomotionGait * Mathf.Lerp(1, profile.SprintGaitScale, _locomotionSprint)
            * Mathf.Lerp(1, profile.CrouchGaitScale, _locomotionCrouch);
        Vector3 walkPosition = HeldLocomotionProfile.Vector(profile.WalkPosition);
        Vector3 walkRotation = HeldLocomotionProfile.Vector(profile.WalkRotation);
        Vector3 position = new Vector3(side * walkPosition.X, step * walkPosition.Y, step * walkPosition.Z) * gait;
        Vector3 rotation = new Vector3(step * walkRotation.X, side * walkRotation.Y, side * walkRotation.Z) * gait;
        position += HeldLocomotionProfile.Vector(profile.SprintPosition) * Smooth(_locomotionSprint);
        rotation += HeldLocomotionProfile.Vector(profile.SprintRotation) * Smooth(_locomotionSprint);
        Vector3 lookPosition = HeldLocomotionProfile.Vector(profile.LookPosition), lookRotation = HeldLocomotionProfile.Vector(profile.LookRotation);
        position += new Vector3(-_locomotionLook.X * lookPosition.X, _locomotionLook.Y * lookPosition.Y, 0);
        rotation += new Vector3(-_locomotionLook.Y * lookRotation.X, -_locomotionLook.X * lookRotation.Y, _locomotionLook.X * lookRotation.Z);
        Basis turn = Basis.FromEuler(rotation * (gate * MathF.PI / 180));
        Vector3 pivot = HeldLocomotionProfile.Vector(profile.Pivot);
        return new Transform3D(turn, position * gate + pivot - turn * pivot);
    }
}
