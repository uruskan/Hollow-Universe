using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Isolated v2 correction gate. The v1 receipt is still the original lossless baseline;
    // the v2 journal never replaces it. No import, shared resource cache or shader is modified.
    private async Task CheckCoverLossless(string receiptPath, string receiptSha256, string parentReceiptSha256)
    {
        var timer = Stopwatch.StartNew();
        using Process process = Process.GetCurrentProcess();
        long peakWorkingSet = 0;
        string phase = "authenticate migration and original baseline";
        string? family = null, failure = null;
        bool completed = false, allMipEqualityVerified = false;
        CoverLosslessInput? input = null;
        var mips = new List<object>(12);
        try
        {
            Budget();
            input = CoverLosslessAuthenticate(receiptPath, receiptSha256, parentReceiptSha256, Budget);
            family = input.Family;
            Expect("cover lossless authenticated parent and migration", true,
                "Independent v2 and v1 receipt pins; exact policy history, source PNG/model identities, original mip baseline and one-byte migration");
            Expect("cover lossless other four maps unchanged", true,
                "Each current import and CTEX exactly matches the authenticated migration preimage journal");
            phase = "private native lossless texture pair";
            // Validate the stored mip descriptors AND their lossless WebP dimensions before native decoding.
            // A small CTEX file alone does not bound a decoder's output allocation.
            CoverLosslessCtex(input.Baseline);
            CoverLosslessCtex(input.Candidate);
            using (var originalTexture = new CompressedTexture2D())
            using (var candidateTexture = new CompressedTexture2D())
            {
                CoverCompressionRequire(originalTexture.Load(input.Baseline) == Error.Ok, "Cannot load original lossless CTEX");
                CoverCompressionRequire(candidateTexture.Load(input.Candidate) == Error.Ok, "Cannot load corrected lossless CTEX");
                using Image original = originalTexture.GetImage();
                using Image candidate = candidateTexture.GetImage();
                Budget();
                long expected = CoverCompressionPayload(false, 4);
                bool format = original.GetFormat() == Image.Format.Rgba8 && candidate.GetFormat() == Image.Format.Rgba8
                    && !original.IsCompressed() && !candidate.IsCompressed();
                bool dimensions = original.GetWidth() == 2048 && original.GetHeight() == 2048
                    && candidate.GetWidth() == 2048 && candidate.GetHeight() == 2048
                    && original.GetMipmapCount() == 11 && candidate.GetMipmapCount() == 11;
                bool payload = original.GetDataSize() == expected && candidate.GetDataSize() == expected;
                Expect("cover lossless native RGBA8", format, $"original={original.GetFormat()}, corrected={candidate.GetFormat()}");
                Expect("cover lossless native dimensions and all mips", dimensions, "2048x2048 with 11 lower mips in both private native images");
                Expect("cover lossless native payload", payload, $"expected {expected} bytes each; decoded image payload, not GPU residency");
                CoverCompressionRequire(format && dimensions && payload, "Native lossless image descriptor differs");
                // No conversion, resizing, normalization, decompression or mip regeneration here.
                byte[] a = original.GetData(), b = candidate.GetData();
                CoverCompressionRequire(a.LongLength == expected && b.LongLength == expected, "Unexpected native RGBA storage size");
                int contiguousOffset = 0;
                bool equal = true;
                for (int mip = 0; mip < 12; mip++)
                {
                    phase = "exact RGBA mip " + mip;
                    Budget();
                    int size = Math.Max(1, 2048 >> mip), bytes = checked(size * size * 4);
                    long offsetA = original.GetMipmapOffset(mip), offsetB = candidate.GetMipmapOffset(mip);
                    CoverCompressionRequire(offsetA == contiguousOffset && offsetB == contiguousOffset
                        && contiguousOffset <= a.Length - bytes, "Native mip storage is not the complete expected contiguous RGBA chain");
                    int firstMismatch = -1;
                    for (int index = 0; index < bytes; index++)
                    {
                        if ((index & 262143) == 0) Budget();
                        if (a[contiguousOffset + index] != b[contiguousOffset + index] && firstMismatch < 0) firstMismatch = index;
                    }
                    bool mipEqual = firstMismatch < 0;
                    equal &= mipEqual;
                    string originalSha = Convert.ToHexString(SHA256.HashData(a.AsSpan(contiguousOffset, bytes))).ToLowerInvariant();
                    string candidateSha = Convert.ToHexString(SHA256.HashData(b.AsSpan(contiguousOffset, bytes))).ToLowerInvariant();
                    Expect("cover lossless exact RGBA mip " + mip, mipEqual, $"{size}x{size}, {bytes} bytes; first mismatch={firstMismatch}");
                    mips.Add(new { mip, width = size, height = size, bytes, equal = mipEqual, originalSha256 = originalSha,
                        candidateSha256 = candidateSha, firstMismatchByte = firstMismatch,
                        firstMismatchChannel = firstMismatch < 0 ? null : "RGBA"[firstMismatch % 4].ToString() });
                    contiguousOffset += bytes;
                }
                CoverCompressionRequire(contiguousOffset == a.Length, "Native mip chain leaves unexamined bytes");
                allMipEqualityVerified = equal;
            }
            // Recheck the bytes used by native loading and every other map after the decode. This is an
            // isolated qualification run; it does not claim a transaction with a concurrent external writer.
            phase = "post-decode identity check";
            foreach (CoverLosslessPin pin in input.Recheck) { Budget(); CoverHash(pin.Path, pin.Sha256, pin.Maximum); }
            CoverCompressionRequire(allMipEqualityVerified, "Corrected packed N-T differs from the original RGBA mip baseline");
            Expect("cover lossless complete baseline equality", true, "All four channels, all pixels, all 12 original mips are byte-identical");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Budget();
            completed = true;
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Expect("cover lossless execution", false, failure);
        }
        finally
        {
            string report = JsonSerializer.Serialize(new
            {
                schema = "hollow.cover-lossless-native.v1", family, completed, phase, failure,
                receiptPath, receiptSha256, parentReceiptSha256, allMipEqualityVerified,
                seconds = timer.Elapsed.TotalSeconds, peakWorkingSet,
                renderer = RenderingServer.GetCurrentRenderingMethod(), driver = RenderingServer.GetCurrentRenderingDriverName(),
                input, mips, runtimeAppearanceApproved = false,
                interpretation = "Exact original v1 versus corrected v2 packed N-T RGBA bytes across every native mip. Other four imports and CTEX files remain journal-identical. No source texture, mip, channel decoder or shader changes; no artistic acceptance or physical GPU-residency claim. Private decoded image pair plus two managed byte arrays is bounded to 4*22369620 bytes, excluding native texture upload and engine overhead; process guard remains separate."
            }, new JsonSerializerOptions { WriteIndented = true });
            CoverCompressionRequire(Encoding.UTF8.GetByteCount(report) <= 256 * 1024, "Lossless report exceeds byte bound");
            File.WriteAllText(OutputPath + ".cover-lossless.json", report);
        }

        void Budget()
        {
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            if (timer.Elapsed.TotalSeconds > 35) throw new TimeoutException("Cover lossless exceeded its 35 second budget.");
            if (process.WorkingSet64 > 1536L * 1024 * 1024) throw new InvalidOperationException("Cover lossless exceeded its 1536 MiB process guard.");
        }
    }

    private sealed record CoverLosslessPin(string Path, string Sha256, int Maximum);
    private sealed record CoverLosslessInput(string Family, string Baseline, string Candidate,
        string BaselineSha256, string CandidateSha256, CoverLosslessPin[] Recheck);

    private static CoverLosslessInput CoverLosslessAuthenticate(string receiptPath, string receiptSha256,
        string parentReceiptSha256, Action budget)
    {
        const string oldPolicySha = "3407f8a44be725d868dec88ceca02307bd2741c03865e37d67528dd2a16714bf";
        const string policySha = "fc22b5256d552e27cc894b4e2e149486035538cd8c663ff48c5c307c52088652";
        const string catalogSha = "11f87351b1bf2486dd645c12d46edcaf2c0457c2f66db987dbf0dfbba6599239";
        const int jsonCap = 4 * 1024 * 1024, importCap = 128 * 1024, ctexCap = 32 * 1024 * 1024;
        string game = Path.GetFullPath(ProjectSettings.GlobalizePath("res://")).TrimEnd('\\', '/');
        string repository = Path.GetDirectoryName(game)!;
        string artifacts = Path.Combine(repository, ".artifacts");
        string receipt = CoverContained(artifacts, Path.GetRelativePath(artifacts, Path.GetFullPath(receiptPath)));
        CoverCompressionRequire(Regex.IsMatch(receiptSha256, "\\A[0-9a-f]{64}\\z")
            && Regex.IsMatch(parentReceiptSha256, "\\A[0-9a-f]{64}\\z"), "Both independently pinned receipt hashes are required");
        var recheck = new Dictionary<string, CoverLosslessPin>(StringComparer.OrdinalIgnoreCase);
        long admittedBytes = 0;
        Pin(receipt, receiptSha256, jsonCap);
        using JsonDocument document = CoverJson(receipt);
        JsonElement root = document.RootElement;
        string family = root.GetProperty("family").GetString()!;
        CoverCompressionRequire(family is "fern" or "wild" or "cotton" or "tundra", "Unknown corrected cover family");
        CoverCompressionRequire(root.GetProperty("schema").GetString() == "hollow.cover-texture-import-receipt.v2"
            && root.GetProperty("status").GetString() == "applied-awaiting-native-lossless-equality"
            && root.GetProperty("policySchema").GetString() == "hollow.cover-texture-import-policy.v2"
            && root.GetProperty("policySha256").GetString() == policySha && root.GetProperty("sourceAuthenticated").GetBoolean()
            && !root.GetProperty("runtimeAppearanceApproved").GetBoolean() && !root.GetProperty("allMipEqualityVerified").GetBoolean()
            && root.GetProperty("nativeImportRequired").GetBoolean() && root.GetProperty("exclusiveWindowsHandlesRequired").GetBoolean()
            && Path.GetFullPath(root.GetProperty("game").GetString()!).TrimEnd('\\', '/') == game
            && root.GetProperty("selectedImages").GetInt32() == 5 && root.GetProperty("changedImports").GetInt32() == 1,
            "Wrong migration policy, lifecycle, game or selection");
        string directory = Path.GetDirectoryName(receipt)!;
        JsonElement migration = root.GetProperty("migration");
        CoverCompressionRequire(migration.GetProperty("parentReceiptSha256").GetString() == parentReceiptSha256
            && migration.GetProperty("previousPolicySha256").GetString() == oldPolicySha, "Independent original receipt pin does not match migration");
        string parent = CoverContained(repository, migration.GetProperty("parentReceipt").GetString()!);
        parent = CoverContained(artifacts, Path.GetRelativePath(artifacts, parent));
        Pin(parent, parentReceiptSha256, jsonCap);
        Pin(CoverContained(directory, "parent-receipt.json"), parentReceiptSha256, jsonCap);
        using JsonDocument parentDocument = CoverJson(parent);
        JsonElement old = parentDocument.RootElement;
        CoverCompressionRequire(old.GetProperty("schema").GetString() == "hollow.cover-texture-import-receipt.v1"
            && old.GetProperty("status").GetString() == "applied-awaiting-native-import-and-visual-review"
            && old.GetProperty("family").GetString() == family && old.GetProperty("policySha256").GetString() == oldPolicySha
            && Path.GetFullPath(old.GetProperty("game").GetString()!).TrimEnd('\\', '/') == game
            && old.GetProperty("sourceAuthenticated").GetBoolean() && !old.GetProperty("runtimeAppearanceApproved").GetBoolean(),
            "Original receipt does not describe this family's immutable lossless baseline");
        CoverCompressionRequire(root.GetProperty("policySnapshot").GetString() == "policy.json", "Unexpected migration policy snapshot");
        string snapshot = CoverContained(directory, "policy.json");
        Pin(snapshot, policySha, jsonCap);
        string policyPath = CoverContained(repository, "tools/blender/data/ground_cover_texture_imports.json");
        Pin(policyPath, policySha, jsonCap);
        using JsonDocument policy = CoverJson(snapshot);
        JsonElement history = policy.RootElement.GetProperty("previousPolicy");
        CoverCompressionRequire(history.GetProperty("schema").GetString() == "hollow.cover-texture-import-policy.v1"
            && history.GetProperty("sha256").GetString() == oldPolicySha
            && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(history.GetProperty("utf8").GetString()!))).ToLowerInvariant() == oldPolicySha,
            "Original v1 policy bytes are not preserved");
        JsonElement recipe = policy.RootElement.GetProperty("families").EnumerateArray().Single(f => f.GetProperty("key").GetString() == family);
        var expectedSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string catalog = CoverContained(game, "assets/surface/ground-cover/catalog.json");
        Source(catalog, catalogSha, jsonCap);
        string modelResource = recipe.GetProperty("model").GetString()!;
        CoverCompressionRequire(modelResource == "res://assets/surface/ground-cover/" + family + ".glb", "Unexpected model mapping");
        string model = CoverContained(game, modelResource[6..]);
        Source(model, recipe.GetProperty("modelSha256").GetString()!, ctexCap);
        CoverCompressionRequire(new FileInfo(model).Length == recipe.GetProperty("modelBytes").GetInt64(), "Model length changed");
        expectedSources.Add(policyPath, policySha);
        var files = Rows(root, "files", 5); var originals = Rows(old, "files", 5);
        var embedded = Rows(migration, "baselineFiles", 5); var unchanged = Rows(root, "unchangedImports", 4);
        JsonElement[] images = Rows(recipe, "images", 5);
        string parentDirectory = Path.GetDirectoryName(parent)!;
        string? baseline = null, candidate = null, baselineSha = null, candidateSha = null;
        foreach (JsonElement image in images)
        {
            budget();
            string relative = image.GetProperty("path").GetString()!, role = image.GetProperty("role").GetString()!;
            string png = CoverContained(game, relative), path = relative + ".import";
            Source(png, image.GetProperty("sha256").GetString()!, 16 * 1024 * 1024);
            CoverCompressionRequire(new FileInfo(png).Length == image.GetProperty("bytes").GetInt64(), "Source PNG length changed");
            JsonElement entry = files.Single(f => f.GetProperty("path").GetString() == path);
            JsonElement original = originals.Single(f => f.GetProperty("path").GetString() == path);
            JsonElement embeddedOriginal = embedded.Single(f => f.GetProperty("path").GetString() == path);
            CoverCompressionRequire(CoverLosslessJsonEqual(original, embeddedOriginal), "Embedded baseline differs from independently pinned parent");
            CoverCompressionRequire(entry.GetProperty("role").GetString() == role && original.GetProperty("role").GetString() == role
                && entry.GetProperty("uid").GetString() == original.GetProperty("uid").GetString(), "Texture role or UID changed");
            string originalImport = CoverContained(parentDirectory, original.GetProperty("backupImport").GetString()!);
            Pin(originalImport, original.GetProperty("beforeSha256").GetString()!, importCap);
            string oldCtex = CoverContained(parentDirectory, original.GetProperty("backupCtex").GetString()!);
            string oldCtexSha = original.GetProperty("baselineCtexSha256").GetString()!;
            Pin(oldCtex, oldCtexSha, ctexCap);
            CoverCompressionRequire(new FileInfo(oldCtex).Length == original.GetProperty("baselineCtexBytes").GetInt64(), "Original CTEX length changed");
            string backup = CoverContained(directory, entry.GetProperty("backupImport").GetString()!);
            Pin(backup, entry.GetProperty("beforeSha256").GetString()!, importCap);
            string current = CoverContained(game, path);
            string currentSha = Pin(current, null, importCap);
            string before = CoverText(originalImport), preimage = CoverText(backup), after = CoverText(current);
            var oldParams = CoverParams(before); var priorParams = CoverParams(preimage); var currentParams = CoverParams(after);
            var settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["compress/mode"] = role == "billboard_normal_alpha" ? "0" : "2",
                ["compress/high_quality"] = role == "normal_xy" ? "false" : "true",
                ["compress/normal_map"] = role == "normal_xy" ? "1" : "2", ["detect_3d/compress_to"] = "0", ["mipmaps/generate"] = "true"
            };
            var received = entry.GetProperty("settings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
            CoverCompressionRequire(received.Count == 5 && settings.All(p => received.GetValueOrDefault(p.Key) == p.Value)
                && oldParams.Count == currentParams.Count && oldParams.All(p => currentParams.GetValueOrDefault(p.Key) == settings.GetValueOrDefault(p.Key, p.Value))
                && oldParams.Count == priorParams.Count && oldParams.All(p => priorParams.GetValueOrDefault(p.Key) ==
                    (p.Key == "compress/mode" ? "2" : settings.GetValueOrDefault(p.Key, p.Value)))
                && currentParams.GetValueOrDefault("mipmaps/limit") == "-1" && currentParams.GetValueOrDefault("process/size_limit") == "0",
                "Current or migration-preimage import parameters differ from their explicit policies");
            foreach (string text in new[] { preimage, after })
                CoverCompressionRequire(CoverField(text, "uid") == CoverField(before, "uid")
                    && CoverField(text, "uid") == entry.GetProperty("uid").GetString()
                    && CoverField(text, "importer") == "\"texture\"" && CoverField(text, "type") == "\"CompressedTexture2D\""
                    && CoverField(text, "source_file") == "\"res://" + relative + "\"" && CoverGenerator(text) == CoverGenerator(before),
                    "Stable source, channel mapping, UID or embedded generator changed");
            string mapped = CoverLosslessCtexPath(after);
            string currentCtex = CoverContained(game, mapped);
            if (role == "billboard_normal_alpha")
            {
                CoverCompressionRequire(baseline == null && migration.GetProperty("changedImport").GetString() == path
                    && mapped == original.GetProperty("baselineCtex").GetString(), "Corrected packed map does not map to the original lossless CTEX variant");
                byte[] raw = File.ReadAllBytes(backup);
                int offset = migration.GetProperty("changedByteOffset").GetInt32();
                CoverCompressionRequire(offset >= 0 && offset < raw.Length && raw[offset] == (byte)'2', "Invalid migration byte preimage");
                Match mode = Regex.Match(preimage, "(?m)^compress/mode=(2)\\r?$");
                CoverCompressionRequire(mode.Success && Encoding.UTF8.GetByteCount(preimage.AsSpan(0, mode.Groups[1].Index)) == offset,
                    "Migration offset is not compress/mode's value byte");
                raw[offset] = (byte)'0';
                CoverCompressionRequire(Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant() == entry.GetProperty("afterSha256").GetString(),
                    "Migration is not exactly the authenticated one-byte correction");
                Pin(CoverContained(directory, entry.GetProperty("backupCtex").GetString()!), entry.GetProperty("baselineCtexSha256").GetString()!, ctexCap);
                baseline = oldCtex; baselineSha = oldCtexSha; candidate = currentCtex; candidateSha = Pin(currentCtex, null, ctexCap);
            }
            else
            {
                JsonElement same = unchanged.Single(f => f.GetProperty("path").GetString() == path);
                CoverCompressionRequire(currentSha == same.GetProperty("sha256").GetString()
                    && currentSha == entry.GetProperty("beforeSha256").GetString() && currentSha == entry.GetProperty("afterSha256").GetString()
                    && mapped == same.GetProperty("ctex").GetString() && mapped == entry.GetProperty("baselineCtex").GetString()
                    && same.GetProperty("ctexSha256").GetString() == entry.GetProperty("baselineCtexSha256").GetString(),
                    "An unchanged map's import or native mapping changed");
                Pin(currentCtex, same.GetProperty("ctexSha256").GetString()!, ctexCap);
            }
        }
        // The extraction/model import is also unchanged. Authenticate the complete pinned source set,
        // including all v1 baseline backups in the v2 journal, while refusing arbitrary extra paths.
        string modelImport = CoverContained(game, modelResource[6..] + ".import");
        var parentSources = Rows(old, "sources", 9);
        JsonElement modelSource = parentSources.Single(s => SamePath(s.GetProperty("path").GetString()!, modelImport));
        Source(modelImport, modelSource.GetProperty("sha256").GetString()!, importCap);
        var parentSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement source in parentSources)
        {
            string path = Path.GetFullPath(source.GetProperty("path").GetString()!);
            CoverCompressionRequire(parentSeen.Add(path) && expectedSources.TryGetValue(path, out string? hash)
                && source.GetProperty("sha256").GetString() == (SamePath(path, policyPath) ? oldPolicySha : hash), "Original source membership changed");
        }
        expectedSources.Add(parent, parentReceiptSha256);
        foreach (JsonElement original in originals)
        {
            expectedSources.Add(CoverContained(parentDirectory, original.GetProperty("backupImport").GetString()!), original.GetProperty("beforeSha256").GetString()!);
            expectedSources.Add(CoverContained(parentDirectory, original.GetProperty("backupCtex").GetString()!), original.GetProperty("baselineCtexSha256").GetString()!);
        }
        var sources = Rows(root, "sources", expectedSources.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement source in sources)
        {
            string path = Path.GetFullPath(source.GetProperty("path").GetString()!);
            CoverCompressionRequire(seen.Add(path) && expectedSources.TryGetValue(path, out string? hash)
                && hash == source.GetProperty("sha256").GetString(), "Migration source membership or identity changed");
        }
        CoverCompressionRequire(baseline != null && candidate != null && baselineSha != null && candidateSha != null,
            "Missing unique packed N-T correction");
        return new(family, baseline!, candidate!, baselineSha!, candidateSha!, recheck.Values.ToArray());

        string Pin(string path, string? expected, int maximum)
        {
            budget();
            CoverCompressionRequire(expected == null || Regex.IsMatch(expected, "\\A[0-9a-f]{64}\\z"), "Invalid independent or recorded SHA-256");
            if (recheck.TryGetValue(path, out CoverLosslessPin? existing))
            { CoverCompressionRequire(expected == null || expected == existing.Sha256, "Conflicting file pins"); return existing.Sha256; }
            admittedBytes = checked(admittedBytes + new FileInfo(path).Length);
            CoverCompressionRequire(admittedBytes <= 384L * 1024 * 1024 && recheck.Count < 64, "Lossless input aggregate bound exceeded");
            string hash = CoverHash(path, expected, maximum);
            recheck.Add(path, new(path, hash, maximum));
            return hash;
        }
        void Source(string path, string expected, int maximum) { Pin(path, expected, maximum); expectedSources.Add(path, expected); }
        static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        static JsonElement[] Rows(JsonElement owner, string field, int count)
        {
            JsonElement rows = owner.GetProperty(field);
            CoverCompressionRequire(rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() == count, "Unexpected bounded " + field + " membership");
            return rows.EnumerateArray().ToArray();
        }
    }

    private static bool CoverLosslessJsonEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        if (a.ValueKind == JsonValueKind.Object)
        {
            var aa = a.EnumerateObject().ToArray(); var bb = b.EnumerateObject().ToArray();
            return aa.Length == bb.Length && aa.All(p => b.TryGetProperty(p.Name, out JsonElement value) && CoverLosslessJsonEqual(p.Value, value));
        }
        if (a.ValueKind == JsonValueKind.Array)
            return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => CoverLosslessJsonEqual(p.First, p.Second));
        return a.ValueKind == JsonValueKind.String ? a.GetString() == b.GetString() : a.GetRawText() == b.GetRawText();
    }

    private static string CoverLosslessCtexPath(string text)
    {
        MatchCollection paths = Regex.Matches(text, "(?m)^path(?:\\.[a-z0-9_]+)?=\"res://([^\"\\r\\n]+\\.ctex)\"\\r?$");
        CoverCompressionRequire(paths.Count == 1 && paths[0].Groups[1].Value.StartsWith(".godot/imported/", StringComparison.Ordinal),
            "Expected one project-owned native texture mapping");
        return paths[0].Groups[1].Value;
    }

    private static void CoverLosslessCtex(string path)
    {
        // Explicit current GST2/v1 WebP-lossless scope. Unknown encodings are refused, not decoded optimistically.
        using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
        CoverCompressionRequire(stream.Length >= 52 && stream.Length <= 32 * 1024 * 1024, "Lossless CTEX file bound exceeded");
        Span<byte> header = stackalloc byte[52]; stream.ReadExactly(header);
        static uint U(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
        CoverCompressionRequire(header[..4].SequenceEqual("GST2"u8) && U(header, 4) == 1 && U(header, 8) == 2048 && U(header, 12) == 2048
            && U(header, 36) == 2 && BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(40, 2)) == 2048
            && BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(42, 2)) == 2048 && U(header, 44) == 11 && U(header, 48) == (uint)Image.Format.Rgba8,
            "Unsupported bounded lossless CTEX descriptor");
        Span<byte> word = stackalloc byte[4]; Span<byte> webp = stackalloc byte[25];
        for (int mip = 0; mip < 12; mip++)
        {
            stream.ReadExactly(word); uint size = BinaryPrimitives.ReadUInt32LittleEndian(word);
            long start = stream.Position, end = start + size;
            CoverCompressionRequire(size >= 25 && end <= stream.Length, "Truncated lossless mip payload");
            stream.ReadExactly(webp);
            CoverCompressionRequire(webp[..4].SequenceEqual("RIFF"u8) && webp.Slice(8, 4).SequenceEqual("WEBP"u8)
                && BinaryPrimitives.ReadUInt32LittleEndian(webp.Slice(4, 4)) + 8L == size
                && webp.Slice(12, 4).SequenceEqual("VP8L"u8) && webp[20] == 0x2f,
                "Mip is not the expected simple lossless WebP stream");
            uint chunk = BinaryPrimitives.ReadUInt32LittleEndian(webp.Slice(16, 4));
            uint dimensions = BinaryPrimitives.ReadUInt32LittleEndian(webp.Slice(21, 4));
            int expected = Math.Max(1, 2048 >> mip);
            CoverCompressionRequire(chunk >= 5 && 20L + chunk + (chunk & 1) == size
                && (dimensions & 0x3fff) + 1 == expected && ((dimensions >> 14) & 0x3fff) + 1 == expected
                && (dimensions >> 29) == 0, "Lossless WebP output dimensions or stream version differ");
            stream.Position = end;
        }
        CoverCompressionRequire(stream.Position == stream.Length, "Unexpected bytes after the complete lossless mip chain");
    }
}
