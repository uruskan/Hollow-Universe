using System.Collections.Generic;
using System.Numerics;
using System;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// Loose items on the ground (GROUND.md §6.1). Everything that lands there goes through Spill: a stack dropped from an
// inventory, a finished craft, a refund or a gift that doesn't fit, a broken box's, furnace's or barrel's contents, a
// spent arrow. Spill packs it into the bag lying nearest within survival.json's bag.mergeRadius that has room, else into
// a new bag where it comes to rest, so what lands together is one bag (Rust's loot bag). A bag is a DroppedContainer:
// take only, with the bag's lifetime (survival.json lifetimes.bag, GROUND_SYSTEMS.md §3.7): gone once emptied, otherwise
// its seconds after the newest item went in.
public sealed class SpillSystem : ZoneSystem
{
    private ActionSystem? _actionsSystem;
    private ActionSystem Actions => _actionsSystem ??= Zone.Require<ActionSystem>();
    private ShipSystem? _shipsSystem;
    private ShipSystem Ships => _shipsSystem ??= Zone.Require<ShipSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    /// <summary>A dropped stack lands this far ahead of the character's feet (less before a wall).</summary>
    private const float DropAhead = 0.7f;

    /// <summary>About how far a bag reaches from its middle (one dropped before a wall stays on this side of it).</summary>
    private const float BagRadius = 0.25f;

    /// <summary>Loose things fall onto what is under them from this high above where they land, at most this far.</summary>
    private const float RestFrom = 0.5f;

    private const float RestDepth = 4f;

    private readonly List<DroppedBag> _bagsNear = new();
    private readonly Container[] _oneBag = new Container[1];

    /// <summary>Drops a stack in front of a character: into the bag lying there, or a new one.</summary>
    public void Drop(Entity by, ItemStack stack)
    {
        (Vector3 at, float yaw) = DropPoint(by);
        Spill(at, stack, yaw);
    }

    /// <summary>Drops several stacks in front of a character (refunds, crafts that don't fit): together, in one bag.</summary>
    public void Drop(Entity by, IReadOnlyList<ItemStack> stacks)
    {
        if (stacks.Count == 0)
        {
            return;
        }
        (Vector3 at, float yaw) = DropPoint(by);
        Vector3 rest = RestingPoint(at);
        foreach (ItemStack stack in stacks)
        {
            Pack(rest, stack, yaw);
        }
    }

    /// <summary>
    /// Gives a stack to a player as far as it fits (onto stacks of it, then empty slots); the rest is dropped in front of
    /// them. Returns how many went into the inventory.
    /// </summary>
    public int GiveOrDrop(Entity entity, Player p, ItemStack stack)
    {
        int left = p.Inventory.Give(Content, stack);
        if (left > 0)
        {
            Drop(entity, stack.WithCount(left));
        }
        return stack.Count - left;
    }

    /// <summary>
    /// Loose items at a point, the one way anything lands on the ground: into the nearest bag within the merge radius
    /// that has room, else a new bag where they come to rest. Returns the bag that took the last of them (null when
    /// there was nothing to spill).
    /// </summary>
    public DroppedBag? Spill(Vector3 point, ItemStack stack, float? yaw = null) => Pack(RestingPoint(point), stack, yaw);

    /// <summary>Everything a container held, at a point (a broken box, furnace or claim device): one bag. The container is left empty.</summary>
    public void Spill(Vector3 point, Container from, float? yaw = null)
    {
        if (from.IsEmpty)
        {
            return;
        }
        Vector3 rest = RestingPoint(point);
        for (int i = 0; i < from.Size; i++)
        {
            if (!from[i].IsEmpty)
            {
                Pack(rest, from[i], yaw);
                from.Set(i, default);
            }
        }
    }

    /// <summary>
    /// Where loose things come to rest below a point: on the first solid surface under it (a floor or foundation, a box's
    /// lid, the ground's own collision) within a few metres, else on the ground.
    /// </summary>
    public Vector3 RestingPoint(Vector3 point)
    {
        Vector3 up = Frame.UpAt(point);
        lock (JoltRuntime.Gate)
        {
            // A ray that starts inside something (a wall, a trunk) meets it at once: then the ground below will do.
            if (Ships.ShipCollider.Ray(point + up * RestFrom, -up, RestFrom + RestDepth, ShipCollider.NoBody, out ShipRayHit hit) && hit.Distance > 1e-3f)
            {
                return hit.Point;
            }
        }
        return Settle(point);
    }

