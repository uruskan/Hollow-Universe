using System;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Targets;
using HollowUniverse.Characters;
using HollowUniverse.Fx;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The local player in first person (GROUND.md §4.8): the character package's <see cref="ViewmodelView"/> holding the
/// active belt item's model (the arms in the worn sleeves) in the pose its catalogue entry gives; a shot's muzzle effect
/// and the flame the catalogue gives a burning item (the effects library, drawn with the arms); blood dripping while
/// bleeding. What the arms do with the item (a swing in the server's rhythm, a bow drawn, a gun dressed, a throw) is its
/// held control's (GROUND_SYSTEMS.md §7), through <see cref="Swing"/>, <see cref="Throw"/> and <see cref="DressGun"/>.
/// Where a held thing would go is the placement ghost's (<see cref="PlacementView"/>, GROUND_SYSTEMS.md §6).
/// </summary>
public partial class FirstPersonView : Node3D
{
    private Node3D _body = null!;
    private ViewmodelView _arms = null!;
    private HeldKind _heldKind;
    private FxHandle _flame;
    private Node3D? _flameSocket;
    private double _drip;
    private Camera3D _camera = null!;
    private string? _heldKey;
    private int _wearRevision = -1;
    private double _sinceEquip;
    private int _equippedSlot = -2;
    private ushort _equippedItem;
    private uint _equipReadyTick;
    private float _lastYaw, _lastPitch;
    private (ItemDef Item, string Action, float Seconds, uint Intent, uint StartedTick)? _pendingAction;
    private ItemDef? _actionItem;
    private double _actionUntil;
    private int _actionSlot = -1;
    private uint _actionIntent, _confirmedSwing;
    private bool _wasReloading;
    private readonly HeldActionQueue _queued = new();

    public GameClient Client { get; set; } = null!;

    /// <summary>
    /// The aim seen from the camera (the camera's basis inverted, times the aim's): identity, unless free look (Alt) has
    /// turned the camera away. The arms belong to the body, so they stay on the aim and leave the view as the head turns.
    /// </summary>
    public Basis ArmsTurn { get; set; } = Basis.Identity;

    public override void _Ready()
    {
        Client.Local.Gun.ShotFired += OnShot;
        Client.Local.Gun.ReloadStarted += OnReload;
        Client.Survival().ProjectileLaunched += OnProjectile;
        Client.Melee.Started += Swing;
        Client.Melee.Cancelled += CancelAction;
        Client.Local.HitMarker += OnHit;
        Client.Local.IntentRejected += OnRejected;
    }

    public override void _ExitTree()
    {
        Client.Local.Gun.ShotFired -= OnShot;
        Client.Local.Gun.ReloadStarted -= OnReload;
        Client.Survival().ProjectileLaunched -= OnProjectile;
        Client.Melee.Started -= Swing;
        Client.Melee.Cancelled -= CancelAction;
        Client.Local.HitMarker -= OnHit;
        Client.Local.IntentRejected -= OnRejected;
    }

    private void OnProjectile(ClientProjectile projectile)
    {
        if (projectile.Shooter == Client.NetId && Client.Content.Item(projectile.Ammo)?.Ammo != null && _arms?.Model?.LeftHanded == true)
        {
            _arms.Trigger(CharacterEvent.Fire);
        }
    }

    /// <summary>The held control supplies aim state for non-firearms, using the same frame as its server input.</summary>
    public void Aim(bool aiming) => _arms.Aim(aiming);

    // A shot of our own, at once (predicted): the arms kick, the gun sounds, its muzzle effect plays on the gun in hand
    // (by its attachments: silenced, braked or plain; a rocket's), drawn with the arms (G4, GROUND.md §6.4, §9.3).
    private void OnShot(ShotEvent shot)
    {
        if (_camera == null)
        {
            return;
        }
        _arms.Trigger(CharacterEvent.Fire);
        Sounds.At(Sounds.Item(shot.Weapon.Key, shot.Silenced ? "shotSilenced" : "shot"), Muzzle());
        string key = GunEffects.MuzzleKey(Client.Content, shot.Weapon.Key, Client.Local.Gun.Mods.Bits);
        if (GunEffects.MuzzleEnd(HeldModel) is { } end)
        {
            FxDirector.Muzzle(key, end, firstPerson: true);
        }
        else
        {
            Vector3 forward = -_body.GlobalBasis.Z;
            FxDirector.Play(key, Muzzle(), forward, new FxParams { Direction = forward, FirstPerson = true });
        }
    }

