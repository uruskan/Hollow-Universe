using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>
/// One of items.json's <c>actions</c> (GROUND_SYSTEMS.md §7): a button of the item panel's ACTIONS. Its place in the list
/// is its number on the wire (<c>ItemActionIntent.Action</c>): append only. Its label may name the item it acts on
/// (<c>{item}</c>: "REMOVE HOLO SIGHT").
/// </summary>
public sealed record ItemActionDef(int Id, string Key, string Label);

/// <summary>
/// A button a stack offers (GROUND_SYSTEMS.md §7): its action, the action's argument (which attachment comes off), its
/// label, and why it can't be done now (null: it can). A refused option still shows; choosing it says why.
/// </summary>
public readonly record struct ItemActionOption(ItemActionDef Action, int Arg, string Label, Outcome? Refusal = null)
{
    public bool Enabled => Refusal == null;
}

/// <summary>
/// The player an item action acts for (GROUND_SYSTEMS.md §7), as the server has them or as the client predicts them: the
/// same rules change the same inventory on both sides, and what an action does besides (eating's vitals, a learnt
/// blueprint's count, the gun in hand's state) is the side's own.
/// </summary>
public interface IItemActor
{
    GameContent Content { get; }

    PlayerInventory Inventory { get; }

    BlueprintBook Blueprints { get; }

    /// <summary>Whether a slot holds the gun in hand while it reloads (its magazine is the reload's until it ends).</summary>
    bool Reloading(ContainerRef from, int slot);

    /// <summary>One was eaten or used: what it does to the player (the server's vitals; the prediction's nothing).</summary>
    void Consumed(ItemDef item);

    /// <summary>A blueprint was learnt from an item.</summary>
    void Learned(Recipe recipe);

    /// <summary>A firearm's magazine was changed by an action (the server tells the gun in hand's owner).</summary>
    void MagazineChanged(ContainerRef from, int slot);
}

/// <summary>What an action looks at: who acts, the slot (one of their own), its stack and the item.</summary>
public readonly struct ItemActionContext(IItemActor actor, ContainerRef from, Container container, int slot, ItemStack stack, ItemDef item)
{
    public IItemActor Actor { get; } = actor;

    public GameContent Content => Actor.Content;

    public ContainerRef From { get; } = from;

    public Container Container { get; } = container;

    public int Slot { get; } = slot;

    public ItemStack Stack { get; } = stack;

    public ItemDef Item { get; } = item;
}

/// <summary>
/// One inventory action of a part (GROUND_SYSTEMS.md §7): the buttons of it a stack offers (enabled or with the reason not;
/// REMOVE one per attachment), and doing one, the same on the server and in the client's prediction. Found by its action
/// (what an intent names: every action has exactly one handler) and listed under its part (what a stack offers).
/// </summary>
public interface IItemAction
{
    ItemPart Part { get; }

    /// <summary>Its action (items.json's key).</summary>
    string Action { get; }

    void Offer(in ItemActionContext ctx, List<ItemActionOption> into);

    Outcome Do(in ItemActionContext ctx, in ItemActionOption option);
}

/// <summary>
/// The inventory actions (GROUND_SYSTEMS.md §7): items.json's list and a handler per action, listed under the part that
/// offers it. A stack offers what its parts' handlers offer; an intent is done by its action's handler, which must offer
/// it on that stack, enabled. DROP and the split are the inventory's own (moving and dropping), not these.
/// </summary>
public sealed class ItemActions
{
    private readonly ItemActionDef[] _all;
    private readonly Registry<IItemAction> _byAction;
    private readonly IItemAction[][] _byPart;

    private ItemActions(ItemActionDef[] all, Registry<IItemAction> byAction, IItemAction[][] byPart)
    {
        _all = all;
        _byAction = byAction;
        _byPart = byPart;
    }

