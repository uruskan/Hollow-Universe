using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// A historical draw snapshot. The grid's arrays are frozen after worker publication; consumers must never mutate
/// them. No scene node, surface, frame or world owner is retained. Refresh after ContactRevision changes.
/// </summary>
internal readonly record struct DrawnTerrainTile(TileKey Key, TileGrid Grid, Transform3D Transform,
    Vector2 MorphRange, Aabb Bounds, long ContactRevision);

/// <summary>Contact on a regular drawn triangle, excluding skirts. Normal follows the grid's outward winding.</summary>
internal readonly record struct TerrainContactSample(Vector3 RenderPosition, Vector3 GeometricNormal,
    int GridTriangle, Vector3 Barycentric, Vector3 VertexMorph);

public sealed partial class TerrainLod
{
    internal const int ContactCells = 32;
    internal const int ContactTileCapacity = 128;
    internal long ContactRevision { get; private set; }

    // Values contain no reference back to their weak grid key. Retired snapshots may keep their arrays alive, but
    // this cache cannot retain a retired grid, scene node or world.
    private sealed record ContactGridBounds(bool Valid, Aabb Local);
    private readonly ConditionalWeakTable<TileGrid, ContactGridBounds> _contactBounds = new();

    private void InvalidateContactTiles() => ContactRevision = checked(ContactRevision + 1);

