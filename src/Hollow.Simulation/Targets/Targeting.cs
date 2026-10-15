using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Targets;

/// <summary>
/// The ray the interact key looks along (GROUND_SYSTEMS.md §5): from the eye, along the view, as far as the farthest verb
/// reaches, past the one looking (<see cref="Ignore"/>: their own character's ID). A family's members are tested against
/// it by their aim shapes; the nearest hit of all families wins.
/// </summary>
public readonly record struct AimRay(Vector3 Origin, Vector3 Direction, float Length, ulong Ignore = 0)
{
    /// <summary>How far along a hit must be to beat the best so far.</summary>
    public float Upto(in AimHit best) => best.Hit ? MathF.Min(Length, best.Distance) : Length;

    /// <summary>A hit <paramref name="t"/> along the ray (negative: none) replaces the best so far when it is nearer.</summary>
    public void Consider(ref AimHit best, float t, in TargetRef target, AimKind kind = AimKind.Thing)
    {
        if (t >= 0 && t <= Length && (!best.Hit || t < best.Distance))
        {
            best = new AimHit(target, t, Origin + Direction * t, kind);
        }
    }
}

/// <summary>What a hit is to the view along the interact key's ray.</summary>
public enum AimKind : byte
{
    /// <summary>Something E may act on: the view ends there.</summary>
    Thing,

    /// <summary>What only stops the view (the ground, the building's pieces, trees and rocks, animals): it offers nothing.</summary>
    Backdrop,

    /// <summary>An open door's opening: the door, unless the view through it meets something beyond to act on.</summary>
    Opening,
}

/// <summary>What the interact key's ray met first: the thing (its family, ID and part), how far along, where, and what it is to the view.</summary>
public readonly record struct AimHit(TargetRef Target, float Distance, Vector3 Point, AimKind Kind = AimKind.Thing)
{
    public static readonly AimHit None = new(TargetRef.None, -1, default);

    public bool Hit => Distance >= 0;
}

/// <summary>
/// What the interaction rules know of a target (GROUND_SYSTEMS.md §5), the same on the server (from the zone's own
/// objects) and on a client (from what it was told): its capabilities here (a part may have its own: a ship's cargo
/// behind its hatch), what it tells the viewer (§3.3), its name for the prompt, and the few facts its options turn on.
/// </summary>
public struct TargetFacts
{
    /// <summary>Its capabilities now, at this part.</summary>
    public CapabilitySet Capabilities;

    /// <summary>What it tells this viewer (GROUND_SYSTEMS.md §3.3).</summary>
    public ViewerFlags Flags;

    /// <summary>How the prompt names it ("Wooden door", "Ada's body").</summary>
    public string Name;

    /// <summary>The stacks a bag of dropped items holds (one is picked up whole at a tap); 0 for anything else.</summary>
    public int Count;

    /// <summary>A person's (a sleeper, a player down, a body): looted, where anything else that holds items is opened.</summary>
    public bool Body;

    /// <summary>Taking from it leaves it where it is (a berry bush is picked bare).</summary>
    public bool Stays;

    /// <summary>A door or a hatch open.</summary>
    public bool Open;

    /// <summary>A ship's seat taken.</summary>
    public bool Busy;

    /// <summary>A ship at rest (its hatch opens and its cargo is reached only then).</summary>
    public bool Still;

    /// <summary>A door's lock.</summary>
    public LockKind Lock;

    /// <summary>The deployable it is, if one (a workbench's level, a machine's kind).</summary>
    public DeployableDef? Deployable;

    /// <summary>Where it stands, in the zone frame (the building rights there).</summary>
    public Vector3 Position;
}

/// <summary>
/// One family of world things as the interact key sees it (GROUND_SYSTEMS.md §5): where a member (or its part) is, its
/// nearest surface (reach is measured to it, §3.4), its members along a ray, and what the interaction rules know of one.
/// The server's families answer from the zone's stores (<see cref="ITargetFamily"/>), a client's from its replicas; both
/// use the same aim shapes (<see cref="AimShapes"/>), so they aim at the same thing.
/// </summary>
public interface ITargetView
{
    TargetFamily Family { get; }

    /// <summary>Where the thing, or its part (<see cref="TargetRef.Part"/>), is now.</summary>
    bool TryPose(in TargetRef target, out TargetPose pose);

    /// <summary>The nearest point of the thing's bounds (or its part's) to a point.</summary>
    bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point);

    /// <summary>Its members along the interact key's ray: the nearest, if nearer than <paramref name="best"/>, replaces it.</summary>
    void Aim(in AimRay ray, ref AimHit best);

    /// <summary>What the interaction rules know of a member for a viewer; false when it doesn't exist.</summary>
    bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts);
}

