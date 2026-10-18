using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Surface;

/// <summary>
/// Finite admission allowances for authenticated visual inputs. Decoded image bytes assume complete RGBA8 mip
/// chains; reservations include CPU preparation and upload scratch, not measured renderer or driver allocations.
/// The loader validates both individual ceilings and aggregate use before publishing any data.
/// </summary>
public sealed record FormationVisualLimits
{
    public int MaxLods { get; init; } = 16;
    public int MaxFiles { get; init; } = 128;
    public int MaxPrimitives { get; init; } = 128;
    public int MaxMaterials { get; init; } = 128;
    public int MaxImages { get; init; } = 32;
    public int MaxBuffersPerLod { get; init; } = 8;
    public int MaxDescriptorsPerLod { get; init; } = 128;
    public int MaxVertices { get; init; } = 262144;
    public int MaxIndices { get; init; } = 786432;
    public int MaxJsonBytes { get; init; } = 4 * 1024 * 1024;
    public long MaxFileBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxEncodedBytes { get; init; } = 256L * 1024 * 1024;
    public long MaxStreamBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxDecodedImageBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxMetadataBytes { get; init; } = 32L * 1024 * 1024;
    public long MaxReservationBytes { get; init; } = 1024L * 1024 * 1024;
    public int MaxImageDimension { get; init; } = 8192;
    public int MaxImageHeaderBytes { get; init; } = 262144;
    public int MaxValidationSteps { get; init; } = 8000000;
}

/// <summary>Aggregate counts and declared byte reservations across all selected LODs and distinct images.</summary>
public readonly record struct FormationVisualSizing(int Lods, int Primitives, long Vertices, long Indices, int Images,
    long MetadataBytes, long EncodedBytes, long StreamBytes, long DecodedImageBytes, long PeakReservationBytes);

/// <summary>
/// An authenticated bounded metadata plan. It holds no decoded mesh streams, native resources or world references.
/// Loading reauthenticates required file bytes; this handle does not make the files immutable on disk.
/// </summary>
public sealed class FormationVisualPreflight
{
    internal FormationVisualPreflight(object plan, FormationVisualSizing sizing, FormationPackageFile[] requiredFiles)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(requiredFiles);
        Plan = plan;
        Sizing = sizing;
        RequiredFiles = Array.AsReadOnly((FormationPackageFile[])requiredFiles.Clone());
    }
    internal object Plan { get; }
    public FormationVisualSizing Sizing { get; }
    public IReadOnlyList<FormationPackageFile> RequiredFiles { get; }
}

/// <summary>
/// Copy-only immutable stream. Its internal constructor transfers exclusive ownership of the supplied array;
/// trusted producers must not retain or mutate that array after publication.
/// </summary>
public sealed class FormationVisualArray<T>
{
    private readonly T[] _values;
    internal FormationVisualArray(T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
    }
    public int Count => _values.Length;
    public T this[int index] => _values[index];
    public void CopyTo(Span<T> destination) => _values.AsSpan().CopyTo(destination);
}

/// <summary>
/// Complete authenticated CPU visual payload. This authenticates future upload inputs, not post-upload GPU contents,
/// and grants no physical support permissions. Internal constructors transfer ownership of all supplied arrays.
/// </summary>
public sealed class FormationVisualData
{
    internal FormationVisualData(string receiptSha256, FormationVisualSizing sizing, FormationVisualLod[] lods,
        FormationVisualImage[] images)
    {
        ArgumentNullException.ThrowIfNull(receiptSha256);
        ArgumentNullException.ThrowIfNull(lods);
        ArgumentNullException.ThrowIfNull(images);
        ReceiptSha256 = receiptSha256;
        Sizing = sizing;
        Lods = Array.AsReadOnly(lods);
        Images = Array.AsReadOnly(images);
    }
    public string ReceiptSha256 { get; }
    public FormationVisualSizing Sizing { get; }
    public IReadOnlyList<FormationVisualLod> Lods { get; }
    public IReadOnlyList<FormationVisualImage> Images { get; }
}

/// <summary>One qualified visual LOD in source primitive order, with an immutable local material table.</summary>
public sealed class FormationVisualLod
{
    internal FormationVisualLod(string geometryKey, int geometryRevision, string lodKey, string assetPath,
        FormationVisualPrimitive[] primitives, FormationVisualMaterial[] materials)
    {
        ArgumentNullException.ThrowIfNull(geometryKey);
        ArgumentNullException.ThrowIfNull(lodKey);
        ArgumentNullException.ThrowIfNull(assetPath);
        ArgumentNullException.ThrowIfNull(primitives);
        ArgumentNullException.ThrowIfNull(materials);
        GeometryKey = geometryKey;
        GeometryRevision = geometryRevision;
        LodKey = lodKey;
        AssetPath = assetPath;
        Primitives = Array.AsReadOnly(primitives);
        Materials = Array.AsReadOnly(materials);
    }
    public string GeometryKey { get; }
    public int GeometryRevision { get; }
    public string LodKey { get; }
    public string AssetPath { get; }
    public IReadOnlyList<FormationVisualPrimitive> Primitives { get; }
    public IReadOnlyList<FormationVisualMaterial> Materials { get; }
}

