using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation.Capabilities;

/// <summary>What a verb does, on the server: the context's zone, doer and thing.</summary>
internal delegate Outcome VerbAction(in InteractContext ctx, in InteractArgs args);

/// <summary>
/// A handler's verbs (GROUND_SYSTEMS.md §5): their numbers by key (interactions.json, resolved once; a key the data lacks
/// is −1, which the verbs' registry names) and what each does, found by its number, so no handler chooses among its
/// verbs by testing them.
/// </summary>
internal sealed class VerbActions
{
    private readonly Dictionary<int, VerbAction> _actions = new();
    private readonly InteractionData _data;

    public VerbActions(InteractionData data, params (string Key, VerbAction Act)[] verbs)
    {
        _data = data;
        Keys = verbs.Select(v => v.Key).ToArray();
        foreach ((string key, VerbAction act) in verbs)
        {
            _actions.TryAdd(data.Index(key), act);
        }
    }

    /// <summary>The verbs, by key (the registry gives each its one handler).</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>A verb's number, by key.</summary>
    public int this[string key] => _data.Index(key);

    public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) =>
        _actions.TryGetValue(verb, out VerbAction? act) ? act(ctx, args) : OutcomeCode.NotUsable;
}

// The interaction side of the things' capabilities (GROUND_SYSTEMS.md §5): what holds items, what is taken whole, a
// deployable picked back up, a fire and a machine switched on and off, a workbench's tree.
public static partial class CapabilityHandlers
{
    /// <summary>Holds items: looted (a body, a sleeper, a player down) or opened (anything else: a box, a bag, a crate, a device, the cupboard's store, a ship's cargo).</summary>
    private sealed partial class Holds : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _open, _loot;

        public Holds(InteractionData data)
        {
            _verbs = new VerbActions(data, ("open", Open), ("loot", Open));
            (_open, _loot) = (_verbs["open"], _verbs["loot"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        // Looking inside is what E does to anything that holds items; a container locked by its owner opens for them alone
        // (and those on its list).
        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            int verb = ctx.Facts.Body ? _loot : _open;
            bool lockedOut = ctx.Has(ViewerFlags.Locked) && !ctx.Has(ViewerFlags.Mine) && !ctx.Has(ViewerFlags.Authorised);
            into.Add(lockedOut ? InteractOption.Cannot(verb, OutcomeCode.Locked, primary: true) : InteractOption.Can(verb, primary: true));
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Open(in InteractContext ctx, in InteractArgs args) => ctx.Zone!.Interaction().OpenLoot(ctx.Player!, new EntityId(ctx.Target.Id));
    }

    /// <summary>Taken whole with E: a natural pickup, a berry bush (picked bare), a bag of dropped items (all that fits), a spent arrow.</summary>
    private sealed class Pickup : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _pickUp, _pick;

        public Pickup(InteractionData data)
        {
            _verbs = new VerbActions(data, ("pick-up", Take), ("pick", Take));
            (_pickUp, _pick) = (_verbs["pick-up"], _verbs["pick"]);
        }

        public string Key => "pickup";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        // A tap takes it, unless it is a bag of several stacks: that opens (its own primary), and taking it all is held E.
        public void Offer(in InteractContext ctx, List<InteractOption> into) =>
            into.Add(InteractOption.Can(ctx.Facts.Stays ? _pick : _pickUp, primary: ctx.Facts.Count <= 1));

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        // The thing's family hands it over (a natural object's yield, a bag's stacks, an arrow).
        private static Outcome Take(in InteractContext ctx, in InteractArgs args) => ctx.Zone!.Interaction().PickUp(ctx.Entity, ctx.Player!, ctx.Target);
    }

    /// <summary>A deployable picked back up as its item (GROUND_SYSTEMS.md §11.3): empty, where the doer has building rights.</summary>
    private sealed class Portable : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _retrieve, _container;

        public Portable(InteractionData data, CapabilityData capabilities)
        {
            _verbs = new VerbActions(data, ("retrieve", Retrieve));
            _retrieve = _verbs["retrieve"];
            _container = capabilities.Index("container");
        }

        public string Key => "portable";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            // Aboard a ship its crew takes things up (IRON_WAKE.md §8: its list is its claim); elsewhere, where no one
            // else's claim covers.
            Outcome why = ctx.Facts.Capabilities.Has(_container) && !ctx.Has(ViewerFlags.Empty) ? OutcomeCode.NotEmpty
                : ctx.Facts.Aboard ? ctx.Has(ViewerFlags.Authorised) ? Outcome.Done : OutcomeCode.NotCrew
                : ctx.Building?.PrivilegeAt(ctx.Doer.Id.Value, ctx.Facts.Position) == Privilege.Blocked ? OutcomeCode.BuildingBlocked
                : Outcome.Done;
            into.Add(new InteractOption(_retrieve, why));
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Retrieve(in InteractContext ctx, in InteractArgs args) =>
            ctx.Thing is Deployable d ? ctx.Zone!.Interaction().TakeDeployable(ctx.Entity, ctx.Player!, d) : OutcomeCode.NotFound;
    }

