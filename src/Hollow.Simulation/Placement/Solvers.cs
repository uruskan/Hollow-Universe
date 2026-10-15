using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Placement;

/// <summary>
/// Turns an aim into candidates (GROUND_SYSTEMS.md §6), registered by name: free (onto the surface under the aim), socket
/// (building pieces: the building's sockets), opening (a door into the doorway or wall frame aimed through), attach (a
/// lock onto the door aimed at), sweep (a charge onto the first surface the view meets). On the server it works the one
/// candidate an intent claims out again, from the claim's support and pose, and refuses a claim it can't make agree.
/// </summary>
public interface IPlacementSolver
{
    string Name { get; }

    /// <summary>What the player is told when the aim finds nothing to place (none: nothing is said).</summary>
    Outcome Nothing { get; }

    /// <summary>The client: candidates from the aim, the best first.</summary>
    void Aim(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, List<PlacementCandidate> into);

    /// <summary>The server: the candidate a claim names, worked out again, or null and why not.</summary>
    PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused);
}

/// <summary>The solvers, by name.</summary>
internal static class Solvers
{
    public static IPlacementSolver[] All => new IPlacementSolver[] { new FreeSolver(), new SocketSolver(), new OpeningSolver(), new AttachSolver(), new SweepSolver() };

    /// <summary>The aim reaches this much past the rule's reach, so a spot too far away shows red rather than nothing.</summary>
    public const float AimSlack = 1.5f;

    /// <summary>Whether a claimed position is within the rule's tolerance of the server's own.</summary>
    public static bool Agrees(in PlacementContext ctx, Vector3 own, Vector3 claimed) =>
        Vector3.DistanceSquared(own, claimed) <= ctx.Rule.Tolerance * ctx.Rule.Tolerance;

