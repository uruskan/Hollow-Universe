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
/// active belt item's model (the arms in the worn sleeves), swinging with the server's rhythm while the attack is
/// held; a plain placeholder for items without a model yet; a shot's muzzle effect and a lit torch's flame (the effects
/// library, drawn with the arms); blood dripping while bleeding; and a ghost where a held deployable would go.
/// </summary>
public partial class FirstPersonView : Node3D
{
    private ViewmodelView _arms = null!;
    private Node3D _placeholder = null!;
    private MeshInstance3D _stick = null!;
    private FxHandle _flame;
    private Node3D? _flameSocket;
    private double _drip;
    private MeshInstance3D _ghost = null!;
    private Camera3D _camera = null!;
    private string? _heldKey;
    private int _wearRevision = -1;
    private double _swing = -1;
    private double _sinceEquip;
    private float _draw;
    private bool _drawing;
    private float _lastYaw, _lastPitch;

    public GameClient Client { get; set; } = null!;

    /// <summary>Where a held deployable would go (the view sets it; the server decides).</summary>
    public Num.Vector3? PlacementPoint { get; set; }

    public float PlacementYaw { get; set; }

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
            Vector3 forward = -_camera.GlobalBasis.Z;
            FxDirector.Play(key, Muzzle(), forward, new FxParams { Direction = forward, FirstPerson = true });
        }
    }

    /// <summary>An explosive thrown: the arm's overhand motion and the throw's sound.</summary>
    public void Throw(string itemKey)
    {
        _arms.Trigger(CharacterEvent.Swing);
        Sounds.Flat(Sounds.Item(itemKey, "throw"));
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
        bool rifle = Client.Local.HeldItem?.Key == "rifle";
        float aim = _arms.AimAmount;
        Vector3 local = new Vector3(0.14f, -0.12f, -(rifle ? 1.0f : 0.6f)).Lerp(new Vector3(0, -0.05f, -(rifle ? 1.05f : 0.6f)), aim);
        return _camera.GlobalTransform * local;
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

    /// <summary>Puts the arms on the player's camera.</summary>
    public void Attach(Camera3D camera)
    {
        _camera = camera;
        _arms = new ViewmodelView { Name = "Viewmodel", Body = (BodyType)Client.Body };
        camera.AddChild(_arms);
        _placeholder = new Node3D { Name = "HeldPlaceholder", Visible = false };
        _stick = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.5f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.33f, 0.21f), Roughness = 0.8f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = ViewmodelView.RenderLayer,
        };
        _placeholder.AddChild(_stick);
        camera.AddChild(_placeholder);
        _ghost = new MeshInstance3D
        {
            Name = "PlacementGhost",
            Mesh = new BoxMesh(),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.4f, 0.85f, 1f, 0.3f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Visible = false,
        };
        AddChild(_ghost);
    }

    /// <summary>Every frame, after the camera is placed: what is held, how it moves, swings, and the placement ghost.</summary>
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
            _swing = -1;
            (HeldKind kind, string? model) = key != null && ItemLooks.Held(key) is { } held ? held : (HeldKind.Unarmed, (string?)null);
            _arms.Hold(kind, model);
        }
        _sinceEquip += delta;
        DressArms();
        // The gun's attachments as its stack has them (sight, muzzle part, light), drawn with the arms.
        if (item?.Firearm != null && HeldModel is { } gun)
        {
            GunEffects.Dress(Client.Content, gun, _arms.Model!.Id, Client.Local.Gun.Mods.Bits, ForArms);
        }

        // Motion of the arms: velocity in the camera's frame, how fast the view turns.
        CharacterState state = Client.Character;
        Basis view = _camera.GlobalBasis;
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

        // Swings follow the server's rhythm: one every swing time while the attack is held, once the item is ready.
        bool swingNow = false;
        if (item?.Melee is { } melee && _sinceEquip > 0.3)
        {
            if (_swing < 0 && input.Primary)
            {
                _swing = 0;
                swingNow = true;
            }
            else if (_swing >= 0)
            {
                _swing += delta;
                if (_swing >= melee.SwingSeconds)
                {
                    _swing = input.Primary ? 0 : -1;
                    swingNow = input.Primary;
                }
            }
        }
        if (swingNow)
        {
            _arms.Trigger(CharacterEvent.Swing);
            Sounds.Flat(Sounds.Item(item!.Key, "swing") ?? "melee.swing");
        }
        // The bow's string drawn.
        bool drawing = item?.Bow != null && input.Secondary && !me.IsDead;
        if (drawing && !_drawing)
        {
            Sounds.Flat(Sounds.Item(item!.Key, "draw") ?? "bow.draw");
        }
        _drawing = drawing;
        UpdatePlaceholder(item, delta, input);
        UpdateTorch(item);
        Bleed(delta);
        UpdateGhost(item);
    }

    // A torch in hand burns: the effects library's flame on its flame socket, drawn with the arms; its light is the
    // flame's.
    private void UpdateTorch(ItemDef? item)
    {
        Node3D? socket = item?.Key == "torch" ? GunEffects.Socket(HeldModel, "flame") : null;
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
            _flame = FxDirector.Start("fire.torch", socket.GlobalPosition, Vector3.Up, new FxParams { Follow = socket, FirstPerson = true });
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

    // Items with no model of their own yet: a plain stick (a torch with its flame, a spear, a bow) that swings.
    private void UpdatePlaceholder(ItemDef? item, double delta, in InputFrame input)
    {
        bool show = item != null && ItemLooks.NeedsPlaceholder(item);
        _placeholder.Visible = show;
        if (!show)
        {
            return;
        }
        float length = item!.Melee?.Reach > 2 ? 1.1f : item.Bow != null ? 0.9f : 0.45f;
        ((BoxMesh)_stick.Mesh).Size = new Vector3(0.045f, 0.045f, length);
        _draw = Mathf.MoveToward(_draw, item.Bow != null && input.Secondary ? 1 : 0, (float)delta * 2.5f);
        float swing = 0;
        if (_swing >= 0 && item.Melee is { } melee)
        {
            float t = (float)(_swing / melee.SwingSeconds);
            swing = t < 0.35f ? t / 0.35f : Math.Max(0, 1 - (t - 0.35f) / 0.65f);
        }
        Vector3 rest = new(0.3f - 0.2f * _draw, -0.3f + 0.1f * _draw, -0.5f - 0.1f * _draw);
        _placeholder.Transform = new Transform3D(Basis.FromEuler(new Vector3(-0.35f - 1.2f * swing, 0.15f, 0)), rest + new Vector3(-0.05f * swing, 0.05f * swing, -0.15f * swing));
    }

    private Node3D? _ghostModel;
    private string? _ghostKey;

    // Where a held deployable would go: its own model in the ghost's colour (a box if it has none).
    private void UpdateGhost(ItemDef? item)
    {
        if (item?.Deploys is not { } def || PlacementPoint is not { } at)
        {
            _ghost.Visible = false;
            if (_ghostModel != null)
            {
                _ghostModel.Visible = false;
            }
            return;
        }
        Num.Vector3 up = Client.Frame.UpAt(at);
        Basis basis = NatureView.Upright(up, PlacementYaw);
        if (_ghostKey != def.Key)
        {
            _ghostKey = def.Key;
            _ghostModel?.QueueFree();
            _ghostModel = GroundModels.Provider.Deployable(def.Key, new Vector3(def.HalfExtents.X, def.HalfExtents.Y, def.HalfExtents.Z));
            if (_ghostModel != null)
            {
                foreach (MeshInstance3D mesh in HollowUniverse.Items.ModelCatalog.Meshes(_ghostModel))
                {
                    mesh.MaterialOverride = _ghost.MaterialOverride;
                    mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
                AddChild(_ghostModel);
            }
        }
        if (_ghostModel != null)
        {
            _ghostModel.GlobalTransform = new Transform3D(basis, GroundView.ToGodot(at));
            _ghostModel.Visible = true;
            _ghost.Visible = false;
            return;
        }
        ((BoxMesh)_ghost.Mesh).Size = new Vector3(def.HalfExtents.X * 2, def.HalfExtents.Y * 2, def.HalfExtents.Z * 2);
        _ghost.GlobalTransform = new Transform3D(basis, GroundView.ToGodot(at + up * def.HalfExtents.Y));
        _ghost.Visible = true;
    }
}