/// <summary>
/// Exact float32 source attributes and local indices. Positions/normals have three components, tangents four and
/// UVs two. Optional absent streams are empty. Indices retain glTF winding; renderer conversion happens at upload.
/// Source offsets describe canonical per-LOD concatenation without welding, reordering or implicit transforms.
/// </summary>
public sealed class FormationVisualPrimitive
{
    internal FormationVisualPrimitive(int ordinal, int materialIndex, int sourceVertexOffset, int sourceTriangleOffset,
        float[] positions, float[] normals, float[] tangents, float[] texCoords, int[] indices)
    {
        Ordinal = ordinal;
        MaterialIndex = materialIndex;
        SourceVertexOffset = sourceVertexOffset;
        SourceTriangleOffset = sourceTriangleOffset;
        Positions = new(positions);
        Normals = new(normals);
        Tangents = new(tangents);
        TexCoords = new(texCoords);
        Indices = new(indices);
    }
    public int Ordinal { get; }
    public int MaterialIndex { get; }
    public int SourceVertexOffset { get; }
    public int SourceTriangleOffset { get; }
    public int VertexCount => Positions.Count / 3;
    public bool HasTangents => Tangents.Count != 0;
    public bool HasTexCoords => TexCoords.Count != 0;
    public FormationVisualArray<float> Positions { get; }
    public FormationVisualArray<float> Normals { get; }
    public FormationVisualArray<float> Tangents { get; }
    public FormationVisualArray<float> TexCoords { get; }
    public FormationVisualArray<int> Indices { get; }
}

/// <summary>Authenticated encoded image bytes plus bounded header dimensions; no pixel decoding has occurred.</summary>
public sealed class FormationVisualImage
{
    internal FormationVisualImage(string path, string sha256, string mimeType, int width, int height,
        long decodedMipBytes, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(sha256);
        ArgumentNullException.ThrowIfNull(mimeType);
        Path = path;
        Sha256 = sha256;
        MimeType = mimeType;
        Width = width;
        Height = height;
        DecodedMipBytes = decodedMipBytes;
        Bytes = new(bytes);
    }
    public string Path { get; }
    public string Sha256 { get; }
    public string MimeType { get; }
    public int Width { get; }
    public int Height { get; }
    public long DecodedMipBytes { get; }
    public FormationVisualArray<byte> Bytes { get; }
}

/// <summary>glTF sampler values. Filter zero means unspecified; wrap 10497 means repeat.</summary>
public readonly record struct FormationVisualSampler(int MagFilter, int MinFilter, int WrapS, int WrapT);

/// <summary>Factor is normal scale or occlusion strength for those roles, and one for other texture roles.</summary>
public sealed record FormationVisualTexture(int ImageIndex, FormationVisualSampler Sampler, int TexCoord, float Factor);

/// <summary>Explicit glTF metallic-roughness PBR values, including defaults and immutable texture-role bindings.</summary>
public sealed class FormationVisualMaterial
{
    internal FormationVisualMaterial(string name, Vector4 baseColourFactor, float metallicFactor, float roughnessFactor,
        Vector3 emissiveFactor, string alphaMode, float alphaCutoff, bool doubleSided, FormationVisualTexture? baseColour,
        FormationVisualTexture? metallicRoughness, FormationVisualTexture? normal, FormationVisualTexture? occlusion,
        FormationVisualTexture? emissive)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(alphaMode);
        Name = name;
        BaseColourFactor = baseColourFactor;
        MetallicFactor = metallicFactor;
        RoughnessFactor = roughnessFactor;
        EmissiveFactor = emissiveFactor;
        AlphaMode = alphaMode;
        AlphaCutoff = alphaCutoff;
        DoubleSided = doubleSided;
        BaseColour = baseColour;
        MetallicRoughness = metallicRoughness;
        Normal = normal;
        Occlusion = occlusion;
        Emissive = emissive;
    }
    public string Name { get; }
    public Vector4 BaseColourFactor { get; }
    public float MetallicFactor { get; }
    public float RoughnessFactor { get; }
    public Vector3 EmissiveFactor { get; }
    public string AlphaMode { get; }
    public float AlphaCutoff { get; }
    public bool DoubleSided { get; }
    public FormationVisualTexture? BaseColour { get; }
    public FormationVisualTexture? MetallicRoughness { get; }
    public FormationVisualTexture? Normal { get; }
    public FormationVisualTexture? Occlusion { get; }
    public FormationVisualTexture? Emissive { get; }
}
