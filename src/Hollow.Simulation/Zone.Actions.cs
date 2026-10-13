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
    // Opening a container is a verb (GROUND_SYSTEMS.md §5, the container capability's): Zone.Interaction.cs.

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

    // ---------------------------------------------------------------- placing

    /// <summary>
    /// Places the deployable in a slot at a point on the ground (GROUND.md §6): within reach, on ground no steeper than
    /// it allows, not under water, overlapping no deployable, tree, outcrop or character.
    /// </summary>
    public Outcome Place(Entity entity, ContainerRef from, int slot, ushort expected, Vector3 target, float yaw)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        if (from is not (ContainerRef.Main or ContainerRef.Belt))
        {
            return OutcomeCode.NotAllowed;
        }
        Container source = p.Inventory[from];
        if (!source.InRange(slot) || !float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z) || !float.IsFinite(yaw))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack s = source[slot];
        if (s.IsEmpty || (expected != 0 && s.Item != expected))
        {
            return OutcomeCode.Stale;
        }
        if (Content!.Item(s.Item)?.Deploys is not { } def)
        {
            return OutcomeCode.NotUsable;
        }
        Outcome check = CanPlace(EyeOf(Entities.Get<Character>(entity).State), def, target, yaw, out Vector3 ground);
        if (check != OutcomeCode.Done)
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
        return OutcomeCode.Done;
    }

    /// <summary>The placement checks alone (the client runs them too, to tint its preview); the ground point found.</summary>
    public Outcome CanPlace(Vector3 eye, DeployableDef def, Vector3 target, float yaw, out Vector3 ground)
    {
        ground = Settle(target);
        if (Vector3.Distance(eye, target) > Content!.PlacementReach + 0.25f)
        {
            return OutcomeCode.OutOfReach;
        }
        if (MathF.Abs(target.X) > PhysicsWorld.Extent - PhysicsWorld.TileSize || MathF.Abs(target.Z) > PhysicsWorld.Extent - PhysicsWorld.TileSize)
        {
            return OutcomeCode.BadPlacement;
        }
        Vector3 up = Frame.UpAt(ground);
        if (MathF.Abs(Vector3.Dot(target - ground, up)) > Content.PlacementMaxStep)
        {
            return OutcomeCode.BadPlacement;
        }
        if (Surface.SeaRadius > 0 && Frame.ToBody(ground).Length < Surface.SeaRadius + 0.05)
        {
            return OutcomeCode.BadPlacement;
        }
        (_, float slope) = GroundNormal(ground);
        if (slope > def.MaxSlopeDegrees * MathF.PI / 180)
        {
            return OutcomeCode.TooSteep;
        }
        Vector3 centre = ground + up * def.HalfExtents.Y;
        _near.Clear();
        DeployablesNear(ground, 5f, _near);
        foreach (Deployable other in _near)
        {
            if (Geometry.BoxesOverlap(centre, def.HalfExtents, yaw, other.Centre, other.Def.HalfExtents, other.Yaw, up))
            {
                return OutcomeCode.Blocked;
            }
        }
        _traceNature.Clear();
        Nature!.Query(ground.X - 4, ground.Z - 4, ground.X + 4, ground.Z + 4, _traceNature);
        foreach (NaturalObject o in _traceNature)
        {
            if (Content.Natural(o.Kind) is { IsSolid: true } kind && !Natural!.IsDepleted(o.Id))
            {
                float radius = kind.Radius * o.Scale, height = NaturalShapes.Top(o, kind);
                if (Geometry.BoxCylinderOverlap(centre, def.HalfExtents, yaw, o.Position, radius, height, up))
                {
                    return OutcomeCode.Blocked;
                }
            }
        }
        ComponentPool<Character> characters = Entities.Pool<Character>();
        for (int i = 0; i < characters.Count; i++)
        {
            ref Character c = ref characters.Values[i];
            if (!c.State.IsDead && Geometry.BoxCylinderOverlap(centre, def.HalfExtents, yaw, c.State.Position, Tuning.Radius, Tuning.StandingHeight, up))
            {
                return OutcomeCode.Blocked;
            }
        }
        return OutcomeCode.Done;
    }
}
