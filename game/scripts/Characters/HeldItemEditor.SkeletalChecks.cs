using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Simulation.Content;

namespace HollowUniverse.Characters;

public partial class HeldItemEditor
{
    private static readonly string[] SourceArmBones =
        { "upperarm_r", "lowerarm_r", "hand_r", "upperarm_l", "lowerarm_l", "hand_l" };

    private static Vector3 EditorPlacementPosition(HeldPresentationProfile profile, string action) =>
        profile.Actions[action].Skeletal != null ? profile.FirstPersonRig?.Position ?? Vector3.Zero : profile.FirstPerson.Position;

    private static void CheckSkeletalRoundTrip(IReadOnlyDictionary<string, HeldPresentationProfile> saved)
    {
        foreach ((string item, HeldPresentationProfile before) in saved)
        {
            HeldPresentationProfile after = HeldPresentations.Get(item);
            Require((before.FirstPersonRig == null) == (after.FirstPersonRig == null), item + ": save/reload must preserve optional FPS rig placement.");
            if (before.FirstPersonRig is { } rig)
                Require(after.FirstPersonRig!.Position.IsEqualApprox(rig.Position) && after.FirstPersonRig.Rotation.IsEqualApprox(rig.Rotation),
                    item + ": save/reload must preserve the full FPS rig placement.");
            foreach ((string action, HeldPresentationClip clip) in before.Actions)
            {
                HeldSkeletalClip? source = after.Actions[action].Skeletal;
                Require((clip.Skeletal == null) == (source == null), $"{item}/{action}: save/reload must preserve optional source motion.");
                if (clip.Skeletal is { } expected)
                    Require(source!.Clip == expected.Clip && source.Start == expected.Start && source.End == expected.End && source.Wrap == expected.Wrap,
                        $"{item}/{action}: save/reload must preserve the source clip, trim and loop boundary.");
            }
        }
    }

    private void CheckSkeletalSourcePose(int camera, float artPhase, string context)
    {
        HeldSkeletalClip source = Clip.Skeletal ?? throw new InvalidOperationException("Source check requires a bound action.");
        bool playing = camera == 2 ? _character.UsingSkeletalMotion : _viewmodel.UsingSkeletalMotion;
        string? error = camera == 2 ? _character.SkeletalMotionError : _viewmodel.SkeletalMotionError;
        string? playedClip = camera == 2 ? _character.SkeletalMotionClip : _viewmodel.SkeletalMotionClip;
        double sampledTime = camera == 2 ? _character.SkeletalSourceTime : _viewmodel.SkeletalSourceTime;
        Require(playing && playedClip == source.Clip && string.IsNullOrEmpty(error),
            $"{context}: configured source '{source.Clip}' must actually play, without fallback ({error ?? "no source sampled"}).");
        AnimationLibrary library = CharacterAssets.For(_body).Library;
        Require(library.HasAnimation(source.Clip), context + ": source clip is absent from this body's library.");
        Animation animation = library.GetAnimation(source.Clip);
        // Independently check the time mapping, including a source cycle beginning away from the loop boundary.
        double sourcePhase = source.Wrap
            ? source.Start + artPhase * (1d - source.Start + source.End)
            : source.Start * (1d - artPhase) + source.End * artPhase;
        if (source.Wrap && sourcePhase >= 1d) sourcePhase -= 1d;
        Require(Math.Abs(sampledTime - sourcePhase * animation.Length) < 0.00001d,
            $"{context}: sampled source time {sampledTime:0.000000} does not match selected phase {sourcePhase:0.000000}.");
        Skeleton3D skeleton = camera == 2 ? _character.Skeleton : FindCheckSkeleton(_viewmodel);
        float maximum = 0;
        foreach (string name in SourceArmBones)
        {
            int bone = skeleton.FindBone(name);
            Require(bone >= 0, context + ": missing source arm bone " + name);
            int track = -1;
            for (int i = 0; i < animation.GetTrackCount(); i++)
                if (animation.TrackGetType(i) == Animation.TrackType.Rotation3D &&
                    animation.TrackGetPath(i).GetConcatenatedSubNames() == name) { track = i; break; }
            Require(track >= 0, context + ": source clip is missing the arm rotation track " + name);
            Quaternion expected = animation.RotationTrackInterpolate(track, sampledTime).Normalized();
            Quaternion actual = skeleton.GetBonePoseRotation(bone).Normalized();
            float dot = Math.Clamp(MathF.Abs(actual.Dot(expected)), 0f, 1f);
            float degrees = 2f * MathF.Acos(dot) * 180f / MathF.PI;
            maximum = Math.Max(maximum, degrees);
            Require(degrees <= 0.1f, $"{context}: {name} differs from the source by {degrees:0.000} degrees; an arm layer is replacing source motion.");
        }
        GD.Print($"[held-source-fidelity] {context} clip={source.Clip} sourceTime={sampledTime:0.000000}s maxArmError={maximum:0.000}deg");
    }

