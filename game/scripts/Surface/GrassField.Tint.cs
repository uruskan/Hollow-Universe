using System;
using Godot;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    /// <summary>
    /// The grass colour's noise on the CPU: the surface's macro noise (NoiseTextures.Clouds) sampled as the shaders sample
    /// it (bilinear, repeating, texel centres, the base level), and surface_grass.gdshaderinc's dryness and brightness at a
    /// place, so a clump's colour, worked out once for its root, is the ground's grass colour under it.
    /// </summary>
    private sealed class TintNoise
    {
        private readonly byte[] _rgba;
        private readonly int _width, _height;

        public TintNoise(Image image)
        {
            using Image copy = (Image)image.Duplicate();
            if (copy.IsCompressed()) copy.Decompress();
            if (copy.HasMipmaps()) copy.ClearMipmaps();
            copy.Convert(Image.Format.Rgba8);
            _width = copy.GetWidth();
            _height = copy.GetHeight();
            _rgba = copy.GetData();
        }

        /// <summary>For the engine's lifetime probe: a flat noise of 0.5.</summary>
        public TintNoise()
        {
            _width = _height = 1;
            _rgba = new byte[] { 128, 128, 128, 128 };
        }

        // One channel of the noise at texture coordinates (u, v), as texture() reads it.
        private double Sample(double u, double v, int channel)
        {
            double x = u * _width - 0.5, y = v * _height - 0.5;
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int x0 = Wrap((long)fx, _width), y0 = Wrap((long)fy, _height);
            int x1 = (x0 + 1) % _width, y1 = (y0 + 1) % _height;
            double tx = x - fx, ty = y - fy;
            double a = _rgba[(y0 * _width + x0) * 4 + channel], b = _rgba[(y0 * _width + x1) * 4 + channel];
            double c = _rgba[(y1 * _width + x0) * 4 + channel], d = _rgba[(y1 * _width + x1) * 4 + channel];
            return ((a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty) / 255.0;
        }

        private static int Wrap(long i, int n) => (int)(((i % n) + n) % n);

        private static double SmoothStep(double e0, double e1, double x)
        {
            double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>hu_grass_dryness: how dry the grass looks at metres (u, v) on its face, for the climate's moisture.</summary>
        public double Dryness(double u, double v, double moisture)
        {
            double macro = Sample(u / 1730.0, v / 1730.0, 1);
            double macro2 = Sample(u / 211.0 + 0.31, v / 211.0 + 0.77, 1);
            double patch = SmoothStep(0.3, 0.7, macro2);
            double local = Sample(u / 13.0 + 0.19, v / 13.0 + 0.67, 0);
            return Math.Clamp((1 - moisture) * 0.8 - 0.12 + (macro - 0.5) * 0.45 + (1 - patch) * 0.22 + (local - 0.5) * 0.5, 0, 1);
        }

        /// <summary>hu_grass_tint's brightness: the large and local patches' product.</summary>
        public double Brightness(double u, double v)
        {
            double macro = Sample(u / 1730.0, v / 1730.0, 0);
            double macro3 = Sample(u / 37.0 + 0.13, v / 37.0 + 0.41, 2);
            double local = Sample(u / 7.7 + 0.41, v / 7.7 + 0.13, 2);
            return (0.84 + 0.32 * macro) * (0.82 + 0.30 * macro3) * (0.84 + 0.28 * local);
        }
    }
}
