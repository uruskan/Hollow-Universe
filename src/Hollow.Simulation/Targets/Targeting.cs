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
    /// <summary>When set, preview a damage query rather than an interaction: solid objects, with this attack radius.</summary>
    public float? AttackRadius { get; init; }
    /// <summary>Ship excluded when resolving a mounted weapon's aim point; its muzzle obstruction is checked separately.</summary>
    public ulong IgnoreShip { get; init; }
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

    /// <summary>A door open, or a ship's door opening or open.</summary>
    public bool Open;

    /// <summary>A ship's seat taken.</summary>
    public bool Busy;

    /// <summary>A ship at rest (its hatch opens and its cargo is reached only then).</summary>
    public bool Still;

    /// <summary>A ship's part (IRON_WAKE.md §5.1): the ship's kind and the part (its seat, its door), or null.</summary>
    public Ships.ShipDef? Ship;

    public byte ShipPart;

    /// <summary>Whether the part's door may open now by its rule (the server's word on the ship's motion), else why not.</summary>
    public OutcomeCode DoorRule;

    /// <summary>The door a part's seat is reached through open at least half way (the Tin Finch's hatch).</summary>
    public bool DoorReady;

    /// <summary>The part's door broken, jammed open; the part itself broken (a station disabled).</summary>
    public bool Jammed;

    public bool Broken;

    /// <summary>A ship's part's health as a share of its most (its door's, its own), or −1 for none.</summary>
    public float Health;

    /// <summary>Health for common inspection feedback; null for targets that do not advertise damage.</summary>
    public TargetVitality? Vitality;

    /// <summary>A door's lock.</summary>
    public LockKind Lock;

    /// <summary>The deployable it is, if one (a workbench's level, a machine's kind).</summary>
    public DeployableDef? Deployable;
    public Authorization.DeviceAccessFacts DeviceAccess;

    /// <summary>
    /// Built or lying aboard a ship (IRON_WAKE.md §8): its crew's to pick up, not a building claim's; the viewer's flags
    /// then say <see cref="ViewerFlags.Authorised"/> when they are on the ship's list.
    /// </summary>
    public bool Aboard;

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
            (Vector3 position, Quaternion rotation) = door.LeafNow(i);
            Vector3 local = Vector3.Transform(at - position, Quaternion.Inverse(rotation));
            foreach (ConvexPart part in BuildingWorld.LeafParts(door, i))
            {
                Vector3 clamped = Vector3.Clamp(local, part.Min, part.Max);
                Vector3 point = position + Vector3.Transform(clamped, rotation);
                float d = Vector3.DistanceSquared(point, at);
                if (d < nearest)
                {
                    nearest = d;
                    best = (point, position + Vector3.Transform((part.Min + part.Max) / 2, rotation));
                }
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

    /// <summary>
    /// The nearest point to <paramref name="at"/> on the hull boxes of one target part (a module's body, VEHICLES.md §9.1), and
    /// whether the part has any.
    /// </summary>
    public static bool NearestOnPart(Ships.ShipDef def, Vector3 centre, Quaternion rotation, Vector3 at, int part, out Vector3 point)
    {
        Vector3 local = Vector3.Transform(at - centre, Quaternion.Conjugate(rotation)) + def.CentreOfMass;
        Vector3 best = local;
        float nearest = float.MaxValue;
        for (int i = 0; i < def.Hull.Count; i++)
        {
            if (def.HullPart(i) != part)
            {
                continue;
            }
            Ships.HullBox box = def.Hull[i];
            Vector3 p = Vector3.Clamp(local, box.Centre - box.HalfExtents, box.Centre + box.HalfExtents);
            float d = Vector3.DistanceSquared(p, local);
            if (d < nearest)
            {
                nearest = d;
                best = p;
            }
        }
        point = centre + Vector3.Transform(best - def.CentreOfMass, rotation);
        return nearest < float.MaxValue;
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
        scratch.Clear();
        // Derive reach from the very shapes tested below: pickup/bush centres can lie above the root, and trunk
        // caps or buried rock spheres can extend below it. A long sky ray must not generate an entire surface disk.
        var largest = new Nature.NaturalObject { Up = Vector3.UnitY, Scale = Nature.NaturalShapes.MaximumGeneratedScale };
        float rootReach = 0;
        foreach (NaturalKind kind in content.NaturalKinds)
        {
            if (ray.AttackRadius != null && !kind.IsSolid) continue;
            Combat.HitShape shape = Nature.NaturalShapes.Bounds(largest, kind);
            float grow = ray.AttackRadius ?? (kind.IsPicked ? AimShapes.PickupMargin : 0);
            rootReach = MathF.Max(rootReach, MathF.Max(shape.A.Length(), shape.B.Length()) + shape.Radius + grow);
        }
        nature.QueryAlong(ray.Origin, end, MathF.BitIncrement(rootReach), scratch);
        foreach (Nature.NaturalObject o in scratch)
        {
            if (content.Natural(o.Kind) is not { } kind || state.IsDepleted(o.Id) || ray.AttackRadius != null && !kind.IsSolid)
            {
                continue;
            }
            // A pickup or bush is taken with E (a little bigger than it is); a tree or a rock only stops the view.
            Combat.HitShape shape = Nature.NaturalShapes.Bounds(o, kind);
            (float grow, AimKind aim) = kind.IsPicked ? (AimShapes.PickupMargin, AimKind.Thing) : (0f, AimKind.Backdrop);
            grow = ray.AttackRadius ?? grow;
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
        if (ray.AttackRadius != null) return; // An empty doorway offers interaction, never an attack contact.
        float length = ray.Upto(best);
        building.PiecesNear(ray.Origin, ray.Origin + ray.Direction * length, 3.5f, scratch);
        var within = ray with { Length = length };
        foreach (Piece piece in scratch)
        {
            if (piece.Door is { Gone: false } door)
            {
                ray.Consider(ref best, AimShapes.Doorway(within, building.Data, piece), door.Ref,
                    door.Open || !door.Def.Opens ? AimKind.Opening : AimKind.Thing);
            }
        }
    }
}

/// <summary>
/// A ship's target parts as the interact key, reach and support links see them (GROUND_SYSTEMS.md §5, IRON_WAKE.md §5.1),
/// the same on both sides: each part's pose (the ship's, moved by what carries it: a battery turns with its turret, the
/// ramp swings), its ball along a ray (a part reached through a door only while that door lets one in), and its nearest
/// point (its ball's surface, or its pieces' in the ship's space). A part met by pieces is found along a ray by the space's
/// query, which names the data file's part (<see cref="Ships.ShipDef.PartOfPieces"/>).
/// </summary>
public static class ShipAim
{
    /// <summary>A part's pose in the zone's frame: the ship's at <paramref name="pose"/>, moved by the part's carrier at the states.</summary>
    public static TargetPose PartPose(Ships.ShipDef def, int part, in Movement.ShipPose pose, ReadOnlySpan<float> states)
    {
        Ships.PartPose motion = Carrier(def, part, states);
        return new TargetPose(pose.ToWorld(motion.Translation), Quaternion.Normalize(pose.Rotation * motion.Rotation));
    }

    private static Ships.PartPose Carrier(Ships.ShipDef def, int part, ReadOnlySpan<float> states)
    {
        if (def.Interior == null || part <= 0 || part >= def.Parts.Count)
        {
            return Ships.PartPose.Rest;
        }
        Ships.ShipTargetPart p = def.Parts[part];
        int carrier = p.Pieces.Count > 0 ? p.Pieces[0] : p.At.Part;
        return carrier > 0 ? def.Interior.PartPose(carrier, states) : Ships.PartPose.Rest;
    }

    // A part's ball's centre in the zone's frame.
    private static Vector3 BallCentre(Ships.ShipDef def, Ships.ShipTargetPart p, in Movement.ShipPose pose, ReadOnlySpan<float> states) =>
        pose.ToWorld(def.Interior != null ? def.Interior.SocketAt(p.At, states) : p.At.Position);

    /// <summary>
    /// The balls of a ship's parts along the ray (its pose, its parts' states): a part reached through a door only while
    /// that door lets one in (bit d of <paramref name="ready"/> for door d).
    /// </summary>
    public static void Balls(ulong id, Ships.ShipDef def, in Movement.ShipPose pose, ReadOnlySpan<float> states, uint ready, in AimRay ray, ref AimHit best)
    {
        float reach = MathF.Max(def.BoundingRadius, def.Interior?.Reach ?? 0) + ray.Length + 1;
        if (Vector3.DistanceSquared(pose.Position, ray.Origin) > reach * reach)
        {
            return;
        }
        for (int i = 1; i < def.Parts.Count; i++)
        {
            Ships.ShipTargetPart p = def.Parts[i];
            if (!p.HasBall || (p.Through >= 0 && (ready & 1u << p.Through) == 0))
            {
                continue;
            }
            ray.Consider(ref best, AimShapes.Sphere(ray, BallCentre(def, p, pose, states), p.Radius), new TargetRef(TargetFamily.Ship, id, (byte)i));
        }
    }

    /// <summary>
    /// The nearest point of a part to a point (reach is measured to it): its ball's surface, or its pieces' (the ship's
    /// space, <paramref name="frame"/>), whichever is nearer; false for the hull and a part not met here (the caller then
    /// measures to the hull).
    /// </summary>
    public static bool Nearest(Ships.ShipDef def, Movement.IShipFrame? frame, in Movement.ShipPose pose, ReadOnlySpan<float> states, int part, Vector3 from,
        List<(int Part, Vector3 Point, float Distance)> scratch, out Vector3 point)
    {
        point = from;
        if (part <= 0 || part >= def.Parts.Count)
        {
            return false;
        }
        Ships.ShipTargetPart p = def.Parts[part];
        float best = float.MaxValue;
        bool found = false;
        if (p.HasBall)
        {
            point = AimShapes.NearestOnSphere(BallCentre(def, p, pose, states), p.Radius, from);
            best = Vector3.Distance(point, from);
            found = true;
        }
        if (p.Pieces.Count > 0 && frame != null)
        {
            Ships.ShipQueries.NearestByPart(frame, pose, from, Vector3.Distance(from, pose.Position) + frame.Interior.Reach, scratch);
            foreach ((int dataPart, Vector3 at, float distance) in scratch)
            {
                if (distance < best && def.PartOfPieces(dataPart) == part)
                {
                    best = distance;
                    point = at;
                    found = true;
                }
            }
        }
        return found;
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
        return Combat.Geometry.RayBox(ray.Origin, ray.Direction, ray.Length, position + up * halfExtents.Y, halfExtents + new Vector3(ray.AttackRadius ?? BoxMargin),
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
        if (!data.TryOpening(frame.Def.Opening, out var aperture))
        {
            return -1;
        }
        BuildingFrame f = frame.Structure.Frame;
        Vector3 end = ray.Origin + ray.Direction * ray.Length;
        Core.Vector3D eye = new(ray.Origin.X, ray.Origin.Y, ray.Origin.Z), far = new(end.X, end.Y, end.Z);
        Pose opening = frame.Pose with { Position = frame.Pose.Position + Core.Vector3D.UnitY * aperture.Sill };
        if (!PieceGeometry.RayOpening(opening, aperture.Width, aperture.Height, f.ToLocal(eye), f.ToLocal(far), out Core.Vector3D hit))
        {
            return -1;
        }
        return (float)(f.ToZone(hit) - eye).Length;
    }

    /// <summary>The nearest point of a frame piece's opening (the rectangle in its plane) to a point.</summary>
    public static bool NearestInDoorway(BuildingData data, Piece frame, Vector3 from, out Vector3 point)
    {
        point = from;
        if (!data.TryOpening(frame.Def.Opening, out var aperture))
        {
            return false;
        }
        BuildingFrame f = frame.Structure.Frame;
        Core.Vector3D local = PieceGeometry.ToLocal(frame.Pose, f.ToLocal(new Core.Vector3D(from.X, from.Y, from.Z)));
        var clamped = new Core.Vector3D(Math.Clamp(local.X, -aperture.Width / 2, aperture.Width / 2),
            Math.Clamp(local.Y, aperture.Sill, aperture.Sill + aperture.Height), 0);
        Core.Vector3D zone = f.ToZone(PieceGeometry.ToFrame(frame.Pose, clamped));
        point = new Vector3((float)zone.X, (float)zone.Y, (float)zone.Z);
        return true;
    }
}
