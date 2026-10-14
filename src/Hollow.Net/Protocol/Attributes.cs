using System;

namespace Hollow.Net.Protocol;

// The vocabulary of message definitions (Messages.cs). tools/Hollow.NetGen reads the definitions as text and writes
// their serializers into Messages.g.cs; these attributes exist so the definitions compile and say what they mean.
// See ARCHITECTURE.md §11.2.

/// <summary>A top-level message with a type ID, sent as its own packet or packet payload.</summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class NetMessageAttribute(byte id) : Attribute
{
    public byte Id { get; } = id;
}

/// <summary>A struct written inside messages.</summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class NetStructAttribute : Attribute
{
}

/// <summary>An entity's replicated state: also gets <c>WriteDelta</c> and <c>ReadDelta</c> against a baseline.</summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class NetStateAttribute : Attribute
{
}

/// <summary>An unsigned integer or enum in this many bits.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class BitsAttribute(int bits) : Attribute
{
    public int Bits { get; } = bits;
}

/// <summary>A signed integer in this many bits (zigzag); in a delta, the difference in as few bits as fit.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SignedAttribute(int bits) : Attribute
{
    public int Bits { get; } = bits;
}

/// <summary>An unsigned integer in 7-bit groups: small values are small.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class VarIntAttribute : Attribute
{
}

/// <summary>A <c>ushort</c> angle (1/65536 turn); in a delta, the wrapped difference.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AngleAttribute : Attribute
{
}

/// <summary>A float clamped to [min, max] in this many bits.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class QuantizeAttribute(float min, float max, int bits) : Attribute
{
    public float Min { get; } = min;
    public float Max { get; } = max;
    public int Bits { get; } = bits;
}

/// <summary>A string of at most this many UTF-8 bytes.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class MaxLengthAttribute(int bytes) : Attribute
{
    public int Bytes { get; } = bytes;
}

/// <summary>An array of at most this many elements.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class MaxCountAttribute(int count) : Attribute
{
    public int Count { get; } = count;
}
