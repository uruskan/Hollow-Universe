using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Draws a planet's ground out to the horizon (GROUND.md §4.6): a quadtree of tiles on each cube face, split near
/// the camera and merged far away. Tile grids come from <see cref="TileMesh.Build"/> on worker threads, so the
/// vertices near the player are exactly the ones collision uses. Vertices morph toward the parent's grid as they
/// near the distance where the parent takes over, so levels change without popping; skirts close any remaining
/// cracks between levels.
/// </summary>
public sealed partial class TerrainLod : Node3D
{
    public const int MaxLevel = PlanetSurface.CollisionLevel;

    /// <summary>A tile splits when the camera is closer than this many tile edges.</summary>
    private const double SplitFactor = 1.6;
    private const double MergeFactor = 1.8;
    private const int MeshesPerFrame = 10;

    private sealed class Node
    {
        public TileKey Key;
        public Node[]? Children;
        public TileGrid? Grid;
        public TileMeshData? Data;
        public ArrayMesh? Mesh;
        public MeshInstance3D? Instance;
        public ArrayMesh? WaterMesh;
        public MeshInstance3D? WaterInstance;
        public bool Requested;
        public Job? Job;
        public Vector3D Centre;
        public double Radius;
        public double Distance;
        public bool Split;
        public bool Drawn;
        public float MinHeight;
        public float MaxHeight;
    }

    private sealed class Job
    {
        public Node Node = null!;
        public double Priority;
        public volatile bool Cancelled;
    }

    private PlanetSurface _surface = null!;
    private RenderFrame _frame = null!;
    private ShaderMaterial _material = null!;
    private ShaderMaterial? _waterMaterial;
    private readonly Stack<MeshInstance3D> _waterPool = new();
    private readonly Node[] _roots = new Node[CubeSphere.FaceCount];
    private readonly List<Job> _queue = new();
    private readonly object _lock = new();
    private readonly ConcurrentQueue<Job> _done = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly List<Thread> _workers = new();
    private readonly List<Node> _drawn = new();
    private readonly Stack<MeshInstance3D> _pool = new();
    private readonly List<Node> _built = new();
    private volatile bool _stopping;
    private int _pending;
    private Vector3D _camera;

    /// <summary>Tiles drawn last frame.</summary>
    public int DrawnTiles => _drawn.Count;

    /// <summary>Tile grids waiting for a worker or for upload.</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>True when nothing is queued and the drawn set didn't change last update.</summary>
    public bool IsIdle { get; private set; }

    /// <summary>Microseconds spent per tile build, averaged (for --trace).</summary>
    public double BuildMicroseconds { get; private set; }

    private long _buildTicks;
    private int _builds;

    public void Setup(PlanetSurface surface, RenderFrame frame, ShaderMaterial material, ShaderMaterial? water)
    {
        _surface = surface;
        _frame = frame;
        _material = material;
        _waterMaterial = water;
        for (int f = 0; f < CubeSphere.FaceCount; f++)
        {
            _roots[f] = NewNode(new TileKey(f, 0, 0, 0), null);
        }
        int count = Math.Clamp(System.Environment.ProcessorCount - 4, 2, 8);
        for (int i = 0; i < count; i++)
        {
            var thread = new Thread(Work) { IsBackground = true, Name = $"Terrain {i}", Priority = ThreadPriority.BelowNormal };
            thread.Start();
            _workers.Add(thread);
        }
    }

    public override void _ExitTree()
    {
        _stopping = true;
        _signal.Release(_workers.Count);
    }

    private Node NewNode(TileKey key, Node? parent)
    {
        double edge = TileKey.EdgeLength(key.Level, _surface.Radius);
        var node = new Node { Key = key, Centre = key.Centre * _surface.Radius };
        // Before its grid exists, a tile's height range is its parent's (or the planet's for the roots).
        node.MinHeight = parent?.MinHeight ?? -11000;
        node.MaxHeight = parent?.MaxHeight ?? 9000;
        node.Radius = edge * 0.75 + Math.Max(Math.Abs(node.MinHeight), Math.Abs(node.MaxHeight));
        return node;
    }

    // ---------------------------------------------------------------- update

