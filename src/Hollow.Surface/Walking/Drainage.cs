using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>A lake of a walking-scale world: still water at one level in a hollow of the ground.</summary>
public readonly struct WalkingLake
{
    public WalkingLake(double level, double area, double maxDepth, Vector3D centre, int cells)
    {
        Level = level;
        Area = area;
        MaxDepth = maxDepth;
        Centre = centre;
        Cells = cells;
    }

    /// <summary>The water's height above the sea, metres.</summary>
    public double Level { get; }

    /// <summary>Square metres of water, about (the drainage cells it covers).</summary>
    public double Area { get; }

    /// <summary>Its deepest point, metres below the water.</summary>
    public double MaxDepth { get; }

    /// <summary>Unit direction of its middle (area-weighted).</summary>
    public Vector3D Centre { get; }

    /// <summary>Drainage cells under it.</summary>
    public int Cells { get; }
}

/// <summary>
/// A piece of the drainage network between two cells (docs/WALKING_GROUND.md §4): a stream or river with water, or a dry
/// valley. Along it the floor, the water's surface and the widths go linearly from end A (upstream) to end B.
/// </summary>
public readonly struct DrainageSegment
{
    internal DrainageSegment(Vector3D a, Vector3D b, bool stream, double catchment, double floorA, double floorB,
        double surfaceA, double surfaceB, double halfWidthA, double halfWidthB, double valleyA, double valleyB,
        double steepA, double steepB, double wallA, double wallB)
    {
        SteepA = steepA;
        SteepB = steepB;
        WallA = wallA;
        WallB = wallB;
        A = a;
        B = b;
        Stream = stream;
        Catchment = catchment;
        FloorA = floorA;
        FloorB = floorB;
        SurfaceA = surfaceA;
        SurfaceB = surfaceB;
        HalfWidthA = halfWidthA;
        HalfWidthB = halfWidthB;
        ValleyA = valleyA;
        ValleyB = valleyB;
    }

    /// <summary>Unit directions of the upstream and downstream ends.</summary>
    public Vector3D A { get; }
    public Vector3D B { get; }

    /// <summary>Whether water runs in it (a stream or river), or it is a dry valley.</summary>
    public bool Stream { get; }

    /// <summary>The ground that drains through its upstream end, m².</summary>
    public double Catchment { get; }

    /// <summary>The lowest ground along it (the stream's bed, the valley's floor) at each end, metres.</summary>
    public double FloorA { get; }
    public double FloorB { get; }

    /// <summary>The water's surface at each end (NaN for a dry valley), metres.</summary>
    public double SurfaceA { get; }
    public double SurfaceB { get; }

    /// <summary>Half the channel's width, and half the valley's, at each end, metres.</summary>
    public double HalfWidthA { get; }
    public double HalfWidthB { get; }
    public double ValleyA { get; }
    public double ValleyB { get; }

    /// <summary>
    /// How far the valley's sides are walls (0: a smooth valley; 1: a canyon or gorge) and the walls' steepness (rise over
    /// run), at each end.
    /// </summary>
    public double SteepA { get; }
    public double SteepB { get; }
    public double WallA { get; }
    public double WallB { get; }
}

/// <summary>
/// Where water goes on a walking-scale world (docs/WALKING_GROUND.md §4), worked out once when the world is made: the
/// ground is sampled on a cube-sphere grid of about <see cref="WalkingParameters.DrainageCell"/> metres; a priority flood
/// from the sea gives every cell a way down to the sea, filling hollows to the level where they spill; hollows deep and
/// large enough hold lakes at that level; the ground each cell drains is summed down the ways, and where it passes
/// a threshold a dry valley begins, and further down a stream. So every stream runs from its source to the sea or a lake,
/// downhill all the way, and a stream leaving a lake starts at the lake's level. <see cref="Shape"/> gives each lake a
/// smooth shore (a field over its cells: a bowl inside, a low lip outside) and cuts the network's channels and valleys
/// into the ground, with low banks so a stream's water stays in its channel.
/// <para>Deterministic: heights from the world's pure function, a heap ordered by (level, cell), sums in a fixed order.</para>
/// </summary>
internal sealed class Drainage
{
    /// <summary>How far a stream's banks stand above its water, metres.</summary>
    private const double BankRise = 0.35;

    /// <summary>Metres beyond a channel's edge where a stream's water still counts (the bank's foot).</summary>
    private const double WaterMargin = 1.5;

