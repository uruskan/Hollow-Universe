using System;
using Godot;
using Hollow.Simulation.Content;

namespace HollowUniverse.Characters;

public partial class CharacterView
{
    private string? _presentationAction;
    private float _presentationTime, _presentationDuration;
    private bool _presentationPhaseSought;
    private string? _previewAction;
    private float _previewPhase;
    private int _presentationRevision = -1;
    private float _actionCarryRemaining;
    private string? _actionCarryItem;
    private HeldPresentationKey? _sampledPresentation;

    public HeldPresentationProfile? CurrentPresentation => _itemModel == null ? null : HeldPresentations.Get(_itemModel.Id);

    /// <summary>Starts the same authored action used by first person. Duration comes from the item's simulation ticks.</summary>
    public void PlayAction(string action, float duration = 0, float? phase = null)
    {
        if (CurrentPresentation is not { } profile || !profile.Actions.TryGetValue(action, out HeldPresentationClip? clip)) return;
        _presentationAction = action;
        _presentationDuration = duration > 0 ? duration : clip.Duration;
        _presentationDuration = Math.Max(HeldActionTiming.Seconds(1), _presentationDuration);
        float normalized = Math.Clamp(phase ?? 0f, 0f, 1f);
        _presentationTime = normalized * _presentationDuration;
        _presentationPhaseSought = phase.HasValue;
        if (action == "draw") _draw = normalized;
        // Rifle hand/magazine choreography follows exactly the same normalized reload clock.
        _reload = action == "reload" ? normalized : -1f;
        _pistolReload = action == "reload" ? normalized : -1f;
        _use = action == "bandage" ? UseMotion.Bandage : action == "inject" ? UseMotion.Inject : UseMotion.None;
        _useTime = _use != UseMotion.None ? _presentationTime : -1f;
        _tree.Set(CharacterAnimator.Action, (int)AnimationNodeOneShot.OneShotRequest.Abort);
        _actionFade = -1f;
        _hold = 1f;
    }

    /// <summary>Temporarily presents the item an accepted action consumed; it never changes inventory or gameplay.</summary>
    public void PlayItemAction(string item, string action, float duration, float phase = 0)
    {
        if (!_ready || IsDead || Lying || _frame.Stance is CharacterStance.Sit or CharacterStance.Crawl or CharacterStance.Sleep) return;
        if (HeldItems.Find(item) is not { } model) return;
        if (_itemModel?.Id != item) SwapItem(model);
        _held = model.Kind;
        _heldId = model.Id;
        PlayAction(action, duration, phase);
        _actionCarryItem = item;
        _actionCarryRemaining = Math.Max(0f, _presentationDuration - _presentationTime);
    }

    public void SetPreview(string action, float normalizedTime)
    {
        _previewAction = action;
        _previewPhase = Math.Clamp(normalizedTime, 0f, 1f);
        _presentationAction = null;
        _presentationPhaseSought = false;
        _reload = _pistolReload = _thrust = _useTime = -1f;
        _use = UseMotion.None;
        _reloadMagazineHand = null;
        _reloadParts?.Restore();
        if (_ready) _tree.Set(CharacterAnimator.Action, (int)AnimationNodeOneShot.OneShotRequest.Abort);
    }

    public void ClearPreview()
    {
        _previewAction = null;
        CancelActions();
    }

    public void RefreshPresentation() => _presentationRevision = -1;

    public void CancelActions()
    {
        _presentationAction = null;
        _presentationPhaseSought = false;
        _presentationTime = _actionCarryRemaining = 0;
        _actionCarryItem = null;
        _reload = _pistolReload = _thrust = _useTime = _loose = _eating = _actionFade = -1f;
        _draw = _bowAim = _recoil = 0f;
        _use = UseMotion.None;
        _reloadMagazineHand = null;
        _reloadParts?.Restore();
        if (_ready) _tree.Set(CharacterAnimator.Action, (int)AnimationNodeOneShot.OneShotRequest.Abort);
    }

