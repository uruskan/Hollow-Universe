using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

internal sealed record FormationNativeLimits
{
    internal int MaxTextures { get; init; } = 64;
    internal int MaxMaterials { get; init; } = 128;
    internal int MaxSurfaces { get; init; } = 128;
    internal long MaxNativeBytes { get; init; } = 1073741824;
    internal long MaxUploadBytes { get; init; } = 536870912;
    internal long MaxCustom0Bytes { get; init; } = 16777216;
}

// Explicit approval of a primitive's authored PBR data. Nothing infers approval from names, colors or valid files.
// Soil requires a separate future reviewed handler and per-piece overrides; it is not a StandardPbr fallback.
internal sealed record FormationMaterialApproval(string GeometryKey, int GeometryRevision, string LodKey, int PrimitiveOrdinal);
internal sealed record FormationVisualLodSelection(string GeometryKey, int GeometryRevision, string NearLodKey, string FarLodKey);
internal readonly record struct FormationVisualSurfaceSlot(string GeometryKey, int GeometryRevision, string LodKey,
    int PrimitiveOrdinal, int PartIndex, int SurfaceIndex);
internal sealed record FormationVisualCustom0Binding(string GeometryKey, int GeometryRevision, string LodKey,
    int PrimitiveOrdinal, string BindingIdentity, string ContentSha256, FormationVisualArray<float> Values);

/// <summary>
/// Root-authenticated, immutable float4 mask streams. This owner borrows the CPU arrays: their producer reserves
/// and retains them until session retirement. Profile identity includes binding/content identities and float counts;
/// native preflight verifies exact float bits, vertex alignment and ranges before any upload copy is allocated.
/// </summary>
internal sealed class FormationVisualCustom0Profile
{
    internal FormationVisualCustom0Profile(IReadOnlyList<FormationVisualCustom0Binding> bindings, string? expectedVisualReceiptSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        VisualReceiptSha256 = expectedVisualReceiptSha256 == null ? null : FormationVisualLoader.Digest(expectedVisualReceiptSha256);
        if (bindings.Count is < 1 or > 128) throw new FormationCapacityException("Custom0 binding count exceeded.");
        var copied = bindings.ToArray();
        foreach (FormationVisualCustom0Binding b in copied)
        {
            ArgumentNullException.ThrowIfNull(b); ArgumentNullException.ThrowIfNull(b.Values);
            if (string.IsNullOrWhiteSpace(b.GeometryKey) || b.GeometryKey.Length > 128 || b.GeometryKey.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(b.LodKey) || b.LodKey.Length > 128 || b.LodKey.Any(char.IsControl) ||
                b.GeometryRevision < 1 || b.PrimitiveOrdinal is < 0 or > 31 || b.Values.Count is < 4 or > 1048576 || b.Values.Count % 4 != 0)
                throw new FormationValidationException("Invalid custom0 primitive binding.");
            _ = FormationVisualLoader.Digest(b.BindingIdentity); _ = FormationVisualLoader.Digest(b.ContentSha256);
        }
        copied = copied.OrderBy(b => b.GeometryKey, StringComparer.Ordinal).ThenBy(b => b.GeometryRevision)
            .ThenBy(b => b.LodKey, StringComparer.Ordinal).ThenBy(b => b.PrimitiveOrdinal).ToArray();
        if (copied.Select(b => (b.GeometryKey, b.GeometryRevision, b.LodKey, b.PrimitiveOrdinal)).Distinct().Count() != copied.Length)
            throw new FormationValidationException("Duplicate custom0 primitive binding.");
        Bindings = Array.AsReadOnly(copied);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("formation-custom0-rgba-float-v2"); writer.Write(VisualReceiptSha256 ?? ""); writer.Write(copied.Length);
        foreach (FormationVisualCustom0Binding b in copied)
        {
            writer.Write(b.GeometryKey); writer.Write(b.GeometryRevision); writer.Write(b.LodKey); writer.Write(b.PrimitiveOrdinal);
            writer.Write(FormationVisualLoader.Digest(b.BindingIdentity)); writer.Write(FormationVisualLoader.Digest(b.ContentSha256));
            writer.Write(b.Values.Count);
        }
        writer.Flush(); Identity = Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
    internal string Identity { get; }
    internal string? VisualReceiptSha256 { get; }
    internal IReadOnlyList<FormationVisualCustom0Binding> Bindings { get; }

    /// <summary>
    /// Adapts the root's authenticated additive payload without copying its arrays. The caller must already hold
    /// its CPU reservation and keep it charged until this profile/session retires. Receipt pins are external inputs;
    /// neither loading nor this adapter approves materials, appearance or physical capabilities.
    /// </summary>
    internal static FormationVisualCustom0Profile FromAuthenticatedAppearance(FormationAppearanceData appearance,
        string expectedAppearanceReceiptSha256, string expectedVisualReceiptSha256)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        string receipt = FormationVisualLoader.Digest(expectedAppearanceReceiptSha256);
        if (FormationVisualLoader.Digest(appearance.ReceiptSha256) != receipt ||
            FormationVisualLoader.Digest(appearance.VisualReceiptSha256) != FormationVisualLoader.Digest(expectedVisualReceiptSha256))
            throw new FormationValidationException("Additive appearance differs from its independent receipt bindings.");
        if (appearance.Lods.Count is < 1 or > 16) throw new FormationCapacityException("Additive appearance LOD count exceeded.");
        int count = 0; long bytes = 0;
        foreach (FormationAppearanceLod lod in appearance.Lods)
            foreach (FormationAppearancePrimitive primitive in lod.Primitives)
            {
                if (++count > 128 || primitive.Values.Count is < 4 or > 1048576 || primitive.Values.Count % 4 != 0)
                    throw new FormationCapacityException("Additive appearance primitive count exceeded.");
                bytes = checked(bytes + 4L * primitive.Values.Count);
                if (bytes > 16777216) throw new FormationCapacityException("Additive appearance float budget exceeded.");
            }
        var bindings = new List<FormationVisualCustom0Binding>(count);
        foreach (FormationAppearanceLod lod in appearance.Lods)
            foreach (FormationAppearancePrimitive primitive in lod.Primitives)
                bindings.Add(new(lod.GeometryKey, lod.GeometryRevision, lod.LodKey, primitive.Ordinal, receipt,
                    FormationVisualLoader.Custom0Digest(primitive.Values), primitive.Values));
        return new FormationVisualCustom0Profile(bindings, expectedVisualReceiptSha256);
    }
}