    /// <summary>The tiles the network's pieces are found by: about this size, metres.</summary>
    private const double BucketMetres = 120;

    /// <summary>A stream's banks keep its top for this far past the water's margin, then fall this much a metre.</summary>
    private const double LeveeWidth = 6, LeveeFall = 0.4;

    /// <summary>
    /// Lakes' shapes: each lake cell weighs (1 − x²)² out to this many cells; water lies where the sum passes the
    /// threshold (give or take the shore's noise), a bowl below it and a lip of this height just outside.
    /// </summary>
    private const double LakeReach = 1.6, LakeThreshold = 0.45, ShoreNoise = 0.3, LipHeight = 0.4;

    private readonly double _radius;
    private readonly int _n;
    private readonly Vector3D[] _dir;
    private readonly double[] _height;
    private readonly double[] _filled;
    private readonly int[] _parent;
    private readonly double[] _catchment;
    private readonly int[] _lakeOf;
    private readonly int[] _neighbours;
    private readonly bool[] _stream;
    private readonly bool[] _node;
    private readonly double[] _surface;
    private readonly double[] _floor;
    private readonly List<WalkingLake> _lakes = new();
    private readonly DrainageSegment[] _segments;
    private readonly int[][] _buckets;
    private readonly int _bucketLevel;
    private readonly ulong _seed;
    private readonly double _lakeReach; // metres
    private readonly int[]?[] _lakeCellsNear;
    private readonly bool[] _frozen;

