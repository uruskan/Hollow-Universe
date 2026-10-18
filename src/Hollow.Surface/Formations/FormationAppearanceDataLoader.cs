using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Hollow.Surface;

/// <summary>
/// Reads one bounded additive appearance package against the exact immutable visual upload inputs.
/// The caller must reserve LoadingReservationBytes before calling, including while earlier owners retire.
/// This is a declared CPU allowance; native upload and the borrowed visual payload are separate charges.
/// No source geometry, material permissions, world identity or input stream is changed.
/// </summary>
public static class FormationAppearanceDataLoader
{
    public const long LoadingReservationBytes = 64L * 1024 * 1024;
    private const int JsonLimit = 1024 * 1024, MaxVerticesPerLod = 60000, MaxTrianglesPerLod = 50000;
    private sealed record Pin(int Bytes, string Sha);

    public static FormationAppearanceData Load(string directory, string receiptSha256, FormationAppearancePins expected,
        FormationVisualData visual, long reservedBytes, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(visual);
        if (reservedBytes < LoadingReservationBytes) throw new FormationCapacityException("Appearance loading needs its complete declared CPU reservation.");
        string receiptHash = Hash(receiptSha256);
        string moduleHash = Hash(expected.ModuleReceiptSha256), tangentHash = Hash(expected.TangentReceiptSha256), recipeHash = Hash(expected.RecipeSha256);
        if (visual.Lods.Count is < 1 or > 2) throw Invalid("Appearance package supports one or two explicitly bound LODs.");
        string root = string.Empty;
        var pins = new Dictionary<string, Pin>(StringComparer.Ordinal);
        var fileAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int steps = 0;
        void Step() { cancellation.ThrowIfCancellationRequested(); if (++steps > 2000000) throw new FormationCapacityException("Appearance validation work exceeded two million steps."); }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            root = Path.GetFullPath(directory);
            CheckAncestors(root);
            using JsonDocument receipt = FormationJson.Parse(Read("appearance-receipt.json", JsonLimit, receiptHash), JsonLimit);
            JsonElement r = receipt.RootElement;
            Require(Text(r, "schema") == "forest-contact-additive-appearance-v1", "Unsupported appearance schema.");
            Require(Text(r, "status") == "unadmitted-additive-appearance" && False(r, "assetAccepted") &&
                False(r, "appearanceAccepted") && False(r, "rootPermissions") && Array(r, "capabilities").GetArrayLength() == 0,
                "Additive data cannot self-authorize art or physical permissions.");
            SameHash(Text(r, "baseModuleReceiptSha256"), moduleHash);
            SameHash(Text(r, "tangentReceiptSha256"), tangentHash);
            SameHash(Text(r, "recipeSha256"), recipeHash);
            JsonElement files = Array(r, "outputs"), bindings = Array(r, "lods");
            Require(bindings.GetArrayLength() == visual.Lods.Count && files.GetArrayLength() == 2 * visual.Lods.Count,
                "Appearance package must bind every selected visual LOD exactly once.");
            foreach (JsonElement file in files.EnumerateArray())
            {
                Step(); string name = Filename(Text(file, "file")); int bytes = Integer(file, "bytes");
                Require(bytes > 0 && bytes <= JsonLimit && fileAliases.Add(name) &&
                    pins.TryAdd(name, new Pin(bytes, Hash(Text(file, "sha256")))), "Invalid, duplicate or aliased appearance file pin.");
            }
            var wanted = new Dictionary<string, FormationVisualLod>(StringComparer.Ordinal);
            foreach (FormationVisualLod lod in visual.Lods)
                Require(wanted.TryAdd(lod.LodKey, lod), "Appearance LOD keys must be unambiguous across the supplied visual payload.");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new FormationAppearanceLod[visual.Lods.Count];
            long resident = 8192;
            foreach (JsonElement binding in bindings.EnumerateArray())
            {
                Step(); string key = Text(binding, "key");
                Require(wanted.Remove(key, out FormationVisualLod? found), "Missing or repeated appearance LOD binding.");
                FormationVisualLod target = found ?? throw Invalid("Missing appearance visual target.");
                string metaName = Filename(Text(binding, "metadataFile")), dataName = Filename(Text(binding, "dataFile"));
                Require(metaName.EndsWith(".json", StringComparison.Ordinal) && dataName.EndsWith(".bin", StringComparison.Ordinal)
                    && used.Add(metaName) && used.Add(dataName), "Appearance file aliases or roles are invalid.");
                using JsonDocument metadata = FormationJson.Parse(Pinned(metaName), JsonLimit);
                JsonElement m = metadata.RootElement;
                Require(Text(m, "schema") == "forest-contact-additive-appearance-lod-v1" && Text(m, "lod") == key &&
                    Text(m, "dataFile") == dataName, "Appearance LOD metadata identity differs.");
                Pin dataPin = RequirePin(dataName);
                Require(Integer(m, "bytes") == dataPin.Bytes, "Appearance data length differs."); SameHash(Text(m, "sha256"), dataPin.Sha);
                JsonElement rows = Array(m, "primitives");
                Require(target.Primitives.Count is >= 1 and <= 32 && rows.GetArrayLength() == target.Primitives.Count,
                    "Appearance primitive count differs from visual upload inputs.");
                int byteEnd = 0, vertices = 0, triangles = 0;
                // Validate the complete layout and target identity BEFORE allocating binary or output masks.
                for (int i = 0; i < rows.GetArrayLength(); i++)
                {
                    Step(); JsonElement row = rows[i]; FormationVisualPrimitive p = target.Primitives[i];
                    Require(p.Ordinal == i && Integer(row, "primitive") == i && Integer(row, "material") == p.MaterialIndex &&
                        Integer(row, "firstVertex") == p.SourceVertexOffset && Integer(row, "firstTriangle") == p.SourceTriangleOffset &&
                        p.SourceVertexOffset == vertices && p.SourceTriangleOffset == triangles,
                        "Appearance primitive order or material binding differs.");
                    Require(Integer(row, "vertexCount") == p.VertexCount && Integer(row, "triangleCount") == p.Indices.Count / 3 &&
                        p.Indices.Count % 3 == 0 && Integer(row, "components") == 4 && Integer(row, "strideBytes") == 16 &&
                        Text(row, "componentType") == "float32-le", "Appearance vertex layout differs.");
                    Require(p.VertexCount <= MaxVerticesPerLod - vertices && p.Indices.Count / 3 <= MaxTrianglesPerLod - triangles,
                        "Appearance target exceeds admitted geometry counts.");
                    int bytes = checked(p.VertexCount * 16);
                    Require(Integer(row, "byteOffset") == byteEnd && Integer(row, "byteLength") == bytes && bytes <= dataPin.Bytes - byteEnd,
                        "Appearance masks overlap, leave gaps or exceed their data file.");
                    Require(Text(row, "sourceKind") == (i == 0 ? "original-scan" : "added-closure") &&
                        Required(row, "sourceAtlasSamplingAllowed").ValueKind == (i == 0 ? JsonValueKind.True : JsonValueKind.False),
                        "Source scan sampling must be restricted to the original primitive.");
                    CheckVisualHashes(row, p, Step);
                    byteEnd += bytes; vertices += p.VertexCount; triangles += p.Indices.Count / 3;
                }
                Require(byteEnd == dataPin.Bytes && byteEnd <= MaxVerticesPerLod * 16, "Unbound trailing appearance data.");
                byte[] binary = Pinned(dataName);
                var parts = new FormationAppearancePrimitive[rows.GetArrayLength()];
                for (int i = 0; i < parts.Length; i++)
                {
                    JsonElement row = rows[i]; FormationVisualPrimitive p = target.Primitives[i];
                    int start = Integer(row, "byteOffset"), bytes = p.VertexCount * 16;
                    SameHash(Convert.ToHexString(SHA256.HashData(binary.AsSpan(start, bytes))), Text(row, "vertexMaskSha256"));
                    using var cornerHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    foreach (int v in Indices(p)) { Step(); cornerHash.AppendData(binary.AsSpan(start + v * 16, 16)); }
                    SameHash(Convert.ToHexString(cornerHash.GetHashAndReset()), Text(row, "cornerMaskSha256"));
                    var values = new float[p.VertexCount * 4];
                    for (int vertex = 0; vertex < p.VertexCount; vertex++)
                    {
                        Step();
                        for (int channel = 0; channel < 4; channel++)
                        {
                            int at = vertex * 4 + channel;
                            float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(binary.AsSpan(start + at * 4, 4)));
                            Require(float.IsFinite(value) && value >= 0 && value <= 1, "Appearance mask is not finite in [0,1].");
                            values[at] = value;
                        }
                        Require((double)values[vertex * 4 + 2] + values[vertex * 4 + 3] <= 1, "Cap and toe weights exceed unity.");
                    }
                    parts[i] = new(i, values); resident += bytes + 256;
                }
                int targetIndex = 0; while (!ReferenceEquals(visual.Lods[targetIndex], target)) targetIndex++;
                result[targetIndex] = new(target, parts);
            }
            Require(wanted.Count == 0 && used.Count == pins.Count, "Unused or missing appearance outputs.");
            cancellation.ThrowIfCancellationRequested();
            return new(receiptHash, visual.ReceiptSha256, result, resident);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw Invalid("Cannot load complete appearance data: " + error.Message); }

        Pin RequirePin(string name) => pins.TryGetValue(name, out Pin? pin) ? pin : throw Invalid("Missing appearance output pin.");
        byte[] Pinned(string name) { Pin p = RequirePin(name); return Read(name, p.Bytes, p.Sha, p.Bytes); }
        byte[] Read(string name, int maximum, string sha, int exact = -1)
        {
            Step(); string path = Path.Combine(root, Filename(name)); CheckAncestors(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(stream.Length is > 0 && stream.Length <= maximum && (exact < 0 || stream.Length == exact), "Appearance file length differs from admission.");
            var bytes = new byte[checked((int)stream.Length)]; int offset = 0;
            while (offset < bytes.Length) { Step(); int n = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset)); Require(n > 0, "Truncated appearance file."); offset += n; }
            Require(stream.ReadByte() == -1, "Appearance file grew during reading.");
            SameHash(Convert.ToHexString(SHA256.HashData(bytes)), sha); return bytes;
        }
    }

    private static void CheckVisualHashes(JsonElement row, FormationVisualPrimitive p, Action step)
    {
        Require(p.HasTangents && p.HasTexCoords && p.Normals.Count == p.VertexCount * 3 && p.Tangents.Count == p.VertexCount * 4 &&
            p.TexCoords.Count == p.VertexCount * 2, "Appearance binding requires complete authored visual streams.");
        using var vertices = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var corners = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var tangents = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var indices = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> attribute = stackalloc byte[32]; Span<byte> scalar = stackalloc byte[4]; Span<byte> tangent = stackalloc byte[16];
        for (int v = 0; v < p.VertexCount; v++)
        {
            step(); PutAttributes(p, v, attribute); vertices.AppendData(attribute);
            for (int c = 0; c < 4; c++) Put(tangent[(c * 4)..], p.Tangents[v * 4 + c]);
            tangents.AppendData(tangent);
        }
        foreach (int index in Indices(p))
        { step(); BinaryPrimitives.WriteUInt32LittleEndian(scalar, (uint)index); indices.AppendData(scalar); PutAttributes(p, index, attribute); corners.AppendData(attribute); }
        SameHash(Convert.ToHexString(vertices.GetHashAndReset()), Text(row, "orderedVertexPositionNormalUvSha256"));
        SameHash(Convert.ToHexString(corners.GetHashAndReset()), Text(row, "baseCornerPositionNormalUvSha256"));
        SameHash(Convert.ToHexString(tangents.GetHashAndReset()), Text(row, "orderedTangentSha256"));
        SameHash(Convert.ToHexString(indices.GetHashAndReset()), Text(row, "orderedLocalIndexSha256"));
    }
    private static IEnumerable<int> Indices(FormationVisualPrimitive p)
    { for (int i = 0; i < p.Indices.Count; i++) { int v = p.Indices[i]; Require((uint)v < (uint)p.VertexCount, "Invalid bound visual corner."); yield return v; } }
    private static void PutAttributes(FormationVisualPrimitive p, int v, Span<byte> bytes)
    {
        for (int c = 0; c < 3; c++) { Put(bytes[(c * 4)..], p.Positions[v * 3 + c]); Put(bytes[((c + 3) * 4)..], p.Normals[v * 3 + c]); }
        for (int c = 0; c < 2; c++) Put(bytes[((c + 6) * 4)..], p.TexCoords[v * 2 + c]);
    }
    private static void Put(Span<byte> bytes, float value) => BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
    private static void CheckAncestors(string path)
    { for (string? p = path; p != null; p = Path.GetDirectoryName(p)) if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw Invalid("Appearance paths must not traverse reparse points."); }
    private static string Filename(string name)
    { Require(name.Length is > 0 and <= 128 && Path.GetFileName(name) == name && name.IndexOfAny(['/', '\\', ':']) < 0 && name != "." && name != "..", "Appearance outputs must use bounded leaf filenames."); return name; }
    private static string Hash(string value)
    { Require(value != null && value.Length == 64, "Invalid appearance SHA-256."); foreach (char c in value) Require(char.IsAsciiHexDigit(c), "Invalid appearance SHA-256."); return value.ToUpperInvariant(); }
    private static void SameHash(string a, string b) => Require(Hash(a) == Hash(b), "Appearance hash or visual-stream binding differs.");
    private static JsonElement Required(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) ? v : throw Invalid("Missing appearance property: " + name);
    private static string Text(JsonElement e, string name) { JsonElement v = Required(e, name); Require(v.ValueKind == JsonValueKind.String, "Appearance text field expected."); return v.GetString()!; }
    private static int Integer(JsonElement e, string name) { JsonElement v = Required(e, name); Require(v.TryGetInt32(out int n) && n >= 0, "Appearance nonnegative integer expected."); return n; }
    private static JsonElement Array(JsonElement e, string name) { JsonElement v = Required(e, name); Require(v.ValueKind == JsonValueKind.Array, "Appearance array expected."); return v; }
    private static bool False(JsonElement e, string name) => Required(e, name).ValueKind == JsonValueKind.False;
    private static void Require([DoesNotReturnIf(false)] bool value, string message) { if (!value) throw Invalid(message); }
    private static FormationValidationException Invalid(string message) => new(message);
}
