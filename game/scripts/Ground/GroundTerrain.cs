using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Placeholder ground (GROUND.md §4.3): the collision tiles of <see cref="ISurface.GroundCollision"/> around the
/// player, drawn as plain meshes coloured by slope. The planet package's terrain renderer replaces this. Tiles are
/// built on worker threads and turned into meshes a few per frame, so walking never hitches.
/// </summary>
public partial class GroundTerrain : Node3D
{
    /// <summary>Tiles drawn out from the player's tile: 7 × 7 tiles, 448 m across.</summary>
    private const int Radius = 3;

    private const int MeshesPerFrame = 2;

    private readonly Dictionary<(int X, int Z), MeshInstance3D> _tiles = new();
    private readonly Dictionary<(int X, int Z), Task<TileArrays>> _building = new();
    private StandardMaterial3D? _material;
    private (int X, int Z) _centre = (int.MinValue, 0);

    /// <summary>Every tile around the player is drawn.</summary>
    public bool IsSettled { get; private set; }

    public void Follow(PhysicsWorld world, Num.Vector3 feet)
    {
        _material ??= new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f };
        _centre = PhysicsWorld.TileOf(feet);
        for (int z = _centre.Z - Radius; z <= _centre.Z + Radius; z++)
        {
            for (int x = _centre.X - Radius; x <= _centre.X + Radius; x++)
            {
                if (!_tiles.ContainsKey((x, z)) && !_building.ContainsKey((x, z)))
                {
                    int tx = x, tz = z;
                    _building.Add((x, z), Task.Run(() => Build(world, tx, tz)));
                }
            }
        }

        // Nearest finished tiles first, a few a frame.
        int made = 0;
        foreach (((int, int) key, Task<TileArrays> task) in _building.Where(b => b.Value.IsCompleted)
            .OrderBy(b => Math.Abs(b.Key.X - _centre.X) + Math.Abs(b.Key.Z - _centre.Z)).ToList())
        {
            if (made++ >= MeshesPerFrame)
            {
                break;
            }
            _building.Remove(key);
            if (task.IsCompletedSuccessfully)
            {
                AddTile(key, task.Result);
            }
        }

        foreach ((int X, int Z) key in _tiles.Keys.Where(k => Math.Max(Math.Abs(k.X - _centre.X), Math.Abs(k.Z - _centre.Z)) > Radius + 1).ToList())
        {
            _tiles[key].QueueFree();
            _tiles.Remove(key);
        }
        IsSettled = _building.Count == 0 && _tiles.Count >= (2 * Radius + 1) * (2 * Radius + 1);
    }

    private void AddTile((int X, int Z) key, TileArrays arrays)
    {
        var data = new Godot.Collections.Array();
        data.Resize((int)Mesh.ArrayType.Max);
        data[(int)Mesh.ArrayType.Vertex] = arrays.Vertices;
        data[(int)Mesh.ArrayType.Normal] = arrays.Normals;
        data[(int)Mesh.ArrayType.Color] = arrays.Colours;
        data[(int)Mesh.ArrayType.Index] = arrays.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data);
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = _material, Name = $"Tile {key.X},{key.Z}" };
        AddChild(instance);
        _tiles.Add(key, instance);
    }

    private readonly record struct TileArrays(Vector3[] Vertices, Vector3[] Normals, Color[] Colours, int[] Indices);

    // On a worker thread: the tile's triangles, smooth normals, and a colour from slope and a little noise.
    private static TileArrays Build(PhysicsWorld world, int tx, int tz)
    {
        Hollow.Surface.TriangleMesh mesh = world.TileMesh(tx, tz);
        int count = mesh.VertexCount;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2]);
        }
        // Godot's front faces wind clockwise; the surface's wind counter-clockwise seen from above.
        var indices = new int[mesh.Indices.Length];
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[3 * t], b = mesh.Indices[3 * t + 1], c = mesh.Indices[3 * t + 2];
            Vector3 normal = (vertices[b] - vertices[a]).Cross(vertices[c] - vertices[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
            indices[3 * t] = a;
            indices[3 * t + 1] = c;
            indices[3 * t + 2] = b;
        }
        var colours = new Color[count];
        ZoneFrame frame = world.Frame;
        var grass = new Color(0.20f, 0.33f, 0.11f);
        var dry = new Color(0.40f, 0.37f, 0.21f);
        var rock = new Color(0.38f, 0.37f, 0.34f);
        for (int i = 0; i < count; i++)
        {
            normals[i] = normals[i].Normalized();
            Num.Vector3 up = frame.UpAt(new Num.Vector3(vertices[i].X, vertices[i].Y, vertices[i].Z));
            float flatness = normals[i].Dot(new Vector3(up.X, up.Y, up.Z));
            Color ground = grass.Lerp(dry, Math.Clamp((0.985f - flatness) * 12f, 0f, 1f));
            ground = ground.Lerp(rock, Math.Clamp((0.92f - flatness) * 6f, 0f, 1f));
            // A little variation, fixed to the place.
            ulong h = Hash64.Of((ulong)(long)Math.Floor(vertices[i].X * 0.5f), (ulong)(long)Math.Floor(vertices[i].Z * 0.5f));
            float shade = 0.88f + 0.2f * ((h >> 40) & 0xFFFF) / 65535f;
            colours[i] = new Color(ground.R * shade, ground.G * shade, ground.B * shade);
        }
        return new TileArrays(vertices, normals, colours, indices);
    }
}
