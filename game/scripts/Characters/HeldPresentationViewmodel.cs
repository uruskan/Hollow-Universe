using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Content;
using HollowUniverse.Items;

namespace HollowUniverse.Characters;

/// <summary>The authoring and gameplay routes both use this clock and the same profile sampler.</summary>
public partial class ViewmodelView
{
    private string? _action, _previewAction;
    private float _actionPhase, _actionDuration = 1f, _previewTime, _idleTime;
    private bool _actionPhaseSought;
    private int _presentationRevision = -1;
    private Vector3? _contactTarget;
    private bool _contactConfirmed;
    private float _contactAge = 10f;
    private HeldPresentationKey _presentationSample = new();
    private HeldPresentationKey _handSample = new();

    public HeldPresentationProfile? CurrentPresentation => _model?.Presentation;
    public Transform3D? PresentedGrip => _item == null || _model == null ? null : _item.GlobalTransform * HeldItems.Grip(_item, _model);
    public Vector3? PresentedContact => _item == null || _model == null || !HeldItems.HasSocket(_item, _model.Presentation.ContactSocket)
        ? null : (_item.GlobalTransform * HeldItems.Socket(_item, _model.Presentation.ContactSocket)).Origin;
    /// <summary>Distance from the artist's final primary wrist target to the solved arm; exposes unreachable poses.</summary>
    public float PrimaryHandAttachmentError { get; private set; }
    public float SecondaryHandAttachmentError { get; private set; }
    public bool PrimaryHandShouldAttach => ActiveAction != "throw" || _model == null ||
        !_model.Presentation.Actions.TryGetValue("throw", out HeldPresentationClip? thrown) ||
        _presentationSample.At < thrown.Marker || _presentationSample.At >= 0.9f;
    public bool SecondaryHandShouldAttach => _model != null && (_model.LeftHanded ? _bowAim > 0.99f && _loose < 0 :
        _model.Kind is HeldKind.Pistol or HeldKind.Rifle && (_reload < 0 || Math.Abs(_reload - 0.28f) < 0.01f || Math.Abs(_reload - 0.62f) < 0.01f)
            && _item != null && HeldItems.HasSocket(_item, "grip_l"));
    public float AuthoredReachCorrection { get; private set; }
    /// <summary>Fitted-optic ADS shoulder accommodation; limb lengths and the native optical eye stay unchanged.</summary>
    public float OpticShoulderAccommodation { get; private set; }
    public string? PreviewAction => _previewAction;
    public float PreviewTime => _previewTime;
    public string ActiveAction => _previewAction ?? _action ?? (_model?.LeftHanded == true && _frame.Aiming ? "draw" :
        _model?.Kind is HeldKind.Pistol or HeldKind.Rifle && _aim > 0 ? "aim" : "idle");
    public float ActivePhase => _previewAction != null ? _previewTime : _action != null ? _actionPhase :
        ActiveAction == "draw" ? _draw : ActiveAction == "aim" ? _aim :
        CurrentPresentation is { } p ? _idleTime % p.Actions["idle"].Duration / p.Actions["idle"].Duration : 0;

    /// <summary>Start a presentation action. A gameplay duration overrides the art preview's duration.</summary>
    public void PlayAction(string action, float duration = 0f, float? phase = null)
    {
        if (_model == null || !_model.Presentation.Actions.TryGetValue(action, out HeldPresentationClip? clip)) return;
        _previewAction = null;
        _action = action;
        _actionPhase = phase.HasValue && float.IsFinite(phase.Value) ? Math.Clamp(phase.Value, 0, 1) : 0f;
        _actionPhaseSought = phase.HasValue;
        _actionDuration = duration > 0 && float.IsFinite(duration) ? duration : action == "swing"
            ? (float)HeldActionTiming.Quantize(_model.SwingTime) : clip.Duration;
        if (action is "swing" or "repair") { _contactTarget = null; _contactConfirmed = false; }
        if (action == "release")
        {
            _releasedDraw = Math.Max(_draw, 0.15f);
            _draw = 0;
            _loose = 0;
        }
        if (action == "fire") _slideKick = 1f;
        SynchronizeAction();
    }

