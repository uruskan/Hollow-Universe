using System.Collections.Generic;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;

namespace Hollow.Net.Replication;

/// <summary>
/// The client's half of snapshot replication (<see cref="SnapshotSender"/>): decodes snapshots against the ones it
/// kept, and builds the acknowledgement it sends back with every input packet. A snapshot that can't be decoded
/// completely is dropped and not acknowledged, so the server never builds on it.
/// </summary>
public sealed class SnapshotReceiver
{
    public const int History = SnapshotSender.History;

    /// <summary>More entities than this in one snapshot is nonsense.</summary>
    public const int MaxEntities = 4096;

    /// <summary>A gap of more snapshots than this (over 8 minutes) is nonsense too.</summary>
    public const uint MaxSequenceJump = 10_000;

    private readonly Dictionary<uint, CharacterNetState>[] _kept = new Dictionary<uint, CharacterNetState>[History];
    private readonly uint[] _keptSequence = new uint[History];
    private readonly List<(uint NetId, CharacterNetState State)> _decoded = new();

    public SnapshotReceiver()
    {
        for (int i = 0; i < History; i++)
        {
            _kept[i] = new Dictionary<uint, CharacterNetState>();
        }
    }

    /// <summary>The newest snapshot decoded (0 before the first).</summary>
    public uint LatestSequence { get; private set; }

    /// <summary>Bit i: snapshot <see cref="LatestSequence"/> − 1 − i was decoded too.</summary>
    public uint AckBits { get; private set; }

    /// <summary>Snapshots dropped because they were stale, or couldn't be decoded.</summary>
    public int Stale { get; private set; }

    public int Undecodable { get; private set; }

    /// <summary>
    /// Decodes a snapshot whose type byte was already read. On success fills <paramref name="entities"/> (ascending net
    /// ID) and returns true; on failure returns false and changes nothing.
    /// </summary>
    public bool TryRead(BitReader r, out SnapshotHeader header, List<(uint NetId, CharacterNetState State)> entities)
    {
        entities.Clear();
        header = SnapshotHeader.Read(r);
        if (r.Failed || header.Sequence == 0 || header.EntityCount > MaxEntities)
        {
            Undecodable++;
            return false;
        }
        if (LatestSequence != 0 && header.Sequence <= LatestSequence)
        {
            Stale++;
            return false;
        }
        if (LatestSequence != 0 && header.Sequence - LatestSequence > MaxSequenceJump)
        {
            // Snapshots come 20 a second; a jump of minutes is a corrupt packet, which must not end reception.
            Undecodable++;
            return false;
        }
        _decoded.Clear();
        uint netId = 0;
        for (int i = 0; i < header.EntityCount; i++)
        {
            int gap = r.ReadDelta();
            uint age = r.ReadBits(6);
            if (gap <= 0 || r.Failed)
            {
                Undecodable++;
                return false;
            }
            netId += (uint)gap;
            CharacterNetState state;
            if (age == 0)
            {
                state = CharacterNetState.Read(r);
            }
            else
            {
                uint baseSequence = header.Sequence - age;
                int slot = (int)(baseSequence % History);
                if (age >= header.Sequence || _keptSequence[slot] != baseSequence || !_kept[slot].TryGetValue(netId, out CharacterNetState baseline))
                {
                    Undecodable++;
                    return false;
                }
                state = CharacterNetState.ReadDelta(r, baseline);
            }
            if (r.Failed)
            {
                Undecodable++;
                return false;
            }
            _decoded.Add((netId, state));
        }

        // Keep it, for later snapshots to build on, and acknowledge it.
        int keep = (int)(header.Sequence % History);
        _keptSequence[keep] = header.Sequence;
        Dictionary<uint, CharacterNetState> kept = _kept[keep];
        kept.Clear();
        foreach ((uint id, CharacterNetState state) in _decoded)
        {
            kept[id] = state;
            entities.Add((id, state));
        }
        if (LatestSequence == 0)
        {
            AckBits = 0;
        }
        else
        {
            uint shift = header.Sequence - LatestSequence;
            AckBits = shift > 32 ? 0 : shift == 32 ? 1u << 31 : (AckBits << (int)shift) | (1u << (int)(shift - 1));
        }
        LatestSequence = header.Sequence;
        return true;
    }
}