/// <summary>The nearest points of a door's leaves (boxes hanging from their hinges, as they swing), the same on both sides.</summary>
public static class DoorShapes
{
    /// <summary>The nearest point of a door's leaves to a point, and that leaf's centre.</summary>
    public static (Vector3 Point, Vector3 Centre) Nearest(Door door, Vector3 at)
    {
        (Vector3 Point, Vector3 Centre) best = default;
        float nearest = float.MaxValue;
        for (int i = 0; i < door.Def.Leaves.Count; i++)
        {
            LeafDef leaf = door.Def.Leaves[i];
            (Vector3 position, Quaternion rotation) = door.Leaf(i, door.Open);
            Vector3 local = Vector3.Transform(at - position, Quaternion.Inverse(rotation));
            Vector3 size = leaf.Size;
            var min = new Vector3(MathF.Min(0, size.X), 0, -MathF.Abs(size.Z) / 2);
            var max = new Vector3(MathF.Max(0, size.X), size.Y, MathF.Abs(size.Z) / 2);
            Vector3 clamped = Vector3.Clamp(local, min, max);
            Vector3 point = position + Vector3.Transform(clamped, rotation);
            float d = Vector3.DistanceSquared(point, at);
            if (d < nearest)
            {
                nearest = d;
                best = (point, position + Vector3.Transform((min + max) / 2, rotation));
            }
        }
        return best;
    }

    public static Vector3 NearestOnLeaves(Door door, Vector3 at) => Nearest(door, at).Point;
}

/// <summary>The nearest point of a ship's hull (its boxes), the same on both sides.</summary>
public static class ShipShapes
{
    /// <summary>The point of a ship's hull nearest to a point, the ship's centre of mass and turn given.</summary>
    public static Vector3 NearestOnHull(Ships.ShipDef def, Vector3 centre, Quaternion rotation, Vector3 at)
    {
        Vector3 local = Vector3.Transform(at - centre, Quaternion.Conjugate(rotation)) + def.CentreOfMass;
        Vector3 best = local;
        float nearest = float.MaxValue;
        foreach (Ships.HullBox box in def.Hull)
        {
            Vector3 p = Vector3.Clamp(local, box.Centre - box.HalfExtents, box.Centre + box.HalfExtents);
            float d = Vector3.DistanceSquared(p, local);
            if (d < nearest)
            {
                nearest = d;
                best = p;
            }
        }
        return centre + Vector3.Transform(best - def.CentreOfMass, rotation);
    }
}

/// <summary>
/// Natural objects along the interact key's ray, the same on both sides (each generates them from the surface): a pickup
/// or bush by a ball a little bigger than it, a tree by its trunk, a rock by its sphere; what was used up isn't there.
/// </summary>
public static class NaturalAim
{
    public static void Aim(Nature.INaturalObjects nature, Nature.NaturalState state, GameContent content, in AimRay ray, ref AimHit best,
        List<Nature.NaturalObject> scratch)
    {
        float length = ray.Upto(best);
        Vector3 end = ray.Origin + ray.Direction * length;
        const float margin = 4f;
        scratch.Clear();
        nature.Query((ray.Origin + end) / 2, length / 2 + margin, scratch);
        foreach (Nature.NaturalObject o in scratch)
        {
            if (content.Natural(o.Kind) is not { } kind || state.IsDepleted(o.Id))
            {
                continue;
            }
            // A pickup or bush is taken with E (a little bigger than it is); a tree or a rock only stops the view.
            Combat.HitShape shape = Nature.NaturalShapes.Bounds(o, kind);
            (float grow, AimKind aim) = kind.IsPicked ? (AimShapes.PickupMargin, AimKind.Thing) : (0f, AimKind.Backdrop);
            ray.Consider(ref best, shape.Raycast(ray.Origin, ray.Direction, length, grow), new TargetRef(TargetFamily.Natural, o.Id), aim);
        }
    }
}

/// <summary>
/// The building along the interact key's ray, the same on both sides (the client's mirror holds the server's bodies): one
/// query over every building body (pieces, doors' leaves, the tool cupboard), named by what owns the body (a piece only
/// stops the view); and doorways' openings, which name their doors (an open one hides nothing behind it, §5).
/// </summary>
public static class BuildingAim
{
    public static void Bodies(BuildingWorld building, in AimRay ray, ref AimHit best)
    {
        if (building.Physics is { } physics && physics.Ray(ray.Origin, ray.Direction, ray.Upto(best), out RayHit hit, buildingOnly: true) &&
            physics.OwnerOf(hit.Body) is ITargetable owner)
        {
            ray.Consider(ref best, hit.Distance, owner.Ref, owner is Piece ? AimKind.Backdrop : AimKind.Thing);
        }
    }

