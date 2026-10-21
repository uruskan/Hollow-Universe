using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Hollow.Surface;

/// <summary>
/// Inactive, engine-free visual admission. Preflight authenticates bounded metadata and file/header reads without
/// retaining mesh streams or encoded image payloads. Load reauthenticates the actual bytes it decodes, preserving source float bits,
/// primitive/corner order and material roles. It does not repair tangents, decode pixels, upload, cache or activate art.
/// Reservations are declared CPU/upload allowances, not measurements of a managed heap or driver. The qualified
/// catalog is borrowed immutable data; callers separately bound concurrent plans/results and its residency.
/// </summary>
public static class FormationVisualDataLoader
{
    private sealed record View(int Buffer, int Offset, int Length, int Stride, int Target);
    private sealed record Accessor(int View, int Buffer, int Start, int Count, int Stride, int Components, int Component)
    {
        internal int Width => Components * (Component == 5123 ? 2 : 4);
        internal long End => (long)Start + (long)(Count - 1) * Stride + Width;
    }
    private sealed record Primitive(int Material, int Position, int Normal, int Tangent, int Uv, int Indices,
        int VertexOffset, int TriangleOffset);
    private sealed record ImagePlan(FormationPackageFile File, string Mime, int Width, int Height, long MipBytes);
    private sealed record LodPlan(FormationVisualAsset Binding, FormationGeometry Qualified,
        FormationPackageFile[] Buffers, Accessor[] Accessors, Primitive[] Primitives, FormationVisualMaterial[] Materials);
    private sealed record Plan(string Root, string Receipt, FormationVisualLimits Limits, LodPlan[] Lods,
        ImagePlan[] Images, FormationPackageFile[] Files);
    private sealed class Work(FormationVisualLimits limits, CancellationToken cancellation)
    {
        private int _steps;
        internal void Step()
        {
            cancellation.ThrowIfCancellationRequested();
            if (++_steps > limits.MaxValidationSteps) throw Capacity("Visual validation step budget exceeded.");
        }
    }
    private sealed class Admission(FormationPackage package, string root, FormationVisualLimits limits, CancellationToken cancellation)
    {
        internal readonly Dictionary<string, FormationPackageFile> Pins = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, FormationPackageFile> Required = new(StringComparer.Ordinal);
        internal readonly List<ImagePlan> Images = new();
        private readonly Dictionary<string, int> _imagePaths = new(StringComparer.Ordinal);
        internal long Encoded, Streams, Vertices, Indices, Decoded, LargestImage, Metadata;
        internal int Primitives, Materials;
        internal readonly Work Work = new(limits, cancellation);
        internal FormationVisualLimits Limits => limits;
        internal string Root => root;
        internal CancellationToken Cancellation => cancellation;
        internal FormationPackage Package => package;

        internal FormationPackageFile Require(string path)
        {
            ValidatePath(path);
            if (!Pins.TryGetValue(path, out FormationPackageFile? file)) throw Invalid("Unlisted visual dependency: " + path);
            if (Required.TryAdd(path, file))
            {
                Encoded = checked(Encoded + file.Bytes);
                if (Required.Count > limits.MaxFiles || Encoded > limits.MaxEncodedBytes)
                    throw Capacity("Aggregate encoded dependency reservation exceeded.");
            }
            return file;
        }
        internal int Image(string path, string? declaredMime)
        {
            FormationPackageFile file = Require(path);
            if (_imagePaths.TryGetValue(path, out int known))
            {
                if (declaredMime != null && declaredMime != Images[known].Mime) throw Invalid("Inconsistent image MIME declarations.");
                return known;
            }
            byte[] header = Verify(root, file, Math.Min(limits.MaxImageHeaderBytes, checked((int)file.Bytes)), cancellation);
            (string mime, int width, int height) = ImageHeader(header);
            if (declaredMime != null && declaredMime != mime) throw Invalid("Image MIME type differs from its authenticated header.");
            if (width < 1 || height < 1 || width > limits.MaxImageDimension || height > limits.MaxImageDimension)
                throw Capacity("Decoded image dimensions exceed admission.");
            long mip = 0;
            for (int w = width, h = height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
            { mip = checked(mip + 4L * w * h); if (w == 1 && h == 1) break; }
            // Encoded snapshots are shared by content identity. Decode colour-space roles stay explicit on bindings.
            for (int i = 0; i < Images.Count; i++)
                if (Images[i].File.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) && Images[i].Mime == mime &&
                    Images[i].Width == width && Images[i].Height == height) { _imagePaths.Add(path, i); return i; }
            Decoded = checked(Decoded + mip); LargestImage = Math.Max(LargestImage, mip);
            if (Images.Count >= limits.MaxImages || Decoded > limits.MaxDecodedImageBytes)
                throw Capacity("Aggregate decoded image reservation exceeded.");
            Images.Add(new(file, mime, width, height, mip));
            _imagePaths.Add(path, Images.Count - 1);
            return Images.Count - 1;
        }
    }

