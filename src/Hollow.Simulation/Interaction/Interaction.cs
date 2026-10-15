using System;
using System.Collections.Generic;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Interaction;

/// <summary>
/// One thing the E key can do to a target now (GROUND_SYSTEMS.md §5): a verb (interactions.json's number), done or
/// refused with the reason (an <see cref="Outcome"/>: the radial says why), whether its handler makes it the primary
/// one (a tap of E does the highest such by priority), and what the verb acts on when that isn't the target's own name
/// (a ship's part: "OPEN RAMP", "MAN FORWARD BATTERY", "BOARD TIN FINCH"; null: the target's name).
/// </summary>
public readonly record struct InteractOption(int Verb, Outcome Reason, bool Primary = false, string? Name = null)
{
    public bool Enabled => Reason.Ok;

    /// <summary>An option that can be chosen.</summary>
    public static InteractOption Can(int verb, bool primary = false, string? name = null) => new(verb, Outcome.Done, primary, name);

    /// <summary>An option shown with the reason it can't be chosen now.</summary>
    public static InteractOption Cannot(int verb, Outcome why, bool primary = false, string? name = null) => new(verb, why, primary, name);
}

/// <summary>What a verb carries besides its target (GROUND_SYSTEMS.md §5): a number (the tech tree's recipe) and a code.</summary>
public readonly record struct InteractArgs(uint Arg = 0, string Code = "", string Text = "");

/// <summary>What a verb's gates look at in the one doing it.</summary>
[Flags]
public enum DoerState : byte
{
    None = 0,
    Dead = 1,
    Asleep = 2,
    Wounded = 4,

    /// <summary>In a ship's seat.</summary>
    Seated = 8,
}

/// <summary>
/// Who does a verb: their ID, the state the gates look at, and their team (in one: "authorise team"; its leader invites,
/// while it has room).
/// </summary>
public readonly record struct Doer(EntityId Id, DoerState State, bool InTeam = false, bool LeadsTeam = false, bool TeamFull = false);

/// <summary>
/// Everything a capability's handler needs to offer its verbs and do one (GROUND_SYSTEMS.md §5): the game's data, the
/// target and what is known of it (<see cref="TargetFacts"/>, the same on both sides), who does it, and the building
/// (building rights at a place: the zone's on the server, the client's mirror). On the server it also carries the zone,
/// the player's entity and the thing itself, which <see cref="IInteractCapability.Do"/> acts on; a client has none.
/// </summary>
public readonly struct InteractContext
{
    public GameContent Content { get; init; }

    public TargetRef Target { get; init; }

    public TargetFacts Facts { get; init; }

    public Doer Doer { get; init; }

    public BuildingWorld? Building { get; init; }

    /// <summary>The server's zone (null on a client: nothing is done there).</summary>
    public Zone? Zone { get; init; }

    public Entity Entity { get; init; }

    public Player? Player { get; init; }

    /// <summary>The target's own object (its family's, <see cref="ITargetFamily.Resolve"/>), on the server.</summary>
    public object? Thing { get; init; }

    /// <summary>Whether this viewer is told a flag of the target.</summary>
    public bool Has(ViewerFlags flag) => (Facts.Flags & flag) != 0;
}

/// <summary>
/// A family whose things are taken whole with E (GROUND_SYSTEMS.md §5): the pickup capability's verb hands the thing to
/// the taker through its family (a natural pickup's yield, a bag's stacks, a spent arrow).
/// </summary>
public interface IPickupFamily
{
    Outcome PickUp(Entity entity, Player p, in TargetRef target);
}

/// <summary>
/// A family whose things keep a list of authorised players (the cupboard capability's verbs, GROUND_SYSTEMS.md §5): the
/// tool cupboard's building rights, a ship's crew at its helm (IRON_WAKE.md §5.2). The verbs go to the family's own rules.
/// </summary>
public interface IAuthoriseFamily
{
    Outcome Authorise(Player p, in TargetRef target);

    Outcome Deauthorise(Player p, in TargetRef target);

    Outcome AuthoriseTeam(Player p, in TargetRef target, List<ulong> team);

    Outcome ClearList(Player p, in TargetRef target);
}

/// <summary>
/// A family whose things the hammer mends (GROUND.md §6.3, IRON_WAKE.md §5.5): a building's piece, a ship's door or battery.
/// </summary>
public interface IRepairFamily
{
    Outcome Repair(Entity entity, Player p, in TargetRef target, Building.IBuildingWallet wallet);
}

