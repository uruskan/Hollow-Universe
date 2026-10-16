using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>A scanned formation embedded in a steep slope; body-frame axes are shared by rendering and collision.</summary>
public readonly record struct CliffPlacement(TileKey Tile, int Model, Vector3D Position, Vector3D Right,
    Vector3D Up, Vector3D Forward, double Scale, int Part = 0)
{
    public (TileKey Tile, int Part) Key => (Tile, Part);
    public Vector3D Transform(Vector3D point) => Position + (Right * point.X + Up * point.Y + Forward * point.Z) * Scale;
}

/// <summary>
/// Sparse, stable geological formations on the walking world's steep dry faces. Candidate streams are separate
/// from harvestable scatter; no natural-object or loot ID changes when this presentation/collision layer is added.
/// </summary>
public sealed class CliffPlacements
{
    public const double CellMetres = 48;
    public const double MaximumExtent = 40;
    public const double MaximumOffset = 64;
    public const int MaximumTiers = 5;
    private readonly ISurface _surface;
    private readonly ulong _seed;
    private readonly ConcurrentDictionary<TileKey, CliffPlacement[]> _cache = new();
    public int Level { get; }

    public CliffPlacements(ISurface surface, ulong seed)
    {
        _surface = surface;
        _seed = seed;
        Level = TileKey.LevelFor(surface.Radius, CellMetres);
    }

    public CliffPlacement? At(TileKey tile)
    {
        IReadOnlyList<CliffPlacement> parts = FormationAt(tile);
        return parts.Count == 0 ? null : parts[0];
    }

    public IReadOnlyList<CliffPlacement> FormationAt(TileKey tile) =>
        tile.Level == Level ? _cache.GetOrAdd(tile, Build) : Array.Empty<CliffPlacement>();

    private CliffPlacement[] Build(TileKey tile)
    {
        var random = new Rng(Hash64.Of(_seed, 0x434C4946, tile.Packed)); // "CLIF"
        if (!random.Chance(0.8)) return Array.Empty<CliffPlacement>();
        Vector3D up = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span * random.Range(0.15, 0.85),
            tile.V0 + tile.Span * random.Range(0.15, 0.85));
        if (!Slope(up, out Vector3D forward, out Vector3D right, out double gradient) || gradient < 0.62)
            return Array.Empty<CliffPlacement>();
        Vector3D ground = up * (_surface.Radius + _surface.HeightAt(up));
        // Neighbours overlap along the contour, and each refits to its own radial up and slope.
        // Keep the first piece stable as the group's anchor; parts have distinct streaming identities.
        int count = 3 + random.NextInt(3);
        var result = new List<CliffPlacement>();
        double step = 13.5;
        for (int part = 0; part < count; part++)
        {
            int side = part == 0 ? 0 : (part + 1) / 2 * (part % 2 == 1 ? -1 : 1);
            Vector3D direction = (ground + right * (side * step)).Normalized;
            if (!Slope(direction, out Vector3D face, out Vector3D along, out double localGradient)) continue;
            // The same Nordic scans used by RustMVP's local replacement path: tall fronts on steep
            // faces, broad ledges on gentler slopes. Group members share a geological family.
            string family = gradient < 0.95 ? "nordic_forest_ledge" : gradient < 1.5 ? "nordic_broad" : "nordic_tall";
            int model = 0;
            for (int i = 0; i < CliffGeometry.All.Count; i++)
                if (CliffGeometry.All[i].Key == family) { model = i; break; }
            CliffGeometry geometry = CliffGeometry.All[model];
            double columnScale = random.Range(0.90, 1.12);
            // Cover the face down to its toe, instead of leaving a narrow strip across its crest.
            // Rows descend in the local slope frame; their height overlap hides seams without
            // stretching the scanned mesh vertically or treating world Y as planetary up.
            Vector3D columnBase = direction * (_surface.Radius + _surface.HeightAt(direction));
            double tierStep = Math.Clamp(geometry.Height * columnScale * 0.58 / localGradient, 2, 8);
            double previousRadius = double.PositiveInfinity;
            for (int tier = 0; tier < MaximumTiers; tier++)
            {
                Vector3D rowDirection = (columnBase + face * (tier * tierStep)).Normalized;
                if (!Slope(rowDirection, out Vector3D rowFace, out Vector3D rowAlong, out _)) break;
                if (Vector3D.Dot(rowFace, face) < 0.5) break; // reached a valley or a different face
                var rowRandom = new Rng(Hash64.Of(_seed, tile.Packed, (ulong)(part * MaximumTiers + tier + 1)));
                double scale = columnScale * (tier == 0 ? 1 : rowRandom.Range(0.96, 1.04));
                Vector3D centre = rowDirection * (_surface.Radius + _surface.HeightAt(rowDirection));
                Vector3D toe = (centre + rowFace * geometry.Depth * scale * 0.35).Normalized;
                double baseHeight = Math.Min(_surface.HeightAt(rowDirection), _surface.HeightAt(toe));
                if (baseHeight <= _surface.WaterLevelAt(toe) + 0.3) break;
                double embed = Math.Clamp(geometry.Height * scale * 0.08, 0.25, 0.9);
                Vector3D at = rowDirection * (_surface.Radius + baseHeight - embed);
                if (at.Length >= previousRadius - 0.4) break;
                previousRadius = at.Length;
                result.Add(new CliffPlacement(tile, model, at, rowAlong, rowDirection, rowFace, scale,
                    part * MaximumTiers + tier));
            }
        }
        return result.ToArray();
    }

    private bool Slope(Vector3D up, out Vector3D forward, out Vector3D right, out double gradient)
    {
        forward = right = Vector3D.Zero;
        gradient = 0;
        if (_surface is WalkingWorld walking && walking.Monuments.At(up, MaximumExtent) != null) return false;
        double height = _surface.HeightAt(up);
        if (height < _surface.WaterLevelAt(up) + 0.3) return false;
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
        Vector3D south = Vector3D.Cross(east, up);
        Vector3D ground = up * (_surface.Radius + height);
        double Rise(Vector3D axis) => (_surface.HeightAt((ground + axis * 5).Normalized)
            - _surface.HeightAt((ground - axis * 5).Normalized)) / 10;
        double x = Rise(east), z = Rise(south);
        gradient = Math.Sqrt(x * x + z * z);
        if (gradient < 0.35 || gradient > 3.5) return false;
        forward = -(east * x + south * z).Normalized;
        right = Vector3D.Cross(up, forward).Normalized;
        return true;
    }

    public List<CliffPlacement> Near(Vector3D position, double reach)
    {
        var tiles = new List<TileKey>();
        TileKey.Around(position, reach + MaximumExtent + MaximumOffset, _surface.Radius, Level, tiles);
        var result = new List<CliffPlacement>();
        foreach (TileKey tile in tiles)
        {
            foreach (CliffPlacement cliff in FormationAt(tile))
                if (Vector3D.Distance(position, cliff.Position) <= reach + MaximumExtent) result.Add(cliff);
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
