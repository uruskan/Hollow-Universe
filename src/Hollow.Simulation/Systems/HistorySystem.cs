using System;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>
/// Records every player's pose at the end of the tick (ARCHITECTURE.md §11.5): the second of poses hits are judged
/// against as the attacker saw them (<see cref="Zone.History"/>). Last in the order, after everything that moved anyone.
/// Every ship's pose is recorded too, and a player aboard one is recorded in its frame (IRON_WAKE.md §4.9), so a pose
/// sampled at a moment is the ship's then carrying the player as they stood on its deck then.
/// </summary>
public sealed class HistorySystem : ZoneSystem
{
    protected internal override void Step()
    {
        if (Zone.Get<ShipSystem>() is { } ships)
        {
            foreach (Ship ship in ships.All)
            {
                History.RecordShip(ship.Id.Value, Tick, ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass));
            }
        }
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        ReadOnlySpan<int> slots = players.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            Player p = players.Values[i].Player;
            if (p.Dead)
            {
                continue;
            }
            Entity entity = Entities.AtSlot(slots[i]);
            ref Character c = ref Entities.Get<Character>(entity);
            if (c.State.Stance == Stance.Seated && Zone.Get<ShipSystem>()?.SeatExposed(entity) != true)
            {
                // In a sheltered seat (a cabin, the bridge, IRON_WAKE.md IW4): shots hit the hull, not the one in it.
                History.Forget(p.Id);
                continue;
            }
            Pose pose = c.State.Stance == Stance.Seated ? Pose.Seated : p.Sleeping || p.Wounded ? Pose.Lying : c.State.Stance == Stance.Crouching ? Pose.Crouching : Pose.Standing;
            if (c.State.Frame != 0)
            {
                // Aboard: in the ship's frame, its up +Y, the view turned into it if it came in another.
                Vector3 view = Zone.Frames.ViewIn(c.Input.View, c.Input.Frame, c.State.Frame, Tick);
                History.Record(p.Id, Tick, new HitPose { Feet = c.State.Position, Up = Vector3.UnitY, Yaw = Facing.YawOf(view, Vector3.UnitY), Pose = pose }, c.State.Frame);
                continue;
            }
            Vector3 up = Frame.UpAt(c.State.Position);
            History.Record(p.Id, Tick, new HitPose
            {
                Feet = c.State.Position,
                Up = up,
                Yaw = Facing.YawOf(Zone.Frames.ViewIn(c.Input.View, c.Input.Frame, 0, Tick), up),
                Pose = pose,
            });
        }
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class HistorySystemAccess
{
    public static HistorySystem HistorySys(this Zone zone) => zone.Require<HistorySystem>();
}