    private bool TryPresentationEvent(CharacterEvent e)
    {
        if (CurrentPresentation is not { } profile) return false;
        string? action = e switch
        {
            CharacterEvent.Swing => profile.Actions.ContainsKey("swing") ? "swing" : profile.UseAction,
            CharacterEvent.Reload => "reload",
            CharacterEvent.Use => profile.UseAction,
            CharacterEvent.Eat => "eat",
            CharacterEvent.Bandage => "bandage",
            CharacterEvent.Inject => "inject",
            _ => null,
        };
        if (action == null || !profile.Actions.TryGetValue(action, out HeldPresentationClip? clip)) return false;
        float seconds = action == "swing" ? HeldActionTiming.Quantize(_itemModel!.SwingTime) : HeldActionTiming.Quantize(clip.Duration);
        PlayAction(action, seconds);
        return true;
    }

    private void AdvancePresentation(float dt)
    {
        if (_previewAction != null)
        {
            if (CurrentPresentation?.Actions.TryGetValue(_previewAction, out HeldPresentationClip? preview) == true)
            {
                _presentationDuration = Math.Max(HeldActionTiming.Seconds(1), preview.Duration);
                _reload = _pistolReload = _previewAction == "reload" ? _previewPhase : -1f;
                _use = _previewAction == "bandage" ? UseMotion.Bandage : _previewAction == "inject" ? UseMotion.Inject : UseMotion.None;
                _useTime = _use == UseMotion.None ? -1f : _previewPhase * _presentationDuration;
                if (_itemModel?.LeftHanded == true)
                {
                    _bowAim = _previewAction == "draw" ? Math.Clamp(_previewPhase / 0.25f, 0f, 1f)
                        : _previewAction == "release" ? (_frame.Aiming ? 1f : 1f - Smooth(Math.Clamp((_previewPhase - 0.2f) / 0.8f, 0f, 1f)))
                        : _frame.Aiming ? 1f : 0f;
                    _draw = _previewAction == "draw" ? _previewPhase : _previewAction == "release" && _frame.Aiming
                        ? Math.Clamp(_previewPhase * _presentationDuration / Math.Max(HeldActionTiming.Seconds(1), HeldActionTiming.Quantize(_itemModel.DrawSeconds)), 0f, 1f)
                        : _frame.Aiming && _previewAction != "release" ? 1f : 0f;
                    _releasedDraw = 1f;
                    _loose = _previewAction == "release" ? _previewPhase * _presentationDuration : -1f;
                }
            }
            return;
        }
        // A network seek already includes this frame's elapsed ticks, including any LOD accumulation.
        if (_presentationPhaseSought)
        {
            dt = 0f;
            _presentationPhaseSought = false;
        }
        _actionCarryRemaining = Math.Max(0f, _actionCarryRemaining - dt);
        if (_presentationAction == null) return;
        _presentationTime += dt;
        if (_presentationTime >= _presentationDuration)
        {
            if (_presentationAction == "release") _loose = -1f;
            _presentationAction = null;
            _reload = _pistolReload = _useTime = -1;
            _use = UseMotion.None;
        }
        else
        {
            float phase = _presentationTime / _presentationDuration;
            if (_presentationAction == "reload") _reload = _pistolReload = phase;
            if (_presentationAction == "draw") _draw = phase;
            if (_presentationAction == "release") _loose = _presentationTime;
            if (_use != UseMotion.None) _useTime = _presentationTime;
        }
    }

    private HeldPresentationKey PresentationKey(HeldPresentationProfile profile)
    {
        string action = _previewAction ?? _presentationAction ??
            (_frame.Aiming && _itemModel?.LeftHanded == true ? "draw" : _frame.Aiming && profile.Actions.ContainsKey("aim") ? "aim" : "idle");
        if (!profile.Actions.TryGetValue(action, out HeldPresentationClip? clip)) clip = profile.Actions["idle"];
        float phase = _previewAction != null ? _previewPhase : _presentationAction != null ? _presentationTime / _presentationDuration : action == "draw" ? _draw : action == "aim" ? 1f : 0f;
        if (_previewAction == null && action == "swing" && _itemModel != null)
        {
            float duration = Math.Max(HeldActionTiming.Seconds(1), HeldActionTiming.Quantize(_itemModel.SwingTime));
            phase = clip.AlignMarker(phase, HeldActionTiming.Quantize(_itemModel.StrikeTime) / duration);
        }
        else if (_previewAction == null && _presentationAction != null && HeldActionTiming.AnticipationSeconds(action) > 0)
            phase = clip.AlignMarker(phase, HeldActionTiming.CommitFraction(action, _presentationDuration));
        return clip.Sample(phase, profile.RightHand.Fingers, profile.LeftHand.Fingers);
    }

