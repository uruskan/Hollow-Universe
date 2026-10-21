using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Verified physical catalog plus immutable visual file bindings. The relative paths identify authenticated package
/// content, not a live renderer resource. A renderer must authenticate its actual loaded/imported inputs separately.
/// These presentation bindings do not enter physical identity or activate generation.
/// </summary>
public sealed class FormationPackage
{
    internal FormationPackage(FormationCatalog catalog, string receiptSha256, FormationVisualAsset[] visuals,
        FormationPackageFile[] files)
    {
        Catalog = catalog; ReceiptSha256 = receiptSha256;
        Visuals = System.Array.AsReadOnly(visuals); Files = System.Array.AsReadOnly(files);
        long bytes = 512L + 16L * (visuals.Length + files.Length);
        foreach (FormationVisualAsset visual in visuals)
            bytes += 128L + 2L * (visual.GeometryKey.Length + visual.LodKey.Length + visual.AssetPath.Length);
        foreach (FormationPackageFile file in files) bytes += 128L + 2L * (file.Path.Length + file.Sha256.Length);
        EstimatedBindingBytes = bytes;
    }
    public FormationCatalog Catalog { get; }
    public string ReceiptSha256 { get; }
    public IReadOnlyList<FormationVisualAsset> Visuals { get; }
    public IReadOnlyList<FormationPackageFile> Files { get; }
    public long EstimatedBindingBytes { get; }
}

public sealed class FormationVisualAsset
{
    internal FormationVisualAsset(string geometryKey, int geometryRevision, string lodKey, string assetPath,
        string contentSha256)
    { GeometryKey = geometryKey; GeometryRevision = geometryRevision; LodKey = lodKey; AssetPath = assetPath; ContentSha256 = contentSha256; }
    public string GeometryKey { get; }
    public int GeometryRevision { get; }
    public string LodKey { get; }
    public string AssetPath { get; }
    public string ContentSha256 { get; }
}

public sealed class FormationPackageFile
{
    internal FormationPackageFile(string path, string sha256, long bytes) { Path = path; Sha256 = sha256; Bytes = bytes; }
    public string Path { get; }
    public string Sha256 { get; }
    public long Bytes { get; }
}

public sealed record FormationPackageLimits
{
    public int MaxFiles { get; init; } = 64;
    public long MaxFileBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxTotalFileBytes { get; init; } = 256L * 1024 * 1024;
    public int MaxJsonBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxDefinitions { get; init; } = 8;
}

/// <summary>
/// Reads bounded, receipt-pinned authoring packages. The caller supplies the expected receipt digest, never a digest
/// read from that same untrusted package. Every decoded byte is authenticated from its own read. Output textures are
/// streamed through SHA-256, not retained or decoded. This authenticates content; it does not approve art, support
/// permissions, collision quality, recipes or a new generation revision. Receipt identity is not physical identity.
/// </summary>
public static class FormationPackageLoader
{
    private sealed record FilePin(string Path, string Sha256, long Bytes);
    private sealed class Admission(FormationPackageLimits limits)
    {
        private int _files;
        private long _bytes;
        public void Add(long bytes)
        {
            Require(_files < limits.MaxFiles && bytes > 0 && bytes <= limits.MaxFileBytes && bytes <= limits.MaxTotalFileBytes - _bytes,
                "Package source/output admission exceeded.");
            _files++; _bytes += bytes;
        }
    }

