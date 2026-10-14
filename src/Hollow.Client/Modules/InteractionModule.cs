using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Client;

/// <summary>
/// What the interact key acts on now (GROUND_SYSTEMS.md §5): the target the one query found, what is known of it, and its
/// options for this player within reach, ranked; which one a tap does (−1: none, holding shows them all).
/// </summary>
public sealed class InteractAim
{
    private readonly List<InteractOption> _options = new();

    public TargetRef Target { get; internal set; }

    public TargetFacts Facts { get; internal set; }

    /// <summary>How far along the view it was met.</summary>
    public float Distance { get; internal set; }

    /// <summary>Its options, enabled or with the reason not, in the ring's order.</summary>
    public IReadOnlyList<InteractOption> Options => _options;

    internal List<InteractOption> Writable => _options;

    /// <summary>The option a tap does, or −1.</summary>
    public int Primary { get; internal set; } = -1;

    public bool HasOptions => _options.Count > 0;
}

// Interaction in the client (GROUND_SYSTEMS.md §5): the one targeting query over this client's target families, and the
// options a target offers this player from the same rules (the capabilities' handlers) the server checks a verb with, so
// the prompt and the radial show what the server will do. The intent is LocalPlayer's Interact.
public sealed class InteractionModule : ClientModule
{
    private InteractionRules? _interactions;
    private readonly InteractAim _aim = new();
    private TargetModule? _targetModule;
    private TargetModule Targets => _targetModule ??= Client.Require<TargetModule>();
    private SurvivalModule? _survivalModule;
    private SurvivalModule Survival => _survivalModule ??= Client.Require<SurvivalModule>();
    private BuildingModule? _buildingModule;
    private BuildingModule Building => _buildingModule ??= Client.Require<BuildingModule>();
    private ShipModule? _shipModule;
    private ShipModule Ships => _shipModule ??= Client.Require<ShipModule>();

    /// <summary>The interaction rules: the verbs' handlers and the options a target offers (GROUND_SYSTEMS.md §5).</summary>
    public InteractionRules Interactions => _interactions ??= new InteractionRules(Content, CapabilityHandlers.Create(Content));

    /// <summary>
    /// What the interact key would act on from the eye along a direction: the nearest member of any family along the ray,
    /// as far as the farthest verb reaches, past this player (the server's own query, on what this client was told).
    /// </summary>
    public AimHit Aim(Vector3 eye, Vector3 direction) =>
        Targeting.Aim(Targets.TargetViews, new AimRay(eye, Vector3.Normalize(direction), Interactions.Verbs.LongestReach, EntityId.Value));

    /// <summary>This player as the doer of a verb: their state for its gates, and their team.</summary>
    public Doer Doer
    {
        get
        {
            DoerState state = (Local.IsDead ? DoerState.Dead : 0) | (Local.IsWounded ? DoerState.Wounded : 0) | (Ships.Piloting ? DoerState.Seated : 0);
            TeamView? team = Local.Team;
            return new Doer(EntityId, state, team != null, Local.LeadsTeam, team != null && team.Members.Count >= Content.World.TeamSize);
        }
    }

    /// <summary>
    /// The options a target offers this player now, from an eye: each of its capabilities' handlers, gated, ranked, and only
    /// those whose verb reaches its nearest surface (the server refuses the rest). False when nothing of it is known here.
    /// </summary>
    public bool Options(in TargetRef target, Vector3 eye, List<InteractOption> into, out TargetFacts facts)
    {
        into.Clear();
        facts = default;
        if (Targets.TargetViewOf(target.Family) is not { } view || !view.TryFacts(target, new Viewer(EntityId, null, null), out facts))
        {
            return false;
        }
        var ctx = new InteractContext { Content = Content, Target = target, Facts = facts, Doer = Doer, Building = Building.World };
        Interactions.Options(ctx, into);
        float distance = view.TryNearest(target, eye, out Vector3 nearest) ? Vector3.Distance(eye, nearest) : float.PositiveInfinity;
        for (int i = into.Count - 1; i >= 0; i--)
        {
            if (Interactions.Verbs.Verb(into[i].Verb) is not { } verb || distance > verb.Reach)
            {
                into.RemoveAt(i);
            }
        }
        return true;
    }

    /// <summary>
    /// What the interact key acts on from the eye along a direction, with its options (the prompt's and the radial's), or
    /// null when nothing there offers anything. The same object every call: read it before the next.
    /// </summary>
    public InteractAim? AimInteraction(Vector3 eye, Vector3 direction)
    {
        AimHit hit = Aim(eye, direction);
        if (!hit.Hit || !Options(hit.Target, eye, _aim.Writable, out TargetFacts facts) || _aim.Writable.Count == 0)
        {
            return null;
        }
        _aim.Target = hit.Target;
        _aim.Facts = facts;
        _aim.Distance = hit.Distance;
        _aim.Primary = InteractionRules.Primary(_aim.Writable);
        return _aim;
    }

    /// <summary>
    /// The workbench whose level counts for this player now (the best within survival.json's reach.station, as crafting
    /// counts it): what a tech tree's unlock is sent to. None: no bench in reach.
    /// </summary>
    public TargetRef BenchNear()
    {
        if (!HasCharacter)
        {
            return TargetRef.None;
        }
        Vector3 eye = Survival.StationEye;
        float reach = Content.Survival.StationReach;
        int best = 0;
        TargetRef bench = TargetRef.None;
        foreach (RemoteThing thing in Survival.Things)
        {
            if (thing.Deployable(Content) is { Workbench: > 0 } d && d.Workbench > best &&
                Vector3.DistanceSquared(Geometry.NearestOnBox(thing.Position, Frame.UpAt(thing.Position), thing.Yaw, d.HalfExtents, eye), eye) <= reach * reach)
            {
                best = d.Workbench;
                bench = new TargetRef(TargetFamily.Thing, thing.Id.Value);
            }
        }
        return bench;
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class InteractionModuleAccess
{
    public static InteractionModule Interaction(this GameClient client) => client.Require<InteractionModule>();
}
