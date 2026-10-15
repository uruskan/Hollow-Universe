using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace Hollow.Client.Map;

/// <summary>A map picture: <see cref="Size"/> × <see cref="Size"/> pixels, RGBA, rows from the north edge down.</summary>
public sealed record MapPicture(int Size, byte[] Rgba);

/// <summary>
/// The map of a zone (GROUND.md §6.8): a square of world.json's <c>map.metres</c> centred on the zone's origin, north
/// up; its grid of <c>map.cell</c> cells, lettered across from the west and numbered down from the north, as Rust's
/// (A0 is the north-west corner); conversions between zone metres and the map's 0–1 coordinates (u east, v south);
/// and its picture, rendered from the surface in Rust's parchment manner (<see cref="ZoneMapImage"/>), deterministic,
/// so the game caches it on disk under <see cref="CacheKey"/>.
/// </summary>
public sealed class ZoneMap
{
    private static readonly ConcurrentDictionary<string, MapPicture> Pictures = new();
    private readonly ISurface _surface;
    private readonly ZoneFrame _frame;

    public ZoneMap(ZoneSpec spec, ISurface surface, ZoneFrame frame, WorldData world)
    {
        _surface = surface;
        _frame = frame;
        Metres = world.MapMetres;
        Cell = world.MapCell;
        // Everything the picture depends on: the body's surface, where the zone sits, the map's size and its look.
        ulong key = Hash64.Of(spec.SystemAddress, spec.BodyPath, spec.SurfaceSeed, (ulong)BitConverter.DoubleToInt64Bits(spec.LatitudeDegrees));
        key = Hash64.Combine(key, Hash64.Of((ulong)BitConverter.DoubleToInt64Bits(spec.LongitudeDegrees), (ulong)BitConverter.DoubleToInt64Bits(Metres),
            ZoneMapImage.Version, surface switch
            {
                PlanetSurface => 1UL,
                WalkingWorld => 2UL | (ulong)WalkingWorld.Version << 8,
                _ => 0UL,
            }));
        CacheKey = $"map-{key:X16}";
    }

    /// <summary>The map's side in metres, and its grid's cell.</summary>
    public double Metres { get; }

    public double Cell { get; }

    /// <summary>Cells along a side.</summary>
    public int Cells => Math.Max(1, (int)Math.Round(Metres / Cell));

    /// <summary>Names the picture (with its size) for a cache on disk: the same key is always the same picture.</summary>
    public string CacheKey { get; }

    /// <summary>A zone point on the map (0–1 across from the west edge, 0–1 down from the north edge).</summary>
    public Vector2 ToMap(Vector3 zone) => new((float)((zone.X + Metres / 2) / Metres), (float)((zone.Z + Metres / 2) / Metres));

    /// <summary>A map point in the zone (on the ground).</summary>
    public Vector3 ToZone(Vector2 map)
    {
        double x = map.X * Metres - Metres / 2, z = map.Y * Metres - Metres / 2;
        Vector3D direction = _frame.ToBody(x, 0, z).Normalized;
        return _frame.ToLocal(direction * (_surface.Radius + _surface.HeightAt(direction)));
    }

    /// <summary>A grid cell's name, as Rust's: column letters (A … Z, then AA …) and row number, "K12".</summary>
    public string GridName(Vector2 map)
    {
        int column = Math.Clamp((int)Math.Floor(map.X * Cells), 0, Cells - 1);
        int row = Math.Clamp((int)Math.Floor(map.Y * Cells), 0, Cells - 1);
        return Column(column) + row.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A column's letters: A … Z, then AA, AB …</summary>
    public static string Column(int index) =>
        index < 26 ? ((char)('A' + Math.Max(0, index))).ToString() : Column(index / 26 - 1) + (char)('A' + index % 26);

    /// <summary>The picture at a size (rendered once per size and process; a second or so on the first world).</summary>
    public MapPicture Render(int pixels) =>
        Pictures.GetOrAdd($"{CacheKey}-{pixels}", _ => ZoneMapImage.Render(_surface, _frame, Metres, pixels));
}

/// <summary>
/// Renders a zone's map picture from its surface (GROUND.md §6.8), Rust's parchment look: land tinted by its biomes and
/// darker where forest stands, hill shading from the north-west, faint contours every 25 m and stronger every 100 m,
/// the sea in muted teal deepening offshore, an inked shoreline, and paper grain and stains with a darker edge. The
/// grid and its labels are the interface's (crisp at any scale). Deterministic: pure functions of the surface and
/// Hash64 noise (no platform maths), each pixel on its own, so rows render in parallel.
/// </summary>
public static class ZoneMapImage
{
    /// <summary>Raised by hand when the look changes (it is part of the cache key).</summary>
    public const ulong Version = 1;

