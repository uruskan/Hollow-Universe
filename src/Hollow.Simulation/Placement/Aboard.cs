using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Placement;

/// <summary>
/// A ship people walk in, as placement sees it (IRON_WAKE.md §8): its frame (its space, interior and parts' states), where
/// it is (the server's ship now, a client's as drawn: what the player sees is what the ghost stands on), how the world
/// names it, and its kind.
/// </summary>
public readonly record struct PlacedSpace(IShipFrame Frame, ShipPose Pose, TargetRef Ref, ShipDef Def)
{
    public ShipSpace Space => Frame.Space;

    public ShipInterior Interior => Frame.Interior;
}

/// <summary>What a ray of the zone's frame first meets of a ship's pieces: the ship, and the hit in its frame.</summary>
public readonly record struct AboardHit(PlacedSpace Ship, SpaceHit Hit);

/// <summary>
/// Where a candidate goes aboard a ship (IRON_WAKE.md §8), in the ship's frame: its base on the deck, the deck's normal,
/// its turn about the ship's up (a heading as <see cref="Facing"/> has it with +Y up), and the body of the ship's space under
/// it (0 the hull's decks; a moving part's is no deck). The candidate's own fields are this, carried into the zone's frame
/// where the ship is, for the checks that look at the world (reach, the characters and things near).
/// </summary>
public sealed class AboardSpot
{
    public AboardSpot(PlacedSpace ship)
    {
        Ship = ship;
    }

    public PlacedSpace Ship { get; }

    /// <summary>Its base, in the ship's frame.</summary>
    public Vector3 Position { get; set; }

    /// <summary>The normal of the deck under it, in the ship's frame.</summary>
    public Vector3 Normal { get; set; } = Vector3.UnitY;

    /// <summary>Its turn about the ship's +Y.</summary>
    public float Yaw { get; set; }

    /// <summary>The space's body under it (0 the hull; −1 none).</summary>
    public int Body { get; set; } = -1;

    /// <summary>Its turn in the ship's frame, as it is drawn there (X right, Y up, −Z forward).</summary>
    public Quaternion Rotation => Geometry.Upright(Vector3.UnitY, Yaw);
}

/// <summary>
/// The free solver's half aboard a ship (IRON_WAKE.md §8): when the aim meets a ship's pieces first, the candidate is
/// worked out in the ship's frame, with its down: the deck under the aimed point, a turn about its up. A claim aboard
/// names the ship as its support and gives its pose in the ship's frame, so a ship that moved between the client's frame
/// and the server's tick changes nothing.
/// </summary>
internal static class Aboard
{
    /// <summary>The nearest piece of any ship people walk in that a ray of the zone's frame meets within <paramref name="length"/>.</summary>
    public static bool Ray(PlacementWorld world, Vector3 origin, Vector3 direction, float length, out AboardHit hit)
    {
        hit = default;
        bool found = false;
        float best = length;
        foreach (PlacedSpace ship in world.SpacesNear(origin, length))
        {
            if (Geometry.RaySphere(origin, direction, best, ship.Pose.Position, ship.Interior.Reach) < 0)
            {
                continue;
            }
            bool met;
            SpaceHit local;
            lock (JoltRuntime.Gate)
            {
                met = ship.Space.Ray(ship.Pose.ToLocal(origin), ship.Pose.DirectionToLocal(direction), best, out local);
            }
            if (met && local.Distance < best)
            {
                best = local.Distance;
                hit = new AboardHit(ship, local);
                found = true;
            }
        }
        return found;
    }

    /// <summary>The client: a candidate on the deck under where the aim met the ship, turned by the aim in the ship's frame.</summary>
    public static PlacementCandidate Aim(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, in AboardHit hit)
    {
        PlacedSpace ship = hit.Ship;
        Vector3 along = Vector3.Normalize(ship.Pose.DirectionToLocal(aim.Direction));
        bool ceiling = hit.Hit.Normal.Y < -0.2f;
        PlacementCandidate c = Under(ctx, ship, hit.Hit.Point - along * 0.02f, hit.Hit.Point, ceiling ? 0.02f : ctx.Rule.MaxStep);
        // The aim's heading about the ship's up (a heading's frame is the viewer's, the zone's or a ship's: the aim's
        // direction is the one thing both sides read alike): what the player faces, as a turn on its deck.
        Solvers.Turn(world, ctx, aim with { ViewYaw = Facing.YawOf(along, Vector3.UnitY) }, c);
        Finish(c);
        c.Nearest = c.Placeable.Footprint.NearestTo(c, ctx.Eye);
        return c;
    }

