using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Simulation.Physics;
using Hollow.Surface;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground near the player in Godot's own physics (layer 1), for what Godot simulates: ragdolls of the dead and
/// sleeping, and the characters' feet finding the ground (GROUND.md §4.5: Godot's physics is cosmetic only). The same
/// triangles as the game's collision tiles (<see cref="PhysicsWorld.TileMesh"/>), built on worker threads and added a
/// few a frame.
/// </summary>
public partial class GroundCollision : Node3D
{
    /// <summary>Tiles kept within this many metres of the player.</summary>
    private const float Reach = 140;

    private readonly Dictionary<TileKey, StaticBody3D> _tiles = new();
    private readonly Dictionary<TileKey, Task<Vector3[]>> _building = new();
    private readonly List<TileKey> _near = new();
    private bool _stopped;
    public Task WorkersStopped { get; private set; } = Task.CompletedTask;

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        WorkersStopped = Task.WhenAll(_building.Values.Cast<Task>());
        _ = WorkersStopped.ContinueWith(static task => { _ = task.Exception; },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        _building.Clear();
        _near.Clear();
        foreach (StaticBody3D tile in _tiles.Values) tile.QueueFree();
        _tiles.Clear();
    }

    public override void _ExitTree() => Stop();

    public void Follow(PhysicsWorld world, Num.Vector3 feet)
    {
        if (_stopped) return;
        _near.Clear();
        TileKey.Around(world.Frame.ToBody(feet), Reach, world.Surface.Radius, world.Level, _near);
        foreach (TileKey key in _near)
        {
            if (!_tiles.ContainsKey(key) && !_building.ContainsKey(key))
            {
                TileKey tile = key;
                ISurface surface = world.Surface;
                ZoneFrame frame = world.Frame;
                _building.Add(key, Task.Run(() => Faces(surface, frame, tile)));
            }
        }
        foreach ((TileKey key, Task<Vector3[]> task) in _building.Where(b => b.Value.IsCompleted).Take(1).ToList())
        {
            _building.Remove(key);
            if (task.IsFaulted) GD.PushError($"Cosmetic ground tile {key} failed: {task.Exception}");
            if (task.IsCompletedSuccessfully)
            {
                var shape = new ConcavePolygonShape3D { Data = task.Result, BackfaceCollision = true };
                var body = new StaticBody3D { Name = $"Ground {key}", CollisionLayer = 1, CollisionMask = 0 };
                body.AddChild(new CollisionShape3D { Shape = shape });
                AddChild(body);
                FrameEvents.Note("collision tile");
                _tiles[key] = body;
            }
        }
        double radius = world.Surface.Radius;
        foreach (TileKey key in _tiles.Keys.Where(k => Num.Vector3.Distance(world.Frame.ToLocal(k.Centre * radius), feet) > Reach + 120).ToList())
        {
            _tiles[key].QueueFree();
            _tiles.Remove(key);
        }
    }

    // On a worker: the tile's triangles as a soup, wound as Godot expects.
    private static Vector3[] Faces(ISurface surface, ZoneFrame frame, TileKey tile)
    {
        Hollow.Surface.TriangleMesh mesh = surface.GroundCollision(frame, tile, PhysicsWorld.ClipMargin);
        var faces = new Vector3[mesh.Indices.Length];
        float[] p = mesh.Positions;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[3 * t], b = mesh.Indices[3 * t + 1], c = mesh.Indices[3 * t + 2];
            faces[3 * t] = new Vector3(p[3 * a], p[3 * a + 1], p[3 * a + 2]);
            faces[3 * t + 1] = new Vector3(p[3 * c], p[3 * c + 1], p[3 * c + 2]);
            faces[3 * t + 2] = new Vector3(p[3 * b], p[3 * b + 1], p[3 * b + 2]);
        }
        return faces;
    }
}
