using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Hollow.Core;

namespace Hollow.Tests;

/// <summary>
/// Golden hashes over generated objects. Hashes every stored field (public or private, including the fields behind
/// auto-properties), recursively, in name order, so a field added to a body later is pinned without touching the
/// tests. Computed properties are skipped: they derive from stored fields. Sequences hash their count and elements.
/// </summary>
public static class Golden
{
    public static ulong Of(object? value)
    {
        return Combine(0x5EED_601DUL, value, 0);
    }

    private static ulong Combine(ulong h, object? value, int depth)
    {
        if (depth > 16)
        {
            throw new InvalidOperationException("Golden hash: object graph too deep (a cycle?).");
        }
        switch (value)
        {
            case null:
                return Hash64.Combine(h, 0xA11_0000_0000_0001UL);
            case string s:
                return Hash64.Combine(h, Hash64.Of(s));
            case bool b:
                return Hash64.Combine(h, b ? 1UL : 0UL);
            case double d:
                return Hash64.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(d));
            case float f:
                return Hash64.Combine(h, BitConverter.SingleToUInt32Bits(f));
            case Enum e:
                return Hash64.Combine(h, Convert.ToUInt64(e, System.Globalization.CultureInfo.InvariantCulture));
            case ulong u:
                return Hash64.Combine(h, u);
            case byte or sbyte or short or ushort or int or uint or long or char:
                return Hash64.Combine(h, unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)));
            case IEnumerable sequence:
            {
                ulong count = 0;
                ulong inner = 0;
                foreach (object? item in sequence)
                {
                    inner = Combine(inner, item, depth + 1);
                    count++;
                }
                return Hash64.Combine(Hash64.Combine(h, count), inner);
            }
        }
        Type type = value.GetType();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            h = Combine(h, field.GetValue(value), depth + 1);
        }
        return h;
    }
}
