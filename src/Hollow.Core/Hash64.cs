namespace Hollow.Core;

/// <summary>
/// Fixed 64-bit hashing for seeds and streams. Unlike <see cref="System.HashCode"/> and
/// <see cref="string.GetHashCode()"/>, which are randomised per process, these values never change.
/// </summary>
public static class Hash64
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;
    private const ulong Start = 0x6A09E667F3BCC908UL;

    /// <summary>The SplitMix64 finaliser: a bijective, well-mixed scramble of 64 bits.</summary>
    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Folds <paramref name="value"/> into <paramref name="hash"/>. Order matters.</summary>
    public static ulong Combine(ulong hash, ulong value) => Mix(hash ^ Mix(value + Golden));

    public static ulong Of(ulong a) => Combine(Start, a);

    public static ulong Of(ulong a, ulong b) => Combine(Combine(Start, a), b);

    public static ulong Of(ulong a, ulong b, ulong c) => Combine(Of(a, b), c);

    public static ulong Of(ulong a, ulong b, ulong c, ulong d) => Combine(Of(a, b, c), d);

    /// <summary>FNV-1a over the string's UTF-16 code units, then mixed. Stable forever.</summary>
    public static ulong Of(string text)
    {
        ulong h = 0xCBF29CE484222325UL;
        foreach (char c in text)
        {
            h ^= c;
            h *= 0x100000001B3UL;
        }
        return Mix(h);
    }
}
