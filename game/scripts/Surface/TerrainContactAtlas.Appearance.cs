using System;
using System.Collections.Generic;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

internal sealed partial class TerrainContactAtlas
{
    // Optional copies of the CURRENT published grid's appearance inputs, never a separately sampled surface.
    // Each RGBAF layer has two 33-row bands. Material band 0 has packed bytes 0..11; band 1 has bytes 12..19
    // in three float24 integers plus local-water height. Normal bands hold fine xyz/UV2.x and morph xyz/UV2.y.
    private const int AppearanceLayerFloats = GridSide * GridSide * 8;
    private const int AppearanceLayerBytes = AppearanceLayerFloats * sizeof(float);
    private readonly bool _appearanceEnabled;
    private Texture2DArray? _appearanceMaterials, _appearanceNormals;
    private float[]? _appearanceFloats;
    private byte[]? _appearanceBytes;
    internal bool AppearanceEnabled => _appearanceEnabled;
    internal long AppearancePayloadBytes => _appearanceEnabled ? 2L * AppearanceLayerBytes * Capacity : 0;
    // Two reused managed stages and at most one upload Image. Does not claim a native-driver residency cap.
    internal long AppearanceStagingBytes => _appearanceEnabled ? 3L * AppearanceLayerBytes : 0;
    internal long AppearanceReservedBytes => AppearancePayloadBytes + AppearanceStagingBytes;

    private void CreateAppearance()
    {
        if (!_appearanceEnabled) return;
        if (AppearanceReservedBytes > 9L * 1024 * 1024)
            throw new InvalidOperationException("Drawn appearance exceeds its fixed nine-MiB payload/staging reservation.");
        _appearanceFloats = new float[AppearanceLayerFloats];
        _appearanceBytes = new byte[AppearanceLayerBytes];
        _appearanceMaterials = new Texture2DArray();
        _appearanceNormals = new Texture2DArray();
        using Image zero = Image.CreateFromData(GridSide, GridSide * 2, false, Image.Format.Rgbaf, _appearanceBytes);
        var images = new Godot.Collections.Array<Image>();
        using var storage = (Godot.Collections.Array)images;
        for (int i = 0; i < Capacity; i++) images.Add(zero);
        if (_appearanceMaterials.CreateFromImages(images) != Error.Ok || _appearanceNormals.CreateFromImages(images) != Error.Ok)
            throw new InvalidOperationException("Cannot allocate bounded drawn-terrain appearance arrays.");
    }

