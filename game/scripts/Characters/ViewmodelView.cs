using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Characters;

/// <summary>What the first-person view needs each frame (GROUND.md §4.8).</summary>
public struct ViewmodelFrame
{
    /// <summary>Velocity in the camera's frame (m/s): x right, y up, z backwards.</summary>
    public Vector3 LocalVelocity;

    /// <summary>How fast the view turned this frame (radians per second): x yaw (left positive), y pitch (up positive).</summary>
    public Vector2 LookRate;

    public bool Grounded;
    public bool Sprinting;
    public bool Crouching;

    /// <summary>Aiming down the sights (guns), or drawing the bow and holding it drawn.</summary>
    public bool Aiming;
}

/// <summary>
/// The first-person arms and the held item (GROUND.md §4.8). A child of the player's camera: its local space is the
/// camera's. The arms are the player body's own (the same meshes and skin, only the arm regions drawn, with the worn
/// sleeves), on render layer 2, drawn with their own field of view and pulled towards the near plane so they never
/// clip into walls. Both hands reach the item's grips by IK; the item itself moves procedurally: sway, walk bob,
/// sprint pose, swing arc, recoil kick, aiming down the sights and a reload.
/// </summary>
public partial class ViewmodelView : Node3D
{
    /// <summary>Render layer of everything in the view model (bit 1, layer 2).</summary>
    public const uint RenderLayer = 1u << 1;

    private static readonly Dictionary<Material, Material> ItemMaterials = new();

    private Node3D _rig = null!;
    private Skeleton3D _skeleton = null!;
    private readonly List<MeshInstance3D> _arms = new();
    private readonly List<(MeshInstance3D Mesh, Wearable Item)> _sleeves = new();
    private readonly List<Wearable> _worn = new();
    private (Basis FrameToBone, Vector3 Palm) _gripR, _gripL;
    private int _upperR, _lowerR, _handR, _upperL, _lowerL, _handL;

    private HeldItemModel? _model;
    private Node3D? _item;
    private Node3D? _slide, _magazine, _bolt;
    private Transform3D _slideRest, _magazineRest, _boltRest;
    private ViewmodelFrame _frame;

    // Procedural state.
    private Vector2 _sway, _swayVelocity;
    private float _bobPhase, _bobAmount;
    private float _sprint, _aim, _crouch;
    private float _swing = -1f, _reload = -1f;

    // The bow's draw (0–1), how far it is raised to aim (0–1), and the time since an arrow was loosed; a spear's thrust
    // (0–1); eating, bandaging or injecting (seconds in).
    private float _draw, _bowAim, _loose = -1f, _thrust = -1f, _useTime = -1f;
    private CharacterEvent _use;
    private BowRig? _bowRig;

    /// <summary>How far the string comes back in first person (of a full draw): the drawing hand stops in front of the
    /// face instead of behind the camera.</summary>
    private const float FirstPersonDraw = 0.56f;
    private Vector3 _kick, _kickVelocity;
    private float _slideKick;
    private float _equip = 1f;

    /// <summary>Vertical field of view of the arms and item, degrees (a player setting, shared by every view model).</summary>
    public float Fov
    {
        get => CharacterMaterials.ViewmodelFov;
        set => CharacterMaterials.SetViewmodelFov(value);
    }

    /// <summary>0 at the hip, 1 fully aimed down the sights (the owner may narrow the camera's view with it).</summary>
    public float AimAmount => _aim;

    public HeldItemModel? Model => _model;

    /// <summary>Whose arms these are; set it before the view enters the tree.</summary>
    public BodyType Body { get; set; } = BodyType.Male;

    private BodyAssets _assets = null!;
    private Look? _look;

    /// <summary>The player's look (<see cref="Looks.ForPlayer(ulong, BodyType)"/>): the arms take its skin tone.</summary>
    public Look Look
    {
        get => _look ?? Looks.Default(Body);
        set
        {
            _look = value;
            if (_arms.Count > 0)
            {
                LookUniforms.Apply(_arms, value);
            }
        }
    }

