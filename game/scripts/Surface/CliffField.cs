using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>Scanned geology at authoritative placements, streamed separately from harvestable nodes.</summary>
public sealed partial class CliffField : Node3D
{
    private const double Reach = 340, RefreshDistance = 24, NearDistance = 95, LodMargin = 4;
    private const int MaximumInstantiations = 2, MaximumPendingChecks = 32;
    private const ulong StreamBudgetMicroseconds = 1500;
    private sealed class Model(string path)
    {
        public readonly string NearPath = path + "_lod0.gltf", FarPath = path + "_lod1.gltf";
        public PackedScene? Near, Far;
        public bool Requested, Failed, Measured;
        public Aabb Bounds;
        public int NearTriangles, FarTriangles;
        public bool Ready => Near != null && Far != null;
    }
    private sealed class Instance(CliffPlacement placement, Model model, Node3D root, Node3D near, Node3D far)
    {
        public readonly CliffPlacement Placement = placement;
        public readonly Model Model = model;
        public readonly Node3D Root = root, Near = near, Far = far;
        public bool NearActive;
    }
    private readonly Dictionary<(TileKey, int), Instance> _instances = new();
    private readonly Queue<CliffPlacement> _pending = new();
    private readonly List<Model> _models = new();
    private CliffPlacements _source = null!;
    private RenderFrame _frame = null!;
    private Vector3D _centre = new(double.MaxValue, 0, 0);
    private Vector3D _lastCamera = new(double.MaxValue, 0, 0), _lastOrigin;
    private Task<List<CliffPlacement>>? _job;
    private bool _queried, _stopped;
    public Task WorkersStopped { get; private set; } = Task.CompletedTask;
    public string? Failure { get; private set; }
    public bool IsIdle => _queried && !_stopped && Failure == null && _job == null && _pending.Count == 0;
    public int Count => _instances.Count;
    public int PendingCount => _pending.Count;
    public int NearCount { get; private set; }
    public int FarCount => Count - NearCount;
    /// <summary>Enabled LOD triangles before frustum/occlusion culling, not a GPU draw counter.</summary>
    public long SelectedLodTriangles { get; private set; }
    public ulong LastFollowMicroseconds { get; private set; }
    public ulong LastStreamMicroseconds { get; private set; }
    public long InstantiationCount { get; private set; }
    public long TransformWriteCount { get; private set; }
    public long LodSwitchCount { get; private set; }
    public int FailedModelCount => _models.Count(model => model.Failed);

    public void Setup(CliffPlacements source, RenderFrame frame)
    {
        if (_models.Count > 0) throw new InvalidOperationException("CliffField is already configured.");
        _source = source;
        _frame = frame;
        _lastOrigin = frame.Origin;
        foreach (CliffGeometry model in CliffGeometry.All)
            _models.Add(new Model("res://assets/surface/cliffs/" + model.Key));
    }

    public void Follow(Vector3D camera)
    {
        if (_stopped || Failure != null) return;
        ulong began = Time.GetTicksUsec();
        if (_job is { IsCompleted: true })
        {
            if (_job.IsCompletedSuccessfully)
            {
                // Do not install a stale teleport destination while its replacement query runs.
                if (Vector3D.Distance(camera, _centre) < Reach)
                {
                    ApplyDesired(_job.Result, camera);
                    _queried = true;
                }
            }
            else Fail("Cliff placement failed: " + _job.Exception);
            _job = null;
        }
        if (Failure != null) return;
        if (_job == null && Vector3D.Distance(camera, _centre) > RefreshDistance)
        {
            if (Vector3D.Distance(camera, _centre) >= Reach) _pending.Clear();
            _centre = camera;
            // The worker captures only the engine-free source, never this scene node.
            CliffPlacements source = _source;
            _job = Task.Run(() => source.Near(camera, Reach));
        }
        bool moved = camera != _lastCamera, rebased = _frame.Origin != _lastOrigin;
        if (moved || rebased)
        {
            foreach (Instance instance in _instances.Values)
            {
                // RenderFrame axes are fixed; the ground game's world frame never moves.
                if (rebased)
                {
                    instance.Root.Position = _frame.ToRender(instance.Placement.Position);
                    TransformWriteCount++;
                }
                if (moved) UpdateLod(instance, camera);
            }
            _lastCamera = camera;
            _lastOrigin = _frame.Origin;
        }
        foreach (Model model in _models)
            if (model.Requested && !model.Ready && !model.Failed) PollModel(model);
        if (Failure == null) StreamPending(camera);
        LastFollowMicroseconds = Time.GetTicksUsec() - began;
    }

