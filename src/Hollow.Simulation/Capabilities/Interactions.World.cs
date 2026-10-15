using System.Collections.Generic;
using System.Linq;
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
    /// A ship's seat (IRON_WAKE.md §5.2), at the part that names it: sat in with the seat's own verb (board the Tin Finch,
    /// take the helm, man a battery, sit in the copilot's seat), the seat's words for the prompt; refused when it is taken,
    /// when its sitters exclude the doer (its crew alone; strangers while the ship is locked), when its station is
    /// disabled, and through a door, while that door is shut or the ship isn't at rest. A tap's once its door lets them in.
    /// </summary>
    private sealed class SeatCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;

        public SeatCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ShipSeatDef.Verbs.Select(v => (v, (VerbAction)Sit)).ToArray());
        }

        public string Key => "seat";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            TargetFacts f = ctx.Facts;
            if (f.Ship is not { } ship || ctx.Target.Part >= ship.Parts.Count || ship.Parts[ctx.Target.Part].Seat is not (>= 0 and var index))
            {
                return;
            }
            ShipSeatDef seat = ship.Seats[index];
            bool crew = ctx.Has(ViewerFlags.Authorised);
            Outcome why = f.Busy ? OutcomeCode.SeatTaken
                : seat.Sitters == SeatSitters.Crew && !crew ? OutcomeCode.NotCrew
                : seat.Sitters == SeatSitters.Unlocked && ctx.Has(ViewerFlags.Locked) && !crew ? OutcomeCode.Locked
                : f.Broken ? OutcomeCode.Disabled
                : seat.Door >= 0 && !f.DoorReady ? OutcomeCode.HatchShut
                : seat.Door >= 0 && !f.Still ? OutcomeCode.ShipMoving
                : Outcome.Done;
            into.Add(new InteractOption(seat.Verb, why, Primary: seat.Door < 0 || f.DoorReady, Name: seat.Name));
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Sit(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Ship ship && ctx.Target.Part < ship.Def.Parts.Count && ship.Def.Parts[ctx.Target.Part].Seat is >= 0 and var seat
                ? ctx.Zone!.Ships().Sit(ctx.Entity, ctx.Player!, ship, seat) : OutcomeCode.NotFound;
    }

    /// <summary>
    /// A ship's door (IRON_WAKE.md §5.3), at the parts that name it (the ramp's leaves and its switch, the airlock, the Tin
    /// Finch's hatch): opened as its rule allows (at rest, slow and low, any time) by anyone the ship's lock lets through,
    /// shut by anyone; jammed open while broken. The ship's crew lock and unlock its doors there.
    /// </summary>
    private sealed class ShipDoorCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _open, _close, _lock, _unlock;

        public ShipDoorCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("hatch-open", (in InteractContext c, in InteractArgs a) => Door(c, true)),
                ("hatch-close", (in InteractContext c, in InteractArgs a) => Door(c, false)),
                ("ship-lock", (in InteractContext c, in InteractArgs a) => Lock(c, true)), ("ship-unlock", (in InteractContext c, in InteractArgs a) => Lock(c, false)));
            (_open, _close, _lock, _unlock) = (_verbs["hatch-open"], _verbs["hatch-close"], _verbs["ship-lock"], _verbs["ship-unlock"]);
        }

        public string Key => "ship-door";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            TargetFacts f = ctx.Facts;
            if (f.Ship is not { } ship || ctx.Target.Part >= ship.Parts.Count || ship.Parts[ctx.Target.Part].Door is not (>= 0 and var index))
            {
                return;
            }
            string name = ship.Doors[index].Name;
            bool crew = ctx.Has(ViewerFlags.Authorised), locked = ctx.Has(ViewerFlags.Locked);
            if (f.Jammed)
            {
                into.Add(InteractOption.Cannot(_close, OutcomeCode.Jammed, primary: true, name: name));
            }
            else if (f.Open)
            {
                into.Add(InteractOption.Can(_close, primary: true, name: name));
            }
            else
            {
                Outcome why = locked && !crew ? OutcomeCode.Locked : f.DoorRule != OutcomeCode.Done ? f.DoorRule : Outcome.Done;
                into.Add(new InteractOption(_open, why, Primary: true, Name: name));
            }
            if (crew)
            {
                into.Add(InteractOption.Can(locked ? _unlock : _lock, name: ship.Name));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Door(in InteractContext ctx, bool open) =>
            ctx.Thing is Ship ship && ctx.Target.Part < ship.Def.Parts.Count && ship.Def.Parts[ctx.Target.Part].Door is >= 0 and var door
                ? ctx.Zone!.Ships().SetDoorOnFoot(ctx.Player!, ship, door, open) : OutcomeCode.NotFound;

        private static Outcome Lock(in InteractContext ctx, bool locked) =>
            ctx.Thing is Ship ship ? ctx.Zone!.Ships().SetShipLock(ctx.Player!, ship, locked) : OutcomeCode.NotFound;
    }

    /// <summary>A ship's tank, at its fuel cap: filled from the player's fuel (lamp oil).</summary>
    private sealed class FuelCapability : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _refuel;

        public FuelCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("refuel", Refuel));
            _refuel = _verbs["refuel"];
        }

        public string Key => "fuel";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into) => into.Add(InteractOption.Can(_refuel, primary: true, name: ctx.Facts.Ship?.Name));

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Refuel(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Ship ship ? ctx.Zone!.Ships().Refuel(ctx.Player!, ship) : OutcomeCode.NotFound;
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
