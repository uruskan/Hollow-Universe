using System;
using System.Collections.Generic;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;

namespace Hollow.Net.Replication;

/// <summary>An entity state the generator writes with deltas (<c>[NetState]</c>): what a snapshot section needs of it.</summary>
public interface INetState<T> where T : struct, INetState<T>
{
    void Write(BitWriter w);

    void WriteDelta(BitWriter w, in T baseline);

    int DeltaBits(in T baseline);

    static abstract T Read(BitReader r);

    static abstract T ReadDelta(BitReader r, in T baseline);

    /// <summary>The bits of a state written in full.</summary>
    static abstract int FullSize { get; }
}

/// <summary>An entity offered to a snapshot section: its net ID, its state now, and how much it matters to this client.</summary>
public interface IDeltaCandidate<T> where T : struct
{
    uint Id { get; }

    T Value { get; }

    float Weight { get; }
}

/// <summary>A ship offered to a snapshot (G6).</summary>
public struct ShipCandidate : IDeltaCandidate<ShipNetState>
{
    public uint NetId;
    public ShipNetState State;

    /// <summary>Added to the ship's priority each snapshot it waits.</summary>
    public float Weight;

    readonly uint IDeltaCandidate<ShipNetState>.Id => NetId;

    readonly ShipNetState IDeltaCandidate<ShipNetState>.Value => State;

    readonly float IDeltaCandidate<ShipNetState>.Weight => Weight;
}

/// <summary>
/// The server's half of one kind of entity in snapshots (<see cref="SnapshotSender"/>): per-entity baselines confirmed by
/// the client, accumulated priorities, and what each snapshot carried. Characters and ships each have one; they share
/// the snapshots' sequence numbers and acknowledgements.
/// </summary>
internal sealed class DeltaChannel<T> where T : struct, INetState<T>
{
    public const int AgeBits = 6;

    private readonly Sent[] _sent = new Sent[SnapshotSender.History];
    private readonly Dictionary<uint, (uint Sequence, T State)> _baselines = new();
    private readonly Dictionary<uint, float> _priority = new();
    private readonly List<(float Priority, uint NetId, int Index)> _order = new();
    private readonly List<(uint NetId, int Index)> _chosen = new();

    public DeltaChannel()
    {
        for (int i = 0; i < _sent.Length; i++)
        {
            _sent[i] = new Sent();
        }
    }

    /// <summary>Entities chosen for the snapshot being written.</summary>
    public int Chosen => _chosen.Count;

    /// <summary>The state of an entity the client has confirmed (G7: animals at rest are left out while they hold it).</summary>
    public bool TryGetBaseline(uint netId, out T state)
    {
        bool found = _baselines.TryGetValue(netId, out (uint Sequence, T State) b);
        state = b.State;
        return found;
    }

    public void Forget(uint netId)
    {
        _baselines.Remove(netId);
        _priority.Remove(netId);
    }

