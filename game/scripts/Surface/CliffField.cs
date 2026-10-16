using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>Scanned geology at the authoritative cliff placements, streamed separately from harvestable nodes.</summary>
public sealed partial class CliffField : Node3D
{
    private const double Reach = 340;
    private readonly Dictionary<(TileKey, int), Node3D> _instances = new();
    private readonly Queue<CliffPlacement> _pending = new();
    private readonly Dictionary<(TileKey, int), CliffPlacement> _placements = new();
    private sealed class Model(string path)
    {
        public string NearPath = path + "_lod0.gltf", FarPath = path + "_lod1.gltf";
        public PackedScene? Near, Far;
        public bool Requested, Failed;
    }
    private readonly List<Model> _models = new();
    private CliffPlacements _source = null!;
    private RenderFrame _frame = null!;
    private Vector3D _centre = new(double.MaxValue, 0, 0);
    private Task<List<CliffPlacement>>? _job;
    public bool IsIdle => _job == null && _pending.Count == 0;
    public int Count => _instances.Count;

    public void Setup(CliffPlacements source, RenderFrame frame)
    {
        _source = source;
        _frame = frame;
        foreach (CliffGeometry model in CliffGeometry.All)
        {
            string path = "res://assets/surface/cliffs/" + model.Key;
            _models.Add(new Model(path));
        }
    }

    public void Follow(Vector3D camera)
    {
        if (_job is { IsCompleted: true })
        {
            if (_job.IsCompletedSuccessfully)
            {
                List<CliffPlacement> desired = _job.Result;
                var keep = desired.Select(p => p.Key).ToHashSet();
                foreach (var key in _instances.Keys.Where(key => !keep.Contains(key)).ToArray())
                {
                    _instances[key].QueueFree();
                    _instances.Remove(key);
                    _placements.Remove(key);
                }
                _pending.Clear();
                foreach (CliffPlacement cliff in desired)
                    if (!_instances.ContainsKey(cliff.Key)) _pending.Enqueue(cliff);
            }
            else GD.PushError($"Cliff placement failed: {_job.Exception}");
            _job = null;
        }
        if (_job == null && Vector3D.Distance(camera, _centre) > 24)
        {
            _centre = camera;
            _job = Task.Run(() => _source.Near(camera, Reach));
        }
        // Keep resource instantiation bounded; shader compilation/import happens in the normal project warm-up.
        for (int i = 0; i < 2 && _pending.TryPeek(out CliffPlacement cliff); i++)
        {
            Model model = _models[cliff.Model];
            if (!ModelReady(model))
            {
                if (model.Failed) _pending.Dequeue();
                break;
            }
            _pending.Dequeue();
            var root = new Node3D { Name = "Cliff" };
            root.AddChild(model.Near!.Instantiate<Node3D>());
            root.AddChild(model.Far!.Instantiate<Node3D>());
            AddChild(root);
            _instances.Add(cliff.Key, root);
            _placements.Add(cliff.Key, cliff);
        }
        foreach (var (key, instance) in _instances)
        {
            CliffPlacement cliff = _placements[key];
            var basis = new Basis(_frame.DirectionToRender(cliff.Right), _frame.DirectionToRender(cliff.Up),
                _frame.DirectionToRender(cliff.Forward)).Scaled(Vector3.One * (float)cliff.Scale);
            instance.Transform = new Transform3D(basis, _frame.ToRender(cliff.Position));
            bool near = Vector3D.Distance(camera, cliff.Position) < 95;
            instance.GetChild<Node3D>(0).Visible = near;
            instance.GetChild<Node3D>(1).Visible = !near;
        }
    }

    // Loading every 4K scan synchronously while a client joins can starve its network pump.
    // Request only nearby families, then keep frames/network processing while their shared maps load.
    private static bool ModelReady(Model model)
    {
        if (model.Failed) return false;
        if (model.Near != null && model.Far != null) return true;
        if (!model.Requested)
        {
            model.Requested = true;
            if (ResourceLoader.LoadThreadedRequest(model.NearPath, "PackedScene") != Error.Ok ||
                ResourceLoader.LoadThreadedRequest(model.FarPath, "PackedScene") != Error.Ok)
            {
                model.Failed = true;
                GD.PushError("Cannot load scanned cliff family: " + model.NearPath);
                return false;
            }
        }
        foreach (string path in new[] { model.NearPath, model.FarPath })
        {
            ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
            {
                model.Failed = true;
                GD.PushError("Failed to stream scanned cliff: " + path);
                return false;
            }
            if (status != ResourceLoader.ThreadLoadStatus.Loaded) return false;
        }
        model.Near = (PackedScene)ResourceLoader.LoadThreadedGet(model.NearPath);
        model.Far = (PackedScene)ResourceLoader.LoadThreadedGet(model.FarPath);
        return true;
    }
}