/// <summary>
/// Immutable explicit standard-PBR subset approval. Its computed Identity must be pinned independently by the host.
/// Its normal-map policy explicitly accepts Godot's positive-Z reconstruction from RG and renormalized mips;
/// this is an approved rendering approximation, not preservation of arbitrary authored RGB normals.
/// Slot metadata is the future per-piece/per-view override seam: consumers bind their own materials to instances,
/// register their roots with the lease, and never change shared ArrayMesh surface materials.
/// </summary>
internal sealed class FormationMaterialProfile
{
    internal FormationMaterialProfile(string approvedReceiptSha256, IReadOnlyList<FormationMaterialApproval> approvals,
        IReadOnlyList<FormationVisualLodSelection> lods, FormationVisualCustom0Profile? custom0 = null)
    {
        ApprovedReceiptSha256 = FormationVisualLoader.Digest(approvedReceiptSha256);
        if (custom0?.VisualReceiptSha256 != null && custom0.VisualReceiptSha256 != ApprovedReceiptSha256)
            throw new FormationValidationException("Additive appearance was authenticated for a different visual receipt.");
        ArgumentNullException.ThrowIfNull(approvals); ArgumentNullException.ThrowIfNull(lods);
        if (approvals.Count is < 1 or > 128 || lods.Count is < 1 or > 8)
            throw new FormationCapacityException("Formation material approval count exceeds its finite profile.");
        FormationMaterialApproval[] copied = approvals.ToArray();
        FormationVisualLodSelection[] selections = lods.ToArray();
        foreach (FormationMaterialApproval approval in copied)
        {
            ArgumentNullException.ThrowIfNull(approval);
            Text(approval.GeometryKey); Text(approval.LodKey);
            if (approval.GeometryRevision < 1 || approval.PrimitiveOrdinal is < 0 or > 31)
                throw new FormationValidationException("Invalid primitive material approval.");
        }
        foreach (FormationVisualLodSelection selection in selections)
        {
            ArgumentNullException.ThrowIfNull(selection);
            Text(selection.GeometryKey); Text(selection.NearLodKey); Text(selection.FarLodKey);
            if (selection.GeometryRevision < 1 || selection.NearLodKey == selection.FarLodKey)
                throw new FormationValidationException("Invalid approved near/far selection.");
        }
        copied = copied.OrderBy(a => a.GeometryKey, StringComparer.Ordinal).ThenBy(a => a.GeometryRevision)
            .ThenBy(a => a.LodKey, StringComparer.Ordinal).ThenBy(a => a.PrimitiveOrdinal).ToArray();
        selections = selections.OrderBy(a => a.GeometryKey, StringComparer.Ordinal).ThenBy(a => a.GeometryRevision).ToArray();
        if (copied.Distinct().Count() != copied.Length ||
            selections.Select(a => (a.GeometryKey, a.GeometryRevision)).Distinct().Count() != selections.Length)
            throw new FormationValidationException("Duplicate formation material approval or LOD selection.");
        Approvals = Array.AsReadOnly(copied); Lods = Array.AsReadOnly(selections); Custom0 = custom0;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("formation-standard-pbr-linear-mips-positive-z-normal-rg-v2"); writer.Write(ApprovedReceiptSha256);
        writer.Write(copied.Length);
        foreach (FormationMaterialApproval a in copied)
        { writer.Write(a.GeometryKey); writer.Write(a.GeometryRevision); writer.Write(a.LodKey); writer.Write(a.PrimitiveOrdinal); }
        writer.Write(selections.Length);
        foreach (FormationVisualLodSelection a in selections)
        { writer.Write(a.GeometryKey); writer.Write(a.GeometryRevision); writer.Write(a.NearLodKey); writer.Write(a.FarLodKey); }
        writer.Write(custom0?.Identity ?? "");
        writer.Flush(); Identity = Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
    internal string ApprovedReceiptSha256 { get; }
    internal string Identity { get; }
    internal IReadOnlyList<FormationMaterialApproval> Approvals { get; }
    internal IReadOnlyList<FormationVisualLodSelection> Lods { get; }
    internal FormationVisualCustom0Profile? Custom0 { get; }
    private static void Text(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
            throw new FormationValidationException("Invalid formation approval key.");
    }
}

/// <summary>
/// Inactive private native upload from authenticated CPU inputs. No file imports, mesh/texture readback, generation,
/// global shader mutation or physics permission is performed. Caller reserves CPU/native/upload allowances first.
/// Authenticated float32 inputs are retained exactly; Godot's internal normal/tangent packing is not bit-exact storage.
/// </summary>
internal static class FormationVisualLoader
{
    internal readonly record struct TextureVariant(int Image, bool NormalMips);
    internal readonly record struct MaterialAddress(int Lod, int Material);
    internal sealed class NativePlan
    {
        internal NativePlan(FormationPackage package, FormationVisualData data, FormationMaterialProfile profile,
            TextureVariant[] textures, MaterialAddress[] materials, Dictionary<(int Lod, int Primitive), FormationVisualArray<float>> custom0,
            long nativeBytes, long uploadBytes, long textureBytes, long custom0Bytes)
        {
            Package = package; Data = data; Profile = profile; Textures = textures; Materials = materials;
            Custom0 = custom0; Custom0Bytes = custom0Bytes;
            NativeReservationBytes = nativeBytes; UploadReservationBytes = uploadBytes; TextureBytes = textureBytes;
        }
        internal FormationPackage Package { get; }
        internal FormationVisualData Data { get; }
        internal FormationMaterialProfile Profile { get; }
        internal TextureVariant[] Textures { get; }
        internal MaterialAddress[] Materials { get; }
        internal IReadOnlyDictionary<(int Lod, int Primitive), FormationVisualArray<float>> Custom0 { get; }
        internal long Custom0Bytes { get; }
        internal long NativeReservationBytes { get; }
        internal long UploadReservationBytes { get; }
        internal long TextureBytes { get; }
    }