    public Drainage(ulong seed, double radius, WalkingParameters p, Func<Vector3D, (double Height, double Rugged, double Mesa)> ground,
        bool water)
    {
        _seed = seed;
        _radius = radius;
        Level = TileKey.LevelFor(radius, p.DrainageCell, 12);
        _n = 1 << Level;
        int count = CubeSphere.FaceCount * _n * _n;
        _dir = new Vector3D[count];
        var area = new double[count];
        double span = 2.0 / _n;
        for (int i = 0; i < count; i++)
        {
            (int face, int x, int y) = Cell(i);
            double u = -1 + (x + 0.5) * span, v = -1 + (y + 0.5) * span;
            _dir[i] = CubeSphere.Direction(face, u, v);
            Vector3D du = CubeSphere.Direction(face, u + span / 2, v) - CubeSphere.Direction(face, u - span / 2, v);
            Vector3D dv = CubeSphere.Direction(face, u, v + span / 2) - CubeSphere.Direction(face, u, v - span / 2);
            area[i] = Vector3D.Cross(du, dv).Length * radius * radius;
        }
        _height = new double[count];
        var rugged = new double[count];
        var mesa = new double[count];
        Parallel.For(0, count, i => (_height[i], rugged[i], mesa[i]) = ground(_dir[i]));
        _neighbours = Neighbours(count, span);

        // The priority flood: from every cell under the sea, lowest first; each cell reached takes the level it was
        // reached at (its own height, or the hollow's spill level if that is higher) and drains to the cell it came from.
        _filled = new double[count];
        _parent = new int[count];
        Array.Fill(_parent, -1);
        var visited = new bool[count];
        var order = new int[count];
        int popped = 0;
        var queue = new PriorityQueue<int, (double, int)>(count / 8);
        int lowest = 0;
        for (int i = 0; i < count; i++)
        {
            if (_height[i] < _height[lowest])
            {
                lowest = i;
            }
            if (_height[i] < 0)
            {
                visited[i] = true;
                _filled[i] = _height[i];
                queue.Enqueue(i, (_height[i], i));
            }
        }
        if (queue.Count == 0)
        {
            visited[lowest] = true;
            _filled[lowest] = _height[lowest];
            queue.Enqueue(lowest, (_height[lowest], lowest));
        }
        while (queue.TryDequeue(out int i, out _))
        {
            order[popped++] = i;
            for (int k = 0; k < 8; k++)
            {
                int j = _neighbours[i * 8 + k];
                if (j < 0 || visited[j])
                {
                    continue;
                }
                visited[j] = true;
                _filled[j] = Math.Max(_height[j], _filled[i]);
                _parent[j] = i;
                queue.Enqueue(j, (_filled[j], j));
            }
        }

        // The ground each cell drains, summed from the top of every way down.
        _catchment = new double[count];
        for (int i = 0; i < count; i++)
        {
            _catchment[i] = _parent[i] >= 0 ? area[i] : 0;
        }
        for (int k = popped - 1; k >= 0; k--)
        {
            int i = order[k];
            if (_parent[i] >= 0)
            {
                _catchment[_parent[i]] += _catchment[i];
            }
        }

        _lakeOf = water ? FindLakes(p, area) : NoLakes(count);
        _lakeReach = LakeReach * TileKey.EdgeLength(Level, radius);
        _lakeCellsNear = LakeCellsNear(count);
        _frozen = new bool[_lakes.Count];
        // A lake's outlet: the cell its water leaves by starts at the lake's level.
        var outletLevel = new double[count];
        Array.Fill(outletLevel, double.NegativeInfinity);
        for (int i = 0; i < count; i++)
        {
            int d = _parent[i];
            if (_lakeOf[i] >= 0 && d >= 0 && _lakeOf[d] < 0)
            {
                outletLevel[d] = Math.Max(outletLevel[d], _lakes[_lakeOf[i]].Level);
            }
        }
        _node = new bool[count];
        _stream = new bool[count];
        _surface = new double[count];
        _floor = new double[count];
        double[] floor = _floor;
        var halfWidth = new double[count];
        var valley = new double[count];
        var steep = new double[count];
        var wall = new double[count];
        for (int i = 0; i < count; i++)
        {
            _node[i] = _parent[i] >= 0 && _lakeOf[i] < 0 && _catchment[i] >= p.ValleyCatchment;
            _stream[i] = water && _node[i] && _catchment[i] >= p.StreamCatchment;
        }

        // Floors and water surfaces, downstream first (the flood's order), so each takes its outlet's into account:
        // water never runs uphill, and a stream meets the sea at sea level and a lake at the lake's.
        for (int k = 0; k < popped; k++)
        {
            int i = order[k];
            if (!_node[i])
            {
                continue;
            }
            (double downSurface, double downFloor) = Outlet(i);
            // On tablelands water cuts canyons, on mountains gorges: deeper as the catchment grows, between walls.
            double canyon = DMath.SmoothStep(0.3, 0.8, mesa[i]), gorge = DMath.SmoothStep(0.3, 0.8, rugged[i]);
            double cut = Math.Min(1, Math.Sqrt(_catchment[i] / p.CanyonCatchment));
            double incision = (p.CanyonDepth * canyon + p.GorgeDepth * gorge) * cut;
            steep[i] = Math.Max(canyon, gorge);
            wall[i] = canyon >= gorge ? p.CanyonWall : p.GorgeWall;
            if (_stream[i])
            {
                double size = Math.Sqrt(_catchment[i] / p.StreamCatchment);
                double growth = 0.6 + 0.4 * size;
                halfWidth[i] = Math.Min(16, p.StreamHalfWidth + p.StreamWidthGrowth * (size - 1));
                valley[i] = Math.Max(Math.Min(150, p.StreamValley + p.ValleyGrowth * (size - 1)),
                    halfWidth[i] + (incision + 4) / wall[i] + 8);
                double drop = Math.Min(2.5, p.StreamDrop * growth) + incision;
                _surface[i] = Math.Max(Math.Max(downSurface, _height[i] - drop), outletLevel[i]);
                floor[i] = _surface[i] - Math.Min(3, p.StreamDepth * growth);
            }
            else
            {
                // Dry valleys fade out on mountains, whose ridged relief has valleys of its own; on tablelands they
                // are dry canyons with a flat floor.
                double size = Math.Sqrt(_catchment[i] / p.ValleyCatchment);
                double keep = 1 - p.RuggedValleyFade * DMath.SmoothStep(0.2, 0.8, rugged[i]);
                halfWidth[i] = 1.5 + 4 * canyon;
                valley[i] = Math.Max(Math.Max(2, p.DryValley * (0.7 + 0.3 * size) * keep), halfWidth[i] + (incision + 3) / wall[i] + 6);
                _surface[i] = double.NaN;
                floor[i] = Math.Max(downFloor, _height[i] - Math.Min(2, 0.4 + 0.5 * (size - 1)) * keep - incision);
            }
        }

        _segments = BuildSegments(count, order, popped, floor, halfWidth, valley, steep, wall, p.Meander);
        _bucketLevel = TileKey.LevelFor(radius, BucketMetres, 12);
        _buckets = BuildBuckets();
    }

