using System;
using System.Buffers.Binary;
using System.Text.Json;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Restricted, engine-free glTF 2 physical-coordinate reader. The package owner authenticates the JSON,
/// every buffer and every other dependency before calling; this reader performs no I/O or image loading.
/// Authenticated input memory must remain immutable throughout the call. No transform or topology repair
/// is applied, and the result grants no walkable, plantable or soil permissions.
/// </summary>
internal static class FormationGltfDecoder
{
    internal const int MaximumJsonBytes = 4 * 1024 * 1024;
    internal const int MaximumBufferBytes = 32 * 1024 * 1024;
    internal const int MaximumVertices = 60000;
    internal const int MaximumTriangles = 50000;
    internal const int MaximumPrimitives = 32;
    internal const int MaximumBuffers = 8;
    internal const int MaximumDescriptors = 128;
    private const int MaximumOverlapSteps = 4 * 1024 * 1024;

    internal static FormationGeometry Decode(string key, int revision, string family, ReadOnlyMemory<byte> gltf,
        Func<string, ReadOnlyMemory<byte>> resolveBuffer, FormationGeometryLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(resolveBuffer);
        limits ??= new FormationGeometryLimits();
        ValidateLimits(limits);
        FormationInput.Key(key, nameof(key)); FormationInput.Key(family, nameof(family));
        if (revision < 0) throw Invalid("Geometry revision must be nonnegative.");
        using JsonDocument document = FormationJson.Parse(gltf, MaximumJsonBytes);
        JsonElement root = document.RootElement;
        Fields(root, "asset", "scene", "scenes", "nodes", "meshes", "buffers", "bufferViews", "accessors",
            "materials", "textures", "images", "samplers", "animations", "skins", "extensionsUsed", "extensionsRequired", "extras");
        RejectExtensions(root);
        EmptyOptionalArray(root, "animations"); EmptyOptionalArray(root, "skins");
        EmptyOptionalArray(root, "extensionsUsed"); EmptyOptionalArray(root, "extensionsRequired");
        JsonElement asset = Required(root, "asset");
        Fields(asset, "version", "minVersion", "generator", "copyright", "extras");
        if (String(Required(asset, "version")) != "2.0" ||
            (asset.TryGetProperty("minVersion", out JsonElement minimum) && String(minimum) != "2.0"))
            throw Invalid("Only glTF 2.0 is supported.");
        if (OptionalInt(root, "scene", 0) != 0) throw Invalid("The sole scene must be scene zero.");
        JsonElement scene = One(Required(root, "scenes"));
        Fields(scene, "name", "nodes", "extras");
        if (Int(One(Required(scene, "nodes"))) != 0) throw Invalid("The scene must reference only node zero.");
        JsonElement node = One(Required(root, "nodes"));
        // Even explicit identity TRS/matrix fields are outside the admitted authoring contract.
        Fields(node, "name", "mesh", "extras");
        if (Int(Required(node, "mesh")) != 0) throw Invalid("The node must reference mesh zero.");
        JsonElement mesh = One(Required(root, "meshes"));
        Fields(mesh, "name", "primitives", "extras");
        JsonElement primitiveRows = Array(Required(mesh, "primitives"), MaximumPrimitives);
        JsonElement bufferRows = Array(Required(root, "buffers"), MaximumBuffers);
        var buffers = new Buffer[bufferRows.GetArrayLength()];
        int totalBufferBytes = 0;
        for (int i = 0; i < buffers.Length; i++)
        {
            JsonElement buffer = bufferRows[i];
            Fields(buffer, "uri", "byteLength", "name", "extras");
            string uri = String(Required(buffer, "uri"));
            ValidateUri(uri);
            int length = Int(Required(buffer, "byteLength"));
            if (length <= 0) throw Invalid("A nonempty external buffer is required.");
            if (length > MaximumBufferBytes - totalBufferBytes)
                throw new FormationCapacityException("Aggregate glTF buffers exceed 32 MiB.");
            for (int previous = 0; previous < i; previous++)
                if (StringComparer.OrdinalIgnoreCase.Equals(buffers[previous].Uri, uri))
                    throw Invalid("Buffer URIs must identify distinct dependencies.");
            buffers[i] = new Buffer(uri, length); totalBufferBytes += length;
        }
        JsonElement viewRows = Array(Required(root, "bufferViews"), MaximumDescriptors);
        var views = new BufferView[viewRows.GetArrayLength()];
        var usedBuffers = new bool[buffers.Length];
        for (int i = 0; i < views.Length; i++)
        {
            views[i] = ReadView(viewRows[i], buffers);
            usedBuffers[views[i].Buffer] = true;
        }
        foreach (bool used in usedBuffers) if (!used) throw Invalid("Unused buffers are outside the restricted mesh contract.");
        JsonElement accessorRows = Array(Required(root, "accessors"), MaximumDescriptors);
        var accessors = new Accessor[accessorRows.GetArrayLength()];
        var usedViews = new bool[views.Length];
        for (int i = 0; i < accessors.Length; i++)
        {
            accessors[i] = ReadAccessor(accessorRows[i], views);
            usedViews[accessors[i].View] = true;
        }
        foreach (bool used in usedViews) if (!used) throw Invalid("Unused buffer views are outside the restricted mesh contract.");
        var usedAccessors = new bool[accessors.Length];
        var primitives = new Primitive[primitiveRows.GetArrayLength()];
        int totalVertices = 0, totalTriangles = 0;
        for (int p = 0; p < primitives.Length; p++)
        {
            JsonElement primitive = primitiveRows[p];
            Fields(primitive, "attributes", "indices", "material", "mode", "extras");
            if (OptionalInt(primitive, "mode", 4) != 4) throw Invalid("Only indexed triangle primitives are supported.");
            JsonElement attributes = Required(primitive, "attributes");
            Fields(attributes, "POSITION", "NORMAL", "TANGENT", "TEXCOORD_0", "TEXCOORD_1", "COLOR_0");
            int positionIndex = Index(Required(attributes, "POSITION"), accessors.Length);
            Accessor position = accessors[positionIndex];
            if (position.Type != "VEC3" || position.Component != 5126 || position.Count < 3)
                throw Invalid("POSITION must contain at least three float32 VEC3 values.");
            if (position.Count > limits.MaxVertices - totalVertices)
                throw new FormationCapacityException("Aggregate glTF vertices exceed the geometry limit.");
            int indexIndex = Index(Required(primitive, "indices"), accessors.Length);
            Accessor indices = accessors[indexIndex];
            if (indices.Type != "SCALAR" || (indices.Component != 5123 && indices.Component != 5125) || indices.Count % 3 != 0)
                throw Invalid("Indices must be complete unsigned-short or unsigned-int triangles.");
            if (indices.Count / 3 > limits.MaxTriangles - totalTriangles)
                throw new FormationCapacityException("Aggregate glTF triangles exceed the geometry limit.");
            if (views[indices.View].Stride != 0 || views[indices.View].Target == 34962)
                throw Invalid("Index data cannot use vertex stride or a vertex buffer target.");
            if (usedAccessors[indexIndex]) throw Invalid("Primitives must own distinct accessors.");
            usedAccessors[indexIndex] = true;
            foreach (JsonProperty attribute in attributes.EnumerateObject())
            {
                int index = Index(attribute.Value, accessors.Length);
                Accessor value = accessors[index];
                string expected = attribute.Name switch
                {
                    "POSITION" or "NORMAL" => "VEC3", "TANGENT" => "VEC4",
                    "TEXCOORD_0" or "TEXCOORD_1" => "VEC2", _ => value.Type == "VEC3" ? "VEC3" : "VEC4"
                };
                if (usedAccessors[index] || value.Type != expected || value.Component != 5126 || value.Count != position.Count ||
                    views[value.View].Target == 34963)
                    throw Invalid("Vertex attributes must have distinct float accessors matching POSITION and their static layout.");
                usedAccessors[index] = true;
            }
            primitives[p] = new Primitive(positionIndex, indexIndex, totalVertices, totalTriangles);
            totalVertices += position.Count; totalTriangles += indices.Count / 3;
        }
        foreach (bool used in usedAccessors) if (!used) throw Invalid("Unused accessors are outside the restricted mesh contract.");
        int overlapSteps = 0;
        for (int a = 0; a < accessors.Length; a++)
            for (int b = a + 1; b < accessors.Length; b++)
                if (Overlaps(accessors[a], accessors[b], ref overlapSteps)) throw Invalid("Accessor element bytes overlap.");

        // Metadata and aggregate admission precede every dependency callback and geometry allocation.
        var bufferData = new ReadOnlyMemory<byte>[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            bufferData[i] = resolveBuffer(buffers[i].Uri);
            if (bufferData[i].Length != buffers[i].Length)
                throw Invalid("Resolved buffer length differs from its authenticated glTF declaration.");
        }
        var vertices = new Vector3D[totalVertices];
        var triangles = new GeometryTriangle[totalTriangles];
        // Canonical ordinal: primitive declaration order, then original vertex/triangle order.
        // Separate authored vertices stay separate, including equal positions at material seams.
        foreach (Primitive primitive in primitives)
        {
            Accessor position = accessors[primitive.Position], indices = accessors[primitive.Indices];
            ReadOnlySpan<byte> positions = bufferData[position.Buffer].Span, indexData = bufferData[indices.Buffer].Span;
            for (int i = 0; i < position.Count; i++)
            {
                ReadOnlySpan<byte> value = positions.Slice(position.Start + i * position.Stride, 12);
                float x = Float(value), y = Float(value[4..]), z = Float(value[8..]);
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw Invalid("POSITION contains a nonfinite value.");
                vertices[primitive.VertexStart + i] = new Vector3D(x, y, z);
            }
            for (int i = 0; i < indices.Count / 3; i++)
                triangles[primitive.TriangleStart + i] = new GeometryTriangle(
                    primitive.VertexStart + ReadIndex(indexData, indices, i * 3, position.Count),
                    primitive.VertexStart + ReadIndex(indexData, indices, i * 3 + 1, position.Count),
                    primitive.VertexStart + ReadIndex(indexData, indices, i * 3 + 2, position.Count), 0);
        }
        return new FormationGeometry(key, revision, family, vertices, triangles,
            new[] { new SupportPatch("visual", "unqualified", SupportCapabilities.None) }, limits);
    }

