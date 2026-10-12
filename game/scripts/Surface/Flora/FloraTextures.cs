using System;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// The painted textures of the surface view's plants (presentation only), after hollow-orbit's flora: a 4 × 4 atlas of
/// conifer branches, broadleaf and birch leaf clusters, bush and berry-bush leaves, grass and dry-grass blades, fern
/// fronds, flowers and twigs; and tiling bark (grooved, and birch) with normal maps. The atlas's mipmaps keep the
/// alpha-tested coverage of each cell, so far foliage doesn't thin out.
/// </summary>
public static class FloraTextures
{
    public const int AtlasSize = 2048;
    public const int Cell = 512;

    /// <summary>Atlas cells (column, row), row 0 at the top.</summary>
    public static readonly (int X, int Y) Pine = (0, 0), Spruce = (1, 0), Broad = (2, 0), BirchLeaf = (3, 0),
        Grass = (0, 1), Fern = (1, 1), Meadow = (2, 1), Bush = (3, 1),
        Twigs = (0, 2), Flower = (1, 2), Beech = (2, 2), DryGrass = (3, 2),
        OakDark = (0, 3), Berry = (1, 3), PineFar = (2, 3), Herb = (3, 3);

    /// <summary>UV rectangle of a cell: (u0, v_bottom, u1, v_top), v measured from the top row, with a pad in pixels.</summary>
    public static Vector4 CellUv((int X, int Y) cell, float pad = 4)
    {
        const float s = 0.25f;
        return new Vector4(cell.X * s + pad / AtlasSize, (cell.Y + 1) * s - pad / AtlasSize, (cell.X + 1) * s - pad / AtlasSize, cell.Y * s + pad / AtlasSize);
    }

    private static Color Rgba(double r, double g, double b, double a = 1) => new((float)(r / 255), (float)(g / 255), (float)(b / 255), (float)a);

    // ---------------------------------------------------------------- atlas

