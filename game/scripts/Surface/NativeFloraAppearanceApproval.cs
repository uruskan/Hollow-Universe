using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Fixture-only authority supplied outside the appearance package. The caller pins this record's exact bytes;
/// no package declaration supplies that pin. Reading the record does not load a package or create native resources.
/// File checks require immutable inputs during admission; they are not an OS-level race-proof directory sandbox.
/// </summary>
internal sealed class NativeFloraAppearanceApproval
{
    private const string Schema = "native-flora-appearance-approval-v1";
    private const int MaxRecordBytes = 1048576, MaxPathCharacters = 1024;
    private const long MaxDependencyBytes = 268435456, MaxDependencyTotalBytes = 1073741824;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private NativeFloraAppearanceApproval(string packageDirectory, FloraAppearancePin pin,
        string expectedRuntimeFingerprint, string expectedCatalogIdentity)
    {
        PackageDirectory = packageDirectory;
        Pin = pin;
        ExpectedRuntimeFingerprint = expectedRuntimeFingerprint;
        ExpectedCatalogIdentity = expectedCatalogIdentity;
    }

    internal string PackageDirectory { get; }
    internal FloraAppearancePin Pin { get; }
    internal string ExpectedRuntimeFingerprint { get; }
    internal string ExpectedCatalogIdentity { get; }

