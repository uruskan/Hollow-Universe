using System;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;

namespace Hollow.Net.Replication;

/// <summary>An entity offered to a snapshot: its state now and how much it matters to this client.</summary>
public struct SnapshotCandidate : IDeltaCandidate<CharacterNetState>
{
    public uint NetId;
    public CharacterNetState State;

    /// <summary>Added to the entity's priority each snapshot it waits (nearer and moving entities weigh more).</summary>
    public float Weight;

    readonly uint IDeltaCandidate<CharacterNetState>.Id => NetId;

    readonly CharacterNetState IDeltaCandidate<CharacterNetState>.Value => State;

    readonly float IDeltaCandidate<CharacterNetState>.Weight => Weight;
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
/// <para>
/// <b>Two sections</b> (G6): characters (the header's count), then ships (a count of their own), each kind with its own
/// baselines and priorities under the same sequence numbers. Ships choose first, within <see cref="ShipShare"/> of the
/// budget: they are few, big and fast.
/// </para>
/// </summary>
public sealed class SnapshotSender
{
    public const int History = 64;

    /// <summary>The client keeps <see cref="History"/> snapshots; a baseline must be younger than that.</summary>
    public const int MaxBaselineAge = History - 1;

    /// <summary>Ships may take at most this share of a snapshot's budget.</summary>
    public const float ShipShare = 0.35f;

    // Measure the generated header itself: hand-maintained constants silently undercount when own-state fields grow.
    private readonly BitWriter _headerSizer = new(192);

    private readonly DeltaChannel<CharacterNetState> _characters = new();
    private readonly DeltaChannel<ShipNetState> _ships = new();

    /// <summary>The sequence number of the last snapshot written (0 before the first).</summary>
    public uint LastSequence { get; private set; }

    /// <summary>Entities written in the last snapshot, and how many were offered.</summary>
    public int LastWritten { get; private set; }

    public int LastOffered { get; private set; }

    /// <summary>Ships written in the last snapshot.</summary>
    public int LastShips { get; private set; }

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

    // A delta with nothing changed: a bit a field.
    private static readonly int UnchangedBits = default(CharacterNetState).DeltaBits(default);

    /// <summary>
    /// Whether the client has confirmed exactly this state of an entity, so a snapshot need not carry it (G7: animals
    /// standing still are left out until they move).
    /// </summary>
    public bool Holds(uint netId, in CharacterNetState state) =>
        _characters.TryGetBaseline(netId, out CharacterNetState b) && state.DeltaBits(b) <= UnchangedBits;

    /// <summary>An entity stopped being relevant to this client: its next state goes in full.</summary>
    public void Forget(uint netId)
    {
        _characters.Forget(netId);
        _ships.Forget(netId);
    }

    /// <summary>
    /// Writes one snapshot packet: the type byte, the header, then as many of <paramref name="candidates"/> as fit in
    /// <paramref name="byteBudget"/>, highest priority first, in ascending net ID; and an empty ship section.
    /// </summary>
    public void Write(BitWriter w, SnapshotHeader header, ReadOnlySpan<SnapshotCandidate> candidates, int byteBudget) =>
        Write(w, header, candidates, ReadOnlySpan<ShipCandidate>.Empty, byteBudget);

    /// <summary>Writes one snapshot packet with ships (G6): the header, the characters that fit, the ships that fit.</summary>
    public void Write(BitWriter w, SnapshotHeader header, ReadOnlySpan<SnapshotCandidate> candidates, ReadOnlySpan<ShipCandidate> ships,
        int byteBudget)
    {
        uint sequence = ++LastSequence;
        LastOffered = candidates.Length;
        header.Sequence = sequence;
        // Offered counts bound the encoded widths of the final chosen counts (normally only one byte).
        header.EntityCount = (uint)candidates.Length;
        _headerSizer.Reset();
        _headerSizer.WriteBits(Protocol.Protocol.SnapshotTypeId, 8);
        header.Write(_headerSizer);
        _headerSizer.WriteVarUInt((uint)ships.Length);
        int budget = byteBudget * 8 - _headerSizer.BitCount;
        if (budget < 0) throw new ArgumentOutOfRangeException(nameof(byteBudget), "Snapshot header exceeds the packet budget.");
        int shipBits = _ships.Choose(ships, sequence, (int)(Math.Max(0, budget) * ShipShare));
        _characters.Choose(candidates, sequence, budget - shipBits);

        header.Sequence = sequence;
        header.EntityCount = (uint)_characters.Chosen;
        w.WriteBits(Protocol.Protocol.SnapshotTypeId, 8);
        header.Write(w);
        _characters.Write(w, candidates, sequence);
        w.WriteVarUInt((uint)_ships.Chosen);
        _ships.Write(w, ships, sequence);
        LastWritten = _characters.Chosen;
        LastShips = _ships.Chosen;
    }

    private void Confirm(uint sequence)
    {
        _characters.Confirm(sequence);
        _ships.Confirm(sequence);
    }
}
