using System.Collections.Generic;
using System.Numerics;
using System;
using BuildingPiece = Hollow.Simulation.Building.Piece;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// Interaction (GROUND_SYSTEMS.md §5): one intent, one path. A player names a target and a verb; the server resolves the
// target through its family, checks the verb's gates (alive, awake, not down, not seated), the one reach and sight rule
// (§3.4) with the verb's reach, and that the target's capabilities offer the verb, enabled, to this player now; then the
// verb's one handler does it. The same offer is what a client's prompt and radial show, from the same rules.
public sealed class InteractionSystem : ZoneSystem
{
    private BuildingSystem? _buildingSystem;
    private BuildingSystem Building => _buildingSystem ??= Zone.Require<BuildingSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private ShipSystem? _shipsSystem;
    private ShipSystem Ships => _shipsSystem ??= Zone.Require<ShipSystem>();
    private SpillSystem? _spillSystem;
    private SpillSystem Spill => _spillSystem ??= Zone.Require<SpillSystem>();
    private TargetSystem? _targetsSystem;
    private TargetSystem Targets => _targetsSystem ??= Zone.Require<TargetSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();
    private WorldSystem? _worldSystem;
    private WorldSystem World => _worldSystem ??= Zone.Require<WorldSystem>();

    private InteractionRules? _interactions;
    private readonly List<InteractOption> _options = new();
    internal readonly List<Animal> _aimAnimals = new();
    internal readonly List<NaturalObject> _aimNature = new();
    internal readonly List<Deployable> _aimThings = new();
    internal readonly List<BuildingPiece> _aimPieces = new();

    /// <summary>The interaction rules: every verb's handler, the options a target offers (null in a movement-only zone).</summary>
    public InteractionRules Interactions => _interactions ?? throw new InvalidOperationException("a zone without game data has no interactions");

    /// <summary>A player as the doer of a verb: their state for its gates, and their team.</summary>
    public Doer DoerOf(Entity entity, Player p)
    {
        DoerState state = (p.Dead ? DoerState.Dead : 0) | (p.Sleeping ? DoerState.Asleep : 0) | (p.Wounded ? DoerState.Wounded : 0) |
            (Ships.SeatOf(entity) != null ? DoerState.Seated : 0);
        var team = World.Teams.TeamOf(p.Id);
        return new Doer(p.Id, state, team != null, team?.Leader == p.Id, team != null && team.Members.Count >= Content.World.TeamSize);
    }

    /// <summary>
    /// What the interact key would act on from an eye along a direction (GROUND_SYSTEMS.md §5): the nearest member of any
    /// family along the ray, as far as the farthest verb reaches, past the one looking. The same query a client runs on
    /// what it was told.
    /// </summary>
    public AimHit Aim(Vector3 eye, Vector3 direction, ulong ignore = 0) =>
        Targeting.Aim(Targets._views, new AimRay(eye, Vector3.Normalize(direction), Interactions.Verbs.LongestReach, ignore));

    /// <summary>The context of a player acting on a target (the server's: with the zone, the player and the thing), or none.</summary>
    private bool TryContext(Entity entity, in TargetRef target, out InteractContext ctx)
    {
        ctx = default;
        if (Players.PlayerOf(entity) is not { } p || Targets.FamilyOf(target.Family) is not { } family || !family.TryFacts(target, Targets.ViewerOf(p), out TargetFacts facts))
        {
            return false;
        }
        ctx = new InteractContext
        {
            Content = Content, Target = target, Facts = facts, Doer = DoerOf(entity, p), Building = Building.World, Zone = Zone, Entity = entity, Player = p,
            Thing = family.Resolve(target.Id),
        };
        return true;
    }

    /// <summary>Every option a target offers a player now, gated and ranked (tests, the server's own view of a prompt).</summary>
    public bool Options(Entity entity, in TargetRef target, List<InteractOption> into)
    {
        into.Clear();
        if (!TryContext(entity, target, out InteractContext ctx))
        {
            return false;
        }
        Interactions.Options(ctx, into);
        return true;
    }

