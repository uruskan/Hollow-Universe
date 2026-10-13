using System;
using Godot;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Content;
using HollowUniverse.Audio;
using HollowUniverse.Characters;
using HollowUniverse.Fx;
using HollowUniverse.Items;

namespace HollowUniverse.Animals;

/// <summary>
/// What an <see cref="AnimalView"/> draws, given every frame (GROUND.md §6.9): where the animal stands and faces, what it
/// is doing, how fast it goes, and its counters of attacks and hits, whose changes are its events. Positions are in the
/// view's parent space (render space). The ground game fills it from <c>GameClient.Animals</c>, the sandbox from a
/// local zone (<see cref="AnimalsView"/> does both).
/// </summary>
public struct AnimalFrame
{
    /// <summary>Who it is (the net ID on a client, the entity ID's low bits in a local zone).</summary>
    public uint Id;

    /// <summary>Its species' key (animals.json), which names its model.</summary>
    public string Species;

    /// <summary>The species' numbers when known (speeds, temperament); null draws it from the model's facts alone.</summary>
    public AnimalSpecies? Data;

    /// <summary>Its feet, on the ground.</summary>
    public Vector3 Position;

    /// <summary>Away from the planet's centre.</summary>
    public Vector3 Up;

    /// <summary>Heading, the characters' convention: 0 faces north (−Z laid flat), anticlockwise from above.</summary>
    public float Yaw;

    public AnimalState State;

    /// <summary>Speed over the ground, m/s: the gait and how fast it plays.</summary>
    public float Speed;

    /// <summary>Aware of a threat: head up.</summary>
    public bool Alert;

    /// <summary>Warning someone off: a fighter's growl, snort or roar, the bear rearing.</summary>
    public bool Warning;

    public bool Dead;

    /// <summary>Health as a fraction; a corpse's, the harvest left.</summary>
    public float Health;

    /// <summary>Attacks begun and hits taken (wrapping counts): each change is one.</summary>
    public byte AttackCount;

    public byte HurtCount;

    public AttackKind Attack;
}

/// <summary>
/// One animal (GROUND.md §6.9): its model (three levels of detail), a small state machine over the model's clips (idle,
/// graze, alert, threaten, eat; walk, trot and run played at the speed over the ground ÷ the clip's own, so the feet keep
/// up; attack, hurt and death as one-shots, the death pose held for the corpse), tilted along the way it climbs, with
/// its footsteps and voice through <see cref="AudioDirector"/> and its blood through <see cref="FxDirector"/>. Far away
/// its clips advance less often.
/// </summary>
public partial class AnimalView : Node3D
{
    /// <summary>Where the levels of detail change and where it goes: the full body to 22 m, the middle to 60 m, the lightest to 450 m.</summary>
    public static readonly float[] LodDistances = { 22f, 60f, 450f };

    private const float BlendSeconds = 0.25f;
    private const float MoveFrom = 0.25f, StopBelow = 0.12f;

    private Node3D _body = null!;
    private AnimationPlayer? _anim;
    private Skeleton3D? _skeleton;
    private int _chest = -1;
    private readonly System.Collections.Generic.List<MeshInstance3D> _antlers = new();
    private AnimalFrame _frame;
    private bool _has;
    private string _playing = "";
    private string? _oneShot;
    private double _oneShotLeft;
    private int _gait;
    private float _pitch;
    private Vector3 _lastPosition;
    private double _lastPhase = -1;
    private double _callIn;
    private double _bleedIn;
    private double _animDebt;
    private int _frameIndex;
    private Random _rng = new(1);

    public AnimalModel Model { get; private set; } = null!;

    public uint Id => _frame.Id;

    /// <summary>The frame drawn last.</summary>
    public AnimalFrame Frame => _frame;

    /// <summary>The clip playing (screenshots wait on it), and how long it has played.</summary>
    public string Playing => _playing;

    public double PlayingFor { get; private set; }

    /// <summary>Sounds for footsteps and voices (calls, alarms, threats, attacks, hurts, deaths); off for silent previews.</summary>
    public bool Sounds { get; set; } = true;

