using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>
/// The natural objects of a real surface (GROUND.md §6.1): the planet's own scatter (<see cref="Scatter"/>), the same
/// trees, berry bushes, ferns, boulders and outcrops the client draws, under the same IDs on the server and every
/// client. gather.json maps scatter kinds onto gameplay kinds (<see cref="NaturalKind.ScatterKinds"/>); scatter
/// objects of other kinds (plain bushes, small stones) are scenery and not here.
/// <para>
/// <b>IDs</b> (ARCHITECTURE.md §7: tile and index): 4 bits tag, 3 bits cube face, 16 bits each of the level-16 tile's
/// X and Y, 8 bits the scatter kind and 17 bits the candidate cell. An ID from outside is untrusted: one that doesn't
/// decode to a tile of this zone, a mapped kind and an object that exists is simply not found, and
/// <see cref="TryGetNear"/> refuses a far one before generating anything.
/// </para>
/// Scatter tiles are generated on demand (about 15 ms each) and cached, at most <see cref="MaxTiles"/> of them, least
/// recently used first out. Thread-safe.
/// </summary>
public sealed class ScatterNaturalObjects : INaturalObjects
{
    /// <summary>The top four bits of every ID this provider makes.</summary>
    public const ulong Tag = 0xAUL << 60;

    /// <summary>Scatter tiles kept generated at most.</summary>
    public const int MaxTiles = 384;

    private const int KindShift = 17;
    private const ulong CandidateMask = (1UL << KindShift) - 1;

    private readonly PlanetSurface _surface;
    private readonly ZoneFrame _frame;
    private readonly (NaturalKind Kind, float MinScale)?[] _kinds = new (NaturalKind, float)?[256];
    private readonly ConcurrentDictionary<ulong, Lazy<TileObjects>> _tiles = new();
    private readonly double _tileEdge;
    private long _clock;

    private sealed class TileObjects(NaturalObject[] objects, Dictionary<ulong, int> byId)
    {
        public readonly NaturalObject[] Objects = objects;
        public readonly Dictionary<ulong, int> ById = byId;
        public long LastUsed;
    }

    public ScatterNaturalObjects(PlanetSurface surface, ZoneFrame frame, GameContent content)
    {
        _surface = surface;
        _frame = frame;
        foreach (NaturalKind kind in content.NaturalKinds)
        {
            foreach (ScatterKind s in kind.ScatterKinds)
            {
                _kinds[(int)s] = (kind, kind.ScatterMinScale);
            }
        }
        _tileEdge = TileKey.EdgeLength(Scatter.Level, surface.Radius);
    }

    /// <summary>Scatter tiles generated and kept now.</summary>
    public int CachedTiles => _tiles.Count;

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

    /// <summary>The scatter tile and item index an ID names, or null if it can't be one of ours.</summary>
    public static (TileKey Tile, int Index)? Decode(ulong id)
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
        return (new TileKey(face, Scatter.Level, x, y), kind << 24 | candidate);
    }

    public void Query(float minX, float minZ, float maxX, float maxZ, List<NaturalObject> into)
    {
        if (maxX <= minX || maxZ <= minZ)
        {
            return;
        }
        foreach (TileKey key in TilesCovering(minX, minZ, maxX, maxZ))
        {
            foreach (NaturalObject o in Tile(key).Objects)
            {
                if (o.Position.X >= minX && o.Position.X < maxX && o.Position.Z >= minZ && o.Position.Z < maxZ)
                {
                    into.Add(o);
                }
            }
        }
    }

    public bool TryGet(ulong id, out NaturalObject found) => TryGetNear(id, Vector3.Zero, float.PositiveInfinity, out found);

    /// <summary>The object with an ID if its scatter tile is generated already (never generates one: for a frame's work).</summary>
    public bool TryGetCached(ulong id, out NaturalObject found)
    {
        found = default;
        if (Decode(id) is not var (tile, _) || !_tiles.TryGetValue(tile.Packed, out Lazy<TileObjects>? entry) || !entry.IsValueCreated ||
            !entry.Value.ById.TryGetValue(id, out int at))
        {
            return false;
        }
        found = entry.Value.Objects[at];
        return true;
    }

    public bool TryGetNear(ulong id, Vector3 point, float radius, out NaturalObject found)
    {
        found = default;
        if (Decode(id) is not var (tile, index) || _kinds[index >> 24] == null)
        {
            return false;
        }
        // The tile must lie in this zone, and near the point: checked on its centre, before generating anything.
        Vector3 centre = _frame.ToLocal(tile.Centre * _surface.Radius);
        float reach = (float)(_tileEdge * 0.75);
        if (MathF.Abs(centre.X) > PhysicsWorld.Extent + reach || MathF.Abs(centre.Z) > PhysicsWorld.Extent + reach ||
            Vector2.Distance(new Vector2(centre.X, centre.Z), new Vector2(point.X, point.Z)) > radius + reach)
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

    // The scatter tiles a rectangle of the tangent plane touches, in a fixed order (by key). Sampled at a third of a
    // tile, since tiles are turned against the zone's axes and vary in size.
    private List<TileKey> TilesCovering(float minX, float minZ, float maxX, float maxZ)
    {
        var keys = new SortedSet<ulong>();
        double step = _tileEdge / 3;
        int nx = Math.Max(1, (int)Math.Ceiling((maxX - minX) / step)), nz = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / step));
        for (int j = 0; j <= nz; j++)
        {
            for (int i = 0; i <= nx; i++)
            {
                double x = minX + (double)(maxX - minX) * i / nx, z = minZ + (double)(maxZ - minZ) * j / nz;
                keys.Add(TileKey.At(_frame.ToBody(x, 0, z), Scatter.Level).Packed);
            }
        }
        var tiles = new List<TileKey>(keys.Count);
        foreach (ulong k in keys)
        {
            tiles.Add(TileKey.FromPacked(k)!.Value);
        }
        return tiles;
    }

    private TileObjects Tile(TileKey key)
    {
        long now = Interlocked.Increment(ref _clock);
        // One thread makes a tile; others asking for it meanwhile wait for it rather than make it again (tiles built
        // ahead on several workers often share scatter tiles).
        TileObjects objects = _tiles.GetOrAdd(key.Packed, static (packed, self) => new Lazy<TileObjects>(
            () => self.Generate(TileKey.FromPacked(packed)!.Value), LazyThreadSafetyMode.ExecutionAndPublication), this).Value;
        objects.LastUsed = now;
        if (_tiles.Count > MaxTiles)
        {
            Trim();
        }
        return objects;
    }

    // Drops the least recently used quarter of the cache (regenerating a tile gives the same objects).
    private void Trim()
    {
        var all = new List<(long Used, ulong Key)>();
        foreach (KeyValuePair<ulong, Lazy<TileObjects>> pair in _tiles)
        {
            // One still being made counts as just used.
            all.Add((pair.Value.IsValueCreated ? pair.Value.Value.LastUsed : long.MaxValue, pair.Key));
        }
        all.Sort();
        for (int i = 0; i < all.Count - MaxTiles * 3 / 4; i++)
        {
            _tiles.TryRemove(all[i].Key, out _);
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
            Vector3 position = _frame.ToLocal(item.Position(_surface.Radius));
            byId[id] = objects.Count;
            objects.Add(new NaturalObject
            {
                Id = id,
                Kind = kind.Index,
                Position = position,
                Up = Vector3.Normalize(_frame.DirectionToLocal(item.Direction)),
                Scale = item.Scale,
                Yaw = item.Yaw,
            });
        }
        return new TileObjects(objects.ToArray(), byId);
    }
}