    /// <summary>Paints the atlas (about a second; run it on a worker thread) and returns it with its mipmaps.</summary>
    public static Image Atlas(int seed)
    {
        var atlas = new float[AtlasSize * AtlasSize * 4];
        var rng = new Rng(Hash64.Of((ulong)seed, 0x464C4F52)); // "FLOR"
        void At((int X, int Y) cell, Action<Painter, float> paint)
        {
            var p = new Painter(Cell * 2, Cell * 2);
            p.Scale(2);
            paint(p, Cell);
            float[] px = Painter.Downsample(p.Pixels, Cell * 2, Cell * 2);
            for (int y = 0; y < Cell; y++)
            {
                Array.Copy(px, y * Cell * 4, atlas, ((cell.Y * Cell + y) * AtlasSize + cell.X * Cell) * 4, Cell * 4);
            }
        }
        double R() => rng.NextDouble();

        void Leaf(Painter p, float x, float y, float len, float wid, float ang, Color col, Color? vein)
        {
            p.Save();
            p.Translate(x, y);
            p.Rotate(ang);
            p.BeginPath();
            p.MoveTo(0, 0);
            p.QuadraticCurveTo(len * 0.35f, -wid, len, 0);
            p.QuadraticCurveTo(len * 0.35f, wid, 0, 0);
            p.Fill(col);
            if (vein.HasValue)
            {
                p.Line(len * 0.05f, 0, len * 0.85f, 0, Math.Max(1, wid * 0.12f), vein.Value);
            }
            p.Restore();
        }

        // Conifer branch: twig along u from the trunk (left) to the tip (right), needles around.
        void Conifer(Painter p, float s, int[][] pal, double dens, double needleLen)
        {
            float cy = s / 2;
            void Twig(float x0, float y0, float x1, float y1, float w)
            {
                p.BeginPath();
                p.MoveTo(x0, y0);
                p.QuadraticCurveTo((x0 + x1) / 2, (y0 + y1) / 2 + 6, x1, y1);
                p.Stroke(Rgba(70, 50, 34), w);
                p.BeginPath();
            }
            Twig(0, cy, s * 0.97f, cy, 7);
            var subs = new System.Collections.Generic.List<(float, float, float, float)>();
            for (int i = 0; i < 9; i++)
            {
                float x = s * (0.1f + 0.09f * i);
                int side = i % 2 == 1 ? 1 : -1;
                float l = (float)(s * (0.36 - i * 0.026) * (0.8 + 0.4 * R()));
                float ang = (float)(side * (0.6 + 0.3 * R()));
                subs.Add((x, cy, x + MathF.Cos(ang) * l, cy + MathF.Sin(ang) * l));
                Twig(x, cy, x + MathF.Cos(ang) * l, cy + MathF.Sin(ang) * l, 3.5f);
            }
            subs.Add((0, cy, s * 0.97f, cy));
            foreach ((float x0, float y0, float x1, float y1) in subs)
            {
                float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                float dx = (x1 - x0) / len, dy = (y1 - y0) / len;
                int n = (int)(len * dens);
                for (int k = 0; k < n; k++)
                {
                    float t = (float)k / n, px = x0 + dx * len * t, py = y0 + dy * len * t;
                    float nl = (float)(needleLen * (0.7 + 0.5 * R()) * (1 - 0.35 * t));
                    float side = R() < 0.5 ? 1 : -1;
                    float a = MathF.Atan2(dy, dx) + side * (float)(0.5 + 0.6 * R());
                    int[] c = pal[(int)(R() * pal.Length)];
                    double sh = 0.75 + 0.5 * R() * (0.6 + 0.4 * t);
                    p.Line(px, py, px + MathF.Cos(a) * nl, py + MathF.Sin(a) * nl, (float)(2.2 + R() * 1.4), Rgba(c[0] * sh, c[1] * sh, c[2] * sh));
                }
            }
        }

        // Broadleaf cluster: twigs from the centre, leaves spread over a disc, darker toward the middle.
        void Cluster(Painter p, float s, int[][] pal, int n, double lmin, double lmax, double wr, Color? twig)
        {
            float cx = s / 2, cy = s / 2;
            for (int k = 0; k < 6; k++)
            {
                double a = R() * Math.PI * 2;
                p.Line(cx, cy, cx + (float)Math.Cos(a) * s * 0.3f, cy + (float)Math.Sin(a) * s * 0.3f, 5, twig ?? Rgba(80, 60, 40));
            }
            for (int k = 0; k < n; k++)
            {
                double rr = Math.Sqrt(R()) * s * 0.43, a = R() * Math.PI * 2;
                float x = cx + (float)(Math.Cos(a) * rr), y = cy + (float)(Math.Sin(a) * rr);
                float len = (float)(lmin + (lmax - lmin) * R());
                int[] c = pal[(int)(R() * pal.Length)];
                double edge = rr / (s * 0.43);
                double sh = (0.7 + 0.45 * R()) * (1.05 - 0.25 * (1 - edge));
                Leaf(p, x, y, len, (float)(len * wr), (float)(R() * Math.PI * 2), Rgba(c[0] * sh, c[1] * sh, c[2] * sh),
                    Rgba(c[0] * sh * 0.75, c[1] * sh * 0.8, c[2] * sh * 0.7));
            }
        }

        // Grass blades from the bottom of the cell up: dark at the base, light at the tips.
        void Blades(Painter p, float s, int n, int[][] pal, double hmin, double hmax, double wmax)
        {
            for (int k = 0; k < n; k++)
            {
                float x = (float)(s * (0.04 + 0.92 * R())), h = (float)(s * (hmin + (hmax - hmin) * R())), w = (float)(4 + R() * wmax);
                float lean = (float)((R() - 0.5) * s * 0.35);
                int[] c = pal[(int)(R() * pal.Length)];
                double sh = 0.75 + 0.45 * R();
                float top = s - h;
                p.BeginPath();
                p.MoveTo(x - w / 2, s);
                p.QuadraticCurveTo(x - w * 0.3f + lean * 0.4f, s - h * 0.55f, x + lean, top);
                p.QuadraticCurveTo(x + w * 0.3f + lean * 0.4f, s - h * 0.55f, x + w / 2, s);
                p.ClosePath();
                // The painter works in device pixels (twice the cell): map back to the blade's height.
                p.Fill((_, dy) =>
                {
                    float t = Math.Clamp((s - dy / 2) / h, 0, 1);
                    double k0 = t < 0.5 ? 0.45 + (0.85 - 0.45) * t / 0.5 : 0.85 + (1.1 - 0.85) * (t - 0.5) / 0.5;
                    return Rgba(c[0] * sh * k0, c[1] * sh * k0, c[2] * sh * Math.Min(k0, 1.05));
                });
                p.BeginPath();
            }
        }

        int[][] P(params int[][] colours) => colours;

        At(Pine, (p, s) => Conifer(p, s, P(new[] { 46, 70, 34 }, new[] { 58, 84, 40 }, new[] { 38, 58, 30 }, new[] { 70, 92, 46 }), 0.55, 40));
        At(Spruce, (p, s) => Conifer(p, s, P(new[] { 30, 56, 40 }, new[] { 40, 68, 46 }, new[] { 24, 46, 36 }, new[] { 50, 78, 52 }), 0.8, 26));
        At(PineFar, (p, s) => Conifer(p, s, P(new[] { 42, 66, 34 }, new[] { 54, 78, 40 }, new[] { 34, 54, 30 }), 1.0, 48));
        At(Broad, (p, s) => Cluster(p, s, P(new[] { 62, 92, 36 }, new[] { 74, 104, 42 }, new[] { 52, 80, 30 }, new[] { 86, 112, 48 }), 520, 26, 44, 0.42, null));
        At(OakDark, (p, s) => Cluster(p, s, P(new[] { 48, 74, 30 }, new[] { 58, 86, 34 }, new[] { 42, 64, 26 }), 560, 24, 40, 0.45, null));
        At(Beech, (p, s) => Cluster(p, s, P(new[] { 78, 104, 38 }, new[] { 92, 118, 44 }, new[] { 66, 92, 32 }, new[] { 104, 126, 52 }), 460, 30, 48, 0.5, null));
        At(BirchLeaf, (p, s) => Cluster(p, s, P(new[] { 112, 140, 50 }, new[] { 128, 152, 58 }, new[] { 96, 126, 44 }, new[] { 140, 160, 70 }), 420, 18, 30, 0.6, Rgba(90, 80, 70)));
        At(Bush, (p, s) => Cluster(p, s, P(new[] { 50, 82, 36 }, new[] { 62, 96, 42 }, new[] { 42, 70, 30 }), 700, 16, 28, 0.55, null));
        At(Berry, (p, s) => Cluster(p, s, P(new[] { 40, 74, 40 }, new[] { 50, 86, 46 }, new[] { 34, 62, 34 }), 620, 18, 30, 0.5, null));
        At(Grass, (p, s) => Blades(p, s, 150, P(new[] { 200, 220, 170 }, new[] { 186, 206, 150 }, new[] { 214, 226, 184 }), 0.45, 0.98, 12));
        At(DryGrass, (p, s) => Blades(p, s, 110, P(new[] { 226, 204, 150 }, new[] { 206, 186, 132 }, new[] { 236, 218, 170 }), 0.35, 0.95, 9));
        At(Meadow, (p, s) => Blades(p, s, 90, P(new[] { 196, 222, 160 }, new[] { 220, 214, 150 }, new[] { 180, 204, 140 }), 0.55, 0.99, 7));

        // Fern frond: a rachis from the bottom centre to the top, pinnae on both sides.
        At(Fern, (p, s) =>
        {
            float cx = s / 2;
            p.BeginPath();
            p.MoveTo(cx, s);
            p.QuadraticCurveTo(cx + 10, s * 0.5f, cx, s * 0.03f);
            p.Stroke(Rgba(70, 96, 40), 5);
            p.BeginPath();
            for (int k = 0; k < 34; k++)
            {
                float t = k / 34f, y = s * (0.97f - 0.92f * t);
                float len = s * 0.42f * MathF.Sin(MathF.PI * (0.12f + 0.85f * t)) * (1 - 0.3f * t);
                foreach (int side in new[] { -1, 1 })
                {
                    float a = side > 0 ? -0.35f : MathF.PI + 0.35f;
                    for (int j = 0; j < 9; j++)
                    {
                        float tt = j / 9f, px = cx + MathF.Cos(a) * len * tt, py = y + MathF.Sin(a) * len * tt;
                        double sh = 0.8 + 0.4 * R();
                        Leaf(p, px, py, len * 0.16f * (1 - tt * 0.5f), len * 0.05f, a + side * 1.2f, Rgba(78 * sh, 120 * sh, 44 * sh), null);
                        Leaf(p, px, py, len * 0.16f * (1 - tt * 0.5f), len * 0.05f, a - side * 1.2f, Rgba(70 * sh, 110 * sh, 40 * sh), null);
                    }
                }
            }
        });

        // Dry twigs: a few recursive branches with small leaves.
        At(Twigs, (p, s) =>
        {
            void Branch(float x, float y, float a, float len, float w, int d)
            {
                if (d > 5 || len < 6)
                {
                    return;
                }
                float x1 = x + MathF.Cos(a) * len, y1 = y + MathF.Sin(a) * len;
                p.Line(x, y, x1, y1, w, Rgba(120 + R() * 30, 100 + R() * 20, 80));
                int n = 2 + (R() < 0.4 ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    Branch(x1, y1, a + (float)((R() - 0.5) * 1.3), len * (float)(0.6 + 0.2 * R()), w * 0.7f, d + 1);
                }
                if (d >= 3 && R() < 0.6)
                {
                    Leaf(p, x1, y1, 10, 4, a, Rgba(120, 118, 70, 0.9), null);
                }
            }
            for (int k = 0; k < 7; k++)
            {
                Branch(s / 2 + (float)((R() - 0.5) * 40), s, -MathF.PI / 2 + (float)((R() - 0.5) * 1.2), s * 0.28f, 7, 0);
            }
        });

        // Flower: stem and leaves, white petals (tinted per instance), a yellow centre.
        At(Flower, (p, s) =>
        {
            p.BeginPath();
            p.MoveTo(s / 2, s);
            p.QuadraticCurveTo(s / 2 + 14, s * 0.6f, s / 2, s * 0.32f);
            p.Stroke(Rgba(70, 110, 50), 6);
            p.BeginPath();
            Leaf(p, s / 2, s * 0.75f, 70, 14, -0.6f, Rgba(70, 110, 50), null);
            Leaf(p, s / 2, s * 0.68f, 60, 12, MathF.PI + 0.7f, Rgba(70, 110, 50), null);
            float cx = s / 2, cy = s * 0.3f;
            for (int i = 0; i < 9; i++)
            {
                Leaf(p, cx, cy, 82, 30, i / 9f * MathF.Tau, Rgba(250, 250, 244), Rgba(220, 220, 210));
            }
            p.Circle(cx, cy, 18, _ => Rgba(240, 190, 40));
        });

        // Herbs: tall stems with paired leaves, for meadow plants.
        At(Herb, (p, s) =>
        {
            for (int k = 0; k < 7; k++)
            {
                float x = s * (0.15f + 0.7f * (float)R()), h = s * (0.55f + 0.4f * (float)R()), lean = (float)((R() - 0.5) * 60);
                p.BeginPath();
                p.MoveTo(x, s);
                p.QuadraticCurveTo(x + lean * 0.3f, s - h * 0.5f, x + lean, s - h);
                p.Stroke(Rgba(76, 112, 46), 4);
                p.BeginPath();
                for (int j = 0; j < 6; j++)
                {
                    float t = 0.2f + 0.13f * j, lx = x + lean * t * t, ly = s - h * t;
                    double sh = 0.8 + 0.4 * R();
                    Leaf(p, lx, ly, 46 * (1.1f - t), 12 * (1.1f - t), -0.5f - (float)R() * 0.4f, Rgba(70 * sh, 116 * sh, 44 * sh), null);
                    Leaf(p, lx, ly, 46 * (1.1f - t), 12 * (1.1f - t), MathF.PI + 0.5f + (float)R() * 0.4f, Rgba(66 * sh, 108 * sh, 40 * sh), null);
                }
            }
        });

        Dilate(atlas, AtlasSize, AtlasSize, 6);
        return WithCoverageMipmaps(atlas, AtlasSize, AtlasSize, 4, 4);
    }

