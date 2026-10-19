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
/// The far layer (docs/TERRAIN_SCENERY_PLAN.md T2): every formation the near layer has not installed, drawn as one
/// MultiMesh per model in each chunk of the cube sphere, so a cliff's silhouette never drops out at the near layer's
/// reach, walking or flying. The world's cells are built once, nearest the camera first, on two low-priority threads
/// and outside the shared formation cache. A model's far mesh is its LOD1 scan with Godot's generated LOD chain, so the
/// screen size of a chunk picks its detail (Godot gives a MultiMesh one LOD, from its box). Chunks wholly behind the
/// planet's horizon are hidden. A piece is drawn by exactly one layer: its chunk rebuilds in the frame the near layer
/// installs or frees it. The far layer casts no shadows: from 300 m up its shadow passes cost about 2.3 ms of GPU
/// time, drawing it about 0.4 ms (ENVIRONMENT_BASELINE.md); a piece's shadow is the near layer's, out to its reach.
/// </summary>
public sealed partial class CliffField
{
    private const double FarChunkMetres = 240, FarReadyReach = 2 * Reach, FarRefocusMetres = 120;
    private const int FarThreads = 2;

    private sealed class FarChunk(Vector3D anchor, int models)
    {
        /// <summary>On the mean sphere under the chunk's centre; batches are placed relative to it.</summary>
        public readonly Vector3D Anchor = anchor;
        public readonly List<CliffPlacement> Parts = new();
        public readonly MultiMeshInstance3D?[] Batches = new MultiMeshInstance3D?[models];
        public Node3D? Node;
        public bool Dirty, Shown = true;
        /// <summary>The widest angle from the anchor to any part's edge, and the farthest a part reaches from the centre.</summary>
        public double Angle, TopRadius;
        public int Drawn;
    }

    /// <summary>Builds every cell once, on threads of its own; the nearest to the latest focus first.</summary>
    private sealed class FarSweep
    {
        private readonly CliffPlacements _source;
        private readonly Vector3D[] _centres;
        private readonly double _refocus;
        private readonly object _gate = new();
        private readonly List<int> _remaining;
        private Vector3D _focus, _sortedFor;
        private volatile bool _stopped;
        public readonly ConcurrentQueue<(int Cell, CliffPlacement[] Parts)> Results = new();
        public volatile Exception? Error;
        public readonly Task Finished;

