using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>A reviewed whole-plant anchor. It conveys no ground support or collision permission.</summary>
public sealed class GroundCoverRoot
{
    public string Node { get; }
    public string MeshSha256 { get; }
    public string SourceGeometrySha256 { get; }
    public int VertexCount { get; }
    public int TriangleCount { get; }
    public Vector3D Root { get; }
    public Vector3D GrowthAxis { get; }
    public double GrowthLength { get; }

    internal GroundCoverRoot(string node, string meshHash, string geometryHash, int vertices, int triangles,
        Vector3D root, Vector3D axis, double growthLength)
    {
        Node = node; MeshSha256 = meshHash; SourceGeometrySha256 = geometryHash;
        VertexCount = vertices; TriangleCount = triangles;
        Root = root; GrowthAxis = axis; GrowthLength = growthLength;
    }

    /// <summary>Reviewed axial growth in metres, independent of source wind attributes and component connectivity.</summary>
    public float GrowthWeight(Vector3D vertex)
    {
        GroundCoverRootMetadata.CheckVector(vertex, "vertex");
        return (float)Math.Clamp(Vector3D.Dot(vertex - Root, GrowthAxis) / GrowthLength, 0, 1);
    }
}

/// <summary>
/// Receipt-bound fern anchors. Authentication must start with a trusted expected sidecar digest;
/// a digest copied from the untrusted sidecar itself is not an admission decision.
/// Source geometry/review hashes are provenance: original source files are never loaded at runtime.
/// </summary>
public sealed class GroundCoverRootMetadata
{
    public const string Schema = "hollow.cover-root-runtime.v1";
    public const int MaximumSidecarBytes = 64 * 1024, MaximumCatalogBytes = 256 * 1024;
    public const int MaximumModelBytes = 32 * 1024 * 1024;
    private readonly Dictionary<string, GroundCoverRoot> _roots;
    public IReadOnlyCollection<string> Nodes => _roots.Keys;
    public string ReviewSha256 { get; }

    private GroundCoverRootMetadata(Dictionary<string, GroundCoverRoot> roots, string reviewHash)
    { _roots = roots; ReviewSha256 = reviewHash; }

    public GroundCoverRoot Get(string node) => _roots.TryGetValue(node, out GroundCoverRoot? root)
        ? root : throw new ArgumentException("No admitted whole-plant root for " + node);

