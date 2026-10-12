using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// The animation graph every third-person character shares (GROUND.md §4.8). One <see cref="AnimationNodeBlendTree"/>
/// resource; each character's <see cref="AnimationTree"/> holds its own parameters.
/// <code>
/// stand / crouch / swim speed blends → stance → rate → air (fall) → jump, land
///   → hold_r (right arm: tool, pistol, rifle pose) → hold_l (left arm and chest: pistol, rifle pose)
///   → action (upper body: swing, punch, fire, reload) → hit (upper body flinch) → output
/// </code>
/// Parameter names are cached <see cref="StringName"/>s: building one from a string every frame for every character
/// would feed the garbage collector.
/// Directional movement comes from <see cref="CharacterPoser"/>, which turns the hips towards the direction of travel
/// and the chest back towards the aim (the clips only run forward).
/// </summary>
public static class CharacterAnimator
{
    public static readonly StringName Stance = "parameters/stance/transition_request";
    public static readonly StringName StandSpeed = "parameters/stand/blend_position";
    public static readonly StringName CrouchSpeed = "parameters/crouch/blend_position";
    public static readonly StringName SwimSpeed = "parameters/swim/blend_position";
    public static readonly StringName Rate = "parameters/rate/scale";
    public static readonly StringName Air = "parameters/air/blend_amount";
    public static readonly StringName Jump = "parameters/jump/request";
    public static readonly StringName Land = "parameters/land/request";
    public static readonly StringName HeldRight = "parameters/held_r/transition_request";
    public static readonly StringName HeldLeft = "parameters/held_l/transition_request";
    public static readonly StringName HoldRight = "parameters/hold_r/blend_amount";
    public static readonly StringName HoldLeft = "parameters/hold_l/blend_amount";
    public static readonly StringName ActionClip = "parameters/action_clip/transition_request";
    public static readonly StringName Action = "parameters/action/request";
    public static readonly StringName ActionActive = "parameters/action/active";
    public static readonly StringName HitClip = "parameters/hit_clip/transition_request";
    public static readonly StringName Hit = "parameters/hit/request";

    /// <summary>Speeds (m/s) the stand blend places its clips at: the movement code's crouch, walk and sprint.</summary>
    public static readonly (string Clip, float Speed)[] StandPoints = { ("idle", 0f), ("walk", 1.25f), ("jog", 2.8f), ("sprint", 5.5f) };

    public static readonly (string Clip, float Speed)[] CrouchPoints = { ("crouch_idle", 0f), ("crouch_walk", 1.3f) };

    public static readonly (string Clip, float Speed)[] SwimPoints = { ("swim_idle", 0f), ("swim", 2.0f) };

    /// <summary>Where one chop starts in the chopping loop (its wind-up), seconds into the clip.</summary>
    public const float ChopWindUp = 0.6f;

    /// <summary>How much faster than the clip a chop plays.</summary>
    public const float ChopRate = 1.3f;

    /// <summary>When the chop fades out, seconds after it starts (after the follow-through).</summary>
    public const float ChopFadeAt = 0.42f;

    /// <summary>Clips for one-off actions, by name in the action transition.</summary>
    public static readonly string[] ActionClips = { "tool_swing", "chop", "punch_jab", "pistol_fire", "pistol_reload", "throw" };

    private static AnimationNodeBlendTree? _tree;

    public static AnimationNodeBlendTree Tree => _tree ??= Build();

    /// <summary>Highest speed of the stand blend; faster movement plays it faster.</summary>
    public static float TopSpeed => StandPoints[^1].Speed;

    public static readonly string[] RightArm = Bones("clavicle_r", "upperarm_r", "lowerarm_r", "hand_r", Fingers("r"));

    public static readonly string[] LeftArmAndChest = Bones("clavicle_l", "upperarm_l", "lowerarm_l", "hand_l", Fingers("l"), "spine_03");

    public static readonly string[] UpperBody = Bones("spine_02", "spine_03", "neck_01", "head", RightArm, LeftArmAndChest);

