using System.Collections.Generic;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
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
            bool opens = ctx.Building?.DoorOf(new EntityId(ctx.Target.Id))?.Def.Opens ?? true;
            if (opens)
            {
                int swing = ctx.Facts.Open ? _close : _open;
                into.Add(ctx.Has(ViewerFlags.CanUse) ? InteractOption.Can(swing, primary: true) : InteractOption.Cannot(swing, OutcomeCode.Locked, primary: true));
            }
            if (ctx.Facts.Lock == LockKind.None)
            {
                Outcome why = opens && !ctx.Facts.Open ? OutcomeCode.OpenItFirst
                    : ctx.Building?.PrivilegeAt(ctx.Doer.Id.Value, ctx.Facts.Position) == Privilege.Blocked ? OutcomeCode.BuildingBlocked
                    : Outcome.Done;
                into.Add(new InteractOption(_pickUp, why, Primary: !opens));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Swing(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.Building().World!.Toggle(ctx.Doer.Id.Value, door, ctx.Zone!.Building().KeysOf(ctx.Player!)) : OutcomeCode.NotFound;

        private static Outcome PickUp(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Door door ? ctx.Zone!.Building().TakeDoor(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;
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
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().World!.SetLocked(ctx.Doer.Id.Value, door, locked, ctx.Zone!.Building().KeysOf(ctx.Player!)) : OutcomeCode.NotFound;

        private static Outcome Cut(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().CutKey(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;

        private static Outcome Remove(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().TakeLock(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;
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
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().World!.SetCode(ctx.Doer.Id.Value, door, args.Code) : OutcomeCode.NotFound;

        private static Outcome EnterCode(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().World!.EnterCode(ctx.Doer.Id.Value, door, args.Code, ctx.Zone!.Building().BuildingNow) : OutcomeCode.NotFound;

        private static Outcome Turn(in InteractContext ctx, bool locked) =>
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().World!.SetLocked(ctx.Doer.Id.Value, door, locked) : OutcomeCode.NotFound;

        private static Outcome Team(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().World!.AuthoriseOnLock(ctx.Doer.Id.Value, door, ctx.Zone!.World().TeamOf(ctx.Player!)) : OutcomeCode.NotFound;

        private static Outcome Remove(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ILockHost door ? ctx.Zone!.Building().TakeLock(ctx.Entity, ctx.Player!, door) : OutcomeCode.NotFound;
    }

    /// <summary>
    /// A list of authorised players: the tool cupboard (GROUND.md §6.3) and a ship's helm (IRON_WAKE.md §5.2, the ship is
    /// its own cupboard). Anyone authorises at it (E's primary until they are on its list); those on it deauthorise, clear
    /// the list and put their team on it; a cupboard (a deployable) is picked up with its store. The list is its family's
    /// (<see cref="IAuthoriseFamily"/>).
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
            into.Add(InteractOption.Can(listed ? _deauthorise : _authorise, primary: !listed && ctx.Facts.Deployable != null));
            if (ctx.Doer.InTeam)
            {
                into.Add(InteractOption.Can(_team));
            }
            if (listed)
            {
                into.Add(InteractOption.Can(_clear));
                if (ctx.Facts.Deployable != null)
                {
                    into.Add(InteractOption.Can(_pickUp));
                }
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static IAuthoriseFamily? List(in InteractContext ctx) => ctx.Zone!.Targets().FamilyOf(ctx.Target.Family) as IAuthoriseFamily;

        private static Outcome Authorise(in InteractContext ctx, in InteractArgs args) =>
            List(ctx) is { } list ? list.Authorise(ctx.Player!, ctx.Target) : OutcomeCode.NotUsable;

        private static Outcome Deauthorise(in InteractContext ctx, in InteractArgs args) =>
            List(ctx) is { } list ? list.Deauthorise(ctx.Player!, ctx.Target) : OutcomeCode.NotUsable;

        private static Outcome Team(in InteractContext ctx, in InteractArgs args) =>
            List(ctx) is { } list ? list.AuthoriseTeam(ctx.Player!, ctx.Target, ctx.Zone!.World().TeamOf(ctx.Player!)) : OutcomeCode.NotUsable;

        private static Outcome Clear(in InteractContext ctx, in InteractArgs args) =>
            List(ctx) is { } list ? list.ClearList(ctx.Player!, ctx.Target) : OutcomeCode.NotUsable;

        private static Outcome PickUp(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is ClaimDevice device ? ctx.Zone!.Building().TakeClaim(ctx.Entity, ctx.Player!, device) : OutcomeCode.NotFound;
    }
}
