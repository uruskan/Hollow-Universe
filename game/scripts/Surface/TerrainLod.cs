using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Draws a planet's ground out to the horizon (GROUND.md §4.6), from any <see cref="ISurface"/> (docs/WHOLE_WORLD.md
/// §10.1): a quadtree of tiles on each cube face, split near the camera and merged far away. Tile grids come from
/// <see cref="TileMesh.Build"/> on worker threads; the finest tiles are chosen by size, so their vertices are the
/// lattice the physics' ground is built on and feet stand on what is drawn. Vertices morph toward the parent's grid as
/// they near the distance where the parent takes over, so levels change without popping; skirts close any remaining
/// cracks between levels. Each tile carries its cube face (the instance uniform <c>tile_face</c>) and texture
/// coordinates in metres on it (UV2), which the shaders blend across face seams.
/// </summary>
public sealed partial class TerrainLod : Node3D
{
    /// <summary>The finest tiles are the level whose edge is nearest this (39 m on the real first world, 30 m on a 2.41 km one).</summary>
    public const double FinestTileMetres = 36;

    /// <summary>The finest level drawn on this surface (<see cref="FinestTileMetres"/>, by size).</summary>
    public int MaxLevel { get; private set; } = PlanetSurface.CollisionLevel;

    /// <summary>A tile splits when the camera is closer than this many tile edges.</summary>
    // A tile splits within 1.6 of its edges and merges back beyond 1.8, times the graphics settings' terrain detail
    // (GROUND.md §8.6, 0.6–1.6).
    private static double SplitFactor => 1.6 * Math.Clamp(HollowUniverse.Settings.GraphicsSettings.Current.TerrainDetail, 0.5f, 2f);
    private static double MergeFactor => SplitFactor * 1.125;
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
        public bool Failed;
        public float MinHeight;
        public float MaxHeight;
        public Vector2 MorphRange;
    }

    private sealed class Job
    {
        public Node Node = null!;
        public TileKey Key;
        public double Priority;
        public volatile bool Cancelled;
        public TileGrid? Grid;
        public TileMeshData? Data;
        public Exception? Error;
    }

    private ISurface _surface = null!;
    private double _seaLevel;
    private (double Low, double High) _bounds;
    private RenderFrame _frame = null!;
    private ShaderMaterial _material = null!;
    private ShaderMaterial? _waterMaterial;
    private readonly Stack<MeshInstance3D> _waterPool = new();
    private readonly Node[] _roots = new Node[CubeSphere.FaceCount];
    private BuildSession? _work;
    private readonly List<Node> _drawn = new();
    private readonly Stack<MeshInstance3D> _pool = new();
    private readonly List<Node> _built = new();
    private Vector3D _camera;
    private int _requiredFailures;

    /// <summary>Tiles drawn last frame.</summary>
    public int DrawnTiles => _drawn.Count;

    /// <summary>Tile grids waiting for a worker or for upload.</summary>
    public int Pending => _work?.Pending ?? 0;

    /// <summary>Failed builds keep required terrain from reporting ready. Reset by a new Setup.</summary>
    public int FailedBuilds => _requiredFailures;

    /// <summary>Worker completion for the current setup; retain this task before retiring the view.</summary>
    public Task WorkersStopped => _work?.Completion ?? Task.CompletedTask;

    /// <summary>Managed/native submission state for streaming evidence, including the source radii of drawn grids.</summary>
    public (int Drawn, int Pending, int Failed, double MinimumRadius, double MaximumRadius) CaptureDiagnostics()
    {
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        foreach (Node node in _drawn)
        {
            if (node.Grid == null) continue;
            minimum = Math.Min(minimum, node.Grid.Radius);
            maximum = Math.Max(maximum, node.Grid.Radius);
        }
        return (_drawn.Count, Pending, FailedBuilds, _drawn.Count == 0 ? 0 : minimum, _drawn.Count == 0 ? 0 : maximum);
    }

    /// <summary>True when nothing is queued and the drawn set didn't change last update.</summary>
    public bool IsIdle { get; private set; }

    /// <summary>Microseconds spent per tile build, averaged (for --trace).</summary>
    public double BuildMicroseconds => _work?.BuildMicroseconds ?? 0;

    public void Setup(ISurface surface, RenderFrame frame, ShaderMaterial material, ShaderMaterial? water, TerrainViewLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(material);
        ValidateViewSetup(surface, limits); // Refusal preserves the coherent current setup and all its tickets.
        Stop();
        BeginCohorts(surface, limits);
        _surface = surface;
        _seaLevel = surface.SeaRadius > 0 ? surface.SeaRadius - surface.Radius : 0;
        _bounds = surface.HeightBounds;
        MaxLevel = TerrainLattice.Of(surface)?.FinestTileLevel ?? TileKey.LevelFor(surface.Radius, FinestTileMetres);
        _frame = frame;
        _material = material;
        _waterMaterial = water;
        for (int f = 0; f < CubeSphere.FaceCount; f++)
        {
            _roots[f] = NewNode(new TileKey(f, 0, 0, 0), null);
        }
        _work = new BuildSession(surface, Math.Min(Math.Clamp(System.Environment.ProcessorCount - 4, 2, 8),
            limits?.MaxOutstandingJobs ?? 8), _workReservation, limits?.MaxOutstandingJobs ?? int.MaxValue);
        IsIdle = false;
    }

    public override void _ExitTree() => Stop();

    /// <summary>
    /// Retires this setup without blocking the scene thread. Running jobs own their original surface and can only
    /// finish into their retired session, never into a replacement. Completion can be awaited by diagnostics/shutdown.
    /// </summary>
    public void Stop()
    {
        RetireCohorts(); // Invalidate readiness before touching workers or native instances.
        InvalidateContactTiles();
        _work?.Stop();
        foreach (Node root in _roots)
        {
            if (root == null) continue;
            Prune(root);
            Release(root);
        }
        Array.Clear(_roots);
        _nodes.Clear();
        _drawn.Clear();
        _requiredFailures = 0;
        _built.Clear();
        CollectRetiredInstances();
        while (_pool.TryPop(out MeshInstance3D? mesh)) RetireInstance(mesh);
        while (_waterPool.TryPop(out MeshInstance3D? water)) RetireInstance(water);
        _surface = null!;
        _frame = null!;
        _material = null!;
        _waterMaterial = null;
        IsIdle = false;
    }

    private Node NewNode(TileKey key, Node? parent)
    {
        double edge = TileKey.EdgeLength(key.Level, _surface.Radius);
        var node = new Node { Key = key, Centre = key.Centre * _surface.Radius };
        // Before its grid exists, a tile's height range is its parent's (or the surface's for the roots).
        node.MinHeight = parent?.MinHeight ?? (float)_bounds.Low;
        node.MaxHeight = parent?.MaxHeight ?? (float)_bounds.High;
        node.Radius = edge * 0.75 + Math.Max(Math.Abs(node.MinHeight), Math.Abs(node.MaxHeight));
        _nodes.Add(key, node);
        return node;
    }

    // ---------------------------------------------------------------- update

    /// <summary>Picks the tiles to draw for a camera position (body frame) and uploads finished tiles.</summary>
    public void UpdateLod(Vector3D camera)
    {
        if (_work == null || _work.Stopped) return;
        _camera = camera;
        _capacityDeferred = 0;
        _retiringTerrain.RemoveAll(session => session.Completion.IsCompleted);
        CollectRetiredInstances();
        int uploads = 0;
        while (uploads < (Hurry ? HurriedMeshesPerFrame : MeshesPerFrame) && _work.TryTake(out Job? job))
        {
            if (job!.Cancelled || job.Node.Job != job)
            {
                job.Data = null;
                job.Grid = null;
                continue;
            }
            if (job.Error != null)
            {
                FailNode(job.Node, job.Error);
                job.Data = null;
                job.Grid = null;
                continue;
            }
            if (job.Data == null)
            {
                FailNode(job.Node, new InvalidOperationException("Terrain worker completed without vertex data."));
                continue;
            }
            job.Node.Data = job.Data;
            job.Node.Grid = job.Grid;
            job.Data = null;
            job.Grid = null;
            try { Upload(job.Node); }
            catch (Exception error) { FailNode(job.Node, error); }
            uploads++;
        }

        foreach (Node n in _drawn)
        {
            n.Drawn = false;
        }
        var previous = new HashSet<Node>(_drawn);
        _drawn.Clear();
        // Forced paths get scheduling opportunities first even when another face is near the camera.
        foreach (Node root in _roots) if (_forcedNodes.ContainsKey(root.Key)) Visit(root);
        foreach (Node root in _roots) if (!_forcedNodes.ContainsKey(root.Key)) Visit(root);
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
        if (changed) InvalidateContactTiles();
        _work.Reprioritize(j => Priority(j.Node));
        IsIdle = !changed && Pending == 0 && FailedBuilds == 0 && _capacityDeferred == 0;
    }

    private double Priority(Node n) => _forcedNodes.ContainsKey(n.Key) ? -1_000_000 + n.Key.Level :
        DistanceTo(n) / TileKey.EdgeLength(n.Key.Level, _surface.Radius) - n.Key.Level * 0.01;

    private void FailNode(Node node, Exception error)
    {
        if (!node.Failed) { node.Failed = true; _requiredFailures++; }
        node.Requested = false; node.Job = null;
        node.Mesh?.Dispose(); node.Mesh = null;
        node.WaterMesh?.Dispose(); node.WaterMesh = null;
        node.Data = null; node.Grid = null;
        GD.PushError($"Terrain tile {node.Key} failed: {error}");
    }

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
        bool wantSplit = node.Key.Level < MaxLevel && (_forcedSplits.ContainsKey(node.Key) ||
            node.Distance < (node.Split ? MergeFactor : SplitFactor) * edge);
        if (node.Mesh == null)
        {
            // Only roots get here before they are ready; everything else is visited once its grid is drawn.
            Request(node);
            return;
        }
        if (wantSplit && TryCreateChildren(node))
        {
            bool ready = true;
            foreach (Node c in node.Children!)
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
                foreach (Node c in node.Children!)
                {
                    Visit(c);
                }
                return;
            }
        }
        else if (!wantSplit && node.Children != null)
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
        if (n.Failed) { n.Failed = false; _requiredFailures--; }
        Hide(n);
        n.Mesh?.Dispose();
        n.WaterMesh?.Dispose();
        n.Mesh = null;
        n.WaterMesh = null;
        n.Grid = null;
        n.Data = null;
        if (n.Requested && n.Job != null)
        {
            _work?.Cancel(n.Job);
        }
        n.Job = null;
        n.Requested = false;
        n.Drawn = false;
        _nodes.Remove(n.Key);
    }

    private void Request(Node n)
    {
        if (n.Requested || n.Failed)
        {
            return;
        }
        var job = new Job { Node = n, Key = n.Key, Priority = Priority(n) };
        if (!_work!.TryRequest(job)) { _capacityDeferred++; return; }
        n.Requested = true;
        n.Job = job;
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
            n.MorphRange = new Vector2(start, end);
            n.Instance.SetInstanceShaderParameter("morph_range", n.MorphRange);
            n.Instance.SetInstanceShaderParameter("tile_face", (int)n.Key.Face);
            n.Instance.Visible = true;
            if (n.WaterMesh != null && _waterMaterial != null)
            {
                n.WaterInstance = _waterPool.Count > 0 ? _waterPool.Pop() : NewWaterInstance();
                n.WaterInstance.Mesh = n.WaterMesh;
                n.WaterInstance.Transform = n.Instance.Transform;
                n.WaterInstance.SetInstanceShaderParameter("morph_range", n.MorphRange);
                n.WaterInstance.SetInstanceShaderParameter("tile_face", (int)n.Key.Face);
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
        InvalidateContactTiles();
    }

    private void Upload(Node n)
    {
        TileMeshData data = n.Data!;
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Climate;
        arrays[(int)Mesh.ArrayType.TexUV] = data.Heights;
        arrays[(int)Mesh.ArrayType.TexUV2] = data.FaceMetres;
        arrays[(int)Mesh.ArrayType.Custom0] = data.MorphPositions;
        arrays[(int)Mesh.ArrayType.Custom1] = data.MorphNormals;
        arrays[(int)Mesh.ArrayType.Custom2] = data.MaterialsPacked;
        arrays[(int)Mesh.ArrayType.Custom3] = data.MaterialsLast;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        var format = (Mesh.ArrayFormat)(
            ((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | ((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
            | ((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift)
            | ((int)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom3Shift));
        var mesh = new ArrayMesh();
        n.Mesh = mesh; // Failure cleanup owns the native resource even when upload itself throws.
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
        if (mesh.GetSurfaceCount() != 1) throw new InvalidOperationException("Terrain upload did not create a native surface.");
        n.WaterMesh = null;
        if (data.WaterVertices != null)
        {
            using var water = new Godot.Collections.Array();
            water.Resize((int)Mesh.ArrayType.Max);
            water[(int)Mesh.ArrayType.Vertex] = data.WaterVertices;
            water[(int)Mesh.ArrayType.TexUV] = data.WaterDepth!;
            water[(int)Mesh.ArrayType.TexUV2] = data.WaterFaceMetres!;
            water[(int)Mesh.ArrayType.Custom0] = data.WaterMorph!;
            water[(int)Mesh.ArrayType.Index] = data.WaterIndices!;
            var waterMesh = new ArrayMesh();
            n.WaterMesh = waterMesh;
            waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, water, null, null,
                (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift));
            if (waterMesh.GetSurfaceCount() != 1) throw new InvalidOperationException("Water upload did not create a native surface.");
        }
        // The grid's heights are above the sea; the split test measures from the mean radius.
        n.MinHeight = n.Grid!.MinHeight + (float)_seaLevel;
        n.MaxHeight = n.Grid.MaxHeight + (float)_seaLevel;
        n.Centre = n.Grid.Origin;
        n.Radius = n.Grid.BoundingRadius;
        n.Data = null; // the grid stays for re-anchoring; the arrays now live on the GPU
        n.Requested = false;
        n.Job = null;
    }

}

/// <summary>A tile grid as Godot vertex arrays, with skirts, prepared on a worker thread.</summary>
public sealed class TileMeshData
{
    public Vector3[] Vertices = Array.Empty<Vector3>();
    public Vector3[] Normals = Array.Empty<Vector3>();
    public Color[] Climate = Array.Empty<Color>();
    /// <summary>Terrain UV.x: height above local water; UV.y: skirt flag. Water uses its separate depth array.</summary>
    public Vector2[] Heights = Array.Empty<Vector2>();
    public Vector2[] FaceMetres = Array.Empty<Vector2>();
    public float[] MorphPositions = Array.Empty<float>();
    public float[] MorphNormals = Array.Empty<float>();
    /// <summary>
    /// The sixteen ground materials' weights (GroundMaterial order): the first twelve three to a float, as the exact
    /// integer w₀ + 256 w₁ + 65,536 w₂ (CUSTOM2, four floats); the last four as bytes (CUSTOM3).
    /// </summary>
    public float[] MaterialsPacked = Array.Empty<float>();
    public byte[] MaterialsLast = Array.Empty<byte>();
    public int[] Indices = Array.Empty<int>();

    /// <summary>
    /// The water over this tile (the sea, lakes and streams: one surface at the grid's water levels), when any of it lies
    /// under water (null otherwise). Its depth's second channel is how inland it is: 0 the sea, 1 a lake's or a stream's.
    /// </summary>
    public Vector3[]? WaterVertices;
    public float[]? WaterMorph;
    public Vector2[]? WaterDepth;
    public Vector2[]? WaterFaceMetres;
    public int[]? WaterIndices;

    private static readonly ConcurrentDictionary<int, int[]> IndexCache = new();
    private static readonly ConcurrentDictionary<int, int[]> WaterIndexCache = new();

    public static TileMeshData From(TileGrid grid)
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
            FaceMetres = new Vector2[total],
            MorphPositions = new float[total * 3],
            MorphNormals = new float[total * 3],
            MaterialsPacked = new float[total * 4],
            MaterialsLast = new byte[total * 4],
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
        if (grid.HasWater)
        {
            BuildWater(grid, d);
        }
        return d;
    }

    // The water's surface on the same grid as the ground (so they cut each other the same way at every level), at each
    // vertex's water level (the sea's, a lake's, a stream's), with the same morph toward the parent's grid. Depth below
    // it per vertex, for shallows and foam, and how far above the sea it lies (inland water has no swell or surf).
    private static void BuildWater(TileGrid grid, TileMeshData d)
    {
        int n = grid.Cells + 1, count = n * n;
        var positions = new Hollow.Core.Vector3D[count];
        d.WaterVertices = new Vector3[count];
        d.WaterDepth = new Vector2[count];
        d.WaterFaceMetres = new Vector2[count];
        d.WaterMorph = new float[count * 3];
        for (int v = 0; v < count; v++)
        {
            Hollow.Core.Vector3D body = grid.Vertex(v);
            float level = grid.WaterLevels[v];
            Hollow.Core.Vector3D p = body.Normalized * (grid.Radius + grid.SeaLevel + level) - grid.Origin;
            positions[v] = p;
            d.WaterVertices[v] = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
            float inland = Math.Clamp((level - 0.05f) / 0.55f, 0f, 1f);
            d.WaterDepth[v] = new Vector2(level - grid.Heights[v], inland * inland * (3 - 2 * inland));
            d.WaterFaceMetres[v] = new Vector2(grid.FaceMetres[2 * v], grid.FaceMetres[2 * v + 1]);
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
        int stride = Hollow.Surface.BiomeTable.MaterialCount, at = stride * source;
        for (int k = 0; k < 4; k++)
        {
            d.MaterialsPacked[4 * target + k] = grid.Materials[at + 3 * k] + 256f * grid.Materials[at + 3 * k + 1] + 65536f * grid.Materials[at + 3 * k + 2];
            d.MaterialsLast[4 * target + k] = grid.Materials[at + 12 + k];
        }
        d.Climate[target] = new Color(grid.Climate[4 * source] / 255f, grid.Climate[4 * source + 1] / 255f,
            grid.Climate[4 * source + 2] / 255f, grid.Climate[4 * source + 3] / 255f);
        // Terrain UV.x is the wetness height: use the same local stream/lake/sea level as the water mesh.
        // TileGrid.Heights itself remains ocean-relative for terrain bounds, water depth and physics.
        d.Heights[target] = new Vector2(grid.Heights[source] - grid.WaterLevels[source], offset == Vector3.Zero ? 0 : 1);
        d.FaceMetres[target] = new Vector2(grid.FaceMetres[2 * source], grid.FaceMetres[2 * source + 1]);
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
