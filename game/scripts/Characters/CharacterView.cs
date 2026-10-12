using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// A player as others see them (GROUND.md §4.8). Give it a <see cref="CharacterFrame"/> every frame with
/// <see cref="Apply"/> and one-off <see cref="Trigger"/> events; it animates from that alone. Clothing comes from
/// <see cref="Wear"/>.
/// <para>
/// Cost control: three mesh levels of detail by camera distance, and animation at full rate near the camera, at
/// half rate in the middle distance and at a quarter rate far away (staggered over frames). Procedural layers (aim,
/// IK, feet) run only where they can be seen.
/// </para>
/// </summary>
public partial class CharacterView : Node3D
{
    private static int _counter;

    private readonly int _index = _counter++;
    private Node3D _model = null!;
    private Skeleton3D _skeleton = null!;
    private AnimationTree _tree = null!;
    private CharacterPoser _poser = null!;
    private readonly List<(MeshInstance3D Mesh, int[] Lods, bool Body)> _meshes = new();
    private readonly List<(MeshInstance3D Mesh, int[] Lods, Wearable Item)> _clothes = new();
    private readonly List<Wearable> _worn = new();
    private CharacterFrame _frame = CharacterFrame.Standing(Vector3.Zero, Vector3.Up);
    private bool _hasFrame;
    private int _lod = -1;
    private double _pending;
    private int _frameCount;

    // Animation state.
    private CharacterStance _stance = (CharacterStance)(-1);
    private HeldKind _held = (HeldKind)(-1);
    private string? _heldId;
    private float _legYaw;
    private float _air;
    private float _airTime;
    private bool _wasGrounded = true;
    private float _holdR, _holdL;
    private float _recoil;
    private float _hold = 1f;
    private float _reload = -1f;
    private float _actionFade = -1f;

    // Held item.
    private BoneAttachment3D _handR = null!, _handL = null!;
    private Node3D _carry = null!;
    private Node3D? _item;
    private HeldItemModel? _itemModel;
    private (Basis FrameToBone, Vector3 Palm) _gripR, _gripL;

    private CharacterRagdoll? _ragdoll;
    private PhysicsRayQueryParameters3D? _ray;

    /// <summary>Forces a level of detail (0–2) regardless of distance; −1 picks by distance.</summary>
    public int ForcedLod { get; set; } = -1;

    /// <summary>Feet placement on the ground below (needs Godot physics colliders); on by default.</summary>
    public bool FootPlacement { get; set; } = true;

    public int Lod => _lod;

    public bool IsDead => _ragdoll?.Active == true;

    public Skeleton3D Skeleton => _skeleton;

    public CharacterPoser Poser => _poser;

    /// <summary>The held item's model, if any (for debugging and tests).</summary>
    public Node3D? HeldModel => _item;

    public IReadOnlyList<Wearable> Worn => _worn;

    public override void _Ready()
    {
        CharacterAssets.Load();
        // The art faces +Z; the view's forward is −Z.
        _model = new Node3D { Name = "Model", RotationDegrees = new Vector3(0, 180, 0) };
        AddChild(_model);
        _skeleton = CharacterAssets.NewSkeleton();
        _skeleton.ModifierCallbackModeProcess = Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
        _model.AddChild(_skeleton);
        foreach (MeshPart part in CharacterAssets.Meshes.Values)
        {
            if (!IsBodyPart(part.Name))
            {
                continue;
            }
            MeshInstance3D mi = Instance(part);
            _meshes.Add((mi, part.Lods, part.Name.StartsWith("body", StringComparison.Ordinal)));
        }
        _poser = new CharacterPoser { Name = "Poser" };
        _skeleton.AddChild(_poser);
        _handR = new BoneAttachment3D { Name = "HandR", BoneName = "hand_r" };
        _skeleton.AddChild(_handR);
        _handL = new BoneAttachment3D { Name = "HandL", BoneName = "hand_l" };
        _skeleton.AddChild(_handL);
        _carry = new Node3D { Name = "Carry" };
        _skeleton.AddChild(_carry);
        _gripR = HandFrame.Measure(_skeleton, right: true);
        _gripL = HandFrame.Measure(_skeleton, right: false);

        _tree = new AnimationTree
        {
            Name = "Animation",
            TreeRoot = CharacterAnimator.Tree,
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
            RootNode = new NodePath(".."),
        };
        _tree.AddAnimationLibrary("", CharacterAssets.Library);
        _model.AddChild(_tree);
        _tree.Active = true;
        ApplyRegions();
    }