    private static void ValidateLimits(FormationGeometryLimits limits)
    {
        if (limits.MaxVertices < 3 || limits.MaxTriangles < 1 || limits.MaxPatches < 1 || limits.MaxEstimatedBytes < 0)
            throw Invalid("Invalid glTF geometry limits.");
        if (limits.MaxVertices > MaximumVertices || limits.MaxTriangles > MaximumTriangles)
            throw new FormationCapacityException("glTF geometry limits exceed the fixed decoder ceilings.");
        // Include the decoder's arrays (24/16 bytes per element) while FormationGeometry copies them.
        // Input buffer/JSON caps are separate; this estimate is not a managed-heap measurement.
        long reserved = 512L + 96L * limits.MaxVertices + 64L * limits.MaxTriangles + 2048L * limits.MaxPatches;
        if (reserved > limits.MaxEstimatedBytes)
            throw new FormationCapacityException("glTF geometry reservation exceeds its byte budget.");
    }

    private readonly record struct Buffer(string Uri, int Length);
    private readonly record struct Primitive(int Position, int Indices, int VertexStart, int TriangleStart);
    private readonly record struct BufferView(int Buffer, int Offset, int Length, int Stride, int Target);
    private readonly record struct Accessor(int View, int Buffer, int Start, int Count, int Stride, int Width, int Component, string Type)
    {
        internal long End => (long)Start + (long)(Count - 1) * Stride + Width;
    }

