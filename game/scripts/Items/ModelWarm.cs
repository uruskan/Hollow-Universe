using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Items;

/// <summary>
/// The model catalogue's part of the renderer's warm-up (GROUND.md §4.3.1): every model of <c>models.json</c> (items,
/// world models, ships), at every stage, with all its levels of detail drawn at once, as the world draws them. The
/// building's kits are the building art's to draw (<c>BuildingWarm</c>); items held in the hand, the first person's
/// (<c>CharacterWarm</c>).
/// </summary>
public static class ModelWarm
{
    public static IEnumerable<WarmItem> Items()
    {
        foreach (ModelEntry entry in ModelCatalog.Entries)
        {
            if (entry.IsKit)
            {
                continue;
            }
            for (int stage = 0; stage < entry.Files.Count; stage++)
            {
                int s = stage;
                yield return new WarmItem($"{entry.Key}/{s}", new[] { entry.Files[s] }, _ => EveryLevel(ModelCatalog.Instantiate(entry.Key, s)));
            }
        }
    }

    // Every level of detail at once: the farther levels are drawn too (their meshes may differ in what they carry).
    private static Node3D? EveryLevel(Node3D? model)
    {
        if (model == null)
        {
            return null;
        }
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(model))
        {
            mesh.VisibilityRangeBegin = 0;
            mesh.VisibilityRangeEnd = 0;
            mesh.Visible = true;
        }
        return model;
    }
}
