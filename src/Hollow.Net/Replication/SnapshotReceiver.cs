using System.Collections.Generic;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;

namespace Hollow.Net.Replication;

/// <summary>
/// The client's half of snapshot replication (<see cref="SnapshotSender"/>): decodes snapshots against the ones it
/// kept, and builds the acknowledgement it sends back with every input packet. A snapshot that can't be decoded
/// completely (its characters or its ships, G6) is dropped and not acknowledged, so the server never builds on it.
/// </summary>
public sealed class SnapshotReceiver
{
    public const int History = SnapshotSender.History;

    /// <summary>More entities than this in one snapshot is nonsense.</summary>
    public const int MaxEntities = 4096;

    /// <summary>A gap of more snapshots than this (over 8 minutes) is nonsense too.</summary>
    public const uint MaxSequenceJump = 10_000;

    private readonly DeltaKeeper<CharacterNetState> _characters = new();
    private readonly DeltaKeeper<ShipNetState> _ships = new();
    private readonly List<(uint NetId, CharacterNetState State)> _decoded = new();
    private readonly List<(uint NetId, ShipNetState State)> _decodedShips = new();
    private readonly List<(uint NetId, ShipNetState State)> _discardShips = new();

    /// <summary>The newest snapshot decoded (0 before the first).</summary>
    public uint LatestSequence { get; private set; }

    /// <summary>Bit i: snapshot <see cref="LatestSequence"/> − 1 − i was decoded too.</summary>
    public uint AckBits { get; private set; }

    /// <summary>Snapshots dropped because they were stale, or couldn't be decoded.</summary>
    public int Stale { get; private set; }

    public int Undecodable { get; private set; }

    /// <summary>
    /// Decodes a snapshot whose type byte was already read. On success fills <paramref name="entities"/> (ascending net
    /// ID) and returns true; on failure returns false and changes nothing. Its ships are read and dropped.
    /// </summary>
    public bool TryRead(BitReader r, out SnapshotHeader header, List<(uint NetId, CharacterNetState State)> entities) =>
        TryRead(r, out header, entities, _discardShips);

    /// <summary>Decodes a snapshot with its ships (G6), all or nothing.</summary>
    public bool TryRead(BitReader r, out SnapshotHeader header, List<(uint NetId, CharacterNetState State)> entities,
        List<(uint NetId, ShipNetState State)> ships)
    {
        entities.Clear();
        ships.Clear();
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
        if (!_characters.Read(r, header.Sequence, header.EntityCount, _decoded))
        {
            Undecodable++;
            return false;
        }
        uint shipCount = r.ReadVarUInt();
        if (r.Failed || shipCount > MaxEntities || !_ships.Read(r, header.Sequence, shipCount, _decodedShips))
        {
            Undecodable++;
            return false;
        }

        // Keep it, for later snapshots to build on, and acknowledge it.
        _characters.Keep(header.Sequence, _decoded);
        _ships.Keep(header.Sequence, _decodedShips);
        entities.AddRange(_decoded);
        ships.AddRange(_decodedShips);
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