    public void CancelActions()
    {
        _action = _previewAction = null;
        _actionPhase = 0;
        _actionPhaseSought = false;
        _swing = _reload = _thrust = _useTime = _loose = -1f;
        _draw = _releasedDraw = _bowAim = _aim = 0;
        _frame.Aiming = false;
        _contactTarget = null;
        _contactConfirmed = false;
        _contactAge = 10f;
        _kick = _kickVelocity = Vector3.Zero;
        _slideKick = 0;
    }

    /// <summary>Scrub an authored clip without advancing simulation, sending input, or generating a hit.</summary>
    public void SetPreview(string action, float normalizedTime)
    {
        if (_model == null || !_model.Presentation.Actions.ContainsKey(action))
            throw new ArgumentException($"The held item has no '{action}' presentation.", nameof(action));
        _previewAction = action;
        _previewTime = Math.Clamp(normalizedTime, 0f, 1f);
        _equip = 1;
        _sway = _swayVelocity = Vector2.Zero;
        _bobAmount = _sprint = _crouch = 0;
        _kick = _kickVelocity = Vector3.Zero;
        _aim = action == "aim" ? _previewTime : _frame.Aiming && action != "reload" ? 1f : 0;
        _bowAim = action == "draw" ? Math.Clamp(_previewTime / 0.25f, 0, 1)
            : action == "release" ? (_frame.Aiming ? 1f : 1f - Smooth(Math.Clamp((_previewTime - 0.2f) / 0.8f, 0, 1)))
            : _frame.Aiming ? 1f : 0;
        _draw = action == "draw" ? _previewTime : action == "release" && _frame.Aiming
            ? Math.Clamp(_previewTime * _model.Presentation.Actions[action].Duration /
                Math.Max(0.01f, (float)HeldActionTiming.Quantize(_model.DrawSeconds)), 0, 1)
            : _frame.Aiming && action != "release" ? 1f : 0;
        _releasedDraw = action == "release" ? 1f : 0;
        SynchronizeAction();
        RefreshPresentation();
        if (_item != null && IsInsideTree())
        {
            _skeleton.ResetBonePoses();
            Pose();
            _skeleton.Advance(0);
        }
    }

    public void ClearPreview()
    {
        CancelActions();
        _equip = 1;
    }

    /// <summary>Applies a profile replacement to an equipped item without recreating its meshes.</summary>
    public void RefreshPresentation()
    {
        if (_item == null || _model == null) return;
        HeldPresentationProfile p = _model.Presentation;
        _grip = HeldItems.Grip(_item, _model);
        (float along, float lift) = HandPoses.Seat(p.RightHand.Seat);
        _palmR = HandFrame.Measure(_skeleton, right: true, along: along, lift: lift);
        _gripR = _palmR;
        (along, lift) = HandPoses.Seat(p.LeftHand.Seat);
        _gripL = HandFrame.Measure(_skeleton, right: false, along: along, lift: lift);
        _presentationRevision = HeldPresentations.Revision;
    }

    /// <summary>Camera-space accepted/predicted contact supplied by the shared hit query. A prediction steers the
    /// contact pose but never plays impact feedback; only confirmed=true starts that response.</summary>
    public void SetContactTarget(Vector3? cameraPoint, bool confirmed = false)
    {
        _contactTarget = cameraPoint;
        _contactConfirmed = cameraPoint != null && confirmed;
        if (_contactConfirmed) _contactAge = 0;
    }

