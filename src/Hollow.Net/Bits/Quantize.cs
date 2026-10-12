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
}