    /// <summary>
    /// A player does a verb to a target (GROUND_SYSTEMS.md §5, <c>InteractIntent</c>): the verb's gates, the reach and sight
    /// rule with the verb's reach, the target's offer holding the verb enabled, then its handler. Done, or the reason.
    /// </summary>
    public Outcome Interact(Entity entity, in TargetRef target, int verb, uint arg = 0, string? code = null)
    {
        if (Players.PlayerOf(entity) is not { } p || Interactions.Verbs.Verb(verb) is not { } def)
        {
            return OutcomeCode.Invalid;
        }
        Outcome gate = InteractionRules.Gate(def, DoerOf(entity, p));
        if (!gate.Ok)
        {
            return gate;
        }
        if (def.Panel != null)
        {
            return OutcomeCode.NotUsable; // the interface's own (a panel), never the server's
        }
        if (!TryContext(entity, target, out InteractContext ctx))
        {
            return OutcomeCode.NotFound;
        }
        Outcome reach = Targets.Reach(EyeOf(Entities.Get<Character>(entity).State), target, def.Reach, def.Sight);
        if (!reach.Ok)
        {
            return reach;
        }
        Interactions.Options(ctx, _options);
        if (InteractionRules.Find(_options, verb) is not { } option)
        {
            return OutcomeCode.NotUsable;
        }
        return option.Enabled ? Interactions.Handlers[verb].Do(ctx, verb, new InteractArgs(arg, code ?? "")) : option.Reason;
    }

    /// <summary>A player does a verb, named by its key in interactions.json (tests, the sandboxes).</summary>
    public Outcome Interact(Entity entity, in TargetRef target, string verb, uint arg = 0, string? code = null) =>
        Interact(entity, target, Interactions.Verbs.Index(verb), arg, code);

    // ---------------------------------------------------------------- what the handlers do here

    /// <summary>Opens what a player loots: a box, a device, a body, a sleeper, the cupboard's store, a ship's cargo.</summary>
    internal Outcome OpenLoot(Player p, EntityId target)
    {
        p.Looting = target;
        p.LootRevision++;
        return Outcome.Done;
    }

    /// <summary>Takes a thing whole into a player's slots, as its family hands it over (a pickup, a bag, an arrow).</summary>
    internal Outcome PickUp(Entity entity, Player p, in TargetRef target) =>
        Targets.FamilyOf(target.Family) is IPickupFamily family ? family.PickUp(entity, p, target) : OutcomeCode.NotUsable;

    /// <summary>
    /// A bag of dropped items taken whole: everything in it that fits (a bag of one stack is taken at a tap; one holding
    /// more opens, and all of it is taken with held E). What doesn't fit stays; an emptied bag goes.
    /// </summary>
    internal Outcome TakeBag(Player p, DroppedBag bag)
    {
        bool taken = false;
        for (int i = 0; i < bag.Loot.Size; i++)
        {
            ItemStack stack = bag.Loot[i];
            int left = stack.IsEmpty ? 0 : p.Inventory.Give(Content, stack);
            if (left == stack.Count)
            {
                continue;
            }
            Events.Add(new ItemsGained(Player: p.Id, Item: stack.Item, Count: stack.Count - left, Reason: GainReason.PickedUp));
            bag.Loot.Set(i, stack.WithCount(left));
            taken = true;
        }
        if (!taken)
        {
            return OutcomeCode.NoSpace;
        }
        bag.PublishChanges();
        Things.EndIfEmptied(bag);
        return OutcomeCode.Done;
    }

    /// <summary>A deployable picked back up as its item (GROUND_SYSTEMS.md §11.3): it goes, its item into the player's slots.</summary>
    internal Outcome TakeDeployable(Entity entity, Player p, Deployable d)
    {
        Things.RemoveStatic(d);
        Spill.GiveOrDrop(entity, p, ItemStack.Of(d.Def.Item, 1));
        Events.Add(new ItemsGained(Player: p.Id, Item: d.Def.Item.Id, Count: 1, Reason: GainReason.PickedUp));
        return Outcome.Done;
    }

    /// <summary>Lights a campfire or furnace (it needs fuel) or puts it out.</summary>
    internal Outcome SwitchBurner(Deployable d, bool on)
    {
        if (d.Lit == on)
        {
            return Outcome.Done;
        }
        if (on && !HasFuel(d))
        {
            return OutcomeCode.NoFuel;
        }
        d.Lit = on;
        d.BurnTicksLeft = 0;
        d.Revision++;
        return Outcome.Done;
    }

    private bool HasFuel(Deployable d)
    {
        if (d.Def.Device is not { } device || d.Storage is not { } storage)
        {
            return false;
        }
        foreach (int s in device.FuelSlots)
        {
            if (Content.Item(storage[s].Item)?.Fuel != null)
            {
                return true;
            }
        }
        return false;
    }

    public override int InitOrder => 90;

    /// <summary>The rules come after the target system made the capabilities (GroundSystems lists it first).</summary>
    protected internal override void Init() => _interactions = new InteractionRules(Content, Targets.Capabilities);
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class InteractionSystemAccess
{
    public static InteractionSystem Interaction(this Zone zone) => zone.Require<InteractionSystem>();
}
