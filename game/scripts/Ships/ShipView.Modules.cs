using Godot;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using HollowUniverse.Items;

namespace HollowUniverse.Ships;

/// <summary>
/// A ship's modules drawn (VEHICLES.md §9.5, docs/MINICOPTER.md §8): each installed module's own model (modules.json
/// <c>model</c>, by default its key) under the ship's model, its frame's origin where the build put it (sockets don't turn
/// modules). A module the catalogue has no model for is left to the blocks (ShipView.Blocks.cs). Mounted once: a view is
/// made for one composed layout, and a ship whose build changes gets a new view (ShipsView).
/// </summary>
public partial class ShipView
{
    /// <summary>The layout this view draws: the ship's kind with its build (a new build is a new view).</summary>
    public ShipDef Def => _def;

    private void MountModules()
    {
        foreach (PlacedModule placed in _def.Placed)
        {
            string key = string.IsNullOrEmpty(placed.Module.Model) ? placed.Module.Key : placed.Module.Model;
            if (ModelCatalog.Instantiate(key) is not { } model)
            {
                continue;
            }
            model.Name = $"Module_{placed.Entry.Socket}_{placed.Entry.Level}";
            model.Position = new Vector3(placed.Origin.X, placed.Origin.Y, placed.Origin.Z);
            _model.AddChild(model);
        }
    }
}