    public override void _Ready()
    {
        _assets = CharacterAssets.For(Body);
        _rig = new Node3D { Name = "Rig" };
        AddChild(_rig);
        _skeleton = _assets.NewSkeleton();
        _skeleton.ModifierCallbackModeProcess = Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
        _rig.AddChild(_skeleton);
        // The camera sits at the eyes: the body faces +Z, the camera looks along −Z.
        int head = _skeleton.FindBone("head");
        Vector3 eye = new(0f, _assets.EyeHeight, _skeleton.GetBoneGlobalRest(head).Origin.Z + 0.09f);
        _rig.Transform = new Transform3D(new Basis(Vector3.Up, MathF.PI), Vector3.Zero) * new Transform3D(Basis.Identity, -eye);
        MeshPart body = _assets.Part("body_lod0")!;
        _arms.Add(Instance(body));
        // In first person the fist closes tightly, so the handle sits further into the palm, across its crease.
        _gripR = HandFrame.Measure(_skeleton, right: true, along: HandPoses.FistAlong, lift: HandPoses.FistLift);
        _gripL = HandFrame.Measure(_skeleton, right: false);
        LookUniforms.Apply(_arms, Look);
        _upperR = _skeleton.FindBone("upperarm_r");
        _lowerR = _skeleton.FindBone("lowerarm_r");
        _handR = _skeleton.FindBone("hand_r");
        _upperL = _skeleton.FindBone("upperarm_l");
        _lowerL = _skeleton.FindBone("lowerarm_l");
        _handL = _skeleton.FindBone("hand_l");
        ApplyRegions();
        Visible = false;
    }

    private MeshInstance3D Instance(MeshPart part)
    {
        var mi = new MeshInstance3D
        {
            Name = part.Name,
            Mesh = part.Mesh,
            Skin = part.Skin,
            Skeleton = new NodePath(".."),
            Layers = RenderLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 2f,
        };
        for (int i = 0; i < part.Mesh.GetSurfaceCount(); i++)
        {
            string surface = part.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "skin";
            mi.SetSurfaceOverrideMaterial(i, CharacterAssets.MaterialFor(surface, viewmodel: true));
        }
        _skeleton.AddChild(mi);
        return mi;
    }

    // ---------------------------------------------------------------- the contract

    /// <summary>The worn clothing; only what covers the arms shows.</summary>
    public void Wear(IEnumerable<Wearable> items)
    {
        foreach ((MeshInstance3D mesh, _) in _sleeves)
        {
            CharacterView.Discard(mesh);
        }
        _sleeves.Clear();
        _worn.Clear();
        _worn.AddRange(items);
        foreach (Wearable w in _worn)
        {
            MeshPart? part = w.OnArms ? _assets.Part(w.Mesh + "_lod0") : null;
            if (part != null)
            {
                _sleeves.Add((Instance(part), w));
            }
        }
        ApplyRegions();
    }

    /// <summary>What the hands hold. Unarmed shows no arms, as in Rust.</summary>
    public void Hold(HeldKind kind, string? itemId = null)
    {
        HeldItemModel? model = HeldItems.For(kind, itemId);
        if (model == _model)
        {
            return;
        }
        _item?.QueueFree();
        _item = null;
        _slide = _magazine = _bolt = null;
        _model = model;
        _equip = 0f;
        _swing = _reload = -1f;
        Visible = model != null;
        if (model == null)
        {
            return;
        }
        _item = HeldItems.Instantiate(model);
        if (_item == null)
        {
            return;
        }
        AddChild(_item);
        _bowRig = model.LeftHanded ? BowRig.Attach(_item) : null;
        _draw = _bowAim = 0f;
        _loose = _thrust = _useTime = -1f;
        PrepareItem(_item);
        // The catalogue's guns name their moving parts Slide, Magazine, Bolt (the art package's part_* names too).
        _slide = (_item.FindChild("part_slide", true, false) ?? _item.FindChild("Slide", true, false)) as Node3D;
        _magazine = (_item.FindChild("part_magazine", true, false) ?? _item.FindChild("Magazine", true, false)) as Node3D;
        _bolt = (_item.FindChild("part_bolt", true, false) ?? _item.FindChild("Bolt", true, false)) as Node3D;
        _slideRest = _slide?.Transform ?? Transform3D.Identity;
        _magazineRest = _magazine?.Transform ?? Transform3D.Identity;
        _boltRest = _bolt?.Transform ?? Transform3D.Identity;
    }

    public void Apply(in ViewmodelFrame frame) => _frame = frame;