    public static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>The client: the rule's rotation turns a candidate by the aim (the view, R's presses), then its kind orients it.</summary>
    public static void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, PlacementCandidate c)
    {
        world.Data.Handlers.Rotation(ctx.Rule).Turn(world, ctx, aim, c);
        c.Rotation = c.Placeable.Kind.Orient(c);
    }

    /// <summary>The server: the rule's rotation turns a candidate as the claim says (what the rule allows of it), then its kind orients it.</summary>
    public static void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, PlacementCandidate c)
    {
        world.Data.Handlers.Rotation(ctx.Rule).Settle(world, ctx, claim, c);
        c.Rotation = c.Placeable.Kind.Orient(c);
    }

    public static Vector3 F(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static Vector3D D(Vector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>
/// Onto the surface under the aim (RustMVP's deployables): the first body the aim meets, then straight down from a little
/// above that point (in front of a wall, the floor at its foot) to the surface it would stand on, within the rule's step.
/// The rule's rotation turns it; its kind says how it is drawn.
/// </summary>
internal sealed class FreeSolver : IPlacementSolver
{
    public string Name => "free";

    public Outcome Nothing => Outcome.Done;

    public void Aim(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, List<PlacementCandidate> into)
    {
        float reach = ctx.Rule.Reach + Solvers.AimSlack;
        bool met = world.Ray(ctx.Eye, aim.Direction, reach, out PlacementHit hit);
        // A ship's pieces first (a parked one's body in the zone is the same hull): the candidate is the ship's frame's,
        // with its down (IRON_WAKE.md §8).
        if (Aboard.Ray(world, ctx.Eye, aim.Direction, met ? hit.Distance + 0.05f : reach, out AboardHit aboard))
        {
            into.Add(Aboard.Aim(world, ctx, aim, aboard));
            return;
        }
        if (!met)
        {
            return;
        }
        // Under a ceiling the floor is looked for from just under it, never on the storey above.
        bool ceiling = Vector3.Dot(hit.Normal, world.Frame.UpAt(hit.Point)) < -0.2f;
        PlacementCandidate c = Under(world, ctx, hit.Point - aim.Direction * 0.02f, hit.Point, ceiling ? 0.02f : ctx.Rule.MaxStep);
        Solvers.Turn(world, ctx, aim, c);
        c.Nearest = c.Placeable.Footprint.NearestTo(c, ctx.Eye);
        into.Add(c);
    }

    public PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused)
    {
        refused = OutcomeCode.Invalid;
        if (claim.Support.Family == TargetFamily.Ship)
        {
            return Aboard.Settle(world, ctx, claim, out refused);
        }
        if (!Solvers.Finite(claim.Position))
        {
            return null;
        }
        PlacementCandidate c = Under(world, ctx, claim.Position, claim.Position, ctx.Rule.MaxStep);
        if (c.Support.Whole != claim.Support.Whole || !Solvers.Agrees(ctx, c.Position, claim.Position))
        {
            refused = c.Support.IsNone ? OutcomeCode.BadPlacement : OutcomeCode.Misplaced;
            return null;
        }
        Solvers.Turn(world, ctx, claim, c);
        c.Nearest = c.Placeable.Footprint.NearestTo(c, ctx.Eye);
        refused = Outcome.Done;
        return c;
    }

    // What stands under a point: a ray down from <paramref name="above"/> over it to the rule's step below, from just in
    // front of what was aimed at. None found: the candidate floats where it was aimed (the step check says so).
    private static PlacementCandidate Under(PlacementWorld world, in PlacementContext ctx, Vector3 from, Vector3 aimed, float above)
    {
        Vector3 up = world.Frame.UpAt(aimed);
        float step = ctx.Rule.MaxStep;
        var c = new PlacementCandidate(ctx.Placeable);
        if (world.Ray(from + up * above, -up, above + step + 0.05f, out PlacementHit under))
        {
            c.Position = under.Point;
            c.Support = under.Target;
            c.Surface = under.Surface;
            c.Normal = under.Normal;
            c.Step = MathF.Abs(Vector3.Dot(aimed - under.Point, up));
        }
        else
        {
            c.Position = aimed;
            c.Support = TargetRef.None;
            c.Surface = PlacementSurfaces.None;
            c.Normal = up;
            c.Step = float.PositiveInfinity;
        }
        c.Up = ctx.Rule.Align == AlignMode.Normal ? c.Normal : world.Frame.UpAt(c.Position);
        return c;
    }
}

/// <summary>
/// Building pieces (GROUND.md §6.3), plugged in: the building's own scoring of every compatible socket near the aim (RustMVP's
/// preview), best first, or a free foundation on the ground; the server snaps the claimed socket again (or lays the free
/// foundation again from its top) and compares.
/// </summary>
internal sealed class SocketSolver : IPlacementSolver
{
    public string Name => "socket";

    public Outcome Nothing => OutcomeCode.NoSocket;

    public void Aim(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, List<PlacementCandidate> into)
    {
        if (world.Building is not { } building)
        {
            return;
        }
        List<PlacementPlan> plans = world.Plans;
        building.Candidates((PieceDef)ctx.Placeable.Definition, ctx.Eye, aim.Direction, aim.Turns, plans);
        foreach (PlacementPlan plan in plans)
        {
            PlacementCandidate c = Of(world, ctx, plan);
            Solvers.Turn(world, ctx, aim, c);
            into.Add(c);
        }
    }

    public PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused)
    {
        refused = OutcomeCode.Invalid;
        if (world.Building is not { } building || !Solvers.Finite(claim.Position))
        {
            return null;
        }
        var piece = (PieceDef)ctx.Placeable.Definition;
        PlacementPlan? plan;
        if (claim.Support.Family == TargetFamily.Piece)
        {
            if (building.PieceOf(new EntityId(claim.Support.Id)) is not { } support)
            {
                refused = OutcomeCode.NotFound;
                return null;
            }
            if (SocketId.FromCode(claim.Socket) is not { } socket || claim.Variant is < 0 or > 255)
            {
                return null;
            }
            plan = building.FromIntent(piece, support, socket, claim.Variant);
            refused = OutcomeCode.NoSocket;
        }
        else
        {
            // A free foundation: laid again from its top and the existing wire pose's continuous heading.
            if (claim.Support != TargetRef.Ground || piece.Family != PieceFamily.Foundation || !float.IsFinite(claim.Yaw))
            {
                return null;
            }
            Vector3 up = world.Frame.UpAt(claim.Position);
            plan = building.FoundationAt(piece, claim.Position - up * (float)building.Data.FoundationClearance, claim.Yaw);
            refused = OutcomeCode.Floating;
        }
        if (plan == null)
        {
            return null;
        }
        PlacementCandidate c = Of(world, ctx, plan);
        if (!Solvers.Agrees(ctx, c.Position, claim.Position))
        {
            refused = OutcomeCode.Misplaced;
            return null;
        }
        Solvers.Turn(world, ctx, claim, c);
        refused = Outcome.Done;
        return c;
    }

    internal static PlacementCandidate Of(PlacementWorld world, in PlacementContext ctx, PlacementPlan plan)
    {
        (Vector3 position, Quaternion rotation) = plan.ZoneTransform;
        BuildingFrame frame = plan.Frame;
        Vector3D eyeLocal = frame.ToLocal(Solvers.D(ctx.Eye));
        Vector3 up = Solvers.F(frame.AxisY);
        bool free = plan.Support == null;
        return new PlacementCandidate(ctx.Placeable)
        {
            Position = position,
            Rotation = rotation,
            Up = up,
            Normal = up,
            Support = plan.Support?.Ref ?? TargetRef.Ground,
            Surface = free ? PlacementSurfaces.Terrain : world.Data.SurfaceOf(plan.Support!.Def.Family),
            Socket = free ? 0 : plan.Socket.Code,
            Variant = free ? 0 : plan.Orientation,
            Yaw = free ? (float)plan.Heading : 0,
            Nearest = Solvers.F(frame.ToZone(PieceGeometry.ClosestPoint(world.Building!.Data, plan.Piece, plan.Pose, eyeLocal))),
            SightThroughGround = plan.Piece.Family == PieceFamily.Foundation,
            Detail = plan,
        };
    }
}

