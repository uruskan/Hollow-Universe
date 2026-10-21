using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>
/// Retained entries include unfinished and empty tiles. Payload bytes count completed object elements and index
/// key/value data; they exclude collection capacity/overhead and arrays retained by callers. ActiveBuilds includes
/// work whose entry was evicted: callers, rather than this synchronous cache, bound their outstanding work.
/// </summary>
public readonly record struct NaturalObjectCacheDiagnostics(int Capacity, int Entries, int CompletedEntries,
    int PendingEntries, int EmptyEntries, int RetainedObjects, long RetainedPayloadBytes,
    long Hits, long Misses, long Evictions, int Builds, int ActiveBuilds);

/// <summary>
/// The natural objects of a surface with a scatter (GROUND.md §6.1): the real planet's or the walking-scale ground's
/// (<see cref="IScatterField"/>, <see cref="Scatter"/>, at the surface's scatter level), the same trees, berry bushes,
/// ferns, boulders and outcrops the client draws, under the same IDs on the server and every client. gather.json maps scatter kinds onto gameplay kinds (<see cref="NaturalKind.ScatterKinds"/>); scatter
/// objects of other kinds (plain bushes, small stones) are scenery and not here.
/// <para>
/// <b>IDs</b> (ARCHITECTURE.md §7: tile and index): 4 bits tag, 3 bits cube face, 16 bits each of the scatter tile's
/// X and Y (level 16 on the real planet, 5 on the first world's walking-scale ground), 8 bits the scatter kind and 17
/// bits the candidate cell. An ID from outside is untrusted: one that doesn't
/// decode to a tile of this zone, a mapped kind and an object that exists is simply not found, and
/// <see cref="TryGetNear"/> refuses a far one before generating anything.
/// </para>
/// Scatter tiles are generated on demand (about 15 ms each) and cached, at most <see cref="MaxTiles"/> of them, least
/// recently used first out. Thread-safe.
/// </summary>
public sealed class ScatterNaturalObjects : IPlannedNaturalCollision
{
    /// <summary>The top four bits of every ID this provider makes.</summary>
    public const ulong Tag = 0xAUL << 60;

    /// <summary>Maximum retained entries, including pending builds and empty results.</summary>
    public const int MaxTiles = 384;

    private const int KindShift = 17;
    private const ulong CandidateMask = (1UL << KindShift) - 1;

    private readonly IScatterField _surface;
    private readonly int _level;
    private readonly ZoneFrame _frame;
    private readonly (NaturalKind Kind, float MinScale)?[] _kinds = new (NaturalKind, float)?[256];
    private readonly object _cacheGate = new();
    private readonly object _collisionIdentity = new();
    private readonly Dictionary<ulong, CacheEntry> _tiles = new();
    private readonly LinkedList<ulong> _recency = new();
    private readonly double _tileEdge;
    private long _hits, _misses, _evictions;
    private int _activeBuilds;

