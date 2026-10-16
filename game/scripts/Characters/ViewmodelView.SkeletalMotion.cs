using Godot;

namespace HollowUniverse.Characters;

public partial class ViewmodelView
{
    private Transform3D _defaultRigTransform = Transform3D.Identity;
    private readonly HeldSkeletalMotion _heldSkeletalMotion = new();

    public bool UsingSkeletalMotion { get; private set; }
    public string? SkeletalMotionClip => UsingSkeletalMotion ? _heldSkeletalMotion.AppliedClip : null;
    public string? SkeletalMotionError { get; private set; }
    public double SkeletalSourceTime => _heldSkeletalMotion.SourceTime;

    private bool PoseSkeletalMotion()
    {
        UsingSkeletalMotion = false;
        _rig.Transform = _defaultRigTransform;
        if (_model == null || _item == null ||
            !_model.Presentation.Actions.TryGetValue(ActiveAction, out HeldPresentationClip? clip) ||
            clip.Skeletal is not { } skeletal)
        {
            SkeletalMotionError = null;
            ShowSourceArms(false);
            return false;
        }
        if (!_heldSkeletalMotion.Apply(_skeleton, _assets.Library, skeletal, _presentationSample.At))
        {
            string error = _heldSkeletalMotion.LastError;
            if (SkeletalMotionError != error) GD.PushWarning($"Held animation '{skeletal.Clip}' could not play: {error}");
            SkeletalMotionError = error;
            SynchronizeAction(allowSkeletal: false);
            ShowSourceArms(false);
            return false;
        }

        _rig.Transform = (_model.Presentation.FirstPersonRig?.Transform ?? Transform3D.Identity) * _defaultRigTransform;
        UsingSkeletalMotion = true;
        ShowSourceArms(true);
        SkeletalMotionError = null;
        AuthoredReachCorrection = OpticShoulderAccommodation = 0;
        PrimaryHandAttachmentError = SecondaryHandAttachmentError = 0;

        // Invert the same measured grip used by the full body. The item follows the source wrist;
        // no IK target, authored wrist offset, shoulder displacement or item curve alters its motion.
        bool right = !_model.LeftHanded;
        int hand = right ? _handR : _handL;
        var measured = right ? _palmR : _gripL;
        Transform3D handInItem = HandFrame.HandFor(_grip, HandFrame.PowerGrip, measured);
        _item.Transform = _rig.Transform * _skeleton.Transform * _skeleton.GetBoneGlobalPose(hand) * handInItem.AffineInverse();
        _item.Visible = PrimaryHandShouldAttach;
        AnimateParts();

        // Fitting the holding fingers does not change the wrist, elbow, shoulder or free-arm performance.
        HeldPresentationHand grip = right ? _model.Presentation.RightHand : _model.Presentation.LeftHand;
        HandPoses.Apply(_skeleton, _model.Fingers, right, radius: grip.Radius, seat: grip.Seat, curlDegrees: grip.Fingers);
        return true;
    }

    private void ShowSourceArms(bool visible)
    {
        if (_sourceArmsVisible == visible) return;
        _sourceArmsVisible = visible;
        ApplyRegions();
    }
}
