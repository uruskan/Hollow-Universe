using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Net.Replication;

/// <summary>
/// Which entities each client hears about (ARCHITECTURE.md §11.3): a uniform grid over the zone's tangent plane,
/// rebuilt once per snapshot tick, queried for the entities within a radius of a point. Per client, an entity becomes
/// relevant inside <see cref="Radius"/> and stops being relevant beyond <see cref="Radius"/> + <see cref="Hysteresis"/>,
/// so one walking along the edge doesn't spawn and despawn over and over.
/// </summary>
public sealed class RelevanceGrid
{
    private readonly Dictionary<(int X, int Z), List<int>> _cells = new();
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
        (int X, int Z) key = CellOf(position);
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
    /// indices into the order of <see cref="Add"/>.
    /// </summary>
    public void Query(Vector3 centre, HashSet<uint> wasRelevant, List<int> result)
    {
        result.Clear();
        float outer = Radius + Hysteresis;
        int reach = (int)MathF.Ceiling(outer / CellSize);
        (int cx, int cz) = CellOf(centre);
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                if (!_cells.TryGetValue((x, z), out List<int>? cell))
                {
                    continue;
                }
                foreach (int index in cell)
                {
                    (uint netId, Vector3 position) = _entities[index];
                    float dx = position.X - centre.X, dz = position.Z - centre.Z;
                    float distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared <= Radius * Radius || (distanceSquared <= outer * outer && wasRelevant.Contains(netId)))
                    {
                        result.Add(index);
                    }
                }
            }
        }
        result.Sort();
    }

    public (uint NetId, Vector3 Position) this[int index] => _entities[index];

    private (int X, int Z) CellOf(Vector3 p) => ((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Z / CellSize));
}
