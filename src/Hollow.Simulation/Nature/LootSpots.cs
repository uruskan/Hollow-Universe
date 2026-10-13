using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>What kind of place a loot cluster stands in (GROUND.md §6.8).</summary>
public enum LootSite : byte
{
    Coast,
    ForestEdge,
    Open,

    /// <summary>The cluster always kept near the zone's origin, so arrivals find loot.</summary>
    Landing,

    /// <summary>A recycler's fixed point near the landing site.</summary>
    Recycler,
}

/// <summary>Where a world prop stands: its stable ID (the spot's), what it is, its base on the ground, up, turn, and the kind of place.</summary>
public readonly record struct LootSpot(ulong Id, PropDef Prop, Vector3 Position, Vector3 Up, float Yaw, LootSite Site);

/// <summary>
/// Where the world's barrels, crates and recyclers stand (GROUND.md §6.8): generated, never stored. One candidate
/// cluster per cell of the zone's tangent plane within a radius of its origin (never within <c>clear</c> of it, where
/// players arrive), kept by what is around it: the sea within 35 m (a coast), a forest edge (forest and open biomes
/// side by side, or trees near but not on the spot), or open ground, each with its chance. A kept cluster is a few
/// barrels and maybe a crate within a few metres, each on dry, gentle ground clear of trees. Recyclers take the first
/// valid of their fixed points. Deterministic (ARCHITECTURE.md §8: Hash64, Rng, DMath and the surface only), so a spot's
/// ID names the same place after every restart and the overrides stored against it stay true.
/// </summary>
public sealed class LootSpots
{
    /// <summary>Top byte of every loot spot's ID (entity IDs of the allocator stay far below it for decades).</summary>
    public const ulong Tag = 0xC4UL << 56;

    /// <summary>Top byte of the recyclers' IDs.</summary>
    public const ulong RecyclerTag = 0xC5UL << 56;

    private const int LandingKey = 0x3F_FFFF;

    /// <summary>tan 22°: the steepest ground a prop stands on.</summary>
    private const double MaxRise = 0.40403;

    private readonly ISurface _surface;
    private readonly ZoneFrame _frame;
    private readonly INaturalObjects _nature;
    private readonly GameContent _content;
    private readonly double _seaLevel;
    private readonly List<NaturalObject> _near = new();

    private LootSpots(ISurface surface, ZoneFrame frame, INaturalObjects nature, GameContent content)
    {
        _surface = surface;
        _frame = frame;
        _nature = nature;
        _content = content;
        _seaLevel = surface.SeaRadius > 0 ? surface.SeaRadius - surface.Radius : double.NegativeInfinity;
    }

    /// <summary>Every spot, clusters in grid order then the recyclers.</summary>
    public IReadOnlyList<LootSpot> Spots { get; private set; } = Array.Empty<LootSpot>();

    /// <summary>Clusters kept, by kind of place (for the report).</summary>
    public int Coasts { get; private set; }

    public int ForestEdges { get; private set; }

    public int Opens { get; private set; }

    /// <summary>The spots of a zone (the server's, once at start).</summary>
    public static LootSpots Generate(ISurface surface, ZoneFrame frame, INaturalObjects nature, GameContent content, ulong seed)
    {
        var spots = new LootSpots(surface, frame, nature, content);
        spots.Build(seed);
        return spots;
    }