    // Explicit opt-in at construction and binding: existing contact-only consumers allocate no appearance data.
    internal void BindAppearance(ShaderMaterial material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_appearanceEnabled) throw new InvalidOperationException("This contact atlas was created without appearance storage.");
        Bind(material);
        material.SetShaderParameter("terrain_appearance_materials", _appearanceMaterials!);
        material.SetShaderParameter("terrain_appearance_normals", _appearanceNormals!);
        material.SetShaderParameter("terrain_appearance_enabled", true);
    }

    private static bool ValidAppearanceGrid(TileGrid grid)
    {
        const int count = GridSide * GridSide;
        if (BiomeTable.MaterialCount != 16 || grid.Materials.Length != count * 16 || grid.Climate.Length != count * 4
            || grid.Normals.Length != count * 3 || grid.MorphNormals.Length != count * 3
            || grid.FaceMetres.Length != count * 2 || grid.Heights.Length != count || grid.WaterLevels.Length != count) return false;
        for (int i = 0; i < count; i++)
        {
            if (!float.IsFinite(grid.FaceMetres[2 * i]) || !float.IsFinite(grid.FaceMetres[2 * i + 1])
                || !float.IsFinite(grid.Heights[i]) || !float.IsFinite(grid.WaterLevels[i])
                || !float.IsFinite(grid.Heights[i] - grid.WaterLevels[i])) return false;
            var a = new Vector3(grid.Normals[3 * i], grid.Normals[3 * i + 1], grid.Normals[3 * i + 2]);
            var b = new Vector3(grid.MorphNormals[3 * i], grid.MorphNormals[3 * i + 1], grid.MorphNormals[3 * i + 2]);
            if (!Finite(a) || !Finite(b) || !float.IsFinite(a.LengthSquared()) || !float.IsFinite(b.LengthSquared())) return false;
            // TileGrid documents unit fine normals; Godot's octahedral NORMAL stream assumes that domain.
            // Morph normals intentionally need not be unit. Reject a different fine-normal convention.
            if (Math.Abs(a.LengthSquared() - 1) > 0.0002f) return false;
            Vector3 difference = b - a;
            float length = difference.LengthSquared();
            if (!float.IsFinite(length)) return false;
            float k = length > 0 ? Math.Clamp(-a.Dot(difference) / length, 0, 1) : 0;
            if (!float.IsFinite(k) || (a + difference * k).LengthSquared() <= 1e-12f) return false;
            for (int c = 0; c < 3; c++)
                if (!float.IsFinite(grid.Positions[3 * i + c]) || !float.IsFinite(grid.MorphPositions[3 * i + c])) return false;
        }
        return true;

    }

    // TerrainLod's RenderFrame is a proper rotation. Do not silently claim normal parity for arbitrary scaled
    // or sheared caller-created snapshots: Godot's native normal transform is outside this admitted domain.
    private static bool ValidAppearanceTransform(Basis basis) => Math.Abs(basis.X.LengthSquared() - 1) < 0.0002f
        && Math.Abs(basis.Y.LengthSquared() - 1) < 0.0002f && Math.Abs(basis.Z.LengthSquared() - 1) < 0.0002f
        && Math.Abs(basis.X.Dot(basis.Y)) < 0.0002f && Math.Abs(basis.X.Dot(basis.Z)) < 0.0002f
        && Math.Abs(basis.Y.Dot(basis.Z)) < 0.0002f && Math.Abs(basis.Determinant() - 1) < 0.0003f;

    private void UploadAppearance(TileGrid grid, int slot)
    {
        if (!_appearanceEnabled) return;
        const int count = GridSide * GridSide, band = count * 4;
        float[] data = _appearanceFloats!;
        for (int i = 0; i < count; i++)
        {
            for (int packed = 0; packed < 7; packed++)
            {
                int first = packed * 3;
                int value = Byte(first) | Byte(first + 1) << 8 | Byte(first + 2) << 16;
                data[(packed < 4 ? 0 : band) + 4 * i + (packed < 4 ? packed : packed - 4)] = value;
            }
            data[band + 4 * i + 3] = grid.Heights[i] - grid.WaterLevels[i];
            int Byte(int channel) => channel < 16 ? grid.Materials[16 * i + channel]
                : channel < 20 ? grid.Climate[4 * i + channel - 16] : 0;
        }
        UploadAppearanceLayer(_appearanceMaterials!, slot);
        ThrowFixtureFault(FixtureFault.AfterAppearanceMaterials);
        for (int i = 0; i < count; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                data[4 * i + c] = grid.Normals[3 * i + c];
                data[band + 4 * i + c] = grid.MorphNormals[3 * i + c];
            }
            // TerrainLod uploads FaceMetres verbatim. It has already subtracted FaceAnchor; do not add it back.
            data[4 * i + 3] = grid.FaceMetres[2 * i];
            data[band + 4 * i + 3] = grid.FaceMetres[2 * i + 1];
        }
        UploadAppearanceLayer(_appearanceNormals!, slot);
    }

    private void UploadAppearanceLayer(Texture2DArray texture, int slot)
    {
        Buffer.BlockCopy(_appearanceFloats!, 0, _appearanceBytes!, 0, AppearanceLayerBytes);
        using Image image = Image.CreateFromData(GridSide, GridSide * 2, false, Image.Format.Rgbaf, _appearanceBytes!);
        texture.UpdateLayer(image, slot);
        UploadedBytes += AppearanceLayerBytes;
    }

    private void RetireAppearance(ref List<Exception>? failures)
    {
        // Inject a failure before the first appearance handle is retired, retaining that actual live resource.
        // Every other resource still gets its independent cleanup attempt. The next explicit Dispose retries it.
        bool injected = _fixtureFault is FixtureFault.DuringAppearanceRetirement or FixtureFault.AppearanceConstructionAndRetirement;
        if (injected)
        {
            _fixtureFault = FixtureFault.None;
            (failures ??= new()).Add(new InvalidOperationException("Expected appearance native retirement fixture failure."));
        }
        else Retire(_appearanceMaterials, ref failures);
        Retire(_appearanceNormals, ref failures);
        if (failures == null) { _appearanceFloats = null; _appearanceBytes = null; }
    }

    internal GodotObject[] AppearanceResourcesForFixture() => _appearanceEnabled
        ? new GodotObject[] { _appearanceMaterials!, _appearanceNormals! } : Array.Empty<GodotObject>();
}
