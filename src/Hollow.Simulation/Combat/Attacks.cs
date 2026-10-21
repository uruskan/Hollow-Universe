using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Combat;

/// <summary>Attack protocol domains, not item kinds. IDs are scoped to one connected actor.</summary>
public enum AttackDomain : byte { None, Melee, Firearm, Station }

/// <summary>A monotonic attempt number; never an ammunition count or a count of successful shots.</summary>
public readonly record struct AttackId(AttackDomain Kind, uint Sequence, uint Session = 0)
{
    public bool IsNone => Kind == AttackDomain.None || Sequence == 0;
}

/// <summary>Untrusted input. Authority chooses results, contact, ammunition and launch state.</summary>
public readonly record struct AttackRequest(AttackId Id, uint Tick, byte Slot = 255, ushort Item = 0,
    Vector3 View = default, ulong Frame = 0, byte ViewDelay = 0, EntityId Source = default,
    int Station = 0, bool Cancel = false)
{
    public OutcomeCode Refused { get; init; }
}

/// <summary>One attempt's authoritative decision; state sequence is the resource revision that incorporates it.</summary>
public readonly record struct AttackResolution(AttackId Id, OutcomeCode Code, uint RequestedTick,
    uint StartTick = 0, uint ContactTick = 0, uint ReadyTick = 0, uint StateSequence = 0, int Barrel = -1)
{
    public bool Cancelled { get; init; }
}

/// <summary>
/// Resource-consuming attack streams cannot pass a missing earlier request merely because transport is unordered.
/// In-order input has no added delay. A gap waits only inside the handler's existing late-request window.
/// </summary>
public static class AttackOrder
{
    public static bool AwaitingEarlier(uint sequence, ulong nextSequence, long requestedTick, long now, int lateTicks) =>
        sequence > nextSequence && requestedTick >= now - lateTicks;
}

/// <summary>
/// Bounded authority decision history shared by attack handlers. Duplicate reliable requests replay their
/// original answer. Evicted identities cannot become fresh attacks; reordering inside the window is allowed.
/// </summary>
public sealed class AttackJournal
{
    public const int Capacity = 256;
    private readonly Dictionary<AttackId, AttackResolution> _results = new();
    private readonly Queue<AttackId> _order = new();
    private readonly uint[] _floor = new uint[4];
    public int Count => _results.Count;
    public bool TryGet(AttackId id, out AttackResolution resolution) => _results.TryGetValue(id, out resolution);
    public bool IsExpired(AttackId id) => id.IsNone || (int)id.Kind >= _floor.Length || id.Sequence <= _floor[(int)id.Kind];
    public void Remember(in AttackResolution resolution)
    {
        if (resolution.Id.IsNone || (int)resolution.Id.Kind >= _floor.Length) return;
        if (!_results.ContainsKey(resolution.Id)) _order.Enqueue(resolution.Id);
        _results[resolution.Id] = resolution;
        while (_order.Count > Capacity)
        {
            AttackId old = _order.Dequeue();
            _results.Remove(old);
            _floor[(int)old.Kind] = Math.Max(_floor[(int)old.Kind], old.Sequence);
        }
    }
    public void Clear() { _results.Clear(); _order.Clear(); Array.Clear(_floor); }
}
