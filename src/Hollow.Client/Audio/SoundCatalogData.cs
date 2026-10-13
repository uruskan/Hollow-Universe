using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hollow.Client.Audio;

/// <summary>How loud the files of one category are made (GROUND.md §4.9): a metric, a target and a range in LUFS.</summary>
/// <param name="Metric">"event" (a one-shot over its own length) or "integrated" (a loop).</param>
public sealed record SoundCategory(string Key, string Metric, double Target, double Min, double Max);

/// <summary>One distance layer of a layered event (gunshots, explosions): played between <see cref="From"/> and
/// <see cref="To"/> metres from the listener, crossfading over <see cref="Fade"/> metres at each end.</summary>
public sealed class SoundLayer
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public double From { get; init; }
    public double To { get; init; } = double.PositiveInfinity;
    public double Fade { get; init; } = 10;
    public double Volume { get; init; }
    public double UnitSize { get; init; } = 4;
    public double MaxDistance { get; init; } = 100;
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
}

/// <summary>One sound event of the catalogue (game/data/ground/sounds.json): what plays, how loud, how far, where.</summary>
public sealed class SoundEventData
{
    public string Key { get; init; } = "";
    public string Category { get; init; } = "";

    /// <summary>Master, SFX, UI, Ambience or Footsteps.</summary>
    public string Bus { get; init; } = "SFX";

    /// <summary>The variants (res:// paths); empty for a layered event.</summary>
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

    /// <summary>dB added to every variant.</summary>
    public double Volume { get; init; }

    public double PitchMin { get; init; } = 1;
    public double PitchMax { get; init; } = 1;

    /// <summary>Random volume, ± dB.</summary>
    public double Jitter { get; init; }

    /// <summary>Metres at which the inverse-distance falloff starts (Godot's unit size).</summary>
    public double UnitSize { get; init; } = 4;

    /// <summary>Metres beyond which it isn't heard (not started at all).</summary>
    public double MaxDistance { get; init; } = 60;

    /// <summary>0–100; a new sound takes a voice from one of lower priority when the pool is full.</summary>
    public int Priority { get; init; } = 50;

    /// <summary>Most voices of this event at once; the oldest is cut for a new one.</summary>
    public int Limit { get; init; } = 4;

    public bool Loop { get; init; }

    /// <summary>False for interface and ambience beds: not placed in the world.</summary>
    public bool Positional { get; init; } = true;

    /// <summary>Muffled (low-passed) when something solid is between it and the listener.</summary>
    public bool Occlude { get; init; } = true;

    /// <summary>Starts after the time sound takes to travel to the listener (343 m/s).</summary>
    public bool Delay { get; init; }

    /// <summary>Seconds after one play during which another is ignored (hover spam).</summary>
    public double Cooldown { get; init; }

    public IReadOnlyList<SoundLayer> Layers { get; init; } = Array.Empty<SoundLayer>();

    public bool Layered => Layers.Count > 0;

    /// <summary>Every file the event can play.</summary>
    public IEnumerable<string> AllFiles => Files.Concat(Layers.SelectMany(l => l.Files));
}

/// <summary>
/// The sound catalogue without Godot (GROUND.md §4.9): parsed from sounds.json, shared by the game's
/// <c>AudioDirector</c> and the tests. Unknown fields are ignored; missing ones take their defaults.
/// </summary>
public sealed class SoundCatalogData
{
    public const string FileName = "sounds.json";

