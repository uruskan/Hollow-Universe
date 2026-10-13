using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Simulation.Diagnostics;
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

    /// <summary>A ground tile keeps the surface's triangles this far beyond its square (neighbours overlap by this much).</summary>
    public const float ClipMargin = 2f;

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
    private readonly Dictionary<(int X, int Z), TileBuild> _pending = new();
    private readonly List<(int X, int Z)> _expired = new();
    private readonly List<(int X, int Z)> _adopted = new();
    private readonly Dictionary<(PropLayer, ulong), Prop> _props = new();
    private readonly bool[] _propIdUsed = new bool[MaxProps];
    private int _tilesBuilt;
    private long _buildTicks;
    private readonly ObjectLayerPairFilterTable _layerPairs;
    private readonly BroadPhaseLayerInterfaceTable _broadPhaseLayers;
    private readonly ObjectVsBroadPhaseLayerFilterTable _objectVsBroadPhase;
    private long _epoch;
    private bool _disposed;

    // Jolt gives a body the ID asked for (CreateBodyWithID) by walking its list of free IDs, and with the IDs this world
    // uses (tiles by grid position, props by hash: far apart) that walk cost 20–60 µs a body, so a tile's trees took
    // 2–3 ms to add (GROUND.md §5.9). So a body, once made, is kept: taken out of the world it leaves the broadphase
    // and is parked on a tiny shared shape; put back, it takes its new shape and pose exactly as a new body would.
    private const byte NoBody = 0, ParkedBody = 1, BodyInWorld = 2;
    private readonly byte[] _bodies = new byte[FirstShipBody + MaxShipBodies];
    private readonly Shape _parked;

    public PhysicsWorld(ISurface surface, ZoneFrame frame)
    {
        Surface = surface;
        Frame = frame;
        JoltRuntime.EnsureInitialised();
        lock (JoltRuntime.Gate)
        {
            using (var parked = new SphereShapeSettings(0.01f))
            {
                _parked = parked.Create();
            }
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

    /// <summary>Times the ground's work on the owning thread (the server's tick; null in a client's prediction).</summary>
    public TickProfile? Profile { get; set; }

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
                EnsureTile(x, z);
            }
        }
    }

    /// <summary>
    /// Makes sure the ground exists wherever something within <paramref name="reach"/> metres of a point (across the
    /// zone's tangent plane) could touch it, natural props included (<see cref="PropReach"/>): the tiles a ship's
    /// queries of this tick can meet, one to four instead of <see cref="EnsureGround"/>'s nine. Marks them in use.
    /// </summary>
    public void EnsureGroundAround(Vector3 position, float reach)
    {
        AdoptPrefetched();
        float r = reach + PropReach;
        (int x0, int z0) = TileOf(new Vector3(position.X - r, 0, position.Z - r));
        (int x1, int z1) = TileOf(new Vector3(position.X + r, 0, position.Z + r));
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                EnsureTile(x, z);
            }
        }
    }

    /// <summary>
    /// How far a natural prop's shape reaches across the ground from its base, the point that puts it in a tile: a
    /// boulder's 0.95 m radius at scale up to 2.45 (gather.json, <c>Scatter</c>) is the most, 2.3 m.
    /// </summary>
    public float PropReach { get; set; } = 3f;

    private void EnsureTile(int x, int z)
    {
        if (x < -TileRange || x >= TileRange || z < -TileRange || z >= TileRange)
        {
            return;
        }
        if (!_tiles.TryGetValue((x, z), out Tile? tile))
        {
            Shape shape;
            using (Profile.Time(ProfileSection.TileWait))
            {
                // On its way: taken over and built here if no worker has started it yet (it may be queued behind
                // hundreds of others in a respawn storm), else waited for.
                shape = _pending.Remove((x, z), out TileBuild? building) ? building.Take()! : BuildShape(x, z);
            }
            TilesBuiltOnDemand++;
            using (Profile.Time(ProfileSection.TileAdd))
            {
                tile = AddTile(x, z, shape);
                Listener?.TileAdded(x, z);
            }
        }
        tile.LastUsed = _epoch;
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
                PrefetchTile(x, z);
            }
        }
    }

    /// <summary>
    /// <see cref="Prefetch"/> for the tiles <see cref="EnsureGroundAround"/> would want at a point: the ground a ship
    /// will need along its way, built before it gets there.
    /// </summary>
    public void PrefetchAround(Vector3 position, float reach)
    {
        float r = reach + PropReach;
        (int x0, int z0) = TileOf(new Vector3(position.X - r, 0, position.Z - r));
        (int x1, int z1) = TileOf(new Vector3(position.X + r, 0, position.Z + r));
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                PrefetchTile(x, z);
            }
        }
    }

    /// <summary>
    /// <see cref="PrefetchAround"/> all along a segment: points half a tile apart, each box widened by half that, so
    /// together they cover every point within <paramref name="reach"/> of the segment.
    /// </summary>
    public void PrefetchAlong(Vector3 from, Vector3 to, float reach)
    {
        const float step = TileSize / 2;
        float length = Vector2.Distance(new Vector2(from.X, from.Z), new Vector2(to.X, to.Z));
        int n = Math.Max(1, (int)MathF.Ceiling(length / step));
        for (int i = 0; i <= n; i++)
        {
            PrefetchAround(Vector3.Lerp(from, to, (float)i / n), reach + step / 2);
        }
    }

    /// <summary>
    /// Whether the ground within <paramref name="radius"/> tiles of a point is ready: in the world, or built and
    /// waiting to be taken in. What isn't on its way yet is started (<see cref="Prefetch"/>). A respawn or an arrival
    /// waits for this, so the tick that brings the player in never builds ground itself.
    /// </summary>
    public bool GroundReady(Vector3 position, int radius = 1)
    {
        Prefetch(position, radius);
        (int cx, int cz) = TileOf(position);
        for (int z = cz - radius; z <= cz + radius; z++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x >= -TileRange && x < TileRange && z >= -TileRange && z < TileRange && !_tiles.ContainsKey((x, z)) &&
                    !(_pending.TryGetValue((x, z), out TileBuild? build) && build.IsBuilt))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void PrefetchTile(int x, int z)
    {
        if (x < -TileRange || x >= TileRange || z < -TileRange || z >= TileRange || _pending.ContainsKey((x, z)))
        {
            return;
        }
        if (_tiles.TryGetValue((x, z), out Tile? tile))
        {
            // Near someone: keep it.
            tile.LastUsed = _epoch;
            return;
        }
        ITileListener? listener = Listener;
        _pending.Add((x, z), new TileBuild(() =>
        {
            if (_closing)
            {
                return null;
            }
            listener?.TilePreparing(x, z);
            return BuildShape(x, z);
        }));
    }

    /// <summary>Tiles a step had to build or wait for itself (none, when prefetching keeps ahead).</summary>
    public int TilesBuiltOnDemand { get; private set; }

    /// <summary>
    /// Tiles built ahead taken into the world at most each epoch (tick): a burst of them (a respawn storm, ships
    /// streaming) is spread over the next ticks. A tile needed now is always taken in.
    /// </summary>
    public int AdoptPerEpoch { get; set; } = 8;

    private long _adoptEpoch = -1;
    private int _adoptedThisEpoch;

    // Adds tiles whose background build has finished, up to the epoch's budget.
    private void AdoptPrefetched()
    {
        if (_adoptEpoch != _epoch)
        {
            _adoptEpoch = _epoch;
            _adoptedThisEpoch = 0;
        }
        if (_pending.Count == 0 || _adoptedThisEpoch >= AdoptPerEpoch)
        {
            return;
        }
        _adopted.Clear();
        foreach (((int, int) key, TileBuild build) in _pending)
        {
            if (build.IsBuilt)
            {
                _adopted.Add(key);
                if (_adoptedThisEpoch + _adopted.Count >= AdoptPerEpoch)
                {
                    break;
                }
            }
        }
        foreach ((int x, int z) in _adopted)
        {
            using TickProfile.Scope _ = Profile.Time(ProfileSection.TileAdd);
            _pending.Remove((x, z), out TileBuild? build);
            Tile tile = AddTile(x, z, build!.Take()!);
            tile.LastUsed = _epoch;
            Listener?.TileAdded(x, z);
            _adoptedThisEpoch++;
        }
    }

    /// <summary>A body came or went outside this class (a parked ship, G6): the broadphase wants rebuilding (<see cref="OptimizeBroadPhase"/>).</summary>
    internal void NoteBodyChanged() => Interlocked.Increment(ref _bodyChanges);

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
            foreach ((int X, int Z) key in _expired)
            {
                using TickProfile.Scope _ = Profile.Time(ProfileSection.TileRemove);
                Listener?.TileRemoved(key.X, key.Z);
                Tile tile = _tiles[key];
                RemoveStaticBody(tile.Body.ID);
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
            // Only the triangles over the tile's square and a margin: a surface's grids cover more than the square
            // (the planet's level-18 tiles overlap it by up to a tile), and neighbours would otherwise hold the same
            // triangles twice, which every character's contact query pays for.
            // The square is the tangent plane's, so each vertex is seen from the body's centre on that plane (on a
            // small body the ground falls away from the plane and its triangles crowd towards the origin).
            float x0 = x * TileSize - ClipMargin, x1 = (x + 1) * TileSize + ClipMargin;
            float z0 = z * TileSize - ClipMargin, z1 = (z + 1) * TileSize + ClipMargin;
            double r0 = Frame.Origin.Length;
            Vector2 Plane(Vector3 v) => r0 + v.Y > r0 * 0.01 ? new Vector2(v.X, v.Z) * (float)(r0 / (r0 + v.Y)) : new Vector2(v.X, v.Z);
            int kept = 0;
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int a = mesh.Indices[3 * t], b = mesh.Indices[3 * t + 1], c = mesh.Indices[3 * t + 2];
                Vector2 pa = Plane(vertices[a]), pb = Plane(vertices[b]), pc = Plane(vertices[c]);
                if (MathF.Max(pa.X, MathF.Max(pb.X, pc.X)) < x0 || MathF.Min(pa.X, MathF.Min(pb.X, pc.X)) > x1 ||
                    MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y)) < z0 || MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y)) > z1)
                {
                    continue;
                }
                triangles[kept++] = new IndexedTriangle(a, b, c, 0, 0);
            }
            if (kept == 0)
            {
                // Nothing over the square (it can't happen with a surface that covers it): keep everything.
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    triangles[t] = new IndexedTriangle(mesh.Indices[3 * t], mesh.Indices[3 * t + 1], mesh.Indices[3 * t + 2], 0, 0);
                }
                kept = mesh.TriangleCount;
            }
            using var settings = new MeshShapeSettings(vertices.AsSpan(0, mesh.VertexCount), triangles.AsSpan(0, kept));
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

    private int _bodyChanges;

    /// <summary>
    /// Keeps Jolt's broadphase tree in shape after bodies came and went (ground tiles, props, building, parked ships):
    /// a tree grown body by body makes every contact query walk all that was added since. The world never steps Jolt
    /// (characters and ships move by queries), so this runs the broadphase part of Jolt's update alone (an update with
    /// no time step does nothing else): it rebuilds only the parts of the tree that changed, as Jolt does for a
    /// streaming world, in about 0.1–0.3 ms where a whole rebuild took 5–10 ms with 30,000 bodies (GROUND.md §5.9).
    /// Queries find the same bodies whatever the tree's shape. Call with the Jolt lock held, once a tick before
    /// characters move.
    /// </summary>
    public void OptimizeBroadPhase()
    {
        if (_bodyChanges == 0)
        {
            return;
        }
        long started = Stopwatch.GetTimestamp();
        Jolt.Update(0f, 1, JoltRuntime.Jobs);
        TimeSpan took = Stopwatch.GetElapsedTime(started);
        BroadPhaseTime += took;
        BroadPhaseWorst = took > BroadPhaseWorst ? took : BroadPhaseWorst;
        BroadPhaseRebuilds++;
        _bodyChanges = 0;
    }

    /// <summary>Broadphase updates so far, their time in all and the longest (the load tests' statistics).</summary>
    public int BroadPhaseRebuilds { get; private set; }

    public TimeSpan BroadPhaseTime { get; private set; }

    public TimeSpan BroadPhaseWorst { get; private set; }

    /// <summary>
    /// Puts a static body into the world under an ID: made the first time, afterwards the same body taken out of its
    /// parking (see <see cref="_bodies"/>). Ground tiles, props, building pieces and parked ships all come in here.
    /// </summary>
    internal void AddStaticBody(uint id, Shape shape, Vector3 position, Quaternion rotation)
    {
        lock (JoltRuntime.Gate)
        {
            var bodyId = new BodyID(id);
            BodyInterface bodies = Jolt.BodyInterface;
            switch (_bodies[id])
            {
                case NoBody:
                {
                    using var creation = new BodyCreationSettings(shape, position, rotation, MotionType.Static, StaticLayer);
                    _ = bodies.CreateBodyWithID(bodyId, creation) ?? throw new InvalidOperationException($"Jolt refused body {id}.");
                    BodiesMade++;
                    break;
                }
                case ParkedBody:
                    // The shape first: the pose then puts its centre of mass where a new body's would be.
                    bodies.SetShape(bodyId, shape, false, Activation.DontActivate);
                    bodies.SetPositionAndRotation(bodyId, position, rotation, Activation.DontActivate);
                    BodiesReused++;
                    break;
                default:
                    throw new InvalidOperationException($"Body {id} is in the world already.");
            }
            bodies.AddBody(bodyId, Activation.DontActivate);
            _bodies[id] = BodyInWorld;
            _bodyChanges++;
        }
    }

    /// <summary>Bodies made in Jolt so far, and bodies put back from their parking (statistics, tests).</summary>
    public int BodiesMade { get; private set; }

    public int BodiesReused { get; private set; }

    /// <summary>Takes a static body out of the world; it stays made, parked, for the next body under its ID.</summary>
    internal void RemoveStaticBody(uint id)
    {
        if (_disposed || _bodies[id] != BodyInWorld)
        {
            return;
        }
        lock (JoltRuntime.Gate)
        {
            var bodyId = new BodyID(id);
            BodyInterface bodies = Jolt.BodyInterface;
            bodies.RemoveBody(bodyId);
            // Lets go of its shape (the owner disposes its own handle).
            bodies.SetShape(bodyId, _parked, false, Activation.DontActivate);
            _bodies[id] = ParkedBody;
            _bodyChanges++;
        }
    }

    // Adds a built tile to the physics system, under the tile's own body ID. On the owning thread.
    private Tile AddTile(int x, int z, Shape shape)
    {
        var id = new BodyID((uint)((x + TileRange) + (z + TileRange) * 2 * TileRange));
        AddStaticBody(id.ID, shape, Vector3.Zero, Quaternion.Identity);
        var tile = new Tile(id, shape);
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
        // The body ID comes from the key (the next free one after its hash), not from the order props arrive in, so
        // the server and every client give a prop the same ID whatever else they hold, and Jolt orders contacts
        // with two props the same way on both sides.
        if (_props.Count >= MaxProps)
        {
            return; // full: the prop just doesn't collide
        }
        uint slot = (uint)(Core.Hash64.Of((ulong)layer, key) % MaxProps);
        while (_propIdUsed[slot])
        {
            slot = (slot + 1) % MaxProps;
        }
        _propIdUsed[slot] = true;
        uint id = TileIdCount + slot;
        lock (JoltRuntime.Gate)
        {
            if (!_propBodiesMade && MakePropBodiesAhead)
            {
                MakePropBodies();
            }
            Shape shape = prop.CreateShape();
            AddStaticBody(id, shape, prop.Centre, prop.Rotation);
            _props.Add((layer, key), new Prop(new BodyID(id), shape, id));
        }
    }

    private bool _propBodiesMade;

    /// <summary>Whether the first prop makes every prop body ahead (see <see cref="MakePropBodies"/>); tests compare without.</summary>
    internal bool MakePropBodiesAhead { get; set; } = true;

    // Props' IDs are spread over their whole range by hash, so even a prop's first body would walk Jolt's free list.
    // With the first prop, every prop body is made at once, parked, from the top of the range down: each new ID is
    // then at the head of the list (about 0.1 µs a body, 65,536 bodies, 9 MB). Ground tiles and building pieces are
    // made as they come (tiles walk the tiles' part of the list the first time, at most 30 µs).
    private void MakePropBodies()
    {
        _propBodiesMade = true;
        using var creation = new BodyCreationSettings(_parked, Vector3.Zero, Quaternion.Identity, MotionType.Static, StaticLayer);
        BodyInterface bodies = Jolt.BodyInterface;
        for (uint id = TileIdCount + MaxProps - 1; id >= TileIdCount; id--)
        {
            if (_bodies[id] == NoBody)
            {
                _ = bodies.CreateBodyWithID(new BodyID(id), creation) ?? throw new InvalidOperationException($"Jolt refused body {id}.");
                _bodies[id] = ParkedBody;
            }
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
            RemoveStaticBody(prop.Id);
            prop.Shape.Dispose();
        }
        _propIdUsed[prop.Id - TileIdCount] = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _closing = true;
        foreach (TileBuild building in _pending.Values)
        {
            building.Worker.Wait();
            building.Take()?.Dispose();
        }
        _pending.Clear();
        lock (JoltRuntime.Gate)
        {
            // Jolt frees every body still made (in the world or parked) with the physics system, and with them their
            // hold on the shapes; the shapes' own handles go here.
            _disposed = true;
            foreach (Prop prop in _props.Values)
            {
                prop.Shape.Dispose();
            }
            _props.Clear();
            foreach (Tile tile in _tiles.Values)
            {
                tile.Shape.Dispose();
            }
            _tiles.Clear();
            _parked.Dispose();
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

    // Set when the world closes: tile builds not started yet don't start.
    private volatile bool _closing;

    /// <summary>
    /// A tile being built ahead: a worker builds it when it gets to it, and a tick that needs it sooner takes it over
    /// and builds it itself if no worker has started it, rather than wait behind every tile queued before it.
    /// </summary>
    private sealed class TileBuild
    {
        private readonly Lazy<Shape?> _shape;

        public TileBuild(Func<Shape?> build)
        {
            _shape = new Lazy<Shape?>(build, LazyThreadSafetyMode.ExecutionAndPublication);
            Worker = Task.Run(() => _shape.Value);
        }

        public Task Worker { get; }

        public bool IsBuilt => _shape.IsValueCreated;

        /// <summary>The shape: built here and now if no worker has started on it, else waited for.</summary>
        public Shape? Take() => _shape.Value;
    }
}
