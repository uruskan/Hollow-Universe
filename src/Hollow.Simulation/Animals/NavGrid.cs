using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace Hollow.Simulation.Animals;

/// <summary>What a cell of the navigation grid holds.</summary>
[Flags]
public enum NavFlags : byte
{
    None = 0,

    /// <summary>Under the sea (deeper than wading).</summary>
    Water = 1,

    /// <summary>A tree trunk, an outcrop or a boulder stands in it.</summary>
    Blocked = 2,
}

/// <summary>
/// One 64 m square of the navigation grid: the ground's height at the corners of 2 m cells, each cell's slope, and
/// whether it is under water or holds a trunk or a rock. Built once from the surface and the natural objects (both pure
/// functions), on a worker thread, and read on the zone's.
/// </summary>
public sealed class NavArea
{
    public const int Cells = 32;
    public const int Corners = Cells + 1;

    public NavArea(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int X { get; }

    public int Z { get; }

    /// <summary>The ground's local height (zone frame Y) at each corner, row by row from the area's −X −Z corner.</summary>
    public readonly float[] Heights = new float[Corners * Corners];

    /// <summary>Each cell's flags, and its slope in degrees.</summary>
    public readonly NavFlags[] Flags = new NavFlags[Cells * Cells];

    public readonly byte[] Slope = new byte[Cells * Cells];

    /// <summary>The epoch it was last used in (unused areas are dropped).</summary>
    internal long LastUsed;
}

/// <summary>
/// The animals' map of a zone (GROUND.md §6.9): a coarse grid over the zone's tangent plane, 2 m cells in 64 m areas,
/// built lazily around animals that are awake. It tells the steering where the ground is, how steep, where the water
/// starts and where trees and rocks stand, and finds short paths around them (<see cref="FindPath"/>). Building pieces
/// and deployables change, so the steering asks the zone about those directly.
/// </summary>
public sealed class NavGrid
{
    public const float AreaSize = 64f;
    public const float CellSize = AreaSize / NavArea.Cells;

    /// <summary>Wading is fine; deeper than this (metres under the sea) is water.</summary>
    public const float WadeDepth = 0.35f;

    /// <summary>A trunk or rock blocks the cells within its radius and this much more.</summary>
    public const float Clearance = 0.45f;

    private readonly ISurface _surface;
    private readonly ZoneFrame _frame;
    private readonly INaturalObjects? _nature;
    private readonly GameContent? _content;
    private readonly Dictionary<(int X, int Z), NavArea> _areas = new();
    private readonly Dictionary<(int X, int Z), Task<NavArea>> _pending = new();
    private readonly List<(int X, int Z)> _done = new();
    private long _epoch;

    public NavGrid(ISurface surface, ZoneFrame frame, INaturalObjects? nature, GameContent? content)
    {
        _surface = surface;
        _frame = frame;
        _nature = nature;
        _content = content;
    }

    /// <summary>Areas built so far, and areas built on the zone's own thread because a worker hadn't finished.</summary>
    public int Built { get; private set; }

    public int BuiltOnDemand { get; private set; }

    public int Count => _areas.Count;

    /// <summary>Builds areas on the calling thread instead of on workers (tests that want everything at once).</summary>
    public bool Synchronous { get; set; }

    public static (int X, int Z) AreaOf(float x, float z) => ((int)MathF.Floor(x / AreaSize), (int)MathF.Floor(z / AreaSize));

