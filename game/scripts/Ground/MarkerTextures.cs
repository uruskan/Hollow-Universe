using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Ground;

/// <summary>
/// The weak spots' textures (GROUND.md §9.3), painted in code once and kept (our own art): gather.json's
/// <c>look.marker.style</c> names one. "painted-x": a spray-painted X with soft overspray, speckles and a few drips;
/// "hotspot": a hot glow with a white core and thin rays; the glint drawn over a hotspot is a four-pointed star.
/// Each style is an albedo (grey paint, tinted by the marker's colour, alpha its coverage) and an emission
/// (premultiplied: black where nothing glows).
/// </summary>
public static class MarkerTextures
{
    public sealed record Style(Texture2D Albedo, Texture2D Emission);

    private static readonly Dictionary<string, Func<(Image Albedo, Image Emission)>> Painters = new(StringComparer.Ordinal)
    {
        ["painted-x"] = PaintedX,
        ["hotspot"] = Hotspot,
    };

    private static readonly Dictionary<string, Style> Made = new(StringComparer.Ordinal);
    private static Texture2D? _glint;

    /// <summary>The textures of a style (painted the first time), or null for a style there is no painter for.</summary>
    public static Style? For(string style)
    {
        if (Made.TryGetValue(style, out Style? made))
        {
            return made;
        }
        if (!Painters.TryGetValue(style, out var paint))
        {
            GD.PushWarning($"[markers] no painter for the style \"{style}\"");
            return null;
        }
        (Image albedo, Image emission) = paint();
        made = new Style(Texture(albedo), Texture(emission));
        Made[style] = made;
        return made;
    }

