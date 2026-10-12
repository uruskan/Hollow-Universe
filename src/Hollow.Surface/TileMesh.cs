using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// The ground of one tile as a grid of (cells + 1)² vertices (GROUND.md §4.6). The client draws it and the server's
/// collision is made of the very same vertices, so feet stand on what is drawn. Positions are float offsets from
/// <see cref="Origin"/> in body-frame axes; each vertex also carries where it sits in the parent tile's coarser grid
/// (for morphing between levels of detail), its ground materials and the climate the shader tints with.
/// </summary>
public sealed class TileGrid
{
    internal TileGrid(TileKey key, int cells)
    {
        Key = key;
        Cells = cells;
        int n = (cells + 1) * (cells + 1);
        Positions = new float[n * 3];
        Normals = new float[n * 3];
        MorphPositions = new float[n * 3];
        MorphNormals = new float[n * 3];
        Materials = new byte[n * BiomeTable.MaterialCount];
        Climate = new byte[n * 4];
        Heights = new float[n];
    }

    public TileKey Key { get; }

    /// <summary>Cells per edge; the grid has <c>Cells + 1</c> vertices per edge.</summary>
    public int Cells { get; }

    public int VertexCount => (Cells + 1) * (Cells + 1);

    /// <summary>Body-frame point the positions are relative to: the tile centre at the mean radius.</summary>
    public Vector3D Origin { get; internal set; }

    /// <summary>x, y, z per vertex, metres from <see cref="Origin"/>, body-frame axes. Row-major, u fastest.</summary>
    public float[] Positions { get; }

    /// <summary>Unit normal per vertex (body-frame axes).</summary>
    public float[] Normals { get; }

    /// <summary>Where each vertex lies in the parent tile's grid (same units as <see cref="Positions"/>).</summary>
    public float[] MorphPositions { get; }

    /// <summary>The parent grid's normal at each vertex (not normalised).</summary>
    public float[] MorphNormals { get; }

    /// <summary>Eight ground material weights per vertex (<see cref="GroundMaterial"/> order), 0–255, adding up to about 255.</summary>
    public byte[] Materials { get; }

    /// <summary>Per vertex: temperature (−30 to 40 °C as 0–255), moisture, grass density and tree cover (0–255).</summary>
    public byte[] Climate { get; }

    /// <summary>Height above sea level per vertex, metres.</summary>
    public float[] Heights { get; }

    public float MinHeight { get; internal set; }
    public float MaxHeight { get; internal set; }

    /// <summary>Largest distance of any vertex from <see cref="Origin"/>, metres.</summary>
    public float BoundingRadius { get; internal set; }

    /// <summary>Body-frame position of a vertex.</summary>
    public Vector3D Vertex(int index) =>
        Origin + new Vector3D(Positions[3 * index], Positions[3 * index + 1], Positions[3 * index + 2]);
}

