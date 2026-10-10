using System;

namespace Hollow.Core;

/// <summary>
/// Deterministic maths. Every function here is built only from IEEE 754 operations that are exactly rounded
/// (+, −, ×, ÷, <see cref="Math.Sqrt(double)"/>, <see cref="Math.Floor(double)"/>, comparisons and bit
/// manipulation), so results are bit-identical on Windows, Linux, macOS, x64 and ARM64.
/// <see cref="Math.Exp(double)"/>, <see cref="Math.Log(double)"/>, <see cref="Math.Sin(double)"/> and friends
/// call the platform's C library and may differ in the last bit between platforms, which is enough to make
/// two machines disagree about whether a star exists. Use these instead in anything that generates the
/// universe (ARCHITECTURE.md §8). Accuracy: within a few ulp of the platform functions.
/// </summary>
public static class DMath
{
    public const double Pi = 3.141592653589793;
    public const double TwoPi = 6.283185307179586;
    public const double HalfPi = 1.5707963267948966;
    public const double DegToRad = Pi / 180.0;
    public const double RadToDeg = 180.0 / Pi;

    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;
    private const double InvLn2 = 1.44269504088896338700e+00;
    private const double Sqrt2 = 1.4142135623730951;
    private const double Sqrt3 = 1.7320508075688772;
    private const double TanPiOver12 = 0.2679491924311227;
    private const double Ln10 = 2.302585092994046;

    // pi/2 split into three 33-bit pieces plus a tail (fdlibm), for exact argument reduction.
    private const double PiO2Part1 = 1.57079632673412561417e+00;
    private const double PiO2Part2 = 6.07710050630396597660e-11;
    private const double PiO2Part2Tail = 2.02226624879595063154e-21;
    private const double TwoOverPi = 6.36619772367581382433e-01;

    /// <summary>e^x.</summary>
    public static double Exp(double x)
    {
        if (double.IsNaN(x))
        {
            return x;
        }
        if (x > 709.782712893384)
        {
            return double.PositiveInfinity;
        }
        if (x < -745.1332191019412)
        {
            return 0.0;
        }

        double kf = Math.Floor(x * InvLn2 + 0.5);
        int k = (int)kf;
        double r = (x - kf * Ln2Hi) - kf * Ln2Lo; // |r| <= ~0.3466

        // Taylor series of e^r to degree 13 (truncation error < 1e-17 for |r| <= 0.35).
        double p = 1.0 + r * (1.0 + r * (1.0 / 2 + r * (1.0 / 6 + r * (1.0 / 24 + r * (1.0 / 120 + r * (1.0 / 720
            + r * (1.0 / 5040 + r * (1.0 / 40320 + r * (1.0 / 362880 + r * (1.0 / 3628800 + r * (1.0 / 39916800
            + r * (1.0 / 479001600 + r * (1.0 / 6227020800)))))))))))));
        return ScaleByPowerOfTwo(p, k);
    }

    /// <summary>Natural logarithm.</summary>
    public static double Log(double x)
    {
        if (double.IsNaN(x) || x < 0)
        {
            return double.NaN;
        }
        if (x == 0)
        {
            return double.NegativeInfinity;
        }
        if (double.IsPositiveInfinity(x))
        {
            return x;
        }

        long bits = BitConverter.DoubleToInt64Bits(x);
        int biased = (int)((bits >> 52) & 0x7FF);
        int exponent;
        if (biased == 0)
        {
            // Subnormal: scale into the normal range first.
            x *= 18014398509481984.0; // 2^54
            bits = BitConverter.DoubleToInt64Bits(x);
            biased = (int)((bits >> 52) & 0x7FF);
            exponent = biased - 1023 - 54;
        }
        else
        {
            exponent = biased - 1023;
        }

        double m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L); // [1, 2)
        if (m > Sqrt2)
        {
            m *= 0.5;
            exponent++;
        }