    /// <summary>The cube-sphere level of the drainage grid (7 on the first world: cells of about 30 m).</summary>
    public int Level { get; }

    public int CellCount => _dir.Length;

    public IReadOnlyList<WalkingLake> Lakes => _lakes;

    public IReadOnlyList<DrainageSegment> Segments => _segments;

    public Vector3D CellCentre(int cell) => _dir[cell];

    public double CellHeight(int cell) => _height[cell];

    /// <summary>The cell a cell drains to (−1 for the sea's cells, where every way ends).</summary>
    public int Downstream(int cell) => _parent[cell];

    /// <summary>The ground that drains through a cell, m².</summary>
    public double Catchment(int cell) => _catchment[cell];

    /// <summary>The lake over a cell, or −1.</summary>
    public int LakeOf(int cell) => _lakeOf[cell];

    public bool IsStream(int cell) => _stream[cell];

    /// <summary>A stream cell's water surface, metres (NaN elsewhere).</summary>
    public double StreamSurface(int cell) => _stream[cell] ? _surface[cell] : double.NaN;

    /// <summary>Whether a cell is under the sea (a flood source).</summary>
    public bool IsSea(int cell) => _parent[cell] < 0 && _height[cell] < 0;

    /// <summary>The cell of the grid a unit direction falls in.</summary>
    public int CellAt(Vector3D direction) => (int)TileKey.At(direction, Level).Index;

    /// <summary>Stream cells that no other stream flows into: where streams begin.</summary>
    public List<int> StreamSources()
    {
        var fed = new bool[_dir.Length];
        for (int i = 0; i < _dir.Length; i++)
        {
            if (_stream[i] && _parent[i] >= 0)
            {
                fed[_parent[i]] = true;
            }
        }
        var sources = new List<int>();
        for (int i = 0; i < _dir.Length; i++)
        {
            if (_stream[i] && !fed[i])
            {
                sources.Add(i);
            }
        }
        return sources;
    }

    /// <summary>
    /// The ground at a unit direction shaped by the water: lakes' bowls and shores, then the network's channels and
    /// valleys. Also the water's surface there (a lake's level or a stream's surface where it is above the ground, else
    /// −∞; the sea is the caller's) and how near a stream it lies (0 far, 1 in its channel).
    /// </summary>
    public double Shape(Vector3D p, double h, out double water, out double wet, out bool ice)
    {
        h = LakeShape(p, h, out double lake, out ice);
        if (ice)
        {
            // A frozen lake is ground: its ice, flat at the lake's level, with nothing cut into it.
            water = double.NegativeInfinity;
            wet = 0;
            return h;
        }
        h = Carve(p, h, lake > double.NegativeInfinity, out double stream, out wet);
        water = double.NegativeInfinity;
        if (lake > h)
        {
            water = lake;
        }
        if (stream > h && stream > water)
        {
            water = stream;
        }
        return h;
    }

    /// <summary>Marks the lakes that lie frozen all year: their surface is ice to walk on, not water (§5).</summary>
    public void Freeze(Func<WalkingLake, bool> frozen)
    {
        for (int k = 0; k < _lakes.Count; k++)
        {
            _frozen[k] = frozen(_lakes[k]);
        }
    }

    /// <summary>Whether a lake is frozen (its surface ice).</summary>
    public bool IsFrozen(int lake) => _frozen[lake];