    /// <summary>An explosive thrown: the arm's overhand motion and the throw's sound.</summary>
    public void Throw(string itemKey)
    {
        if (Client.Content.ItemByKey(itemKey) is { } item) Play(item, "throw");
        Sounds.Flat(Sounds.Item(itemKey, "throw"));
    }

    /// <summary>A swing of the item in hand: the arms' motion and its sound.</summary>
    public void Swing(ItemDef item)
    {
        _confirmedSwing = 0;
        Play(item, "swing");
        Sounds.Flat(Sounds.Item(item.Key, "swing") ?? "melee.swing");
    }

    /// <summary>Presentation of an attempted held action. The server's refusal cancels it; it never grants a gameplay result.</summary>
    public void Play(ItemDef item, string action, uint intent = 0)
    {
        if (HeldItems.Find(item.Key) == null || !HeldPresentations.All.TryGetValue(item.Key, out HeldPresentationProfile? profile)) return;
        if (action == "use") action = profile.UseAction;
        if (!profile.Actions.TryGetValue(action, out HeldPresentationClip? clip)) return;
        float seconds = HeldActionTiming.Duration(item, action, clip.Duration);
        _pendingAction = (item, action, seconds, intent, Client.PredictedTick);
        _actionSlot = Client.Local.HeldSlot;
    }

    public bool BeginAction(ItemDef item, string action, Func<uint> dispatch)
    {
        // Ghost-only placement has no held model to animate and keeps its ordinary validated intent path.
        if (HeldItems.Find(item.Key) == null || !HeldPresentations.All.TryGetValue(item.Key, out HeldPresentationProfile? profile)) return dispatch() != 0;
        if (item.Id != _equippedItem || Client.Local.HeldSlot != _equippedSlot || Client.PredictedTick < _equipReadyTick) return false;
        if (action == "use") action = profile.UseAction;
        if (!profile.Actions.TryGetValue(action, out HeldPresentationClip? clip)) return false;
        float duration = HeldActionTiming.Duration(item, action, clip.Duration);
        if (!_queued.Schedule(Client.PredictedTick, item.Id, Client.Local.HeldSlot, action, duration, dispatch)) return false;
        Play(item, action);
        return true;
    }

    private void OnRejected(IntentAnswer answer)
    {
        if (answer.Seq != 0 && (answer.Seq == _actionIntent || answer.Seq == _pendingAction?.Intent)) CancelAction();
    }

    private void CancelAction()
    {
        _queued.Cancel();
        _pendingAction = null;
        _actionItem = null;
        _actionUntil = 0;
        _actionIntent = 0;
        _arms?.CancelActions();
    }

    private void OnHit(HitMarkerEvent hit)
    {
        if (_camera == null || Client.Local.IsDead || Client.Local.IsWounded || Client.Ships().Seated ||
            Client.Local.HeldItem?.Id != hit.Weapon || Client.Local.HeldItem.Melee == null ||
            hit.ActionTick != Client.Melee.ActionTick || _confirmedSwing == hit.ActionTick) return;
        _confirmedSwing = hit.ActionTick;
        _arms.SetContactTarget(_body.GlobalTransform.AffineInverse() * GroundView.ToGodot(hit.Point), confirmed: true);
    }

    /// <summary>Counts every change of what is in hand (a held control starts afresh when it changes).</summary>
    public int Equipped { get; private set; }

    /// <summary>Hide camera-mounted arms and their flame while the admin inspects the full character.</summary>
    public bool ShowBody { get; set; } = true;

    public bool BodyVisible => _body?.Visible == true;

    /// <summary>Seconds since what is in hand was taken up.</summary>
    public double SinceEquip => _sinceEquip;

    /// <summary>The gun in hand's attachments as its stack has them (sight, muzzle part, light), drawn with the arms.</summary>
    public void DressGun()
    {
        if (HeldModel is { } gun)
        {
            GunEffects.Dress(Client.Content, gun, _arms.Model!.Id, Client.Local.Gun.Mods.Bits, ForArms, firstPerson: true);
        }
    }

    /// <summary>The model in hand as the arms draw it (the catalogue's, named by its model), if any.</summary>
    public Node3D? HeldModel => _arms?.Model is { } model ? _arms.GetNodeOrNull<Node3D>(model.Id) : null;

    // Where the muzzle is seen, for the start of a tracer: the gun's muzzle as the arms draw it (with their own field of
    // view) put where it shows in the world's view; without a model, a point past the gun's end, lower right at the hip,
    // under the sight when aimed.
    public Vector3 Muzzle()
    {
        if (GunEffects.MuzzleEnd(HeldModel) is { } end)
        {
            return FxDirector.ViewmodelToWorld(end.GlobalPosition);
        }
        // A rifle's (its catalogue pose) is further out than a pistol's.
        bool rifle = _heldKind == HeldKind.Rifle;
        float aim = _arms.AimAmount;
        Vector3 local = new Vector3(0.14f, -0.12f, -(rifle ? 1.0f : 0.6f)).Lerp(new Vector3(0, -0.05f, -(rifle ? 1.05f : 0.6f)), aim);
        return _body.GlobalTransform * local;
    }

