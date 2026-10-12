using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>
/// Natural objects placed by a seeded stream per 64 m tile of the zone's tangent plane, until the planet package's
/// scatter replaces it (<see cref="ZoneSpec.CreateNature"/> is the one place that picks). Each kind has a density per
/// tile (gather.json, <c>standIn</c>); positions are on the ground from <see cref="ISurface.HeightAt"/>. Deterministic
/// (ARCHITECTURE.md §8: Hash64, Rng and the surface only), so the server and every client place the same objects
/// under the same IDs: a tag byte, the tile and the index within it. The spawn area is kept clear.
/// </summary>
public sealed class StandInNaturalObjects : INaturalObjects
{
    /// <summary>Top byte of every ID this provider makes.</summary>
    public const ulong Tag = 0x5AUL << 56;

    /// <summary>Nothing grows within this many metres of the zone's origin (where players arrive).</summary>
    public const float ClearRadius = 9f;

    private readonly ISurface _surface;
    private readonly ZoneFrame _frame;
    private readonly GameContent _content;
    private readonly ulong _seed;
    private readonly ConcurrentDictionary<(int X, int Z), NaturalObject[]> _tiles = new();

    public StandInNaturalObjects(ISurface surface, ZoneFrame frame, GameContent content, ulong seed)
    {
        _surface = surface;
        _frame = frame;
        _content = content;
        _seed = seed;
    }

    public void Query(float minX, float minZ, float maxX, float maxZ, List<NaturalObject> into)
    {
        int x0 = (int)MathF.Floor(minX / PhysicsWorld.TileSize), x1 = (int)MathF.Floor((maxX - 1e-3f) / PhysicsWorld.TileSize);
        int z0 = (int)MathF.Floor(minZ / PhysicsWorld.TileSize), z1 = (int)MathF.Floor((maxZ - 1e-3f) / PhysicsWorld.TileSize);
        for (int tz = z0; tz <= z1; tz++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                foreach (NaturalObject o in Tile(tx, tz))
                {
                    if (o.Position.X >= minX && o.Position.X < maxX && o.Position.Z >= minZ && o.Position.Z < maxZ)
                    {
                        into.Add(o);
                    }
                }
            }
        }
    }

    public bool TryGet(ulong id, out NaturalObject found)
    {
        found = default;
        if ((id & (0xFFUL << 56)) != Tag)
        {
            return false;
        }
        int tx = (int)((id >> 40) & 0xFF) - PhysicsWorld.TileRange;
        int tz = (int)((id >> 32) & 0xFF) - PhysicsWorld.TileRange;
        if ((id & 0x00FF_0000_FFFF_0000UL) != 0)
        {
            return false;
        }
        foreach (NaturalObject o in Tile(tx, tz))
        {
            if (o.Id == id)
            {
                found = o;
                return true;
            }
        }
        return false;
    }

    /// <summary>The objects of one tile (cached; empty outside the zone's ground).</summary>
    public NaturalObject[] Tile(int tx, int tz)
    {
        if (tx < -PhysicsWorld.TileRange || tx >= PhysicsWorld.TileRange || tz < -PhysicsWorld.TileRange || tz >= PhysicsWorld.TileRange)
        {
            return Array.Empty<NaturalObject>();
        }
        return _tiles.GetOrAdd((tx, tz), key => Generate(key.X, key.Z));
    }

    private NaturalObject[] Generate(int tx, int tz)
    {
        var rng = new Rng(Hash64.Of(_seed, (ulong)(long)tx, (ulong)(long)tz, 0x7A7E));
        var objects = new List<NaturalObject>();
        int index = 0;
        foreach (NaturalKind kind in _content.NaturalKinds)
        {
            double density = kind.StandInPerTile;
            int count = (int)Math.Floor(density) + (rng.NextDouble() < density - Math.Floor(density) ? 1 : 0);
            for (int i = 0; i < count; i++)
            {
                // Every draw happens whether or not the object is kept, so a skipped one never shifts the others.
                double x = (tx + rng.NextDouble()) * PhysicsWorld.TileSize;
                double z = (tz + rng.NextDouble()) * PhysicsWorld.TileSize;
                double size = kind.IsHarvested ? rng.Range(0.8, 1.25) : rng.Range(0.9, 1.1);
                double yaw = rng.Range(-Math.PI, Math.PI);
                ulong id = Tag | ((ulong)(uint)(tx + PhysicsWorld.TileRange) << 40) | ((ulong)(uint)(tz + PhysicsWorld.TileRange) << 32) |
                    (uint)index;
                index++;
                if (x * x + z * z < ClearRadius * ClearRadius)
                {
                    continue;
                }
                Vector3D direction = _frame.ToBody(x, 0, z).Normalized;
                Vector3 position = _frame.ToLocal(direction * (_surface.Radius + _surface.HeightAt(direction)));
                if (_surface.SeaRadius > 0 && _frame.ToBody(position).Length < _surface.SeaRadius + 0.3)
                {
                    continue; // nothing grows under water
                }
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
