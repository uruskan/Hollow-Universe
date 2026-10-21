using System;
using Godot;

namespace HollowUniverse.Surface;

/// <summary>
/// Tiling noise textures for the surface's shaders (presentation only): the cloud field, and the large-scale
/// variation that keeps ground textures from repeating visibly. Generated once at start-up.
/// </summary>
public static class NoiseTextures
{
    /// <summary>
    /// A forest seen from far away, 512² RGBA, tiling: crowns on a jittered grid of 24 × 24 per tile (about six metres
    /// apart when the tile covers 150 m). r = crown height (0 in the gaps), g = a random number per crown (its kind and
    /// shade), b and a = the crown surface's slope (x and y, 0.5 = flat) for lighting.
    /// </summary>
    public static ImageTexture Canopy(int seed)
    {
        const int Size = 512, Cells = 24;
        var height = new float[Size * Size];
        var kind = new float[Size * Size];
        var rng = new Hollow.Core.Rng((ulong)seed * 0x9E3779B97F4A7C15UL + 7);
        var crowns = new (float X, float Y, float R, float H, float K)[Cells * Cells];
        for (int i = 0; i < crowns.Length; i++)
        {
            int cx = i % Cells, cy = i / Cells;
            crowns[i] = ((cx + 0.15f + 0.7f * (float)rng.NextDouble()) / Cells, (cy + 0.15f + 0.7f * (float)rng.NextDouble()) / Cells,
                (0.42f + 0.38f * (float)rng.NextDouble()) / Cells, 0.65f + 0.35f * (float)rng.NextDouble(), (float)rng.NextDouble());
        }
        System.Threading.Tasks.Parallel.For(0, Size, y =>
        {
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                int gx = (int)(u * Cells), gy = (int)(v * Cells);
                float best = 0, bestKind = 0;
                for (int j = -2; j <= 2; j++)
                {
                    for (int i = -2; i <= 2; i++)
                    {
                        int cx = ((gx + i) % Cells + Cells) % Cells, cy = ((gy + j) % Cells + Cells) % Cells;
                        var c = crowns[cy * Cells + cx];
                        // The crown's centre in this copy of the tile (wrapping).
                        float dx = u - (c.X + (gx + i - cx) / (float)Cells);
                        float dy = v - (c.Y + (gy + j - cy) / (float)Cells);
                        float d = MathF.Sqrt(dx * dx + dy * dy) / c.R;
                        if (d >= 1)
                        {
                            continue;
                        }
                        float h = c.H * MathF.Sqrt(1 - d * d);
                        if (h > best)
                        {
                            best = h;
                            bestKind = c.K;
                        }
                    }
                }
                height[y * Size + x] = best;
                kind[y * Size + x] = bestKind;
            }
        });
        var data = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float hx = height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size];
                float hy = height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x];
                int k = (y * Size + x) * 4;
                data[k] = ToByte(height[y * Size + x]);
                data[k + 1] = ToByte(kind[y * Size + x]);
                data[k + 2] = ToByte(0.5 - hx * 4);
                data[k + 3] = ToByte(0.5 - hy * 4);
            }
        }
        Image image = Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// 512² RGBA: r = soft cover (few large blobs), g = billows (rounded cells), b = fine detail, a = wisps. Every
    /// channel tiles.
    /// </summary>
    public static ImageTexture Clouds(int seed)
    {
        const int Size = 512;
        var data = new byte[Size * Size * 4];
        System.Threading.Tasks.Parallel.For(0, Size, y =>
        {
            for (int x = 0; x < Size; x++)
            {
                double u = (double)x / Size, v = (double)y / Size;
                double cover = Fbm(u, v, 3, 5, 0.55, seed);
                double billow = 1 - Worley(u, v, 10, seed + 7);
                billow = 0.65 * billow + 0.35 * (Fbm(u, v, 12, 3, 0.5, seed + 3) * 0.5 + 0.5);
                double detail = Fbm(u, v, 24, 4, 0.55, seed + 11) * 0.5 + 0.5;
                double wisps = Fbm(u, v, 6, 5, 0.6, seed + 13) * 0.5 + 0.5;
                int k = (y * Size + x) * 4;
                data[k] = ToByte(cover * 0.5 + 0.5);
                data[k + 1] = ToByte(billow);
                data[k + 2] = ToByte(detail);
                data[k + 3] = ToByte(wisps);
            }
        });
        Image image = Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// 512² tiling normal map of small wind waves: a height field of fractal noise stretched across the wind,
    /// turned into normals (tangent space, OpenGL convention), with a separate wave-height foam mask in alpha.
    /// </summary>
    public static ImageTexture WaveNormals(int seed)
    {
        const int Size = 512;
        var height = new double[Size * Size];
        System.Threading.Tasks.Parallel.For(0, Size, y =>
        {
            for (int x = 0; x < Size; x++)
            {
                double u = (double)x / Size, v = (double)y / Size;
                // Crests: sharpened noise, longer across the wind than along it.
                // V covers half a lattice period: wrap its gradients at that half-period too.
                double a = Fbm(u, v * 0.5 + 0.13 * Fbm(u, v, 4, 2, 0.5, seed + 5), 8, 4, 0.55, seed, verticalPeriod: 4);
                double b = Fbm(u + 0.31, v, 16, 3, 0.5, seed + 9);
                height[y * Size + x] = 1 - Math.Abs(a) * 1.2 + 0.35 * b;
            }
        });
        var data = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                double hx = height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size];
                double hy = height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x];
                var n = new Vector3((float)(-hx * 6), (float)(-hy * 6), 1).Normalized();
                int k = (y * Size + x) * 4;
                data[k] = ToByte(n.X * 0.5 + 0.5);
                data[k + 1] = ToByte(n.Y * 0.5 + 0.5);
                data[k + 2] = ToByte(n.Z * 0.5 + 0.5);
                // Normal Z stays near one and is not a usable noise field. Foam reads this independent mask.
                data[k + 3] = ToByte((height[y * Size + x] - 0.3) / 0.9);
            }
        }
        Image image = Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static byte ToByte(double x) => (byte)Math.Clamp((int)(x * 255 + 0.5), 0, 255);

    // Tiling gradient noise with independent horizontal and vertical lattice periods.
    private static double Perlin(double x, double y, int period, int verticalPeriod, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double ux = fx * fx * fx * (fx * (fx * 6 - 15) + 10), uy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
        double Grad(int ix, int iy, double dx, double dy)
        {
            int px = ((ix % period) + period) % period, py = ((iy % verticalPeriod) + verticalPeriod) % verticalPeriod;
            uint h = (uint)(px * 374761393 + py * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            double a = (h & 0xFFFF) / 65536.0 * Math.PI * 2;
            return Math.Cos(a) * dx + Math.Sin(a) * dy;
        }
        double n00 = Grad(x0, y0, fx, fy), n10 = Grad(x0 + 1, y0, fx - 1, fy);
        double n01 = Grad(x0, y0 + 1, fx, fy - 1), n11 = Grad(x0 + 1, y0 + 1, fx - 1, fy - 1);
        double a0 = n00 + ux * (n10 - n00), a1 = n01 + ux * (n11 - n01);
        return (a0 + uy * (a1 - a0)) * 1.4;
    }

    /// <summary>Tiling fractal noise over the unit square, roughly −1 to 1.</summary>
    public static double Fbm(double u, double v, int period, int octaves, double gain, int seed)
        => Fbm(u, v, period, octaves, gain, seed, period);

    // The public/default path keeps square wrapping. Only the stretched wave field opts into a rectangle.
    private static double Fbm(double u, double v, int period, int octaves, double gain, int seed, int verticalPeriod)
    {
        double sum = 0, amplitude = 1, norm = 0;
        int p = period, py = verticalPeriod;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Perlin(u * p, v * p, p, py, seed + i * 31);
            norm += amplitude;
            amplitude *= gain;
            p *= 2;
            py *= 2;
        }
        return sum / norm;
    }

    // Tiling distance to the nearest of one random point per cell, 0–1.
    private static double Worley(double u, double v, int period, int seed)
    {
        double x = u * period, y = v * period;
        int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
        double best = 9;
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                int ix = cx + i, iy = cy + j;
                int px = ((ix % period) + period) % period, py = ((iy % period) + period) % period;
                uint h = (uint)(px * 73856093 ^ py * 19349663 ^ seed * 83492791);
                h = (h ^ (h >> 13)) * 1274126177;
                double ox = (h & 0xFFFF) / 65536.0, oy = ((h >> 16) & 0xFFFF) / 65536.0;
                double dx = ix + ox - x, dy = iy + oy - y;
                best = Math.Min(best, dx * dx + dy * dy);
            }
        }
        return Math.Clamp(Math.Sqrt(best), 0, 1);
    }
}
