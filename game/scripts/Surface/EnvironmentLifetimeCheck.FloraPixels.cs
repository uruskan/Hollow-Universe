using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Bounded synthetic pixels, no library/assets/viewport. Reference arithmetic below is intentionally independent
    // of the production scanline, band and reusable-mip implementation, and retains the previous full-array order.
    private void CheckFloraPixels()
    {
        foreach ((int w, int h, int cx, int cy) in new[]
        {
            (1, 1, 1, 1), (1, 129, 1, 3), (131, 1, 1, 1), (9, 7, 3, 1),
            (16, 132, 4, 3), (128, 132, 8, 11), (257, 130, 7, 5), (128, 256, 8, 4),
        })
        {
            for (int pattern = 0; pattern < 3; pattern++)
            {
                byte[] rgba = FloraPixelFixture(w, h, pattern);
                byte[] unchanged = (byte[])rgba.Clone();
                float[] source = LegacyFloraFloats(rgba);
                foreach (int passes in new[] { 0, 1, 4, 6 })
                {
                    float[] expected = (float[])source.Clone(), actual = (float[])source.Clone();
                    LegacyFloraDilate(expected, w, h, passes);
                    FloraTextures.Dilate(actual, w, h, passes);
                    int different = -1; // compare float bits, including signed zero
                    for (int i = 0; i < actual.Length; i++)
                        if (BitConverter.SingleToInt32Bits(actual[i]) != BitConverter.SingleToInt32Bits(expected[i]))
                        { different = i; break; }
                    Expect($"flora scanline dilation parity {w}x{h}/{pattern}/{passes}", different < 0,
                        $"float-bit comparison; first differing component={different}");
                }

                LegacyFloraDilate(source, w, h, 4);
                byte[] colour = LegacyFloraMipBytes(source, w, h, cx, cy);
                byte[] actualColour = FloraTextures.ImpostorPixels(rgba, w, h, cx, cy, true);
                Expect($"flora band colour/mip parity {w}x{h}/{pattern}", colour.SequenceEqual(actualColour),
                    $"all {colour.Length} bytes; includes alpha 0/1/2/3/127/128/254/255 and cell/band/image boundaries");
                byte[] normal = LegacyFloraPack(source);
                byte[] actualNormal = FloraTextures.ImpostorPixels(rgba, w, h, cx, cy, false);
                Expect($"flora band normal parity {w}x{h}/{pattern}", normal.SequenceEqual(actualNormal) && rgba.SequenceEqual(unchanged),
                    "all base bytes match; original readback remains immutable");
                using Image painted = FloraTextures.WithCoverageMipmaps(source, w, h, cx, cy);
                Expect($"flora reusable painted mip parity {w}x{h}/{pattern}", colour.SequenceEqual(painted.GetData()),
                    "same native RGBA8 mip image from borrowed unquantized base");
            }
        }

        const int nativeW = 32, nativeH = 132, nativeRows = 3;
        byte[] nativeSource = FloraPixelFixture(nativeW, nativeH, 2);
        for (int pass = 0; pass < 2; pass++)
        {
            float[] floats = LegacyFloraFloats(nativeSource);
            LegacyFloraDilate(floats, nativeW, nativeH, 4);
            byte[] reference = pass == 0 ? LegacyFloraMipBytes(floats, nativeW, nativeH, 8, nativeRows) : LegacyFloraPack(floats);
            using Image expected = Image.CreateFromData(nativeW, nativeH, pass == 0, Image.Format.Rgba8, reference);
            if (pass == 1) expected.GenerateMipmaps();
            Image input = Image.CreateFromData(nativeW, nativeH, false, Image.Format.Rgba8, nativeSource);
            using Image actual = ImpostorBaker.FinishLifecycleFixture(input, pass, nativeRows);
            Expect($"flora native finish pass {pass} parity and input release", !GodotObject.IsInstanceValid(input)
                && expected.GetData().SequenceEqual(actual.GetData()), "production Finish owns readback and returns identical complete mip bytes");
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Image cancelled = Image.CreateFromData(nativeW, nativeH, false, Image.Format.Rgba8, nativeSource);
        bool observed = false;
        try { using Image unexpected = ImpostorBaker.FinishLifecycleFixture(cancelled, 0, nativeRows, cancellation.Token); }
        catch (OperationCanceledException) { observed = true; }
        Expect("flora cancelled finish releases isolated readback", observed && !GodotObject.IsInstanceValid(cancelled),
            "worker entry still executes its owning using block when already cancelled");
    }

    private static byte[] FloraPixelFixture(int w, int h, int pattern)
    {
        var bytes = new byte[w * h * 4];
        byte[] alpha = { 0, 1, 2, 3, 127, 128, 254, 255 };
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                uint hash = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(pattern * 83492791));
                hash ^= hash >> 13; hash *= 1274126177u; hash ^= hash >> 16;
                int k = (y * w + x) * 4;
                bytes[k] = (byte)hash; bytes[k + 1] = (byte)(hash >> 8); bytes[k + 2] = (byte)(hash >> 16);
                bytes[k + 3] = pattern == 0 ? alpha[(x + 3 * y) % alpha.Length]
                    : pattern == 1 ? (byte)(hash % 19 == 0 ? 255 : 0)
                    : (byte)(x == w / 2 || y % 64 is 0 or 63 || y == h - 1 ? 255 : 0);
            }
        }
        return bytes;
    }

    private static float[] LegacyFloraFloats(byte[] bytes)
    {
        var px = new float[bytes.Length];
        for (int i = 0; i < px.Length; i++) px[i] = bytes[i] / 255f;
        for (int i = 0; i < px.Length; i += 4)
            if (px[i + 3] < 0.01f) px[i] = px[i + 1] = px[i + 2] = 0;
        return px;
    }

    private static void LegacyFloraDilate(float[] px, int w, int h, int passes)
    {
        var copy = new float[px.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            Array.Copy(px, copy, px.Length);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int k = (y * w + x) * 4;
                    if (copy[k + 3] > 0.01f) continue;
                    float r = 0, g = 0, b = 0, n = 0;
                    for (int j = -1; j <= 1; j++)
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            int xx = x + i, yy = y + j;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            int q = (yy * w + xx) * 4;
                            float a = copy[q + 3] > 0.01f ? 1 : (copy[q] + copy[q + 1] + copy[q + 2] > 0 ? 0.5f : 0);
                            if (a <= 0) continue;
                            r += copy[q] * a; g += copy[q + 1] * a; b += copy[q + 2] * a; n += a;
                        }
                    }
                    if (n > 0) { px[k] = r / n; px[k + 1] = g / n; px[k + 2] = b / n; }
                }
            }
        }
    }

    private static byte[] LegacyFloraMipBytes(float[] level0, int width, int height, int cellsX, int cellsY)
    {
        var levels = new List<float[]> { level0 };
        var sizes = new List<(int W, int H)> { (width, height) };
        var coverage = new float[cellsX * cellsY];
        int cw = width / cellsX, ch = height / cellsY;
        for (int cy = 0; cy < cellsY; cy++)
            for (int cx = 0; cx < cellsX; cx++)
                coverage[cy * cellsX + cx] = LegacyFloraCoverage(level0, width, cx * cw, cy * ch, cw, ch, 1);
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
                        for (int i = 0; i < 2; i++)
                        {
                            int sx = Math.Min(sw - 1, 2 * x + i), sy = Math.Min(sh - 1, 2 * y + j);
                            int k = (sy * sw + sx) * 4;
                            r += src[k]; g += src[k + 1]; b += src[k + 2]; a += src[k + 3];
                        }
                    int o = (y * w + x) * 4;
                    dst[o] = r / 4; dst[o + 1] = g / 4; dst[o + 2] = b / 4; dst[o + 3] = a / 4;
                }
            }
            if (w >= cellsX * 2 && h >= cellsY * 2)
            {
                cw = w / cellsX; ch = h / cellsY;
                for (int cy = 0; cy < cellsY; cy++)
                    for (int cx = 0; cx < cellsX; cx++)
                    {
                        float want = coverage[cy * cellsX + cx], lo = 0.5f, hi = 8f;
                        for (int it = 0; it < 12; it++)
                        {
                            float mid = (lo + hi) / 2;
                            float got = LegacyFloraCoverage(dst, w, cx * cw, cy * ch, cw, ch, mid);
                            if (got < want) lo = mid; else hi = mid;
                        }
                        float scale = (lo + hi) / 2;
                        for (int y = cy * ch; y < (cy + 1) * ch; y++)
                            for (int x = cx * cw; x < (cx + 1) * cw; x++)
                            { int k = (y * w + x) * 4 + 3; dst[k] = Math.Min(1, dst[k] * scale); }
                    }
            }
            levels.Add(dst); sizes.Add((w, h));
        }
        var bytes = new byte[levels.Sum(level => level.Length)];
        int offset = 0;
        foreach (float[] level in levels)
        {
            byte[] packed = LegacyFloraPack(level);
            Array.Copy(packed, 0, bytes, offset, packed.Length); offset += packed.Length;
        }
        return bytes;
    }

    private static float LegacyFloraCoverage(float[] px, int width, int x0, int y0, int cw, int ch, float scale)
    {
        int n = 0;
        for (int y = y0; y < y0 + ch; y++)
            for (int x = x0; x < x0 + cw; x++)
                if (px[(y * width + x) * 4 + 3] * scale >= 0.5f) n++;
        return (float)n / (cw * ch);
    }

    private static byte[] LegacyFloraPack(float[] px)
    {
        var bytes = new byte[px.Length];
        for (int i = 0; i < px.Length; i++) bytes[i] = (byte)Math.Clamp((int)(px[i] * 255 + 0.5f), 0, 255);
        return bytes;
    }
}