    private sealed class CacheEntry(ulong key, Func<TileObjects> build)
    {
        public readonly LinkedListNode<ulong> Node = new(key);
        public readonly Lazy<TileObjects> Value = new(build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private sealed class TileObjects(NaturalObject[] objects, Dictionary<ulong, int> byId)
    {
        public readonly NaturalObject[] Objects = objects;
        public readonly Dictionary<ulong, int> ById = byId;
    }

    public ScatterNaturalObjects(IScatterField surface, ZoneFrame frame, GameContent content)
    {
        _surface = surface;
        _level = surface.ScatterPlan.Level;
        _frame = frame;
        foreach (NaturalKind kind in content.NaturalKinds)
        {
            foreach (ScatterKind s in kind.ScatterKinds)
            {
                _kinds[(int)s] = (kind, kind.ScatterMinScale);
            }
        }
        _tileEdge = TileKey.EdgeLength(_level, surface.Radius);
    }

    /// <summary>Scatter tiles retained now, including pending builds.</summary>
    public int CachedTiles { get { lock (_cacheGate) return _tiles.Count; } }

    public NaturalObjectCacheDiagnostics CacheDiagnostics
    {
        get
        {
            lock (_cacheGate)
            {
                int completed = 0, empty = 0, objects = 0;
                long indexBytes = 0;
                foreach (CacheEntry entry in _tiles.Values)
                {
                    if (!entry.Value.IsValueCreated) continue;
                    TileObjects tile = entry.Value.Value;
                    completed++;
                    if (tile.Objects.Length == 0) empty++;
                    objects += tile.Objects.Length;
                    indexBytes += (long)tile.ById.Count * (sizeof(ulong) + sizeof(int));
                }
                return new(MaxTiles, _tiles.Count, completed, _tiles.Count - completed, empty, objects,
                    (long)objects * Unsafe.SizeOf<NaturalObject>() + indexBytes, _hits, _misses, _evictions,
                    Generated, Volatile.Read(ref _activeBuilds));
            }
        }
    }

    /// <summary>Scatter tiles generated so far (again after leaving the cache too), for statistics.</summary>
    public int Generated => Volatile.Read(ref _generated);

    private int _generated;

    /// <summary>The ID of a scatter object of a tile.</summary>
    public static ulong IdOf(TileKey tile, int index)
    {
        ulong kind = (ulong)(index >> 24) & 0xFF;
        ulong candidate = (ulong)(index & 0xFFFFFF);
        return Tag | (ulong)tile.Face << 57 | (ulong)(uint)tile.X << 41 | (ulong)(uint)tile.Y << 25 | kind << KindShift | (candidate & CandidateMask);
    }

    /// <summary>The scatter tile (of a scatter level, the real planet's by default) and item index an ID names, or null if it can't be one of ours.</summary>
    public static (TileKey Tile, int Index)? Decode(ulong id, int level = Scatter.Level)
    {
        if ((id & (0xFUL << 60)) != Tag)
        {
            return null;
        }
        int face = (int)((id >> 57) & 7);
        if (face >= CubeSphere.FaceCount)
        {
            return null;
        }
        int x = (int)((id >> 41) & 0xFFFF), y = (int)((id >> 25) & 0xFFFF);
        int kind = (int)((id >> KindShift) & 0xFF);
        int candidate = (int)(id & CandidateMask);
        // Untrusted: coordinates outside the face name nothing (never an exception, docs/WHOLE_WORLD.md §3.2).
        return TileKey.Checked(face, level, x, y) is { } tile ? (tile, kind << 24 | candidate) : null;
    }

    public void Query(Vector3 centre, float radius, List<NaturalObject> into)
    {
        if (!(radius > 0))
        {
            return;
        }
        float r2 = radius * radius;
        foreach (TileKey key in TilesCovering(centre, radius))
        {
            foreach (NaturalObject o in Tile(key).Objects)
            {
                if (Vector3.DistanceSquared(o.Position, centre) <= r2)
                {
                    into.Add(o);
                }
            }
        }
    }

    public bool TryGet(ulong id, out NaturalObject found) => TryGetNear(id, Vector3.Zero, float.PositiveInfinity, out found);

    public void QueryAlong(Vector3 from, Vector3 to, float rootReach, List<NaturalObject> into)
    {
        if (!NaturalSegmentQuery.Valid(from, to, rootReach)) return;
        if (_surface is ISurface bounded)
            NaturalSegmentQuery.Query(this, bounded, _frame, from, to, rootReach, into);
        else
        {
            // Custom scatter fields need not publish height bounds; retain their ordinary enclosing-sphere query.
            if (NaturalSegmentQuery.EnclosingSphere(from, to, rootReach, out Vector3 centre, out float radius))
                Query(centre, radius, into);
        }
    }

    public int Level => _level;

    public IReadOnlyList<NaturalObject> InTile(TileKey tile) => tile.Level == _level ? Tile(tile).Objects : Array.Empty<NaturalObject>();

    public TileKey? TileOf(ulong id) => Decode(id, _level)?.Tile;

    public NaturalCollisionPlan PrepareCollision(ReadOnlySpan<TileKey> completeRootTiles, NaturalCollisionLimits limits,
        CancellationToken cancellation = default)
    {
        ScatterPlan scatter = _surface.ScatterPlan;
        NaturalCollisionReservation reservation = NaturalCollisionPlan.Estimate(scatter, completeRootTiles.Length, limits);
        if (scatter.Level != _level) throw new FormationValidationException("The source scatter plan changed after provider construction.");
        cancellation.ThrowIfCancellationRequested();
        var tiles = completeRootTiles.ToArray();
        Array.Sort(tiles, static (a, b) => a.Packed.CompareTo(b.Packed));
        for (int i = 0; i < tiles.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (tiles[i].Level != _level || (i > 0 && tiles[i] == tiles[i - 1]))
                throw new FormationValidationException("Natural collision requires unique tiles at the source scatter level.");
        }
        // Freeze only scalar physical inputs. No content definitions escape this bounded source generation.
        var kinds = new NaturalCollisionKind[_kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
            if (_kinds[i] is var (kind, minScale) && kind.IsSolid)
            {
                if (!float.IsFinite(minScale) || minScale < 0 || !float.IsFinite(kind.Radius) || kind.Radius <= 0 ||
                    !float.IsFinite(kind.Height) || kind.Height < 0)
                    throw new FormationValidationException("Natural collision content has invalid primitive dimensions.");
                kinds[i] = new(true, kind.Index, kind.Class, minScale, kind.Radius, kind.Height);
            }
        var entries = new NaturalCollisionEntry[reservation.OutputCapacity];
        long candidates = Scatter.CandidateCount(scatter);
        var generation = new ScatterGenerationLimits { MaxCandidateVisits = candidates, MaxItems = checked((int)candidates),
            MaxEstimatedBytes = Scatter.EstimatedBuildBytes(checked((int)candidates)) };
        int count = 0;
        foreach (TileKey tile in tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_surface.ScatterPlan != scatter) throw new FormationValidationException("Natural collision source scatter plan changed during preparation.");
            // Deliberately bypass the LRU: neither existing cached arrays nor evicted caller-held work are a budget.
            foreach (ScatterItem item in Scatter.ForTile(_surface, tile, generation, cancellation))
            {
                cancellation.ThrowIfCancellationRequested();
                NaturalCollisionKind kind = kinds[(int)item.Kind];
                if (!kind.Solid || item.Scale < kind.MinScale) continue;
                if (count == entries.Length) throw new FormationCapacityException("Natural collision solid output exceeds the complete-request allowance.");
                NaturalObject value = ObjectOf(tile, item, kind.Index);
                entries[count++] = new(value, NaturalCollisionShape.From(value, kind));
            }
        }
        cancellation.ThrowIfCancellationRequested();
        if (_surface.ScatterPlan != scatter) throw new FormationValidationException("Natural collision source scatter plan changed during preparation.");
        return new(_collisionIdentity, tiles, entries, count, reservation);
    }

    public NaturalCollisionSnapshot CaptureDepletion(NaturalCollisionPlan plan, NaturalState state, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(state);
        if (!ReferenceEquals(plan.SourceIdentity, _collisionIdentity) || !ReferenceEquals(state.Objects, this))
            throw new FormationValidationException("Natural collision plan/state belong to a different provider.");
        cancellation.ThrowIfCancellationRequested();
        return NaturalCollisionSnapshot.Capture(plan, state, cancellation);
    }

    public bool IsCurrent(NaturalCollisionSnapshot snapshot, NaturalState state)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(state);
        return ReferenceEquals(state.Objects, this) && snapshot.Matches(_collisionIdentity, state);
    }

