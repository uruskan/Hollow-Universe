using System;
using System.Collections.Generic;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;

namespace Hollow.Net.Replication;

/// <summary>An entity offered to a snapshot: its state now and how much it matters to this client.</summary>
public struct SnapshotCandidate
{
    public uint NetId;
    public CharacterNetState State;

    /// <summary>Added to the entity's priority each snapshot it waits (nearer and moving entities weigh more).</summary>
    public float Weight;
}

/// <summary>
/// The server's half of snapshot replication for one client (GROUND.md §4.4, ARCHITECTURE.md §11.3).
/// <para>
/// <b>Delta compression with per-entity baselines.</b> The client acknowledges every snapshot it decodes (the newest
/// and a bit for each of the 32 before it). Each entity is coded against its state in the newest snapshot the client
/// confirmed holding it, at most <see cref="MaxBaselineAge"/> snapshots back; otherwise in full. Nothing depends on a
/// snapshot that may have been lost, so loss costs bandwidth, never correctness.
/// </para>
/// <para>
/// <b>Priority within a budget.</b> Entities accumulate priority while they wait; each snapshot takes the highest
/// first until the byte budget is used, so in a crowd near and moving things update often and the rest still update.
/// An entity left out of a snapshot is simply not updated in it (spawn and despawn are reliable messages).
/// </para>
/// </summary>
public sealed class SnapshotSender
{
    public const int History = 64;

    /// <summary>The client keeps <see cref="History"/> snapshots; a baseline must be younger than that.</summary>
    public const int MaxBaselineAge = History - 1;

    private const int AgeBits = 6;

    // Header and own state, with room for the varints at their largest.
    private const int HeaderBits = 8 + 32 + 32 + 8 + 32 + 6 * 32 + 1 + 8 + 4 + 32 + 40;

    private readonly Sent[] _sent = new Sent[History];
    private readonly Dictionary<uint, (uint Sequence, CharacterNetState State)> _baselines = new();
    private readonly Dictionary<uint, float> _priority = new();
    private readonly List<(float Priority, uint NetId, int Index)> _order = new();
    private readonly List<(uint NetId, int Index)> _chosen = new();

    public SnapshotSender()
    {
        for (int i = 0; i < History; i++)
        {
            _sent[i] = new Sent();
        }
    }

    /// <summary>The sequence number of the last snapshot written (0 before the first).</summary>
    public uint LastSequence { get; private set; }

    /// <summary>Entities written in the last snapshot, and how many were offered.</summary>
    public int LastWritten { get; private set; }

    public int LastOffered { get; private set; }

    /// <summary>The client confirmed these snapshots (from its input packets). Untrusted: anything odd is ignored.</summary>
    public void Acknowledge(uint newest, uint bits)
    {
        if (newest == 0 || newest > LastSequence)
        {
            return;
        }
        Confirm(newest);
        for (int i = 0; i < 32; i++)
        {
            if ((bits & (1u << i)) != 0 && newest > (uint)(i + 1))
            {
                Confirm(newest - (uint)(i + 1));
            }
        }
    }

    /// <summary>An entity stopped being relevant to this client: its next state goes in full.</summary>
    public void Forget(uint netId)
    {
        _baselines.Remove(netId);
        _priority.Remove(netId);
    }

    /// <summary>
    /// Writes one snapshot packet: the type byte, the header, then as many of <paramref name="candidates"/> as fit in
    /// <paramref name="byteBudget"/>, highest priority first, in ascending net ID.
    /// </summary>
    public void Write(BitWriter w, SnapshotHeader header, ReadOnlySpan<SnapshotCandidate> candidates, int byteBudget)
    {
        uint sequence = ++LastSequence;
        LastOffered = candidates.Length;

        // Choose: accumulate priority, then take from the top while the (slightly pessimistic) size fits.
        _order.Clear();
        for (int i = 0; i < candidates.Length; i++)
        {
            ref readonly SnapshotCandidate c = ref candidates[i];
            float priority = _priority.GetValueOrDefault(c.NetId) + c.Weight;
            _priority[c.NetId] = priority;
            _order.Add((priority, c.NetId, i));
        }
        _order.Sort(static (a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.NetId.CompareTo(b.NetId));
        int budget = byteBudget * 8 - HeaderBits;
        _chosen.Clear();
        foreach ((float _, uint netId, int index) in _order)
        {
            ref readonly SnapshotCandidate c = ref candidates[index];
            int bits = 16 + AgeBits + (Baseline(c.NetId, sequence, out CharacterNetState b) ? c.State.DeltaBits(b) : CharacterNetState.FullBits);
            if (bits > budget)
            {
                break;
            }
            budget -= bits;
            _chosen.Add((netId, index));
        }
        _chosen.Sort(static (a, b) => a.NetId.CompareTo(b.NetId));

        // Write.
        header.Sequence = sequence;
        header.EntityCount = (uint)_chosen.Count;
        w.WriteBits(Protocol.Protocol.SnapshotTypeId, 8);
        header.Write(w);
        Sent sent = _sent[sequence % History];
        sent.Reset(sequence, _chosen.Count);
        uint previous = 0;
        foreach ((uint _, int index) in _chosen)
        {
            ref readonly SnapshotCandidate c = ref candidates[index];
            w.WriteDelta((int)(c.NetId - previous));
            previous = c.NetId;
            if (Baseline(c.NetId, sequence, out CharacterNetState baseline, out uint age))
            {
                w.WriteBits(age, AgeBits);
                c.State.WriteDelta(w, baseline);
            }
            else
            {
                w.WriteBits(0, AgeBits);
                c.State.Write(w);
            }
            sent.Add(c.NetId, c.State);
            _priority[c.NetId] = 0;
        }
        LastWritten = _chosen.Count;
    }

    private bool Baseline(uint netId, uint sequence, out CharacterNetState state) => Baseline(netId, sequence, out state, out _);

    private bool Baseline(uint netId, uint sequence, out CharacterNetState state, out uint age)
    {
        if (_baselines.TryGetValue(netId, out (uint Sequence, CharacterNetState State) b) && sequence - b.Sequence <= MaxBaselineAge)
        {
            state = b.State;
            age = sequence - b.Sequence;
            return true;
        }
        state = default;
        age = 0;
        return false;
    }

    private void Confirm(uint sequence)
    {
        Sent sent = _sent[sequence % History];
        if (sent.Sequence != sequence || sent.Confirmed)
        {
            return;
        }
        sent.Confirmed = true;
        for (int i = 0; i < sent.Count; i++)
        {
            uint netId = sent.NetIds[i];
            if (!_baselines.TryGetValue(netId, out (uint Sequence, CharacterNetState State) b) || b.Sequence < sequence)
            {
                _baselines[netId] = (sequence, sent.States[i]);
            }
        }
    }

    private sealed class Sent
    {
        public uint Sequence;
        public bool Confirmed;
        public int Count;
        public uint[] NetIds = new uint[16];
        public CharacterNetState[] States = new CharacterNetState[16];

        public void Reset(uint sequence, int capacity)
        {
            Sequence = sequence;
            Confirmed = false;
            Count = 0;
            if (NetIds.Length < capacity)
            {
                NetIds = new uint[Math.Max(capacity, NetIds.Length * 2)];
                States = new CharacterNetState[NetIds.Length];
            }
        }

        public void Add(uint netId, in CharacterNetState state)
        {
            NetIds[Count] = netId;
            States[Count] = state;
            Count++;
        }
    }
}
