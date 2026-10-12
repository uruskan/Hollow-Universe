using System;
using System.Globalization;

namespace Hollow.Simulation.Entities;

/// <summary>
/// A persistent entity's permanent 64-bit ID (ARCHITECTURE.md §7): 41 bits of milliseconds since 1 January 2026,
/// 9 bits naming the allocator, 14 bits of sequence. Unique across the shard without asking anyone; 0 is "none".
/// </summary>
public readonly record struct EntityId(ulong Value)
{
    public static readonly EntityId None = default;

    public bool IsNone => Value == 0;

    public override string ToString() => "#" + Value.ToString("X16", CultureInfo.InvariantCulture);
}

/// <summary>
/// Hands out <see cref="EntityId"/>s for one allocator number. Time comes from a clock delegate (milliseconds since the
/// Unix epoch) so tests can fix it. IDs only grow: when more than 16,384 are taken in one millisecond, or the clock
/// steps back, the time part runs ahead of the clock until it catches up.
/// </summary>
public sealed class EntityIdAllocator
{
    /// <summary>1 January 2026 00:00 UTC in Unix milliseconds: the time part's zero.</summary>
    public const long EpochUnixMilliseconds = 1_767_225_600_000;

    public const int AllocatorBits = 9;
    public const int SequenceBits = 14;

    private readonly Func<long> _unixMilliseconds;
    private readonly ulong _allocator;
    private long _time = -1;
    private int _sequence;

    public EntityIdAllocator(int allocator, Func<long> unixMilliseconds)
    {
        if (allocator < 0 || allocator >= 1 << AllocatorBits)
        {
            throw new ArgumentOutOfRangeException(nameof(allocator), "0 … 511");
        }
        _allocator = (ulong)allocator;
        _unixMilliseconds = unixMilliseconds;
    }

    public EntityId Next()
    {
        long now = Math.Max(0, _unixMilliseconds() - EpochUnixMilliseconds);
        if (now > _time)
        {
            _time = now;
            _sequence = 0;
        }
        else if (++_sequence >= 1 << SequenceBits)
        {
            _time++;
            _sequence = 0;
        }
        ulong value = ((ulong)_time << (AllocatorBits + SequenceBits)) | (_allocator << SequenceBits) | (uint)_sequence;
        // Only allocator 0 in the epoch's first millisecond could make 0, which means "none".
        return value == 0 ? Next() : new EntityId(value);
    }
}