/// <summary>Doors: the doorway or wall frame the aim passes through (its empty opening counts, as in RustMVP), on the side R chose.</summary>
internal sealed class OpeningSolver : IPlacementSolver
{
    public string Name => "opening";

    public Outcome Nothing => OutcomeCode.NotAnOpening;

    public void Aim(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, List<PlacementCandidate> into)
    {
        if (world.Building?.OpeningAimed(ctx.Eye, aim.Direction, ctx.Rule.Reach + Solvers.AimSlack) is { } frame)
        {
            PlacementCandidate c = Of(world, ctx, frame);
            Solvers.Turn(world, ctx, aim, c);
            into.Add(c);
        }
    }

    public PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused)
    {
        refused = OutcomeCode.NotAnOpening;
        if (claim.Support.Family != TargetFamily.Piece || world.Building?.PieceOf(new EntityId(claim.Support.Id)) is not { } frame)
        {
            return null;
        }
        PlacementCandidate c = Of(world, ctx, frame);
        if (!Solvers.Agrees(ctx, c.Position, claim.Position))
        {
            refused = OutcomeCode.Misplaced;
            return null;
        }
        Solvers.Turn(world, ctx, claim, c);
        refused = Outcome.Done;
        return c;
    }

    private static PlacementCandidate Of(PlacementWorld world, in PlacementContext ctx, Piece frame)
    {
        (Vector3 position, Quaternion rotation) = frame.ZoneTransform;
        BuildingFrame structure = frame.Structure.Frame;
        Vector3D eyeLocal = structure.ToLocal(Solvers.D(ctx.Eye));
        Vector3 up = Solvers.F(structure.AxisY);
        return new PlacementCandidate(ctx.Placeable)
        {
            Position = position,
            Rotation = rotation,
            Up = up,
            Normal = up,
            Support = frame.Ref,
            Surface = PlacementSurfaces.Opening,
            Nearest = Solvers.F(structure.ToZone(PieceGeometry.ClosestPoint(world.Building!.Data, frame.Def, frame.Pose, eyeLocal))),
            Detail = frame,
        };
    }
}

