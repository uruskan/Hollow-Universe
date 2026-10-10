using System;
using System.Globalization;
using Hollow.Core;

namespace Hollow.Tests;

public static class CoreTests
{
    // Golden values: if these change, deterministic maths or hashing changed, which changes the whole universe.
    // A mismatch on another platform (Windows, ARM) means generation is NOT cross-platform deterministic.
    private const ulong DMathGolden = 0xAFF50095664D5483UL;
    private const ulong RngGolden = 0x158A24BA981B6D2BUL;

    [Test]
    public static void DMathMatchesPlatformMaths()
    {
        var rng = new Rng(12345);
        double worst = 0;
        for (int i = 0; i < 200_000; i++)
        {
            double x = rng.Range(-700, 700);
            worst = Math.Max(worst, Ulps(Math.Exp(x), DMath.Exp(x)));
            double y = rng.LogUniform(1e-300, 1e300);
            worst = Math.Max(worst, Ulps(Math.Log(y), DMath.Log(y), 1e-300));
            double a = rng.Range(-1e4, 1e4);
            worst = Math.Max(worst, Ulps(Math.Sin(a), DMath.Sin(a), 1e-16));
            worst = Math.Max(worst, Ulps(Math.Cos(a), DMath.Cos(a), 1e-16));
            double t = rng.Range(-50, 50);
            double u = rng.Range(-50, 50);
            worst = Math.Max(worst, Ulps(Math.Atan2(t, u), DMath.Atan2(t, u), 1e-16));
            double p = rng.Range(0.01, 100);
            double q = rng.Range(-4, 4);
            worst = Math.Max(worst, Ulps(Math.Pow(p, q), DMath.Pow(p, q)) / 64); // pow amplifies log error by |q·log p|
        }
        Fixtures.Report($"worst error: {worst:0.0} ulp");
        Assert.True(worst <= 4, $"DMath error {worst} ulp exceeds 4 ulp");
        Assert.Equal(1.0, DMath.Exp(0), "exp(0)");
        Assert.Equal(0.0, DMath.Log(1), "log(1)");
        Assert.Equal(0.0, DMath.Sin(0), "sin(0)");
        Assert.Equal(1.0, DMath.Cos(0), "cos(0)");
    }

    [Test]
    public static void DMathIsBitIdenticalToGolden()
    {
        ulong h = 0;
        for (int i = 0; i < 20000; i++)
        {
            double x = -50 + i * 0.005;
            h = Hash64.Combine(h, Bits(DMath.Exp(x / 5)));
            h = Hash64.Combine(h, Bits(DMath.Log(1 + i * 0.37)));
            h = Hash64.Combine(h, Bits(DMath.Sin(x)));
            h = Hash64.Combine(h, Bits(DMath.Cos(x * 3.1)));
            h = Hash64.Combine(h, Bits(DMath.Atan2(x, 1.5)));
            h = Hash64.Combine(h, Bits(DMath.Pow(1 + i * 0.001, 2.5)));
            h = Hash64.Combine(h, Bits(DMath.Sech2(x / 7)));
        }
        Fixtures.Report($"hash 0x{h:X16}");
        Assert.Equal(DMathGolden, h, "DMath golden hash");
    }

    [Test]
    public static void RngAndHashAreStable()
    {
        var rng = new Rng(Hash64.Of("Hollow"));
        ulong h = Hash64.Of(1, 2, 3, 4);
        for (int i = 0; i < 1000; i++)
        {
            h = Hash64.Combine(h, rng.NextULong());
            h = Hash64.Combine(h, Bits(rng.Gaussian()));
            h = Hash64.Combine(h, (ulong)rng.Poisson(i * 0.1));
        }
        Fixtures.Report($"hash 0x{h:X16}");
        Assert.Equal(RngGolden, h, "Rng golden hash");
    }

    [Test]
    public static void PoissonHasTheRightMean()
    {
        var rng = new Rng(7);
        foreach (double lambda in new[] { 0.3, 4.0, 25.0, 120.0 })
        {
            double sum = 0;
            const int n = 40000;
            for (int i = 0; i < n; i++)
            {
                sum += rng.Poisson(lambda);
            }
            Assert.Near(lambda, sum / n, Math.Max(0.02, 4 * Math.Sqrt(lambda / n)), $"Poisson mean for λ={lambda}");
        }
    }

    private static ulong Bits(double d) => (ulong)BitConverter.DoubleToInt64Bits(d);

    private static double Ulps(double expected, double actual, double absoluteFloor = 0)
    {
        if (expected == actual)
        {
            return 0;
        }
        double diff = Math.Abs(expected - actual);
        if (diff <= absoluteFloor)
        {
            return 0;
        }
        double ulp = Math.BitIncrement(Math.Abs(expected)) - Math.Abs(expected);
        return diff / ulp;
    }

    internal static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
