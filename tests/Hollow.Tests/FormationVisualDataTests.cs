using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationVisualDataTests
{
    [Test]
    public static void FormationVisualDataPreservesOrderedPrimitivesExactStreamsAndSourceWinding()
    {
        foreach (bool wide in new[] { false, true })
        {
            using var fixture = new Fixture(wideIndices: wide);
            FormationVisualPreflight preflight = fixture.Preflight();
            FormationVisualData data = FormationVisualDataLoader.Load(preflight);
            Assert.Equal(fixture.Package.ReceiptSha256, data.ReceiptSha256, "external receipt pin follows the published payload");
            Assert.Equal(preflight.Sizing, data.Sizing, "load preserves admitted sizing");
            Assert.Equal(2, data.Lods.Count, "both independently qualified LODs are published together");
            Assert.Equal(4, data.Sizing.Primitives, "primitive count is aggregate across LODs");
            Assert.Equal(12L, data.Sizing.Vertices, "identical vertex ordinals in distinct primitives remain distinct");
            Assert.Equal(12L, data.Sizing.Indices, "all source triangle corners remain present");
            foreach (FormationVisualLod lod in data.Lods)
            {
                Assert.Equal("fixture", lod.GeometryKey, "physical geometry key is retained");
                Assert.Equal(3, lod.GeometryRevision, "physical geometry revision is retained");
                Assert.Equal(2, lod.Primitives.Count, "declaration order is not merged by material");
                for (int part = 0; part < lod.Primitives.Count; part++)
                {
                    FormationVisualPrimitive primitive = lod.Primitives[part];
                    Assert.Equal(part, primitive.Ordinal, "source primitive ordinal");
                    Assert.Equal(part, primitive.MaterialIndex, "source material slot");
                    Assert.Equal(part * 3, primitive.SourceVertexOffset, "canonical concatenation vertex offset");
                    Assert.Equal(part, primitive.SourceTriangleOffset, "canonical concatenation triangle offset");
                    Assert.Equal(3, primitive.VertexCount, "authored vertex count");
                    SameBits(fixture.Positions[part], primitive.Positions, "POSITION");
                    SameBits(Fixture.Normals, primitive.Normals, "NORMAL");
                    SameBits(Fixture.Tangents, primitive.Tangents, "TANGENT including handedness");
                    SameBits(Fixture.TexCoords, primitive.TexCoords, "UV including values outside the unit square");
                    Assert.True(primitive.HasTangents && primitive.HasTexCoords, "present streams are explicit");
                    Assert.Equal(2, primitive.Indices[0], "source first corner is not reordered");
                    Assert.Equal(0, primitive.Indices[1], "source second corner is not reversed for Godot here");
                    Assert.Equal(1, primitive.Indices[2], "source third corner is preserved");
                }
            }
        }
    }

    [Test]
    public static void FormationVisualDataRetainsPbrDefaultsAndTextureRoleSemantics()
    {
        using var fixture = new Fixture();
        foreach (JsonObject document in fixture.Documents.Values)
        {
            JsonObject material = document["materials"]![0]!.AsObject();
            material["doubleSided"] = true;
            material["alphaMode"] = "MASK";
            material["alphaCutoff"] = 0.375;
            material["emissiveFactor"] = Node(new[] { 0.125f, 0.25f, 0.5f });
            material["emissiveTexture"] = Node(new { index = 0 });
            material["normalTexture"]!["scale"] = 0.75;
            material["occlusionTexture"]!["strength"] = 0.625;
            material["pbrMetallicRoughness"]!["baseColorFactor"] = Node(new[] { 0.5f, 0.25f, 0.125f, 0.75f });
            material["pbrMetallicRoughness"]!["metallicFactor"] = 0.25;
            material["pbrMetallicRoughness"]!["roughnessFactor"] = 0.875;
            document["samplers"] = Node(new[] { new { magFilter = 9729, minFilter = 9987, wrapS = 33071, wrapT = 33648 } });
            document["textures"]![0]!["sampler"] = 0;
        }
        fixture.Repin();
        FormationVisualData data = fixture.Load();
        foreach (FormationVisualLod lod in data.Lods)
        {
            FormationVisualMaterial material = lod.Materials[0];
            Assert.Equal(new Vector4(.5f, .25f, .125f, .75f), material.BaseColourFactor, "linear color factors are not gamma transformed");
            Assert.Equal(.25f, material.MetallicFactor, "metallic factor");
            Assert.Equal(.875f, material.RoughnessFactor, "roughness factor");
            Assert.Equal(new Vector3(.125f, .25f, .5f), material.EmissiveFactor, "emissive factor");
            Assert.Equal("MASK", material.AlphaMode, "alpha mode");
            Assert.Equal(.375f, material.AlphaCutoff, "alpha cutoff");
            Assert.True(material.DoubleSided, "double-sided rendering request is retained");
            Assert.Equal(.75f, material.Normal!.Factor, "normal-map scale stays role-specific");
            Assert.Equal(.625f, material.Occlusion!.Factor, "AO strength stays role-specific");
            Assert.Equal(1f, material.BaseColour!.Factor, "base texture has no normal/AO factor");
            Assert.Equal(new FormationVisualSampler(9729, 9987, 33071, 33648), material.BaseColour.Sampler, "exact glTF filter and wrap enums");
            Assert.Equal(material.BaseColour.ImageIndex, material.MetallicRoughness!.ImageIndex, "packed data texture binding is not split into new images");
            Assert.Equal(material.BaseColour.ImageIndex, material.Emissive!.ImageIndex, "color-space interpretation belongs to the texture role");
            FormationVisualMaterial defaults = lod.Materials[1];
            Assert.Equal(Vector4.One, defaults.BaseColourFactor, "glTF base color defaults to white");
            Assert.Equal(1f, defaults.MetallicFactor, "glTF metallic default is one, not a guessed rock default");
            Assert.Equal(1f, defaults.RoughnessFactor, "glTF roughness default");
            Assert.Equal(Vector3.Zero, defaults.EmissiveFactor, "glTF emissive default");
            Assert.Equal("OPAQUE", defaults.AlphaMode, "glTF opaque default");
            Assert.Equal(.5f, defaults.AlphaCutoff, "glTF alpha cutoff default is retained even while opaque");
            Assert.True(!defaults.DoubleSided, "glTF culling default");
        }
        using var unspecified = new Fixture();
        FormationVisualTexture texture = unspecified.Load().Lods[0].Materials[0].BaseColour!;
        Assert.Equal(new FormationVisualSampler(0, 0, 10497, 10497), texture.Sampler, "unspecified filters stay unspecified; wraps use glTF repeat default");
    }

    [Test]
    public static void FormationVisualDataSynthesizesDefaultMaterialsWithoutExpandingDeclaredIndexRange()
    {
        using var omitted = new Fixture(textured: false, tangents: false, texCoords: false);
        foreach (JsonObject document in omitted.Documents.Values)
        {
            document.Remove("materials");
            foreach (JsonNode? row in document["meshes"]![0]!["primitives"]!.AsArray())
                row!.AsObject().Remove("material");
        }
        omitted.Repin();
        foreach (FormationVisualLod lod in omitted.Load().Lods)
        foreach (FormationVisualPrimitive primitive in lod.Primitives)
        {
            FormationVisualMaterial material = lod.Materials[primitive.MaterialIndex];
            Assert.Equal(Vector4.One, material.BaseColourFactor, "omitted material uses the glTF base-color default");
            Assert.Equal(1f, material.MetallicFactor, "omitted material uses the glTF metallic default");
            Assert.Equal(1f, material.RoughnessFactor, "omitted material uses the glTF roughness default");
            Assert.True(material.Normal == null && material.BaseColour == null, "default material invents no texture dependency");
        }

        using var invalid = new Fixture(textured: false);
        JsonArray primitives = invalid.Documents["near.gltf"]["meshes"]![0]!["primitives"]!.AsArray();
        primitives[0]!.AsObject().Remove("material");
        primitives[1]!["material"] = 2; // There are only two declared materials, even after synthesizing a default.
        invalid.Repin();
        Reject<FormationValidationException>(() => invalid.Preflight());
    }

    [Test]
    public static void FormationVisualDataDoesNotInventMissingTangentsOrUvStreams()
    {
        using var plain = new Fixture(textured: false, tangents: false, texCoords: false);
        FormationVisualPrimitive primitive = plain.Load().Lods[0].Primitives[0];
        Assert.True(!primitive.HasTangents && primitive.Tangents.Count == 0, "untextured source does not acquire generated tangents");
        Assert.True(!primitive.HasTexCoords && primitive.TexCoords.Count == 0, "absent UV stream remains absent");
        using var noTangents = new Fixture(tangents: false);
        Reject<FormationValidationException>(() => noTangents.Preflight());
        using var noUv = new Fixture(texCoords: false);
        Reject<FormationValidationException>(() => noUv.Preflight());
        using var noNormals = new Fixture(textured: false);
        noNormals.Documents["near.gltf"]["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject().Remove("NORMAL");
        noNormals.Repin();
        Reject<FormationValidationException>(() => noNormals.Preflight());
    }

    [Test]
    public static void FormationVisualDataSnapshotsCannotBeMutatedThroughPublicBuffersOrCollections()
    {
        using var fixture = new Fixture();
        FormationVisualPreflight preflight = fixture.Preflight();
        FormationVisualData data = FormationVisualDataLoader.Load(preflight);
        FormationVisualPrimitive primitive = data.Lods[0].Primitives[0];
        var positions = new float[primitive.Positions.Count]; primitive.Positions.CopyTo(positions);
        positions[0] = 99;
        Assert.Equal(BitConverter.SingleToInt32Bits(fixture.Positions[0][0]), BitConverter.SingleToInt32Bits(primitive.Positions[0]), "copy mutation cannot alter authenticated POSITION");
        var indices = new int[primitive.Indices.Count]; primitive.Indices.CopyTo(indices); indices[0] = 1;
        Assert.Equal(2, primitive.Indices[0], "copy mutation cannot alter authenticated winding");
        var image = new byte[data.Images[0].Bytes.Count]; data.Images[0].Bytes.CopyTo(image); image[0] ^= 0xff;
        Assert.Equal((byte)137, data.Images[0].Bytes[0], "encoded image bytes are private");
        Reject<ArgumentException>(() => primitive.Positions.CopyTo(new float[1]));
        Reject<IndexOutOfRangeException>(() => { _ = primitive.Indices[-1]; });
        Reject<NotSupportedException>(() => ((IList<FormationVisualLod>)data.Lods)[0] = data.Lods[1]);
        Reject<NotSupportedException>(() => ((IList<FormationVisualImage>)data.Images).Clear());
        Reject<NotSupportedException>(() => ((IList<FormationVisualPrimitive>)data.Lods[0].Primitives).Clear());
        Reject<NotSupportedException>(() => ((IList<FormationVisualMaterial>)data.Lods[0].Materials).Clear());
        Reject<NotSupportedException>(() => ((IList<FormationPackageFile>)preflight.RequiredFiles).Clear());
        File.WriteAllBytes(fixture.PathOf("image.png"), new byte[] { 0 });
        File.WriteAllBytes(fixture.PathOf("mesh0.bin"), new byte[] { 0 });
        Assert.Equal((byte)137, data.Images[0].Bytes[0], "published image no longer depends on mutable disk contents");
        Assert.Equal(3, primitive.VertexCount, "published streams no longer depend on disk");
    }

    [Test]
    public static void FormationVisualDataPreflightDeduplicatesFilesAndReservesFullDecodedMipChains()
    {
        using var fixture = new Fixture();
        FormationVisualPreflight preflight = fixture.Preflight();
        FormationVisualSizing sizing = preflight.Sizing;
        Assert.Equal(1, sizing.Images, "same image referenced by both LODs is retained once");
        Assert.Equal(20L, sizing.DecodedImageBytes, "2x2 RGBA plus 1x1 RGBA mip is reserved before pixel decoding");
        Assert.Equal(5, preflight.RequiredFiles.Count, "two JSON files, two shared buffers and one shared image");
        Assert.Equal(preflight.RequiredFiles.Count, preflight.RequiredFiles.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count(), "required-file pins are unique");
        Assert.True(sizing.MetadataBytes > 0 && sizing.EncodedBytes > 0 && sizing.StreamBytes > 0, "all retained CPU payload categories are counted");
        Assert.True(sizing.PeakReservationBytes >= sizing.MetadataBytes + sizing.EncodedBytes + sizing.StreamBytes + sizing.DecodedImageBytes,
            "peak reservation covers payload categories before extra scratch");
        FormationVisualData data = FormationVisualDataLoader.Load(preflight);
        Assert.Equal(1, data.Images.Count, "one encoded image snapshot is published");
        Assert.Equal(2, data.Images[0].Width, "bounded PNG width");
        Assert.Equal(2, data.Images[0].Height, "bounded PNG height");
        Assert.Equal("image/png", data.Images[0].MimeType, "encoded format is explicit");
        Assert.Equal(20L, data.Images[0].DecodedMipBytes, "image publishes its complete conservative pixel allowance");
        Assert.Equal(0, data.Lods[0].Materials[0].BaseColour!.ImageIndex, "first LOD uses global image ordinal");
        Assert.Equal(0, data.Lods[1].Materials[0].BaseColour!.ImageIndex, "second LOD reuses global image ordinal");
    }

    [Test]
    public static void FormationVisualDataRejectsHashChangesTruncationAndTrailingBytesAfterPreflight()
    {
        foreach (string name in new[] { "near.gltf", "mesh0.bin", "image.png" })
        foreach (int change in new[] { 0, 1, 2 })
        {
            using var fixture = new Fixture();
            FormationVisualPreflight preflight = fixture.Preflight();
            byte[] bytes = File.ReadAllBytes(fixture.PathOf(name));
            byte[] altered = change switch { 1 => bytes[..^1], 2 => bytes.Concat(new byte[] { 0 }).ToArray(), _ => (byte[])bytes.Clone() };
            if (change == 0) altered[^1] ^= 1;
            File.WriteAllBytes(fixture.PathOf(name), altered);
            FormationVisualData? result = null;
            Reject<FormationValidationException>(() => result = FormationVisualDataLoader.Load(preflight));
            Assert.True(result == null, "failure in any final dependency publishes no partial payload");
        }
        using var wrongPin = new Fixture();
        Reject<FormationValidationException>(() => FormationVisualDataLoader.Preflight(wrongPin.Package, wrongPin.Directory,
            Hash(Encoding.UTF8.GetBytes("unapproved receipt"))));
        byte[] corrupt = File.ReadAllBytes(wrongPin.PathOf("mesh1.bin")); corrupt[0] ^= 1;
        File.WriteAllBytes(wrongPin.PathOf("mesh1.bin"), corrupt);
        Reject<FormationValidationException>(() => wrongPin.Preflight());
    }

    [Test]
    public static void FormationVisualDataCrosschecksRepinnedStreamsAgainstQualifiedLodGeometry()
    {
        foreach (bool changeIndices in new[] { false, true })
        {
            using var fixture = new Fixture();
            byte[] buffer = File.ReadAllBytes(fixture.PathOf("mesh0.bin"));
            if (changeIndices)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(fixture.IndexOffsets[0]), 0);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(fixture.IndexOffsets[0] + 2), 2);
            }
            else BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(.25f));
            File.WriteAllBytes(fixture.PathOf("mesh0.bin"), buffer);
            fixture.Repin(); // Byte pins are now valid, but separately qualified geometry is unchanged.
            FormationVisualPreflight preflight = fixture.Preflight();
            Reject<FormationValidationException>(() => FormationVisualDataLoader.Load(preflight));
        }
    }

    [Test]
    public static void FormationVisualDataRejectsNonfiniteAndInvalidAuthoredAttributeValues()
    {
        foreach ((int offset, float value) in new[]
        {
            (0, float.NaN), (36, float.PositiveInfinity), (72, float.NaN), (120, float.NegativeInfinity),
        })
        {
            using var fixture = new Fixture();
            byte[] buffer = File.ReadAllBytes(fixture.PathOf("mesh0.bin"));
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), BitConverter.SingleToInt32Bits(value));
            File.WriteAllBytes(fixture.PathOf("mesh0.bin"), buffer); fixture.Repin();
            Reject<FormationValidationException>(() => fixture.Load());
        }
        using var outside = new Fixture(wideIndices: true);
        byte[] indices = File.ReadAllBytes(outside.PathOf("mesh0.bin"));
        BinaryPrimitives.WriteUInt32LittleEndian(indices.AsSpan(outside.IndexOffsets[0]), uint.MaxValue);
        File.WriteAllBytes(outside.PathOf("mesh0.bin"), indices); outside.Repin();
        Reject<FormationValidationException>(() => outside.Load());
    }

    [Test]
    public static void FormationVisualDataRejectsUnsupportedTopologyTransformsAndAccessorLayouts()
    {
        Action<JsonObject>[] mutations =
        {
            g => g["nodes"]![0]!["translation"] = Node(new[] { 0, 0, 0 }),
            g => g["nodes"]![0]!["matrix"] = Node(new[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }),
            g => g["nodes"]![0]!["children"] = Node(new[] { 0 }),
            g => g["meshes"]![0]!["primitives"]![0]!["mode"] = 5,
            g => g["meshes"]![0]!["primitives"]![0]!["targets"] = new JsonArray(),
            g => g["extensionsUsed"] = Node(new[] { "KHR_mesh_quantization" }),
            g => g["accessors"]![0]!["normalized"] = true,
            g => g["accessors"]![0]!["sparse"] = new JsonObject(),
            g => g["accessors"]![1]!["count"] = 2,
            g => g["accessors"]![0]!["componentType"] = 5123,
            g => g["accessors"]![0]!["byteOffset"] = int.MaxValue,
            g => g["bufferViews"]![0]!["byteStride"] = 8,
            g => g["bufferViews"]![1]!["byteOffset"] = 0,
            g => g["bufferViews"]![0]!["byteLength"] = int.MaxValue,
            g => g["meshes"]![0]!["primitives"]![1]!["attributes"]!["POSITION"] = 0,
            g => g["accessors"]!.AsArray().Add(g["accessors"]![0]!.DeepClone()),
            g => g["bufferViews"]!.AsArray().Add(g["bufferViews"]![0]!.DeepClone()),
            g => g["buffers"]!.AsArray().Add(g["buffers"]![0]!.DeepClone()),
            g => g["buffers"]![1]!["uri"] = "MESH0.BIN",
        };
        foreach (Action<JsonObject> mutate in mutations)
        {
            using var fixture = new Fixture(); mutate(fixture.Documents["near.gltf"]); fixture.Repin();
            RejectAnyAdmission(() => fixture.Preflight());
        }
    }

    [Test]
    public static void FormationVisualDataRejectsAmbiguousPathsAndUnlistedDependencies()
    {
        foreach (string uri in new[] { "../mesh0.bin", "/mesh0.bin", "C:/mesh0.bin", "folder\\mesh0.bin", "./mesh0.bin",
            "mesh0.bin#fragment", "mesh0%2ebin", "mesh0.bin?query", "data:application/octet-stream;base64,AA==", "unlisted.bin" })
        {
            using var fixture = new Fixture();
            fixture.Documents["near.gltf"]["buffers"]![0]!["uri"] = uri; fixture.Repin();
            RejectAnyAdmission(() => fixture.Preflight());
        }
        using var image = new Fixture();
        image.Documents["far.gltf"]["images"]![0]!["uri"] = "../image.png"; image.Repin();
        RejectAnyAdmission(() => image.Preflight());
    }

    [Test]
    public static void FormationVisualDataRejectsAuthoredZeroFiltersButAcceptsOmittedFilters()
    {
        foreach (string key in new[] { "magFilter", "minFilter" })
        {
            using var fixture = new Fixture();
            JsonObject sampler = new() { ["magFilter"] = 9729, ["minFilter"] = 9987 };
            sampler[key] = 0;
            fixture.Documents["near.gltf"]["samplers"] = new JsonArray(sampler);
            fixture.Documents["near.gltf"]["textures"]![0]!["sampler"] = 0;
            fixture.Repin();
            Reject<FormationValidationException>(() => fixture.Preflight());
        }
        using var omitted = new Fixture();
        omitted.Documents["near.gltf"]["samplers"] = new JsonArray(new JsonObject());
        omitted.Documents["near.gltf"]["textures"]![0]!["sampler"] = 0;
        omitted.Repin();
        FormationVisualData data = FormationVisualDataLoader.Load(omitted.Preflight());
        FormationVisualLod near = data.Lods.Single(lod => lod.LodKey == "near");
        Assert.Equal(new FormationVisualSampler(0, 0, 10497, 10497), near.Materials[0].BaseColour!.Sampler,
            "omitted filters inside an explicit sampler retain the unspecified sentinel");
    }

    [Test]
    public static void FormationVisualDataRejectsInvalidMaterialAndImageContracts()
    {
        Action<JsonObject>[] mutations =
        {
            g => g["materials"]![0]!["unknown"] = true,
            g => g["materials"]![0]!["alphaMode"] = "ADDITIVE",
            g => g["materials"]![0]!["pbrMetallicRoughness"]!["metallicFactor"] = 1.01,
            g => g["materials"]![0]!["pbrMetallicRoughness"]!["baseColorFactor"] = Node(new[] { 1, 1, 1 }),
            g => g["materials"]![0]!["normalTexture"]!["texCoord"] = 1,
            g => g["materials"]![0]!["occlusionTexture"]!["strength"] = -0.1,
            g => g["textures"]![0]!["source"] = 99,
            g => g["textures"]![0]!["sampler"] = 0,
            g => g["images"]![0]!["mimeType"] = "image/webp",
            g => g["images"]![0]!["bufferView"] = 0,
            g => { g["samplers"] = Node(new[] { new { magFilter = 9987 } }); g["textures"]![0]!["sampler"] = 0; },
            g => g["materials"]![0]!["normalTexture"]!["extensions"] = new JsonObject(),
        };
        foreach (Action<JsonObject> mutate in mutations)
        {
            using var fixture = new Fixture(); mutate(fixture.Documents["near.gltf"]); fixture.Repin();
            RejectAnyAdmission(() => fixture.Preflight());
        }
        foreach (byte[] bytes in new[] { new byte[] { 137, 80, 78, 71 }, new byte[33] })
        {
            using var fixture = new Fixture(); File.WriteAllBytes(fixture.PathOf("image.png"), bytes); fixture.Repin();
            Reject<FormationValidationException>(() => fixture.Preflight());
        }
    }

    [Test]
    public static void FormationVisualDataRejectsDuplicateJsonAndBoundedDepthBeforeLoad()
    {
        using var duplicate = new Fixture();
        string json = duplicate.Documents["near.gltf"].ToJsonString().Replace("\"version\":\"2.0\"", "\"version\":\"2.0\",\"version\":\"2.0\"", StringComparison.Ordinal);
        duplicate.WriteJsonBytes("near.gltf", Encoding.UTF8.GetBytes(json));
        Reject<FormationValidationException>(() => duplicate.Preflight());
        using var deep = new Fixture();
        JsonNode nested = JsonValue.Create(1)!;
        for (int i = 0; i < 40; i++) nested = new JsonArray(nested);
        deep.Documents["near.gltf"]["extras"] = nested; deep.Repin();
        Reject<FormationValidationException>(() => deep.Preflight());
    }

    [Test]
    public static void FormationVisualDataAppliesAggregateAndPeakBudgetsBeforePublication()
    {
        using var fixture = new Fixture();
        FormationVisualSizing sizing = fixture.Preflight().Sizing;
        FormationVisualLimits[] limits =
        {
            new() { MaxLods = 1 }, new() { MaxFiles = 1 }, new() { MaxPrimitives = 3 },
            new() { MaxMaterials = 1 }, new() { MaxBuffersPerLod = 1 }, new() { MaxDescriptorsPerLod = 1 },
            new() { MaxVertices = 11 }, new() { MaxIndices = 11 }, new() { MaxJsonBytes = 16 },
            new() { MaxFileBytes = 16 }, new() { MaxEncodedBytes = 1 }, new() { MaxStreamBytes = 1 },
            new() { MaxDecodedImageBytes = 19 }, new() { MaxMetadataBytes = 1 },
            new() { MaxReservationBytes = sizing.PeakReservationBytes - 1 }, new() { MaxImageDimension = 1 },
            new() { MaxValidationSteps = 1 },
        };
        foreach (FormationVisualLimits limit in limits)
            Reject<FormationCapacityException>(() => fixture.Preflight(limit));
        Assert.Equal(sizing, fixture.Preflight(new FormationVisualLimits { MaxReservationBytes = sizing.PeakReservationBytes }).Sizing,
            "exact peak allowance passes; the bound is not off by one");
        RejectAnyAdmission(() => fixture.Preflight(new FormationVisualLimits { MaxDescriptorsPerLod = 129 }));
        RejectAnyAdmission(() => fixture.Preflight(new FormationVisualLimits { MaxImageDimension = 0 }));
    }

    [Test]
    public static void FormationVisualDataCancellationPublishesNoPayloadAndAllowsIndependentRetry()
    {
        using var fixture = new Fixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => fixture.Preflight(cancellation: cancelled.Token));
        FormationVisualPreflight preflight = fixture.Preflight();
        FormationVisualData? result = null;
        Reject<OperationCanceledException>(() => result = FormationVisualDataLoader.Load(preflight, cancelled.Token));
        Assert.True(result == null, "cancelled load cannot return partial LODs/images");
        Assert.Equal(2, FormationVisualDataLoader.Load(preflight).Lods.Count, "one cancelled attempt does not mutate the immutable preflight plan");
    }

    private static void SameBits(float[] expected, FormationVisualArray<float> actual, string stream)
    {
        Assert.Equal(expected.Length, actual.Count, stream + " component count");
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(BitConverter.SingleToInt32Bits(expected[i]), BitConverter.SingleToInt32Bits(actual[i]), stream + " exact component " + i);
    }

    private static JsonNode Node<T>(T value) => JsonSerializer.SerializeToNode(value)!;
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static T Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
    private static void RejectAnyAdmission(Action action)
    {
        try { action(); }
        catch (FormationValidationException) { return; }
        catch (FormationCapacityException) { return; }
        throw new TestFailure("Expected explicit formation visual admission failure.");
    }

    // Synthetic already-qualified package. It deliberately does not use or change the old one-primitive authoring
    // loader, and supplies no art acceptance or physical permissions for any real candidate.
    private sealed class Fixture : IDisposable
    {
        internal static readonly float[] Normals = { 0, 1, 0, 0, 1, 0, 0, 1, 0 };
        internal static readonly float[] Tangents = { 1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1 };
        internal static readonly float[] TexCoords = { .125f, -0f, 2.25f, .5f, -.25f, 1.25f };
        internal readonly float[][] Positions = { new[] { -0f, 0, 0, 0, 0, 1, 1, 0, 0 }, new[] { 3f, 0, 0, 3, 0, 1, 4, 0, 0 } };
        internal readonly int[] IndexOffsets = new int[2];
        internal readonly Dictionary<string, JsonObject> Documents = new(StringComparer.Ordinal);
        private readonly List<string> _files = new();
        private readonly string _receipt = Hash(Encoding.UTF8.GetBytes("independently approved synthetic visual receipt"));
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "hollow-visual-data-" + Guid.NewGuid().ToString("N"));
        internal FormationPackage Package { get; private set; } = null!;

        internal Fixture(bool textured = true, bool tangents = true, bool texCoords = true, bool wideIndices = false)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var views = new JsonArray(); var accessors = new JsonArray(); var primitives = new JsonArray(); var buffers = new JsonArray();
            for (int part = 0; part < 2; part++)
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                int Attribute(float[] values, string shape)
                {
                    int view = views.Count, accessor = accessors.Count, offset = checked((int)stream.Length);
                    foreach (float value in values) writer.Write(value);
                    views.Add(Node(new { buffer = part, byteOffset = offset, byteLength = values.Length * 4, target = 34962 }));
                    accessors.Add(Node(new { bufferView = view, componentType = 5126, count = 3, type = shape }));
                    return accessor;
                }
                var attributes = new JsonObject { ["POSITION"] = Attribute(Positions[part], "VEC3"), ["NORMAL"] = Attribute(Normals, "VEC3") };
                if (tangents) attributes["TANGENT"] = Attribute(Tangents, "VEC4");
                if (texCoords) attributes["TEXCOORD_0"] = Attribute(TexCoords, "VEC2");
                int indexView = views.Count, indexAccessor = accessors.Count;
                IndexOffsets[part] = checked((int)stream.Length);
                foreach (int index in new[] { 2, 0, 1 }) { if (wideIndices) writer.Write((uint)index); else writer.Write((ushort)index); }
                views.Add(Node(new { buffer = part, byteOffset = IndexOffsets[part], byteLength = wideIndices ? 12 : 6, target = 34963 }));
                accessors.Add(Node(new { bufferView = indexView, componentType = wideIndices ? 5125 : 5123, count = 3, type = "SCALAR" }));
                primitives.Add(new JsonObject { ["attributes"] = attributes, ["indices"] = indexAccessor, ["material"] = part, ["mode"] = 4 });
                string file = "mesh" + part + ".bin";
                Write(file, stream.ToArray());
                buffers.Add(Node(new { uri = file, byteLength = stream.Length }));
            }
            var materials = new JsonArray(new JsonObject { ["name"] = "source-pbr" }, new JsonObject { ["name"] = "defaults" });
            if (textured)
            {
                materials[0]!["pbrMetallicRoughness"] = Node(new { baseColorTexture = new { index = 0 }, metallicRoughnessTexture = new { index = 0 } });
                materials[0]!["normalTexture"] = Node(new { index = 0 });
                materials[0]!["occlusionTexture"] = Node(new { index = 0 });
            }
            var document = new JsonObject
            {
                ["asset"] = Node(new { version = "2.0" }), ["scene"] = 0,
                ["scenes"] = Node(new[] { new { nodes = new[] { 0 } } }), ["nodes"] = Node(new[] { new { mesh = 0 } }),
                ["meshes"] = new JsonArray(new JsonObject { ["primitives"] = primitives }),
                ["buffers"] = buffers, ["bufferViews"] = views, ["accessors"] = accessors, ["materials"] = materials,
            };
            if (textured)
            {
                Write("image.png", Png());
                document["images"] = Node(new[] { new { uri = "image.png", mimeType = "image/png" } });
                document["textures"] = Node(new[] { new { source = 0 } });
            }
            foreach (string name in new[] { "near.gltf", "far.gltf" }) Documents.Add(name, document.DeepClone().AsObject());
            Repin();
        }

        internal string PathOf(string file) => Path.Combine(Directory, file);
        private void Write(string file, byte[] bytes)
        {
            File.WriteAllBytes(PathOf(file), bytes);
            if (!_files.Contains(file)) _files.Add(file);
        }
        internal void Repin()
        {
            foreach (var pair in Documents) Write(pair.Key, Encoding.UTF8.GetBytes(pair.Value.ToJsonString()));
            Pin();
        }
        internal void WriteJsonBytes(string file, byte[] bytes) { Write(file, bytes); Pin(); }
        private void Pin()
        {
            var vertices = Positions.SelectMany(values => Enumerable.Range(0, 3).Select(i => new Vector3D(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]))).ToArray();
            var patches = new[] { new SupportPatch("bare", "rock", SupportCapabilities.None) };
            var physical = new FormationGeometry("fixture", 3, "stone", vertices[..3], new[] { new GeometryTriangle(0, 1, 2, 0) }, patches);
            var lods = new List<CliffLodBinding>(); var visuals = new List<FormationVisualAsset>();
            foreach (string key in new[] { "near", "far" })
            {
                string file = key + ".gltf", hash = Hash(File.ReadAllBytes(PathOf(file)));
                var mesh = new FormationGeometry("fixture/" + key, 3, "stone", vertices,
                    new[] { new GeometryTriangle(2, 0, 1, 0), new GeometryTriangle(5, 3, 4, 0) }, patches);
                lods.Add(new CliffLodBinding(key, hash, mesh, Array.Empty<CliffLodTriangleMap>(), 0, 1));
                visuals.Add(new FormationVisualAsset("fixture", 3, key, file, hash));
            }
            var definition = new CliffSupportDefinition(physical, _receipt, _receipt, 1, 1,
                new[] { new CliffPatchDomain("bare", new[] { new CliffPatchBoundary(new[] { 0, 1, 2 }) }) },
                Array.Empty<CliffContactDomain>(), lods);
            var profile = new SubstrateProfile("rock", 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 40, .6);
            var catalog = new FormationCatalog(new[] { definition }, new[] { profile }, Array.Empty<FormationRecipe>());
            Package = new FormationPackage(catalog, _receipt, visuals.ToArray(), _files.OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new FormationPackageFile(f, Hash(File.ReadAllBytes(PathOf(f))), new FileInfo(PathOf(f)).Length)).ToArray());
        }
        internal FormationVisualPreflight Preflight(FormationVisualLimits? limits = null, CancellationToken cancellation = default)
            => FormationVisualDataLoader.Preflight(Package, Directory, _receipt, limits, cancellation);
        internal FormationVisualData Load() => FormationVisualDataLoader.Load(Preflight());
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        private static byte[] Png()
        {
            using var output = new MemoryStream();
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, 2); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 2);
            header[8] = 8; header[9] = 6;
            Chunk("IHDR", header);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(new byte[] { 0, 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 255 });
            Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
            return output.ToArray();
            void Chunk(string type, byte[] payload)
            {
                byte[] length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, payload.Length); output.Write(length);
                byte[] tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(payload);
                uint crc = uint.MaxValue;
                foreach (byte value in tag.Concat(payload))
                {
                    crc ^= value;
                    for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
                }
                BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); output.Write(length);
            }
        }
    }
}
