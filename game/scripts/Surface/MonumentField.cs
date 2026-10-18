using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>The live world's original monument assemblies, at the same placements used by loot and physics.</summary>
public sealed partial class MonumentField : Node3D
{
    private const double Reach = 850;
    private readonly Dictionary<TileKey, Node3D> _instances = new();
    private readonly Dictionary<string, PackedScene> _models = new();
    private readonly Queue<MonumentPlacement> _pending = new();
    private MonumentPlacements _source = null!;
    private RenderFrame _frame = null!;
    private Vector3D _centre = new(double.MaxValue, 0, 0);
    private bool _stopped;
    public bool IsIdle => !_stopped && _pending.Count == 0;

    public void Setup(MonumentPlacements source, RenderFrame frame) => (_source, _frame) = (source, frame);

    public void Follow(Vector3D camera)
    {
        if (_stopped) return;
        if (Vector3D.Distance(camera, _centre) > 30)
        {
            _centre = camera;
            var nearby = _source.All.Where(p => Vector3D.Distance(camera, p.Position) < Reach).ToArray();
            var keep = nearby.Select(p => p.Tile).ToHashSet();
            foreach (TileKey key in _instances.Keys.Where(k => !keep.Contains(k)).ToArray())
            {
                _instances[key].QueueFree();
                _instances.Remove(key);
            }
            _pending.Clear();
            foreach (MonumentPlacement site in nearby)
                if (!_instances.ContainsKey(site.Tile)) _pending.Enqueue(site);
        }
        if (_pending.TryDequeue(out MonumentPlacement next))
        {
            var root = new Node3D { Name = next.Definition.Key };
            AddModel(root, next.Definition.Model, Vector3.Zero, Quaternion.Identity);
            foreach (MonumentPiece piece in next.Definition.Pieces)
                AddModel(root, $"res://assets/monuments/kit/{piece.Key}.glb", new Vector3((float)piece.At.X, (float)piece.At.Y, (float)piece.At.Z),
                    new Quaternion((float)piece.X, (float)piece.Y, (float)piece.Z, (float)piece.W).Normalized());
            AddChild(root);
            _instances.Add(next.Tile, root);
        }
        foreach (MonumentPlacement site in _source.All)
            if (_instances.TryGetValue(site.Tile, out Node3D? instance))
                instance.Transform = new Transform3D(new Basis(_frame.DirectionToRender(site.Right), _frame.DirectionToRender(site.Up),
                    _frame.DirectionToRender(site.Forward)), _frame.ToRender(site.Position));
    }

    /// <summary>Main-thread only: monuments perform no background queries and release their source immediately.</summary>
    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _pending.Clear();
        _source = null!;
        _frame = null!;
        _models.Clear();
        _instances.Clear();
        Visible = false;
    }

    public override void _ExitTree() => Stop();

    private void AddModel(Node3D parent, string path, Vector3 position, Quaternion rotation)
    {
        if (!_models.TryGetValue(path, out PackedScene? scene)) _models[path] = scene = GD.Load<PackedScene>(path);
        var model = scene.Instantiate<Node3D>();
        model.Position = position;
        model.Quaternion = rotation;
        Configure(model, path.StartsWith("res://assets/revised/", System.StringComparison.Ordinal));
        parent.AddChild(model);
    }

    private static void Configure(Node root, bool native)
    {
        // Shared engine-free collision is authoritative; native scene collision must not create a second world.
        if (root is CollisionObject3D collider) { collider.CollisionLayer = 0; collider.CollisionMask = 0; }
        if (root is GeometryInstance3D mesh)
        {
            mesh.SetInstanceShaderParameter("piece_tint", Colors.White);
            string name = mesh.Name.ToString().ToLowerInvariant();
            int lod = name.EndsWith("_lod0") ? 0 : name.EndsWith("_lod1") ? 1 : name.EndsWith("_lod2") ? 2 : -1;
            if (lod >= 0 && !native)
            {
                mesh.VisibilityRangeBegin = lod == 0 ? 0 : lod == 1 ? 70 : 230;
                mesh.VisibilityRangeEnd = lod == 0 ? 70 : lod == 1 ? 230 : 900;
                mesh.VisibilityRangeBeginMargin = 3;
                mesh.VisibilityRangeEndMargin = 3;
            }
        }
        foreach (Node child in root.GetChildren()) Configure(child, native);
    }
}
