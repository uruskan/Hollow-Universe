using System;
using System.Linq;
using Godot;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    /// <summary>Exercise additive inspection on the actual FPS renderer without changing authored profiles.</summary>
    private async void RunAdmireChecks()
    {
        int samples = 0, sourceItems = 0;
        try
        {
            SetPlaying(false);
            _aiming = false;
            SetView(0);
            for (int index = 0; index < HeldItems.All.Count; index++)
            {
                HeldItemModel item = HeldItems.All[index];
                SelectItem(item);
                SelectAction("idle"); SetPhase(0);
                await Settle(1);
                Require(_viewmodel.Model?.Id == item.Id, item.Id + ": selected model must be equipped.");
                Node3D held = _viewmodel.GetNode<Node3D>(item.Id);
                Skeleton3D skeleton = FindCheckSkeleton(_viewmodel);
                Transform3D rest = held.Transform;
                Transform3D skeletonRest = skeleton.GlobalTransform;
                Transform3D[] bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBoneGlobalPose).ToArray();
                int hand = skeleton.FindBone(item.LeftHanded ? "hand_l" : "hand_r");
                Require(hand >= 0, item.Id + ": primary hand bone must exist.");
                Transform3D attachment = AdmireHandToGrip(skeleton, hand);
                int otherHand = skeleton.FindBone(item.LeftHanded ? "hand_r" : "hand_l");
                bool supportAttached = _viewmodel.SecondaryHandShouldAttach;
                Require(otherHand >= 0, item.Id + ": support hand bone must exist.");
                Transform3D supportAttachment = AdmireHandToGrip(skeleton, otherHand);
                int[] shoulders = { skeleton.FindBone("upperarm_r"), skeleton.FindBone("upperarm_l") };
                Require(shoulders.All(bone => bone >= 0), item.Id + ": shoulder bones must exist.");
                float palmGap = _viewmodel.PrimaryPalmContactError;
                bool source = Clip.Skeletal != null;
                if (source) { CheckSkeletalSourcePose(0, 0, item.Id + "/admire-idle"); sourceItems++; }
                string? sourceClip = _viewmodel.SkeletalMotionClip;
                double sourceTime = _viewmodel.SkeletalSourceTime;
                SelectAction("admire");
                Require(_action == "admire", item.Id + ": admire action must be available.");
                bool moved = false;
                foreach (float phase in new[] { 0f, 0.22f, 0.4f, 0.63f, 0.78f, 1f })
                {
                    SetPhase(phase);
                    await Settle(1);
                    string context = $"{item.Id}/admire/{phase:0.00}";
                    CheckFinite(_viewmodel, context);
                    Require(_viewmodel.UsingSkeletalMotion == source && _viewmodel.SkeletalMotionError == null,
                        context + ": preserve the existing idle animation route.");
                    if (source)
                        Require(_viewmodel.SkeletalMotionClip == sourceClip && Math.Abs(_viewmodel.SkeletalSourceTime - sourceTime) < 0.00001,
                            context + ": preserve the sampled source idle.");
                    Require(AdmireNear(skeletonRest, skeleton.GlobalTransform), context + ": keep the FPS rig rooted.");
                    foreach (int shoulder in shoulders)
                        Require(bones[shoulder].Origin.DistanceTo(skeleton.GetBoneGlobalPose(shoulder).Origin) < 0.0002f,
                            context + ": keep shoulder origin fixed: " + skeleton.GetBoneName(shoulder));
                    Transform3D currentAttachment = AdmireHandToGrip(skeleton, hand);
                    Require(AdmireNear(attachment, currentAttachment, 0.002f),
                        context + $": preserve primary wrist-to-grip relationship (gap change {attachment.Origin.DistanceTo(currentAttachment.Origin):0.000000} m).");
                    if (supportAttached)
                        Require(AdmireNear(supportAttachment, AdmireHandToGrip(skeleton, otherHand), 0.002f),
                            context + ": preserve the attached support wrist's relationship to the item.");
                    Require(MathF.Abs(_viewmodel.PrimaryPalmContactError - palmGap) < 0.002f,
                        context + ": preserve palm contact distance within the arm solver's tolerance.");
                    if (phase is 0f or 1f)
                    {
                        Require(AdmireNear(rest, held.Transform), context + ": endpoints must return the item to the original idle.");
                        for (int bone = 0; bone < bones.Length; bone++)
                            Require(AdmireNear(bones[bone], skeleton.GetBoneGlobalPose(bone)),
                                context + ": endpoints must restore idle bone " + skeleton.GetBoneName(bone));
                    }
                    else moved |= !AdmireNear(rest, held.Transform);
                    samples++;
                }
                Require(moved, item.Id + ": inspection must visibly move the held item.");

                // Keep lifecycle checks synchronous: the editor otherwise reapplies its preview every frame.
                _viewmodel.ClearPreview();
                _viewmodel.Apply(new ViewmodelFrame { Grounded = true });
                _viewmodel.PlayAction("admire");
                _viewmodel._Process(Clip.Duration * 0.4);
                Require(_viewmodel.ActiveAction == "admire" && _viewmodel.ActivePhase > 0.39f && _viewmodel.PreviewAction == null,
                    item.Id + ": real gameplay playback must advance independently of editor preview.");
                CheckFinite(_viewmodel, item.Id + "/admire/playback");
                _viewmodel._Process(Clip.Duration + 0.01);
                Require(_viewmodel.ActiveAction == "idle", item.Id + ": completed admire must return to idle.");
                _viewmodel.PlayAction("admire");
                _viewmodel._Process(0.1);
                _viewmodel.CancelActions();
                _viewmodel._Process(0);
                Require(_viewmodel.ActiveAction == "idle", item.Id + ": cancellation must return to idle.");
                _viewmodel.PlayAction("admire");
                _viewmodel.PlayAction(_profile.UseAction);
                Require(_viewmodel.ActiveAction == _profile.UseAction, item.Id + ": ordinary use must replace admire immediately.");
                _viewmodel.PlayAction("admire");
                HeldItemModel next = HeldItems.All[(index + 1) % HeldItems.All.Count];
                _viewmodel.Hold(next.Kind, next.Id);
                Require(_viewmodel.Model?.Id == next.Id && _viewmodel.ActiveAction == "idle",
                    item.Id + ": changing held items must discard admire.");
            }
            GD.Print($"[admire-check] PASS: {HeldItems.All.Count} held items, {sourceItems} source idles, {samples} finite FPS samples; " +
                "idle endpoint fidelity, rooted rig and shoulders, attached grips, playback, completion, cancellation, use interruption and item switching.");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("[admire-check] FAIL: " + error);
            GetTree().Quit(1);
        }
    }

    private Transform3D AdmireHandToGrip(Skeleton3D skeleton, int hand) =>
        (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(hand)).AffineInverse() *
        (_viewmodel.PresentedGrip ?? throw new InvalidOperationException("Missing held grip."));

    private static bool AdmireNear(Transform3D a, Transform3D b, float positionTolerance = 0.0002f) =>
        a.Origin.DistanceTo(b.Origin) < positionTolerance && a.Basis.X.DistanceTo(b.Basis.X) < 0.0002f &&
        a.Basis.Y.DistanceTo(b.Basis.Y) < 0.0002f && a.Basis.Z.DistanceTo(b.Basis.Z) < 0.0002f;
}
