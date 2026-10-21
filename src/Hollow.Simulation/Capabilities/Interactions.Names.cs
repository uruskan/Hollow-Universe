using System.Collections.Generic;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Capabilities;

public interface ICustomNamed
{
    string CustomName { get; }
    void Rename(string name);
}

public static partial class CapabilityHandlers
{
    private sealed class Nameable : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _rename;
        public Nameable(InteractionData data)
        {
            _verbs = new VerbActions(data, ("rename", Rename));
            _rename = _verbs["rename"];
        }
        public string Key => "nameable";
        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;
        public IReadOnlyList<string> Verbs => _verbs.Keys;
        public void Offer(in InteractContext ctx, List<InteractOption> into) =>
            into.Add(new InteractOption(_rename, ctx.Has(ViewerFlags.Mine) ? Outcome.Done : OutcomeCode.NotOwner, Primary: true));
        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);
        private static Outcome Rename(in InteractContext ctx, in InteractArgs args)
        {
            if (!ctx.Has(ViewerFlags.Mine)) return OutcomeCode.NotOwner;
            if (ctx.Thing is not ICustomNamed thing) return OutcomeCode.NotUsable;
            if (!Names.TryNormalize(args.Text, out string name)) return OutcomeCode.Invalid;
            thing.Rename(name);
            ctx.Player!.MapRevision++;
            return Outcome.Done;
        }
    }
}
