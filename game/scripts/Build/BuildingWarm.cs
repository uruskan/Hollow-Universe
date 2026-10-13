using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Simulation.Building;
using HollowUniverse.Common;

namespace HollowUniverse.Build;

/// <summary>
/// The building's part of the renderer's warm-up (GROUND.md §4.3.1): every grade's material on its pieces' levels of
/// detail, batched as <see cref="BuildingRenderer"/> batches them; the placement ghost's hologram; every door and window
/// part; the garage door.
/// </summary>
public static class BuildingWarm
{
    public static IEnumerable<WarmItem> Items(BuildingData data)
    {
        yield return new WarmItem("building grades", BuildingAssets.Files, spot =>
        {
            BuildingAssets assets = BuildingAssets.Load(data);
            var group = new Node3D { Name = "Grades" };
            spot.Holder.AddChild(group);
            float x = 0;
            Mesh? ghostMesh = null;
            foreach ((int grade, Mesh[] lods) in assets.GradeSamples())
            {
                foreach (Mesh mesh in lods)
                {
                    var batch = new MultiMesh
                    {
                        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = 1,
                    };
                    batch.SetInstanceTransform(0, Transform3D.Identity);
                    batch.SetInstanceColor(0, Colors.White);
                    group.AddChild(new MultiMeshInstance3D { Multimesh = batch, MaterialOverride = assets.Grade(grade), Position = new Vector3(x += 4, 0, 0) });
                }
                ghostMesh ??= lods.Length > 1 ? lods[1] : null;
            }
            if (ghostMesh != null)
            {
                // The placement ghost shows a piece's middle level in its hologram (GROUND_SYSTEMS.md §6).
                MeshInstance3D ghost = HollowUniverse.Ground.PlacementGhost.Warmup(ghostMesh);
                ghost.Name = "Ghost";
                ghost.Position = new Vector3(x + 4, 0, 0);
                group.AddChild(ghost);
            }
            return group;
        });
        yield return new WarmItem("doors and windows", BuildingAssets.Files, _ =>
        {
            BuildingAssets assets = BuildingAssets.Load(data);
            var group = new Node3D { Name = "Parts" };
            float x = 0;
            foreach (Mesh part in assets.AllParts)
            {
                group.AddChild(new MeshInstance3D { Mesh = part, MaterialOverride = assets.PartMaterial(part), Position = new Vector3(x += 3, 0, 0) });
            }
            if (assets.Garage?.Instantiate() is Node3D garage)
            {
                garage.Position = new Vector3(x + 4, 0, 0);
                group.AddChild(garage);
            }
            return group;
        });
    }
}