    private void ApplyHeldPresentation()
    {
        if (_item == null || _itemModel == null || CurrentPresentation is not { } profile || Lying || _stance == CharacterStance.Sit) return;
        if (_presentationRevision != HeldPresentations.Revision)
        {
            _presentationRevision = HeldPresentations.Revision;
            (float along, float lift) = HandPoses.Seat(profile.RightHand.Seat);
            _gripR = HandFrame.Measure(_skeleton, true, along, lift);
            (along, lift) = HandPoses.Seat(profile.LeftHand.Seat);
            _gripL = HandFrame.Measure(_skeleton, false, along, lift);
            // A one-handed item stays attached at the edited grip, including while the editor is open.
            if (_item.GetParent() is BoneAttachment3D)
            {
                Transform3D grip = HeldItems.Grip(_item, _itemModel);
                _item.Transform = HandFrame.HandFor(grip, HandFrame.PowerGrip, _itemModel.LeftHanded ? _gripL : _gripR).AffineInverse();
            }
        }
        HeldPresentationKey key = _sampledPresentation = PresentationKey(profile);
        Vector3 anchor = _skeleton.GetBoneGlobalPose(_skeleton.FindBone("spine_03")).Origin;
        bool left = _itemModel.LeftHanded;
        Transform3D item;
        Transform3D primaryGrip = HeldItems.Grip(_item, _itemModel);
        if (_item.GetParent() == _carry)
        {
            item = _item.Transform;
        }
        else
        {
            // Derive the model from the desired hand, never from yesterday's bone-attachment transform.
            Transform3D hand = left ? _poser.LeftHand : _poser.RightHand;
            if (_held == HeldKind.Pistol)
            {
                Basis forward = new Basis(Vector3.Left, _frame.Pitch) * new Basis(Vector3.Back, Vector3.Up, Vector3.Left);
                var at = new Transform3D(forward, anchor + new Vector3(-0.17f, 0.08f, 0.48f));
                hand = HandFrame.HandFor(at, HandFrame.PowerGrip, _gripR);
            }
            Transform3D handInItem = HandFrame.HandFor(primaryGrip, HandFrame.PowerGrip, left ? _gripL : _gripR);
            item = hand * handInItem.AffineInverse();
        }
        // Translation authors the arm's reach; rotation turns the tool about its grip without undoing that reach.
        Vector3 pivot = (item * primaryGrip).Origin;
        Basis turn = Basis.FromEuler(key.ThirdPersonRotation * (MathF.PI / 180f));
        var motion = new Transform3D(turn, pivot - turn * pivot + key.ThirdPersonPosition);
        Transform3D change = profile.ThirdPerson.Transform * motion;
        Transform3D transformed = change * item;
        if (_item.GetParent() == _carry) _item.Transform = transformed;

        Transform3D primary = HandFrame.HandFor(transformed * primaryGrip, HandFrame.PowerGrip, left ? _gripL : _gripR);
        if (left)
        {
            _poser.LeftHand = primary;
            _poser.RightHand = change * _poser.RightHand;
        }
        else
        {
            _poser.RightHand = primary;
            _poser.LeftHand = change * _poser.LeftHand;
            _poser.RightHandWeight = 1f;
            if (_held == HeldKind.Pistol)
            {
                Transform3D support = (HeldItems.HasSocket(_item, "grip_l") ? HeldItems.Socket(_item, "grip_l") : primaryGrip.Translated(new Vector3(0.025f, -0.025f, 0f))) * profile.LeftHand.GripOffset.Transform;
                Basis supportFrame = _itemModel.SupportFrame;
                if (_pistolReload >= 0f)
                {
                    // The same normalized choreography as first person: well, pouch, insertion, slide, support.
                    Vector4 weights = ViewmodelMotion.ReloadHand(_pistolReload);
                    Vector3 well = primaryGrip.Origin + new Vector3(-0.01f, -0.1f, 0f);
                    _reloadMagazineHand = HandFrame.HandFor(new Transform3D(support.Basis, well), HandFrame.PowerGrip, _gripL) *
                        profile.LeftHand.WristOffset.Transform;
                    supportFrame = ViewmodelMotion.ReloadGrip(supportFrame, _pistolReload);
                    Vector3 pouch = transformed.AffineInverse() * (anchor + new Vector3(0.15f, -0.2f, 0.16f));
                    Vector3 slide = HeldItems.HasSocket(_item, "sight")
                        ? HeldItems.Socket(_item, "sight").Origin + new Vector3(0.02f, -0.012f, 0f)
                        : primaryGrip.Origin + new Vector3(0.02f, 0.08f, 0f);
                    Vector3 pull = new Vector3(-0.2f, 0f, 0f) * Math.Max(0f, weights.W - 1f);
                    support.Origin = support.Origin * weights.X + well * weights.Y + pouch * weights.Z +
                        (slide + pull) * Math.Min(1f, weights.W);
                }
                _poser.LeftHand = HandFrame.HandFor(transformed * support, supportFrame, _gripL);
                _poser.LeftHandWeight = 1f;
            }
        }
        _poser.RightHand = OffsetHand(_poser.RightHand, profile.RightHand, key.RightHandPosition, key.RightHandRotation);
        _poser.LeftHand = OffsetHand(_poser.LeftHand, profile.LeftHand, key.LeftHandPosition, key.LeftHandRotation);
        // The art's camera-space poles are converted to the full body's axes (+Z forward, -X right).
        _poser.RightPole = new Vector3(-Math.Abs(profile.RightHand.Pole.X), profile.RightHand.Pole.Y, -profile.RightHand.Pole.Z);
        _poser.LeftPole = new Vector3(Math.Abs(profile.LeftHand.Pole.X), profile.LeftHand.Pole.Y, -profile.LeftHand.Pole.Z);
    }