    private void ApplyDesired(List<CliffPlacement> desired, Vector3D camera)
    {
        var keep = desired.Select(p => p.Key).ToHashSet();
        foreach (var key in _instances.Keys.Where(key => !keep.Contains(key)).ToArray())
        {
            Instance instance = _instances[key];
            if (instance.NearActive) NearCount--;
            SelectedLodTriangles -= instance.NearActive ? instance.Model.NearTriangles : instance.Model.FarTriangles;
            instance.Root.QueueFree();
            _instances.Remove(key);
        }
        _pending.Clear();
        // Start all needed families together, prioritizing nearby parts without changing collision order.
        foreach (CliffPlacement cliff in desired.OrderBy(p => (p.Position - camera).LengthSquared))
        {
            if (_instances.ContainsKey(cliff.Key)) continue;
            Model model = _models[cliff.Model];
            RequestModel(model);
            if (!model.Failed) _pending.Enqueue(cliff);
        }
    }

    private void StreamPending(Vector3D camera)
    {
        ulong began = Time.GetTicksUsec();
        int checkedCount = Math.Min(_pending.Count, MaximumPendingChecks), created = 0;
        while (checkedCount-- > 0 && created < MaximumInstantiations && _pending.TryDequeue(out CliffPlacement cliff))
        {
            Model model = _models[cliff.Model];
            if (model.Failed) continue;
            if (!model.Ready) { _pending.Enqueue(cliff); continue; }
            var root = new Node3D { Name = "Cliff" };
            bool installed = false;
            try
            {
                Node3D near = model.Near!.Instantiate<Node3D>();
                root.AddChild(near);
                Node3D far = model.Far!.Instantiate<Node3D>();
                root.AddChild(far);
                if (!model.Measured)
                {
                    Aabb? nearBounds = null, farBounds = null;
                    Measure(near, Transform3D.Identity, ref nearBounds, ref model.NearTriangles);
                    Measure(far, Transform3D.Identity, ref farBounds, ref model.FarTriangles);
                    model.Bounds = (nearBounds ?? throw new InvalidOperationException("No mesh: " + model.NearPath))
                        .Merge(farBounds ?? throw new InvalidOperationException("No mesh: " + model.FarPath));
                    model.Measured = true;
                }
                var basis = new Basis(_frame.DirectionToRender(cliff.Right), _frame.DirectionToRender(cliff.Up),
                    _frame.DirectionToRender(cliff.Forward)).Scaled(Vector3.One * (float)cliff.Scale);
                root.Transform = new Transform3D(basis, _frame.ToRender(cliff.Position));
                var instance = new Instance(cliff, model, root, near, far)
                    { NearActive = DistanceSquared(model.Bounds, cliff, camera) < NearDistance * NearDistance };
                near.Visible = instance.NearActive;
                far.Visible = !instance.NearActive;
                AddChild(root);
                _instances.Add(cliff.Key, instance);
                installed = true;
                if (instance.NearActive) NearCount++;
                SelectedLodTriangles += instance.NearActive ? model.NearTriangles : model.FarTriangles;
                InstantiationCount++;
                TransformWriteCount++;
                created++;
            }
            catch (Exception error)
            {
                model.Failed = true;
                Fail("Cannot instantiate scanned cliff " + model.NearPath + ": " + error);
                break;
            }
            finally { if (!installed) root.Free(); }
            // An instantiation cannot be preempted; do not start a second after exhausting this budget.
            if (Time.GetTicksUsec() - began >= StreamBudgetMicroseconds) break;
        }
        LastStreamMicroseconds = Time.GetTicksUsec() - began;
    }