    public static void Openings(BuildingWorld building, in AimRay ray, ref AimHit best, List<Piece> scratch)
    {
        float length = ray.Upto(best);
        building.PiecesNear(ray.Origin, ray.Origin + ray.Direction * length, 3.5f, scratch);
        var within = ray with { Length = length };
        foreach (Piece piece in scratch)
        {
            if (piece.Door is { Gone: false } door)
            {
                ray.Consider(ref best, AimShapes.Doorway(within, building.Data, piece), door.Ref, door.Open ? AimKind.Opening : AimKind.Thing);
            }
        }
    }
}

/// <summary>A ship's parts along the interact key's ray (GROUND_SYSTEMS.md §5), the same on both sides: the hatch, the fuel cap, and the cargo while the hatch is open.</summary>
public static class ShipAim
{
    public static void Parts(ulong id, Ships.ShipDef def, Vector3 centre, Quaternion rotation, bool hatchOpen, in AimRay ray, ref AimHit best)
    {
        if (Vector3.DistanceSquared(centre, ray.Origin) > (def.BoundingRadius + ray.Length + 1) * (def.BoundingRadius + ray.Length + 1))
        {
            return;
        }
        for (byte part = Ships.ShipParts.Hatch; part < Ships.ShipParts.Count; part++)
        {
            if (part == Ships.ShipParts.Cargo && (!hatchOpen || def.CargoSlots == 0))
            {
                continue;
            }
            Vector3 point = centre + Vector3.Transform(def.PartPoint(part) - def.CentreOfMass, rotation);
            ray.Consider(ref best, AimShapes.Sphere(ray, point, Ships.ShipParts.Radius(part)), new TargetRef(TargetFamily.Ship, id, part));
        }
    }
}

/// <summary>
/// The one targeting query (GROUND_SYSTEMS.md §5): every registered family is asked for its nearest member along the
/// ray, and the nearest of all is what the interact key acts on, not the first of a list in some order. What blocks the
/// view (the ground, a wall, a tree) is a family's member too, so nothing is reached through it. An open doorway hides
/// nothing: the view goes on through it, and its door is what E meets there only when nothing beyond it is to act on (a
/// box just inside, a player down in the doorway are aimed at through it; the far wall, the floor or nothing is not).
/// </summary>
public static class Targeting
{
    // How far past an opening the view goes on from (clear of its plane).
    private const float PastOpening = 0.01f;

    public static AimHit Aim(IReadOnlyList<ITargetView> families, in AimRay ray)
    {
        AimHit best = Nearest(families, ray);
        if (best.Kind != AimKind.Opening || ray.Length - best.Distance <= PastOpening)
        {
            return best;
        }
        var beyond = ray with { Origin = best.Point + ray.Direction * PastOpening, Length = ray.Length - best.Distance - PastOpening };
        AimHit next = Nearest(families, beyond);
        return next is { Hit: true, Kind: AimKind.Thing } ? next with { Distance = next.Distance + best.Distance + PastOpening } : best;
    }

    private static AimHit Nearest(IReadOnlyList<ITargetView> families, in AimRay ray)
    {
        AimHit best = AimHit.None;
        for (int i = 0; i < families.Count; i++)
        {
            families[i].Aim(ray, ref best);
        }
        return best;
    }
}

/// <summary>
/// The aim shapes and nearest points every family's members share on both sides (GROUND_SYSTEMS.md §5): a box for a
/// deployable, a sphere for a bag or a body, a capsule for an arrow's shaft, a doorway's opening, the parts of a ship.
/// </summary>
public static class AimShapes
{
    /// <summary>The margin round a deployable's box the aim forgives.</summary>
    public const float BoxMargin = 0.1f;

    /// <summary>An arrow's shaft, as the aim and the reach see it.</summary>
    public const float ArrowRadius = 0.06f;

    /// <summary>A bag of dropped items: a sphere this far above its base, this big.</summary>
    public const float BagLift = 0.15f, BagRadius = 0.35f;

    /// <summary>A body on the ground: lower and wider.</summary>
    public const float BodyLift = 0.25f, BodyRadius = 0.6f;

    /// <summary>A dud or a charge: a sphere round it.</summary>
    public const float ExplosiveRadius = 0.35f;

    /// <summary>A natural pickup or bush: its kind's radius and this much more.</summary>
    public const float PickupMargin = 0.2f;

    /// <summary>A character or an animal: its hit shapes, this much thicker.</summary>
    public const float BodyMargin = 0.1f;

    // A bag of dropped items and a body lie as balls: this far above their base, this big, by their kind (StaticKind's
    // order: a deployable and a prop are boxes).
    private static readonly (float Lift, float Radius)[] DroppedBalls = { (0, 0), (BagLift, BagRadius), (BodyLift, BodyRadius), (0, 0) };