    public void Trigger(CharacterEvent e)
    {
        if (_model == null)
        {
            return;
        }
        switch (e)
        {
            case CharacterEvent.Swing when _model.Class == "spear" && _thrust < 0f:
                _thrust = 0f;
                break;
            case CharacterEvent.Fire when _model.LeftHanded:
                if (_draw > 0.15f)
                {
                    _loose = 0f;
                    _draw = 0f;
                }
                break;
            case CharacterEvent.Use or CharacterEvent.Eat or CharacterEvent.Bandage or CharacterEvent.Inject when _useTime < 0f:
                _use = e != CharacterEvent.Use ? e : _model.Class switch
                {
                    "food" => CharacterEvent.Eat,
                    "medical" when _model.Id.Contains("injector", StringComparison.Ordinal) => CharacterEvent.Inject,
                    "medical" => CharacterEvent.Bandage,
                    _ => CharacterEvent.Use,
                };
                if (_use != CharacterEvent.Use)
                {
                    _useTime = 0f;
                }
                break;
            case CharacterEvent.Swing when _model.Swung && _swing < 0f:
                _swing = 0f;
                break;
            case CharacterEvent.Fire when _model.Kind is HeldKind.Pistol or HeldKind.Rifle:
                float strength = _model.Kind == HeldKind.Rifle ? 1f : 0.7f;
                _kickVelocity += new Vector3(0.06f * strength, 1.25f * strength, 2.4f * strength);
                _slideKick = 1f;
                break;
            case CharacterEvent.Reload when _model.Kind is HeldKind.Pistol or HeldKind.Rifle && _reload < 0f:
                _reload = 0f;
                break;
        }
    }

    /// <summary>What first person draws: forearms and hands. Upper arms would only fill the view near the camera.</summary>
    private const BodyRegions Shown = BodyRegions.Forearm | BodyRegions.Hand;

    private void ApplyRegions()
    {
        BodyRegions hidden = BodyRegions.All & ~Shown;
        foreach (Wearable w in _worn)
        {
            hidden |= w.Hides & Shown;
        }
        foreach (MeshInstance3D arm in _arms)
        {
            arm.SetInstanceShaderParameter("hidden_regions", (int)hidden);
        }
        foreach ((MeshInstance3D mesh, _) in _sleeves)
        {
            mesh.SetInstanceShaderParameter("hidden_regions", (int)(BodyRegions.All & ~Shown));
        }
    }

    /// <summary>Layer 2, no shadows, and the item's own materials drawn with the view model's field of view.</summary>
    private void PrepareItem(Node node)
    {
        if (node is MeshInstance3D mi)
        {
            mi.Layers = RenderLayer;
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.ExtraCullMargin = 2f;
            for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
            {
                Material? m = mi.GetActiveMaterial(i);
                if (m != null)
                {
                    if (!ItemMaterials.TryGetValue(m, out Material? vm))
                    {
                        vm = CharacterMaterials.ForViewmodel(m);
                        ItemMaterials[m] = vm;
                    }
                    mi.SetSurfaceOverrideMaterial(i, vm);
                }
            }
            // Only the most detailed mesh of the item.
            if (mi.Name.ToString().Contains("_lod", StringComparison.Ordinal) && !mi.Name.ToString().EndsWith("_lod0", StringComparison.Ordinal))
            {
                mi.Visible = false;
            }
        }
        foreach (Node child in node.GetChildren())
        {
            PrepareItem(child);
        }
    }

    // ---------------------------------------------------------------- motion

    public override void _Process(double delta)
    {
        if (_model == null || _item == null)
        {
            return;
        }
        float dt = (float)Math.Min(delta, 0.05);
        Animate(dt);
        _skeleton.ResetBonePoses();
        Pose();
        _skeleton.Advance(dt);
    }

