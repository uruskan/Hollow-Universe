using System;
using System.Collections.Generic;
using Godot;

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

    /// <summary>Aiming down the sights (guns only).</summary>
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
    private Vector3 _kick, _kickVelocity;
    private float _slideKick;
    private float _equip = 1f;

    /// <summary>Vertical field of view of the arms and item, degrees.</summary>
    public float Fov => CharacterMaterials.ViewmodelFov;

    /// <summary>0 at the hip, 1 fully aimed down the sights (the owner may narrow the camera's view with it).</summary>
    public float AimAmount => _aim;

    public HeldItemModel? Model => _model;

    public override void _Ready()
    {
        CharacterAssets.Load();
        _rig = new Node3D { Name = "Rig" };
        AddChild(_rig);
        _skeleton = CharacterAssets.NewSkeleton();
        _skeleton.ModifierCallbackModeProcess = Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
        _rig.AddChild(_skeleton);
        // The camera sits at the eyes: the body faces +Z, the camera looks along −Z.
        int head = _skeleton.FindBone("head");
        Vector3 eye = new(0f, CharacterAssets.EyeHeight, _skeleton.GetBoneGlobalRest(head).Origin.Z + 0.09f);
        _rig.Transform = new Transform3D(new Basis(Vector3.Up, MathF.PI), Vector3.Zero) * new Transform3D(Basis.Identity, -eye);
        MeshPart body = CharacterAssets.Part("body_lod0")!;
        _arms.Add(Instance(body));
        _gripR = HandFrame.Measure(_skeleton, right: true);
        _gripL = HandFrame.Measure(_skeleton, right: false);
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
            mesh.QueueFree();
        }
        _sleeves.Clear();
        _worn.Clear();
        _worn.AddRange(items);
        foreach (Wearable w in _worn)
        {
            MeshPart? part = w.OnArms ? CharacterAssets.Part(w.Mesh + "_lod0") : null;
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
        PrepareItem(_item);
        _slide = _item.FindChild("part_slide", true, false) as Node3D;
        _magazine = _item.FindChild("part_magazine", true, false) as Node3D;
        _bolt = _item.FindChild("part_bolt", true, false) as Node3D;
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
            case CharacterEvent.Swing when _model.Kind == HeldKind.Tool && _swing < 0f:
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

    private void ApplyRegions()
    {
        BodyRegions hidden = BodyRegions.All & ~BodyRegions.Arms;
        foreach (Wearable w in _worn)
        {
            hidden |= w.Hides & BodyRegions.Arms;
        }
        foreach (MeshInstance3D arm in _arms)
        {
            arm.SetInstanceShaderParameter("hidden_regions", (int)hidden);
        }
        foreach ((MeshInstance3D mesh, _) in _sleeves)
        {
            mesh.SetInstanceShaderParameter("hidden_regions", (int)(BodyRegions.All & ~BodyRegions.Arms));
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
                if (m is BaseMaterial3D bm)
                {
                    var copy = (BaseMaterial3D)bm.Duplicate();
                    copy.UseFovOverride = true;
                    copy.FovOverride = Fov;
                    copy.UseZClipScale = true;
                    copy.ZClipScale = 0.12f;
                    mi.SetSurfaceOverrideMaterial(i, copy);
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
            _swing += dt / 0.62f;
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
    }

    private static float Approach(float value, float target, float step) =>
        value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>The item's transform in camera space, from its hip and sight placements and the motion layers.</summary>
    private Transform3D ItemPlacement()
    {
        HeldItemModel m = _model!;
        // Item axes (+X barrel or blade, +Y up) to camera axes (−Z forward, +Y up).
        var toCamera = new Basis(new Vector3(0, 0, -1), new Vector3(0, 1, 0), new Vector3(1, 0, 0));
        Transform3D grip = HeldItems.Socket(_item!, "grip_r");
        Vector3 hip;
        Basis hipTurn;
        switch (m.Kind)
        {
            case HeldKind.Pistol:
                hip = new Vector3(0.13f, -0.16f, -0.36f);
                hipTurn = Basis.FromEuler(new Vector3(0.03f, 0.05f, 0f));
                break;
            case HeldKind.Rifle:
                hip = new Vector3(0.12f, -0.17f, -0.28f);
                hipTurn = Basis.FromEuler(new Vector3(0.02f, 0.035f, 0f));
                break;
            default:
                // Tool: held up at the right, head forward and a little inward.
                hip = new Vector3(0.26f, -0.27f, -0.42f);
                hipTurn = Basis.FromEuler(new Vector3(0.95f, 0.25f, 0.2f));
                break;
        }
        // Hip placement puts the right grip at `hip`.
        Basis basis = hipTurn * toCamera;
        Vector3 origin = hip - basis * grip.Origin;
        var place = new Transform3D(basis, origin);

        if (m.Kind is HeldKind.Pistol or HeldKind.Rifle && _aim > 0f)
        {
            // Sights on the camera's centre line, at the item's eye relief.
            Transform3D sight = HeldItems.Socket(_item!, "sight");
            Basis aimBasis = toCamera;
            Vector3 aimOrigin = new Vector3(0, 0, -m.EyeRelief) - aimBasis * sight.Origin;
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

        // Swing: wind up, strike down and across, recover.
        if (_swing >= 0f)
        {
            float t = _swing;
            float wind = t < 0.3f ? Smooth(t / 0.3f) : t < 0.48f ? 1f - Smooth((t - 0.3f) / 0.18f) * 1.9f : -0.9f + Smooth((t - 0.48f) / 0.52f) * 0.9f;
            Basis arc = Basis.FromEuler(new Vector3(0.95f * wind, -0.25f * wind, -0.35f * wind));
            Vector3 shoulder = new(0.18f, -0.18f, 0.02f);
            place = new Transform3D(arc * place.Basis, shoulder + arc * (place.Origin - shoulder) + new Vector3(0f, 0.04f * Math.Max(0f, wind), 0f));
        }

        // Recoil kick: back and up.
        if (_kick.LengthSquared() > 1e-8f)
        {
            Basis up = Basis.FromEuler(new Vector3(_kick.Y * 0.06f * notAiming + _kick.Y * 0.025f, _kick.X * 0.05f, 0f));
            place = new Transform3D(up * place.Basis, place.Origin + new Vector3(0f, _kick.Y * 0.004f, _kick.Z * 0.012f));
        }

        // Reload: tilt the item and bring it down a little.
        if (_reload >= 0f)
        {
            float r = MathF.Sin(MathF.Min(1f, _reload * 1.15f) * MathF.PI);
            Basis tilt = Basis.FromEuler(new Vector3(0.25f * r, 0.25f * r, 0.55f * r));
            place = new Transform3D(tilt * place.Basis, place.Origin + new Vector3(-0.03f, -0.05f, 0.02f) * r);
        }

        // Equip: rise into view; crouch: a touch lower.
        float equip = 1f - Smooth(_equip);
        place.Origin += new Vector3(0f, -0.28f * equip - 0.012f * _crouch * notAiming, 0.05f * equip);
        place.Basis = Basis.FromEuler(new Vector3(-0.6f * equip, 0f, 0f)) * place.Basis;
        return place;
    }

    private void Pose()
    {
        Transform3D item = ItemPlacement();
        _item!.Transform = item;
        AnimateParts();

        // Hands onto the grips (camera space → skeleton space).
        Transform3D toSkeleton = (_rig.Transform * _skeleton.Transform).AffineInverse();
        Transform3D gripR = item * HeldItems.Socket(_item, "grip_r");
        Transform3D handR = toSkeleton * HandFrame.HandFor(gripR, HandFrame.PowerGrip, _gripR);
        CharacterPoser.ReachArm(_skeleton, _upperR, _lowerR, _handR, handR, 1f, new Vector3(-0.7f, -0.7f, -0.1f));
        HandPoses.Apply(_skeleton, _model!.Kind == HeldKind.Tool ? HandPoses.Grip.Fist : HandPoses.Grip.Pistol, right: true);
        bool twoHands = _model.Kind is HeldKind.Pistol or HeldKind.Rifle && HeldItems.HasSocket(_item, "grip_l");
        if (twoHands)
        {
            Transform3D gripL = item * HeldItems.Socket(_item, "grip_l");
            Transform3D handL = toSkeleton * HandFrame.HandFor(gripL, HandFrame.SupportGrip, _gripL);
            if (_reload >= 0f)
            {
                // To the magazine and back.
                float t = MathF.Sin(MathF.Min(1f, _reload * 1.1f) * MathF.PI);
                Transform3D mag = item * (_magazine != null ? _magazine.Transform : HeldItems.Socket(_item, "grip_l"));
                Vector3 to = toSkeleton * (mag.Origin + new Vector3(0f, -0.12f, 0.02f));
                handL.Origin = handL.Origin.Lerp(to, t);
            }
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, handL, 1f, new Vector3(0.7f, -0.7f, -0.2f));
            HandPoses.Apply(_skeleton, HandPoses.Grip.Support, right: false);
        }
        else
        {
            // The free left hand stays down out of view.
            Transform3D rest = _skeleton.GetBoneGlobalRest(_handL);
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, rest, 1f, new Vector3(0.7f, -0.7f, -0.2f));
        }
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
