using System;
using System.Collections.Generic;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>
/// Moving items between slots (GROUND.md §6, Rust's rules): the server applies these to its containers, and the client
/// to its copy for an optimistic update, so both agree. Every operation either changes nothing and says why, or does
/// all it said.
/// </summary>
public static class InventoryRules
{
    /// <summary>
    /// Drag <paramref name="amount"/> (0: all) from one slot to another. Onto an empty slot it moves (a split when not
    /// all); onto the same item it tops that stack up to its size and leaves the rest; onto another item it swaps the
    /// two (only when moving the whole stack). <paramref name="expected"/> is the item the client saw in the source (0:
    /// don't check).
    /// </summary>
    public static Outcome Move(GameContent content, Container from, int fromSlot, Container to, int toSlot, int amount, ushort expected = 0)
    {
        if (!from.InRange(fromSlot) || !to.InRange(toSlot) || amount < 0 || (from == to && fromSlot == toSlot))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack source = from[fromSlot];
        if (source.IsEmpty || (expected != 0 && source.Item != expected) || amount > source.Count)
        {
            return OutcomeCode.Stale;
        }
        ItemDef? item = content.Item(source.Item);
        if (item == null)
        {
            return OutcomeCode.Invalid;
        }
        int moving = amount == 0 ? source.Count : amount;
        ItemStack target = to[toSlot];
        if (target.IsEmpty)
        {
            ItemStack moved = source.WithCount(moving);
            if (!to.Accepts(content, toSlot, moved, from == to ? fromSlot : -1))
            {
                return OutcomeCode.NotAllowed;
            }
            source.Count -= moving;
            from.Set(fromSlot, source);
            to.Set(toSlot, moved);
            return OutcomeCode.Done;
        }
        if (target.Item == source.Item && target.Condition == source.Condition && item.StackSize > 1)
        {
            int room = item.StackSize - target.Count;
            if (room <= 0)
            {
                return OutcomeCode.NoSpace;
            }
            if (!to.Accepts(content, toSlot, new ItemStack(source.Item, Math.Min(item.StackSize, target.Count + 1), source.Condition)))
            {
                return OutcomeCode.NotAllowed;
            }
            int n = Math.Min(room, moving);
            target.Count += n;
            source.Count -= n;
            to.Set(toSlot, target);
            from.Set(fromSlot, source);
            return OutcomeCode.Done;
        }
        // A different item (or two of a kind that doesn't stack): swap, whole stacks only.
        if (moving != source.Count)
        {
            return OutcomeCode.NotAllowed;
        }
        if (!to.Accepts(content, toSlot, source, from == to ? fromSlot : toSlot) ||
            !from.Accepts(content, fromSlot, target, from == to ? toSlot : fromSlot))
        {
            return OutcomeCode.NotAllowed;
        }
        to.Set(toSlot, source);
        from.Set(fromSlot, target);
        return OutcomeCode.Done;
    }

    /// <summary>
    /// Moves as much of a slot as fits into <paramref name="targets"/>, in order: first onto stacks of the same item,
    /// then into empty slots that take it (Rust's quick move, and how picked-up items land). What doesn't fit stays.
    /// </summary>
    public static Outcome QuickMove(GameContent content, Container from, int fromSlot, IReadOnlyList<Container> targets, ushort expected = 0)
    {
        if (!from.InRange(fromSlot))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack source = from[fromSlot];
        if (source.IsEmpty || (expected != 0 && source.Item != expected))
        {
            return OutcomeCode.Stale;
        }
        int left = AddTo(content, targets, source, from);
        if (left == source.Count)
        {
            return OutcomeCode.NoSpace;
        }
        source.Count = left;
        from.Set(fromSlot, source);
        return OutcomeCode.Done;
    }

    /// <summary>
    /// Puts a stack into containers: onto partial stacks of the same item first (in container order), then into empty
    /// slots that accept it. Returns how many didn't fit. <paramref name="except"/>'s slots are skipped entirely (the
    /// source of a quick move). <paramref name="byPlayer"/> false: the game itself fills them (a bag of dropped items),
    /// so what a player may put where (<see cref="Container.Accepts"/>: take only, clothing layers, devices' slots)
    /// doesn't apply.
    /// </summary>
    public static int AddTo(GameContent content, IReadOnlyList<Container> targets, ItemStack stack, Container? except = null, bool byPlayer = true)
    {
        ItemDef? item = content.Item(stack.Item);
        if (item == null || stack.IsEmpty)
        {
            return stack.Count;
        }
        int left = stack.Count;
        if (item.StackSize > 1)
        {
            foreach (Container c in targets)
            {
                if (c == except)
                {
                    continue;
                }
                for (int i = 0; i < c.Size && left > 0; i++)
                {
                    ItemStack s = c[i];
                    if (s.Item == stack.Item && s.Condition == stack.Condition && s.Count < item.StackSize &&
                        (!byPlayer || c.Accepts(content, i, new ItemStack(s.Item, s.Count + 1, s.Condition))))
                    {
                        int n = Math.Min(item.StackSize - s.Count, left);
                        s.Count += n;
                        left -= n;
                        c.Set(i, s);
                    }
                }
            }
        }
        foreach (Container c in targets)
        {
            if (c == except)
            {
                continue;
            }
            for (int i = 0; i < c.Size && left > 0; i++)
            {
                if (!c[i].IsEmpty)
                {
                    continue;
                }
                ItemStack part = stack.WithCount(Math.Min(item.StackSize, left));
                if (!byPlayer || c.Accepts(content, i, part))
                {
                    c.Set(i, part);
                    left -= part.Count;
                }
            }
        }
        return left;
    }

    /// <summary>Takes <paramref name="amount"/> (0: all) out of a slot, for dropping it; the stack taken, or none.</summary>
    public static Outcome Remove(Container from, int slot, int amount, ushort expected, out ItemStack taken)
    {
        taken = default;
        if (!from.InRange(slot) || amount < 0)
        {
            return OutcomeCode.Invalid;
        }
        ItemStack source = from[slot];
        if (source.IsEmpty || (expected != 0 && source.Item != expected) || amount > source.Count)
        {
            return OutcomeCode.Stale;
        }
        int n = amount == 0 ? source.Count : amount;
        taken = source.WithCount(n);
        source.Count -= n;
        from.Set(slot, source);
        return OutcomeCode.Done;
    }

    /// <summary>How many of an item the containers hold.</summary>
    public static int Count(IReadOnlyList<Container> containers, ushort item)
    {
        int total = 0;
        foreach (Container c in containers)
        {
            for (int i = 0; i < c.Size; i++)
            {
                if (c[i].Item == item)
                {
                    total += c[i].Count;
                }
            }
        }
        return total;
    }

    /// <summary>Takes a number of an item from the containers, in slot order; all or nothing.</summary>
    public static bool Take(IReadOnlyList<Container> containers, ushort item, int count)
    {
        if (count <= 0)
        {
            return true;
        }
        if (Count(containers, item) < count)
        {
            return false;
        }
        int left = count;
        foreach (Container c in containers)
        {
            for (int i = 0; i < c.Size && left > 0; i++)
            {
                ItemStack s = c[i];
                if (s.Item != item)
                {
                    continue;
                }
                int n = Math.Min(s.Count, left);
                s.Count -= n;
                left -= n;
                c.Set(i, s);
            }
        }
        return true;
    }
}
