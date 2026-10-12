using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

/// <summary>
/// Trees, bushes, berry bushes, ferns and rocks of the surface view (GROUND.md §4.6), from the surface generator's
/// scatter: the same objects the server will know by tile and index. Each species has seeded variants built in code
/// (<see cref="FloraLibrary"/>) with two meshes; near the camera every object is drawn with the first, farther with the
/// second, grouped per variant into MultiMeshes rebuilt as the camera moves. Trees then become impostors (eight baked
/// views), batched per block of scatter tiles, out to <see cref="ImpostorEnd"/>; smaller things shrink into the ground
/// at the end of their range. Shaders pick each instance's level by its own distance and cross-fade.
/// </summary>
public sealed partial class Vegetation : Node3D
{
    public const float ImpostorStart = 230; // trees' shadow meshes end here
    public const float TreeNear = 58;       // trees' full meshes end here, impostors begin
    public const float ImpostorEnd = 2200;
    private const float FadeWidth = 6;
    private const double NearSetRadius = 300;
    private const double RebuildDistance = 8;
    private const int BlockLevel = Scatter.Level - 3; // eight scatter tiles a side

    // One MultiMesh: a variant of a species at one level of detail.
    private sealed class Set
    {
        public FloraLibrary.Species Species = null!;
        public FloraLibrary.Variant Variant = null!;
        public int Lod;
        public MultiMeshInstance3D? Instance;
        public readonly List<float> Buffer = new();
    }

    private sealed class Block
    {
        public TileKey Key;
        public Vector3D Anchor;
        public MultiMeshInstance3D? Instance;
        public bool Built;
    }

    private PlanetSurface _surface = null!;
    private RenderFrame _frame = null!;
    private TextureLayered _groundAlbedo = null!;
    private TextureLayered _groundNormal = null!;
    private Task<FloraLibrary.Data>? _data;
    private FloraLibrary? _library;
    private readonly Dictionary<(FloraLibrary.Variant, int), Set> _sets = new();
    private ImpostorBaker? _baker;
    private ShaderMaterial? _impostors;
    private ArrayMesh _quad = null!;
    private readonly ConcurrentDictionary<ulong, List<ScatterItem>> _full = new();
    private readonly ConcurrentDictionary<ulong, List<ScatterItem>> _trees = new();
    private readonly ConcurrentDictionary<ulong, byte> _requested = new();
    private readonly BlockingQueue _jobs = new();
    private readonly List<Thread> _workers = new();
    private readonly Dictionary<ulong, Block> _blocks = new();
    private Vector3D _nearCentre = new(double.MaxValue, 0, 0);
    private Vector3D _nearAnchor;
    private int _nearMissing;
    private bool _nearDirty = true;
    private volatile bool _stopping;
    private int _busy;

    /// <summary>Instances drawn as meshes near the camera, and as impostors.</summary>
    public int MeshInstances { get; private set; }
    public int ImpostorInstances { get; private set; }

    /// <summary>True when the library is built, the impostors baked and no scatter is pending around the camera.</summary>
    public bool IsIdle => _impostors != null && _jobs.Count == 0 && _nearMissing == 0 && Volatile.Read(ref _busy) == 0;

    public FloraLibrary? Library => _library;

    /// <summary>
    /// Look-dev: one of each tree species at this distance (metres) in front of the camera, small plants and a rock
    /// nearer, all at one level of detail (0, 1, or 2 for impostors). Set before the first update.
    /// </summary>
    public (float Distance, int Lod, Vector3 Forward)? Lineup { get; set; }

    private bool _lineupBuilt;

    public void Setup(PlanetSurface surface, RenderFrame frame, TextureLayered groundAlbedo, TextureLayered groundNormal)
    {
        _surface = surface;
        _frame = frame;
        _groundAlbedo = groundAlbedo;
        _groundNormal = groundNormal;
        _quad = BuildQuad();
        int seed = (int)(surface.Seed & 0x7FFFFFFF);
        _data = Task.Run(() => FloraLibrary.Build(seed));
        for (int i = 0; i < 2; i++)
        {
            var t = new Thread(Work) { IsBackground = true, Name = $"Scatter {i}", Priority = ThreadPriority.BelowNormal };
            t.Start();
            _workers.Add(t);
        }
    }

    public override void _ExitTree()
    {
        _stopping = true;
        _jobs.Stop();
    }

    // ---------------------------------------------------------------- library

