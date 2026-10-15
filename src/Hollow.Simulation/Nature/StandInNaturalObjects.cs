using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>
/// Natural objects of the stand-in world (docs/WHOLE_WORLD.md §7.1), placed by a seeded stream per cube-sphere tile of
/// the level nearest <see cref="TileMetres"/> (59 m on a 2.41 km world), until S4's ground brings the planet's own
/// scatter (<see cref="ZoneSpec.CreateNature"/> is the one place that picks). Each kind has a density per 64 m square
/// (gather.json, <c>standIn</c>), scaled to the tile's size; positions are on the ground from
/// <see cref="ISurface.HeightAt"/>. Deterministic (ARCHITECTURE.md §8: Hash64, Rng and the surface only), so the server
/// and every client place the same objects under the same IDs: a tag byte, the tile's place among its level's tiles and
/// the index within it. Nothing grows within <see cref="ClearRadius"/> of the landing site, nor under the sea.
/// </summary>
public sealed class StandInNaturalObjects : INaturalObjects
{
    /// <summary>Top byte of every ID this provider makes.</summary>
    public const ulong Tag = 0x5AUL << 56;

    /// <summary>Nothing grows within this many metres of the landing site (where players arrive).</summary>
    public const float ClearRadius = 9f;

    /// <summary>The edge, in metres, the tiles' level is chosen for; gather.json's densities are per square of this side.</summary>
    public const double TileMetres = 64;

    private const int IndexBits = 16;
    private const ulong TileMask = (1UL << 40) - 1;

    private readonly ISurface _surface;
    private readonly ZoneFrame _frame;
    private readonly GameContent _content;
    private readonly ulong _seed;
    private readonly Vector3D _landing;
    private readonly double _area;
    private readonly ConcurrentDictionary<TileKey, NaturalObject[]> _tiles = new();

    public StandInNaturalObjects(ISurface surface, ZoneFrame frame, GameContent content, ulong seed, Vector3D landing)
    {
        _surface = surface;
        _frame = frame;
        _content = content;
        _seed = seed;
        Level = Math.Min(TileKey.LevelFor(surface.Radius, TileMetres), 20);
        double edge = TileKey.EdgeLength(Level, surface.Radius);
        _area = edge * edge / (TileMetres * TileMetres);
        _landing = landing.LengthSquared > 0 ? landing.Normalized * (surface.Radius + surface.HeightAt(landing.Normalized)) : Vector3D.Zero;
    }

    /// <summary>The cube-sphere level of the stand-in's tiles.</summary>
    public int Level { get; }

    public void Query(Vector3 centre, float radius, List<NaturalObject> into)
    {
        if (!(radius > 0))
        {
            return;
        }
        var tiles = new List<TileKey>();
        TileKey.Around(_frame.ToBody(centre), radius, _surface.Radius, Level, tiles);
        tiles.Sort((a, b) => a.Packed.CompareTo(b.Packed));
        float r2 = radius * radius;
        foreach (TileKey tile in tiles)
        {
            foreach (NaturalObject o in Tile(tile))
            {
                if (Vector3.DistanceSquared(o.Position, centre) <= r2)
                {
                    into.Add(o);
                }
            }
        }
    }

    public bool TryGet(ulong id, out NaturalObject found)
    {
        found = default;
        if ((id & (0xFFUL << 56)) != Tag || TileKey.FromIndex(Level, (long)((id >> IndexBits) & TileMask)) is not { } tile)
        {
            return false;
        }
        foreach (NaturalObject o in Tile(tile))
        {
            if (o.Id == id)
            {
                found = o;
                return true;
            }
        }
        return false;
    }

    /// <summary>The objects of one tile (cached).</summary>
    public NaturalObject[] Tile(TileKey tile) => _tiles.GetOrAdd(tile, Generate);

    private NaturalObject[] Generate(TileKey tile)
    {
        var rng = new Rng(Hash64.Of(_seed, tile.Packed, 0x7A7E));
        var objects = new List<NaturalObject>();
        int index = 0;
        ulong prefix = Tag | ((ulong)tile.Index & TileMask) << IndexBits;
        foreach (NaturalKind kind in _content.NaturalKinds)
        {
            double density = kind.StandInPerTile * _area;
            int count = (int)Math.Floor(density) + (rng.NextDouble() < density - Math.Floor(density) ? 1 : 0);
            for (int i = 0; i < count; i++)
            {
                // Every draw happens whether or not the object is kept, so a skipped one never shifts the others.
                double u = tile.U0 + rng.NextDouble() * tile.Span;
                double v = tile.V0 + rng.NextDouble() * tile.Span;
                double size = kind.IsHarvested ? rng.Range(0.8, 1.25) : rng.Range(0.9, 1.1);
                double yaw = rng.Range(-Math.PI, Math.PI);
                ulong id = prefix | (uint)index;
                index++;
                Vector3D direction = CubeSphere.Direction(tile.Face, u, v);
                Vector3D ground = direction * (_surface.Radius + _surface.HeightAt(direction));
                if (_landing.LengthSquared > 0 && (ground - _landing).LengthSquared < ClearRadius * ClearRadius)
                {
                    continue;
                }
                if (_surface.SeaRadius > 0 && ground.Length < _surface.SeaRadius + 0.3)
                {
                    continue; // nothing grows under water
                }
                Vector3 position = _frame.ToLocal(ground);
                objects.Add(new NaturalObject
                {
                    Id = id,
                    Kind = kind.Index,
                    Position = position,
                    Up = _frame.UpAt(position),
                    Scale = (float)size,
                    Yaw = (float)yaw,
                });
            }
        }
        return objects.ToArray();
    }
}