    /// <summary>Picks the tiles to draw for a camera position (body frame) and uploads finished tiles.</summary>
    public void UpdateLod(Vector3D camera)
    {
        _camera = camera;
        int uploads = 0;
        while (uploads < MeshesPerFrame && _done.TryDequeue(out Job? job))
        {
            Interlocked.Decrement(ref _pending);
            if (job.Cancelled || job.Node.Data == null || job.Node.Job != job)
            {
                continue;
            }
            Upload(job.Node);
            uploads++;
        }

        foreach (Node n in _drawn)
        {
            n.Drawn = false;
        }
        var previous = new HashSet<Node>(_drawn);
        _drawn.Clear();
        foreach (Node root in _roots)
        {
            Visit(root);
        }
        bool changed = previous.Count != _drawn.Count;
        foreach (Node n in _drawn)
        {
            if (!previous.Remove(n))
            {
                changed = true;
            }
            Show(n);
        }
        foreach (Node n in previous)
        {
            Hide(n);
        }
        lock (_lock)
        {
            foreach (Job j in _queue)
            {
                j.Priority = Priority(j.Node);
            }
        }
        IsIdle = !changed && Pending == 0 && _done.IsEmpty;
    }

    private double Priority(Node n) => DistanceTo(n) / TileKey.EdgeLength(n.Key.Level, _surface.Radius) - n.Key.Level * 0.01;

    private double DistanceTo(Node n) => Math.Max(0, (n.Centre - _camera).Length - n.Radius);

    // Distance used to split: to the tile's footprint (half an edge around its centre) and its height range.
    private double SplitDistance(Node n)
    {
        double half = 0.55 * TileKey.EdgeLength(n.Key.Level, _surface.Radius);
        Vector3D up = n.Key.Centre;
        Vector3D d = _camera - up * _surface.Radius;
        double vertical = Vector3D.Dot(d, up);
        double horizontal = Math.Max(0, (d - up * vertical).Length - half);
        double above = vertical > n.MaxHeight ? vertical - n.MaxHeight : vertical < n.MinHeight ? n.MinHeight - vertical : 0;
        return Math.Sqrt(horizontal * horizontal + above * above);
    }

    private void Visit(Node node)
    {
        double edge = TileKey.EdgeLength(node.Key.Level, _surface.Radius);
        node.Distance = SplitDistance(node);
        bool wantSplit = node.Key.Level < MaxLevel && node.Distance < (node.Split ? MergeFactor : SplitFactor) * edge;
        if (node.Mesh == null)
        {
            // Only roots get here before they are ready; everything else is visited once its grid is drawn.
            Request(node);
            return;
        }
        if (wantSplit)
        {
            node.Children ??= new[] { NewNode(node.Key.Child(0), node), NewNode(node.Key.Child(1), node), NewNode(node.Key.Child(2), node), NewNode(node.Key.Child(3), node) };
            bool ready = true;
            foreach (Node c in node.Children)
            {
                if (c.Mesh == null)
                {
                    ready = false;
                    Request(c);
                }
            }
            if (ready)
            {
                node.Split = true;
                foreach (Node c in node.Children)
                {
                    Visit(c);
                }
                return;
            }
        }
        else if (node.Children != null)
        {
            Prune(node);
        }
        node.Split = false;
        node.Drawn = true;
        _drawn.Add(node);
    }

    private void Prune(Node node)
    {
        if (node.Children == null)
        {
            return;
        }
        foreach (Node c in node.Children)
        {
            Prune(c);
            Release(c);
        }
        node.Children = null;
        node.Split = false;
    }