    private void AdvancePresentation(float dt)
    {
        if (_presentationRevision != HeldPresentations.Revision) RefreshPresentation();
        if (_previewAction == null)
        {
            _idleTime += dt;
            _contactAge += dt;
            if (_action != null)
            {
                // A network/input action can arrive already partway through a simulation tick. Its supplied phase
                // includes this frame's elapsed time; adding the full render delta again would skip the windup.
                if (!_actionPhaseSought) _actionPhase = Math.Min(1f, _actionPhase + dt / Math.Max(0.01f, _actionDuration));
                _actionPhaseSought = false;
                if (_actionPhase >= 1f) { _action = null; _contactTarget = null; _contactConfirmed = false; }
            }
        }
        SynchronizeAction();
    }

    private void SynchronizeAction()
    {
        if (_model == null) return;
        string action = ActiveAction;
        float t = ActivePhase;
        HeldPresentationClip? clip = _model.Presentation.Actions.GetValueOrDefault(action);
        float seconds = _previewAction != null ? clip?.Duration ?? 1f : _actionDuration;
        _swing = action is "swing" or "repair" ? t : -1;
        _thrust = -1;
        _reload = action == "reload" ? t : -1;
        _useTime = action is "eat" or "bandage" or "inject" ? t * seconds : -1;
        _use = action switch { "eat" => CharacterEvent.Eat, "bandage" => CharacterEvent.Bandage,
            "inject" => CharacterEvent.Inject, _ => CharacterEvent.Use };
        _loose = action == "release" ? t * seconds : -1;
        if (_previewAction != null)
        {
            _slideKick = action == "fire" ? Math.Max(0, 1f - t * 4f) : 0;
            _bowRig?.Update(Smooth(_draw) * FirstPersonDraw, action == "draw");
        }
        float sampleAt = t;
        if (_previewAction == null && action == "swing" && clip != null)
        {
            float fraction = (float)(HeldActionTiming.Quantize(_model.StrikeTime) / Math.Max(0.01, HeldActionTiming.Quantize(_model.SwingTime)));
            sampleAt = clip.AlignMarker(t, fraction);
        }
        else if (_previewAction == null && clip != null && HeldActionTiming.AnticipationSeconds(action) > 0)
        {
            sampleAt = clip.AlignMarker(t, HeldActionTiming.CommitFraction(action, _actionDuration));
        }
        _presentationSample = clip?.Sample(sampleAt, _model.Presentation.RightHand.Fingers, _model.Presentation.LeftHand.Fingers) ?? new HeldPresentationKey();
        _handSample = new HeldPresentationKey
        {
            RightHandPosition = _presentationSample.RightHandPosition, RightHandRotation = _presentationSample.RightHandRotation,
            LeftHandPosition = _presentationSample.LeftHandPosition, LeftHandRotation = _presentationSample.LeftHandRotation,
            RightFingers = _presentationSample.RightFingers.Length == 15 ? _presentationSample.RightFingers : _model.Presentation.RightHand.Fingers,
            LeftFingers = _presentationSample.LeftFingers.Length == 15 ? _presentationSample.LeftFingers : _model.Presentation.LeftHand.Fingers,
        };
        if (_previewAction == null)
        {
            HeldPresentationProfile p = _model.Presentation;
            if (_action != null) LayerHands(p.Actions["idle"], _idleTime % p.Actions["idle"].Duration / p.Actions["idle"].Duration);
            if (_sprint > 0 && p.Actions.TryGetValue("sprint", out HeldPresentationClip? sprint)) LayerHands(sprint, Smooth(_sprint));
            if (_equip < 1 && p.Actions.TryGetValue("equip", out HeldPresentationClip? equip)) LayerHands(equip, _equip);
        }
    }

