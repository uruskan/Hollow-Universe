using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace Hollow.Client.Map;

/// <summary>
/// The map's picture of a whole world: six square faces of <see cref="Size"/> × <see cref="Size"/> pixels, RGBA, one per
/// cube face, stacked in <see cref="CubeSphere"/>'s order (face f's rows start at row f·Size). A face's columns run along
/// its u from −1 and its rows along its v from +1 down, as <see cref="WorldGrid"/> names its cells.
/// </summary>
public sealed record MapPicture(int Size, byte[] Rgba);

/// <summary>
/// The map of a zone's whole world (docs/WHOLE_WORLD.md §11, GROUND.md §6.8): its grid (<see cref="WorldGrid"/>: faces A
/// to F, <see cref="Cells"/> × <see cref="Cells"/> cells a face of about world.json's <c>map.cell</c>), the ground under
/// a direction, and its picture, rendered from the surface by direction (<see cref="ZoneMapImage"/>), deterministic,
/// so the game caches it on disk under <see cref="CacheKey"/>. The projection that draws it is
/// <see cref="GlobeProjection"/>; nothing here assumes a kind of surface.
/// </summary>
public sealed class ZoneMap : IDisposable
{
    private readonly object _gate = new();
    private ISurface? _surface;
    private WorldLease? _lease;
    private readonly ZoneFrame _frame;

    public ZoneMap(ZoneSpec spec, ISurface surface, ZoneFrame frame, WorldData world)
    {
        _surface = surface;
        _frame = frame;
        Radius = surface.Radius;
        Cells = WorldGrid.CellsPerFace(Radius, world.MapCell);
        // Everything the picture depends on: the body, the world's radius, the surface as it is now (measured, so a
        // change to the generator makes a new picture without anyone raising a number), and the picture's look.
        ulong key = Hash64.Of(spec.SystemAddress, spec.BodyPath, spec.SurfaceSeed, (ulong)BitConverter.DoubleToInt64Bits(Radius));
        key = Hash64.Combine(Hash64.Combine(key, SurfaceVersion(surface)), ZoneMapImage.Version);
        CacheKey = $"globe-{key:X16}";
    }

    /// <summary>Takes this lease; every admitted map render obtains its own pin until all pixel workers finish.</summary>
    public ZoneMap(ZoneSpec spec, WorldLease lease, ZoneFrame frame, WorldData world)
    {
        try
        {
            _surface = lease.Surface;
            _frame = frame;
            Radius = _surface.Radius;
            Cells = WorldGrid.CellsPerFace(Radius, world.MapCell);
            CacheKey = $"globe-{lease.Descriptor.Identity}-v{ZoneMapImage.Version}";
            _lease = lease;
        }
        catch { lease.Dispose(); throw; }
    }

    /// <summary>The world's mean radius in metres: the map's sphere.</summary>
    public double Radius { get; }

    /// <summary>Cells along a face's side.</summary>
    public int Cells { get; }

    /// <summary>Names the picture (with its size) for a cache on disk: the same key is always the same picture.</summary>
    public string CacheKey { get; }

    /// <summary>The direction from the centre of a point of the zone (body frame, unit).</summary>
    public Vector3D DirectionOf(Vector3 zone)
    {
        Vector3D body = _frame.ToBody(zone);
        return body.LengthSquared > 1e-12 ? body.Normalized : Vector3D.UnitY;
    }

    /// <summary>The ground under a direction, as a point of the zone (where a marker put down on the map lies).</summary>
    public Vector3 Ground(Vector3D direction)
    {
        lock (_gate)
        {
            ISurface surface = _surface ?? throw new ObjectDisposedException(nameof(ZoneMap));
            Vector3D d = direction.Normalized;
            double height = surface.HeightAt(d);
            return _frame.ToLocal(d * (surface.Radius + (double.IsFinite(height) ? height : 0)));
        }
    }

    /// <summary>The grid cell a point of the zone lies in: "C K12".</summary>
    public string GridName(Vector3 zone) => WorldGrid.Name(DirectionOf(zone), Cells);

    /// <summary>The picture with faces of a size: a second or so on the first world, so the interface renders it on a worker and keeps it on disk.</summary>
    public MapPicture Render(int pixels, CancellationToken cancellation = default)
    {
        WorldLease? reader;
        ISurface surface;
        lock (_gate)
        {
            cancellation.ThrowIfCancellationRequested();
            surface = _surface ?? throw new ObjectDisposedException(nameof(ZoneMap));
            reader = _lease?.Fork();
        }
        using (reader) return ZoneMapImage.Render(surface, pixels, cancellation);
    }

    public void Dispose()
    {
        WorldLease? lease;
        lock (_gate)
        {
            lease = _lease;
            _lease = null;
            _surface = null;
        }
        lease?.Dispose();
    }

