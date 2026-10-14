using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// A stand-in surface: a sphere with gentle hills from value noise. The network work uses it until the real planet
/// surface lands (GROUND.md §4.6), and tests use it as a simple <see cref="ISurface"/>. Deterministic: only + − × ÷,
/// floor and <see cref="Hash64"/>.
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

    public TriangleMesh GroundCollision(in ZoneFrame frame, double centreX, double centreZ, double size) =>
        GridCollision(this, frame, centreX, centreZ, size, spacing: 1.0);

    /// <summary>
    /// Ground triangles on a square grid of the zone's tangent plane: each grid point is projected onto the sphere
    /// and lifted to the ground. Any <see cref="ISurface"/> can use it; real surfaces may build collision from their
    /// own tiles instead, so it matches what is drawn.
    /// </summary>
    public static TriangleMesh GridCollision(ISurface surface, in ZoneFrame frame, double centreX, double centreZ, double size, double spacing)
    {
        int cells = Math.Max(1, (int)Math.Ceiling(size / spacing));
        double step = size / cells, x0 = centreX - size / 2, z0 = centreZ - size / 2;
        int n = cells + 1;
        var positions = new float[n * n * 3];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3D direction = frame.ToBody(x0 + i * step, 0, z0 + j * step).Normalized;
                var local = frame.ToLocal(direction * (surface.Radius + surface.HeightAt(direction)));
                int k = (j * n + i) * 3;
                positions[k] = local.X;
                positions[k + 1] = local.Y;
                positions[k + 2] = local.Z;
            }
        }
        var indices = new List<int>(cells * cells * 6);
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                // +X east, +Z south: (c − a) × (b − a) points up, so the order a, c, b is counter-clockwise from above.
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }
        return new TriangleMesh(positions, indices.ToArray());
    }

    /// <summary>Trilinear value noise in [0, 1) with smoothstep weights, on the integer lattice.</summary>
    private static double ValueNoise(Vector3D p, ulong seed)
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
