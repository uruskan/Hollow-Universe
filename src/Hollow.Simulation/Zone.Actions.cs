using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

// Players' actions (ARCHITECTURE.md §11.4: intents): every one checks everything against the server's state and
// either does all it says or nothing, with the reason (Outcome). The server calls these as intents arrive.
public sealed partial class Zone
{
    private readonly List<ItemStack> _refunds = new();
    private readonly List<Container> _targets = new();

    private bool Acting(Entity entity, out Player p)
    {
        p = PlayerOf(entity)!;
        return p is { CanAct: true };
    }

    // ---------------------------------------------------------------- containers

    /// <summary>What a player is looting: a static thing with storage, or a sleeper; nothing if it is gone.</summary>
    public (WorldObject? Thing, Player? Sleeper, Entity Entity) LootTarget(Player p)
    {
        if (p.Looting.IsNone || !Entities.TryFind(p.Looting, out Entity e))
        {
            return default;
        }
        if (StaticOf(e) is { Container: not null } thing)
        {
            return (thing, null, e);
        }
        if (PlayerOf(e) is { Dead: false } sleeper && (sleeper.Sleeping || sleeper.Wounded))
        {
            // A sleeper, or a player down (Rust lets the wounded be looted too).
            return (null, sleeper, e);
        }
        return default;
    }

    /// <summary>How what a player loots is looted: by its container's kind (a ship's cargo, G6, is a box's), or as a sleeper.</summary>
    public LootKind LootKindOf(Player p)
    {
        if (LootShip(p) is { } ship)
        {
            return LootRules.KindOf(ship.Cargo);
        }
        (WorldObject? thing, Player? sleeper, _) = LootTarget(p);
        return thing?.Container is { } container ? LootRules.KindOf(container) : sleeper != null ? LootKind.Sleeper : LootKind.None;
    }

    /// <summary>The container an intent names, or null when there is no such open container.</summary>
    public Container? Resolve(Player p, ContainerRef which)
    {
        switch (which)
        {
            case ContainerRef.Main:
            case ContainerRef.Belt:
            case ContainerRef.Wear:
                return p.Inventory[which];
        }
        if (LootShip(p) is { } ship)
        {
            return which == ContainerRef.Loot ? ship.Cargo : null;
        }
        (WorldObject? thing, Player? sleeper, _) = LootTarget(p);
        if (thing != null)
        {
            return which == ContainerRef.Loot ? thing.Container : null;
        }
        if (sleeper != null)
        {
            return which switch
            {
                ContainerRef.Loot => sleeper.Inventory.Main,
                ContainerRef.LootBelt => sleeper.Inventory.Belt,
                ContainerRef.LootWear => sleeper.Inventory.Wear,
                _ => null,
            };
        }
        return null;
    }