        public FarSweep(CliffPlacements source, Vector3D[] centres, Vector3D focus)
        {
            _source = source;
            _centres = centres;
            _refocus = FarRefocusMetres / source.Surface.Radius;
            _remaining = new List<int>(centres.Length);
            for (int i = 0; i < centres.Length; i++) _remaining.Add(i);
            _focus = focus;
            Sort();
            var finished = new Task[FarThreads];
            for (int i = 0; i < FarThreads; i++)
            {
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(() =>
                {
                    try { Run(); }
                    catch (Exception error) { Error ??= error; }
                    finally { done.SetResult(); }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Cliff far sweep" };
                thread.Start();
                finished[i] = done.Task;
            }
            Finished = Task.WhenAll(finished);
        }

        public void Focus(Vector3D direction) { lock (_gate) _focus = direction; }
        public void Stop() => _stopped = true;

        private void Run()
        {
            while (!_stopped && Take() is int cell)
                Results.Enqueue((cell, _source.Generate(TileKey.FromIndex(_source.Level, cell)!.Value)));
        }

        private int? Take()
        {
            lock (_gate)
            {
                if (_remaining.Count == 0) return null;
                if ((_focus - _sortedFor).Length > _refocus) Sort();
                int cell = _remaining[^1];
                _remaining.RemoveAt(_remaining.Count - 1);
                return cell;
            }
        }

        // Farthest first, so the nearest is taken from the end.
        private void Sort()
        {
            Vector3D focus = _sortedFor = _focus;
            _remaining.Sort((a, b) => Vector3D.Dot(_centres[a], focus).CompareTo(Vector3D.Dot(_centres[b], focus)));
        }
    }

    private readonly Dictionary<TileKey, FarChunk> _farChunks = new();
    private readonly List<FarChunk> _farDirty = new();
    private FarSweep? _sweep;
    private Node3D? _farRoot;
    private Vector3D[] _farCentres = Array.Empty<Vector3D>();
    private bool[] _farDone = Array.Empty<bool>(), _farRequired = Array.Empty<bool>();
    private int _farLevel, _farMissing, _farSwept;
    private ulong _farStarted;
    private double _occluder;
    /// <summary>`--no-far-cliffs`: the near layer alone, as before the far layer, for comparisons.</summary>
    private bool _farOff;
    private Vector3D _farRequiredFor = new(double.MaxValue, 0, 0);

    /// <summary>Cells the far sweep has built, of <see cref="FarCellCount"/>.</summary>
    public int FarSweptCells => _farSwept;
    public int FarCellCount => _farCentres.Length;
    public bool FarSweepComplete => _farCentres.Length > 0 && _farSwept == _farCentres.Length;
    public int FarChunkCount => _farChunks.Count;
    public int FarShownChunks { get; private set; }
    /// <summary>Far pieces in shown chunks (each chunk at the LOD Godot picks for it).</summary>
    public int FarShownPieces { get; private set; }
    private bool _farShadows;

    /// <summary>Whether the far layer casts shadows (off; the near layer's pieces always do): the performance breakdown's diagnostic.</summary>
    public bool FarShadows
    {
        get => _farShadows;
        set
        {
            _farShadows = value;
            foreach (FarChunk chunk in _farChunks.Values)
                foreach (MultiMeshInstance3D? batch in chunk.Batches)
                    if (batch != null) batch.CastShadow = Shadows;
        }
    }

    private GeometryInstance3D.ShadowCastingSetting Shadows =>
        _farShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
    public long FarRebuildCount { get; private set; }
    public ulong LastFarMicroseconds { get; private set; }
    /// <summary>
    /// Idle once every cell within <see cref="FarReadyReach"/> of the camera is built and no chunk waits to be rebuilt;
    /// the rest of the world fills in behind, nearest first, without holding a loading screen.
    /// </summary>
    private bool FarIdle => _farOff || (_sweep != null && _farMissing == 0 && _farDirty.Count == 0);

    private void SetupFar()
    {
        ISurface surface = _source.Surface;
        // The lowest the line of sight can graze: the sea where there is one, else the lowest ground.
        double low = surface is WalkingWorld walking ? walking.HeightBounds.Low : -0.05 * surface.Radius;
        _occluder = Math.Max(surface.Radius + low, surface.SeaRadius) - 2;
        _farLevel = Math.Min(_source.Level, TileKey.LevelFor(surface.Radius, FarChunkMetres));
        _farRoot = new Node3D { Name = "Far" };
        AddChild(_farRoot);
        _farOff = Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-far-cliffs") >= 0;
    }

    private void FollowFar(Vector3D camera, bool moved, bool rebased)
    {
        if (_farOff) return;
        ulong began = Time.GetTicksUsec();
        if (_sweep == null) StartSweep(camera);
        if (_sweep!.Error is { } error)
        {
            Fail("Cliff far sweep failed: " + error);
            return;
        }
        Vector3D focus = camera.Normalized;
        if (moved) _sweep.Focus(focus);
        while (_sweep.Results.TryDequeue(out (int Cell, CliffPlacement[] Parts) built))
        {
            _farDone[built.Cell] = true;
            _farSwept++;
            if (_farRequired[built.Cell]) _farMissing--;
            foreach (CliffPlacement part in built.Parts) AddFar(part);
            if (_farSwept == _farCentres.Length) SweepDone();
        }
        if ((focus - _farRequiredFor).Length * _source.Surface.Radius > RefreshDistance) Require(focus);
        if (rebased)
            foreach (FarChunk chunk in _farChunks.Values)
                if (chunk.Node != null) chunk.Node.Position = _frame.ToRender(chunk.Anchor);
        PrepareFarMesh();
        for (int i = _farDirty.Count - 1; i >= 0; i--)
        {
            FarChunk chunk = _farDirty[i];
            if (Rebuild(chunk)) continue;
            chunk.Dirty = false;
            _farDirty.RemoveAt(i);
        }
        int shown = 0, pieces = 0;
        foreach (FarChunk chunk in _farChunks.Values)
        {
            bool show = OverHorizon(chunk, camera);
            if (show != chunk.Shown && chunk.Node != null) chunk.Node.Visible = show;
            chunk.Shown = show;
            if (show && chunk.Drawn > 0) { shown++; pieces += chunk.Drawn; }
        }
        (FarShownChunks, FarShownPieces) = (shown, pieces);
        LastFarMicroseconds = Time.GetTicksUsec() - began;
    }

    private void StartSweep(Vector3D camera)
    {
        int level = _source.Level;
        var centres = new Vector3D[TileKey.CountAt(level)];
        for (int i = 0; i < centres.Length; i++) centres[i] = TileKey.FromIndex(level, i)!.Value.Centre;
        _farCentres = centres;
        _farDone = new bool[centres.Length];
        _farRequired = new bool[centres.Length];
        _farStarted = Time.GetTicksMsec();
        _sweep = new FarSweep(_source, centres, camera.Normalized);
    }

    private void SweepDone()
    {
        int pieces = 0;
        foreach (FarChunk chunk in _farChunks.Values) pieces += chunk.Parts.Count;
        GD.Print($"[cliffs] far layer: {_farCentres.Length} cells swept in {(Time.GetTicksMsec() - _farStarted) / 1000.0:0.0} s, " +
            $"{pieces} pieces in {_farChunks.Count} chunks");
    }

    // The cells that must be built before the field reports idle: those within FarReadyReach of the camera.
    private void Require(Vector3D focus)
    {
        double radius = _source.Surface.Radius;
        double reach = Math.Cos(Math.Min(Math.PI, (FarReadyReach + TileKey.EdgeLength(_source.Level, radius)) / radius));
        int missing = 0;
        for (int i = 0; i < _farCentres.Length; i++)
        {
            bool required = Vector3D.Dot(_farCentres[i], focus) >= reach;
            _farRequired[i] = required;
            if (required && !_farDone[i]) missing++;
        }
        _farMissing = missing;
        _farRequiredFor = focus;
    }

    private void AddFar(CliffPlacement part)
    {
        TileKey key = part.Tile;
        while (key.Level > _farLevel) key = key.Parent;
        if (!_farChunks.TryGetValue(key, out FarChunk? chunk))
        {
            chunk = new FarChunk(key.Centre * _source.Surface.Radius, _models.Count);
            _farChunks.Add(key, chunk);
        }
        chunk.Parts.Add(part);
        double extent = CliffGeometry.All[part.Model].BoundingRadius * part.Scale, distance = part.Position.Length;
        double angle = Math.Acos(Math.Clamp(Vector3D.Dot(chunk.Anchor.Normalized, part.Position / distance), -1, 1));
        chunk.Angle = Math.Max(chunk.Angle, angle + extent / Math.Max(distance - extent, 1));
        chunk.TopRadius = Math.Max(chunk.TopRadius, distance + extent);
        RequestModel(_models[part.Model]);
        FarDirty(chunk);
    }

    /// <summary>The near layer installed or freed a piece: its chunk draws it, or stops drawing it, this frame.</summary>
    private void FarTouched(CliffPlacement part)
    {
        if (_sweep == null) return;
        TileKey key = part.Tile;
        while (key.Level > _farLevel) key = key.Parent;
        if (_farChunks.TryGetValue(key, out FarChunk? chunk)) FarDirty(chunk);
    }

    private void FarDirty(FarChunk chunk)
    {
        if (chunk.Dirty) return;
        chunk.Dirty = true;
        _farDirty.Add(chunk);
    }

    // Whether any part of the chunk can rise over the planet's curve from the camera (the ground between is taken to be
    // no lower than the occluding sphere: the sea, or the lowest ground).
    private bool OverHorizon(FarChunk chunk, Vector3D camera)
    {
        double eye = camera.Length, occluder = Math.Min(_occluder, eye - 0.5);
        if (occluder <= 0) return true;
        double limit = Math.Acos(occluder / eye) + Math.Acos(Math.Min(1, occluder / chunk.TopRadius));
        double angle = Math.Acos(Math.Clamp(Vector3D.Dot(camera / eye, chunk.Anchor.Normalized), -1, 1));
        return angle - chunk.Angle <= limit;
    }

    // Writes the chunk's pieces the near layer is not drawing into one batch per model. True while a model's far mesh
    // is still being prepared, so the chunk is tried again.
    private bool Rebuild(FarChunk chunk)
    {
        if (chunk.Node == null)
        {
            chunk.Node = new Node3D { Name = "FarChunk", Position = _frame.ToRender(chunk.Anchor), Visible = chunk.Shown };
            _farRoot!.AddChild(chunk.Node);
        }
        Span<int> counts = stackalloc int[_models.Count];
        foreach (CliffPlacement part in chunk.Parts)
            if (!_instances.ContainsKey(part.Key)) counts[part.Model]++;
        bool waiting = false;
        int drawn = 0;
        for (int m = 0; m < _models.Count; m++)
        {
            Model model = _models[m];
            MultiMeshInstance3D? batch = chunk.Batches[m];
            if (counts[m] > 0 && model.FarMesh == null)
            {
                waiting |= !model.Failed && !model.FarFailed;
                counts[m] = 0;
            }
            if (counts[m] == 0)
            {
                if (batch != null) batch.Multimesh.InstanceCount = 0;
                continue;
            }
            if (batch == null)
            {
                batch = new MultiMeshInstance3D
                {
                    Name = model.FarMesh!.ResourceName,
                    Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = model.FarMesh },
                    CastShadow = Shadows,
                };
                chunk.Node.AddChild(batch);
                chunk.Batches[m] = batch;
            }
            var buffer = new float[counts[m] * 12];
            int slot = 0;
            foreach (CliffPlacement part in chunk.Parts)
            {
                if (part.Model != m || _instances.ContainsKey(part.Key)) continue;
                var basis = new Basis(_frame.DirectionToRender(part.Right), _frame.DirectionToRender(part.Up),
                    _frame.DirectionToRender(part.Forward)).Scaled(Vector3.One * (float)part.Scale);
                Transform3D t = new Transform3D(basis, _frame.DirectionToRender(part.Position - chunk.Anchor)) * model.FarLocal;
                // Godot's 3 x 4 rows.
                Span<float> row = buffer.AsSpan(slot++ * 12, 12);
                (row[0], row[1], row[2], row[3]) = (t.Basis.Row0.X, t.Basis.Row0.Y, t.Basis.Row0.Z, t.Origin.X);
                (row[4], row[5], row[6], row[7]) = (t.Basis.Row1.X, t.Basis.Row1.Y, t.Basis.Row1.Z, t.Origin.Y);
                (row[8], row[9], row[10], row[11]) = (t.Basis.Row2.X, t.Basis.Row2.Y, t.Basis.Row2.Z, t.Origin.Z);
            }
            batch.Multimesh.InstanceCount = counts[m];
            batch.Multimesh.Buffer = buffer;
            drawn += counts[m];
        }
        chunk.Drawn = drawn;
        FarRebuildCount++;
        return waiting;
    }

