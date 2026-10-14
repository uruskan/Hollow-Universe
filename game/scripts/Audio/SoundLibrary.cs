using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client.Audio;

namespace HollowUniverse.Audio;

/// <summary>
/// The sound catalogue in the game (GROUND.md §4.9): <c>res://data/ground/sounds.json</c> read once, its streams
/// loaded on first use and kept. Loop events' streams are set to loop here too, whatever their import says.
/// </summary>
public static class SoundLibrary
{
    public const string DataPath = "res://data/ground/sounds.json";
    public const string LevelsPath = "res://assets/audio/levels.json";

    private static SoundCatalogData? _data;
    private static readonly Dictionary<string, AudioStream?> Streams = new(StringComparer.Ordinal);

    public static SoundCatalogData Data => _data ??= Load();

    public static SoundEventData? Find(string key) => Data.Find(key);

    /// <summary>Every file the catalogue's events play (a game loads them ahead, GROUND.md §4.3.1).</summary>
    public static IReadOnlyList<string> Files => Data.Events.SelectMany(e => e.AllFiles).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>A file's stream, loaded once; null (and a warning) when it is missing.</summary>
    public static AudioStream? Stream(string path, bool loop = false)
    {
        if (Streams.TryGetValue(path, out AudioStream? cached))
        {
            return cached;
        }
        AudioStream? stream = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
        if (stream == null)
        {
            GD.PushWarning($"Sound file {path} is missing.");
        }
        else if (loop)
        {
            switch (stream)
            {
                case AudioStreamOggVorbis ogg:
                    ogg.Loop = true;
                    break;
                case AudioStreamWav wav:
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                    break;
            }
        }
        Streams[path] = stream;
        return stream;
    }

    /// <summary>Lets go of the loaded streams (the director does when it leaves the tree).</summary>
    public static void Release() => Streams.Clear();

    /// <summary>Loads every file of these events now, so their first play doesn't wait on the disk.</summary>
    public static void Preload(IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            if (Find(key) is SoundEventData e)
            {
                foreach (string f in e.AllFiles)
                {
                    Stream(f, e.Loop);
                }
            }
        }
    }

    private static SoundCatalogData Load()
    {
        if (!FileAccess.FileExists(DataPath))
        {
            GD.PushError($"{DataPath} is missing: no sounds.");
            return SoundCatalogData.Parse("{\"events\": []}");
        }
        try
        {
            return SoundCatalogData.Parse(FileAccess.GetFileAsString(DataPath));
        }
        catch (FormatException e)
        {
            GD.PushError(e.Message);
            return SoundCatalogData.Parse("{\"events\": []}");
        }
    }
}

/// <summary>Picks variants in shuffled rounds, so none repeats twice in a row and all come up evenly.</summary>
public sealed class VariantBag
{
    private readonly int _count;
    private readonly List<int> _left = new();
    private int _last = -1;

    public VariantBag(int count) => _count = Math.Max(1, count);

    public int Next(Random rng)
    {
        if (_count == 1)
        {
            return 0;
        }
        if (_left.Count == 0)
        {
            for (int i = 0; i < _count; i++)
            {
                _left.Add(i);
            }
            for (int i = _left.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (_left[i], _left[j]) = (_left[j], _left[i]);
            }
            // A new round never starts with the variant that ended the last one.
            if (_left[^1] == _last)
            {
                (_left[0], _left[^1]) = (_left[^1], _left[0]);
            }
        }
        int pick = _left[^1];
        _left.RemoveAt(_left.Count - 1);
        _last = pick;
        return pick;
    }
}