    /// <summary>A view for a species' model (add it to the tree, then <see cref="Apply"/> every frame).</summary>
    public static AnimalView Create(AnimalModel model)
    {
        var view = new AnimalView { Name = "Animal " + model.Key, Model = model };
        view._body = model.Scene.Instantiate<Node3D>();
        view._body.Name = "Model";
        view.AddChild(view._body);
        ModelCatalog.SetLodRanges(view._body, LodDistances);
        view._anim = Find<AnimationPlayer>(view._body);
        view._skeleton = Find<Skeleton3D>(view._body);
        view._chest = view._skeleton?.FindBone("chest") ?? -1;
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(view._body))
        {
            if (mesh.Name.ToString().StartsWith("Antlers", StringComparison.Ordinal))
            {
                view._antlers.Add(mesh);
            }
        }
        if (view._anim != null)
        {
            view._anim.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            foreach (AnimalClip clip in model.Clips.Values)
            {
                if (view._anim.HasAnimation(clip.Name))
                {
                    view._anim.GetAnimation(clip.Name).LoopMode = clip.Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                }
            }
        }
        else
        {
            GD.PushWarning($"Animal model {model.Key} has no AnimationPlayer.");
        }
        return view;
    }

    /// <summary>Forgets the animal drawn (a view taken from the spares for another of its species).</summary>
    public void Reset()
    {
        _has = false;
        _oneShot = null;
        _playing = "";
        _gait = 0;
        _pitch = 0;
        _lastPhase = -1;
        _anim?.Stop();
    }

    /// <summary>The head, where its voice comes from.</summary>
    public Vector3 HeadPosition => Transform * new Vector3(0, Model.Head.Y, -Model.Head.X);

    /// <summary>The middle of its body (blood, hit sounds): its chest as posed now, lying or standing.</summary>
    public Vector3 Middle => _skeleton != null && _chest >= 0
        ? _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_chest).Origin
        : _frame.Position + _frame.Up * (Model.Height * (_frame.Dead ? 0.25f : 0.72f));

    /// <summary>Draws a frame: <paramref name="eye"/> is the camera (far animals animate less often).</summary>
    public void Apply(in AnimalFrame f, double delta, Vector3 eye)
    {
        bool first = !_has;
        if (first)
        {
            _has = true;
            _frame = f;
            _lastPosition = f.Position;
            _rng = new Random((int)(f.Id * 2654435761u));
            _callIn = CallInterval() * _rng.NextDouble();
            // A quarter of them are males with antlers (a herd is mostly hinds), the same one every time.
            bool male = (f.Id * 2654435761u >> 20) % 4 == 0;
            foreach (MeshInstance3D antlers in _antlers)
            {
                antlers.Visible = male;
            }
            Transform = new Transform3D(CharacterFrame.BasisOf(f.Up, f.Yaw), f.Position);
            if (f.Dead)
            {
                // A corpse that comes into view lies as it fell.
                Play("Death", 0);
                if (_anim != null && _anim.HasAnimation("Death"))
                {
                    _anim.Seek(_anim.GetAnimation("Death").Length, true);
                }
                return;
            }
        }
        else
        {
            Events(f);
        }
        _frame = f;
        Place(f, delta);
        Animate(f, delta, eye, first);
        if (!f.Dead)
        {
            Voice(f, delta);
            Bleed(f, delta);
        }
    }

    // ---------------------------------------------------------------- events

    private void Events(in AnimalFrame f)
    {
        AnimalFrame was = _frame;
        if (f.Dead && !was.Dead)
        {
            _oneShot = null;
            Play("Death", 0.2f);
            Say("death");
            FxDirector.Play("blood.hit.heavy", Middle, -Forward(f), new FxParams { Direction = -Forward(f) });
            return;
        }
        if (f.Dead)
        {
            // Butchered: a little blood where the tool goes in.
            if (f.Health < was.Health - 0.004f)
            {
                Vector3 at = Middle;
                FxDirector.Play("blood.hit", at, f.Up, new FxParams { Direction = f.Up, Scale = 0.7f });
                FxDirector.Play("blood.drip", at - f.Up * (at - f.Position).Dot(f.Up), f.Up);
            }
            return;
        }
        if (f.AttackCount != was.AttackCount)
        {
            OneShot("Attack");
            Say("attack");
        }
        if (f.HurtCount != was.HurtCount)
        {
            Vector3 back = -Forward(f);
            FxDirector.Play("blood.hit", Middle, back, new FxParams { Direction = back });
            Say("hurt");
            if (_oneShot == null && _gait <= 1)
            {
                OneShot("Hurt");
            }
        }
        if (f.State == AnimalState.Flee && was.State != AnimalState.Flee)
        {
            Say("alarm");
        }
        if ((f.Warning && !was.Warning) || (f.State == AnimalState.Chase && was.State is not (AnimalState.Chase or AnimalState.Attack)))
        {
            Say("threat");
        }
    }

    private void OneShot(string clip)
    {
        if (_anim == null || !_anim.HasAnimation(clip))
        {
            return;
        }
        _oneShot = clip;
        _oneShotLeft = Model.Clip(clip)?.Seconds ?? _anim.GetAnimation(clip).Length;
        _anim.Play(clip, 0.1);
        _anim.Seek(0, true);
        _playing = clip;
        PlayingFor = 0;
    }

    // ---------------------------------------------------------------- placing

    private void Place(in AnimalFrame f, double delta)
    {
        // Tilted along the slope it walks: the climb seen between frames, smoothed; level again when it stops.
        Vector3 moved = f.Position - _lastPosition;
        _lastPosition = f.Position;
        float climb = moved.Dot(f.Up);
        float along = (moved - f.Up * climb).Length();
        float target = f.Dead ? 0 : along > 0.01f ? Math.Clamp(MathF.Atan2(climb, along), -0.4f, 0.4f) : _pitch * 0.98f;
        float k = 1 - MathF.Exp(-(float)delta * 4);
        _pitch += (target - _pitch) * k;
        Basis basis = CharacterFrame.BasisOf(f.Up, f.Yaw) * new Basis(Vector3.Right, _pitch);
        Transform = new Transform3D(basis, f.Position);
    }

    private static Vector3 Forward(in AnimalFrame f) => CharacterFrame.Facing(f.Up, f.Yaw);

    // ---------------------------------------------------------------- animation

    private void Animate(in AnimalFrame f, double delta, Vector3 eye, bool first)
    {
        if (_anim == null)
        {
            return;
        }
        PlayingFor += delta;
        float walk = Model.GaitSpeed("Walk", f.Data?.WalkSpeed ?? 1.4f);
        float trot = Model.GaitSpeed("Trot", f.Data?.TrotSpeed ?? 3.5f);
        float run = Model.GaitSpeed("Run", f.Data?.RunSpeed ?? 8f);
        _gait = f.Dead ? 0 : Gait(f.Speed, walk, trot, run);

        if (_oneShot != null)
        {
            _oneShotLeft -= delta;
            if (_oneShotLeft <= 0)
            {
                _oneShot = null;
            }
        }
        string clip = f.Dead ? "Death"
            : _oneShot ?? _gait switch
            {
                1 => "Walk",
                2 => "Trot",
                3 => "Run",
                _ => Standing(f),
            };
        if (clip != _playing && _oneShot == null)
        {
            Play(clip, first ? 0 : BlendSeconds);
        }
        float native = _gait switch { 1 => walk, 2 => trot, 3 => run, _ => 0 };
        _anim.SpeedScale = native > 0 && _oneShot == null && !f.Dead ? Math.Clamp(f.Speed / native, 0.6f, 1.6f) : 1f;

        // Far away the skeleton is posed less often.
        float distance = eye.DistanceTo(f.Position);
        int every = distance < 35 ? 1 : distance < 90 ? 2 : distance < 200 ? 4 : 8;
        _animDebt += delta;
        if (first || ++_frameIndex % every == 0)
        {
            _anim.Advance(_animDebt);
            _animDebt = 0;
        }
        Steps(f);
    }

    // Walk, trot or run by the speed over the ground, with some slack at each change so it doesn't flicker.
    private int Gait(float speed, float walk, float trot, float run)
    {
        float walkTrot = (walk + trot) / 2, trotRun = (trot + run) / 2;
        if (_gait == 0)
        {
            return speed < MoveFrom ? 0 : speed < walkTrot ? 1 : speed < trotRun ? 2 : 3;
        }
        if (speed < StopBelow)
        {
            return 0;
        }
        int wanted = speed < walkTrot ? 1 : speed < trotRun ? 2 : 3;
        if (wanted == _gait)
        {
            return wanted;
        }
        float boundary = wanted > _gait ? (_gait == 1 ? walkTrot : trotRun) : (_gait == 2 ? walkTrot : trotRun);
        return MathF.Abs(speed - boundary) > boundary * 0.08f ? wanted : _gait;
    }

    // Standing still: what it is doing. The threat display (a wolf's growl, the bear rearing) only while it warns.
    private static string Standing(in AnimalFrame f)
    {
        if (f.Warning)
        {
            return "Threaten";
        }
        return f.State switch
        {
            AnimalState.Graze => "Graze",
            AnimalState.Eat => "Eat",
            AnimalState.Alert or AnimalState.Flee or AnimalState.Chase or AnimalState.Attack => "Alert",
            _ => f.Alert ? "Alert" : "Idle",
        };
    }

    private void Play(string clip, float blend)
    {
        if (_anim == null || !_anim.HasAnimation(clip))
        {
            return;
        }
        _anim.Play(clip, blend);
        _playing = clip;
        PlayingFor = 0;
    }

    // ---------------------------------------------------------------- sound and blood

    // A footstep at each footfall of the gait clip playing: four a stride at a walk, two at a trot or a run.
    private void Steps(in AnimalFrame f)
    {
        if (_anim == null || _gait == 0 || _oneShot != null || f.Dead || !_anim.IsPlaying())
        {
            _lastPhase = -1;
            return;
        }
        double length = _anim.CurrentAnimationLength;
        if (length <= 0)
        {
            return;
        }
        double phase = _anim.CurrentAnimationPosition / length;
        int falls = _gait == 1 ? 4 : 2;
        if (_lastPhase >= 0)
        {
            double a = _lastPhase * falls, b = phase * falls;
            if (b < a)
            {
                b += falls;
            }
            if (Math.Floor(b) > Math.Floor(a))
            {
                Say("step", _gait == 1 ? -5f : _gait == 2 ? -2.5f : 0f, f.Position);
            }
        }
        _lastPhase = phase;
    }

    // Now and then a calm animal calls: a deer's bleat, a boar's grunt, a wolf's howl, a bear's huff.
    private void Voice(in AnimalFrame f, double delta)
    {
        _callIn -= delta;
        if (_callIn > 0)
        {
            return;
        }
        _callIn = CallInterval();
        if (f.State is AnimalState.Idle or AnimalState.Graze or AnimalState.Wander or AnimalState.Eat && !f.Warning)
        {
            Say("call", -2f);
        }
    }

    private double CallInterval()
    {
        (double least, double most) = Model.Key switch
        {
            "deer" => (25.0, 70.0),
            "boar" => (4.0, 12.0),
            "wolf" => (50.0, 150.0),
            "bear" => (15.0, 45.0),
            _ => (20.0, 60.0),
        };
        return least + (most - least) * _rng.NextDouble();
    }

    // Badly hurt and moving: a trail of drops on the ground (hunters follow it).
    private void Bleed(in AnimalFrame f, double delta)
    {
        if (f.Health > 0.5f || f.Speed < 0.3f)
        {
            return;
        }
        _bleedIn -= delta;
        if (_bleedIn <= 0)
        {
            _bleedIn = 0.7 + _rng.NextDouble() * 0.9;
            FxDirector.Play("blood.drip", f.Position, f.Up);
        }
    }

    private void Say(string role, float db = 0, Vector3? at = null)
    {
        if (Sounds)
        {
            AudioDirector.Instance?.Play($"animal.{Model.Key}.{role}", at ?? HeadPosition, db);
        }
    }

    private static T? Find<T>(Node root) where T : Node
    {
        if (root is T found)
        {
            return found;
        }
        foreach (Node child in root.GetChildren())
        {
            if (Find<T>(child) is { } inner)
            {
                return inner;
            }
        }
        return null;
    }
}