    private static Transform3D OffsetHand(Transform3D hand, HeldPresentationHand profile, Vector3 position, Vector3 rotation)
    {
        hand *= profile.WristOffset.Transform;
        return new Transform3D(Basis.FromEuler(rotation * (MathF.PI / 180f)) * hand.Basis, hand.Origin + position);
    }

    private void PoseHeldFingers()
    {
        if (CurrentPresentation is not { } profile || Lying || _stance == CharacterStance.Sit) return;
        OffsetShoulder(true, profile.RightHand.Shoulder - new Vector3(-0.03f, -0.1f, -0.06f));
        OffsetShoulder(false, profile.LeftHand.Shoulder - new Vector3(0.1f, -0.12f, -0.08f));
        HandPoses.Apply(_skeleton, _itemModel!.Fingers, true, radius: profile.RightHand.Radius, seat: profile.RightHand.Seat,
            curlDegrees: _sampledPresentation?.RightFingers.Length > 0 ? _sampledPresentation.RightFingers : profile.RightHand.Fingers);
        if (_poser.LeftHandWeight > 0.01f)
            HandPoses.Apply(_skeleton, HandPoses.Grip.Support, false, radius: profile.LeftHand.Radius, seat: profile.LeftHand.Seat,
                curlDegrees: _sampledPresentation?.LeftFingers.Length > 0 ? _sampledPresentation.LeftFingers : profile.LeftHand.Fingers);
    }

    private void OffsetShoulder(bool right, Vector3 cameraDelta)
    {
        int bone = _skeleton.FindBone(right ? "upperarm_r" : "upperarm_l");
        int parent = _skeleton.GetBoneParent(bone);
        Vector3 bodyDelta = new(-cameraDelta.X, cameraDelta.Y, -cameraDelta.Z);
        Basis parentFrame = parent >= 0 ? _skeleton.GetBoneGlobalPose(parent).Basis : Basis.Identity;
        _skeleton.SetBonePosePosition(bone, _skeleton.GetBoneRest(bone).Origin + parentFrame.Inverse() * bodyDelta);
    }
}