    public static FormationVisualPreflight Preflight(FormationPackage package, string packageDirectory,
        string expectedReceiptSha256, FormationVisualLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(packageDirectory);
        cancellation.ThrowIfCancellationRequested(); limits ??= new(); ValidateLimits(limits);
        string receipt = Digest(expectedReceiptSha256);
        if (!receipt.Equals(Digest(package.ReceiptSha256), StringComparison.Ordinal)) throw Invalid("Visual receipt pin mismatch.");
        if (package.Visuals.Count < 1 || package.Visuals.Count > limits.MaxLods || package.Files.Count > limits.MaxFiles)
            throw Capacity("Visual binding/file count exceeds admission.");
        string root = Path.GetFullPath(packageDirectory);
        var admission = new Admission(package, root, limits, cancellation);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long largestJson = 0;
        foreach (FormationPackageFile file in package.Files)
        {
            ValidatePath(file.Path); _ = Digest(file.Sha256);
            if (file.Bytes > limits.MaxFileBytes) throw Capacity("Visual file pin exceeds its byte allowance.");
            if (file.Bytes < 1 || !aliases.Add(file.Path) || !admission.Pins.TryAdd(file.Path, file))
                throw Invalid("Invalid, duplicate or aliased visual file pin.");
        }
        // Reserve parser/plan metadata before reading JSON. Counts/serialized bytes are hard admission bounds;
        // this conservative heap allowance is not a promise about runtime object overhead.
        foreach (FormationVisualAsset visual in package.Visuals)
        {
            FormationPackageFile file = admission.Require(visual.AssetPath);
            if (file.Bytes > limits.MaxJsonBytes) throw Capacity("Visual JSON exceeds its metadata limit.");
            if (Digest(visual.ContentSha256) != Digest(file.Sha256)) throw Invalid("Visual binding differs from file pin.");
            largestJson = Math.Max(largestJson, file.Bytes);
        }
        admission.Metadata = checked(4096L + package.Files.Count * 2048L + package.Visuals.Count *
            (4096L + limits.MaxDescriptorsPerLod * 256L + 32 * 512L) + limits.MaxMaterials * 2048L +
            limits.MaxImages * 2048L + 4 * largestJson + limits.MaxImageHeaderBytes + 65536);
        if (admission.Metadata > limits.MaxMetadataBytes || admission.Metadata > limits.MaxReservationBytes)
            throw Capacity("Visual metadata reservation exceeded before parsing.");
        var lods = new List<LodPlan>();
        var identities = new HashSet<(string, int, string)>();
        foreach (FormationVisualAsset visual in package.Visuals.OrderBy(v => v.GeometryKey, StringComparer.Ordinal)
            .ThenBy(v => v.GeometryRevision).ThenBy(v => v.LodKey, StringComparer.Ordinal))
        {
            admission.Work.Step();
            if (!identities.Add((visual.GeometryKey, visual.GeometryRevision, visual.LodKey))) throw Invalid("Duplicate visual LOD binding.");
            try { lods.Add(ParseLod(visual, admission)); }
            catch (Exception error) when ((error is JsonException or InvalidOperationException or FormatException or OverflowException)
                && error is not FormationCapacityException)
            { throw Invalid("Malformed visual metadata: " + error.Message); }
        }
        long reservation = checked(admission.Metadata + admission.Encoded + admission.Streams + admission.Decoded + 2 * admission.LargestImage);
        if (reservation > limits.MaxReservationBytes) throw Capacity("Visual peak reservation exceeds admission.");
        FormationPackageFile[] files = admission.Required.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        // Hash all required bytes before issuing the preflight. No buffer/image payload arrays are retained.
        foreach (FormationPackageFile file in files) _ = Verify(root, file, 0, cancellation);
        var sizing = new FormationVisualSizing(lods.Count, admission.Primitives, admission.Vertices, admission.Indices,
            admission.Images.Count, admission.Metadata, admission.Encoded, admission.Streams, admission.Decoded, reservation);
        return new(new Plan(root, receipt, limits, lods.ToArray(), admission.Images.ToArray(), files), sizing, files);
    }