    /// <summary>Spreads colour into fully transparent texels, so filtering at leaf edges blends with leaf colour, not black.</summary>
    public static void Dilate(float[] px, int w, int h, int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            var copy = (float[])px.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int k = (y * w + x) * 4;
                    if (copy[k + 3] > 0.01f)
                    {
                        continue;
                    }
                    float r = 0, g = 0, b = 0, n = 0;
                    for (int j = -1; j <= 1; j++)
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            int xx = x + i, yy = y + j;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h)
                            {
                                continue;
                            }
                            int q = (yy * w + xx) * 4;
                            float a = copy[q + 3] > 0.01f ? 1 : (copy[q] + copy[q + 1] + copy[q + 2] > 0 ? 0.5f : 0);
                            if (a <= 0)
                            {
                                continue;
                            }
                            r += copy[q] * a;
                            g += copy[q + 1] * a;
                            b += copy[q + 2] * a;
                            n += a;
                        }
                    }
                    if (n > 0)
                    {
                        px[k] = r / n;
                        px[k + 1] = g / n;
                        px[k + 2] = b / n;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Mipmaps whose alpha is rescaled per cell (a grid of <paramref name="cellsX"/> × <paramref name="cellsY"/>) so the
    /// share of texels passing the 0.5 alpha test stays the same at every level: far foliage keeps its density.
    /// </summary>
    public static Image WithCoverageMipmaps(float[] level0, int width, int height, int cellsX, int cellsY)
    {
        var levels = new System.Collections.Generic.List<float[]> { level0 };
        var sizes = new System.Collections.Generic.List<(int W, int H)> { (width, height) };
        float[] coverage = CellCoverage(level0, width, height, cellsX, cellsY, 1);
        while (sizes[^1].W > 1 || sizes[^1].H > 1)
        {
            (int sw, int sh) = sizes[^1];
            int w = Math.Max(1, sw / 2), h = Math.Max(1, sh / 2);
            float[] src = levels[^1];
            var dst = new float[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < 2; j++)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            int sx = Math.Min(sw - 1, 2 * x + i), sy = Math.Min(sh - 1, 2 * y + j);
                            int k = (sy * sw + sx) * 4;
                            r += src[k];
                            g += src[k + 1];
                            b += src[k + 2];
                            a += src[k + 3];
                        }
                    }
                    int o = (y * w + x) * 4;
                    dst[o] = r / 4;
                    dst[o + 1] = g / 4;
                    dst[o + 2] = b / 4;
                    dst[o + 3] = a / 4;
                }
            }
            if (w >= cellsX * 2 && h >= cellsY * 2)
            {
                // Scale each cell's alpha so its coverage at 0.5 matches the full-size texture.
                int cw = w / cellsX, ch = h / cellsY;
                for (int cy = 0; cy < cellsY; cy++)
                {
                    for (int cx = 0; cx < cellsX; cx++)
                    {
                        float want = coverage[cy * cellsX + cx];
                        float lo = 0.5f, hi = 8f;
                        for (int it = 0; it < 12; it++)
                        {
                            float mid = (lo + hi) / 2;
                            float got = Coverage(dst, w, cx * cw, cy * ch, cw, ch, mid);
                            if (got < want)
                            {
                                lo = mid;
                            }
                            else
                            {
                                hi = mid;
                            }
                        }
                        float scale = (lo + hi) / 2;
                        for (int y = cy * ch; y < (cy + 1) * ch; y++)
                        {
                            for (int x = cx * cw; x < (cx + 1) * cw; x++)
                            {
                                int k = (y * w + x) * 4 + 3;
                                dst[k] = Math.Min(1, dst[k] * scale);
                            }
                        }
                    }
                }
            }
            levels.Add(dst);
            sizes.Add((w, h));
        }
        int total = 0;
        foreach (float[] level in levels)
        {
            total += level.Length;
        }
        var bytes = new byte[total];
        int offset = 0;
        foreach (float[] level in levels)
        {
            for (int i = 0; i < level.Length; i++)
            {
                bytes[offset + i] = (byte)Math.Clamp((int)(level[i] * 255 + 0.5f), 0, 255);
            }
            offset += level.Length;
        }
        return Image.CreateFromData(width, height, true, Image.Format.Rgba8, bytes);
    }

    private static float[] CellCoverage(float[] px, int width, int height, int cellsX, int cellsY, float scale)
    {
        var result = new float[cellsX * cellsY];
        int cw = width / cellsX, ch = height / cellsY;
        for (int cy = 0; cy < cellsY; cy++)
        {
            for (int cx = 0; cx < cellsX; cx++)
            {
                result[cy * cellsX + cx] = Coverage(px, width, cx * cw, cy * ch, cw, ch, scale);
            }
        }
        return result;
    }

    private static float Coverage(float[] px, int width, int x0, int y0, int cw, int ch, float scale)
    {
        int n = 0;
        for (int y = y0; y < y0 + ch; y++)
        {
            for (int x = x0; x < x0 + cw; x++)
            {
                if (px[(y * width + x) * 4 + 3] * scale >= 0.5f)
                {
                    n++;
                }
            }
        }
        return (float)n / (cw * ch);
    }

    // ---------------------------------------------------------------- bark

    /// <summary>Tiling bark, 256²: grooved plates (tinted per species by vertex colour) or birch, with a normal map.</summary>
    public static (Image Albedo, Image Normal) Bark(bool birch, int seed)
    {
        const int S = 256;
        var height = new float[S * S];
        var albedo = new float[S * S * 4];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                double u = (double)x / S, v = (double)y / S;
                int k = (y * S + x) * 4;
                if (birch)
                {
                    double n = NoiseTextures.Fbm(u, v, 4, 3, 0.5, seed + 21) * 0.5 + 0.5;
                    double f = NoiseTextures.Fbm(u, v, 32, 2, 0.5, seed + 22) * 0.5 + 0.5;
                    double g = (214 + 30 * n - 18 * f) / 255;
                    albedo[k] = (float)g;
                    albedo[k + 1] = (float)(g * 0.98);
                    albedo[k + 2] = (float)(g * 0.94);
                    height[y * S + x] = (float)(0.6 + 0.2 * n);
                }
                else
                {
                    double warp = NoiseTextures.Fbm(u, v, 4, 2, 0.5, seed + 31) * 0.08;
                    double groove = Math.Abs(Math.Sin((u + warp) * Math.PI * 9 + NoiseTextures.Fbm(u, v, 3, 2, 0.5, seed + 33) * 2.0));
                    double plate = NoiseTextures.Fbm(u * 3 % 1, v, 6, 3, 0.5, seed + 35) * 0.5 + 0.5;
                    double fine = NoiseTextures.Fbm(u, v, 48, 2, 0.5, seed + 37) * 0.5 + 0.5;
                    double g = 0.35 + 0.55 * Math.Pow(groove, 0.6) * (0.75 + 0.25 * plate) + 0.12 * fine;
                    albedo[k] = (float)(190 * g / 255);
                    albedo[k + 1] = (float)(172 * g / 255);
                    albedo[k + 2] = (float)(156 * g / 255);
                    height[y * S + x] = (float)g;
                }
                albedo[k + 3] = 1;
            }
        }
        if (birch)
        {
            // Dark lenticels and a few bands, wrapped so the texture tiles.
            var p = new Painter(S, S);
            var rng = new Rng(Hash64.Of((ulong)seed, 77));
            for (int k = 0; k < 70; k++)
            {
                float x = (float)rng.NextDouble() * S, yy = (float)rng.NextDouble() * S, w = 8 + (float)rng.NextDouble() * 40, h = 1.5f + (float)rng.NextDouble() * 4;
                var c = new Color(30 / 255f, 26 / 255f, 24 / 255f, 0.55f + (float)rng.NextDouble() * 0.4f);
                foreach (int ox in new[] { -S, 0, S })
                {
                    foreach (int oy in new[] { -S, 0, S })
                    {
                        p.Ellipse(x + ox, yy + oy, w / 2, h / 2, c);
                    }
                }
            }
            for (int k = 0; k < 6; k++)
            {
                float yy = (float)rng.NextDouble() * S, x = (float)rng.NextDouble() * S, w = 40 + (float)rng.NextDouble() * 70, h = 8 + (float)rng.NextDouble() * 14;
                foreach (int ox in new[] { -S, 0, S })
                {
                    p.Ellipse(x + ox, yy, w / 2, h / 2, new Color(36 / 255f, 30 / 255f, 28 / 255f, 0.75f));
                }
            }
            float[] marks = p.Pixels;
            for (int i = 0; i < S * S; i++)
            {
                float a = marks[i * 4 + 3];
                for (int c = 0; c < 3; c++)
                {
                    albedo[i * 4 + c] = albedo[i * 4 + c] * (1 - a) + marks[i * 4 + c] * a;
                }
                height[i] -= a * 0.4f;
            }
        }
        var normal = new byte[S * S * 4];
        var colour = new byte[S * S * 4];
        const float Strength = 3.0f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float hx = height[y * S + (x + 1) % S] - height[y * S + (x + S - 1) % S];
                float hy = height[((y + 1) % S) * S + x] - height[((y + S - 1) % S) * S + x];
                var n = new Vector3(-hx * Strength, hy * Strength, 1).Normalized();
                int k = (y * S + x) * 4;
                normal[k] = (byte)(n.X * 127.5f + 127.5f);
                normal[k + 1] = (byte)(n.Y * 127.5f + 127.5f);
                normal[k + 2] = (byte)(n.Z * 127.5f + 127.5f);
                normal[k + 3] = 255;
                for (int c = 0; c < 3; c++)
                {
                    colour[k + c] = (byte)Math.Clamp((int)(albedo[k + c] * 255 + 0.5f), 0, 255);
                }
                colour[k + 3] = 255;
            }
        }
        Image albedoImage = Image.CreateFromData(S, S, false, Image.Format.Rgba8, colour);
        Image normalImage = Image.CreateFromData(S, S, false, Image.Format.Rgba8, normal);
        albedoImage.GenerateMipmaps();
        normalImage.GenerateMipmaps();
        return (albedoImage, normalImage);
    }
}
