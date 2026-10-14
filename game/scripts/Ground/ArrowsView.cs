using System;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.World;
using HollowUniverse.Items;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Spent arrows stuck where they hit (GROUND_SYSTEMS.md §5): every one this client knows, placed each frame where its holder
/// is now (riding a body's bone, swinging with a door's leaf), drawn with the arrow's own model as one multimesh: one draw
/// call and no shadow, the first <see cref="Most"/> within <see cref="Range"/> metres of the player.
/// </summary>
public partial class ArrowsView : Node3D
{
    /// <summary>The most arrows drawn at once.</summary>
    public const int Most = 512;

    /// <summary>Arrows farther than this from the player aren't drawn (a thin thing, lost at that distance anyway).</summary>
    public const float Range = 60f;

    private MultiMeshInstance3D? _instances;

    // The mesh in the arrow model's frame (the nock at the origin, the shaft along +X to the tip, SpentArrow's frame).
    private Transform3D _mesh = Transform3D.Identity;

    public GameClient Client { get; set; } = null!;

    public override void _Ready()
    {
        if (ModelCatalog.Instantiate("arrow") is not { } arrow)
        {
            return;
        }
        // The finest of its levels of detail: an arrow is a few hundred triangles, and is looked at closely to pick it up.
        if (ModelCatalog.Meshes(arrow).OrderBy(m => Math.Max(0, ModelCatalog.LodOf(m))).FirstOrDefault() is { Mesh: { } mesh } finest)
        {
            _mesh = ModelCatalog.RelativeTransform(arrow, finest);
            _instances = new MultiMeshInstance3D
            {
                Name = "Stuck",
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = Most, VisibleInstanceCount = 0,
                },
            };
            AddChild(_instances);
        }
        arrow.Free();
    }

    /// <summary>Every frame, after the bodies, doors and things that hold arrows have moved.</summary>
    public void Refresh()
    {
        if (_instances?.Multimesh is not { } multimesh)
        {
            return;
        }
        Num.Vector3 me = Client.Character.Position;
        int shown = 0;
        foreach (RemoteArrow arrow in Client.Arrows().All)
        {
            if (shown == Most)
            {
                break;
            }
            if (!arrow.Placed || Num.Vector3.DistanceSquared(arrow.Pose.Position, me) > Range * Range)
            {
                continue;
            }
            (Num.Vector3 nock, _) = SpentArrow.Shaft(arrow.Pose);
            Num.Quaternion q = arrow.Pose.Rotation;
            multimesh.SetInstanceTransform(shown++, new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), GroundView.ToGodot(nock)) * _mesh);
        }
        multimesh.VisibleInstanceCount = shown;
    }
}
