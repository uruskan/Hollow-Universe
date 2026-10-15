using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Eroded ridged noise for the walking-scale ground's mountains (docs/WALKING_GROUND.md §4), after
/// <see cref="Noise.Swiss"/>: each octave displaced along the slopes summed so far and damped where the sum is low, so
/// valleys are smooth and spurs branch. Two changes keep it from drawing straight lines at walking scale: every octave is
/// turned by a rotation of its own (a ridged octave folds along its lattice's planes, which would otherwise line up from
/// octave to octave and run straight for hundreds of metres), and crests are rounded (a smooth |n|) rather than folds,
/// the large ones most. Deterministic: the rotations come from <see cref="Rng"/>, the rest is + − × ÷ and √.
/// </summary>
internal sealed class RidgedNoise
{
    private readonly double[] _rotation; // 9 a octave, row-major
    private readonly ulong _seed;
    private readonly int _octaves;
    private readonly double _gain, _warp, _crest;

    /// <param name="crest">How round the first octave's crests are (the smooth |n|'s width); each next octave's is half.</param>
    public RidgedNoise(ulong seed, int octaves, double gain, double warp, double crest)
    {
        _seed = seed;
        _octaves = octaves;
        _gain = gain;
        _warp = warp;
        _crest = crest;
        _rotation = new double[9 * octaves];
        var rng = new Rng(Hash64.Of(seed, 0x524F54)); // "ROT"
        for (int i = 0; i < octaves; i++)
        {
            Vector3D a = RandomUnit(ref rng), b = RandomUnit(ref rng);
            Vector3D x = a;
            Vector3D y = (b - x * Vector3D.Dot(b, x)).Normalized;
            Vector3D z = Vector3D.Cross(x, y);
            int k = 9 * i;
            _rotation[k] = x.X; _rotation[k + 1] = x.Y; _rotation[k + 2] = x.Z;
            _rotation[k + 3] = y.X; _rotation[k + 4] = y.Y; _rotation[k + 5] = y.Z;
            _rotation[k + 6] = z.X; _rotation[k + 7] = z.Y; _rotation[k + 8] = z.Z;
        }
    }

    private static Vector3D RandomUnit(ref Rng rng)
    {
        double z = rng.Range(-1, 1), a = rng.Range(0, DMath.TwoPi), r = Math.Sqrt(Math.Max(0, 1 - z * z));
        return new Vector3D(r * DMath.Cos(a), z, r * DMath.Sin(a));
    }

    /// <summary>The noise at a unit direction on a sphere of <paramref name="radius"/>: about 0 (valleys) to 1 (crests).</summary>
    public double At(Vector3D p, double radius, double wavelength, NoiseBasis basis)
    {
        double f0 = radius / wavelength;
        double px = p.X * f0, py = p.Y * f0, pz = p.Z * f0;
        double sum = 0, amplitude = 1, frequency = 1, norm = 0, crest = _crest;
        double sx = 0, sy = 0, sz = 0; // the slopes summed so far, in the unrotated frame
        for (int i = 0; i < _octaves; i++)
        {
            int k = 9 * i;
            // This octave's frame: the point and the warp turned by its rotation.
            double wx = px + _warp * sx, wy = py + _warp * sy, wz = pz + _warp * sz;
            double rx = _rotation[k] * wx + _rotation[k + 1] * wy + _rotation[k + 2] * wz;
            double ry = _rotation[k + 3] * wx + _rotation[k + 4] * wy + _rotation[k + 5] * wz;
            double rz = _rotation[k + 6] * wx + _rotation[k + 7] * wy + _rotation[k + 8] * wz;
            ulong s = _seed + (ulong)i * 0x632BE59BD9B4E019UL;
            double o = Noise.Offset(s);
            double n = Noise.Sample(rx * frequency + o, ry * frequency - o, rz * frequency + 0.5 * o, s,
                out double dx, out double dy, out double dz, basis);
            // A rounded crest: 1 − smooth|n|, scaled so it still runs 0 to 1.
            double smooth = Math.Sqrt(n * n + crest * crest) - crest;
            double r = 1 - smooth / (Math.Sqrt(1 + crest * crest) - crest);
            sum += amplitude * r * r;
            norm += amplitude;
            // The slope back in the unrotated frame (the rotation's transpose).
            double gx = _rotation[k] * dx + _rotation[k + 3] * dy + _rotation[k + 6] * dz;
            double gy = _rotation[k + 1] * dx + _rotation[k + 4] * dy + _rotation[k + 7] * dz;
            double gz = _rotation[k + 2] * dx + _rotation[k + 5] * dy + _rotation[k + 8] * dz;
            sx -= amplitude * gx * n;
            sy -= amplitude * gy * n;
            sz -= amplitude * gz * n;
            frequency *= Noise.Lacunarity;
            amplitude *= _gain * Math.Clamp(sum / norm * 1.6, 0, 1);
            crest *= 0.5;
        }
        return (sum / norm - 0.3) / 0.62;
    }
}
