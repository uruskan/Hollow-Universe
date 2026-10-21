using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Hollow.Surface;

public class FloraAppearanceValidationException : Exception
{
    public FloraAppearanceValidationException(string message) : base(message) { }
    public FloraAppearanceValidationException(string message, Exception inner) : base(message, inner) { }
}

public sealed class FloraAppearanceCapacityException : FloraAppearanceValidationException
{
    public FloraAppearanceCapacityException(string message) : base(message) { }
}

/// <summary>
/// Explicit per-load byte admission. Counts exact manifest/payload reads, not native uploads, driver copies or a
/// session cache. Metadata has separate schema count/string/depth bounds. The caller must also bound concurrent loads.
/// </summary>
public sealed class FloraAppearanceLimits
{
    public FloraAppearanceLimits(int maxManifestBytes, int maxImageBytes, long maxTotalBytes)
    {
        if (maxManifestBytes is < 1 or > 1048576 || maxImageBytes is < 1 or > 67108864 ||
            maxTotalBytes is < 1 or > 268435456)
            throw new FloraAppearanceCapacityException("Invalid flora package byte limits.");
        MaxManifestBytes = maxManifestBytes; MaxImageBytes = maxImageBytes; MaxTotalBytes = maxTotalBytes;
    }
    public int MaxManifestBytes { get; }
    public int MaxImageBytes { get; }
    public long MaxTotalBytes { get; }
}

/// <summary>Linear RGB; preserves finite Single bits, including the legacy BladeMean, without recomputing pixels.</summary>
public readonly record struct FloraAppearanceColour(float R, float G, float B);

/// <summary>Schema v1 fixes these seven complete RGBA8 chains. MipLevels includes the base level.</summary>
public sealed class FloraAppearanceImageLayout
{
    internal FloraAppearanceImageLayout(string role, string file, int width, int height, string mipPolicy)
    {
        Role = role; File = file; Width = width; Height = height; MipPolicy = mipPolicy;
        int w = width, h = height, levels = 0;
        long bytes = 0;
        do
        {
            bytes = checked(bytes + (long)w * h * 4); levels++;
            if (w == 1 && h == 1) break;
            w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
        } while (true);
        ByteLength = checked((int)bytes); MipLevels = levels;
    }
    public string Role { get; }
    public string File { get; }
    public int Width { get; }
    public int Height { get; }
    public int MipLevels { get; }
    public int ByteLength { get; }
    public string Format => "RGBA8";
    public string MipPolicy { get; }
}

/// <summary>Finite legacy layout, independent of any world descriptor, physical generation identity or palette.</summary>
public static class FloraAppearanceSchema
{
    public const string Package = "hollow-flora-appearance-package-v1";
    public const string Input = "hollow-flora-appearance-input-v1";
    public const int Rows = 11, Views = 8, CellPixels = 256, NativeRowStart = 4, NativeRowEnd = 11;
    public const int MaxDependencies = 256;
    public const long PayloadBytes = 87381292;
    public static IReadOnlyList<FloraAppearanceImageLayout> Images { get; } = Array.AsReadOnly(new[]
    {
        new FloraAppearanceImageLayout("near-atlas", "near-atlas.rgba8-mips", 2048, 2048, "coverage-4x4-v1"),
        new FloraAppearanceImageLayout("bark", "bark.rgba8-mips", 512, 512, "ordinary-v1"),
        new FloraAppearanceImageLayout("bark-normal", "bark-normal.rgba8-mips", 512, 512, "ordinary-v1"),
        new FloraAppearanceImageLayout("birch", "birch.rgba8-mips", 256, 256, "ordinary-v1"),
        new FloraAppearanceImageLayout("birch-normal", "birch-normal.rgba8-mips", 256, 256, "ordinary-v1"),
        new FloraAppearanceImageLayout("impostor-colour", "impostor-colour.rgba8-mips", 2048, 2816, "dilation-coverage-8x11-v1"),
        new FloraAppearanceImageLayout("impostor-normal", "impostor-normal.rgba8-mips", 2048, 2816, "dilation-ordinary-v1"),
    });

    internal static (ScatterKind Kind, int Variant) Binding(int row) => row switch
    {
        < 0 or >= Rows => throw new FloraAppearanceValidationException("Invalid flora row ordinal."),
        < 4 => (ScatterKind.Broadleaf, row),
        < 7 => (ScatterKind.Fir, row - 4),
        < 10 => (ScatterKind.Birch, row - 7),
        _ => (ScatterKind.Pine, 0),
    };
}

/// <summary>
/// Logical input inventory entry. Keys identify source/import/runtime dependencies, not paths to open. The independent
/// build integration must enumerate the complete effective dependency closure; this loader cannot discover renderer inputs.
/// </summary>
public sealed class FloraAppearanceDependency
{
    public FloraAppearanceDependency(string key, long bytes, string sha256)
    {
        Key = FloraAppearanceCheck.Text(key, 240);
        if (bytes < 1) throw new FloraAppearanceValidationException("Input dependency length must be positive.");
        Bytes = bytes; Sha256 = FloraAppearanceCheck.Hash(sha256);
    }
    public string Key { get; }
    public long Bytes { get; }
    public string Sha256 { get; }
}

