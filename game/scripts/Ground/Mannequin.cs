using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Movement;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Another player, as a placeholder until the character work (GROUND.md §1): a capsule of the character's real size
/// (shorter when crouching, lying down asleep), in a colour of its own, with a visor showing where it looks, and a
/// stick in its hand for what it holds that jerks with each swing or shot.
/// </summary>
public partial class Mannequin : Node3D
{
    private Node3D _figure = null!;
    private MeshInstance3D _body = null!;
    private MeshInstance3D _visor = null!;
    private MeshInstance3D _held = null!;
    private CapsuleMesh _capsule = null!;
    private Stance _stance = Stance.Standing;
    private byte _actions;
    private double _swing = -1;

    public string PlayerName { get; set; } = "";

    /// <summary>Where the name label goes (above the head), in Godot coordinates.</summary>
    public Vector3 LabelPoint { get; private set; }

    public override void _Ready()
    {
        // A steady colour per name: a hue from its hash, kept soft.
        float hue = (Hash64.Of(PlayerName.ToUpperInvariant()) % 1000) / 1000f;
        var paint = new StandardMaterial3D { AlbedoColor = Color.FromHsv(hue, 0.45f, 0.80f), Roughness = 0.7f };
        _figure = new Node3D { Name = "Figure" };
        AddChild(_figure);
        _capsule = new CapsuleMesh { Radius = 0.32f, Height = 1.76f, RadialSegments = 24, Rings = 8 };
        _body = new MeshInstance3D { Mesh = _capsule, MaterialOverride = paint, Position = new Vector3(0, 0.88f, 0) };
        _figure.AddChild(_body);
        _visor = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.09f, 0.14f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.09f, 0.11f), Roughness = 0.25f, Metallic = 0.4f },
            Position = new Vector3(0, 1.58f, -0.26f),
        };
        _figure.AddChild(_visor);
        _held = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.5f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.40f, 0.33f) },
            Position = new Vector3(0.34f, 1.05f, -0.3f),
            Visible = false,
        };
        _figure.AddChild(_held);
    }

    /// <summary>Places the mannequin where the entity is drawn now, upright along <paramref name="up"/>, facing its yaw.</summary>
    public void Show(RemoteEntity entity, Num.Vector3 up)
    {
        (Num.Vector3 forward, _) = CharacterMotor.HorizontalBasis(entity.Yaw, up);
        Vector3 feet = GroundView.ToGodot(entity.Position);
        GlobalTransform = new Transform3D(Basis.LookingAt(GroundView.ToGodot(forward), GroundView.ToGodot(up)), feet);
        if (entity.Stance != _stance)
        {
            _stance = entity.Stance;
            float height = _stance == Stance.Crouching ? 1.10f : 1.76f;
            _capsule.Height = height;
            _body.Position = new Vector3(0, height / 2, 0);
            _visor.Position = new Vector3(0, height - 0.18f, -0.26f);
        }
        // Asleep: lying on the ground, head forward.
        _figure.Transform = entity.Sleeping
            ? new Transform3D(Basis.FromEuler(new Vector3(-Mathf.Pi / 2, 0, 0)), new Vector3(0, 0.3f, 0.8f))
            : Transform3D.Identity;
        // The held item, and a jerk for each swing or shot.
        _held.Visible = entity.Held != 0 && !entity.Sleeping;
        if (entity.ActionCount != _actions)
        {
            _actions = entity.ActionCount;
            _swing = 0;
        }
        if (_swing >= 0)
        {
            _swing += GetProcessDeltaTime();
            float t = (float)(_swing / 0.4);
            float lift = t < 0.3f ? t / 0.3f : Mathf.Max(0, 1 - (t - 0.3f) / 0.7f);
            _held.Rotation = new Vector3(-1.4f * lift, 0, 0);
            if (_swing > 0.4)
            {
                _swing = -1;
            }
        }
        float top = entity.Sleeping ? 0.6f : _stance == Stance.Crouching ? 1.10f : 1.76f;
        LabelPoint = feet + GroundView.ToGodot(up) * (top + 0.32f);
        Visible = (entity.Flags & MoveFlags.Dead) == 0;
    }
}