    private void Release(Node n)
    {
        Hide(n);
        n.Mesh = null;
        n.WaterMesh = null;
        n.Grid = null;
        n.Data = null;
        if (n.Requested && n.Job != null)
        {
            n.Job.Cancelled = true;
            lock (_lock)
            {
                if (_queue.Remove(n.Job))
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        n.Job = null;
        n.Requested = false;
    }

    private void Request(Node n)
    {
        if (n.Requested)
        {
            return;
        }
        n.Requested = true;
        Interlocked.Increment(ref _pending);
        var job = new Job { Node = n, Priority = Priority(n) };
        n.Job = job;
        lock (_lock)
        {
            _queue.Add(job);
        }
        _signal.Release();
    }

    // ---------------------------------------------------------------- drawing

    private void Show(Node n)
    {
        if (n.Instance == null)
        {
            n.Instance = _pool.Count > 0 ? _pool.Pop() : NewInstance();
            n.Instance.Mesh = n.Mesh;
            n.Instance.Transform = _frame.Anchor(n.Grid!.Origin);
            double edge = TileKey.EdgeLength(n.Key.Level, _surface.Radius);
            // Morph toward the parent's grid from 1.2 to 1.9 split distances of this level, so the parent's own
            // geometry is reached before it takes over (at 2 × SplitFactor tile edges).
            float start = (float)(1.2 * SplitFactor * edge);
            float end = (float)(1.9 * SplitFactor * edge);
            if (n.Key.Level == 0)
            {
                start = end = 1e12f;
            }
            n.Instance.SetInstanceShaderParameter("morph_range", new Vector2(start, end));
            n.Instance.Visible = true;
            if (n.WaterMesh != null && _waterMaterial != null)
            {
                n.WaterInstance = _waterPool.Count > 0 ? _waterPool.Pop() : NewWaterInstance();
                n.WaterInstance.Mesh = n.WaterMesh;
                n.WaterInstance.Transform = n.Instance.Transform;
                n.WaterInstance.SetInstanceShaderParameter("morph_range", new Vector2(start, end));
                n.WaterInstance.Visible = true;
            }
        }
    }

    private void Hide(Node n)
    {
        if (n.Instance == null)
        {
            return;
        }
        n.Instance.Visible = false;
        n.Instance.Mesh = null;
        _pool.Push(n.Instance);
        n.Instance = null;
        if (n.WaterInstance != null)
        {
            n.WaterInstance.Visible = false;
            n.WaterInstance.Mesh = null;
            _waterPool.Push(n.WaterInstance);
            n.WaterInstance = null;
        }
    }

    private MeshInstance3D NewWaterInstance()
    {
        var instance = new MeshInstance3D
        {
            MaterialOverride = _waterMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        AddChild(instance);
        return instance;
    }

    private MeshInstance3D NewInstance()
    {
        var instance = new MeshInstance3D
        {
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        AddChild(instance);
        return instance;
    }

    /// <summary>Re-anchors every drawn tile after the render origin moved.</summary>
    public void Reanchor()
    {
        foreach (Node n in _drawn)
        {
            if (n.Instance != null && n.Grid != null)
            {
                n.Instance.Transform = _frame.Anchor(n.Grid.Origin);
                if (n.WaterInstance != null)
                {
                    n.WaterInstance.Transform = n.Instance.Transform;
                }
            }
        }
    }

    private void Upload(Node n)
    {
        TileMeshData data = n.Data!;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Climate;
        arrays[(int)Mesh.ArrayType.TexUV] = data.Heights;
        arrays[(int)Mesh.ArrayType.Custom0] = data.MorphPositions;
        arrays[(int)Mesh.ArrayType.Custom1] = data.MorphNormals;
        arrays[(int)Mesh.ArrayType.Custom2] = data.Materials0;
        arrays[(int)Mesh.ArrayType.Custom3] = data.Materials1;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        var format = (Mesh.ArrayFormat)(
            ((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | ((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
            | ((int)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom2Shift)
            | ((int)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom3Shift));
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
        n.Mesh = mesh;
        n.WaterMesh = null;
        if (data.WaterVertices != null)
        {
            var water = new Godot.Collections.Array();
            water.Resize((int)Mesh.ArrayType.Max);
            water[(int)Mesh.ArrayType.Vertex] = data.WaterVertices;
            water[(int)Mesh.ArrayType.TexUV] = data.WaterDepth!;
            water[(int)Mesh.ArrayType.Custom0] = data.WaterMorph!;
            water[(int)Mesh.ArrayType.Index] = data.WaterIndices!;
            var waterMesh = new ArrayMesh();
            waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, water, null, null,
                (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift));
            n.WaterMesh = waterMesh;
        }
        n.MinHeight = n.Grid!.MinHeight;
        n.MaxHeight = n.Grid.MaxHeight;
        n.Centre = n.Grid.Origin;
        n.Radius = n.Grid.BoundingRadius;
        n.Data = null; // the grid stays for re-anchoring; the arrays now live on the GPU
        n.Requested = false;
        n.Job = null;
    }

    // ---------------------------------------------------------------- workers

    private void Work()
    {
        while (!_stopping)
        {
            _signal.Wait();
            if (_stopping)
            {
                return;
            }
            Job? job = null;
            lock (_lock)
            {
                int best = -1;
                for (int i = 0; i < _queue.Count; i++)
                {
                    if (best < 0 || _queue[i].Priority < _queue[best].Priority)
                    {
                        best = i;
                    }
                }
                if (best >= 0)
                {
                    job = _queue[best];
                    _queue.RemoveAt(best);
                }
            }
            if (job == null)
            {
                continue;
            }
            if (job.Cancelled)
            {
                _done.Enqueue(job);
                continue;
            }
            try
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                TileGrid grid = TileMesh.Build(_surface, job.Node.Key);
                job.Node.Data = TileMeshData.From(grid, _surface.SeaRadius);
                job.Node.Grid = grid;
                Interlocked.Add(ref _buildTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
                int builds = Interlocked.Increment(ref _builds);
                BuildMicroseconds = _buildTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency / builds;
            }
            catch (Exception e)
            {
                GD.PushError($"Terrain tile {job.Node.Key} failed: {e}");
            }
            _done.Enqueue(job);
        }
    }
}

/// <summary>A tile grid as Godot vertex arrays, with skirts, prepared on a worker thread.</summary>
public sealed class TileMeshData
{
    public Vector3[] Vertices = Array.Empty<Vector3>();
    public Vector3[] Normals = Array.Empty<Vector3>();
    public Color[] Climate = Array.Empty<Color>();
    public Vector2[] Heights = Array.Empty<Vector2>();
    public float[] MorphPositions = Array.Empty<float>();
    public float[] MorphNormals = Array.Empty<float>();
    public byte[] Materials0 = Array.Empty<byte>();
    public byte[] Materials1 = Array.Empty<byte>();
    public int[] Indices = Array.Empty<int>();

    /// <summary>The sea surface over this tile, when any of it lies below sea level (null otherwise).</summary>
    public Vector3[]? WaterVertices;
    public float[]? WaterMorph;
    public Vector2[]? WaterDepth;
    public int[]? WaterIndices;

    private static readonly ConcurrentDictionary<int, int[]> IndexCache = new();
    private static readonly ConcurrentDictionary<int, int[]> WaterIndexCache = new();

    public static TileMeshData From(TileGrid grid, double seaRadius)
    {
        int cells = grid.Cells, n = cells + 1, count = n * n;
        int border = 4 * cells;
        int total = count + border;
        var d = new TileMeshData
        {
            Vertices = new Vector3[total],
            Normals = new Vector3[total],
            Climate = new Color[total],
            Heights = new Vector2[total],
            MorphPositions = new float[total * 3],
            MorphNormals = new float[total * 3],
            Materials0 = new byte[total * 4],
            Materials1 = new byte[total * 4],
        };
        for (int v = 0; v < count; v++)
        {
            Copy(grid, d, v, v, Vector3.Zero);
        }
        // Skirts: the border ring again, hanging below the tile toward the planet's centre.
        double edge = TileKey.EdgeLength(grid.Key.Level, Math.Max(grid.Origin.Length, 1));
        float depth = (float)(edge * 0.03 + 3);
        Vector3 down = -new Vector3((float)grid.Origin.X, (float)grid.Origin.Y, (float)grid.Origin.Z).Normalized() * depth;
        int[] ring = Ring(cells);
        for (int i = 0; i < border; i++)
        {
            Copy(grid, d, ring[i], count + i, down);
        }
        d.Indices = IndexCache.GetOrAdd(cells, BuildIndices);
        if (seaRadius > 0 && grid.MinHeight < 1)
        {
            BuildWater(grid, d, seaRadius);
        }
        return d;
    }

    // The sea's surface on the same grid as the ground (so they cut each other the same way at every level), with
    // the same morph toward the parent's grid. Depth below it per vertex, for shallows and foam.
    private static void BuildWater(TileGrid grid, TileMeshData d, double seaRadius)
    {
        int n = grid.Cells + 1, count = n * n;
        var positions = new Hollow.Core.Vector3D[count];
        d.WaterVertices = new Vector3[count];
        d.WaterDepth = new Vector2[count];
        d.WaterMorph = new float[count * 3];
        for (int v = 0; v < count; v++)
        {
            Hollow.Core.Vector3D body = grid.Vertex(v);
            Hollow.Core.Vector3D p = body.Normalized * seaRadius - grid.Origin;
            positions[v] = p;
            d.WaterVertices[v] = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
            d.WaterDepth[v] = new Vector2(-grid.Heights[v], 0);
        }
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int v = j * n + i, a, b;
                bool oddI = (i & 1) == 1, oddJ = (j & 1) == 1;
                if (oddI && oddJ) { a = (j - 1) * n + (i - 1); b = (j + 1) * n + (i + 1); }
                else if (oddI) { a = v - 1; b = v + 1; }
                else if (oddJ) { a = v - n; b = v + n; }
                else { a = b = v; }
                Hollow.Core.Vector3D m = (positions[a] + positions[b]) * 0.5;
                d.WaterMorph[3 * v] = (float)m.X;
                d.WaterMorph[3 * v + 1] = (float)m.Y;
                d.WaterMorph[3 * v + 2] = (float)m.Z;
            }
        }
        d.WaterIndices = WaterIndexCache.GetOrAdd(grid.Cells, cells =>
        {
            int[] g = TileMesh.Indices(cells);
            var r = new int[g.Length];
            for (int t = 0; t < g.Length; t += 3)
            {
                r[t] = g[t];
                r[t + 1] = g[t + 2];
                r[t + 2] = g[t + 1];
            }
            return r;
        });
    }

    private static void Copy(TileGrid grid, TileMeshData d, int source, int target, Vector3 offset)
    {
        d.Vertices[target] = new Vector3(grid.Positions[3 * source], grid.Positions[3 * source + 1], grid.Positions[3 * source + 2]) + offset;
        d.Normals[target] = new Vector3(grid.Normals[3 * source], grid.Normals[3 * source + 1], grid.Normals[3 * source + 2]);
        d.MorphPositions[3 * target] = grid.MorphPositions[3 * source] + offset.X;
        d.MorphPositions[3 * target + 1] = grid.MorphPositions[3 * source + 1] + offset.Y;
        d.MorphPositions[3 * target + 2] = grid.MorphPositions[3 * source + 2] + offset.Z;
        d.MorphNormals[3 * target] = grid.MorphNormals[3 * source];
        d.MorphNormals[3 * target + 1] = grid.MorphNormals[3 * source + 1];
        d.MorphNormals[3 * target + 2] = grid.MorphNormals[3 * source + 2];
        for (int k = 0; k < 4; k++)
        {
            d.Materials0[4 * target + k] = grid.Materials[8 * source + k];
            d.Materials1[4 * target + k] = grid.Materials[8 * source + 4 + k];
        }
        d.Climate[target] = new Color(grid.Climate[4 * source] / 255f, grid.Climate[4 * source + 1] / 255f,
            grid.Climate[4 * source + 2] / 255f, grid.Climate[4 * source + 3] / 255f);
        d.Heights[target] = new Vector2(grid.Heights[source], offset == Vector3.Zero ? 0 : 1);
    }

    // Border vertices in order around the tile: bottom row, right column, top row backwards, left column backwards.
    private static int[] Ring(int cells)
    {
        int n = cells + 1;
        var ring = new int[4 * cells];
        int k = 0;
        for (int i = 0; i < cells; i++) ring[k++] = i;
        for (int j = 0; j < cells; j++) ring[k++] = j * n + cells;
        for (int i = cells; i > 0; i--) ring[k++] = cells * n + i;
        for (int j = cells; j > 0; j--) ring[k++] = j * n;
        return ring;
    }

    // Godot draws clockwise triangles as front faces: the grid's counter-clockwise ones are reversed. Skirt quads
    // get both windings, so they close cracks seen from either side.
    private static int[] BuildIndices(int cells)
    {
        int n = cells + 1, count = n * n, border = 4 * cells;
        int[] grid = TileMesh.Indices(cells);
        var indices = new int[grid.Length + border * 12];
        for (int t = 0; t < grid.Length; t += 3)
        {
            indices[t] = grid[t];
            indices[t + 1] = grid[t + 2];
            indices[t + 2] = grid[t + 1];
        }
        int[] ring = Ring(cells);
        int k = grid.Length;
        for (int i = 0; i < border; i++)
        {
            int a = ring[i], b = ring[(i + 1) % border];
            int a2 = count + i, b2 = count + (i + 1) % border;
            indices[k++] = a; indices[k++] = b; indices[k++] = b2;
            indices[k++] = a; indices[k++] = b2; indices[k++] = a2;
            indices[k++] = a; indices[k++] = b2; indices[k++] = b;
            indices[k++] = a; indices[k++] = a2; indices[k++] = b2;
        }
        return indices;
    }
}
