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
    private readonly CapabilitySet _owned, _container, _burner, _machine, _standing, _station, _portable;
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
        _standing = Set("standing");
        _station = Set("station");
        _portable = Set("portable");
        Bag = Set("container", "lifetime", "pickup");
        Corpse = Set("container", "lifetime");
        Door = Set("door", "owned");
        _locks = new CapabilitySet[Enum.GetValues<LockKind>().Length];
        _locks[(int)LockKind.Key] = Set("key-lock");
        _locks[(int)LockKind.Code] = Set("code-lock");
        Cupboard = Set("cupboard", "owned", "container");
        Ship = Set("ship", "owned");
        Explosive = Set("owned", "explosive");
        Dud = Set("owned", "explosive", "lifetime", "dud");
        Carcass = Set("lifetime");
        Sleeper = Set("container");
        Down = Set("container", "wounded");
        Awake = Set("character");
        Pickup = Set("pickup");
        Arrow = Set("pickup", "lifetime", "arrow");
        Fire = Set("owned", "fire");
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

    /// <summary>A door (its lock's are added while it has one, <see cref="Lock"/>), the tool cupboard (its store a container), a ship, an explosive.</summary>
    public CapabilitySet Door { get; }

    public CapabilitySet Cupboard { get; }

    /// <summary>A ship, on every part (a part adds its own, ships.json parts: a seat, a door, a tank, its cargo, the helm's list).</summary>
    public CapabilitySet Ship { get; }

    public CapabilitySet Explosive { get; }

    /// <summary>An explosive whose fuse went out: it lies there for its lifetime unless someone lights it again.</summary>
    public CapabilitySet Dud { get; }

    /// <summary>An animal's carcass: it goes after its species' time, or once harvested out.</summary>
    public CapabilitySet Carcass { get; }

    /// <summary>A player asleep (logged off): looted.</summary>
    public CapabilitySet Sleeper { get; }

    /// <summary>A player down (wounded): looted, and helped up.</summary>
    public CapabilitySet Down { get; }

    /// <summary>A player up and about: a team's leader invites them.</summary>
    public CapabilitySet Awake { get; }

    /// <summary>A natural pickup or berry bush: taken with E.</summary>
    public CapabilitySet Pickup { get; }

    /// <summary>A spent arrow stuck where it hit: taken with E, gone after a while, falls when what holds it goes.</summary>
    public CapabilitySet Arrow { get; }

    /// <summary>A fire a blast left (IW4): its owner's, and it falls to what is below when what holds it goes.</summary>
    public CapabilitySet Fire { get; }

    /// <summary>A lock's, by its kind (none for none).</summary>
    public CapabilitySet Lock(LockKind kind) => (uint)kind < (uint)_locks.Length ? _locks[(int)kind] : CapabilitySet.Empty;

    /// <summary>
    /// A deployable's (or world prop's), from its blocks: placed by a player (owned), standing on what it was placed on,
    /// <c>slots</c> (container), <c>device</c> (burner), <c>recycler</c> or <c>research</c> (machine), <c>workbench</c>
    /// (station), <c>pickup</c> (portable).
    /// </summary>
    public CapabilitySet Of(DeployableDef def)
    {
        CapabilitySet set = _owned.With(_standing);
        if (def.Slots > 0)
        {
            set = set.With(_container);
        }
        if (def.Device != null)
        {
            set = set.With(_burner);
        }
        if (def.Recycler != null || def.Research != null)
        {
            set = set.With(_machine);
        }
        if (def.Workbench > 0)
        {
            set = set.With(_station);
        }
        if (def.Pickup)
        {
            set = set.With(_portable);
        }
        return set;
    }
}
