using System;
using System.Text;
using Hollow.Core;

namespace Hollow.Net.Bits;

/// <summary>
/// Packs values into a byte buffer bit by bit, least significant bit first (GROUND.md §4.4). Reuse one per thread or
/// connection: <see cref="Reset"/> keeps the buffer. The generated serializers (<c>Messages.g.cs</c>) call it.
/// </summary>
public sealed class BitWriter
{
    private byte[] _buffer;
    private ulong _scratch;
    private int _scratchBits;
    private int _bytes;

    public BitWriter(int capacity = 1500)
    {
        _buffer = new byte[Math.Max(16, capacity)];
    }

    /// <summary>Bits written so far.</summary>
    public int BitCount => _bytes * 8 + _scratchBits;

    /// <summary>Bytes the data takes (the last one may be partly used).</summary>
    public int ByteCount => (BitCount + 7) >> 3;

    public void Reset()
    {
        _scratch = 0;
        _scratchBits = 0;
        _bytes = 0;
    }

    /// <summary>The bytes written so far. Valid until the next write.</summary>
    public ReadOnlySpan<byte> Data
    {
        get
        {
            Reserve(1);
            if (_scratchBits > 0)
            {
                _buffer[_bytes] = (byte)_scratch;
            }
            return _buffer.AsSpan(0, ByteCount);
        }
    }

    public byte[] ToArray() => Data.ToArray();

    /// <summary>The low <paramref name="bits"/> bits of <paramref name="value"/> (0 to 32).</summary>
    public void WriteBits(uint value, int bits)
    {
        if (bits <= 0)
        {
            return;
        }
        ulong masked = bits >= 32 ? value : value & ((1u << bits) - 1);
        _scratch |= masked << _scratchBits;
        _scratchBits += bits;
        while (_scratchBits >= 8)
        {
            Reserve(1);
            _buffer[_bytes++] = (byte)_scratch;
            _scratch >>= 8;
            _scratchBits -= 8;
        }
    }

    /// <summary>The low <paramref name="bits"/> bits of a 64-bit value (0 to 64).</summary>
    public void WriteBits64(ulong value, int bits)
    {
        if (bits <= 32)
        {
            WriteBits((uint)value, bits);
            return;
        }
        WriteBits((uint)value, 32);
        WriteBits((uint)(value >> 32), bits - 32);
    }

    public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

    /// <summary>A signed value in <paramref name="bits"/> bits, zigzag-coded (small magnitudes either way are small).</summary>
    public void WriteSigned(int value, int bits) => WriteBits(ZigZag(value), bits);

    public void WriteSigned64(long value, int bits) => WriteBits64((ulong)((value << 1) ^ (value >> 63)), bits);

    /// <summary>An unsigned value in 7-bit groups, each followed by a "more" bit: 8 bits below 128.</summary>
    public void WriteVarUInt(uint value)
    {
        while (value >= 0x80)
        {
            WriteBits((value & 0x7F) | 0x80, 8);
            value >>= 7;
        }
        WriteBits(value, 8);
    }

    public void WriteVarULong(ulong value)
    {
        while (value >= 0x80)
        {
            WriteBits((uint)(value & 0x7F) | 0x80, 8);
            value >>= 7;
        }
        WriteBits((uint)value, 8);
    }

    public void WriteFloat(float value) => WriteBits(BitConverter.SingleToUInt32Bits(value), 32);

    public void WriteDouble(double value) => WriteBits64(BitConverter.DoubleToUInt64Bits(value), 64);

    /// <summary>A float clamped to [min, max] in <paramref name="bits"/> bits (<see cref="Quantize"/>).</summary>
    public void WriteQuantized(float value, float min, float max, int bits) =>
        WriteBits(Quantize.ToUnsigned(value, min, max, bits), bits);

    /// <summary>
    /// A difference between two integers in as few bits as fit it: a 2-bit size class, then the zigzag-coded value in
    /// 5, 9, 14 or 32 bits. A walking character's position changes by 9 to 11 bits per axis per snapshot.
    /// </summary>
    public void WriteDelta(int delta)
    {
        uint z = ZigZag(delta);
        if (z < 1u << 5)
        {
            WriteBits(0, 2);
            WriteBits(z, 5);
        }
        else if (z < 1u << 9)
        {
            WriteBits(1, 2);
            WriteBits(z, 9);
        }
        else if (z < 1u << 14)
        {
            WriteBits(2, 2);
            WriteBits(z, 14);
        }
        else
        {
            WriteBits(3, 2);
            WriteBits(z, 32);
        }
    }

    /// <summary>UTF-8 text of at most <paramref name="maxBytes"/> bytes (longer text is cut at a character boundary).</summary>
    public void WriteString(string? value, int maxBytes)
    {
        value ??= "";
        Span<byte> bytes = stackalloc byte[Math.Min(Encoding.UTF8.GetMaxByteCount(value.Length), 4096)];
        int length = Encoding.UTF8.GetBytes(value.AsSpan(0, Math.Min(value.Length, bytes.Length / 3)), bytes);
        if (length > maxBytes)
        {
            length = maxBytes;
            // Don't end inside a multi-byte character.
            while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            {
                length--;
            }
        }
        WriteVarUInt((uint)length);
        for (int i = 0; i < length; i++)
        {
            WriteBits(bytes[i], 8);
        }
    }

    /// <summary>The bits <see cref="WriteDelta"/> takes for a value.</summary>
    public static int DeltaBits(int delta)
    {
        uint z = ZigZag(delta);
        return z < 1u << 5 ? 7 : z < 1u << 9 ? 11 : z < 1u << 14 ? 16 : 34;
    }

    /// <summary>Appends everything another writer holds.</summary>
    public void WriteFrom(BitWriter other)
    {
        ReadOnlySpan<byte> data = other.Data;
        int whole = other.BitCount >> 3;
        for (int i = 0; i < whole; i++)
        {
            WriteBits(data[i], 8);
        }
        int rest = other.BitCount & 7;
        if (rest > 0)
        {
            WriteBits(data[whole], rest);
        }
    }

    public static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

    private void Reserve(int bytes)
    {
        if (_bytes + bytes > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _bytes + bytes));
        }
    }
}
