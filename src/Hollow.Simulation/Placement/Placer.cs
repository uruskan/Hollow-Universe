using System;
using System.Collections.Generic;
using System.Linq;

namespace Hollow.Simulation.Placement;

/// <summary>
/// The placement system's handlers (GROUND_SYSTEMS.md §6, §3.5): the solvers, rotations, checks and kinds, each found by
/// its name in placement.json's lists and sealed against them at start (one handler to a name, a name to a handler), and
/// the checks in their fixed order. Stateless: the client and the server, every bot and the zone share one per game data.
/// </summary>
public sealed class PlacementHandlers
{
    private readonly IPlacementSolver[] _solvers;
    private readonly IPlacementRotation[] _rotations;

    private PlacementHandlers(Registry<IPlacementSolver> solvers, Registry<IPlacementRotation> rotations, Registry<IPlacementCheck> checks,
        Registry<PlaceableKind> kinds)
    {
        _solvers = Enumerable.Range(0, solvers.Count).Select(i => solvers[i]).ToArray();
        _rotations = Enumerable.Range(0, rotations.Count).Select(i => rotations[i]).ToArray();
        Checks = Enumerable.Range(0, checks.Count).Select(i => checks[i]).ToArray();
        Kinds = kinds;
        SolverNames = solvers.Keys;
        RotationNames = rotations.Keys;
    }

    /// <summary>The checks, in the pipeline's order.</summary>
    public IReadOnlyList<IPlacementCheck> Checks { get; }

    /// <summary>The kinds, by placement.json's names.</summary>
    public Registry<PlaceableKind> Kinds { get; }

    internal IReadOnlyList<string> SolverNames { get; }

    internal IReadOnlyList<string> RotationNames { get; }

    /// <summary>A rule's solver.</summary>
    public IPlacementSolver Solver(PlacementRule rule) => _solvers[rule.SolverIndex];

    /// <summary>A rule's rotation.</summary>
    public IPlacementRotation Rotation(PlacementRule rule) => _rotations[rule.RotationIndex];

    /// <summary>
    /// The handlers for placement.json's names, sealed: a name without its handler, or a handler without its name, is a
    /// problem (the content check reports it), and none are made.
    /// </summary>
    internal static PlacementHandlers? Create(IReadOnlyList<string> solvers, IReadOnlyList<string> rotations, IReadOnlyList<string> checks,
        IReadOnlyList<string> kinds, List<string> problems)
    {
        var solverRegistry = Fill(new Registry<IPlacementSolver>("placement solvers", solvers), Placement.Solvers.All, s => s.Name);
        var rotationRegistry = Fill(new Registry<IPlacementRotation>("placement rotations", rotations), Placement.Rotations.All, r => r.Name);
        var checkRegistry = Fill(new Registry<IPlacementCheck>("placement checks", checks), Placement.Checks.All, c => c.Name);
        var kindRegistry = Fill(new Registry<PlaceableKind>("placeable kinds", kinds), Placement.Kinds.All, k => k.Name);
        int before = problems.Count;
        problems.AddRange(solverRegistry.Problems.Concat(rotationRegistry.Problems).Concat(checkRegistry.Problems).Concat(kindRegistry.Problems));
        if (problems.Count > before)
        {
            return null;
        }
        return new PlacementHandlers(solverRegistry.Seal(), rotationRegistry.Seal(), checkRegistry.Seal(), kindRegistry.Seal());
    }

    private static Registry<T> Fill<T>(Registry<T> registry, IEnumerable<T> handlers, Func<T, string> name) where T : class
    {
        foreach (T handler in handlers)
        {
            registry.Add(name(handler), handler);
        }
        return registry;
    }
}

/// <summary>
/// The placement pipeline (GROUND_SYSTEMS.md §6): one way to place anything. The client asks <see cref="Plan"/> every frame
/// for the ghost (its solver's candidates, each through the checks in order: the first that passes, else the best with its
/// reason); the server asks <see cref="Settle"/> for the candidate an intent claims (worked out again from the claim, a pose
/// within the rule's tolerance of the server's own) and <see cref="Check"/> for the verdict. The same code on both sides.
/// </summary>
public static class Placer
{
    /// <summary>
    /// The ghost: what the placeable would be where the player aims, checked (its <see cref="PlacementCandidate.Outcome"/>
    /// says whether it fits); null when the aim finds nothing (<see cref="Nothing"/> says what to tell the player).
    /// </summary>
    public static PlacementCandidate? Plan(PlacementWorld world, in PlacementContext ctx, in PlacementAim aim)
    {
        if (!Valid(aim.Direction))
        {
            return null;
        }
        List<PlacementCandidate> candidates = world.Candidates;
        candidates.Clear();
        world.Data.Handlers.Solver(ctx.Rule).Aim(world, ctx, aim with { Direction = System.Numerics.Vector3.Normalize(aim.Direction) }, candidates);
        PlacementCandidate? best = null;
        foreach (PlacementCandidate c in candidates)
        {
            c.Outcome = Check(world, ctx, c);
            if (c.Valid)
            {
                return c;
            }
            best ??= c;
        }
        return best;
    }

    /// <summary>What to tell the player when the aim finds nothing to place (none: say nothing).</summary>
    public static Outcome Nothing(PlacementWorld world, Placeable placeable) => world.Data.Handlers.Solver(placeable.Rule).Nothing;

    /// <summary>
    /// The server: the candidate an intent claims, worked out again by its solver from the claim and compared (the server
    /// never takes the client's pose: it accepts the claim only within the rule's tolerance of its own), then checked; null
    /// and why when it doesn't agree or doesn't pass.
    /// </summary>
    public static PlacementCandidate? Settle(PlacementWorld world, in PlacementContext ctx, in PlacementClaim claim, out Outcome refused)
    {
        PlacementCandidate? c = world.Data.Handlers.Solver(ctx.Rule).Settle(world, ctx, claim, out refused);
        if (c == null)
        {
            return null;
        }
        c.Outcome = Check(world, ctx, c);
        refused = c.Outcome;
        return c.Valid ? c : null;
    }

    /// <summary>
    /// A building piece's candidate for a plan the building rules made (its sockets' placement, a free foundation, a test's
    /// own): what the socket solver proposes for it, unchecked.
    /// </summary>
    public static PlacementCandidate OfPlan(PlacementWorld world, in PlacementContext ctx, Building.PlacementPlan plan) => SocketSolver.Of(world, ctx, plan);

    /// <summary>Every check, in placement.json's order; the first refusal is the verdict.</summary>
    public static Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
    {
        foreach (IPlacementCheck check in world.Data.Handlers.Checks)
        {
            Outcome outcome = check.Check(world, ctx, c);
            if (!outcome.Ok)
            {
                return outcome;
            }
        }
        return Outcome.Done;
    }

    private static bool Valid(System.Numerics.Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && v.LengthSquared() > 1e-8f;
}