    private void Animate(float dt)
    {
        ViewmodelFrame f = _frame;
        bool gun = _model!.Kind is HeldKind.Pistol or HeldKind.Rifle;
        _equip = Math.Min(1f, _equip + dt * 3f);
        float speed = new Vector2(f.LocalVelocity.X, f.LocalVelocity.Z).Length();
        _sprint = Approach(_sprint, f.Sprinting && speed > 3f && _swing < 0f && _reload < 0f ? 1f : 0f, dt * 6f);
        _aim = Approach(_aim, f.Aiming && gun && _reload < 0f && _sprint < 0.5f ? 1f : 0f, dt * 9f);
        _crouch = Approach(_crouch, f.Crouching ? 1f : 0f, dt * 6f);

        // Sway: the item lags behind the view, on a spring.
        Vector2 target = new Vector2(-f.LookRate.X, f.LookRate.Y) * 0.012f * (1f - 0.75f * _aim);
        target = target.LimitLength(0.07f);
        Vector2 force = (target - _sway) * 140f - _swayVelocity * 18f;
        _swayVelocity += force * dt;
        _sway += _swayVelocity * dt;

        // Walk bob, in step with distance walked.
        float bobTarget = f.Grounded ? Math.Clamp(speed / 5.5f, 0f, 1f) : 0f;
        _bobAmount = Approach(_bobAmount, bobTarget, dt * 5f);
        _bobPhase += dt * (speed > 0.1f ? 2.1f + speed * 0.85f : 0f) * MathF.PI;

        // Recoil: a kick on a stiff spring.
        Vector3 kickForce = -_kick * 260f - _kickVelocity * 24f;
        _kickVelocity += kickForce * dt;
        _kick += _kickVelocity * dt;
        _slideKick = Math.Max(0f, _slideKick - dt * 14f);

        if (_swing >= 0f)
        {
            _swing += dt / Math.Max(0.3f, _model.SwingTime);
            if (_swing >= 1f)
            {
                _swing = -1f;
            }
        }
        if (_reload >= 0f)
        {
            _reload += dt / (_model.Kind == HeldKind.Rifle ? 2.3f : 1.8f);
            if (_reload >= 1f)
            {
                _reload = -1f;
            }
        }
        if (_model.LeftHanded)
        {
            _bowAim = Approach(_bowAim, f.Aiming && _sprint < 0.5f ? 1f : 0f, dt * 3.2f);
            if (_loose >= 0f)
            {
                _loose += dt;
                if (_loose > 0.55f)
                {
                    _loose = -1f;
                }
            }
            if (f.Aiming && _loose < 0f && _bowAim > 0.6f)
            {
                _draw = Math.Min(1f, _draw + dt / 0.95f);
            }
            else if (!f.Aiming)
            {
                _draw = Math.Max(0f, _draw - dt * 2.5f);
            }
            _bowRig?.Update(Smooth(_draw) * FirstPersonDraw, f.Aiming && _loose < 0f);
        }
        if (_thrust >= 0f)
        {
            _thrust += dt / Math.Max(0.5f, _model.SwingTime);
            if (_thrust >= 1f)
            {
                _thrust = -1f;
            }
        }
        if (_useTime >= 0f)
        {
            _useTime += dt;
            if (_useTime >= UseSeconds(_use))
            {
                _useTime = -1f;
            }
        }
    }

    private static float UseSeconds(CharacterEvent use) => use switch
    {
        CharacterEvent.Bandage => 2.3f,
        CharacterEvent.Inject => 1.4f,
        _ => 1.3f,
    };