    /// <summary>The server: the candidate a claim aboard names, worked out again in the ship's frame, or null and why not.</summary>
    public static PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused)
    {
        refused = OutcomeCode.NotFound;
        if (!world.SpaceOf(claim.Support, ctx.Eye, out PlacedSpace ship))
        {
            return null;
        }
        refused = OutcomeCode.Invalid;
        if (!Solvers.Finite(claim.Position) || claim.Position.LengthSquared() > ship.Interior.Reach * ship.Interior.Reach * 4)
        {
            return null;
        }
        PlacementCandidate c = Under(ctx, ship, claim.Position, claim.Position, ctx.Rule.MaxStep);
        if (c.Support.Whole != claim.Support.Whole || Vector3.DistanceSquared(c.Aboard!.Position, claim.Position) > ctx.Rule.Tolerance * ctx.Rule.Tolerance)
        {
            refused = c.Support.IsNone ? OutcomeCode.BadPlacement : OutcomeCode.Misplaced;
            return null;
        }
        Solvers.Turn(world, ctx, claim, c);
        Finish(c);
        c.Nearest = c.Placeable.Footprint.NearestTo(c, ctx.Eye);
        refused = Outcome.Done;
        return c;
    }

    // What stands under a point of the ship's frame: a ray down its −Y from above it to the rule's step below (the ship's
    // own pieces; a thing aboard is no floor). None found: it floats where it was aimed (the step check says so).
    private static PlacementCandidate Under(in PlacementContext ctx, PlacedSpace ship, Vector3 from, Vector3 aimed, float above)
    {
        float step = ctx.Rule.MaxStep;
        var spot = new AboardSpot(ship);
        var c = new PlacementCandidate(ctx.Placeable) { Aboard = spot };
        bool met;
        SpaceHit under;
        lock (JoltRuntime.Gate)
        {
            met = ship.Space.Ray(from + Vector3.UnitY * above, -Vector3.UnitY, above + step + 0.05f, out under);
        }
        if (met)
        {
            spot.Position = under.Point;
            spot.Normal = under.Normal;
            spot.Body = under.Part;
            c.Support = ship.Ref with { Part = (byte)Math.Clamp(ship.Def.PartOfPieces(under.Part), 0, 255) };
            c.Surface = PlacementSurfaces.Ship;
            c.Step = MathF.Abs(aimed.Y - under.Point.Y);
        }
        else
        {
            spot.Position = aimed;
            c.Support = TargetRef.None;
            c.Surface = PlacementSurfaces.None;
            c.Step = float.PositiveInfinity;
        }
        // Until it is carried into the zone's frame (Finish), the candidate stands in the ship's: +Y up.
        c.Position = spot.Position;
        c.Normal = spot.Normal;
        c.Up = ctx.Rule.Align == AlignMode.Normal ? spot.Normal : Vector3.UnitY;
        return c;
    }

    /// <summary>
    /// Carries a candidate turned in the ship's frame (its yaw a turn about the ship's +Y) into the zone's frame where the
    /// ship is: its base, up, normal, heading and turn as it is drawn there.
    /// </summary>
    public static void Finish(PlacementCandidate c)
    {
        AboardSpot spot = c.Aboard!;
        ShipPose pose = spot.Ship.Pose;
        spot.Yaw = c.Yaw;
        Quaternion turn = Quaternion.Normalize(pose.Rotation * spot.Rotation);
        c.Position = pose.ToWorld(spot.Position);
        c.Normal = Vector3.Normalize(pose.DirectionToWorld(spot.Normal));
        c.Up = pose.Up;
        c.Yaw = Facing.YawOf(Vector3.Transform(-Vector3.UnitZ, turn), c.Up);
        c.Rotation = c.Placeable.Kind.Orient(c);
    }

    /// <summary>
    /// Where a footprint aboard may stand (IRON_WAKE.md §8): clear of every exclusion (the ramp's top and swing, the
    /// airlock's approach, the stairs, the bridge, the lanes to the stations, the stations' room), on the hull's decks (a
    /// moving part is no deck), inside one build zone, and under its headroom with the rule's clearance to spare (so the
    /// tall bay alone takes the tall things).
    /// </summary>
    public static Outcome Where(in PlacementContext ctx, PlacementCandidate c)
    {
        AboardSpot spot = c.Aboard!;
        ShipInterior interior = spot.Ship.Interior;
        Vector3 half = c.Placeable.Footprint.HalfExtents;
        // The footprint's box in the ship's frame, from its base to its top, as a box along the frame's axes.
        Quaternion turn = spot.Rotation;
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        Span<Vector3> corners = stackalloc Vector3[4];
        int n = 0;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = spot.Position + Vector3.Transform(new Vector3(x * half.X, 0, z * half.Z), turn);
                corners[n++] = corner;
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner + Vector3.UnitY * (2 * half.Y));
            }
        }
        const float touch = 0.01f;
        foreach (ShipExclusion keep in interior.Exclusions)
        {
            if (min.X < keep.Max.X - touch && max.X > keep.Min.X + touch && min.Y < keep.Max.Y - touch && max.Y > keep.Min.Y + touch &&
                min.Z < keep.Max.Z - touch && max.Z > keep.Min.Z + touch)
            {
                return OutcomeCode.KeepClear;
            }
        }
        if (spot.Body != (int)ShipSpace.HullBody)
        {
            return OutcomeCode.NotInBuildZone;
        }
        foreach (ShipBuildZone zone in interior.BuildZones)
        {
            if (spot.Position.Y < zone.Min.Y - ctx.Rule.SupportStep || spot.Position.Y > zone.Max.Y || !Inside(zone, corners))
            {
                continue;
            }
            return spot.Position.Y + 2 * half.Y + ctx.Rule.Headroom <= zone.Ceiling ? Outcome.Done : OutcomeCode.TooTall;
        }
        return OutcomeCode.NotInBuildZone;
    }

    private static bool Inside(ShipBuildZone zone, ReadOnlySpan<Vector3> corners)
    {
        foreach (Vector3 corner in corners)
        {
            if (corner.X < zone.Min.X || corner.X > zone.Max.X || corner.Z < zone.Min.Z || corner.Z > zone.Max.Z)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Every footprint corner aboard stands on a deck of the hull within the rule's support step, facing up.</summary>
    public static Outcome Support(in PlacementContext ctx, PlacementCandidate c)
    {
        AboardSpot spot = c.Aboard!;
        Span<Vector3> corners = stackalloc Vector3[8];
        int count = c.Placeable.Footprint.Corners(c, corners);
        float step = ctx.Rule.SupportStep;
        ShipPose pose = spot.Ship.Pose;
        foreach (Vector3 world in corners[..count])
        {
            Vector3 corner = pose.ToLocal(world);
            bool met;
            SpaceHit hit;
            lock (JoltRuntime.Gate)
            {
                met = spot.Ship.Space.Ray(corner + Vector3.UnitY * step, -Vector3.UnitY, 2 * step, out hit);
            }
            if (!met || hit.Part != (int)ShipSpace.HullBody || hit.Normal.Y < Checks.CornerUp)
            {
                return OutcomeCode.Overhangs;
            }
        }
        return Outcome.Done;
    }

    /// <summary>Nothing of the ship between the eye and the nearest point (a deck above, a bulkhead), as the zone's sight rule.</summary>
    public static Outcome Sight(in PlacementContext ctx, PlacementCandidate c)
    {
        float slack = ctx.Rule.Sight;
        ShipPose pose = c.Aboard!.Ship.Pose;
        Vector3 eye = pose.ToLocal(ctx.Eye), nearest = pose.ToLocal(c.Nearest);
        Vector3 d = nearest - eye;
        float distance = d.Length();
        if (slack < 0 || distance <= slack)
        {
            return Outcome.Done;
        }
        bool met;
        SpaceHit hit;
        lock (JoltRuntime.Gate)
        {
            met = c.Aboard.Ship.Space.Ray(eye, d / distance, distance, out hit);
        }
        return !met || hit.Distance >= distance - slack || Vector3.Distance(hit.Point, nearest) <= slack ? Outcome.Done : OutcomeCode.OutOfSight;
    }

    /// <summary>The room a box aboard needs: clear of the ship's pieces and the solid things aboard (the space's bodies).</summary>
    public static bool Meets(PlacementCandidate c, Vector3 half, float lift)
    {
        AboardSpot spot = c.Aboard!;
        lock (JoltRuntime.Gate)
        {
            return spot.Ship.Space.BoxMeets(half, spot.Position + Vector3.UnitY * lift, spot.Rotation, 0.02f);
        }
    }
}
