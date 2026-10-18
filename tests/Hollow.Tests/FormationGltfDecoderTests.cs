using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationGltfDecoderTests
{
    [Test]
    public static void FormationGltfPreservesFloatPositionsTriangleOrdinalsAndWindingWithoutPermissions()
    {
        foreach (bool wide in new[] { false, true })
        {
            Fixture source = Make(wide);
            int resolutions = 0;
            FormationGeometry geometry = FormationGltfDecoder.Decode("fixture", 7, "rock", Bytes(source.Json), uri =>
            {
                Assert.Equal("mesh.bin", uri, "only the declared buffer is resolved");
                resolutions++;
                return source.Buffer;
            });
            Assert.Equal(1, resolutions, "one buffer callback");
            Assert.Equal("fixture", geometry.Key, "key");
            Assert.Equal(7, geometry.Revision, "revision");
            Assert.Equal("rock", geometry.FamilyKey, "family");
            Assert.Equal(4, geometry.VertexCount, "all authored vertex ordinals retained");
            Assert.Equal(2, geometry.TriangleCount, "all authored triangle ordinals retained");
            Assert.Equal(new Vector3D(-1.25, 2.5, -2), geometry.Vertices[0], "float values converted exactly");
            Assert.Equal(new Vector3D(2.75, 2.5, 3), geometry.Vertices[2], "last bounds corner");
            Assert.Equal(new GeometryTriangle(2, 0, 1, 0), geometry.Triangles[0], "first triangle order");
            Assert.Equal(new GeometryTriangle(3, 2, 1, 0), geometry.Triangles[1], "second triangle order");
            Assert.Equal(SupportCapabilities.None, geometry.Patches[0].Capabilities, "visual decoding grants no physical permissions");
            Assert.Equal(1, geometry.PatchCount, "single neutral patch");
            source.Buffer.AsSpan().Clear();
            Assert.Equal(new Vector3D(-1.25, 2.5, -2), geometry.Vertices[0], "published geometry owns its copy");
            Assert.Equal(new GeometryTriangle(2, 0, 1, 0), geometry.Triangles[0], "indices independent of input buffer");
        }
    }

    [Test]
    public static void FormationGltfAcceptsBoundedInterleavingAndOffsetPaddingWithoutReadingImages()
    {
        Fixture source = Make(interleaved: true);
        source.Json["images"] = JsonNode.Parse("[{\"uri\":\"texture.png\"}]");
        source.Json["materials"] = JsonNode.Parse("[{\"name\":\"unchanged art metadata\"}]");
        source.Json["textures"] = JsonNode.Parse("[{\"source\":0}]");
        source.Json["meshes"]![0]!["primitives"]![0]!["material"] = 0;
        FormationGeometry geometry = Decode(source);
        Assert.Equal(4, geometry.VertexCount, "position stride and accessor/view offsets");
        Assert.Equal(new Vector3D(2.75, 2.5, 3), geometry.Vertices[2], "interleaved vertex remains exact");
        Assert.Equal(new GeometryTriangle(3, 2, 1, 0), geometry.Triangles[1], "index view offset remains exact");
        source.Json["accessors"]![0]!["normalized"] = false;
        Assert.Equal(4, Decode(source).VertexCount, "explicit false normalization is ordinary float input");
    }

    [Test]
    public static void FormationGltfRejectsSceneTransformsSkinMorphCompressedAndUnknownCoordinatePaths()
    {
        Action<JsonObject>[] mutations =
        {
            j => j["asset"]!["version"] = "1.0",
            j => j["asset"]!["minVersion"] = "2.1",
            j => j["scene"] = 1,
            j => j["scenes"]![0]!["nodes"] = JsonNode.Parse("[0,0]"),
            j => j["nodes"]!.AsArray().Add(new JsonObject { ["mesh"] = 0 }),
            j => j["nodes"]![0]!["mesh"] = 1,
            j => j["nodes"]![0]!["translation"] = JsonNode.Parse("[0,0,0]"),
            j => j["nodes"]![0]!["rotation"] = JsonNode.Parse("[0,0,0,1]"),
            j => j["nodes"]![0]!["scale"] = JsonNode.Parse("[1,1,1]"),
            j => j["nodes"]![0]!["matrix"] = JsonNode.Parse("[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]"),
            j => j["nodes"]![0]!["children"] = JsonNode.Parse("[]"),
            j => j["nodes"]![0]!["skin"] = 0,
            j => j["animations"] = JsonNode.Parse("[{}]"),
            j => j["skins"] = JsonNode.Parse("[{}]"),
            j => j["meshes"]![0]!["weights"] = JsonNode.Parse("[0]"),
            j => j["meshes"]![0]!["primitives"]![0]!["targets"] = JsonNode.Parse("[]"),
            j => j["meshes"]![0]!["primitives"]![0]!["mode"] = 5,
            j => j["meshes"]![0]!["primitives"]!.AsArray().Add(new JsonObject()),
            j => j["meshes"]![0]!["primitives"]![0]!["attributes"]!["JOINTS_0"] = 0,
            j => j["extensionsUsed"] = JsonNode.Parse("[\"KHR_mesh_quantization\"]"),
            j => j["extensionsRequired"] = JsonNode.Parse("[\"KHR_draco_mesh_compression\"]"),
            j => j["bufferViews"]![0]!["extensions"] = JsonNode.Parse("{\"EXT_meshopt_compression\":{}}"),
            j => j["materials"] = JsonNode.Parse("[{\"extensions\":{\"KHR_texture_transform\":{}}}]"),
            j => j["accessors"]![0]!["sparse"] = new JsonObject(),
            j => j["accessors"]![0]!["normalized"] = true,
            j => j["accessors"]![0]!["normalized"] = 0,
            j => j["accessors"]![0]!["componentType"] = 5122,
            j => j["accessors"]![0]!["type"] = "MAT3"
        };
        foreach (Action<JsonObject> mutation in mutations) RejectBeforeBuffer<FormationValidationException>(mutation);
    }

    [Test]
    public static void FormationGltfRejectsAccessorRangesAlignmentAliasingAndMalformedReferences()
    {
        Action<JsonObject>[] mutations =
        {
            j => j["buffers"]!.AsArray().Add(new JsonObject { ["uri"] = "extra.bin", ["byteLength"] = 1 }),
            j => j["bufferViews"]![0]!["buffer"] = 1,
            j => j["bufferViews"]![0]!["byteOffset"] = -1,
            j => j["bufferViews"]![0]!["byteOffset"] = int.MaxValue,
            j => j["bufferViews"]![0]!["byteLength"] = int.MaxValue,
            j => j["bufferViews"]![0]!["byteLength"] = 47,
            j => j["bufferViews"]![0]!["byteStride"] = 0,
            j => j["bufferViews"]![0]!["byteStride"] = 8,
            j => j["bufferViews"]![0]!["byteStride"] = 14,
            j => j["bufferViews"]![0]!["byteStride"] = 256,
            j => j["bufferViews"]![0]!["target"] = 0,
            j => j["bufferViews"]![0]!["target"] = 34963,
            j => j["bufferViews"]![1]!["target"] = 34962,
            j => j["bufferViews"]![1]!["byteStride"] = 4,
            j => j["accessors"]![0]!["bufferView"] = -1,
            j => j["accessors"]![0]!["bufferView"] = 2,
            j => j["accessors"]![0]!["byteOffset"] = 2,
            j => j["accessors"]![0]!["byteOffset"] = int.MaxValue,
            j => j["accessors"]![0]!["count"] = 0,
            j => j["accessors"]![0]!["count"] = 5,
            j => j["accessors"]![0]!["count"] = 2147483648L,
            j => j["accessors"]![0]!["count"] = "4",
            j => j["accessors"]![1]!["count"] = 5,
            j => j["accessors"]![1]!["componentType"] = 5126,
            j => j["meshes"]![0]!["primitives"]![0]!["indices"] = -1,
            j => j["meshes"]![0]!["primitives"]![0]!["attributes"]!["POSITION"] = true,
            j => j["bufferViews"]![1]!["byteOffset"] = 0,
            j => j["meshes"]![0]!["primitives"]![0]!["attributes"]!["NORMAL"] = 0,
            j => j["accessors"]!.AsArray().Add(j["accessors"]![0]!.DeepClone()),
            j => j["bufferViews"]!.AsArray().Add(j["bufferViews"]![0]!.DeepClone())
        };
        foreach (Action<JsonObject> mutation in mutations) RejectBeforeBuffer<FormationValidationException>(mutation);
        Fixture interleaved = Make(interleaved: true);
        interleaved.Json["accessors"]![1]!["byteOffset"] = 8;
        Reject<FormationValidationException>(() => Decode(interleaved));
    }

    [Test]
    public static void FormationGltfRejectsNonfinitePositionsInvalidIndicesDegeneracyAndWrongBufferLengths()
    {
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Fixture source = Make();
            WriteFloat(source.Buffer, 0, value);
            Reject<FormationValidationException>(() => Decode(source));
        }
        foreach (uint value in new[] { 4U, uint.MaxValue })
        {
            Fixture source = Make(wide: true);
            BinaryPrimitives.WriteUInt32LittleEndian(source.Buffer.AsSpan(48), value);
            Reject<FormationValidationException>(() => Decode(source));
        }
        Fixture degenerate = Make();
        BinaryPrimitives.WriteUInt16LittleEndian(degenerate.Buffer.AsSpan(48), 0);
        Reject<FormationValidationException>(() => Decode(degenerate));
        Fixture valid = Make();
        Reject<FormationValidationException>(() => FormationGltfDecoder.Decode("mesh", 1, "rock", Bytes(valid.Json), _ => valid.Buffer.AsMemory(1)));
        Reject<FormationValidationException>(() => FormationGltfDecoder.Decode("mesh", 1, "rock", Bytes(valid.Json), _ => new byte[valid.Buffer.Length + 1]));
        var dependencyFailure = new InvalidOperationException("owner rejected dependency");
        try { FormationGltfDecoder.Decode("mesh", 1, "rock", Bytes(valid.Json), _ => throw dependencyFailure); }
        catch (InvalidOperationException error) { Assert.True(ReferenceEquals(error, dependencyFailure), "owner failure preserved"); return; }
        throw new TestFailure("Missing dependency failure.");
    }

    [Test]
    public static void FormationGltfHonoursCallerBudgetsAndHardCeilingsBeforeDependencyResolution()
    {
        var limits = new FormationGeometryLimits { MaxVertices = 4, MaxTriangles = 2, MaxPatches = 1, MaxEstimatedBytes = 4096 };
        Assert.Equal(2, Decode(Make(), limits).TriangleCount, "exact count budgets admitted");
        foreach (FormationGeometryLimits low in new[]
        {
            limits with { MaxVertices = 3 }, limits with { MaxTriangles = 1 }, limits with { MaxEstimatedBytes = 1 },
            limits with { MaxEstimatedBytes = 512 + 72 * 4 + 48 * 2 + 2048 },
            limits with { MaxVertices = 60001 }, limits with { MaxTriangles = 50001 }
        })
            RejectBeforeBuffer<FormationCapacityException>(_ => { }, low);
        RejectBeforeBuffer<FormationValidationException>(_ => { }, limits with { MaxVertices = 2 });
        RejectBeforeBuffer<FormationValidationException>(_ => { }, limits with { MaxPatches = 0 });
        RejectBeforeBuffer<FormationCapacityException>(j => j["buffers"]![0]!["byteLength"] = 32 * 1024 * 1024 + 1);
        RejectBeforeBuffer<FormationCapacityException>(j => j["accessors"]![0]!["count"] = 60001);
        RejectBeforeBuffer<FormationCapacityException>(j => j["accessors"]![1]!["count"] = 150001);
        Fixture large = Make();
        large.Json["extras"] = new string(' ', FormationGltfDecoder.MaximumJsonBytes);
        int callbacks = 0;
        Reject<FormationCapacityException>(() => FormationGltfDecoder.Decode("mesh", 1, "rock", Bytes(large.Json), _ => { callbacks++; return large.Buffer; }));
        Assert.Equal(0, callbacks, "JSON cap checked before callback");
    }

    [Test]
    public static void FormationGltfOnlyAcceptsUnambiguousRelativeExternalBufferUris()
    {
        foreach (string uri in new[] { "", " mesh.bin", "mesh.bin ", "/mesh.bin", "../mesh.bin", "a/../mesh.bin", "./mesh.bin", "a//mesh.bin",
            "a\\mesh.bin", "data:application/octet-stream;base64,AA==", "https://example.invalid/mesh.bin", "mesh.bin?x=1", "mesh.bin#part", "%6desh.bin", "mesh\nbin" })
            RejectBeforeBuffer<FormationValidationException>(j => j["buffers"]![0]!["uri"] = uri);
        Fixture valid = Make(); valid.Json["buffers"]![0]!["uri"] = "buffers/mesh.bin";
        FormationGeometry result = FormationGltfDecoder.Decode("mesh", 1, "rock", Bytes(valid.Json), uri =>
        {
            Assert.Equal("buffers/mesh.bin", uri, "relative URI passed unchanged to authenticated owner"); return valid.Buffer;
        });
        Assert.Equal(2, result.TriangleCount, "nested relative buffer");
    }

    [Test]
    public static void FormationJsonRejectsDuplicateFieldsAtEveryDepthMalformedAndOverdeepInput()
    {
        foreach (string json in new[]
        {
            "{\"a\":1,\"a\":2}", "{\"outer\":[{\"name\":1,\"na\\u006de\":2}]}",
            "{\"a\":1,}", "{/* comment */\"a\":1}", "{\"a\":NaN}", "{\"a\":Infinity}", "",
            new string('[', 33) + "0" + new string(']', 33)
        }) Reject<FormationValidationException>(() => { using JsonDocument ignored = FormationJson.Parse(Encoding.UTF8.GetBytes(json)); });
        using JsonDocument valid = FormationJson.Parse(Encoding.UTF8.GetBytes("{\"a\":1,\"A\":2,\"nested\":[{\"a\":3}]}"));
        Assert.Equal(1, valid.RootElement.GetProperty("a").GetInt32(), "property names are ordinal and scoped");
        Reject<FormationCapacityException>(() => { using JsonDocument ignored = FormationJson.Parse(Encoding.UTF8.GetBytes("{} "), 2); });
        Reject<FormationValidationException>(() => { using JsonDocument ignored = FormationJson.Parse(Encoding.UTF8.GetBytes("{}"), 0); });
        Reject<FormationValidationException>(() => { using JsonDocument ignored = FormationJson.Parse(Encoding.UTF8.GetBytes("{}"), FormationJson.MaximumBytes + 1); });
        Fixture source = Make();
        string duplicate = source.Json.ToJsonString().Replace("\"componentType\":5126", "\"componentType\":5126,\"componentType\":5126", StringComparison.Ordinal);
        int callbacks = 0;
        Reject<FormationValidationException>(() => FormationGltfDecoder.Decode("mesh", 1, "rock", Encoding.UTF8.GetBytes(duplicate), _ => { callbacks++; return source.Buffer; }));
        Assert.Equal(0, callbacks, "duplicate declaration rejected before dependency callback");
    }

    private sealed record Fixture(JsonObject Json, byte[] Buffer);
    [Test]
    public static void FormationGltfMultipleMaterialsAndBuffersKeepCanonicalPrimitiveOrdinals()
    {
        foreach (bool sharedBuffer in new[] { false, true })
        {
            Fixture first = Make(interleaved: true), second = Make(interleaved: true);
            // Distinct heights make a mistakenly reused buffer detectable.
            for (int i = 0; i < 4; i++) WriteFloat(second.Buffer, 8 + i * 32, 12.5f);
            AppendPrimitive(first.Json, second.Json, sharedBuffer, first.Buffer.Length);
            byte[] combined = new byte[first.Buffer.Length + second.Buffer.Length];
            first.Buffer.CopyTo(combined, 0); second.Buffer.CopyTo(combined, first.Buffer.Length);
            int resolutions = 0;
            FormationGeometry result = FormationGltfDecoder.Decode("multi", 1, "rock", Bytes(first.Json), uri =>
            {
                resolutions++;
                return sharedBuffer ? combined : uri == "mesh.bin" ? first.Buffer : second.Buffer;
            });
            Assert.Equal(sharedBuffer ? 1 : 2, resolutions, "each declared buffer resolved once");
            Assert.Equal(8, result.VertexCount, "vertices concatenate in primitive declaration order");
            Assert.Equal(4, result.TriangleCount, "triangles concatenate in primitive declaration order");
            Assert.Equal(new Vector3D(-1.25, 12.5, -2), result.Vertices[4], "second primitive uses its own buffer view");
            Assert.Equal(new GeometryTriangle(6, 4, 5, 0), result.Triangles[2], "second primitive indices rebase exactly once");
            Assert.Equal(new GeometryTriangle(7, 6, 5, 0), result.Triangles[3], "last authored ordinal and winding");
            Assert.Equal(SupportCapabilities.None, result.Patches[0].Capabilities, "material regions grant no support permissions");
        }
    }

    [Test]
    public static void FormationGltfMultiplePrimitiveAdmissionIsAggregateAndPrecedesDependencies()
    {
        foreach (string failure in new[] { "vertices", "triangles", "buffers", "duplicate-uri", "alias", "shared-accessor", "primitives" })
        {
            Fixture first = Make(), second = Make();
            AppendPrimitive(first.Json, second.Json, false, 0);
            var limits = new FormationGeometryLimits { MaxVertices = 8, MaxTriangles = 4, MaxPatches = 1, MaxEstimatedBytes = 8192 };
            switch (failure)
            {
                case "vertices": limits = limits with { MaxVertices = 7 }; break;
                case "triangles": limits = limits with { MaxTriangles = 3 }; break;
                case "buffers": first.Json["buffers"]![1]!["byteLength"] = FormationGltfDecoder.MaximumBufferBytes; break;
                case "duplicate-uri": first.Json["buffers"]![1]!["uri"] = "MESH.BIN"; break;
                case "alias": first.Json["bufferViews"]![3]!["byteOffset"] = 0; break;
                case "shared-accessor": first.Json["meshes"]![0]!["primitives"]![1]!["attributes"]!["POSITION"] = 0; break;
                case "primitives":
                    while (first.Json["meshes"]![0]!["primitives"]!.AsArray().Count <= FormationGltfDecoder.MaximumPrimitives)
                        first.Json["meshes"]![0]!["primitives"]!.AsArray().Add(new JsonObject());
                    break;
            }
            int callbacks = 0;
            bool refused = false;
            try { FormationGltfDecoder.Decode("multi", 1, "rock", Bytes(first.Json), _ => { callbacks++; return first.Buffer; }, limits); }
            catch (Exception error) when (error is FormationValidationException or FormationCapacityException) { refused = true; }
            Assert.True(refused, failure + " refused");
            Assert.Equal(0, callbacks, failure + " refused before dependencies");
        }
    }

    [Test]
    public static void FormationGltfIndicesCannotEscapeTheirOwnPrimitiveVertexRange()
    {
        Fixture first = Make(), second = Make();
        AppendPrimitive(first.Json, second.Json, false, 0);
        // 4 exists in the flattened result but not in either primitive's authored local array.
        BinaryPrimitives.WriteUInt16LittleEndian(second.Buffer.AsSpan(48), 4);
        Reject<FormationValidationException>(() => FormationGltfDecoder.Decode("multi", 1, "rock", Bytes(first.Json),
            uri => uri == "mesh.bin" ? first.Buffer : second.Buffer));
    }

    private static void AppendPrimitive(JsonObject target, JsonObject source, bool sharedBuffer, int bufferOffset)
    {
        int viewStart = target["bufferViews"]!.AsArray().Count, accessorStart = target["accessors"]!.AsArray().Count;
        if (sharedBuffer) target["buffers"]![0]!["byteLength"] = bufferOffset + source["buffers"]![0]!["byteLength"]!.GetValue<int>();
        else
        {
            JsonNode buffer = source["buffers"]![0]!.DeepClone(); buffer["uri"] = "second.bin";
            target["buffers"]!.AsArray().Add(buffer);
        }
        foreach (JsonNode? original in source["bufferViews"]!.AsArray())
        {
            JsonNode view = original!.DeepClone(); view["buffer"] = sharedBuffer ? 0 : 1;
            if (sharedBuffer) view["byteOffset"] = bufferOffset + (view["byteOffset"]?.GetValue<int>() ?? 0);
            target["bufferViews"]!.AsArray().Add(view);
        }
        foreach (JsonNode? original in source["accessors"]!.AsArray())
        {
            JsonNode accessor = original!.DeepClone(); accessor["bufferView"] = viewStart + accessor["bufferView"]!.GetValue<int>();
            target["accessors"]!.AsArray().Add(accessor);
        }
        JsonNode primitive = source["meshes"]![0]!["primitives"]![0]!.DeepClone();
        primitive["indices"] = accessorStart + primitive["indices"]!.GetValue<int>();
        var names = new System.Collections.Generic.List<string>();
        foreach (var attribute in primitive["attributes"]!.AsObject()) names.Add(attribute.Key);
        foreach (string name in names) primitive["attributes"]![name] = accessorStart + primitive["attributes"]![name]!.GetValue<int>();
        target["meshes"]![0]!["primitives"]!.AsArray().Add(primitive);
    }

    private static Fixture Make(bool wide = false, bool interleaved = false)
    {
        int indexSize = wide ? 4 : 2, viewOffset = interleaved ? 4 : 0, stride = interleaved ? 32 : 12;
        int positionLength = 4 * stride, indexOffset = viewOffset + positionLength;
        byte[] buffer = new byte[indexOffset + 6 * indexSize];
        float[][] vertices = { new[] { -1.25f, 2.5f, -2f }, new[] { -1.25f, 2.5f, 3f }, new[] { 2.75f, 2.5f, 3f }, new[] { 2.75f, 2.5f, -2f } };
        for (int i = 0; i < vertices.Length; i++)
        {
            for (int axis = 0; axis < 3; axis++) WriteFloat(buffer, viewOffset + i * stride + axis * 4, vertices[i][axis]);
            if (interleaved) WriteFloat(buffer, viewOffset + i * stride + 16, 1);
        }
        uint[] indices = { 2, 0, 1, 3, 2, 1 };
        for (int i = 0; i < indices.Length; i++)
            if (wide) BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(indexOffset + i * indexSize), indices[i]);
            else BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(indexOffset + i * indexSize), (ushort)indices[i]);
        JsonObject json = JsonNode.Parse("""
            {"asset":{"version":"2.0"},"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],
             "buffers":[{"uri":"mesh.bin","byteLength":60}],
             "bufferViews":[{"buffer":0,"byteLength":48,"target":34962},{"buffer":0,"byteOffset":48,"byteLength":12,"target":34963}],
             "accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3"},{"bufferView":1,"componentType":5123,"count":6,"type":"SCALAR"}]}
            """)!.AsObject();
        json["buffers"]![0]!["byteLength"] = buffer.Length;
        json["bufferViews"]![0]!["byteOffset"] = viewOffset;
        json["bufferViews"]![0]!["byteLength"] = positionLength;
        json["bufferViews"]![1]!["byteOffset"] = indexOffset;
        json["bufferViews"]![1]!["byteLength"] = 6 * indexSize;
        json["accessors"]![1]!["componentType"] = wide ? 5125 : 5123;
        if (interleaved)
        {
            json["bufferViews"]![0]!["byteStride"] = stride;
            JsonNode index = json["accessors"]![1]!.DeepClone();
            json["accessors"]!.AsArray().RemoveAt(1);
            json["accessors"]!.AsArray().Add(JsonNode.Parse("{\"bufferView\":0,\"byteOffset\":12,\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"}"));
            json["accessors"]!.AsArray().Add(JsonNode.Parse("{\"bufferView\":0,\"byteOffset\":24,\"componentType\":5126,\"count\":4,\"type\":\"VEC2\"}"));
            json["accessors"]!.AsArray().Add(index);
            json["meshes"]![0]!["primitives"]![0]!["attributes"]!["NORMAL"] = 1;
            json["meshes"]![0]!["primitives"]![0]!["attributes"]!["TEXCOORD_0"] = 2;
            json["meshes"]![0]!["primitives"]![0]!["indices"] = 3;
        }
        return new Fixture(json, buffer);
    }
    private static void WriteFloat(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), BitConverter.SingleToInt32Bits(value));
    private static byte[] Bytes(JsonObject value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    private static FormationGeometry Decode(Fixture source, FormationGeometryLimits? limits = null) =>
        FormationGltfDecoder.Decode("mesh", 1, "rock", Bytes(source.Json), _ => source.Buffer, limits);
    private static void RejectBeforeBuffer<T>(Action<JsonObject> mutation, FormationGeometryLimits? limits = null) where T : Exception
    {
        Fixture source = Make(); mutation(source.Json);
        int callbacks = 0;
        Reject<T>(() => FormationGltfDecoder.Decode("mesh", 1, "rock", Bytes(source.Json), _ => { callbacks++; return source.Buffer; }, limits));
        Assert.Equal(0, callbacks, "invalid metadata must fail before resolving dependency bytes");
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
