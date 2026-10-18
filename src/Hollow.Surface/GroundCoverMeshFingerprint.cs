using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Hollow.Surface;

/// <summary>
/// Versioned exact imported-array identity, not original glTF corner identity. Float bit patterns,
/// stream presence and index order matter. Engine adapters must reject streams they cannot represent.
/// </summary>
public static class GroundCoverMeshFingerprint
{
    public const string Version = "hollow.cover-imported-arrays.v1";
    public const int MaximumVertices = 6000, MaximumTriangles = 6000;

    public static string Compute(ReadOnlySpan<float> positions, ReadOnlySpan<float> normals,
        ReadOnlySpan<float> tangents, ReadOnlySpan<float> colors, ReadOnlySpan<float> uv, ReadOnlySpan<float> uv2,
        ReadOnlySpan<int> indices)
    {
        int vertices = positions.Length / 3;
        if (positions.Length % 3 != 0 || vertices is < 3 or > MaximumVertices
            || indices.Length is < 3 or > MaximumTriangles * 3 || indices.Length % 3 != 0)
            throw new ArgumentException("Unsupported authored-cover vertex or triangle count.");
        Length(normals, vertices * 3, false); Length(tangents, vertices * 4, true);
        Length(colors, vertices * 4, true); Length(uv, vertices * 2, false); Length(uv2, vertices * 2, true);
        foreach (float position in positions)
            if (!float.IsFinite(position) || Math.Abs(position) > 10) throw new ArgumentException("Invalid cover position.");
        foreach (int index in indices) if (index < 0 || index >= vertices) throw new ArgumentException("Cover index outside mesh.");
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(Version));
        Append(hash, positions); Append(hash, normals); Append(hash, tangents); Append(hash, colors);
        Append(hash, uv); Append(hash, uv2);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, indices.Length); hash.AppendData(number);
        foreach (int index in indices) { BinaryPrimitives.WriteInt32LittleEndian(number, index); hash.AppendData(number); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Length(ReadOnlySpan<float> values, int expected, bool optional)
    {
        if (values.Length != expected && !(optional && values.Length == 0)) throw new ArgumentException("Cover stream length mismatch.");
    }
    private static void Append(IncrementalHash hash, ReadOnlySpan<float> values)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, values.Length); hash.AppendData(number);
        foreach (float value in values)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Non-finite cover attribute.");
            BinaryPrimitives.WriteInt32LittleEndian(number, BitConverter.SingleToInt32Bits(value)); hash.AppendData(number);
        }
    }
}
