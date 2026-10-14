using System;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>
/// Records every player's pose at the end of the tick (ARCHITECTURE.md §11.5): the second of poses hits are judged
/// against as the attacker saw them (<see cref="Zone.History"/>). Last in the order, after everything that moved anyone.
/// </summary>
public sealed class HistorySystem : ZoneSystem
{
    protected internal override void Step()
    {
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        ReadOnlySpan<int> slots = players.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            Player p = players.Values[i].Player;
            if (p.Dead)
            {
                continue;
            }
            ref Character c = ref Entities.Get<Character>(Entities.AtSlot(slots[i]));
            if (c.State.Stance == Stance.Seated)
            {
                // In a ship's cabin (G6): shots hit the hull, not the pilot.
                History.Forget(p.Id);
                continue;
            }
            History.Record(p.Id, Tick, new HitPose
            {
                Feet = c.State.Position,
                Up = Frame.UpAt(c.State.Position),
                Yaw = c.Input.YawAt(Frame.UpAt(c.State.Position)),
                Pose = p.Sleeping || p.Wounded ? Pose.Lying : c.State.Stance == Stance.Crouching ? Pose.Crouching : Pose.Standing,
            });
        }
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class HistorySystemAccess
{
    public static HistorySystem HistorySys(this Zone zone) => zone.Require<HistorySystem>();
}