    private void LayerHands(HeldPresentationClip clip, float phase)
    {
        HeldPresentationKey layer = clip.Sample(phase, _handSample.RightFingers, _handSample.LeftFingers);
        _handSample.RightHandPosition += layer.RightHandPosition;
        _handSample.RightHandRotation += layer.RightHandRotation;
        _handSample.LeftHandPosition += layer.LeftHandPosition;
        _handSample.LeftHandRotation += layer.LeftHandRotation;
        if (layer.RightFingers.Length == 15) _handSample.RightFingers = layer.RightFingers;
        if (layer.LeftFingers.Length == 15) _handSample.LeftFingers = layer.LeftFingers;
    }

    private Transform3D ApplyPresentation(Transform3D place)
    {
        HeldPresentationProfile profile = _model!.Presentation;
        HeldPresentationKey sample = _presentationSample;
        if (_previewAction == null && _action != null)
            place = ApplyKey(place, profile.Actions["idle"].Sample(_idleTime % profile.Actions["idle"].Duration / profile.Actions["idle"].Duration));
        place = ApplyKey(place, sample);
        if (_previewAction == null && _sprint > 0 && profile.Actions.TryGetValue("sprint", out HeldPresentationClip? sprint))
            place = ApplyKey(place, sprint.Sample(Smooth(_sprint)));
        if (_previewAction == null && _equip < 1 && profile.Actions.TryGetValue("equip", out HeldPresentationClip? equip))
            place = ApplyKey(place, equip.Sample(_equip));
        if (_contactTarget is Vector3 target && ActiveAction is "swing" or "repair" &&
            profile.Actions.TryGetValue(ActiveAction, out HeldPresentationClip? attack) && attack.Marker >= 0)
        {
            float at = sample.At;
            float window = 1f - Smooth(Math.Clamp(Math.Abs(at - attack.Marker) / 0.16f, 0, 1));
            Transform3D socket = HeldItems.HasSocket(_item!, profile.ContactSocket) ? HeldItems.Socket(_item!, profile.ContactSocket) : _grip;
            Vector3 contact = place * socket.Origin;
            // Viewmodels have their own FOV and shallow depth. Match the target's screen ray at the natural
            // head depth, rather than extending arms to gameplay reach or changing the server's hit result.
            if (target.Z < -0.01f && contact.Z < -0.01f)
            {
                Camera3D? aimCamera = null;
                for (Node? ancestor = GetParent(); ancestor != null; ancestor = ancestor.GetParent())
                    if (ancestor is Camera3D camera) { aimCamera = camera; break; }
                float cameraFov = (aimCamera ?? GetViewport().GetCamera3D())?.Fov ?? Fov;
                float projection = MathF.Tan(Mathf.DegToRad(Fov) * 0.5f) / MathF.Tan(Mathf.DegToRad(cameraFov) * 0.5f);
                Vector3 projected = new(target.X * contact.Z / target.Z * projection, target.Y * contact.Z / target.Z * projection, contact.Z);
                place.Origin += (projected - contact).LimitLength(0.20f) * window;
            }
        }
        if (_contactConfirmed && _contactAge < 0.16f && _previewAction == null)
        {
            float response = MathF.Sin(_contactAge * MathF.PI / 0.16f) * (1f - _contactAge / 0.16f);
            place = ApplyKey(place, new HeldPresentationKey { Position = new Vector3(0, 0, 0.008f * response), Rotation = new Vector3(1.5f * response, 0, 0) });
        }
        return place;
    }

    private Transform3D ApplyKey(Transform3D place, HeldPresentationKey key)
    {
        Basis turn = Basis.FromEuler(key.Rotation * (MathF.PI / 180f));
        Vector3 pivot = place * _grip.Origin + _model!.Presentation.SwingPivot;
        return new Transform3D(turn * place.Basis, pivot + turn * (place.Origin - pivot) + key.Position);
    }

    private Transform3D ArtHand(Transform3D handCamera, bool right)
    {
        HeldPresentationHand settings = right ? _model!.Presentation.RightHand : _model!.Presentation.LeftHand;
        Transform3D hand = handCamera * settings.WristOffset.Transform;
        Vector3 rotation = right ? _handSample.RightHandRotation : _handSample.LeftHandRotation;
        hand.Basis = Basis.FromEuler(rotation * (MathF.PI / 180f)) * hand.Basis;
        hand.Origin += right ? _handSample.RightHandPosition : _handSample.LeftHandPosition;
        return hand;
    }