    // A lake's bounds: the field of its cells near the point, generous so it holds the whole hollow. Within them the water
    // stands at the lake's level wherever the ground lies lower (a frozen lake's ice there), so the shore follows the
    // ground's own contour and its slopes run down into the water as they are; just beyond them, ground lower than the
    // lake rises to a low lip, so no water stands against a wall at the bounds' edge.
    private double LakeShape(Vector3D p, double h, out double level, out bool ice)
    {
        level = double.NegativeInfinity;
        ice = false;
        int[]? near = _lakeCellsNear[CellAt(p)];
        if (near == null)
        {
            return h;
        }
        Span<int> ids = stackalloc int[4];
        Span<double> field = stackalloc double[4];
        int n = 0;
        foreach (int c in near)
        {
            double x = (p - _dir[c]).Length * _radius / _lakeReach;
            if (x >= 1)
            {
                continue;
            }
            double k = (1 - x * x) * (1 - x * x);
            int lake = _lakeOf[c], slot = 0;
            while (slot < n && ids[slot] != lake)
            {
                slot++;
            }
            if (slot == n)
            {
                if (n == ids.Length)
                {
                    continue;
                }
                ids[n] = lake;
                field[n++] = 0;
            }
            field[slot] += k;
        }
        if (n == 0)
        {
            return h;
        }
        double t = LakeThreshold * (1 + ShoreNoise * Noise.Fbm(p, _radius, 60, 2, 0.5, _seed, NoiseBasis.Corners));
        for (int slot = 0; slot < n; slot++)
        {
            double l = _lakes[ids[slot]].Level, f = field[slot];
            if (f > t)
            {
                // Within the lake's bounds the water fills the hollow to its level: the shore is wherever the ground
                // itself rises past it. A frozen lake's ice is that water's surface, flat.
                if (_frozen[ids[slot]])
                {
                    if (h < l)
                    {
                        h = l;
                        ice = true;
                    }
                }
                else
                {
                    level = Math.Max(level, l);
                }
            }
            else if (h < l)
            {
                // Just beyond the bounds, ground lower than the lake rises to a low lip (none of the lake stands above it).
                h = Math.Max(h, l + LipHeight * DMath.SmoothStep(0.1 * t, 0.5 * t, f) * DMath.SmoothStep(t, 0.6 * t, f));
            }
        }
        return h;
    }

    // The network's channels and valleys: the lowest of every carved profile within reach. A channel is a parabola from
    // its bed to its banks, the valley eases from the banks to the ground around. Outside every channel (and every lake),
    // ground lower than a stream's banks beside it is raised to them, falling away gently, so the stream's water never
    // spreads past its channel's margin.
    private double Carve(Vector3D p, double h, bool inLake, out double water, out double wet)
    {
        water = double.NegativeInfinity;
        wet = 0;
        double ground = double.PositiveInfinity, levee = double.NegativeInfinity;
        bool any = false, inChannel = false;
        int[] list = _buckets[(int)TileKey.At(p, _bucketLevel).Index];
        for (int k = 0; k < list.Length; k++)
        {
            ref readonly DrainageSegment s = ref _segments[list[k]];
            Vector3D ab = s.B - s.A;
            double len2 = ab.LengthSquared;
            double t = len2 > 0 ? Math.Clamp(Vector3D.Dot(p - s.A, ab) / len2, 0, 1) : 0;
            double dist = (p - (s.A + ab * t)).Length * _radius;
            double valley = s.ValleyA + (s.ValleyB - s.ValleyA) * t;
            if (dist >= valley)
            {
                continue;
            }
            any = true;
            double w = s.HalfWidthA + (s.HalfWidthB - s.HalfWidthA) * t;
            double floor = s.FloorA + (s.FloorB - s.FloorA) * t;
            double surface = s.Stream ? s.SurfaceA + (s.SurfaceB - s.SurfaceA) * t : floor;
            double top = s.Stream ? surface + BankRise : floor;
            double g;
            if (dist <= w)
            {
                double f = dist / w;
                g = floor + (top - floor) * f * f;
                inChannel = true;
            }
            else
            {
                // A smooth valley eases from the banks to the ground; a canyon's or gorge's walls rise at their slope
                // until they meet it, and meet the ground at the valley's edge.
                g = h > top ? top + (h - top) * DMath.SmoothStep(w, valley, dist) : h;
                double steep = s.SteepA + (s.SteepB - s.SteepA) * t;
                if (steep > 0)
                {
                    double wall = s.WallA + (s.WallB - s.WallA) * t;
                    double vee = Math.Min(h, top + (dist - w) * wall);
                    vee += (h - vee) * DMath.SmoothStep(0.8 * valley, valley, dist);
                    g += (vee - g) * steep;
                }
            }
            if (g < ground)
            {
                ground = g;
            }
            if (s.Stream)
            {
                double near = DMath.SmoothStep(valley, w, dist);
                if (near > wet)
                {
                    wet = near;
                }
                if (dist < w + WaterMargin && surface > water)
                {
                    water = surface;
                }
                if (dist > w && dist < w + WaterMargin + LeveeWidth)
                {
                    levee = Math.Max(levee, top - LeveeFall * Math.Max(0, dist - w - WaterMargin));
                }
            }
        }
        if (!any)
        {
            return h;
        }
        if (!inChannel && !inLake && h >= 0 && levee > ground)
        {
            ground = levee;
        }
        return ground;
    }

