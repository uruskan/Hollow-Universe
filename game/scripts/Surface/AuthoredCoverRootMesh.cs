using System;
using System.Text;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Adds one reviewed whole-plant root, without mapping original glTF corners to imported vertices.
/// Only detached, identity-transformed, single-surface static meshes are supported. The returned mesh
/// is caller-owned; the imported mesh/material remain shared and untouched. Current cover casts no
/// shadows: an imported shadow mesh is not reused because it lacks the new deformation metadata.
/// </summary>
internal static class AuthoredCoverRootMesh
{
    internal readonly record struct Description(string Node, int VertexCount, int TriangleCount, string MeshSha256);

    internal static Description Describe(MeshInstance3D source)
    {
        ArrayMesh mesh = ValidateSource(source);
        using var arrays = mesh.SurfaceGetArrays(0);
        return DescribeArrays(source.Name.ToString(), arrays);
    }

    internal static ArrayMesh Create(MeshInstance3D source, GroundCoverRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArrayMesh original = ValidateSource(source);
        using var arrays = original.SurfaceGetArrays(0);
        Description description = DescribeArrays(source.Name.ToString(), arrays);
        if (description.Node != root.Node || description.VertexCount != root.VertexCount
            || description.TriangleCount != root.TriangleCount || description.MeshSha256 != root.MeshSha256)
            throw new ArgumentException("Reviewed root does not authenticate this exact imported mesh.");
        Vector3 at = new((float)root.Root.X, (float)root.Root.Y, (float)root.Root.Z);
        if (!original.GetAabb().Grow(0.0001f).HasPoint(at))
            throw new ArgumentException("A near/middle fern root must lie within its authenticated geometry bounds.");
        Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var custom = new float[positions.Length * 4];
        for (int i = 0; i < positions.Length; i++)
        {
            custom[i * 4] = at.X; custom[i * 4 + 1] = at.Y; custom[i * 4 + 2] = at.Z;
            Vector3 p = positions[i];
            custom[i * 4 + 3] = root.GrowthWeight(new Vector3D(p.X, p.Y, p.Z));
        }
        arrays[(int)Mesh.ArrayType.Custom0] = custom;
        ArrayMesh? result = null;
        try
        {
            result = new ArrayMesh { ResourceName = original.ResourceName + "_rooted", CustomAabb = original.CustomAabb };
            Mesh.ArrayFormat flags = original.SurfaceGetFormat(0)
                & (Mesh.ArrayFormat.FlagUseDynamicUpdate | Mesh.ArrayFormat.FlagCompressAttributes);
            flags |= (Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
            if (result.GetSurfaceCount() != 1) throw new InvalidOperationException("Root mesh native upload failed.");
            result.SurfaceSetName(0, original.SurfaceGetName(0));
            result.SurfaceSetMaterial(0, original.SurfaceGetMaterial(0));
            PreservePackedVertices(original, result);
            // A changed import/upload representation must be qualified explicitly, never admitted by a loose tolerance.
            using var uploaded = result.SurfaceGetArrays(0);
            if (DescribeArrays(source.Name.ToString(), uploaded).MeshSha256 != description.MeshSha256)
                throw new InvalidOperationException("Root augmentation changed an original imported mesh stream during native upload. "
                    + DescribeUploadDifference(original, result, arrays, uploaded));
            return result;
        }
        catch { result?.Dispose(); throw; }
    }

    private static void PreservePackedVertices(ArrayMesh source, ArrayMesh target)
    {
        // AddSurfaceFromArrays must allocate the extra attribute stream, but its normal/tangent packing
        // is not idempotent. Compressed positions can also shift by one quantization step. CUSTOM0 lives
        // in attribute_data, so retain the original vertex_data bytes using the public update API.
        // Copying packed bytes is only valid with identical decoding metadata and vertex-buffer layout.
        using var original = RenderingServer.MeshGetSurface(source.GetRid(), 0);
        using var augmented = RenderingServer.MeshGetSurface(target.GetRid(), 0);
        Mesh.ArrayFormat addition = Mesh.ArrayFormat.FormatCustom0
            | (Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        bool compatible = target.SurfaceGetFormat(0) == (source.SurfaceGetFormat(0) | addition)
            && original["vertex_count"].AsInt32() == augmented["vertex_count"].AsInt32()
            && original["index_count"].AsInt32() == augmented["index_count"].AsInt32()
            && original["aabb"].AsAabb() == augmented["aabb"].AsAabb();
        bool sourceUv = original.TryGetValue("uv_scale", out Variant oldUv), targetUv = augmented.TryGetValue("uv_scale", out Variant newUv);
        compatible &= sourceUv == targetUv && (!sourceUv || oldUv.AsVector4() == newUv.AsVector4());
        byte[] originalVertices = original["vertex_data"].AsByteArray();
        byte[] targetVertices = augmented["vertex_data"].AsByteArray();
        compatible &= originalVertices.Length == targetVertices.Length && originalVertices.Length > 0;
        if (!compatible)
            throw new InvalidOperationException("Root augmentation cannot retain packed vertices: format, count, AABB or UV decoding metadata changed.");
        if (!originalVertices.AsSpan().SequenceEqual(targetVertices))
            target.SurfaceUpdateVertexRegion(0, 0, originalVertices);
    }

    private static ArrayMesh ValidateSource(MeshInstance3D source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.IsInsideTree()) throw new ArgumentException("Root augmentation requires a detached imported source scene.");
        int depth = 0;
        for (Node? node = source; node != null; node = node.GetParent())
        {
            if (++depth > 32 || node is Node3D spatial && spatial.Transform != Transform3D.Identity)
                throw new ArgumentException("Root source must have bounded identity node/ancestor transforms.");
        }
        if (source.Skin != null || source.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() != 1
            || mesh.GetBlendShapeCount() != 0 || mesh.SurfaceGetPrimitiveType(0) != Mesh.PrimitiveType.Triangles)
            throw new ArgumentException("Root source must be one static triangle ArrayMesh surface.");
        Mesh.ArrayFormat forbidden = Mesh.ArrayFormat.FormatCustom0 | Mesh.ArrayFormat.FormatCustom1
            | Mesh.ArrayFormat.FormatCustom2 | Mesh.ArrayFormat.FormatCustom3 | Mesh.ArrayFormat.FormatBones
            | Mesh.ArrayFormat.FormatWeights | Mesh.ArrayFormat.FlagUse2DVertices | Mesh.ArrayFormat.FlagUse8BoneWeights;
        if ((mesh.SurfaceGetFormat(0) & forbidden) != 0)
            throw new ArgumentException("Root source has unsupported custom, skin or non-3D streams.");
        if (mesh.SurfaceGetArrayLen(0) is < 3 or > GroundCoverMeshFingerprint.MaximumVertices
            || mesh.SurfaceGetArrayIndexLen(0) is < 3 or > GroundCoverMeshFingerprint.MaximumTriangles * 3)
            throw new ArgumentException("Root source exceeds bounded imported geometry limits.");
        // Inspect the actual renderer surface, not import settings which may have changed since import.
        using var raw = RenderingServer.MeshGetSurface(mesh.GetRid(), 0);
        if (raw.TryGetValue("lods", out Variant lodValue))
        {
            using var lods = lodValue.AsGodotArray();
            if (lods.Count != 0) throw new ArgumentException("Embedded mesh LODs need their own verified root-preserving path.");
        }
        return mesh;
    }

    private static Description DescribeArrays(string node, Godot.Collections.Array arrays)
    {
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        float[] tangents = Floats(arrays[(int)Mesh.ArrayType.Tangent]);
        Color[] colors = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil
            ? Array.Empty<Color>() : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Vector2[] uv2 = arrays[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil
            ? Array.Empty<Vector2>() : arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        string digest = GroundCoverMeshFingerprint.Compute(Flatten(vertices), Flatten(normals), tangents, Flatten(colors),
            Flatten(uv), Flatten(uv2), indices);
        return new Description(node, vertices.Length, indices.Length / 3, digest);
    }
    private static float[] Floats(Variant value) => value.VariantType == Variant.Type.Nil ? Array.Empty<float>() : value.AsFloat32Array();

    // Failure-only instrumentation: reports actual native readback, without tolerating or approving differences.
    private static string DescribeUploadDifference(ArrayMesh source, ArrayMesh output,
        Godot.Collections.Array before, Godot.Collections.Array after)
    {
        var report = new StringBuilder();
        report.Append($"sourceFormat=0x{(ulong)source.SurfaceGetFormat(0):X}, outputFormat=0x{(ulong)output.SurfaceGetFormat(0):X}; ");
        Compare("POSITION", Flatten(before[(int)Mesh.ArrayType.Vertex].AsVector3Array()), Flatten(after[(int)Mesh.ArrayType.Vertex].AsVector3Array()));
        Compare("NORMAL", Flatten(before[(int)Mesh.ArrayType.Normal].AsVector3Array()), Flatten(after[(int)Mesh.ArrayType.Normal].AsVector3Array()));
        Compare("TANGENT", Floats(before[(int)Mesh.ArrayType.Tangent]), Floats(after[(int)Mesh.ArrayType.Tangent]));
        Compare("COLOR", ColorValues(before[(int)Mesh.ArrayType.Color]), ColorValues(after[(int)Mesh.ArrayType.Color]));
        Compare("UV", UvValues(before[(int)Mesh.ArrayType.TexUV]), UvValues(after[(int)Mesh.ArrayType.TexUV]));
        Compare("UV2", UvValues(before[(int)Mesh.ArrayType.TexUV2]), UvValues(after[(int)Mesh.ArrayType.TexUV2]));
        int[] oldIndices = before[(int)Mesh.ArrayType.Index].AsInt32Array(), newIndices = after[(int)Mesh.ArrayType.Index].AsInt32Array();
        int indexChanges = 0;
        for (int i = 0; i < Math.Min(oldIndices.Length, newIndices.Length); i++) if (oldIndices[i] != newIndices[i]) indexChanges++;
        report.Append($"INDEX lengths={oldIndices.Length}/{newIndices.Length}, changed={indexChanges}; ");
        using var oldRaw = RenderingServer.MeshGetSurface(source.GetRid(), 0);
        using var newRaw = RenderingServer.MeshGetSurface(output.GetRid(), 0);
        foreach (string key in new[] { "aabb", "uv_scale", "vertex_count", "index_count" })
        {
            bool oldPresent = oldRaw.TryGetValue(key, out Variant oldValue), newPresent = newRaw.TryGetValue(key, out Variant newValue);
            report.Append($"{key}={((oldPresent) ? oldValue.ToString() : "absent")}/{((newPresent) ? newValue.ToString() : "absent")}; ");
        }
        foreach (string key in new[] { "vertex_data", "attribute_data", "index_data" })
        {
            byte[] oldBytes = oldRaw.TryGetValue(key, out Variant oldValue) ? oldValue.AsByteArray() : Array.Empty<byte>();
            byte[] newBytes = newRaw.TryGetValue(key, out Variant newValue) ? newValue.AsByteArray() : Array.Empty<byte>();
            report.Append($"{key} bytes={oldBytes.Length}/{newBytes.Length}, equal={oldBytes.AsSpan().SequenceEqual(newBytes)}; ");
        }
        return report.ToString();

        void Compare(string name, float[] left, float[] right)
        {
            int changed = 0, first = -1; double maximum = 0;
            for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                if (BitConverter.SingleToInt32Bits(left[i]) == BitConverter.SingleToInt32Bits(right[i])) continue;
                changed++; if (first < 0) first = i;
                maximum = Math.Max(maximum, Math.Abs((double)left[i] - right[i]));
            }
            report.Append($"{name} lengths={left.Length}/{right.Length}, changed={changed}, maxAbs={maximum:R}");
            if (first >= 0) report.Append($", first[{first}]={left[first]:R}/0x{BitConverter.SingleToInt32Bits(left[first]):X8}->{right[first]:R}/0x{BitConverter.SingleToInt32Bits(right[first]):X8}");
            report.Append("; ");
        }
        static float[] ColorValues(Variant value) => value.VariantType == Variant.Type.Nil ? Array.Empty<float>() : Flatten(value.AsColorArray());
        static float[] UvValues(Variant value) => value.VariantType == Variant.Type.Nil ? Array.Empty<float>() : Flatten(value.AsVector2Array());
    }
    private static float[] Flatten(Vector3[] values)
    {
        var result = new float[values.Length * 3];
        for (int i = 0; i < values.Length; i++) { result[i * 3] = values[i].X; result[i * 3 + 1] = values[i].Y; result[i * 3 + 2] = values[i].Z; }
        return result;
    }
    private static float[] Flatten(Vector2[] values)
    {
        var result = new float[values.Length * 2];
        for (int i = 0; i < values.Length; i++) { result[i * 2] = values[i].X; result[i * 2 + 1] = values[i].Y; }
        return result;
    }
    private static float[] Flatten(Color[] values)
    {
        var result = new float[values.Length * 4];
        for (int i = 0; i < values.Length; i++) { result[i * 4] = values[i].R; result[i * 4 + 1] = values[i].G; result[i * 4 + 2] = values[i].B; result[i * 4 + 3] = values[i].A; }
        return result;
    }
}