    private void UpdateLod(Instance instance, Vector3D camera)
    {
        // Extent distance and hysteresis, retaining the source's PBR materials and sidedness.
        double boundary = NearDistance + (instance.NearActive ? LodMargin : -LodMargin);
        bool near = DistanceSquared(instance.Model.Bounds, instance.Placement, camera) < boundary * boundary;
        if (near == instance.NearActive) return;
        int change = instance.Model.NearTriangles - instance.Model.FarTriangles;
        SelectedLodTriangles += near ? change : -change;
        NearCount += near ? 1 : -1;
        instance.NearActive = near;
        instance.Near.Visible = near;
        instance.Far.Visible = !near;
        LodSwitchCount++;
    }

    private static double DistanceSquared(Aabb bounds, CliffPlacement cliff, Vector3D camera)
    {
        Vector3D offset = (camera - cliff.Position) / cliff.Scale;
        double x = Vector3D.Dot(offset, cliff.Right), y = Vector3D.Dot(offset, cliff.Up), z = Vector3D.Dot(offset, cliff.Forward);
        Vector3 min = bounds.Position, max = bounds.End;
        x -= Math.Clamp(x, min.X, max.X);
        y -= Math.Clamp(y, min.Y, max.Y);
        z -= Math.Clamp(z, min.Z, max.Z);
        return (x * x + y * y + z * z) * cliff.Scale * cliff.Scale;
    }

    private static void Measure(Node node, Transform3D parent, ref Aabb? bounds, ref int triangles)
    {
        Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
        if (node is MeshInstance3D instance && instance.Mesh is { } mesh)
        {
            Aabb local = transform * mesh.GetAabb();
            bounds = bounds is { } existing ? existing.Merge(local) : local;
            if (mesh is ArrayMesh array)
                for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
                    if (array.SurfaceGetPrimitiveType(surface) == Mesh.PrimitiveType.Triangles)
                    {
                        int indices = array.SurfaceGetArrayIndexLen(surface);
                        triangles += (indices > 0 ? indices : array.SurfaceGetArrayLen(surface)) / 3;
                    }
        }
        foreach (Node child in node.GetChildren()) Measure(child, transform, ref bounds, ref triangles);
    }

    private void RequestModel(Model model)
    {
        if (model.Requested || model.Failed) return;
        model.Requested = true;
        Error near = ResourceLoader.LoadThreadedRequest(model.NearPath, "PackedScene");
        Error far = ResourceLoader.LoadThreadedRequest(model.FarPath, "PackedScene");
        if (near != Error.Ok || far != Error.Ok)
        {
            model.Failed = true;
            Fail($"Cannot load scanned cliff family: {model.NearPath} ({near}, {far})");
        }
    }

    private void PollModel(Model model)
    {
        ResourceLoader.ThreadLoadStatus near = ResourceLoader.LoadThreadedGetStatus(model.NearPath);
        ResourceLoader.ThreadLoadStatus far = ResourceLoader.LoadThreadedGetStatus(model.FarPath);
        if (near is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource ||
            far is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
        {
            model.Failed = true;
            Fail($"Failed to stream scanned cliff: {model.NearPath} ({near}, {far})");
            return;
        }
        if (near != ResourceLoader.ThreadLoadStatus.Loaded || far != ResourceLoader.ThreadLoadStatus.Loaded) return;
        model.Near = (PackedScene)ResourceLoader.LoadThreadedGet(model.NearPath);
        model.Far = (PackedScene)ResourceLoader.LoadThreadedGet(model.FarPath);
    }

    private void Fail(string message) { Failure ??= message; GD.PushError(message); }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        Visible = false;
        _pending.Clear();
        if (_job is { } job)
        {
            WorkersStopped = job;
            // Observe worker faults after teardown without touching Godot from a worker.
            _ = job.ContinueWith(failed => { _ = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            _job = null;
        }
        _source = null!;
        _frame = null!;
    }

    public override void _ExitTree() => Stop();
}