    /// <summary>
    /// The surface as it is, as a number: its kind, radius and sea, and its height and biome at 1,024 directions spread
    /// evenly over the world (a Fibonacci sphere, about 270 m apart on the first world). The same surface always gives
    /// the same number; a changed generator almost always another.
    /// </summary>
    public static ulong SurfaceVersion(ISurface surface)
    {
        const int samples = 1024;
        ulong h = Hash64.Of(surface.GetType().FullName ?? surface.GetType().Name);
        h = Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(surface.Radius));
        h = Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(surface.SeaRadius));
        if (surface is WalkingWorld walking)
            h = Hash64.Combine(h, unchecked((ulong)walking.Parameters.TerrainRevision));
        double golden = DMath.Pi * (3 - Math.Sqrt(5));
        for (int k = 0; k < samples; k++)
        {
            double y = 1 - 2 * (k + 0.5) / samples;
            double r = Math.Sqrt(Math.Max(0, 1 - y * y));
            var d = new Vector3D(r * DMath.Cos(golden * k), y, r * DMath.Sin(golden * k));
            h = Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(surface.HeightAt(d)));
            h = Hash64.Combine(h, (ulong)surface.BiomeAt(d));
        }
        return h;
    }
}

/// <summary>
/// Renders a world's map picture from its surface by direction (GROUND.md §6.8), in Rust's parchment manner: land tinted
/// by its biome (<see cref="ISurface.BiomeAt"/>), hill shading measured against local up with the light from the
/// north-west, faint contours every 10 m and stronger every 50 m, the sea (from <see cref="ISurface.SeaRadius"/>, none
/// when it is 0) in muted teal deepening offshore with a light band at the surf and an inked shoreline, and paper grain
/// and stains laid on the sphere so they run on across the faces' seams. The grid and its labels are the interface's
/// (crisp at any scale). Works for any surface. Deterministic: pure functions of the surface, <see cref="Hash64"/> noise
/// and DMath, each pixel on its own, so rows render in parallel.
/// </summary>
public static class ZoneMapImage
{
    /// <summary>Raised by hand when the look changes (it is part of the cache key).</summary>
    public const ulong Version = 2;

    private static readonly (double R, double G, double B)[] BiomeColours =
    {
        (0.30, 0.43, 0.47), // ocean (land below no sea: drawn as the shore's)
        (0.86, 0.80, 0.62), // beach
        (0.93, 0.92, 0.88), // ice sheet
        (0.71, 0.69, 0.57), // tundra
        (0.47, 0.52, 0.37), // boreal forest
        (0.52, 0.56, 0.38), // temperate forest
        (0.72, 0.70, 0.49), // grassland
        (0.71, 0.66, 0.47), // shrubland
        (0.85, 0.76, 0.55), // desert
        (0.78, 0.71, 0.48), // savanna
        (0.45, 0.52, 0.35), // tropical forest
        (0.67, 0.64, 0.56), // alpine
    };

    private static readonly (double R, double G, double B) Shallow = (0.52, 0.65, 0.63);
    private static readonly (double R, double G, double B) Deep = (0.31, 0.44, 0.48);
    private static readonly (double R, double G, double B) Surf = (0.62, 0.72, 0.68);
    private static readonly (double R, double G, double B) Ink = (0.30, 0.26, 0.20);

    /// <summary>How deep the sea is where its colour is darkest, in metres; how much the shading exaggerates slopes.</summary>
    private const double DeepAt = 30, Relief = 2.5;

