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
    private string? _bodyClip;

    // The procedural layer's actions (GROUND.md §4.8): the bow's draw, aim and loose; a spear's thrust; bandaging and
    // injecting; the pistol's magazine in a reload.
    private enum UseMotion { None, Bandage, Inject }
    private UseMotion _use;
    private float _useTime = -1f;
    private float _thrust = -1f;
    private float _draw, _bowAim, _loose = -1f;
    private BowRig? _bowRig;
    private float _pistolReload = -1f;
    private Node3D? _pistolMagazine;
    private Transform3D _pistolMagazineRest;
    private MeshInstance3D? _magazineProp;

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

    /// <summary>Which body this character has; set it before the view enters the tree.</summary>
    public BodyType Body { get; set; } = BodyType.Male;

    private BodyAssets _assets = null!;
    private Look? _look;
    private bool _ready;
    private readonly List<(MeshInstance3D Mesh, int[] Shapes)> _faceShapes = new();

    /// <summary>
    /// How this player looks (GROUND.md §4.8): skin tone, face, hair style and colour, stubble, from
    /// <see cref="Looks.ForPlayer(ulong, BodyType)"/>. Set it any time; until it is set, the body's default look.
    /// </summary>
    public Look Look
    {
        get => _look ?? Looks.Default(Body);
        set
        {
            _look = value;
            if (_ready)
            {
                ApplyLook();
            }
        }
    }

    /// <summary>Holds the eyelids at this closure (0 open, 1 shut) instead of blinking now and then; −1 blinks.</summary>
    public float BlinkOverride { get; set; } = -1f;

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
        _assets = CharacterAssets.For(Body);
        // The art faces +Z; the view's forward is −Z.
        _model = new Node3D { Name = "Model", RotationDegrees = new Vector3(0, 180, 0) };
        AddChild(_model);
        _skeleton = _assets.NewSkeleton();
        _skeleton.ModifierCallbackModeProcess = Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
        _model.AddChild(_skeleton);
        foreach (MeshPart part in _assets.Meshes.Values)
        {
            if (!IsBodyPart(part.Name))
            {
                continue;
            }
            MeshInstance3D mi = Instance(part);
            _meshes.Add((mi, part.Lods, part.Name.StartsWith("body", StringComparison.Ordinal)));
            int shape = mi.FindBlendShapeByName("blink");
            if (shape >= 0)
            {
                _blinkers.Add((mi, shape));
            }
            var faces = new int[_assets.Faces.Count];
            for (int f = 0; f < faces.Length; f++)
            {
                faces[f] = mi.FindBlendShapeByName(_assets.Faces[f]);
            }
            if (Array.Exists(faces, i => i >= 0))
            {
                _faceShapes.Add((mi, faces));
            }
        }
        _nextBlink = 1f + (float)(GD.Randf() * 4.0);
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
            TreeRoot = CharacterAnimator.TreeFor(_assets),
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
            RootNode = new NodePath(".."),
        };
        _tree.AddAnimationLibrary("", _assets.Library);
        _model.AddChild(_tree);
        _tree.Active = true;
        _ready = true;
        ApplyLook();
        ApplyRegions();
    }

    // The hair is the look's (ApplyLook); everything else of the body is always there.
    private static bool IsBodyPart(string name) =>
        name.StartsWith("body_", StringComparison.Ordinal) || name.StartsWith("eyes_", StringComparison.Ordinal) ||
        name.StartsWith("brows_", StringComparison.Ordinal) || name.StartsWith("lashes_", StringComparison.Ordinal);

    /// <summary>Puts the look on: its hair style's meshes, the skin's tone, stubble and hair base, the hair and brow
    /// colour, and its face shape.</summary>
    private void ApplyLook()
    {
        Look look = Look;
        for (int i = _meshes.Count - 1; i >= 0; i--)
        {
            if (_meshes[i].Mesh.Name.ToString().StartsWith("hair", StringComparison.Ordinal))
            {
                Discard(_meshes[i].Mesh);
                _meshes.RemoveAt(i);
            }
        }
        foreach (MeshPart part in HairParts(_assets, look.Hair))
        {
            _meshes.Add((Instance(part), part.Lods, false));
        }
        LookUniforms.Apply(_meshes.ConvertAll(m => m.Mesh), look);
        foreach ((MeshInstance3D mesh, int[] shapes) in _faceShapes)
        {
            for (int f = 0; f < shapes.Length; f++)
            {
                if (shapes[f] >= 0)
                {
                    mesh.SetBlendShapeValue(shapes[f], f + 1 == look.Face ? 1f : 0f);
                }
            }
        }
        _lod = -1;
    }

    /// <summary>The meshes of a hair style (none for the buzz cut; the body's first style if it has no such style).</summary>
    internal static List<MeshPart> HairParts(BodyAssets assets, string style)
    {
        var parts = new List<MeshPart>();
        if (style == Looks.Buzz)
        {
            return parts;
        }
        if (!Contains(assets.HairStyles, style))
        {
            style = assets.HairStyles.Count > 0 ? assets.HairStyles[0] : "";
        }
        string stem = style.Length > 0 ? "hair_" + style + "_lod" : "hair_lod";
        for (int level = 0; level < 3; level++)
        {
            if (assets.Part(stem + level) is MeshPart part)
            {
                parts.Add(part);
            }
        }
        return parts;
    }

    private static bool Contains(IReadOnlyList<string> list, string item)
    {
        foreach (string s in list)
        {
            if (s == item)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Takes a mesh out of the tree at once and frees it: a new one of the same name added in the same frame keeps its
    /// name (the hair and clothing are told apart by name), instead of being renamed past the one still waiting to go.
    /// </summary>
    internal static void Discard(Node node)
    {
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

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
            Discard(mesh);
        }
        _clothes.Clear();
        _worn.Clear();
        _worn.AddRange(bySlot.Values);
        foreach (Wearable w in _worn)
        {
            string mesh = MeshOf(w, bySlot);
            for (int level = 0; level < 3; level++)
            {
                MeshPart? part = _assets.Part(mesh + "_lod" + level);
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

    /// <summary>The mesh a garment is drawn with: its own, or its variant over what is worn under it (the hood over a
    /// mask) when the body has that variant.</summary>
    private string MeshOf(Wearable w, Dictionary<WearSlot, Wearable> bySlot) =>
        w.Over is (WearSlot under, string variant) && bySlot.ContainsKey(under) && _assets.Part(variant + "_lod0") != null
            ? variant
            : w.Mesh;

    /// <summary>A one-off action. <paramref name="direction"/> is where a hit or a death push travels (parent space).</summary>
    public void Trigger(CharacterEvent e, Vector3 direction = default)
    {
        if (IsDead && e != CharacterEvent.Death)
        {
            return;
        }
        if (Lying && e != CharacterEvent.Death && e != CharacterEvent.Hit)
        {
            return;
        }
        switch (e)
        {
            case CharacterEvent.Swing:
                if (_itemModel?.Class == "spear")
                {
                    _thrust = 0f;
                    break;
                }
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
                if (_itemModel?.LeftHanded == true)
                {
                    // The bow: the string is loosed (the arrow flies) and the hand flicks back.
                    if (_draw > 0.15f)
                    {
                        _loose = 0f;
                        _draw = 0f;
                    }
                    break;
                }
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
                    _pistolReload = 0f;
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
            case CharacterEvent.HelpUp:
                PlayBody("help_up");
                break;
            case CharacterEvent.Use:
                CharacterEvent use = _itemModel?.Class switch
                {
                    "food" => CharacterEvent.Eat,
                    "medical" when _itemModel.Id.Contains("injector", StringComparison.Ordinal) => CharacterEvent.Inject,
                    "medical" => CharacterEvent.Bandage,
                    _ => CharacterEvent.Use,
                };
                if (use != CharacterEvent.Use)
                {
                    Trigger(use, direction);
                }
                break;
            case CharacterEvent.Eat:
                // The clip lifts the left hand to the mouth: the food goes to that hand while it eats.
                PlayAction("consume");
                _eating = _assets.Clips.TryGetValue("consume", out ClipInfo? eat) ? eat.Length : 1.3f;
                MoveItemTo(left: true);
                break;
            case CharacterEvent.Bandage:
                _use = UseMotion.Bandage;
                _useTime = 0f;
                break;
            case CharacterEvent.Inject:
                _use = UseMotion.Inject;
                _useTime = 0f;
                break;
        }
    }

    /// <summary>Lying down: wounded or asleep.</summary>
    public bool Lying => _stance is CharacterStance.Crawl or CharacterStance.Sleep;

    // Seconds of eating left; the food is in the left hand meanwhile.
    private float _eating = -1f;

    /// <summary>Moves a one-handed item to the other hand's palm (eating), or back to the hand it is held in.</summary>
    private void MoveItemTo(bool left)
    {
        if (_item == null || _itemModel == null || _itemModel.Kind == HeldKind.Rifle || _itemModel.Class == "spear" ||
            _itemModel.LeftHanded)
        {
            return;
        }
        BoneAttachment3D hand = left ? _handL : _handR;
        if (_item.GetParent() == hand)
        {
            return;
        }
        _item.GetParent()?.RemoveChild(_item);
        hand.AddChild(_item);
        Transform3D grip = HeldItems.Socket(_item, "grip_r");
        Transform3D handInItem = HandFrame.HandFor(grip, HandFrame.PowerGrip, left ? _gripL : _gripR);
        _item.Transform = handInItem.AffineInverse();
    }

    private void PlayBody(string clip)
    {
        if (!_assets.Clips.ContainsKey(clip))
        {
            return;
        }
        _tree.Set(CharacterAnimator.BodyClip, clip);
        _tree.Set(CharacterAnimator.Body, (int)AnimationNodeOneShot.OneShotRequest.Fire);
        _bodyClip = clip;
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
        if (lod == 0)
        {
            Blink(dt);
        }
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

    // The eyelids (the nearest level of detail carries a "blink" shape): every two to six seconds, shut in 70 ms and
    // open again in 130 ms; now and then twice.
    private readonly List<(MeshInstance3D Mesh, int Shape)> _blinkers = new();
    private float _nextBlink, _blinkClock = -1f;

    private void Blink(float dt)
    {
        float closure;
        if (BlinkOverride >= 0f || _stance == CharacterStance.Sleep)
        {
            closure = _stance == CharacterStance.Sleep ? 1f : Math.Clamp(BlinkOverride, 0f, 1f);
        }
        else
        {
            _nextBlink -= dt;
            if (_nextBlink <= 0f && _blinkClock < 0f)
            {
                _blinkClock = 0f;
                _nextBlink = GD.Randf() < 0.15f ? 0.3f : 2f + (float)(GD.Randf() * 4.0);
            }
            closure = 0f;
            if (_blinkClock >= 0f)
            {
                _blinkClock += dt;
                closure = _blinkClock < 0.07f ? _blinkClock / 0.07f : 1f - (_blinkClock - 0.07f) / 0.13f;
                if (_blinkClock >= 0.2f)
                {
                    _blinkClock = -1f;
                    closure = 0f;
                }
            }
        }
        foreach ((MeshInstance3D mesh, int shape) in _blinkers)
        {
            mesh.SetBlendShapeValue(shape, Math.Clamp(closure, 0f, 1f));
        }
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
                if (Wardrobe.Layer(other) > Wardrobe.Layer(item))
                {
                    under |= other.Hides;
                }
            }
            mesh.SetInstanceShaderParameter("hidden_regions", (int)under);
        }
    }

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
        bool lyingClips = _assets.Clips.ContainsKey("down_idle");
        CharacterStance stance = !lyingClips && f.Stance is CharacterStance.Crawl or CharacterStance.Sleep ? CharacterStance.Crouch : f.Stance;
        bool lying = stance is CharacterStance.Crawl or CharacterStance.Sleep;
        if (stance == CharacterStance.Swim || speed < 0.2f || lying)
        {
            legTarget = 0f;
        }
        _legYaw += (legTarget - _legYaw) * Math.Min(1f, dt * 9f);

        if (stance != _stance)
        {
            CharacterStance before = _stance;
            _stance = stance;
            _tree.Set(CharacterAnimator.Stance, _stance switch
            {
                CharacterStance.Crouch => "crouch",
                CharacterStance.Swim => "swim",
                CharacterStance.Crawl => "down",
                CharacterStance.Sleep => "sleep",
                _ => "stand",
            });
            // Knocked down from standing: the fall; up again from the ground: getting up.
            if (_stance == CharacterStance.Crawl && before is CharacterStance.Stand or CharacterStance.Crouch)
            {
                PlayBody("down_fall");
            }
            else if (before == CharacterStance.Crawl && _stance is CharacterStance.Stand or CharacterStance.Crouch)
            {
                PlayBody("get_up");
            }
        }
        float top = _stance switch
        {
            CharacterStance.Crouch => 1.3f,
            CharacterStance.Swim => 2.0f,
            CharacterStance.Crawl => 0.8f,
            CharacterStance.Sleep => 100f,
            _ => CharacterAnimator.TopSpeed,
        };
        _tree.Set(CharacterAnimator.StandSpeed, Math.Min(speed, CharacterAnimator.TopSpeed));
        _tree.Set(CharacterAnimator.CrouchSpeed, Math.Min(speed, 1.3f));
        _tree.Set(CharacterAnimator.SwimSpeed, Math.Min(speed, 2.0f));
        if (lyingClips)
        {
            _tree.Set(CharacterAnimator.DownSpeed, Math.Min(speed, 0.8f));
        }
        float rate = Math.Max(1f, speed / top);
        _tree.Set(CharacterAnimator.Rate, backwards && !lying ? -rate : rate);

        // Air: falling pose blended in; a jump plays its take-off, a landing after a real fall its landing.
        bool grounded = f.Grounded || stance == CharacterStance.Swim || lying;
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
        // Lying, only the head follows the view (crawling); asleep, nothing does.
        _poser.Pitch = _stance switch
        {
            CharacterStance.Sleep => 0f,
            CharacterStance.Crawl => Math.Clamp(f.Pitch, -0.4f, 0.5f),
            _ => f.Pitch,
        };
        _poser.SpinePitchShare = lying ? 0f : _held is HeldKind.Pistol or HeldKind.Rifle || _bowAim > 0.5f ? 0.85f : 0.45f;
        _poser.GroundProbe = _lod == 0 && FootPlacement && !lying ? Probe : null;
        _poser.Active = lod <= 1 || MathF.Abs(_legYaw) > 0.05f || MathF.Abs(f.Pitch) > 0.05f || _held == HeldKind.Rifle ||
            _bowAim > 0f || _thrust >= 0f || _useTime >= 0f;
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
        bool lying = Lying;
        if (_item != null)
        {
            // Nothing is held lying down (the wounded can't use items; sleepers hold nothing).
            _item.Visible = !lying;
        }
        bool leftHanded = _itemModel?.LeftHanded == true;
        bool spear = _itemModel?.Class == "spear";
        float wantR = lying || _held == HeldKind.Unarmed || leftHanded || spear ? 0f : 1f;
        float wantL = !lying && _held is HeldKind.Pistol or HeldKind.Rifle ? 1f : 0f;
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
        Advance(ref _reload, dt / ReloadSeconds);
        Advance(ref _pistolReload, dt / PistolReloadSeconds);
        Advance(ref _thrust, dt / Math.Max(0.5f, _itemModel?.SwingTime ?? 0.9f));
        if (_useTime >= 0f)
        {
            _useTime += dt;
            if (_useTime >= UseSeconds(_use))
            {
                _useTime = -1f;
                _use = UseMotion.None;
            }
        }
        if (_loose >= 0f)
        {
            _loose += dt;
            if (_loose > 0.5f)
            {
                _loose = -1f;
            }
        }
        AnimatePistolMagazine();
        if (_eating >= 0f)
        {
            _eating -= dt;
            if (_eating < 0f)
            {
                MoveItemTo(left: false);
            }
        }
        _poser.ChestKick = new Quaternion(Vector3.Left, 0.09f * Kick(_recoil));
        _poser.TorsoTwist = 0f;
        _poser.TorsoLean = 0f;
        _poser.RightPole = CharacterPoser.DefaultRightPole;
        _poser.LeftPole = CharacterPoser.DefaultLeftPole;
        // While an upper-body action or a whole-body one-shot plays, the clip moves the arms; otherwise the hands hold
        // their grips.
        bool acting = (bool)_tree.Get(CharacterAnimator.ActionActive) || BodyActing;
        _hold = Math.Clamp(_hold + (acting ? -dt * 9f : dt * 5f), 0f, 1f);
        if (_magazineProp != null)
        {
            _magazineProp.Visible = false;
        }
        if (lying)
        {
            _poser.RightHandWeight = 0f;
            _poser.LeftHandWeight = 0f;
            _bowAim = 0f;
            _draw = 0f;
            return;
        }
        if (_useTime >= 0f)
        {
            PlaceUse();
        }
        else if (_held == HeldKind.Rifle && _item != null)
        {
            PlaceRifle(f);
        }
        else if (spear && _item != null)
        {
            PlaceSpear(f);
        }
        else if (leftHanded && _item != null)
        {
            PlaceBow(f, dt);
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

    /// <summary>The server's reload time (weapons.json: 2.4 s); the pistol's third-person reload is its clip's length.</summary>
    private const float ReloadSeconds = 2.4f, PistolReloadSeconds = 1.67f;

    private static float UseSeconds(UseMotion use) => use == UseMotion.Bandage ? 2.3f : 1.4f;

    private bool BodyActing => _assets.Clips.ContainsKey("help_up") && (bool)_tree.Get(CharacterAnimator.BodyActive);

    private static void Advance(ref float t, float step)
    {
        if (t < 0f)
        {
            return;
        }
        t += step;
        if (t >= 1f)
        {
            t = -1f;
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

    /// <summary>
    /// The bow (GROUND.md §4.8): at the side in the left hand until the player aims; aiming, the chest turns so the
    /// left shoulder leads, the left arm holds the bow out along the aim (canted a little), and the right hand draws the
    /// string back to the jaw over a second, an arrow on it; a shot looses it (the hand flicks back) and, still aiming,
    /// the next is drawn.
    /// </summary>
    private void PlaceBow(CharacterFrame f, float dt)
    {
        _bowAim = Math.Clamp(_bowAim + (f.Aiming ? dt * 3.5f : -dt * 3f), 0f, 1f);
        if (f.Aiming && _loose < 0f && _bowAim > 0.5f)
        {
            _draw = Math.Min(1f, _draw + dt / 0.95f);
        }
        else if (!f.Aiming)
        {
            _draw = Math.Max(0f, _draw - dt * 2.5f);
        }
        _bowRig?.Update(Smooth(_draw), f.Aiming && _loose < 0f);
        float a = Smooth(_bowAim);
        if (a <= 0.001f)
        {
            CarryTool(left: true);
            return;
        }
        Vector3 anchor = _skeleton.GetBoneGlobalPose(_skeleton.FindBone("spine_03")).Origin;
        var aim = new Basis(Vector3.Left, Math.Clamp(f.Pitch, -1.2f, 1.2f));
        Vector3 forward = aim * new Vector3(0, 0, 1);
        Vector3 up = (aim * Vector3.Up).Rotated(forward, -0.15f);
        var bow = new Basis(forward, up, forward.Cross(up));
        Vector3 grip = anchor + new Vector3(0.1f, 0.27f, 0.05f) + forward * 0.62f;
        var gripFrame = new Transform3D(bow, grip);
        // Where the bow then is, so the right hand finds the string's nock on it.
        Transform3D item = gripFrame * HeldItems.Socket(_item!, "grip_l").AffineInverse();
        Vector3 nock = item * (_bowRig?.Nock(Smooth(_draw)) ?? new Vector3(-0.1f - 0.55f * _draw, 0f, 0f));
        if (_loose >= 0f)
        {
            // The release: the drawing hand flies back past the jaw and opens.
            float k = MathF.Sin(MathF.Min(1f, _loose / 0.18f) * MathF.PI * 0.5f) * (1f - Smooth(Math.Clamp((_loose - 0.2f) / 0.3f, 0f, 1f)));
            nock += -forward * 0.07f * k + new Vector3(-0.04f, 0.01f, 0f) * k;
        }
        var drawFrame = new Transform3D(new Basis(forward, up, forward.Cross(up)), nock);
        _poser.LeftHand = HandFrame.HandFor(gripFrame, HandFrame.PowerGrip, _gripL).InterpolateWith(CarryHand(true), 1f - a);
        _poser.RightHand = HandFrame.HandFor(drawFrame, HandFrame.PowerGrip, _gripR);
        _poser.LeftHandWeight = Smooth(_hold);
        _poser.RightHandWeight = Smooth(_hold) * a;
        _poser.RightPole = new Vector3(-0.7f, 0.15f, -0.7f);
        _poser.LeftPole = new Vector3(0.6f, -0.6f, -0.1f);
        _poser.TorsoTwist = -0.85f * a;
    }

    /// <summary>Where a hand carrying a tool at the hip is (the bow's resting pose).</summary>
    private Transform3D CarryHand(bool left)
    {
        float hip = _skeleton.GetBoneGlobalRest(_skeleton.FindBone(left ? "thigh_l" : "thigh_r")).Origin.Y;
        var grip = new Transform3D(
            new Basis(Vector3.Left, -0.5f) * new Basis(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0)),
            new Vector3(left ? 0.25f : -0.25f, hip + 0.08f, 0.2f));
        return HandFrame.HandFor(grip, HandFrame.PowerGrip, left ? _gripL : _gripR);
    }

    /// <summary>
    /// A spear in both hands at the hip, its point forward and a little up (following the view's pitch a little); a
    /// swing is a thrust: drawn back, driven forward 0.4 m with the chest leaning and turning into it, held, recovered.
    /// </summary>
    private void PlaceSpear(CharacterFrame f)
    {
        Vector3 anchor = _skeleton.GetBoneGlobalPose(_skeleton.FindBone("spine_03")).Origin;
        Vector3 thrust = _thrust >= 0f ? ViewmodelMotion.Sample(SpearThrust, _thrust).Turn : Vector3.Zero;
        var aim = new Basis(Vector3.Left, Math.Clamp(f.Pitch * 0.6f, -0.5f, 0.6f) + 0.1f);
        Vector3 along = aim * new Vector3(0, 0, 1);
        Vector3 up = aim * Vector3.Up;
        var basis = new Basis(along, up, along.Cross(up));
        Transform3D gripR = HeldItems.Socket(_item!, "grip_r");
        Vector3 back = anchor + new Vector3(-0.15f, -0.24f, 0.12f) + along * thrust.X;
        var item = new Transform3D(basis, back - basis * gripR.Origin);
        _item!.Transform = item;
        Vector3 tip = HeldItems.HasSocket(_item, "tip") ? HeldItems.Socket(_item, "tip").Origin : gripR.Origin + Vector3.Right;
        var gripL = new Transform3D(gripR.Basis, gripR.Origin + (tip - gripR.Origin).Normalized() * 0.42f);
        _poser.RightHand = HandFrame.HandFor(item * gripR, HandFrame.PowerGrip, _gripR);
        _poser.LeftHand = HandFrame.HandFor(item * gripL, HandFrame.SupportGrip, _gripL);
        _poser.RightHandWeight = Smooth(_hold);
        _poser.LeftHandWeight = Smooth(_hold);
        _poser.TorsoLean = thrust.Y;
        _poser.TorsoTwist = thrust.Z;
    }

    // A spear's thrust: (push along the spear in metres, chest lean, chest turn) over the swing.
    private static readonly ViewmodelMotion.Key[] SpearThrust =
    {
        new(0f, Vector3.Zero, Vector3.Zero),
        new(0.24f, new Vector3(-0.13f, -0.04f, 0.14f), Vector3.Zero),
        new(0.4f, new Vector3(0.42f, 0.22f, -0.2f), Vector3.Zero, ViewmodelMotion.Ease.In),
        new(0.56f, new Vector3(0.38f, 0.2f, -0.18f), Vector3.Zero, ViewmodelMotion.Ease.Out),
        new(1f, Vector3.Zero, Vector3.Zero),
    };

    /// <summary>
    /// Bandaging (the left forearm raised across the chest, the right hand winding the bandage round it two and a half
    /// times, then pressing it down) and injecting (the right hand brings the injector to the left upper arm, presses,
    /// holds, and takes it away). The item in hand is the bandage or the injector.
    /// </summary>
    private void PlaceUse()
    {
        Vector3 anchor = _skeleton.GetBoneGlobalPose(_skeleton.FindBone("spine_03")).Origin;
        float t = _useTime;
        float seconds = UseSeconds(_use);
        float weight = Smooth(Math.Clamp(t / 0.25f, 0f, 1f)) * (1f - Smooth(Math.Clamp((t - (seconds - 0.3f)) / 0.3f, 0f, 1f)));
        if (_use == UseMotion.Bandage)
        {
            // Skeleton space: forward +Z, left +X, up +Y. The left forearm lies across the chest, fingers to the right.
            Vector3 wrist = anchor + new Vector3(-0.04f, 0.05f, 0.33f);
            Vector3 fingers = new Vector3(-1f, 0f, 0.25f).Normalized();
            var left = new Basis(fingers, Vector3.Up, fingers.Cross(Vector3.Up));
            _poser.LeftHand = HandFrame.HandFor(new Transform3D(left, wrist), HandFrame.PowerGrip, _gripL);
            Vector3 axis = -fingers;
            Vector3 centre = wrist + axis * 0.11f;
            Vector3 u = Vector3.Up, v = axis.Cross(u).Normalized();
            float winding = Smooth(Math.Clamp((t - 0.25f) / 1.5f, 0f, 1f)) * 2.5f * MathF.Tau;
            float press = Smooth(Math.Clamp((t - 1.75f) / 0.2f, 0f, 1f));
            Vector3 at = centre + (u * MathF.Cos(winding) + v * MathF.Sin(winding)) * 0.075f * (1f - press) + u * 0.05f * press;
            var right = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0));
            _poser.RightHand = HandFrame.HandFor(new Transform3D(right, at), HandFrame.PowerGrip, _gripR);
            _poser.LeftHandWeight = weight;
            _poser.RightHandWeight = weight;
            _poser.LeftPole = new Vector3(0.7f, -0.5f, -0.3f);
            return;
        }
        // Injecting: into the left upper arm, from in front and outside.
        Vector3 arm = _skeleton.GetBoneGlobalPose(_skeleton.FindBone("upperarm_l")).Origin + new Vector3(0.03f, -0.1f, 0.03f);
        Vector3 needle = new Vector3(0.8f, -0.1f, -0.55f).Normalized();
        float reach = Smooth(Math.Clamp((t - 0.1f) / 0.3f, 0f, 1f));
        float push = Smooth(Math.Clamp((t - 0.42f) / 0.08f, 0f, 1f)) * (1f - Smooth(Math.Clamp((t - 0.9f) / 0.15f, 0f, 1f)));
        Vector3 tipAt = arm - needle * (0.05f * (1f - reach) + 0.012f * (1f - push));
        Vector3 side = needle.Cross(Vector3.Up).Normalized();
        var basis = new Basis(needle, side.Cross(needle), side);
        Transform3D tip = HeldItems.HasSocket(_item!, "tip") ? HeldItems.Socket(_item!, "tip") : new Transform3D(Basis.Identity, new Vector3(0.09f, 0, 0));
        Vector3 grip = tipAt - basis * tip.Origin;
        _poser.RightHand = HandFrame.HandFor(new Transform3D(basis, grip), HandFrame.PowerGrip, _gripR);
        _poser.RightHandWeight = weight;
        _poser.LeftHandWeight = 0f;
        _poser.RightPole = new Vector3(-0.6f, -0.7f, -0.1f);
    }

    /// <summary>The pistol's magazine in a reload: out and away down the grip, then a full one pushed home.</summary>
    private void AnimatePistolMagazine()
    {
        if (_pistolMagazine == null)
        {
            return;
        }
        float t = _pistolReload;
        if (t < 0f)
        {
            _pistolMagazine.Transform = _pistolMagazineRest;
            _pistolMagazine.Visible = true;
            return;
        }
        float drop = t < 0.18f ? 0f : t < 0.32f ? Smooth((t - 0.18f) / 0.14f) : t < 0.56f ? 1f : t < 0.72f ? 0.65f * (1f - Smooth((t - 0.56f) / 0.16f)) : 0f;
        _pistolMagazine.Visible = t < 0.32f || t > 0.56f;
        _pistolMagazine.Transform = _pistolMagazineRest.Translated(new Vector3(0f, -0.11f * drop, 0f));
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Kick(float t) => t <= 0f ? 0f : MathF.Sin(MathF.Min(1f, (1f - t) * 4f) * MathF.PI * 0.5f) * t;

    private void SwapItem(HeldItemModel? model)
    {
        _item?.QueueFree();
        _item = null;
        _itemModel = model;
        _bowRig = null;
        _pistolMagazine = null;
        _draw = _bowAim = 0f;
        _thrust = -1f;
        _eating = -1f;
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
        _pistolMagazine = model.Kind == HeldKind.Pistol ? _item.FindChild("Magazine", true, false) as Node3D : null;
        _pistolMagazineRest = _pistolMagazine?.Transform ?? Transform3D.Identity;
        _bowRig = model.LeftHanded ? BowRig.Attach(_item) : null;
        if (model.Kind == HeldKind.Rifle || model.Class == "spear")
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
    /// A rifle is carried at the right shoulder, pointing where the player looks; both hands reach its grips. A reload
    /// (2.4 s, the server's) rolls the rifle over and dips it; the left hand goes to the magazine, away to the pouch at
    /// the left side, back with a full magazine (drawn in the hand) which it seats, then to the charging handle, pulls
    /// it and returns to the handguard.
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
        float t = _reload;
        if (t >= 0f)
        {
            // Rolled over to the left and the muzzle dipped while the magazine is changed.
            float window = Smooth(Math.Clamp(t / 0.12f, 0f, 1f)) * (1f - Smooth(Math.Clamp((t - 0.78f) / 0.18f, 0f, 1f)));
            Vector3 barrel = aim * Vector3.Back;
            b = new Basis(barrel, 0.55f * window) * new Basis(aim * Vector3.Right, -0.22f * window) * b;
        }
        Transform3D gripR = HeldItems.Socket(_item!, "grip_r");
        // Place the item so its right grip sits at the shoulder anchor.
        var item = new Transform3D(b, shoulder - b * gripR.Origin);
        _item!.Transform = item;
        Transform3D gripL = HeldItems.Socket(_item!, "grip_l");
        _poser.RightHand = HandFrame.HandFor(item * gripR, HandFrame.PowerGrip, _gripR);
        Transform3D left = HandFrame.HandFor(item * gripL, HandFrame.SupportGrip, _gripL);
        if (t >= 0f)
        {
            Vector4 w = ViewmodelMotion.ReloadHand(t);
            Vector3 well = (gripR.Origin + gripL.Origin) * 0.5f + new Vector3(0f, -0.09f, 0f);
            Vector3 pouch = item.AffineInverse() * (anchor + new Vector3(0.13f, -0.2f, 0.16f));
            Vector3 handle = gripR.Origin + new Vector3(0.13f, 0.065f, 0.05f);
            Vector3 pull = new Vector3(-0.12f, 0f, 0f) * Math.Max(0f, w.W - 1f);
            Vector3 at = gripL.Origin * w.X + well * w.Y + pouch * w.Z + (handle + pull) * Math.Min(1f, w.W);
            left = HandFrame.HandFor(new Transform3D(gripL.Basis, at), HandFrame.SupportGrip, _gripL);
            left = new Transform3D(item.Basis * left.Basis, item * left.Origin);
            // A full magazine in the hand from the pouch until it is seated.
            if (t > 0.36f && t < 0.6f)
            {
                ShowMagazine(item * at, item.Basis);
            }
        }
        _poser.LeftHand = left;
        _poser.RightHandWeight = Smooth(_hold);
        _poser.LeftHandWeight = Smooth(_hold);
    }

    /// <summary>The magazine a rifle's reload brings from the pouch: a dark steel box curving forward, under the hand.</summary>
    private void ShowMagazine(Vector3 palm, Basis gun)
    {
        if (_magazineProp == null)
        {
            _magazineProp = new MeshInstance3D
            {
                Name = "Magazine",
                Mesh = new BoxMesh { Size = new Vector3(0.075f, 0.17f, 0.03f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.07f, 0.075f), Metallic = 0.6f, Roughness = 0.45f },
            };
            _carry.AddChild(_magazineProp);
        }
        _magazineProp.Visible = true;
        // Item axes: +X barrel, +Y up; the magazine hangs below the palm, leaning forward like the gun's own.
        Basis lean = gun * new Basis(Vector3.Back, 0.25f);
        _magazineProp.Transform = new Transform3D(lean, palm + gun * new Vector3(0.01f, -0.07f, 0f));
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
