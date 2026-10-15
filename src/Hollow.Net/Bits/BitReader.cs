using System;
using System.Text;
using Hollow.Core;

namespace Hollow.Net.Bits;

/// <summary>
/// Reads what a <see cref="BitWriter"/> wrote. Packets come from outside, so nothing here throws on bad data: reading
/// past the end, an over-long string or a bad count gives zeros and marks the reader <see cref="Failed"/>. Decode a
/// whole message, then drop it if <see cref="Failed"/>.
/// </summary>
public sealed class BitReader
{
    private byte[] _data = Array.Empty<byte>();
    private int _start;
    private int _lengthBits;
    private int _position;

    public BitReader()
    {
    }

    public BitReader(byte[] data) => Reset(data, 0, data.Length);

    /// <summary>True once anything was read that wasn't there or wasn't allowed.</summary>
    public bool Failed { get; private set; }

    public int BitPosition => _position;

    public int RemainingBits => _lengthBits - _position;

    public void Reset(byte[] data, int offset, int length)
    {
        _data = data;
        _start = offset;
        _lengthBits = Math.Max(0, length) * 8;
        _position = 0;
        Failed = false;
    }

    /// <summary>Marks the data as bad (for checks the caller makes, such as a count out of range).</summary>
    public void Fail() => Failed = true;

    public uint ReadBits(int bits)
    {
        if (bits <= 0)
        {
            return 0;
        }
        if (bits > 32 || _position + bits > _lengthBits)
        {
            Failed = true;
            _position = _lengthBits;
            return 0;
        }
        uint result = 0;
        int got = 0;
        while (got < bits)
        {
            int index = _start + (_position >> 3);
            int offset = _position & 7;
            int take = Math.Min(8 - offset, bits - got);
            uint chunk = (uint)(_data[index] >> offset) & ((1u << take) - 1);
            result |= chunk << got;
            got += take;
            _position += take;
        }
        return result;
    }

    public ulong ReadBits64(int bits)
    {
        if (bits <= 32)
        {
            return ReadBits(bits);
        }
        ulong low = ReadBits(32);
        return low | ((ulong)ReadBits(bits - 32) << 32);
    }

    public bool ReadBool() => ReadBits(1) != 0;

    public int ReadSigned(int bits) => UnZigZag(ReadBits(bits));

    public long ReadSigned64(int bits)
    {
        ulong z = ReadBits64(bits);
        return (long)(z >> 1) ^ -(long)(z & 1);
    }

    public uint ReadVarUInt()
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            uint b = ReadBits(8);
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }
        Failed = true;
        return 0;
    }

    public ulong ReadVarULong()
    {
        ulong value = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            uint b = ReadBits(8);
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }
        Failed = true;
        return 0;
    }

    public float ReadFloat() => BitConverter.UInt32BitsToSingle(ReadBits(32));

    public double ReadDouble() => BitConverter.UInt64BitsToDouble(ReadBits64(64));

    public float ReadQuantized(float min, float max, int bits) => Quantize.FromUnsigned(ReadBits(bits), min, max, bits);

    /// <summary>A difference written by <see cref="BitWriter.WriteDelta"/>.</summary>
    public int ReadDelta()
    {
        uint size = ReadBits(2);
        int bits = size switch
        {
            0 => 5,
            1 => 9,
            2 => 14,
            _ => 32,
        };
        return UnZigZag(ReadBits(bits));
    }

    public string ReadString(int maxBytes)
    {
        uint length = ReadVarUInt();
        if (length > maxBytes || length * 8 > RemainingBits)
        {
            Failed = true;
            return "";
        }
        Span<byte> bytes = stackalloc byte[(int)length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)ReadBits(8);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    public static int UnZigZag(uint z) => (int)(z >> 1) ^ -(int)(z & 1);
}
