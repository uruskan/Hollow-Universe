using System;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Placement;

/// <summary>
/// One check of the placement pipeline (GROUND_SYSTEMS.md §6), registered by name and run in placement.json's fixed order,
/// configured by the placeable's rule: a check with nothing to say for a rule passes. The same code gives the client's
/// ghost its colour and reason and the server its verdict.
/// </summary>
public interface IPlacementCheck
{
    string Name { get; }

    Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c);
}

/// <summary>The checks, by name.</summary>
internal static class Checks
{
    public static IPlacementCheck[] All => new IPlacementCheck[]
    {
        new Reach(), new Sight(), new Surface(), new Step(), new Slope(), new Support(), new Water(), new Rights(), new Overlap(),
        new Limits(), new Own(), new Aboard(),
    };

    /// <summary>A footprint corner's surface must face this far up (RustMVP's 0.75) to hold it.</summary>
    public const float CornerUp = 0.75f;

    /// <summary>The eye reaches its nearest point (GROUND_SYSTEMS.md §3.4: to the nearest surface, the rule's reach).</summary>
    private sealed class Reach : IPlacementCheck
    {
        public string Name => "reach";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
            Vector3.DistanceSquared(ctx.Eye, c.Nearest) <= ctx.Rule.Reach * ctx.Rule.Reach ? Outcome.Done : OutcomeCode.OutOfReach;
    }

    /// <summary>
    /// Nothing between the eye and its nearest point (the building's rule, every placeable's now): what the ray meets
    /// short of it by more than the rule's slack, and away from it, hides it; the ground doesn't hide what is meant to sit
    /// in it (a foundation).
    /// </summary>
    private sealed class Sight : IPlacementCheck
    {
        public string Name => "sight";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
        {
            if (c.Aboard != null)
            {
                return Placement.Aboard.Sight(ctx, c);
            }
            float slack = ctx.Rule.Sight;
            Vector3 d = c.Nearest - ctx.Eye;
            float distance = d.Length();
            if (slack < 0 || distance <= slack || !world.Ray(ctx.Eye, d / distance, distance, out PlacementHit hit))
            {
                return Outcome.Done;
            }
            return (hit.Target.Family == TargetFamily.Ground && c.SightThroughGround) ||
                (c.SightThroughSupport && !c.Support.IsNone && hit.Target.Whole == c.Support.Whole) || hit.Distance >= distance - slack ||
                Vector3.Distance(hit.Point, c.Nearest) <= slack
                ? Outcome.Done
                : OutcomeCode.OutOfSight;
        }
    }

    /// <summary>On a surface the rule allows: refused on another (a floor-only placeable says where it goes).</summary>
    private sealed class Surface : IPlacementCheck
    {
        public string Name => "surface";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
        {
            PlacementSurfaces allowed = ctx.Rule.Surfaces;
            if ((allowed & c.Surface) != 0)
            {
                // Climbable wall mounts can use structural walls or a deployable's vertical face, never its top.
                if (ctx.Placeable.Definition is Content.DeployableDef { Climb: not null } &&
                    MathF.Abs(Vector3.Dot(c.Normal, world.Frame.UpAt(c.Nearest))) > .2f)
                    return OutcomeCode.BadPlacement;
                return Outcome.Done;
            }
            bool floorsOnly = (allowed & PlacementSurfaces.Floor) != 0 && (allowed & PlacementSurfaces.Terrain) == 0;
            return floorsOnly ? OutcomeCode.NotOnStructure : OutcomeCode.BadPlacement;
        }
    }

    /// <summary>The surface it stands on is within the rule's step of where it was aimed (not floating in the air).</summary>
    private sealed class Step : IPlacementCheck
    {
        public string Name => "step";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
            c.Step <= ctx.Rule.MaxStep ? Outcome.Done : OutcomeCode.BadPlacement;
    }

    /// <summary>The surface under it is no steeper than the rule allows.</summary>
    private sealed class Slope : IPlacementCheck
    {
        public string Name => "slope";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
            (c.Aboard is { } aboard ? aboard.Normal.Y : Vector3.Dot(c.Normal, world.Frame.UpAt(c.Position))) >= ctx.Rule.MinUp ? Outcome.Done : OutcomeCode.TooSteep;
    }

