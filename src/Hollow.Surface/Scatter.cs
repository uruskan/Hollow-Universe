using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// One natural object placed by the scatter (GROUND.md §4.6). Its identity is its tile plus <see cref="Index"/>
/// (ARCHITECTURE.md §7): the index is fixed by the kind and the candidate cell, so it never shifts when anything
/// else changes.
/// </summary>
public readonly struct ScatterItem
{
    public ScatterItem(int index, ScatterKind kind, byte variant, Vector3D direction, float height, float yaw, float scale)
    {
        Index = index;
        Kind = kind;
        Variant = variant;
        Direction = direction;
        Height = height;
        Yaw = yaw;
        Scale = scale;
    }

    /// <summary>Kind in the top byte, candidate cell below: unique and stable within the tile.</summary>
    public int Index { get; }

    public ScatterKind Kind { get; }

    /// <summary>Which model of the kind, 0–255 (the client takes it modulo the models it has).</summary>
    public byte Variant { get; }

    /// <summary>Unit direction from the body's centre (body frame).</summary>
    public Vector3D Direction { get; }

    /// <summary>Ground height there, metres above the mean radius.</summary>
    public float Height { get; }

    /// <summary>Rotation about the local up, radians.</summary>
    public float Yaw { get; }

    /// <summary>Size relative to the model.</summary>
    public float Scale { get; }

    /// <summary>Body-frame position of the object's foot.</summary>
    public Vector3D Position(double radius) => Direction * (radius + Height);
}

/// <summary>
/// Places trees, bushes, ferns and rocks on scatter tiles: a jittered grid of candidates per kind, each kept with
/// the probability the climate, slope and clearings give at that spot. A pure function of the surface and the tile.
/// </summary>
public static class Scatter
{
    /// <summary>Quadtree level of scatter tiles (about 154 m across on the first world).</summary>
    public const int Level = 16;

    private const int DensityGrid = 8; // density samples per tile edge (plus one)

    // Candidate cells per tile edge for each kind (trees, bushes, ferns, rocks).
    private const int TreeCells = 44;
    private const int BushCells = 64;
    private const int FernCells = 96;
    private const int RockCells = 48;

    private readonly struct Densities
    {
        public readonly float Trees, Bushes, Ferns, Rocks, Area;
        public readonly float Fir, Pine, Birch, Broadleaf;

        public Densities(in VegetationSample v, double area, ReadOnlySpan<double> kinds)
        {
            Trees = (float)v.Trees;
            Bushes = (float)v.Bushes;
            Ferns = (float)v.Ferns;
            Rocks = (float)v.Rocks;
            Area = (float)area;
            Fir = (float)kinds[0];
            Pine = (float)kinds[1];
            Birch = (float)kinds[2];
            Broadleaf = (float)kinds[3];
        }
    }

    /// <summary>Everything the scatter places on a tile of <see cref="Level"/>, in a fixed order.</summary>
    public static List<ScatterItem> ForTile(PlanetSurface surface, TileKey tile)
    {
        var items = new List<ScatterItem>();
        if (tile.Level != Level)
        {
            return items;
        }
        Densities[] grid = DensityField(surface, tile);
        ulong tileSeed = Hash64.Of(surface.Seed, 0x53434154, tile.Packed); // "SCAT"
        Place(surface, tile, grid, tileSeed, ScatterKind.Fir, TreeCells, items);
        Place(surface, tile, grid, tileSeed, ScatterKind.Bush, BushCells, items);
        Place(surface, tile, grid, tileSeed, ScatterKind.Fern, FernCells, items);
        Place(surface, tile, grid, tileSeed, ScatterKind.Rock, RockCells, items);
        return items;
    }

