using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Ground triangles of a cube-sphere tile from any surface's heights (docs/WHOLE_WORLD.md §6.1): a grid of the face's
/// own dyadic coordinates, a power of two cells a tile, widened by whole cells for the margin, each point lifted to the
/// ground along its direction. Neighbouring tiles of a face share their grid points bit for bit where they overlap,
/// and everything is a pure function of the surface, so the server and every client build the same triangles. Points
/// may run a little past the face's edge: the cube-sphere's warp is defined there and lands on the neighbouring face.
/// </summary>
public static class SurfaceGrid
{
    /// <summary>About a metre between grid points.</summary>
    public const double DefaultSpacing = 1.0;

    /// <summary>The most cells a side of one tile's grid gets, whatever its size.</summary>
    public const int MostCells = 128;

    /// <summary>Cells a side for a tile: the power of two nearest its edge over the spacing, 1 … <see cref="MostCells"/>.</summary>
    public static int CellsFor(TileKey tile, double radius, double spacing)
    {
        double wanted = TileKey.EdgeLength(tile.Level, radius) / spacing;
        int cells = 1;
        while (cells < MostCells && cells * 1.5 < wanted)
        {
            cells *= 2;
        }
        return cells;
    }

    public static TriangleMesh Collision(ISurface surface, in ZoneFrame frame, TileKey tile, double margin, double spacing)
    {
        int cells = CellsFor(tile, surface.Radius, spacing);
        double cellMetres = TileKey.EdgeLength(tile.Level, surface.Radius) / cells;
        int extra = Math.Max(0, (int)Math.Ceiling(margin / cellMetres));
        int first = -extra, count = cells + 2 * extra, n = count + 1;
        var positions = new float[n * n * 3];
        for (int j = 0; j < n; j++)
        {
            double v = TileKey.GridCoordinate(tile.Y, tile.Level, first + j, cells);
            for (int i = 0; i < n; i++)
            {
                double u = TileKey.GridCoordinate(tile.X, tile.Level, first + i, cells);
                Vector3D direction = CubeSphere.Direction(tile.Face, u, v);
                System.Numerics.Vector3 local = frame.ToLocal(direction * (surface.Radius + surface.HeightAt(direction)));
                int k = (j * n + i) * 3;
                positions[k] = local.X;
                positions[k + 1] = local.Y;
                positions[k + 2] = local.Z;
            }
        }
        var indices = new int[count * count * 6];
        int t = 0;
        for (int j = 0; j < count; j++)
        {
            for (int i = 0; i < count; i++)
            {
                // (u, v) runs counter-clockwise seen from outside the planet (CubeSphere), so (b − a) × (c − a) points up.
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                indices[t++] = a;
                indices[t++] = b;
                indices[t++] = d;
                indices[t++] = a;
                indices[t++] = d;
                indices[t++] = c;
            }
        }
        return new TriangleMesh(positions, indices);
    }
}
