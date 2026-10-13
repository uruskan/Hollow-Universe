using System.Collections.Generic;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Capabilities;

// The interaction side of the building's capabilities (GROUND_SYSTEMS.md §5, GROUND.md §6.3): doors, their key and code
// locks, the tool cupboard. What each offers comes from what the server tells the viewer of the door or cupboard (its
// viewer flags, §3.3), so a client offers exactly what the server will do; what each does is the building rules'.
public static partial class CapabilityHandlers
{
    /// <summary>A door: opened and shut by whoever its lock lets through; picked up open, without a lock, with building rights.</summary>
    private sealed partial class DoorCapability : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _open, _close, _pickUp;

        public DoorCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("door-open", Swing), ("door-close", Swing), ("door-pick-up", PickUp));
            (_open, _close, _pickUp) = (_verbs["door-open"], _verbs["door-close"], _verbs["door-pick-up"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            int swing = ctx.Facts.Open ? _close : _open;
            into.Add(ctx.Has(ViewerFlags.CanUse) ? InteractOption.Can(swing, primary: true) : InteractOption.Cannot(swing, OutcomeCode.Locked, primary: true));
            if (ctx.Facts.Lock == LockKind.None)
            {
                Outcome why = !ctx.Facts.Open ? OutcomeCode.OpenItFirst
                    : ctx.Building?.PrivilegeAt(ctx.Doer.Id.Value, ctx.Facts.Position) == Privilege.Blocked ? OutcomeCode.BuildingBlocked
                    : Outcome.Done;
                into.Add(new InteractOption(_pickUp, why));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Swing(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.Building!.Toggle(ctx.Doer.Id.Value, door, ctx.Zone.KeysOf(ctx.Player!)) : OutcomeCode.NotFound;

        private static Outcome PickUp(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.TakeDoor(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;
    }

    /// <summary>
    /// A key lock (Rust's): its owner, or anyone carrying its key, locks and unlocks it; its owner cuts keys for wood and
    /// takes it off while it is unlocked.
    /// </summary>
    private sealed partial class KeyLockCapability : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _lock, _unlock, _cut, _remove;

        public KeyLockCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("key-lock", (in InteractContext c, in InteractArgs a) => Turn(c, true)),
                ("key-unlock", (in InteractContext c, in InteractArgs a) => Turn(c, false)), ("cut-key", Cut), ("key-remove", Remove));
            (_lock, _unlock, _cut, _remove) = (_verbs["key-lock"], _verbs["key-unlock"], _verbs["cut-key"], _verbs["key-remove"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            bool locked = ctx.Has(ViewerFlags.Locked);
            int turn = locked ? _unlock : _lock;
            into.Add(ctx.Has(ViewerFlags.CanUse) ? InteractOption.Can(turn) : InteractOption.Cannot(turn, OutcomeCode.NeedsKey));
            if (ctx.Has(ViewerFlags.Mine))
            {
                into.Add(InteractOption.Can(_cut));
                into.Add(locked ? InteractOption.Cannot(_remove, OutcomeCode.UnlockItFirst) : InteractOption.Can(_remove));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Turn(in InteractContext ctx, bool locked) =>
            ctx.Thing is Door door ? ctx.Zone!.Building!.SetLocked(ctx.Doer.Id.Value, door, locked, ctx.Zone.KeysOf(ctx.Player!)) : OutcomeCode.NotFound;

        private static Outcome Cut(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.CutKey(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;

        private static Outcome Remove(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.TakeLock(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;
    }

    /// <summary>
    /// A code lock (RustMVP's): a fresh one's owner sets its code first (E's primary: the keypad asks); with a code, a
    /// stranger at the locked door enters it; those on its list lock, unlock, change the code (unlocked), put their team
    /// on the list and take it off (unlocked).
    /// </summary>
    private sealed partial class CodeLockCapability : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _set, _enter, _change, _lock, _unlock, _team, _remove;

        public CodeLockCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("set-code", SetCode), ("enter-code", EnterCode), ("change-code", SetCode),
                ("code-lock", (in InteractContext c, in InteractArgs a) => Turn(c, true)), ("code-unlock", (in InteractContext c, in InteractArgs a) => Turn(c, false)),
                ("code-team", Team), ("code-remove", Remove));
            (_set, _enter, _change) = (_verbs["set-code"], _verbs["enter-code"], _verbs["change-code"]);
            (_lock, _unlock, _team, _remove) = (_verbs["code-lock"], _verbs["code-unlock"], _verbs["code-team"], _verbs["code-remove"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            bool listed = ctx.Has(ViewerFlags.Authorised), coded = ctx.Has(ViewerFlags.HasCode), locked = ctx.Has(ViewerFlags.Locked);
            if (!listed)
            {
                if (coded && locked)
                {
                    into.Add(InteractOption.Can(_enter, primary: true));
                }
                return;
            }
            if (!coded)
            {
                into.Add(InteractOption.Can(_set, primary: true));
                into.Add(InteractOption.Cannot(_lock, OutcomeCode.NoCode));
            }
            else
            {
                into.Add(InteractOption.Can(locked ? _unlock : _lock));
                into.Add(locked ? InteractOption.Cannot(_change, OutcomeCode.UnlockItFirst) : InteractOption.Can(_change));
            }
            if (ctx.Doer.InTeam)
            {
                into.Add(InteractOption.Can(_team));
            }
            into.Add(locked ? InteractOption.Cannot(_remove, OutcomeCode.UnlockItFirst) : InteractOption.Can(_remove));
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        // Setting a code, or changing it: four digits, unlocked; it locks, and lists only the setter.
        private static Outcome SetCode(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.Building!.SetCode(ctx.Doer.Id.Value, door, args.Code) : OutcomeCode.NotFound;

        private static Outcome EnterCode(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.Building!.EnterCode(ctx.Doer.Id.Value, door, args.Code, ctx.Zone.BuildingNow) : OutcomeCode.NotFound;

        private static Outcome Turn(in InteractContext ctx, bool locked) =>
            ctx.Thing is Door door ? ctx.Zone!.Building!.SetLocked(ctx.Doer.Id.Value, door, locked) : OutcomeCode.NotFound;

        private static Outcome Team(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.Building!.AuthoriseOnLock(ctx.Doer.Id.Value, door, ctx.Zone.TeamOf(ctx.Player!)) : OutcomeCode.NotFound;

        private static Outcome Remove(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.TakeLock(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;
    }

    /// <summary>
    /// The tool cupboard (GROUND.md §6.3): anyone authorises at it (E's primary until they are on its list, then its store
    /// opens); those on it deauthorise, clear the list, put their team on it and pick it up with its store.
    /// </summary>
    private sealed partial class CupboardCapability : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _authorise, _deauthorise, _team, _clear, _pickUp;

        public CupboardCapability(InteractionData data)
        {
            _verbs = new VerbActions(data, ("authorise", Authorise), ("deauthorise", Deauthorise), ("authorise-team", Team), ("clear-list", Clear),
                ("cupboard-pick-up", PickUp));
            (_authorise, _deauthorise, _team) = (_verbs["authorise"], _verbs["deauthorise"], _verbs["authorise-team"]);
            (_clear, _pickUp) = (_verbs["clear-list"], _verbs["cupboard-pick-up"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            bool listed = ctx.Has(ViewerFlags.Authorised);
            into.Add(InteractOption.Can(listed ? _deauthorise : _authorise, primary: !listed));
            if (ctx.Doer.InTeam)
            {
                into.Add(InteractOption.Can(_team));
            }
            if (listed)
            {
                into.Add(InteractOption.Can(_clear));
                into.Add(InteractOption.Can(_pickUp));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Authorise(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ClaimDevice device ? ctx.Zone!.Building!.Authorise(ctx.Doer.Id.Value, device) : OutcomeCode.NotFound;

        private static Outcome Deauthorise(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ClaimDevice device ? ctx.Zone!.Building!.Deauthorise(ctx.Doer.Id.Value, device) : OutcomeCode.NotFound;

        private static Outcome Team(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ClaimDevice device ? ctx.Zone!.AuthoriseTeam(ctx.Doer.Id.Value, device, ctx.Zone.TeamOf(ctx.Player!)) : OutcomeCode.NotFound;

        private static Outcome Clear(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ClaimDevice device ? ctx.Zone!.Building!.ClearAuthorised(ctx.Doer.Id.Value, device) : OutcomeCode.NotFound;

        private static Outcome PickUp(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ClaimDevice device ? ctx.Zone!.TakeClaim(ctx.Entity, ctx.Player!, device) : OutcomeCode.NotFound;
    }
}
