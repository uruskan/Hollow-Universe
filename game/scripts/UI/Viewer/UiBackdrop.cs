using Godot;

namespace HollowUniverse.UI.Viewer;

/// <summary>
/// A plain scene behind the interface viewer, so the panels' translucency and blur can be judged against something
/// like the references' worlds: an overcast sky, a sandy plain, a workbench-like table and a tall cupboard.
/// </summary>
public partial class UiBackdrop : Node3D
{
    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.42f, 0.47f, 0.46f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.78f, 0.80f, 0.78f),
                AmbientLightEnergy = 0.5f,
                FogEnabled = true,
                FogLightColor = new Color(0.55f, 0.58f, 0.56f),
                FogDensity = 0.012f,
            },
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52, -38, 0),
            LightEnergy = 0.75f,
            ShadowEnabled = true,
        });
        Block(new Vector3(400, 0.2f, 400), new Vector3(0, -0.1f, 0), new Color(0.78f, 0.72f, 0.53f));

        // A table: top and four legs.
        Block(new Vector3(2.2f, 0.12f, 1.1f), new Vector3(-2.3f, 0.95f, -4.4f), new Color(0.88f, 0.70f, 0.42f));
        foreach ((float x, float z) in new[] { (-3.3f, -4.0f), (-1.3f, -4.0f), (-3.3f, -4.8f), (-1.3f, -4.8f) })
        {
            Block(new Vector3(0.1f, 0.9f, 0.1f), new Vector3(x, 0.45f, z), new Color(0.27f, 0.38f, 0.34f));
        }
        // A cupboard and a crate.
        Block(new Vector3(1.6f, 2.4f, 0.7f), new Vector3(2.1f, 1.2f, -5.2f), new Color(0.85f, 0.68f, 0.44f));
        Block(new Vector3(0.05f, 2.1f, 0.05f), new Vector3(2.1f, 1.2f, -4.84f), new Color(0.55f, 0.42f, 0.27f));
        Block(new Vector3(1.1f, 0.8f, 0.9f), new Vector3(1.4f, 0.4f, -3.4f), new Color(0.62f, 0.57f, 0.42f));

        AddChild(new Camera3D
        {
            Position = new Vector3(0, 1.65f, 0),
            RotationDegrees = new Vector3(-10, 0, 0),
            Fov = 70,
            Current = true,
        });
    }

    private void Block(Vector3 size, Vector3 at, Color colour)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = at,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f },
        });
    }
}