    /// <summary>Keep the physical hand on the item instead of letting the arm solver clamp away from its grip.
    /// A ray correction preserves the contact's screen position. The editor reports this correction so authors can
    /// bring an unreachable pose back into range; neither limb length nor gameplay reach changes.</summary>
    private Transform3D ConstrainReach(Transform3D item, Transform3D toSkeleton)
    {
        OpticShoulderAccommodation = 0;
        Vector3 original = item.Origin;
        Transform3D toCamera = toSkeleton.AffineInverse();
        (Vector3 Centre, float Radius) ReachBall(Transform3D wrist, bool right)
        {
            int upper = right ? _upperR : _upperL, lower = right ? _lowerR : _lowerL, end = right ? _handR : _handL;
            Vector3 shoulder = toCamera * _skeleton.GetBoneGlobalPose(upper).Origin;
            float reach = _skeleton.GetBoneGlobalRest(upper).Origin.DistanceTo(_skeleton.GetBoneGlobalRest(lower).Origin)
                + _skeleton.GetBoneGlobalRest(lower).Origin.DistanceTo(_skeleton.GetBoneGlobalRest(end).Origin) - 0.004f;
            // Every hand target moves by the same item translation. Each arm therefore defines a sphere of valid
            // translations; their intersection keeps both hands connected without changing either limb's length.
            return (shoulder - wrist.Origin, reach);
        }
        bool left = _model!.LeftHanded;
        Transform3D primary = ArtHand(HandFrame.HandFor(item * _grip, HandFrame.PowerGrip, left ? _gripL : _palmR), !left);
        (Vector3 centre, float radius) = ReachBall(primary, !left);
        Vector3 otherCentre = centre;
        float otherRadius = radius;
        bool supportedGun = _model.Kind is HeldKind.Pistol or HeldKind.Rifle && HeldItems.HasSocket(_item!, "grip_l");
        // Include the future string target throughout raising so a constraint does not suddenly engage at full aim.
        // During a reload the magazine well remains reachable, so extracting/inserting the actual part cannot snap.
        bool both = SecondaryHandShouldAttach || left && _bowRig != null && _loose < 0 || supportedGun && _reload >= 0;
        if (both)
        {
            Transform3D secondary;
            if (left && _bowRig != null)
            {
                Vector3 fingers = item.Basis.X.Normalized(), thumb = item.Basis.Y.Normalized();
                var hook = new Transform3D(new Basis(fingers, thumb, fingers.Cross(thumb)), item * _bowRig.Nock(Smooth(_draw) * FirstPersonDraw));
                secondary = ArtHand(HandFrame.HandFor(hook, HandFrame.PowerGrip, _gripR), true);
            }
            else
            {
                Transform3D grip = HeldItems.Socket(_item!, "grip_l") * _model.Presentation.LeftHand.GripOffset.Transform;
                grip.Origin += _model.Entry.Held?.SupportHand?.Offset ?? Vector3.Zero;
                Basis support = _model.SupportFrame;
                if (_reload >= 0)
                {
                    Vector4 weights = ViewmodelMotion.ReloadHand(_reload);
                    Vector3 right = HeldItems.Socket(_item!, "grip_r").Origin, leftGrip = grip.Origin;
                    bool pistol = _model.Kind == HeldKind.Pistol;
                    Vector3 well = pistol ? right + new Vector3(-0.01f, -0.1f, 0) : (right + leftGrip) * 0.5f + new Vector3(0, -0.1f, 0);
                    Vector3 work = pistol ? HeldItems.Socket(_item!, "sight").Origin + new Vector3(0.02f, -0.012f, 0)
                        : right + new Vector3(0.12f, 0.07f, 0.045f);
                    Vector3 pull = new Vector3(-0.2f, 0, 0) * Math.Max(0, weights.W - 1f);
                    // The detached hand may travel to the pouch; keep its eventual magazine seat in range instead
                    // of dragging the whole weapon down with that independent hand excursion.
                    grip.Origin = leftGrip * weights.X + well * (weights.Y + weights.Z) + (work + pull) * Math.Min(1, weights.W);
                    support = ViewmodelMotion.ReloadGrip(support, _reload);
                }
                secondary = ArtHand(HandFrame.HandFor(item * grip, support, _gripL), false);
            }
            (otherCentre, otherRadius) = ReachBall(secondary, left);
        }

        static bool Inside(Vector3 at, Vector3 c, float r) => at.DistanceSquaredTo(c) <= r * r + 0.0000001f;
        static Vector3 Project(Vector3 at, Vector3 c, float r) => c + (at - c).LimitLength(r);
        if (Inside(Vector3.Zero, centre, radius) && (!both || Inside(Vector3.Zero, otherCentre, otherRadius)))
        { AuthoredReachCorrection = 0; return item; }

        Vector3 nearest = Project(Vector3.Zero, centre, radius);
        if (both && !Inside(nearest, otherCentre, otherRadius))
        {
            Vector3 other = Project(Vector3.Zero, otherCentre, otherRadius);
            if (Inside(other, centre, radius)) nearest = other;
            else
            {
                // The closest point lies on the two spheres' intersection circle when neither individual closest
                // point satisfies the other arm. This is continuous, unlike switching between depth and radial clamps.
                Vector3 between = otherCentre - centre;
                float distance = between.Length();
                if (distance > 0.00001f && distance <= radius + otherRadius && distance >= Math.Abs(radius - otherRadius))
                {
                    Vector3 axis = between / distance;
                    float along = (radius * radius - otherRadius * otherRadius + distance * distance) / (2f * distance);
                    Vector3 circle = centre + axis * along;
                    float extent = MathF.Sqrt(Math.Max(0, radius * radius - along * along));
                    Vector3 toward = -circle + axis * circle.Dot(axis);
                    if (toward.LengthSquared() < 0.00000001f)
                    {
                        toward = axis.Cross(Vector3.Up);
                        if (toward.LengthSquared() < 0.00000001f) toward = axis.Cross(Vector3.Right);
                    }
                    nearest = circle + toward.Normalized() * extent;
                }
                else
                {
                    // Invalid authored hand separation cannot be repaired by translating one rigid item. Leave a
                    // finite closest compromise for the editor's attachment diagnostics instead of stretching limbs.
                    nearest = (nearest + other) * 0.5f;
                }
            }
        }

        if (_aim > 0 && _reload < 0 && GunAttachmentPresentation.TryEye(_item!, _model.Id, out _))
        {
            // The aligned optic fixes the gun relative to the camera. Translating it toward the native gun sight
            // can pull the eyepiece through the near plane. The same feasible hand/shoulder separation can instead
            // come from leaning the FPS shoulders toward the fixed grips, without lengthening either arm.
            // Blend as ADS engages; malformed/outlying poses retain the ordinary bounded item correction.
            Vector3 accommodation = nearest.LimitLength(0.12f) * Smooth(_aim);
            Shoulder(left ? _upperL : _upperR, -accommodation, toSkeleton);
            if (both) Shoulder(left ? _upperR : _upperL, -accommodation, toSkeleton);
            item.Origin += nearest - accommodation;
            OpticShoulderAccommodation = accommodation.Length();
            AuthoredReachCorrection = item.Origin.DistanceTo(original);
            return item;
        }

        string anchorSocket = _model.Presentation.ContactSocket;
        Vector3 anchor = item * (HeldItems.HasSocket(_item!, anchorSocket) ? HeldItems.Socket(_item!, anchorSocket).Origin : _grip.Origin);
        if (_aim > 0 && HeldItems.HasSocket(_item!, "sight"))
            anchor = anchor.Lerp(item * HeldItems.Socket(_item!, "sight").Origin, Smooth(_aim));
        Vector3 ray = anchor.Z < -0.01f ? -anchor.Normalized() : Vector3.Back;
        float minimum = float.NegativeInfinity, maximum = float.PositiveInfinity, clearance = float.PositiveInfinity;
        bool RayInterval(Vector3 c, float r)
        {
            float along = c.Dot(ray), discriminant = r * r - (c.LengthSquared() - along * along);
            if (discriminant < 0) return false;
            float half = MathF.Sqrt(discriminant);
            minimum = Math.Max(minimum, along - half); maximum = Math.Min(maximum, along + half);
            clearance = Math.Min(clearance, half);
            return minimum <= maximum;
        }
        Vector3 shift = nearest;
        if (RayInterval(centre, radius) && (!both || RayInterval(otherCentre, otherRadius)))
        {
            Vector3 onRay = ray * Math.Clamp(0f, minimum, maximum);
            clearance = Math.Min(clearance, (maximum - minimum) * 0.5f);
            // Fade to the closest feasible point near tangency, where a ray ceases to intersect the reach volume.
            // Both endpoints are feasible and the volume is convex, so their blend preserves hand attachment.
            float rayPreference = Smooth(Math.Clamp(clearance / 0.04f, 0, 1));
            if (supportedGun && _reload >= 0)
            {
                // A reload has no target screen pixel to preserve. As the support hand moves between the foregrip,
                // magazine and receiver, a screen ray can require a large camera-ward translation even when a short
                // sideways correction reaches both wrists. Prefer the nearest feasible pose throughout the action;
                // blend only at its ends while the hand remains on its ordinary support grip.
                float reloading = Smooth(Math.Clamp(_reload / 0.12f, 0, 1))
                    * (1f - Smooth(Math.Clamp((_reload - 0.92f) / 0.08f, 0, 1)));
                rayPreference *= 1f - reloading;
            }
            shift = nearest.Lerp(onRay, rayPreference);
        }
        item.Origin += shift;
        AuthoredReachCorrection = item.Origin.DistanceTo(original);
        return item;
    }