    internal static NativePlan Preflight(FormationPackage package, FormationVisualData data,
        FormationMaterialProfile profile, FormationNativeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxTextures is < 1 or > 64 || limits.MaxMaterials is < 1 or > 128 || limits.MaxSurfaces is < 1 or > 128 ||
            limits.MaxNativeBytes is < 1 or > 8589934592L || limits.MaxUploadBytes is < 1 or > 2147483648L ||
            limits.MaxCustom0Bytes is < 0 or > 16777216)
            throw new FormationCapacityException("Invalid native formation limits.");
        if (Digest(package.ReceiptSha256) != Digest(data.ReceiptSha256) || Digest(data.ReceiptSha256) != profile.ApprovedReceiptSha256)
            throw new FormationValidationException("Native formation data/profile receipt differs from the qualified package.");
        if (profile.Custom0?.VisualReceiptSha256 != null && profile.Custom0.VisualReceiptSha256 != Digest(data.ReceiptSha256))
            throw new FormationValidationException("Additive appearance was authenticated for different visual inputs.");
        if (data.Lods.Count != profile.Lods.Count * 2 || data.Lods.Count > 16)
            throw new FormationValidationException("Native profile requires exactly its explicitly selected near/far LODs.");
        var selected = new HashSet<(string Key, int Revision, string Lod)>();
        foreach (FormationVisualLodSelection selection in profile.Lods)
        {
            CliffSupportDefinition definition = package.Catalog.RequireQualified(selection.GeometryKey, selection.GeometryRevision);
            RequireLod(definition, selection.NearLodKey); RequireLod(definition, selection.FarLodKey);
            selected.Add((selection.GeometryKey, selection.GeometryRevision, selection.NearLodKey));
            selected.Add((selection.GeometryKey, selection.GeometryRevision, selection.FarLodKey));
        }
        var approvals = new HashSet<FormationMaterialApproval>(profile.Approvals);
        var variants = new HashSet<TextureVariant>();
        var materials = new HashSet<MaterialAddress>();
        var customBindings = (profile.Custom0?.Bindings ?? Array.Empty<FormationVisualCustom0Binding>()).ToDictionary(
            b => (b.GeometryKey, b.GeometryRevision, b.LodKey, b.PrimitiveOrdinal));
        var custom0 = new Dictionary<(int Lod, int Primitive), FormationVisualArray<float>>();
        long custom0Bytes = 0;
        long largestPrimitive = 0, largestEncoded = 0, largestMip = 0, nativeTextures = 0;
        int surfaces = 0;
        for (int i = 0; i < data.Lods.Count; i++)
        {
            FormationVisualLod lod = data.Lods[i];
            if (!selected.Remove((lod.GeometryKey, lod.GeometryRevision, lod.LodKey)))
                throw new FormationValidationException("Duplicate or unapproved native LOD.");
            foreach (FormationVisualPrimitive primitive in lod.Primitives)
            {
                if (!approvals.Remove(new(lod.GeometryKey, lod.GeometryRevision, lod.LodKey, primitive.Ordinal)))
                    throw new FormationValidationException("Primitive lacks explicit material approval.");
                if (++surfaces > limits.MaxSurfaces) throw new FormationCapacityException("Native surface budget exceeded.");
                FormationVisualMaterial material = lod.Materials[primitive.MaterialIndex];
                ValidateMaterial(material);
                materials.Add(new(i, primitive.MaterialIndex));
                foreach ((FormationVisualTexture? texture, bool normal) in TextureRoles(material))
                    if (texture != null) variants.Add(new(texture.ImageIndex, normal));
                long bytes = checked(4L * (primitive.Positions.Count + primitive.Normals.Count + primitive.Tangents.Count +
                    primitive.TexCoords.Count + primitive.Indices.Count));
                if (customBindings.Remove((lod.GeometryKey, lod.GeometryRevision, lod.LodKey, primitive.Ordinal), out FormationVisualCustom0Binding? mask))
                {
                    if (mask.Values.Count != checked(primitive.VertexCount * 4))
                        throw new FormationValidationException("Custom0 count differs from authored primitive vertex order.");
                    long maskBytes = checked(mask.Values.Count * 4L); custom0Bytes = checked(custom0Bytes + maskBytes);
                    if (custom0Bytes > limits.MaxCustom0Bytes) throw new FormationCapacityException("Custom0 aggregate float budget exceeded.");
                    ValidateCustom0(mask); custom0.Add((i, primitive.Ordinal), mask.Values); bytes = checked(bytes + maskBytes);
                }
                largestPrimitive = Math.Max(largestPrimitive, bytes);
            }
        }
        if (selected.Count != 0 || approvals.Count != 0 || customBindings.Count != 0)
            throw new FormationValidationException("Native profile contains unused LOD/material approvals.");
        if (variants.Count > limits.MaxTextures || materials.Count > limits.MaxMaterials)
            throw new FormationCapacityException("Native texture variant/material budget exceeded.");
        foreach (TextureVariant variant in variants)
        {
            FormationVisualImage image = data.Images[variant.Image];
            nativeTextures = checked(nativeTextures + image.DecodedMipBytes);
            largestEncoded = Math.Max(largestEncoded, image.Bytes.Count); largestMip = Math.Max(largestMip, image.DecodedMipBytes);
        }
        // Explicit conservative allowances include renderer metadata and interop. These are not measured driver bounds.
        long native = checked(nativeTextures + 2 * (data.Sizing.StreamBytes + custom0Bytes) + 16384L * materials.Count + 65536L * data.Lods.Count + 16777216);
        long upload = checked(3 * largestMip + 2 * largestEncoded + 4 * largestPrimitive + 1048576);
        if (native > limits.MaxNativeBytes || upload > limits.MaxUploadBytes)
            throw new FormationCapacityException("Native formation preparation/resident reservation exceeds admission.");
        return new NativePlan(package, data, profile, variants.OrderBy(v => v.Image).ThenBy(v => v.NormalMips).ToArray(),
            materials.OrderBy(m => m.Lod).ThenBy(m => m.Material).ToArray(), custom0, native, upload, nativeTextures, custom0Bytes);
    }