    public static FormationPackage Load(string packageDirectory, string sourceDirectory, string expectedReceiptSha256,
        FormationPackageLimits? limits = null)
    {
        try { return LoadCore(packageDirectory, sourceDirectory, expectedReceiptSha256, limits); }
        catch (Exception error) when ((error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
            && error is not FormationCapacityException)
        { throw new FormationValidationException("Malformed formation package: " + error.Message); }
    }

    private static FormationPackage LoadCore(string packageDirectory, string sourceDirectory, string expectedReceiptSha256,
        FormationPackageLimits? limits)
    {
        limits ??= new FormationPackageLimits();
        if (limits.MaxFiles < 1 || limits.MaxFiles > 256 || limits.MaxFileBytes < 1 || limits.MaxFileBytes > 128L * 1024 * 1024 ||
            limits.MaxTotalFileBytes < 1 || limits.MaxTotalFileBytes > 1024L * 1024 * 1024 ||
            limits.MaxJsonBytes < 1 || limits.MaxJsonBytes > 16 * 1024 * 1024 || limits.MaxDefinitions < 1 || limits.MaxDefinitions > 64)
            throw new FormationValidationException("Invalid package admission limits.");
        string expected = Digest(expectedReceiptSha256);
        string root = Path.GetFullPath(packageDirectory), sourceRoot = Path.GetFullPath(sourceDirectory);
        var admission = new Admission(limits);
        string receiptPath = SafePath(root, "qualification-receipt.json");
        admission.Add(new FileInfo(receiptPath).Length);
        byte[] receiptBytes = ReadChecked(receiptPath, expected, Math.Min(limits.MaxJsonBytes, 1024 * 1024));
        using JsonDocument receipt = FormationJson.Parse(receiptBytes, limits.MaxJsonBytes);
        JsonElement receiptRoot = receipt.RootElement;
        Require(receiptRoot.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported package receipt schema.");
        var files = new Dictionary<string, FilePin>(StringComparer.Ordinal);
        foreach (JsonElement entry in Array(receiptRoot, "outputFiles", limits.MaxFiles))
        {
            string name = Text(entry, "file");
            long length = entry.GetProperty("bytes").GetInt64();
            admission.Add(length);
            var pin = new FilePin(SafePath(root, name), Digest(Text(entry, "sha256")), length);
            Require(files.TryAdd(name, pin), "Duplicate package file.");
            VerifyFile(pin);
        }
        // The original scan and its dependency hashes bind provenance independently of the copied/reduced LODs.
        JsonElement source = receiptRoot.GetProperty("source");
        string sourceHash = Digest(Text(source, "sha256"));
        VerifySource(source, sourceRoot, limits, admission);
        JsonElement manifest = receiptRoot.GetProperty("manifest");
        string manifestPath = SafePath(sourceRoot, Text(manifest, "file"));
        admission.Add(new FileInfo(manifestPath).Length);
        using (JsonDocument provenance = FormationJson.Parse(ReadChecked(manifestPath, Digest(Text(manifest, "sha256")), limits.MaxJsonBytes), limits.MaxJsonBytes))
            VerifyManifest(provenance.RootElement, source);

        byte[] ReadOutput(string name, int maximum)
        {
            if (!files.TryGetValue(name, out FilePin? pin)) throw new FormationValidationException("Unlisted package dependency: " + name);
            Require(pin.Bytes <= maximum, "Decoded dependency exceeds byte admission.");
            return ReadChecked(pin.Path, pin.Sha256, maximum, pin.Bytes);
        }
        using JsonDocument sidecar = FormationJson.Parse(ReadOutput("sidecar.json", limits.MaxJsonBytes), limits.MaxJsonBytes);
        JsonElement document = sidecar.RootElement;
        Require(document.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported formation sidecar schema.");
        var definitions = new List<CliffSupportDefinition>();
        var visuals = new List<FormationVisualAsset>();
        // Schema 1 authoring packages currently bind one physical.json. Multiple definitions would ambiguously share it.
        JsonElement definitionArray = Bounded(document.GetProperty("definitions"), limits.MaxDefinitions);
        Require(definitionArray.GetArrayLength() == 1, "Schema 1 packages require exactly one physical definition.");
        using JsonDocument physicalDocument = FormationJson.Parse(ReadOutput("physical.json", limits.MaxJsonBytes), limits.MaxJsonBytes);
        foreach (JsonElement definition in definitionArray.EnumerateArray())
        {
            string key = Text(definition, "key"), family = Text(definition, "familyKey");
            int revision = definition.GetProperty("revision").GetInt32();
            Require(Text(definition, "units") == "metres" && Text(definition, "basis") == "right-handed-y-up", "Unsupported geometry coordinate convention.");
            Require(Digest(Text(definition, "sourceSha256")) == sourceHash, "Definition source digest mismatch.");
            Require(Digest(Text(definition, "physicalSha256")) == files["physical.json"].Sha256, "Physical digest mismatch.");
            Require(JsonElement.DeepEquals(definition.GetProperty("physical"), physicalDocument.RootElement), "Embedded physical mesh differs from authenticated physical bytes.");
            FormationGeometry physical = ReadPhysical(key, revision, family, physicalDocument.RootElement);
            var domains = new List<CliffPatchDomain>();
            foreach (JsonElement domain in Array(definition, "domains", 256))
            {
                var boundaries = new List<CliffPatchBoundary>();
                foreach (JsonElement loop in Array(domain, "boundaries", 64))
                    boundaries.Add(new CliffPatchBoundary(Ints(loop, 4096)));
                domains.Add(new CliffPatchDomain(Text(domain, "patchKey"), boundaries));
            }
            var contacts = new List<CliffContactDomain>();
            foreach (JsonElement contact in Array(definition, "contacts", 128))
                contacts.Add(new CliffContactDomain(Text(contact, "key"), Text(contact, "role"), Text(contact, "patchKey"),
                    Ints(contact.GetProperty("vertices"), 64), Number(contact, "minimumBurialMetres"),
                    Number(contact, "maximumBurialMetres"), Number(contact, "maximumGapMetres")));
            var contactLoops = new List<FormationContactLoop>();
            if (definition.TryGetProperty("contactLoops", out JsonElement physicalLoops))
                foreach (JsonElement loop in Bounded(physicalLoops, FormationContactLoop.MaximumLoops).EnumerateArray())
                    contactLoops.Add(new FormationContactLoop(Text(loop, "key"), Ints(loop.GetProperty("vertices"), FormationContactLoop.MaximumVertices)));
            var lods = new List<CliffLodBinding>();
            foreach (JsonElement lod in Array(definition, "lods", 4))
            {
                string asset = Text(lod, "assetPath"), lodKey = Text(lod, "key");
                Require(files.TryGetValue(asset, out FilePin? pin) && pin.Sha256 == Digest(Text(lod, "contentSha256")), "LOD digest differs from receipt.");
                int jsonLimit = Math.Min(limits.MaxJsonBytes, 4 * 1024 * 1024);
                byte[] gltf = ReadOutput(asset, jsonLimit);
                using (JsonDocument decoded = FormationJson.Parse(gltf, jsonLimit))
                    VerifyDependencyClosure(decoded.RootElement, asset, path =>
                    {
                        _ = SafePath(root, path);
                        Require(files.ContainsKey(path), "Unlisted visual dependency: " + path);
                    });
                // glTF URI paths are relative to their document, still constrained by the package root and receipt.
                string prefix = asset.Contains('/') ? asset[..(asset.LastIndexOf('/') + 1)] : "";
                FormationGeometry visual = FormationGltfDecoder.Decode(key + "/" + lodKey, revision, family, gltf,
                    path => ReadOutput(prefix + path, 32 * 1024 * 1024),
                    new FormationGeometryLimits { MaxVertices = 60000, MaxTriangles = 50000, MaxPatches = 1, MaxEstimatedBytes = 10L * 1024 * 1024 });
                var maps = new List<CliffLodTriangleMap>();
                foreach (JsonElement map in Array(lod, "triangleMaps", 131072))
                {
                    int[] values = Ints(map, 2);
                    Require(values.Length == 2, "Triangle maps require two ordinals.");
                    maps.Add(new CliffLodTriangleMap(values[0], values[1]));
                }
                var loopBindings = new List<FormationContactLoopBinding>();
                if (lod.TryGetProperty("contactLoops", out JsonElement visualLoops))
                    foreach (JsonElement loop in Bounded(visualLoops, FormationContactLoop.MaximumLoops).EnumerateArray())
                        loopBindings.Add(new FormationContactLoopBinding(Text(loop, "loopKey"), Ints(loop.GetProperty("vertices"), FormationContactLoop.MaximumVertices),
                            loop.TryGetProperty("edgeVertices", out JsonElement edgeVertices)
                                ? Ints(edgeVertices, 2 * FormationContactLoop.MaximumVertices) : null));
                lods.Add(new CliffLodBinding(lodKey, Text(lod, "contentSha256"), visual, maps,
                    Number(lod, "maximumPositionErrorMetres"), Number(lod, "minimumNormalDot"), loopBindings));
                visuals.Add(new FormationVisualAsset(key, revision, lodKey, asset, pin!.Sha256));
            }
            definitions.Add(new CliffSupportDefinition(physical, sourceHash, Text(definition, "physicalSha256"),
                Number(definition, "minimumScale"), Number(definition, "maximumScale"), domains, contacts, lods, contactLoops: contactLoops));
        }
        var profiles = new List<SubstrateProfile>();
        foreach (JsonElement p in Array(document, "profiles", 128))
            profiles.Add(new SubstrateProfile(Text(p, "key"), Number(p, "bedrock"), Number(p, "looseStone"), Number(p, "soil"),
                Number(p, "soilDepthMetres"), Number(p, "plantSuitability"), Number(p, "grassCoverage"), Number(p, "shrubsPerSquareMetre"),
                Number(p, "treesPerSquareMetre"), Number(p, "litter"), Number(p, "minimumTemperatureC"), Number(p, "maximumTemperatureC"), Number(p, "minimumSoilUpDot")));
        // Qualified geometry packages do not activate placement. Recipe admission is a separate, versioned catalog input.
        Require(Bounded(document.GetProperty("recipes"), 128).GetArrayLength() == 0, "Geometry package cannot activate recipes.");
        return new FormationPackage(new FormationCatalog(definitions, profiles, System.Array.Empty<FormationRecipe>()), expected,
            visuals.OrderBy(v => v.GeometryKey, StringComparer.Ordinal).ThenBy(v => v.GeometryRevision)
                .ThenBy(v => v.LodKey, StringComparer.Ordinal).ToArray(),
            files.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new FormationPackageFile(pair.Key, pair.Value.Sha256, pair.Value.Bytes)).ToArray());
    }

    private static FormationGeometry ReadPhysical(string key, int revision, string family, JsonElement physical)
    {
        var vertices = new List<Vector3D>();
        foreach (JsonElement v in Array(physical, "vertices", 8192))
        {
            Require(v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3, "Physical vertex needs three coordinates.");
            vertices.Add(new Vector3D(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble()));
        }
        var triangles = new List<GeometryTriangle>();
        foreach (JsonElement t in Array(physical, "triangles", 16384))
        {
            int[] values = Ints(t, 4);
            Require(values.Length == 4, "Physical triangle needs three corners and patch ordinal.");
            triangles.Add(new GeometryTriangle(values[0], values[1], values[2], values[3]));
        }
        var patches = new List<SupportPatch>();
        foreach (JsonElement p in Array(physical, "patches", 256))
        {
            SupportCapabilities capabilities = SupportCapabilities.None;
            foreach (JsonElement flag in Array(p, "capabilities", 3))
            {
                SupportCapabilities parsed = flag.GetString() switch
                {
                    "Walkable" => SupportCapabilities.Walkable, "Plantable" => SupportCapabilities.Plantable,
                    "Soil" => SupportCapabilities.Soil, _ => throw new FormationValidationException("Unknown support capability."),
                };
                Require((capabilities & parsed) == 0, "Duplicate support capability."); capabilities |= parsed;
            }
            patches.Add(new SupportPatch(Text(p, "key"), Text(p, "substrateProfileKey"), capabilities));
        }
        return new FormationGeometry(key, revision, family, vertices, triangles, patches);
    }

    private static void VerifySource(JsonElement source, string root, FormationPackageLimits limits, Admission admission)
    {
        var pins = new Dictionary<string, FilePin>(StringComparer.Ordinal);
        FilePin Check(JsonElement row)
        {
            string name = Text(row, "file");
            string path = SafePath(root, name);
            long bytes = new FileInfo(path).Length;
            admission.Add(bytes);
            var pin = new FilePin(path, Digest(Text(row, "sha256")), bytes);
            Require(pins.TryAdd(name, pin), "Duplicate source dependency.");
            VerifyFile(pin); return pin;
        }
        FilePin scan = Check(source);
        foreach (JsonElement dependency in Array(source, "dependencies", limits.MaxFiles)) Check(dependency);
        int jsonLimit = Math.Min(limits.MaxJsonBytes, 4 * 1024 * 1024);
        using JsonDocument document = FormationJson.Parse(ReadChecked(scan.Path, scan.Sha256, jsonLimit), jsonLimit);
        VerifyDependencyClosure(document.RootElement, Text(source, "file"), path =>
        {
            _ = SafePath(root, path);
            Require(pins.ContainsKey(path) && path != Text(source, "file"), "Unlisted source dependency: " + path);
        });
    }

    private static void VerifyDependencyClosure(JsonElement gltf, string name, Action<string> requireDependency)
    {
        string prefix = name.Contains('/') ? name[..(name.LastIndexOf('/') + 1)] : "";
        foreach (string field in new[] { "buffers", "images" })
        {
            if (!gltf.TryGetProperty(field, out JsonElement dependencies)) continue;
            foreach (JsonElement dependency in Bounded(dependencies, 64).EnumerateArray())
            {
                string uri = Text(dependency, "uri");
                Require(!uri.Contains('%') && !uri.Contains('#') && !uri.Contains('?'), "Encoded or fragment dependency URIs are unsupported.");
                requireDependency(prefix + uri);
            }
        }
    }

    private static void VerifyManifest(JsonElement manifest, JsonElement source)
    {
        int matches = 0;
        foreach (JsonElement asset in Array(manifest, "assets", 256))
            foreach (JsonElement output in Array(asset, "outputs", 16))
                if (Text(output, "file") == Text(source, "file"))
                {
                    matches++;
                    Require(Digest(Text(output, "sha256")) == Digest(Text(source, "sha256")), "Source differs from provenance manifest.");
                    Dictionary<string, string> Pins(JsonElement owner)
                    {
                        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (JsonElement dependency in Array(owner, "dependencies", 64))
                            Require(pins.TryAdd(Text(dependency, "file"), Digest(Text(dependency, "sha256"))), "Duplicate manifest dependency.");
                        return pins;
                    }
                    var declared = Pins(source); var recorded = Pins(output);
                    Require(declared.Count == recorded.Count && declared.All(pair => recorded.TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
                        "Source dependency set differs from provenance manifest.");
                }
        Require(matches == 1, "Source must occur exactly once in the provenance manifest.");
    }

    private static void VerifyFile(FilePin pin)
    {
        using FileStream stream = File.Open(pin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length == pin.Bytes, "File size differs from receipt.");
        Require(Convert.ToHexStringLower(SHA256.HashData(stream)) == pin.Sha256, "File digest differs from receipt: " + Path.GetFileName(pin.Path));
    }

    private static byte[] ReadChecked(string path, string digest, int maximum, long? exact = null)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length > 0 && stream.Length <= maximum && (exact == null || stream.Length == exact), "Decoded file exceeds admission or changed size.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == digest, "Decoded file digest mismatch: " + Path.GetFileName(path));
        return bytes;
    }

    internal static string SafePath(string root, string relative)
    {
        Require(!string.IsNullOrWhiteSpace(relative) && relative.Length <= 512 && !relative.Contains('\\') && !relative.Contains(':') && !Path.IsPathRooted(relative), "Package path must be a relative slash-separated path.");
        string current = root;
        foreach (string segment in relative.Split('/'))
        {
            Require(segment.Length > 0 && segment != "." && segment != ".." && segment == segment.Trim() && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0,
                "Invalid package path segment.");
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
                Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Package paths cannot traverse links.");
        }
        return current;
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement owner, string key, int maximum) => Bounded(owner.GetProperty(key), maximum).EnumerateArray();
    private static JsonElement Bounded(JsonElement array, int maximum)
    { Require(array.ValueKind == JsonValueKind.Array && array.GetArrayLength() <= maximum, "JSON collection exceeds admission."); return array; }
    private static int[] Ints(JsonElement array, int maximum) => Bounded(array, maximum).EnumerateArray().Select(value => value.GetInt32()).ToArray();
    private static string Text(JsonElement owner, string name) => owner.GetProperty(name).GetString() ?? throw new FormationValidationException("Required text is null: " + name);
    private static double Number(JsonElement owner, string name) => owner.GetProperty(name).GetDouble();
    private static string Digest(string text)
    { Require(text != null && text.Length == 64 && text.All(Uri.IsHexDigit), "Expected a SHA-256 digest."); return text!.ToLowerInvariant(); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new FormationValidationException(message); }
}
