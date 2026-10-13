using Godot;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

/// <summary>
/// The plan's ghost (GROUND.md §6.3): the piece the rules would place, where they would place it, translucent green when
/// allowed and red when not. It shows exactly what <see cref="BuildingWorld.Plan"/> returned: the same code the server
/// checks a placement with.
/// </summary>
public partial class BuildGhost : Node3D
{
    private static readonly Color Allowed = new(0.30f, 0.86f, 0.42f, 0.42f);
    private static readonly Color Refused = new(0.96f, 0.24f, 0.18f, 0.45f);

    private MeshInstance3D _mesh = null!;
    private MeshInstance3D _outline = null!;
    private StandardMaterial3D _fill = null!;
    private StandardMaterial3D _edges = null!;

    public BuildingAssets Assets { get; set; } = null!;

    public override void _Ready()
    {
        _fill = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = Allowed,
            RenderPriority = 1,
        };
        _edges = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(Allowed.R * 0.55f, Allowed.G * 0.55f, Allowed.B * 0.55f, 0.9f),
            CullMode = BaseMaterial3D.CullModeEnum.Front,
            Grow = true,
            GrowAmount = 0.03f,
            RenderPriority = 0,
        };
        _mesh = new MeshInstance3D { Name = "Ghost", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = _fill };
        _outline = new MeshInstance3D { Name = "Outline", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = _edges };
        AddChild(_outline);
        AddChild(_mesh);
        Visible = false;
    }

    /// <summary>Shows the ghost's materials on <paramref name="mesh"/> where it stands (the renderer's warm-up).</summary>
    public void ShowForWarmup(Mesh mesh)
    {
        _mesh.Mesh = mesh;
        _outline.Mesh = mesh;
        Visible = true;
    }

    /// <summary>Shows a plan, or hides the ghost for null.</summary>
    public void Show(PlacementPlan? plan)
    {
        if (plan == null)
        {
            Visible = false;
            return;
        }
        Mesh mesh = Assets.HasArt(plan.Piece, 2) ? Assets.Piece(plan.Piece, 2, 1) : Assets.Hulls(plan.Piece);
        _mesh.Mesh = mesh;
        _outline.Mesh = mesh;
        Color colour = plan.Valid ? Allowed : Refused;
        _fill.AlbedoColor = colour;
        _edges.AlbedoColor = new Color(colour.R * 0.55f, colour.G * 0.55f, colour.B * 0.55f, 0.9f);
        (Num.Vector3 position, Num.Quaternion rotation) = plan.ZoneTransform;
        Transform = BuildingRenderer.ToGodot(position, rotation);
        Visible = true;
    }
}