/// <summary>
/// Ordered geometry/material input identity. Geometry digests bind actual vertex/index/attribute data for each LOD;
/// MaterialSha256 binds both LODs' ordered surface materials, effective texture/mip identities, factors, cutoffs and
/// near/bake shader interpretation (including native/vertex-colour overrides). They are not
/// file paths or assertions of physical/collision qualification. The independent renderer adapter computes these values.
/// </summary>
public sealed class FloraAppearanceRow
{
    public FloraAppearanceRow(int row, ScatterKind kind, int variant, string variantKey, float speciesScale,
        float height, float extent, string lod0GeometrySha256, string lod1GeometrySha256, string materialSha256)
    {
        if (FloraAppearanceSchema.Binding(row) != (kind, variant))
            throw new FloraAppearanceValidationException("Flora row species/variant order differs from schema v1.");
        FloraAppearanceCheck.Positive(speciesScale, 16); FloraAppearanceCheck.Positive(height, 1024);
        FloraAppearanceCheck.Positive(extent, 1024);
        Row = row; Kind = kind; Variant = variant; VariantKey = FloraAppearanceCheck.Id(variantKey);
        SpeciesScale = speciesScale; Height = height; Extent = extent;
        Lod0GeometrySha256 = FloraAppearanceCheck.Hash(lod0GeometrySha256);
        Lod1GeometrySha256 = FloraAppearanceCheck.Hash(lod1GeometrySha256);
        MaterialSha256 = FloraAppearanceCheck.Hash(materialSha256);
    }
    public int Row { get; }
    public ScatterKind Kind { get; }
    public int Variant { get; }
    public string VariantKey { get; }
    public float SpeciesScale { get; }
    public float Height { get; }
    public float Extent { get; }
    public string Lod0GeometrySha256 { get; }
    public string Lod1GeometrySha256 { get; }
    public string MaterialSha256 { get; }
    public bool Native => Row >= FloraAppearanceSchema.NativeRowStart;
}

/// <summary>
/// Actual exporter configuration, all input to appearance identity. BakeRevision also versions the adapter's geometry
/// and material digest encodings; the loader treats those externally computed digests as opaque approved inputs.
/// Device changes do not claim bit-exact rebakes. Renderer-side dependency discovery/measurement is still required.
/// </summary>
public sealed class FloraAppearanceBakeProfile
{
    public FloraAppearanceBakeProfile(string engineBuild, string renderer, string deviceProfile, string bakeRevision,
        string finishRevision, string projectionRevision)
    {
        EngineBuild = FloraAppearanceCheck.Text(engineBuild, 160); Renderer = FloraAppearanceCheck.Text(renderer, 80);
        DeviceProfile = FloraAppearanceCheck.Text(deviceProfile, 160); BakeRevision = FloraAppearanceCheck.Id(bakeRevision);
        FinishRevision = FloraAppearanceCheck.Id(finishRevision); ProjectionRevision = FloraAppearanceCheck.Id(projectionRevision);
    }
    public string EngineBuild { get; }
    public string Renderer { get; }
    public string DeviceProfile { get; }
    public string BakeRevision { get; }
    public string FinishRevision { get; }
    public string ProjectionRevision { get; }
}