    private void Build(ulong seed)
    {
        WorldData world = _content.World;
        LootSpotSettings s = world.Loot;
        var result = new List<LootSpot>();
        PropDef? barrel = null, fuelBarrel = null, crate = null, military = null, recycler = null;
        foreach (PropDef p in world.Props)
        {
            switch (p.Kind)
            {
                case WorldPropKind.Barrel when p.LootKey == "fuel-barrel":
                    fuelBarrel ??= p;
                    break;
                case WorldPropKind.Barrel:
                    barrel ??= p;
                    break;
                case WorldPropKind.Crate when p.LootKey == "military-crate":
                    military ??= p;
                    break;
                case WorldPropKind.Crate:
                    crate ??= p;
                    break;
                case WorldPropKind.Recycler:
                    recycler ??= p;
                    break;
            }
        }
        fuelBarrel ??= barrel;
        military ??= crate;
        if (barrel != null && crate != null)
        {
            if (s.Landing is { } landing && Valid(landing.X, landing.Z, 1.0))
            {
                Cluster(result, LandingKey, landing.X, landing.Z, LootSite.Landing, new Rng(Hash64.Of(seed, 0x1A4D)), s, barrel, fuelBarrel!, crate, military!);
            }
            int n = (int)Math.Ceiling(s.Radius / s.Cell);
            for (int j = -n; j < n; j++)
            {
                for (int i = -n; i < n; i++)
                {
                    var rng = new Rng(Hash64.Of(seed, (ulong)(long)i, (ulong)(long)j, 0x1007));
                    double x = (i + 0.5 + rng.Range(-0.35, 0.35)) * s.Cell;
                    double z = (j + 0.5 + rng.Range(-0.35, 0.35)) * s.Cell;
                    double keep = rng.NextDouble();
                    double d = Math.Sqrt(x * x + z * z);
                    if (d > s.Radius || d < s.Clear + s.Spread || !Valid(x, z, 1.0))
                    {
                        continue;
                    }
                    LootSite site = Classify(x, z);
                    double chance = site switch
                    {
                        LootSite.Coast => s.Coast,
                        LootSite.ForestEdge => s.ForestEdge,
                        _ => s.Open,
                    };
                    if (keep >= chance)
                    {
                        continue;
                    }
                    int key = ((i + 1024) << 11) | (j + 1024);
                    if (Cluster(result, key, x, z, site, rng, s, barrel, fuelBarrel!, crate, military!))
                    {
                        switch (site)
                        {
                            case LootSite.Coast:
                                Coasts++;
                                break;
                            case LootSite.ForestEdge:
                                ForestEdges++;
                                break;
                            default:
                                Opens++;
                                break;
                        }
                    }
                }
            }
        }
        if (recycler != null)
        {
            int placed = 0;
            var rng = new Rng(Hash64.Of(seed, 0x2EC1));
            for (int k = 0; k < world.RecyclerPoints.Count && placed < world.RecyclerCount; k++)
            {
                Vector2 point = world.RecyclerPoints[k];
                float yaw = (float)rng.Range(-Math.PI, Math.PI);
                if (!Valid(point.X, point.Y, 1.6) || TreeWithin(point.X, point.Y, 3.0))
                {
                    continue;
                }
                Vector3 ground = Ground(point.X, point.Y);
                result.Add(new LootSpot(RecyclerTag | (uint)k, recycler, ground, _frame.UpAt(ground), yaw, LootSite.Recycler));
                placed++;
            }
        }
        Spots = result;
    }