    private static float Approach(float value, float target, float step) =>
        value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>The item's transform in camera space, from its hip and sight placements and the motion layers.</summary>
    private Transform3D ItemPlacement()
    {
        HeldItemModel m = _model!;
        // Item axes (+X barrel or blade, +Y up) to camera axes (−Z forward, +Y up).
        var toCamera = new Basis(new Vector3(0, 0, -1), new Vector3(0, 1, 0), new Vector3(1, 0, 0));
        Transform3D grip = HeldItems.Socket(_item!, m.LeftHanded ? "grip_l" : "grip_r");
        Vector3 hip;
        Basis hipTurn;
        switch (m.Kind)
        {
            case HeldKind.Pistol:
                hip = new Vector3(0.06f, -0.17f, -0.36f);
                hipTurn = Basis.FromEuler(new Vector3(0.03f, 0.05f, 0f));
                break;
            case HeldKind.Rifle:
                hip = new Vector3(0.155f, -0.205f, -0.17f);
                hipTurn = Basis.FromEuler(new Vector3(0.02f, 0.06f, 0f));
                break;
            default:
                // Tool: the handle rises from the right hand, leaning forward and in; the blade faces forward.
                hip = new Vector3(0.235f, -0.195f, -0.42f);
                hipTurn = Basis.Identity;
                break;
        }
        // Hip placement puts the right grip at `hip`.
        Basis basis = hipTurn * toCamera;
        if (m.Kind == HeldKind.Tool)
        {
            Vector3 handle = new Vector3(-0.24f, 0.78f, -0.57f).Normalized();
            Vector3 blade = new Vector3(-0.45f, -0.25f, -0.86f);
            blade = (blade - handle * blade.Dot(handle)).Normalized();
            basis = new Basis(blade, handle, blade.Cross(handle));
        }
        Vector3 origin = hip - basis * grip.Origin;
        var place = new Transform3D(basis, origin);
        if (m.Kind == HeldKind.Tool && !m.Swung && m.Entry.Held is HeldInfo held)
        {
            // What isn't swung by a handle (spears, the bow, food, bandages, locks, charges) sits where the model
            // catalogue's first-person pose puts its grip.
            place = held.FirstPerson.Transform * grip.AffineInverse();
        }
        if (m.LeftHanded && _bowAim > 0f)
        {
            // The bow raised to aim: upright (canted a little to the right) in front of the left eye, the arrow on its
            // shelf pointing at the crosshair from just below and right of it.
            Vector3 forward = new Vector3(-0.03f, 0.035f, -1f).Normalized();
            Vector3 up = Vector3.Up.Rotated(forward, -0.2f);
            up = (up - forward * up.Dot(forward)).Normalized();
            var bow = new Basis(forward, up, forward.Cross(up));
            var aimed = new Transform3D(bow, new Vector3(-0.035f, -0.085f, -0.66f) - bow * grip.Origin);
            float a = Smooth(_bowAim);
            place = new Transform3D(new Basis(place.Basis.GetRotationQuaternion().Slerp(aimed.Basis.GetRotationQuaternion(), a)),
                place.Origin.Lerp(aimed.Origin, a));
        }
        if (_thrust >= 0f)
        {
            // A spear's thrust: drawn back a little, driven forward along the shaft, held, recovered.
            Vector3 push = ViewmodelMotion.Sample(SpearThrust, _thrust).Turn;
            place.Origin += place.Basis.X.Normalized() * push.X + new Vector3(0f, push.Y, 0f);
        }
        if (_useTime >= 0f)
        {
            place = UsePlacement(place, grip);
        }

        if (m.Kind is HeldKind.Pistol or HeldKind.Rifle && _aim > 0f)
        {
            // Sights on the camera's centre line: the model's eye socket at the camera, or the sight at the item's
            // eye relief.
            Basis aimBasis = toCamera;
            Vector3 aimOrigin = m.EyeRelief <= 0f && HeldItems.HasSocket(_item!, "eye")
                ? -(aimBasis * HeldItems.Socket(_item!, "eye").Origin)
                : new Vector3(0, 0, -Math.Max(m.EyeRelief, 0.1f)) - aimBasis * HeldItems.Socket(_item!, "sight").Origin;
            float a = Smooth(_aim);
            place = new Transform3D(
                new Basis(place.Basis.GetRotationQuaternion().Slerp(aimBasis.GetRotationQuaternion(), a)),
                place.Origin.Lerp(aimOrigin, a));
        }

        float notAiming = 1f - 0.85f * _aim;
        // Sway and bob.
        Vector3 bob = new Vector3(MathF.Sin(_bobPhase) * 0.011f, -MathF.Abs(MathF.Cos(_bobPhase)) * 0.009f, 0f) * _bobAmount * notAiming;
        Basis sway = Basis.FromEuler(new Vector3(_sway.Y, _sway.X, _sway.X * 0.6f));
        place = new Transform3D(sway * place.Basis, sway * place.Origin + bob + new Vector3(_sway.X * 0.25f, _sway.Y * 0.2f, 0f));

        // Sprint: tools lowered and tilted, guns pointed down and across.
        if (_sprint > 0f)
        {
            float s = Smooth(_sprint);
            Basis lower = m.Kind is HeldKind.Pistol or HeldKind.Rifle
                ? Basis.FromEuler(new Vector3(-0.55f, 0.75f, 0.35f) * s)
                : Basis.FromEuler(new Vector3(-0.45f, 0.15f, -0.25f) * s);
            Vector3 pivot = place * grip.Origin;
            place = new Transform3D(lower * place.Basis, pivot + lower * (place.Origin - pivot) + new Vector3(-0.04f, -0.05f, 0.03f) * s);
        }

        // Swing: up and back over the shoulder, a beat, down and across to land at the item's strike time, a jolt on
        // the impact, follow through and back (ViewmodelMotion).
        if (_swing >= 0f)
        {
            (Vector3 turn, Vector3 offset) = ViewmodelMotion.Swing(_swing, m.StrikeTime / Math.Max(0.3f, m.SwingTime), m.SwingTime);
            Basis arc = Basis.FromEuler(turn);
            Vector3 hand = place * grip.Origin;
            place = new Transform3D(arc * place.Basis, hand + arc * (place.Origin - hand) + offset);
        }

        // Recoil kick: back and up.
        if (_kick.LengthSquared() > 1e-8f)
        {
            Basis up = Basis.FromEuler(new Vector3(_kick.Y * 0.06f * notAiming + _kick.Y * 0.025f, _kick.X * 0.05f, 0f));
            place = new Transform3D(up * place.Basis, place.Origin + new Vector3(0f, _kick.Y * 0.004f, _kick.Z * 0.012f));
        }

        // Reload: tilt in, magazine out and in (a jolt as it seats), work the slide or handle (a jolt), back.
        if (_reload >= 0f)
        {
            (Vector3 turn, Vector3 offset) = ViewmodelMotion.Reload(_reload);
            Basis tilt = Basis.FromEuler(turn);
            Vector3 pivot = place * grip.Origin;
            place = new Transform3D(tilt * place.Basis, pivot + tilt * (place.Origin - pivot) + offset);
        }

        // Equip: rise into view; crouch: a touch lower.
        float equip = 1f - Smooth(_equip);
        place.Origin += new Vector3(0f, -0.28f * equip - 0.012f * _crouch * notAiming, 0.05f * equip);
        place.Basis = Basis.FromEuler(new Vector3(-0.6f * equip, 0f, 0f)) * place.Basis;
        return place;
    }

