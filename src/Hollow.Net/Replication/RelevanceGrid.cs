using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Net.Replication;

/// <summary>
/// Which entities each client hears about (ARCHITECTURE.md §11.3): a uniform grid of cubes in the zone's frame (the
/// world's own, docs/WHOLE_WORLD.md §3.3), rebuilt once per snapshot tick, queried for the entities within a straight-line
/// radius of a point. Per client, an entity becomes relevant inside <see cref="Radius"/> and stops being relevant beyond
/// <see cref="Radius"/> + <see cref="Hysteresis"/>, so one walking along the edge doesn't spawn and despawn over and over.
/// </summary>
public sealed class RelevanceGrid
{
    private readonly Dictionary<(int X, int Y, int Z), List<int>> _cells = new();
    private readonly Stack<List<int>> _spare = new();
    private readonly List<(uint NetId, Vector3 Position)> _entities = new();

    public RelevanceGrid(float radius = 300f, float cellSize = 64f, float hysteresis = 20f)
    {
        Radius = radius;
        CellSize = cellSize;
        Hysteresis = hysteresis;
    }

    public float Radius { get; }

    public float CellSize { get; }

    public float Hysteresis { get; }

    /// <summary>The entities placed since the last <see cref="Clear"/>.</summary>
    public int Count => _entities.Count;

    public void Clear()
    {
        foreach (List<int> cell in _cells.Values)
        {
            cell.Clear();
            _spare.Push(cell);
        }
        _cells.Clear();
        _entities.Clear();
    }

    public void Add(uint netId, Vector3 position)
    {
        int index = _entities.Count;
        _entities.Add((netId, position));
        (int, int, int) key = CellOf(position);
        if (!_cells.TryGetValue(key, out List<int>? cell))
        {
            cell = _spare.Count > 0 ? _spare.Pop() : new List<int>();
            _cells.Add(key, cell);
        }
        cell.Add(index);
    }

    /// <summary>
    /// The entities relevant to a viewer at <paramref name="centre"/>: within <see cref="Radius"/>, or within the
    /// hysteresis band if they are in <paramref name="wasRelevant"/> already. Fills <paramref name="result"/> with
    /// indices into the order of <see cref="Add"/>. Looks through the cubes within reach, or through every occupied
    /// cube when there are fewer of those.
    /// </summary>
    public void Query(Vector3 centre, HashSet<uint> wasRelevant, List<int> result)
    {
        result.Clear();
        float outer = Radius + Hysteresis;
        int reach = (int)MathF.Ceiling(outer / CellSize);
        (int cx, int cy, int cz) = CellOf(centre);
        int span = 2 * reach + 1;
        if ((long)span * span * span <= _cells.Count)
        {
            for (int z = cz - reach; z <= cz + reach; z++)
            {
                for (int y = cy - reach; y <= cy + reach; y++)
                {
                    for (int x = cx - reach; x <= cx + reach; x++)
                    {
                        if (_cells.TryGetValue((x, y, z), out List<int>? cell))
                        {
                            Take(cell, centre, outer, wasRelevant, result);
                        }
                    }
                }
            }
        }
        else
        {
            foreach (((int X, int Y, int Z) key, List<int> cell) in _cells)
            {
                if (Math.Abs(key.X - cx) <= reach && Math.Abs(key.Y - cy) <= reach && Math.Abs(key.Z - cz) <= reach)
                {
                    Take(cell, centre, outer, wasRelevant, result);
                }
            }
        }
        result.Sort();
    }

    private void Take(List<int> cell, Vector3 centre, float outer, HashSet<uint> wasRelevant, List<int> result)
    {
        foreach (int index in cell)
        {
            (uint netId, Vector3 position) = _entities[index];
            float distanceSquared = Vector3.DistanceSquared(position, centre);
            if (distanceSquared <= Radius * Radius || (distanceSquared <= outer * outer && wasRelevant.Contains(netId)))
            {
                result.Add(index);
            }
        }
    }

    public (uint NetId, Vector3 Position) this[int index] => _entities[index];

    private (int X, int Y, int Z) CellOf(Vector3 p) =>
        ((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Y / CellSize), (int)MathF.Floor(p.Z / CellSize));
}