    // ---------------------------------------------------------------- building

    private (int Face, int X, int Y) Cell(int index)
    {
        int perFace = _n * _n;
        int face = index / perFace, rest = index - face * perFace;
        return (face, rest % _n, rest / _n);
    }

    // Eight neighbours a cell: across the face by index, past its edge by the direction a cell further on (which lands
    // on the neighbouring face). −1 where a corner of the cube leaves fewer.
    private int[] Neighbours(int count, double span)
    {
        var result = new int[count * 8];
        for (int i = 0; i < count; i++)
        {
            (int face, int x, int y) = Cell(i);
            int k = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }
                    int nx = x + dx, ny = y + dy, j;
                    if (nx >= 0 && ny >= 0 && nx < _n && ny < _n)
                    {
                        j = face * _n * _n + ny * _n + nx;
                    }
                    else
                    {
                        double u = -1 + (x + 0.5 + dx) * span, v = -1 + (y + 0.5 + dy) * span;
                        j = (int)TileKey.At(CubeSphere.Direction(face, u, v), Level).Index;
                    }
                    for (int m = 0; m < k; m++)
                    {
                        if (result[i * 8 + m] == j)
                        {
                            j = -1;
                        }
                    }
                    result[i * 8 + k++] = j == i ? -1 : j;
                }
            }
        }
        return result;
    }

    // For every cell within two steps of a lake, the lake cells within two steps of it (null elsewhere).
    private int[]?[] LakeCellsNear(int count)
    {
        var result = new int[]?[count];
        var ring = new List<int>();
        var lakes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            ring.Clear();
            ring.Add(i);
            for (int k = 0; k < 8; k++)
            {
                int j = _neighbours[i * 8 + k];
                if (j < 0)
                {
                    continue;
                }
                if (!ring.Contains(j))
                {
                    ring.Add(j);
                }
                for (int m = 0; m < 8; m++)
                {
                    int q = _neighbours[j * 8 + m];
                    if (q >= 0 && !ring.Contains(q))
                    {
                        ring.Add(q);
                    }
                }
            }
            lakes.Clear();
            foreach (int c in ring)
            {
                if (_lakeOf[c] >= 0)
                {
                    lakes.Add(c);
                }
            }
            if (lakes.Count > 0)
            {
                lakes.Sort();
                result[i] = lakes.ToArray();
            }
        }
        return result;
    }

    private static int[] NoLakes(int count)
    {
        var none = new int[count];
        Array.Fill(none, -1);
        return none;
    }

    // Hollows: cells the flood filled above their own height, joined with neighbours filled to the same level. One that
    // is deep and large enough holds a lake.
    private int[] FindLakes(WalkingParameters p, double[] area)
    {
        int count = _dir.Length;
        var lakeOf = new int[count];
        Array.Fill(lakeOf, -1);
        var seen = new bool[count];
        var stack = new Stack<int>();
        var cells = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (seen[i] || !(_filled[i] > _height[i]))
            {
                continue;
            }
            cells.Clear();
            seen[i] = true;
            stack.Push(i);
            double level = _filled[i], total = 0, deepest = 0;
            Vector3D centre = Vector3D.Zero;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                cells.Add(c);
                total += area[c];
                deepest = Math.Max(deepest, level - _height[c]);
                centre += _dir[c] * area[c];
                for (int k = 0; k < 8; k++)
                {
                    int j = _neighbours[c * 8 + k];
                    if (j >= 0 && !seen[j] && _filled[j] == level && _filled[j] > _height[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }
            if (deepest >= p.LakeMinDepth && total >= p.LakeMinArea)
            {
                foreach (int c in cells)
                {
                    lakeOf[c] = _lakes.Count;
                }
                _lakes.Add(new WalkingLake(level, total, deepest, centre.Normalized, cells.Count));
            }
        }
        return lakeOf;
    }

    // Where a node's water goes: the sea (level 0), a lake (its level) or the next node down.
    private (double Surface, double Floor) Outlet(int i)
    {
        int d = _parent[i];
        if (_parent[d] < 0)
        {
            return (0, Math.Min(_height[d], 0));
        }
        if (_lakeOf[d] >= 0)
        {
            return (_lakes[_lakeOf[d]].Level, _height[d]);
        }
        if (_node[d])
        {
            return (_stream[d] ? _surface[d] : _floor[d], _floor[d]);
        }
        return (_filled[d], _height[d]);
    }

    // One piece per node, to the cell it drains to. Each node is first pulled a quarter of the way toward its
    // neighbours up and down the main stream, so the grid's steps round off into curves.
    private DrainageSegment[] BuildSegments(int count, int[] order, int popped, double[] floor, double[] halfWidth, double[] valley,
        double[] steep, double[] wall, double meander)
    {
        var main = new int[count];
        Array.Fill(main, -1);
        for (int k = popped - 1; k >= 0; k--)
        {
            int i = order[k];
            int d = _parent[i];
            if (!_node[i] || d < 0 || !_node[d])
            {
                continue;
            }
            if (main[d] < 0 || _catchment[i] > _catchment[main[d]] || (_catchment[i] == _catchment[main[d]] && i < main[d]))
            {
                main[d] = i;
            }
        }
        // Two passes of smoothing along the main stream, then a wander across the way it runs: the grid's eight
        // directions give way to curves. Sources and the cells where a way meets the sea or a lake stay put.
        var position = (Vector3D[])_dir.Clone();
        var next = new Vector3D[count];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                next[i] = position[i];
                int d = _parent[i];
                if (_node[i] && d >= 0 && _node[d] && main[i] >= 0)
                {
                    next[i] = (position[i] * 0.5 + position[d] * 0.25 + position[main[i]] * 0.25).Normalized;
                }
            }
            (position, next) = (next, position);
        }
        double cell = TileKey.EdgeLength(Level, 1);
        for (int i = 0; i < count; i++)
        {
            int d = _parent[i];
            if (!_node[i] || d < 0 || !_node[d] || main[i] < 0)
            {
                continue;
            }
            Vector3D along = position[d] - position[main[i]];
            Vector3D across = Vector3D.Cross(position[i], along);
            if (across.LengthSquared < 1e-24)
            {
                continue;
            }
            double wander = meander * cell * Noise.Fbm(_dir[i], _radius, 170, 2, 0.5, _seed + 1, NoiseBasis.Corners);
            position[i] = (position[i] + across.Normalized * wander).Normalized;
        }
        var segments = new List<DrainageSegment>();
        for (int k = 0; k < popped; k++)
        {
            int i = order[k];
            if (!_node[i])
            {
                continue;
            }
            int d = _parent[i];
            (double downSurface, double downFloor) = Outlet(i);
            bool sameKind = _node[d] && _stream[d] == _stream[i];
            double widthB = sameKind ? halfWidth[d] : _parent[d] < 0 && _stream[i] ? halfWidth[i] * 1.4 : halfWidth[i];
            double valleyB = sameKind ? valley[d] : valley[i];
            double floorB = _node[d] ? floor[d] : Math.Min(downFloor, floor[i]);
            double surfaceB = _stream[i] ? downSurface : double.NaN;
            segments.Add(new DrainageSegment(position[i], _node[d] ? position[d] : _dir[d], _stream[i], _catchment[i],
                floor[i], floorB, _stream[i] ? _surface[i] : double.NaN, surfaceB, halfWidth[i], widthB, valley[i], valleyB,
                steep[i], sameKind ? steep[d] : steep[i], wall[i], sameKind ? wall[d] : wall[i]));
        }
        return segments.ToArray();
    }

    private int[][] BuildBuckets()
    {
        var lists = new List<int>[TileKey.CountAt(_bucketLevel)];
        var tiles = new List<TileKey>();
        for (int s = 0; s < _segments.Length; s++)
        {
            DrainageSegment seg = _segments[s];
            double reach = Math.Max(seg.ValleyA, seg.ValleyB) + 1;
            Vector3D mid = (seg.A + seg.B).Normalized;
            double half = (seg.A - seg.B).Length * _radius / 2;
            tiles.Clear();
            // Around samples a third of a tile apart; one more step out catches a tile the reach only clips.
            TileKey.Around(mid, half + reach + TileKey.EdgeLength(_bucketLevel, _radius) / 3, _radius, _bucketLevel, tiles);
            foreach (TileKey tile in tiles)
            {
                (lists[tile.Index] ??= new List<int>()).Add(s);
            }
        }
        var buckets = new int[lists.Length][];
        for (int i = 0; i < lists.Length; i++)
        {
            buckets[i] = lists[i]?.ToArray() ?? Array.Empty<int>();
        }
        return buckets;
    }
}