    public static FormationVisualData Load(FormationVisualPreflight preflight, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(preflight); cancellation.ThrowIfCancellationRequested();
        if (preflight.Plan is not Plan plan) throw Invalid("Unknown visual preflight producer.");
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var bufferPaths = new HashSet<string>(plan.Lods.SelectMany(l => l.Buffers).Select(f => f.Path), StringComparer.Ordinal);
        var imagePaths = new HashSet<string>(plan.Images.Select(i => i.File.Path), StringComparer.Ordinal);
        foreach (FormationPackageFile file in plan.Files)
        {
            cancellation.ThrowIfCancellationRequested();
            if (bufferPaths.Contains(file.Path) || imagePaths.Contains(file.Path)) payloads.Add(file.Path, Read(plan.Root, file, cancellation));
            else _ = Verify(plan.Root, file, 0, cancellation);
        }
        var work = new Work(plan.Limits, cancellation);
        var lods = new FormationVisualLod[plan.Lods.Length];
        for (int l = 0; l < lods.Length; l++)
        {
            LodPlan lod = plan.Lods[l];
            var primitives = new FormationVisualPrimitive[lod.Primitives.Length];
            for (int p = 0; p < primitives.Length; p++)
            {
                Primitive primitive = lod.Primitives[p];
                float[] positions = Floats(primitive.Position), normals = Floats(primitive.Normal);
                float[] tangents = Floats(primitive.Tangent), uv = Floats(primitive.Uv);
                Accessor index = lod.Accessors[primitive.Indices];
                byte[] bytes = payloads[lod.Buffers[index.Buffer].Path];
                int[] indices = new int[index.Count];
                for (int i = 0; i < indices.Length; i++)
                {
                    work.Step(); ReadOnlySpan<byte> value = bytes.AsSpan(index.Start + i * index.Stride);
                    uint ordinal = index.Component == 5123 ? BinaryPrimitives.ReadUInt16LittleEndian(value) : BinaryPrimitives.ReadUInt32LittleEndian(value);
                    if (ordinal >= (uint)(positions.Length / 3)) throw Invalid("Visual index lies outside its primitive.");
                    indices[i] = (int)ordinal;
                }
                for (int i = 0; i < positions.Length / 3; i++)
                {
                    work.Step(); int n = i * 3;
                    var expected = lod.Qualified.Vertices[primitive.VertexOffset + i];
                    if (expected.X != positions[n] || expected.Y != positions[n + 1] || expected.Z != positions[n + 2])
                        throw Invalid("Visual POSITION differs from qualified source vertex order.");
                    Unit(normals[n], normals[n + 1], normals[n + 2], "NORMAL");
                    if (tangents.Length != 0)
                    {
                        int t = i * 4; Unit(tangents[t], tangents[t + 1], tangents[t + 2], "TANGENT");
                        if (tangents[t + 3] != 1 && tangents[t + 3] != -1) throw Invalid("TANGENT handedness must be +1 or -1.");
                    }
                }
                for (int i = 0; i < indices.Length; i += 3)
                {
                    work.Step(); GeometryTriangle expected = lod.Qualified.Triangles[primitive.TriangleOffset + i / 3];
                    if (expected.A != primitive.VertexOffset + indices[i] || expected.B != primitive.VertexOffset + indices[i + 1] ||
                        expected.C != primitive.VertexOffset + indices[i + 2]) throw Invalid("Visual triangle order/winding differs from qualified LOD.");
                }
                primitives[p] = new(p, primitive.Material, primitive.VertexOffset, primitive.TriangleOffset,
                    positions, normals, tangents, uv, indices);
            }
            lods[l] = new(lod.Binding.GeometryKey, lod.Binding.GeometryRevision, lod.Binding.LodKey,
                lod.Binding.AssetPath, primitives, lod.Materials);

            float[] Floats(int accessorIndex)
            {
                if (accessorIndex < 0) return Array.Empty<float>();
                Accessor a = lod.Accessors[accessorIndex];
                byte[] data = payloads[lod.Buffers[a.Buffer].Path];
                var values = new float[checked(a.Count * a.Components)];
                for (int i = 0; i < a.Count; i++)
                    for (int c = 0; c < a.Components; c++)
                    {
                        work.Step(); float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(a.Start + i * a.Stride + c * 4)));
                        if (!float.IsFinite(value)) throw Invalid("Nonfinite visual attribute.");
                        values[i * a.Components + c] = value;
                    }
                return values;
            }
        }
        var images = new FormationVisualImage[plan.Images.Length];
        for (int i = 0; i < images.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); ImagePlan image = plan.Images[i];
            images[i] = new(image.File.Path, Digest(image.File.Sha256), image.Mime, image.Width, image.Height,
                image.MipBytes, payloads[image.File.Path]);
        }
        return new(plan.Receipt, preflight.Sizing, lods, images);
    }

    private static LodPlan ParseLod(FormationVisualAsset binding, Admission admission)
    {
        admission.Work.Step();
        FormationVisualLimits limits = admission.Limits;
        FormationPackageFile json = admission.Require(binding.AssetPath);
        using JsonDocument document = FormationJson.Parse(Read(admission.Root, json, admission.Cancellation), limits.MaxJsonBytes);
        JsonElement root = document.RootElement;
        Fields(root, "asset", "scene", "scenes", "nodes", "meshes", "buffers", "bufferViews", "accessors", "materials", "textures", "images", "samplers", "animations", "skins", "extensionsUsed", "extensionsRequired", "extras");
        foreach (string key in new[] { "animations", "skins", "extensionsUsed", "extensionsRequired" })
            if (root.TryGetProperty(key, out JsonElement empty) && ArrayRows(empty, 0).GetArrayLength() != 0) throw Invalid("Unsupported visual feature: " + key);
        JsonElement asset = Required(root, "asset"); Fields(asset, "version", "minVersion", "generator", "copyright", "extras");
        if (Text(Required(asset, "version")) != "2.0" || asset.TryGetProperty("minVersion", out JsonElement min) && Text(min) != "2.0") throw Invalid("Only glTF 2.0 is supported.");
        JsonElement scene = One(Required(root, "scenes")); Fields(scene, "name", "nodes", "extras");
        if (OptionalInt(root, "scene", 0) != 0 || Integer(One(Required(scene, "nodes"))) != 0) throw Invalid("Expected only scene/node zero.");
        JsonElement node = One(Required(root, "nodes")); Fields(node, "name", "mesh", "extras");
        if (Integer(Required(node, "mesh")) != 0) throw Invalid("Expected identity node and mesh zero.");
        JsonElement mesh = One(Required(root, "meshes")); Fields(mesh, "name", "primitives", "extras");
        string prefix = binding.AssetPath.Contains('/') ? binding.AssetPath[..(binding.AssetPath.LastIndexOf('/') + 1)] : "";
        JsonElement bufferRows = ArrayRows(Required(root, "buffers"), limits.MaxBuffersPerLod);
        var buffers = new FormationPackageFile[bufferRows.GetArrayLength()];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long bufferBytes = 0;
        for (int i = 0; i < buffers.Length; i++)
        {
            admission.Work.Step();
            JsonElement row = bufferRows[i]; Fields(row, "uri", "byteLength", "name", "extras");
            string uri = Text(Required(row, "uri")); ValidatePath(uri);
            if (!names.Add(uri)) throw Invalid("Duplicate buffer URI.");
            buffers[i] = admission.Require(prefix + uri);
            if (Integer(Required(row, "byteLength")) != buffers[i].Bytes) throw Invalid("Buffer pin length differs from declaration.");
            bufferBytes += buffers[i].Bytes;
        }
        if (buffers.Length == 0 || bufferBytes > 32L * 1024 * 1024) throw Capacity("Per-LOD aggregate buffer limit is 32 MiB.");
        JsonElement viewRows = ArrayRows(Required(root, "bufferViews"), limits.MaxDescriptorsPerLod);
        var views = new View[viewRows.GetArrayLength()]; var usedBuffers = new bool[buffers.Length];
        for (int i = 0; i < views.Length; i++)
        {
            admission.Work.Step();
            JsonElement row = viewRows[i]; Fields(row, "buffer", "byteOffset", "byteLength", "byteStride", "target", "name", "extras");
            int buffer = Index(Required(row, "buffer"), buffers.Length), offset = OptionalInt(row, "byteOffset", 0);
            int length = Integer(Required(row, "byteLength")), stride = OptionalInt(row, "byteStride", 0), target = OptionalInt(row, "target", 0);
            if (offset < 0 || length <= 0 || (long)offset + length > buffers[buffer].Bytes ||
                row.TryGetProperty("byteStride", out _) && (stride < 4 || stride > 252 || stride % 4 != 0) ||
                row.TryGetProperty("target", out _) && target is not (34962 or 34963)) throw Invalid("Invalid buffer view range/stride/target.");
            views[i] = new(buffer, offset, length, stride, target); usedBuffers[buffer] = true;
        }
        JsonElement accessorRows = ArrayRows(Required(root, "accessors"), limits.MaxDescriptorsPerLod);
        var accessors = new Accessor[accessorRows.GetArrayLength()]; var usedViews = new bool[views.Length];
        for (int i = 0; i < accessors.Length; i++)
        { admission.Work.Step(); accessors[i] = ReadAccessor(accessorRows[i], views); usedViews[accessors[i].View] = true; }
        RequireAll(usedBuffers, "buffers"); RequireAll(usedViews, "buffer views");
        int overlapSteps = 0;
        for (int i = 0; i < accessors.Length; i++)
            for (int j = i + 1; j < accessors.Length; j++)
                if (Overlap(accessors[i], accessors[j], admission.Work, ref overlapSteps)) throw Invalid("Visual accessor bytes overlap.");
        FormationVisualMaterial[] materials = Materials(root, prefix, admission);
        int declaredMaterialCount = materials.Length, defaultMaterial = -1;
        JsonElement primitiveRows = ArrayRows(Required(mesh, "primitives"), 32);
        if (primitiveRows.GetArrayLength() == 0) throw Invalid("A visual LOD needs primitives.");
        var primitives = new Primitive[primitiveRows.GetArrayLength()]; var used = new bool[accessors.Length];
        int vertexOffset = 0, triangleOffset = 0;
        for (int i = 0; i < primitives.Length; i++)
        {
            admission.Work.Step();
            JsonElement row = primitiveRows[i]; Fields(row, "attributes", "indices", "material", "mode", "extras");
            if (OptionalInt(row, "mode", 4) != 4) throw Invalid("Only indexed triangle primitives are supported.");
            JsonElement attributes = Required(row, "attributes"); Fields(attributes, "POSITION", "NORMAL", "TANGENT", "TEXCOORD_0");
            int position = Attribute("POSITION", 3, required: true), normal = Attribute("NORMAL", 3, required: true);
            int tangent = Attribute("TANGENT", 4, required: false), uv = Attribute("TEXCOORD_0", 2, required: false);
            int index = Index(Required(row, "indices"), accessors.Length); Accessor indices = accessors[index];
            if (used[index] || indices.Components != 1 || indices.Component is not (5123 or 5125) || indices.Count % 3 != 0 ||
                views[indices.View].Stride != 0 || views[indices.View].Target == 34962) throw Invalid("Invalid triangle index stream.");
            used[index] = true;
            int count = accessors[position].Count;
            if (count < 3) throw Invalid("POSITION must contain at least three vertices per primitive.");
            foreach (int a in new[] { normal, tangent, uv }) if (a >= 0 && accessors[a].Count != count) throw Invalid("Attribute count differs from POSITION.");
            int material = row.TryGetProperty("material", out JsonElement materialIndex) ? Index(materialIndex, declaredMaterialCount) : -1;
            if (material < 0)
            {
                if (defaultMaterial < 0)
                {
                    defaultMaterial = materials.Length; materials = materials.Append(DefaultMaterial()).ToArray();
                    if (++admission.Materials > limits.MaxMaterials) throw Capacity("Aggregate material count exceeded.");
                }
                material = defaultMaterial;
            }
            FormationVisualMaterial appearance = materials[material];
            if (appearance.Normal != null && tangent < 0) throw Invalid("A normal-mapped primitive requires an authored TANGENT stream.");
            if (uv < 0 && (appearance.BaseColour != null || appearance.MetallicRoughness != null || appearance.Normal != null || appearance.Occlusion != null || appearance.Emissive != null))
                throw Invalid("A textured primitive requires TEXCOORD_0.");
            primitives[i] = new(material, position, normal, tangent, uv, index, vertexOffset, triangleOffset);
            vertexOffset = checked(vertexOffset + count); triangleOffset = checked(triangleOffset + indices.Count / 3);
            admission.Vertices += count; admission.Indices += indices.Count; admission.Primitives++;
            admission.Streams = checked(admission.Streams + 4L * (count * (6L + (tangent < 0 ? 0 : 4) + (uv < 0 ? 0 : 2)) + indices.Count));
            if (vertexOffset > 60000 || triangleOffset > 50000 || admission.Vertices > limits.MaxVertices ||
                admission.Indices > limits.MaxIndices || admission.Primitives > limits.MaxPrimitives || admission.Streams > limits.MaxStreamBytes)
                throw Capacity("Visual stream counts/reservation exceeded.");

            int Attribute(string key, int components, bool required)
            {
                if (!attributes.TryGetProperty(key, out JsonElement value))
                { if (required) throw Invalid("Missing visual " + key + " stream."); return -1; }
                int a = Index(value, accessors.Length); Accessor candidate = accessors[a];
                if (used[a] || candidate.Component != 5126 || candidate.Components != components || views[candidate.View].Target == 34963)
                    throw Invalid("Distinct float attributes are required per primitive.");
                used[a] = true; return a;
            }
        }
        RequireAll(used, "accessors");
        CliffSupportDefinition definition = admission.Package.Catalog.RequireQualified(binding.GeometryKey, binding.GeometryRevision);
        CliffLodBinding? qualified = null;
        foreach (CliffLodBinding lod in definition.Lods) if (lod.Key == binding.LodKey) qualified = lod;
        if (qualified == null || Digest(qualified.ContentSha256) != Digest(binding.ContentSha256) ||
            qualified.Mesh.VertexCount != vertexOffset || qualified.Mesh.TriangleCount != triangleOffset)
            throw Invalid("Visual metadata differs from its qualified LOD binding.");
        return new(binding, qualified.Mesh, buffers, accessors, primitives, materials);
    }

    private static FormationVisualMaterial[] Materials(JsonElement root, string prefix, Admission admission)
    {
        JsonElement images = OptionalRows(root, "images", admission.Limits.MaxImages);
        var imageIndices = new int[images.GetArrayLength()];
        for (int i = 0; i < imageIndices.Length; i++)
        {
            JsonElement image = images[i]; Fields(image, "uri", "mimeType", "name", "extras");
            string uri = Text(Required(image, "uri")); ValidatePath(uri);
            imageIndices[i] = admission.Image(prefix + uri, image.TryGetProperty("mimeType", out JsonElement mime) ? Text(mime) : null);
        }
        JsonElement samplerRows = OptionalRows(root, "samplers", 128);
        var samplers = new FormationVisualSampler[samplerRows.GetArrayLength()];
        for (int i = 0; i < samplers.Length; i++)
        {
            JsonElement row = samplerRows[i]; Fields(row, "magFilter", "minFilter", "wrapS", "wrapT", "name", "extras");
            int mag = OptionalInt(row, "magFilter", 0), min = OptionalInt(row, "minFilter", 0);
            int s = OptionalInt(row, "wrapS", 10497), t = OptionalInt(row, "wrapT", 10497);
            if (mag == 0 && row.TryGetProperty("magFilter", out _) || min == 0 && row.TryGetProperty("minFilter", out _))
                throw Invalid("Sampler filter zero is reserved for an omitted filter.");
            if (mag is not (0 or 9728 or 9729) || min is not (0 or 9728 or 9729 or 9984 or 9985 or 9986 or 9987) ||
                s is not (33071 or 33648 or 10497) || t is not (33071 or 33648 or 10497)) throw Invalid("Unsupported sampler value.");
            samplers[i] = new(mag, min, s, t);
        }
        JsonElement textures = OptionalRows(root, "textures", 128);
        var textureInputs = new (int Image, FormationVisualSampler Sampler)[textures.GetArrayLength()];
        for (int i = 0; i < textureInputs.Length; i++)
        {
            JsonElement row = textures[i]; Fields(row, "source", "sampler", "name", "extras");
            textureInputs[i] = (imageIndices[Index(Required(row, "source"), imageIndices.Length)],
                row.TryGetProperty("sampler", out JsonElement sampler) ? samplers[Index(sampler, samplers.Length)] : new(0, 0, 10497, 10497));
        }
        JsonElement rows = OptionalRows(root, "materials", admission.Limits.MaxMaterials);
        admission.Materials += rows.GetArrayLength();
        if (admission.Materials > admission.Limits.MaxMaterials) throw Capacity("Aggregate material count exceeded.");
        var result = new FormationVisualMaterial[rows.GetArrayLength()];
        for (int i = 0; i < result.Length; i++)
        {
            JsonElement row = rows[i]; Fields(row, "name", "pbrMetallicRoughness", "normalTexture", "occlusionTexture", "emissiveTexture", "emissiveFactor", "alphaMode", "alphaCutoff", "doubleSided", "extras");
            Vector4 colour = Vector4.One; float metallic = 1, roughness = 1;
            FormationVisualTexture? colourMap = null, metalMap = null;
            if (row.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
            {
                Fields(pbr, "baseColorFactor", "baseColorTexture", "metallicFactor", "roughnessFactor", "metallicRoughnessTexture", "extras");
                if (pbr.TryGetProperty("baseColorFactor", out JsonElement c)) { float[] values = Vector(c, 4); colour = new(values[0], values[1], values[2], values[3]); }
                metallic = Number(pbr, "metallicFactor", 1, 0, 1); roughness = Number(pbr, "roughnessFactor", 1, 0, 1);
                colourMap = Texture(pbr, "baseColorTexture"); metalMap = Texture(pbr, "metallicRoughnessTexture");
            }
            Vector3 emissive = Vector3.Zero;
            if (row.TryGetProperty("emissiveFactor", out JsonElement e)) { float[] values = Vector(e, 3); emissive = new(values[0], values[1], values[2]); }
            string alpha = row.TryGetProperty("alphaMode", out JsonElement a) ? Text(a) : "OPAQUE";
            if (alpha is not ("OPAQUE" or "MASK" or "BLEND")) throw Invalid("Unsupported alpha mode.");
            bool doubleSided = false;
            if (row.TryGetProperty("doubleSided", out JsonElement d))
            { if (d.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid("doubleSided must be boolean."); doubleSided = d.GetBoolean(); }
            result[i] = new(row.TryGetProperty("name", out JsonElement name) ? Text(name) : "", colour, metallic, roughness, emissive,
                alpha, Number(row, "alphaCutoff", 0.5f, 0, float.MaxValue), doubleSided, colourMap, metalMap,
                Texture(row, "normalTexture"), Texture(row, "occlusionTexture"), Texture(row, "emissiveTexture"));
        }
        return result;

        FormationVisualTexture? Texture(JsonElement owner, string role)
        {
            if (!owner.TryGetProperty(role, out JsonElement row)) return null;
            Fields(row, "index", "texCoord", "scale", "strength", "extras");
            if (role != "normalTexture" && row.TryGetProperty("scale", out _) || role != "occlusionTexture" && row.TryGetProperty("strength", out _)) throw Invalid("Texture factor in the wrong role.");
            int index = Index(Required(row, "index"), textureInputs.Length), uv = OptionalInt(row, "texCoord", 0);
            if (uv != 0) throw Invalid("Only TEXCOORD_0 bindings are supported.");
            float factor = role == "normalTexture" ? Number(row, "scale", 1, -float.MaxValue, float.MaxValue) :
                role == "occlusionTexture" ? Number(row, "strength", 1, 0, 1) : 1;
            return new(textureInputs[index].Image, textureInputs[index].Sampler, uv, factor);
        }
    }

    private static Accessor ReadAccessor(JsonElement row, View[] views)
    {
        Fields(row, "bufferView", "byteOffset", "componentType", "normalized", "count", "type", "min", "max", "name", "extras");
        if (row.TryGetProperty("normalized", out JsonElement normalized) && normalized.ValueKind != JsonValueKind.False) throw Invalid("Normalized accessors are unsupported.");
        int viewIndex = Index(Required(row, "bufferView"), views.Length); View view = views[viewIndex];
        int component = Integer(Required(row, "componentType")), count = Integer(Required(row, "count"));
        int componentBytes = component switch { 5123 => 2, 5125 or 5126 => 4, _ => throw Invalid("Unsupported accessor component.") };
        int components = Text(Required(row, "type")) switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => throw Invalid("Unsupported accessor shape.") };
        if (count < 1 || count > (components == 1 ? 150000 : 60000)) throw Capacity("Accessor count exceeds fixed qualification ceilings.");
        int offset = OptionalInt(row, "byteOffset", 0), width = components * componentBytes, stride = view.Stride == 0 ? width : view.Stride;
        if (offset < 0 || offset % componentBytes != 0 || view.Offset % componentBytes != 0 || stride < width || stride % componentBytes != 0 ||
            (long)offset + (long)(count - 1) * stride + width > view.Length) throw Invalid("Invalid accessor alignment/range.");
        foreach (string bound in new[] { "min", "max" })
            if (row.TryGetProperty(bound, out JsonElement values))
            { if (ArrayRows(values, 4).GetArrayLength() != components) throw Invalid("Accessor bound width mismatch."); foreach (JsonElement value in values.EnumerateArray()) _ = Finite(value); }
        return new(viewIndex, view.Buffer, view.Offset + offset, count, stride, components, component);
    }
    private static bool Overlap(Accessor a, Accessor b, Work work, ref int steps)
    {
        if (a.Buffer != b.Buffer || a.Start >= b.End || b.Start >= a.End) return false;
        int ai = 0, bi = 0;
        while (ai < a.Count && bi < b.Count)
        {
            if (++steps > 4000000) throw Capacity("Per-LOD accessor overlap work exceeded qualification ceiling.");
            work.Step(); long first = (long)a.Start + (long)ai * a.Stride, second = (long)b.Start + (long)bi * b.Stride;
            if (first + a.Width <= second) ai++;
            else if (second + b.Width <= first) bi++;
            else return true;
        }
        return false;
    }
    private static FormationVisualMaterial DefaultMaterial() => new("", Vector4.One, 1, 1, Vector3.Zero, "OPAQUE", 0.5f, false, null, null, null, null, null);
    private static void Unit(float x, float y, float z, string role)
    { double squared = (double)x * x + (double)y * y + (double)z * z; if (squared < 0.98 || squared > 1.02) throw Invalid(role + " must be a source unit vector."); }

    // Full-file authentication and header capture share one open handle; Load authenticates again from its own read.
    private static byte[] Verify(string root, FormationPackageFile file, int prefixBytes, CancellationToken cancellation)
    {
        using FileStream stream = Open(root, file); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] prefix = prefixBytes == 0 ? Array.Empty<byte>() : new byte[prefixBytes];
        byte[] scratch = new byte[65536]; long read = 0;
        while (read < file.Bytes)
        {
            cancellation.ThrowIfCancellationRequested(); int n = stream.Read(scratch, 0, (int)Math.Min(scratch.Length, file.Bytes - read));
            if (n == 0) throw Invalid("Visual file ended before its pinned length.");
            hash.AppendData(scratch, 0, n);
            if (read < prefix.Length) scratch.AsSpan(0, Math.Min(n, prefix.Length - (int)read)).CopyTo(prefix.AsSpan((int)read));
            read += n;
        }
        if (stream.ReadByte() != -1 || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw Invalid("Visual file digest/length mismatch: " + file.Path);
        return prefix;
    }
    private static byte[] Read(string root, FormationPackageFile file, CancellationToken cancellation)
    {
        using FileStream stream = Open(root, file); byte[] bytes = new byte[checked((int)file.Bytes)]; int offset = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (offset < bytes.Length)
        {
            cancellation.ThrowIfCancellationRequested(); int n = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (n == 0) throw Invalid("Visual file ended early."); hash.AppendData(bytes, offset, n); offset += n;
        }
        if (stream.ReadByte() != -1 || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw Invalid("Visual file digest/length mismatch: " + file.Path);
        return bytes;
    }
    private static FileStream Open(string root, FormationPackageFile file)
    {
        ValidatePath(file.Path); string current = root;
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw Invalid("Visual package root is a reparse point.");
        string[] segments = file.Path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]); FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || i == segments.Length - 1 && (attributes & FileAttributes.Directory) != 0)
                throw Invalid("Visual dependencies must be regular files without reparse points.");
        }
        var stream = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        if (stream.Length != file.Bytes) { stream.Dispose(); throw Invalid("Visual file length differs from pin."); }
        return stream;
    }
    private static (string Mime, int Width, int Height) ImageHeader(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length >= 33 && bytes[..8].SequenceEqual(png))
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) ||
                bytes[24] != 8 || bytes[25] is not (0 or 2 or 3 or 4 or 6) || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1)
                throw Invalid("Unsupported PNG header.");
            uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
            if (width > int.MaxValue || height > int.MaxValue) throw Capacity("PNG dimensions exceed integer range.");
            return ("image/png", (int)width, (int)height);
        }
        if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216)
        {
            int at = 2;
            while (at < bytes.Length)
            {
                if (bytes[at++] != 255) throw Invalid("Malformed JPEG marker.");
                while (at < bytes.Length && bytes[at] == 255) at++;
                if (at >= bytes.Length) break; int marker = bytes[at++];
                if (marker is 0 or 216 or 217 or 218) throw Invalid("JPEG dimensions must precede scan data.");
                if (marker is 1 or >= 208 and <= 215) continue;
                if (at + 2 > bytes.Length) break; int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[at..]);
                if (length < 2) throw Invalid("Malformed JPEG segment length.");
                if (at + length > bytes.Length) break;
                if (marker is 192 or 193 or 194)
                {
                    if (length < 8 || bytes[at + 2] != 8 || bytes[at + 7] is not (1 or 3) || length != 8 + 3 * bytes[at + 7]) throw Invalid("Unsupported JPEG frame layout.");
                    return ("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 3)..]));
                }
                if (marker is >= 195 and <= 207 && marker is not (196 or 200 or 204)) throw Invalid("Unsupported JPEG coding mode.");
                at += length;
            }
            throw Capacity("JPEG header exceeds bounded header admission or is incomplete.");
        }
        throw Invalid("Only external PNG/JPEG images with bounded headers are supported.");
    }
    private static void ValidateLimits(FormationVisualLimits x)
    {
        if (x.MaxLods is < 1 or > 64 || x.MaxFiles is < 1 or > 256 || x.MaxPrimitives is < 1 or > 2048 ||
            x.MaxMaterials is < 1 or > 512 || x.MaxImages is < 1 or > 128 || x.MaxBuffersPerLod is < 1 or > 8 ||
            x.MaxDescriptorsPerLod is < 1 or > 128 || x.MaxVertices is < 3 or > 1048576 || x.MaxIndices is < 3 or > 3145728 ||
            x.MaxJsonBytes is < 1 or > 4194304 || x.MaxFileBytes is < 1 or > 134217728 || x.MaxEncodedBytes is < 1 or > 1073741824 ||
            x.MaxStreamBytes is < 1 or > 268435456 || x.MaxDecodedImageBytes is < 1 or > 2147483648L ||
            x.MaxMetadataBytes is < 1 or > 134217728 || x.MaxReservationBytes is < 1 or > 4294967296L ||
            x.MaxImageDimension is < 1 or > 16384 || x.MaxImageHeaderBytes is < 33 or > 1048576 || x.MaxValidationSteps is < 1 or > 16000000)
            throw Capacity("Invalid visual admission limits.");
    }
    private static void ValidatePath(string path)
    {
        if (path.Length == 0 || path.Length > 1024 || path != path.Trim() || path.IndexOfAny(new[] { ':', '\\', '?', '#', '%', '\0' }) >= 0)
            throw Invalid("Expected canonical relative visual path.");
        foreach (char value in path) if (char.IsControl(value)) throw Invalid("Control character in visual path.");
        foreach (string segment in path.Split('/'))
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')) throw Invalid("Ambiguous visual path segment.");
    }
    private static string Digest(string value)
    {
        if (value == null || value.Length != 64 || !value.All(Uri.IsHexDigit)) throw Invalid("Expected SHA-256 pin.");
        return value.ToLowerInvariant();
    }
    private static JsonElement Required(JsonElement owner, string key)
    { if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(key, out JsonElement value)) throw Invalid("Missing visual field: " + key); return value; }
    private static void Fields(JsonElement row, params string[] fields)
    {
        if (row.ValueKind != JsonValueKind.Object) throw Invalid("Expected visual object.");
        foreach (JsonProperty field in row.EnumerateObject()) if (!fields.Contains(field.Name, StringComparer.Ordinal)) throw Invalid("Unsupported visual field: " + field.Name);
    }
    private static JsonElement ArrayRows(JsonElement value, int maximum)
    { if (value.ValueKind != JsonValueKind.Array) throw Invalid("Expected visual array."); if (value.GetArrayLength() > maximum) throw Capacity("Visual array count exceeds admission."); return value; }
    private static JsonElement OptionalRows(JsonElement owner, string key, int maximum)
        => owner.TryGetProperty(key, out JsonElement value) ? ArrayRows(value, maximum) : EmptyRows;
    private static readonly JsonElement EmptyRows = JsonSerializer.SerializeToElement(Array.Empty<int>());
    private static JsonElement One(JsonElement value)
    { if (ArrayRows(value, 1).GetArrayLength() != 1) throw Invalid("Expected exactly one visual entry."); return value[0]; }
    private static int Integer(JsonElement value)
    { if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number)) throw Invalid("Expected visual integer."); return number; }
    private static int OptionalInt(JsonElement owner, string key, int fallback) => owner.TryGetProperty(key, out JsonElement value) ? Integer(value) : fallback;
    private static int Index(JsonElement value, int length)
    { int index = Integer(value); if ((uint)index >= (uint)length) throw Invalid("Visual index outside declaration."); return index; }
    private static string Text(JsonElement value)
    { if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: <= 1024 } text) throw Invalid("Expected bounded visual string."); return text; }
    private static float Finite(JsonElement value)
    { if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out float number) || !float.IsFinite(number)) throw Invalid("Expected finite visual number."); return number; }
    private static float Number(JsonElement owner, string key, float fallback, float min, float max)
    { float value = owner.TryGetProperty(key, out JsonElement found) ? Finite(found) : fallback; if (value < min || value > max) throw Invalid("Visual scalar outside its range: " + key); return value; }
    private static float[] Vector(JsonElement row, int count)
    {
        if (ArrayRows(row, count).GetArrayLength() != count) throw Invalid("Visual vector length mismatch.");
        var result = new float[count]; for (int i = 0; i < count; i++) { result[i] = Finite(row[i]); if (result[i] < 0 || result[i] > 1) throw Invalid("Material factor outside [0,1]."); } return result;
    }
    private static void RequireAll(bool[] used, string role)
    { foreach (bool value in used) if (!value) throw Invalid("Unused visual " + role + " are outside admission."); }
    private static FormationValidationException Invalid(string message) => new(message);
    private static FormationCapacityException Capacity(string message) => new(message);
}
