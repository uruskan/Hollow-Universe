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
/// <b>Ground tiles</b> are the world's cube-sphere tiles at <see cref="Level"/>, the level whose edge is nearest
/// <see cref="TileSize"/> metres on this world (docs/WHOLE_WORLD.md §6.1: 59 m on a 2.41 km world), each a static mesh
/// body built from <see cref="ISurface.GroundCollision"/>. A tile's body ID is its place among its level's tiles, so the
/// server and every client have the same bodies under the same IDs whatever else they hold. Jolt sorts contacts by
/// body ID, so this keeps a character's movement identical on both sides where tiles meet.
/// </para>
/// </summary>
public sealed partial class PhysicsWorld : IDisposable
{
    /// <summary>The edge, in metres, a ground tile's level is chosen for.</summary>
    public const float TileSize = GroundCollisionLayout.TileMetres;

    /// <summary>The deepest level ground tiles take, so every tile of the world has its own body ID (6 × 4⁶ of them).</summary>
    public const int MaxTileLevel = GroundCollisionLayout.MaximumLevel;

    /// <summary>A ground tile keeps the surface's triangles this far beyond its edges (neighbours overlap by this much).</summary>
    public const float ClipMargin = GroundCollisionLayout.MarginMetres;

    /// <summary>How far round a point <see cref="EnsureGround"/> makes sure of the ground, and <see cref="Prefetch"/> builds ahead.</summary>
    public const float EnsureReach = 64f, PrefetchReach = 128f;

    internal static readonly ObjectLayer StaticLayer = new(0);
    internal static readonly ObjectLayer MovingLayer = new(1);

    private const int TileIdCount = 6 << (2 * MaxTileLevel);

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

    private readonly Dictionary<TileKey, Tile> _tiles = new();
    private readonly Dictionary<TileKey, TileBuild> _pending = new();
    private readonly List<TileKey> _expired = new();
    private readonly List<TileKey> _adopted = new();
    private readonly NearTiles _near;
    private readonly Dictionary<(PropLayer, ulong), Prop> _props = new();
    private readonly Dictionary<uint, (PropLayer Layer, ulong Key)> _propOf = new();
    private readonly bool[] _propIdUsed = new bool[MaxProps];
    private int _tilesBuilt;
    private long _buildTicks;
    private readonly NativeSetup _nativeSetup;
    private long _epoch;
    private bool _disposed;

    // Jolt gives a body the ID asked for (CreateBodyWithID) by walking its list of free IDs, and with the IDs this world
    // uses (tiles by grid position, props by hash: far apart) that walk cost 20–60 µs a body, so a tile's trees took
    // 2–3 ms to add (GROUND.md §5.11). So a body, once made, is kept: taken out of the world it leaves the broadphase
    // and is parked on a tiny shared shape; put back, it takes its new shape and pose exactly as a new body would.
    private const byte NoBody = 0, ParkedBody = 1, BodyInWorld = 2;
    private readonly byte[] _bodies = new byte[FirstShipBody + MaxShipBodies];
    private readonly Shape _parked;

    public PhysicsWorld(ISurface surface, ZoneFrame frame) : this(surface, frame, CreateLegacySetup(surface)) { }

    private PhysicsWorld(ISurface surface, ZoneFrame frame, NativeSetup setup)
    {
        Surface = surface;
        Frame = frame;
        Level = LevelOn(surface.Radius);
        _near = new NearTiles(surface.Radius, Level);
        _nativeSetup = setup; _parked = setup.Parked; Jolt = setup.System;
    }

    public ISurface Surface { get; private set; }

    public ZoneFrame Frame { get; }

    /// <summary>The cube-sphere level of the ground tiles on this world.</summary>
    public int Level { get; }

    /// <summary>The ground tiles' level on a world of this radius (what <see cref="Level"/> is; for code without a world).</summary>
    public static int LevelOn(double radius) => GroundCollisionLayout.LevelOn(radius);

    internal PhysicsSystem Jolt { get; }

    /// <summary>Times the ground's work on the owning thread (the server's tick; null in a client's prediction).</summary>
    public TickProfile? Profile { get; set; }

    /// <summary>Ground tiles currently in the world.</summary>
    public int TileCount => _selectedGround?.ResidentCount ?? _tiles.Count;