        // log(m) = 2 atanh(s), s = (m - 1) / (m + 1), |s| <= 0.1716.
        double s = (m - 1.0) / (m + 1.0);
        double s2 = s * s;
        double t = s2 * (1.0 / 3 + s2 * (1.0 / 5 + s2 * (1.0 / 7 + s2 * (1.0 / 9 + s2 * (1.0 / 11 + s2 * (1.0 / 13
            + s2 * (1.0 / 15 + s2 * (1.0 / 17 + s2 * (1.0 / 19 + s2 * (1.0 / 21 + s2 * (1.0 / 23)))))))))));
        double logM = 2.0 * s + 2.0 * s * t;
        double e = exponent;
        return e * Ln2Hi + (e * Ln2Lo + logM);
    }

    /// <summary>Base-10 logarithm.</summary>
    public static double Log10(double x) => Log(x) / Ln10;

    /// <summary>x^y for x &gt;= 0 (and integer y for negative x).</summary>
    public static double Pow(double x, double y)
    {
        if (y == 0)
        {
            return 1.0;
        }
        if (x == 0)
        {
            return y > 0 ? 0.0 : double.PositiveInfinity;
        }
        if (x < 0)
        {
            double yi = Math.Floor(y);
            if (yi != y)
            {
                return double.NaN;
            }
            double r = Exp(y * Log(-x));
            bool odd = Math.Floor(yi * 0.5) != yi * 0.5;
            return odd ? -r : r;
        }
        return Exp(y * Log(x));
    }

    /// <summary>10^x.</summary>
    public static double Pow10(double x) => Exp(x * Ln10);

    /// <summary>Sine. Exact argument reduction for |x| &lt; 1e5.</summary>
    public static double Sin(double x)
    {
        Reduce(x, out double r, out int q);
        return (q & 3) switch
        {
            0 => SinKernel(r),
            1 => CosKernel(r),
            2 => -SinKernel(r),
            _ => -CosKernel(r),
        };
    }

    /// <summary>Cosine. Exact argument reduction for |x| &lt; 1e5.</summary>
    public static double Cos(double x)
    {
        Reduce(x, out double r, out int q);
        return (q & 3) switch
        {
            0 => CosKernel(r),
            1 => -SinKernel(r),
            2 => -CosKernel(r),
            _ => SinKernel(r),
        };
    }

    /// <summary>Arc tangent, result in [−π/2, π/2].</summary>
    public static double Atan(double x)
    {
        if (double.IsNaN(x))
        {
            return x;
        }
        bool negative = x < 0;
        if (negative)
        {
            x = -x;
        }
        bool inverted = false;
        if (x > 1.0)
        {
            x = 1.0 / x;
            inverted = true;
        }
        double offset = 0.0;
        if (x > TanPiOver12)
        {
            // atan(x) = π/6 + atan((x√3 − 1) / (x + √3)); the new argument is within ±tan(π/12).
            x = (x * Sqrt3 - 1.0) / (x + Sqrt3);
            offset = Pi / 6.0;
        }
        double x2 = x * x;
        double series = x * (1.0 - x2 * (1.0 / 3 - x2 * (1.0 / 5 - x2 * (1.0 / 7 - x2 * (1.0 / 9 - x2 * (1.0 / 11
            - x2 * (1.0 / 13 - x2 * (1.0 / 15 - x2 * (1.0 / 17 - x2 * (1.0 / 19 - x2 * (1.0 / 21 - x2 * (1.0 / 23
            - x2 * (1.0 / 25 - x2 * (1.0 / 27 - x2 * (1.0 / 29 - x2 * (1.0 / 31))))))))))))))));
        double result = offset + series;
        if (inverted)
        {
            result = HalfPi - result;
        }
        return negative ? -result : result;
    }

    /// <summary>Angle of (x, y) in (−π, π].</summary>
    public static double Atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }
        if (x > 0)
        {
            return Atan(y / x);
        }
        if (x < 0)
        {
            return y >= 0 ? Atan(y / x) + Pi : Atan(y / x) - Pi;
        }
        if (y > 0)
        {
            return HalfPi;
        }
        return y < 0 ? -HalfPi : 0.0;
    }

    /// <summary>Arc sine, from <see cref="Atan2"/>.</summary>
    public static double Asin(double x) => Atan2(x, Math.Sqrt(Math.Max(0.0, 1.0 - x * x)));

    /// <summary>Arc cosine, from <see cref="Atan2"/>.</summary>
    public static double Acos(double x) => Atan2(Math.Sqrt(Math.Max(0.0, 1.0 - x * x)), x);

    /// <summary>sech²(x) = 1 / cosh²(x), computed without overflow.</summary>
    public static double Sech2(double x)
    {
        double e = Exp(-2.0 * Math.Abs(x));
        double d = 1.0 + e;
        return 4.0 * e / (d * d);
    }

    /// <summary>Hermite smoothstep from 0 at <paramref name="edge0"/> to 1 at <paramref name="edge1"/>.</summary>
    public static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Wraps an angle into (−π, π].</summary>
    public static double WrapAngle(double a)
    {
        double w = a - TwoPi * Math.Floor((a + Pi) / TwoPi);
        return w <= -Pi ? w + TwoPi : w;
    }

    /// <summary>x · 2^k, exact (including subnormal results).</summary>
    public static double ScaleByPowerOfTwo(double x, int k)
    {
        while (k > 1023)
        {
            x *= 8.98846567431158e307; // 2^1023
            k -= 1023;
        }
        while (k < -1022)
        {
            x *= 2.2250738585072014e-308; // 2^-1022
            k += 1022;
        }
        return x * BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
    }

    private static void Reduce(double x, out double r, out int quadrant)
    {
        double kf = Math.Floor(x * TwoOverPi + 0.5);
        r = ((x - kf * PiO2Part1) - kf * PiO2Part2) - kf * PiO2Part2Tail;
        long k = (long)kf;
        quadrant = (int)(k & 3);
    }

    // sin(r) for |r| <= π/4, Taylor series to r^17.
    private static double SinKernel(double r)
    {
        double r2 = r * r;
        return r * (1.0 - r2 * (1.0 / 6 - r2 * (1.0 / 120 - r2 * (1.0 / 5040 - r2 * (1.0 / 362880
            - r2 * (1.0 / 39916800 - r2 * (1.0 / 6227020800 - r2 * (1.0 / 1307674368000 - r2 * (1.0 / 355687428096000)))))))));
    }

    // cos(r) for |r| <= π/4, Taylor series to r^18.
    private static double CosKernel(double r)
    {
        double r2 = r * r;
        return 1.0 - r2 * (1.0 / 2 - r2 * (1.0 / 24 - r2 * (1.0 / 720 - r2 * (1.0 / 40320 - r2 * (1.0 / 3628800
            - r2 * (1.0 / 479001600 - r2 * (1.0 / 87178291200 - r2 * (1.0 / 20922789888000 - r2 * (1.0 / 6402373705728000)))))))));
    }
}
