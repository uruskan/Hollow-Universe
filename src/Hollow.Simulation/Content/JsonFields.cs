using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content;

/// <summary>
/// Reads fields of the content files and notes every problem (a missing field, a wrong type, an unknown name) with
/// where it is, instead of stopping at the first: <see cref="GameContent.Problems"/> lists them all.
/// </summary>
internal sealed class JsonFields
{
    private readonly List<string> _problems;

    public JsonFields(List<string> problems)
    {
        _problems = problems;
    }

    public void Problem(string where, string what) => _problems.Add($"{where}: {what}");

    /// <summary>Notes properties the object has that aren't in <paramref name="known"/> (usually a typo).</summary>
    public void OnlyKnown(JsonElement o, string where, params string[] known)
    {
        if (o.ValueKind != JsonValueKind.Object)
        {
            Problem(where, "expected an object");
            return;
        }
        foreach (JsonProperty p in o.EnumerateObject())
        {
            if (Array.IndexOf(known, p.Name) < 0)
            {
                Problem(where, $"unknown field \"{p.Name}\" (known: {string.Join(", ", known)})");
            }
        }
    }

    public bool Has(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v) && v.ValueKind != JsonValueKind.Null;

    public string Text(JsonElement o, string name, string where, string? fallback = null)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.String)
            {
                return v.GetString() ?? "";
            }
            Problem(where, $"\"{name}\" must be text");
            return fallback ?? "";
        }
        if (fallback == null)
        {
            Problem(where, $"\"{name}\" is missing");
        }
        return fallback ?? "";
    }

    public float Number(JsonElement o, string name, string where, float? fallback = null)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d))
            {
                return (float)d;
            }
            Problem(where, $"\"{name}\" must be a number");
            return fallback ?? 0;
        }
        if (fallback == null)
        {
            Problem(where, $"\"{name}\" is missing");
        }
        return fallback ?? 0;
    }

    /// <summary>A number at full precision (the weapons' recoil patterns and cadences).</summary>
    public double Double(JsonElement o, string name, string where, double? fallback = null)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d))
            {
                return d;
            }
            Problem(where, $"\"{name}\" must be a number");
            return fallback ?? 0;
        }
        if (fallback == null)
        {
            Problem(where, $"\"{name}\" is missing");
        }
        return fallback ?? 0;
    }

    public int Integer(JsonElement o, string name, string where, int? fallback = null)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i))
            {
                return i;
            }
            Problem(where, $"\"{name}\" must be a whole number");
            return fallback ?? 0;
        }
        if (fallback == null)
        {
            Problem(where, $"\"{name}\" is missing");
        }
        return fallback ?? 0;
    }

    public bool Flag(JsonElement o, string name, string where, bool fallback)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
        {
            if (v.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return v.GetBoolean();
            }
            Problem(where, $"\"{name}\" must be true or false");
        }
        return fallback;
    }

    public bool TryChild(JsonElement o, string name, out JsonElement child)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out child) && child.ValueKind == JsonValueKind.Object)
        {
            return true;
        }
        child = default;
        return false;
    }

    public JsonElement[] List(JsonElement o, string name, string where, bool required = true)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
        {
            if (v.ValueKind == JsonValueKind.Array)
            {
                return v.EnumerateArray().ToArray();
            }
            Problem(where, $"\"{name}\" must be a list");
            return Array.Empty<JsonElement>();
        }
        if (required)
        {
            Problem(where, $"\"{name}\" is missing");
        }
        return Array.Empty<JsonElement>();
    }

    public string[] Texts(JsonElement o, string name, string where)
    {
        var result = new List<string>();
        foreach (JsonElement e in List(o, name, where, required: false))
        {
            if (e.ValueKind == JsonValueKind.String)
            {
                result.Add(e.GetString() ?? "");
            }
            else
            {
                Problem(where, $"\"{name}\" must hold text");
            }
        }
        return result.ToArray();
    }

    public int[] Integers(JsonElement o, string name, string where)
    {
        var result = new List<int>();
        foreach (JsonElement e in List(o, name, where, required: false))
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int i))
            {
                result.Add(i);
            }
            else
            {
                Problem(where, $"\"{name}\" must hold whole numbers");
            }
        }
        return result.ToArray();
    }

    /// <summary>A tuple such as <c>["wood", 200]</c> or <c>["arrow", 4, 12, 1.5]</c>: text, then numbers.</summary>
    public bool Tuple(JsonElement e, string where, int numbers, out string key, out double[] values, int optional = 0)
    {
        key = "";
        values = new double[numbers + optional];
        int length = e.ValueKind == JsonValueKind.Array ? e.GetArrayLength() : -1;
        if (length < 1 + numbers || length > 1 + numbers + optional || e[0].ValueKind != JsonValueKind.String)
        {
            Problem(where, $"expected [\"key\"{string.Concat(Enumerable.Repeat(", number", numbers))}], found {e.GetRawText()}");
            return false;
        }
        key = e[0].GetString() ?? "";
        for (int i = 0; i < length - 1; i++)
        {
            if (e[i + 1].ValueKind != JsonValueKind.Number || !e[i + 1].TryGetDouble(out double d) || !double.IsFinite(d))
            {
                Problem(where, $"{e.GetRawText()}: entry {i + 2} must be a number");
                return false;
            }
            values[i] = d;
        }
        return true;
    }

    public static string Quote(string text) => "\"" + text + "\"";

    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
