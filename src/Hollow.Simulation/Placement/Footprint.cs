using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation.Placement;

/// <summary>
/// What a placement occupies (GROUND_SYSTEMS.md §6's footprint): a box (a deployable's or the claim device's size, or half
/// extents a rule gives), a ship's hull (its boxes from ships.json, which come from its model), or a point (pieces, doors
/// and locks are the building's to check; a charge is a point on a surface). Each says where its nearest point to the eye
/// is, where its corners stand, and whether the space it needs is clear: one kind of footprint, one way.
/// </summary>
public abstract class Footprint
{
    /// <summary>Nothing to occupy: a point.</summary>
    public static readonly Footprint Point = new PointFootprint();

    /// <summary>Half its size, for the ghost when it has no model (zero for a point).</summary>
    public abstract Vector3 HalfExtents { get; }

    /// <summary>The nearest point of it, where a candidate puts it, to the eye.</summary>
    public abstract Vector3 NearestTo(PlacementCandidate c, Vector3 eye);

    /// <summary>The corners that must stand on something (RustMVP's four), on the plane of the surface under it; how many.</summary>
    public abstract int Corners(PlacementCandidate c, Span<Vector3> into);

    /// <summary>Whether the space it needs where a candidate puts it is clear of everything but what it stands on.</summary>
    public abstract Outcome Clear(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c);

    /// <summary>A point on the plane of the surface under a candidate, <paramref name="offset"/> across from its base.</summary>
    protected static Vector3 OnSupport(PlacementCandidate c, Vector3 offset)
    {
        float up = Vector3.Dot(c.Normal, c.Up);
        float lift = up > 1e-3f ? -Vector3.Dot(offset, c.Normal) / up : 0;
        return c.Position + offset + c.Up * lift;
    }

    private sealed class PointFootprint : Footprint
    {
        public override Vector3 HalfExtents => Vector3.Zero;

        public override Vector3 NearestTo(PlacementCandidate c, Vector3 eye) => c.Position;

        public override int Corners(PlacementCandidate c, Span<Vector3> into) => 0;

        public override Outcome Clear(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) => Outcome.Done;
    }
}

/// <summary>A box standing on its base along up, turned by its yaw: a deployable, the claim device.</summary>
public sealed class BoxFootprint(Vector3 half) : Footprint
{
    /// <summary>A box clears what it stands on by this much (RustMVP's 2 cm), so touching a floor isn't overlapping it.</summary>
    private const float Lift = 0.02f;

    private readonly List<Capsule> _characters = new();

    public override Vector3 HalfExtents { get; } = half;

    public override Vector3 NearestTo(PlacementCandidate c, Vector3 eye) => Geometry.NearestOnBox(c.Position, c.Up, c.Yaw, HalfExtents, eye);

    public override int Corners(PlacementCandidate c, Span<Vector3> into)
    {
        (Vector3 right, Vector3 forward) = Geometry.Axes(c.Up, c.Yaw);
        int n = 0;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                into[n++] = OnSupport(c, right * (x * HalfExtents.X) + forward * (z * HalfExtents.Z));
            }
        }
        return n;
    }

    public override Outcome Clear(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
    {
        Vector3 lifted = c.Position + c.Up * Lift;
        // Aboard a ship (IRON_WAKE.md §8): its space's bodies, the ship's pieces, doors and solid things aboard, in its frame.
        // Else the zone's bodies: pieces, doors, the claim device, trees and rocks, solid deployables, parked ships (the
        // ground isn't: the slope is its check).
        if (c.Aboard != null ? Aboard.Meets(c, HalfExtents, Lift) : world.Overlaps(this, HalfExtents, lifted, Geometry.Upright(c.Up, c.Yaw)).Count > 0)
        {
            return OutcomeCode.Blocked;
        }
        // Things without bodies (a sleeping bag, a campfire) by their boxes.
        Vector3 centre = lifted + c.Up * HalfExtents.Y;
        float reach = HalfExtents.Length() + 2.5f;
        foreach (PlacedThing thing in world.ThingsNear(c.Position, reach))
        {
            if (thing.Ref != c.Support.Whole &&
                Geometry.BoxesOverlap(centre, HalfExtents, c.Yaw, thing.Centre, thing.Def.HalfExtents, thing.Yaw, c.Up))
            {
                return OutcomeCode.Blocked;
            }
        }
        _characters.Clear();
        world.Around.CharactersNear(c.Position, reach, _characters);
        foreach (Capsule character in _characters)
        {
            if (Geometry.BoxCylinderOverlap(centre, HalfExtents, c.Yaw, character.Feet, character.Radius, character.Height, c.Up))
            {
                return OutcomeCode.PlayerInTheWay;
            }
        }
        return Outcome.Done;
    }
}