    // One model a frame: its LOD1 scan with a generated LOD chain, the mesh node's own transform in its scene, and the
    // scene's material. A scan the far layer cannot use is left to the near layer alone.
    private void PrepareFarMesh()
    {
        foreach (Model model in _models)
        {
            if (!model.Ready || model.FarMesh != null || model.FarFailed) continue;
            Node3D scene = model.Far!.Instantiate<Node3D>();
            try
            {
                MeshInstance3D? found = null;
                Transform3D local = Transform3D.Identity;
                int meshes = 0;
                Find(scene, Transform3D.Identity, ref found, ref local, ref meshes);
                if (meshes != 1 || found!.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() != 1)
                {
                    model.FarFailed = true;
                    GD.PushWarning($"Cliff far layer skips {model.FarPath}: it needs one single-surface mesh, found {meshes}.");
                    continue;
                }
                ImporterMesh importer = ImporterMesh.FromMesh(mesh);
                importer.GenerateLods(60, 0, new Godot.Collections.Array());
                ArrayMesh far = importer.GetMesh();
                if ((found.MaterialOverride ?? found.GetSurfaceOverrideMaterial(0)) is { } material) far.SurfaceSetMaterial(0, material);
                far.ResourceName = model.FarPath.GetFile().GetBaseName();
                model.FarLocal = local;
                model.FarMesh = far;
            }
            finally { scene.Free(); }
            return;
        }
    }

    private static void Find(Node node, Transform3D parent, ref MeshInstance3D? found, ref Transform3D local, ref int meshes)
    {
        Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
        if (node is MeshInstance3D instance && instance.Mesh != null)
        {
            meshes++;
            found ??= instance;
            if (ReferenceEquals(found, instance)) local = transform;
        }
        foreach (Node child in node.GetChildren()) Find(child, transform, ref found, ref local, ref meshes);
    }

    private void StopFar(List<Task> workers)
    {
        if (_sweep is not { } sweep) return;
        sweep.Stop();
        workers.Add(sweep.Finished);
    }
}
