using System;

namespace Hollow.Core;

/// <summary>
/// Deterministic random stream (SplitMix64). Seed one per generated object from its address with
/// <see cref="Hash64"/>, so objects never share or shift each other's streams (ARCHITECTURE.md §8).
/// A mutable struct: pass it by <c>ref</c>.
/// </summary>
public struct Rng
{
    private ulong _state;

    public Rng(ulong seed)
    {
        _state = seed;
    }

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Hash64.Mix(_state);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform in (0, 1]: safe to take the logarithm of.</summary>
    public double NextDoubleNonZero() => 1.0 - NextDouble();

    /// <summary>Uniform in [min, max).</summary>
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }
        return (int)(((NextULong() >> 32) * (ulong)maxExclusive) >> 32);
    }

    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Standard normal deviate (Box–Muller with deterministic maths).</summary>
    public double Gaussian()
    {
        double u1 = NextDoubleNonZero();
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * DMath.Log(u1)) * DMath.Cos(DMath.TwoPi * u2);
    }

    public double Gaussian(double mean, double sigma) => mean + sigma * Gaussian();

    /// <summary>Log-uniform in [min, max).</summary>
    public double LogUniform(double min, double max) => DMath.Exp(Range(DMath.Log(min), DMath.Log(max)));

    /// <summary>Log-normal with the given median and spread in decades (dex).</summary>
    public double LogNormal(double median, double sigmaDex) => median * DMath.Pow10(sigmaDex * Gaussian());

    /// <summary>Rayleigh-distributed value with scale <paramref name="sigma"/> (non-negative).</summary>
    public double Rayleigh(double sigma) => sigma * Math.Sqrt(-2.0 * DMath.Log(NextDoubleNonZero()));

    /// <summary>Poisson-distributed count with mean <paramref name="lambda"/>.</summary>
    public long Poisson(double lambda)
    {
        if (!(lambda > 0))
        {
            return 0;
        }
        if (lambda < 30.0)
        {
            double limit = DMath.Exp(-lambda);
            long k = 0;
            double p = NextDouble();
            while (p > limit)
            {
                k++;
                p *= NextDouble();
            }
            return k;
        }
        double n = Math.Floor(lambda + Math.Sqrt(lambda) * Gaussian() + 0.5);
        return n < 0 ? 0 : (long)n;
    }

    /// <summary>Index chosen with probability proportional to its weight. Returns −1 if all weights are 0.</summary>
    public int PickWeighted(ReadOnlySpan<double> weights)
    {
        double total = 0;
        foreach (double w in weights)
        {
            total += w;
        }
        if (!(total > 0))
        {
            return -1;
        }
        double target = NextDouble() * total;
        double acc = 0;
        int last = -1;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0)
            {
                continue;
            }
            acc += weights[i];
            last = i;
            if (target < acc)
            {
                return i;
            }
        }
        return last;
    }
}
