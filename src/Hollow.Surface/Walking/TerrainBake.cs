using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>What the bake reads of the designed relief at a cell: its height, and how much of it is tableland or mountain.</summary>
public readonly record struct BakeInput(double Height, double Tableland, double Mountains);

/// <summary>
/// The planet's large shape, baked once (docs/WORLD_TERRAIN_PLAN.md §4.1): the designed relief, sampled on a cube-sphere
/// grid of about <see cref="WalkingParameters.BakeCell"/> metres, worn down by water and gravity. Each step floods the
/// ground from the sea (lakes fill their hollows, every cell gets a way down), routes water to the steepest neighbour,
/// sums the ground each cell drains, cuts each cell toward the one it drains to by the stream-power law, solved
/// implicitly (Braun &amp; Willett 2013), and lets slopes steeper than their rock holds slide (thermal erosion). Tablelands
/// carry a hard caprock above a height, so their edges retreat as escarpments with talus below.
/// <para>Deterministic: sequential flood and erosion in the flood's order, parallel passes that only read the last
/// step's heights and write their own cell, a heap ordered by (level, cell), ties broken by <see cref="Hash64"/>.</para>
/// <para>The result is cached on disk by a key of everything it depends on (<see cref="Get"/>), so a game starts
/// without baking; the server and every client bake or load the same grid.</para>
/// </summary>
public sealed class TerrainBake
{
    /// <summary>Changes whenever the bake's method changes, so caches of an older method are never read.</summary>
    public const int Version = 1;

    private const uint Magic = 0x4B414248; // "HBAK"
    private const double Epsilon = 1e-4;   // the flood's rise per cell over still water, so every cell drains
    private const double SlideRate = 1.0 / 16; // the share of a slope's excess that slides per neighbour and sweep

    private readonly double _radius;
    private readonly int _n;
    private readonly Vector3D[] _dir;
    private readonly int[] _nb;
    private readonly float[] _height;
    private readonly float[] _catchment;
    private readonly float[] _wear;
    private readonly int[] _lakeOf;
    private readonly WalkingLake[] _lakes;
    private readonly double _reach;

    private TerrainBake(double radius, int level, float[] height, float[] catchment, float[] wear, int[] lakeOf, WalkingLake[] lakes)
    {
        _radius = radius;
        Level = level;
        _n = 1 << level;
        _dir = Directions(_n);
        _nb = Neighbours(_n, level);
        _height = height;
        _catchment = catchment;
        _wear = wear;
        _lakeOf = lakeOf;
        _lakes = lakes;
        // The sampling kernel's reach: 1.3 of the shortest spacing, so a point's 3 × 3 cells are all that can reach it.
        double shortest = double.MaxValue;
        for (int i = 0; i < _dir.Length; i++)
        {
            int j = _nb[i * 8 + 4];
            if (j >= 0)
            {
                shortest = Math.Min(shortest, (_dir[i] - _dir[j]).Length);
            }
        }
        _reach = 1.3 * shortest;
    }

    /// <summary>The cube-sphere level of the grid (8 on Earth: cells of about 7.4 m).</summary>
    public int Level { get; }

    public int Count => _height.Length;

    public IReadOnlyList<WalkingLake> Lakes => _lakes;

    /// <summary>How long the bake took, or the load from the cache.</summary>
    public TimeSpan Time { get; private set; }

    public bool FromCache { get; private set; }

