using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>Fields more than one reader shares: lists of numbers as vectors (the deployables' bays, the ships) and a ship's turn axis.</summary>
internal static class ReaderHelpers
{
    /// <summary>Exactly <paramref name="count"/> numbers, or null with the problem noted.</summary>
    public static double[]? Numbers(this JsonFields f, JsonElement e, string name, string where, int count)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out JsonElement v))
        {
            f.Problem(where, $"\"{name}\" is missing");
            return null;
        }
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != count || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number))
        {
            f.Problem(where, $"\"{name}\" must be {count} numbers");
            return null;
        }
        return v.EnumerateArray().Select(x => x.GetDouble()).ToArray();
    }

    /// <summary>Three numbers as a vector (zero with the problem noted when they aren't).</summary>
    public static Vector3 Vector(this JsonFields f, JsonElement o, string name, string where)
    {
        JsonElement[] v = f.List(o, name, where);
        if (v.Length == 3 && v.All(x => x.ValueKind == JsonValueKind.Number))
        {
            return new Vector3((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble());
        }
        if (f.Has(o, name))
        {
            f.Problem(where, $"\"{name}\" must be three numbers");
        }
        return Vector3.Zero;
    }

    /// <summary>A list of vectors, each three numbers.</summary>
    public static Vector3[] Vectors(this JsonFields f, JsonElement o, string name, string where, bool required = true)
    {
        var result = new List<Vector3>();
        foreach (JsonElement v in f.List(o, name, where, required))
        {
            if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
            {
                result.Add(new Vector3((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble()));
            }
            else
            {
                f.Problem(where, $"each of \"{name}\" is three numbers");
            }
        }
        return result.ToArray();
    }

    /// <summary>A ship's turn axis, [most rate °/s, acceleration °/s²], in radians.</summary>
    public static Ships.TurnAxis Turn(this JsonFields f, JsonElement o, string name, string where)
    {
        JsonElement[] v = f.List(o, name, where);
        if (v.Length == 2 && v.All(x => x.ValueKind == JsonValueKind.Number))
        {
            const float degrees = MathF.PI / 180;
            return new Ships.TurnAxis((float)v[0].GetDouble() * degrees, (float)v[1].GetDouble() * degrees);
        }
        f.Problem(where, $"\"{name}\" is [most rate °/s, acceleration °/s²]");
        return default;
    }
}
