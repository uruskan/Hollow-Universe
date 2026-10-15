using System.Collections.Concurrent;
using Hollow.Surface;

namespace Hollow.Client.Streaming;

/// <summary>Deduplicates scatter jobs by both their complete cube-sphere address and requested contents.</summary>
public sealed class ScatterRequestSet
{
    // TileKey.Packed uses all 64 bits: its high three bits name the face. A mode bit must not be overlaid on it.
    private readonly ConcurrentDictionary<(ulong Tile, bool Full), byte> _requests = new();

    public bool TryAdd(TileKey tile, bool full) => _requests.TryAdd((tile.Packed, full), 0);
}
