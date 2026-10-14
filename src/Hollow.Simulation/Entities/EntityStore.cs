using System;
using System.Collections.Generic;

namespace Hollow.Simulation.Entities;

/// <summary>
/// A live entity in one <see cref="EntityStore"/>: its slot and the slot's generation, so a handle kept after the
/// entity is destroyed never reaches whatever reuses the slot. Handles are local to a process; <see cref="EntityId"/>
/// is the permanent identity.
/// </summary>
public readonly record struct Entity(int Index, int Generation)
{
    public static readonly Entity None = new(-1, 0);
}

/// <summary>
/// Entities and their components (ARCHITECTURE.md §10.2, §18): an entity is a slot with a permanent 64-bit
/// <see cref="EntityId"/>; components are plain structs, one <see cref="ComponentPool{T}"/> per type (a sparse set:
/// dense arrays iterated without lookups, and O(1) add, get and remove by slot). Small and owned by us; archetype
/// storage can replace the pools if profiling ever asks for it. One thread at a time (the zone's).
/// </summary>
public sealed class EntityStore
{
    private readonly List<int> _generations = new();
    private readonly List<EntityId> _ids = new();
    private readonly Stack<int> _free = new();
    private readonly Dictionary<EntityId, int> _byId = new();
    private readonly Dictionary<Type, IComponentPool> _pools = new();

    /// <summary>Live entities.</summary>
    public int Count => _byId.Count;

    public Entity Create(EntityId id)
    {
        if (id.IsNone || _byId.ContainsKey(id))
        {
            throw new ArgumentException($"Entity {id} is none or already exists.", nameof(id));
        }
        int index;
        if (_free.Count > 0)
        {
            index = _free.Pop();
            _ids[index] = id;
        }
        else
        {
            index = _generations.Count;
            _generations.Add(1);
            _ids.Add(id);
        }
        _byId.Add(id, index);
        return new Entity(index, _generations[index]);
    }

    public bool IsAlive(Entity entity) =>
        entity.Index >= 0 && entity.Index < _generations.Count && _generations[entity.Index] == entity.Generation &&
        !_ids[entity.Index].IsNone;

    /// <summary>Destroys an entity and all its components. Does nothing for a stale handle.</summary>
    public void Destroy(Entity entity)
    {
        if (!IsAlive(entity))
        {
            return;
        }
        foreach (IComponentPool pool in _pools.Values)
        {
            pool.Remove(entity.Index);
        }
        _byId.Remove(_ids[entity.Index]);
        _ids[entity.Index] = EntityId.None;
        _generations[entity.Index]++;
        _free.Push(entity.Index);
    }

    public EntityId IdOf(Entity entity) => IsAlive(entity) ? _ids[entity.Index] : EntityId.None;

    public bool TryFind(EntityId id, out Entity entity)
    {
        if (_byId.TryGetValue(id, out int index))
        {
            entity = new Entity(index, _generations[index]);
            return true;
        }
        entity = Entity.None;
        return false;
    }

    /// <summary>The handle of a live slot (for iterating a pool's <see cref="ComponentPool{T}.Slots"/>).</summary>
    public Entity AtSlot(int index) => new(index, _generations[index]);

    /// <summary>The pool of a component type, created on first use.</summary>
    public ComponentPool<T> Pool<T>() where T : struct
    {
        if (!_pools.TryGetValue(typeof(T), out IComponentPool? pool))
        {
            pool = new ComponentPool<T>();
            _pools.Add(typeof(T), pool);
        }
        return (ComponentPool<T>)pool;
    }

    public ref T Add<T>(Entity entity, in T value) where T : struct
    {
        Check(entity);
        return ref Pool<T>().Add(entity.Index, value);
    }

    public ref T Get<T>(Entity entity) where T : struct
    {
        Check(entity);
        return ref Pool<T>().Get(entity.Index);
    }

    public bool Has<T>(Entity entity) where T : struct => IsAlive(entity) && Pool<T>().Has(entity.Index);

    public void Remove<T>(Entity entity) where T : struct
    {
        if (IsAlive(entity))
        {
            Pool<T>().Remove(entity.Index);
        }
    }

    private void Check(Entity entity)
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is not alive.");
        }
    }
}

internal interface IComponentPool
{
    void Remove(int slot);
}

/// <summary>All components of one type: a sparse set keyed by entity slot.</summary>
public sealed class ComponentPool<T> : IComponentPool where T : struct
{
    private int[] _sparse = new int[64];
    private T[] _dense = new T[16];
    private int[] _slots = new int[16];

    public int Count { get; private set; }

    /// <summary>The components, densely packed; iterate with <see cref="Slots"/> for their owners.</summary>
    public Span<T> Values => _dense.AsSpan(0, Count);

    /// <summary>The owning slot of each value in <see cref="Values"/>.</summary>
    public ReadOnlySpan<int> Slots => _slots.AsSpan(0, Count);

    public bool Has(int slot) => slot < _sparse.Length && _sparse[slot] != 0;

    public ref T Get(int slot)
    {
        if (!Has(slot))
        {
            throw new KeyNotFoundException($"No {typeof(T).Name} on slot {slot}.");
        }
        return ref _dense[_sparse[slot] - 1];
    }

    public ref T Add(int slot, in T value)
    {
        if (Has(slot))
        {
            ref T existing = ref _dense[_sparse[slot] - 1];
            existing = value;
            return ref existing;
        }
        if (slot >= _sparse.Length)
        {
            Array.Resize(ref _sparse, Math.Max(slot + 1, _sparse.Length * 2));
        }
        if (Count == _dense.Length)
        {
            Array.Resize(ref _dense, Count * 2);
            Array.Resize(ref _slots, Count * 2);
        }
        _dense[Count] = value;
        _slots[Count] = slot;
        _sparse[slot] = ++Count;
        return ref _dense[Count - 1];
    }

    public void Remove(int slot)
    {
        if (!Has(slot))
        {
            return;
        }
        int index = _sparse[slot] - 1;
        int last = Count - 1;
        if (index != last)
        {
            _dense[index] = _dense[last];
            _slots[index] = _slots[last];
            _sparse[_slots[index]] = index + 1;
        }
        _dense[last] = default;
        _sparse[slot] = 0;
        Count--;
    }
}