/// <summary>
/// A capability's interaction side (GROUND_SYSTEMS.md §5): the verbs it offers on a thing that has it (enabled, or with
/// the reason not; which is primary) and how it does one. Offer runs on the server and on every client alike (the radial
/// and the prompt come from it); Do on the server alone, once the verb's gates, reach and sight passed and the offer
/// held it, enabled. Each verb has exactly one handler.
/// </summary>
public interface IInteractCapability
{
    /// <summary>The verbs it offers and does, by their keys in interactions.json.</summary>
    IReadOnlyList<string> Verbs { get; }

    void Offer(in InteractContext ctx, List<InteractOption> into);

    Outcome Do(in InteractContext ctx, int verb, in InteractArgs args);
}

/// <summary>
/// The interaction rules (GROUND_SYSTEMS.md §5), the same on the server and on clients: every verb's handler, found by
/// its number (checked at start: exactly one each); the options a target offers a doer, gated and ranked; which one a
/// tap does.
/// </summary>
public sealed class InteractionRules
{
    private readonly Comparison<InteractOption> _byPriority;

    public InteractionRules(GameContent content, Registry<ICapability> capabilities)
    {
        Content = content;
        Verbs = content.Interactions;
        Capabilities = capabilities;
        var handlers = new Registry<IInteractCapability>("interaction verbs", Verbs.Keys);
        foreach (ICapability capability in capabilities.Handlers)
        {
            if (capability is IInteractCapability handler)
            {
                foreach (string verb in handler.Verbs)
                {
                    handlers.Add(verb, handler);
                }
            }
        }
        Handlers = handlers.Seal();
        _byPriority = (a, b) =>
        {
            int pa = Verbs.Verb(a.Verb)?.Priority ?? 0, pb = Verbs.Verb(b.Verb)?.Priority ?? 0;
            return pa != pb ? pb.CompareTo(pa) : a.Verb.CompareTo(b.Verb);
        };
    }

    public GameContent Content { get; }

    public InteractionData Verbs { get; }

    public Registry<ICapability> Capabilities { get; }

    /// <summary>Each verb's handler, by its number.</summary>
    public Registry<IInteractCapability> Handlers { get; }

    /// <summary>
    /// Every option a target offers a doer now: each of its capabilities' handlers offers its verbs; a verb whose gates
    /// the doer fails says why; ranked by the verbs' priorities (the ring's order).
    /// </summary>
    public void Options(in InteractContext ctx, List<InteractOption> into)
    {
        into.Clear();
        foreach (int capability in ctx.Facts.Capabilities)
        {
            if (Capabilities.Find(capability) is IInteractCapability handler)
            {
                handler.Offer(ctx, into);
            }
        }
        for (int i = 0; i < into.Count; i++)
        {
            if (Verbs.Verb(into[i].Verb) is { } verb && Gate(verb, ctx.Doer) is { Ok: false } gate)
            {
                into[i] = into[i] with { Reason = gate };
            }
        }
        into.Sort(_byPriority);
    }

    /// <summary>
    /// The option a tap does: the first (by priority) its handler marks primary that can be chosen, else the first marked
    /// primary (the tap says why not), else none (−1: a tap does nothing, and holding shows them all).
    /// </summary>
    public static int Primary(IReadOnlyList<InteractOption> options)
    {
        int first = -1;
        for (int i = 0; i < options.Count; i++)
        {
            if (!options[i].Primary)
            {
                continue;
            }
            if (options[i].Enabled)
            {
                return i;
            }
            first = first < 0 ? i : first;
        }
        return first;
    }

    /// <summary>Whether a doer passes a verb's gates: alive and awake (unless it says not), not down or seated (unless it allows).</summary>
    public static Outcome Gate(VerbDef verb, in Doer doer)
    {
        if ((verb.Alive && (doer.State & DoerState.Dead) != 0) || (verb.Awake && (doer.State & DoerState.Asleep) != 0) ||
            (!verb.Wounded && (doer.State & DoerState.Wounded) != 0))
        {
            return OutcomeCode.Dead;
        }
        return !verb.Seated && (doer.State & DoerState.Seated) != 0 ? OutcomeCode.Seated : Outcome.Done;
    }

    /// <summary>The option of a verb among some, or null.</summary>
    public static InteractOption? Find(IReadOnlyList<InteractOption> options, int verb)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Verb == verb)
            {
                return options[i];
            }
        }
        return null;
    }
}
