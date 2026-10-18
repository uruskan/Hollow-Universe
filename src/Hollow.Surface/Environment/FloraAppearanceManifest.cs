using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hollow.Surface;

public sealed class FloraAppearanceImageHash
{
    public FloraAppearanceImageHash(string role, string sha256)
    {
        if (!FloraAppearanceSchema.Images.Any(i => i.Role == role))
            throw new FloraAppearanceValidationException("Unknown flora image role.");
        Role = role; Sha256 = FloraAppearanceCheck.Hash(sha256);
    }
    public string Role { get; }
    public string Sha256 { get; }
}

/// <summary>
/// Exact exporter/loader wire contract. Encode only after all seven payloads were successfully written and checked,
/// then write appearance.json last. Encoding creates evidence, never a trusted pin or runtime admission.
/// Offsets are implicit, tightly packed successive RGBA8 levels, floor-halving each axis to one; no headers/padding.
/// </summary>
public static class FloraAppearanceManifest
{
    public const string FileName = "appearance.json";

    public static byte[] Encode(string appearanceId, int revision, FloraAppearanceInputContract inputs,
        FloraAppearanceColour bladeMean, IReadOnlyList<FloraAppearanceImageHash> images)
    {
        FloraAppearanceCheck.Id(appearanceId); FloraAppearanceCheck.Colour(bladeMean);
        if (revision < 1) throw new FloraAppearanceValidationException("Invalid appearance revision.");
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(images);
        int imageCount = images.Count;
        if (imageCount != FloraAppearanceSchema.Images.Count)
            throw new FloraAppearanceValidationException("Expected exactly seven image hashes.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", FloraAppearanceSchema.Package); writer.WriteBoolean("complete", true);
            writer.WriteString("appearanceId", appearanceId); writer.WriteNumber("revision", revision);
            writer.WriteString("inputFingerprint", inputs.Fingerprint);
            writer.WritePropertyName("inputs"); WriteInputs(writer, inputs);
            writer.WriteStartArray("bladeMean"); writer.WriteNumberValue(bladeMean.R);
            writer.WriteNumberValue(bladeMean.G); writer.WriteNumberValue(bladeMean.B); writer.WriteEndArray();
            writer.WriteStartArray("images");
            for (int i = 0; i < imageCount; i++)
            {
                FloraAppearanceImageLayout layout = FloraAppearanceSchema.Images[i];
                FloraAppearanceImageHash image = images[i] ?? throw new FloraAppearanceValidationException("Null image hash.");
                if (image.Role != layout.Role) throw new FloraAppearanceValidationException("Image hashes must follow schema role order.");
                writer.WriteStartObject(); writer.WriteString("role", layout.Role); writer.WriteString("file", layout.File);
                writer.WriteNumber("width", layout.Width); writer.WriteNumber("height", layout.Height);
                writer.WriteString("format", layout.Format); writer.WriteNumber("mipLevels", layout.MipLevels);
                writer.WriteString("mipPolicy", layout.MipPolicy); writer.WriteNumber("bytes", layout.ByteLength);
                writer.WriteString("sha256", image.Sha256); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        if (stream.Length > 1048576) throw new FloraAppearanceCapacityException("Flora manifest schema byte bound exceeded.");
        return stream.ToArray();
    }

    private static void WriteInputs(Utf8JsonWriter writer, FloraAppearanceInputContract inputs)
    {
        writer.WriteStartObject(); writer.WriteString("schema", FloraAppearanceSchema.Input);
        writer.WriteNumber("recipeSeed", inputs.RecipeSeed);
        writer.WriteNumber("views", FloraAppearanceSchema.Views); writer.WriteNumber("cellPixels", FloraAppearanceSchema.CellPixels);
        writer.WriteNumber("nativeRowStart", FloraAppearanceSchema.NativeRowStart); writer.WriteNumber("nativeRowEnd", FloraAppearanceSchema.NativeRowEnd);
        writer.WriteStartObject("profile");
        writer.WriteString("engineBuild", inputs.Profile.EngineBuild); writer.WriteString("renderer", inputs.Profile.Renderer);
        writer.WriteString("deviceProfile", inputs.Profile.DeviceProfile); writer.WriteString("bakeRevision", inputs.Profile.BakeRevision);
        writer.WriteString("finishRevision", inputs.Profile.FinishRevision); writer.WriteString("projectionRevision", inputs.Profile.ProjectionRevision);
        writer.WriteEndObject(); writer.WriteStartArray("dependencies");
        foreach (FloraAppearanceDependency dependency in inputs.Dependencies)
        {
            writer.WriteStartObject(); writer.WriteString("key", dependency.Key); writer.WriteNumber("bytes", dependency.Bytes);
            writer.WriteString("sha256", dependency.Sha256); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteStartArray("rows");
        foreach (FloraAppearanceRow row in inputs.Rows)
        {
            writer.WriteStartObject(); writer.WriteNumber("row", row.Row); writer.WriteNumber("kind", (byte)row.Kind);
            writer.WriteNumber("variant", row.Variant); writer.WriteString("variantKey", row.VariantKey);
            writer.WriteNumber("speciesScale", row.SpeciesScale); writer.WriteNumber("height", row.Height); writer.WriteNumber("extent", row.Extent);
            writer.WriteString("lod0GeometrySha256", row.Lod0GeometrySha256); writer.WriteString("lod1GeometrySha256", row.Lod1GeometrySha256);
            writer.WriteString("materialSha256", row.MaterialSha256); writer.WriteBoolean("native", row.Native); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    internal sealed record Decoded(FloraAppearanceColour BladeMean, FloraAppearanceImageHash[] Images);

    internal static Decoded Decode(byte[] bytes, FloraAppearancePin expected)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
            JsonElement root = document.RootElement;
            Object(root, "schema", "complete", "appearanceId", "revision", "inputFingerprint", "inputs", "bladeMean", "images");
            Require(Text(root, "schema") == FloraAppearanceSchema.Package && root.GetProperty("complete").GetBoolean(), "Incomplete or unsupported flora package.");
            Require(Text(root, "appearanceId") == expected.AppearanceId && root.GetProperty("revision").GetInt32() == expected.Revision,
                "Appearance ID/revision differs from the independent pin.");
            FloraAppearanceInputContract inputs = ReadInputs(root.GetProperty("inputs"));
            Require(FloraAppearanceCheck.Hash(Text(root, "inputFingerprint")) == inputs.Fingerprint &&
                inputs.Fingerprint == expected.ExpectedInputs.Fingerprint, "Input fingerprint differs from the independent contract.");
            JsonElement mean = Array(root, "bladeMean", 3, 3);
            var bladeMean = new FloraAppearanceColour(mean[0].GetSingle(), mean[1].GetSingle(), mean[2].GetSingle());
            FloraAppearanceCheck.Colour(bladeMean);
            JsonElement imageElements = Array(root, "images", FloraAppearanceSchema.Images.Count, FloraAppearanceSchema.Images.Count);
            var images = new FloraAppearanceImageHash[imageElements.GetArrayLength()];
            for (int i = 0; i < images.Length; i++)
            {
                JsonElement image = imageElements[i]; FloraAppearanceImageLayout layout = FloraAppearanceSchema.Images[i];
                Object(image, "role", "file", "width", "height", "format", "mipLevels", "mipPolicy", "bytes", "sha256");
                Require(Text(image, "role") == layout.Role && Text(image, "file") == layout.File &&
                    image.GetProperty("width").GetInt32() == layout.Width && image.GetProperty("height").GetInt32() == layout.Height &&
                    Text(image, "format") == layout.Format && image.GetProperty("mipLevels").GetInt32() == layout.MipLevels &&
                    Text(image, "mipPolicy") == layout.MipPolicy && image.GetProperty("bytes").GetInt64() == layout.ByteLength,
                    "Image role/path/layout differs from the complete schema v1 chain.");
                images[i] = new FloraAppearanceImageHash(layout.Role, Text(image, "sha256"));
            }
            return new Decoded(bladeMean, images);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new FloraAppearanceValidationException("Malformed flora appearance manifest.", error); }
    }

    private static FloraAppearanceInputContract ReadInputs(JsonElement input)
    {
        Object(input, "schema", "recipeSeed", "views", "cellPixels", "nativeRowStart", "nativeRowEnd", "profile", "dependencies", "rows");
        Require(Text(input, "schema") == FloraAppearanceSchema.Input && input.GetProperty("views").GetInt32() == FloraAppearanceSchema.Views &&
            input.GetProperty("cellPixels").GetInt32() == FloraAppearanceSchema.CellPixels &&
            input.GetProperty("nativeRowStart").GetInt32() == FloraAppearanceSchema.NativeRowStart &&
            input.GetProperty("nativeRowEnd").GetInt32() == FloraAppearanceSchema.NativeRowEnd, "Unsupported input row/view contract.");
        JsonElement p = input.GetProperty("profile");
        Object(p, "engineBuild", "renderer", "deviceProfile", "bakeRevision", "finishRevision", "projectionRevision");
        var profile = new FloraAppearanceBakeProfile(Text(p, "engineBuild"), Text(p, "renderer"), Text(p, "deviceProfile"),
            Text(p, "bakeRevision"), Text(p, "finishRevision"), Text(p, "projectionRevision"));
        JsonElement dependencyElements = Array(input, "dependencies", 1, FloraAppearanceSchema.MaxDependencies);
        var dependencies = new FloraAppearanceDependency[dependencyElements.GetArrayLength()];
        for (int i = 0; i < dependencies.Length; i++)
        {
            JsonElement d = dependencyElements[i]; Object(d, "key", "bytes", "sha256");
            dependencies[i] = new FloraAppearanceDependency(Text(d, "key"), d.GetProperty("bytes").GetInt64(), Text(d, "sha256"));
        }
        JsonElement rowElements = Array(input, "rows", FloraAppearanceSchema.Rows, FloraAppearanceSchema.Rows);
        var rows = new FloraAppearanceRow[FloraAppearanceSchema.Rows];
        for (int i = 0; i < rows.Length; i++)
        {
            JsonElement r = rowElements[i];
            Object(r, "row", "kind", "variant", "variantKey", "speciesScale", "height", "extent", "lod0GeometrySha256", "lod1GeometrySha256", "materialSha256", "native");
            rows[i] = new FloraAppearanceRow(r.GetProperty("row").GetInt32(), (ScatterKind)r.GetProperty("kind").GetByte(),
                r.GetProperty("variant").GetInt32(), Text(r, "variantKey"), r.GetProperty("speciesScale").GetSingle(),
                r.GetProperty("height").GetSingle(), r.GetProperty("extent").GetSingle(), Text(r, "lod0GeometrySha256"),
                Text(r, "lod1GeometrySha256"), Text(r, "materialSha256"));
            Require(r.GetProperty("native").GetBoolean() == rows[i].Native, "Incorrect native row classification.");
        }
        return new FloraAppearanceInputContract(input.GetProperty("recipeSeed").GetInt32(), profile, dependencies, rows);
    }

    private static void Object(JsonElement value, params string[] fields)
    {
        Require(value.ValueKind == JsonValueKind.Object, "Expected a flora metadata object.");
        var remaining = new HashSet<string>(fields, StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            Require(remaining.Remove(property.Name), "Unknown or duplicate flora metadata property.");
        Require(remaining.Count == 0, "Missing required flora metadata property.");
    }
    private static JsonElement Array(JsonElement parent, string field, int minimum, int maximum)
    {
        JsonElement value = parent.GetProperty(field);
        Require(value.ValueKind == JsonValueKind.Array, "Expected a flora metadata array.");
        Require(value.GetArrayLength() >= minimum && value.GetArrayLength() <= maximum, "Flora metadata array count bound exceeded.");
        return value;
    }
    private static string Text(JsonElement value, string field) => FloraAppearanceCheck.Text(value.GetProperty(field).GetString()!, 240);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new FloraAppearanceValidationException(message);
    }
}