    private static bool IsBodyPart(string name) =>
        name.StartsWith("body_", StringComparison.Ordinal) || name.StartsWith("eyes_", StringComparison.Ordinal) ||
        name.StartsWith("brows_", StringComparison.Ordinal) || name.StartsWith("lashes_", StringComparison.Ordinal) ||
        name.StartsWith("hair_", StringComparison.Ordinal);

    private MeshInstance3D Instance(MeshPart part)
    {
        var mi = new MeshInstance3D
        {
            Name = part.Name,
            Mesh = part.Mesh,
            Skin = part.Skin,
            Skeleton = new NodePath(".."),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Visible = false,
        };
        for (int i = 0; i < part.Mesh.GetSurfaceCount(); i++)
        {
            string surface = part.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "skin";
            mi.SetSurfaceOverrideMaterial(i, CharacterAssets.MaterialFor(surface));
        }
        _skeleton.AddChild(mi);
        return mi;
    }

    // ---------------------------------------------------------------- the contract

    /// <summary>The state to draw this frame.</summary>
    public void Apply(in CharacterFrame frame)
    {
        _frame = frame;
        _hasFrame = true;
    }

    /// <summary>Puts on exactly these items (one per slot; later items replace earlier ones in the same slot).</summary>
    public void Wear(IEnumerable<Wearable> items)
    {
        var bySlot = new Dictionary<WearSlot, Wearable>();
        foreach (Wearable w in items)
        {
            bySlot[w.Slot] = w;
        }
        foreach ((MeshInstance3D mesh, _, _) in _clothes)
        {
            mesh.QueueFree();
        }
        _clothes.Clear();
        _worn.Clear();
        _worn.AddRange(bySlot.Values);
        foreach (Wearable w in _worn)
        {
            for (int level = 0; level < 3; level++)
            {
                MeshPart? part = CharacterAssets.Part(w.Mesh + "_lod" + level);
                if (part == null)
                {
                    continue;
                }
                MeshInstance3D mi = Instance(part);
                _clothes.Add((mi, part.Lods, w));
            }
        }
        _lod = -1;
        ApplyRegions();
    }

    /// <summary>A one-off action. <paramref name="direction"/> is where a hit or a death push travels (parent space).</summary>
    public void Trigger(CharacterEvent e, Vector3 direction = default)
    {
        if (IsDead && e != CharacterEvent.Death)
        {
            return;
        }
        switch (e)
        {
            case CharacterEvent.Swing:
                PlayAction(_held switch
                {
                    HeldKind.Tool when _itemModel?.LeftHanded == true => "",
                    // Hatchets, picks, hammers and torches chop; blades slash; the rock is brought down overhand.
                    HeldKind.Tool => _itemModel?.Id == "rock" ? "throw" : _itemModel?.Class == "blade" ? "tool_swing" : "chop",
                    HeldKind.Unarmed => "punch_jab",
                    _ => "",
                });
                break;
            case CharacterEvent.Fire:
                if (_held == HeldKind.Pistol)
                {
                    PlayAction("pistol_fire");
                }
                _recoil = 1f;
                break;
            case CharacterEvent.Reload:
                if (_held == HeldKind.Pistol)
                {
                    PlayAction("pistol_reload");
                }
                else if (_held == HeldKind.Rifle)
                {
                    _reload = 0f;
                }
                break;
            case CharacterEvent.Hit:
                _tree.Set(CharacterAnimator.HitClip, GD.Randf() < 0.3f ? "head" : "chest");
                _tree.Set(CharacterAnimator.Hit, (int)AnimationNodeOneShot.OneShotRequest.Fire);
                break;
            case CharacterEvent.Death:
                Die(direction);
                break;
        }
    }