    /// <summary>
    /// The ground's height at a unit direction: a plane fitted to the cells within reach, weighted smoothly by distance
    /// (moving least squares), so an even slope stays even between the cells. A weighted average would flatten each
    /// cell's centre into a dimple.
    /// </summary>
    public double HeightAt(Vector3D p)
    {
        int c = (int)TileKey.At(p, Level).Index;
        Vector3D e1 = Vector3D.Cross(Math.Abs(p.Y) < 0.9 ? Vector3D.UnitY : Vector3D.UnitX, p).Normalized;
        Vector3D e2 = Vector3D.Cross(p, e1);
        double s0 = 0, sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0, sh = 0, shx = 0, shy = 0;
        Add(c);
        for (int k = 0; k < 8; k++)
        {
            int j = _nb[c * 8 + k];
            if (j >= 0)
            {
                Add(j);
            }
        }
        // Solve for the plane's height at p (Cramer's rule on the 3 × 3 normal equations).
        double det = s0 * (sxx * syy - sxy * sxy) - sx * (sx * syy - sxy * sy) + sy * (sx * sxy - sxx * sy);
        if (Math.Abs(det) < 1e-12 * Math.Max(1e-30, s0 * s0 * s0))
        {
            return Sample(_height, p);
        }
        double num = sh * (sxx * syy - sxy * sxy) - sx * (shx * syy - sxy * shy) + sy * (shx * sxy - sxx * shy);
        return num / det;

        void Add(int j)
        {
            Vector3D d = _dir[j] - p;
            double r = d.Length / _reach;
            if (r >= 1)
            {
                return;
            }
            double a = 1 - r, w = a * a * a * a * (4 * r + 1);
            double x = Vector3D.Dot(d, e1) / _reach, y = Vector3D.Dot(d, e2) / _reach, h = _height[j];
            s0 += w;
            sx += w * x;
            sy += w * y;
            sxx += w * x * x;
            sxy += w * x * y;
            syy += w * y * y;
            sh += w * h;
            shx += w * h * x;
            shy += w * h * y;
        }
    }

    /// <summary>The ground that drains through a point, m² (the cells within reach, weighted).</summary>
    public double CatchmentAt(Vector3D p) => Sample(_catchment, p);

    /// <summary>How much the bake wore away at a point, metres (negative where material slid in).</summary>
    public double WearAt(Vector3D p) => Sample(_wear, p);

    /// <summary>The lake whose cells lie nearest a point within reach, or −1.</summary>
    public int LakeAt(Vector3D p)
    {
        int c = (int)TileKey.At(p, Level).Index;
        int best = _lakeOf[c];
        double bestLevel = best >= 0 ? _lakes[best].Level : double.NegativeInfinity;
        for (int k = 0; k < 8; k++)
        {
            int j = _nb[c * 8 + k];
            if (j < 0 || _lakeOf[j] < 0)
            {
                continue;
            }
            int lake = _lakeOf[j];
            if (_lakes[lake].Level > bestLevel)
            {
                (best, bestLevel) = (lake, _lakes[lake].Level);
            }
        }
        return best;
    }

    private double Sample(float[] values, Vector3D p)
    {
        int c = (int)TileKey.At(p, Level).Index;
        double sum = 0, weights = 0;
        Add(c);
        for (int k = 0; k < 8; k++)
        {
            int j = _nb[c * 8 + k];
            if (j >= 0)
            {
                Add(j);
            }
        }
        return weights > 0 ? sum / weights : values[c];

        void Add(int j)
        {
            double r = (p - _dir[j]).Length / _reach;
            if (r < 1)
            {
                // Wendland's C2 kernel: smooth, and zero beyond the reach, so crossing into the next cell changes nothing.
                double a = 1 - r, w = a * a * a * a * (4 * r + 1);
                sum += w * values[j];
                weights += w;
            }
        }
    }

    // ---------------------------------------------------------------- the cache

    /// <summary>Where bakes are cached: HOLLOW_BAKE_DIR, or this project's folder under the user's local data.</summary>
    public static string CacheFolder =>
        Environment.GetEnvironmentVariable("HOLLOW_BAKE_DIR") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowUniverseABTestB", "bakes");

    /// <summary>The cache key of a world's bake: everything the bake depends on.</summary>
    public static ulong Key(WalkingParameters p, ulong seed, double radius, double oceanCover) =>
        Hash64.Of(Hash64.Of(p.ToString()), seed, (ulong)BitConverter.DoubleToInt64Bits(radius),
            Hash64.Of((ulong)BitConverter.DoubleToInt64Bits(oceanCover), Version));

    public static string CachePath(ulong key) => Path.Combine(CacheFolder, $"{key:x16}.bake");

