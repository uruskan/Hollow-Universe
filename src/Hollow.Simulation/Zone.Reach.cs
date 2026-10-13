using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

// Reach and sight (GROUND_SYSTEMS.md §3.4): one rule for every action on a thing. The distance runs from the eye to the
// thing's nearest surface (its family's), within a reach from data (survival.json's reach); one ray from the eye to just
// in front of that point must meet nothing of the building but the thing itself, nor the ground short of the thing. It
// replaces the slack constants each action had (+0.3, +0.5, +1.5, +3, +4 to centres, middles and feet).
public sealed partial class Zone
{
    /// <summary>
    /// A line of sight ends this far in front of the thing's nearest point: the eye must see the air before it. What the
    /// thing touches there (a door's jamb beside its leaf, the floor under a box) doesn't hide it, while a wall it is
    /// against from the far side (0.3 m thick) does.
    /// </summary>
    private const float SightShortBy = 0.1f;

    /// <summary>The ground this close to the thing doesn't hide it: what lies on the ground meets it there.</summary>
    private const float SightGroundAtThing = 0.25f;

    /// <summary>
    /// The one reach rule (GROUND_SYSTEMS.md §3.4): whether the eye reaches a thing: its nearest surface within
    /// <paramref name="reach"/> metres, and (unless <paramref name="sight"/> is false: a workbench's level, an open
    /// container staying open) in sight. Done, or NotFound, OutOfReach or OutOfSight.
    /// </summary>
    public Outcome Reach(Vector3 eye, in TargetRef target, float reach, bool sight = true)
    {
        if (!TryNearest(target, eye, out Vector3 point))
        {
            return OutcomeCode.NotFound;
        }
        if (Vector3.DistanceSquared(eye, point) > reach * reach)
        {
            return OutcomeCode.OutOfReach;
        }
        return !sight || InSight(eye, point, target) ? Outcome.Done : OutcomeCode.OutOfSight;
    }

    /// <summary>
    /// Whether the eye sees a thing at one of its points: from the eye to just in front of the point, no building body but
    /// the thing's own (by its ID: the claim device's body is its box's too), and no ground short of the thing.
    /// </summary>
    public bool InSight(Vector3 eye, Vector3 point, in TargetRef target)
    {
        Vector3 d = point - eye;
        float distance = d.Length(), length = distance - SightShortBy;
        if (length <= 0)
        {
            return true;
        }
        Vector3 direction = d / distance;
        if (Building?.Physics is { } physics && physics.Ray(eye, direction, length, out RayHit hit, buildingOnly: true) &&
            (physics.OwnerOf(hit.Body) is not ITargetable owner || owner.Ref.Id != target.Id))
        {
            return false;
        }
        float ground = GroundCrossing(eye, direction, length);
        return ground < 0 || ground > distance - SightGroundAtThing;
    }
}