    private static void ValidateCustom0(FormationVisualCustom0Binding binding)
    {
        if (Custom0Digest(binding.Values) != Digest(binding.ContentSha256))
            throw new FormationValidationException("Custom0 bits differ from the independently bound stream digest.");
    }

    internal static string Custom0Digest(FormationVisualArray<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is < 4 or > 1048576 || values.Count % 4 != 0)
            throw new FormationCapacityException("Custom0 float count exceeded.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[4096]; int used = 0;
        for (int i = 0; i < values.Count; i += 4)
        {
            if ((double)values[i + 2] + values[i + 3] > 1)
                throw new FormationValidationException("Custom0 cap plus toe mix exceeds one.");
            for (int channel = 0; channel < 4; channel++)
            {
                float value = values[i + channel];
                if (!float.IsFinite(value) || value < 0 || value > 1)
                    throw new FormationValidationException("Custom0 mask component is outside zero to one.");
                BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(used, 4), BitConverter.SingleToInt32Bits(value)); used += 4;
            }
            if (used == buffer.Length) { hash.AppendData(buffer); used = 0; }
        }
        if (used != 0) hash.AppendData(buffer[..used]);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void ValidateMaterial(FormationVisualMaterial material)
    {
        if (material.AlphaMode != "OPAQUE" && (material.AlphaMode != "MASK" || material.AlphaCutoff <= 0))
            throw new FormationValidationException("Approved native PBR subset requires opaque or nonzero-cutoff masked alpha.");
        if (material.Occlusion != null && material.Occlusion.Factor != 1)
            throw new FormationValidationException("Non-default AO strength needs a separately reviewed material handler.");
        int? wrap = null;
        foreach ((FormationVisualTexture? texture, _) in TextureRoles(material))
        {
            if (texture == null) continue;
            FormationVisualSampler sampler = texture.Sampler;
            if (texture.TexCoord != 0 || sampler.MagFilter is not (0 or 9729) || sampler.MinFilter is not (0 or 9987) ||
                sampler.WrapS != sampler.WrapT || sampler.WrapS is not (33071 or 10497) ||
                wrap.HasValue && wrap.Value != sampler.WrapS)
                throw new FormationValidationException("PBR subset cannot faithfully represent this texture sampler combination.");
            wrap = sampler.WrapS;
        }
    }