    /// <summary>The cached bake of a key, or a new one built and cached.</summary>
    public static TerrainBake Get(ulong key, double radius, int level, Func<TerrainBake> build)
    {
        string path = CachePath(key);
        var watch = Stopwatch.StartNew();
        if (File.Exists(path))
        {
            try
            {
                if (Load(path, key, radius, level) is { } cached)
                {
                    cached.FromCache = true;
                    cached.Time = watch.Elapsed;
                    return cached;
                }
            }
            catch (IOException)
            {
                // A cache being written by another process, or damaged (cut short): bake again.
            }
        }
        TerrainBake built = build();
        try
        {
            Directory.CreateDirectory(CacheFolder);
            built.Save(path, key);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return built;
    }

    private void Save(string path, ulong key)
    {
        string temp = $"{path}.{Environment.ProcessId}.tmp";
        using (var w = new BinaryWriter(File.Create(temp)))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(key);
            w.Write(Level);
            w.Write(Count);
            foreach (float[] array in new[] { _height, _catchment, _wear })
            {
                foreach (float v in array)
                {
                    w.Write(v);
                }
            }
            foreach (int v in _lakeOf)
            {
                w.Write(v);
            }
            w.Write(_lakes.Length);
            foreach (WalkingLake lake in _lakes)
            {
                w.Write(lake.Level);
                w.Write(lake.Area);
                w.Write(lake.MaxDepth);
                w.Write(lake.Centre.X);
                w.Write(lake.Centre.Y);
                w.Write(lake.Centre.Z);
                w.Write(lake.Cells);
            }
        }
        try
        {
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException)
        {
            File.Delete(temp);
        }
    }

    private static TerrainBake? Load(string path, ulong key, double radius, int level)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (r.ReadUInt32() != Magic || r.ReadInt32() != Version || r.ReadUInt64() != key || r.ReadInt32() != level)
        {
            return null;
        }
        int count = r.ReadInt32();
        if (count != CubeSphere.FaceCount << (2 * level))
        {
            return null;
        }
        float[] Floats()
        {
            var a = new float[count];
            for (int i = 0; i < count; i++)
            {
                a[i] = r.ReadSingle();
            }
            return a;
        }
        float[] height = Floats(), catchment = Floats(), wear = Floats();
        var lakeOf = new int[count];
        for (int i = 0; i < count; i++)
        {
            lakeOf[i] = r.ReadInt32();
        }
        var lakes = new WalkingLake[r.ReadInt32()];
        for (int k = 0; k < lakes.Length; k++)
        {
            double level0 = r.ReadDouble(), area = r.ReadDouble(), depth = r.ReadDouble();
            var centre = new Vector3D(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            lakes[k] = new WalkingLake(level0, area, depth, centre, r.ReadInt32());
        }
        return new TerrainBake(radius, level, height, catchment, wear, lakeOf, lakes);
    }

    // ---------------------------------------------------------------- the bake

