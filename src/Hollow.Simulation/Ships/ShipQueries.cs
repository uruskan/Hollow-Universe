using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;

namespace Hollow.Simulation.Ships;

/// <summary>What a query of the zone's frame met in a ship's space: the ship's frame, the hit there, and how far along the query it is.</summary>
public readonly record struct ShipQueryHit(IShipFrame Ship, SpaceHit Hit, float Distance)
{
    /// <summary>The ship's key (its ID on the server, its net ID on a client).</summary>
    public ulong Key => Ship.Key;
}

/// <summary>
/// The queries of the zone's frame through ships' frames (IRON_WAKE.md §4.9), in one place so none reimplements them:
/// a segment or a sphere enters every ship space it crosses, tested first against the space's bounding sphere, then
/// turned into the ship's frame and met against its pieces. The aim (the families' backdrops and parts), reach and sight,
/// the damage pipeline's traces (bullets, arrows, swings) and a blast's reach and line of sight all ask here. A late
/// shot passes the moment it was seen at: each ship is then where the pose history had it (the server's), else where it
/// is now. On a client the same queries run against the spaces it keeps near the player.
/// </summary>
public static class ShipQueries
{
    /// <summary>A ship's pose for a query: at the moment the history kept for <paramref name="viewTick"/>, else now.</summary>
    public static bool PoseFor(IShipFrame ship, long tick, HitboxHistory? history, double viewTick, out ShipPose pose)
    {
        if (history != null && viewTick < tick && history.SampleShip(ship.Key, viewTick, out pose))
        {
            return true;
        }
        return ship.TryPose(tick, out pose);
    }

    /// <summary>
    /// The nearest piece of any ship's space a segment of the zone's frame meets (from <paramref name="origin"/> along the
    /// unit <paramref name="direction"/> for <paramref name="length"/>), but <paramref name="ignore"/>'s (its key; 0 none).
    /// </summary>
    public static bool Ray(IReadOnlyList<IShipFrame> ships, Vector3 origin, Vector3 direction, float length, long tick, out ShipQueryHit hit,
        HitboxHistory? history = null, double viewTick = double.MaxValue, ulong ignore = 0)
    {
        hit = default;
        bool found = false;
        float best = length;
        for (int i = 0; i < ships.Count; i++)
        {
            IShipFrame ship = ships[i];
            if (ship.Key == ignore || !PoseFor(ship, tick, history, viewTick, out ShipPose pose) ||
                Geometry.RaySphere(origin, direction, best, pose.Position, ship.Interior.Reach) < 0)
            {
                continue;
            }
            bool met;
            SpaceHit local;
            lock (JoltRuntime.Gate)
            {
                met = ship.Space.Ray(pose.ToLocal(origin), pose.DirectionToLocal(direction), best, out local);
            }
            if (met && local.Distance < best)
            {
                best = local.Distance;
                hit = new ShipQueryHit(ship, local, local.Distance);
                found = true;
            }
        }
        return found;
    }

    /// <summary>Whether a segment of the zone's frame between two points meets no ship's piece (but <paramref name="ignore"/>'s).</summary>
    public static bool Clear(IReadOnlyList<IShipFrame> ships, Vector3 from, Vector3 to, long tick, ulong ignore = 0)
    {
        Vector3 d = to - from;
        float length = d.Length();
        return length < 1e-4f || !Ray(ships, from, d / length, length, tick, out _, ignore: ignore);
    }

    /// <summary>
    /// The nearest point of a ship's pieces to a point of the zone's frame within <paramref name="radius"/>, in the zone's
    /// frame, with the part and the distance (0 inside a piece). False when nothing of it is that near.
    /// </summary>
    public static bool Nearest(IShipFrame ship, in ShipPose pose, Vector3 point, float radius, out Vector3 nearest, out int part, out float distance)
    {
        nearest = point;
        part = 0;
        distance = float.MaxValue;
        if (Vector3.Distance(point, pose.Position) > radius + ship.Interior.Reach)
        {
            return false;
        }
        bool found;
        Vector3 local;
        lock (JoltRuntime.Gate)
        {
            found = ship.Space.Nearest(pose.ToLocal(point), radius, out local, out part, out distance);
        }
        if (found)
        {
            nearest = pose.ToWorld(local);
        }
        return found;
    }
}