    /// <summary>Ground tiles built so far, and the time that took (on whatever thread), for statistics.</summary>
    public int TilesBuilt => Volatile.Read(ref _tilesBuilt);

    public double TileBuildMilliseconds => Interlocked.Read(ref _buildTicks) * 1000.0 / Stopwatch.Frequency;

    /// <summary>The ground tile holding a point (its direction from the centre).</summary>
    public TileKey TileOf(Vector3 position) => TileKey.At(Frame.ToBody(position), Level);

    // The tiles within a reach of a point (on the owning thread). Every character on foot asks for 64 and 128 m every
    // tick, on the server and again in its client's prediction and every replay of it, so the answers are kept by the
    // fine cell the point lies in (NearTiles).
    private ReadOnlySpan<TileKey> TilesNear(Vector3 position, float reach) => _near.Around(Frame.ToBody(position), reach);

    /// <summary>
    /// Makes sure the ground exists within <see cref="EnsureReach"/> of <paramref name="position"/>, and marks those
    /// tiles as in use this epoch. Tiles built ahead by <see cref="Prefetch"/> are taken in; a tile still missing is
    /// built now (or waited for, if already on its way).
    /// </summary>
    public void EnsureGround(Vector3 position) => EnsureGroundWithin(position, EnsureReach);

    /// <summary><see cref="EnsureGround"/> within a reach of its own, in metres.</summary>
    public void EnsureGroundWithin(Vector3 position, float reach)
    {
        RequireLegacyGround();
        AdoptPrefetched();
        foreach (TileKey tile in TilesNear(position, reach))
        {
            EnsureTile(tile);
        }
    }

    /// <summary>
    /// Makes sure the ground exists wherever something within <paramref name="reach"/> metres of a point could touch
    /// it, natural props included (<see cref="PropReach"/>): the tiles a ship's queries of this tick can meet. Marks
    /// them in use.
    /// </summary>
    public void EnsureGroundAround(Vector3 position, float reach) => EnsureGroundWithin(position, reach + PropReach);

    /// <summary>
    /// How far a natural prop's shape reaches across the ground from its base, the point that puts it in a tile: a
    /// boulder's 0.95 m radius at scale up to 2.45 (gather.json, <c>Scatter</c>) is the most, 2.3 m.
    /// </summary>
    public float PropReach { get; set; } = 3f;

    private void EnsureTile(TileKey key)
    {
        if (!_tiles.TryGetValue(key, out Tile? tile))
        {
            Shape shape;
            using (Profile.Time(ProfileSection.TileWait))
            {
                // On its way: taken over and built here if no worker has started it yet (it may be queued behind
                // hundreds of others in a respawn storm), else waited for.
                shape = _pending.Remove(key, out TileBuild? building) ? building.Take()! : BuildShape(key);
            }
            TilesBuiltOnDemand++;
            using (Profile.Time(ProfileSection.TileAdd))
            {
                tile = AddTile(key, shape);
                Listener?.TileAdded(key);
            }
        }
        tile.LastUsed = _epoch;
    }

    /// <summary>
    /// Starts building, on worker threads, the tiles within <see cref="PrefetchReach"/> of a point that don't exist
    /// yet, so that <see cref="EnsureGround"/> finds them ready and a tick never waits for a tile (about 10 ms each).
    /// Tiles already there are marked as in use.
    /// </summary>
    public void Prefetch(Vector3 position) => PrefetchWithin(position, PrefetchReach);

    /// <summary><see cref="Prefetch"/> within a reach of its own, in metres.</summary>
    public void PrefetchWithin(Vector3 position, float reach)
    {
        RequireLegacyGround();
        using TickProfile.Scope timed = Profile.Time(ProfileSection.Prefetch);
        foreach (TileKey tile in TilesNear(position, reach))
        {
            PrefetchTile(tile);
        }
    }

    /// <summary>
    /// <see cref="Prefetch"/> for the tiles <see cref="EnsureGroundAround"/> would want at a point: the ground a ship
    /// will need along its way, built before it gets there.
    /// </summary>
    public void PrefetchAround(Vector3 position, float reach) => PrefetchWithin(position, reach + PropReach);

