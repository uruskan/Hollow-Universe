using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using HollowUniverse.Common;

namespace HollowUniverse.GalaxyMap;

/// <summary>
/// Streams galaxy cells around the camera focus (GALAXY.md §11). Each level is shown out to a radius that grows
/// with the zoom (bright levels reach further), up to a per-level maximum, and shrinks wherever the galaxy is too
/// dense for <see cref="LevelBudget"/> stars (towards the core). Cells are generated in blocks on worker threads;
/// each block becomes one point mesh on the main thread; blocks out of range are freed.
/// </summary>
public partial class StarStreamer : Node3D
{
    /// <summary>Radius multiplier of the view radius per level (bright levels reach further).</summary>
    private static readonly double[] LevelReach = { 7, 5, 3.5, 2.5, 2, 1.5, 1.2, 1.0 };

    /// <summary>Largest radius per level (keeps each level to tens of thousands of stars near Sol).</summary>
    public static readonly double[] LevelMaxRadius = { 3500, 1850, 1000, 900, 600, 420, 330, 200 };

    /// <summary>Cells per block edge per level: one mesh and one task per block.</summary>
    private static readonly int[] BlockCells = { 1, 1, 1, 2, 4, 4, 8, 8 };

    /// <summary>
    /// Most stars of one level the map keeps loaded (approximately). Near Sol only the widest zooms reach it.
    /// Towards the core, a level whose full radius would hold more shows a smaller radius instead, and its blocks
    /// keep only the stars inside a sphere around the focus.
    /// </summary>
    public const double LevelBudget = 100_000;

    /// <summary>A budget-limited level loads stars out to this multiple of its radius, so small moves need no rebuild.</summary>
    private const double LoadMargin = 1.25;

    private const int LevelCount = StarClassTable.LevelCount;

