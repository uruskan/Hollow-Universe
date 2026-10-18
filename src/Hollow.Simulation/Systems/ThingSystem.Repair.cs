using System;
using System.Collections.Generic;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

public sealed partial class ThingSystem
{
    /// <summary>Called by the shared hammer route after its held tool, reach and sight checks.</summary>
    internal Outcome RepairDeployable(EntityId player, Deployable thing, IBuildingWallet wallet)
    {
        if (StaticOf(thing.Id) != thing || thing.Health <= 0) return OutcomeCode.NotFound;
        if (thing.Def.RepairCost.Count == 0) return OutcomeCode.NotUsable;
        BuildingWorld building = Building.World;
        Privilege privilege = building.PrivilegeAt(player.Value, thing.Position);
        if (privilege == Privilege.Blocked) return OutcomeCode.BuildingBlocked;
        if (thing.Owner != player && privilege != Privilege.Allowed) return OutcomeCode.NotOwner;
        float maximum = thing.Def.Health;
        if (thing.Health >= maximum) return OutcomeCode.FullHealth;
        if (thing.LastDamagedAtUnixMs != long.MinValue)
        {
            double wait = (thing.LastDamagedAtUnixMs + building.Data.RepairDelay * 1000 - UnixMilliseconds) / 1000;
            if (wait > 0) return OutcomeCode.RecentlyDamaged.With((int)Math.Ceiling(wait));
        }
        float restored = Math.Min((float)building.Data.RepairPerHit, maximum - thing.Health);
        var costs = new List<ItemAmount>(thing.Def.RepairCost.Count);
        foreach (ItemAmount material in thing.Def.RepairCost)
        {
            int amount = building.RepairCost(material.Count, restored, maximum);
            if (wallet.Count(material.Item.Key) < amount) return OutcomeCode.NotEnough.With(material.Item.Id);
            costs.Add(new ItemAmount(material.Item, amount));
        }
        // The zone is single-threaded: preflight every material before taking any from its inventory wallet.
        foreach (ItemAmount material in costs)
            if (!wallet.Take(material.Item.Key, material.Count)) return OutcomeCode.NotEnough.With(material.Item.Id);
        thing.Health = Math.Min(maximum, thing.Health + restored);
        thing.Revision++;
        return Outcome.Done;
    }
}