/// <summary>Builds tile grids and collision from a <see cref="PlanetSurface"/>.</summary>
public static class TileMesh
{
    /// <summary>
    /// Triangle indices of a grid of <paramref name="cells"/>² quads, each split along its (i, j)–(i+1, j+1)
    /// diagonal, counter-clockwise seen from outside the planet. The same diagonal at every level, so a grid
    /// morphed onto its parent's coarser grid lies exactly on the parent's triangles.
    /// </summary>
    public static int[] Indices(int cells)
    {
        int n = cells + 1;
        var indices = new int[cells * cells * 6];
        int k = 0;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = d;
                indices[k++] = a; indices[k++] = d; indices[k++] = c;
            }
        }
        return indices;
    }

    /// <summary>Unit direction of grid vertex (i, j) of a tile (i, j may step one beyond the tile, for normals).</summary>
    public static Vector3D Direction(TileKey key, int cells, int i, int j) =>
        CubeSphere.Direction(key.Face, TileKey.GridCoordinate(key.X, key.Level, i, cells), TileKey.GridCoordinate(key.Y, key.Level, j, cells));

    /// <summary>Body-frame positions of a tile's (cells + 1)² vertices, row-major: what collision is built from.</summary>
    public static Vector3D[] Positions(ISurface surface, TileKey key, int cells)
    {
        int n = cells + 1;
        var positions = new Vector3D[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3D d = Direction(key, cells, i, j);
                positions[j * n + i] = d * (surface.Radius + surface.HeightAt(d));
            }
        }
        return positions;
    }

    /// <summary>The full grid of a tile for drawing: positions, normals, morph targets, materials and climate.</summary>
    public static TileGrid Build(PlanetSurface surface, TileKey key, int cells = PlanetSurface.TileCells)
    {
        var grid = new TileGrid(key, cells);
        int n = cells + 1, m = cells + 3; // m: with a ring of samples around the tile for normals
        var points = new Vector3D[m * m];
        var samples = new SurfaceSample[m * m];
        var directions = new Vector3D[m * m];
        for (int j = 0; j < m; j++)
        {
            for (int i = 0; i < m; i++)
            {
                Vector3D d = Direction(key, cells, i - 1, j - 1);
                SurfaceSample s = surface.Sample(d);
                int k = j * m + i;
                directions[k] = d;
                samples[k] = s;
                points[k] = d * (surface.Radius + s.Height);
            }
        }
        Vector3D origin = key.Centre * surface.Radius;
        grid.Origin = origin;

        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        float minH = float.MaxValue, maxH = float.MinValue, radius = 0;
        var normals = new Vector3D[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int k = (j + 1) * m + (i + 1);
                int v = j * n + i;
                Vector3D p = points[k];
                Vector3D du = points[k + 1] - points[k - 1], dv = points[k + m] - points[k - m];
                Vector3D normal = Vector3D.Cross(du, dv).Normalized;
                normals[v] = normal;
                Vector3D offset = p - origin;
                grid.Positions[3 * v] = (float)offset.X;
                grid.Positions[3 * v + 1] = (float)offset.Y;
                grid.Positions[3 * v + 2] = (float)offset.Z;
                grid.Normals[3 * v] = (float)normal.X;
                grid.Normals[3 * v + 1] = (float)normal.Y;
                grid.Normals[3 * v + 2] = (float)normal.Z;
                radius = Math.Max(radius, (float)offset.Length);

                SurfaceSample s = samples[k];
                float h = (float)s.Height;
                grid.Heights[v] = h;
                minH = Math.Min(minH, h);
                maxH = Math.Max(maxH, h);

                Vector3D d = directions[k];
                double up = Vector3D.Dot(normal, d);
                double slope = Math.Sqrt(Math.Max(0, 1 - up * up));
                ClimateSample climate = surface.Climate(d, s);
                surface.BiomeWeights(d, s, climate, biomes);
                VegetationSample vegetation = surface.Vegetation(d, s, climate, biomes, slope);
                double treeCover = Math.Clamp(vegetation.Trees / 0.022, 0, 1);
                surface.Materials(s, climate, biomes, slope, treeCover, materials);
                PackWeights(materials, grid.Materials, v * BiomeTable.MaterialCount);
                grid.Climate[4 * v] = ToByte((climate.Temperature + 30) / 70);
                grid.Climate[4 * v + 1] = ToByte(climate.Moisture);
                grid.Climate[4 * v + 2] = ToByte(vegetation.Grass);
                grid.Climate[4 * v + 3] = ToByte(treeCover);
            }
        }
        grid.MinHeight = minH;
        grid.MaxHeight = maxH;
        grid.BoundingRadius = radius;

        // Morph targets: even vertices are the parent's own; odd ones sit on the parent's edges and diagonals.
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int v = j * n + i;
                int a, b;
                bool oddI = (i & 1) == 1, oddJ = (j & 1) == 1;
                if (oddI && oddJ)
                {
                    a = (j - 1) * n + (i - 1);
                    b = (j + 1) * n + (i + 1);
                }
                else if (oddI)
                {
                    a = v - 1;
                    b = v + 1;
                }
                else if (oddJ)
                {
                    a = v - n;
                    b = v + n;
                }
                else
                {
                    a = b = v;
                }
                for (int c = 0; c < 3; c++)
                {
                    grid.MorphPositions[3 * v + c] = (grid.Positions[3 * a + c] + grid.Positions[3 * b + c]) * 0.5f;
                    grid.MorphNormals[3 * v + c] = (grid.Normals[3 * a + c] + grid.Normals[3 * b + c]) * 0.5f;
                }
            }
        }
        return grid;
    }

    /// <summary>
    /// Ground triangles in a zone's frame covering the square of side <paramref name="size"/> centred on
    /// (<paramref name="centreX"/>, <paramref name="centreZ"/>): every tile of <paramref name="level"/> the square
    /// touches, with exactly the vertices the client draws for those tiles.
    /// </summary>
    public static TriangleMesh Collision(ISurface surface, in ZoneFrame frame, double centreX, double centreZ, double size,
        int level, int cells)
    {
        List<TileKey> tiles = TilesCovering(surface, frame, centreX, centreZ, size, level);
        int n = cells + 1;
        int[] gridIndices = Indices(cells);
        var positions = new float[tiles.Count * n * n * 3];
        var indices = new int[tiles.Count * gridIndices.Length];
        for (int t = 0; t < tiles.Count; t++)
        {
            Vector3D[] points = Positions(surface, tiles[t], cells);
            int baseVertex = t * n * n;
            for (int v = 0; v < points.Length; v++)
            {
                var local = frame.ToLocal(points[v]);
                int k = (baseVertex + v) * 3;
                positions[k] = local.X;
                positions[k + 1] = local.Y;
                positions[k + 2] = local.Z;
            }
            int baseIndex = t * gridIndices.Length;
            for (int i = 0; i < gridIndices.Length; i++)
            {
                indices[baseIndex + i] = baseVertex + gridIndices[i];
            }
        }
        return new TriangleMesh(positions, indices);
    }

    /// <summary>The tiles of a level that a square of a zone's tangent plane touches, in a fixed order.</summary>
    public static List<TileKey> TilesCovering(ISurface surface, in ZoneFrame frame, double centreX, double centreZ, double size, int level)
    {
        double edge = TileKey.EdgeLength(level, surface.Radius);
        int steps = Math.Max(2, (int)Math.Ceiling(size / (edge * 0.3)) + 1);
        var keys = new SortedSet<ulong>();
        for (int j = 0; j < steps; j++)
        {
            for (int i = 0; i < steps; i++)
            {
                double x = centreX - size / 2 + size * i / (steps - 1);
                double z = centreZ - size / 2 + size * j / (steps - 1);
                keys.Add(TileKey.At(frame.ToBody(x, 0, z), level).Packed);
            }
        }
        var tiles = new List<TileKey>(keys.Count);
        foreach (ulong k in keys)
        {
            tiles.Add(TileKey.FromPacked(k)!.Value);
        }
        return tiles;
    }

    private static void PackWeights(ReadOnlySpan<double> weights, byte[] target, int offset)
    {
        double sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            sum += Math.Max(0, weights[i]);
        }
        double scale = sum > 0 ? 255 / sum : 0;
        for (int i = 0; i < weights.Length; i++)
        {
            target[offset + i] = (byte)Math.Clamp((int)Math.Floor(Math.Max(0, weights[i]) * scale + 0.5), 0, 255);
        }
    }

    private static byte ToByte(double x) => (byte)Math.Clamp((int)Math.Floor(x * 255 + 0.5), 0, 255);
}