    private bool EnsureLibrary()
    {
        if (_library != null)
        {
            return true;
        }
        if (_data == null || !_data.IsCompleted)
        {
            return false;
        }
        if (_data.IsFaulted)
        {
            GD.PushError($"Flora failed: {_data.Exception}");
            _data = null;
            return false;
        }
        _library = FloraLibrary.Create(_data.Result, _groundAlbedo, _groundNormal);
        _data = null;
        foreach (FloraLibrary.Species s in _library.All)
        {
            foreach (FloraLibrary.Variant v in s.Variants)
            {
                for (int lod = 0; lod < 2; lod++)
                {
                    _sets[(v, lod)] = new Set { Species = s, Variant = v, Lod = lod };
                }
            }
        }
        _baker = new ImpostorBaker { Name = "Impostors" };
        AddChild(_baker);
        BakeImpostors();
        return true;
    }

    private async void BakeImpostors()
    {
        ImpostorBaker.Result result = await _baker!.Bake(_library!);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_impostor.gdshader") };
        material.SetShaderParameter("colour_atlas", result.Colour);
        material.SetShaderParameter("normal_atlas", result.Normal);
        material.SetShaderParameter("rows", (float)result.Rows);
        material.SetShaderParameter("views", (float)ImpostorBaker.Views);
        material.SetShaderParameter("fade_in", new Vector2(TreeNear - FadeWidth, TreeNear));
        material.SetShaderParameter("fade_out", ImpostorEnd);
        _impostors = material;
        _baker.QueueFree();
        _baker = null;
    }

    private static ArrayMesh BuildQuad()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 0, 3, 2 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // ---------------------------------------------------------------- update

    /// <summary>Streams scatter around the camera (body frame) and rebuilds what it draws.</summary>
    public void UpdateVegetation(Vector3D camera)
    {
        if (!EnsureLibrary())
        {
            return;
        }
        double edge = TileKey.EdgeLength(Scatter.Level, _surface.Radius);
        Vector3D up = camera.Normalized;
        Vector3D ground = up * _surface.Radius;
        var zone = new ZoneFrame(ground);
        // Sample at a third of a tile: tiles are turned against the zone's axes and vary in size, so sampling at a
        // whole tile's spacing would skip some.
        double step = edge / 3;
        int nearSteps = (int)Math.Ceiling((NearSetRadius + edge) / step);
        _nearMissing = 0;
        var nearTiles = new List<TileKey>();
        var seen = new HashSet<ulong>();
        for (int j = -nearSteps; j <= nearSteps; j++)
        {
            for (int i = -nearSteps; i <= nearSteps; i++)
            {
                TileKey key = TileKey.At(zone.ToBody(i * step, 0, j * step), Scatter.Level);
                if (!seen.Add(key.Packed) || Vector3D.Distance(key.Centre * _surface.Radius, ground) > NearSetRadius + edge)
                {
                    continue;
                }
                nearTiles.Add(key);
                if (!_full.ContainsKey(key.Packed))
                {
                    _nearMissing++;
                    Request(key, full: true, priority: Vector3D.Distance(key.Centre * _surface.Radius, ground));
                }
            }
        }
        if (_nearMissing == 0 && (Vector3D.Distance(camera, _nearCentre) > RebuildDistance || _nearDirty))
        {
            BuildNear(nearTiles, camera);
        }
        if (_impostors != null)
        {
            UpdateBlocks(zone, ground);
        }
        if (Lineup is { } lineup && !_lineupBuilt && (lineup.Lod < 2 || _impostors != null))
        {
            BuildLineup(camera, lineup.Distance, lineup.Lod, lineup.Forward);
            _lineupBuilt = true;
        }
    }

