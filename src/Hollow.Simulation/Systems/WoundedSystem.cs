using System.Numerics;
using System;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// The wounded state (GROUND.md §6.3, Rust's): a lethal hit that is small enough, and isn't a shot to the head, knocks
// the player down instead. Down they crawl with a little health and can't act; another player can help them up; any more
// damage finishes them off; when their time runs out they get up by themselves now and then (more often well fed and
// watered), else they die of it. A wounded player who logs off dies. survival.json "wounded" has the numbers.
public sealed class WoundedSystem : ZoneSystem
{
    private GunSystem? _gunsSystem;
    private GunSystem Guns => _gunsSystem ??= Zone.Require<GunSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();

    /// <summary>Whether a lethal hit of <paramref name="amount"/> knocks this player down rather than killing them.</summary>
    public bool CanBeWounded(Player p, float amount) =>
        !p.Wounded && !p.Sleeping && !p.Dead && amount < Content.Survival.WoundMaxDamage;

    /// <summary>Knocks a player down (instead of a death): a little health, crawling, a timer; what they were doing stops.</summary>
    public void Wound(Entity entity, Player p, string cause, EntityId by, string byName)
    {
        SurvivalTuning s = Content.Survival;
        var rng = new Rng(Hash64.Of(p.Id.Value, (ulong)Tick, 0x3D0E));
        int least = (int)Math.Ceiling(s.WoundedHealthLeast), most = Math.Max(least, (int)Math.Floor(s.WoundedHealthMost));
        p.Wounded = true;
        p.Vitals.Health = least + rng.NextInt(most - least + 1);
        p.WoundedUntilTick = Tick + CharacterTicks.From(rng.Range(s.WoundedSecondsLeast, s.WoundedSecondsMost));
        p.WoundedCause = cause;
        p.WoundedBy = by;
        p.WoundedByName = byName;
        p.Looting = EntityId.None;
        p.LootRevision++;
        p.StrikeTick = -1;
        p.DrawStartTick = -1;
        p.VitalsRevision++;
        Guns.RefuseGunActions(p, OutcomeCode.Dead);
        ref Character c = ref Entities.Get<Character>(entity);
        c.State.Stance = Stance.Crawling;
        PlayerSystem.SyncHealth(ref c, p);
        Events.Add(new WentDown(Player: p.Id, By: by, Cause: cause, Position: WorldOf(c.State)));
    }

    /// <summary>
    /// Helps a wounded player up (Rust's assist: the wounded capability's verb, GROUND_SYSTEMS.md §5). They get up with the
    /// health they have.
    /// </summary>
    internal Outcome HelpUp(Entity helper, Player down)
    {
        if (!down.Wounded || down.Dead || !Players.TryFindPlayer(down.Id, out Entity e, out _) || e == helper)
        {
            return OutcomeCode.NotFound;
        }
        GetUp(e, down, Entities.IdOf(helper));
        return OutcomeCode.Done;
    }

    /// <summary>Whether a wounded player whose time ran out gets up by themselves (a roll fixed by who and when).</summary>
    public bool RecoversByThemselves(Player p)
    {
        SurvivalTuning s = Content.Survival;
        float fed = Math.Clamp((p.Vitals.Calories / s.MaxCalories + p.Vitals.Hydration / s.MaxHydration) / 2, 0, 1);
        double chance = s.WoundedRecoverChance + s.WoundedFedBonus * fed;
        var rng = new Rng(Hash64.Of(p.Id.Value, (ulong)p.WoundedUntilTick, 0x0E5C));
        return rng.NextDouble() < chance;
    }

    // Every tick: the wounded whose time has run out get up or die.
    private void StepWounded()
    {
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            if (!p.Wounded || p.Dead || Tick < p.WoundedUntilTick)
            {
                continue;
            }
            Entity entity = Entities.AtSlot(players.Slots[i]);
            if (RecoversByThemselves(p))
            {
                GetUp(entity, p, EntityId.None);
            }
            else
            {
                Players.Die(entity, p, p.WoundedCause.Length > 0 ? p.WoundedCause : "Bled out", p.WoundedBy, p.WoundedByName);
            }
        }
    }

    private void GetUp(Entity entity, Player p, EntityId helper)
    {
        p.Wounded = false;
        p.VitalsRevision++;
        ref Character c = ref Entities.Get<Character>(entity);
        // Up to a crouch; the next step stands up where there is room.
        c.State.Stance = Stance.Crouching;
        PlayerSystem.SyncHealth(ref c, p);
        Events.Add(new GotUp(Player: p.Id, Helper: helper, Position: WorldOf(c.State)));
    }

    protected internal override void Step()
    {
        StepWounded();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class WoundedSystemAccess
{
    public static WoundedSystem Wounded(this Zone zone) => zone.Require<WoundedSystem>();
}
