using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// The ground of one tile as a grid of (cells + 1)² vertices (GROUND.md §4.6), which the client draws. On a whole world
/// the collision comes from <see cref="SurfaceGrid"/>; drawing the same points is S3c's (docs/WHOLE_WORLD.md §10.1),
/// so that feet stand on what is drawn again. Positions are float offsets from
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
        FaceMetres = new float[n * 2];
        WaterLevels = new float[n];
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

    /// <summary>
    /// <see cref="BiomeTable.MaterialCount"/> ground material weights per vertex (<see cref="GroundMaterial"/> order), 0–255,
    /// adding up to about 255.
    /// </summary>
    public byte[] Materials { get; }

    /// <summary>Per vertex: temperature (−30 to 40 °C as 0–255), moisture, grass density and tree cover (0–255).</summary>
    public byte[] Climate { get; }

    /// <summary>Height above sea level per vertex, metres (above the mean radius on a world without a sea).</summary>
    public float[] Heights { get; }

    /// <summary>
    /// The water's surface per vertex, metres above sea level (docs/WALKING_GROUND.md §12): one water for the sea, lakes
    /// and streams, from <see cref="ISurface.WaterLevelAt"/>. Where a vertex is dry but water lies within two vertices,
    /// that water's level, so shores cut where the ground crosses it; elsewhere 2 m under the ground, hidden. Read only
    /// when <see cref="HasWater"/>.
    /// </summary>
    public float[] WaterLevels { get; }

    /// <summary>Whether any vertex lies under water.</summary>
    public bool HasWater { get; internal set; }

    /// <summary>The surface's mean radius and its sea's height above it (0 without a sea): what the levels are measured from.</summary>
    public double Radius { get; internal set; }

    public double SeaLevel { get; internal set; }

    /// <summary>
    /// Texture coordinates per vertex: metres along the tile's cube face (u and v, <see cref="TileMesh.FaceMetresOf"/>)
    /// less <see cref="FaceAnchor"/>, so they stay small in floats on a planet of any size (docs/WHOLE_WORLD.md §10.1).
    /// </summary>
    public float[] FaceMetres { get; }

    /// <summary>
    /// Metres along the face taken off <see cref="FaceMetres"/>: the tile centre's, rounded to a multiple of
    /// <see cref="TileMesh.TexturePeriod"/> (zero everywhere on a world under 8 km across).
    /// </summary>
    public (double U, double V) FaceAnchor { get; internal set; }

    public float MinHeight { get; internal set; }
    public float MaxHeight { get; internal set; }

    /// <summary>Largest distance of any vertex from <see cref="Origin"/>, metres.</summary>
    public float BoundingRadius { get; internal set; }

    /// <summary>Body-frame position of a vertex.</summary>
    public Vector3D Vertex(int index) =>
        Origin + new Vector3D(Positions[3 * index], Positions[3 * index + 1], Positions[3 * index + 2]);
}

/// <summary>Builds tile grids for drawing from any <see cref="ISurface"/>, and collision patches of a tangent plane.</summary>
public static class TileMesh
{
    /// <summary>
    /// Texture coordinates are kept within this many metres of a multiple of it (<see cref="TileGrid.FaceAnchor"/>), so
    /// floats hold them to a fraction of a millimetre; a world smaller than it never needs an anchor.
    /// </summary>
    public const double TexturePeriod = 4096;