    internal static NativeFloraAppearanceApproval Read(string approvalPath, string expectedSha256,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        string expected = Hash(expectedSha256);
        try
        {
            string path = AbsolutePath(approvalPath);
            ValidateFilePath(path);
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.SequentialScan))
            {
                long length = stream.Length;
                if (length is < 1 or > MaxRecordBytes)
                    throw new FloraAppearanceCapacityException("Native flora approval exceeds its one-MiB byte bound.");
                bytes = new byte[checked((int)length)];
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int offset = 0;
                while (offset < bytes.Length)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int read = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
                    if (read == 0) throw Invalid("Truncated native flora approval.");
                    hash.AppendData(bytes, offset, read);
                    offset += read;
                }
                cancellation.ThrowIfCancellationRequested();
                if (stream.ReadByte() != -1 || stream.Length != length)
                    throw Invalid("Trailing or concurrently changed native flora approval.");
                if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expected, StringComparison.Ordinal))
                    throw Invalid("Native flora approval differs from its independent SHA-256 pin.");
                ValidateFilePath(path);
            }
            cancellation.ThrowIfCancellationRequested();
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = 12, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow,
            });
            JsonElement root = document.RootElement;
            Fields(root, "schema", "packageDirectory", "manifestSha256", "inputFingerprint",
                "runtimeFingerprint", "catalogIdentity", "inputs");
            Require(Text(root, "schema") == Schema, "Unsupported native flora approval schema.");
            string directory = AbsolutePath(Text(root, "packageDirectory", MaxPathCharacters));
            string manifest = Hash(Text(root, "manifestSha256"));
            string fingerprint = Hash(Text(root, "inputFingerprint"));
            string runtime = Hash(Text(root, "runtimeFingerprint"));
            string catalog = Hash(Text(root, "catalogIdentity"));
            FloraAppearanceInputContract inputs = ReadInputs(root.GetProperty("inputs"), cancellation);
            Require(inputs.Fingerprint == fingerprint, "Approval inputs differ from their canonical fingerprint.");
            cancellation.ThrowIfCancellationRequested();
            return new NativeFloraAppearanceApproval(directory,
                new FloraAppearancePin(FloraAppearanceCatalog.EarthAppearanceId, 1, manifest, inputs), runtime, catalog);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new FloraAppearanceValidationException("Cannot read the complete native flora approval record.", error); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or OverflowException or ArgumentException)
        { throw new FloraAppearanceValidationException("Malformed native flora approval record.", error); }
    }

    private static FloraAppearanceInputContract ReadInputs(JsonElement input, CancellationToken cancellation)
    {
        Fields(input, "schema", "recipeSeed", "views", "cellPixels", "nativeRowStart", "nativeRowEnd",
            "profile", "dependencies", "rows");
        Require(Text(input, "schema") == FloraAppearanceSchema.Input &&
            Integer(input, "views") == FloraAppearanceSchema.Views &&
            Integer(input, "cellPixels") == FloraAppearanceSchema.CellPixels &&
            Integer(input, "nativeRowStart") == FloraAppearanceSchema.NativeRowStart &&
            Integer(input, "nativeRowEnd") == FloraAppearanceSchema.NativeRowEnd,
            "Unsupported native flora input row/view contract.");
        int seed = Integer(input, "recipeSeed");
        JsonElement p = input.GetProperty("profile");
        Fields(p, "engineBuild", "renderer", "deviceProfile", "bakeRevision", "finishRevision", "projectionRevision");
        var profile = new FloraAppearanceBakeProfile(Text(p, "engineBuild", 160), Text(p, "renderer", 80),
            Text(p, "deviceProfile", 160), Text(p, "bakeRevision", 80), Text(p, "finishRevision", 80),
            Text(p, "projectionRevision", 80));

        JsonElement dependencyRows = Rows(input, "dependencies", 1, FloraAppearanceSchema.MaxDependencies);
        var dependencies = new FloraAppearanceDependency[dependencyRows.GetArrayLength()];
        long total = 0;
        for (int i = 0; i < dependencies.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            JsonElement d = dependencyRows[i];
            Fields(d, "key", "bytes", "sha256");
            JsonElement size = d.GetProperty("bytes");
            if (size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out long length) || length < 1)
                throw Invalid("Expected a positive native flora input byte length.");
            if (length > MaxDependencyBytes || length > MaxDependencyTotalBytes - total)
                throw new FloraAppearanceCapacityException("Native flora approval dependency byte bounds exceeded.");
            total += length;
            dependencies[i] = new FloraAppearanceDependency(Text(d, "key"), length, Hash(Text(d, "sha256")));
        }

        JsonElement rowElements = Rows(input, "rows", FloraAppearanceSchema.Rows, FloraAppearanceSchema.Rows);
        var rows = new FloraAppearanceRow[FloraAppearanceSchema.Rows];
        for (int i = 0; i < rows.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            JsonElement r = rowElements[i];
            Fields(r, "row", "kind", "variant", "variantKey", "speciesScale", "height", "extent",
                "lod0GeometrySha256", "lod1GeometrySha256", "materialSha256", "native");
            int kind = Integer(r, "kind");
            Require(kind is >= 0 and <= byte.MaxValue, "Invalid native flora species value.");
            rows[i] = new FloraAppearanceRow(Integer(r, "row"), (ScatterKind)kind, Integer(r, "variant"),
                Text(r, "variantKey", 80), Finite(r, "speciesScale"), Finite(r, "height"), Finite(r, "extent"),
                Hash(Text(r, "lod0GeometrySha256")), Hash(Text(r, "lod1GeometrySha256")), Hash(Text(r, "materialSha256")));
            JsonElement native = r.GetProperty("native");
            Require(native.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "Native flora row classification must be boolean.");
            Require(native.GetBoolean() == rows[i].Native, "Incorrect native flora row classification.");
        }
        // The public contract copies/sorts dependency keys, rejects duplicates and requires exact ordered rows.
        return new FloraAppearanceInputContract(seed, profile, dependencies, rows);
    }

    private static void ValidateFilePath(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw Invalid("Native flora approval must be a regular file without links/reparse points.");
        for (DirectoryInfo? directory = new FileInfo(path).Directory; directory != null; directory = directory.Parent)
        {
            FileAttributes attributes = File.GetAttributes(directory.FullName);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw Invalid("Native flora approval ancestors must be real directories without links/reparse points.");
        }
    }

    private static string AbsolutePath(string value)
    {
        CheckText(value, MaxPathCharacters);
        Require(value == value.Trim() && Path.IsPathFullyQualified(value), "Expected an absolute native flora approval/package path.");
        string path = Path.GetFullPath(value);
        Require(path.Length <= MaxPathCharacters, "Canonical native flora path exceeds its character bound.");
        return path;
    }

    private static void Fields(JsonElement value, params string[] fields)
    {
        Require(value.ValueKind == JsonValueKind.Object, "Expected a native flora approval object.");
        var remaining = new HashSet<string>(fields, StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            Require(remaining.Remove(property.Name), "Unknown or duplicate native flora approval property.");
        Require(remaining.Count == 0, "Missing native flora approval property.");
    }

    private static JsonElement Rows(JsonElement parent, string key, int minimum, int maximum)
    {
        JsonElement value = parent.GetProperty(key);
        Require(value.ValueKind == JsonValueKind.Array, "Expected a native flora approval array.");
        if (value.GetArrayLength() < minimum || value.GetArrayLength() > maximum)
            throw new FloraAppearanceCapacityException("Native flora approval array count exceeds its bounds.");
        return value;
    }

    private static string Text(JsonElement owner, string key, int maximum = 240)
    {
        JsonElement value = owner.GetProperty(key);
        Require(value.ValueKind == JsonValueKind.String, "Expected a native flora approval string.");
        string text = value.GetString()!;
        CheckText(text, maximum);
        return text;
    }

    private static void CheckText(string value, int maximum)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= maximum, "Invalid native flora approval string length.");
        foreach (char c in value) Require(!char.IsControl(c), "Control character in native flora approval string.");
        _ = StrictUtf8.GetByteCount(value);
    }

    private static int Integer(JsonElement owner, string key)
    {
        JsonElement value = owner.GetProperty(key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
            throw Invalid("Expected a native flora approval integer.");
        return number;
    }

    private static float Finite(JsonElement owner, string key)
    {
        JsonElement value = owner.GetProperty(key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out float number) || !float.IsFinite(number))
            throw Invalid("Expected a finite native flora approval scalar.");
        return number;
    }

    private static string Hash(string value)
    {
        Require(value != null && value.Length == 64, "Expected a native flora SHA-256 pin.");
        foreach (char c in value)
            Require(c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F', "Invalid native flora SHA-256 pin.");
        return value.ToUpperInvariant();
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw Invalid(message); }

    private static FloraAppearanceValidationException Invalid(string message) => new(message);
}
