using System;
using System.Text.Json;

namespace Hollow.Client.Audio;

/// <summary>Optional sounds.json voiceBudget settings. Logical loops remain owned by their live emitters.</summary>
public sealed record AudioVoiceBudget
{
    public int SpatialVoices { get; init; } = 56;
    public int FlatVoices { get; init; } = 20;
    public double UpdateHz { get; init; } = 20;
    public double VirtualizeBelowDb { get; init; } = -60;
    public double HysteresisDb { get; init; } = 2;
    public double PriorityWeightDb { get; init; } = 0.5;

    public static AudioVoiceBudget Default { get; } = new();

    public AudioVoiceBudget Validate()
    {
        if (SpatialVoices is < 0 or > 512 || FlatVoices is < 0 or > 512 ||
            !In(UpdateHz, 5, 60) || !In(VirtualizeBelowDb, -79, -20) || !In(HysteresisDb, 0, 6) || !In(PriorityWeightDb, 0, 2))
            throw new FormatException("sounds.json voiceBudget: invalid pool, loop, update-rate or audibility limit");
        return this;
    }

    private static bool In(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;

    public static AudioVoiceBudget Parse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("sounds.json: expected an object");
            if (!doc.RootElement.TryGetProperty("voiceBudget", out JsonElement b)) return Default;
            if (b.ValueKind != JsonValueKind.Object) throw new FormatException("sounds.json voiceBudget: expected an object");
            return new AudioVoiceBudget
            {
                SpatialVoices = Integer(b, "spatialVoices", 56), FlatVoices = Integer(b, "flatVoices", 20),
                UpdateHz = Number(b, "updateHz", 20),
                VirtualizeBelowDb = Number(b, "virtualizeBelowDb", -60), HysteresisDb = Number(b, "hysteresisDb", 2),
                PriorityWeightDb = Number(b, "priorityWeightDb", 0.5),
            }.Validate();
        }
        catch (JsonException e) { throw new FormatException("sounds.json voiceBudget: malformed JSON", e); }
    }

    private static double Number(JsonElement b, string key, double fallback)
    {
        if (!b.TryGetProperty(key, out JsonElement value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number))
            throw new FormatException($"sounds.json voiceBudget: {key} must be a number");
        return number;
    }

    private static int Integer(JsonElement b, string key, int fallback)
    {
        double value = Number(b, key, fallback);
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new FormatException($"sounds.json voiceBudget: {key} must be an integer");
        return (int)value;
    }
}
