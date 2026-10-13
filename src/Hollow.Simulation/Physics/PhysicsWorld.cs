using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Surface;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Physics;

/// <summary>
/// One zone's Jolt physics system (GROUND.md §4.5): the ground near characters, as static mesh tiles built from the
/// surface, and the queries characters move with. The server has one per zone; a client has one for its prediction.
/// <para>
/// <b>Ground tiles</b> are squares of <see cref="TileSize"/> metres of the zone's tangent plane, each a static mesh body
/// built from <see cref="ISurface.GroundCollision"/>. A tile's body ID is a function of its grid position, so the
/// server and every client have the same bodies under the same IDs whatever else they hold. Jolt sorts contacts by
/// body ID, so this keeps a character's movement identical on both sides where tiles meet.
/// </para>
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    /// <summary>Side of a ground tile in metres.</summary>
    public const float TileSize = 64f;

    /// <summary>Tiles exist from −<see cref="TileRange"/> to <see cref="TileRange"/> − 1 on each axis: ±8.2 km.</summary>
    public const int TileRange = 128;

    /// <summary>Half the width of the zone that can have ground, in metres.</summary>
    public const float Extent = TileRange * TileSize;

    internal static readonly ObjectLayer StaticLayer = new(0);
    internal static readonly ObjectLayer MovingLayer = new(1);

    private const int TileIdCount = 2 * TileRange * 2 * TileRange;

    /// <summary>Body IDs after the ground tiles' are for props (natural objects, deployables): this many at once.</summary>
    public const int MaxProps = 65536;

    /// <summary>
    /// Body IDs from here on belong to building pieces, doors and claim devices (<c>Building/BuildingPhysics</c>), at
    /// most <see cref="MaxBuildingBodies"/> of them; ground tiles, props and the 64 spare bodies come first.
    /// </summary>
    public const int FirstBuildingBody = TileIdCount + MaxProps + 64;

    public const int MaxBuildingBodies = 16384;

    /// <summary>
    /// Body IDs from here on belong to ships parked on the ground (<c>Ships/ShipCollider</c>, G6), at most
    /// <see cref="MaxShipBodies"/> of them; everything above comes first.
    /// </summary>
    public const int FirstShipBody = FirstBuildingBody + MaxBuildingBodies;

    public const int MaxShipBodies = 1024;

    /// <summary>Whether a body is a ground tile.</summary>
    public static bool IsGroundBody(uint id) => id < TileIdCount;

    private readonly Dictionary<(int X, int Z), Tile> _tiles = new();
    private readonly Dictionary<(int X, int Z), Task<Shape>> _pending = new();
    private readonly List<(int X, int Z)> _expired = new();
    private readonly List<(int X, int Z)> _adopted = new();
    private readonly Dictionary<(PropLayer, ulong), Prop> _props = new();
    private readonly Stack<uint> _freePropIds = new();
    private uint _nextPropId = TileIdCount;
    private int _tilesBuilt;
    private long _buildTicks;
    private readonly ObjectLayerPairFilterTable _layerPairs;
    private readonly BroadPhaseLayerInterfaceTable _broadPhaseLayers;
    private readonly ObjectVsBroadPhaseLayerFilterTable _objectVsBroadPhase;
    private long _epoch;
    private bool _disposed;

    public PhysicsWorld(ISurface surface, ZoneFrame frame)
    {
        Surface = surface;
        Frame = frame;
        JoltRuntime.EnsureInitialised();
        lock (JoltRuntime.Gate)
        {
            _layerPairs = new ObjectLayerPairFilterTable(2);
            _layerPairs.EnableCollision(StaticLayer, MovingLayer);
            _layerPairs.EnableCollision(MovingLayer, MovingLayer);
            _broadPhaseLayers = new BroadPhaseLayerInterfaceTable(2, 2);
            _broadPhaseLayers.MapObjectToBroadPhaseLayer(StaticLayer, new BroadPhaseLayer(0));
            _broadPhaseLayers.MapObjectToBroadPhaseLayer(MovingLayer, new BroadPhaseLayer(1));
            _objectVsBroadPhase = new ObjectVsBroadPhaseLayerFilterTable(_broadPhaseLayers, 2, _layerPairs, 2);
            Jolt = new PhysicsSystem(new PhysicsSystemSettings
            {
                // Ground tile IDs take the bottom of the range (see the class remarks); later bodies take the rest.
                MaxBodies = FirstShipBody + MaxShipBodies,
                MaxBodyPairs = 4096,
                MaxContactConstraints = 4096,
                NumBodyMutexes = 0,
                ObjectLayerPairFilter = _layerPairs,
                BroadPhaseLayerInterface = _broadPhaseLayers,
                ObjectVsBroadPhaseLayerFilter = _objectVsBroadPhase,
            });
        }
    }

    public ISurface Surface { get; }

    public ZoneFrame Frame { get; }

    internal PhysicsSystem Jolt { get; }

    /// <summary>Ground tiles currently in the world.</summary>
    public int TileCount => _tiles.Count;

    /// <summary>Ground tiles built so far, and the time that took (on whatever thread), for statistics.</summary>
    public int TilesBuilt => Volatile.Read(ref _tilesBuilt);

    public double TileBuildMilliseconds => Interlocked.Read(ref _buildTicks) * 1000.0 / Stopwatch.Frequency;

    /// <summary>The tile holding a local point.</summary>
    public static (int X, int Z) TileOf(Vector3 position) =>
        ((int)MathF.Floor(position.X / TileSize), (int)MathF.Floor(position.Z / TileSize));

    /// <summary>
    /// Makes sure the ground exists in the square of tiles within <paramref name="radius"/> tiles of the tile under
    /// <paramref name="position"/>, and marks those tiles as in use this epoch. Tiles built ahead by
    /// <see cref="Prefetch"/> are taken in; a tile still missing is built now (or waited for, if already on its way).
    /// </summary>
    public void EnsureGround(Vector3 position, int radius = 1)
    {
        AdoptPrefetched();
        (int cx, int cz) = TileOf(position);
        for (int z = cz - radius; z <= cz + radius; z++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < -TileRange || x >= TileRange || z < -TileRange || z >= TileRange)
                {
                    continue;
                }
                if (!_tiles.TryGetValue((x, z), out Tile? tile))
                {
                    Shape shape;
                    if (_pending.Remove((x, z), out Task<Shape>? building))
                    {
                        shape = building.Result;
                    }
                    else
                    {
                        shape = BuildShape(x, z);
                    }
                    TilesBuiltOnDemand++;
                    tile = AddTile(x, z, shape);
                    Listener?.TileAdded(x, z);
                }
                tile.LastUsed = _epoch;
            }
        }
    }

    /// <summary>
    /// Starts building, on worker threads, the tiles within <paramref name="radius"/> of a point that don't exist yet,
    /// so that <see cref="EnsureGround"/> finds them ready and a tick never waits for a tile (about 10 ms each). Tiles
    /// already there are marked as in use.
    /// </summary>
    public void Prefetch(Vector3 position, int radius = 2)
    {
        (int cx, int cz) = TileOf(position);
        for (int z = cz - radius; z <= cz + radius; z++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < -TileRange || x >= TileRange || z < -TileRange || z >= TileRange || _pending.ContainsKey((x, z)))
                {
                    continue;
                }
                if (_tiles.TryGetValue((x, z), out Tile? tile))
                {
                    // Near someone: keep it.
                    tile.LastUsed = _epoch;
                    continue;
                }
                (int X, int Z) key = (x, z);
                _pending.Add(key, Task.Run(() => BuildShape(key.X, key.Z)));
            }
        }
    }

    /// <summary>Tiles a step had to build or wait for itself (none, when prefetching keeps ahead).</summary>
    public int TilesBuiltOnDemand { get; private set; }

    // Adds the tiles whose background build has finished.
    private void AdoptPrefetched()
    {
        if (_pending.Count == 0)
        {
            return;
        }
        _adopted.Clear();
        foreach (((int, int) key, Task<Shape> task) in _pending)
        {
            if (task.IsCompleted)
            {
                _adopted.Add(key);
            }
        }
        foreach ((int x, int z) in _adopted)
        {
            _pending.Remove((x, z), out Task<Shape>? task);
            Tile tile = AddTile(x, z, task!.Result);
            tile.LastUsed = _epoch;
            Listener?.TileAdded(x, z);
        }
    }

    /// <summary>
    /// Starts a new epoch and removes tiles nobody marked in the last <paramref name="keepEpochs"/> epochs. Call once a
    /// tick (server) or frame (client), after the <see cref="EnsureGround"/> calls of the previous one.
    /// </summary>
    public void ReleaseUnusedGround(int keepEpochs)
    {
        _epoch++;
        _expired.Clear();
        foreach (((int, int) key, Tile tile) in _tiles)
        {
            if (_epoch - tile.LastUsed > keepEpochs)
            {
                _expired.Add(key);
            }
        }
        if (_expired.Count == 0)
        {
            return;
        }
        lock (JoltRuntime.Gate)
        {
            BodyInterface bodies = Jolt.BodyInterface;
            foreach ((int X, int Z) key in _expired)
            {
                Listener?.TileRemoved(key.X, key.Z);
                Tile tile = _tiles[key];
                bodies.RemoveAndDestroyBody(tile.Body);
                tile.Shape.Dispose();
                _tiles.Remove(key);
            }
        }
    }

    /// <summary>The triangles of a tile, as the surface gives them (also what the client draws as placeholder ground).</summary>
    public TriangleMesh TileMesh(int x, int z) =>
        Surface.GroundCollision(Frame, (x + 0.5) * TileSize, (z + 0.5) * TileSize, TileSize);

    // The mesh shape of a tile: the surface's triangles, then Jolt's tree over them. Thread-safe (a pure function of
    // the surface, and Jolt builds shapes without the shared allocator), so prefetching runs it on worker threads.
    private Shape BuildShape(int x, int z)
    {
        long start = Stopwatch.GetTimestamp();
        TriangleMesh mesh = TileMesh(x, z);
        // Pooled: these are large-object-heap sized, and tiles come and go all the time.
        Vector3[] vertices = ArrayPool<Vector3>.Shared.Rent(mesh.VertexCount);
        IndexedTriangle[] triangles = ArrayPool<IndexedTriangle>.Shared.Rent(mesh.TriangleCount);
        Shape shape;
        try
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                vertices[i] = new Vector3(mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2]);
            }
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int a = mesh.Indices[3 * t], b = mesh.Indices[3 * t + 1], c = mesh.Indices[3 * t + 2];
                triangles[t] = new IndexedTriangle(a, b, c, 0, 0);
            }
            using var settings = new MeshShapeSettings(vertices.AsSpan(0, mesh.VertexCount), triangles.AsSpan(0, mesh.TriangleCount));
            shape = settings.Create();
        }
        finally
        {
            ArrayPool<Vector3>.Shared.Return(vertices);
            ArrayPool<IndexedTriangle>.Shared.Return(triangles);
        }
        Interlocked.Increment(ref _tilesBuilt);
        Interlocked.Add(ref _buildTicks, Stopwatch.GetTimestamp() - start);
        return shape;
    }

    // Adds a built tile to the physics system, under the tile's own body ID. On the owning thread.
    private Tile AddTile(int x, int z, Shape shape)
    {
        var id = new BodyID((uint)((x + TileRange) + (z + TileRange) * 2 * TileRange));
        Tile tile;
        lock (JoltRuntime.Gate)
        {
            using var creation = new BodyCreationSettings(shape, Vector3.Zero, Quaternion.Identity, MotionType.Static, StaticLayer);
            BodyInterface bodies = Jolt.BodyInterface;
            Body body = bodies.CreateBodyWithID(id, creation)
                ?? throw new InvalidOperationException($"Jolt refused ground tile {x},{z} (body {id.ID}).");
            bodies.AddBody(body.ID, Activation.DontActivate);
            tile = new Tile(body.ID, shape);
        }
        _tiles.Add((x, z), tile);
        return tile;
    }

    // ---------------------------------------------------------------- props

    /// <summary>Told when ground tiles come and go, on the owning thread (natural objects follow the tiles).</summary>
    public ITileListener? Listener { get; set; }

    /// <summary>Whether a ground tile is in the world now.</summary>
    public bool HasTile(int x, int z) => _tiles.ContainsKey((x, z));

    /// <summary>Props in the world now.</summary>
    public int PropCount => _props.Count;

    public bool HasProp(PropLayer layer, ulong key) => _props.ContainsKey((layer, key));

    /// <summary>
    /// Adds a static prop (a tree, an outcrop, a box) under a key; nothing if the key is there already. Its body ID
    /// comes after the ground tiles', so a tile is always ordered before a prop in Jolt's contact lists.
    /// </summary>
    public void AddProp(PropLayer layer, ulong key, in PropShape prop)
    {
        if (_props.ContainsKey((layer, key)) || _disposed)
        {
            return;
        }
        uint id;
        if (_freePropIds.Count > 0)
        {
            id = _freePropIds.Pop();
        }
        else if (_nextPropId < TileIdCount + MaxProps)
        {
            id = _nextPropId++;
        }
        else
        {
            return; // full: the prop just doesn't collide
        }
        lock (JoltRuntime.Gate)
        {
            Shape shape = prop.CreateShape();
            using var creation = new BodyCreationSettings(shape, prop.Centre, prop.Rotation, MotionType.Static, StaticLayer);
            BodyInterface bodies = Jolt.BodyInterface;
            var bodyId = new BodyID(id);
            Body body = bodies.CreateBodyWithID(bodyId, creation)
                ?? throw new InvalidOperationException($"Jolt refused prop {layer} {key} (body {id}).");
            bodies.AddBody(body.ID, Activation.DontActivate);
            _props.Add((layer, key), new Prop(body.ID, shape, id));
        }
    }

    public void RemoveProp(PropLayer layer, ulong key)
    {
        if (!_props.Remove((layer, key), out Prop? prop))
        {
            return;
        }
        lock (JoltRuntime.Gate)
        {
            Jolt.BodyInterface.RemoveAndDestroyBody(prop.Body);
            prop.Shape.Dispose();
        }
        _freePropIds.Push(prop.Id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        lock (JoltRuntime.Gate)
        {
            foreach (Prop prop in _props.Values)
            {
                Jolt.BodyInterface.RemoveAndDestroyBody(prop.Body);
                prop.Shape.Dispose();
            }
            _props.Clear();
        }
        foreach (Task<Shape> building in _pending.Values)
        {
            building.Wait();
            building.Result.Dispose();
        }
        _pending.Clear();
        lock (JoltRuntime.Gate)
        {
            BodyInterface bodies = Jolt.BodyInterface;
            foreach (Tile tile in _tiles.Values)
            {
                bodies.RemoveAndDestroyBody(tile.Body);
                tile.Shape.Dispose();
            }
            _tiles.Clear();
            Jolt.Dispose();
            _objectVsBroadPhase.Dispose();
            _broadPhaseLayers.Dispose();
            _layerPairs.Dispose();
        }
    }

    private sealed class Prop(BodyID body, Shape shape, uint id)
    {
        public readonly BodyID Body = body;
        public readonly Shape Shape = shape;
        public readonly uint Id = id;
    }

    private sealed class Tile(BodyID body, Shape shape)
    {
        public readonly BodyID Body = body;
        public readonly Shape Shape = shape;
        public long LastUsed;
    }
}
