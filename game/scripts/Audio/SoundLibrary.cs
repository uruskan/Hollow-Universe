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
    public const string FootwearPath = "res://data/ground/footsteps.json";

    private static SoundCatalogData? _data;
    private static FootwearAudioData? _footwear;
    private static AudioVoiceBudget? _voiceBudget;
    private static readonly Dictionary<(string Path, bool Loop), AudioStream?> Streams = new();
    // Only these resources are ours. ResourceLoader's original resources may have other users.
    private static readonly HashSet<AudioStream> OwnedModes = new();
    internal static int OwnedModeCount => OwnedModes.Count;

    public static SoundCatalogData Data => _data ??= Load();
    public static FootwearAudioData Footwear => _footwear ??= LoadFootwear();
    public static AudioVoiceBudget VoiceBudget => _voiceBudget ??= LoadVoiceBudget();

    public static SoundEventData? Find(string key) => Data.Find(key);

    /// <summary>Every file the catalogue's events play (a game loads them ahead, GROUND.md §4.3.1).</summary>
    public static IReadOnlyList<string> Files => Data.Events.SelectMany(e => e.AllFiles).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>A file's stream, loaded once; null (and a warning) when it is missing.</summary>
    public static AudioStream? Stream(string path, bool loop = false)
    {
        if (Streams.TryGetValue((path, loop), out AudioStream? cached))
        {
            return cached;
        }
        AudioStream? stream = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
        if (stream == null)
        {
            GD.PushWarning($"Sound file {path} is missing.");
        }
        else
        {
            switch (stream)
            {
                case AudioStreamOggVorbis ogg when ogg.Loop != loop:
                    // A caller may explicitly loop a one-shot. Do not mutate the resource used by ordinary plays.
                    var loopOgg = (AudioStreamOggVorbis)ogg.Duplicate();
                    loopOgg.Loop = loop;
                    stream = loopOgg;
                    OwnedModes.Add(stream);
                    break;
                case AudioStreamWav wav when (wav.LoopMode != AudioStreamWav.LoopModeEnum.Disabled) != loop:
                    var loopWav = (AudioStreamWav)wav.Duplicate();
                    loopWav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                    if (loop && loopWav.LoopEnd <= loopWav.LoopBegin) loopWav.LoopEnd = (int)(loopWav.GetLength() * loopWav.MixRate);
                    stream = loopWav;
                    OwnedModes.Add(stream);
                    break;
            }
        }
        Streams[(path, loop)] = stream;
        return stream;
    }

    /// <summary>Releases mode clones after the last director clears its players; shared imported resources are never disposed here.</summary>
    public static void Release()
    {
        Streams.Clear();
        foreach (AudioStream stream in OwnedModes) stream.Dispose();
        OwnedModes.Clear();
    }

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

    private static FootwearAudioData LoadFootwear()
    {
        if (!FileAccess.FileExists(FootwearPath))
        {
            GD.PushWarning($"{FootwearPath} is missing: using legacy foot contacts.");
            return FootwearAudioData.Fallback;
        }
        try
        {
            return FootwearAudioData.Parse(FileAccess.GetFileAsString(FootwearPath));
        }
        catch (FormatException e)
        {
            GD.PushError(e.Message);
            return FootwearAudioData.Fallback;
        }
    }

    private static AudioVoiceBudget LoadVoiceBudget()
    {
        if (!FileAccess.FileExists(DataPath)) return AudioVoiceBudget.Default;
        try { return AudioVoiceBudget.Parse(FileAccess.GetFileAsString(DataPath)); }
        catch (FormatException e) { GD.PushError(e.Message); return AudioVoiceBudget.Default; }
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