    /// <summary>
    /// Metres along a cube face for face coordinates (u, v) on a sphere of a radius: π/4 × radius × (u, v), the arc from
    /// the face's centre along its axes (the cube-sphere's warp is the tangent of π/4 × u). The shaders work out the
    /// same from a direction, as radius × atan(d·U / d·N).
    /// </summary>
    public static (double U, double V) FaceMetresOf(double u, double v, double radius) => (u * DMath.Pi / 4 * radius, v * DMath.Pi / 4 * radius);

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
        TerrainLattice? lattice = TerrainLattice.Of(surface);
        TerrainPinSet? pins = TerrainPinSet.Of(surface);
        lattice?.ValidateSampleGrid(key, cells);
        int n = cells + 1;
        var positions = new Vector3D[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3D d = lattice?.GridDirection(key, cells, i, j) ?? Direction(key, cells, i, j);
                positions[j * n + i] = pins != null && lattice!.TryGridVertex(key, cells, i, j, out CubeVertexKey vertex) &&
                    pins.TryGet(vertex, out TerrainPinnedVertex pin) ? pin.Position : d * (surface.Radius + surface.HeightAt(d));
            }
        }
        return positions;
    }

    /// <summary>
    /// The full grid of a tile for drawing, from any surface: positions, normals, morph targets, materials and climate
    /// (<see cref="ISurface.Cover"/>), and texture coordinates on the cube face.
    /// </summary>
    public static TileGrid Build(ISurface surface, TileKey key, int cells = PlanetSurface.TileCells)
    {
        TerrainLattice? lattice = TerrainLattice.Of(surface);
        TerrainPinSet? pins = TerrainPinSet.Of(surface);
        lattice?.ValidateRenderGrid(key, cells);
        var grid = new TileGrid(key, cells);
        // m: with two rings of samples around the tile, for normals and for the water's shores.
        const int Ring = 2;
        int n = cells + 1, m = cells + 1 + 2 * Ring;
        var points = new Vector3D[m * m];
        var samples = new SurfaceSample[m * m];
        var directions = new Vector3D[m * m];
        var water = new double[m * m];
        double seaLevel = surface.SeaRadius > 0 ? surface.SeaRadius - surface.Radius : 0;
        for (int j = 0; j < m; j++)
        {
            for (int i = 0; i < m; i++)
            {
                Vector3D d = lattice?.GridDirection(key, cells, i - Ring, j - Ring) ?? Direction(key, cells, i - Ring, j - Ring);
                SurfaceSample s = surface.Sample(d);
                int k = j * m + i;
                directions[k] = d;
                samples[k] = s;
                points[k] = pins != null && lattice!.TryGridVertex(key, cells, i - Ring, j - Ring, out CubeVertexKey vertex) &&
                    pins.TryGet(vertex, out TerrainPinnedVertex pin) ? pin.Position : d * (surface.Radius + s.Height);
                water[k] = surface.WaterLevelAt(d);
            }
        }
        Vector3D origin = key.Centre * surface.Radius;
        grid.Origin = origin;
        grid.Radius = surface.Radius;
        grid.SeaLevel = seaLevel;
        WaterLevels(grid, samples, water, m, Ring, seaLevel);
        (double centreU, double centreV) = FaceMetresOf(key.U0 + key.Span / 2, key.V0 + key.Span / 2, surface.Radius);
        double anchorU = Math.Round(centreU / TexturePeriod) * TexturePeriod, anchorV = Math.Round(centreV / TexturePeriod) * TexturePeriod;
        grid.FaceAnchor = (anchorU, anchorV);

        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        float minH = float.MaxValue, maxH = float.MinValue, radius = 0;
        var normals = new Vector3D[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int k = (j + Ring) * m + (i + Ring);
                int v = j * n + i;
                Vector3D p = points[k];
                Vector3D du = points[k + 1] - points[k - 1], dv = points[k + m] - points[k - m];
                Vector3D normal = Vector3D.Cross(du, dv).Normalized;
                if (pins != null && lattice!.TryGridVertex(key, cells, i, j, out CubeVertexKey vertex) &&
                    pins.TryGet(vertex, out TerrainPinnedVertex pin)) normal = pin.Normal;
                normals[v] = normal;
                Vector3D offset = p - origin;
                grid.Positions[3 * v] = (float)offset.X;
                grid.Positions[3 * v + 1] = (float)offset.Y;
                grid.Positions[3 * v + 2] = (float)offset.Z;
                grid.Normals[3 * v] = (float)normal.X;
                grid.Normals[3 * v + 1] = (float)normal.Y;
                grid.Normals[3 * v + 2] = (float)normal.Z;
                radius = Math.Max(radius, (float)offset.Length);
                (double fu, double fv) = FaceMetresOf(TileKey.GridCoordinate(key.X, key.Level, i, cells),
                    TileKey.GridCoordinate(key.Y, key.Level, j, cells), surface.Radius);
                grid.FaceMetres[2 * v] = (float)(fu - anchorU);
                grid.FaceMetres[2 * v + 1] = (float)(fv - anchorV);

                SurfaceSample s = samples[k];
                float h = (float)(s.Height - seaLevel);
                grid.Heights[v] = h;
                minH = Math.Min(minH, h);
                maxH = Math.Max(maxH, h);

                Vector3D d = directions[k];
                double up = Vector3D.Dot(normal, d);
                double slope = Math.Sqrt(Math.Max(0, 1 - up * up));
                GroundCover cover = surface.Cover(d, s, slope, biomes, materials);
                PackWeights(materials, grid.Materials, v * BiomeTable.MaterialCount);
                grid.Climate[4 * v] = ToByte((cover.Temperature + 30) / 70);
                grid.Climate[4 * v + 1] = ToByte(cover.Moisture);
                grid.Climate[4 * v + 2] = ToByte(cover.Grass);
                grid.Climate[4 * v + 3] = ToByte(cover.TreeCover);
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
                if (pins != null && lattice!.TryGridVertex(key, cells, i, j, out CubeVertexKey vertex) && pins.TryGet(vertex, out _))
                    for (int c = 0; c < 3; c++)
                    {
                        grid.MorphPositions[3 * v + c] = grid.Positions[3 * v + c];
                        grid.MorphNormals[3 * v + c] = grid.Normals[3 * v + c];
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
        var positions = new float[tiles.Count * n * n * 3];
        var indices = new int[tiles.Count * cells * cells * 6];
        WriteCollision(surface, frame, tiles, cells, positions, indices);
        return new TriangleMesh(positions, indices);
    }

    /// <summary>
    /// <see cref="Collision"/>'s triangles, the same to the bit, in arrays rented from the shared pools: the physics
    /// builds ground tiles all the time, and each made 600 KB of garbage, most of it large-object arrays that bring on
    /// full collections (GROUND.md §5.11). Dispose the result to give them back.
    /// </summary>
    public static PooledTriangles CollisionPooled(ISurface surface, in ZoneFrame frame, double centreX, double centreZ, double size,
        int level, int cells, GridHeights? heights = null)
    {
        List<TileKey> tiles = TilesCovering(surface, frame, centreX, centreZ, size, level);
        int n = cells + 1;
        int vertices = tiles.Count * n * n, indexCount = tiles.Count * cells * cells * 6;
        float[] positions = ArrayPool<float>.Shared.Rent(vertices * 3);
        int[] indices = ArrayPool<int>.Shared.Rent(indexCount);
        WriteCollision(surface, frame, tiles, cells, positions, indices, heights);
        return new PooledTriangles(positions, indices, vertices, indexCount / 3, rented: true);
    }

    private static readonly ConcurrentDictionary<int, int[]> IndexCache = new();

    // Every grid's vertices in the zone's frame (as Positions computes them) and its triangles, one grid after another.
    private static void WriteCollision(ISurface surface, in ZoneFrame frame, List<TileKey> tiles, int cells, float[] positions, int[] indices,
        GridHeights? heights = null)
    {
        int n = cells + 1;
        int[] gridIndices = IndexCache.GetOrAdd(cells, Indices);
        for (int t = 0; t < tiles.Count; t++)
        {
            TileKey key = tiles[t];
            int baseVertex = t * n * n;
            double[]? known = heights?.Get(surface, key, cells);
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3D d = Direction(key, cells, i, j);
                    var local = frame.ToLocal(d * (surface.Radius + (known != null ? known[j * n + i] : surface.HeightAt(d))));
                    int k = (baseVertex + j * n + i) * 3;
                    positions[k] = local.X;
                    positions[k + 1] = local.Y;
                    positions[k + 2] = local.Z;
                }
            }
            int baseIndex = t * gridIndices.Length;
            for (int i = 0; i < gridIndices.Length; i++)
            {
                indices[baseIndex + i] = baseVertex + gridIndices[i];
            }
        }
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

    // One water for every vertex (TileGrid.WaterLevels): its own where it lies under water; a dry vertex within two
    // steps of water takes the highest such level (so the water's triangles stay flat to the shore, and a morph between
    // levels of detail never lifts water out of the ground); any other sits 2 m under its ground.
    private static void WaterLevels(TileGrid grid, SurfaceSample[] samples, double[] water, int m, int ring, double seaLevel)
    {
        int n = grid.Cells + 1;
        bool any = false;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int k = (j + ring) * m + (i + ring);
                double ground = samples[k].Height;
                double level;
                if (water[k] > ground)
                {
                    level = water[k];
                    any = true;
                }
                else
                {
                    level = double.NegativeInfinity;
                    for (int b = -2; b <= 2; b++)
                    {
                        for (int a = -2; a <= 2; a++)
                        {
                            int q = k + b * m + a;
                            if (water[q] > samples[q].Height && water[q] > level)
                            {
                                level = water[q];
                            }
                        }
                    }
                    if (level == double.NegativeInfinity)
                    {
                        level = ground - 2;
                    }
                }
                grid.WaterLevels[j * n + i] = (float)(level - seaLevel);
            }
        }
        grid.HasWater = any;
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