/// <summary>A ship's hull (its boxes, from its model by way of ships.json), standing on its skids.</summary>
public sealed class HullFootprint(ShipDef def) : Footprint
{
    /// <summary>The ground may poke into the hull this deep on bumpy ground; anything else blocks.</summary>
    public const float GroundDepth = 0.25f;

    private readonly List<Capsule> _characters = new();
    private readonly List<PlacedShip> _ships = new();

    public ShipDef Def { get; } = def;

    public override Vector3 HalfExtents { get; } = HalfOf(def);

    public override Vector3 NearestTo(PlacementCandidate c, Vector3 eye)
    {
        ShipState standing = ShipMotor.Standing(Def, c.Position, c.Up, c.Yaw);
        Vector3 best = c.Position;
        float nearest = float.MaxValue;
        foreach (HullBox box in Def.Hull)
        {
            Vector3 centre = standing.Position + Vector3.Transform(box.Centre - Def.CentreOfMass, standing.Rotation);
            Vector3 local = Vector3.Transform(eye - centre, Quaternion.Conjugate(standing.Rotation));
            Vector3 clamped = Vector3.Clamp(local, -box.HalfExtents, box.HalfExtents);
            Vector3 point = centre + Vector3.Transform(clamped, standing.Rotation);
            float d = Vector3.DistanceSquared(point, eye);
            if (d < nearest)
            {
                (nearest, best) = (d, point);
            }
        }
        return best;
    }

    public override int Corners(PlacementCandidate c, Span<Vector3> into)
    {
        Quaternion rotation = ShipMotor.Orientation(c.Up, c.Yaw);
        int n = 0;
        foreach (Vector3 skid in Def.SkidPoints)
        {
            if (n < into.Length)
            {
                Vector3 offset = Vector3.Transform(skid, rotation);
                into[n++] = OnSupport(c, offset - c.Up * Vector3.Dot(offset, c.Up));
            }
        }
        return n;
    }

    public override Outcome Clear(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
        Clear(world, Def, c.Position, c.Yaw, _characters, _ships);

    /// <summary>
    /// Whether a ship would stand on the ground at a point, its nose toward a yaw, without overlapping anything but the
    /// ground (a little), a character or another ship: placing one, and a vehicle workbench rolling one out onto its bay.
    /// </summary>
    public static Outcome Clear(PlacementWorld world, ShipDef def, Vector3 ground, float yaw, List<Capsule> characters, List<PlacedShip> ships)
    {
        Vector3 up = world.Frame.UpAt(ground);
        ShipState probe = ShipMotor.Standing(def, ground + up * 0.05f, up, yaw);
        if (world.Ships is { } collider && world.Physics is { } physics)
        {
            Span<ShipContact> contacts = stackalloc ShipContact[4];
            int found;
            lock (JoltRuntime.Gate)
            {
                physics.EnsureGround(ground);
                found = collider.Overlaps(def, probe.Position, probe.Rotation, ShipCollider.NoBody, contacts);
            }
            for (int i = 0; i < found; i++)
            {
                if (!PhysicsWorld.IsGroundBody(contacts[i].Body) || contacts[i].Depth > GroundDepth)
                {
                    return OutcomeCode.Blocked;
                }
            }
        }
        characters.Clear();
        world.Around.CharactersNear(probe.Position, def.BoundingRadius + 0.5f, characters);
        foreach (Capsule character in characters)
        {
            if (Inside(def, probe.Position, probe.Rotation, character.Feet + up * 0.9f, 0.4f))
            {
                return OutcomeCode.PlayerInTheWay;
            }
        }
        ships.Clear();
        world.Around.ShipsNear(probe.Position, def.BoundingRadius * 2 + 30, ships);
        foreach (PlacedShip other in ships)
        {
            if (Vector3.Distance(other.Position, probe.Position) < other.Def.BoundingRadius + def.BoundingRadius - 0.5f)
            {
                return OutcomeCode.Blocked;
            }
        }
        return Outcome.Done;
    }

    /// <summary>Whether a point (with a margin) is inside one of a ship's hull boxes, the ship's centre of mass at a pose.</summary>
    public static bool Inside(ShipDef def, Vector3 position, Quaternion rotation, Vector3 point, float margin)
    {
        Vector3 local = Vector3.Transform(point - position, Quaternion.Conjugate(rotation)) + def.CentreOfMass;
        foreach (HullBox box in def.Hull)
        {
            Vector3 d = Vector3.Abs(local - box.Centre) - box.HalfExtents - new Vector3(margin);
            if (d.X <= 0 && d.Y <= 0 && d.Z <= 0)
            {
                return true;
            }
        }
        return false;
    }

    // The hull's half size about the model's pivot (the ghost's box when there is no model).
    private static Vector3 HalfOf(ShipDef def)
    {
        Vector3 most = Vector3.Zero;
        foreach (HullBox box in def.Hull)
        {
            most = Vector3.Max(most, Vector3.Abs(box.Centre) + box.HalfExtents);
        }
        return most;
    }
}
