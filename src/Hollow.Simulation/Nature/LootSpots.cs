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

    /// <summary>The cluster always kept near the landing site, so arrivals there find loot.</summary>
    Landing,

    /// <summary>A recycler's site: one per cube face (docs/WHOLE_WORLD.md decision 9).</summary>
    Recycler,
}

/// <summary>Where a world prop stands: its stable ID (the spot's), what it is, its base on the ground, up, turn, and the kind of place.</summary>
public readonly record struct LootSpot(ulong Id, PropDef Prop, Vector3 Position, Vector3 Up, float Yaw, LootSite Site);

/// <summary>
/// Where the world's barrels, crates and recyclers stand (GROUND.md §6.8, docs/WHOLE_WORLD.md §7.2): generated, never
/// stored. One candidate cluster per cube-sphere cell of the level nearest <c>cell</c> metres, over the whole world
/// (never within <c>clear</c> of the landing site, where players may arrive), kept by what is around it: the sea within
/// 35 m (a coast), a forest edge (forest and open biomes side by side, or trees near but not on the spot), or open
/// ground, each with its chance. A kept cluster is a few barrels and maybe a crate within a few metres, each on dry,
/// gentle ground clear of trees. Recyclers stand one per cube face, on the face's first coastal candidate that has room
/// (or its first one with room). Each candidate is worked out in its own small frame on the ground (east and south of
/// it, measured along the ground), so nothing depends on where the zone's frame stands. Deterministic (ARCHITECTURE.md
/// §8: Hash64, Rng, DMath and the surface only), so a spot's ID names the same place after every restart and the
/// overrides stored against it stay true.
/// </summary>
public sealed class LootSpots
{
    /// <summary>Top byte of every loot spot's ID (entity IDs of the allocator stay far below it for decades).</summary>
    public const ulong Tag = 0xC6UL << 56;

    /// <summary>Top byte of the recyclers' IDs.</summary>
    public const ulong RecyclerTag = 0xC7UL << 56;

    private const ulong LandingKey = 0xFF_FFFF_FFFF;

    /// <summary>tan 22°: the steepest ground a prop stands on.</summary>
    private const double MaxRise = 0.40403;

    private readonly ISurface _surface;
    private readonly ZoneFrame _frame;
    private readonly INaturalObjects _nature;
    private readonly GameContent _content;
    private readonly double _seaLevel;
    private readonly Vector3D _landing;
    private readonly List<NaturalObject> _near = new();

    private LootSpots(ISurface surface, ZoneFrame frame, INaturalObjects nature, GameContent content, Vector3D landing)
    {
        _surface = surface;
        _frame = frame;
        _nature = nature;
        _content = content;
        _landing = landing.Normalized;
        _seaLevel = surface.SeaRadius > 0 ? surface.SeaRadius - surface.Radius : double.NegativeInfinity;
    }

    /// <summary>Every spot: the landing cluster, the clusters cell by cell, then the recyclers.</summary>
    public IReadOnlyList<LootSpot> Spots { get; private set; } = Array.Empty<LootSpot>();

    /// <summary>Clusters kept, by kind of place (for the report).</summary>
    public int Coasts { get; private set; }

    public int ForestEdges { get; private set; }

    public int Opens { get; private set; }

    /// <summary>The cube-sphere level of the candidate cells on this world.</summary>
    public int Level { get; private set; }

    /// <summary>The spots of a world (the server's, once at start), with <paramref name="landing"/> the landing site's direction.</summary>
    public static LootSpots Generate(ISurface surface, ZoneFrame frame, INaturalObjects nature, GameContent content, ulong seed, Vector3D landing)
    {
        var spots = new LootSpots(surface, frame, nature, content, landing);
        spots.Build(seed);
        return spots;
    }

    // A small frame on the ground at a direction: east and south of it, measured along the ground.
    private readonly struct Patch
    {
        public readonly Vector3D Up, East, South;

        public Patch(Vector3D up)
        {
            Up = up;
            Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
            East = east.LengthSquared > 1e-18 ? east.Normalized : new Vector3D(0, 0, -1);
            South = Vector3D.Cross(East, Up);
        }
    }

