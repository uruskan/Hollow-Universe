using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class GroundCoverRootMetadataTests
{
    [Test]
    public static void CoverRootsAuthenticateSixWholePlantsAndUseExplicitGrowthAxis()
    {
        Fixture fixture = Make();
        GroundCoverRootMetadata metadata = Read(fixture);
        Assert.Equal(6, metadata.Nodes.Count, "six near/mid mesh identities");
        GroundCoverRoot root = metadata.Get("fern_v1_lod0");
        Assert.Equal(Vector3D.Zero, root.Root, "one whole-plant root");
        Assert.Equal(root.Root, metadata.Get("fern_v1_lod1").Root, "LOD correspondence");
        Assert.Equal(0f, root.GrowthWeight(new Vector3D(2, -1, 3)), "below-anchor fronds retain geometry but no axial bend");
        Assert.Equal(0.5f, root.GrowthWeight(new Vector3D(-2, 0.5, -3)), "growth ignores width and leaf connectivity");
        Assert.Equal(1f, root.GrowthWeight(new Vector3D(1, 3, -1)), "growth clamps at explicit length");
        Reject(() => metadata.Get("fern_v0_lod2"));
        Reject(() => metadata.Get("cotton_v2_lod0"));
        byte[] bytes = Bytes(fixture.Sidecar);
        GroundCoverRootMetadata owned = GroundCoverRootMetadata.Read(bytes, GroundCoverRootMetadata.Sha256(bytes), fixture.Catalog, fixture.Model);
        bytes.AsSpan().Clear();
        Assert.Equal(root.MeshSha256, owned.Get(root.Node).MeshSha256, "published metadata owns its values");
    }

    [Test]
    public static void CoverRootsRequireExternalReceiptCatalogAndOriginalGeometryBindings()
    {
        Fixture f = Make();
        Reject(() => GroundCoverRootMetadata.Read(Bytes(f.Sidecar), new string('0', 64), f.Catalog, f.Model));
        byte[] changedCatalog = f.Catalog.ToArray(); changedCatalog[^1] ^= 1;
        Reject(() => GroundCoverRootMetadata.Read(Bytes(f.Sidecar), GroundCoverRootMetadata.Sha256(Bytes(f.Sidecar)), changedCatalog, f.Model));
        byte[] changedModel = f.Model.ToArray(); changedModel[0] ^= 1;
        Reject(() => GroundCoverRootMetadata.Read(Bytes(f.Sidecar), GroundCoverRootMetadata.Sha256(Bytes(f.Sidecar)), f.Catalog, changedModel));
        f.Sidecar["sourceBufferSha256"] = new string('c', 64);
        Reject(() => Read(f));
        f = Make(); f.Sidecar["runtimeAdmitted"] = false; Reject(() => Read(f));
        f = Make(); f.Sidecar["status"] = "unreviewed-logical-group-candidates"; Reject(() => Read(f));
        f = Make(); f.Sidecar["schema"] = "hollow.cover-root-preview-candidates.v1"; Reject(() => Read(f));
    }

    [Test]
    public static void CoverRootsRejectUnknownFieldsDuplicateEscapedKeysAndUnboundedInput()
    {
        Fixture f = Make(); f.Sidecar["guessRootsFromLeaves"] = true; Reject(() => Read(f));
        f = Make(); Mesh(f, 0)["wind"] = 1; Reject(() => Read(f));
        f = Make();
        string json = f.Sidecar.ToJsonString().Replace("\"runtimeAdmitted\":true", "\"runtimeAdmitted\":true,\"runtime\\u0041dmitted\":true", StringComparison.Ordinal);
        byte[] duplicate = Encoding.UTF8.GetBytes(json);
        Reject(() => GroundCoverRootMetadata.Read(duplicate, GroundCoverRootMetadata.Sha256(duplicate), f.Catalog, f.Model));
        byte[] oversized = new byte[GroundCoverRootMetadata.MaximumSidecarBytes + 1];
        Reject(() => GroundCoverRootMetadata.Read(oversized, GroundCoverRootMetadata.Sha256(oversized), f.Catalog, f.Model));
    }

    [Test]
    public static void CoverRootsRejectLodRootDisagreementBadAxesMissingPlantsAndCatalogMismatch()
    {
        Action<Fixture>[] edits =
        {
            f => Mesh(f, 0)["root"] = JsonSerializer.SerializeToNode(new[] { 0.0, 0.1, 0 }),
            f => Mesh(f, 0)["growthAxis"] = JsonSerializer.SerializeToNode(new[] { 0, 2, 0 }),
            f => Mesh(f, 0)["growthLength"] = 0,
            f => Mesh(f, 0)["root"] = JsonSerializer.SerializeToNode(new[] { 11, 0, 0 }),
            f => Mesh(f, 0)["triangleCount"] = 2,
            f => Mesh(f, 0)["vertexCount"] = GroundCoverMeshFingerprint.MaximumVertices + 1,
            f => Mesh(f, 0)["node"] = "fern_v0_lod2",
            f => Mesh(f, 0)["meshSha256"] = "not-a-digest",
            f => Mesh(f, 0)["sourceGeometrySha256"] = new string('e', 64),
            f => f.Sidecar["meshes"]!.AsArray().RemoveAt(5),
            f => Mesh(f, 5)["node"] = "fern_v0_lod0"
        };
        foreach (Action<Fixture> edit in edits) { Fixture f = Make(); edit(f); Reject(() => Read(f)); }
    }

    [Test]
    public static void CoverImportedIdentityIncludesEveryPreservedStreamAndIndexOrder()
    {
        float[][] streams =
        {
            new float[] { -1, 0, 0, 1, 0, 0, 0, 1, 0 },
            new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0 },
            Enumerable.Repeat(new float[] { 1, 0, 0, 1 }, 3).SelectMany(x => x).ToArray(),
            Enumerable.Repeat(new float[] { 1, 0.5f, 0.2f, 1 }, 3).SelectMany(x => x).ToArray(),
            new float[] { 0, 0, 1, 0, 0.5f, 1 }, new float[] { 0, 0, 0.5f, 0, 0.25f, 0.5f }
        };
        int[] indices = { 2, 0, 1 };
        string expected = Fingerprint(streams, indices);
        Assert.Equal(expected, Fingerprint(streams.Select(x => x.ToArray()).ToArray(), indices.ToArray()), "identity is byte-value based, not array-reference based");
        for (int slot = 0; slot < streams.Length; slot++)
        {
            float[][] changed = streams.Select(x => x.ToArray()).ToArray(); changed[slot][0] += 0.125f;
            Assert.True(Fingerprint(changed, indices) != expected, "stream " + slot + " is authenticated");
        }
        Assert.True(Fingerprint(streams, new[] { 0, 2, 1 }) != expected, "triangle winding and vertex ordinals remain exact");
        streams[0][1] = -0.0f;
        Assert.True(Fingerprint(streams, indices) != expected, "float sign bit remains part of exact imported identity");
        streams[0][1] = float.NaN; Reject(() => Fingerprint(streams, indices));
        streams[0][1] = 0; Reject(() => Fingerprint(streams, new[] { 0, 1, 3 }));
        streams[1] = Array.Empty<float>(); Reject(() => Fingerprint(streams, indices));
    }

    private sealed record Fixture(JsonObject Sidecar, byte[] Catalog, byte[] Model);
    private static JsonObject Mesh(Fixture f, int index) => f.Sidecar["meshes"]![index]!.AsObject();
    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
    private static GroundCoverRootMetadata Read(Fixture f)
    {
        byte[] sidecar = Bytes(f.Sidecar);
        return GroundCoverRootMetadata.Read(sidecar, GroundCoverRootMetadata.Sha256(sidecar), f.Catalog, f.Model);
    }
    private static Fixture Make()
    {
        byte[] model = { 1, 2, 3, 4 };
        string modelHash = GroundCoverRootMetadata.Sha256(model), gltf = new('a', 64), buffer = new('b', 64);
        var variants = Enumerable.Range(0, 3).Select(v => new
        {
            variant = v, levels = Enumerable.Range(0, 3).Select(l => new { node = $"fern_v{v}_lod{l}", triangles = 1, billboard = l == 2 }).ToArray()
        }).ToArray();
        byte[] catalog = JsonSerializer.SerializeToUtf8Bytes(new
        {
            families = new[] { new { key = "fern", model = "res://assets/surface/ground-cover/fern.glb", outputSha256 = modelHash,
                outputBytes = model.Length, inputs = new[] { new { sha256 = gltf }, new { sha256 = buffer } }, variants } }
        });
        object[] meshes = Enumerable.Range(0, 6).Select(i => (object)new
        {
            node = $"fern_v{i / 2}_lod{i % 2}", meshSha256 = new string('d', 64), sourceGeometrySha256 = new string('f', 64),
            proposalSha256 = new string('a', 64), vertexCount = 3, triangleCount = 1,
            root = new[] { 0, 0, 0 }, growthAxis = new[] { 0, 1, 0 }, growthLength = 1
        }).ToArray();
        var sidecar = JsonSerializer.SerializeToNode(new
        {
            schema = GroundCoverRootMetadata.Schema, status = "reviewed", runtimeAdmitted = true,
            catalogSha256 = GroundCoverRootMetadata.Sha256(catalog), modelSha256 = modelHash, reviewSha256 = new string('9', 64),
            sourceGltfSha256 = gltf, sourceBufferSha256 = buffer, meshes
        })!.AsObject();
        return new Fixture(sidecar, catalog, model);
    }
    private static string Fingerprint(float[][] values, int[] indices) => GroundCoverMeshFingerprint.Compute(values[0], values[1], values[2], values[3], values[4], values[5], indices);
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new TestFailure("Expected invalid cover metadata or geometry to be refused.");
    }
}
