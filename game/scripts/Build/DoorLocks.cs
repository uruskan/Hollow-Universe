using System.Linq;
using Godot;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

/// <summary>
/// The locks on doors, drawn (docs/DOORS.md §6): the catalogue's code or key lock on the +Z face of the door's first leaf,
/// by its free edge, so it swings with the leaf, and a matching back plate on the other face, so the lock is seen from both
/// sides of the door. The game's building view and the build sandbox share it.
/// </summary>
public static class DoorLocks
{
    // How deep a lock's back plate is, as a fraction of the lock itself: the housing without its keypad's depth.
    private const float BackPlateDepth = 0.4f;

    /// <summary>Puts each door's lock on its leaf (or takes it off) as the door has it now.</summary>
    public static void Sync(BuildingRenderer renderer)
    {
        foreach (Node child in renderer.GetChildren())
        {
            if (child is not DoorView view || view.Door.Def.Leaves.Count == 0)
            {
                continue;
            }
            string? want = view.Door.Lock switch
            {
                CodeLock => "code-lock",
                KeyLock => "key-lock",
                _ => null,
            };
            LeafDef leaf = view.Door.Def.Leaves[0];
            var hinge = new Vector3(leaf.Hinge.X, leaf.Hinge.Y, leaf.Hinge.Z);
            MeshInstance3D? leafNode = view.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.Position.IsEqualApprox(hinge));
            if (leafNode == null)
            {
                continue;
            }
            Node3D? fitted = leafNode.GetNodeOrNull<Node3D>("Lock");
            if (fitted != null && fitted.GetMeta("key", "").AsString() == want)
            {
                continue;
            }
            fitted?.QueueFree();
            leafNode.GetNodeOrNull<Node3D>("LockBack")?.QueueFree();
            if (want == null || HollowUniverse.Items.ModelCatalog.Instantiate(want) is not { } model)
            {
                continue;
            }
            var holder = new Node3D { Name = "Lock" };
            holder.SetMeta("key", want);
            // By the free edge, at hand height, its back on the outside face (a mounted model faces +Z from its back): where
            // the placement ghost put it.
            Num.Vector3 spot = Door.LockOffset(leaf);
            holder.Position = new Vector3(spot.X, spot.Y, spot.Z);
            holder.AddChild(model);
            leafNode.AddChild(holder);
            // The back plate on the other face: the same model turned round and pressed flat to a housing.
            if (HollowUniverse.Items.ModelCatalog.Instantiate(want) is { } plate)
            {
                var back = new Node3D { Name = "LockBack", Scale = new Vector3(1, 1, BackPlateDepth) };
                Num.Vector3 behind = Door.LockBackOffset(leaf);
                back.Position = new Vector3(behind.X, behind.Y, behind.Z);
                back.Rotation = new Vector3(0, Mathf.Pi, 0);
                back.AddChild(plate);
                leafNode.AddChild(back);
            }
        }
    }
}
