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
/// few a frame. Solids a surface places whole (<see cref="IInstancedSolids"/>: the cliff kit's pieces) are not copied
/// into the tiles' meshes: each one's shape is made once and placed on the tile its pivot stands over.
/// </summary>
public partial class GroundCollision : Node3D
{
    /// <summary>Tiles kept within this many metres of the player.</summary>
    private const float Reach = 140;

    // A tile's ground triangles, and the instanced solids standing over it: each one's shape and place.
    private sealed record Tile(Vector3[] Faces, int[] Shapes, Transform3D[] Places);

    private readonly Dictionary<TileKey, StaticBody3D> _tiles = new();
    private readonly Dictionary<TileKey, Task<Tile>> _building = new();
    private readonly List<TileKey> _near = new();
    private readonly Dictionary<int, ConcavePolygonShape3D> _solidShapes = new();
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
                _building.Add(key, Task.Run(() => Build(surface, frame, tile)));
            }
        }
        foreach ((TileKey key, Task<Tile> task) in _building.Where(b => b.Value.IsCompleted).Take(1).ToList())
        {
            _building.Remove(key);
            if (task.IsFaulted) GD.PushError($"Cosmetic ground tile {key} failed: {task.Exception}");
            if (task.IsCompletedSuccessfully)
            {
                Tile built = task.Result;
                var shape = new ConcavePolygonShape3D { Data = built.Faces, BackfaceCollision = true };
                var body = new StaticBody3D { Name = $"Ground {key}", CollisionLayer = 1, CollisionMask = 0 };
                body.AddChild(new CollisionShape3D { Shape = shape });
                for (int i = 0; i < built.Shapes.Length; i++)
                {
                    var solid = new CollisionShape3D { Shape = SolidShape((IInstancedSolids)world.Surface, built.Shapes[i]) };
                    solid.Transform = built.Places[i];
                    body.AddChild(solid);
                }
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

    // On a worker: the tile's ground and, on a surface with instanced solids, the solids over it.
    private static Tile Build(ISurface surface, ZoneFrame frame, TileKey tile)
    {
        if (surface is not IInstancedSolids solids)
        {
            return new Tile(Faces(surface.GroundCollision(frame, tile, PhysicsWorld.ClipMargin)), Array.Empty<int>(), Array.Empty<Transform3D>());
        }
        var placed = new List<KitPlacement>();
        solids.SolidsOver(tile, placed);
        var shapes = new int[placed.Count];
        var places = new Transform3D[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            KitPlacement k = placed[i];
            float s = (float)k.Scale;
            var basis = new Basis(ToGodot(frame.DirectionToLocal(k.AxisX)) * s, ToGodot(frame.DirectionToLocal(k.AxisY)) * s,
                ToGodot(frame.DirectionToLocal(k.AxisZ)) * s);
            shapes[i] = k.Piece;
            places[i] = new Transform3D(basis, ToGodot(frame.ToLocal(k.Position)));
        }
        return new Tile(Faces(solids.GroundCollisionWithoutSolids(frame, tile, PhysicsWorld.ClipMargin)), shapes, places);
    }

    // A solid's shape, made the first time one is placed: its shell as a soup in its own frame.
    private ConcavePolygonShape3D SolidShape(IInstancedSolids solids, int index)
    {
        if (!_solidShapes.TryGetValue(index, out ConcavePolygonShape3D? shape))
        {
            CliffCollision shell = solids.SolidShapes[index];
            var faces = new Vector3[shell.Indices.Length];
            for (int t = 0; t < shell.Indices.Length; t += 3)
            {
                faces[t] = ToGodot(shell.Vertices[shell.Indices[t]]);
                faces[t + 1] = ToGodot(shell.Vertices[shell.Indices[t + 2]]);
                faces[t + 2] = ToGodot(shell.Vertices[shell.Indices[t + 1]]);
            }
            shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true };
            _solidShapes[index] = shape;
        }
        return shape;
    }

    // The triangles as a soup, wound as Godot expects.
    private static Vector3[] Faces(Hollow.Surface.TriangleMesh mesh)
    {
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

    private static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ToGodot(Hollow.Core.Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