    /// <summary>Stands back up after a death (respawn).</summary>
    public void Revive()
    {
        _ragdoll?.Stop();
        _tree.Active = true;
        _poser.Active = true;
    }

    private void PlayAction(string clip)
    {
        if (clip.Length == 0)
        {
            return;
        }
        _tree.Set(CharacterAnimator.ActionClip, clip);
        _tree.Set(CharacterAnimator.Action, (int)AnimationNodeOneShot.OneShotRequest.Fire);
        // The chop is cut from a loop: it ends after its follow-through.
        _actionFade = clip == "chop" ? CharacterAnimator.ChopFadeAt : -1f;
    }

    private void Die(Vector3 direction)
    {
        _ragdoll ??= new CharacterRagdoll(_skeleton);
        Vector3 push = direction.LengthSquared() > 1e-6f ? direction.Normalized() * 2.2f : Vector3.Zero;
        // Bring the skeleton fully up to date before the bodies take over.
        _tree.Advance(0);
        _skeleton.Advance(0);
        _tree.Active = false;
        _poser.Active = false;
        _ragdoll.Start(_frame.Velocity + push, _frame.Up.Normalized());
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        if (!_hasFrame)
        {
            return;
        }
        Transform = new Transform3D(CharacterFrame.BasisOf(_frame.Up, _frame.Yaw), _frame.Position);
        int lod = ForcedLod >= 0 ? ForcedLod : PickLod();
        if (lod != _lod)
        {
            SetLod(lod);
        }
        if (lod > 2)
        {
            return;
        }
        if (IsDead)
        {
            _skeleton.Advance(delta);
            return;
        }
        _pending += delta;
        _frameCount++;
        int every = lod switch { 0 => 1, 1 => 2, _ => 4 };
        if ((_frameCount + _index) % every != 0)
        {
            return;
        }
        float dt = (float)_pending;
        _pending = 0;
        Animate(dt, lod);
        _tree.Advance(dt);
        _skeleton.Advance(dt);
    }