    private static AnimationNodeBlendTree Build()
    {
        var t = new AnimationNodeBlendTree();
        t.AddNode("stand", Blend1D(StandPoints), new Vector2(0, 0));
        t.AddNode("crouch", Blend1D(CrouchPoints), new Vector2(0, 150));
        t.AddNode("swim", Blend1D(SwimPoints), new Vector2(0, 300));
        var stance = new AnimationNodeTransition { XfadeTime = 0.22f };
        foreach (string s in new[] { "stand", "crouch", "swim" })
        {
            stance.AddInput(s);
        }
        t.AddNode("stance", stance, new Vector2(250, 150));
        t.ConnectNode("stance", 0, "stand");
        t.ConnectNode("stance", 1, "crouch");
        t.ConnectNode("stance", 2, "swim");
        t.AddNode("rate", new AnimationNodeTimeScale(), new Vector2(450, 150));
        t.ConnectNode("rate", 0, "stance");

        t.AddNode("fall", Clip("fall"), new Vector2(450, 300));
        t.AddNode("air", new AnimationNodeBlend2(), new Vector2(650, 150));
        t.ConnectNode("air", 0, "rate");
        t.ConnectNode("air", 1, "fall");
        t.AddNode("jump_clip", Clip("jump_start"), new Vector2(650, 300));
        t.AddNode("jump", new AnimationNodeOneShot { FadeInTime = 0.06f, FadeOutTime = 0.25f }, new Vector2(850, 150));
        t.ConnectNode("jump", 0, "air");
        t.ConnectNode("jump", 1, "jump_clip");
        t.AddNode("land_clip", Clip("jump_land"), new Vector2(850, 300));
        var land = new AnimationNodeOneShot { FadeInTime = 0.05f, FadeOutTime = 0.3f };
        t.AddNode("land", land, new Vector2(1050, 150));
        t.ConnectNode("land", 0, "jump");
        t.ConnectNode("land", 1, "land_clip");

        // Held poses: the right arm for anything held, the left arm and chest for two-handed things.
        var heldR = new AnimationNodeTransition { XfadeTime = 0.2f };
        t.AddNode("held_r", heldR, new Vector2(1050, 300));
        AddInputs(t, "held_r", heldR, ("tool", "torch_idle"), ("pistol", "pistol_idle"), ("rifle", "pistol_idle"));
        var holdR = new AnimationNodeBlend2 { FilterEnabled = true };
        Filter(holdR, RightArm);
        t.AddNode("hold_r", holdR, new Vector2(1250, 150));
        t.ConnectNode("hold_r", 0, "land");
        t.ConnectNode("hold_r", 1, "held_r");
        var heldL = new AnimationNodeTransition { XfadeTime = 0.2f };
        t.AddNode("held_l", heldL, new Vector2(1250, 300));
        AddInputs(t, "held_l", heldL, ("pistol", "pistol_idle"), ("rifle", "pistol_idle"));
        var holdL = new AnimationNodeBlend2 { FilterEnabled = true };
        Filter(holdL, LeftArmAndChest);
        t.AddNode("hold_l", holdL, new Vector2(1450, 150));
        t.ConnectNode("hold_l", 0, "hold_r");
        t.ConnectNode("hold_l", 1, "held_l");

        // One-off actions and flinches on the upper body.
        var actionClip = new AnimationNodeTransition { XfadeTime = 0f };
        t.AddNode("action_clip", actionClip, new Vector2(1450, 300));
        AddInputs(t, "action_clip", actionClip, Array.ConvertAll(ActionClips, c => (c, c)));
        // The chop is a loop that starts mid-swing: it starts at its wind-up, a little faster than the clip, and the
        // character fades it out after the follow-through (CharacterView, ChopFadeAt).
        if (t.GetNode("action_clip_chop") is AnimationNodeAnimation chop && CharacterAssets.Clips.TryGetValue("chop", out ClipInfo? info))
        {
            chop.UseCustomTimeline = true;
            chop.StartOffset = ChopWindUp;
            chop.TimelineLength = info.Length / ChopRate;
            chop.StretchTimeScale = true;
            chop.LoopMode = Animation.LoopModeEnum.Linear;
        }
        var action = new AnimationNodeOneShot { FadeInTime = 0.08f, FadeOutTime = 0.22f, FilterEnabled = true, BreakLoopAtEnd = true };
        Filter(action, UpperBody);
        t.AddNode("action", action, new Vector2(1650, 150));
        t.ConnectNode("action", 0, "hold_l");
        t.ConnectNode("action", 1, "action_clip");
        var hitClip = new AnimationNodeTransition { XfadeTime = 0f };
        t.AddNode("hit_clip", hitClip, new Vector2(1650, 300));
        AddInputs(t, "hit_clip", hitClip, ("chest", "hit_chest"), ("head", "hit_head"));
        var hit = new AnimationNodeOneShot { FadeInTime = 0.04f, FadeOutTime = 0.2f, FilterEnabled = true };
        Filter(hit, UpperBody);
        t.AddNode("hit", hit, new Vector2(1850, 150));
        t.ConnectNode("hit", 0, "action");
        t.ConnectNode("hit", 1, "hit_clip");
        t.ConnectNode("output", 0, "hit");
        return t;
    }