    private SoundCatalogData(Dictionary<string, SoundCategory> categories, List<SoundEventData> events,
        Dictionary<string, IReadOnlyDictionary<string, string>> items)
    {
        Categories = categories;
        Events = events;
        Items = items;
        ByKey = events.ToDictionary(e => e.Key, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, SoundCategory> Categories { get; }

    /// <summary>Item keys (and building.json's door kinds) → role ("swing", "shot", "open"...) → event key.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Items { get; }

    /// <summary>The event an item plays for a role ("stone-hatchet", "swing" → "melee.swing.heavy"), if it has one.</summary>
    public string? ItemEvent(string item, string role) =>
        Items.TryGetValue(item, out IReadOnlyDictionary<string, string>? roles) && roles.TryGetValue(role, out string? key)
            ? key
            : null;
    public IReadOnlyList<SoundEventData> Events { get; }
    public IReadOnlyDictionary<string, SoundEventData> ByKey { get; }

    public SoundEventData? Find(string key) => ByKey.TryGetValue(key, out SoundEventData? e) ? e : null;

    public static SoundCatalogData Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses the catalogue; lists every problem at once (a duplicate key, an event without files...).</summary>
    public static SoundCatalogData Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        JsonElement root = doc.RootElement;
        var problems = new List<string>();
        var categories = new Dictionary<string, SoundCategory>(StringComparer.Ordinal);
        if (root.TryGetProperty("categories", out JsonElement cats) && cats.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty c in cats.EnumerateObject())
            {
                categories[c.Name] = new SoundCategory(c.Name, Str(c.Value, "metric", "event"), Num(c.Value, "target", -23),
                    Num(c.Value, "min", -100), Num(c.Value, "max", 0));
            }
        }
        var events = new List<SoundEventData>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("events", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in list.EnumerateArray())
            {
                string key = Str(e, "key", "");
                if (key.Length == 0)
                {
                    problems.Add("an event has no key");
                    continue;
                }
                if (!seen.Add(key))
                {
                    problems.Add($"{key}: listed twice");
                }
                double[] pitch = e.TryGetProperty("pitch", out JsonElement p) && p.ValueKind == JsonValueKind.Array
                    ? p.EnumerateArray().Select(x => x.GetDouble()).ToArray()
                    : new[] { 1.0, 1.0 };
                var layers = new List<SoundLayer>();
                if (e.TryGetProperty("layers", out JsonElement ls) && ls.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement l in ls.EnumerateArray())
                    {
                        layers.Add(new SoundLayer
                        {
                            Name = Str(l, "name", ""),
                            Category = Str(l, "category", Str(e, "category", "")),
                            From = Num(l, "from", 0),
                            To = Num(l, "to", double.PositiveInfinity),
                            Fade = Num(l, "fade", 10),
                            Volume = Num(l, "volume", 0),
                            UnitSize = Num(l, "unitSize", 4),
                            MaxDistance = Num(l, "maxDistance", 100),
                            Files = Files(l),
                        });
                    }
                }
                var ev = new SoundEventData
                {
                    Key = key,
                    Category = Str(e, "category", ""),
                    Bus = Str(e, "bus", "SFX"),
                    Files = Files(e),
                    Volume = Num(e, "volume", 0),
                    PitchMin = pitch.Length > 0 ? pitch[0] : 1,
                    PitchMax = pitch.Length > 1 ? pitch[1] : 1,
                    Jitter = Num(e, "jitter", 0),
                    UnitSize = Num(e, "unitSize", 4),
                    MaxDistance = Num(e, "maxDistance", 60),
                    Priority = (int)Num(e, "priority", 50),
                    Limit = Math.Max(1, (int)Num(e, "limit", 4)),
                    Loop = Bool(e, "loop", false),
                    Positional = Str(e, "space", "3d") != "2d",
                    Occlude = Bool(e, "occlude", true),
                    Delay = Bool(e, "delay", false),
                    Cooldown = Num(e, "cooldown", 0),
                    Layers = layers,
                };
                if (ev.Files.Count == 0 && layers.Count == 0)
                {
                    problems.Add($"{key}: no files");
                }
                if (layers.Any(l => l.Files.Count == 0))
                {
                    problems.Add($"{key}: a layer without files");
                }
                if (ev.PitchMin <= 0 || ev.PitchMax < ev.PitchMin)
                {
                    problems.Add($"{key}: pitch range {ev.PitchMin}–{ev.PitchMax}");
                }
                if (ev.Category.Length > 0 && categories.Count > 0 && !categories.ContainsKey(ev.Category))
                {
                    problems.Add($"{key}: unknown category {ev.Category}");
                }
                events.Add(ev);
            }
        }
        var items = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("items", out JsonElement its) && its.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty item in its.EnumerateObject())
            {
                var roles = new Dictionary<string, string>(StringComparer.Ordinal);
                if (item.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty role in item.Value.EnumerateObject())
                    {
                        string key = role.Value.GetString() ?? "";
                        if (!seen.Contains(key))
                        {
                            problems.Add($"items: {item.Name} {role.Name} plays {key}, which isn't an event");
                        }
                        roles[role.Name] = key;
                    }
                }
                items[item.Name] = roles;
            }
        }
        if (problems.Count > 0)
        {
            throw new FormatException("sounds.json: " + string.Join("; ", problems));
        }
        return new SoundCatalogData(categories, events, items);
    }

    private static IReadOnlyList<string> Files(JsonElement e) =>
        e.TryGetProperty("files", out JsonElement f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();

    private static string Str(JsonElement e, string name, string fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static double Num(JsonElement e, string name, double fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble()
        : e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String
          && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d
        : fallback;

    private static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        } : fallback;
}