    /// <summary>The object with an ID if its scatter tile is generated already (never generates one: for a frame's work).</summary>
    public bool TryGetCached(ulong id, out NaturalObject found)
    {
        found = default;
        if (Decode(id, _level) is not var (tile, _)) return false;
        TileObjects objects;
        lock (_cacheGate)
        {
            if (!_tiles.TryGetValue(tile.Packed, out CacheEntry? entry) || !entry.Value.IsValueCreated) return false;
            objects = entry.Value.Value;
        }
        if (!objects.ById.TryGetValue(id, out int at)) return false;
        found = objects.Objects[at];
        return true;
    }

    public bool TryGetNear(ulong id, Vector3 point, float radius, out NaturalObject found)
    {
        found = default;
        if (Decode(id, _level) is not var (tile, index) || _kinds[index >> 24] == null)
        {
            return false;
        }
        // The tile must lie near the point: checked on its centre, before generating anything.
        Vector3 centre = _frame.ToLocal(tile.Centre * _surface.Radius);
        float reach = (float)(_tileEdge * 0.75);
        if (Vector3.Distance(centre, point) > radius + reach + 100)
        {
            return false;
        }
        TileObjects objects = Tile(tile);
        if (!objects.ById.TryGetValue(id, out int at))
        {
            return false;
        }
        found = objects.Objects[at];
        return Vector3.Distance(found.Position, point) <= radius;
    }

