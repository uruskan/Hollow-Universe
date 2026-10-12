using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Simulation.Physics;
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
    /// <summary>Tiles kept out from the player's tile: 5 × 5 tiles, 320 m across.</summary>
    private const int Radius = 2;

    private readonly Dictionary<(int X, int Z), StaticBody3D> _tiles = new();
    private readonly Dictionary<(int X, int Z), Task<Vector3[]>> _building = new();

    public void Follow(PhysicsWorld world, Num.Vector3 feet)
    {
        (int cx, int cz) = PhysicsWorld.TileOf(feet);
        for (int z = cz - Radius; z <= cz + Radius; z++)
        {
            for (int x = cx - Radius; x <= cx + Radius; x++)
            {
                if (!_tiles.ContainsKey((x, z)) && !_building.ContainsKey((x, z)))
                {
                    int tx = x, tz = z;
                    _building.Add((x, z), Task.Run(() => Faces(world, tx, tz)));
                }
            }
        }
        foreach (((int, int) key, Task<Vector3[]> task) in _building.Where(b => b.Value.IsCompleted).Take(1).ToList())
        {
            _building.Remove(key);
            if (task.IsCompletedSuccessfully)
            {
                var shape = new ConcavePolygonShape3D { Data = task.Result, BackfaceCollision = true };
                var body = new StaticBody3D { Name = $"Ground {key}", CollisionLayer = 1, CollisionMask = 0 };
                body.AddChild(new CollisionShape3D { Shape = shape });
                AddChild(body);
                _tiles[key] = body;
            }
        }
        foreach ((int X, int Z) key in _tiles.Keys.Where(k => Math.Max(Math.Abs(k.X - cx), Math.Abs(k.Z - cz)) > Radius + 1).ToList())
        {
            _tiles[key].QueueFree();
            _tiles.Remove(key);
        }
    }

    // On a worker: the tile's triangles as a soup, wound as Godot expects.
    private static Vector3[] Faces(PhysicsWorld world, int tx, int tz)
    {
        Hollow.Surface.TriangleMesh mesh = world.TileMesh(tx, tz);
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
