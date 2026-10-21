using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
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
/// One cube-sphere tile of the navigation grid (docs/WHOLE_WORLD.md §7.3): the ground's height above the world's mean
/// radius at the corners of its cells (a grid of the tile's own face coordinates), each cell's slope against the local
/// up, and whether it is under water or holds a trunk or a rock. Built once from the surface and the natural objects
/// (both pure functions), on a worker thread, and read on the zone's.
/// </summary>
public sealed class NavArea
{
    public const int Cells = 32;
    public const int Corners = Cells + 1;

    public NavArea(TileKey tile)
    {
        Tile = tile;
    }

    public TileKey Tile { get; }

    /// <summary>The ground's height above the mean radius at each corner, row by row from the tile's (u, v) corner.</summary>
    public readonly float[] Heights = new float[Corners * Corners];

    /// <summary>Each cell's flags, and its slope in degrees.</summary>
    public readonly NavFlags[] Flags = new NavFlags[Cells * Cells];

    public readonly byte[] Slope = new byte[Cells * Cells];

    /// <summary>The epoch it was last used in (unused areas are dropped).</summary>
    internal long LastUsed;
}

/// <summary>
/// The animals' map of a world (GROUND.md §6.9): a coarse grid on the world's cube-sphere tiles of the level nearest
/// <see cref="AreaSize"/> metres, <see cref="NavArea.Cells"/> cells a side (about 2 m), built lazily around animals that
/// are awake. It tells the steering where the ground is, how steep, where the water starts and where trees and rocks
/// stand, and finds short paths around them (<see cref="FindPath"/>) on a small flat grid laid on the ground at the
/// start, whose cells look their flags up wherever they fall, so face seams and corners need nothing special. Building
/// pieces and deployables change, so the steering asks the zone about those directly.
/// </summary>
public sealed class NavGrid : IDisposable
{
    /// <summary>The edge, in metres, an area's level is chosen for.</summary>
    public const float AreaSize = 64f;

    /// <summary>A path step: about a cell.</summary>
    public const float CellSize = 2f;

    /// <summary>Wading is fine; deeper than this (metres under the sea) is water.</summary>
    public const float WadeDepth = 0.35f;

    /// <summary>A trunk or rock blocks the cells within its radius and this much more.</summary>
    public const float Clearance = 0.45f;

    private ISurface _surface;
    private readonly ZoneFrame _frame;
    private INaturalObjects? _nature;
    private GameContent? _content;
    private readonly Dictionary<TileKey, NavArea> _areas = new();
    private readonly Dictionary<TileKey, Task<NavArea>> _pending = new();
    private readonly List<TileKey> _done = new();
    private readonly NearTiles _near;
    private readonly CancellationTokenSource _cancellation = new();
    private volatile bool _stopped;
    private bool _disposed;
    private long _epoch;

    public NavGrid(ISurface surface, ZoneFrame frame, INaturalObjects? nature, GameContent? content)
    {
        _surface = surface;
        _frame = frame;
        _nature = nature;
        _content = content;
        Level = Math.Min(TileKey.LevelFor(surface.Radius, AreaSize), 20);
        _near = new NearTiles(surface.Radius, Level);
    }

    /// <summary>The cube-sphere level of the areas on this world.</summary>
    public int Level { get; }

    /// <summary>Areas built so far, and areas built on the zone's own thread because a worker hadn't finished.</summary>
    public int Built { get; private set; }

    public int BuiltOnDemand { get; private set; }

    public int Count => _areas.Count;

    /// <summary>Admitted builds still owned by this grid; read on the owning thread.</summary>
    public int Pending => _pending.Count;

    public bool IsStopped => _stopped;

    /// <summary>Builds areas on the calling thread instead of on workers (tests that want everything at once).</summary>
    public bool Synchronous { get; set; }

    /// <summary>The area (tile) holding a point.</summary>
    public TileKey AreaOf(Vector3 point) => TileKey.At(_frame.ToBody(point), Level);

    /// <summary>The areas in the world now (the developer panel draws them).</summary>
    public IEnumerable<NavArea> Areas => _areas.Values;

    /// <summary>
    /// Starts building, on a worker, the areas within <paramref name="radius"/> metres of a point not built yet (every
    /// animal asks at every think: the areas near it are kept by the fine cell it stands in, <see cref="NearTiles"/>).
    /// </summary>
    public void Prefetch(Vector3 point, float radius)
    {
        ThrowIfStopped();
        foreach (TileKey tile in _near.Around(_frame.ToBody(point), radius))
        {
            if (_areas.TryGetValue(tile, out NavArea? area))
            {
                area.LastUsed = _epoch;
                continue;
            }
            if (_pending.ContainsKey(tile))
            {
                continue;
            }
            if (Synchronous)
            {
                Ensure(tile);
                continue;
            }
            TileKey key = tile;
            CancellationToken cancellation = _cancellation.Token;
            _pending.Add(tile, Task.Run(() => Build(key, cancellation), cancellation));
        }
    }