    /// <summary>Bakes a world's shape from its designed relief (<paramref name="relief"/>, a pure function).</summary>
    public static TerrainBake Build(double radius, int level, bool sea, WalkingParameters p, Func<Vector3D, BakeInput> relief, ulong seed)
    {
        var watch = Stopwatch.StartNew();
        int n = 1 << level, count = CubeSphere.FaceCount * n * n;
        Vector3D[] dir = Directions(n);
        int[] nb = Neighbours(n, level);
        var dist = new double[count * 8];
        for (int i = 0; i < count; i++)
        {
            for (int k = 0; k < 8; k++)
            {
                int j = nb[i * 8 + k];
                dist[i * 8 + k] = j >= 0 ? (dir[i] - dir[j]).Length * radius : 0;
            }
        }
        double[] area = Areas(n, radius);

        // The designed relief, and the rock: soft, harder on mountains, a hard caprock on tablelands above a height.
        var h = new double[count];
        var hardness = new double[count];
        var slide = new double[count];
        var capBase = new double[count];
        Parallel.For(0, count, i =>
        {
            BakeInput b = relief(dir[i]);
            // A little roughness on land, so water gathers into branching valleys instead of running down a smooth slope
            // in parallel rills.
            double rough = b.Height > 1 ? p.BakeRoughness * Noise.Fbm(dir[i], radius, 90, 3, 0.5, seed + 3, NoiseBasis.Corners) : 0;
            h[i] = b.Height + rough;
            double m = Math.Clamp(b.Mountains, 0, 1);
            hardness[i] = 1 + (p.MountainHardness - 1) * m;
            double vary = 1 + 0.2 * Noise.Fbm(dir[i], radius, 160, 2, 0.5, seed + 1, NoiseBasis.Corners);
            slide[i] = (p.TalusSlope + (p.MountainTalusSlope - p.TalusSlope) * m) * vary;
            capBase[i] = b.Tableland >= 0.5
                ? p.CaprockBase + p.CaprockVariation * Noise.Fbm(dir[i], radius, 400, 2, 0.5, seed + 2, NoiseBasis.Corners)
                : double.PositiveInfinity;
        });
        double[] h0 = (double[])h.Clone();
        var isSea = new bool[count];
        if (sea)
        {
            for (int i = 0; i < count; i++)
            {
                isSea[i] = h0[i] < 0;
            }
        }

        var water = new double[count];
        var order = new int[count];
        var receiver = new int[count];
        var catchment = new double[count];
        var next = new double[count];
        for (int step = 0; step < p.BakeSteps; step++)
        {
            int popped = Flood(h, isSea, nb, water, order);
            Receivers(water, isSea, nb, dist, receiver, seed, step);
            Spread(order, popped, water, isSea, nb, dist, area, catchment);
            // Stream power, implicitly, in the flood's order: each cell after the one it drains to.
            for (int k = 0; k < popped; k++)
            {
                int i = order[k], r = receiver[i];
                if (r < 0 || water[i] - h[i] > 1e-6)
                {
                    continue; // the sea, and still water: nothing cuts under a lake
                }
                double baseLevel = Math.Max(h[r], water[r]);
                if (h[i] <= baseLevel)
                {
                    continue;
                }
                double rate = p.ErosionRate * (h[i] > capBase[i] ? p.CaprockHardness : hardness[i]);
                double f = rate * Math.Sqrt(catchment[i]) / ((dir[i] - dir[r]).Length * radius);
                h[i] = (h[i] + f * baseLevel) / (1 + f);
            }
            // Slopes steeper than their rock holds slide: each cell gives a share of every excess and takes its share.
            for (int sweep = 0; sweep < p.ThermalSweeps; sweep++)
            {
                double[] from = h, to = next;
                Parallel.For(0, count, i =>
                {
                    double hi = from[i], delta = 0;
                    double ti = hi > capBase[i] ? p.CaprockTalusSlope : slide[i];
                    for (int k = 0; k < 8; k++)
                    {
                        int j = nb[i * 8 + k];
                        if (j < 0)
                        {
                            continue;
                        }
                        double d = dist[i * 8 + k], hj = from[j];
                        if (hi > hj)
                        {
                            double excess = hi - hj - ti * d;
                            if (excess > 0)
                            {
                                delta -= SlideRate * excess;
                            }
                        }
                        else
                        {
                            double tj = hj > capBase[j] ? p.CaprockTalusSlope : slide[j];
                            double excess = hj - hi - tj * d;
                            if (excess > 0)
                            {
                                delta += SlideRate * excess;
                            }
                        }
                    }
                    to[i] = hi + delta;
                });
                (h, next) = (next, h);
            }
            // Soil creep: every cell eases toward its neighbours a little, which rounds what a cell or two wide would
            // otherwise cut in ribs, and the sliding's cell-to-cell ripple; the streams' cutting outpaces it where water
            // gathers.
            {
                double[] from = h, to = next;
                double creep = p.BakeCreep;
                Parallel.For(0, count, i =>
                {
                    if (isSea[i])
                    {
                        to[i] = from[i];
                        return;
                    }
                    double hi = from[i], delta = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = nb[i * 8 + k];
                        if (j >= 0)
                        {
                            bool edge = k == 1 || k == 3 || k == 4 || k == 6; // the diagonals count half
                            delta += edge ? from[j] - hi : 0.5 * (from[j] - hi);
                        }
                    }
                    to[i] = hi + creep * delta;
                });
                (h, next) = (next, h);
            }
        }

