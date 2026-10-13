using System;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
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
    private float _lastYaw, _lastPitch;

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
    }

    public override void _ExitTree()
    {
        Client.Local.Gun.ShotFired -= OnShot;
        Client.Local.Gun.ReloadStarted -= OnReload;
    }

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
        _arms.Trigger(CharacterEvent.Swing);
        Sounds.Flat(Sounds.Item(itemKey, "throw"));
    }

    /// <summary>A swing of the item in hand: the arms' motion and its sound.</summary>
    public void Swing(ItemDef item)
    {
        _arms.Trigger(CharacterEvent.Swing);
        Sounds.Flat(Sounds.Item(item.Key, "swing") ?? "melee.swing");
    }

    /// <summary>Counts every change of what is in hand (a held control starts afresh when it changes).</summary>
    public int Equipped { get; private set; }

    /// <summary>Seconds since what is in hand was taken up.</summary>
    public double SinceEquip => _sinceEquip;

    /// <summary>The gun in hand's attachments as its stack has them (sight, muzzle part, light), drawn with the arms.</summary>
    public void DressGun()
    {
        if (HeldModel is { } gun)
        {
            GunEffects.Dress(Client.Content, gun, _arms.Model!.Id, Client.Local.Gun.Mods.Bits, ForArms);
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
        _arms.Trigger(CharacterEvent.Reload);
        string key = reload.Weapon.Key;
        Sounds.Flat(Sounds.Item(key, "magOut"));
        SceneTreeTimer magIn = GetTree().CreateTimer(reload.Seconds * 0.55);
        magIn.Timeout += () => Sounds.Flat(Sounds.Item(key, "magIn"));
        SceneTreeTimer rack = GetTree().CreateTimer(reload.Seconds * 0.85);
        rack.Timeout += () => Sounds.Flat(Sounds.Item(key, "rack"));
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
    public void Refresh(double delta, in InputFrame input, float yaw, float pitch)
    {
        LocalPlayer me = Client.Local;
        // In a ship's seat the hands are on the stick: nothing held is drawn (G6).
        ItemDef? item = me.IsDead || Client.Piloting ? null : me.HeldItem;
        string? key = item?.Key;
        if (key != _heldKey)
        {
            _heldKey = key;
            _sinceEquip = 0;
            Equipped++;
            (HeldKind kind, string? model) = key != null && ItemLooks.Held(key) is { } held ? held : (HeldKind.Unarmed, (string?)null);
            _heldKind = kind;
            _arms.Hold(kind, model);
        }
        _sinceEquip += delta;
        DressArms();

        // Motion of the arms: velocity in their own frame (the aim's), how fast the view turns.
        _body.Basis = ArmsTurn;
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
        UpdateFlame(item);
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