    /// <summary>
    /// Copies only currently drawn regular grids intersecting a render-space sphere around the body-frame camera.
    /// Main thread only. A stopped owner yields an empty successful snapshot. Unsupported grids or capacity overflow
    /// fail with an empty destination; a prefix is never usable. The hard contract is 32 cells and at most 128 tiles.
    /// </summary>
    internal bool CopyContactTiles(Vector3D camera, double reach, List<DrawnTerrainTile> destination,
        int maxTiles = ContactTileCapacity)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        if (!double.IsFinite(camera.X) || !double.IsFinite(camera.Y) || !double.IsFinite(camera.Z))
            throw new ArgumentOutOfRangeException(nameof(camera));
        if (!double.IsFinite(reach) || reach < 0 || reach > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(reach));
        if (maxTiles < 1 || maxTiles > ContactTileCapacity)
            throw new ArgumentOutOfRangeException(nameof(maxTiles));
        if (_work == null || _work.Stopped || _frame == null) return true;
        Vector3 renderCamera = _frame.ToRender(camera);
        if (!ContactFinite(renderCamera)) return false;
        foreach (Node node in _drawn)
        {
            TileGrid? grid = node.Grid;
            if (!node.Drawn || grid == null || node.Instance == null || !node.Instance.IsVisibleInTree()
                || node.Instance.Mesh == null || !node.Key.Equals(grid.Key))
                return FailedContactCopy(destination);
            ContactGridBounds cached = _contactBounds.GetValue(grid, BuildContactBounds);
            Transform3D transform = node.Instance.GlobalTransform;
            if (!cached.Valid || !ContactFinite(transform.Origin) || !ContactFinite(transform.Basis.X)
                || !ContactFinite(transform.Basis.Y) || !ContactFinite(transform.Basis.Z)
                || !float.IsFinite(node.MorphRange.X) || !float.IsFinite(node.MorphRange.Y))
                return FailedContactCopy(destination);
            Aabb bounds = transform * cached.Local;
            if (!ContactFinite(bounds.Position) || !ContactFinite(bounds.End))
                return FailedContactCopy(destination);
            // Affine AABB arithmetic and shader mixing may round a boundary by a few float ULPs. This is a
            // conservative culling envelope, not a displacement/contact offset applied to sampled positions.
            float extent = Math.Max(Math.Max(Math.Abs(bounds.Position.X), Math.Abs(bounds.End.X)),
                Math.Max(Math.Max(Math.Abs(bounds.Position.Y), Math.Abs(bounds.End.Y)),
                    Math.Max(Math.Abs(bounds.Position.Z), Math.Abs(bounds.End.Z))));
            bounds = bounds.Grow(Math.Max(0.002f, extent * (8 * 1.1920929e-7f)));
            if (!ContactFinite(bounds.Position) || !ContactFinite(bounds.End))
                return FailedContactCopy(destination);
            Vector3 closest = new(Math.Clamp(renderCamera.X, bounds.Position.X, bounds.End.X),
                Math.Clamp(renderCamera.Y, bounds.Position.Y, bounds.End.Y),
                Math.Clamp(renderCamera.Z, bounds.Position.Z, bounds.End.Z));
            double dx = (double)closest.X - renderCamera.X, dy = (double)closest.Y - renderCamera.Y,
                dz = (double)closest.Z - renderCamera.Z;
            if (dx * dx + dy * dy + dz * dz > reach * reach) continue;
            if (destination.Count == maxTiles) return FailedContactCopy(destination);
            destination.Add(new DrawnTerrainTile(node.Key, grid, transform, node.MorphRange, bounds, ContactRevision));
        }
        destination.Sort(static (a, b) => a.Key.Packed.CompareTo(b.Key.Packed));
        return true;
    }

    private static bool FailedContactCopy(List<DrawnTerrainTile> destination)
    {
        destination.Clear();
        return false;
    }

    private static ContactGridBounds BuildContactBounds(TileGrid grid)
    {
        const int components = (ContactCells + 1) * (ContactCells + 1) * 3;
        if (grid.Cells != ContactCells || grid.Positions.Length != components || grid.MorphPositions.Length != components)
            return new ContactGridBounds(false, default);
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int offset = 0; offset < components; offset += 3)
        {
            Vector3 fine = ContactVertex(grid.Positions, offset / 3), morph = ContactVertex(grid.MorphPositions, offset / 3);
            if (!ContactFinite(fine) || !ContactFinite(morph)) return new ContactGridBounds(false, default);
            minimum = new Vector3(Math.Min(minimum.X, Math.Min(fine.X, morph.X)),
                Math.Min(minimum.Y, Math.Min(fine.Y, morph.Y)), Math.Min(minimum.Z, Math.Min(fine.Z, morph.Z)));
            maximum = new Vector3(Math.Max(maximum.X, Math.Max(fine.X, morph.X)),
                Math.Max(maximum.Y, Math.Max(fine.Y, morph.Y)), Math.Max(maximum.Z, Math.Max(fine.Z, morph.Z)));
        }
        return new ContactGridBounds(true, new Aabb(minimum, maximum - minimum));
    }

    /// <summary>
    /// Samples an explicit tile at cube-face coordinates in [0,1]. Both tile edges are accepted, including face UV=1;
    /// choosing ownership at a shared edge is the caller's job. Reproduces surface_terrain.gdshader vertex morphing:
    /// distance is measured from EACH transformed fine vertex, local positions are mixed, then the drawn triangle
    /// is interpolated. This samples the supplied snapshot, not a hidden/current surface or a collision lattice.
    /// </summary>
    internal static bool TrySampleContact(in DrawnTerrainTile tile, TileKey key, Vector2 cubeFaceUv01,
        Vector3 cameraRender, out TerrainContactSample result)
    {
        result = default;
        const int side = ContactCells + 1, components = side * side * 3;
        TileGrid? grid = tile.Grid;
        if (grid == null || !tile.Key.Equals(key) || !grid.Key.Equals(key) || grid.Cells != ContactCells
            || grid.Positions.Length != components || grid.MorphPositions.Length != components
            || !ContactFinite(cameraRender) || !float.IsFinite(cubeFaceUv01.X) || !float.IsFinite(cubeFaceUv01.Y)
            || cubeFaceUv01.X < 0 || cubeFaceUv01.X > 1 || cubeFaceUv01.Y < 0 || cubeFaceUv01.Y > 1
            || !float.IsFinite(tile.MorphRange.X) || !float.IsFinite(tile.MorphRange.Y)) return false;
        double count = 1L << key.Level;
        double u = ((double)cubeFaceUv01.X * count - key.X) * ContactCells;
        double v = ((double)cubeFaceUv01.Y * count - key.Y) * ContactCells;
        if (u < 0 || u > ContactCells || v < 0 || v > ContactCells) return false;
        int i = Math.Min((int)u, ContactCells - 1), j = Math.Min((int)v, ContactCells - 1);
        float x = (float)(u - i), y = (float)(v - j);
        int a = j * side + i, b, c, half;
        Vector3 weights;
        if (y <= x)
        {
            b = a + 1; c = a + side + 1; half = 0;
            weights = new Vector3(1 - x, x - y, y); // a, b, d
        }
        else
        {
            b = a + side + 1; c = a + side; half = 1;
            weights = new Vector3(1 - y, x, y - x); // a, d, c
        }
        if (!DrawContactVertex(tile, a, cameraRender, out Vector3 pa, out float ka)
            || !DrawContactVertex(tile, b, cameraRender, out Vector3 pb, out float kb)
            || !DrawContactVertex(tile, c, cameraRender, out Vector3 pc, out float kc)) return false;
        Vector3 normal = (pb - pa).Cross(pc - pa);
        float length = normal.Length();
        Vector3 position = pa * weights.X + pb * weights.Y + pc * weights.Z;
        if (!ContactFinite(position) || !ContactFinite(normal) || !float.IsFinite(length) || length <= 0) return false;
        result = new TerrainContactSample(position, normal / length, (j * ContactCells + i) * 2 + half,
            weights, new Vector3(ka, kb, kc));
        return true;
    }

    private static bool DrawContactVertex(in DrawnTerrainTile tile, int vertex, Vector3 camera,
        out Vector3 position, out float morph)
    {
        Vector3 fine = ContactVertex(tile.Grid.Positions, vertex), parent = ContactVertex(tile.Grid.MorphPositions, vertex);
        Vector3 transformedFine = tile.Transform * fine;
        float distance = (transformedFine - camera).Length();
        morph = Math.Clamp((distance - tile.MorphRange.X) / Math.Max(tile.MorphRange.Y - tile.MorphRange.X, 1f), 0f, 1f);
        // Match GLSL mix's two weighted operands, rather than mixing the already interpolated contact point.
        position = tile.Transform * (fine * (1 - morph) + parent * morph);
        return ContactFinite(fine) && ContactFinite(parent) && float.IsFinite(distance) && float.IsFinite(morph)
            && ContactFinite(position);
    }

    private static Vector3 ContactVertex(float[] values, int vertex) =>
        new(values[3 * vertex], values[3 * vertex + 1], values[3 * vertex + 2]);

    private static bool ContactFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