    /// <summary>
    /// <see cref="PrefetchAround"/> all along a segment: points half a tile apart, each reach widened by half that, so
    /// together they cover every point within <paramref name="reach"/> of the segment.
    /// </summary>
    public void PrefetchAlong(Vector3 from, Vector3 to, float reach)
    {
        RequireLegacyGround();
        const float step = TileSize / 2;
        float length = Vector3.Distance(from, to);
        int n = Math.Max(1, (int)MathF.Ceiling(length / step));
        for (int i = 0; i <= n; i++)
        {
            PrefetchAround(Vector3.Lerp(from, to, (float)i / n), reach + step / 2);
        }
    }

    /// <summary>
    /// Whether the ground within <see cref="EnsureReach"/> of a point is ready: in the world, or built and waiting to
    /// be taken in. What isn't on its way yet is started (<see cref="Prefetch"/>). A respawn or an arrival waits for
    /// this, so the tick that brings the player in never builds ground itself.
    /// </summary>
    public bool GroundReady(Vector3 position)
    {
        RequireLegacyGround();
        PrefetchWithin(position, EnsureReach);
        foreach (TileKey tile in TilesNear(position, EnsureReach))
        {
            if (!_tiles.ContainsKey(tile) && !(_pending.TryGetValue(tile, out TileBuild? build) && build.IsBuilt))
            {
                return false;
            }
        }
        return true;
    }

    private void PrefetchTile(TileKey key)
    {
        if (_pending.ContainsKey(key))
        {
            return;
        }
        if (_tiles.TryGetValue(key, out Tile? tile))
        {
            // Near someone: keep it.
            tile.LastUsed = _epoch;
            return;
        }
        StartBuild(key);
    }

