using System;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>A small, fast random source for effects (xorshift); seeded, so screenshots repeat.</summary>
public sealed class FxRandom
{
    private ulong _state;

    public FxRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public void Reseed(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    /// <summary>0 (inclusive) to 1 (exclusive).</summary>
    public float Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return (float)((_state >> 40) * (1.0 / (1UL << 24)));
    }

    public float Range(float min, float max) => min + (max - min) * Next();

    public float Range(Vector2 range) => range.X + (range.Y - range.X) * Next();

    /// <summary>A whole count in [min, max], the fraction of a non-whole maximum taken by chance.</summary>
    public int Count(Vector2 range, float scale = 1f)
    {
        float n = Range(range) * scale;
        int whole = (int)MathF.Floor(n);
        return whole + (Next() < n - whole ? 1 : 0);
    }

    public Vector3 UnitVector()
    {
        float z = Range(-1f, 1f);
        float a = Range(0f, MathF.Tau);
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(r * MathF.Cos(a), z, r * MathF.Sin(a));
    }

    /// <summary>A direction within <paramref name="degrees"/> of <paramref name="axis"/>, spread evenly over the cap.</summary>
    public Vector3 InCone(Vector3 axis, float degrees)
    {
        Vector3 n = axis.LengthSquared() > 1e-10f ? axis.Normalized() : Vector3.Up;
        float cosMax = MathF.Cos(Mathf.DegToRad(Math.Clamp(degrees, 0f, 180f)));
        float z = Range(cosMax, 1f);
        float a = Range(0f, MathF.Tau);
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        (Vector3 t, Vector3 b) = Frame(n);
        return (t * (r * MathF.Cos(a)) + b * (r * MathF.Sin(a)) + n * z).Normalized();
    }

    /// <summary>Two directions at right angles to <paramref name="n"/> and each other.</summary>
    public static (Vector3 T, Vector3 B) Frame(Vector3 n)
    {
        Vector3 t = MathF.Abs(n.Y) < 0.95f ? Vector3.Up.Cross(n).Normalized() : Vector3.Right.Cross(n).Normalized();
        return (t, n.Cross(t));
    }

    public Basis Turn() => new Basis(UnitVector(), Range(0f, MathF.Tau));

    /// <summary>A stable hash of a string (FNV-1a), the same in every run.</summary>
    public static ulong Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h = (h ^ c) * 1099511628211UL;
        }
        return h;
    }
}
