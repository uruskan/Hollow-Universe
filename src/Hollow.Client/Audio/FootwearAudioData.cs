using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;

namespace Hollow.Client.Audio;

/// <summary>
/// Foot-contact profiles from footsteps.json. Items declare a profile on their wear capability; both predicted
/// local equipment and replicated remote equipment resolve through this registry. Authoring filters and gain are
/// baked into the generated banks and catalogue, so playback does not apply them a second time.
/// </summary>
public sealed class FootwearAudioData
{
    public const string FileName = "footsteps.json";
    private static readonly HashSet<string> ContactKeys = new(Footsteps.AllKeys().Where(key => key != Footsteps.SwimKey), StringComparer.Ordinal);
    private readonly HashSet<string> _profiles;

    private FootwearAudioData(string defaultProfile, List<string> profiles)
    {
        DefaultProfile = defaultProfile;
        Profiles = profiles.AsReadOnly();
        _profiles = new HashSet<string>(profiles, StringComparer.Ordinal);
    }

    /// <summary>Used for bare feet, missing assignments and safe fallback from unknown equipment profiles.</summary>
    public string DefaultProfile { get; }

    public IReadOnlyList<string> Profiles { get; }

    /// <summary>A missing presentation file can still use legacy unsuffixed catalogue events.</summary>
    public static FootwearAudioData Fallback { get; } = new("barefoot", new List<string> { "barefoot" });

    public static FootwearAudioData Load(string path) => Parse(File.ReadAllText(path));

    public static FootwearAudioData Parse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("footsteps.json: expected an object");
            var problems = new List<string>();
            string defaultProfile = Key(root, "defaultProfile");
            if (!WearStats.IsFootstepProfileKey(defaultProfile)) problems.Add("defaultProfile must be a valid profile key");
            var profiles = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (!root.TryGetProperty("profiles", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                problems.Add("profiles must be an array");
            else
            {
                foreach (JsonElement profile in list.EnumerateArray())
                {
                    string key = Key(profile, "key");
                    if (!WearStats.IsFootstepProfileKey(key)) problems.Add($"invalid profile key '{key}'");
                    else if (!seen.Add(key)) problems.Add($"duplicate profile '{key}'");
                    else profiles.Add(key);
                }
            }
            if (profiles.Count == 0) problems.Add("at least one profile is required");
            if (!seen.Contains(defaultProfile)) problems.Add($"defaultProfile '{defaultProfile}' is not registered");
            if (problems.Count != 0) throw new FormatException("footsteps.json: " + string.Join("; ", problems));
            return new FootwearAudioData(defaultProfile, profiles);
        }
        catch (JsonException e)
        {
            throw new FormatException("footsteps.json: malformed JSON", e);
        }
    }

    private static string Key(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out JsonElement key) && key.ValueKind == JsonValueKind.String
            ? key.GetString() ?? "" : "";

    public bool Contains(string profile) => _profiles.Contains(profile);

    /// <summary>Local predicted inventory. Only the worn container belongs here, never the main inventory or belt.</summary>
    public string Profile(GameContent content, ReadOnlySpan<ItemStack> worn)
    {
        foreach (ItemStack stack in worn)
        {
            if (!stack.IsEmpty && Declared(content.Item(stack.Item)) is { } profile) return Registered(profile);
        }
        return DefaultProfile;
    }

    /// <summary>Remote clothing IDs from CharacterLook; the same item capability determines its sound.</summary>
    public string Profile(GameContent content, ReadOnlySpan<ushort> worn)
    {
        foreach (ushort item in worn)
        {
            if (Declared(content.Item(item)) is { } profile) return Registered(profile);
        }
        return DefaultProfile;
    }

    private static string? Declared(ItemDef? item) => item?.Wear is { } wear && (wear.Layers & WearLayer.Feet) != 0
        ? wear.FootstepProfile : null;

    private string Registered(string? profile) => profile != null && _profiles.Contains(profile) ? profile : DefaultProfile;

    /// <summary>Cadence keeps its base keys. Only foot contacts gain a footwear suffix; swimming stays shared.</summary>
    public string EventKey(string baseKey, string? profile) => ContactKeys.Contains(baseKey) ? baseKey + "." + Registered(profile) : baseKey;

    /// <summary>Incomplete optional presentation data falls back to the default bank and then the legacy event.</summary>
    public string ResolveEvent(string baseKey, string? profile, SoundCatalogData catalogue)
    {
        string key = EventKey(baseKey, profile);
        if (catalogue.Find(key) != null) return key;
        string fallback = EventKey(baseKey, DefaultProfile);
        return catalogue.Find(fallback) != null ? fallback : baseKey;
    }

    /// <summary>Authoring validation: unknown item references and missing banks cannot silently ship as fallbacks.</summary>
    public IReadOnlyList<string> Validate(GameContent content, SoundCatalogData catalogue)
    {
        var problems = new List<string>();
        foreach (ItemDef item in content.Items)
        {
            if (item.Wear?.FootstepProfile is { } profile && !_profiles.Contains(profile))
                problems.Add($"{item.Key}: unknown footstep profile '{profile}'");
        }
        foreach (string profile in Profiles)
        {
            foreach (string baseKey in ContactKeys)
            {
                string key = EventKey(baseKey, profile);
                SoundEventData? sound = catalogue.Find(key);
                if (sound == null || !sound.AllFiles.Any() || sound.Loop)
                    problems.Add($"{key}: missing playable foot-contact event");
            }
        }
        return problems;
    }
}
