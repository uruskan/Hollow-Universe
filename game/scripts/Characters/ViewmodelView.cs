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

    /// <summary>Speed parallel to the character's gravity plane, independent of camera pitch.
    /// Synthetic previews may omit it and use camera XZ velocity.</summary>
    public float? GroundSpeed;

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
/// clip into walls. A skeletal action plays the imported arm performance and attaches the item to its hand.
/// Other profiles use item curves and hand IK with sway, walk bob, sight alignment and articulated weapon parts.
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

    // The item's grip (item space: the hand's frame on the axis its fingers close round) and where that axis sits in
    // the right hand (a thicker thing further out from the palm), from the catalogue (HeldItems.Grip).
    private Transform3D _grip = Transform3D.Identity;
    private (Basis FrameToBone, Vector3 Palm) _palmR;

    private HeldItemModel? _model;
    private Node3D? _item;
    private Node3D? _slide, _bolt;
    private HeldReloadParts? _reloadParts;
    private Transform3D _slideRest, _boltRest;
    private ViewmodelFrame _frame;

    // Procedural state.
    private Vector2 _sway, _swayVelocity;
    private float _bobPhase, _bobAmount;
    private float _sprint, _aim, _crouch;
    private float _swing = -1f, _reload = -1f;

    // The bow's draw (0–1), how far it is raised to aim (0–1), and the time since an arrow was loosed; a spear's thrust
    // (0–1); eating, bandaging or injecting (seconds in).
    private float _draw, _releasedDraw, _bowAim, _loose = -1f, _thrust = -1f, _useTime = -1f;
    private CharacterEvent _use;
    private BowRig? _bowRig;

    /// <summary>How far the string comes back in first person (of a full draw): the drawing hand stops in front of the
    /// face instead of behind the camera.</summary>
    private float FirstPersonDraw => _model?.Presentation.BowDrawDistance ?? 0.42f;
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
    public bool HasReloadMagazine => _reloadParts != null;
    public bool CarryingReloadMagazine => _reloadParts?.Carrying == true;
    public Transform3D? PresentedMagazine => _reloadParts?.MagazineGlobal;

    /// <summary>Capture evidence from the animation state, rather than inferring a draw from a screenshot.</summary>
    public string PresentationState => $"item={_model?.Id} equip={_equip:0.000} aim={_aim:0.000} bowAim={_bowAim:0.000} " +
        $"draw={_draw:0.000} loose={_loose:0.000} nock={_bowRig?.Nock(Smooth(_draw) * FirstPersonDraw)} " +
        $"action={ActiveAction} phase={ActivePhase:0.000} artPhase={_presentationSample.At:0.000} " +
        $"magazine={HasReloadMagazine} carryingMagazine={CarryingReloadMagazine}";

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
        _defaultRigTransform = _rig.Transform;
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
            if (_presentationRevision != HeldPresentations.Revision) RefreshPresentation();
            return;
        }
        _item?.QueueFree();
        _item = null;
        _slide = _bolt = null;
        _reloadParts = null;
        _model = model;
        CancelActions();
        ResetSkeletalLocomotion();
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
        _grip = HeldItems.Grip(_item, model);
        (float along, float lift) = HandPoses.Seat(model.GripSeat);
        _palmR = HandFrame.Measure(_skeleton, right: true, along: along, lift: lift);
        _bowRig = model.LeftHanded ? BowRig.Attach(_item) : null;
        _draw = _bowAim = 0f;
        _loose = _thrust = _useTime = -1f;
        RefreshPresentation();
        PrepareItem(_item);
        // The catalogue's guns name their moving parts Slide, Magazine, Bolt (the art package's part_* names too).
        _slide = HeldReloadParts.FindPart(_item, "slide");
        _reloadParts = HeldReloadParts.Attach(_item);
        _bolt = HeldReloadParts.FindPart(_item, "bolt");
        _slideRest = _slide?.Transform ?? Transform3D.Identity;
        _boltRest = _bolt?.Transform ?? Transform3D.Identity;
    }

    public void Apply(in ViewmodelFrame frame) => _frame = frame;

    public void Aim(bool aiming) => _frame.Aiming = aiming;

    public void Trigger(CharacterEvent e)
    {
        if (_model == null) return;
        string? action = e switch
        {
            CharacterEvent.Swing => "swing",
            CharacterEvent.Fire => _model.LeftHanded ? "release" : "fire",
            CharacterEvent.Reload => "reload",
            CharacterEvent.Eat => "eat",
            CharacterEvent.Bandage => "bandage",
            CharacterEvent.Inject => "inject",
            CharacterEvent.Use => _model.Presentation.UseAction,
            _ => null,
        };
        if (action != null) PlayAction(action);
    }

    /// <summary>What first person draws: forearms and hands. Upper arms would only fill the view near the camera.</summary>
    private const BodyRegions Shown = BodyRegions.Forearm | BodyRegions.Hand;
    private bool _sourceArmsVisible;

    private void ApplyRegions()
    {
        // Source clips can bring an elbow into view. Preserve the complete arm so it does not end in an open cut.
        BodyRegions shown = _sourceArmsVisible ? BodyRegions.Arms : Shown;
        BodyRegions hidden = BodyRegions.All & ~shown;
        foreach (Wearable w in _worn)
        {
            hidden |= w.Hides & shown;
        }
        foreach (MeshInstance3D arm in _arms)
        {
            arm.SetInstanceShaderParameter("hidden_regions", (int)hidden);
        }
        foreach ((MeshInstance3D mesh, _) in _sleeves)
        {
            mesh.SetInstanceShaderParameter("hidden_regions", (int)(BodyRegions.All & ~shown));
        }
    }

    /// <summary>Layer 2, no shadows, and the item's own materials drawn with the view model's field of view.</summary>
    public static void PrepareItem(Node node)
    {
        if (node is MeshInstance3D mi)
        {
            mi.Layers = RenderLayer;
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.ExtraCullMargin = 2f;
            if (mi.MaterialOverride is { } material)
            {
                if (!ItemMaterials.TryGetValue(material, out Material? viewMaterial))
                {
                    viewMaterial = CharacterMaterials.ForViewmodel(material);
                    ItemMaterials[material] = viewMaterial;
                }
                mi.MaterialOverride = viewMaterial;
            }
            for (int i = 0; mi.MaterialOverride == null && i < mi.GetSurfaceOverrideMaterialCount(); i++)
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
        float elapsed = (float)Math.Max(0, delta);
        if (_previewAction == null) Animate(dt, elapsed);
        AdvancePresentation(elapsed);
        AdvanceSkeletalLocomotion(elapsed);
        _skeleton.ResetBonePoses();
        Pose();
        _skeleton.Advance(dt);
    }

    private void Animate(float dt, float elapsed)
    {
        ViewmodelFrame f = _frame;
        bool gun = _model!.Kind is HeldKind.Pistol or HeldKind.Rifle;
        float equipSeconds = _model.Presentation.Actions.TryGetValue("equip", out HeldPresentationClip? equip) ? equip.Duration : 0.3f;
        _equip = Math.Min(1f, _equip + elapsed / Math.Max(0.01f, equipSeconds));
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

        _slideKick = Math.Max(0f, _slideKick - dt * 14f);

        if (_model.LeftHanded)
        {
            if (f.Aiming)
            {
                _draw = Math.Min(1f, _draw + elapsed / Math.Max(0.01f, Hollow.Simulation.Content.HeldActionTiming.Quantize(_model.DrawSeconds)));
            }
            else if (!f.Aiming)
            {
                _draw = Math.Max(0f, _draw - dt * 2.5f);
            }
            // The first quarter of a draw raises the bow from its authored low carry. On a continued aim the next
            // draw starts when the server resets its draw clock, including while the release follow-through plays.
            float raised = f.Aiming && _sprint < 0.5f ? Math.Clamp(_draw / 0.25f, 0, 1) : 0;
            if (ActiveAction == "release")
                raised = f.Aiming ? 1f : 1f - Smooth(Math.Clamp((ActivePhase - 0.2f) / 0.8f, 0, 1));
            _bowAim = ActiveAction == "draw" || ActiveAction == "release" ? raised : Approach(_bowAim, raised, dt * 3.2f);
            _bowRig?.Update(Smooth(_draw) * FirstPersonDraw, f.Aiming && _loose < 0f);
        }
        // Action phases are advanced once, by AdvancePresentation, with authoritative durations.
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
        Transform3D grip = _grip;
        // All items use the same authored grip placement, including tools held by a handle.
        Transform3D place = m.Presentation.FirstPerson.Transform * grip.AffineInverse();
        if (m.LeftHanded && _bowAim > 0f)
        {
            // The bow raised to aim: upright (canted a little to the right) in front of the left eye, the arrow on its
            // shelf pointing at the crosshair from just below and right of it.
            Vector3 forward = new Vector3(-0.03f, 0.035f, -1f).Normalized();
            Vector3 up = Vector3.Up.Rotated(forward, -0.2f);
            up = (up - forward * up.Dot(forward)).Normalized();
            var bow = new Basis(forward, up, forward.Cross(up));
            var aimed = new Transform3D(bow, new Vector3(-0.14f, -0.11f, -0.76f) - bow * grip.Origin);
            Transform3D adjustment = m.Presentation.AimOffset.Transform;
            Vector3 aimedGrip = aimed * grip.Origin + adjustment.Origin;
            aimed.Basis = adjustment.Basis * aimed.Basis;
            aimed.Origin = aimedGrip - aimed.Basis * grip.Origin;
            float a = Smooth(_bowAim);
            place = new Transform3D(new Basis(place.Basis.GetRotationQuaternion().Slerp(aimed.Basis.GetRotationQuaternion(), a)),
                place.Origin.Lerp(aimed.Origin, a));
        }
        if (m.Kind is HeldKind.Pistol or HeldKind.Rifle && _aim > 0f)
        {
            Basis aimBasis;
            Vector3 aimOrigin;
            if (GunAttachmentPresentation.TryEye(_item!, m.Id, out Transform3D opticEye))
            {
                // The fitted optic's authored eye replaces the iron-sight artist adjustment.
                aimBasis = toCamera * opticEye.Basis.Inverse();
                aimOrigin = -(aimBasis * opticEye.Origin);
            }
            else
            {
                aimBasis = m.Presentation.AimOffset.Transform.Basis * toCamera;
                aimOrigin = m.EyeRelief <= 0f && HeldItems.HasSocket(_item!, "eye")
                    ? -(aimBasis * HeldItems.Socket(_item!, "eye").Origin)
                    : new Vector3(0, 0, -Math.Max(m.EyeRelief, 0.1f)) - aimBasis * HeldItems.Socket(_item!, "sight").Origin;
                aimOrigin += m.Presentation.AimOffset.Position;
            }
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

        place = ApplyPresentation(place);
        place.Origin += new Vector3(0, -0.012f * _crouch * notAiming, 0);
        return place;
    }

    // Where the left forearm is raised to while bandaging or injecting (camera space): across the bottom of the view.
    private static readonly Vector3 ForearmWrist = new(0.03f, -0.13f, -0.42f);
    private static readonly Vector3 ForearmAxis = new Vector3(-1f, -0.35f, 0.45f).Normalized();

    /// <summary>How far the left forearm is raised into view for a bandage or an injection (0 to 1).</summary>
    private float ForearmRaised
    {
        get
        {
            if (_useTime < 0f || _use is not (CharacterEvent.Bandage or CharacterEvent.Inject))
            {
                return 0f;
            }
            float t = ActivePhase;
            return Smooth(Math.Clamp(t / 0.15f, 0f, 1f)) * (1f - Smooth(Math.Clamp((t - 0.85f) / 0.15f, 0f, 1f)));
        }
    }

    private void Pose()
    {
        if (!PoseSkeletalMotion())
        {
            PoseProcedural();
            // Compose the finished performance, including both arms and held parts. Moving only
            // the item would make the reach solver pull it back or stretch the hands toward it.
            Transform3D composition = ProceduralComposition;
            _rig.Transform = composition * _rig.Transform;
            _item!.Transform = composition * _item.Transform;
        }
        PoseAdmire();
    }

    private void PoseAdmire()
    {
        if (ActiveAction != "admire" || _item == null || _model == null ||
            !_model.Presentation.Actions.TryGetValue("admire", out HeldPresentationClip? clip)) return;
        HeldPresentationKey sample = clip.Sample(ActivePhase);
        if (sample.Position.IsZeroApprox() && sample.Rotation.IsZeroApprox()) return;
        Basis turn = Basis.FromEuler(sample.Rotation * (MathF.PI / 180f));
        Vector3 pivot = _item.Transform * _grip.Origin;
        var offset = new Transform3D(turn, pivot - turn * pivot + sample.Position);
        // Keep shoulders rooted: rotating the whole rig exposes the cut ends of FPS forearms.
        // Transform the existing wrist-to-item relationship, then solve the holding arms only.
        Transform3D toCamera = _rig.Transform * _skeleton.Transform;
        Transform3D toSkeleton = toCamera.AffineInverse();
        bool left = _model.LeftHanded;
        var primary = (Upper: left ? _upperL : _upperR, Lower: left ? _lowerL : _lowerR, Hand: left ? _handL : _handR);
        var secondary = (Upper: left ? _upperR : _upperL, Lower: left ? _lowerR : _lowerL, Hand: left ? _handR : _handL);
        bool both = SecondaryHandShouldAttach;
        Transform3D target = toSkeleton * offset * toCamera * _skeleton.GetBoneGlobalPose(primary.Hand);
        Transform3D other = toSkeleton * offset * toCamera * _skeleton.GetBoneGlobalPose(secondary.Hand);
        Vector3 correction = Vector3.Zero;
        void Limit((int Upper, int Lower, int Hand) arm, Vector3 wrist)
        {
            Vector3 shoulder = _skeleton.GetBoneGlobalPose(arm.Upper).Origin;
            Vector3 elbow = _skeleton.GetBoneGlobalPose(arm.Lower).Origin;
            float reach = shoulder.DistanceTo(elbow) + elbow.DistanceTo(_skeleton.GetBoneGlobalPose(arm.Hand).Origin) - 0.002f;
            Vector3 relative = wrist + correction - shoulder;
            correction += relative.LimitLength(reach) - relative;
        }
        for (int i = 0; i < 24; i++)
        {
            Limit(primary, target.Origin);
            if (both) Limit(secondary, other.Origin);
        }
        offset.Origin += toCamera.Basis * correction;
        target.Origin += correction;
        other.Origin += correction;
        void Reach((int Upper, int Lower, int Hand) arm, Transform3D wrist)
        {
            Vector3 pole = _skeleton.GetBoneGlobalPose(arm.Lower).Origin - _skeleton.GetBoneGlobalPose(arm.Upper).Origin;
            CharacterPoser.ReachArm(_skeleton, arm.Upper, arm.Lower, arm.Hand, wrist, 1, pole, wristTwist: 0);
        }
        Reach(primary, target);
        if (both) Reach(secondary, other);
        _item.Transform = offset * _item.Transform;
    }

    private Transform3D ProceduralComposition =>
        (_model?.Presentation.FirstPersonRig?.Transform ?? Transform3D.Identity)
            .InterpolateWith(Transform3D.Identity, Smooth(Math.Max(_aim, _bowAim)));

    private void PoseProcedural()
    {
        // Hands onto the grips (camera space → skeleton space), from shoulders set lower and further forward than the
        // body's, so the forearms rise into view from the lower corners as in Rust (only forearms and hands are drawn;
        // nobody sees the shoulders move).
        Transform3D toSkeleton = (_rig.Transform * _skeleton.Transform).AffineInverse();
        HeldPresentationProfile profile = _model!.Presentation;
        Shoulder(_upperR, profile.RightHand.Shoulder + profile.RightHand.FirstPersonShoulderOffset, toSkeleton);
        Shoulder(_upperL, profile.LeftHand.Shoulder + profile.LeftHand.FirstPersonShoulderOffset, toSkeleton);
        Transform3D item = ConstrainReach(ItemPlacement(), toSkeleton);
        item = AlignBowAim(item, toSkeleton);
        _item!.Transform = item;
        _item.Visible = PrimaryHandShouldAttach;
        PrimaryHandAttachmentError = SecondaryHandAttachmentError = 0;
        AnimateParts();
        if (_model!.LeftHanded)
        {
            // The bow: the left hand holds its grip; the right hand draws the string from the nock (an arrow on it) while
            // aiming, and is down out of view otherwise.
            Transform3D gripL1 = item * _grip;
            Transform3D leftTarget = toSkeleton * ArtHand(HandFrame.HandFor(gripL1, HandFrame.PowerGrip, _gripL), false);
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, leftTarget, 1f, toSkeleton.Basis * profile.LeftHand.Pole);
            PrimaryHandAttachmentError = _skeleton.GetBoneGlobalPose(_handL).Origin.DistanceTo(leftTarget.Origin);
            ArtFingers(false, HandPoses.Grip.Fist);
            Transform3D rest = _skeleton.GetBoneGlobalRest(_handR);
            float a = Smooth(_bowAim);
            if (a > 0.001f && _bowRig != null)
            {
                float returnToString = _loose >= 0 ? Smooth(Math.Clamp((ActivePhase - 0.5f) / 0.5f, 0, 1)) : 1;
                float handDraw = _loose >= 0f ? Mathf.Lerp(_releasedDraw, _draw, returnToString) : _draw;
                Vector3 nock = item * _bowRig.Nock(Smooth(handDraw) * FirstPersonDraw);
                if (_loose >= 0f)
                {
                    // Loosed: the hand flies back past the cheek and opens, then drops away.
                    float k = MathF.Sin(MathF.Min(1f, _loose / 0.15f) * MathF.PI * 0.5f);
                    nock += (new Vector3(0.05f, -0.02f, 0.06f) * k + new Vector3(0.04f, -0.12f, 0.05f)
                        * Smooth(Math.Clamp((_loose - 0.25f) / 0.3f, 0f, 1f))) * (1f - returnToString);
                }
                Vector3 fingers = item.Basis.X.Normalized();
                Vector3 thumb = item.Basis.Y.Normalized();
                var hook = new Transform3D(new Basis(fingers, thumb, fingers.Cross(thumb)), nock);
                Transform3D drawing = toSkeleton * ArtHand(HandFrame.HandFor(hook, HandFrame.PowerGrip, _gripR), true);
                CharacterPoser.ReachArm(_skeleton, _upperR, _lowerR, _handR, rest.InterpolateWith(drawing, a), 1f,
                    toSkeleton.Basis * profile.RightHand.Pole);
                SecondaryHandAttachmentError = _skeleton.GetBoneGlobalPose(_handR).Origin.DistanceTo(drawing.Origin);
                ArtFingers(true, _loose >= 0f ? HandPoses.Grip.Relaxed : HandPoses.Grip.Support);
                return;
            }
            CharacterPoser.ReachArm(_skeleton, _upperR, _lowerR, _handR, rest, 1f, new Vector3(-0.7f, -0.7f, -0.2f));
            return;
        }
        // The right hand closes on the item's grip: round a handle, or round a rock's girth (the catalogue's grip).
        Transform3D gripR = item * _grip;
        Transform3D handR = toSkeleton * ArtHand(HandFrame.HandFor(gripR, HandFrame.PowerGrip, _palmR), true);
        CharacterPoser.ReachArm(_skeleton, _upperR, _lowerR, _handR, handR, 1f, toSkeleton.Basis * profile.RightHand.Pole);
        PrimaryHandAttachmentError = _skeleton.GetBoneGlobalPose(_handR).Origin.DistanceTo(handR.Origin);
        ArtFingers(true, _model.Fingers);
        bool twoHands = _model.Kind is HeldKind.Pistol or HeldKind.Rifle && HeldItems.HasSocket(_item, "grip_l");
        if (twoHands)
        {
            Transform3D gripL = item * (HeldItems.Socket(_item, "grip_l") * profile.LeftHand.GripOffset.Transform);
            SupportHandInfo? supportPose = _model.Entry.Held?.SupportHand;
            gripL.Origin += item.Basis * (supportPose?.Offset ?? Vector3.Zero);
            Basis support = _model.SupportFrame;
            Transform3D handL = HandFrame.HandFor(gripL, support, _gripL);
            Transform3D? seatedMagazineHand = null;
            if (_reload >= 0f)
            {
                // The support hand goes to the magazine well, away below the gun for a new magazine, back up to seat
                // it, then to the slide (pistol) or the charging handle (rifle) and pulls it, then back to its grip.
                Vector4 w = ViewmodelMotion.ReloadHand(_reload);
                bool pistol = _model.Kind == HeldKind.Pistol;
                Vector3 right = HeldItems.Socket(_item, "grip_r").Origin;
                Vector3 left = item.AffineInverse() * gripL.Origin;
                Vector3 well = pistol ? right + new Vector3(-0.01f, -0.1f, 0f) : (right + left) * 0.5f + new Vector3(0f, -0.1f, 0f);
                Transform3D wellGrip = HeldItems.Socket(_item, "grip_l") * profile.LeftHand.GripOffset.Transform;
                wellGrip.Origin = well;
                seatedMagazineHand = HandFrame.HandFor(wellGrip, HandFrame.PowerGrip, _gripL) * profile.LeftHand.WristOffset.Transform;
                Vector3 away = well + new Vector3(-0.06f, -0.22f, -0.08f);
                Vector3 work = pistol
                    ? HeldItems.Socket(_item, "sight").Origin + new Vector3(0.02f, -0.012f, 0f)
                    : right + new Vector3(0.12f, 0.07f, 0.045f);
                Vector3 pull = new Vector3(-0.2f, 0f, 0f) * Math.Max(0f, w.W - 1f);
                Vector3 at = left * w.X + well * w.Y + away * w.Z + (work + pull) * Math.Min(1f, w.W);
                handL = HandFrame.HandFor(new Transform3D(gripL.Basis, item * at), ViewmodelMotion.ReloadGrip(support, _reload), _gripL);
            }
            handL = toSkeleton * ArtHand(handL, false);
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, handL, 1f, toSkeleton.Basis * profile.LeftHand.Pole);
            SecondaryHandAttachmentError = _skeleton.GetBoneGlobalPose(_handL).Origin.DistanceTo(handL.Origin);
            ArtFingers(false, HandPoses.Grip.Support);
            if (seatedMagazineHand is Transform3D seated)
                _reloadParts?.Update(_reload, _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_handL), seated);
        }
        else if (ForearmRaised > 0f)
        {
            // Bandaging or injecting: the left forearm raised across the bottom of the view, palm down.
            Vector3 fingers = -ForearmAxis;
            Vector3 side = fingers.Cross(Vector3.Up).Normalized();
            var palmDown = new Basis(fingers, side.Cross(fingers).Normalized(), side);
            Transform3D raised = toSkeleton * ArtHand(HandFrame.HandFor(new Transform3D(palmDown, ForearmWrist), HandFrame.PowerGrip, _gripL), false);
            Transform3D rest = _skeleton.GetBoneGlobalRest(_handL);
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, rest.InterpolateWith(raised, ForearmRaised), 1f,
                toSkeleton.Basis * profile.LeftHand.Pole);
            ArtFingers(false, HandPoses.Grip.Relaxed);
        }
        else
        {
            // The free left hand stays down out of view.
            Transform3D rest = _skeleton.GetBoneGlobalRest(_handL);
            CharacterPoser.ReachArm(_skeleton, _upperL, _lowerL, _handL, rest, 1f, new Vector3(0.7f, -0.7f, -0.2f));
        }
    }

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
        _reloadParts?.Restore();
    }
}
