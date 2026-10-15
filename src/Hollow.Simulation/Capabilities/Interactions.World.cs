using System.Collections.Generic;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation.Capabilities;

// The interaction side of people, explosives, ships and spent arrows (GROUND_SYSTEMS.md §5): helping the wounded up, a
// team invite, a dud lit again, a ship's hatch, seat, tank and lock; and what becomes of an arrow when what it is stuck in
// goes.
public static partial class CapabilityHandlers
{
    /// <summary>A player down (the wounded state, GROUND.md §6.4): another helps them up, E's primary.</summary>
    private sealed class WoundedCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _help;

        public WoundedCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("help-up", HelpUp));
            _help = _verbs["help-up"];
        }

        public string Key => "wounded";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into) => into.Add(InteractOption.Can(_help, primary: true));

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome HelpUp(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Player down ? ctx.Zone!.Wounded().HelpUp(ctx.Entity, down) : OutcomeCode.NotFound;
    }

    /// <summary>A player up and about: the leader of a team invites them into it (Rust's invite by looking at them).</summary>
    private sealed class CharacterCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _invite;

        public CharacterCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("invite", Invite));
            _invite = _verbs["invite"];
        }

        public string Key => "character";

        public ViewerFlags Flags(object thing, in Viewer viewer) =>
            thing is Player p && viewer.IsTeammate(p.Id.Value) ? ViewerFlags.Teammate : ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            if (ctx.Doer.LeadsTeam && !ctx.Has(ViewerFlags.Teammate))
            {
                into.Add(ctx.Doer.TeamFull ? InteractOption.Cannot(_invite, OutcomeCode.TeamFull, primary: true) : InteractOption.Can(_invite, primary: true));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Invite(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Player invited ? ctx.Zone!.World().Invite(ctx.Player!, invited) : OutcomeCode.NotFound;
    }

    /// <summary>An explosive whose fuse went out (GROUND.md §6.6): anyone lights it again, a new fuse that may go out too.</summary>
    private sealed class DudCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _relight;

        public DudCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("relight", Relight));
            _relight = _verbs["relight"];
        }

        public string Key => "dud";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into) => into.Add(InteractOption.Can(_relight, primary: true));

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Relight(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is LiveExplosive { Dud: true } dud ? ctx.Zone!.Explosives().Relight(dud) : OutcomeCode.NotUsable;
    }

    /// <summary>
    /// A ship on the ground (GROUND.md §6.7), by its parts: at its hatch, the hatch opened (at rest, and by its owner when
    /// locked) and shut, the seat boarded through the open hatch; at its fuel cap, the tank filled from lamp oil; its owner
    /// locks and unlocks it. Its cargo, through the open hatch, is a container's. The pilot's keys in the seat stay the
    /// ship's own intent.
    /// </summary>
    private sealed partial class ShipCapability : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _open, _close, _board, _refuel, _lock, _unlock;

        public ShipCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("hatch-open", (in InteractContext c, in InteractArgs a) => Hatch(c, true)),
                ("hatch-close", (in InteractContext c, in InteractArgs a) => Hatch(c, false)), ("board", Board), ("refuel", Refuel),
                ("ship-lock", (in InteractContext c, in InteractArgs a) => Lock(c, true)), ("ship-unlock", (in InteractContext c, in InteractArgs a) => Lock(c, false)));
            (_open, _close, _board) = (_verbs["hatch-open"], _verbs["hatch-close"], _verbs["board"]);
            (_refuel, _lock, _unlock) = (_verbs["refuel"], _verbs["ship-lock"], _verbs["ship-unlock"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            TargetFacts f = ctx.Facts;
            bool atHatch = ctx.Target.Part is ShipParts.Hull or ShipParts.Hatch, lockedOut = ctx.Has(ViewerFlags.Locked) && !ctx.Has(ViewerFlags.Mine);
            // The hatch: opened at rest (by its owner when locked), shut by anyone; the seat through it, a tap's once it is open.
            into.Add(f.Open ? InteractOption.Can(_close)
                : new InteractOption(_open, !f.Still ? OutcomeCode.ShipMoving : lockedOut ? OutcomeCode.Locked : Outcome.Done, Primary: atHatch));
            Outcome seat = f.Busy ? OutcomeCode.SeatTaken : lockedOut ? OutcomeCode.Locked : !f.Open ? OutcomeCode.HatchShut
                : !f.Still ? OutcomeCode.ShipMoving : Outcome.Done;
            into.Add(new InteractOption(_board, seat, Primary: atHatch && f.Open));
            into.Add(InteractOption.Can(_refuel, primary: ctx.Target.Part == ShipParts.FuelCap));
            if (ctx.Has(ViewerFlags.Mine))
            {
                into.Add(InteractOption.Can(ctx.Has(ViewerFlags.Locked) ? _unlock : _lock));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Hatch(in InteractContext ctx, bool open) =>
            ctx.Thing is Ship ship ? ctx.Zone!.Ships().SetHatch(ctx.Player!, ship, open) : OutcomeCode.NotFound;

        private static Outcome Board(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Ship ship ? ctx.Zone!.Ships().BoardShip(ctx.Entity, ctx.Player!, ship) : OutcomeCode.NotFound;

        private static Outcome Refuel(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Ship ship ? ctx.Zone!.Ships().Refuel(ctx.Player!, ship) : OutcomeCode.NotFound;

        private static Outcome Lock(in InteractContext ctx, bool locked) =>
            ctx.Thing is Ship ship ? ctx.Zone!.Ships().SetShipLock(ctx.Player!, ship, locked) : OutcomeCode.NotFound;
    }

    /// <summary>
    /// A spent arrow stuck where it hit (GROUND_SYSTEMS.md §5; taken back by the pickup capability): when what holds it
    /// goes (a tree felled, a box broken, a body gone), it falls to the ground below where it was.
    /// </summary>
    private sealed class ArrowCapability : ICapability, IDependentCapability
    {
        public string Key => "arrow";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public void SupportLost(Zone zone, in TargetRef dependent, object thing)
        {
            if (thing is SpentArrow arrow)
            {
                zone.Arrows().ArrowFalls(arrow);
            }
        }
    }
}
