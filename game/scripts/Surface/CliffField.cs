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
    private readonly Dictionary<TileKey, Node3D> _instances = new();
    private readonly Queue<CliffPlacement> _pending = new();
    private readonly Dictionary<TileKey, CliffPlacement> _placements = new();
    private readonly List<(PackedScene Near, PackedScene Far)> _models = new();
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
            _models.Add((GD.Load<PackedScene>(path + "_lod0.glb"), GD.Load<PackedScene>(path + "_lod1.glb")));
        }
    }

    public void Follow(Vector3D camera)
    {
        if (_job is { IsCompleted: true })
        {
            if (_job.IsCompletedSuccessfully)
            {
                List<CliffPlacement> desired = _job.Result;
                var keep = desired.Select(p => p.Tile).ToHashSet();
                foreach (TileKey key in _instances.Keys.Where(key => !keep.Contains(key)).ToArray())
                {
                    _instances[key].QueueFree();
                    _instances.Remove(key);
                    _placements.Remove(key);
                }
                _pending.Clear();
                foreach (CliffPlacement cliff in desired)
                    if (!_instances.ContainsKey(cliff.Tile)) _pending.Enqueue(cliff);
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
        for (int i = 0; i < 2 && _pending.TryDequeue(out CliffPlacement cliff); i++)
        {
            var root = new Node3D { Name = "Cliff" };
            (PackedScene near, PackedScene far) = _models[cliff.Model];
            root.AddChild(near.Instantiate<Node3D>());
            root.AddChild(far.Instantiate<Node3D>());
            AddChild(root);
            _instances.Add(cliff.Tile, root);
            _placements.Add(cliff.Tile, cliff);
        }
        foreach ((TileKey key, Node3D instance) in _instances)
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
}