/// <summary>
/// The heights of grids' vertices, kept for the grids around them (GROUND.md §5.11): a 64 m physics tile samples
/// about nine level-18 grids and its neighbours share most of them, so each grid's 1,089 heights are worked out once
/// instead of three or four times. Heights are a pure function of the surface, so a kept one is the same to the bit.
/// Thread-safe; the least recently used quarter goes when it holds more than its limit.
/// </summary>
public sealed class GridHeights
{
    private readonly ConcurrentDictionary<(ulong Key, int Cells), Grid> _grids = new();
    private readonly int _limit;
    private long _clock;
    private int _trimming;

    public GridHeights(int limit) => _limit = limit;

    /// <summary>Grids kept now.</summary>
    public int Count => _grids.Count;

    private sealed class Grid(double[] heights)
    {
        public readonly double[] Heights = heights;
        public long LastUsed;
    }

    /// <summary>The heights above the surface's radius of a grid's (cells + 1)² vertices, row-major.</summary>
    public double[] Get(ISurface surface, TileKey key, int cells)
    {
        long now = Interlocked.Increment(ref _clock);
        if (!_grids.TryGetValue((key.Packed, cells), out Grid? grid))
        {
            int n = cells + 1;
            var heights = new double[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    heights[j * n + i] = surface.HeightAt(TileMesh.Direction(key, cells, i, j));
                }
            }
            // Two threads may make the same grid at once: the same numbers, so either is kept.
            grid = _grids.GetOrAdd((key.Packed, cells), new Grid(heights));
            if (_grids.Count > _limit && Interlocked.Exchange(ref _trimming, 1) == 0)
            {
                Trim();
                Volatile.Write(ref _trimming, 0);
            }
        }
        grid.LastUsed = now;
        return grid.Heights;
    }

    private void Trim()
    {
        var all = new List<(long Used, (ulong, int) Key)>(_grids.Count);
        foreach (KeyValuePair<(ulong, int), Grid> pair in _grids)
        {
            all.Add((pair.Value.LastUsed, pair.Key));
        }
        all.Sort((a, b) => a.Used.CompareTo(b.Used));
        for (int i = 0; i < all.Count - _limit * 3 / 4; i++)
        {
            _grids.TryRemove(all[i].Key, out _);
        }
    }
}