    private static Densities[] DensityField(PlanetSurface surface, TileKey tile)
    {
        int n = DensityGrid + 1, m = n + 2;
        var directions = new Vector3D[m * m];
        var samples = new SurfaceSample[m * m];
        var points = new Vector3D[m * m];
        for (int j = 0; j < m; j++)
        {
            for (int i = 0; i < m; i++)
            {
                Vector3D d = TileMesh.Direction(tile, DensityGrid, i - 1, j - 1);
                SurfaceSample s = surface.Sample(d);
                int k = j * m + i;
                directions[k] = d;
                samples[k] = s;
                points[k] = d * (surface.Radius + s.Height);
            }
        }
        var result = new Densities[n * n];
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> kinds = stackalloc double[4];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int k = (j + 1) * m + (i + 1);
                Vector3D du = points[k + 1] - points[k - 1], dv = points[k + m] - points[k - m];
                Vector3D cross = Vector3D.Cross(du, dv);
                double length = cross.Length;
                double up = length > 0 ? Vector3D.Dot(cross, directions[k]) / length : 1;
                double slope = Math.Sqrt(Math.Max(0, 1 - up * up));
                // Ground area per grid cell: the cross product spans two cells each way.
                double area = length / 4;
                ClimateSample climate = surface.Climate(directions[k], samples[k]);
                surface.BiomeWeights(directions[k], samples[k], climate, biomes);
                VegetationSample v = surface.Vegetation(directions[k], samples[k], climate, biomes, slope);
                BiomeTable.TreeKinds(biomes, kinds);
                result[j * n + i] = new Densities(v, area, kinds);
            }
        }
        return result;
    }

    private static void Place(PlanetSurface surface, TileKey tile, Densities[] grid, ulong tileSeed, ScatterKind kind,
        int cells, List<ScatterItem> items)
    {
        int n = DensityGrid + 1;
        double span = tile.Span;
        double cellArea = 1.0 / ((double)cells * cells) * (DensityGrid * DensityGrid); // in density-grid cell areas
        for (int cj = 0; cj < cells; cj++)
        {
            for (int ci = 0; ci < cells; ci++)
            {
                int candidate = cj * cells + ci;
                var rng = new Rng(Hash64.Of(tileSeed, (ulong)kind, (ulong)candidate));
                double fu = (ci + rng.Range(0.12, 0.88)) / cells, fv = (cj + rng.Range(0.12, 0.88)) / cells;
                // Bilinear density at the candidate.
                double gx = fu * DensityGrid, gy = fv * DensityGrid;
                int x0 = Math.Min((int)gx, DensityGrid - 1), y0 = Math.Min((int)gy, DensityGrid - 1);
                double tx = gx - x0, ty = gy - y0;
                Densities a = grid[y0 * n + x0], b = grid[y0 * n + x0 + 1], c = grid[(y0 + 1) * n + x0], d = grid[(y0 + 1) * n + x0 + 1];
                double density = Bilinear(Density(a, kind), Density(b, kind), Density(c, kind), Density(d, kind), tx, ty);
                double area = Bilinear(a.Area, b.Area, c.Area, d.Area, tx, ty) * cellArea;
                if (!(rng.NextDouble() < density * area))
                {
                    continue;
                }
                double u = tile.U0 + fu * span, v = tile.V0 + fv * span;
                Vector3D direction = CubeSphere.Direction(tile.Face, u, v);
                ScatterKind placed = kind;
                float scale;
                if (kind == ScatterKind.Fir)
                {
                    placed = TreeKind(tile, tileSeed, direction, ref rng,
                        Bilinear(a.Fir, b.Fir, c.Fir, d.Fir, tx, ty), Bilinear(a.Pine, b.Pine, c.Pine, d.Pine, tx, ty),
                        Bilinear(a.Birch, b.Birch, c.Birch, d.Birch, tx, ty), Bilinear(a.Broadleaf, b.Broadleaf, c.Broadleaf, d.Broadleaf, tx, ty));
                    scale = (float)rng.Range(0.7, 1.3);
                }
                else if (kind == ScatterKind.Rock)
                {
                    // Many small stones, few boulders.
                    double r = rng.NextDouble();
                    scale = (float)(0.25 + 2.2 * r * r * r);
                }
                else
                {
                    scale = (float)rng.Range(0.6, 1.4);
                }
                byte variant = (byte)rng.NextInt(256);
                float yaw = (float)rng.Range(0, DMath.TwoPi);
                float height = (float)surface.HeightAt(direction);
                items.Add(new ScatterItem((int)placed << 24 | candidate, placed, variant, direction, height, yaw, scale));
            }
        }
    }

    // Trees grow in stands: a stand (about 40 m across) mostly holds one kind, picked from the local mix.
    private static ScatterKind TreeKind(TileKey tile, ulong tileSeed, Vector3D direction, ref Rng rng,
        double fir, double pine, double birch, double broadleaf)
    {
        Span<double> w = stackalloc double[] { fir, pine, birch, broadleaf };
        TileKey stand = TileKey.At(direction, Level + 2);
        var standRng = new Rng(Hash64.Of(tileSeed, 0x5354414E, stand.Packed)); // "STAN"
        int dominant = standRng.PickWeighted(w);
        int pick = rng.Chance(0.7) && dominant >= 0 ? dominant : rng.PickWeighted(w);
        return pick switch
        {
            1 => ScatterKind.Pine,
            2 => ScatterKind.Birch,
            3 => ScatterKind.Broadleaf,
            _ => ScatterKind.Fir,
        };
    }

    private static double Density(in Densities d, ScatterKind kind) => kind switch
    {
        ScatterKind.Fir => d.Trees,
        ScatterKind.Bush => d.Bushes,
        ScatterKind.Fern => d.Ferns,
        _ => d.Rocks,
    };

    private static double Bilinear(double a, double b, double c, double d, double tx, double ty) =>
        (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;
}