    /// <summary>
    /// The actions items.json lists, bound to their handlers (one each); <paramref name="problems"/> gets an action no
    /// handler does, a handler's action the list hasn't, two for one.
    /// </summary>
    public static ItemActions Bind(IReadOnlyList<ItemActionDef> actions, List<string> problems)
    {
        IItemAction[] handlers = { new Use(), new Eat(), new WearIt(), new Unequip(), new Learn(), new Unload(), new Remove() };
        var byAction = new Registry<IItemAction>("item actions", actions.Select(a => a.Key).ToArray());
        foreach (IItemAction handler in handlers)
        {
            byAction.Add(handler.Action, handler);
        }
        problems.AddRange(byAction.Problems.Select(p => "items.json: actions: " + p));
        // Under each part its handlers, in the list's order (the order its buttons show in).
        IItemAction[][] byPart = Enum.GetValues<ItemPart>()
            .Select(part => actions.Select(a => Array.Find(handlers, h => h.Action == a.Key)).Where(h => h?.Part == part).Select(h => h!).ToArray())
            .ToArray();
        return new ItemActions(actions.ToArray(), byAction, byPart);
    }

    /// <summary>UNLOAD's key: the gun in hand's goes on the gun's own timeline (the client routes it there).</summary>
    public const string UnloadKey = "unload";

    /// <summary>Every action, by number.</summary>
    public IReadOnlyList<ItemActionDef> All => _all;

    /// <summary>An action by number (from outside: untrusted, null when unknown) or key.</summary>
    public ItemActionDef? this[int id] => id >= 0 && id < _all.Length ? _all[id] : null;

    public ItemActionDef? this[string key] => Array.Find(_all, a => a.Key == key);

    /// <summary>The buttons a stack in one of the actor's own slots offers, in its parts' order.</summary>
    public void Offer(IItemActor actor, ContainerRef from, int slot, List<ItemActionOption> into)
    {
        if (!Context(actor, from, slot, 0, out ItemActionContext ctx).Ok)
        {
            return;
        }
        foreach (ItemPart part in ctx.Item.Parts)
        {
            foreach (IItemAction handler in _byPart[(int)part])
            {
                handler.Offer(ctx, into);
            }
        }
    }

    /// <summary>The first button one of an item's parts offers, enabled or not (what a click with it in hand does), or null.</summary>
    public ItemActionOption? First(IItemActor actor, ContainerRef from, int slot, ItemPart part)
    {
        if (!Context(actor, from, slot, 0, out ItemActionContext ctx).Ok || !ctx.Item.Parts.Has(part))
        {
            return null;
        }
        var options = new List<ItemActionOption>();
        foreach (IItemAction handler in _byPart[(int)part])
        {
            handler.Offer(ctx, options);
        }
        return options.Count > 0 ? options[0] : null;
    }

    /// <summary>
    /// Does an action on a stack (the server, and the client predicting it): the stack must be what the client saw, and
    /// the action's handler must offer it on that stack with that argument, enabled.
    /// </summary>
    public Outcome Do(IItemActor actor, ContainerRef from, int slot, ushort expected, int action, int arg)
    {
        Outcome found = Context(actor, from, slot, expected, out ItemActionContext ctx);
        if (!found.Ok)
        {
            return found;
        }
        if (_byAction.Find(action) is not { } handler)
        {
            return OutcomeCode.Invalid;
        }
        if (!ctx.Item.Parts.Has(handler.Part))
        {
            return OutcomeCode.NotUsable;
        }
        var options = new List<ItemActionOption>();
        handler.Offer(ctx, options);
        foreach (ItemActionOption option in options)
        {
            if (option.Arg == arg)
            {
                return option.Refusal ?? handler.Do(ctx, option);
            }
        }
        return OutcomeCode.NotUsable;
    }