    private void BuildLineup(Vector3D camera, float distance, int lod, Vector3 forward)
    {
        Vector3D up = camera.Normalized;
        Vector3D f = _frame.DirectionToBody(forward);
        f = (f - up * Vector3D.Dot(f, up)).Normalized;
        Vector3D side = Vector3D.Cross(f, up).Normalized;
        var rows = new List<(ScatterKind Kind, float Along, float Across)>
        {
            (ScatterKind.Fir, distance, -15), (ScatterKind.Pine, distance, -5), (ScatterKind.Birch, distance, 5), (ScatterKind.Broadleaf, distance, 15),
            (ScatterKind.Bush, distance * 0.3f, -4.4f), (ScatterKind.BerryRed, distance * 0.3f, -2.2f), (ScatterKind.BerryBlue, distance * 0.3f, 0),
            (ScatterKind.Fern, distance * 0.3f, 2.2f), (ScatterKind.Rock, distance * 0.3f, 4.4f),
        };
        foreach ((ScatterKind kind, float along, float across) in rows)
        {
            FloraLibrary.Species? species = _library!.For(kind);
            if (species == null)
            {
                continue;
            }
            FloraLibrary.Variant variant = species.Variants[0];
            Vector3D d = (camera + f * along + side * across).Normalized;
            double h = _surface.HeightAt(d);
            var item = new ScatterItem(0, kind, 0, d, (float)h, 0.6f, 1.0f);
            Vector3D body = item.Position(_surface.Radius);
            Transform3D t = InstanceTransform(item, body, _frame.Origin, species.Scale, 0);
            var buffer = new List<float>();
            bool impostor = lod == 2 && species.Tree;
            if (impostor)
            {
                Append(buffer, t, 0.5f, variant.ImpostorRow, variant.Extent, variant.Height);
            }
            else
            {
                Append(buffer, t, 0.5f, 0.5f, 0, 0);
            }
            var mmi = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true,
                    Mesh = impostor ? _quad : lod == 0 ? variant.Lod0 : variant.Lod1,
                    InstanceCount = 1,
                    Buffer = buffer.ToArray(),
                },
                MaterialOverride = impostor ? _impostors : null,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            if (impostor)
            {
                mmi.Multimesh.CustomAabb = new Aabb(new Vector3(-50, -50, -50), new Vector3(100, 100, 100));
                _impostors!.SetShaderParameter("fade_in", new Vector2(0, 0));
            }
            AddChild(mmi);
        }
    }

    private void Request(TileKey key, bool full, double priority)
    {
        ulong id = key.Packed ^ (full ? 1UL << 63 : 0);
        if (_requested.TryAdd(id, 0))
        {
            Interlocked.Increment(ref _busy);
            _jobs.Add(priority + (full ? 0 : 1e6), () =>
            {
                try
                {
                    List<ScatterItem> items = Scatter.ForTile(_surface, key, treesOnly: !full);
                    (full ? _full : _trees)[key.Packed] = items;
                }
                catch (Exception e)
                {
                    GD.PushError($"Scatter {key} failed: {e}");
                }
                finally
                {
                    Interlocked.Decrement(ref _busy);
                }
            });
        }
    }

    private void Work()
    {
        while (!_stopping)
        {
            Action? job = _jobs.Take();
            job?.Invoke();
        }
    }

    // ---------------------------------------------------------------- meshes near the camera

    private void BuildNear(List<TileKey> tiles, Vector3D camera)
    {
        _nearCentre = camera;
        _nearDirty = false;
        _nearAnchor = _frame.Origin;
        foreach (Set set in _sets.Values)
        {
            set.Buffer.Clear();
        }
        int count = 0;
        double margin = 2 * RebuildDistance;
        foreach (TileKey key in tiles)
        {
            if (!_full.TryGetValue(key.Packed, out List<ScatterItem>? items))
            {
                continue;
            }
            foreach (ScatterItem item in items)
            {
                FloraLibrary.Species? species = _library!.For(item.Kind);
                if (species == null || species.Variants.Count == 0)
                {
                    continue;
                }
                Vector3D body = item.Position(_surface.Radius);
                double d = Vector3D.Distance(body, camera);
                FloraLibrary.Bands bands = species.Bands;
                if (d > bands.Far + margin)
                {
                    continue;
                }
                FloraLibrary.Variant variant = species.Variants[item.Variant % species.Variants.Count];
                float scale = item.Scale * species.Scale;
                float sink = item.Kind == ScatterKind.Rock ? -0.12f * scale : -0.05f;
                Transform3D t = InstanceTransform(item, body, _nearAnchor, scale, sink);
                (float a, float b) = Shade(item);
                // Each level only holds what it may draw before the next rebuild: the shader still picks per instance.
                if (d < bands.Near + margin)
                {
                    Append(_sets[(variant, 0)].Buffer, t, a, b, 0, 0);
                }
                if (d > bands.Near - FadeWidth - margin)
                {
                    Append(_sets[(variant, 1)].Buffer, t, a, b, 0, 0);
                }
                count++;
            }
        }
        foreach (Set set in _sets.Values)
        {
            Fill(set);
        }
        MeshInstances = count;
    }

    // Two stable random numbers per object, for its colour.
    private static (float A, float B) Shade(in ScatterItem item)
    {
        ulong h = Hash64.Of((ulong)(uint)item.Index, item.Variant, 0x54494E54); // "TINT"
        return ((h & 0xFFFF) / 65535f, ((h >> 16) & 0xFFFF) / 65535f);
    }

    // Look-dev: HU_VEG_SKIP=lod0,lod1,impostor leaves those levels out.
    private static readonly string DebugSkip = OS.GetEnvironment("HU_VEG_SKIP");

    private void Fill(Set set)
    {
        int n = set.Buffer.Count / 16;
        if (DebugSkip.Contains("lod" + set.Lod))
        {
            n = 0;
        }
        if (n == 0)
        {
            if (set.Instance != null)
            {
                set.Instance.Visible = false;
            }
            return;
        }
        if (set.Instance == null)
        {
            FloraLibrary.Bands b = set.Species.Bands;
            bool lastLevel = set.Lod == 1;
            set.Instance = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true,
                    Mesh = set.Lod == 0 ? set.Variant.Lod0 : set.Variant.Lod1,
                },
                // Trees: the second mesh only casts shadows, out to where impostors take over drawing it.
                CastShadow = set.Lod == 0 ? GeometryInstance3D.ShadowCastingSetting.On
                    : set.Species.Tree ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            Vector4 band = set.Lod == 0
                ? new Vector4(0, 0, b.Near - FadeWidth, b.Near)
                : b.Impostor
                    ? new Vector4(b.Near - FadeWidth, b.Near, ImpostorStart, ImpostorStart + 20)
                    : new Vector4(b.Near - FadeWidth, b.Near, b.Far * 0.82f, b.Far);
            set.Instance.SetInstanceShaderParameter("lod_band", band);
            set.Instance.SetInstanceShaderParameter("shrink", lastLevel && !b.Impostor ? 1.0f : 0.0f);
            AddChild(set.Instance);
        }
        set.Instance.Multimesh.InstanceCount = n;
        set.Instance.Multimesh.Buffer = set.Buffer.ToArray();
        set.Instance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
        set.Instance.Visible = true;
    }

    // Instance transform in render axes relative to an anchor: the object's up is the planet's up there.
    private Transform3D InstanceTransform(in ScatterItem item, Vector3D body, Vector3D anchor, float scale, float sink)
    {
        Vector3 up = _frame.DirectionToRender(item.Direction);
        Vector3 reference = Math.Abs(up.Z) < 0.9f ? Vector3.Back : Vector3.Right;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = x.Cross(up);
        var basis = new Basis(x, up, z).Rotated(up, item.Yaw).Scaled(new Vector3(scale, scale, scale));
        Vector3 origin = _frame.DirectionToRender(body - anchor) + up * sink;
        return new Transform3D(basis, origin);
    }

    private static void Append(List<float> buffer, Transform3D t, float c0, float c1, float c2, float c3)
    {
        Basis b = t.Basis;
        buffer.Add(b.X.X); buffer.Add(b.Y.X); buffer.Add(b.Z.X); buffer.Add(t.Origin.X);
        buffer.Add(b.X.Y); buffer.Add(b.Y.Y); buffer.Add(b.Z.Y); buffer.Add(t.Origin.Y);
        buffer.Add(b.X.Z); buffer.Add(b.Y.Z); buffer.Add(b.Z.Z); buffer.Add(t.Origin.Z);
        buffer.Add(c0);
        buffer.Add(c1);
        buffer.Add(c2);
        buffer.Add(c3);
    }

    // ---------------------------------------------------------------- impostor blocks

    private void UpdateBlocks(ZoneFrame zone, Vector3D ground)
    {
        double blockEdge = TileKey.EdgeLength(BlockLevel, _surface.Radius);
        double step = blockEdge / 3;
        int steps = (int)Math.Ceiling((ImpostorEnd + blockEdge) / step);
        var wanted = new HashSet<ulong>();
        int impostors = 0;
        for (int j = -steps; j <= steps; j++)
        {
            for (int i = -steps; i <= steps; i++)
            {
                TileKey key = TileKey.At(zone.ToBody(i * step, 0, j * step), BlockLevel);
                double d = Vector3D.Distance(key.Centre * _surface.Radius, ground);
                if (d > ImpostorEnd + blockEdge || !wanted.Add(key.Packed))
                {
                    continue;
                }
                if (!_blocks.TryGetValue(key.Packed, out Block? block))
                {
                    block = new Block { Key = key, Anchor = key.Centre * _surface.Radius };
                    _blocks[key.Packed] = block;
                }
                if (!block.Built)
                {
                    TryBuildBlock(block, d);
                }
                impostors += block.Instance?.Multimesh.InstanceCount ?? 0;
            }
        }
        var stale = new List<ulong>();
        foreach ((ulong key, Block block) in _blocks)
        {
            if (!wanted.Contains(key))
            {
                stale.Add(key);
                block.Instance?.QueueFree();
            }
        }
        foreach (ulong key in stale)
        {
            _blocks.Remove(key);
        }
        ImpostorInstances = impostors;
    }

    private void TryBuildBlock(Block block, double distance)
    {
        int span = 1 << (Scatter.Level - BlockLevel);
        var tiles = new List<TileKey>(span * span);
        bool ready = true;
        for (int y = 0; y < span; y++)
        {
            for (int x = 0; x < span; x++)
            {
                var key = new TileKey(block.Key.Face, Scatter.Level, block.Key.X * span + x, block.Key.Y * span + y);
                tiles.Add(key);
                if (!_trees.ContainsKey(key.Packed) && !_full.ContainsKey(key.Packed))
                {
                    ready = false;
                    Request(key, full: false, priority: distance);
                }
            }
        }
        if (!ready)
        {
            return;
        }
        var buffer = new List<float>();
        foreach (TileKey key in tiles)
        {
            List<ScatterItem>? items = _full.TryGetValue(key.Packed, out var f) ? f : _trees.TryGetValue(key.Packed, out var t) ? t : null;
            if (items == null)
            {
                continue;
            }
            foreach (ScatterItem item in items)
            {
                FloraLibrary.Species? species = _library!.For(item.Kind);
                if (species == null || !species.Tree)
                {
                    continue;
                }
                FloraLibrary.Variant variant = species.Variants[item.Variant % species.Variants.Count];
                if (variant.ImpostorRow < 0)
                {
                    continue;
                }
                float scale = item.Scale * species.Scale;
                Transform3D tr = InstanceTransform(item, item.Position(_surface.Radius), block.Anchor, scale, -0.05f);
                (float a, _) = Shade(item);
                Append(buffer, tr, a, variant.ImpostorRow, variant.Extent, variant.Height);
            }
        }
        if (buffer.Count > 0 && !DebugSkip.Contains("impostor"))
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = _quad,
                InstanceCount = buffer.Count / 16,
                Buffer = buffer.ToArray(),
            };
            double edge = TileKey.EdgeLength(BlockLevel, _surface.Radius);
            float half = (float)(edge * 0.75 + 60);
            mm.CustomAabb = new Aabb(new Vector3(-half, -half, -half), new Vector3(2 * half, 2 * half, 2 * half));
            block.Instance = new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = _impostors,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Transform = new Transform3D(Basis.Identity, _frame.ToRender(block.Anchor)),
            };
            AddChild(block.Instance);
        }
        block.Built = true;
    }

    /// <summary>Re-anchors everything after the render origin moved.</summary>
    public void Reanchor()
    {
        foreach (Set set in _sets.Values)
        {
            if (set.Instance != null)
            {
                set.Instance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
            }
        }
        foreach (Block block in _blocks.Values)
        {
            if (block.Instance != null)
            {
                block.Instance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(block.Anchor));
            }
        }
    }

    /// <summary>A queue of jobs taken lowest priority value first.</summary>
    private sealed class BlockingQueue
    {
        private readonly List<(double Priority, Action Job)> _items = new();
        private readonly object _lock = new();
        private bool _stopped;

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        public void Add(double priority, Action job)
        {
            lock (_lock)
            {
                _items.Add((priority, job));
                Monitor.Pulse(_lock);
            }
        }

        public Action? Take()
        {
            lock (_lock)
            {
                while (_items.Count == 0 && !_stopped)
                {
                    Monitor.Wait(_lock);
                }
                if (_stopped)
                {
                    return null;
                }
                int best = 0;
                for (int i = 1; i < _items.Count; i++)
                {
                    if (_items[i].Priority < _items[best].Priority)
                    {
                        best = i;
                    }
                }
                Action job = _items[best].Job;
                _items.RemoveAt(best);
                return job;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _stopped = true;
                Monitor.PulseAll(_lock);
            }
        }
    }
}