/// <summary>Locks: onto the door aimed at, where a lock sits on its first leaf (<see cref="Door.LockSpot"/>).</summary>
internal sealed class AttachSolver : IPlacementSolver
{
    public string Name => "attach";

    public Outcome Nothing => Outcome.Done;

    public void Aim(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, List<PlacementCandidate> into)
    {
        if (world.Building?.Target(ctx.Eye, aim.Direction, ctx.Rule.Reach + Solvers.AimSlack, out _) is Door door)
        {
            PlacementCandidate c = Of(ctx, door);
            Solvers.Turn(world, ctx, aim, c);
            into.Add(c);
        }
    }

    public PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused)
    {
        refused = OutcomeCode.NotFound;
        if (claim.Support.Family != TargetFamily.Door || world.Building?.DoorOf(new EntityId(claim.Support.Id)) is not { Gone: false } door)
        {
            return null;
        }
        PlacementCandidate c = Of(ctx, door);
        if (!Solvers.Agrees(ctx, c.Position, claim.Position))
        {
            refused = OutcomeCode.Misplaced;
            return null;
        }
        Solvers.Turn(world, ctx, claim, c);
        refused = Outcome.Done;
        return c;
    }

    private static PlacementCandidate Of(in PlacementContext ctx, Door door)
    {
        (Vector3 position, Quaternion rotation) = door.LockSpot;
        Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);
        return new PlacementCandidate(ctx.Placeable)
        {
            Position = position,
            Rotation = rotation,
            Up = up,
            Normal = up,
            Support = door.Ref with { Part = 1 },
            Surface = PlacementSurfaces.Door,
            SightThroughSupport = true,
            Nearest = position,
            Detail = door,
        };
    }
}

/// <summary>
/// Charges (GROUND.md §6.6): the first surface the view meets within reach, by the explosives' own sweep (a wall, a door's
/// leaf, a box, a tree, the ground), the charge stuck a little out from it along its normal. The server sweeps again from
/// its eye toward the claimed point.
/// </summary>
internal sealed class SweepSolver : IPlacementSolver
{
    public string Name => "sweep";

    public Outcome Nothing => Outcome.Done;

    public void Aim(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, List<PlacementCandidate> into)
    {
        if (!world.Sweep(ctx.Eye, ctx.Eye + aim.Direction * (ctx.Rule.Reach + Solvers.AimSlack), 0, out PlacementHit hit))
        {
            return;
        }
        PlacementCandidate c = Of(world, ctx, hit);
        Solvers.Turn(world, ctx, aim, c);
        into.Add(c);
    }

    public PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused)
    {
        refused = OutcomeCode.Invalid;
        if (!Solvers.Finite(claim.Position))
        {
            return null;
        }
        Vector3 toward = claim.Position - ctx.Eye;
        float distance = toward.Length();
        if (distance < 1e-3f)
        {
            return null;
        }
        refused = OutcomeCode.Misplaced;
        if (!world.Sweep(ctx.Eye, ctx.Eye + toward / distance * (distance + 0.25f), 0, out PlacementHit hit))
        {
            return null;
        }
        PlacementCandidate c = Of(world, ctx, hit);
        if (c.Support.Whole != claim.Support.Whole || !Solvers.Agrees(ctx, c.Position, claim.Position))
        {
            return null;
        }
        Solvers.Turn(world, ctx, claim, c);
        refused = Outcome.Done;
        return c;
    }

    private static PlacementCandidate Of(PlacementWorld world, in PlacementContext ctx, in PlacementHit hit) => new(ctx.Placeable)
    {
        Position = hit.Point + hit.Normal * ExplosiveSystem.StuckOffset,
        Nearest = hit.Point,
        Normal = hit.Normal,
        Up = ctx.Rule.Align == AlignMode.Normal ? hit.Normal : world.Frame.UpAt(hit.Point),
        Support = hit.Target,
        Surface = hit.Surface,
        Detail = hit.Point,
    };
}