        // The last flood: lakes where still water stands deep and wide enough; smaller hollows are filled, so no puddle
        // pits remain.
        int last = Flood(h, isSea, nb, water, order);
        Receivers(water, isSea, nb, dist, receiver, seed, p.BakeSteps);
        // The masks share each cell's water among its lower neighbours, so wet ground follows the valleys' floors, not
        // one cell's stepped path.
        Spread(order, last, water, isSea, nb, dist, area, catchment);
        var lakeOf = new int[count];
        Array.Fill(lakeOf, -1);
        var lakes = new List<WalkingLake>();
        var seen = new bool[count];
        var stack = new Stack<int>();
        var members = new List<int>();
        for (int s = 0; s < count; s++)
        {
            if (seen[s] || isSea[s] || water[s] - h[s] <= 0.05 || water[s] <= 0)
            {
                continue;
            }
            members.Clear();
            seen[s] = true;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                members.Add(i);
                for (int k = 0; k < 8; k++)
                {
                    int j = nb[i * 8 + k];
                    if (j >= 0 && !seen[j] && !isSea[j] && water[j] - h[j] > 0.05 && water[j] > 0)
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }
            members.Sort();
            double lakeArea = 0, depth = 0, level0 = double.PositiveInfinity;
            Vector3D centre = Vector3D.Zero;
            foreach (int i in members)
            {
                lakeArea += area[i];
                depth = Math.Max(depth, water[i] - h[i]);
                level0 = Math.Min(level0, water[i]);
                centre += dir[i] * area[i];
            }
            if (lakeArea >= p.BakeLakeMinArea && depth >= p.BakeLakeMinDepth)
            {
                int id = lakes.Count;
                lakes.Add(new WalkingLake(level0, lakeArea, depth, centre.Normalized, members.Count));
                foreach (int i in members)
                {
                    lakeOf[i] = id;
                }
            }
            else
            {
                foreach (int i in members)
                {
                    h[i] = water[i];
                }
            }
        }

