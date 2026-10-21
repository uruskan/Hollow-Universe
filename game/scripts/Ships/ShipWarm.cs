using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Simulation.Content;
using Hollow.Simulation.Ships;
using HollowUniverse.Common;
using HollowUniverse.Items;

namespace HollowUniverse.Ships;

/// <summary>
/// The ships' part of the renderer's warm-up (GROUND.md §4.3.1, §6.7): every ship an item makes (a kind with its default
/// build, or a preset's, VEHICLES.md §9.11: the Monster TOGG's frame and parts) as <see cref="ShipView"/> draws it with its
/// engines running and boosting: the model, its modules, every plume, the lamps and lights.
/// </summary>
public static class ShipWarm
{
    public static IEnumerable<WarmItem> Items(GameContent content) =>
        content.Items.Where(item => item.Ship != null).Select(item => (item.Key, Def: content.ShipMade(item)!)).Select(made =>
            new WarmItem($"ship {made.Key}", ModelCatalog.Find(made.Def.Model)?.Files ?? Array.Empty<string>(), spot =>
        {
            var view = new ShipView(made.Def);
            spot.Holder.AddChild(view);
            Transform3D pivot = spot.Holder.GlobalTransform * new Transform3D(Basis.Identity, spot.Place);
            view.Show(new ShipLook(pivot, 1, new System.Numerics.Vector3(1, 1, 1.5f), Engines: true, Boosting: true, Piloted: true,
                Destroyed: false, Spool: 1), 0.5);
            return view;
        }));
}