    private static AnimationNodeBlendSpace1D Blend1D((string Clip, float Speed)[] points)
    {
        var space = new AnimationNodeBlendSpace1D { MinSpace = 0f, MaxSpace = points[^1].Speed, SyncMode = AnimationNodeBlendSpace1D.SyncModeEnum.CyclicMutable };
        foreach ((string clip, float speed) in points)
        {
            space.AddBlendPoint(Clip(clip, speed), speed, -1, clip);
        }
        return space;
    }

    /// <summary>A clip; placed at a speed, it is stretched so its feet keep up with the ground at that speed.</summary>
    private static AnimationNodeAnimation Clip(string name, float speed = 0f)
    {
        var node = new AnimationNodeAnimation { Animation = name };
        if (speed > 0f && CharacterAssets.Clips.TryGetValue(name, out ClipInfo? info) && info.GroundSpeed > 0f)
        {
            float rate = Math.Clamp(speed / info.GroundSpeed, 0.7f, 1.45f);
            node.UseCustomTimeline = true;
            node.TimelineLength = info.Length / rate;
            node.StretchTimeScale = true;
            node.LoopMode = Animation.LoopModeEnum.Linear;
        }
        return node;
    }

    private static void AddInputs(AnimationNodeBlendTree t, string transitionName, AnimationNodeTransition transition,
        params (string Input, string Clip)[] inputs)
    {
        for (int i = 0; i < inputs.Length; i++)
        {
            transition.AddInput(inputs[i].Input);
            string node = transitionName + "_" + inputs[i].Input;
            t.AddNode(node, Clip(inputs[i].Clip), new Vector2(0, 450 + 60 * i));
            t.ConnectNode(transitionName, i, node);
        }
    }

    private static void Filter(AnimationNode node, IEnumerable<string> bones)
    {
        foreach (string bone in bones)
        {
            node.SetFilterPath(new NodePath("Skeleton:" + bone), true);
        }
    }

    private static string[] Fingers(string side)
    {
        var list = new List<string>();
        foreach (string f in new[] { "thumb", "index", "middle", "ring", "pinky" })
        {
            for (int k = 1; k <= 3; k++)
            {
                list.Add($"{f}_0{k}_{side}");
            }
        }
        return list.ToArray();
    }

    private static string[] Bones(params object[] items)
    {
        var list = new List<string>();
        foreach (object item in items)
        {
            if (item is string s)
            {
                list.Add(s);
            }
            else if (item is IEnumerable<string> many)
            {
                list.AddRange(many);
            }
        }
        return list.ToArray();
    }
}
