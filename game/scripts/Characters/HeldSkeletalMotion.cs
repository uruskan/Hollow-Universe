using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>Samples the already-retargeted character animation library for held actions. Source rotations drive
/// the bones directly: the caller attaches the item to the resulting hand, rather than solving an arm to an item
/// curve. Each view owns a sampler so cached skeleton references live no longer than that view.</summary>
public sealed class HeldSkeletalMotion
{
    private static readonly char[] InvalidClipCharacters = { '/', '\\', ':', '[', ']', ',' };
    private sealed class BoundAnimation
    {
        public Animation? Animation;
        public (int Track, int Bone)[] Rotations = Array.Empty<(int, int)>();
        public string Error = "";
    }

    private readonly Dictionary<(Skeleton3D Skeleton, AnimationLibrary Library, string Clip, bool UpperBody), BoundAnimation> _cache = new();

    public string LastError { get; private set; } = "";
    public string AppliedClip { get; private set; } = "";
    public int AppliedTrackCount { get; private set; }
    /// <summary>Normalized source clip phase after trimming, rather than the gameplay action phase.</summary>
    public float SourcePhase { get; private set; }
    public double SourceTime { get; private set; }

    internal static bool ValidBinding(HeldSkeletalClip binding) =>
        !string.IsNullOrWhiteSpace(binding.Clip) && binding.Clip.Length <= 128 &&
        binding.Clip.IndexOfAny(InvalidClipCharacters) < 0 &&
        float.IsFinite(binding.Start) && float.IsFinite(binding.End) &&
        binding.Start >= 0f && binding.Start <= 1f && binding.End >= 0f && binding.End <= 1f &&
        (binding.Wrap ? binding.End <= binding.Start : binding.Start <= binding.End);

    /// <summary>Apply an artist-selected source segment at an already marker-aligned action phase. The source
    /// bone rotations are absolute local poses, as used by Godot's AnimationTree. Pelvis translation/root motion
    /// are deliberately excluded. Upper-body mode starts at spine_01 and leaves pelvis and leg locomotion intact.
    /// Missing or invalid bindings return false before any pose is changed; callers may use their fallback path.</summary>
    public bool Apply(Skeleton3D skeleton, AnimationLibrary library, HeldSkeletalClip binding, float phase,
        bool upperBodyOnly = false)
    {
        AppliedClip = "";
        AppliedTrackCount = 0;
        SourcePhase = 0f;
        SourceTime = 0;
        if (!float.IsFinite(phase) || !ValidBinding(binding))
        {
            LastError = "Skeletal motion requires a finite phase, simple clip name and a valid source segment in [0,1].";
            return false;
        }
        var key = (skeleton, library, binding.Clip, upperBodyOnly);
        if (!_cache.TryGetValue(key, out BoundAnimation? bound))
        {
            bound = Bind(skeleton, library, binding.Clip, upperBodyOnly);
            _cache.Add(key, bound);
        }
        if (bound.Animation == null)
        {
            LastError = bound.Error;
            return false;
        }

        if (binding.Wrap && bound.Animation.LoopMode == Animation.LoopModeEnum.None)
        {
            LastError = $"Skeletal clip '{binding.Clip}' cannot wrap because it is not a looping source clip.";
            return false;
        }
        float span = binding.End - binding.Start + (binding.Wrap ? 1f : 0f);
        SourcePhase = binding.Start + span * Math.Clamp(phase, 0f, 1f);
        if (binding.Wrap) SourcePhase %= 1f;
        SourceTime = SourcePhase * bound.Animation.Length;
        foreach ((int track, int bone) in bound.Rotations)
            skeleton.SetBonePoseRotation(bone, bound.Animation.RotationTrackInterpolate(track, SourceTime));
        LastError = "";
        AppliedClip = binding.Clip;
        AppliedTrackCount = bound.Rotations.Length;
        return true;
    }

    private static BoundAnimation Bind(Skeleton3D skeleton, AnimationLibrary library, string clip, bool upperBodyOnly)
    {
        if (!library.HasAnimation(clip)) return new BoundAnimation { Error = $"Skeletal clip '{clip}' is missing from this body's animation library." };
        Animation animation = library.GetAnimation(clip);
        if (!double.IsFinite(animation.Length) || animation.Length <= 0)
            return new BoundAnimation { Error = $"Skeletal clip '{clip}' has no usable duration." };
        int spine = upperBodyOnly ? skeleton.FindBone("spine_01") : -1;
        if (upperBodyOnly && spine < 0)
            return new BoundAnimation { Error = "The character skeleton has no spine_01 upper-body root." };
        var rotations = new List<(int, int)>();
        for (int track = 0; track < animation.GetTrackCount(); track++)
        {
            if (animation.TrackGetType(track) != Animation.TrackType.Rotation3D || !animation.TrackIsEnabled(track) ||
                animation.TrackGetKeyCount(track) == 0) continue;
            string name = animation.TrackGetPath(track).GetConcatenatedSubNames();
            int bone = skeleton.FindBone(name);
            if (bone < 0 || upperBodyOnly && !Within(skeleton, bone, spine)) continue;
            rotations.Add((track, bone));
        }
        return rotations.Count == 0
            ? new BoundAnimation { Error = $"Skeletal clip '{clip}' has no matching rotation tracks." }
            : new BoundAnimation { Animation = animation, Rotations = rotations.ToArray() };
    }

    private static bool Within(Skeleton3D skeleton, int bone, int root)
    {
        for (int ancestor = bone; ancestor >= 0; ancestor = skeleton.GetBoneParent(ancestor))
            if (ancestor == root) return true;
        return false;
    }
}