    // The scatter tiles within a reach of a point, in a fixed order (by key).
    private List<TileKey> TilesCovering(Vector3 centre, float radius)
    {
        var tiles = new List<TileKey>();
        TileKey.Around(_frame.ToBody(centre), radius, _surface.Radius, _level, tiles);
        tiles.Sort((a, b) => a.Packed.CompareTo(b.Packed));
        return tiles;
    }

    private TileObjects Tile(TileKey key)
    {
        // One finite LRU budget includes pending and negative entries. Eviction never waits for a build, and no
        // overflow queue/side table retains it. An evicted reader keeps its own entry alive and may share a duplicate
        // with a later request; navigation/physics/view owners bound and drain those caller-owned jobs.
        CacheEntry entry;
        lock (_cacheGate)
        {
            if (_tiles.TryGetValue(key.Packed, out CacheEntry? existing))
            {
                entry = existing;
                _hits++;
                _recency.Remove(entry.Node);
                _recency.AddLast(entry.Node);
            }
            else
            {
                _misses++;
                if (_tiles.Count == MaxTiles)
                {
                    _tiles.Remove(_recency.First!.Value);
                    _recency.RemoveFirst();
                    _evictions++;
                }
                entry = new CacheEntry(key.Packed, () =>
                {
                    Interlocked.Increment(ref _activeBuilds);
                    try { return Generate(TileKey.FromPacked(key.Packed)!.Value); }
                    finally { Interlocked.Decrement(ref _activeBuilds); }
                });
                _tiles.Add(key.Packed, entry);
                _recency.AddLast(entry.Node);
            }
        }
        try { return entry.Value.Value; }
        catch
        {
            lock (_cacheGate)
            {
                // A failed Lazy is retryable; a late failure cannot erase a replacement admitted after eviction.
                if (_tiles.TryGetValue(key.Packed, out CacheEntry? current) && ReferenceEquals(current, entry))
                {
                    _tiles.Remove(key.Packed);
                    _recency.Remove(entry.Node);
                }
            }
            throw;
        }
    }

    private TileObjects Generate(TileKey key)
    {
        Interlocked.Increment(ref _generated);
        IReadOnlyList<ScatterItem> items = Scatter.ForTile(_surface, key);
        var objects = new List<NaturalObject>();
        var byId = new Dictionary<ulong, int>();
        foreach (ScatterItem item in items)
        {
            if (_kinds[(int)item.Kind] is not var (kind, minScale) || item.Scale < minScale)
            {
                continue;
            }
            ulong id = IdOf(key, item.Index);
            byId[id] = objects.Count;
            objects.Add(ObjectOf(key, item, kind.Index));
        }
        return new TileObjects(objects.ToArray(), byId);
    }

    private NaturalObject ObjectOf(TileKey tile, in ScatterItem item, ushort kind) => new()
    {
        Id = IdOf(tile, item.Index), Kind = kind, Position = _frame.ToLocal(item.Position(_surface.Radius)),
        Up = Vector3.Normalize(_frame.DirectionToLocal(item.Direction)), Scale = item.Scale, Yaw = item.Yaw,
    };
}
