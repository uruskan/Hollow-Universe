using System.Collections.Generic;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Capabilities;

public static partial class CapabilityHandlers
{
    // The existing ship capability is present on its hull and every part. Hold-E therefore offers the same
    // engine switch at the helm or elsewhere aboard; authorization, reach and sight use the shared interaction gates.
    private sealed partial class ShipCapability : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _on, _off, _fuel;
        public ShipCapability(InteractionData data)
        {
            _verbs = new VerbActions(data,
                ("engine-on", (in InteractContext c, in InteractArgs a) => Engine(c, true)),
                ("engine-off", (in InteractContext c, in InteractArgs a) => Engine(c, false)),
                ("open-fuel", OpenFuel));
            (_on, _off) = (_verbs["engine-on"], _verbs["engine-off"]);
            _fuel = _verbs["open-fuel"];
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;
        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            if (ctx.Has(ViewerFlags.Authorised))
            {
                into.Add(InteractOption.Can(ctx.Has(ViewerFlags.Lit) ? _off : _on));
                into.Add(InteractOption.Can(_fuel));
            }
        }
        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);
        private static Outcome Engine(in InteractContext ctx, bool on) => ctx.Thing is Ship ship
            ? ctx.Zone!.Ships().SetEngine(ctx.Player!, ship, on) : OutcomeCode.NotFound;
        private static Outcome OpenFuel(in InteractContext ctx, in InteractArgs args) => ctx.Thing is Ship ship
            ? ctx.Zone!.Ships().OpenFuel(ctx.Player!, ship, ctx.Target.Part) : OutcomeCode.NotFound;
    }
}
