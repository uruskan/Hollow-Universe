using System.Numerics;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

/// <summary>
/// A character in the zone's frame, wherever it moves (IRON_WAKE.md §4.2): aboard a ship its state is the ship's frame's,
/// so what the rules do in the world (a swing, a shot, the air's temperature, where a body falls, who hears it) asks here
/// for where the ship carries it. The zone's frame's characters are where they are.
/// </summary>
public static class CharacterWorld
{
    /// <summary>A character's feet in the zone's frame (aboard a ship, where the ship carries them).</summary>
    public static Vector3 WorldOf(this Zone zone, in CharacterState state) => zone.Frames.ToWorld(state.Position, state.Frame, zone.Tick);

    /// <summary>
    /// What carries a character, for who hears of it (IRON_WAKE.md §4.11): aboard a ship, the ship's centre of mass (a
    /// player sees the people on a ship as far as they see the ship); null in the zone's frame.
    /// </summary>
    public static Vector3? AnchorOf(this Zone zone, in CharacterState state) =>
        state.Frame != 0 && zone.Frames.Ship(state.Frame) is { } ship && ship.TryPose(zone.Tick, out ShipPose pose) ? pose.Position : null;

    /// <summary>Up at a character in the zone's frame: away from the planet's centre, aboard the ship's up.</summary>
    public static Vector3 UpOf(this Zone zone, in CharacterState state) =>
        state.Frame == 0 ? zone.Frame.UpAt(state.Position) : zone.Frames.DirectionToWorld(Vector3.UnitY, state.Frame, zone.Tick);

    /// <summary>A character's velocity in the zone's frame: aboard, its walk on the deck and the ship's motion where it stands.</summary>
    public static Vector3 VelocityOf(this Zone zone, in CharacterState state)
    {
        if (state.Frame == 0 || zone.Frames.Ship(state.Frame) is not { } ship || !ship.TryPose(zone.Tick, out ShipPose pose))
        {
            return state.Velocity;
        }
        return pose.DirectionToWorld(state.Velocity) + pose.VelocityAt(pose.ToWorld(state.Position));
    }

    /// <summary>A character's view in the zone's frame: its input's, turned out of the frame it was given in.</summary>
    public static Vector3 ViewOf(this Zone zone, in Character c) => zone.Frames.DirectionToWorld(c.Input.View, c.Input.Frame, zone.Tick);

    /// <summary>
    /// A frame's pose as a player saw it at a moment (IRON_WAKE.md §4.9): a ship's where the pose history had it then, or
    /// now; false for the zone's frame. A swing, an arrow or a shot from aboard leaves from there, where the player saw
    /// both their ship and whatever they aimed at.
    /// </summary>
    public static bool SeenPose(this Zone zone, ulong frame, double viewTick, out ShipPose pose)
    {
        pose = default;
        return frame != 0 && zone.Frames.Ship(frame) is { } ship && ShipQueries.PoseFor(ship, zone.Tick, zone.History, viewTick, out pose);
    }

    /// <summary>
    /// The line a character attacks along (a swing, an arrow, a throw), in the zone's frame: its eye and its view, held a
    /// degree off straight up and down; aboard, as it saw its ship at <paramref name="viewTick"/> (IRON_WAKE.md §4.9), with
    /// the velocity the ship gave its eye then (a projectile keeps it, §4.9), none ashore.
    /// </summary>
    public static (Vector3 Eye, Vector3 Direction, Vector3 Carried) AttackLine(this Zone zone, in Character c, double viewTick)
    {
        Vector3 view = zone.Frames.ViewIn(c.Input.View, c.Input.Frame, c.State.Frame, zone.Tick);
        if (c.State.Frame == 0)
        {
            return (zone.EyeOf(c.State), CharacterMotor.SafeView(view, zone.Frame.UpAt(c.State.Position)), Vector3.Zero);
        }
        Vector3 eye = c.State.Position + Vector3.UnitY * zone.Tuning.EyeHeight(c.State.Stance);
        Vector3 direction = CharacterMotor.SafeView(view, Vector3.UnitY);
        if (!zone.SeenPose(c.State.Frame, viewTick, out ShipPose pose))
        {
            return (zone.EyeOf(c.State), zone.Frames.DirectionToWorld(direction, c.State.Frame, zone.Tick), Vector3.Zero);
        }
        Vector3 world = pose.ToWorld(eye);
        return (world, pose.DirectionToWorld(direction), pose.VelocityAt(world));
    }

    /// <summary>Takes a character off its ship into the zone's frame where it is, with the ship's velocity there (§4.5).</summary>
    public static void ToZoneFrame(this Zone zone, ref CharacterState state)
    {
        if (state.Frame != 0 && zone.Frames.Ship(state.Frame) is { } ship && ship.TryPose(zone.Tick, out ShipPose pose))
        {
            Vector3 world = pose.ToWorld(state.Position);
            state.Velocity = pose.DirectionToWorld(state.Velocity) + pose.VelocityAt(world);
            state.Position = world;
        }
        state.Frame = 0;
    }
}