    /// <summary>A bag's or a body's ball (its kind's lift and size) along a ray: the distance, or −1.</summary>
    public static float Dropped(in AimRay ray, Vector3 position, Vector3 up, World.StaticKind kind)
    {
        (float lift, float radius) = (int)kind < DroppedBalls.Length ? DroppedBalls[(int)kind] : (0, 0);
        return radius > 0 ? Sphere(ray, position + up * lift, radius) : -1;
    }

    /// <summary>The nearest point of a bag's or a body's ball to a point.</summary>
    public static Vector3 NearestOnDropped(Vector3 position, Vector3 up, World.StaticKind kind, Vector3 from)
    {
        (float lift, float radius) = (int)kind < DroppedBalls.Length ? DroppedBalls[(int)kind] : (0, 0);
        return NearestOnSphere(position + up * lift, radius, from);
    }

    /// <summary>A deployable's box (base on the ground, half extents) along a ray: the distance, or −1.</summary>
    public static float Box(in AimRay ray, Vector3 position, Vector3 up, float yaw, Vector3 halfExtents)
    {
        (Vector3 right, Vector3 forward) = Combat.Geometry.Axes(up, yaw);
        return Combat.Geometry.RayBox(ray.Origin, ray.Direction, ray.Length, position + up * halfExtents.Y, halfExtents + new Vector3(BoxMargin),
            right, up, forward);
    }

    /// <summary>A sphere along a ray: the distance, or −1.</summary>
    public static float Sphere(in AimRay ray, Vector3 centre, float radius) =>
        Combat.Geometry.RaySphere(ray.Origin, ray.Direction, ray.Length, centre, radius);

    /// <summary>A capsule along a ray: the distance, or −1.</summary>
    public static float Capsule(in AimRay ray, Vector3 a, Vector3 b, float radius) =>
        Combat.Geometry.RayCapsule(ray.Origin, ray.Direction, ray.Length, a, b, radius);

    /// <summary>The nearest point of a sphere's surface to a point (the point itself inside it).</summary>
    public static Vector3 NearestOnSphere(Vector3 centre, float radius, Vector3 from)
    {
        Vector3 off = from - centre;
        float d = off.Length();
        return d <= radius ? from : centre + off * (radius / d);
    }

    /// <summary>The nearest point of a capsule's surface to a point.</summary>
    public static Vector3 NearestOnCapsule(Vector3 a, Vector3 b, float radius, Vector3 from) => new Combat.HitShape(a, b, radius, HitRegion.Body).Nearest(from);

    /// <summary>
    /// A frame piece's opening (a doorway's or wall frame's, building.json's sizes) along a ray: where the ray crosses
    /// its plane inside it, or −1. A door's target includes its opening, so a door whose leaves have swung or rolled out
    /// of the ray is still aimed at.
    /// </summary>
    public static float Doorway(in AimRay ray, BuildingData data, Piece frame)
    {
        if (frame.Def.Opening is not (Building.Opening.Door or Building.Opening.Double))
        {
            return -1;
        }
        (double w, double h) = frame.Def.Opening == Building.Opening.Door ? data.DoorOpening : data.DoubleOpening;
        BuildingFrame f = frame.Structure.Frame;
        Vector3 end = ray.Origin + ray.Direction * ray.Length;
        Core.Vector3D eye = new(ray.Origin.X, ray.Origin.Y, ray.Origin.Z), far = new(end.X, end.Y, end.Z);
        if (!PieceGeometry.RayOpening(frame.Pose, w, h, f.ToLocal(eye), f.ToLocal(far), out Core.Vector3D hit))
        {
            return -1;
        }
        return (float)(f.ToZone(hit) - eye).Length;
    }

    /// <summary>The nearest point of a frame piece's opening (the rectangle in its plane) to a point.</summary>
    public static bool NearestInDoorway(BuildingData data, Piece frame, Vector3 from, out Vector3 point)
    {
        point = from;
        if (frame.Def.Opening is not (Building.Opening.Door or Building.Opening.Double))
        {
            return false;
        }
        (double w, double h) = frame.Def.Opening == Building.Opening.Door ? data.DoorOpening : data.DoubleOpening;
        BuildingFrame f = frame.Structure.Frame;
        Core.Vector3D local = PieceGeometry.ToLocal(frame.Pose, f.ToLocal(new Core.Vector3D(from.X, from.Y, from.Z)));
        var clamped = new Core.Vector3D(Math.Clamp(local.X, -w / 2, w / 2), Math.Clamp(local.Y, 0, h), 0);
        Core.Vector3D zone = f.ToZone(PieceGeometry.ToFrame(frame.Pose, clamped));
        point = new Vector3((float)zone.X, (float)zone.Y, (float)zone.Z);
        return true;
    }
}