    // A reload: the arms play it, and its parts sound as they come (magazine out, in, the slide or bolt).
    private void OnReload(ReloadEvent reload)
    {
        Play(reload.Weapon, "reload");
        _wasReloading = true;
        string key = reload.Weapon.Key;
        float seconds = HeldActionTiming.Duration(reload.Weapon, "reload", reload.Seconds);
        int equipped = Equipped;
        Sounds.Flat(Sounds.Item(key, "magOut"));
        SceneTreeTimer magIn = GetTree().CreateTimer(seconds * 0.55);
        magIn.Timeout += () => { if (Equipped == equipped && Client.Local.Gun.Reloading) Sounds.Flat(Sounds.Item(key, "magIn")); };
        SceneTreeTimer rack = GetTree().CreateTimer(seconds * 0.85);
        rack.Timeout += () => { if (Equipped == equipped && Client.Local.Gun.Reloading) Sounds.Flat(Sounds.Item(key, "rack")); };
    }

    /// <summary>Puts the arms on the player's camera (on the aim, <see cref="ArmsTurn"/>).</summary>
    public void Attach(Camera3D camera)
    {
        _camera = camera;
        _body = new Node3D { Name = "Arms" };
        camera.AddChild(_body);
        _arms = new ViewmodelView { Name = "Viewmodel", Body = (BodyType)Client.Body };
        _body.AddChild(_arms);
    }

    /// <summary>Every frame, after the camera is placed: what is held, and how the arms move.</summary>
    public void Refresh(double delta, in InputFrame input, float yaw, float pitch, bool handsAvailable = true)
    {
        LocalPlayer me = Client.Local;
        bool canAct = !me.IsDead && !me.IsWounded && !Client.Ships().Seated;
        bool equipChanged = _equippedSlot != me.HeldSlot || _equippedItem != (me.HeldItem?.Id ?? 0);
        if (equipChanged)
        {
            _equippedSlot = me.HeldSlot;
            _equippedItem = me.HeldItem?.Id ?? 0;
            _equipReadyTick = Client.PredictedTick + (uint)Math.Max(0, CharacterTicks.From(Client.Content.Survival.EquipSeconds));
            _sinceEquip = 0;
            Equipped++;
        }
        if (!canAct || _actionSlot != me.HeldSlot && _actionItem != null) CancelAction();
        if (_wasReloading && !me.Gun.Reloading)
        {
            if (_pendingAction?.Action == "reload") _pendingAction = null;
            _arms.CancelActions();
        }
        _wasReloading = me.Gun.Reloading;
        // In a ship's seat the hands are on the stick: nothing held is drawn (G6).
        ItemDef? item = canAct ? me.HeldItem : null;
        if (canAct && item == null && _actionSlot == me.HeldSlot)
        {
            item = _pendingAction?.Item ?? (Client.ClockSeconds < _actionUntil ? _actionItem : null);
        }
        string? key = item?.Key;
        if (key != _heldKey)
        {
            _heldKey = key;
            _sinceEquip = 0;
            if (!equipChanged) Equipped++;
            (HeldKind kind, string? model) = key != null && ItemLooks.Held(key) is { } held ? held : (HeldKind.Unarmed, (string?)null);
            _heldKind = kind;
            _arms.Hold(kind, model);
        }
        if (canAct && _pendingAction is { } pending && item?.Key == pending.Item.Key)
        {
            _pendingAction = null;
            _actionItem = pending.Item;
            float elapsed = Math.Max(0, (long)Client.PredictedTick - pending.StartedTick) / (float)CharacterTicks.Rate;
            _actionUntil = Client.ClockSeconds + Math.Max(0f, pending.Seconds - elapsed);
            _actionIntent = pending.Intent;
            _arms.PlayAction(pending.Action, pending.Seconds, Math.Clamp(elapsed / pending.Seconds, 0f, 1f));
        }
        // Activate first: a frame hitch may cross the commit tick before the first drawn pose. The returned intent
        // must survive activation so a refusal can cancel it, and its pose starts at the elapsed input-tick phase.
        if (_queued.Pending)
        {
            uint sent = _queued.Step(Client.PredictedTick, me.HeldItem?.Id ?? 0, me.HeldSlot, canAct && handsAvailable);
            if (sent != 0) _actionIntent = sent;
            else if (!_queued.Pending) CancelAction();
        }
        _sinceEquip += delta;
        DressArms();
        // Seated, the first-person arms hide: the hands are on the controls, drawn for others (IRON_WAKE.md §9.6).
        _body.Visible = canAct && ShowBody;

        // Motion of the arms: velocity in their own frame (the aim's), how fast the view turns.
        _body.Basis = ArmsTurn;
        if (item?.Melee is { } melee && _confirmedSwing != Client.Melee.ActionTick && _arms.ActiveAction == "swing")
        {
            Num.Vector3 eye = Client.RenderEye;
            Vector3 aim = -_body.GlobalBasis.Z;
            Num.Vector3 forward = new(aim.X, aim.Y, aim.Z);
            AimHit contact = Client.Targets().PredictMelee(eye, forward, melee.Reach);
            _arms.SetContactTarget(contact.Hit && contact.Target.Family != TargetFamily.Ground
                ? _body.GlobalTransform.AffineInverse() * GroundView.ToGodot(contact.Point) : null);
        }
        CharacterState state = Client.Character;
        Basis view = _body.GlobalBasis;
        Vector3 v = GroundView.ToGodot(state.Velocity);
        float dt = (float)Math.Max(delta, 1e-4);
        var rate = new Vector2(Mathf.AngleDifference(_lastYaw, yaw) / dt, (pitch - _lastPitch) / dt);
        _lastYaw = yaw;
        _lastPitch = pitch;
        bool grounded = (state.Flags & MoveFlags.OnGround) != 0;
        _arms.Apply(new ViewmodelFrame
        {
            LocalVelocity = view.Inverse() * v,
            LookRate = rate,
            Grounded = grounded,
            Sprinting = (state.Flags & MoveFlags.Sprinting) != 0,
            Crouching = state.Stance == Stance.Crouching,
            Aiming = Client.Local.Gun.Aimed,
        });
        UpdateFlame(ShowBody ? item : null);
        Bleed(delta);
    }

