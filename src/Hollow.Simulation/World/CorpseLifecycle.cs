using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.World;

/// <summary>Configurable player-body rules; the lootbag's later expiry is the existing survival lifetime.</summary>
public sealed class CorpseTuning
{
    public float BodySeconds { get; init; } = 150;
    public float HarvestHealth { get; init; } = 100;
    public IReadOnlyList<ItemAmount> Yield { get; init; } = Array.Empty<ItemAmount>();
    public long BodyTicks => Math.Max(1, (long)Math.Ceiling(BodySeconds * CharacterMotor.TickRate));
}

public enum CorpseStage : byte { Body, Bag }

/// <summary>
/// Server-owned body phase, harvest progress and appearance. Loot is deliberately outside this state: changing phase
/// cannot copy, reset, drop or replace the container being looted. Ship wrecks do not have this player-body state.
/// </summary>
public sealed class CorpseLifecycle
{
    public CorpseLifecycle(CorpseTuning tuning, long bornTick, byte body, IEnumerable<ushort>? wear = null)
    {
        Tuning = tuning;
        BodyUntilTick = bornTick + tuning.BodyTicks;
        HarvestLeft = tuning.HarvestHealth;
        Body = body <= 1 ? body : (byte)0;
        Wear = (wear ?? Array.Empty<ushort>()).Take(7).ToArray();
    }

    public CorpseTuning Tuning { get; }
    public CorpseStage Stage { get; private set; }
    public long BodyUntilTick { get; private set; }
    public float HarvestLeft { get; private set; }
    public byte Body { get; }
    public IReadOnlyList<ushort> Wear { get; }
    public bool IsBody => Stage == CorpseStage.Body;
    public float Fraction => IsBody && Tuning.HarvestHealth > 0 ? Math.Clamp(HarvestLeft / Tuning.HarvestHealth, 0, 1) : 0;

    /// <summary>True exactly once when time or harvesting turns the body into its lootbag.</summary>
    public bool Advance(long tick)
    {
        if (!IsBody || (tick < BodyUntilTick && HarvestLeft > 0)) return false;
        Stage = CorpseStage.Bag;
        HarvestLeft = 0;
        return true;
    }

    /// <summary>A rate supplied by the shared harvest-tool table, after the normal attack/reach checks.</summary>
    public bool Harvest(float rate, List<ItemAmount> gained)
    {
        if (!IsBody) return false;
        float left = HarvestLeft;
        bool hit = HarvestProgress.Take(Tuning.Yield, Tuning.HarvestHealth, ref left, rate, gained);
        HarvestLeft = left;
        return hit;
    }

    public long RemainingBodyTicks(long tick) => IsBody ? Math.Max(0, BodyUntilTick - tick) : 0;

    /// <summary>Restore relative world time and partial harvest. Restoring a bag can never renew its harvest.</summary>
    public void Restore(CorpseStage stage, long remainingTicks, float harvestLeft, long tick)
    {
        Stage = stage == CorpseStage.Body ? CorpseStage.Body : CorpseStage.Bag;
        BodyUntilTick = tick + Math.Clamp(remainingTicks, 0, Tuning.BodyTicks);
        HarvestLeft = IsBody && float.IsFinite(harvestLeft) ? Math.Clamp(harvestLeft, 0, Tuning.HarvestHealth) : 0;
    }
}