    /// <summary>The six faces at <paramref name="size"/> pixels a side (16 … 2,048).</summary>
    public static MapPicture Render(ISurface surface, int size, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var parallel = new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
        size = Math.Clamp(size, 16, 2048);
        // The sea, read safely: a surface without one (SeaRadius 0, or nonsense) is all land, measured from its radius.
        double seaRadius = surface.SeaRadius;
        bool hasSea = double.IsFinite(seaRadius) && seaRadius > surface.Radius * 0.5;
        double seaLevel = hasSea ? seaRadius - surface.Radius : 0;
        double radius = surface.Radius;
        var rgba = new byte[CubeSphere.FaceCount * size * size * 4];
        int grid = size + 2;
        var heights = new double[grid * grid];
        var directions = new Vector3D[grid * grid];
        var colours = new (double R, double G, double B)[size * size];
        ulong grainSeed = Hash64.Of(0x6A9, 1), stainSeed = Hash64.Of(0x6A9, 2);
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            // Heights and directions with a ring of one pixel round the face (it reaches onto the neighbours), colours inside.
            Parallel.For(0, grid, parallel, row =>
            {
                double v = 1 - (row - 0.5) * 2 / size;
                for (int col = 0; col < grid; col++)
                {
                    if ((col & 31) == 0) cancellation.ThrowIfCancellationRequested();
                    double u = -1 + (col - 0.5) * 2 / size;
                    Vector3D d = CubeSphere.Direction(face, u, v);
                    double h = surface.HeightAt(d);
                    int c = row * grid + col;
                    directions[c] = d;
                    heights[c] = (double.IsFinite(h) ? h : 0) - seaLevel;
                    if (row >= 1 && row <= size && col >= 1 && col <= size)
                    {
                        int b = (int)surface.BiomeAt(d);
                        colours[(row - 1) * size + col - 1] = BiomeColours[b >= 0 && b < BiomeColours.Length ? b : (int)Biome.Grassland];
                    }
                }
            });
            int offset = face * size * size * 4;
            Parallel.For(0, size, parallel, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    int c = (y + 1) * grid + x + 1;
                    double h = heights[c], east = heights[c + 1], west = heights[c - 1], south = heights[c + grid], north = heights[c - grid];
                    Vector3D d = directions[c];
                    bool water = hasSea && h < 0;
                    bool shore = hasSea && (water ? east >= 0 || west >= 0 || north >= 0 || south >= 0 : east < 0 || west < 0 || north < 0 || south < 0);
                    (double r, double g, double b) colour;
                    if (water)
                    {
                        double depth = Smooth(0, DeepAt, -h);
                        colour = Mix(Shallow, Deep, depth);
                        if (shore)
                        {
                            colour = Mix(colour, Surf, 0.35);
                        }
                    }
                    else
                    {
                        colour = Blurred(colours, size, x, y);
                        double shade = Math.Clamp(0.72 + 0.32 * Lit(d, directions, heights, c, grid, radius), 0.62, 1.12);
                        colour = (colour.r * shade, colour.g * shade, colour.b * shade);
                        if (Crosses(h, east, 50) || Crosses(h, south, 50))
                        {
                            colour = Mix(colour, Ink, 0.28);
                        }
                        else if (Crosses(h, east, 10) || Crosses(h, south, 10))
                        {
                            colour = Mix(colour, Ink, 0.12);
                        }
                        if (shore)
                        {
                            colour = Mix(colour, Ink, 0.75);
                        }
                    }
                    // Paper: fine grain and wide stains, laid on the sphere, and a warm cast.
                    double grain = 0.965 + 0.05 * SphereSurface.ValueNoise(d * (radius / 2.6), grainSeed);
                    double stain = 1 - 0.07 * Smooth(0.55, 0.95, SphereSurface.ValueNoise(d * (radius / 260), stainSeed));
                    double k = grain * stain;
                    int o = offset + (y * size + x) * 4;
                    rgba[o] = Byte(colour.r * k);
                    rgba[o + 1] = Byte(colour.g * k * 0.975);
                    rgba[o + 2] = Byte(colour.b * k * 0.9);
                    rgba[o + 3] = 255;
                }
            });
        }
        return new MapPicture(size, rgba);
    }

    // How lit the ground is at a pixel, 1 on level ground: its slope from the heights of its four neighbours, measured
    // against local up (the neighbours' chords laid on the ground there), the light high from the north-west.
    private static double Lit(Vector3D up, Vector3D[] directions, double[] heights, int c, int grid, double radius)
    {
        Vector3D tu = (directions[c + 1] - directions[c - 1]) * radius, tv = (directions[c - grid] - directions[c + grid]) * radius;
        tu -= up * Vector3D.Dot(tu, up);
        tv -= up * Vector3D.Dot(tv, up);
        double hu = (heights[c + 1] - heights[c - 1]) * Relief, hv = (heights[c - grid] - heights[c + grid]) * Relief;
        // The slope g on the ground with g·tu = hu and g·tv = hv.
        double uu = Vector3D.Dot(tu, tu), uv = Vector3D.Dot(tu, tv), vv = Vector3D.Dot(tv, tv);
        double det = uu * vv - uv * uv;
        if (!(det > 1e-12))
        {
            return 1;
        }
        double alpha = (hu * vv - hv * uv) / det, beta = (hv * uu - hu * uv) / det;
        Vector3D normal = (up - (tu * alpha + tv * beta)).Normalized;
        if (GlobeProjection.Compass(up) is not { } compass)
        {
            return Vector3D.Dot(normal, up);
        }
        Vector3D light = compass.East * -0.55 + up * 0.75 + compass.North * 0.55;
        return Vector3D.Dot(normal, light) / 0.75;
    }

    // The land's colour softened over its neighbours (a 3 × 3 box), so biome borders don't stair-step.
    private static (double R, double G, double B) Blurred((double R, double G, double B)[] colours, int size, int x, int y)
    {
        (double r, double g, double b) = (0, 0, 0);
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int yy = y + dy;
            if (yy < 0 || yy >= size)
            {
                continue;
            }
            for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx;
                if (xx < 0 || xx >= size)
                {
                    continue;
                }
                (double R, double G, double B) c = colours[yy * size + xx];
                r += c.R;
                g += c.G;
                b += c.B;
                n++;
            }
        }
        return (r / n, g / n, b / n);
    }

    private static bool Crosses(double a, double b, double interval) => Math.Floor(a / interval) != Math.Floor(b / interval);

    private static (double, double, double) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static double Smooth(double edge0, double edge1, double x) => DMath.SmoothStep(edge0, edge1, x);

    private static byte Byte(double v) => double.IsFinite(v) ? (byte)Math.Clamp(Math.Round(v * 255), 0, 255) : (byte)0;
}