/// <summary>
/// Immutable, bounded canonical input snapshot. Construct independently from approved source/runtime inputs, before
/// reading an export's declarations. Its digest is an appearance identity only and must never seed placement or saves.
/// </summary>
public sealed class FloraAppearanceInputContract
{
    public FloraAppearanceInputContract(int recipeSeed, FloraAppearanceBakeProfile profile,
        IReadOnlyList<FloraAppearanceDependency> dependencies, IReadOnlyList<FloraAppearanceRow> rows)
    {
        if (recipeSeed < 0) throw new FloraAppearanceValidationException("Legacy appearance recipe seed must be nonnegative.");
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(dependencies); ArgumentNullException.ThrowIfNull(rows);
        int dependencyCount = dependencies.Count, rowCount = rows.Count;
        if (dependencyCount is < 1 or > FloraAppearanceSchema.MaxDependencies || rowCount != FloraAppearanceSchema.Rows)
            throw new FloraAppearanceValidationException("Invalid flora input inventory/row count.");
        var ordered = new FloraAppearanceDependency[dependencyCount];
        for (int i = 0; i < ordered.Length; i++) ordered[i] = dependencies[i] ?? throw new FloraAppearanceValidationException("Null dependency.");
        Array.Sort(ordered, (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        for (int i = 1; i < ordered.Length; i++)
            if (ordered[i - 1].Key == ordered[i].Key) throw new FloraAppearanceValidationException("Duplicate input dependency key.");
        var rowCopy = new FloraAppearanceRow[rowCount];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rowCopy.Length; i++)
        {
            rowCopy[i] = rows[i] ?? throw new FloraAppearanceValidationException("Null flora row.");
            if (rowCopy[i].Row != i || !keys.Add(rowCopy[i].VariantKey))
                throw new FloraAppearanceValidationException("Flora rows must be complete, ordered and uniquely named.");
        }
        RecipeSeed = recipeSeed; Profile = profile;
        Dependencies = Array.AsReadOnly(ordered); Rows = Array.AsReadOnly(rowCopy);
        Fingerprint = CanonicalFingerprint();
    }
    public int RecipeSeed { get; }
    public FloraAppearanceBakeProfile Profile { get; }
    public IReadOnlyList<FloraAppearanceDependency> Dependencies { get; }
    public IReadOnlyList<FloraAppearanceRow> Rows { get; }
    public string Fingerprint { get; }

    private string CanonicalFingerprint()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
        writer.Write(FloraAppearanceSchema.Input); writer.Write(RecipeSeed);
        writer.Write(Profile.EngineBuild); writer.Write(Profile.Renderer); writer.Write(Profile.DeviceProfile);
        writer.Write(Profile.BakeRevision); writer.Write(Profile.FinishRevision); writer.Write(Profile.ProjectionRevision);
        writer.Write(FloraAppearanceSchema.Views); writer.Write(FloraAppearanceSchema.CellPixels);
        writer.Write(FloraAppearanceSchema.NativeRowStart); writer.Write(FloraAppearanceSchema.NativeRowEnd);
        writer.Write(Dependencies.Count);
        foreach (FloraAppearanceDependency d in Dependencies) { writer.Write(d.Key); writer.Write(d.Bytes); writer.Write(d.Sha256); }
        writer.Write(Rows.Count);
        foreach (FloraAppearanceRow r in Rows)
        {
            writer.Write(r.Row); writer.Write((byte)r.Kind); writer.Write(r.Variant); writer.Write(r.VariantKey);
            writer.Write(r.SpeciesScale); writer.Write(r.Height); writer.Write(r.Extent);
            writer.Write(r.Lod0GeometrySha256); writer.Write(r.Lod1GeometrySha256); writer.Write(r.MaterialSha256); writer.Write(r.Native);
        }
        writer.Write(FloraAppearanceSchema.Images.Count);
        foreach (FloraAppearanceImageLayout image in FloraAppearanceSchema.Images)
        {
            writer.Write(image.Role); writer.Write(image.File); writer.Write(image.Width); writer.Write(image.Height);
            writer.Write(image.Format); writer.Write(image.MipLevels); writer.Write(image.MipPolicy); writer.Write(image.ByteLength);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(memory.GetBuffer().AsSpan(0, checked((int)memory.Length))));
    }
}

/// <summary>
/// Trust anchor supplied by an approved catalog/build record OUTSIDE the package. There is deliberately no loader
/// that manufactures this pin from a package manifest. Hashes authenticate an approved export, not its artistic truth.
/// </summary>
public sealed class FloraAppearancePin
{
    public FloraAppearancePin(string appearanceId, int revision, string manifestSha256, FloraAppearanceInputContract expectedInputs)
    {
        AppearanceId = FloraAppearanceCheck.Id(appearanceId);
        if (revision < 1) throw new FloraAppearanceValidationException("Invalid appearance revision.");
        ArgumentNullException.ThrowIfNull(expectedInputs);
        Revision = revision; ManifestSha256 = FloraAppearanceCheck.Hash(manifestSha256); ExpectedInputs = expectedInputs;
    }
    public string AppearanceId { get; }
    public int Revision { get; }
    public string ManifestSha256 { get; }
    public FloraAppearanceInputContract ExpectedInputs { get; }
}

internal static class FloraAppearanceCheck
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    internal static string Text(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
            throw new FloraAppearanceValidationException("Invalid flora text field.");
        try { _ = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException error) { throw new FloraAppearanceValidationException("Invalid Unicode in flora text.", error); }
        return value;
    }
    internal static string Id(string value)
    {
        Text(value, 80);
        if (value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')) || value is "." or "..")
            throw new FloraAppearanceValidationException("Invalid flora identifier.");
        return value;
    }
    internal static string Hash(string value)
    {
        if (value == null || value.Length != 64 || value.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
            throw new FloraAppearanceValidationException("Expected a SHA-256 digest.");
        return value.ToUpperInvariant();
    }
    internal static void Positive(float value, float maximum)
    {
        if (!float.IsFinite(value) || value <= 0 || value > maximum)
            throw new FloraAppearanceValidationException("Invalid flora geometry scalar.");
    }
    internal static void Colour(FloraAppearanceColour value)
    {
        if (!float.IsFinite(value.R) || !float.IsFinite(value.G) || !float.IsFinite(value.B) ||
            value.R is < 0 or > 1 || value.G is < 0 or > 1 || value.B is < 0 or > 1)
            throw new FloraAppearanceValidationException("Invalid linear flora colour.");
    }
}