    /// <summary>
    /// Chooses what fits in <paramref name="budget"/> bits, highest accumulated priority first, then in net ID order;
    /// returns the bits it takes (the count's varint not included).
    /// </summary>
    public int Choose<TC>(ReadOnlySpan<TC> candidates, uint sequence, int budget) where TC : struct, IDeltaCandidate<T>
    {
        _order.Clear();
        for (int i = 0; i < candidates.Length; i++)
        {
            ref readonly TC c = ref candidates[i];
            float priority = _priority.GetValueOrDefault(c.Id) + c.Weight;
            _priority[c.Id] = priority;
            _order.Add((priority, c.Id, i));
        }
        _order.Sort(static (a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.NetId.CompareTo(b.NetId));
        int left = budget;
        _chosen.Clear();
        foreach ((float _, uint netId, int index) in _order)
        {
            int bits = 16 + EntityBits(candidates[index], sequence);
            if (bits > left)
            {
                break;
            }
            left -= bits;
            _chosen.Add((netId, index));
        }
        // In net ID order the gaps are known exactly. Each was guessed at 16 bits; should the real ones not fit (net
        // IDs far apart), drop the least important until they do: a snapshot must never outgrow the packet.
        _chosen.Sort(static (a, b) => a.NetId.CompareTo(b.NetId));
        int exact;
        while (_chosen.Count > 0 && (exact = ExactBits(candidates, sequence)) > budget)
        {
            int last = _chosen.FindIndex(c => c.Index == _order[_chosen.Count - 1].Index);
            _chosen.RemoveAt(last >= 0 ? last : _chosen.Count - 1);
        }
        return _chosen.Count == 0 ? 0 : ExactBits(candidates, sequence);
    }

    /// <summary>Writes the chosen entities (gap, baseline age, state) and remembers them under the sequence.</summary>
    public void Write<TC>(BitWriter w, ReadOnlySpan<TC> candidates, uint sequence) where TC : struct, IDeltaCandidate<T>
    {
        Sent sent = _sent[sequence % SnapshotSender.History];
        sent.Reset(sequence, _chosen.Count);
        uint previous = 0;
        foreach ((uint netId, int index) in _chosen)
        {
            ref readonly TC c = ref candidates[index];
            w.WriteDelta((int)(netId - previous));
            previous = netId;
            T state = c.Value;
            if (Baseline(netId, sequence, out T baseline, out uint age))
            {
                w.WriteBits(age, AgeBits);
                state.WriteDelta(w, baseline);
            }
            else
            {
                w.WriteBits(0, AgeBits);
                state.Write(w);
            }
            sent.Add(netId, state);
            _priority[netId] = 0;
        }
    }

    public void Confirm(uint sequence)
    {
        Sent sent = _sent[sequence % SnapshotSender.History];
        if (sent.Sequence != sequence || sent.Confirmed)
        {
            return;
        }
        sent.Confirmed = true;
        for (int i = 0; i < sent.Count; i++)
        {
            uint netId = sent.NetIds[i];
            if (!_baselines.TryGetValue(netId, out (uint Sequence, T State) b) || b.Sequence < sequence)
            {
                _baselines[netId] = (sequence, sent.States[i]);
            }
        }
    }

    private int EntityBits<TC>(in TC c, uint sequence) where TC : struct, IDeltaCandidate<T> =>
        AgeBits + (Baseline(c.Id, sequence, out T b, out _) ? c.Value.DeltaBits(b) : T.FullSize);

    private int ExactBits<TC>(ReadOnlySpan<TC> candidates, uint sequence) where TC : struct, IDeltaCandidate<T>
    {
        int bits = 0;
        uint previous = 0;
        foreach ((uint netId, int index) in _chosen)
        {
            bits += BitWriter.DeltaBits((int)(netId - previous)) + EntityBits(candidates[index], sequence);
            previous = netId;
        }
        return bits;
    }

    private bool Baseline(uint netId, uint sequence, out T state, out uint age)
    {
        if (_baselines.TryGetValue(netId, out (uint Sequence, T State) b) && sequence - b.Sequence <= SnapshotSender.MaxBaselineAge)
        {
            state = b.State;
            age = sequence - b.Sequence;
            return true;
        }
        state = default;
        age = 0;
        return false;
    }

    private sealed class Sent
    {
        public uint Sequence;
        public bool Confirmed;
        public int Count;
        public uint[] NetIds = new uint[16];
        public T[] States = new T[16];

        public void Reset(uint sequence, int capacity)
        {
            Sequence = sequence;
            Confirmed = false;
            Count = 0;
            if (NetIds.Length < capacity)
            {
                NetIds = new uint[Math.Max(capacity, NetIds.Length * 2)];
                States = new T[NetIds.Length];
            }
        }

        public void Add(uint netId, in T state)
        {
            NetIds[Count] = netId;
            States[Count] = state;
            Count++;
        }
    }
}

/// <summary>The client's half of one kind of entity in snapshots: the states of each kept snapshot, to decode deltas against.</summary>
internal sealed class DeltaKeeper<T> where T : struct, INetState<T>
{
    private readonly Dictionary<uint, T>[] _kept = new Dictionary<uint, T>[SnapshotReceiver.History];
    private readonly uint[] _keptSequence = new uint[SnapshotReceiver.History];

    public DeltaKeeper()
    {
        for (int i = 0; i < _kept.Length; i++)
        {
            _kept[i] = new Dictionary<uint, T>();
        }
    }

    /// <summary>Reads <paramref name="count"/> entities of a snapshot into <paramref name="into"/>; false (and nothing kept) when they don't decode.</summary>
    public bool Read(BitReader r, uint sequence, uint count, List<(uint NetId, T State)> into)
    {
        into.Clear();
        uint netId = 0;
        for (int i = 0; i < count; i++)
        {
            int gap = r.ReadDelta();
            uint age = r.ReadBits(DeltaChannel<T>.AgeBits);
            if (gap <= 0 || r.Failed)
            {
                return false;
            }
            netId += (uint)gap;
            T state;
            if (age == 0)
            {
                state = T.Read(r);
            }
            else
            {
                uint baseSequence = sequence - age;
                int slot = (int)(baseSequence % SnapshotReceiver.History);
                if (age >= sequence || _keptSequence[slot] != baseSequence || !_kept[slot].TryGetValue(netId, out T baseline))
                {
                    return false;
                }
                state = T.ReadDelta(r, baseline);
            }
            if (r.Failed)
            {
                return false;
            }
            into.Add((netId, state));
        }
        return true;
    }

    /// <summary>Keeps a decoded snapshot's states for later ones to build on.</summary>
    public void Keep(uint sequence, List<(uint NetId, T State)> decoded)
    {
        int keep = (int)(sequence % SnapshotReceiver.History);
        _keptSequence[keep] = sequence;
        Dictionary<uint, T> kept = _kept[keep];
        kept.Clear();
        foreach ((uint id, T state) in decoded)
        {
            kept[id] = state;
        }
    }
}
