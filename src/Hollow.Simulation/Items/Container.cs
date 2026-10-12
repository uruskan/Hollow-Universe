using System;
using System.Collections.Generic;
using System.Globalization;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>Some of one item in a slot. Empty when <see cref="Item"/> is 0.</summary>
public struct ItemStack : IEquatable<ItemStack>
{
    public ItemStack(ushort item, int count, ushort condition = 0)
    {
        Item = item;
        Count = count;
        Condition = condition;
    }

    /// <summary>The item's ID (items.json); 0 for an empty slot.</summary>
    public ushort Item;

    public int Count;

    /// <summary>Uses left before it breaks (items with condition); 0 otherwise.</summary>
    public ushort Condition;

    public readonly bool IsEmpty => Item == 0 || Count <= 0;

    public static ItemStack Of(ItemDef item, int count) => new(item.Id, count, (ushort)Math.Clamp(item.MaxCondition, 0, ushort.MaxValue));

    public readonly bool Equals(ItemStack other) => Item == other.Item && Count == other.Count && Condition == other.Condition;

    public override readonly bool Equals(object? obj) => obj is ItemStack s && Equals(s);

    public override readonly int GetHashCode() => HashCode.Combine(Item, Count, Condition);

    public static bool operator ==(ItemStack a, ItemStack b) => a.Equals(b);

    public static bool operator !=(ItemStack a, ItemStack b) => !a.Equals(b);

    public override readonly string ToString() => IsEmpty ? "empty" :
        string.Create(CultureInfo.InvariantCulture, $"{Count} × #{Item}{(Condition > 0 ? $" ({Condition})" : "")}");
}

/// <summary>What a container is: it decides which items its slots take.</summary>
public enum ContainerKind : byte
{
    /// <summary>A player's 24 main slots.</summary>
    Main,

    /// <summary>A player's 6 belt slots (the hotbar).</summary>
    Belt,

    /// <summary>A player's 7 clothing slots: only clothing, one item per layer.</summary>
    Wear,

    /// <summary>A storage box: anything.</summary>
    Box,

    /// <summary>A dead player's belongings: take only.</summary>
    Corpse,

    /// <summary>A campfire or furnace: fuel, input and output slots (<see cref="DeviceStats"/>).</summary>
    Device,
}

/// <summary>
/// Slots of items with a kind that decides what they take. Every change goes through <see cref="Set"/>, which marks
/// the slot dirty (the server sends dirty slots to whoever sees the container) and raises <see cref="Revision"/>.
/// One thread at a time.
/// </summary>
public sealed class Container
{
    private readonly ItemStack[] _slots;
    private readonly bool[] _dirty;
    private int _dirtyCount;

    public Container(ContainerKind kind, int size, DeployableDef? device = null)
    {
        if (size is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "1 … 1024 slots");
        }
        Kind = kind;
        _slots = new ItemStack[size];
        _dirty = new bool[size];
        Deployable = device;
    }

    public ContainerKind Kind { get; }

    public int Size => _slots.Length;

    /// <summary>The deployable this is the storage of (a device's slots come from it).</summary>
    public DeployableDef? Deployable { get; }

    /// <summary>Raised on every change.</summary>
    public uint Revision { get; private set; }

    public ItemStack this[int slot] => _slots[slot];

    public ReadOnlySpan<ItemStack> Slots => _slots;

    public bool InRange(int slot) => slot >= 0 && slot < _slots.Length;

    public void Set(int slot, ItemStack stack)
    {
        if (stack.IsEmpty)
        {
            stack = default;
        }
        if (_slots[slot] == stack)
        {
            return;
        }
        _slots[slot] = stack;
        if (!_dirty[slot])
        {
            _dirty[slot] = true;
            _dirtyCount++;
        }
        Revision++;
    }

    public void Clear()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            Set(i, default);
        }
    }

    public bool IsEmpty
    {
        get
        {
            foreach (ItemStack s in _slots)
            {
                if (!s.IsEmpty)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public bool HasDirty => _dirtyCount > 0;

    /// <summary>Adds the slots changed since the last call to <paramref name="into"/> and forgets them.</summary>
    public void TakeDirty(List<int> into)
    {
        if (_dirtyCount == 0)
        {
            return;
        }
        for (int i = 0; i < _dirty.Length; i++)
        {
            if (_dirty[i])
            {
                into.Add(i);
                _dirty[i] = false;
            }
        }
        _dirtyCount = 0;
    }

    /// <summary>Forgets what changed (after a full copy was sent).</summary>
    public void ClearDirty()
    {
        Array.Clear(_dirty);
        _dirtyCount = 0;
    }

    /// <summary>Copies another container's slots into this one (same size), marking changes.</summary>
    public void CopyFrom(Container other)
    {
        for (int i = 0; i < Math.Min(Size, other.Size); i++)
        {
            Set(i, other[i]);
        }
    }

    // ---------------------------------------------------------------- what goes where

    /// <summary>
    /// Whether a player may put <paramref name="stack"/> into <paramref name="slot"/>, given what else is in the
    /// container (<paramref name="replacing"/>: the slot whose item leaves in the same move, ignored for clothing layers).
    /// </summary>
    public bool Accepts(GameContent content, int slot, in ItemStack stack, int replacing = -1)
    {
        if (!InRange(slot) || stack.IsEmpty)
        {
            return false;
        }
        ItemDef? item = content.Item(stack.Item);
        if (item == null || stack.Count > item.StackSize)
        {
            return false;
        }
        switch (Kind)
        {
            case ContainerKind.Wear:
                if (item.Wear == null || stack.Count != 1)
                {
                    return false;
                }
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (i == slot || i == replacing || _slots[i].IsEmpty)
                    {
                        continue;
                    }
                    if (content.Item(_slots[i].Item)?.Wear is { } worn && (worn.Layers & item.Wear.Layers) != 0)
                    {
                        return false;
                    }
                }
                return true;
            case ContainerKind.Corpse:
                return false;
            case ContainerKind.Device:
                DeviceStats? device = Deployable?.Device;
                if (device == null)
                {
                    return false;
                }
                if (Contains(device.FuelSlots, slot))
                {
                    return item.Fuel != null;
                }
                if (Contains(device.InputSlots, slot))
                {
                    return item.Cook != null && item.Cook.In == device.Cooks;
                }
                return false;
            default:
                return true;
        }
    }

    private static bool Contains(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return true;
            }
        }
        return false;
    }
}