    private static IEnumerable<(FormationVisualTexture? Texture, bool Normal)> TextureRoles(FormationVisualMaterial material)
    {
        yield return (material.BaseColour, false); yield return (material.MetallicRoughness, false);
        yield return (material.Normal, true); yield return (material.Occlusion, false); yield return (material.Emissive, false);
    }

    internal static UploadJob BeginUpload(NativePlan plan, CancellationToken cancellation = default)
    { MainThread(); ArgumentNullException.ThrowIfNull(plan); cancellation.ThrowIfCancellationRequested(); return new(plan, cancellation); }

    internal sealed class UploadJob : IDisposable
    {
        private NativePlan? _plan;
        private readonly CancellationToken _cancellation;
        private readonly Retirement _resources = new();
        private readonly Dictionary<TextureVariant, ImageTexture> _textures = new();
        private readonly Dictionary<MaterialAddress, StandardMaterial3D> _materials = new();
        private readonly Dictionary<int, ArrayMesh> _meshes = new();
        private readonly List<FormationVisualSurfaceSlot> _slots = new();
        private int _texture, _material, _lod, _primitive;
        private bool _cancelled, _taken, _disposed;
        private Owner? _owner;
        internal UploadJob(NativePlan plan, CancellationToken cancellation) { _plan = plan; _cancellation = cancellation; }
        internal Exception? Failure { get; private set; }
        internal bool IsCompleted => _owner != null && Failure == null && !_disposed;
        internal bool IsFinished => IsCompleted || Failure != null || _disposed || _taken;
        internal int OwnedResourceCount { get { MainThread(); return _taken ? 0 : _resources.Count; } }