    /// <summary>A four-pointed star with a bright core, white, for additive glints.</summary>
    public static Texture2D Glint()
    {
        if (_glint != null)
        {
            return _glint;
        }
        const int N = 64;
        var px = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                float r = MathF.Sqrt(u * u + v * v);
                float core = MathF.Exp(-r * r / 0.012f);
                float cross = MathF.Exp(-v * v / 0.0009f) * MathF.Max(0, 1 - MathF.Abs(u)) + MathF.Exp(-u * u / 0.0009f) * MathF.Max(0, 1 - MathF.Abs(v));
                float d1 = (u - v) * 0.7071f, d2 = (u + v) * 0.7071f;
                float diagonal = 0.45f * (MathF.Exp(-d1 * d1 / 0.0006f) + MathF.Exp(-d2 * d2 / 0.0006f)) * MathF.Max(0, 1 - r * 1.6f);
                float k = Math.Clamp(core + cross * 0.9f + diagonal, 0, 1) * MathF.Max(0, 1 - r);
                Put(px, N, x, y, k, k, k, k);
            }
        }
        _glint = Texture(Image.CreateFromData(N, N, false, Image.Format.Rgba8, px));
        return _glint;
    }

    // ---------------------------------------------------------------- painters

    // Spray paint: two strokes crossing, a little bowed and uneven, soft at the edge with speckles of overspray, and a
    // few drips running down (+v is down on a trunk: the marker lays its texture so).
    private static (Image, Image) PaintedX()
    {
        const int N = 256;
        var albedo = new byte[N * N * 4];
        var glow = new byte[N * N * 4];
        Vector2[][] strokes =
        {
            Bowed(new Vector2(0.22f, 0.19f), new Vector2(0.79f, 0.81f), 0.025f),
            Bowed(new Vector2(0.78f, 0.18f), new Vector2(0.23f, 0.8f), -0.02f),
        };
        (Vector2 From, float Length)[] drips = { (new Vector2(0.78f, 0.84f), 0.1f), (new Vector2(0.25f, 0.83f), 0.07f), (new Vector2(0.53f, 0.56f), 0.09f) };
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2((x + 0.5f) / N, (y + 0.5f) / N);
                float alpha = 0;
                foreach (Vector2[] stroke in strokes)
                {
                    (float d, float t) = Distance(stroke, p);
                    float half = 0.078f * (0.84f + 0.16f * MathF.Sin(MathF.PI * t)) * (0.9f + 0.2f * Noise(t * 7, 3.1f));
                    float edge = d + 0.009f * (Noise(p.X * 38, p.Y * 38) - 0.5f) + 0.004f * (Noise(p.X * 90, p.Y * 90) - 0.5f);
                    float a = Smooth(half + 0.007f, half - 0.007f, edge);
                    // Overspray: speckles thinning out past the edge.
                    float beyond = (edge - half) / 0.055f;
                    if (beyond is > 0 and < 1 && Hash(x, y) < 0.22f * (1 - beyond) * (1 - beyond))
                    {
                        a = MathF.Max(a, 0.35f + 0.45f * Hash(y, x));
                    }
                    alpha = MathF.Max(alpha, a);
                }
                foreach ((Vector2 from, float length) in drips)
                {
                    float along = p.Y - from.Y, across = MathF.Abs(p.X - from.X - 0.004f * MathF.Sin(along * 60));
                    if (along > 0 && along < length)
                    {
                        float width = 0.01f * (1 - 0.4f * along / length);
                        alpha = MathF.Max(alpha, Smooth(width + 0.003f, width - 0.002f, across) * (1 - 0.3f * along / length));
                    }
                    float blob = p.DistanceTo(new Vector2(from.X, from.Y + length));
                    alpha = MathF.Max(alpha, Smooth(0.017f, 0.011f, blob) * 0.85f);
                }
                // Uneven paint: thicker and thinner patches.
                float shade = 0.84f + 0.16f * Noise(p.X * 11, p.Y * 11);
                Put(albedo, N, x, y, shade, shade, shade, alpha);
                Put(glow, N, x, y, shade * alpha, shade * alpha, shade * alpha, alpha);
            }
        }
        return (Mipmapped(albedo, N), Mipmapped(glow, N));
    }

    // A glowing patch: a white-hot core, a warm halo and four thin rays; it lightens the rock under it a little.
    private static (Image, Image) Hotspot()
    {
        const int N = 128;
        var albedo = new byte[N * N * 4];
        var glow = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
                float r = MathF.Sqrt(u * u + v * v);
                float core = MathF.Exp(-r * r / 0.0035f);
                float halo = MathF.Exp(-r * r / 0.018f) * 0.55f;
                float rays = 0;
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.PI / 4 + 0.3f;
                    float perp = MathF.Abs(-MathF.Sin(a) * u + MathF.Cos(a) * v);
                    rays += MathF.Exp(-perp * perp / 0.00012f) * MathF.Max(0, 1 - r / 0.46f) * (i % 2 == 0 ? 0.5f : 0.3f);
                }
                float k = Math.Clamp(core + halo + rays, 0, 1) * Smooth(0.5f, 0.42f, r);
                float lighten = Math.Clamp(halo * 1.3f + core, 0, 1) * 0.55f * Smooth(0.5f, 0.4f, r);
                Put(albedo, N, x, y, 1, 1, 1, lighten);
                Put(glow, N, x, y, k, k, k, k);
            }
        }
        return (Mipmapped(albedo, N), Mipmapped(glow, N));
    }

    // ---------------------------------------------------------------- helpers

    private static Vector2[] Bowed(Vector2 a, Vector2 b, float bow)
    {
        Vector2 mid = (a + b) / 2, side = new Vector2(-(b - a).Y, (b - a).X).Normalized() * bow;
        var points = new Vector2[13];
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / (points.Length - 1f);
            Vector2 c = mid + side;
            points[i] = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
        }
        return points;
    }

    // The distance from a point to a polyline, and how far along it (0 … 1) the nearest point is.
    private static (float Distance, float Along) Distance(Vector2[] line, Vector2 p)
    {
        float best = float.MaxValue, along = 0;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 a = line[i], ab = line[i + 1] - a;
            float t = Math.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
            float d = p.DistanceTo(a + ab * t);
            if (d < best)
            {
                best = d;
                along = (i + t) / (line.Length - 1);
            }
        }
        return (best, along);
    }

    private static float Smooth(float edge0, float edge1, float x) => Mathf.SmoothStep(edge0, edge1, x);

    // Value noise in [0, 1].
    private static float Noise(float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    private static float Hash(int x, int y) => (Hash64.Of((ulong)(uint)x, (ulong)(uint)y, 0x4D41524BUL) & 0xFFFFFF) / 16777216f;

    private static void Put(byte[] px, int n, int x, int y, float r, float g, float b, float a)
    {
        int i = (y * n + x) * 4;
        px[i] = (byte)Math.Clamp(r * 255 + 0.5f, 0, 255);
        px[i + 1] = (byte)Math.Clamp(g * 255 + 0.5f, 0, 255);
        px[i + 2] = (byte)Math.Clamp(b * 255 + 0.5f, 0, 255);
        px[i + 3] = (byte)Math.Clamp(a * 255 + 0.5f, 0, 255);
    }

    private static Image Mipmapped(byte[] px, int n)
    {
        Image image = Image.CreateFromData(n, n, false, Image.Format.Rgba8, px);
        image.GenerateMipmaps();
        return image;
    }

    private static Texture2D Texture(Image image) => ImageTexture.CreateFromImage(image);
}