    // A spear's thrust: (push along the shaft, lift) over the swing.
    private static readonly ViewmodelMotion.Key[] SpearThrust =
    {
        new(0f, Vector3.Zero, Vector3.Zero),
        new(0.24f, new Vector3(-0.08f, 0.01f, 0f), Vector3.Zero),
        new(0.4f, new Vector3(0.36f, -0.01f, 0f), Vector3.Zero, ViewmodelMotion.Ease.In),
        new(0.56f, new Vector3(0.33f, -0.005f, 0f), Vector3.Zero, ViewmodelMotion.Ease.Out),
        new(1f, Vector3.Zero, Vector3.Zero),
    };

    // Where the left forearm is raised to while bandaging or injecting (camera space): across the bottom of the view.
    private static readonly Vector3 ForearmWrist = new(0.03f, -0.13f, -0.42f);
    private static readonly Vector3 ForearmAxis = new Vector3(-1f, -0.35f, 0.45f).Normalized();

    /// <summary>
    /// The item in hand while it is used: food to the mouth and a few bites; a bandage wound round the raised left
    /// forearm two and a half times and pressed down; an injector brought to the forearm, pushed in, held and taken
    /// out.
    /// </summary>
    private Transform3D UsePlacement(Transform3D place, Transform3D grip)
    {
        float t = _useTime;
        float seconds = UseSeconds(_use);
        float k = Smooth(Math.Clamp(t / 0.3f, 0f, 1f)) * (1f - Smooth(Math.Clamp((t - (seconds - 0.3f)) / 0.3f, 0f, 1f)));
        Transform3D target = place;
        switch (_use)
        {
            case CharacterEvent.Eat:
            {
                float chew = MathF.Sin(Math.Max(0f, t - 0.3f) * MathF.Tau * 2.6f) * 0.006f * (t > 0.3f && t < 1.0f ? 1f : 0f);
                var basis = Basis.FromEuler(new Vector3(0.9f, 0.4f, 0f)) * place.Basis;
                target = new Transform3D(basis, new Vector3(0.02f, -0.11f + chew, -0.17f) - basis * grip.Origin);
                break;
            }
            case CharacterEvent.Bandage:
            {
                Vector3 centre = ForearmWrist + ForearmAxis * 0.07f;
                Vector3 v = ForearmAxis.Cross(Vector3.Up).Normalized();
                Vector3 u = v.Cross(ForearmAxis).Normalized();
                float winding = Smooth(Math.Clamp((t - 0.3f) / 1.45f, 0f, 1f)) * 2.5f * MathF.Tau;
                float press = Smooth(Math.Clamp((t - 1.75f) / 0.2f, 0f, 1f));
                Vector3 at = centre + (u * MathF.Cos(winding) + v * MathF.Sin(winding)) * 0.06f * (1f - press) + u * 0.045f * press;
                var basis = Basis.FromEuler(new Vector3(0f, 0.6f, 0.4f)) * place.Basis;
                target = new Transform3D(basis, at - basis * grip.Origin);
                break;
            }
            case CharacterEvent.Inject:
            {
                Vector3 arm = ForearmWrist + ForearmAxis * 0.09f + new Vector3(0f, 0.02f, 0f);
                Vector3 needle = new Vector3(-0.55f, -0.35f, -0.4f).Normalized();
                float push = Smooth(Math.Clamp((t - 0.45f) / 0.08f, 0f, 1f)) * (1f - Smooth(Math.Clamp((t - 0.95f) / 0.15f, 0f, 1f)));
                Vector3 side = needle.Cross(Vector3.Up).Normalized();
                var basis = new Basis(needle, side.Cross(needle), side);
                Transform3D tip = HeldItems.HasSocket(_item!, "tip") ? HeldItems.Socket(_item!, "tip") : new Transform3D(Basis.Identity, new Vector3(0.09f, 0f, 0f));
                Vector3 tipAt = arm - needle * (0.03f - 0.02f * push);
                target = new Transform3D(basis, tipAt - basis * tip.Origin);
                break;
            }
        }
        return place.InterpolateWith(target, k);
    }