    /// <summary>Starts building, on a worker, the areas within <paramref name="radius"/> metres of a point not built yet.</summary>
    public void Prefetch(Vector3 point, float radius)
    {
        (int x0, int z0) = AreaOf(point.X - radius, point.Z - radius);
        (int x1, int z1) = AreaOf(point.X + radius, point.Z + radius);
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (_areas.TryGetValue((x, z), out NavArea? area))
                {
                    area.LastUsed = _epoch;
                    continue;
                }
                if (_pending.ContainsKey((x, z)))
                {
                    continue;
                }
                if (Synchronous)
                {
                    Ensure(x, z);
                    continue;
                }
                int ax = x, az = z;
                _pending.Add((x, z), Task.Run(() => Build(ax, az)));
            }
        }
    }

    /// <summary>Takes in the areas whose build finished. Once a tick, on the zone's thread.</summary>
    public void Adopt()
    {
        _epoch++;
        if (_pending.Count == 0)
        {
            return;
        }
        _done.Clear();
        foreach (((int, int) key, Task<NavArea> task) in _pending)
        {
            if (task.IsCompleted)
            {
                _done.Add(key);
            }
        }
        foreach ((int X, int Z) key in _done)
        {
            _pending.Remove(key, out Task<NavArea>? task);
            if (task!.IsCompletedSuccessfully)
            {
                NavArea area = task.Result;
                area.LastUsed = _epoch;
                _areas[key] = area;
                Built++;
            }
        }
    }

    /// <summary>Whether the area under a point is built (an animal on unbuilt ground waits for it).</summary>
    public bool IsReady(float x, float z) => _areas.ContainsKey(AreaOf(x, z));

    /// <summary>The area under a point, built now if a worker hasn't (waits for one under way).</summary>
    public NavArea Ensure(float x, float z)
    {
        (int ax, int az) = AreaOf(x, z);
        return Ensure(ax, az);
    }

    private NavArea Ensure(int ax, int az)
    {
        if (_areas.TryGetValue((ax, az), out NavArea? area))
        {
            area.LastUsed = _epoch;
            return area;
        }
        if (_pending.Remove((ax, az), out Task<NavArea>? building))
        {
            area = building.Result;
        }
        else
        {
            area = Build(ax, az);
            BuiltOnDemand++;
        }
        area.LastUsed = _epoch;
        _areas[(ax, az)] = area;
        Built++;
        return area;
    }

    /// <summary>Drops the areas nobody used in the last <paramref name="keepEpochs"/> epochs (ticks).</summary>
    public void Release(long keepEpochs)
    {
        if (_epoch % 30 != 0 || _areas.Count < 64)
        {
            return;
        }
        _done.Clear();
        foreach (((int, int) key, NavArea area) in _areas)
        {
            if (_epoch - area.LastUsed > keepEpochs)
            {
                _done.Add(key);
            }
        }
        foreach ((int, int) key in _done)
        {
            _areas.Remove(key);
        }
    }

    // ---------------------------------------------------------------- queries

    private NavArea AreaAt(float x, float z, out int i, out int j, out float fx, out float fz)
    {
        NavArea area = Ensure(x, z);
        float lx = x - area.X * AreaSize, lz = z - area.Z * AreaSize;
        float cx = Math.Clamp(lx / CellSize, 0, NavArea.Cells - 1e-3f), cz = Math.Clamp(lz / CellSize, 0, NavArea.Cells - 1e-3f);
        i = (int)cx;
        j = (int)cz;
        fx = cx - i;
        fz = cz - j;
        return area;
    }

    /// <summary>The ground's local height under a point (bilinear between the cell's corners).</summary>
    public float Height(float x, float z)
    {
        NavArea a = AreaAt(x, z, out int i, out int j, out float fx, out float fz);
        int k = j * NavArea.Corners + i;
        float h00 = a.Heights[k], h10 = a.Heights[k + 1], h01 = a.Heights[k + NavArea.Corners], h11 = a.Heights[k + NavArea.Corners + 1];
        return (h00 + (h10 - h00) * fx) + ((h01 + (h11 - h01) * fx) - (h00 + (h10 - h00) * fx)) * fz;
    }

    public NavFlags Flags(float x, float z)
    {
        NavArea a = AreaAt(x, z, out int i, out int j, out _, out _);
        return a.Flags[j * NavArea.Cells + i];
    }

    /// <summary>The cell's slope in radians.</summary>
    public float Slope(float x, float z)
    {
        NavArea a = AreaAt(x, z, out int i, out int j, out _, out _);
        return a.Slope[j * NavArea.Cells + i] * (MathF.PI / 180);
    }

    /// <summary>Whether an animal that climbs at most <paramref name="maxSlope"/> may stand at a point.</summary>
    public bool Walkable(float x, float z, float maxSlope)
    {
        NavArea a = AreaAt(x, z, out int i, out int j, out _, out _);
        int c = j * NavArea.Cells + i;
        return a.Flags[c] == NavFlags.None && a.Slope[c] * (MathF.PI / 180) <= maxSlope;
    }

    /// <summary>
    /// Whether the straight walk from <paramref name="from"/> to <paramref name="to"/> crosses only walkable cells
    /// (sampled every metre).
    /// </summary>
    public bool Clear(Vector3 from, Vector3 to, float maxSlope)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        int steps = Math.Max(1, (int)MathF.Ceiling(length / (CellSize * 0.5f)));
        for (int s = 1; s <= steps; s++)
        {
            float t = s / (float)steps;
            if (!Walkable(from.X + dx * t, from.Z + dz * t, maxSlope))
            {
                return false;
            }
        }
        return true;
    }

    // ---------------------------------------------------------------- paths

    private readonly PriorityQueue<int, float> _open = new();
    private readonly Dictionary<int, int> _cameFrom = new();
    private readonly Dictionary<int, float> _cost = new();

    /// <summary>
    /// A short path from <paramref name="from"/> towards <paramref name="to"/> over walkable cells (A* on the grid,
    /// eight neighbours), within a window of <paramref name="window"/> cells around the start and at most
    /// <paramref name="budget"/> cells looked at. Returns the farthest point of the path the animal can walk to in a
    /// straight line (so it cuts corners like an animal, not a grid), or false when nothing nearer the goal was found.
    /// When the goal itself can't be reached it heads for the reachable cell nearest it.
    /// </summary>
    public bool FindPath(Vector3 from, Vector3 to, float maxSlope, out Vector3 next, int window = 24, int budget = 1200)
    {
        next = to;
        int sx = (int)MathF.Floor(from.X / CellSize), sz = (int)MathF.Floor(from.Z / CellSize);
        int gx = Math.Clamp((int)MathF.Floor(to.X / CellSize), sx - window, sx + window);
        int gz = Math.Clamp((int)MathF.Floor(to.Z / CellSize), sz - window, sz + window);
        int span = 2 * window + 1;
        int Index(int x, int z) => (z - sz + window) * span + (x - sx + window);
        int start = Index(sx, sz), goal = Index(gx, gz);
        _open.Clear();
        _cameFrom.Clear();
        _cost.Clear();
        _open.Enqueue(start, 0);
        _cost[start] = 0;
        int best = start;
        float bestH = Heuristic(sx, sz, gx, gz);
        int looked = 0;
        while (_open.Count > 0 && looked < budget)
        {
            int current = _open.Dequeue();
            looked++;
            if (current == goal)
            {
                best = current;
                break;
            }
            int cx = current % span - window + sx, cz = current / span - window + sz;
            float h = Heuristic(cx, cz, gx, gz);
            if (h < bestH)
            {
                bestH = h;
                best = current;
            }
            float here = _cost[current];
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }
                    int nx = cx + dx, nz = cz + dz;
                    if (Math.Abs(nx - sx) > window || Math.Abs(nz - sz) > window)
                    {
                        continue;
                    }
                    if (!Walkable((nx + 0.5f) * CellSize, (nz + 0.5f) * CellSize, maxSlope))
                    {
                        continue;
                    }
                    // No cutting between two blocked diagonal neighbours.
                    if (dx != 0 && dz != 0 && (!Walkable((cx + dx + 0.5f) * CellSize, (cz + 0.5f) * CellSize, maxSlope) ||
                        !Walkable((cx + 0.5f) * CellSize, (cz + dz + 0.5f) * CellSize, maxSlope)))
                    {
                        continue;
                    }
                    int n = Index(nx, nz);
                    float cost = here + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                    if (_cost.TryGetValue(n, out float known) && known <= cost)
                    {
                        continue;
                    }
                    _cost[n] = cost;
                    _cameFrom[n] = current;
                    _open.Enqueue(n, cost + Heuristic(nx, nz, gx, gz));
                }
            }
        }
        if (best == start)
        {
            return false;
        }
        // Back from the best cell to the start; then the farthest cell along the path still in a straight, clear line.
        var path = new List<int>();
        for (int c = best; c != start; c = _cameFrom[c])
        {
            path.Add(c);
        }
        path.Reverse();
        Vector3 target = Centre(path[0]);
        for (int k = 0; k < path.Count && k < 12; k++)
        {
            Vector3 point = Centre(path[k]);
            if (!Clear(from, point, maxSlope))
            {
                break;
            }
            target = point;
        }
        next = best == goal && Clear(from, to, maxSlope) ? to : target;
        return true;

        Vector3 Centre(int index)
        {
            int x = index % span - window + sx, z = index / span - window + sz;
            float px = (x + 0.5f) * CellSize, pz = (z + 0.5f) * CellSize;
            return new Vector3(px, Height(px, pz), pz);
        }
    }

    private static float Heuristic(int x, int z, int gx, int gz)
    {
        int dx = Math.Abs(x - gx), dz = Math.Abs(z - gz);
        return Math.Max(dx, dz) + 0.4142f * Math.Min(dx, dz);
    }

    // ---------------------------------------------------------------- building an area

    private NavArea Build(int ax, int az)
    {
        var area = new NavArea(ax, az);
        float x0 = ax * AreaSize, z0 = az * AreaSize;
        double sea = _surface.SeaRadius;
        var under = new bool[NavArea.Corners * NavArea.Corners];
        for (int j = 0; j < NavArea.Corners; j++)
        {
            for (int i = 0; i < NavArea.Corners; i++)
            {
                double x = x0 + i * CellSize, z = z0 + j * CellSize;
                Vector3D direction = _frame.ToBody(x, 0, z).Normalized;
                double radius = _surface.Radius + _surface.HeightAt(direction);
                Vector3 local = _frame.ToLocal(direction * radius);
                int k = j * NavArea.Corners + i;
                area.Heights[k] = local.Y;
                under[k] = sea > 0 && radius < sea - WadeDepth;
            }
        }
        for (int j = 0; j < NavArea.Cells; j++)
        {
            for (int i = 0; i < NavArea.Cells; i++)
            {
                int k = j * NavArea.Corners + i;
                float h00 = area.Heights[k], h10 = area.Heights[k + 1], h01 = area.Heights[k + NavArea.Corners], h11 = area.Heights[k + NavArea.Corners + 1];
                float gx = ((h10 + h11) - (h00 + h01)) / (2 * CellSize), gz = ((h01 + h11) - (h00 + h10)) / (2 * CellSize);
                double degrees = DMath.Atan(Math.Sqrt(gx * gx + gz * gz)) * (180 / Math.PI);
                int c = j * NavArea.Cells + i;
                area.Slope[c] = (byte)Math.Clamp((int)Math.Round(degrees), 0, 90);
                int wet = (under[k] ? 1 : 0) + (under[k + 1] ? 1 : 0) + (under[k + NavArea.Corners] ? 1 : 0) + (under[k + NavArea.Corners + 1] ? 1 : 0);
                if (wet >= 2)
                {
                    area.Flags[c] |= NavFlags.Water;
                }
            }
        }
        // Trunks and rocks: the zone's natural objects (on the planet, its own scatter's trees and boulders).
        if (_nature != null && _content != null)
        {
            var objects = new List<NaturalObject>();
            _nature.Query(x0 - 2, z0 - 2, x0 + AreaSize + 2, z0 + AreaSize + 2, objects);
            foreach (NaturalObject o in objects)
            {
                if (_content.Natural(o.Kind) is { IsSolid: true } kind)
                {
                    Stamp(area, o.Position.X, o.Position.Z, kind.Radius * o.Scale);
                }
            }
        }
        return area;
    }

    // Marks the cells whose centre lies within the obstacle's radius (and the clearance), and the cell it stands in.
    private static void Stamp(NavArea area, float x, float z, float radius)
    {
        float x0 = area.X * AreaSize, z0 = area.Z * AreaSize;
        float r = radius + Clearance;
        int i0 = (int)MathF.Floor((x - r - x0) / CellSize), i1 = (int)MathF.Floor((x + r - x0) / CellSize);
        int j0 = (int)MathF.Floor((z - r - z0) / CellSize), j1 = (int)MathF.Floor((z + r - z0) / CellSize);
        for (int j = Math.Max(0, j0); j <= Math.Min(NavArea.Cells - 1, j1); j++)
        {
            for (int i = Math.Max(0, i0); i <= Math.Min(NavArea.Cells - 1, i1); i++)
            {
                float cx = x0 + (i + 0.5f) * CellSize, cz = z0 + (j + 0.5f) * CellSize;
                bool inside = (cx - x) * (cx - x) + (cz - z) * (cz - z) <= r * r;
                bool home = x >= x0 + i * CellSize && x < x0 + (i + 1) * CellSize && z >= z0 + j * CellSize && z < z0 + (j + 1) * CellSize;
                if (inside || home)
                {
                    area.Flags[j * NavArea.Cells + i] |= NavFlags.Blocked;
                }
            }
        }
    }
}
