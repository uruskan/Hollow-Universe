using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Deterministic 3D gradient noise with its analytic gradient (Perlin's lattice gradients, quintic fade), and the
/// sums of octaves the surface is built from. Only + − × ÷ and floor, so every machine gets the same bits
/// (ARCHITECTURE.md §8). The lattice hash is a 64-bit mix of the cell and the seed, so the noise never repeats.
/// </summary>
internal static class Noise
{
    // Perlin's twelve cube-edge gradients, four of them twice, so a 4-bit index picks one.
    private static readonly double[] Gx = { 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0, 1, -1, 0, 0 };
    private static readonly double[] Gy = { 1, 1, -1, -1, 0, 0, 0, 0, 1, -1, 1, -1, 1, 1, -1, -1 };
    private static readonly double[] Gz = { 0, 0, 0, 0, 1, 1, -1, -1, 1, 1, -1, -1, 0, 0, 1, 1 };

    private static int Corner(ulong seed, long x, long y, long z)
    {
        ulong h = seed ^ (ulong)x * 0x9E3779B97F4A7C15UL ^ (ulong)y * 0xC2B2AE3D27D4EB4FUL ^ (ulong)z * 0x165667B19E3779F9UL;
        h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
        h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
        return (int)(h >> 60);
    }

    /// <summary>Gradient noise at a point, roughly in [−1, 1], without the gradient (the same value as <see cref="Sample"/>).</summary>
    public static double Value(double x, double y, double z, ulong seed)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
        long ix = (long)fx, iy = (long)fy, iz = (long)fz;
        double tx = x - fx, ty = y - fy, tz = z - fz;
        double ux = tx * tx * tx * (tx * (tx * 6 - 15) + 10);
        double uy = ty * ty * ty * (ty * (ty * 6 - 15) + 10);
        double uz = tz * tz * tz * (tz * (tz * 6 - 15) + 10);
        int a = Corner(seed, ix, iy, iz), b = Corner(seed, ix + 1, iy, iz);
        int c = Corner(seed, ix, iy + 1, iz), d = Corner(seed, ix + 1, iy + 1, iz);
        int e = Corner(seed, ix, iy, iz + 1), f = Corner(seed, ix + 1, iy, iz + 1);
        int g = Corner(seed, ix, iy + 1, iz + 1), h = Corner(seed, ix + 1, iy + 1, iz + 1);
        double va = Gx[a] * tx + Gy[a] * ty + Gz[a] * tz;
        double vb = Gx[b] * (tx - 1) + Gy[b] * ty + Gz[b] * tz;
        double vc = Gx[c] * tx + Gy[c] * (ty - 1) + Gz[c] * tz;
        double vd = Gx[d] * (tx - 1) + Gy[d] * (ty - 1) + Gz[d] * tz;
        double ve = Gx[e] * tx + Gy[e] * ty + Gz[e] * (tz - 1);
        double vf = Gx[f] * (tx - 1) + Gy[f] * ty + Gz[f] * (tz - 1);
        double vg = Gx[g] * tx + Gy[g] * (ty - 1) + Gz[g] * (tz - 1);
        double vh = Gx[h] * (tx - 1) + Gy[h] * (ty - 1) + Gz[h] * (tz - 1);
        double x00 = va + ux * (vb - va), x10 = vc + ux * (vd - vc);
        double x01 = ve + ux * (vf - ve), x11 = vg + ux * (vh - vg);
        double y0 = x00 + uy * (x10 - x00), y1 = x01 + uy * (x11 - x01);
        return y0 + uz * (y1 - y0);
    }

    /// <summary>Gradient noise at a point, roughly in [−1, 1], and its gradient.</summary>
    public static double Sample(double x, double y, double z, ulong seed, out double dx, out double dy, out double dz)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
        long ix = (long)fx, iy = (long)fy, iz = (long)fz;
        double tx = x - fx, ty = y - fy, tz = z - fz;

        double ux = tx * tx * tx * (tx * (tx * 6 - 15) + 10);
        double uy = ty * ty * ty * (ty * (ty * 6 - 15) + 10);
        double uz = tz * tz * tz * (tz * (tz * 6 - 15) + 10);
        double dux = 30 * tx * tx * (tx * (tx - 2) + 1);
        double duy = 30 * ty * ty * (ty * (ty - 2) + 1);
        double duz = 30 * tz * tz * (tz * (tz - 2) + 1);

        int a = Corner(seed, ix, iy, iz), b = Corner(seed, ix + 1, iy, iz);
        int c = Corner(seed, ix, iy + 1, iz), d = Corner(seed, ix + 1, iy + 1, iz);
        int e = Corner(seed, ix, iy, iz + 1), f = Corner(seed, ix + 1, iy, iz + 1);
        int g = Corner(seed, ix, iy + 1, iz + 1), h = Corner(seed, ix + 1, iy + 1, iz + 1);

        double gax = Gx[a], gay = Gy[a], gaz = Gz[a];
        double gbx = Gx[b], gby = Gy[b], gbz = Gz[b];
        double gcx = Gx[c], gcy = Gy[c], gcz = Gz[c];
        double gdx = Gx[d], gdy = Gy[d], gdz = Gz[d];
        double gex = Gx[e], gey = Gy[e], gez = Gz[e];
        double gfx = Gx[f], gfy = Gy[f], gfz = Gz[f];
        double ggx = Gx[g], ggy = Gy[g], ggz = Gz[g];
        double ghx = Gx[h], ghy = Gy[h], ghz = Gz[h];

        double va = gax * tx + gay * ty + gaz * tz;
        double vb = gbx * (tx - 1) + gby * ty + gbz * tz;
        double vc = gcx * tx + gcy * (ty - 1) + gcz * tz;
        double vd = gdx * (tx - 1) + gdy * (ty - 1) + gdz * tz;
        double ve = gex * tx + gey * ty + gez * (tz - 1);
        double vf = gfx * (tx - 1) + gfy * ty + gfz * (tz - 1);
        double vg = ggx * tx + ggy * (ty - 1) + ggz * (tz - 1);
        double vh = ghx * (tx - 1) + ghy * (ty - 1) + ghz * (tz - 1);

        double k1 = vb - va, k2 = vc - va, k3 = ve - va;
        double k4 = va - vb - vc + vd, k5 = va - vc - ve + vg, k6 = va - vb - ve + vf;
        double k7 = -va + vb + vc - vd + ve - vf - vg + vh;

        double value = va + ux * k1 + uy * k2 + uz * k3 + ux * uy * k4 + uy * uz * k5 + uz * ux * k6 + ux * uy * uz * k7;

        // Gradient: the interpolated corner gradients plus the fade's derivative times the value differences.
        double uxy = ux * uy, uyz = uy * uz, uzx = uz * ux, uxyz = uxy * uz;
        dx = gax + ux * (gbx - gax) + uy * (gcx - gax) + uz * (gex - gax) + uxy * (gax - gbx - gcx + gdx)
            + uyz * (gax - gcx - gex + ggx) + uzx * (gax - gbx - gex + gfx)
            + uxyz * (-gax + gbx + gcx - gdx + gex - gfx - ggx + ghx)
            + dux * (k1 + uy * k4 + uz * k6 + uyz * k7);
        dy = gay + ux * (gby - gay) + uy * (gcy - gay) + uz * (gey - gay) + uxy * (gay - gby - gcy + gdy)
            + uyz * (gay - gcy - gey + ggy) + uzx * (gay - gby - gey + gfy)
            + uxyz * (-gay + gby + gcy - gdy + gey - gfy - ggy + ghy)
            + duy * (k2 + uz * k5 + ux * k4 + uzx * k7);
        dz = gaz + ux * (gbz - gaz) + uy * (gcz - gaz) + uz * (gez - gaz) + uxy * (gaz - gbz - gcz + gdz)
            + uyz * (gaz - gcz - gez + ggz) + uzx * (gaz - gbz - gez + gfz)
            + uxyz * (-gaz + gbz + gcz - gdz + gez - gfz - ggz + ghz)
            + duz * (k3 + ux * k6 + uy * k5 + uxy * k7);
        return value;
    }

    /// <summary>
    /// Fractal sum of <paramref name="octaves"/> octaves of noise at a unit direction, the first with wavelength
    /// <paramref name="wavelength"/> on a sphere of radius <paramref name="radius"/> (same units), each next one at
    /// <see cref="Lacunarity"/> times the frequency and <paramref name="gain"/> times the amplitude. Normalised to
    /// roughly [−1, 1].
    /// </summary>
    public static double Fbm(Vector3D p, double radius, double wavelength, int octaves, double gain, ulong seed)
    {
        double f = radius / wavelength, amplitude = 1, sum = 0, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            ulong s = seed + (ulong)i * 0x632BE59BD9B4E019UL;
            double o = Offset(s);
            sum += amplitude * Value(p.X * f + o, p.Y * f - o, p.Z * f + 0.5 * o, s);
            norm += amplitude;
            amplitude *= gain;
            f *= Lacunarity;
        }
        return sum / norm;
    }

    /// <summary>Lacunarity of every octave sum: a little off 2, so the lattices of octaves never line up.</summary>
    public const double Lacunarity = 2.03;

    /// <summary>A per-octave offset in [0, 64), so octaves don't share their lattice origin.</summary>
    public static double Offset(ulong seed) => (Hash64.Mix(seed) >> 11) * (64.0 / 9007199254740992.0);

    /// <summary>A vector of three independent noises (for domain warping), roughly in [−1, 1] each.</summary>
    public static Vector3D Vector(Vector3D p, double radius, double wavelength, int octaves, double gain, ulong seed) =>
        new(Fbm(p, radius, wavelength, octaves, gain, seed),
            Fbm(p, radius, wavelength, octaves, gain, seed ^ 0x5851F42D4C957F2DUL),
            Fbm(p, radius, wavelength, octaves, gain, seed ^ 0x14057B7EF767814FUL));
}