    // The stack in one of the actor's own slots (main, belt, clothing), when it is what the client saw.
    private static Outcome Context(IItemActor actor, ContainerRef from, int slot, ushort expected, out ItemActionContext ctx)
    {
        ctx = default;
        if (from is not (ContainerRef.Main or ContainerRef.Belt or ContainerRef.Wear))
        {
            return OutcomeCode.NotAllowed;
        }
        Container container = actor.Inventory[from];
        if (!container.InRange(slot))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack stack = container[slot];
        if (stack.IsEmpty || (expected != 0 && stack.Item != expected) || actor.Content.Item(stack.Item) is not { } item)
        {
            return OutcomeCode.Stale;
        }
        ctx = new ItemActionContext(actor, from, container, slot, stack, item);
        return OutcomeCode.Done;
    }

    // ---------------------------------------------------------------- the handlers

    // An option of an action by key (every key a handler names is in the list: Bind checks it).
    private static ItemActionOption Option(in ItemActionContext ctx, string key, Outcome? refusal = null, int arg = 0, string? item = null)
    {
        ItemActionDef def = ctx.Content.ItemActions[key]!;
        return new ItemActionOption(def, arg, def.Label.Replace("{item}", (item ?? ctx.Item.Name).ToUpperInvariant(), StringComparison.Ordinal), refusal);
    }

    // Food (it feeds: calories or water) is eaten, medicine used: one at a time, from the main slots or the belt.
    private static bool Feeds(ItemDef item) => item.Consume is { } food && (food.Calories > 0 || food.Hydration > 0);

    private static Outcome Consume(in ItemActionContext ctx)
    {
        Outcome taken = InventoryRules.Remove(ctx.Container, ctx.Slot, 1, ctx.Stack.Item, out _);
        if (taken.Ok)
        {
            ctx.Actor.Consumed(ctx.Item);
        }
        return taken;
    }

    /// <summary>USE: medicine (a consumable that doesn't feed).</summary>
    private sealed class Use : IItemAction
    {
        public ItemPart Part => ItemPart.Consumable;

        public string Action => "use";

        public void Offer(in ItemActionContext ctx, List<ItemActionOption> into)
        {
            if (ctx.From != ContainerRef.Wear && !Feeds(ctx.Item))
            {
                into.Add(Option(ctx, Action));
            }
        }

        public Outcome Do(in ItemActionContext ctx, in ItemActionOption option) => Consume(ctx);
    }

    /// <summary>EAT: food.</summary>
    private sealed class Eat : IItemAction
    {
        public ItemPart Part => ItemPart.Consumable;

        public string Action => "eat";

        public void Offer(in ItemActionContext ctx, List<ItemActionOption> into)
        {
            if (ctx.From != ContainerRef.Wear && Feeds(ctx.Item))
            {
                into.Add(Option(ctx, Action));
            }
        }

        public Outcome Do(in ItemActionContext ctx, in ItemActionOption option) => Consume(ctx);
    }

    /// <summary>WEAR: clothing into the clothing slot that takes it; refused while something worn has its layer.</summary>
    private sealed class WearIt : IItemAction
    {
        public ItemPart Part => ItemPart.Wearable;

        public string Action => "wear";

        public void Offer(in ItemActionContext ctx, List<ItemActionOption> into)
        {
            if (ctx.From == ContainerRef.Wear)
            {
                return;
            }
            Container wear = ctx.Actor.Inventory.Wear;
            bool fits = false;
            for (int i = 0; i < wear.Size && !fits; i++)
            {
                fits = wear[i].IsEmpty && wear.Accepts(ctx.Content, i, ctx.Stack.WithCount(1));
            }
            into.Add(Option(ctx, Action, fits ? null : OutcomeCode.Worn));
        }

        public Outcome Do(in ItemActionContext ctx, in ItemActionOption option) =>
            InventoryRules.QuickMove(ctx.Content, ctx.Container, ctx.Slot, new[] { ctx.Actor.Inventory.Wear }, ctx.Stack.Item);
    }