    private void ArtFingers(bool right, HandPoses.Grip fallback)
    {
        HeldPresentationHand hand = right ? _model!.Presentation.RightHand : _model!.Presentation.LeftHand;
        // Let go during a throw or bow release. The profile is the starting pose, not a permanent closed fist.
        float[] keyed = right ? _handSample.RightFingers : _handSample.LeftFingers;
        if (keyed.Length == 15)
        {
            HandPoses.Apply(_skeleton, fallback, right, radius: hand.Radius, seat: hand.Seat, curlDegrees: keyed);
            return;
        }
        float release = ActiveAction == "throw" && _model.Presentation.Actions.TryGetValue("throw", out HeldPresentationClip? thrown)
            ? Smooth(Math.Clamp((ActivePhase - thrown.Marker) / 0.12f, 0, 1)) : right && ActiveAction == "release" ? Smooth(Math.Clamp(ActivePhase / 0.15f, 0, 1)) : 0;
        if (release <= 0)
        {
            HandPoses.Apply(_skeleton, fallback, right, radius: hand.Radius, seat: hand.Seat, curlDegrees: hand.Fingers);
            return;
        }
        float[] curls = new float[15];
        for (int i = 0; i < curls.Length; i++) curls[i] = Mathf.Lerp(hand.Fingers.Length == 15 ? hand.Fingers[i] : 40f, 8f, release);
        HandPoses.Apply(_skeleton, fallback, right, radius: hand.Radius, seat: hand.Seat, curlDegrees: curls);
    }
}
