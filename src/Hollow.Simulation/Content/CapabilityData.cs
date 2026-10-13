using System;
using System.Collections.Generic;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;

namespace Hollow.Simulation.Content;

/// <summary>
/// capabilities.json (GROUND_SYSTEMS.md §2.1): the kinds of behaviour a world thing may have, in their append-only order
/// (an entry's index is its number), and which ones each kind of thing has. A deployable's come from its blocks in
/// deployables.json and world.json; the things the rules make (bags and bodies, doors and their locks, the claim device,
/// ships, explosives) have theirs here. Each capability has one handler (<see cref="CapabilityHandlers"/>).
/// </summary>
public sealed class CapabilityData
{
    public const string File = "capabilities.json";

    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly CapabilitySet _owned, _container, _burner, _machine;
    private readonly CapabilitySet[] _locks;

    internal CapabilityData(IReadOnlyList<string> keys)
    {
        Keys = keys;
        for (int i = 0; i < keys.Count; i++)
        {
            _index.TryAdd(keys[i], i);
        }
        _owned = Set("owned");
        _container = Set("container");
        _burner = Set("burner");
        _machine = Set("machine");
        Bag = Set("container", "lifetime");
        Corpse = Set("container", "lifetime");
        Door = Set("door", "owned");
        _locks = new CapabilitySet[Enum.GetValues<LockKind>().Length];
        _locks[(int)LockKind.Key] = Set("key-lock");
        _locks[(int)LockKind.Code] = Set("code-lock");
        Cupboard = Set("cupboard", "owned");
        Ship = Set("ship", "owned", "container");
        Explosive = Set("owned", "explosive");
        Dud = Set("owned", "explosive", "lifetime");
        Carcass = Set("lifetime");
    }

    /// <summary>The list's keys, by index.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>A key's index, or −1.</summary>
    public int Index(string key) => _index.TryGetValue(key, out int i) ? i : -1;

    /// <summary>The set of some keys (a key not in the list adds nothing; the registry's check names it).</summary>
    public CapabilitySet Set(params string[] keys)
    {
        CapabilitySet set = CapabilitySet.Empty;
        foreach (string key in keys)
        {
            set = set.With(Index(key));
        }
        return set;
    }

    /// <summary>A bag of dropped items, a body: they hold items, and go after a while.</summary>
    public CapabilitySet Bag { get; }

    public CapabilitySet Corpse { get; }

    /// <summary>A door (its lock's are added while it has one, <see cref="Lock"/>), the claim device, a ship, an explosive.</summary>
    public CapabilitySet Door { get; }

    public CapabilitySet Cupboard { get; }

    public CapabilitySet Ship { get; }

    public CapabilitySet Explosive { get; }

    /// <summary>An explosive whose fuse went out: it lies there for its lifetime unless someone lights it again.</summary>
    public CapabilitySet Dud { get; }

    /// <summary>An animal's carcass: it goes after its species' time, or once harvested out.</summary>
    public CapabilitySet Carcass { get; }

    /// <summary>A lock's, by its kind (none for none).</summary>
    public CapabilitySet Lock(LockKind kind) => (uint)kind < (uint)_locks.Length ? _locks[(int)kind] : CapabilitySet.Empty;

    /// <summary>
    /// A deployable's (or world prop's), from its blocks: placed by a player (owned), <c>slots</c> (container),
    /// <c>device</c> (burner), <c>recycler</c> or <c>research</c> (machine).
    /// </summary>
    public CapabilitySet Of(DeployableDef def)
    {
        CapabilitySet set = _owned;
        if (def.Slots > 0)
        {
            set = set.With(_container);
        }
        if (def.Device != null)
        {
            set = set.With(_burner);
        }
        if (def.IsMachine)
        {
            set = set.With(_machine);
        }
        return set;
    }
}