    // Where a character's drop lands: a little ahead of their feet, short of a wall or anything else solid in the way.
    private (Vector3 At, float Yaw) DropPoint(Entity by)
    {
        ref Character c = ref Entities.Get<Character>(by);
        Vector3 feet = c.State.Position;
        Vector3 up = Frame.UpAt(feet);
        (Vector3 forward, _) = CharacterMotor.HorizontalBasis(c.Input.Yaw, up);
        float ahead = DropAhead;
        lock (JoltRuntime.Gate)
        {
            if (Ships.ShipCollider.Ray(feet + up * 0.6f, forward, DropAhead + BagRadius, ShipCollider.NoBody, out ShipRayHit hit))
            {
                ahead = MathF.Max(0, hit.Distance - BagRadius);
            }
        }
        return (feet + forward * ahead, c.Input.Yaw);
    }

    // A stack into the bags near where it rests, the nearest with room first, then into new bags there.
    private DroppedBag? Pack(Vector3 rest, ItemStack stack, float? yaw)
    {
        GameContent content = Content;
        if (stack.IsEmpty || content.Item(stack.Item) == null)
        {
            return null;
        }
        LifetimeRule lifetime = content.Survival.BagLifetime;
        BagsNear(rest, content.Survival.BagMergeRadius, _bagsNear);
        int left = stack.Count;
        DroppedBag? last = null;
        foreach (DroppedBag bag in _bagsNear)
        {
            int before = left;
            left = Fill(bag, stack.WithCount(left));
            last = left < before ? bag : last;
            if (left == 0)
            {
                return last;
            }
        }
        while (left > 0)
        {
            EntityId id = NextId();
            var bag = new DroppedBag(id, content.Survival.BagName, content.Survival.BagSlots, rest, Frame.UpAt(rest), yaw ?? BagYaw(id), lifetime,
                Tick + lifetime.Ticks);
            Things.AddStatic(bag);
            int before = left;
            left = Fill(bag, stack.WithCount(left));
            last = bag;
            if (left == before)
            {
                break; // nothing goes into an empty bag (a stack the game can't hold): don't make bags forever
            }
        }
        return last;
    }

    // What fits of a stack goes into a bag (onto stacks of it, then empty slots), and its time starts again (its lifetime
    // restarts on a deposit). Returns what is left.
    private int Fill(DroppedBag bag, ItemStack stack)
    {
        _oneBag[0] = bag.Loot;
        int left = InventoryRules.AddTo(Content, _oneBag, stack, byPlayer: false);
        if (left < stack.Count)
        {
            Things.Restart(bag, LifetimeRestart.Deposit);
            bag.PublishChanges();
        }
        return left;
    }

    // A bag's turn when nothing gives one (a broken box's contents, an arrow): its own, so bags don't all lie alike.
    private static float BagYaw(EntityId id) => id.Value % 628 / 100f;

    /// <summary>Bags lying within <paramref name="radius"/> of a point, nearest first (looked for among the loose things alone).</summary>
    public void BagsNear(Vector3 point, float radius, List<DroppedBag> into)
    {
        into.Clear();
        foreach (ILifetime thing in Things._loose)
        {
            if (thing is DroppedBag bag && Vector3.DistanceSquared(bag.Position, point) <= radius * radius)
            {
                into.Add(bag);
            }
        }
        into.Sort((a, b) => Vector3.DistanceSquared(a.Position, point).CompareTo(Vector3.DistanceSquared(b.Position, point)));
    }

    // What a looter took from shows others its count (a bag's stacks) at once; a loose thing (a body, a bag) whose last
    // stack they took goes at once if its lifetime ends it so (GROUND_SYSTEMS.md §3.7), and the looter's panel with it.
    internal Outcome TookFrom(Player p, Outcome result)
    {
        if (!result.Ok || Actions.LootTarget(p).Thing is not { } looted)
        {
            return result;
        }
        looted.PublishChanges();
        if (looted is ILifetime loose && Things.EndIfEmptied(loose))
        {
            p.Looting = EntityId.None;
            p.LootRevision++;
        }
        return result;
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class SpillSystemAccess
{
    public static SpillSystem Spill(this Zone zone) => zone.Require<SpillSystem>();
}
