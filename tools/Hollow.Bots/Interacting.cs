using System.Collections.Generic;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;

namespace Hollow.Bots;

/// <summary>
/// The interact key, as a bot presses it (GROUND_SYSTEMS.md §5): a tap does a target's primary option from where the bot's
/// eye is, as the player's prompt shows it; a held E chooses any option by its verb.
/// </summary>
internal static class Interacting
{
    [System.ThreadStatic]
    private static List<InteractOption>? _options;

    private static Vector3 Eye(GameClient client) => client.Character.Position + client.Frame.UpAt(client.Character.Position) * client.EyeHeight;

    /// <summary>A tap of E on a target: its primary option, if it has one that can be chosen (0: nothing sent).</summary>
    public static uint Tap(GameClient client, in TargetRef target)
    {
        List<InteractOption> options = _options ??= new List<InteractOption>();
        if (!client.Interaction().Options(target, Eye(client), options, out _) || InteractionRules.Primary(options) is not (>= 0 and var primary) || !options[primary].Enabled)
        {
            return 0;
        }
        return client.Local.Interact(target, options[primary].Verb);
    }

    /// <summary>A thing in the entity store (a deployable, a prop, a bag) as a target.</summary>
    public static TargetRef Thing(Hollow.Simulation.Entities.EntityId id) => new(TargetFamily.Thing, id.Value);

    /// <summary>A player as a target.</summary>
    public static TargetRef Character(Hollow.Simulation.Entities.EntityId id) => new(TargetFamily.Character, id.Value);
}