    /// <summary>UNEQUIP: clothing off, into the main slots, then the belt.</summary>
    private sealed class Unequip : IItemAction
    {
        public ItemPart Part => ItemPart.Wearable;

        public string Action => "unequip";

        public void Offer(in ItemActionContext ctx, List<ItemActionOption> into)
        {
            if (ctx.From == ContainerRef.Wear)
            {
                into.Add(Option(ctx, Action));
            }
        }

        public Outcome Do(in ItemActionContext ctx, in ItemActionOption option) =>
            InventoryRules.QuickMove(ctx.Content, ctx.Container, ctx.Slot, ctx.Actor.Inventory.Carried, ctx.Stack.Item);
    }

    /// <summary>LEARN: a blueprint's recipe (its stack's condition) is learnt and the blueprint used up; one known already isn't.</summary>
    private sealed class Learn : IItemAction
    {
        public ItemPart Part => ItemPart.Blueprint;

        public string Action => "learn";

        public void Offer(in ItemActionContext ctx, List<ItemActionOption> into)
        {
            if (ctx.From == ContainerRef.Wear)
            {
                return;
            }
            Recipe? recipe = ctx.Content.Recipe(ctx.Stack.Condition);
            into.Add(Option(ctx, Action, recipe == null ? OutcomeCode.Invalid : ctx.Actor.Blueprints.Knows(recipe) ? OutcomeCode.AlreadyKnown : null));
        }

        public Outcome Do(in ItemActionContext ctx, in ItemActionOption option)
        {
            Recipe recipe = ctx.Content.Recipe(ctx.Stack.Condition)!;
            Outcome taken = InventoryRules.Remove(ctx.Container, ctx.Slot, 1, ctx.Stack.Item, out _);
            if (taken.Ok && ctx.Actor.Blueprints.Learn(recipe))
            {
                ctx.Actor.Learned(recipe);
            }
            return taken;
        }
    }

    /// <summary>UNLOAD: a firearm's rounds back into the inventory (all of them; refused when they don't fit).</summary>
    private sealed class Unload : IItemAction
    {
        public ItemPart Part => ItemPart.Firearm;

        public string Action => UnloadKey;

        public void Offer(in ItemActionContext ctx, List<ItemActionOption> into)
        {
            if (ctx.From != ContainerRef.Wear)
            {
                into.Add(Option(ctx, Action, ctx.Stack.Loaded == 0 ? OutcomeCode.Empty : ctx.Actor.Reloading(ctx.From, ctx.Slot) ? OutcomeCode.Reloading : null));
            }
        }

        public Outcome Do(in ItemActionContext ctx, in ItemActionOption option)
        {
            Outcome unloaded = FirearmRules.Unload(ctx.Content, ctx.Actor.Inventory, ctx.Container, ctx.Slot);
            if (unloaded.Ok)
            {
                ctx.Actor.MagazineChanged(ctx.From, ctx.Slot);
            }
            return unloaded;
        }
    }

    /// <summary>REMOVE: an attachment off a firearm into the inventory; one button per attachment, its slot the argument.</summary>
    private sealed class Remove : IItemAction
    {
        public ItemPart Part => ItemPart.Firearm;

        public string Action => "remove";

        public void Offer(in ItemActionContext ctx, List<ItemActionOption> into)
        {
            if (ctx.From == ContainerRef.Wear)
            {
                return;
            }
            foreach (AttachmentSlot slot in GunMods.Slots)
            {
                if (ctx.Content.Item(ctx.Stack.Mod(slot)) is { } part)
                {
                    into.Add(Option(ctx, Action, arg: (int)slot, item: part.Name));
                }
            }
        }

        public Outcome Do(in ItemActionContext ctx, in ItemActionOption option) =>
            AttachmentRules.Detach(ctx.Content, ctx.Actor.Inventory, ctx.Container, ctx.Slot, (AttachmentSlot)option.Arg);
    }
}
