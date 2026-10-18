using System.Collections.Generic;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Simulation.Authorization;

namespace Hollow.Simulation.Capabilities;

public static partial class CapabilityHandlers
{
    private sealed class DefenseCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        public DefenseCapability(InteractionData data) => _verbs = new VerbActions(data,
            ("defense-on", (in InteractContext c, in InteractArgs a) => Switch(c, true)),
            ("defense-off", (in InteractContext c, in InteractArgs a) => Switch(c, false)), ("defense-open", Open),
            ("defense-authorise", (in InteractContext c, in InteractArgs a) => c.Thing is Deployable d
                ? c.Zone!.DeviceAuthorization().AuthorizeSelf(d, c.Doer.Id) : OutcomeCode.NotFound),
            ("defense-deauthorise", (in InteractContext c, in InteractArgs a) => c.Thing is Deployable d
                ? c.Zone!.DeviceAuthorization().ClearSelf(d, c.Doer.Id) : OutcomeCode.NotFound),
            ("defense-clear", (in InteractContext c, in InteractArgs a) => c.Thing is Deployable d
                ? c.Zone!.DeviceAuthorization().ClearAll(d, c.Doer.Id) : OutcomeCode.NotFound));
        public string Key => "defense";
        public IReadOnlyList<string> Verbs => _verbs.Keys;
        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;
        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            DeviceAccessFacts access = ctx.Facts.DeviceAccess;
            if (!access.Present) return;
            Offer(access.Enabled ? "defense-off" : "defense-on", access.CanManage, into);
            Offer("defense-open", access.CanManage, into);
            Offer(access.Friendly ? "defense-deauthorise" : "defense-authorise", access.Friendly || access.CanManage, into);
            Offer("defense-clear", access.CanManage, into);
        }
        private void Offer(string key, bool allowed, List<InteractOption> into) => into.Add(allowed
            ? InteractOption.Can(_verbs[key]) : InteractOption.Cannot(_verbs[key], OutcomeCode.NotAuthorised));
        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);
        private static Outcome Switch(in InteractContext ctx, bool enabled) => ctx.Thing is Deployable d
            ? ctx.Zone!.Defenses().SetEnabled(d, ctx.Doer.Id, enabled) : OutcomeCode.NotFound;
        private static Outcome Open(in InteractContext ctx, in InteractArgs args) => ctx.Thing is Deployable d &&
            ctx.Zone!.Defenses().CanManage(d, ctx.Doer.Id)
                ? ctx.Zone!.Interaction().OpenLoot(ctx.Player!, d.Id) : OutcomeCode.NotAuthorised;
    }
}
