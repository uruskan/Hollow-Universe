using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Animals;

/// <summary>One of a model's clips, as art/animals/build_animals.py made it.</summary>
public sealed record AnimalClip(string Name, float Seconds, bool Loop, float Speed, float Hit);

/// <summary>
/// A species' model (GROUND.md §6.9): the scene (skinned body in three levels of detail, its skeleton and clips) and
/// the facts written beside it: each clip's length, whether it loops, the speed a gait was made for (so a view plays
/// it at speed ÷ that and the feet keep up with the ground), when an attack lands, and the body's size.
/// </summary>
public sealed class AnimalModel
{
    public required string Key { get; init; }

    public required PackedScene Scene { get; init; }

    public required IReadOnlyDictionary<string, AnimalClip> Clips { get; init; }

    public float Length { get; init; }

    public float Height { get; init; }

    /// <summary>The head at rest: metres ahead of the feet's middle, and up.</summary>
    public Vector2 Head { get; init; }

    public AnimalClip? Clip(string name) => Clips.GetValueOrDefault(name);

    /// <summary>A gait's native speed (m/s), or a fallback when the facts don't say.</summary>
    public float GaitSpeed(string gait, float fallback) => Clip(gait) is { Speed: > 0 } c ? c.Speed : fallback;
}

/// <summary>Loads each species' model once (res://assets/animals/&lt;key&gt;.glb and .json); null for one there is none of.</summary>
public static class AnimalAssets
{
    public const string Folder = "res://assets/animals/";

    private static readonly Dictionary<string, AnimalModel?> Models = new(StringComparer.Ordinal);

    public static AnimalModel? Get(string key)
    {
        if (Models.TryGetValue(key, out AnimalModel? known))
        {
            return known;
        }
        AnimalModel? model = Load(key);
        Models[key] = model;
        return model;
    }

    private static AnimalModel? Load(string key)
    {
        string scenePath = Folder + key + ".glb";
        if (!ResourceLoader.Exists(scenePath))
        {
            GD.PushWarning($"No animal model {scenePath}.");
            return null;
        }
        var scene = ResourceLoader.Load<PackedScene>(scenePath);
        var clips = new Dictionary<string, AnimalClip>(StringComparer.Ordinal);
        float length = 1, height = 1;
        Vector2 head = new(0.5f, 1);
        string factsPath = Folder + key + ".json";
        if (FileAccess.FileExists(factsPath))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(factsPath));
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("clips", out JsonElement c))
                {
                    foreach (JsonProperty p in c.EnumerateObject())
                    {
                        JsonElement v = p.Value;
                        clips[p.Name] = new AnimalClip(p.Name,
                            v.TryGetProperty("seconds", out JsonElement s) ? s.GetSingle() : 1,
                            v.TryGetProperty("loop", out JsonElement l) && l.GetBoolean(),
                            v.TryGetProperty("speed", out JsonElement sp) ? sp.GetSingle() : 0,
                            v.TryGetProperty("hit", out JsonElement h) ? h.GetSingle() : 0);
                    }
                }
                length = root.TryGetProperty("length", out JsonElement len) ? len.GetSingle() : length;
                height = root.TryGetProperty("height", out JsonElement hi) ? hi.GetSingle() : height;
                if (root.TryGetProperty("head", out JsonElement hd) && hd.GetArrayLength() == 2)
                {
                    head = new Vector2(hd[0].GetSingle(), hd[1].GetSingle());
                }
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
            {
                GD.PushWarning($"{factsPath}: {e.Message}");
            }
        }
        return new AnimalModel { Key = key, Scene = scene, Clips = clips, Length = length, Height = height, Head = head };
    }
}