    private Vector3D Direction(in Patch p, double x, double z, double radius)
    {
        double d = Math.Sqrt(x * x + z * z);
        if (d < 1e-9)
        {
            return p.Up;
        }
        double angle = d / radius, sin = DMath.Sin(angle);
        return (p.Up * DMath.Cos(angle) + (p.East * (x / d) + p.South * (z / d)) * sin).Normalized;
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
        Level = TileKey.LevelFor(_surface.Radius, s.Cell, 12);
        int n = 1 << Level;
        var landingPatch = new Patch(_landing);
        if (barrel != null && crate != null && s.Landing is { } landing && Valid(landingPatch, landing.X, landing.Z, 1.0))
        {
            Cluster(result, LandingKey, landingPatch, landing.X, landing.Z, LootSite.Landing, new Rng(Hash64.Of(seed, 0x1A4D)), s, barrel, fuelBarrel!, crate,
                military!);
        }
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var tile = new TileKey(face, Level, x, y);
                    var rng = new Rng(Hash64.Of(seed, tile.Packed, 0x1007));
                    double u = tile.U0 + tile.Span * (0.5 + rng.Range(-0.35, 0.35));
                    double v = tile.V0 + tile.Span * (0.5 + rng.Range(-0.35, 0.35));
                    double keep = rng.NextDouble();
                    Vector3D at = CubeSphere.Direction(face, u, v);
                    var patch = new Patch(at);
                    if ((at - _landing).Length * _surface.Radius < s.Clear + s.Spread || !Valid(patch, 0, 0, 1.0))
                    {
                        continue;
                    }
                    if (barrel == null || crate == null)
                    {
                        continue;
                    }
                    LootSite site = Classify(patch);
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
                    if (Cluster(result, (ulong)tile.Index, patch, 0, 0, site, rng, s, barrel, fuelBarrel!, crate, military!))
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
        if (recycler != null && world.RecyclerCount > 0)
        {
            var rng = new Rng(Hash64.Of(seed, 0x2EC1));
            int clusters = result.Count;
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                float yaw = (float)rng.Range(-Math.PI, Math.PI);
                if (RecyclerSite(face, n, result, clusters) is { } at)
                {
                    Vector3 ground = Ground(at, 0, 0);
                    result.Add(new LootSpot(RecyclerTag | (uint)face, recycler, ground, _frame.UpAt(ground), yaw, LootSite.Recycler));
                }
            }
        }
        Spots = result;
    }

    // A face's recycler site: the first cell centre in the face's order that is on a coast, has room and keeps clear of
    // the clusters; else the first that has room and keeps clear.
    private Patch? RecyclerSite(int face, int n, List<LootSpot> spots, int clusters)
    {
        Patch? first = null;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                var tile = new TileKey(face, Level, x, y);
                var patch = new Patch(tile.Centre);
                if (!Valid(patch, 0, 0, 1.6) || TreeWithin(patch, 0, 0, 3.0))
                {
                    continue;
                }
                Vector3 ground = Ground(patch, 0, 0);
                bool clear = true;
                for (int k = 0; k < clusters && clear; k++)
                {
                    clear = Vector3.DistanceSquared(spots[k].Position, ground) >= 12 * 12;
                }
                if (!clear)
                {
                    continue;
                }
                if (Coastal(patch))
                {
                    return patch;
                }
                first ??= patch;
            }
        }
        return first;
    }

    // A few barrels and maybe a crate round a point; false when none found room.
    private bool Cluster(List<LootSpot> into, ulong key, in Patch patch, double cx, double cz, LootSite site, Rng rng, LootSpotSettings s,
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
                if (!Valid(patch, x, z, reach + 0.2) || TreeWithin(patch, x, z, reach + 0.6))
                {
                    continue;
                }
                Vector3 ground = Ground(patch, x, z);
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
                into.Add(new LootSpot(Tag | (key << 8) | (uint)m, prop, ground, _frame.UpAt(ground), yaw, site));
                break;
            }
        }
        return into.Count > start;
    }

    private double HeightAt(in Patch p, double x, double z) => _surface.HeightAt(Direction(p, x, z, _surface.Radius));

    private Vector3 Ground(in Patch p, double x, double z)
    {
        Vector3D direction = Direction(p, x, z, _surface.Radius);
        return _frame.ToLocal(direction * (_surface.Radius + _surface.HeightAt(direction)));
    }

    // Dry (half a metre above the sea at least) and gentle (under about 22° across the footprint both ways).
    private bool Valid(in Patch p, double x, double z, double half)
    {
        double h = HeightAt(p, x, z);
        if (h - _seaLevel < 0.5)
        {
            return false;
        }
        double step = Math.Max(1.0, half);
        double rise = MaxRise * step;
        return Math.Abs(HeightAt(p, x + step, z) - h) < rise && Math.Abs(HeightAt(p, x, z + step) - h) < rise &&
            Math.Abs(HeightAt(p, x - step, z) - h) < rise && Math.Abs(HeightAt(p, x, z - step) - h) < rise;
    }

    // The sea within 35 m of a low point.
    private bool Coastal(in Patch p)
    {
        if (_surface.SeaRadius > 0 && HeightAt(p, 0, 0) - _seaLevel < 12)
        {
            for (int k = 0; k < 8; k++)
            {
                double a = k * DMath.TwoPi / 8;
                if (HeightAt(p, 35 * DMath.Cos(a), 35 * DMath.Sin(a)) < _seaLevel)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private LootSite Classify(in Patch p)
    {
        if (Coastal(p))
        {
            return LootSite.Coast;
        }
        bool forest = Forest(_surface.BiomeAt(p.Up));
        for (int k = 0; k < 4; k++)
        {
            double a = k * DMath.TwoPi / 4;
            if (Forest(_surface.BiomeAt(Direction(p, 30 * DMath.Cos(a), 30 * DMath.Sin(a), _surface.Radius))) != forest)
            {
                return LootSite.ForestEdge;
            }
        }
        // Trees around but none on the spot: the edge of a stand, or a clearing.
        Vector3 centre = Ground(p, 0, 0);
        _near.Clear();
        _nature.Query(centre, 22, _near);
        int around = 0;
        foreach (NaturalObject o in _near)
        {
            if (_content.Natural(o.Kind) is { Class: NaturalClass.Tree })
            {
                if (Vector3.DistanceSquared(o.Position, centre) < 5 * 5)
                {
                    return LootSite.Open;
                }
                around++;
            }
        }
        return around >= 2 ? LootSite.ForestEdge : LootSite.Open;
    }

    private static bool Forest(Biome b) => b is Biome.BorealForest or Biome.TemperateForest or Biome.TropicalForest;

    // A tree, outcrop or other solid natural object within a distance of a point.
    private bool TreeWithin(in Patch p, double x, double z, double distance)
    {
        Vector3 at = Ground(p, x, z);
        _near.Clear();
        _nature.Query(at, (float)(distance + 3), _near);
        foreach (NaturalObject o in _near)
        {
            if (_content.Natural(o.Kind) is { IsSolid: true } kind)
            {
                double room = distance + kind.Radius * o.Scale;
                if (Vector3.DistanceSquared(o.Position, at) < room * room)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