    private static Skeleton3D FindCheckSkeleton(Node node)
    {
        if (node is Skeleton3D skeleton) return skeleton;
        foreach (Node child in node.GetChildren())
            if (child is Skeleton3D found) return found;
        foreach (Node child in node.GetChildren())
        {
            Skeleton3D? found = child.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
            if (found != null) return found;
        }
        throw new InvalidOperationException("The FPS renderer has no skeleton to verify.");
    }

    private async Task CheckSkeletalCurveIsolation()
    {
        HeldPresentationProfile saved = HeldPresentations.Clone(_profile);
        float phase = 0.37f;
        try
        {
            HeldPresentationProfile distorted = HeldPresentations.Clone(_profile);
            distorted.FirstPerson.Position += new Vector3(0.3f, -0.2f, 0.1f);
            distorted.FirstPerson.Rotation += new Vector3(31f, -42f, 53f);
            distorted.ThirdPerson.Position += new Vector3(-0.25f, 0.2f, 0.15f);
            distorted.ThirdPerson.Rotation += new Vector3(-32f, 41f, 52f);
            foreach (HeldPresentationHand hand in new[] { distorted.RightHand, distorted.LeftHand })
            {
                hand.Shoulder += new Vector3(0.1f, 0.1f, 0.1f);
                hand.WristOffset.Position += new Vector3(0.1f, -0.1f, 0.1f);
                hand.WristOffset.Rotation += new Vector3(30f, -40f, 50f);
            }
            foreach (HeldPresentationKey key in distorted.Actions[_action].Keys)
            {
                key.Position += new Vector3(0.3f, 0.2f, 0.1f);
                key.Rotation += new Vector3(35f, -45f, 55f);
                key.ThirdPersonPosition += new Vector3(-0.3f, 0.2f, -0.1f);
                key.ThirdPersonRotation += new Vector3(-35f, 45f, -55f);
                key.RightHandPosition += new Vector3(0.1f, 0.1f, 0.1f);
                key.LeftHandPosition -= new Vector3(0.1f, 0.1f, 0.1f);
                key.RightHandRotation += new Vector3(30f, -40f, 50f);
                key.LeftHandRotation += new Vector3(-30f, 40f, -50f);
            }
            HeldPresentations.Replace(distorted);
            _profile = distorted;
            RefreshPreview();
            foreach (int camera in new[] { 0, 2 })
            {
                SetView(camera); SetPhase(phase);
                await Settle(2);
                CheckSkeletalSourcePose(camera, phase, $"{_held.Id}/{_action}/{camera}/legacy-curve-isolation");
            }
        }
        finally
        {
            HeldPresentations.Replace(saved);
            _profile = saved;
            RefreshPreview();
        }
    }

    private void CheckSkeletalGameplayTiming()
    {
        if (_action != "swing" || _content.Item(_held.Id)?.Melee is not { } melee) return;
        float duration = HeldActionTiming.Duration(_content.Item(_held.Id), _action, Clip.Duration);
        float gameplayPhase = HeldActionTiming.StrikeFraction(melee);
        Require(Clip.Marker > 0 && Clip.Marker < 1, "A bound melee clip needs an authored contact marker.");
        try
        {
            _viewmodel.ClearPreview();
            _character.ClearPreview();
            _viewmodel.PlayAction(_action, duration, gameplayPhase);
            _character.PlayAction(_action, duration, gameplayPhase);
            // Sample the real gameplay entry points without advancing elapsed time. No synthetic hit is emitted.
            _viewmodel._Process(0);
            _character._Process(0);
            CheckSkeletalSourcePose(0, Clip.Marker, $"{_held.Id}/{_action}/FPS/gameplay-strike");
            CheckSkeletalSourcePose(2, Clip.Marker, $"{_held.Id}/{_action}/TPS/gameplay-strike");
            GD.Print($"[held-source-timing] {_held.Id}/{_action} gameplayStrike={HeldActionTiming.Seconds(melee.StrikeTicks):0.000}s duration={duration:0.000}s mapsToArt={Clip.Marker:0.000000}");
        }
        finally { SamplePreview(); }
    }

    private string CaptureSourceCaption() => Clip.Skeletal is { } source ? $"\nSource: {source.Clip}" : "";
}
