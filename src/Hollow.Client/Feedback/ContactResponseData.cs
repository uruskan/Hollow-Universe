using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Hollow.Client.Audio;

namespace Hollow.Client.Feedback;

/// <summary>Presentation selected from an authoritative physical surface, independent of renderer and target kind.</summary>
public sealed record ContactResponse(string Sound, string Effect);

public sealed record SurfaceResponse(FootSurface Footstep, Vector3 Dust, bool Flesh,
    IReadOnlyDictionary<string, ContactResponse> Actions);

/// <summary>
/// One response table for physical surfaces. Actions and optional modifiers are data keys;
/// adding a material never requires a target/item-name branch in a view.
/// </summary>
public sealed class ContactResponseData
{
    public const string FileName = "contact-responses.json";
    public IReadOnlyDictionary<string, SurfaceResponse> Surfaces { get; }

    private ContactResponseData(Dictionary<string, SurfaceResponse> surfaces) => Surfaces = surfaces;

    public SurfaceResponse Surface(string key) => Surfaces.TryGetValue(key, out SurfaceResponse? value)
        ? value : Surfaces["unknown"];

    public ContactResponse Resolve(string surface, string action, string? modifier = null)
    {
        SurfaceResponse entry = Surface(surface);
        if (!string.IsNullOrEmpty(modifier) && entry.Actions.TryGetValue(action + ":" + modifier, out ContactResponse? modified))
            return modified;
        if (entry.Actions.TryGetValue(action, out ContactResponse? response)) return response;
        throw new InvalidDataException($"No contact response for action '{action}' on surface '{surface}'.");
    }

    public static ContactResponseData Load(string path) => Parse(File.ReadAllText(path));

    public static ContactResponseData Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var surfaces = new Dictionary<string, SurfaceResponse>(StringComparer.Ordinal);
        foreach (JsonProperty property in doc.RootElement.GetProperty("surfaces").EnumerateObject())
        {
            JsonElement value = property.Value;
            if (!Enum.TryParse(value.GetProperty("footstep").GetString(), true, out FootSurface footstep) || !Enum.IsDefined(footstep))
                throw new InvalidDataException($"{property.Name}: invalid footstep bank.");
            JsonElement dust = value.GetProperty("dust");
            if (dust.GetArrayLength() != 3) throw new InvalidDataException($"{property.Name}: dust needs three colour components.");
            var colour = new Vector3(dust[0].GetSingle(), dust[1].GetSingle(), dust[2].GetSingle());
            if (!float.IsFinite(colour.X) || !float.IsFinite(colour.Y) || !float.IsFinite(colour.Z) ||
                colour.X < 0 || colour.Y < 0 || colour.Z < 0 || colour.X > 1 || colour.Y > 1 || colour.Z > 1)
                throw new InvalidDataException($"{property.Name}: invalid dust colour.");
            var actions = new Dictionary<string, ContactResponse>(StringComparer.Ordinal);
            foreach (JsonProperty action in value.GetProperty("actions").EnumerateObject())
            {
                string sound = action.Value.GetProperty("sound").GetString() ?? "";
                string effect = action.Value.GetProperty("effect").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(action.Name) || string.IsNullOrWhiteSpace(sound) || string.IsNullOrWhiteSpace(effect) ||
                    !actions.TryAdd(action.Name, new ContactResponse(sound, effect)))
                    throw new InvalidDataException($"{property.Name}: invalid or duplicate action '{action.Name}'.");
            }
            foreach (string required in new[] { "bullet", "arrow" })
                if (!actions.ContainsKey(required)) throw new InvalidDataException($"{property.Name}: missing {required} response.");
            bool flesh = value.TryGetProperty("flesh", out JsonElement flag) && flag.GetBoolean();
            if (!surfaces.TryAdd(property.Name, new SurfaceResponse(footstep, colour, flesh, actions)))
                throw new InvalidDataException($"Duplicate surface response '{property.Name}'.");
        }
        if (!surfaces.ContainsKey("unknown")) throw new InvalidDataException("Contact responses require an explicit unknown fallback.");
        return new ContactResponseData(surfaces);
    }
}
