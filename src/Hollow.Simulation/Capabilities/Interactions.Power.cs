using System.Collections.Generic;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.World;

namespace Hollow.Simulation.Capabilities;

public static partial class CapabilityHandlers
{
    /// <summary>One electrical capability on every generator, consumer or storage object.</summary>
    private sealed class ElectricCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _enable, _disable;

        public ElectricCapability(InteractionData data)
        {
            _verbs = new VerbActions(data,
                ("power-output", Retired),
                ("power-connect", Retired),
                ("power-disconnect", Retired),
                ("power-enable", (in InteractContext c, in InteractArgs a) => c.Thing is Deployable d ? c.Zone!.Power().SetEnabled(c.Doer.Id, d, true) : OutcomeCode.NotFound),
                ("power-disable", (in InteractContext c, in InteractArgs a) => c.Thing is Deployable d ? c.Zone!.Power().SetEnabled(c.Doer.Id, d, false) : OutcomeCode.NotFound));
            (_enable, _disable) = (_verbs["power-enable"], _verbs["power-disable"]);
        }

        public string Key => "electric";
        public IReadOnlyList<string> Verbs => _verbs.Keys;
        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            if (ctx.Facts.Deployable?.Power == null || ctx.Facts.DeviceAccess.Present) return;
            Privilege privilege = ctx.Building?.PrivilegeAt(ctx.Doer.Id.Value, ctx.Facts.Position) ?? Privilege.None;
            Outcome allowed = privilege == Privilege.Blocked ? OutcomeCode.BuildingBlocked
                : ctx.Has(ViewerFlags.Mine) || privilege == Privilege.Allowed ? Outcome.Done : OutcomeCode.NotOwner;
            into.Add(new InteractOption(_enable, allowed));
            into.Add(new InteractOption(_disable, allowed));
        }

        private static Outcome Retired(in InteractContext ctx, in InteractArgs args) => OutcomeCode.NotUsable;

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);
    }
}