    /// <summary>Takes in the areas whose build finished. Once a tick, on the zone's thread.</summary>
    public void Adopt()
    {
        if (_stopped) return;
        _epoch++;
        if (_pending.Count == 0)
        {
            return;
        }
        _done.Clear();
        foreach ((TileKey key, Task<NavArea> task) in _pending)
        {
            if (task.IsCompleted)
            {
                _done.Add(key);
            }
        }
        List<Exception>? failures = null;
        foreach (TileKey key in _done)
        {
            _pending.Remove(key, out Task<NavArea>? task);
            try
            {
                NavArea area = task!.GetAwaiter().GetResult();
                area.LastUsed = _epoch;
                _areas[key] = area;
                Built++;
            }
            catch (Exception error)
            {
                (failures ??= new()).Add(error);
            }
        }
        if (failures != null) throw new AggregateException("Navigation area builds failed.", failures).Flatten();
    }

    /// <summary>Whether the area under a point is built (an animal on unbuilt ground waits for it).</summary>
    public bool IsReady(Vector3 point) => !_stopped && _areas.ContainsKey(AreaOf(point));

    /// <summary>The area under a point, built now if a worker hasn't (waits for one under way).</summary>
    public NavArea Ensure(Vector3 point) => Ensure(AreaOf(point));

    private NavArea Ensure(TileKey tile)
    {
        ThrowIfStopped();
        if (_areas.TryGetValue(tile, out NavArea? area))
        {
            area.LastUsed = _epoch;
            return area;
        }
        if (_pending.Remove(tile, out Task<NavArea>? building))
        {
            area = building.GetAwaiter().GetResult();
        }
        else
        {
            area = Build(tile, _cancellation.Token);
            BuiltOnDemand++;
        }
        area.LastUsed = _epoch;
        _areas[tile] = area;
        Built++;
        return area;
    }

    /// <summary>Drops the areas nobody used in the last <paramref name="keepEpochs"/> epochs (ticks).</summary>
    public void Release(long keepEpochs)
    {
        if (_stopped) return;
        if (_epoch % 30 != 0 || _areas.Count < 64)
        {
            return;
        }
        _done.Clear();
        foreach ((TileKey key, NavArea area) in _areas)
        {
            if (_epoch - area.LastUsed > keepEpochs)
            {
                _done.Add(key);
            }
        }
        foreach (TileKey key in _done)
        {
            _areas.Remove(key);
        }
    }