    /// <summary>A campfire or furnace: lit (with fuel in it) and put out, with E held or the loot panel's switch.</summary>
    private sealed partial class Burner : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _light, _putOut;

        public Burner(InteractionData data)
        {
            _verbs = new VerbActions(data, ("light", (in InteractContext c, in InteractArgs a) => Switch(c, true)),
                ("put-out", (in InteractContext c, in InteractArgs a) => Switch(c, false)));
            (_light, _putOut) = (_verbs["light"], _verbs["put-out"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into) => into.Add(InteractOption.Can(ctx.Has(ViewerFlags.Lit) ? _putOut : _light));

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Switch(in InteractContext ctx, bool on) =>
            ctx.Thing is Deployable d ? ctx.Zone!.Interaction().SwitchBurner(d, on) : OutcomeCode.NotFound;
    }

    /// <summary>A recycler (on and off) or a research table (started; a research can't be stopped, Rust's).</summary>
    private sealed partial class Machine : IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _on, _off;

        public Machine(InteractionData data)
        {
            _verbs = new VerbActions(data, ("turn-on", (in InteractContext c, in InteractArgs a) => Switch(c, true)),
                ("turn-off", (in InteractContext c, in InteractArgs a) => Switch(c, false)));
            (_on, _off) = (_verbs["turn-on"], _verbs["turn-off"]);
        }

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            if (!ctx.Has(ViewerFlags.Lit))
            {
                into.Add(InteractOption.Can(_on));
            }
            else if (ctx.Facts.Deployable?.Recycler != null)
            {
                into.Add(InteractOption.Can(_off));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        private static Outcome Switch(in InteractContext ctx, bool on) =>
            ctx.Thing is Deployable d ? ctx.Zone!.World().SwitchMachine(d, on) : OutcomeCode.NotFound;
    }

    /// <summary>
    /// A workbench (deployables.json workbench): its tech tree (the interface's panel, E's primary) and the tree's unlocks,
    /// which the panel sends with the recipe as the verb's argument.
    /// </summary>
    private sealed class Station : ICapability, IInteractCapability
    {
        private readonly VerbActions _verbs;
        private readonly int _tree, _unlock;

        public Station(InteractionData data)
        {
            _verbs = new VerbActions(data, ("tech-tree", Panel), ("unlock", Unlock));
            (_tree, _unlock) = (_verbs["tech-tree"], _verbs["unlock"]);
        }

        public string Key => "station";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs => _verbs.Keys;

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
            if (ctx.Facts.Deployable is { Workbench: > 0 } bench && ctx.Content.World.Tree(bench.Workbench) != null)
            {
                into.Add(InteractOption.Can(_tree, primary: true));
                into.Add(InteractOption.Can(_unlock));
            }
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => _verbs.Do(ctx, verb, args);

        // The tree is the interface's to open: a panel's verb never reaches the server.
        private static Outcome Panel(in InteractContext ctx, in InteractArgs args) => OutcomeCode.NotUsable;

        // A node of the tree, for its salvage, at a bench of its level within reach (as crafting counts benches).
        private static Outcome Unlock(in InteractContext ctx, in InteractArgs args) =>
            ctx.Zone!.World().UnlockNode(ctx.Entity, ctx.Player!, (int)Math.Min(args.Arg, int.MaxValue));
    }
}
