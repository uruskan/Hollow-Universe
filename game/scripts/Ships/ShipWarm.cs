using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Simulation.Ships;
using HollowUniverse.Common;
using HollowUniverse.Items;

namespace HollowUniverse.Ships;

/// <summary>
/// The ships' part of the renderer's warm-up (GROUND.md §4.3.1, §6.7): every ship of <c>ships.json</c> as
/// <see cref="ShipView"/> draws it with its engines running and boosting: the model, every plume, the lamps and lights.
/// </summary>
public static class ShipWarm
{
    public static IEnumerable<WarmItem> Items(IEnumerable<ShipDef> ships) =>
        ships.Select(def => new WarmItem($"ship {def.Key}", ModelCatalog.Find(def.Key)?.Files ?? Array.Empty<string>(), spot =>
        {
            var view = new ShipView(def);
            spot.Holder.AddChild(view);
            Transform3D pivot = spot.Holder.GlobalTransform * new Transform3D(Basis.Identity, spot.Place);
            view.Show(new ShipLook(pivot, 1, new System.Numerics.Vector3(1, 1, 1.5f), Engines: true, Boosting: true, Piloted: true,
                Destroyed: false), 0.5);
            return view;
        }));
}
