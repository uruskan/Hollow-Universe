using System;

namespace Hollow.Net.Bits;

/// <summary>
/// Turning floats into the integers sent on the wire and back. Every conversion is plain IEEE arithmetic with
/// round-half-to-even, so the client and the server get the same numbers from the same input.
/// </summary>
public static class Quantize
{
    /// <summary>Remote entities' positions: 1/256 m (3.9 mm), enough to draw them and small to send.</summary>
    public const float PositionScale = 256f;

    /// <summary>A float in [min, max] as an integer of <paramref name="bits"/> bits, rounded to the nearest step.</summary>
    public static uint ToUnsigned(float value, float min, float max, int bits)
    {
        uint steps = bits >= 32 ? uint.MaxValue : (1u << bits) - 1;
        if (!(value > min))
        {
            return 0;
        }
        if (value >= max)
        {
            return steps;
        }
        return (uint)Math.Round((double)(value - min) / (max - min) * steps, MidpointRounding.ToEven);
    }

    public static float FromUnsigned(uint q, float min, float max, int bits)
    {
        uint steps = bits >= 32 ? uint.MaxValue : (1u << bits) - 1;
        return (float)(min + (double)Math.Min(q, steps) / steps * (max - min));
    }

    /// <summary>An angle in radians as 1/65536 of a turn.</summary>
    public static ushort Angle16(float radians)
    {
        double turns = radians / (2 * Math.PI);
        turns -= Math.Floor(turns);
        return (ushort)((int)Math.Round(turns * 65536, MidpointRounding.ToEven) & 0xFFFF);
    }

    /// <summary>A 16-bit angle in radians, in [−π, π).</summary>
    public static float FromAngle16(ushort angle) => (float)((short)angle * (Math.PI / 32768));

    /// <summary>A length in whole steps of 1/<paramref name="scale"/> (saturates at the int range).</summary>
    public static int Fixed(float value, float scale)
    {
        double scaled = Math.Round((double)value * scale, MidpointRounding.ToEven);
        return scaled >= int.MaxValue ? int.MaxValue : scaled <= int.MinValue ? int.MinValue : (int)scaled;
    }

    public static float FromFixed(int value, float scale) => (float)(value / (double)scale);

    /// <summary>
    /// A unit direction as two 16-bit numbers, octahedral (docs/WHOLE_WORLD.md §5): the sphere folded onto a square, so
    /// every direction is as fine as any other, about 0.004° at worst, with no pole and no seam. A view or a facing on a
    /// whole world, where an angle would need a reference that vanishes somewhere.
    /// </summary>
    public static (ushort U, ushort V) Octahedral(System.Numerics.Vector3 direction)
    {
        double x = direction.X, y = direction.Y, z = direction.Z;
        double sum = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
        if (!(sum > 0) || double.IsInfinity(sum))
        {
            (x, y, z, sum) = (0, 0, 1, 1); // nothing to fold: straight along +Z
        }
        double u = x / sum, v = y / sum;
        if (z < 0)
        {
            (u, v) = ((1 - Math.Abs(v)) * Sign(u), (1 - Math.Abs(u)) * Sign(v));
        }
        return (Unit16(u), Unit16(v));
    }

    /// <summary>The unit direction of <see cref="Octahedral(System.Numerics.Vector3)"/>'s two numbers.</summary>
    public static System.Numerics.Vector3 FromOctahedral(ushort qu, ushort qv)
    {
        double u = qu / 65535.0 * 2 - 1, v = qv / 65535.0 * 2 - 1;
        double z = 1 - Math.Abs(u) - Math.Abs(v);
        if (z < 0)
        {
            (u, v) = ((1 - Math.Abs(v)) * Sign(u), (1 - Math.Abs(u)) * Sign(v));
        }
        double length = Math.Sqrt(u * u + v * v + z * z);
        return new System.Numerics.Vector3((float)(u / length), (float)(v / length), (float)(z / length));
    }

    /// <summary>
    /// A rotation as its largest component's index and the other three, each 16 bits signed over ±1/√2: smallest three,
    /// about 0.003° at worst, with no gimbal lock anywhere (docs/WHOLE_WORLD.md §5: a ship flying any way on a ball).
    /// </summary>
    public static (byte Largest, short A, short B, short C) SmallestThree(System.Numerics.Quaternion q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        double length = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (!(length > 0) || double.IsInfinity(length))
        {
            return (3, 0, 0, 0); // no turn
        }
        Span<double> c = stackalloc double[] { x / length, y / length, z / length, w / length };
        int largest = 0;
        for (int i = 1; i < 4; i++)
        {
            if (Math.Abs(c[i]) > Math.Abs(c[largest]))
            {
                largest = i;
            }
        }
        double sign = c[largest] < 0 ? -1 : 1; // q and −q are the same turn: the largest is sent positive
        Span<short> rest = stackalloc short[3];
        for (int i = 0, k = 0; i < 4; i++)
        {
            if (i != largest)
            {
                rest[k++] = (short)Math.Round(Math.Clamp(c[i] * sign * Math.Sqrt(2), -1, 1) * 32767, MidpointRounding.ToEven);
            }
        }
        return ((byte)largest, rest[0], rest[1], rest[2]);
    }

    /// <summary>The rotation of <see cref="SmallestThree"/>'s numbers (untrusted: always a unit rotation).</summary>
    public static System.Numerics.Quaternion FromSmallestThree(byte largest, short a, short b, short c)
    {
        double ka = a / 32767.0 / Math.Sqrt(2), kb = b / 32767.0 / Math.Sqrt(2), kc = c / 32767.0 / Math.Sqrt(2);
        double big = Math.Sqrt(Math.Max(0, 1 - ka * ka - kb * kb - kc * kc));
        Span<double> q = stackalloc double[4];
        int index = largest & 3;
        for (int i = 0, k = 0; i < 4; i++)
        {
            q[i] = i == index ? big : k++ switch { 0 => ka, 1 => kb, _ => kc };
        }
        double length = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
        return new System.Numerics.Quaternion((float)(q[0] / length), (float)(q[1] / length), (float)(q[2] / length), (float)(q[3] / length));
    }

    private static double Sign(double value) => value < 0 ? -1 : 1;

    private static ushort Unit16(double value) =>
        (ushort)Math.Round((Math.Clamp(value, -1, 1) + 1) * 0.5 * 65535, MidpointRounding.ToEven);
}
