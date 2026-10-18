using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Hollow.Surface;

/// <summary>Owned authenticated bytes; no array/memory accessor permits callers to mutate the shared payload.</summary>
public sealed class FloraAppearanceImage
{
    private readonly byte[] _bytes;
    internal FloraAppearanceImage(FloraAppearanceImageLayout layout, string sha256, byte[] ownedBytes)
    { Layout = layout; Sha256 = sha256; _bytes = ownedBytes; }
    public FloraAppearanceImageLayout Layout { get; }
    public string Sha256 { get; }
    public int ByteLength => _bytes.Length;
    /// <summary>The destination is caller-owned upload staging and must be separately budgeted by the renderer.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length != _bytes.Length) throw new ArgumentException("Destination must match the complete mip byte count.", nameof(destination));
        _bytes.AsSpan().CopyTo(destination);
    }
    public byte ByteAt(int offset) => _bytes[offset];
}

/// <summary>
/// All-or-nothing immutable CPU package. Contains no paths, streams, world references, materials or native resources.
/// Its owner accounts for PayloadBytes until all consumers release it; this type is not a cache or lifetime manager.
/// </summary>
public sealed class FloraAppearancePackage
{
    internal FloraAppearancePackage(FloraAppearancePin pin, FloraAppearanceColour bladeMean, FloraAppearanceImage[] images)
    { Pin = pin; BladeMean = bladeMean; Images = Array.AsReadOnly(images); }
    public FloraAppearancePin Pin { get; }
    public FloraAppearanceInputContract Inputs => Pin.ExpectedInputs;
    public FloraAppearanceColour BladeMean { get; }
    public IReadOnlyList<FloraAppearanceImage> Images { get; }
    public long PayloadBytes => FloraAppearanceSchema.PayloadBytes;
}

/// <summary>
/// Authenticates the same bytes consumers later copy. The independent pin is mandatory; no self-pinning overload,
/// fallback, bake, decompression, image decode or native resource creation exists. Uses seven fixed leaf paths and
/// rejects links/reparse points at the package root, its ancestors and each file. Packages must be immutable during
/// admission; filesystem link checks are not an OS-level race-proof directory sandbox. Independent hashes still gate
/// every returned byte. Concurrent-load, cache and native/staging budgets remain the owning session's responsibility.
/// </summary>
public static class FloraAppearancePackageLoader
{
    public static FloraAppearancePackage Load(string packageDirectory, FloraAppearancePin expected,
        FloraAppearanceLimits limits, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(limits);
        cancellation.ThrowIfCancellationRequested();
        try { return LoadCore(packageDirectory, expected, limits, cancellation); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new FloraAppearanceValidationException("Cannot read the complete flora appearance package.", error); }
    }

    private static FloraAppearancePackage LoadCore(string directory, FloraAppearancePin expected,
        FloraAppearanceLimits limits, CancellationToken cancellation)
    {
        string root = Path.GetFullPath(directory);
        ValidateRoot(root);
        string manifestPath = FixedPath(root, FloraAppearanceManifest.FileName);
        long manifestLength = new FileInfo(manifestPath).Length;
        if (manifestLength < 1 || manifestLength > limits.MaxManifestBytes)
            throw new FloraAppearanceCapacityException("Flora manifest byte bound exceeded.");
        if (manifestLength > limits.MaxTotalBytes - FloraAppearanceSchema.PayloadBytes)
            throw new FloraAppearanceCapacityException("Complete flora package exceeds total byte admission.");
        foreach (FloraAppearanceImageLayout layout in FloraAppearanceSchema.Images)
            if (layout.ByteLength > limits.MaxImageBytes)
                throw new FloraAppearanceCapacityException("Flora image byte bound exceeded.");

        byte[] manifest = ReadExact(root, FloraAppearanceManifest.FileName, checked((int)manifestLength), expected.ManifestSha256, cancellation);
        FloraAppearanceManifest.Decoded decoded = FloraAppearanceManifest.Decode(manifest, expected);
        var images = new FloraAppearanceImage[FloraAppearanceSchema.Images.Count];
        for (int i = 0; i < images.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            FloraAppearanceImageLayout layout = FloraAppearanceSchema.Images[i];
            string sha256 = decoded.Images[i].Sha256;
            byte[] bytes = ReadExact(root, layout.File, layout.ByteLength, sha256, cancellation);
            images[i] = new FloraAppearanceImage(layout, sha256, bytes);
        }
        cancellation.ThrowIfCancellationRequested();
        // No object/callback escapes while any manifest, image or cancellation check is still pending.
        return new FloraAppearancePackage(expected, decoded.BladeMean, images);
    }

    private static byte[] ReadExact(string root, string name, int length, string expectedSha256, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        ValidateRoot(root);
        string path = FixedPath(root, name);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        if (stream.Length != length) throw new FloraAppearanceValidationException("Flora file length differs from its complete authenticated layout: " + name);
        byte[] bytes = new byte[length];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int offset = 0;
        while (offset < bytes.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            int read = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (read == 0) throw new FloraAppearanceValidationException("Truncated flora payload: " + name);
            hash.AppendData(bytes, offset, read); offset += read;
        }
        cancellation.ThrowIfCancellationRequested();
        if (stream.ReadByte() != -1 || stream.Length != length)
            throw new FloraAppearanceValidationException("Trailing or concurrently changed flora file: " + name);
        if (Convert.ToHexString(hash.GetHashAndReset()) != expectedSha256)
            throw new FloraAppearanceValidationException("Flora SHA-256 mismatch: " + name);
        ValidateRoot(root); _ = FixedPath(root, name);
        return bytes;
    }

    private static void ValidateRoot(string root)
    {
        for (DirectoryInfo? current = new DirectoryInfo(root); current != null; current = current.Parent)
        {
            FileAttributes attributes = File.GetAttributes(current.FullName);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new FloraAppearanceValidationException("Flora package directories must be real directories, without links/reparse points.");
        }
    }

    private static string FixedPath(string root, string name)
    {
        // name is a schema constant, never a path accepted from JSON. Keep the assertion if future schemas expand.
        if (name != FloraAppearanceManifest.FileName && !IsPayloadName(name))
            throw new FloraAppearanceValidationException("Unknown flora package leaf filename.");
        string path = Path.GetFullPath(Path.Combine(root, name));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, comparison)) throw new FloraAppearanceValidationException("Flora path escaped its package root.");
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new FloraAppearanceValidationException("Flora payloads must be regular files, without links/reparse points.");
        return path;
    }

    private static bool IsPayloadName(string name)
    {
        foreach (FloraAppearanceImageLayout layout in FloraAppearanceSchema.Images) if (layout.File == name) return true;
        return false;
    }
}
