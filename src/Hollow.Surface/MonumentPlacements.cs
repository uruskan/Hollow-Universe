using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

public readonly record struct MonumentPlacement(TileKey Tile, int Model, Vector3D Position, Vector3D Right,
    Vector3D Up, Vector3D Forward, double Height)
{
    public MonumentDefinition Definition => MonumentDefinition.All[Model];
    public Vector3D Transform(Vector3D point) => Position + Right * point.X + Up * point.Y + Forward * point.Z;
    public double Distance(Vector3D direction, double radius) => (direction - Up).Length * radius;
}

/// <summary>
/// World-size-dependent small monument sites, isolated from existing scatter and loot ID streams. The same sites
/// flatten the terrain, clear vegetation, draw the assembly and provide its collision on server and client.
/// </summary>
public sealed class MonumentPlacements
{
    /// <summary>Advance when site selection, landing exclusion, pad grading or index coverage changes.</summary>
    public const string GenerationContract = "walking-monument-sites-grading-index-v1";
    public const double CellMetres = 300, Blend = 8;
    internal const double MinimumSpacing = 120, LandingClearance = 90, IndexMargin = 40;
    private readonly WalkingWorld _surface;
    private readonly ulong _seed;
    private readonly Lazy<IReadOnlyList<MonumentPlacement>> _all;
    private readonly Lazy<Dictionary<TileKey, List<MonumentPlacement>>> _index;
    public int Level { get; }
    public IReadOnlyList<MonumentPlacement> All => _all.Value;

    public MonumentPlacements(WalkingWorld surface, ulong seed)
    {
        _surface = surface; _seed = seed;
        Level = TileKey.LevelFor(surface.Radius, Math.Max(MinimumSpacing, surface.Parameters.MonumentSpacing));
        _all = new Lazy<IReadOnlyList<MonumentPlacement>>(() => Array.AsReadOnly(Build()));
        _index = new Lazy<Dictionary<TileKey, List<MonumentPlacement>>>(Index);
    }

    private MonumentPlacement[] Build()
    {
        var result = new List<MonumentPlacement>();
        int n = 1 << Level;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var tile = new TileKey(face, Level, x, y);
            var rng = new Rng(Hash64.Of(_seed, 0x4D4F4E55, tile.Packed)); // "MONU"
            if (!rng.Chance(Math.Clamp(_surface.Parameters.MonumentChance, 0, 1))) continue;
            int model = rng.NextInt(MonumentDefinition.All.Count);
            double footprint = MonumentDefinition.All[model].Radius;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3D up = CubeSphere.Direction(face, tile.U0 + tile.Span * rng.Range(0.2, 0.8),
                    tile.V0 + tile.Span * rng.Range(0.2, 0.8));
                WalkingSample centre = _surface.Fields(up);
                if (centre.Wetted || centre.Height < 2 || centre.Ice > 0) continue;
                if ((up - _surface.LandingSite).Length * _surface.Radius < footprint + LandingClearance) continue;
                bool overlaps = false;
                foreach (MonumentPlacement other in result)
                    if (other.Distance(up, _surface.Radius) < footprint + other.Definition.Radius + Blend * 2) { overlaps = true; break; }
                if (overlaps) continue;
                Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
                east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
                Vector3D south = Vector3D.Cross(east, up);
                double turn = rng.Range(0, DMath.TwoPi), c = DMath.Cos(turn), s = DMath.Sin(turn);
                Vector3D right = east * c + south * s, forward = -east * s + south * c;
                Vector3D at = up * (_surface.Radius + centre.Height);
                double low = centre.Height, high = centre.Height;
                bool dry = true;
                for (int k = 0; k < 8; k++)
                {
                    double angle = k * DMath.TwoPi / 8;
                    WalkingSample edge = _surface.Fields((at + (right * DMath.Cos(angle) + forward * DMath.Sin(angle)) * footprint).Normalized);
                    low = Math.Min(low, edge.Height); high = Math.Max(high, edge.Height);
                    dry &= !edge.Wetted && edge.Height > 1;
                }
                if (!dry || high - low > 2.6) continue;
                // A small graded pad, never the broad terrain flattening needed by kilometre-sized archive landmarks.
                double height = (low + high) * 0.5;
                result.Add(new MonumentPlacement(tile, model, up * (_surface.Radius + height), right, up, forward, height));
                break;
            }
        }
        return result.ToArray();
    }

    private Dictionary<TileKey, List<MonumentPlacement>> Index()
    {
        var result = new Dictionary<TileKey, List<MonumentPlacement>>();
        foreach (MonumentPlacement site in All)
        {
            var tiles = new List<TileKey>();
            TileKey.Around(site.Position, site.Definition.Radius + IndexMargin, _surface.Radius, Level + 2, tiles);
            foreach (TileKey tile in tiles)
            {
                if (!result.TryGetValue(tile, out List<MonumentPlacement>? list)) result[tile] = list = new();
                list.Add(site);
            }
        }
        return result;
    }

    /// <summary>Selected construction completes the same immutable site index before publishing its field.</summary>
    internal void FinalizeGradingIndex() => _ = _index.Value;

    public MonumentPlacement? At(Vector3D direction, double extra = 0)
    {
        if (_index.Value.TryGetValue(TileKey.At(direction, Level + 2), out List<MonumentPlacement>? sites))
            foreach (MonumentPlacement site in sites)
                if (site.Distance(direction, _surface.Radius) < site.Definition.Radius + extra) return site;
        return null;
    }

    public double HeightAt(Vector3D direction, double ground)
    {
        if (At(direction, Blend) is not { } site) return ground;
        double distance = site.Distance(direction, _surface.Radius);
        double amount = 1 - DMath.SmoothStep(site.Definition.Radius, site.Definition.Radius + Blend, distance);
        double plane = (_surface.Radius + site.Height) / Vector3D.Dot(direction, site.Up) - _surface.Radius;
        return ground + (plane - ground) * amount;
    }

    public double VegetationAt(Vector3D direction) => At(direction, 2) == null ? 1 : 0;

    public TriangleMesh AddCollision(TriangleMesh ground, in ZoneFrame frame, TileKey tile, double margin)
    {
        double reach = TileKey.EdgeLength(tile.Level, _surface.Radius) * 0.95 + margin;
        Vector3D centre = tile.Centre * (_surface.Radius + _surface.HeightAt(tile.Centre));
        List<float>? positions = null;
        List<int>? indices = null;
        foreach (MonumentPlacement site in All)
        {
            if (Vector3D.Distance(centre, site.Position) > reach + site.Definition.Radius) continue;
            positions ??= new List<float>(ground.Positions); indices ??= new List<int>(ground.Indices);
            int offset = positions.Count / 3;
            foreach (Vector3D point in site.Definition.Positions)
            {
                System.Numerics.Vector3 local = frame.ToLocal(site.Transform(point));
                positions.Add(local.X); positions.Add(local.Y); positions.Add(local.Z);
            }
            foreach (int index in site.Definition.Indices) indices.Add(offset + index);
        }
        return positions == null ? ground : new TriangleMesh(positions.ToArray(), indices!.ToArray());
    }
}