        internal void Pump(int maxSteps = 1)
        {
            MainThread();
            if (maxSteps is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maxSteps));
            if (IsFinished) return;
            try
            {
                for (int step = 0; step < maxSteps && !IsFinished; step++)
                {
                    CheckCancellation(); NativePlan plan = _plan!;
                    if (_texture < plan.Textures.Length) UploadTexture(plan.Textures[_texture++]);
                    else if (_material < plan.Materials.Length) UploadMaterial(plan.Materials[_material++]);
                    else if (_lod < plan.Data.Lods.Count) UploadSurface();
                    else Publish();
                    CheckCancellation();
                }
            }
            catch (Exception error) { Failure = error; }
        }

        internal Owner TakeCompletedOwner()
        {
            MainThread();
            if (!IsCompleted || _taken) throw new InvalidOperationException("Native formation owner is not available for transfer.");
            Owner owner = _owner!; _owner = null; _taken = true; _plan = null;
            _textures.Clear(); _materials.Clear(); _meshes.Clear(); _slots.Clear();
            return owner;
        }

        internal void Cancel() { MainThread(); _cancelled = true; }
        internal bool TryDispose(out Exception? failure)
        {
            MainThread(); _cancelled = true; _plan = null;
            _textures.Clear(); _materials.Clear(); _meshes.Clear(); _slots.Clear();
            if (_taken) { _disposed = true; failure = null; return true; }
            bool done = _owner != null ? _owner.TryDispose(out failure) : _resources.TryDispose(out failure);
            if (done) { _owner = null; _disposed = true; }
            return done;
        }
        public void Dispose() { if (!TryDispose(out Exception? error)) throw error!; }

        private void UploadTexture(TextureVariant key)
        {
            FormationVisualImage source = _plan!.Data.Images[key.Image];
            byte[] encoded = new byte[source.Bytes.Count]; source.Bytes.CopyTo(encoded);
            Image image = _resources.Own(new Image());
            Error error = source.MimeType == "image/png" ? image.LoadPngFromBuffer(encoded) : image.LoadJpgFromBuffer(encoded);
            if (error != Error.Ok || image.GetWidth() != source.Width || image.GetHeight() != source.Height)
                throw new FormationValidationException("Authenticated image failed native decoding/header agreement.");
            CheckCancellation(); image.Convert(Image.Format.Rgba8);
            if (key.NormalMips) ValidateNormalPixels(image);
            if (image.GenerateMipmaps(key.NormalMips) != Error.Ok || image.GetDataSize() != source.DecodedMipBytes ||
                image.GetMipmapCount() + 1 != MipCount(source.Width, source.Height))
                throw new FormationValidationException("Native complete RGBA8 mip chain differs from admission.");
            CheckCancellation();
            ImageTexture texture = _resources.Own(new ImageTexture()); texture.SetImage(image);
            if (texture.GetWidth() != source.Width || texture.GetHeight() != source.Height)
                throw new FormationValidationException("Native texture upload dimensions differ.");
            _textures.Add(key, texture); _resources.Release(image);
        }