    private static BufferView ReadView(JsonElement row, Buffer[] buffers)
    {
        Fields(row, "buffer", "byteOffset", "byteLength", "byteStride", "target", "name", "extras");
        int buffer = Index(Required(row, "buffer"), buffers.Length);
        int offset = OptionalInt(row, "byteOffset", 0), length = Int(Required(row, "byteLength"));
        int stride = OptionalInt(row, "byteStride", 0), target = OptionalInt(row, "target", 0);
        if (offset < 0 || length <= 0 || (long)offset + length > buffers[buffer].Length ||
            (row.TryGetProperty("byteStride", out _) && (stride < 4 || stride > 252 || stride % 4 != 0)) ||
            (row.TryGetProperty("target", out _) && target != 34962 && target != 34963))
            throw Invalid("Invalid buffer view range, stride or target.");
        return new BufferView(buffer, offset, length, stride, target);
    }

    private static Accessor ReadAccessor(JsonElement row, BufferView[] views)
    {
        Fields(row, "bufferView", "byteOffset", "componentType", "normalized", "count", "type", "min", "max", "name", "extras");
        if (row.TryGetProperty("normalized", out JsonElement normalized) && normalized.ValueKind != JsonValueKind.False)
            throw Invalid("Normalized accessors are not supported.");
        int viewIndex = Index(Required(row, "bufferView"), views.Length);
        BufferView view = views[viewIndex];
        int component = Int(Required(row, "componentType")), count = Int(Required(row, "count"));
        int componentSize = component switch { 5123 => 2, 5125 or 5126 => 4, _ => throw Invalid("Unsupported accessor component type.") };
        string type = String(Required(row, "type"));
        int components = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => throw Invalid("Unsupported accessor shape.") };
        if (count < 1) throw Invalid("Accessor count must be positive.");
        if (count > (type == "SCALAR" ? MaximumTriangles * 3 : MaximumVertices))
            throw new FormationCapacityException("Accessor count exceeds the fixed decoder ceiling.");
        int offset = OptionalInt(row, "byteOffset", 0), width = componentSize * components;
        int stride = view.Stride == 0 ? width : view.Stride;
        if (offset < 0 || offset % componentSize != 0 || view.Offset % componentSize != 0 || stride < width || stride % componentSize != 0 ||
            (long)offset + (long)(count - 1) * stride + width > view.Length)
            throw Invalid("Invalid accessor alignment or byte range.");
        return new Accessor(viewIndex, view.Buffer, view.Offset + offset, count, stride, width, component, type);
    }

    // Interleaved POSITION/NORMAL/UV are valid; two accessors may not alias even one element byte.
    private static bool Overlaps(Accessor a, Accessor b, ref int steps)
    {
        if (a.Buffer != b.Buffer || a.Start >= b.End || b.Start >= a.End) return false;
        int ai = 0, bi = 0;
        while (ai < a.Count && bi < b.Count)
        {
            if (++steps > MaximumOverlapSteps) throw new FormationCapacityException("glTF accessor-overlap work exceeds its limit.");
            long first = (long)a.Start + (long)ai * a.Stride, second = (long)b.Start + (long)bi * b.Stride;
            if (first + a.Width <= second) ai++;
            else if (second + b.Width <= first) bi++;
            else return true;
        }
        return false;
    }

    private static int ReadIndex(ReadOnlySpan<byte> bytes, Accessor accessor, int ordinal, int vertexCount)
    {
        ReadOnlySpan<byte> value = bytes[(accessor.Start + ordinal * accessor.Stride)..];
        uint index = accessor.Component == 5123 ? BinaryPrimitives.ReadUInt16LittleEndian(value) : BinaryPrimitives.ReadUInt32LittleEndian(value);
        if (index >= (uint)vertexCount) throw Invalid("Triangle index is outside POSITION.");
        return (int)index;
    }

    private static float Float(ReadOnlySpan<byte> value) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value));
    private static void ValidateUri(string uri)
    {
        if (uri.Length == 0 || uri.Length > 1024 || uri != uri.Trim() || uri[0] == '/' || uri.IndexOfAny(new[] { ':', '\\', '?', '#', '%', '\0' }) >= 0)
            throw Invalid("An unescaped relative external buffer URI is required.");
        foreach (char value in uri) if (char.IsControl(value)) throw Invalid("Buffer URI contains a control character.");
        foreach (string segment in uri.Split('/'))
            if (segment.Length == 0 || segment == "." || segment == "..") throw Invalid("Buffer URI contains an ambiguous path segment.");
    }
    private static JsonElement Required(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out JsonElement value))
            throw Invalid("Missing glTF field: " + name);
        return value;
    }
    private static int Int(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
        ? result : throw Invalid("Expected an exact 32-bit integer.");
    private static string String(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw Invalid("Expected a string.");
    private static int OptionalInt(JsonElement owner, string name, int fallback) => owner.TryGetProperty(name, out JsonElement value) ? Int(value) : fallback;
    private static int Index(JsonElement value, int length)
    {
        int index = Int(value);
        return (uint)index < (uint)length ? index : throw Invalid("glTF table index is out of range.");
    }
    private static JsonElement Array(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) throw Invalid("A nonempty glTF array is required.");
        if (value.GetArrayLength() > maximum) throw new FormationCapacityException("glTF descriptor count exceeds its limit.");
        return value;
    }
    private static JsonElement One(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 1) throw Invalid("Exactly one glTF entry is required.");
        return value[0];
    }
    private static void Fields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("A glTF object is required.");
        foreach (JsonProperty property in value.EnumerateObject())
        {
            bool found = false;
            foreach (string name in allowed) if (property.NameEquals(name)) { found = true; break; }
            if (!found) throw Invalid("Unsupported glTF field: " + property.Name);
        }
    }
    private static void EmptyOptionalArray(JsonElement owner, string name)
    {
        if (owner.TryGetProperty(name, out JsonElement value) && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 0))
            throw Invalid("Unsupported glTF feature: " + name);
    }
    private static void RejectExtensions(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (property.NameEquals("extensions")) throw Invalid("glTF extensions are outside the restricted mesh contract.");
                RejectExtensions(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in value.EnumerateArray()) RejectExtensions(child);
    }
    private static FormationValidationException Invalid(string message) => new(message);
}
