using Hollow.Net.Protocol;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

// The one interaction intent (GROUND_SYSTEMS.md §5): a verb done to a target, with a number (a tech tree's recipe) and a
// code (a code lock's digits) where the verb takes them. It returns its sequence number (0 if it couldn't be sent); the
// answer comes as every intent's does (IntentAnswered, and IntentRejected when refused), and what it changed arrives with
// the state it changed (a door, the loot, the inventory). A verb that opens a panel (the tech tree) is the interface's own:
// it is never sent.
public sealed partial class LocalPlayer
{
    /// <summary>Does a verb (interactions.json's number) to a target.</summary>
    public uint Interact(in TargetRef target, int verb, uint arg = 0, string? code = null) => Interact(target, verb, arg, code, null);

    /// <summary>Does a verb, named by its key in interactions.json.</summary>
    public uint Interact(in TargetRef target, string verb, uint arg = 0, string? code = null) =>
        Interact(target, Content.Interactions.Index(verb), arg, code, null);

    // An interaction, with what it changes shown at once where the client knows it (an unlock's blueprint and salvage).
    private uint Interact(in TargetRef target, int verb, uint arg, string? code, Pending? shown)
    {
        if (Content.Interactions.Verb(verb) is not { Panel: null })
        {
            return 0;
        }
        TargetNet named = NetConvert.ToNet(target);
        string digits = code ?? "";
        return Intend(shown, w => new InteractIntent { Seq = _nextSeq, Target = named, Verb = (uint)verb, Arg = arg, Code = digits }.WriteMessage(w));
    }
}