        private void ValidateNormalPixels(Image image)
        {
            // This is a bounded copy of the just-decoded CPU image, never a GPU texture readback. It is covered by
            // the upload allowance. Positive-Z unit normals permit RG reconstruction within RGBA8 quantization;
            // interpolation and normal-strength behavior remain the profile's explicit Godot approximation.
            byte[] pixels = image.GetData();
            if (pixels.LongLength != checked(4L * image.GetWidth() * image.GetHeight()))
                throw new FormationValidationException("Normal compatibility requires a base-only RGBA8 decode.");
            const double tolerance = 2.0 / 255.0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if ((i & 16383) == 0) CheckCancellation();
                double x = pixels[i] * (2.0 / 255.0) - 1, y = pixels[i + 1] * (2.0 / 255.0) - 1;
                double z = pixels[i + 2] * (2.0 / 255.0) - 1, xy = x * x + y * y;
                if (z < 0 || xy > 1 + tolerance || Math.Abs(z - Math.Sqrt(Math.Max(0, 1 - xy))) > tolerance)
                    throw new FormationValidationException("Authored normal pixels are outside the approved positive-Z RG reconstruction profile.");
            }
        }

        private void UploadMaterial(MaterialAddress address)
        {
            FormationVisualMaterial source = _plan!.Data.Lods[address.Lod].Materials[address.Material];
            StandardMaterial3D material = _resources.Own(new StandardMaterial3D());
            var colour = source.BaseColourFactor;
            material.AlbedoColor = new Color(colour.X, colour.Y, colour.Z, colour.W).LinearToSrgb();
            material.Metallic = source.MetallicFactor; material.Roughness = source.RoughnessFactor;
            material.CullMode = source.DoubleSided ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back;
            material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
            FormationVisualTexture? first = TextureRoles(source).Select(r => r.Texture).FirstOrDefault(t => t != null);
            material.TextureRepeat = first == null || first.Sampler.WrapS == 10497;
            material.Transparency = source.AlphaMode == "MASK" ? BaseMaterial3D.TransparencyEnum.AlphaScissor : BaseMaterial3D.TransparencyEnum.Disabled;
            if (source.AlphaMode == "MASK") material.AlphaScissorThreshold = source.AlphaCutoff;
            material.AlbedoTexture = Texture(source.BaseColour, false);
            material.MetallicTexture = Texture(source.MetallicRoughness, false);
            material.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Blue;
            material.RoughnessTexture = Texture(source.MetallicRoughness, false);
            material.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green;
            material.NormalEnabled = source.Normal != null;
            if (source.Normal != null) { material.NormalTexture = Texture(source.Normal, true); material.NormalScale = source.Normal.Factor; }
            material.AOEnabled = source.Occlusion != null;
            if (source.Occlusion != null) { material.AOTexture = Texture(source.Occlusion, false); material.AOTextureChannel = BaseMaterial3D.TextureChannel.Red; }
            var emission = source.EmissiveFactor;
            material.EmissionEnabled = source.Emissive != null || emission != System.Numerics.Vector3.Zero;
            material.Emission = new Color(emission.X, emission.Y, emission.Z).LinearToSrgb();
            material.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
            material.EmissionEnergyMultiplier = 1; material.EmissionTexture = Texture(source.Emissive, false);
            _materials.Add(address, material);
        }

        private ImageTexture? Texture(FormationVisualTexture? binding, bool normal) =>
            binding == null ? null : _textures[new TextureVariant(binding.ImageIndex, normal)];

        private void UploadSurface()
        {
            FormationVisualLod lod = _plan!.Data.Lods[_lod]; FormationVisualPrimitive source = lod.Primitives[_primitive];
            if (!_meshes.TryGetValue(_lod, out ArrayMesh? mesh))
            { mesh = _resources.Own(new ArrayMesh()); _meshes.Add(_lod, mesh); }
            var positions = new Vector3[source.VertexCount]; var normals = new Vector3[source.VertexCount];
            for (int i = 0; i < source.VertexCount; i++)
            {
                if ((i & 1023) == 0) CheckCancellation();
                positions[i] = new(source.Positions[i * 3], source.Positions[i * 3 + 1], source.Positions[i * 3 + 2]);
                normals[i] = new(source.Normals[i * 3], source.Normals[i * 3 + 1], source.Normals[i * 3 + 2]);
            }
            int[] indices = new int[source.Indices.Count]; source.Indices.CopyTo(indices);
            for (int i = 0; i < indices.Length; i += 3)
            { if ((i & 3071) == 0) CheckCancellation(); (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]); }
            Godot.Collections.Array arrays = _resources.Own(new Godot.Collections.Array()); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions; arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.Index] = indices;
            if (source.HasTangents)
            {
                var tangents = new float[source.Tangents.Count]; source.Tangents.CopyTo(tangents);
                arrays[(int)Mesh.ArrayType.Tangent] = tangents;
            }
            if (source.HasTexCoords)
            {
                var uv = new Vector2[source.VertexCount];
                for (int i = 0; i < uv.Length; i++) uv[i] = new(source.TexCoords[i * 2], source.TexCoords[i * 2 + 1]);
                arrays[(int)Mesh.ArrayType.TexUV] = uv;
            }
            Mesh.ArrayFormat flags = 0;
            if (_plan.Custom0.TryGetValue((_lod, source.Ordinal), out FormationVisualArray<float>? masks))
            {
                float[] custom = new float[masks.Count]; masks.CopyTo(custom); arrays[(int)Mesh.ArrayType.Custom0] = custom;
                flags = (Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
            }
            CheckCancellation(); int surface = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
            if (mesh.GetSurfaceCount() != surface + 1 || mesh.SurfaceGetArrayIndexLen(surface) != indices.Length ||
                mesh.SurfaceGetArrayLen(surface) != source.VertexCount)
                throw new FormationValidationException("Native surface upload metadata differs from authenticated streams.");
            mesh.SurfaceSetMaterial(surface, _materials[new MaterialAddress(_lod, source.MaterialIndex)]);
            _slots.Add(new(lod.GeometryKey, lod.GeometryRevision, lod.LodKey, source.Ordinal, 0, surface));
            _resources.Release(arrays);
            if (++_primitive == lod.Primitives.Count) { _primitive = 0; _lod++; }
        }

        private void Publish()
        {
            NativePlan plan = _plan!; var templates = new List<FormationVisualTemplate>();
            foreach (FormationVisualLodSelection selection in plan.Profile.Lods)
            {
                CliffSupportDefinition definition = plan.Package.Catalog.RequireQualified(selection.GeometryKey, selection.GeometryRevision);
                int near = FindLod(selection, selection.NearLodKey), far = FindLod(selection, selection.FarLodKey);
                templates.Add(new FormationVisualTemplate(definition, RequireLod(definition, selection.NearLodKey),
                    RequireLod(definition, selection.FarLodKey),
                    new[] { new FormationVisualTemplate.Part(_meshes[near], Transform3D.Identity) },
                    new[] { new FormationVisualTemplate.Part(_meshes[far], Transform3D.Identity) }));
            }
            _owner = new Owner(_resources, templates.ToArray(), _slots.ToArray());
        }
        private int FindLod(FormationVisualLodSelection selection, string key)
        {
            for (int i = 0; i < _plan!.Data.Lods.Count; i++)
            {
                FormationVisualLod lod = _plan.Data.Lods[i];
                if (lod.GeometryKey == selection.GeometryKey && lod.GeometryRevision == selection.GeometryRevision && lod.LodKey == key) return i;
            }
            throw new FormationValidationException("Approved native LOD disappeared.");
        }
        private void CheckCancellation()
        { if (_cancelled) throw new OperationCanceledException(); _cancellation.ThrowIfCancellationRequested(); }
    }

    internal sealed class Owner : IDisposable
    {
        private readonly Retirement _resources;
        private readonly IReadOnlyList<FormationVisualTemplate> _templates;
        private readonly IReadOnlyList<FormationVisualSurfaceSlot> _slots;
        private bool _retiring;
        internal Owner(Retirement resources, FormationVisualTemplate[] templates, FormationVisualSurfaceSlot[] slots)
        { _resources = resources; _templates = Array.AsReadOnly(templates); _slots = Array.AsReadOnly(slots); }
        internal IReadOnlyList<FormationVisualTemplate> Templates { get { Alive(); return _templates; } }
        internal IReadOnlyList<FormationVisualSurfaceSlot> SurfaceSlots { get { Alive(); return _slots; } }
        internal int OwnedResourceCount { get { MainThread(); return _resources.Count; } }
        internal bool TryDispose(out Exception? failure) { MainThread(); _retiring = true; return _resources.TryDispose(out failure); }
        public void Dispose() { if (!TryDispose(out Exception? error)) throw error!; }
        private void Alive() { MainThread(); if (_retiring) throw new ObjectDisposedException(nameof(Owner)); }
    }

    internal sealed class Retirement
    {
        private readonly List<IDisposable> _owners = new();
        internal int Count => _owners.Count;
        internal T Own<T>(T owner) where T : IDisposable { _owners.Add(owner); return owner; }
        internal void Release(IDisposable owner) { owner.Dispose(); _owners.Remove(owner); }
        internal bool TryDispose(out Exception? failure)
        {
            MainThread(); List<Exception>? errors = null;
            for (int i = _owners.Count - 1; i >= 0; i--)
                try { _owners[i].Dispose(); _owners.RemoveAt(i); }
                catch (Exception error) { (errors ??= new()).Add(error); }
            failure = errors == null ? null : new AggregateException("Formation native cleanup retains failed owners.", errors);
            return failure == null;
        }
    }

    internal static string Digest(string value)
    {
        if (value == null || value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
            throw new FormationValidationException("Expected an independent SHA-256 identity.");
        return value.ToUpperInvariant();
    }
    private static CliffLodBinding RequireLod(CliffSupportDefinition definition, string key)
    {
        CliffLodBinding? found = null;
        foreach (CliffLodBinding lod in definition.Lods)
        {
            if (lod.Key != key) continue;
            if (found != null) throw new FormationValidationException("Duplicate approved qualified LOD.");
            found = lod;
        }
        return found ?? throw new FormationValidationException("Missing approved qualified LOD.");
    }
    private static int MipCount(int width, int height)
    { int levels = 1; while (width > 1 || height > 1) { width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); levels++; } return levels; }
    private static void MainThread()
    { if (OS.GetThreadCallerId() != OS.GetMainThreadId()) throw new InvalidOperationException("Formation native ownership belongs to the main thread."); }
}
