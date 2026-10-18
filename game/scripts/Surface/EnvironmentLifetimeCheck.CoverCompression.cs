using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Isolated, explicitly selected family. This is an identity/format gate and a numerical
    // diagnostic, not an automatic artistic acceptance threshold or a physical VRAM measurement.
    private async Task CheckCoverCompression(string receiptPath, string receiptSha256)
    {
        var timer = Stopwatch.StartNew();
        using Process process = Process.GetCurrentProcess();
        long peakWorkingSet = 0;
        string phase = "authenticate receipt";
        bool completed = false;
        string? family = null;
        var maps = new List<object>();
        string? failure = null;
        try
        {
            Budget();
            CoverCompressionFamily qualification = CoverCompressionAuthenticate(receiptPath, receiptSha256);
            family = qualification.Family;
            CoverCompressionInput[] inputs = qualification.Inputs;
            Expect("cover compression receipt authenticates exact " + family + " sources and baseline", true,
                "five original embedded/extracted PNG identities; exact saved imports and CTEX; current policy and original UID/generator preserved");
            // Only compact visibility masks survive a texture pair. Color is processed first so
            // normal/roughness errors can exclude the original invisible atlas background.
            var masks = new Dictionary<string, byte[][]>(StringComparer.Ordinal);
            foreach (CoverCompressionInput input in inputs.OrderBy(i => i.Role == "albedo_cutout" ? 0 : 1))
            {
                phase = "native texture pair " + input.Name;
                Budget();
                object result = Compare(input);
                maps.Add(result);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Budget();
            }
            completed = true;

            object Compare(CoverCompressionInput input)
            {
                // These objects are private loads, so disposal cannot invalidate a live scene's
                // shared ResourceLoader cache. The baseline is its original CTEX, never a PNG approximation.
                using var originalTexture = new CompressedTexture2D();
                using var candidateTexture = new CompressedTexture2D();
                CoverCompressionRequire(originalTexture.Load(input.Baseline) == Error.Ok, "Cannot load original CTEX: " + input.Name);
                CoverCompressionRequire(candidateTexture.Load(input.Candidate) == Error.Ok, "Cannot load candidate CTEX: " + input.Name);
                using Image original = originalTexture.GetImage();
                using Image candidate = candidateTexture.GetImage();
                Budget();
                Image.Format expectedOriginal = input.Role is "normal_xy" or "packed_ort" ? Image.Format.Rgb8 : Image.Format.Rgba8;
                Image.Format expectedCandidate = input.Role == "normal_xy" ? Image.Format.RgtcRg : Image.Format.BptcRgba;
                long originalBytes = original.GetDataSize(), candidateBytes = candidate.GetDataSize();
                long expectedOriginalBytes = CoverCompressionPayload(false, expectedOriginal == Image.Format.Rgb8 ? 3 : 4);
                long expectedCandidateBytes = CoverCompressionPayload(true, 16);
                bool format = original.GetFormat() == expectedOriginal && candidate.GetFormat() == expectedCandidate;
                bool dimensions = original.GetWidth() == 2048 && original.GetHeight() == 2048
                    && candidate.GetWidth() == 2048 && candidate.GetHeight() == 2048
                    && original.GetMipmapCount() == 11 && candidate.GetMipmapCount() == 11;
                bool payload = originalBytes == expectedOriginalBytes && candidateBytes == expectedCandidateBytes;
                Expect("cover native format " + input.Name, format,
                    $"baseline={original.GetFormat()}, candidate={candidate.GetFormat()}, expected={expectedOriginal}/{expectedCandidate}");
                Expect("cover native dimensions and complete mips " + input.Name, dimensions,
                    $"baseline={original.GetWidth()}x{original.GetHeight()}+{original.GetMipmapCount()}, candidate={candidate.GetWidth()}x{candidate.GetHeight()}+{candidate.GetMipmapCount()}");
                Expect("cover native payload " + input.Name, payload,
                    $"baseline={originalBytes}, candidate={candidateBytes}; expected={expectedOriginalBytes}/{expectedCandidateBytes}; image payload, not driver residency");
                CoverCompressionRequire(format && dimensions && payload, "Native format/dimension/payload gate failed: " + input.Name);
                // Preserve the imported mip chain. Never regenerate, resize or synthesize a baseline.
                CoverCompressionRequire(!original.IsCompressed(), "Original pilot baseline must be lossless RGB(A)");
                CoverCompressionRequire(candidate.Decompress() == Error.Ok, "Native candidate decompression failed");
                original.Convert(Image.Format.Rgba8);
                candidate.Convert(Image.Format.Rgba8);
                CoverCompressionRequire(original.GetMipmapCount() == 11 && candidate.GetMipmapCount() == 11,
                    "Native decompression/conversion changed the mip count");
                byte[] a = original.GetData(), b = candidate.GetData();
                CoverCompressionRequire(a.Length == CoverCompressionPayload(false, 4) && b.Length == a.Length,
                    "Unexpected converted mip storage size");
                byte[][]? visibility = null;
                if (input.Role == "albedo_cutout")
                {
                    visibility = new byte[12][];
                    CoverCompressionRequire(masks.TryAdd(input.Material, visibility), "Duplicate visibility mask");
                }
                else CoverCompressionRequire(masks.TryGetValue(input.Material, out visibility), "Missing corresponding material alpha mask");
                var mipReports = new List<object>();
                for (int mip = 0; mip < 12; mip++)
                {
                    phase = input.Name + " mip " + mip;
                    Budget();
                    int size = Math.Max(1, 2048 >> mip), pixels = size * size;
                    long nativeOffsetA = original.GetMipmapOffset(mip), nativeOffsetB = candidate.GetMipmapOffset(mip);
                    CoverCompressionRequire(nativeOffsetA >= 0 && nativeOffsetB >= 0
                        && nativeOffsetA <= a.Length - pixels * 4L && nativeOffsetB <= b.Length - pixels * 4L,
                        "Native mip range is invalid");
                    int offsetA = checked((int)nativeOffsetA), offsetB = checked((int)nativeOffsetB);
                    byte[] mask = input.Role == "albedo_cutout" ? visibility![mip] = new byte[pixels] : visibility![mip];
                    CoverCompressionRequire(mask.Length == pixels, "Material alpha mip dimensions differ");
                    var channels = new[] { new CoverError(), new CoverError(), new CoverError(), new CoverError() };
                    var opaqueEncoded = new CoverError();
                    var opaqueLinear = new CoverError();
                    var roughness = new CoverError();
                    var clampedRoughness = new CoverError();
                    var normal = new CoverAngle();
                    var packedNormal = new CoverAngle();
                    CoverPackedNormalStrata? packedStrata = input.Role == "billboard_normal_alpha" ? new() : null;
                    double[] cutoffs = { 0.42, 0.5, 0.75, 0.95 };
                    long[] originalCoverage = new long[4], candidateCoverage = new long[4], lost = new long[4], gained = new long[4];
                    long originalVisible = 0, intersectionVisible = 0;
                    for (int p = 0; p < pixels; p++)
                    {
                        if ((p & 65535) == 0) Budget();
                        int ia = offsetA + p * 4, ib = offsetB + p * 4;
                        if (input.Role == "albedo_cutout")
                            mask[p] = (byte)((a[ia + 3] / 255.0 >= 0.42 ? 1 : 0) | (b[ib + 3] / 255.0 >= 0.42 ? 2 : 0));
                        for (int channel = 0; channel < 4; channel++) channels[channel].Add(a[ia + channel] / 255.0, b[ib + channel] / 255.0);
                        bool baselineOpaque = (mask[p] & 1) != 0, bothOpaque = mask[p] == 3;
                        if (baselineOpaque) originalVisible++;
                        if (bothOpaque) intersectionVisible++;
                        if (input.Role is "albedo_cutout" or "billboard_normal_alpha")
                            for (int c = 0; c < cutoffs.Length; c++)
                            {
                                bool ca = a[ia + 3] / 255.0 >= cutoffs[c], cb = b[ib + 3] / 255.0 >= cutoffs[c];
                                if (ca) originalCoverage[c]++;
                                if (cb) candidateCoverage[c]++;
                                if (ca && !cb) lost[c]++;
                                if (!ca && cb) gained[c]++;
                            }
                        if (input.Role == "albedo_cutout" && bothOpaque)
                            for (int channel = 0; channel < 3; channel++)
                            {
                                opaqueEncoded.Add(a[ia + channel] / 255.0, b[ib + channel] / 255.0);
                                opaqueLinear.Add(CoverLinear[a[ia + channel]], CoverLinear[b[ib + channel]]);
                            }
                        if (input.Role == "packed_ort" && baselineOpaque)
                        {
                            double ra = a[ia + 1] / 255.0, rb = b[ib + 1] / 255.0;
                            roughness.Add(ra, rb);
                            clampedRoughness.Add(Math.Clamp(ra, 0.65, 1), Math.Clamp(rb, 0.65, 1));
                        }
                        if (input.Role == "normal_xy" && baselineOpaque)
                            normal.Add(a[ia], a[ia + 1], b[ib], b[ib + 1]);
                        if (input.Role == "billboard_normal_alpha" && baselineOpaque)
                        {
                            packedNormal.AddRgb(a[ia], a[ia + 1], a[ia + 2], b[ib], b[ib + 1], b[ib + 2]);
                            packedStrata!.Add(p % size, p / size, a[ia], a[ia + 1], a[ia + 2], a[ia + 3],
                                b[ib], b[ib + 1], b[ib + 2], b[ib + 3]);
                        }
                    }
                    mipReports.Add(new
                    {
                        mip, width = size, height = size, pixels, originalVisible, intersectionVisible,
                        channelErrors = channels.Select(c => c.Report()).ToArray(),
                        alphaCutoffs = cutoffs.Select((cutoff, c) => new { cutoff, baseline = originalCoverage[c], candidate = candidateCoverage[c], lost = lost[c], gained = gained[c] }).ToArray(),
                        opaqueColorEncoded = opaqueEncoded.Report(), opaqueColorLinear = opaqueLinear.Report(),
                        roughness = roughness.Report(), shaderClampedRoughness = clampedRoughness.Report(),
                        reconstructedNormalDegrees = normal.Report(), packedRgbNormalDegrees = packedNormal.Report(),
                        packedRgbNormalSensitivity = packedStrata?.Report()
                    });
                }
                return new { input.Name, input.Role, input.Material, input.BaselineSha256, input.CandidateSha256,
                    baselineFormat = expectedOriginal.ToString(), candidateFormat = expectedCandidate.ToString(),
                    originalBytes, candidateBytes, payloadSavedBytes = originalBytes - candidateBytes, mips = mipReports };
            }
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Expect("cover compression execution", false, failure);
        }
        finally
        {
            File.WriteAllText(OutputPath + ".cover-compression.json", JsonSerializer.Serialize(new
            {
                schema = "hollow.cover-compression-native.v3", family, completed, phase, failure,
                receiptPath = Path.GetFullPath(receiptPath), receiptSha256, seconds = timer.Elapsed.TotalSeconds, peakWorkingSet,
                renderer = RenderingServer.GetCurrentRenderingMethod(), driver = RenderingServer.GetCurrentRenderingDriverName(),
                runtimeAppearanceApproved = false,
                interpretation = "All texels in all imported mips, native CTEX decode. Color uses intersection of alpha>=0.42. Normal and roughness use baseline-visible pixels of the corresponding material. Pure N reconstructs positive Z from signed RG at unit strength. Packed billboard N-T retains the v2 normalized signed RGB XYZ angle metric. Additional source/candidate vector-length distributions, source-length-conditioned angles and source alpha/T strata diagnose normalization sensitivity versus changed well-sized directions; their bands are diagnostic, not artistic validity thresholds or a new decoder. Alpha/T strata use the packed map's independent alpha, not cutout opacity. Four worst baseline-albedo-visible texels per mip retain raw RGBA and locations for follow-up. Current production billboard shading does not consume its normal texture. No lossy appearance threshold or physical GPU-residency claim.",
                maps
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        void Budget()
        {
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            if (timer.Elapsed.TotalSeconds > 35) throw new TimeoutException("Cover compression exceeded its 35 second budget.");
            if (process.WorkingSet64 > 1536L * 1024 * 1024) throw new InvalidOperationException("Cover compression exceeded its 1536 MiB process guard.");
        }
    }

    private sealed record CoverCompressionInput(string Name, string Source, string Role, string Material, string Baseline,
        string Candidate, string BaselineSha256, string CandidateSha256);
    private sealed record CoverCompressionFamily(string Family, string Model, string ModelSha256, CoverCompressionInput[] Inputs);

    private static CoverCompressionFamily CoverCompressionAuthenticate(string receiptPath, string receiptSha256)
    {
        const string policySha = "3407f8a44be725d868dec88ceca02307bd2741c03865e37d67528dd2a16714bf";
        const string catalogSha = "11f87351b1bf2486dd645c12d46edcaf2c0457c2f66db987dbf0dfbba6599239";
        string game = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"));
        string repository = Path.GetDirectoryName(game.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
        string artifacts = Path.Combine(repository, ".artifacts");
        string receipt = CoverContained(artifacts, Path.GetRelativePath(artifacts, Path.GetFullPath(receiptPath)));
        // The coordinator pins this hash when apply completes, outside the receipt itself.
        // Self-declared hashes in an editable receipt cannot authenticate an original baseline.
        CoverCompressionRequire(Regex.IsMatch(receiptSha256, "\\A[0-9a-f]{64}\\z"), "An independently pinned receipt SHA-256 is required");
        CoverHash(receipt, receiptSha256, 4 * 1024 * 1024);
        string directory = Path.GetDirectoryName(receipt)!;
        string policyPath = CoverContained(repository, "tools/blender/data/ground_cover_texture_imports.json");
        CoverHash(policyPath, policySha, 4 * 1024 * 1024);
        using JsonDocument policy = CoverJson(policyPath);
        using JsonDocument document = CoverJson(receipt);
        JsonElement root = document.RootElement;
        string family = root.GetProperty("family").GetString()!;
        CoverCompressionRequire(family is "fern" or "wild" or "cotton" or "tundra", "Select exactly one known cover family");
        CoverCompressionRequire(root.GetProperty("schema").GetString() == "hollow.cover-texture-import-receipt.v1"
            && root.GetProperty("status").GetString() == "applied-awaiting-native-import-and-visual-review"
            && root.GetProperty("policySha256").GetString() == policySha
            && root.GetProperty("sourceAuthenticated").GetBoolean() && !root.GetProperty("runtimeAppearanceApproved").GetBoolean()
            && root.GetProperty("nativeImportRequired").GetBoolean()
            && Path.GetFullPath(root.GetProperty("game").GetString()!).TrimEnd('\\', '/') == game.TrimEnd('\\', '/'),
            "Receipt does not describe this unapproved cover family comparison");
        string catalog = CoverContained(game, "assets/surface/ground-cover/catalog.json");
        JsonElement familyPolicy = policy.RootElement.GetProperty("families").EnumerateArray().Single(f => f.GetProperty("key").GetString() == family);
        string modelResource = familyPolicy.GetProperty("model").GetString()!;
        string modelSha = familyPolicy.GetProperty("modelSha256").GetString()!;
        CoverCompressionRequire(modelResource == "res://assets/surface/ground-cover/" + family + ".glb", "Unexpected family model mapping");
        string model = CoverContained(game, modelResource[6..]);
        CoverHash(catalog, catalogSha, 4 * 1024 * 1024);
        CoverHash(model, modelSha, 32 * 1024 * 1024);
        CoverCompressionRequire(new FileInfo(model).Length == familyPolicy.GetProperty("modelBytes").GetInt64(), "Family model length changed");
        var recipes = familyPolicy.GetProperty("images").EnumerateArray().ToArray();
        var entries = root.GetProperty("files").EnumerateArray().ToArray();
        CoverCompressionRequire(entries.Length == 5 && recipes.Length == 5 && root.GetProperty("selectedImages").GetInt32() == 5,
            "Expected exactly five authenticated maps for one family");
        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [catalog] = catalogSha, [model] = modelSha, [policyPath] = policySha
        };
        var result = new List<CoverCompressionInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement recipe in recipes)
        {
            string relative = recipe.GetProperty("path").GetString()!, role = recipe.GetProperty("role").GetString()!;
            string png = CoverContained(game, relative);
            string pngSha = recipe.GetProperty("sha256").GetString()!;
            CoverHash(png, pngSha, 16 * 1024 * 1024);
            CoverCompressionRequire(new FileInfo(png).Length == recipe.GetProperty("bytes").GetInt64(), "PNG length changed");
            sourceHashes.Add(png, pngSha);
            JsonElement entry = entries.Single(e => e.GetProperty("path").GetString() == relative + ".import");
            CoverCompressionRequire(seen.Add(relative) && entry.GetProperty("role").GetString() == role, "Duplicate image or role mismatch");
            string originalImport = CoverContained(directory, entry.GetProperty("backupImport").GetString()!);
            string baseline = CoverContained(directory, entry.GetProperty("backupCtex").GetString()!);
            string baselineSha = entry.GetProperty("baselineCtexSha256").GetString()!;
            CoverHash(originalImport, entry.GetProperty("beforeSha256").GetString()!, 128 * 1024);
            CoverHash(baseline, baselineSha, 32 * 1024 * 1024);
            CoverCompressionRequire(new FileInfo(baseline).Length == entry.GetProperty("baselineCtexBytes").GetInt64(), "Baseline CTEX length changed");
            string before = CoverText(originalImport), after = CoverText(CoverContained(game, relative + ".import"));
            Dictionary<string, string> oldParameters = CoverParams(before), currentParameters = CoverParams(after);
            var settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["compress/mode"] = "2", ["compress/high_quality"] = role == "normal_xy" ? "false" : "true",
                ["compress/normal_map"] = role == "normal_xy" ? "1" : "2", ["mipmaps/generate"] = "true", ["detect_3d/compress_to"] = "0"
            };
            var received = entry.GetProperty("settings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
            CoverCompressionRequire(received.Count == settings.Count && settings.All(p => received.GetValueOrDefault(p.Key) == p.Value), "Receipt policy was altered");
            CoverCompressionRequire(oldParameters.Count == currentParameters.Count && oldParameters.All(p =>
                currentParameters.GetValueOrDefault(p.Key) == settings.GetValueOrDefault(p.Key, p.Value)), "Unrelated native import parameters changed");
            CoverCompressionRequire(currentParameters.GetValueOrDefault("mipmaps/limit") == "-1"
                && currentParameters.GetValueOrDefault("process/size_limit") == "0", "Resolution/mip policy changed");
            string uid = entry.GetProperty("uid").GetString()!;
            CoverCompressionRequire(CoverField(before, "uid") == uid && CoverField(after, "uid") == uid
                && CoverField(after, "importer") == "\"texture\"" && CoverField(after, "type") == "\"CompressedTexture2D\""
                && CoverField(after, "source_file") == "\"res://" + relative + "\""
                && CoverGenerator(before) == CoverGenerator(after), "Source, UID or embedded generator identity changed");
            MatchCollection paths = Regex.Matches(after, "(?m)^path(?:\\.[a-z0-9_]+)?=\"res://([^\"\\r\\n]+\\.ctex)\"\\r?$");
            CoverCompressionRequire(paths.Count == 1, "Review multiple platform candidates explicitly");
            string candidateRelative = paths[0].Groups[1].Value;
            CoverCompressionRequire(candidateRelative.StartsWith(".godot/imported/", StringComparison.Ordinal), "Candidate escaped native import cache");
            string candidate = CoverContained(game, candidateRelative);
            string candidateSha = CoverHash(candidate, null, 32 * 1024 * 1024);
            string use = recipe.GetProperty("uses").EnumerateArray().Single().GetString()!;
            result.Add(new(Path.GetFileNameWithoutExtension(png), "res://" + relative, role, use.Split('/')[0], baseline, candidate, baselineSha, candidateSha));
        }
        // This scene import is not modified by the tool; its exact extraction/remapping receipt
        // must also remain unchanged. A native rewrite requires explicit investigation, not fallback.
        string modelImport = model + ".import";
        JsonElement[] sources = root.GetProperty("sources").EnumerateArray().ToArray();
        CoverCompressionRequire(sources.Length == sourceHashes.Count + 1, "Unexpected source receipt set");
        foreach (JsonElement source in sources)
        {
            string path = Path.GetFullPath(source.GetProperty("path").GetString()!);
            string sha = source.GetProperty("sha256").GetString()!;
            if (string.Equals(path, modelImport, StringComparison.OrdinalIgnoreCase))
            {
                CoverCompressionRequire(!sourceHashes.ContainsKey(modelImport), "Duplicate scene import receipt");
                sourceHashes.Add(modelImport, sha);
            }
            CoverCompressionRequire(sourceHashes.TryGetValue(path, out string? expected) && sha == expected,
                "Unknown or changed source receipt");
        }
        CoverCompressionRequire(sourceHashes.ContainsKey(modelImport)
            && sources.Select(s => Path.GetFullPath(s.GetProperty("path").GetString()!)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == sources.Length,
            "Missing or duplicate source receipt");
        CoverHash(modelImport, sourceHashes[modelImport], 128 * 1024);
        return new(family, modelResource, modelSha, result.ToArray());
    }

    private static string CoverContained(string root, string relative)
    {
        CoverCompressionRequire(!Path.IsPathRooted(relative) && !relative.Split('/', '\\').Any(p => p is ".." or "."), "Diagnostic path must stay within its owner");
        string owner = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(owner, relative));
        CoverCompressionRequire(result.StartsWith(owner, StringComparison.OrdinalIgnoreCase) && File.Exists(result), "Missing or escaped diagnostic input");
        for (string? path = result; path != null && path.Length >= owner.TrimEnd('\\', '/').Length; path = Path.GetDirectoryName(path))
            CoverCompressionRequire((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Diagnostic receipt paths cannot use reparse points");
        return result;
    }

    private static string CoverHash(string path, string? expected, int maximum)
    {
        CoverCompressionRequire(new FileInfo(path).Length <= maximum, "Diagnostic file exceeds byte bound");
        using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        CoverCompressionRequire(stream.Length <= maximum && (expected == null || actual == expected), "Diagnostic hash mismatch: " + path);
        return actual;
    }

    private static JsonDocument CoverJson(string path)
    {
        CoverCompressionRequire(new FileInfo(path).Length <= 4 * 1024 * 1024, "Diagnostic JSON exceeds byte bound");
        using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
        JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
        try { Visit(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                { CoverCompressionRequire(names.Add(property.Name), "Duplicate diagnostic JSON field"); Visit(property.Value); }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (JsonElement child in element.EnumerateArray()) Visit(child);
        }
    }

    private static string CoverText(string path)
    {
        CoverCompressionRequire(new FileInfo(path).Length <= 128 * 1024, "Import text exceeds byte bound");
        return File.ReadAllText(path);
    }

    private static Dictionary<string, string> CoverParams(string text)
    {
        MatchCollection sections = Regex.Matches(text, "(?m)^\\[params\\]\\r?$");
        CoverCompressionRequire(sections.Count == 1, "Missing/duplicate import params");
        string block = text[(sections[0].Index + sections[0].Length)..];
        CoverCompressionRequire(!Regex.IsMatch(block, "(?m)^\\["), "Unexpected section after params");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(block, "(?m)^([A-Za-z_][A-Za-z0-9_/]*)=([^\\r\\n]*)"))
            CoverCompressionRequire(result.TryAdd(match.Groups[1].Value, match.Groups[2].Value), "Duplicate import parameter");
        return result;
    }

    private static string CoverField(string text, string key)
    {
        MatchCollection matches = Regex.Matches(text, "(?m)^" + Regex.Escape(key) + "=([^\\r\\n]*)");
        CoverCompressionRequire(matches.Count == 1, "Missing/duplicate native import field: " + key);
        return matches[0].Groups[1].Value;
    }

    private static string CoverGenerator(string text)
    {
        MatchCollection matches = Regex.Matches(text, "\"md5\":\\s*\"([0-9a-f]{32})\"");
        CoverCompressionRequire(matches.Count == 1, "Missing/duplicate embedded generator fingerprint");
        return matches[0].Groups[1].Value;
    }

    private static long CoverCompressionPayload(bool compressed, int unitBytes)
    {
        long total = 0;
        for (int size = 2048; size > 0; size >>= 1)
        { int units = compressed ? Math.Max(1, (size + 3) / 4) : size; total += (long)units * units * unitBytes; }
        return total;
    }

    private static void CoverCompressionRequire(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static readonly double[] CoverLinear = Enumerable.Range(0, 256).Select(b =>
    { double c = b / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }).ToArray();

    private sealed class CoverError
    {
        private long _count;
        private double _absolute, _square, _signed, _maximum;
        public void Add(double baseline, double candidate)
        {
            double delta = candidate - baseline;
            _count++; _absolute += Math.Abs(delta); _square += delta * delta; _signed += delta;
            _maximum = Math.Max(_maximum, Math.Abs(delta));
        }
        public object Report() => new { samples = _count, meanAbsolute = _count == 0 ? (double?)null : _absolute / _count,
            rms = _count == 0 ? (double?)null : Math.Sqrt(_square / _count),
            meanSigned = _count == 0 ? (double?)null : _signed / _count, maximum = _count == 0 ? (double?)null : _maximum };
    }

    private sealed class CoverAngle
    {
        private readonly long[] _histogram = new long[1801];
        private readonly CoverError _errors = new();
        private long _count;
        public void Add(byte ar, byte ag, byte br, byte bg)
        {
            double ax = ar / 127.5 - 1, ay = ag / 127.5 - 1, bx = br / 127.5 - 1, by = bg / 127.5 - 1;
            double az = Math.Sqrt(Math.Max(0, 1 - ax * ax - ay * ay)), bz = Math.Sqrt(Math.Max(0, 1 - bx * bx - by * by));
            AddVectors(ax, ay, az, bx, by, bz);
        }
        public void AddRgb(byte ar, byte ag, byte ab, byte br, byte bg, byte bb) =>
            AddVectors(ar / 127.5 - 1, ag / 127.5 - 1, ab / 127.5 - 1, br / 127.5 - 1, bg / 127.5 - 1, bb / 127.5 - 1);
        private void AddVectors(double ax, double ay, double az, double bx, double by, double bz)
        {
            double dot = (ax * bx + ay * by + az * bz) / Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
            double degrees = Math.Acos(Math.Clamp(dot, -1, 1)) * (180 / Math.PI);
            AddDegrees(degrees);
        }
        public void AddDegrees(double degrees)
        {
            _errors.Add(0, degrees); _histogram[Math.Min(1800, (int)Math.Floor(degrees * 10))]++; _count++;
        }
        public object Report()
        {
            long cumulative = 0; double? p95UpperBound = null;
            for (int i = 0; i < _histogram.Length && _count > 0; i++)
            { cumulative += _histogram[i]; if (cumulative >= Math.Ceiling(_count * 0.95)) { p95UpperBound = Math.Min(180, (i + 1) / 10.0); break; } }
            return new { errors = _errors.Report(), p95UpperBound, histogramBinDegrees = 0.1 };
        }
    }

    private sealed class CoverPackedNormalStrata
    {
        private static readonly double[] LengthUpper = { 0.1, 0.25, 0.5, 0.75, 0.9, 1.1, 1.25, 1.75 };
        private readonly CoverLengthDistribution _baseline = new(), _candidate = new();
        private readonly CoverPackedStratum[] _byLength = Enumerable.Range(0, LengthUpper.Length).Select(_ => new CoverPackedStratum()).ToArray();
        private readonly CoverPackedStratum[] _byAlpha = Enumerable.Range(0, 4).Select(_ => new CoverPackedStratum()).ToArray();
        private readonly CoverPackedStratum _sourceLength075To125 = new();
        private readonly CoverPackedWorst[] _worst = new CoverPackedWorst[4];
        private int _worstCount;

        public void Add(int x, int y, byte ar, byte ag, byte ab, byte aa, byte br, byte bg, byte bb, byte ba)
        {
            double ax = ar / 127.5 - 1, ay = ag / 127.5 - 1, az = ab / 127.5 - 1;
            double bx = br / 127.5 - 1, by = bg / 127.5 - 1, bz = bb / 127.5 - 1;
            double al = Math.Sqrt(ax * ax + ay * ay + az * az), bl = Math.Sqrt(bx * bx + by * by + bz * bz);
            // Byte-valued signed channels cannot form an exact zero vector (zero would require 127.5).
            double degrees = Math.Acos(Math.Clamp((ax * bx + ay * by + az * bz) / (al * bl), -1, 1)) * (180 / Math.PI);
            int aBand = Band(al), bBand = Band(bl);
            _baseline.Add(al, aBand); _candidate.Add(bl, bBand);
            double rgbError = (Math.Abs(ar - br) + Math.Abs(ag - bg) + Math.Abs(ab - bb)) / (3 * 255.0);
            _byLength[aBand].Add(al, bl, degrees, rgbError);
            _byAlpha[aa / 64].Add(al, bl, degrees, rgbError);
            if (al >= 0.75 && al <= 1.25) _sourceLength075To125.Add(al, bl, degrees, rgbError);
            int insert = 0;
            while (insert < _worstCount && _worst[insert].Degrees >= degrees) insert++;
            if (insert >= _worst.Length) return;
            for (int i = Math.Min(_worstCount, _worst.Length - 1); i > insert; i--) _worst[i] = _worst[i - 1];
            _worst[insert] = new CoverPackedWorst(x, y, degrees, al, bl, ar, ag, ab, aa, br, bg, bb, ba);
            _worstCount = Math.Min(_worstCount + 1, _worst.Length);
        }

        private static int Band(double length)
        {
            int band = 0;
            while (band < LengthUpper.Length - 1 && length >= LengthUpper[band]) band++;
            return band;
        }

        public object Report() => new
        {
            selection = "baseline corresponding albedo alpha >=0.42; decoded signed RGB = byte/127.5-1",
            baselineLength = _baseline.Report(), candidateLength = _candidate.Report(),
            sourceLength075To125 = _sourceLength075To125.Report(),
            byBaselineLength = _byLength.Select((value, i) => new { lowerInclusive = i == 0 ? 0 : LengthUpper[i - 1],
                upperExclusive = LengthUpper[i], statistics = value.Report() }).ToArray(),
            byBaselineAlphaT = _byAlpha.Select((value, i) => new { byteLowerInclusive = i * 64,
                byteUpperInclusive = i * 64 + 63, statistics = value.Report() }).ToArray(),
            worst = _worst.Take(_worstCount).ToArray()
        };

        private sealed class CoverLengthDistribution
        {
            private readonly long[] _counts = new long[LengthUpper.Length];
            private long _count;
            private double _minimum = double.PositiveInfinity, _maximum, _sum;
            public void Add(double length, int band)
            { _count++; _counts[band]++; _minimum = Math.Min(_minimum, length); _maximum = Math.Max(_maximum, length); _sum += length; }
            public object Report() => new { samples = _count, minimum = _count == 0 ? (double?)null : _minimum,
                maximum = _count == 0 ? (double?)null : _maximum, mean = _count == 0 ? (double?)null : _sum / _count,
                bands = _counts.Select((count, i) => new { lowerInclusive = i == 0 ? 0 : LengthUpper[i - 1],
                    upperExclusive = LengthUpper[i], samples = count }).ToArray() };
        }

        private sealed class CoverPackedStratum
        {
            private readonly CoverAngle _angle = new();
            private readonly CoverError _rgbError = new();
            private long _count;
            private double _sourceLength, _candidateLength;
            public void Add(double sourceLength, double candidateLength, double degrees, double rgbError)
            { _count++; _sourceLength += sourceLength; _candidateLength += candidateLength; _angle.AddDegrees(degrees); _rgbError.Add(0, rgbError); }
            public object Report() => new { samples = _count,
                meanSourceLength = _count == 0 ? (double?)null : _sourceLength / _count,
                meanCandidateLength = _count == 0 ? (double?)null : _candidateLength / _count,
                angleDegrees = _angle.Report(), meanPerPixelRgbError = _rgbError.Report() };
        }

        private readonly record struct CoverPackedWorst(int X, int Y, double Degrees, double SourceLength,
            double CandidateLength, byte SourceR, byte SourceG, byte SourceB, byte SourceAlphaT,
            byte CandidateR, byte CandidateG, byte CandidateB, byte CandidateAlphaT);
    }
}