    /// <summary>
    /// Closes admission and cancels queued/active work without waiting. Call on the owning thread, after its final
    /// query. An in-progress surface/nature call must return before cancellation can be observed.
    /// </summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _cancellation.Cancel();
    }

    /// <summary>
    /// Retires every admitted reader before releasing its inputs. Owner-thread only, outside the physics gate.
    /// Cancellation is normal retirement; other failures are reported together after every task has drained.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Navigation retirement must run outside the Jolt gate.");
        Stop();
        CancellationToken cancellation = _cancellation.Token;
        List<Exception>? failures = null;
        foreach (Task<NavArea> task in _pending.Values)
        {
            try { task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException error) when (error.CancellationToken == cancellation) { }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        _pending.Clear();
        _areas.Clear();
        _done.Clear();
        _open.Clear();
        _cameFrom.Clear();
        _cost.Clear();
        _walkable = Array.Empty<sbyte>();
        _surface = null!;
        _nature = null;
        _content = null;
        _cancellation.Dispose();
        _disposed = true;
        if (failures != null) throw new AggregateException("Navigation retirement observed failed builds.", failures).Flatten();
    }

    private void ThrowIfStopped() => ObjectDisposedException.ThrowIf(_stopped, this);

    // ---------------------------------------------------------------- queries

    // The area under a point, its cell and where in the cell the point falls (0 … 1 both ways). The face coordinates are
    // found once (two arctangents): steering asks about a hundred points a think, a path search thousands.
    private NavArea AreaAt(Vector3 point, out Vector3D direction, out int i, out int j, out float fx, out float fz)
    {
        direction = _frame.ToBody(point).Normalized;
        (int face, double u, double v) = CubeSphere.FromDirection(direction);
        NavArea area = Ensure(TileKey.At(face, u, v, Level));
        TileKey t = area.Tile;
        float cx = (float)Math.Clamp((u - t.U0) / t.Span * NavArea.Cells, 0, NavArea.Cells - 1e-3);
        float cz = (float)Math.Clamp((v - t.V0) / t.Span * NavArea.Cells, 0, NavArea.Cells - 1e-3);
        i = (int)cx;
        j = (int)cz;
        fx = cx - i;
        fz = cz - j;
        return area;
    }

    /// <summary>The ground under (or over) a point, from the grid's heights (bilinear between the cell's corners).</summary>
    public Vector3 Ground(Vector3 point)
    {
        NavArea a = AreaAt(point, out Vector3D direction, out int i, out int j, out float fx, out float fz);
        int k = j * NavArea.Corners + i;
        float h00 = a.Heights[k], h10 = a.Heights[k + 1], h01 = a.Heights[k + NavArea.Corners], h11 = a.Heights[k + NavArea.Corners + 1];
        float h = (h00 + (h10 - h00) * fx) + ((h01 + (h11 - h01) * fx) - (h00 + (h10 - h00) * fx)) * fz;
        return _frame.ToLocal(direction * (_surface.Radius + h));
    }

    public NavFlags Flags(Vector3 point)
    {
        NavArea a = AreaAt(point, out _, out int i, out int j, out _, out _);
        return a.Flags[j * NavArea.Cells + i];
    }

    /// <summary>The cell's slope in radians.</summary>
    public float Slope(Vector3 point)
    {
        NavArea a = AreaAt(point, out _, out int i, out int j, out _, out _);
        return a.Slope[j * NavArea.Cells + i] * (MathF.PI / 180);
    }

    /// <summary>Whether an animal that climbs at most <paramref name="maxSlope"/> may stand at a point.</summary>
    public bool Walkable(Vector3 point, float maxSlope)
    {
        NavArea a = AreaAt(point, out _, out int i, out int j, out _, out _);
        int c = j * NavArea.Cells + i;
        return a.Flags[c] == NavFlags.None && a.Slope[c] * (MathF.PI / 180) <= maxSlope;
    }

    /// <summary>
    /// Whether the straight walk from <paramref name="from"/> to <paramref name="to"/> crosses only walkable cells
    /// (sampled every metre).
    /// </summary>
    public bool Clear(Vector3 from, Vector3 to, float maxSlope)
    {
        float length = Vector3.Distance(from, to);
        int steps = Math.Max(1, (int)MathF.Ceiling(length / (CellSize * 0.5f)));
        for (int s = 1; s <= steps; s++)
        {
            if (!Walkable(Vector3.Lerp(from, to, s / (float)steps), maxSlope))
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
    private sbyte[] _walkable = Array.Empty<sbyte>();

    /// <summary>
    /// A short path from <paramref name="from"/> towards <paramref name="to"/> over walkable cells (A* on a grid of
    /// <see cref="CellSize"/> laid flat on the ground at the start, eight neighbours), within a window of
    /// <paramref name="window"/> cells around the start and at most <paramref name="budget"/> cells looked at. Returns
    /// the farthest point of the path the animal can walk to in a straight line (so it cuts corners like an animal, not
    /// a grid), or false when nothing nearer the goal was found. When the goal itself can't be reached it heads for the
    /// reachable cell nearest it.
    /// </summary>
    public bool FindPath(Vector3 from, Vector3 to, float maxSlope, out Vector3 next, int window = 24, int budget = 1200)
    {
        ThrowIfStopped();
        next = to;
        Vector3 up = _frame.UpAt(from);
        (Vector3 north, Vector3 east) = Facing.Compass(up);
        Vector3 d = to - from;
        int gx = Math.Clamp((int)MathF.Round(Vector3.Dot(d, east) / CellSize), -window, window);
        int gz = Math.Clamp((int)MathF.Round(-Vector3.Dot(d, north) / CellSize), -window, window);
        int span = 2 * window + 1;
        int Index(int x, int z) => (z + window) * span + (x + window);
        Vector3 Cell(int x, int z) => from + (east * x - north * z) * CellSize;
        // Each cell's walkability, looked up once a search: the expansion asks about a cell up to a dozen times.
        if (_walkable.Length < span * span)
        {
            _walkable = new sbyte[span * span];
        }
        Array.Clear(_walkable, 0, span * span);
        bool Walk(int x, int z)
        {
            int c = Index(x, z);
            if (_walkable[c] == 0)
            {
                _walkable[c] = Walkable(Cell(x, z), maxSlope) ? (sbyte)1 : (sbyte)-1;
            }
            return _walkable[c] > 0;
        }
        int start = Index(0, 0), goal = Index(gx, gz);
        _open.Clear();
        _cameFrom.Clear();
        _cost.Clear();
        _open.Enqueue(start, 0);
        _cost[start] = 0;
        int best = start;
        float bestH = Heuristic(0, 0, gx, gz);
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
            int cx = current % span - window, cz = current / span - window;
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
                    if (Math.Abs(nx) > window || Math.Abs(nz) > window)
                    {
                        continue;
                    }
                    if (!Walk(nx, nz))
                    {
                        continue;
                    }
                    // No cutting between two blocked diagonal neighbours.
                    if (dx != 0 && dz != 0 && (!Walk(cx + dx, cz) || !Walk(cx, cz + dz)))
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

        Vector3 Centre(int index) => Ground(Cell(index % span - window, index / span - window));
    }

    private static float Heuristic(int x, int z, int gx, int gz)
    {
        int dx = Math.Abs(x - gx), dz = Math.Abs(z - gz);
        return Math.Max(dx, dz) + 0.4142f * Math.Min(dx, dz);
    }

    // ---------------------------------------------------------------- building an area

    private NavArea Build(TileKey tile, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var area = new NavArea(tile);
        double sea = _surface.SeaRadius;
        var under = new bool[NavArea.Corners * NavArea.Corners];
        var points = new Vector3D[NavArea.Corners * NavArea.Corners];
        for (int j = 0; j < NavArea.Corners; j++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int i = 0; i < NavArea.Corners; i++)
            {
                Vector3D direction = CubeSphere.Direction(tile.Face, TileKey.GridCoordinate(tile.X, tile.Level, i, NavArea.Cells),
                    TileKey.GridCoordinate(tile.Y, tile.Level, j, NavArea.Cells));
                double height = _surface.HeightAt(direction);
                int k = j * NavArea.Corners + i;
                area.Heights[k] = (float)height;
                points[k] = direction * (_surface.Radius + height);
                under[k] = sea > 0 && _surface.Radius + height < sea - WadeDepth;
            }
        }
        for (int j = 0; j < NavArea.Cells; j++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int i = 0; i < NavArea.Cells; i++)
            {
                int k = j * NavArea.Corners + i;
                Vector3D p00 = points[k], p10 = points[k + 1], p01 = points[k + NavArea.Corners], p11 = points[k + NavArea.Corners + 1];
                // The cell's normal from its diagonals, against the up at its middle.
                Vector3D normal = Vector3D.Cross(p11 - p00, p01 - p10).Normalized;
                Vector3D up = (p00 + p10 + p01 + p11).Normalized;
                double cos = Math.Clamp(Math.Abs(Vector3D.Dot(normal, up)), 0, 1);
                double degrees = DMath.Acos(cos) * (180 / Math.PI);
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
            cancellation.ThrowIfCancellationRequested();
            var objects = new List<NaturalObject>();
            double edge = TileKey.EdgeLength(tile.Level, _surface.Radius);
            _nature.Query(_frame.ToLocal(tile.Centre * _surface.Radius), (float)(edge * 0.8 + 6), objects);
            cancellation.ThrowIfCancellationRequested();
            foreach (NaturalObject o in objects)
            {
                cancellation.ThrowIfCancellationRequested();
                if (_content.Natural(o.Kind) is { IsSolid: true } kind)
                {
                    Stamp(area, points, _frame.ToBody(o.Position), kind.Radius * o.Scale);
                }
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return area;
    }

    // Marks the cells whose middle lies within the obstacle's radius (and the clearance), and the cell it stands in.
    private void Stamp(NavArea area, Vector3D[] points, Vector3D at, float radius)
    {
        double r = radius + Clearance;
        TileKey t = area.Tile;
        (int face, double u, double v) = CubeSphere.FromDirection(at);
        int home = -1;
        if (face == t.Face)
        {
            int hi = (int)Math.Floor((u - t.U0) / t.Span * NavArea.Cells), hj = (int)Math.Floor((v - t.V0) / t.Span * NavArea.Cells);
            if (hi >= 0 && hj >= 0 && hi < NavArea.Cells && hj < NavArea.Cells)
            {
                home = hj * NavArea.Cells + hi;
            }
        }
        for (int j = 0; j < NavArea.Cells; j++)
        {
            for (int i = 0; i < NavArea.Cells; i++)
            {
                int k = j * NavArea.Corners + i, c = j * NavArea.Cells + i;
                Vector3D middle = (points[k] + points[k + 1] + points[k + NavArea.Corners] + points[k + NavArea.Corners + 1]) * 0.25;
                if (c == home || (middle - at).LengthSquared <= r * r)
                {
                    area.Flags[c] |= NavFlags.Blocked;
                }
            }
        }
    }
}