    // An item whose catalogue entry gives it a flame (models.json held.flame: the torch) burns in hand: that effect on its
    // flame socket, drawn with the arms; its light is the flame's.
    private void UpdateFlame(ItemDef? item)
    {
        string? effect = item != null ? HollowUniverse.Items.ModelCatalog.Find(item.Key)?.Held?.Flame : null;
        Node3D? socket = effect != null ? GunEffects.Socket(HeldModel, "flame") : null;
        if (socket == _flameSocket)
        {
            return;
        }
        if (_flame.IsValid)
        {
            FxDirector.Stop(_flame, immediate: true);
            _flame = default;
        }
        _flameSocket = socket;
        if (socket != null)
        {
            _flame = FxDirector.Start(effect!, socket.GlobalPosition, Vector3.Up, new FxParams { Follow = socket, FirstPerson = true });
        }
    }

    // While bleeding, a drop falls under the player every second and a half (blood.drip).
    private void Bleed(double delta)
    {
        _drip -= delta;
        if (_drip > 0 || !Client.Local.Vitals.IsBleeding || Client.Local.IsDead)
        {
            return;
        }
        _drip = 1.5;
        Num.Vector3 feet = Client.RenderPosition;
        FxDirector.Play("blood.drip", GroundView.ToGodot(feet), GroundView.ToGodot(Client.Frame.UpAt(feet)));
    }

    private static readonly System.Collections.Generic.Dictionary<Material, Material> ArmsMaterials = new();

    // An attachment readied as the arms ready their item: their render layer, no shadow, the arms' field of view in
    // its materials, only its most detailed mesh.
    private static void ForArms(Node3D part)
    {
        HollowUniverse.Items.ModelCatalog.ForceLod(part, 0);
        foreach (MeshInstance3D mesh in HollowUniverse.Items.ModelCatalog.Meshes(part))
        {
            mesh.Layers = ViewmodelView.RenderLayer;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mesh.ExtraCullMargin = 2f;
            for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is { } m)
                {
                    if (!ArmsMaterials.TryGetValue(m, out Material? arms))
                    {
                        arms = CharacterMaterials.ForViewmodel(m);
                        ArmsMaterials[m] = arms;
                    }
                    mesh.SetSurfaceOverrideMaterial(i, arms);
                }
            }
        }
    }

    private void DressArms()
    {
        int revision = (int)Client.Local.Inventory.Wear.Revision;
        if (revision == _wearRevision)
        {
            return;
        }
        _wearRevision = revision;
        _arms.Wear(ItemLooks.Outfit(Client.Content, Client.Local.Inventory.Wear.Slots.ToArray().Select(s => s.Item)));
    }
}