    private int PickLod()
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return 0;
        }
        float d = camera.GlobalPosition.DistanceTo(GlobalPosition);
        // A little hysteresis so a character at a boundary does not flicker between levels.
        float margin = _lod >= 0 ? 0.6f : 0f;
        for (int level = 0; level < CharacterAssets.LodEnd.Length; level++)
        {
            float end = CharacterAssets.LodEnd[level] + (level >= _lod ? margin : -margin);
            if (d < end)
            {
                return level;
            }
        }
        return 3;
    }

    private void SetLod(int lod)
    {
        _lod = lod;
        foreach ((MeshInstance3D mesh, int[] lods, _) in _meshes)
        {
            mesh.Visible = Array.IndexOf(lods, lod) >= 0 && !(mesh.Name.ToString().StartsWith("hair", StringComparison.Ordinal) && HairHidden());
        }
        foreach ((MeshInstance3D mesh, int[] lods, _) in _clothes)
        {
            mesh.Visible = Array.IndexOf(lods, lod) >= 0;
        }
        if (_item != null)
        {
            // Third person never needs an item's first-person detail.
            HeldItems.SetLod(_item, Math.Min(2, lod + 1));
        }
        _poser.GroundProbe = lod == 0 && FootPlacement ? Probe : null;
    }

    private bool HairHidden()
    {
        foreach (Wearable w in _worn)
        {
            if (w.HidesHair)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Hides body regions under clothing, and parts of inner garments under outer ones.</summary>
    private void ApplyRegions()
    {
        BodyRegions hidden = BodyRegions.None;
        foreach (Wearable w in _worn)
        {
            hidden |= w.Hides;
        }
        foreach ((MeshInstance3D mesh, _, bool body) in _meshes)
        {
            if (body)
            {
                mesh.SetInstanceShaderParameter("hidden_regions", (int)hidden);
            }
        }
        foreach ((MeshInstance3D mesh, _, Wearable item) in _clothes)
        {
            BodyRegions under = BodyRegions.None;
            foreach (Wearable other in _worn)
            {
                if (Layer(other) > Layer(item))
                {
                    under |= other.Hides;
                }
            }
            mesh.SetInstanceShaderParameter("hidden_regions", (int)under);
        }
    }

    /// <summary>Which garment goes over which: trousers under the shirt's hem, both under boots and armour.</summary>
    private static int Layer(Wearable w) => w.Slot switch
    {
        WearSlot.Legs => 1,
        WearSlot.Torso => 2,
        WearSlot.Feet => 3,
        WearSlot.Hands => 3,
        WearSlot.TorsoArmour => 4,
        _ => 2,
    };

    private void Animate(float dt, int lod)
    {
        CharacterFrame f = _frame;
        Vector3 up = f.Up.Normalized();
        Vector3 horizontal = f.Velocity - up * f.Velocity.Dot(up);
        float speed = horizontal.Length();

        // Direction of travel relative to the facing: the hips follow it, the clip runs backwards when backing up.
        Vector3 facing = CharacterFrame.Facing(up, f.Yaw);
        float travel = speed > 0.2f ? MathF.Atan2(facing.Cross(horizontal).Dot(up), facing.Dot(horizontal)) : 0f;
        bool backwards = MathF.Abs(travel) > 1.92f;
        float legTarget = backwards ? Wrap(travel - MathF.PI) : Math.Clamp(travel, -1.3f, 1.3f);
        if (f.Stance == CharacterStance.Swim || speed < 0.2f)
        {
            legTarget = 0f;
        }
        _legYaw += (legTarget - _legYaw) * Math.Min(1f, dt * 9f);

        if (f.Stance != _stance)
        {
            _stance = f.Stance;
            _tree.Set(CharacterAnimator.Stance, _stance switch
            {
                CharacterStance.Crouch => "crouch",
                CharacterStance.Swim => "swim",
                _ => "stand",
            });
        }
        float top = _stance switch { CharacterStance.Crouch => 1.3f, CharacterStance.Swim => 2.0f, _ => CharacterAnimator.TopSpeed };
        _tree.Set(CharacterAnimator.StandSpeed, Math.Min(speed, CharacterAnimator.TopSpeed));
        _tree.Set(CharacterAnimator.CrouchSpeed, Math.Min(speed, 1.3f));
        _tree.Set(CharacterAnimator.SwimSpeed, Math.Min(speed, 2.0f));
        float rate = Math.Max(1f, speed / top);
        _tree.Set(CharacterAnimator.Rate, backwards ? -rate : rate);

        // Air: falling pose blended in; a jump plays its take-off, a landing after a real fall its landing.
        bool grounded = f.Grounded || f.Stance == CharacterStance.Swim;
        _airTime = grounded ? 0f : _airTime + dt;
        float airTarget = grounded ? 0f : Math.Clamp((_airTime - 0.12f) / 0.2f, 0f, 1f);
        _air += (airTarget - _air) * Math.Min(1f, dt * 10f);
        _tree.Set(CharacterAnimator.Air, _air);
        if (_wasGrounded && !grounded && f.Velocity.Dot(up) > 1.5f)
        {
            _tree.Set(CharacterAnimator.Jump, (int)AnimationNodeOneShot.OneShotRequest.Fire);
        }
        if (!_wasGrounded && grounded && _air > 0.5f)
        {
            _tree.Set(CharacterAnimator.Land, (int)AnimationNodeOneShot.OneShotRequest.Fire);
        }
        _wasGrounded = grounded;

        UpdateHeld(f, dt);

        _poser.LegYaw = _legYaw;
        _poser.Pitch = f.Pitch;
        _poser.SpinePitchShare = _held is HeldKind.Pistol or HeldKind.Rifle ? 0.85f : 0.45f;
        _poser.Active = lod <= 1 || MathF.Abs(_legYaw) > 0.05f || MathF.Abs(f.Pitch) > 0.05f || _held == HeldKind.Rifle;
    }

    private static float Wrap(float a)
    {
        while (a > MathF.PI)
        {
            a -= MathF.Tau;
        }
        while (a < -MathF.PI)
        {
            a += MathF.Tau;
        }
        return a;
    }

    // ---------------------------------------------------------------- held items

    private void UpdateHeld(CharacterFrame f, float dt)
    {
        if (f.Held != _held || f.HeldItem != _heldId)
        {
            _held = f.Held;
            _heldId = f.HeldItem;
            SwapItem(HeldItems.For(_held, _heldId));
            if (_held != HeldKind.Unarmed)
            {
                _tree.Set(CharacterAnimator.HeldRight, _held.ToString().ToLowerInvariant());
            }
            if (_held is HeldKind.Pistol or HeldKind.Rifle)
            {
                _tree.Set(CharacterAnimator.HeldLeft, _held.ToString().ToLowerInvariant());
            }
        }
        bool leftHanded = _itemModel?.LeftHanded == true;
        float wantR = _held == HeldKind.Unarmed || leftHanded ? 0f : 1f;
        float wantL = _held is HeldKind.Pistol or HeldKind.Rifle ? 1f : 0f;
        _holdR += (wantR - _holdR) * Math.Min(1f, dt * 8f);
        _holdL += (wantL - _holdL) * Math.Min(1f, dt * 8f);
        _tree.Set(CharacterAnimator.HoldRight, _holdR);
        _tree.Set(CharacterAnimator.HoldLeft, _holdL);

        _recoil = Math.Max(0f, _recoil - dt * 7f);
        if (_actionFade >= 0f)
        {
            _actionFade -= dt;
            if (_actionFade < 0f)
            {
                _tree.Set(CharacterAnimator.Action, (int)AnimationNodeOneShot.OneShotRequest.FadeOut);
            }
        }
        if (_reload >= 0f)
        {
            _reload += dt / 2.2f;
            if (_reload >= 1f)
            {
                _reload = -1f;
            }
        }
        _poser.ChestKick = new Quaternion(Vector3.Left, 0.09f * Kick(_recoil));
        // While an upper-body action plays, the clip moves the arms; otherwise the hands hold their grips.
        bool acting = (bool)_tree.Get(CharacterAnimator.ActionActive);
        _hold = Math.Clamp(_hold + (acting ? -dt * 9f : dt * 5f), 0f, 1f);
        if (_held == HeldKind.Rifle && _item != null)
        {
            PlaceRifle(f);
        }
        else if (_held == HeldKind.Tool && _item != null)
        {
            CarryTool(leftHanded);
        }
        else
        {
            _poser.RightHandWeight = 0f;
            _poser.LeftHandWeight = 0f;
        }
    }

    /// <summary>A tool rides upright in the right hand at the hip, blade forward (a bow in the left hand).</summary>
    private void CarryTool(bool left)
    {
        float hip = _skeleton.GetBoneGlobalRest(_skeleton.FindBone(left ? "thigh_l" : "thigh_r")).Origin.Y;
        // Skeleton space: forward +Z, right −X. Item axes: +X blade, +Y handle up.
        var grip = new Transform3D(
            new Basis(Vector3.Left, -0.5f) * new Basis(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0)),
            new Vector3(left ? 0.25f : -0.25f, hip + 0.08f, 0.2f));
        Transform3D hand = HandFrame.HandFor(grip, HandFrame.PowerGrip, left ? _gripL : _gripR);
        _poser.RightHand = hand;
        _poser.LeftHand = hand;
        _poser.RightHandWeight = left ? 0f : Smooth(_hold);
        _poser.LeftHandWeight = left ? Smooth(_hold) : 0f;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Kick(float t) => t <= 0f ? 0f : MathF.Sin(MathF.Min(1f, (1f - t) * 4f) * MathF.PI * 0.5f) * t;

    private void SwapItem(HeldItemModel? model)
    {
        _item?.QueueFree();
        _item = null;
        _itemModel = model;
        if (model == null)
        {
            return;
        }
        _item = HeldItems.Instantiate(model);
        if (_item == null)
        {
            return;
        }
        HeldItems.SetLod(_item, Math.Min(2, Math.Max(0, _lod) + 1));
        if (model.Kind == HeldKind.Rifle)
        {
            _carry.AddChild(_item);
            return;
        }
        // Held in one hand (the right, or the left for a bow): its grip socket in the palm. Where the hand bone would
        // be holding the item, in item space; the item sits at its inverse from the bone.
        bool left = model.LeftHanded;
        (left ? _handL : _handR).AddChild(_item);
        Transform3D grip = HeldItems.Socket(_item, left ? "grip_l" : "grip_r");
        Transform3D handInItem = HandFrame.HandFor(grip, HandFrame.PowerGrip, left ? _gripL : _gripR);
        _item.Transform = handInItem.AffineInverse();
    }

    /// <summary>
    /// A rifle is carried at the right shoulder, pointing where the player looks; both hands reach its grips.
    /// </summary>
    private void PlaceRifle(CharacterFrame f)
    {
        int chest = _skeleton.FindBone("spine_03");
        // The chest as the clips put it (crouching lowers it); the aim comes from the pitch, not the chest.
        Vector3 anchor = _skeleton.GetBoneGlobalPose(chest).Origin;
        float pitch = f.Pitch;
        // Skeleton space: forward +Z, right −X, up +Y.
        Basis aim = new Basis(Vector3.Left, pitch);
        float kick = Kick(_recoil);
        Vector3 shoulder = anchor + new Vector3(-0.15f, 0.12f, 0.02f) + aim * new Vector3(0, 0.0f, 0.12f - 0.05f * kick);
        // Item axes: +X barrel, +Y up. Skeleton: barrel along +Z.
        var itemBasis = new Basis(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0));
        Basis b = aim * new Basis(Vector3.Left, 0.06f * kick) * itemBasis;
        if (_reload >= 0f)
        {
            float tilt = MathF.Sin(MathF.Min(1f, _reload * 1.4f) * MathF.PI) * 0.45f;
            b = new Basis(aim * Vector3.Back, tilt) * b;
        }
        Transform3D gripR = HeldItems.Socket(_item!, "grip_r");
        // Place the item so its right grip sits at the shoulder anchor.
        var item = new Transform3D(b, shoulder - b * gripR.Origin);
        _item!.Transform = item;
        Transform3D gripL = HeldItems.Socket(_item!, "grip_l");
        _poser.RightHand = HandFrame.HandFor(item * gripR, HandFrame.PowerGrip, _gripR);
        Transform3D left = HandFrame.HandFor(item * gripL, HandFrame.SupportGrip, _gripL);
        if (_reload >= 0f)
        {
            // Left hand to the magazine and back.
            float t = MathF.Sin(MathF.Min(1f, _reload * 1.25f) * MathF.PI);
            left.Origin += (b * new Vector3(-0.17f, -0.17f, 0.02f)) * t;
        }
        _poser.LeftHand = left;
        _poser.RightHandWeight = Smooth(_hold);
        _poser.LeftHandWeight = Smooth(_hold);
    }

    // ---------------------------------------------------------------- ground probe for the feet

    private (bool, Vector3, Vector3) Probe(Vector3 from, Vector3 motion)
    {
        PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return (false, Vector3.Zero, Vector3.Up);
        }
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = 1 };
        _ray.From = from;
        _ray.To = from + motion;
        Godot.Collections.Dictionary hit = space.IntersectRay(_ray);
        if (hit.Count == 0)
        {
            return (false, Vector3.Zero, Vector3.Up);
        }
        return (true, (Vector3)hit["position"], (Vector3)hit["normal"]);
    }
}