        var height = new float[count];
        var drained = new float[count];
        var wear = new float[count];
        for (int i = 0; i < count; i++)
        {
            height[i] = (float)h[i];
            drained[i] = (float)catchment[i];
            wear[i] = (float)(h0[i] - h[i]);
        }
        return new TerrainBake(radius, level, height, drained, wear, lakeOf, lakes.ToArray()) { Time = watch.Elapsed };
    }

    // From every cell under the sea (or the lowest cell, on a dry world), lowest first: each cell reached takes its own
    // height or, if lower, a whisker above the cell it was reached from, so hollows fill to their spill and every cell
    // has a lower neighbour. Returns how many cells were reached; order holds them in the order they were reached.
    private static int Flood(double[] h, bool[] sea, int[] nb, double[] water, int[] order)
    {
        int count = h.Length, popped = 0, lowest = 0;
        var visited = new bool[count];
        var queue = new PriorityQueue<int, (double, int)>(count / 8);
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            if (h[i] < h[lowest])
            {
                lowest = i;
            }
            if (sea[i])
            {
                visited[i] = true;
                water[i] = h[i];
                queue.Enqueue(i, (h[i], i));
                any = true;
            }
        }
        if (!any)
        {
            visited[lowest] = true;
            water[lowest] = h[lowest];
            queue.Enqueue(lowest, (h[lowest], lowest));
        }
        while (queue.TryDequeue(out int i, out _))
        {
            order[popped++] = i;
            for (int k = 0; k < 8; k++)
            {
                int j = nb[i * 8 + k];
                if (j < 0 || visited[j])
                {
                    continue;
                }
                visited[j] = true;
                double spill = water[i] + Epsilon;
                water[j] = h[j] > spill ? h[j] : spill;
                queue.Enqueue(j, (water[j], j));
            }
        }
        return popped;
    }

    // Each cell drains to its steepest lower neighbour on the flooded surface; among neighbours within a tenth of the
    // steepest, a hash picks, so channels don't run along the grid's lines.
    private static void Receivers(double[] water, bool[] sea, int[] nb, double[] dist, int[] receiver, ulong seed, int step)
    {
        Parallel.For(0, water.Length, i =>
        {
            if (sea[i])
            {
                receiver[i] = -1;
                return;
            }
            Span<int> candidates = stackalloc int[8];
            Span<double> slopes = stackalloc double[8];
            int c = 0;
            double best = 0;
            for (int k = 0; k < 8; k++)
            {
                int j = nb[i * 8 + k];
                if (j < 0)
                {
                    continue;
                }
                double s = (water[i] - water[j]) / dist[i * 8 + k];
                if (s > 0)
                {
                    candidates[c] = j;
                    slopes[c++] = s;
                    best = Math.Max(best, s);
                }
            }
            if (c == 0)
            {
                receiver[i] = -1;
                return;
            }
            Span<int> near = stackalloc int[8];
            int m = 0;
            for (int t = 0; t < c; t++)
            {
                if (slopes[t] >= 0.9 * best)
                {
                    near[m++] = candidates[t];
                }
            }
            receiver[i] = near[(int)(Hash64.Of(seed, (ulong)i, (ulong)step) % (ulong)m)];
        });
    }

    // The ground each cell drains, with each cell's water shared among all its lower neighbours in proportion to the
    // drop to each (multiple flow directions, FastScape's exponent 1), summed from the top down: on an even slope the
    // water spreads as a sheet, and only where the ground gathers it does a channel take it.
    private static void Spread(int[] order, int popped, double[] water, bool[] sea, int[] nb, double[] dist, double[] area,
        double[] catchment)
    {
        Array.Copy(area, catchment, area.Length);
        Span<double> share = stackalloc double[8];
        for (int k = popped - 1; k >= 0; k--)
        {
            int i = order[k];
            if (sea[i])
            {
                continue;
            }
            double total = 0;
            for (int m = 0; m < 8; m++)
            {
                int j = nb[i * 8 + m];
                double s = j >= 0 ? (water[i] - water[j]) / dist[i * 8 + m] : 0;
                share[m] = s > 0 ? s : 0;
                total += share[m];
            }
            if (total <= 0)
            {
                continue;
            }
            for (int m = 0; m < 8; m++)
            {
                if (share[m] > 0)
                {
                    catchment[nb[i * 8 + m]] += catchment[i] * share[m] / total;
                }
            }
        }
    }

    // The ground each cell drains: its own, plus everything that drains through it, summed from the top down.
    private static void Accumulate(int[] order, int popped, int[] receiver, double[] area, double[] catchment)
    {
        Array.Copy(area, catchment, area.Length);
        for (int k = popped - 1; k >= 0; k--)
        {
            int i = order[k], r = receiver[i];
            if (r >= 0)
            {
                catchment[r] += catchment[i];
            }
        }
    }

    // ---------------------------------------------------------------- the grid

    private static (int Face, int X, int Y) Cell(int n, int index)
    {
        int perFace = n * n;
        int face = index / perFace, rest = index - face * perFace;
        return (face, rest % n, rest / n);
    }

    private static Vector3D[] Directions(int n)
    {
        int count = CubeSphere.FaceCount * n * n;
        var dir = new Vector3D[count];
        double span = 2.0 / n;
        for (int i = 0; i < count; i++)
        {
            (int face, int x, int y) = Cell(n, i);
            dir[i] = CubeSphere.Direction(face, -1 + (x + 0.5) * span, -1 + (y + 0.5) * span);
        }
        return dir;
    }

    private static double[] Areas(int n, double radius)
    {
        int count = CubeSphere.FaceCount * n * n;
        var area = new double[count];
        double span = 2.0 / n;
        for (int i = 0; i < count; i++)
        {
            (int face, int x, int y) = Cell(n, i);
            double u = -1 + (x + 0.5) * span, v = -1 + (y + 0.5) * span;
            Vector3D du = CubeSphere.Direction(face, u + span / 2, v) - CubeSphere.Direction(face, u - span / 2, v);
            Vector3D dv = CubeSphere.Direction(face, u, v + span / 2) - CubeSphere.Direction(face, u, v - span / 2);
            area[i] = Vector3D.Cross(du, dv).Length * radius * radius;
        }
        return area;
    }

    // Eight neighbours of a cell, in the order (dx, dy) = (−1, −1), (0, −1), (1, −1), (−1, 0), (1, 0), (−1, 1), (0, 1),
    // (1, 1): across the face by index, past its edge by the direction a cell further on (which lands on the
    // neighbouring face). −1 where a corner of the cube leaves fewer.
    private static int[] Neighbours(int n, int level)
    {
        int count = CubeSphere.FaceCount * n * n;
        var result = new int[count * 8];
        double span = 2.0 / n;
        for (int i = 0; i < count; i++)
        {
            (int face, int x, int y) = Cell(n, i);
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
                    if (nx >= 0 && ny >= 0 && nx < n && ny < n)
                    {
                        j = face * n * n + ny * n + nx;
                    }
                    else
                    {
                        double u = -1 + (x + 0.5 + dx) * span, v = -1 + (y + 0.5 + dy) * span;
                        j = (int)TileKey.At(CubeSphere.Direction(face, u, v), level).Index;
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
}
