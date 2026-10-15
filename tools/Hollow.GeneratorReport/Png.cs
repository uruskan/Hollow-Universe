using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Hollow.GeneratorReport;

/// <summary>A minimal PNG writer (8-bit RGB, Paeth-filtered rows): enough for the generator's maps.</summary>
public static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Writes an RGB image; <paramref name="rgb"/> holds width × height × 3 bytes, rows top to bottom.</summary>
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        using FileStream file = File.Create(path);
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB
        Chunk(file, "IHDR", header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            // Every row with the Paeth filter (type 4): each byte as the difference from its predicted value, which
            // smooth maps compress far better than raw bytes.
            int stride = width * 3;
            var row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                int start = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= 3 ? rgb[start + x - 3] : 0;
                    int b = y > 0 ? rgb[start - stride + x] : 0;
                    int c = x >= 3 && y > 0 ? rgb[start - stride + x - 3] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    int predicted = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                    row[x] = (byte)(rgb[start + x] - predicted);
                }
                zlib.WriteByte(4);
                zlib.Write(row, 0, stride);
            }
        }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = 0xFFFFFFFFu;
        crc = Update(crc, typeBytes);
        crc = Update(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFFu);
        output.Write(crcBytes);
    }

    private static uint Update(uint crc, byte[] data)
    {
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
