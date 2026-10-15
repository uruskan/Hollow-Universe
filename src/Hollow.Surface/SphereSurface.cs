using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// A plain surface: a sphere with gentle hills from value noise and no sea. Tests use it as a simple
/// <see cref="ISurface"/>, and <see cref="StandInWorld"/> lays its hills over continents. Deterministic: only
/// + − × ÷, floor and <see cref="Hash64"/>. Its collision is the default grid of a tile (<see cref="SurfaceGrid"/>).
/// </summary>
public sealed class SphereSurface : ISurface
{
    private readonly ulong _seed;
    private readonly (double Wavelength, double Amplitude)[] _octaves;

    public SphereSurface(double radius, ulong seed, double hillHeight = 12)
    {
        Radius = radius;
        _seed = seed;
        _octaves = new[] { (420.0, hillHeight), (130.0, hillHeight * 0.25), (37.0, hillHeight * 0.06) };
    }

    public double Radius { get; }

    public double SeaRadius => 0;

    public double HeightAt(Vector3D direction)
    {
        double h = 0;
        for (int i = 0; i < _octaves.Length; i++)
        {
            (double wavelength, double amplitude) = _octaves[i];
            Vector3D p = direction * (Radius / wavelength);
            h += amplitude * (ValueNoise(p, Hash64.Of(_seed, (ulong)i)) - 0.5) * 2;
        }
        return h;
    }

    /// <summary>Trilinear value noise in [0, 1) with smoothstep weights, on the integer lattice.</summary>
    public static double ValueNoise(Vector3D p, ulong seed)
    {
        double fx = Math.Floor(p.X), fy = Math.Floor(p.Y), fz = Math.Floor(p.Z);
        long ix = (long)fx, iy = (long)fy, iz = (long)fz;
        double tx = Smooth(p.X - fx), ty = Smooth(p.Y - fy), tz = Smooth(p.Z - fz);
        double c000 = Lattice(seed, ix, iy, iz), c100 = Lattice(seed, ix + 1, iy, iz);
        double c010 = Lattice(seed, ix, iy + 1, iz), c110 = Lattice(seed, ix + 1, iy + 1, iz);
        double c001 = Lattice(seed, ix, iy, iz + 1), c101 = Lattice(seed, ix + 1, iy, iz + 1);
        double c011 = Lattice(seed, ix, iy + 1, iz + 1), c111 = Lattice(seed, ix + 1, iy + 1, iz + 1);
        double x00 = c000 + (c100 - c000) * tx, x10 = c010 + (c110 - c010) * tx;
        double x01 = c001 + (c101 - c001) * tx, x11 = c011 + (c111 - c011) * tx;
        double y0 = x00 + (x10 - x00) * ty, y1 = x01 + (x11 - x01) * ty;
        return y0 + (y1 - y0) * tz;
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double Lattice(ulong seed, long x, long y, long z) =>
        (Hash64.Of(seed, (ulong)x, (ulong)y, (ulong)z) >> 11) * (1.0 / (1UL << 53));
}
