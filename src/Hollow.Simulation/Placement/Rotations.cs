using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Placement;

/// <summary>
/// How a placement is turned (GROUND_SYSTEMS.md §6's rotation): from the view and R's presses on the client, from the
/// claim on the server. Sets the candidate's yaw and variant; its kind then says how that is drawn
/// (<see cref="PlaceableKind.Orient"/>). Registered by name: free, steps (of the rule's degrees), support (in its
/// support's steps), flip (a door's side), fixed (as its solver found it).
/// </summary>
public interface IPlacementRotation
{
    string Name { get; }

    /// <summary>The client: the view's yaw and R's presses.</summary>
    void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, PlacementCandidate c);

    /// <summary>The server: the claim's yaw and variant, made what the rule allows.</summary>
    void Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, PlacementCandidate c);
}

/// <summary>The rotations, by name.</summary>
internal static class Rotations
{
    public static IPlacementRotation[] All => new IPlacementRotation[] { new Free(), new Steps(), new Support(), new Flip(), new Fixed() };

    private static float Wrap(float yaw) => MathF.IEEERemainder(yaw, MathF.Tau);

    /// <summary>The view's yaw, plus R's presses of the rule's turn (RustMVP's deployables face where the player looks).</summary>
    private sealed class Free : IPlacementRotation
    {
        public string Name => "free";

        public void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, PlacementCandidate c) =>
            c.Yaw = Wrap(aim.ViewYaw + aim.Turns * ctx.Rule.TurnDegrees * MathF.PI / 180);

        public void Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, PlacementCandidate c) =>
            c.Yaw = float.IsFinite(claim.Yaw) ? Wrap(claim.Yaw) : 0;
    }

    /// <summary>Like free, held to whole steps of the rule's degrees.</summary>
    private sealed class Steps : IPlacementRotation
    {
        public string Name => "steps";

        public void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, PlacementCandidate c) =>
            c.Yaw = Snap(aim.ViewYaw + aim.Turns * ctx.Rule.TurnDegrees * MathF.PI / 180, ctx.Rule);

        public void Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, PlacementCandidate c) =>
            c.Yaw = Snap(float.IsFinite(claim.Yaw) ? claim.Yaw : 0, ctx.Rule);

        private static float Snap(float yaw, PlacementRule rule)
        {
            float step = MathF.Max(1, rule.StepDegrees) * MathF.PI / 180;
            return Wrap(MathF.Round(yaw / step) * step);
        }
    }

    /// <summary>
    /// Square to what holds it: a piece's structure frame, turned in its 30° steps by R's presses of the rule's turn (the
    /// claim device on its foundation). The variant is the turn in those steps; without a piece under it, it turns freely.
    /// </summary>
    private sealed class Support : IPlacementRotation
    {
        public string Name => "support";

        public void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, PlacementCandidate c)
        {
            if (PieceUnder(world, c) is not { } piece)
            {
                c.Yaw = Wrap(aim.ViewYaw + aim.Turns * ctx.Rule.TurnDegrees * MathF.PI / 180);
                return;
            }
            int steps = (int)MathF.Round(ctx.Rule.TurnDegrees / 30);
            Set(c, piece, piece.Pose.Yaw + aim.Turns * steps);
        }

        public void Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, PlacementCandidate c)
        {
            if (PieceUnder(world, c) is not { } piece)
            {
                c.Yaw = float.IsFinite(claim.Yaw) ? Wrap(claim.Yaw) : 0;
                return;
            }
            Set(c, piece, claim.Variant);
        }

        private static Piece? PieceUnder(PlacementWorld world, PlacementCandidate c) =>
            c.Support.Family == TargetFamily.Piece ? world.Building?.PieceOf(new EntityId(c.Support.Id)) : null;

        // The turn in the structure's 30° steps, as the candidate's yaw: the frame's +Z turned, read back as a characters' yaw.
        private static void Set(PlacementCandidate c, Piece piece, int steps)
        {
            c.Variant = PieceGeometry.Wrap(steps);
            (_, Quaternion rotation) = piece.Structure.Frame.ToZone(new Pose(Vector3D.Zero, c.Variant));
            Vector3 back = Vector3.Transform(Vector3.UnitZ, rotation);
            c.Yaw = MathF.Atan2(back.X, back.Z);
        }
    }

    /// <summary>A side: R swaps it (a door hung the other way round in its frame), as the variant; the yaw is the solver's.</summary>
    private sealed class Flip : IPlacementRotation
    {
        public string Name => "flip";

        public void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, PlacementCandidate c) => c.Variant = aim.Turns & 1;

        public void Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, PlacementCandidate c) => c.Variant = claim.Variant & 1;
    }

    /// <summary>
    /// As its solver found it: a lock at its spot on the door, a building piece in the orientation its socket gives (R
    /// cycles a socket's orientations, which the building's candidates do).
    /// </summary>
    private sealed class Fixed : IPlacementRotation
    {
        public string Name => "fixed";

        public void Turn(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim, PlacementCandidate c)
        {
        }

        public void Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, PlacementCandidate c)
        {
        }
    }
}
