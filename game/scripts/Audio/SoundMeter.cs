using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Audio;

/// <summary>
/// Loudness of the game's own sound files, measured in the engine: each stream is decoded through Godot
/// (<see cref="AudioStreamPlayback.MixAudio"/>, at the mix rate) and metered by <see cref="Loudness"/>. The audition
/// viewer shows it beside the pipeline's measurement (<c>res://assets/audio/levels.json</c>), and
/// <c>--view sounds --sound-check</c> compares the two for every file.
/// </summary>
public static class SoundMeter
{
    private static Dictionary<string, (double Event, double Integrated, double TruePeak, string Category)>? _levels;

    /// <summary>What the pipeline measured for a file (res:// path), if anything.</summary>
    public static (double Event, double Integrated, double TruePeak, string Category)? Pipeline(string path)
    {
        _levels ??= LoadLevels();
        string rel = path.StartsWith("res://assets/audio/", StringComparison.Ordinal) ? path["res://assets/audio/".Length..] : path;
        return _levels.TryGetValue(rel, out var v) ? v : null;
    }

    /// <summary>Decodes a stream through Godot and measures it.</summary>
    public static LoudnessResult Measure(AudioStream stream)
    {
        int rate = (int)AudioServer.GetMixRate();
        AudioStreamPlayback playback = stream.InstantiatePlayback();
        long total = (long)Math.Ceiling(stream.GetLength() * rate);
        var left = new List<float>((int)Math.Min(total, 1 << 24));
        var right = new List<float>((int)Math.Min(total, 1 << 24));
        playback.Start(0);
        while (left.Count < total)
        {
            Vector2[] frames = playback.MixAudio(1f, (int)Math.Min(4096, total - left.Count));
            if (frames.Length == 0)
            {
                break;
            }
            foreach (Vector2 f in frames)
            {
                left.Add(f.X);
                right.Add(f.Y);
            }
        }
        playback.Stop();
        bool mono = true;
        for (int i = 0; i < left.Count && mono; i++)
        {
            mono = Math.Abs(left[i] - right[i]) < 1e-6f;
        }
        var channels = mono ? new[] { left.ToArray() } : new[] { left.ToArray(), right.ToArray() };
        return Loudness.Measure(channels, rate);
    }

    private static Dictionary<string, (double, double, double, string)> LoadLevels()
    {
        var map = new Dictionary<string, (double, double, double, string)>(StringComparer.Ordinal);
        if (!FileAccess.FileExists(SoundLibrary.LevelsPath))
        {
            return map;
        }
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(SoundLibrary.LevelsPath));
        if (!doc.RootElement.TryGetProperty("files", out JsonElement files))
        {
            return map;
        }
        foreach (JsonProperty f in files.EnumerateObject())
        {
            JsonElement v = f.Value;
            map[f.Name] = (v.GetProperty("event").GetDouble(), v.GetProperty("integrated").GetDouble(),
                v.GetProperty("truePeak").GetDouble(), v.GetProperty("category").GetString() ?? "");
        }
        return map;
    }
}