    // A few barrels and maybe a crate round a point; false when none found room.
    private bool Cluster(List<LootSpot> into, int key, double cx, double cz, LootSite site, Rng rng, LootSpotSettings s,
        PropDef barrel, PropDef fuelBarrel, PropDef crate, PropDef military)
    {
        int barrels = s.LeastBarrels + rng.NextInt(s.MostBarrels - s.LeastBarrels + 1);
        var members = new List<PropDef>();
        if (rng.Chance(s.Crate))
        {
            members.Add(rng.Chance(s.Military) ? military : crate);
        }
        for (int b = 0; b < barrels; b++)
        {
            members.Add(rng.Chance(s.FuelBarrel) ? fuelBarrel : barrel);
        }
        int start = into.Count;
        double turn = rng.Range(0, DMath.TwoPi);
        for (int m = 0; m < members.Count; m++)
        {
            PropDef prop = members[m];
            float reach = MathF.Max(prop.Deployable.HalfExtents.X, prop.Deployable.HalfExtents.Z);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                double angle = turn + m * 2.399963 + rng.Range(-0.4, 0.4);
                double radius = m == 0 && attempt == 0 ? rng.Range(0, 1.2) : rng.Range(1.2, s.Spread);
                double x = cx + radius * DMath.Cos(angle), z = cz + radius * DMath.Sin(angle);
                float yaw = (float)rng.Range(-Math.PI, Math.PI);
                if (!Valid(x, z, reach + 0.2) || TreeWithin(x, z, reach + 0.6))
                {
                    continue;
                }
                Vector3 ground = Ground(x, z);
                bool apart = true;
                for (int k = start; k < into.Count; k++)
                {
                    LootSpot other = into[k];
                    float room = reach + MathF.Max(other.Prop.Deployable.HalfExtents.X, other.Prop.Deployable.HalfExtents.Z) + 0.4f;
                    if (Vector3.DistanceSquared(other.Position, ground) < room * room)
                    {
                        apart = false;
                        break;
                    }
                }
                if (!apart)
                {
                    continue;
                }
                into.Add(new LootSpot(Tag | ((ulong)(uint)key << 8) | (uint)m, prop, ground, _frame.UpAt(ground), yaw, site));
                break;
            }
        }
        return into.Count > start;
    }

    private double HeightAt(double x, double z) => _surface.HeightAt(_frame.ToBody(x, 0, z).Normalized);

    private Vector3 Ground(double x, double z)
    {
        Vector3D direction = _frame.ToBody(x, 0, z).Normalized;
        return _frame.ToLocal(direction * (_surface.Radius + _surface.HeightAt(direction)));
    }

    // Dry (half a metre above the sea at least) and gentle (under about 22° across the footprint both ways).
    private bool Valid(double x, double z, double half)
    {
        double h = HeightAt(x, z);
        if (h - _seaLevel < 0.5)
        {
            return false;
        }
        double step = Math.Max(1.0, half);
        double rise = MaxRise * step;
        return Math.Abs(HeightAt(x + step, z) - h) < rise && Math.Abs(HeightAt(x, z + step) - h) < rise &&
            Math.Abs(HeightAt(x - step, z) - h) < rise && Math.Abs(HeightAt(x, z - step) - h) < rise;
    }

    private LootSite Classify(double x, double z)
    {
        if (_surface.SeaRadius > 0 && HeightAt(x, z) - _seaLevel < 12)
        {
            for (int k = 0; k < 8; k++)
            {
                double a = k * DMath.TwoPi / 8;
                if (HeightAt(x + 35 * DMath.Cos(a), z + 35 * DMath.Sin(a)) < _seaLevel)
                {
                    return LootSite.Coast;
                }
            }
        }
        bool forest = Forest(_surface.BiomeAt(_frame.ToBody(x, 0, z).Normalized));
        for (int k = 0; k < 4; k++)
        {
            double a = k * DMath.TwoPi / 4;
            if (Forest(_surface.BiomeAt(_frame.ToBody(x + 30 * DMath.Cos(a), 0, z + 30 * DMath.Sin(a)).Normalized)) != forest)
            {
                return LootSite.ForestEdge;
            }
        }
        // Trees around but none on the spot: the edge of a stand, or a clearing.
        _near.Clear();
        _nature.Query((float)(x - 22), (float)(z - 22), (float)(x + 22), (float)(z + 22), _near);
        int around = 0;
        foreach (NaturalObject o in _near)
        {
            if (_content.Natural(o.Kind) is { Class: NaturalClass.Tree })
            {
                double dx = o.Position.X - x, dz = o.Position.Z - z, d2 = dx * dx + dz * dz;
                if (d2 < 5 * 5)
                {
                    return LootSite.Open;
                }
                if (d2 < 22 * 22)
                {
                    around++;
                }
            }
        }
        return around >= 2 ? LootSite.ForestEdge : LootSite.Open;
    }

    private static bool Forest(Biome b) => b is Biome.BorealForest or Biome.TemperateForest or Biome.TropicalForest;

    // A tree, outcrop or other solid natural object within a distance of a point.
    private bool TreeWithin(double x, double z, double distance)
    {
        _near.Clear();
        float reach = (float)(distance + 3);
        _nature.Query((float)x - reach, (float)z - reach, (float)x + reach, (float)z + reach, _near);
        foreach (NaturalObject o in _near)
        {
            if (_content.Natural(o.Kind) is { IsSolid: true } kind)
            {
                double dx = o.Position.X - x, dz = o.Position.Z - z;
                double room = distance + kind.Radius * o.Scale;
                if (dx * dx + dz * dz < room * room)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
