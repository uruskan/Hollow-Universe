using System;
using System.Threading;

namespace HollowUniverse.Surface.Flora;

public static partial class FloraTextures
{
    internal const int ImpostorBandRows = 64;
    private const int ImpostorDilationPasses = 4;

    /// <summary>
    /// Finishes an RGBA8 readback without expanding the whole atlas to floats. Four rows of original neighbours
    /// on either side of each band preserve all four dilation passes, including across atlas-cell boundaries.
    /// The first mip is averaged from unquantized dilated floats, exactly as in the full-atlas path.
    /// </summary>
    internal static byte[] ImpostorPixels(byte[] rgba, int width, int height, int cellsX, int cellsY,
        bool colour, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        int baseCount = checked(width * height * 4);
        if (width <= 0 || height <= 0 || rgba.Length != baseCount || cellsX <= 0 || cellsY <= 0
            || cellsX > width || cellsY > height) throw new ArgumentException("Invalid impostor pixel dimensions.");
        var bytes = new byte[colour ? MipByteCount(width, height) : baseCount];
        int mw = Math.Max(1, width / 2), mh = Math.Max(1, height / 2);
        float[]? firstMip = colour && (width > 1 || height > 1) ? new float[mw * mh * 4] : null;
        FinishImpostorBands(rgba, width, height, bytes, firstMip, cancellation);
        if (firstMip != null)
        {
            float[] coverage = ByteCellCoverage(rgba, width, height, cellsX, cellsY);
            PreserveCoverage(firstMip, mw, mh, cellsX, cellsY, coverage);
            int firstCount = mw * mh * 4;
            PackLevel(firstMip, firstCount, bytes, baseCount);
            AppendCoverageMipmaps(firstMip, mw, mh, cellsX, cellsY, coverage, bytes, baseCount + firstCount, true);
        }
        cancellation.ThrowIfCancellationRequested();
        return bytes;
    }

    private static void FinishImpostorBands(byte[] rgba, int width, int height, byte[] bytes,
        float[]? firstMip, CancellationToken cancellation)
    {
        int stride = width * 4;
        var band = new float[stride * Math.Min(height, ImpostorBandRows + 2 * ImpostorDilationPasses)];
        var scanlines = new float[stride * 3];
        int mw = Math.Max(1, width / 2), mh = Math.Max(1, height / 2);
        for (int start = 0; start < height; start += ImpostorBandRows)
        {
            cancellation.ThrowIfCancellationRequested();
            int end = Math.Min(height, start + ImpostorBandRows);
            int begin = Math.Max(0, start - ImpostorDilationPasses);
            int limit = Math.Min(height, end + ImpostorDilationPasses);
            int count = (limit - begin) * stride;
            for (int i = 0; i < count; i++) band[i] = rgba[begin * stride + i] / 255f;
            // Preserve the previous conversion's transparent-colour rule and its strict alpha comparison.
            for (int i = 0; i < count; i += 4)
                if (band[i + 3] < 0.01f) band[i] = band[i + 1] = band[i + 2] = 0;
            DilateRows(band, width, limit - begin, ImpostorDilationPasses, scanlines);
            int coreStart = (start - begin) * stride, coreCount = (end - start) * stride;
            for (int i = 0; i < coreCount; i++)
                bytes[start * stride + i] = (byte)Math.Clamp((int)(band[coreStart + i] * 255 + 0.5f), 0, 255);

            if (firstMip == null) continue;
            // Bands and halos start on even rows. Odd atlas edges retain the old clamped 2x2 sampling rule.
            for (int y = start / 2; y < Math.Min(mh, (end + 1) / 2); y++)
            {
                for (int x = 0; x < mw; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < 2; j++)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            int sx = Math.Min(width - 1, 2 * x + i), sy = Math.Min(height - 1, 2 * y + j);
                            int k = ((sy - begin) * width + sx) * 4;
                            r += band[k]; g += band[k + 1]; b += band[k + 2]; a += band[k + 3];
                        }
                    }
                    int o = (y * mw + x) * 4;
                    firstMip[o] = r / 4; firstMip[o + 1] = g / 4;
                    firstMip[o + 2] = b / 4; firstMip[o + 3] = a / 4;
                }
            }
        }
    }

    private static float[] ByteCellCoverage(byte[] rgba, int width, int height, int cellsX, int cellsY)
    {
        var coverage = new float[cellsX * cellsY];
        int cw = width / cellsX, ch = height / cellsY;
        for (int cy = 0; cy < cellsY; cy++)
        {
            for (int cx = 0; cx < cellsX; cx++)
            {
                int n = 0;
                for (int y = cy * ch; y < (cy + 1) * ch; y++)
                    for (int x = cx * cw; x < (cx + 1) * cw; x++)
                        if (rgba[(y * width + x) * 4 + 3] / 255f >= 0.5f) n++;
                coverage[cy * cellsX + cx] = (float)n / (cw * ch);
            }
        }
        return coverage;
    }
}
