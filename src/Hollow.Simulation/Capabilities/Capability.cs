using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Teams;

namespace Hollow.Simulation.Capabilities;

/// <summary>
/// A set of capabilities (GROUND_SYSTEMS.md §2.1): bit <c>i</c> is capabilities.json's entry <c>i</c>. A thing's set
/// comes from its definition's blocks (a deployable's <c>slots</c>, <c>device</c>, <c>workbench</c>…), worked out once
/// when the data is read, plus what it carries now (a door's lock).
/// </summary>
public readonly record struct CapabilitySet(ulong Bits)
{
    public static readonly CapabilitySet Empty = default;

    /// <summary>At most this many capabilities (the list's length is checked when the data is read).</summary>
    public const int Most = 64;

    public bool Has(int index) => index is >= 0 and < Most && (Bits & 1UL << index) != 0;

    public bool IsEmpty => Bits == 0;

    public CapabilitySet With(int index) => index is >= 0 and < Most ? new CapabilitySet(Bits | 1UL << index) : this;

    public CapabilitySet With(CapabilitySet other) => new(Bits | other.Bits);

    /// <summary>The indices in the set, lowest first, without allocating.</summary>
    public Enumerator GetEnumerator() => new(Bits);

    public struct Enumerator(ulong bits)
    {
        private ulong _left = bits;

        public int Current { get; private set; }

        public bool MoveNext()
        {
            if (_left == 0)
            {
                return false;
            }
            Current = BitOperations.TrailingZeroCount(_left);
            _left &= _left - 1;
            return true;
        }
    }
}

/// <summary>Who is looking (GROUND_SYSTEMS.md §3.3): their ID, their team, the key numbers they carry.</summary>
public readonly struct Viewer(EntityId id, Team? team, IReadOnlyCollection<int>? keys)
{
    public EntityId Id { get; } = id;

    public Team? Team { get; } = team;

    /// <summary>The numbers of the keys they carry (for key locks).</summary>
    public IReadOnlyCollection<int>? Keys { get; } = keys;

    /// <summary>Whether a player (by ID) is another member of the viewer's team.</summary>
    public bool IsTeammate(ulong player) => player != 0 && player != Id.Value && Team?.Has(new EntityId(player)) == true;
}

/// <summary>
/// A kind of behaviour a world thing may have (GROUND_SYSTEMS.md §2.1): holds items, burns, opens, takes a lock,
/// authorises builders, has an owner, goes after a while. Declared in the data (capabilities.json lists them; the things'
/// definitions say which they have), and handled by one handler each, found in a <see cref="Registry{T}"/>: a new kind of
/// behaviour is one handler and one line in the list. The interaction package adds its options and verbs here.
/// </summary>
public interface ICapability
{
    /// <summary>Its key in capabilities.json.</summary>
    string Key { get; }

    /// <summary>What it tells one viewer about a thing (the thing is its family's object, <see cref="ITargetFamily.Resolve"/>).</summary>
    ViewerFlags Flags(object thing, in Viewer viewer);
}

/// <summary>
/// A capability of things that rest on or stick in others (GROUND_SYSTEMS.md §3.6): what becomes of one when what holds
/// it goes (an explosive falls; the interaction and placement packages add a spent arrow and a deployable on a floor).
/// </summary>
public interface IDependentCapability
{
    void SupportLost(Zone zone, in TargetRef dependent, object thing);
}