    public Outcome MoveItem(Entity entity, ContainerRef from, int fromSlot, ContainerRef to, int toSlot, int amount, ushort expected)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Container? a = Resolve(p, from), b = Resolve(p, to);
        if (a == null || b == null)
        {
            return OutcomeCode.NotOpen;
        }
        if (LootRules.IsLoot(to) && !LootRules.IsLoot(from) && !LootRules.CanDeposit(LootKindOf(p)))
        {
            return OutcomeCode.NotAllowed;
        }
        return TookFrom(p, InventoryRules.Move(Content!, a, fromSlot, b, toSlot, amount, expected));
    }

    public Outcome QuickMove(Entity entity, ContainerRef from, int slot, ushort expected)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Container? source = Resolve(p, from);
        if (source == null)
        {
            return OutcomeCode.NotOpen;
        }
        if (!source.InRange(slot))
        {
            return OutcomeCode.Invalid;
        }
        if (Content!.Item(source[slot].Item) is not { } item)
        {
            return OutcomeCode.Stale;
        }
        _targets.Clear();
        foreach (ContainerRef r in LootRules.QuickTargets(from, item, LootKindOf(p)))
        {
            if (Resolve(p, r) is { } target)
            {
                _targets.Add(target);
            }
        }
        return TookFrom(p, InventoryRules.QuickMove(Content, source, slot, _targets, expected));
    }

    /// <summary>Drops (part of) a stack in front of the player: into the bag lying there, or a new one (<see cref="Spill(Vector3, ItemStack, float?)"/>).</summary>
    public Outcome DropItem(Entity entity, ContainerRef from, int slot, int amount, ushort expected)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Container? source = Resolve(p, from);
        if (source == null)
        {
            return OutcomeCode.NotOpen;
        }
        Outcome result = InventoryRules.Remove(source, slot, amount, expected, out ItemStack taken);
        if (result == OutcomeCode.Done)
        {
            Drop(entity, taken);
        }
        return result;
    }

    /// <summary>
    /// An inventory action (GROUND_SYSTEMS.md §7) on a stack in the player's own slots: eat, use, wear, take off, learn,
    /// unload, take an attachment off, by its number in items.json's list (and its argument: which attachment). The
    /// action's handler checks it is offered on that stack and does it, as the client predicted it.
    /// </summary>
    public Outcome ItemAction(Entity entity, ContainerRef from, int slot, ushort expected, int action, int arg)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        return Content!.ItemActions.Do(new PlayerActor(this, entity, p), from, slot, expected, action, arg);
    }

    /// <summary>The player an item action acts for, as the zone has them: eating feeds them, learning counts, the gun in hand hears of its magazine.</summary>
    private sealed class PlayerActor(Zone zone, Entity entity, Player p) : IItemActor
    {
        public GameContent Content => zone.Content!;

        public PlayerInventory Inventory => p.Inventory;

        public BlueprintBook Blueprints => p.Blueprints;

        public bool Reloading(ContainerRef from, int slot) => from == ContainerRef.Belt && slot == p.HeldSlot && p.Gun.Reloading;

        public void Consumed(ItemDef item)
        {
            ConsumeStats consume = item.Consume!;
            Consume(p, consume, Content.Survival);
            ref Character c = ref zone.Entities.Get<Character>(entity);
            SyncHealth(ref c, p);
            if (consume.Poison > 0)
            {
                // G7: raw or burnt meat makes the eater sick.
                zone.Hurt(entity, p, consume.Poison, "Food poisoning", EntityId.None, "");
            }
        }

        public void Learned(Recipe recipe) => zone.WorldStats.Learned++;

        public void MagazineChanged(ContainerRef from, int slot)
        {
            if (from == ContainerRef.Belt && slot == p.HeldSlot)
            {
                p.GunRevision++;
            }
        }
    }

    // ---------------------------------------------------------------- crafting

    /// <summary>Starts crafting <paramref name="count"/> of a recipe (its output item's ID).</summary>
    public Outcome Craft(Entity entity, int recipe, int count, out uint ticket)
    {
        ticket = 0;
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Vector3 at = EyeOf(Entities.Get<Character>(entity).State);
        int bench = WorkbenchLevel(at);
        ulong station = 0;
        if (Content!.Recipe(recipe) is { Station: { } needs })
        {
            // Made at a station (a ship at the vehicle workbench, G6): the nearest within reach builds it.
            if (StationNear(at, needs) is not { } found)
            {
                return OutcomeCode.NeedsStation.With(needs.Item.Id);
            }
            station = found.Id.Value;
        }
        return p.Crafting.Start(Content!, p.Inventory, recipe, count, bench, out ticket, p.Blueprints, station);
    }

    public Outcome CancelCraft(Entity entity, uint ticket)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        _refunds.Clear();
        Outcome result = p.Crafting.Cancel(Content!, p.Inventory, ticket, _refunds);
        Drop(entity, _refunds);
        return result;
    }

    // ---------------------------------------------------------------- looting

    /// <summary>Opens a box, device, corpse or sleeper within reach (GROUND.md §6).</summary>
    public Outcome Open(Entity entity, EntityId target)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        if (target == p.Id || !Entities.TryFind(target, out Entity e))
        {
            return OutcomeCode.NotFound;
        }
        if (ShipOf(e) is { Destroyed: false } ship)
        {
            return OpenShip(entity, p, ship); // its cargo (G6)
        }
        TargetRef looted;
        if (StaticOf(e) is { Container: not null } thing)
        {
            looted = thing.Ref;
        }
        else if (PlayerOf(e) is { Dead: false } other && (other.Sleeping || other.Wounded))
        {
            looted = other.Ref;
        }
        else
        {
            return OutcomeCode.NotFound;
        }
        Outcome reach = Reach(EyeOf(Entities.Get<Character>(entity).State), looted, Content!.Survival.OpenReach);
        if (!reach.Ok)
        {
            return reach;
        }
        p.Looting = target;
        p.LootRevision++;
        return OutcomeCode.Done;
    }

    public Outcome Close(Entity entity)
    {
        if (PlayerOf(entity) is not { } p)
        {
            return OutcomeCode.Invalid;
        }
        if (!p.Looting.IsNone)
        {
            p.Looting = EntityId.None;
            p.LootRevision++;
        }
        return OutcomeCode.Done;
    }

    // Looting ends when the thing is gone or the looter walks away.
    private void CheckLoot()
    {
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            if (p.Looting.IsNone)
            {
                continue;
            }
            if (ShipOf(p.Looting) is { } ship)
            {
                // A ship's cargo (G6): open while at its hatch, or in its seat.
                if (ship.Destroyed || !p.IsAwakeAndAlive || !StillAtShip(Entities.AtSlot(players.Slots[i]), ship))
                {
                    p.Looting = EntityId.None;
                    p.LootRevision++;
                }
                continue;
            }
            (WorldObject? thing, Player? sleeper, _) = LootTarget(p);
            bool keep = (thing != null || sleeper != null) && p.IsAwakeAndAlive;
            if (keep)
            {
                // Open while its nearest surface is within reach (walking round it is no reason to close).
                Vector3 eye = EyeOf(Entities.Get<Character>(Entities.AtSlot(players.Slots[i])).State);
                keep = Reach(eye, thing?.Ref ?? sleeper!.Ref, Content!.Survival.KeepOpenReach, sight: false).Ok;
            }
            if (!keep)
            {
                p.Looting = EntityId.None;
                p.LootRevision++;
            }
        }
    }

    // ---------------------------------------------------------------- picking up

    /// <summary>Picks a pickup or berry bush with the interact key (GROUND.md §6).</summary>
    public Outcome PickUpNatural(Entity entity, ulong id)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Vector3 eye = EyeOf(Entities.Get<Character>(entity).State);
        // (An untrusted ID naming a place far away is refused before its tile is made: the biggest objects are 4 m across.)
        if (!Nature!.TryGetNear(id, eye, Content!.Survival.InteractReach + 4, out NaturalObject o) || Content.Natural(o.Kind) is not { } kind)
        {
            return OutcomeCode.NotFound;
        }
        if (!kind.IsPicked)
        {
            return OutcomeCode.NotUsable;
        }
        if (Natural!.IsDepleted(id))
        {
            return OutcomeCode.NotFound;
        }
        Outcome reach = Reach(eye, new TargetRef(TargetFamily.Natural, id), Content.Survival.InteractReach);
        if (!reach.Ok)
        {
            return reach;
        }
        GatherResult result = Natural.Pick(o, UnixMilliseconds);
        Give(entity, p, result.Yield, GainReason.PickedUp);
        return OutcomeCode.Done;
    }

    /// <summary>
    /// Picks up a bag of dropped items with the interact key: everything in it that fits (a bag of one stack is taken
    /// whole; the client opens one holding more). What doesn't fit stays in the bag; an emptied bag goes.
    /// </summary>
    public Outcome PickUpItem(Entity entity, EntityId id)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        if (StaticOf(id) is not DroppedBag bag || bag.Loot.IsEmpty)
        {
            return OutcomeCode.NotFound;
        }
        Outcome reach = Reach(EyeOf(Entities.Get<Character>(entity).State), bag.Ref, Content!.Survival.InteractReach);
        if (!reach.Ok)
        {
            return reach;
        }
        bool taken = false;
        for (int i = 0; i < bag.Loot.Size; i++)
        {
            ItemStack stack = bag.Loot[i];
            int left = stack.IsEmpty ? 0 : p.Inventory.Give(Content, stack);
            if (left == stack.Count)
            {
                continue;
            }
            GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = stack.Item, Count = stack.Count - left, Reason = GainReason.PickedUp });
            bag.Loot.Set(i, stack.WithCount(left));
            taken = true;
        }
        if (!taken)
        {
            return OutcomeCode.NoSpace;
        }
        bag.PublishChanges();
        EndIfEmptied(bag);
        return OutcomeCode.Done;
    }

    // ---------------------------------------------------------------- devices

    /// <summary>Lights or puts out a campfire or furnace (it needs fuel to light).</summary>
    public Outcome Toggle(Entity entity, EntityId id)
    {
        if (!Acting(entity, out Player _))
        {
            return OutcomeCode.Dead;
        }
        if (StaticOf(id) is Deployable { Def.Device: null, Def.IsMachine: true, Storage: not null } machine)
        {
            // G7: a recycler (on or off) or a research table (start).
            Outcome near = Reach(EyeOf(Entities.Get<Character>(entity).State), machine.Ref, Content!.Survival.OpenReach);
            return near.Ok ? ToggleMachine(machine) : near;
        }
        if (StaticOf(id) is not Deployable { Def.Device: not null, Storage: { } storage } d)
        {
            return OutcomeCode.NotFound;
        }
        Outcome reach = Reach(EyeOf(Entities.Get<Character>(entity).State), d.Ref, Content!.Survival.OpenReach);
        if (!reach.Ok)
        {
            return reach;
        }
        if (!d.Lit)
        {
            bool fuel = false;
            foreach (int s in d.Def.Device!.FuelSlots)
            {
                fuel |= Content.Item(storage[s].Item)?.Fuel != null;
            }
            if (!fuel)
            {
                return OutcomeCode.NoFuel;
            }
        }
        d.Lit = !d.Lit;
        d.BurnTicksLeft = 0;
        d.Revision++;
        return OutcomeCode.Done;
    }
}
