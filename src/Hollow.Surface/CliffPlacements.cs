using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>A scanned formation embedded in a steep slope; body-frame axes are shared by rendering and collision.</summary>
public readonly record struct CliffPlacement(TileKey Tile, int Model, Vector3D Position, Vector3D Right,
    Vector3D Up, Vector3D Forward, double Scale)
{
    public Vector3D Transform(Vector3D point) => Position + (Right * point.X + Up * point.Y + Forward * point.Z) * Scale;
}

/// <summary>
/// Sparse, stable geological formations on the walking world's steep dry faces. Candidate streams are separate
/// from harvestable scatter; no natural-object or loot ID changes when this presentation/collision layer is added.
/// </summary>
public sealed class CliffPlacements
{
    public const double CellMetres = 48;
    public const double MaximumExtent = 24;
    private readonly ISurface _surface;
    private readonly ulong _seed;
    private readonly ConcurrentDictionary<TileKey, CliffPlacement?> _cache = new();
    public int Level { get; }

    public CliffPlacements(ISurface surface, ulong seed)
    {
        _surface = surface;
        _seed = seed;
        Level = TileKey.LevelFor(surface.Radius, CellMetres);
    }

    public CliffPlacement? At(TileKey tile) => tile.Level == Level ? _cache.GetOrAdd(tile, Build) : null;

    private CliffPlacement? Build(TileKey tile)
    {
        var random = new Rng(Hash64.Of(_seed, 0x434C4946, tile.Packed)); // "CLIF"
        if (!random.Chance(0.8)) return null;
        Vector3D up = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span * random.Range(0.15, 0.85),
            tile.V0 + tile.Span * random.Range(0.15, 0.85));
        if (_surface is WalkingWorld walking && walking.Monuments.At(up, MaximumExtent) != null) return null;
        double height = _surface.HeightAt(up);
        if (height < _surface.WaterLevelAt(up) + 0.3) return null;
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
        Vector3D south = Vector3D.Cross(east, up);
        Vector3D ground = up * (_surface.Radius + height);
        double Rise(Vector3D axis) => (_surface.HeightAt((ground + axis * 5).Normalized)
            - _surface.HeightAt((ground - axis * 5).Normalized)) / 10;
        double x = Rise(east), z = Rise(south);
        double gradient = Math.Sqrt(x * x + z * z);
        if (gradient < 0.62 || gradient > 3.5) return null;
        Vector3D forward = -(east * x + south * z).Normalized;
        Vector3D right = Vector3D.Cross(up, forward).Normalized;
        int model = random.NextInt(CliffGeometry.All.Count);
        double scale = random.Range(0.75, 1.25);
        // The base sits inside the slope and the upper half breaks its silhouette; no floating rectangular skirts.
        Vector3D at = ground - up * (CliffGeometry.All[model].Height * scale * 0.55);
        return new CliffPlacement(tile, model, at, right, up, forward, scale);
    }

    public List<CliffPlacement> Near(Vector3D position, double reach)
    {
        var tiles = new List<TileKey>();
        TileKey.Around(position, reach + MaximumExtent, _surface.Radius, Level, tiles);
        var result = new List<CliffPlacement>();
        foreach (TileKey tile in tiles)
        {
            if (At(tile) is { } cliff && Vector3D.Distance(position, cliff.Position) <= reach + MaximumExtent)
                result.Add(cliff);
        }
        return result;
    }

    /// <summary>The ordinary heightfield plus nearby scan hulls, identical on server, prediction and ragdoll worlds.</summary>
    public TriangleMesh Collision(in ZoneFrame frame, TileKey tile, double margin)
    {
        TriangleMesh terrain = SurfaceGrid.Collision(_surface, frame, tile, margin, SurfaceGrid.DefaultSpacing);
        double reach = TileKey.EdgeLength(tile.Level, _surface.Radius) * 0.95 + margin;
        Vector3D centre = tile.Centre * (_surface.Radius + _surface.HeightAt(tile.Centre));
        List<CliffPlacement> nearby = Near(centre, reach);
        if (nearby.Count == 0) return terrain;
        var positions = new List<float>(terrain.Positions);
        var indices = new List<int>(terrain.Indices);
        foreach (CliffPlacement cliff in nearby)
        {
            CliffGeometry mesh = CliffGeometry.All[cliff.Model];
            int offset = positions.Count / 3;
            foreach (Vector3D point in mesh.Positions)
            {
                System.Numerics.Vector3 local = frame.ToLocal(cliff.Transform(point));
                positions.Add(local.X); positions.Add(local.Y); positions.Add(local.Z);
            }
            foreach (int index in mesh.Indices) indices.Add(offset + index);
        }
        return new TriangleMesh(positions.ToArray(), indices.ToArray());
    }
}
