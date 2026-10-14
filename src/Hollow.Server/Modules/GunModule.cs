using System;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;

namespace Hollow.Server;

/// <summary>
/// Guns on the server (GROUND.md §6.4, G4): shots, reloads and unloads from the player's own gun budget (a refused one
/// is still answered, in order), queued for their tick in the zone; attachments on the general budget. The gun's state
/// goes back to the player with their own state (<see cref="SurvivalModule"/>).
/// </summary>
public sealed class GunModule : ServerModule
{
    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        handlers.On<FireIntent>((PlayerSession session, in FireIntent m) => GunIntent(session, new GunAction
        {
            Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Fire, Slot = m.Slot, Item = Expected(m.Item),
            View = Quantize.FromOctahedral(m.ViewU, m.ViewV), Aimed = m.Aimed, ViewDelay = m.ViewDelay,
        }));
        handlers.On<ReloadIntent>((PlayerSession session, in ReloadIntent m) => GunIntent(session, new GunAction
        {
            Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Reload, Slot = m.Slot, Item = Expected(m.Item), Ammo = Expected(m.Ammo),
        }));
        handlers.On<UnloadIntent>((PlayerSession session, in UnloadIntent m) => GunIntent(session, new GunAction
        {
            Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Unload, Slot = m.Slot, Item = Expected(m.Item),
        }));
        handlers.On<AttachIntent>((PlayerSession session, in AttachIntent message) =>
        {
            AttachIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Guns().Attach(me, m.From, m.Slot, m.WeaponIn, m.WeaponSlot, Expected(m.Expected)));
        });
    }

    // A shot or reload: from its own budget (a refused one is still answered in order), queued for its tick in the zone.
    private void GunIntent(PlayerSession session, GunAction action)
    {
        SurvivalTuning s = Content.Survival;
        GunBudget budget = session.State<GunBudget>();
        budget.Actions++;
        if (!budget.Spend(Zone.Tick, s.GunBurst, s.GunActionsPerSecond))
        {
            action.Refused = OutcomeCode.RateLimited;
            budget.RateLimited++;
        }
        if (session.Playing)
        {
            Zone.Guns().QueueGunAction(session.Entity, action);
        }
    }
}

/// <summary>
/// A player's gun budget (RustMVP's weapon budget, survival.json <c>gunBurst</c>, <c>gunActionsPerSecond</c>): shots and
/// reloads spend from their own bucket, so a firefight never starves the inventory's intents; and the gun actions
/// (shots and reloads) that arrived and those refused for the budget.
/// </summary>
public sealed class GunBudget
{
    private bool _started;
    private double _tokens;
    private long _tick;

    public bool Spend(long tick, int burst, float perSecond)
    {
        if (!_started)
        {
            _started = true;
            _tokens = burst;
            _tick = tick;
        }
        _tokens = Math.Min(burst, _tokens + (tick - _tick) * (double)perSecond / Simulation.Movement.CharacterMotor.TickRate);
        _tick = tick;
        if (_tokens < 1)
        {
            return false;
        }
        _tokens -= 1;
        return true;
    }

    public long Actions { get; set; }

    public long RateLimited { get; set; }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the gun intents.</summary>
public static class GunModuleAccess
{
    public static GunModule Guns(this GameServer server) => server.Require<GunModule>();
}