    private readonly struct BlockKey : IEquatable<BlockKey>
    {
        public readonly int Level;
        public readonly long X;
        public readonly long Y;
        public readonly long Z;

        public BlockKey(int level, long x, long y, long z)
        {
            Level = level;
            X = x;
            Y = y;
            Z = z;
        }

        public double Size => BlockSize(Level);

        public Vector3D Min => SectorGrid.Origin + new Vector3D(X * Size, Y * Size, Z * Size);

        public Vector3D Centre => Min + new Vector3D(Size / 2, Size / 2, Size / 2);

        public bool Equals(BlockKey other) => Level == other.Level && X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is BlockKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Level, X, Y, Z);
    }

    /// <summary>A block to build, keeping the stars within <see cref="Radius"/> of <see cref="Centre"/> (all if infinite).</summary>
    private readonly record struct Job(BlockKey Key, double Priority, Vector3D Centre, double Radius);

    private sealed class BlockEntry
    {
        public BlockKey Key;
        public GalaxyStar[] Stars = Array.Empty<GalaxyStar>();
        public int[] BucketStarts = EmptyBuckets;
        public MeshInstance3D? Mesh;
        // The block holds every star of its cells within CoverRadius of CoverCentre (all of them if infinite).
        public Vector3D CoverCentre;
        public double CoverRadius = double.PositiveInfinity;
    }

    /// <summary>
    /// Recently generated large cells. A budget-limited level keeps only the stars near the focus, so its blocks
    /// are rebuilt as the focus moves; towards the core one coarse cell holds 100,000 stars or more, and
    /// generating it again for every rebuild would waste seconds. Thread-safe; bounded by the number of stars.
    /// </summary>
    private sealed class CellCache
    {
        private const int MinStars = 5_000;
        private readonly int _maxStars;
        private readonly Dictionary<ulong, LinkedListNode<(ulong Key, GalaxyStar[] Stars)>> _index = new();
        private readonly LinkedList<(ulong Key, GalaxyStar[] Stars)> _order = new(); // least recently used first
        private int _stars;

        public CellCache(int maxStars) => _maxStars = maxStars;

        public GalaxyStar[] Get(Galaxy galaxy, CellKey cell)
        {
            lock (_index)
            {
                if (_index.TryGetValue(cell.Packed, out LinkedListNode<(ulong Key, GalaxyStar[] Stars)>? node))
                {
                    _order.Remove(node);
                    _order.AddLast(node);
                    return node.Value.Stars;
                }
            }
            GalaxyStar[] stars = galaxy.GetCellStars(cell);
            if (stars.Length < MinStars)
            {
                return stars;
            }
            lock (_index)
            {
                if (!_index.ContainsKey(cell.Packed))
                {
                    _index[cell.Packed] = _order.AddLast((cell.Packed, stars));
                    _stars += stars.Length;
                    while (_stars > _maxStars && _order.First != _order.Last)
                    {
                        (ulong oldKey, GalaxyStar[] oldStars) = _order.First!.Value;
                        _order.RemoveFirst();
                        _index.Remove(oldKey);
                        _stars -= oldStars.Length;
                    }
                }
            }
            return stars;
        }
    }

    private sealed record BlockResult(BlockKey Key, GalaxyStar[] Stars, int[] BucketStarts, Vector3[] Positions, Color[] Colors, Vector2[] Data,
        Vector3D CoverCentre, double CoverRadius);

    private readonly Dictionary<BlockKey, BlockEntry> _loaded = new();
    private readonly List<Job> _queue = new();
    private readonly HashSet<BlockKey> _building = new();
    private readonly object _queueLock = new();
    private readonly ConcurrentQueue<BlockResult> _results = new();
    private readonly CellCache _cellCache = new(1_200_000);
    private readonly int _maxWorkers = Math.Max(2, System.Environment.ProcessorCount - 1);
    private readonly double[] _radius = new double[LevelCount];
    private readonly double[] _loadRadius = new double[LevelCount];
    private readonly double[] _shownCount = new double[LevelCount];
    private int _running;
    private Vector3D _focus;
    private double _distance = 100;
    private StarCountProfile? _profile;
    private Task<StarCountProfile>? _profileTask;
    private Vector3D _profileRequestedAt = new(double.NaN, 0, 0);
    private Vector3D _lastPlanFocus = new(double.NaN, 0, 0);
    private double _lastPlanDistance;
    private double _sincePlan;

    public Galaxy? Galaxy { get; set; }
    public ShaderMaterial? Material { get; set; }
    public Vector3D RenderOrigin { get; private set; }

    public int LoadedStars { get; private set; }
    public int LoadedBlocks => _loaded.Count;

    public int PendingBlocks
    {
        get
        {
            lock (_queueLock)
            {
                return _queue.Count + _building.Count;
            }
        }
    }

    public bool IsIdle => PendingBlocks == 0 && _results.IsEmpty;

    /// <summary>Main-thread milliseconds per frame spent turning generated blocks into meshes.</summary>
    public double ApplyBudgetMs { get; set; } = 4.0;

    /// <summary>Radius around the focus that the view is about.</summary>
    public static double ViewRadius(double distance) => Math.Max(1.5 * distance, 6);

    /// <summary>Whether a level is shown at a camera distance.</summary>
    public static bool LevelVisible(int level, double distance) => level == 0 || distance <= 1.2 * LevelMaxRadius[level];

    /// <summary>Current radius of a level around the focus (after the star budget).</summary>
    public double LevelRadius(int level) => _radius[level];

    /// <summary>Whether the star budget cut a level's radius short of what the zoom asks for.</summary>
    public bool IsBudgetLimited(int level) => !double.IsPositiveInfinity(_loadRadius[level]);

    /// <summary>Expected number of stars of a level inside its current radius (for exposure).</summary>
    public double ShownCount(int level) => _shownCount[level];

    /// <summary>
    /// The distance from the focus within which about <paramref name="count"/> of the shown stars are expected
    /// (all visible levels together), from the star count profile.
    /// </summary>
    public double NearbyRadius(double count)
    {
        double hi = 0;
        for (int level = 0; level < LevelCount; level++)
        {
            if (LevelVisible(level, _distance))
            {
                hi = Math.Max(hi, _radius[level]);
            }
        }
        if (_profile == null || ShownWithin(hi) <= count)
        {
            return hi;
        }
        double lo = 0;
        for (int i = 0; i < 40; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (ShownWithin(mid) < count)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        return hi;
    }

    private double ShownWithin(double radius)
    {
        double total = 0;
        for (int level = 0; level < LevelCount; level++)
        {
            if (LevelVisible(level, _distance))
            {
                total += _profile!.CountWithin(level, Math.Min(radius, _radius[level]));
            }
        }
        return total;
    }

    /// <summary>Whether a loaded star is on the map now: its level is visible and it is within the level's radius.</summary>
    public bool IsShown(in GalaxyStar star, double fraction = 1.0) =>
        LevelVisible(star.Level, _distance) && Vector3D.Distance(star.Position, _focus) <= fraction * _radius[star.Level];

    private static double BlockSize(int level) => SectorGrid.CellSize(level) * BlockCells[level];

    /// <summary>Called every frame with the camera state.</summary>
    public void SetView(Vector3D focus, double distance)
    {
        _focus = focus;
        _distance = distance;
        UpdateProfile();
        UpdateRadii();
    }

    /// <summary>Moves every mesh to a new render origin (floating origin).</summary>
    public void SetRenderOrigin(Vector3D origin)
    {
        RenderOrigin = origin;
        foreach (BlockEntry entry in _loaded.Values)
        {
            if (entry.Mesh != null)
            {
                entry.Mesh.Position = ToRender(entry.Key.Min);
            }
        }
    }

    public Vector3 ToRender(Vector3D galactic)
    {
        Vector3D d = galactic - RenderOrigin;
        return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
    }

    /// <summary>Buckets per block edge: each block's stars are sorted into a grid of buckets for fast queries.</summary>
    public const int BucketsPerEdge = 8;

    private const int BucketCount = BucketsPerEdge * BucketsPerEdge * BucketsPerEdge;

    private static readonly int[] EmptyBuckets = new int[BucketCount + 1];

    /// <summary>
    /// A loaded block. Its stars are sorted by bucket: bucket b = (z · 8 + y) · 8 + x covers the cube of edge
    /// Size / 8 at Min + (x, y, z) · Size / 8 and holds Stars[BucketStarts[b] .. BucketStarts[b + 1]).
    /// </summary>
    public readonly record struct StarBlock(Vector3D Min, double Size, GalaxyStar[] Stars, int[] BucketStarts)
    {
        public double BucketSize => Size / BucketsPerEdge;

        public Vector3D BucketCentre(int bucket)
        {
            int x = bucket % BucketsPerEdge;
            int y = bucket / BucketsPerEdge % BucketsPerEdge;
            int z = bucket / (BucketsPerEdge * BucketsPerEdge);
            double edge = BucketSize;
            return Min + new Vector3D((x + 0.5) * edge, (y + 0.5) * edge, (z + 0.5) * edge);
        }
    }

    /// <summary>
    /// Every loaded block that has stars (for picking and stalks). Some stars may be outside their level's
    /// radius; see <see cref="IsShown"/>.
    /// </summary>
    public IEnumerable<StarBlock> BlocksWithStars()
    {
        foreach (BlockEntry entry in _loaded.Values)
        {
            if (entry.Stars.Length > 0)
            {
                yield return new StarBlock(entry.Key.Min, entry.Key.Size, entry.Stars, entry.BucketStarts);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (Galaxy == null || _profile == null)
        {
            return;
        }
        _sincePlan += delta;
        bool moved = double.IsNaN(_lastPlanFocus.X)
            || Vector3D.Distance(_focus, _lastPlanFocus) > Math.Max(0.5, _distance * 0.05)
            || Math.Abs(Math.Log(_distance / _lastPlanDistance)) > 0.08;
        // Plan only with a star count profile of where the focus is now: an old one, from a sparser place, could
        // ask for millions of stars at the core.
        bool profileCurrent = Vector3D.Distance(_profile.Centre, _focus) <= Math.Max(0.5, 0.1 * _distance);
        if ((moved || _sincePlan > 0.5) && profileCurrent)
        {
            Plan();
        }
        StartWorkers();
        ApplyResults(ApplyBudgetMs);
    }

    // Keeps a star count profile around the focus, recomputed on a worker thread (a few milliseconds) whenever
    // the focus has moved. The first one is computed at once, so nothing is planned without one.
    private void UpdateProfile()
    {
        if (Galaxy == null)
        {
            return;
        }
        if (_profileTask is { IsCompleted: true })
        {
            if (_profileTask.IsCompletedSuccessfully)
            {
                _profile = _profileTask.Result;
            }
            else
            {
                GD.PushError($"Star count profile failed: {_profileTask.Exception}");
            }
            _profileTask = null;
        }
        if (_profileTask != null || (!double.IsNaN(_profileRequestedAt.X)
            && Vector3D.Distance(_focus, _profileRequestedAt) <= Math.Max(0.25, 0.02 * _distance)))
        {
            return;
        }
        Vector3D at = _focus;
        DensityModel density = Galaxy.Density;
        _profileRequestedAt = at;
        if (_profile == null)
        {
            _profile = new StarCountProfile();
            _profile.Compute(density, at);
            return;
        }
        _profileTask = Task.Run(() =>
        {
            var profile = new StarCountProfile();
            profile.Compute(density, at);
            return profile;
        });
    }

    // The radius of each level: what the zoom asks for, unless the stars inside it would exceed the budget.
    // Cheap; runs every frame with the latest profile.
    private void UpdateRadii()
    {
        if (_profile == null)
        {
            return;
        }
        double view = ViewRadius(_distance);
        for (int level = 0; level < LevelCount; level++)
        {
            double want = Math.Min(view * LevelReach[level], LevelMaxRadius[level]);
            // Whole blocks reach up to 0.87 block sizes past the radius; if even that fits, load whole blocks.
            if (_profile.CountWithin(level, want + 0.87 * BlockSize(level)) <= LevelBudget)
            {
                _radius[level] = want;
                _loadRadius[level] = double.PositiveInfinity;
            }
            else
            {
                double load = Math.Min(_profile.RadiusFor(level, LevelBudget), LoadMargin * want);
                _radius[level] = Math.Max(load / LoadMargin, 1.0);
                _loadRadius[level] = LoadMargin * _radius[level];
            }
            _shownCount[level] = _profile.CountWithin(level, _radius[level]);
        }
    }

    private void Plan()
    {
        _sincePlan = 0;
        _lastPlanFocus = _focus;
        _lastPlanDistance = _distance;

        var remove = new List<BlockKey>();
        foreach (BlockEntry entry in _loaded.Values)
        {
            if (!Wanted(entry.Key, 1.25))
            {
                remove.Add(entry.Key);
            }
        }
        foreach (BlockKey key in remove)
        {
            BlockEntry entry = _loaded[key];
            LoadedStars -= entry.Stars.Length;
            entry.Mesh?.QueueFree();
            _loaded.Remove(key);
        }

        // Every block in range that is missing, or loaded but no longer holding all the stars the view needs.
        var jobs = new List<Job>();
        for (int level = 0; level < LevelCount; level++)
        {
            if (!LevelVisible(level, _distance))
            {
                continue;
            }
            double radius = _radius[level];
            double size = BlockSize(level);
            Vector3D rel = _focus - SectorGrid.Origin;
            long x0 = (long)Math.Floor((rel.X - radius) / size);
            long x1 = (long)Math.Floor((rel.X + radius) / size);
            long y0 = (long)Math.Floor((rel.Y - radius) / size);
            long y1 = (long)Math.Floor((rel.Y + radius) / size);
            long z0 = (long)Math.Floor((rel.Z - radius) / size);
            long z1 = (long)Math.Floor((rel.Z + radius) / size);
            long limit = ((long)SectorGrid.SectorsPerAxis << level) / BlockCells[level];
            double reach = radius + 0.87 * size;
            for (long z = Math.Max(0, z0); z <= Math.Min(limit - 1, z1); z++)
            {
                for (long y = Math.Max(0, y0); y <= Math.Min(limit - 1, y1); y++)
                {
                    for (long x = Math.Max(0, x0); x <= Math.Min(limit - 1, x1); x++)
                    {
                        var key = new BlockKey(level, x, y, z);
                        double d = Vector3D.Distance(key.Centre, _focus);
                        if (d > reach)
                        {
                            continue;
                        }
                        bool loaded = _loaded.TryGetValue(key, out BlockEntry? entry);
                        if (loaded && Covers(entry!))
                        {
                            continue;
                        }
                        // Missing blocks before refreshes of loaded ones (whose old mesh still shows); within each,
                        // coarse levels first, then nearest first.
                        double priority = (loaded ? 1e8 : 0) + level * 1e7 + d / size;
                        jobs.Add(new Job(key, priority, _focus, _loadRadius[level]));
                    }
                }
            }
        }
        lock (_queueLock)
        {
            _queue.Clear();
            foreach (Job job in jobs)
            {
                if (!_building.Contains(job.Key))
                {
                    _queue.Add(job);
                }
            }
            _queue.Sort((a, b) => b.Priority.CompareTo(a.Priority)); // workers take from the end
        }
    }

    private bool Wanted(BlockKey key, double slack)
    {
        if (!LevelVisible(key.Level, _distance / slack))
        {
            return false;
        }
        return Vector3D.Distance(key.Centre, _focus) <= _radius[key.Level] * slack + 0.87 * key.Size;
    }

    // Whether a loaded block holds every star the current view could show from it.
    private bool Covers(BlockEntry entry) =>
        double.IsPositiveInfinity(entry.CoverRadius)
        || Vector3D.Distance(_focus, entry.CoverCentre) + _radius[entry.Key.Level] <= entry.CoverRadius;

    private void StartWorkers()
    {
        while (_running < _maxWorkers)
        {
            lock (_queueLock)
            {
                if (_queue.Count == 0)
                {
                    return;
                }
            }
            Interlocked.Increment(ref _running);
            Galaxy galaxy = Galaxy!;
            Task.Run(() => WorkerLoop(galaxy));
        }
    }

    // Each worker keeps taking the most urgent block until the queue is empty.
    private void WorkerLoop(Galaxy galaxy)
    {
        try
        {
            while (true)
            {
                Job job;
                lock (_queueLock)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }
                    job = _queue[^1];
                    _queue.RemoveAt(_queue.Count - 1);
                    _building.Add(job.Key);
                }
                try
                {
                    _results.Enqueue(Build(galaxy, job));
                }
                catch (Exception e)
                {
                    BlockKey key = job.Key;
                    GD.PushError($"Block L{key.Level} ({key.X},{key.Y},{key.Z}) failed: {e}");
                    _results.Enqueue(new BlockResult(key, Array.Empty<GalaxyStar>(), EmptyBuckets, Array.Empty<Vector3>(), Array.Empty<Color>(),
                        Array.Empty<Vector2>(), job.Centre, double.PositiveInfinity));
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private BlockResult Build(Galaxy galaxy, Job job)
    {
        BlockKey key = job.Key;
        var stars = new List<GalaxyStar>();
        bool filter = !double.IsPositiveInfinity(job.Radius);
        double radiusSq = job.Radius * job.Radius;
        bool complete = true;
        int n = BlockCells[key.Level];
        for (int cz = 0; cz < n; cz++)
        {
            for (int cy = 0; cy < n; cy++)
            {
                for (int cx = 0; cx < n; cx++)
                {
                    if (!CellKey.TryFromGlobal(key.Level, key.X * n + cx, key.Y * n + cy, key.Z * n + cz, out CellKey cell))
                    {
                        continue;
                    }
                    if (!filter)
                    {
                        stars.AddRange(galaxy.GetCellStars(cell));
                        continue;
                    }
                    // Cells outside the sphere are never generated; cells crossing it keep the stars inside.
                    if (NearestSquared(job.Centre, cell.Min, cell.Size) > radiusSq)
                    {
                        complete = false;
                        continue;
                    }
                    GalaxyStar[] cellStars = _cellCache.Get(galaxy, cell);
                    if (FarthestSquared(job.Centre, cell.Min, cell.Size) <= radiusSq)
                    {
                        stars.AddRange(cellStars);
                        continue;
                    }
                    complete = false;
                    foreach (GalaxyStar s in cellStars)
                    {
                        Vector3D d = s.Position - job.Centre;
                        if (d.X * d.X + d.Y * d.Y + d.Z * d.Z <= radiusSq)
                        {
                            stars.Add(s);
                        }
                    }
                }
            }
        }
        Vector3D min = key.Min;
        (GalaxyStar[] sorted, int[] bucketStarts) = SortIntoBuckets(stars, min, key.Size);
        var positions = new Vector3[sorted.Length];
        var colors = new Color[sorted.Length];
        var data = new Vector2[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            ref readonly GalaxyStar s = ref sorted[i];
            Vector3D local = s.Position - min;
            positions[i] = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
            colors[i] = StarColors.For(s.Class, s.Physics.Temperature);
            data[i] = new Vector2(StarColors.DisplayMagnitude(s.Class, s.Physics.AbsoluteMagnitude), key.Level);
        }
        return new BlockResult(key, sorted, bucketStarts, positions, colors, data, job.Centre,
            complete ? double.PositiveInfinity : job.Radius);
    }

    // Counting sort of a block's stars into its grid of buckets.
    private static (GalaxyStar[] Sorted, int[] BucketStarts) SortIntoBuckets(List<GalaxyStar> stars, Vector3D min, double size)
    {
        var bucketOf = new int[stars.Count];
        var starts = new int[BucketCount + 1];
        double perBucket = BucketsPerEdge / size;
        for (int i = 0; i < stars.Count; i++)
        {
            Vector3D local = stars[i].Position - min;
            int x = Math.Clamp((int)(local.X * perBucket), 0, BucketsPerEdge - 1);
            int y = Math.Clamp((int)(local.Y * perBucket), 0, BucketsPerEdge - 1);
            int z = Math.Clamp((int)(local.Z * perBucket), 0, BucketsPerEdge - 1);
            int bucket = (z * BucketsPerEdge + y) * BucketsPerEdge + x;
            bucketOf[i] = bucket;
            starts[bucket + 1]++;
        }
        for (int b = 0; b < BucketCount; b++)
        {
            starts[b + 1] += starts[b];
        }
        var sorted = new GalaxyStar[stars.Count];
        var next = (int[])starts.Clone();
        for (int i = 0; i < stars.Count; i++)
        {
            sorted[next[bucketOf[i]]++] = stars[i];
        }
        return (sorted, starts);
    }

    private static double NearestSquared(Vector3D p, Vector3D min, double size)
    {
        double dx = Math.Max(Math.Max(min.X - p.X, 0), p.X - (min.X + size));
        double dy = Math.Max(Math.Max(min.Y - p.Y, 0), p.Y - (min.Y + size));
        double dz = Math.Max(Math.Max(min.Z - p.Z, 0), p.Z - (min.Z + size));
        return dx * dx + dy * dy + dz * dz;
    }

    private static double FarthestSquared(Vector3D p, Vector3D min, double size)
    {
        double dx = Math.Max(Math.Abs(p.X - min.X), Math.Abs(p.X - (min.X + size)));
        double dy = Math.Max(Math.Abs(p.Y - min.Y), Math.Abs(p.Y - (min.Y + size)));
        double dz = Math.Max(Math.Abs(p.Z - min.Z), Math.Abs(p.Z - (min.Z + size)));
        return dx * dx + dy * dy + dz * dz;
    }

    private void ApplyResults(double budgetMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalMilliseconds < budgetMs && _results.TryDequeue(out BlockResult? result))
        {
            lock (_queueLock)
            {
                _building.Remove(result.Key);
            }
            if (!Wanted(result.Key, 1.25))
            {
                continue;
            }
            // A refreshed block replaces the old one only now, so the map never shows a gap while it rebuilds.
            if (_loaded.TryGetValue(result.Key, out BlockEntry? old))
            {
                LoadedStars -= old.Stars.Length;
                old.Mesh?.QueueFree();
                _loaded.Remove(result.Key);
            }
            var entry = new BlockEntry
            {
                Key = result.Key,
                Stars = result.Stars,
                BucketStarts = result.BucketStarts,
                CoverCentre = result.CoverCentre,
                CoverRadius = result.CoverRadius,
            };
            if (result.Stars.Length > 0 && Material != null)
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = result.Positions;
                arrays[(int)Mesh.ArrayType.Color] = result.Colors;
                arrays[(int)Mesh.ArrayType.TexUV] = result.Data;
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Points, arrays);
                var instance = new MeshInstance3D
                {
                    Mesh = mesh,
                    MaterialOverride = Material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    ExtraCullMargin = (float)result.Key.Size,
                };
                AddChild(instance);
                instance.Position = ToRender(result.Key.Min);
                entry.Mesh = instance;
            }
            _loaded[result.Key] = entry;
            LoadedStars += result.Stars.Length;
        }
    }
}