    /// <summary>How far the left forearm is raised into view for a bandage or an injection (0 to 1).</summary>
    private float ForearmRaised
    {
        get
        {
            if (_useTime < 0f || _use is not (CharacterEvent.Bandage or CharacterEvent.Inject))
            {
                return 0f;
            }
            float seconds = UseSeconds(_use);
            return Smooth(Math.Clamp(_useTime / 0.25f, 0f, 1f)) * (1f - Smooth(Math.Clamp((_useTime - (seconds - 0.25f)) / 0.25f, 0f, 1f)));
        }
    }

    private void Pose()
    {
        Transform3D item = ItemPlacement();
        _item!.Transform = item;
        AnimateParts();

        // Hands onto the grips (camera space → skeleton space), from shoulders set lower and further forward than the
        // body's, so the forearms rise into view from the lower corners as in Rust (only forearms and hands are drawn;
        // nobody sees the shoulders move).
        Transform3D toSkeleton = (_rig.Transform * _skeleton.Transform).AffineInverse();
        Shoulder(_upperR, ShoulderOffsetR, toSkeleton);
        Shoulder(_upperL, ShoulderOffsetL, toSkeleton);
        if (_model!.LeftHanded)
        {
            // The bow: the left hand holds its grip; the right hand draws the string from the nock (an arrow on it) while
            // aiming, and is down out of view otherwise.
            Transform3D gripL1 = item * HeldItems.Socket(_item, "grip_l");
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, toSkeleton * HandFrame.HandFor(gripL1,
                HandFrame.PowerGrip, _gripL), 1f, new Vector3(0.15f, -1f, 0.1f));
            HandPoses.Apply(_skeleton, HandPoses.Grip.Fist, right: false);
            Transform3D rest = _skeleton.GetBoneGlobalRest(_handR);
            float a = Smooth(_bowAim);
            if (a > 0.001f && _bowRig != null)
            {
                Vector3 nock = item * _bowRig.Nock(Smooth(_draw) * FirstPersonDraw);
                if (_loose >= 0f)
                {
                    // Loosed: the hand flies back past the cheek and opens, then drops away.
                    float k = MathF.Sin(MathF.Min(1f, _loose / 0.15f) * MathF.PI * 0.5f);
                    nock += new Vector3(0.05f, -0.02f, 0.06f) * k + new Vector3(0.04f, -0.25f, 0.05f) * Smooth(Math.Clamp((_loose - 0.25f) / 0.3f, 0f, 1f));
                }
                Vector3 fingers = item.Basis.X.Normalized();
                Vector3 thumb = item.Basis.Y.Normalized();
                var hook = new Transform3D(new Basis(fingers, thumb, fingers.Cross(thumb)), nock);
                Transform3D drawing = toSkeleton * HandFrame.HandFor(hook, HandFrame.PowerGrip, _gripR);
                CharacterPoser.ReachArm(_skeleton, _upperR, _lowerR, _handR, rest.InterpolateWith(drawing, a), 1f,
                    new Vector3(-0.8f, -0.4f, -0.3f));
                HandPoses.Apply(_skeleton, _loose >= 0f ? HandPoses.Grip.Relaxed : HandPoses.Grip.Support, right: true);
                return;
            }
            CharacterPoser.ReachArm(_skeleton, _upperR, _lowerR, _handR, rest, 1f, new Vector3(-0.7f, -0.7f, -0.2f));
            return;
        }
        Transform3D gripR = item * HeldItems.Socket(_item, "grip_r");
        Transform3D handR = toSkeleton * HandFrame.HandFor(gripR, HandFrame.PowerGrip, _gripR);
        CharacterPoser.ReachArm(_skeleton, _upperR, _lowerR, _handR, handR, 1f, new Vector3(-0.7f, -0.7f, -0.1f));
        HandPoses.Apply(_skeleton, _model.Kind == HeldKind.Tool ? HandPoses.Grip.Fist : HandPoses.Grip.Pistol, right: true);
        bool twoHands = _model.Kind is HeldKind.Pistol or HeldKind.Rifle && HeldItems.HasSocket(_item, "grip_l");
        if (twoHands)
        {
            Transform3D gripL = item * HeldItems.Socket(_item, "grip_l");
            Basis support = _model.Kind == HeldKind.Pistol ? HandFrame.PistolSupport : HandFrame.SupportGrip;
            Transform3D handL = toSkeleton * HandFrame.HandFor(gripL, support, _gripL);
            if (_reload >= 0f)
            {
                // The support hand goes to the magazine well, away below the gun for a new magazine, back up to seat
                // it, then to the slide (pistol) or the charging handle (rifle) and pulls it, then back to its grip.
                Vector4 w = ViewmodelMotion.ReloadHand(_reload);
                bool pistol = _model.Kind == HeldKind.Pistol;
                Vector3 right = HeldItems.Socket(_item, "grip_r").Origin;
                Vector3 left = HeldItems.Socket(_item, "grip_l").Origin;
                Vector3 well = pistol ? right + new Vector3(-0.01f, -0.1f, 0f) : (right + left) * 0.5f + new Vector3(0f, -0.1f, 0f);
                Vector3 away = well + new Vector3(-0.06f, -0.22f, -0.08f);
                Vector3 work = pistol
                    ? HeldItems.Socket(_item, "sight").Origin + new Vector3(0.02f, -0.012f, 0f)
                    : right + new Vector3(0.12f, 0.07f, 0.045f);
                Vector3 pull = new Vector3(-0.2f, 0f, 0f) * Math.Max(0f, w.W - 1f);
                Vector3 at = left * w.X + well * w.Y + away * w.Z + (work + pull) * Math.Min(1f, w.W);
                handL = toSkeleton * HandFrame.HandFor(new Transform3D(gripL.Basis, item * at), support, _gripL);
            }
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, handL, 1f, new Vector3(0.15f, -1f, 0.1f));
            HandPoses.Apply(_skeleton, HandPoses.Grip.Support, right: false);
        }
        else if (ForearmRaised > 0f)
        {
            // Bandaging or injecting: the left forearm raised across the bottom of the view, palm down.
            Vector3 fingers = -ForearmAxis;
            Vector3 side = fingers.Cross(Vector3.Up).Normalized();
            var palmDown = new Basis(fingers, side.Cross(fingers).Normalized(), side);
            Transform3D raised = toSkeleton * HandFrame.HandFor(new Transform3D(palmDown, ForearmWrist), HandFrame.PowerGrip, _gripL);
            Transform3D rest = _skeleton.GetBoneGlobalRest(_handL);
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, rest.InterpolateWith(raised, ForearmRaised), 1f,
                new Vector3(0.7f, -0.7f, 0.1f));
            HandPoses.Apply(_skeleton, HandPoses.Grip.Relaxed, right: false);
        }
        else
        {
            // The free left hand stays down out of view.
            Transform3D rest = _skeleton.GetBoneGlobalRest(_handL);
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, rest, 1f, new Vector3(0.7f, -0.7f, -0.2f));
        }
    }

    // Where the first-person shoulders sit relative to the body's, in camera space (x right, y up, z back).
    private static readonly Vector3 ShoulderOffsetR = new(-0.03f, -0.1f, -0.06f);
    private static readonly Vector3 ShoulderOffsetL = new(0.1f, -0.12f, -0.08f);

    private void Shoulder(int upper, Vector3 offset, Transform3D toSkeleton)
    {
        Transform3D parent = _skeleton.GetBoneGlobalPose(_skeleton.GetBoneParent(upper));
        Vector3 target = _skeleton.GetBoneGlobalPose(upper).Origin + toSkeleton.Basis * offset;
        _skeleton.SetBonePosePosition(upper, parent.AffineInverse() * target);
    }

    private void AnimateParts()
    {
        if (_slide != null)
        {
            _slide.Transform = _slideRest.Translated(new Vector3(-0.035f * _slideKick, 0f, 0f));
        }
        if (_bolt != null)
        {
            _bolt.Transform = _boltRest.Translated(new Vector3(-0.06f * _slideKick, 0f, 0f));
        }
        if (_magazine != null)
        {
            float drop = 0f;
            if (_reload >= 0f)
            {
                float t = _reload;
                drop = t < 0.35f ? Smooth(t / 0.35f) : t < 0.6f ? 1f : 1f - Smooth((t - 0.6f) / 0.3f);
                drop = Math.Clamp(drop, 0f, 1f);
            }
            _magazine.Transform = _magazineRest.Translated(new Vector3(0.02f * drop, -0.22f * drop, 0f));
            _magazine.Visible = drop < 0.95f;
        }
    }
}
