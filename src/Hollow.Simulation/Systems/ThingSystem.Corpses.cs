using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

public sealed partial class ThingSystem
{
    private void TraceCorpses(in RayQuery ray, ref TraceHit best)
    {
        foreach (ILifetime loose in _loose)
        {
            if (loose is not Corpse { IsBody: true } corpse) continue;
            (Vector3 right, Vector3 forward) = Geometry.Axes(corpse.Up, corpse.Yaw);
            float t = Geometry.RayBox(ray.Origin, ray.Direction, ray.Length, corpse.Position + corpse.Up * 0.3f,
                new Vector3(0.45f, 0.3f, 0.95f) + new Vector3(ray.Thickness), right, corpse.Up, forward);
            ray.Consider(ref best, t, corpse.Ref);
        }
    }

    private DamageOutcome HarvestCorpse(Corpse corpse, in DamageRequest request)
    {
        if (corpse.Life is not { IsBody: true } life || request.Channel != DamageType.Melee ||
            request.By is not { } player || Content.Item(request.Weapon) is not { } tool ||
            !Zone.Players().TryFindPlayer(player.Id, out Entity entity, out _)) return default;
        var gained = new List<ItemAmount>();
        float rate = Content.HarvestRates.Rate(tool);
        if (!life.Harvest(rate, gained)) return default;
        corpse.Revision++;
        Events.Add(new HitConfirmed(By: player.Id, Victim: corpse.Id, Target: corpse.Ref, Region: HitRegion.Body,
            Amount: rate, Point: request.Hit.Point, Weapon: tool.Id));
        if (gained.Count > 0) Zone.Combat().Give(entity, player, gained, GainReason.Gathered);
        AdvanceCorpse(corpse);
        EndIfEmptied(corpse);
        return default; // harvesting is a gathering event, not living-player damage or a second death
    }

    private void AdvanceCorpse(Corpse corpse)
    {
        if (corpse.Life?.Advance(Tick) != true) return;
        // Keep the object and its exact container: connected looters retain the same slots and revision history.
        corpse.DespawnTick = Tick + corpse.Lifetime.Ticks;
        corpse.Revision++;
    }
}