    public static GroundCoverRootMetadata Read(ReadOnlyMemory<byte> sidecar, string expectedSidecarSha256,
        ReadOnlyMemory<byte> catalog, ReadOnlyMemory<byte> model)
    {
        Bound(sidecar.Length, MaximumSidecarBytes, "root sidecar");
        Bound(catalog.Length, MaximumCatalogBytes, "cover catalog");
        Bound(model.Length, MaximumModelBytes, "cover model");
        Authenticate(sidecar.Span, expectedSidecarSha256, "root sidecar");
        using JsonDocument document = Parse(sidecar);
        JsonElement root = document.RootElement;
        Keys(root, "schema", "status", "runtimeAdmitted", "catalogSha256", "modelSha256", "reviewSha256",
            "sourceGltfSha256", "sourceBufferSha256", "meshes");
        Require(Text(root, "schema") == Schema && Text(root, "status") == "reviewed"
            && root.GetProperty("runtimeAdmitted").ValueKind == JsonValueKind.True,
            "Only explicitly reviewed runtime root metadata is accepted.");
        Authenticate(catalog.Span, Text(root, "catalogSha256"), "cover catalog");
        string modelHash = Digest(Text(root, "modelSha256"));
        Authenticate(model.Span, modelHash, "cover model");
        string reviewHash = Digest(Text(root, "reviewSha256"));
        string gltfHash = Digest(Text(root, "sourceGltfSha256"));
        string bufferHash = Digest(Text(root, "sourceBufferSha256"));
        Require(gltfHash != bufferHash, "Source geometry receipts must distinguish glTF and buffer.");
        using JsonDocument catalogDocument = Parse(catalog);
        JsonElement families = catalogDocument.RootElement.GetProperty("families");
        Require(families.ValueKind == JsonValueKind.Array && families.GetArrayLength() <= 16, "Invalid cover families.");
        JsonElement[] matches = families.EnumerateArray().Where(f => Text(f, "key") == "fern").ToArray();
        Require(matches.Length == 1, "Exactly one fern catalog family is required.");
        JsonElement fern = matches[0];
        Require(Text(fern, "model") == "res://assets/surface/ground-cover/fern.glb"
            && Digest(Text(fern, "outputSha256")) == modelHash
            && fern.GetProperty("outputBytes").GetInt32() == model.Length, "Fern model receipt mismatch.");
        JsonElement inputs = fern.GetProperty("inputs");
        Require(inputs.ValueKind == JsonValueKind.Array && inputs.GetArrayLength() <= 64, "Invalid source receipts.");
        var receipts = new HashSet<string>(inputs.EnumerateArray().Select(i => Digest(Text(i, "sha256"))), StringComparer.Ordinal);
        Require(receipts.Contains(gltfHash) && receipts.Contains(bufferHash), "Original geometry receipts are not in the authenticated catalog.");
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        JsonElement variants = fern.GetProperty("variants");
        Require(variants.ValueKind == JsonValueKind.Array && variants.GetArrayLength() == 3, "Expected three fern variants.");
        foreach (JsonElement variant in variants.EnumerateArray())
        {
            int number = variant.GetProperty("variant").GetInt32();
            Require(number is >= 0 and < 3, "Unsupported fern variant.");
            JsonElement lods = variant.GetProperty("levels");
            Require(lods.ValueKind == JsonValueKind.Array && lods.GetArrayLength() == 3, "Expected three catalog fern levels.");
            for (int lod = 0; lod < 2; lod++)
            {
                JsonElement level = lods[lod];
                string name = $"fern_v{number}_lod{lod}";
                Require(Text(level, "node") == name && level.GetProperty("billboard").ValueKind == JsonValueKind.False
                    && levels.TryAdd(name, level.GetProperty("triangles").GetInt32()), "Invalid or duplicate fern level.");
            }
        }
        JsonElement meshes = root.GetProperty("meshes");
        Require(meshes.ValueKind == JsonValueKind.Array && meshes.GetArrayLength() == 6, "Exactly six near/middle fern roots are required.");
        var roots = new Dictionary<string, GroundCoverRoot>(StringComparer.Ordinal);
        foreach (JsonElement mesh in meshes.EnumerateArray())
        {
            Keys(mesh, "node", "meshSha256", "sourceGeometrySha256", "proposalSha256", "vertexCount", "triangleCount",
                "root", "growthAxis", "growthLength");
            string node = Text(mesh, "node");
            int vertices = mesh.GetProperty("vertexCount").GetInt32(), triangles = mesh.GetProperty("triangleCount").GetInt32();
            Require(vertices is >= 3 and <= GroundCoverMeshFingerprint.MaximumVertices
                && triangles is >= 1 and <= GroundCoverMeshFingerprint.MaximumTriangles
                && levels.TryGetValue(node, out int expected) && triangles == expected, "Root mesh does not match the fern catalog.");
            Vector3D at = Vector(mesh.GetProperty("root")), axis = Vector(mesh.GetProperty("growthAxis"));
            double length = mesh.GetProperty("growthLength").GetDouble();
            Require(Math.Abs(axis.LengthSquared - 1) <= 1e-8 && double.IsFinite(length) && length is >= 0.001 and <= 10,
                "Growth needs a unit axis and a finite 1 mm–10 m range.");
            _ = Digest(Text(mesh, "proposalSha256"));
            var entry = new GroundCoverRoot(node, Digest(Text(mesh, "meshSha256")), Digest(Text(mesh, "sourceGeometrySha256")),
                vertices, triangles, at, axis, length);
            Require(roots.TryAdd(node, entry), "Duplicate fern root.");
        }
        for (int variant = 0; variant < 3; variant++)
        {
            GroundCoverRoot near = roots[$"fern_v{variant}_lod0"], mid = roots[$"fern_v{variant}_lod1"];
            Require(near.Root == mid.Root && near.GrowthAxis == mid.GrowthAxis && near.GrowthLength == mid.GrowthLength
                && near.SourceGeometrySha256 == mid.SourceGeometrySha256,
                "Current fern near/middle meshes must share their reviewed whole-plant anchor and growth rule.");
        }
        return new GroundCoverRootMetadata(roots, reviewHash);
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Authenticate(ReadOnlySpan<byte> bytes, string expected, string name)
    { Require(Sha256(bytes) == Digest(expected), name + " SHA-256 mismatch."); }
    private static string Digest(string value)
    {
        Require(value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Expected lowercase SHA-256.");
        return value;
    }
    private static string Text(JsonElement element, string key)
    {
        JsonElement value = element.GetProperty(key);
        Require(value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 256, "Invalid string: " + key);
        return value.GetString()!;
    }
    private static Vector3D Vector(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 3, "Expected XYZ.");
        var result = new Vector3D(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
        CheckVector(result, "root vector");
        return result;
    }
    internal static void CheckVector(Vector3D value, string name) => Require(double.IsFinite(value.X) && double.IsFinite(value.Y)
        && double.IsFinite(value.Z) && Math.Abs(value.X) <= 10 && Math.Abs(value.Y) <= 10 && Math.Abs(value.Z) <= 10,
        name + " must be finite and within the 10 m authored-cover bound.");
    private static void Bound(int count, int limit, string name) => Require(count > 0 && count <= limit, name + " exceeds byte bounds.");
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    private static void Keys(JsonElement element, params string[] names)
    {
        Require(element.ValueKind == JsonValueKind.Object, "Expected root metadata object.");
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject()) Require(allowed.Remove(property.Name), "Unknown or duplicate root property: " + property.Name);
        Require(allowed.Count == 0, "Missing root metadata properties.");
    }
    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 20 });
        try { Unique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            { Require(names.Add(property.Name), "Duplicate JSON property: " + property.Name); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (JsonElement child in element.EnumerateArray()) Unique(child);
    }
}
