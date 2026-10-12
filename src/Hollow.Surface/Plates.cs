using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Tectonic plates of a surface (GROUND.md §4.6): plate centres on the sphere (a Voronoi partition), each plate's
/// rotation (an Euler pole and speed) and how continental it is. At a point it gives a smooth "continental bias" and
/// the relief that the boundaries raise or cut: mountain belts where plates converge on land, trenches where they
/// converge at sea, rift valleys where land is pulled apart and ridges where ocean floor spreads.
/// </summary>
/// <remarks>
/// Every plate near the point contributes with a soft (exponential) weight, and every boundary's relief is a
/// function of the distance across that boundary, the same seen from either side, so the field is continuous
/// everywhere, triple junctions included.
/// </remarks>
internal sealed class Plates
{
    public const int MaxPlates = 32;

    // Softness of the plate weights: e^−1 at about 250 km from a boundary on an Earth-sized body.
    private const double Sharpness = 30;
    private const double WeightCutoff = 7.0 / Sharpness;
    private const double Km = 1000;

    private readonly Vector3D[] _centre;
    private readonly Vector3D[] _omega;
    private readonly double[] _bias;
    private readonly double[] _pairLength;
    private readonly double _radius;
    private readonly double _maxReach;

    public Plates(ulong seed, int count, double continentalShare, double radius)
    {
        if (count < 2 || count > MaxPlates)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        var rng = new Rng(Hash64.Of(seed, 0x504C4154)); // "PLAT"
        _centre = new Vector3D[count];
        _omega = new Vector3D[count];
        _bias = new double[count];
        _radius = radius;
        // Boundary relief reaches about 600 km to either side; beyond that a pair is skipped.
        _maxReach = 600_000 / radius;
        for (int i = 0; i < count; i++)
        {
            _centre[i] = RandomUnit(ref rng);
            _omega[i] = RandomUnit(ref rng) * rng.Range(0.3, 1.0);
        }
        _pairLength = new double[count * count];
        for (int i = 0; i < count; i++)
        {
            for (int k = 0; k < count; k++)
            {
                _pairLength[i * count + k] = (_centre[i] - _centre[k]).Length;
            }
        }
        // A share of the plates carry continents; the rest are ocean floor. Biases stay clear of 0 so every plate
        // is clearly one or the other.
        int continental = Math.Clamp((int)Math.Floor(continentalShare * count + 0.5), 1, count - 1);
        Span<int> order = stackalloc int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        for (int i = 0; i < count; i++)
        {
            _bias[order[i]] = i < continental ? rng.Range(0.25, 1.0) : -rng.Range(0.25, 1.0);
        }
    }

    public int Count => _centre.Length;

    private static Vector3D RandomUnit(ref Rng rng)
    {
        double z = rng.Range(-1, 1);
        double a = rng.Range(0, DMath.TwoPi);
        double r = Math.Sqrt(Math.Max(0, 1 - z * z));
        return new Vector3D(r * DMath.Cos(a), z, r * DMath.Sin(a));
    }

    /// <summary>Dot products of a unit direction with every plate centre; returns the largest.</summary>
    public double Dots(Vector3D q, Span<double> dots)
    {
        double best = -2;
        for (int k = 0; k < _centre.Length; k++)
        {
            double d = Vector3D.Dot(q, _centre[k]);
            dots[k] = d;
            if (d > best)
            {
                best = d;
            }
        }
        return best;
    }

    /// <summary>The smoothed continental bias, −1 (ocean plate) to 1 (continental plate).</summary>
    public double Bias(ReadOnlySpan<double> dots, double best)
    {
        double weightSum = 0, biasSum = 0;
        for (int p = 0; p < _centre.Length; p++)
        {
            double gap = best - dots[p];
            if (gap > WeightCutoff)
            {
                continue;
            }
            double w = DMath.Exp(-Sharpness * gap);
            weightSum += w;
            biasSum += w * _bias[p];
        }
        return biasSum / weightSum;
    }

    /// <summary>
    /// The boundary relief in metres at a unit direction and its positive part (where mountains grow).
    /// <paramref name="strength"/> (0–1) varies the relief along the boundaries; <paramref name="land"/> (0–1) says
    /// whether the boundary crosses land (mountain belts, rifts) or sea (trenches, ridges) here.
    /// </summary>
    public void Relief(Vector3D q, ReadOnlySpan<double> dots, double best, double strength, double land,
        out double relief, out double uplift)
    {
        int n = _centre.Length;
        double weightSum = 0, reliefSum = 0, upliftSum = 0;
        for (int p = 0; p < n; p++)
        {
            double gap = best - dots[p];
            if (gap > WeightCutoff)
            {
                continue;
            }
            double w = DMath.Exp(-Sharpness * gap);
            weightSum += w;
            double r = 0, u = 0;
            for (int k = 0; k < n; k++)
            {
                if (k == p)
                {
                    continue;
                }
                double x = (dots[p] - dots[k]) / _pairLength[p * n + k];
                if (x > _maxReach || x < -_maxReach)
                {
                    continue;
                }
                PairRelief(p, k, q, x * _radius, land, out double h, out double up);
                r += h;
                u += up;
            }
            reliefSum += w * r;
            upliftSum += w * u;
        }
        relief = reliefSum / weightSum * strength;
        uplift = upliftSum / weightSum * strength;
    }

    // Relief of the boundary between plates p and k at distance x (metres) across it.
    private void PairRelief(int p, int k, Vector3D q, double x, double land, out double relief, out double uplift)
    {
        // Relative motion across the boundary: positive when the plates converge.
        Vector3D vp = Vector3D.Cross(_omega[p], q), vk = Vector3D.Cross(_omega[k], q);
        Vector3D toward = _centre[k] - _centre[p];
        toward -= q * Vector3D.Dot(q, toward);
        double tl = toward.Length;
        relief = 0;
        uplift = 0;
        if (tl < 1e-12)
        {
            return;
        }
        double convergence = Vector3D.Dot(vp - vk, toward) / tl;
        if (convergence > 0)
        {
            double s = DMath.SmoothStep(0.03, 0.55, convergence);
            // On land: a mountain belt (collision or coastal range). At sea: a trench with an arc of islands behind.
            double belt = 4600 * Gauss(x, 0, 150 * Km) + 500 * Gauss(x, 0, 320 * Km);
            double trench = -3200 * Gauss(x, 0, 40 * Km) + 1500 * Gauss(Math.Abs(x), 95 * Km, 30 * Km);
            uplift = s * land * belt;
            relief = uplift + s * (1 - land) * trench;
            return;
        }
        double d = DMath.SmoothStep(0.03, 0.55, -convergence);
        double ax = Math.Abs(x);
        // At sea: a broad spreading ridge with a rift along its crest. On land: a rift valley between raised shoulders.
        double ridge = 1900 * Gauss(x, 0, 360 * Km) - 380 * Gauss(x, 0, 14 * Km);
        double rift = -650 * Gauss(ax, 0, 22 * Km) + 750 * Gauss(ax, 62 * Km, 38 * Km);
        uplift = d * land * 750 * Gauss(ax, 62 * Km, 38 * Km);
        relief = d * (land * rift + (1 - land) * ridge);
    }

    private static double Gauss(double x, double centre, double width)
    {
        double t = (x - centre) / width;
        return t * t > 40 ? 0 : DMath.Exp(-t * t);
    }
}
