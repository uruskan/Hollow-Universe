using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Hollow.Surface;

/// <summary>Bounded strict JSON shared by authenticated formation-package readers.</summary>
internal static class FormationJson
{
    internal const int MaximumBytes = 16 * 1024 * 1024;

    /// <summary>The caller owns the returned document and must keep its input bytes immutable until disposal.</summary>
    internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes, int maxBytes = MaximumBytes)
    {
        if (maxBytes < 1 || maxBytes > MaximumBytes)
            throw new FormationValidationException("Invalid formation JSON byte limit.");
        if (bytes.Length > maxBytes)
            throw new FormationCapacityException("Formation JSON exceeds its byte limit.");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = 32, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow
            });
        }
        catch (JsonException error)
        {
            throw new FormationValidationException("Malformed formation JSON: " + error.Message);
        }
        try
        {
            CheckUniqueProperties(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void CheckUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new FormationValidationException("Duplicate formation JSON property: " + property.Name);
                CheckUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in element.EnumerateArray()) CheckUniqueProperties(child);
    }
}
