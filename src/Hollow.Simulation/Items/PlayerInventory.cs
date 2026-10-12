using System;
using System.Collections.Generic;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>How an intent names a container: the player's own three, or one of those they are looting.</summary>
public enum ContainerRef : byte
{
    Main = 0,
    Belt = 1,
    Wear = 2,

    /// <summary>The open loot: a box, a corpse, a device, or a sleeper's main slots.</summary>
    Loot = 3,

    /// <summary>A sleeper's belt and clothing, when looting one.</summary>
    LootBelt = 4,
    LootWear = 5,
}

/// <summary>A player's items (GROUND.md §6): 24 main slots, 6 on the belt, 7 worn.</summary>
public sealed class PlayerInventory
{
    public const int MainSlots = 24;
    public const int BeltSlots = 6;
    public const int WearSlots = 7;

    public PlayerInventory()
    {
        Main = new Container(ContainerKind.Main, MainSlots);
        Belt = new Container(ContainerKind.Belt, BeltSlots);
        Wear = new Container(ContainerKind.Wear, WearSlots);
        Carried = new[] { Main, Belt };
        All = new[] { Main, Belt, Wear };
    }

    public Container Main { get; }

    public Container Belt { get; }

    public Container Wear { get; }

    /// <summary>Main then belt: where materials are counted and taken from, and where gains land.</summary>
    public IReadOnlyList<Container> Carried { get; }

    /// <summary>Main, belt and clothing.</summary>
    public IReadOnlyList<Container> All { get; }

    public Container this[ContainerRef which] => which switch
    {
        ContainerRef.Main => Main,
        ContainerRef.Belt => Belt,
        ContainerRef.Wear => Wear,
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    public int Count(ItemDef item) => InventoryRules.Count(Carried, item.Id);

    /// <summary>Whether there is enough for <paramref name="times"/> × these amounts.</summary>
    public bool Has(IReadOnlyList<ItemAmount> amounts, int times = 1)
    {
        foreach (ItemAmount a in amounts)
        {
            if (Count(a.Item) < (long)a.Count * times)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Takes <paramref name="times"/> × these amounts (all there, or nothing taken).</summary>
    public bool Take(IReadOnlyList<ItemAmount> amounts, int times = 1)
    {
        if (!Has(amounts, times))
        {
            return false;
        }
        foreach (ItemAmount a in amounts)
        {
            InventoryRules.Take(Carried, a.Item.Id, a.Count * times);
        }
        return true;
    }

    /// <summary>
    /// Gives items: onto stacks of the same item in the main slots and belt, then into empty main slots, then the belt.
    /// Returns how many didn't fit (the caller drops them on the ground). Items with condition come new.
    /// </summary>
    public int Give(GameContent content, ItemDef item, int count) => Give(content, ItemStack.Of(item, count));

    public int Give(GameContent content, ItemStack stack) => InventoryRules.AddTo(content, Carried, stack);

    /// <summary>Whether a stack would fit completely (without changing anything).</summary>
    public bool Fits(GameContent content, ItemStack stack)
    {
        ItemDef? item = content.Item(stack.Item);
        if (item == null)
        {
            return false;
        }
        int room = 0;
        foreach (Container c in Carried)
        {
            for (int i = 0; i < c.Size; i++)
            {
                ItemStack s = c[i];
                if (s.IsEmpty)
                {
                    room += item.StackSize;
                }
                else if (s.Item == stack.Item && s.Condition == stack.Condition)
                {
                    room += Math.Max(0, item.StackSize - s.Count);
                }
                if (room >= stack.Count)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Every non-empty stack, main, belt then clothing.</summary>
    public IEnumerable<ItemStack> Stacks()
    {
        foreach (Container c in All)
        {
            for (int i = 0; i < c.Size; i++)
            {
                if (!c[i].IsEmpty)
                {
                    yield return c[i];
                }
            }
        }
    }

    public void Clear()
    {
        foreach (Container c in All)
        {
            c.Clear();
        }
    }

    /// <summary>The total insulation and the protection a hit region gets from what is worn.</summary>
    public float Insulation(GameContent content)
    {
        float total = 0;
        for (int i = 0; i < Wear.Size; i++)
        {
            total += content.Item(Wear[i].Item)?.Wear?.Insulation ?? 0;
        }
        return total;
    }

    /// <summary>The fraction of damage of a kind taken off a hit on a region by worn items covering it (at most 0.8).</summary>
    public float Protection(GameContent content, HitRegion region, bool projectile)
    {
        BodyRegions covered = region switch
        {
            HitRegion.Head => BodyRegions.Head,
            HitRegion.Body => BodyRegions.Body,
            HitRegion.Limb => BodyRegions.Limbs,
            _ => BodyRegions.None,
        };
        float total = 0;
        for (int i = 0; i < Wear.Size; i++)
        {
            if (content.Item(Wear[i].Item)?.Wear is { } wear && (wear.Covers & covered) != 0)
            {
                total += projectile ? wear.Projectile : wear.Melee;
            }
        }
        return Math.Min(0.8f, total);
    }
}
