using Godot;

namespace HollowUniverse.UI;

/// <summary>
/// The figure on the inventory screen: a plain wooden mannequin in its own little world, rendered into a texture the
/// size of its panel (so it stays sharp). It is a placeholder: the character package supplies our real body, with its
/// clothing, later (GROUND.md §1). The held item shows as a block in its hands.
/// </summary>
public partial class CharacterPreview : SubViewport
{
    private static readonly Color Wood = new("9d816b");
    private Node3D _figure = null!;
    private string _signature = "";

    /// <summary>The figure's turn, independent of its clothing and held-item rebuilds.</summary>
    public float Yaw => _figure.Rotation.Y;

    public void Turn(float designPixels) => _figure.RotateY(designPixels * 0.01f);

    /// <summary>
    /// The game's own body for the figure (the character package's), built for the held item key; null keeps the
    /// wooden mannequin. <see cref="BodyLook"/> names what it wears, so a change of clothes builds it again.
    /// </summary>
    public static System.Func<string?, Node3D>? Body { get; set; }

    public static System.Func<string>? BodyLook { get; set; }

    public override void _Ready()
    {
        OwnWorld3D = true;
        TransparentBg = true;
        Msaa3D = Msaa.Msaa4X;
        RenderTargetUpdateMode = UpdateMode.Disabled;
        Size = new Vector2I(455, 628);

        var scene = new Node3D { Name = "Scene" };
        AddChild(scene);
        _figure = new Node3D { Name = "Figure", Rotation = new Vector3(0, -0.18f, 0) };
        scene.AddChild(_figure);
        Dress(null);

        scene.AddChild(new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 2.24f,
            Position = new Vector3(0, 1.04f, 4f),
            Current = true,
        });
        scene.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-25, -30, 0), LightEnergy = 1.6f });
        scene.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(10, 140, 0), LightEnergy = 0.65f, LightColor = new Color("9bb5bb") });
        scene.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b6b7a8"),
                AmbientLightEnergy = 0.7f,
            },
        });
    }

    /// <summary>Renders while the inventory shows it, at exactly the panel's pixel size.</summary>
    public void Present(bool visible, Vector2I pixels)
    {
        if (visible && Size != pixels && pixels.X > 0 && pixels.Y > 0)
        {
            Size = pixels;
        }
        RenderTargetUpdateMode = visible ? UpdateMode.Always : UpdateMode.Disabled;
    }

    /// <summary>Rebuilds the figure for what it holds (an item key, or null).</summary>
    public void Dress(string? held)
    {
        string signature = (held ?? "") + "|" + (BodyLook?.Invoke() ?? "");
        if (signature == _signature)
        {
            return;
        }
        _signature = signature;
        foreach (Node child in _figure.GetChildren())
        {
            child.QueueFree();
        }
        if (Body != null)
        {
            _figure.AddChild(Body(held));
            return;
        }
        Color shirt = Wood, trousers = Wood;
        Part(new Vector3(0.43f, 0.62f, 0.27f), new Vector3(0, 1.23f, 0), shirt, true);
        Part(new Vector3(0.34f, 0.26f, 0.24f), new Vector3(0, 0.91f, 0), trousers, true);
        Part(new Vector3(0.22f, 0.31f, 0.22f), new Vector3(0, 1.72f, 0), Wood, true);
        Part(new Vector3(0.12f, 0.13f, 0.12f), new Vector3(0, 1.53f, 0), Wood, true);
        foreach (float side in new[] { -1f, 1f })
        {
            Part(new Vector3(0.16f, 0.75f, 0.17f), new Vector3(0.12f * side, 0.5f, 0), trousers, true);
            Part(new Vector3(0.17f, 0.12f, 0.26f), new Vector3(0.12f * side, 0.08f, 0.06f), Wood, false);
            Part(new Vector3(0.14f, 0.36f, 0.14f), new Vector3(0.28f * side, 1.31f, 0), shirt, true).Rotation = new Vector3(0, 0, side * 0.16f);
            MeshInstance3D arm = Part(new Vector3(0.12f, 0.37f, 0.12f), new Vector3(0.3f * side, 1.02f, 0.05f), Wood, true);
            if (held != null)
            {
                arm.Rotation = new Vector3(-1f, 0, 0);
                arm.Position = new Vector3(0.22f * side, 1.14f, 0.21f);
            }
        }
        if (held != null)
        {
            (Vector3 size, Color colour) = held switch
            {
                "plan" => (new Vector3(0.39f, 0.25f, 0.03f), new Color("507489")),
                "pistol" => (new Vector3(0.26f, 0.1f, 0.08f), new Color("454a44")),
                "rifle" => (new Vector3(0.76f, 0.1f, 0.09f), new Color("454a44")),
                _ => (new Vector3(0.17f, 0.17f, 0.17f), new Color("a89e7f")),
            };
            Part(size, new Vector3(0, 1.16f, 0.36f), colour, false);
        }
    }

    private MeshInstance3D Part(Vector3 size, Vector3 at, Color colour, bool rounded)
    {
        Mesh mesh = rounded
            ? new CapsuleMesh { Radius = size.X * 0.5f, Height = Mathf.Max(size.Y, size.X), RadialSegments = 24, Rings = 8 }
            : new BoxMesh { Size = size };
        var part = new MeshInstance3D
        {
            Mesh = mesh,
            Position = at,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.88f },
        };
        _figure.AddChild(part);
        return part;
    }
}
