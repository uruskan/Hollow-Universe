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

namespace Hollow.Simulation;

// Players' actions (ARCHITECTURE.md §11.4: intents): every one checks everything against the server's state and
// either does all it says or nothing, with the reason (ActionResult). The server calls these as intents arrive.
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

    public LootKind LootKindOf(Player p)
    {
        (WorldObject? thing, Player? sleeper, _) = LootTarget(p);
        return thing switch
        {
            Deployable { Def.Device: not null } => LootKind.Device,
            Deployable => LootKind.Box,
            Corpse => LootKind.Corpse,
            _ => sleeper != null ? LootKind.Sleeper : LootKind.None,
        };
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

    public ActionResult MoveItem(Entity entity, ContainerRef from, int fromSlot, ContainerRef to, int toSlot, int amount, ushort expected)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        Container? a = Resolve(p, from), b = Resolve(p, to);
        if (a == null || b == null)
        {
            return ActionResult.NotOpen;
        }
        if (LootRules.IsLoot(to) && !LootRules.IsLoot(from) && !LootRules.CanDeposit(LootKindOf(p)))
        {
            return ActionResult.NotAllowed;
        }
        return InventoryRules.Move(Content!, a, fromSlot, b, toSlot, amount, expected);
    }

    public ActionResult QuickMove(Entity entity, ContainerRef from, int slot, ushort expected)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        Container? source = Resolve(p, from);
        if (source == null)
        {
            return ActionResult.NotOpen;
        }
        if (!source.InRange(slot))
        {
            return ActionResult.Invalid;
        }
        if (Content!.Item(source[slot].Item) is not { } item)
        {
            return ActionResult.Stale;
        }
        _targets.Clear();
        foreach (ContainerRef r in LootRules.QuickTargets(from, item, LootKindOf(p)))
        {
            if (Resolve(p, r) is { } target)
            {
                _targets.Add(target);
            }
        }
        return InventoryRules.QuickMove(Content, source, slot, _targets, expected);
    }

    /// <summary>Drops (part of) a stack in front of the player, where it lies until picked up or it despawns.</summary>
    public ActionResult DropItem(Entity entity, ContainerRef from, int slot, int amount, ushort expected)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        Container? source = Resolve(p, from);
        if (source == null)
        {
            return ActionResult.NotOpen;
        }
        ActionResult result = InventoryRules.Remove(source, slot, amount, expected, out ItemStack taken);
        if (result == ActionResult.Accepted)
        {
            Drop(entity, taken);
        }
        return result;
    }

    /// <summary>Eats food or uses a medical item from the player's own slots.</summary>
    public ActionResult UseItem(Entity entity, ContainerRef from, int slot, ushort expected)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        if (from is not (ContainerRef.Main or ContainerRef.Belt))
        {
            return ActionResult.NotAllowed;
        }
        Container source = p.Inventory[from];
        if (!source.InRange(slot))
        {
            return ActionResult.Invalid;
        }
        ItemStack s = source[slot];
        if (s.IsEmpty || (expected != 0 && s.Item != expected))
        {
            return ActionResult.Stale;
        }
        if (Content!.Item(s.Item)?.Consume is not { } consume)
        {
            return ActionResult.NotUsable;
        }
        Consume(p, consume, Content.Survival);
        s.Count--;
        source.Set(slot, s);
        ref Character c = ref Entities.Get<Character>(entity);
        SyncHealth(ref c, p);
        return ActionResult.Accepted;
    }

    // ---------------------------------------------------------------- crafting

    /// <summary>Starts crafting <paramref name="count"/> of a recipe (its output item's ID).</summary>
    public ActionResult Craft(Entity entity, int recipe, int count, out uint ticket)
    {
        ticket = 0;
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        int bench = WorkbenchLevel(Entities.Get<Character>(entity).State.Position);
        return p.Crafting.Start(Content!, p.Inventory, recipe, count, bench, out ticket);
    }

    public ActionResult CancelCraft(Entity entity, uint ticket)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        _refunds.Clear();
        ActionResult result = p.Crafting.Cancel(Content!, p.Inventory, ticket, _refunds);
        DropAll(entity, _refunds);
        return result;
    }

    // ---------------------------------------------------------------- looting

    /// <summary>Opens a box, device, corpse or sleeper within reach (GROUND.md §6).</summary>
    public ActionResult Open(Entity entity, EntityId target)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        if (target == p.Id || !Entities.TryFind(target, out Entity e))
        {
            return ActionResult.NotFound;
        }
        Vector3 at;
        if (StaticOf(e) is { Container: not null } thing)
        {
            at = thing is Deployable d ? d.Centre : thing.Position;
        }
        else if (PlayerOf(e) is { Dead: false } other && (other.Sleeping || other.Wounded))
        {
            at = Entities.Get<Character>(e).State.Position;
        }
        else
        {
            return ActionResult.NotFound;
        }
        if (Vector3.Distance(EyeOf(Entities.Get<Character>(entity).State), at) > Content!.Survival.LootReach + 0.5f)
        {
            return ActionResult.OutOfReach;
        }
        p.Looting = target;
        p.LootRevision++;
        return ActionResult.Accepted;
    }

    public ActionResult Close(Entity entity)
    {
        if (PlayerOf(entity) is not { } p)
        {
            return ActionResult.Invalid;
        }
        if (!p.Looting.IsNone)
        {
            p.Looting = EntityId.None;
            p.LootRevision++;
        }
        return ActionResult.Accepted;
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
            (WorldObject? thing, Player? sleeper, Entity e) = LootTarget(p);
            bool keep = (thing != null || sleeper != null) && p.IsAwakeAndAlive;
            if (keep)
            {
                Vector3 at = thing is Deployable d ? d.Centre : thing?.Position ?? Entities.Get<Character>(e).State.Position;
                Vector3 eye = EyeOf(Entities.Get<Character>(Entities.AtSlot(players.Slots[i])).State);
                keep = Vector3.Distance(eye, at) <= Content!.Survival.LootReach + 1.5f;
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
    public ActionResult PickUpNatural(Entity entity, ulong id)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        if (!Nature!.TryGet(id, out NaturalObject o) || Content!.Natural(o.Kind) is not { } kind)
        {
            return ActionResult.NotFound;
        }
        if (!kind.IsPicked)
        {
            return ActionResult.NotUsable;
        }
        if (Natural!.IsDepleted(id))
        {
            return ActionResult.NotFound;
        }
        Vector3 eye = EyeOf(Entities.Get<Character>(entity).State);
        if (Vector3.Distance(eye, NaturalShapes.Middle(o, kind)) > Content.Survival.InteractReach + kind.Radius * o.Scale)
        {
            return ActionResult.OutOfReach;
        }
        GatherResult result = Natural.Pick(o, UnixMilliseconds);
        Give(entity, p, result.Yield, GainReason.PickedUp);
        return ActionResult.Accepted;
    }

    /// <summary>Picks up a dropped item: as much as fits; the rest stays on the ground.</summary>
    public ActionResult PickUpItem(Entity entity, EntityId id)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        if (StaticOf(id) is not DroppedItem item || item.Stack.IsEmpty)
        {
            return ActionResult.NotFound;
        }
        Vector3 eye = EyeOf(Entities.Get<Character>(entity).State);
        if (Vector3.Distance(eye, item.Position) > Content!.Survival.InteractReach + 0.3f)
        {
            return ActionResult.OutOfReach;
        }
        ItemStack stack = item.Stack;
        int left = p.Inventory.Give(Content, stack);
        if (left == stack.Count)
        {
            return ActionResult.NoSpace;
        }
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = stack.Item, Count = stack.Count - left, Reason = GainReason.PickedUp });
        stack.Count = left;
        item.Stack = stack;
        item.Revision++;
        if (left == 0)
        {
            RemoveStatic(item);
        }
        return ActionResult.Accepted;
    }

    // ---------------------------------------------------------------- devices

    /// <summary>Lights or puts out a campfire or furnace (it needs fuel to light).</summary>
    public ActionResult Toggle(Entity entity, EntityId id)
    {
        if (!Acting(entity, out Player _))
        {
            return ActionResult.Dead;
        }
        if (StaticOf(id) is not Deployable { Def.Device: not null, Storage: { } storage } d)
        {
            return ActionResult.NotFound;
        }
        if (Vector3.Distance(EyeOf(Entities.Get<Character>(entity).State), d.Centre) > Content!.Survival.LootReach + 0.5f)
        {
            return ActionResult.OutOfReach;
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
                return ActionResult.NotEnough;
            }
        }
        d.Lit = !d.Lit;
        d.BurnTicksLeft = 0;
        d.Revision++;
        return ActionResult.Accepted;
    }

    // ---------------------------------------------------------------- placing

    /// <summary>
    /// Places the deployable in a slot at a point on the ground (GROUND.md §6): within reach, on ground no steeper than
    /// it allows, not under water, overlapping no deployable, tree, outcrop or character.
    /// </summary>
    public ActionResult Place(Entity entity, ContainerRef from, int slot, ushort expected, Vector3 target, float yaw)
    {
        if (!Acting(entity, out Player p))
        {
            return ActionResult.Dead;
        }
        if (from is not (ContainerRef.Main or ContainerRef.Belt))
        {
            return ActionResult.NotAllowed;
        }
        Container source = p.Inventory[from];
        if (!source.InRange(slot) || !float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z) || !float.IsFinite(yaw))
        {
            return ActionResult.Invalid;
        }
        ItemStack s = source[slot];
        if (s.IsEmpty || (expected != 0 && s.Item != expected))
        {
            return ActionResult.Stale;
        }
        if (Content!.Item(s.Item)?.Deploys is not { } def)
        {
            return ActionResult.NotUsable;
        }
        ActionResult check = CanPlace(EyeOf(Entities.Get<Character>(entity).State), def, target, yaw, out Vector3 ground);
        if (check != ActionResult.Accepted)
        {
            return check;
        }
        s.Count--;
        source.Set(slot, s);
        var placed = new Deployable(NextId(), def, ground, Frame.UpAt(ground), yaw, p.Id);
        if (def.IsRespawnPoint)
        {
            placed.ReadyAtUnixMs = UnixMilliseconds + (long)(def.RespawnSeconds * 1000);
        }
        AddStatic(placed);
        return ActionResult.Accepted;
    }

    /// <summary>The placement checks alone (the client runs them too, to tint its preview); the ground point found.</summary>
    public ActionResult CanPlace(Vector3 eye, DeployableDef def, Vector3 target, float yaw, out Vector3 ground)
    {
        ground = Settle(target);
        if (Vector3.Distance(eye, target) > Content!.PlacementReach + 0.25f)
        {
            return ActionResult.OutOfReach;
        }
        if (MathF.Abs(target.X) > PhysicsWorld.Extent - PhysicsWorld.TileSize || MathF.Abs(target.Z) > PhysicsWorld.Extent - PhysicsWorld.TileSize)
        {
            return ActionResult.BadPlacement;
        }
        Vector3 up = Frame.UpAt(ground);
        if (MathF.Abs(Vector3.Dot(target - ground, up)) > Content.PlacementMaxStep)
        {
            return ActionResult.BadPlacement;
        }
        if (Surface.SeaRadius > 0 && Frame.ToBody(ground).Length < Surface.SeaRadius + 0.05)
        {
            return ActionResult.BadPlacement;
        }
        (_, float slope) = GroundNormal(ground);
        if (slope > def.MaxSlopeDegrees * MathF.PI / 180)
        {
            return ActionResult.TooSteep;
        }
        Vector3 centre = ground + up * def.HalfExtents.Y;
        _near.Clear();
        DeployablesNear(ground, 5f, _near);
        foreach (Deployable other in _near)
        {
            if (Geometry.BoxesOverlap(centre, def.HalfExtents, yaw, other.Centre, other.Def.HalfExtents, other.Yaw, up))
            {
                return ActionResult.Blocked;
            }
        }
        _traceNature.Clear();
        Nature!.Query(ground.X - 4, ground.Z - 4, ground.X + 4, ground.Z + 4, _traceNature);
        foreach (NaturalObject o in _traceNature)
        {
            if (Content.Natural(o.Kind) is { IsSolid: true } kind && !Natural!.IsDepleted(o.Id))
            {
                float radius = kind.Radius * o.Scale, height = kind.Class == NaturalClass.Tree ? kind.Height * o.Scale : kind.Radius * o.Scale * 1.35f;
                if (Geometry.BoxCylinderOverlap(centre, def.HalfExtents, yaw, o.Position, radius, height, up))
                {
                    return ActionResult.Blocked;
                }
            }
        }
        ComponentPool<Character> characters = Entities.Pool<Character>();
        for (int i = 0; i < characters.Count; i++)
        {
            ref Character c = ref characters.Values[i];
            if (!c.State.IsDead && Geometry.BoxCylinderOverlap(centre, def.HalfExtents, yaw, c.State.Position, Tuning.Radius, Tuning.StandingHeight, up))
            {
                return ActionResult.Blocked;
            }
        }
        return ActionResult.Accepted;
    }
}
