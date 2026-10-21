using System.Collections.Generic;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Simulation.Building;

namespace Hollow.Simulation.Capabilities;

public static partial class CapabilityHandlers
{
    private sealed class GateCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _open, _close;
        public GateCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("gate-open", Open), ("gate-close", Close));
            (_open, _close) = (_verbs["gate-open"], _verbs["gate-close"]);
        }
        public string Key => "gate";
        public ViewerFlags Flags(object thing, in Viewer viewer) =>
            thing is ILockHost host && BuildingWorld.CanUse(viewer.Id.Value, host, viewer.Keys) ? ViewerFlags.CanUse : ViewerFlags.None;
        public IReadOnlyList<string> Verbs => _verbs.Keys;
        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            int verb = ctx.Facts.Open ? _close : _open;
            into.Add(ctx.Has(ViewerFlags.CanUse) ? InteractOption.Can(verb, primary: true) : InteractOption.Cannot(verb, OutcomeCode.Locked, primary: true));
        }
        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);
        private static Outcome Open(in InteractContext ctx, in InteractArgs args) => Set(ctx, true);
        private static Outcome Close(in InteractContext ctx, in InteractArgs args) => Set(ctx, false);
        private static Outcome Set(in InteractContext ctx, bool open) => ctx.Thing is Deployable d
            ? ctx.Zone!.Require<CompoundInteractionSystem>().SetOpen(ctx.Doer.Id.Value, d, open, ctx.Zone!.Building().KeysOf(ctx.Player!)) : OutcomeCode.NotFound;
    }
}