    /// <summary>
    /// RustMVP's corners: every corner of its footprint stands on an allowed surface within the rule's support step of the
    /// plane under its middle, facing up; a box half over a foundation's edge overhangs. With the middle as support, what
    /// holds it is enough.
    /// </summary>
    private sealed class Support : IPlacementCheck
    {
        public string Name => "support";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
        {
            if (ctx.Rule.Support != SupportMode.Corners)
            {
                return Outcome.Done;
            }
            if (c.Aboard != null)
            {
                return Placement.Aboard.Support(ctx, c);
            }
            Span<Vector3> corners = stackalloc Vector3[8];
            int count = c.Placeable.Footprint.Corners(c, corners);
            float step = ctx.Rule.SupportStep;
            foreach (Vector3 corner in corners[..count])
            {
                Vector3 up = world.Frame.UpAt(corner);
                if (!world.Ray(corner + up * step, -up, 2 * step, out PlacementHit hit) || (hit.Surface & ctx.Rule.Surfaces) == 0 ||
                    Vector3.Dot(hit.Normal, up) < CornerUp)
                {
                    return OutcomeCode.Overhangs;
                }
            }
            return Outcome.Done;
        }
    }

    /// <summary>Dry: 5 cm over the sea at least (RustMVP's), unless the rule lets it stand in water.</summary>
    private sealed class Water : IPlacementCheck
    {
        public string Name => "water";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
            ctx.Rule.Water || c.Aboard != null || !world.Ground.InWater(c.Position) ? Outcome.Done : OutcomeCode.InWater;
    }

    /// <summary>Building rights where it goes: none needed, no one else's claim (building blocked), or authorised by one.</summary>
    private sealed class Rights : IPlacementCheck
    {
        public string Name => "privilege";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
        {
            if (ctx.Rule.Privilege == PrivilegeMode.Ignored)
            {
                return Outcome.Done;
            }
            // Aboard a ship its crew builds (IRON_WAKE.md §8): those on its list, authorised at its helm.
            if (c.Aboard != null)
            {
                return world.Around.Crew(c.Support, ctx.Player) ? Outcome.Done : OutcomeCode.NotCrew;
            }
            if (world.Building is not { } building)
            {
                return Outcome.Done;
            }
            Building.Privilege here = building.PrivilegeAt(ctx.Player, c.Position, ctx.Rule.Padding);
            return here == Building.Privilege.Blocked ? OutcomeCode.BuildingBlocked
                : ctx.Rule.Privilege == PrivilegeMode.Needed && here != Building.Privilege.Allowed ? OutcomeCode.NotAuthorised
                : Outcome.Done;
        }
    }

    /// <summary>The space its footprint needs is clear (a box's, a hull's; a point needs none).</summary>
    private sealed class Overlap : IPlacementCheck
    {
        public string Name => "overlap";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) => c.Placeable.Footprint.Clear(world, ctx, c);
    }

    /// <summary>
    /// The rule's limits (RustMVP's): at most so many of its kind on the structure it stands on (20 deployables, one claim
    /// device), at most so many of it a player (128 sleeping bags).
    /// </summary>
    private sealed class Limits : IPlacementCheck
    {
        public string Name => "limits";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
        {
            PlacementRule rule = ctx.Rule;
            int item = c.Placeable.Item?.Id ?? 0;
            // Aboard: so many deployables a ship (IRON_WAKE.md §8).
            if (rule.PerShip > 0 && c.Support.Family == TargetFamily.Ship && world.Around.ThingsOn(c.Support.Whole) >= rule.PerShip)
            {
                return OutcomeCode.ShipFull.With(item);
            }
            if (rule.PerStructure > 0 && c.Support.Family == TargetFamily.Piece &&
                world.Building?.PieceOf(new EntityId(c.Support.Id)) is { } piece &&
                c.Placeable.Kind.CountOn(world, piece.Structure) >= rule.PerStructure)
            {
                return OutcomeCode.StructureFull.With(item);
            }
            if (rule.PerPlayer > 0 && c.Placeable.Kind.CountOwned(world, ctx.Player, c.Placeable) >= rule.PerPlayer)
            {
                return OutcomeCode.TooMany.With(item);
            }
            return Outcome.Done;
        }
    }

    /// <summary>
    /// Aboard a ship (IRON_WAKE.md §8): where its footprint may stand there, as the ship's data file has it: clear of what
    /// is kept clear, on a deck of its hull, in a build zone, under its headroom. In the zone's frame there is nothing to say.
    /// </summary>
    private sealed class Aboard : IPlacementCheck
    {
        public string Name => "aboard";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
            c.Aboard != null ? Placement.Aboard.Where(ctx, c) : Outcome.Done;
    }

    /// <summary>Its kind's own rules: a piece's building checks, a door's opening, one lock to a door, the claim device on a slab.</summary>
    private sealed class Own : IPlacementCheck
    {
        public string Name => "own";

        public Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) => c.Placeable.Kind.Check(world, ctx, c);
    }
}
