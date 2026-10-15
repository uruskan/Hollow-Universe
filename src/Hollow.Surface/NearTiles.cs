using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// <see cref="TileKey.Around"/> for searches repeated from about the same place every tick (a character's ground, an
/// animal's map; docs/WHOLE_WORLD.md §6.1). The search samples the tangent plane a few hundred times (about 15 µs for
/// 128 m on the first world), while the thing searching stays within a few metres for many ticks. So a search of a tile's
/// reach or more goes round the middle of the fine cell the point lies in (a tile <see cref="CellLevels"/> levels below
/// the searched one), its reach widened by the cell's own, which holds every tile the point's own search would; and the
/// answer is kept for that cell. Shorter searches (a few tiles) are cheap and made afresh. Deterministic; one thread.
/// </summary>
public sealed class NearTiles
{
    /// <summary>The fine cells are this many levels below the searched tiles (7.4 m for 59 m tiles).</summary>
    public const int CellLevels = 3;

    /// <summary>At most this many cells' answers are kept; then the store starts again.</summary>
    public const int Capacity = 4096;

    private readonly Dictionary<(ulong Cell, float Reach), TileKey[]> _kept = new();
    private readonly List<TileKey> _scratch = new();
    private readonly double _radius;
    private readonly int _level;
    private readonly int _cellLevel;
    private readonly double _cellReach;
    private readonly double _tileEdge;

    /// <param name="radius">The world's radius in metres.</param>
    /// <param name="level">The level of the tiles searched for.</param>
    public NearTiles(double radius, int level)
    {
        _radius = radius;
        _level = level;
        _cellLevel = Math.Min(level + CellLevels, TileKey.MaxLevel);
        // The farthest a point of a cell lies from its middle: under 0.87 of its edge (a rhombus at the cube's corners).
        _cellReach = 0.9 * TileKey.EdgeLength(_cellLevel, radius);
        _tileEdge = TileKey.EdgeLength(level, radius);
    }

    /// <summary>
    /// The tiles within about <paramref name="reach"/> metres of a point (a body-frame position or direction), each once,
    /// in a fixed order: a kept answer (up to the cell's reach more) or a fresh one. Valid until the next call.
    /// </summary>
    public ReadOnlySpan<TileKey> Around(Vector3D point, float reach)
    {
        if (reach < _tileEdge)
        {
            _scratch.Clear();
            TileKey.Around(point, reach, _radius, _level, _scratch);
            return CollectionsMarshal.AsSpan(_scratch);
        }
        TileKey cell = TileKey.At(point, _cellLevel);
        if (!_kept.TryGetValue((cell.Packed, reach), out TileKey[]? tiles))
        {
            if (_kept.Count >= Capacity)
            {
                _kept.Clear();
            }
            _scratch.Clear();
            TileKey.Around(cell.Centre, reach + _cellReach, _radius, _level, _scratch);
            tiles = _scratch.ToArray();
            _kept.Add((cell.Packed, reach), tiles);
        }
        return tiles;
    }
}