    private static readonly (double R, double G, double B)[] BiomeColours =
    {
        (0.30, 0.43, 0.47), // ocean (not drawn as land)
        (0.86, 0.80, 0.62), // beach
        (0.93, 0.92, 0.88), // ice sheet
        (0.71, 0.69, 0.57), // tundra
        (0.58, 0.60, 0.44), // boreal forest
        (0.63, 0.64, 0.45), // temperate forest
        (0.72, 0.70, 0.49), // grassland
        (0.71, 0.66, 0.47), // shrubland
        (0.85, 0.76, 0.55), // desert
        (0.78, 0.71, 0.48), // savanna
        (0.55, 0.60, 0.40), // tropical forest
        (0.67, 0.64, 0.56), // alpine
    };

    private static readonly (double R, double G, double B) Forest = (0.42, 0.48, 0.33);
    private static readonly (double R, double G, double B) Shallow = (0.52, 0.65, 0.63);
    private static readonly (double R, double G, double B) Deep = (0.31, 0.44, 0.48);
    private static readonly (double R, double G, double B) Ink = (0.30, 0.26, 0.20);

    public static MapPicture Render(ISurface surface, ZoneFrame frame, double metres, int size)
    {
        size = Math.Clamp(size, 16, 4096);
        double step = metres / size, half = metres / 2;
        double seaLevel = surface.SeaRadius > 0 ? surface.SeaRadius - surface.Radius : double.NegativeInfinity;
        int grid = size + 2;
        var heights = new double[grid * grid];
        var colours = new (double R, double G, double B)[size * size];
        var planet = surface as PlanetSurface;
        Parallel.For(0, grid, row =>
        {
            Span<double> biomes = stackalloc double[BiomeTable.Count];
            double z = -half + (row - 0.5) * step;
            for (int col = 0; col < grid; col++)
            {
                double x = -half + (col - 0.5) * step;
                Vector3D p = frame.ToBody(x, 0, z).Normalized;
                bool inside = row >= 1 && row <= size && col >= 1 && col <= size;
                if (planet == null)
                {
                    heights[row * grid + col] = surface.HeightAt(p) - seaLevel;
                    if (inside)
                    {
                        colours[(row - 1) * size + col - 1] = BiomeColours[(int)surface.BiomeAt(p)];
                    }
                    continue;
                }
                SurfaceSample s = planet.Sample(p);
                heights[row * grid + col] = s.Height - seaLevel;
                if (!inside)
                {
                    continue;
                }
                ClimateSample climate = planet.Climate(p, s);
                planet.BiomeWeights(p, s, climate, biomes);
                (double r, double g, double b) = (0, 0, 0);
                double sum = 0;
                for (int i = 1; i < biomes.Length; i++)
                {
                    r += BiomeColours[i].R * biomes[i];
                    g += BiomeColours[i].G * biomes[i];
                    b += BiomeColours[i].B * biomes[i];
                    sum += biomes[i];
                }
                if (sum > 1e-9)
                {
                    (r, g, b) = (r / sum, g / sum, b / sum);
                }
                else
                {
                    (r, g, b) = BiomeColours[(int)Biome.Grassland];
                }
                double trees = Math.Clamp(planet.Vegetation(p, s, climate, biomes, 0).Trees / 0.02, 0, 1) * 0.85;
                colours[(row - 1) * size + col - 1] = (r + (Forest.R - r) * trees, g + (Forest.G - g) * trees, b + (Forest.B - b) * trees);
            }
        });
        var rgba = new byte[size * size * 4];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                int c = (y + 1) * grid + x + 1;
                double h = heights[c], east = heights[c + 1], west = heights[c - 1], south = heights[c + grid], north = heights[c - grid];
                (double r, double g, double b) colour;
                if (h < 0)
                {
                    double depth = Smooth(0, 45, -h);
                    colour = (Shallow.R + (Deep.R - Shallow.R) * depth, Shallow.G + (Deep.G - Shallow.G) * depth, Shallow.B + (Deep.B - Shallow.B) * depth);
                    if (east >= 0 || west >= 0 || north >= 0 || south >= 0)
                    {
                        colour = Mix(colour, (0.62, 0.72, 0.68), 0.35); // the surf's light band
                    }
                }
                else
                {
                    colour = colours[y * size + x];
                    // Hill shading: light from the north-west, high; heights exaggerated twice.
                    double nx = -(east - west) / (2 * step) * 2, nz = -(south - north) / (2 * step) * 2;
                    double length = Math.Sqrt(nx * nx + 1 + nz * nz);
                    double lit = (-0.55 * nx + 0.75 + -0.55 * nz) / length / 0.75;
                    double shade = Math.Clamp(0.72 + 0.32 * lit, 0.62, 1.12);
                    colour = (colour.r * shade, colour.g * shade, colour.b * shade);
                    // Contours: faint every 25 m, stronger every 100 m.
                    if (Crosses(h, east, 100) || Crosses(h, south, 100))
                    {
                        colour = Mix(colour, Ink, 0.30);
                    }
                    else if (Crosses(h, east, 25) || Crosses(h, south, 25))
                    {
                        colour = Mix(colour, Ink, 0.13);
                    }
                    if (east < 0 || west < 0 || north < 0 || south < 0)
                    {
                        colour = Mix(colour, Ink, 0.75); // the inked shoreline
                    }
                }
                // Paper: fine grain, wide stains, a warm cast and a darker edge.
                double grain = 0.965 + 0.05 * Noise(x * 0.9, y * 0.9, 1) + 0.025 * Noise(x * 0.23, y * 0.23, 2);
                double stain = 1 - 0.07 * Smooth(0.55, 0.95, Noise(x * 0.012, y * 0.012, 3));
                double u = (x + 0.5) / size - 0.5, v = (y + 0.5) / size - 0.5;
                double edge = 1 - 0.16 * Smooth(0.3, 0.72, Math.Sqrt(u * u + v * v) * 1.2);
                double k = grain * stain * edge;
                colour = (colour.r * k * 1.0, colour.g * k * 0.975, colour.b * k * 0.9);
                int o = (y * size + x) * 4;
                rgba[o] = Byte(colour.r);
                rgba[o + 1] = Byte(colour.g);
                rgba[o + 2] = Byte(colour.b);
                rgba[o + 3] = 255;
            }
        });
        return new MapPicture(size, rgba);
    }

    private static bool Crosses(double a, double b, double interval) => Math.Floor(a / interval) != Math.Floor(b / interval) && b >= 0;

    private static (double, double, double) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static double Smooth(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    // Value noise in [0, 1] on a unit lattice, from Hash64 (deterministic everywhere).
    private static double Noise(double x, double y, ulong seed)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        double tx = x - fx, ty = y - fy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        long ix = (long)fx, iy = (long)fy;
        double a = Lattice(ix, iy, seed), b = Lattice(ix + 1, iy, seed), c = Lattice(ix, iy + 1, seed), d = Lattice(ix + 1, iy + 1, seed);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    private static double Lattice(long x, long y, ulong seed) => (Hash64.Of((ulong)x, (ulong)y, seed) >> 11) * (1.0 / 9007199254740992.0);
}