    // Apart from PrefetchTile: a lambda capturing the key there would allocate its closure on every call (every
    // character asks for a few dozen tiles a tick), not only for a tile that is started.
    private void StartBuild(TileKey key)
    {
        ITileListener? listener = Listener;
        _pending.Add(key, new TileBuild(() =>
        {
            if (_closing)
            {
                return null;
            }
            listener?.TilePreparing(key);
            return BuildShape(key);
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
        foreach ((TileKey key, TileBuild build) in _pending)
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
        foreach (TileKey key in _adopted)
        {
            using TickProfile.Scope _ = Profile.Time(ProfileSection.TileAdd);
            _pending.Remove(key, out TileBuild? build);
            Tile tile = AddTile(key, build!.Take()!);
            tile.LastUsed = _epoch;
            Listener?.TileAdded(key);
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
        RequireLegacyGround();
        _epoch++;
        _expired.Clear();
        foreach ((TileKey key, Tile tile) in _tiles)
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
            foreach (TileKey key in _expired)
            {
                using TickProfile.Scope _ = Profile.Time(ProfileSection.TileRemove);
                Listener?.TileRemoved(key);
                Tile tile = _tiles[key];
                RemoveStaticBody(tile.Body.ID);
                tile.Shape.Dispose();
                _tiles.Remove(key);
            }
        }
    }

    /// <summary>The triangles of a tile, as the surface gives them (also what the client draws as placeholder ground).</summary>
    public TriangleMesh TileMesh(TileKey tile)
    { RequireLegacyGround(); return Surface.GroundCollision(Frame, tile, ClipMargin); }

    // The mesh shape of a tile: the surface's triangles over the tile and its margin, then Jolt's tree over them.
    // Thread-safe (a pure function of the surface, and Jolt builds shapes without the shared allocator), so prefetching
    // runs it on worker threads.
    private Shape BuildShape(TileKey tile)
    {
        long start = Stopwatch.GetTimestamp();
        // Pooled, as the arrays below: these are large-object-heap sized, and tiles come and go all the time.
        using PooledTriangles mesh = Surface.GroundCollisionPooled(Frame, tile, ClipMargin);
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
                triangles[t] = new IndexedTriangle(mesh.Indices[3 * t], mesh.Indices[3 * t + 1], mesh.Indices[3 * t + 2], 0, 0);
            }
            using var settings = new MeshShapeSettings(vertices.AsSpan(0, mesh.VertexCount), triangles.AsSpan(0, mesh.TriangleCount)).Owned();
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
    /// streaming world, in about 0.1–0.3 ms where a whole rebuild took 5–10 ms with 30,000 bodies (GROUND.md §5.11).
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
                    using var creation = new BodyCreationSettings(shape, position, rotation, MotionType.Static, StaticLayer).Owned();
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

    // Adds a built tile to the physics system, under the tile's own body ID (its place among its level's tiles). On the
    // owning thread.
    private Tile AddTile(TileKey key, Shape shape)
    {
        var id = new BodyID((uint)key.Index);
        AddStaticBody(id.ID, shape, Vector3.Zero, Quaternion.Identity);
        var tile = new Tile(id, shape);
        _tiles.Add(key, tile);
        return tile;
    }

    // ---------------------------------------------------------------- props

    /// <summary>Told when ground tiles come and go, on the owning thread (natural objects follow the tiles).</summary>
    private ITileListener? _listener;
    public ITileListener? Listener
    {
        get => _listener;
        set
        {
            if (_selectedMode && value != null) throw new NotSupportedException("Selected ground requires transactional natural-body ownership before listeners can attach.");
            _listener = value;
        }
    }

    /// <summary>Whether a ground tile is in the world now.</summary>
    public bool HasTile(TileKey tile) => _selectedGround?.HasTile(tile) ?? _tiles.ContainsKey(tile);

    /// <summary>The ground tiles in the world now (the developer panel draws them).</summary>
    public IEnumerable<TileKey> Tiles => _selectedGround?.Tiles ?? _tiles.Keys;

    /// <summary>Props in the world now.</summary>
    public int PropCount => _props.Count;

    public bool HasProp(PropLayer layer, ulong key) => _props.ContainsKey((layer, key));

    /// <summary>The prop a body belongs to (a tree's or a deployable's), by its layer and key; false for any other body.</summary>
    public bool TryPropOf(uint body, out PropLayer layer, out ulong key)
    {
        if (_propOf.TryGetValue(body, out (PropLayer Layer, ulong Key) prop))
        {
            (layer, key) = prop;
            return true;
        }
        (layer, key) = (default, 0);
        return false;
    }

    /// <summary>
    /// Adds a static prop (a tree, an outcrop, a box) under a key; nothing if the key is there already. Its body ID
    /// comes after the ground tiles', so a tile is always ordered before a prop in Jolt's contact lists.
    /// </summary>
    public void AddProp(PropLayer layer, ulong key, in PropShape prop)
    {
        if (_selectedMode) throw new NotSupportedException("Selected natural/prop collision has no admitted transaction yet.");
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
            _propOf[id] = (layer, key);
        }
    }

    private bool _propBodiesMade;

    /// <summary>Whether the first prop makes every prop body ahead (see <see cref="MakePropBodies"/>); tests compare without.</summary>
    internal bool MakePropBodiesAhead { get; set; } = true;

    // Props' IDs are spread over their whole range by hash, so even a prop's first body would walk Jolt's free list.
    // With the first prop, every prop body is made at once, parked, from the top of the range down: each new ID is
    // then at the head of the list (65,536 bodies, 9 MB, 20–50 ms once). Ground tiles and building pieces are made as
    // they come (a tile walks the tiles' part of the list the first time, at most about 30 µs).
    private void MakePropBodies()
    {
        _propBodiesMade = true;
        using var creation = new BodyCreationSettings(_parked, Vector3.Zero, Quaternion.Identity, MotionType.Static, StaticLayer).Owned();
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
        _propOf.Remove(prop.Id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        if (_selectedMode) { DisposeSelected(); return; }
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Drain physics workers outside the Jolt gate before disposing their world.");
        _closing = true;
        List<Exception>? failures = null;
        foreach (TileBuild building in _pending.Values)
        {
            try
            {
                // Drain every worker before destroying the world, even if an earlier build failed. Never wait
                // under the Jolt gate: other worlds may need it while their surface work is still running.
                building.Worker.GetAwaiter().GetResult();
                Release(building.Take());
            }
            catch (Exception error)
            {
                (failures ??= new()).Add(error);
            }
        }
        _pending.Clear();
        lock (JoltRuntime.Gate)
        {
            // Jolt frees every body still made (in the world or parked) with the physics system, and with them their
            // hold on the shapes; the shapes' own handles go here.
            _disposed = true;
            foreach (Prop prop in _props.Values)
            {
                Release(prop.Shape);
            }
            _props.Clear();
            foreach (Tile tile in _tiles.Values)
            {
                Release(tile.Shape);
            }
            _tiles.Clear();
            Release(_nativeSetup);
        }
        if (failures != null)
        {
            throw new AggregateException("Physics world cleanup failed after draining its tile builds.", failures).Flatten();
        }

        void Release(IDisposable? resource)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= new()).Add(error);
            }
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
