using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Surface;

/// <summary>
/// The ground's sixteen texture layers (GROUND.md §9, docs/WALKING_GROUND.md §8), in <c>Hollow.Surface.GroundMaterial</c>
/// order: built from the owned scans by <c>tools/blender/surface_layers.py</c> into one file per layer and map (4K colour
/// and normals as the scans' own files, 2K roughness and height), each imported VRAM-compressed with mipmaps, and put
/// together here into three texture arrays every ground material shares: colour, normals, and roughness (r) with height
/// (g). Also each layer's size in metres and its normals' axes against the first layer's (<c>layers.json</c>).
/// Built once, on the main thread (it reads the imported layers back from the GPU), and kept.
/// </summary>
public static class GroundLayers
{
    public const string Folder = "res://assets/surface/ground/layers/";
    public const int Count = 16;

    private static Loaded? _loaded;

    private sealed record Loaded(Texture2DArray Albedo, Texture2DArray Normal, Texture2DArray RoughHeight, float[] Metres, Vector2[] Flip);

    private sealed record Entry(string Albedo, string Normal, string RoughHeight, float Metres, Vector2 Flip);

    /// <summary>Base colour, one layer a material.</summary>
    public static Texture2DArray Albedo => Load().Albedo;

    /// <summary>Normal maps (each layer in its scan's own convention; <see cref="Apply"/> gives the flips).</summary>
    public static Texture2DArray Normal => Load().Normal;

    /// <summary>Roughness (r) and height (g).</summary>
    public static Texture2DArray RoughHeight => Load().RoughHeight;

    // Read-only borrowed data for the view-owned canonical sampler phase compiler.
    internal static ReadOnlySpan<float> Metres => Load().Metres;

    /// <summary>Every layer's file, for the loading screen to read ahead on a worker thread (GROUND.md §4.3.1).</summary>
    public static IReadOnlyList<string> Files()
    {
        var files = new List<string>();
        foreach (Entry e in Table())
        {
            files.Add(Folder + e.Albedo);
            files.Add(Folder + e.Normal);
            files.Add(Folder + e.RoughHeight);
        }
        return files;
    }

    /// <summary>Puts the layers into their arrays now (the loading screen's warm-up), if not done yet.</summary>
    public static void Warm() => Load();

    /// <summary>Gives a ground shader the arrays and the layers' sizes and normal flips.</summary>
    public static void Apply(ShaderMaterial material)
    {
        Loaded l = Load();
        material.SetShaderParameter("albedo_array", l.Albedo);
        material.SetShaderParameter("normal_array", l.Normal);
        material.SetShaderParameter("rough_height_array", l.RoughHeight);
        material.SetShaderParameter("layer_metres", l.Metres);
        material.SetShaderParameter("layer_flip", l.Flip);
    }

    private static Loaded Load()
    {
        if (_loaded != null)
        {
            return _loaded;
        }
        List<Entry> table = Table();
        var metres = new float[Count];
        var flip = new Vector2[Count];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < table.Count; i++)
        {
            metres[i] = table[i].Metres;
            flip[i] = table[i].Flip;
        }
        Texture2DArray? albedo = null, normal = null, roughHeight = null;
        try
        {
            // Finish and release each channel's readback images before starting the next channel.
            albedo = Upload(table, static e => e.Albedo, "albedo");
            normal = Upload(table, static e => e.Normal, "normal");
            roughHeight = Upload(table, static e => e.RoughHeight, "roughness/height");
            _loaded = new Loaded(albedo, normal, roughHeight, metres, flip);
        }
        catch
        {
            roughHeight?.Dispose();
            normal?.Dispose();
            albedo?.Dispose();
            throw;
        }
        GD.Print($"[ground] {Count} layers in arrays in {watch.ElapsedMilliseconds} ms");
        return _loaded;
    }

    private static Texture2DArray Upload(IReadOnlyList<Entry> table, Func<Entry, string> file, string channel)
    {
        RenderMemoryTrace.Mark($"ground-{channel}:begin");
        var ownedImages = new List<Image>(table.Count);
        var images = new Godot.Collections.Array<Image>();
        // Array<T> itself is not IDisposable; this is its underlying native-reference container.
        using var imageReferences = (Godot.Collections.Array)images;
        Texture2DArray? texture = null;
        try
        {
            foreach (Entry entry in table)
            {
                Image image = Read(file(entry));
                ownedImages.Add(image);
                images.Add(image);
            }
            RenderMemoryTrace.Mark($"ground-{channel}:readbacks-ready");
            texture = new Texture2DArray();
            Error error = texture.CreateFromImages(images);
            if (error != Error.Ok)
                throw new InvalidOperationException($"Ground {channel} array upload failed: {error}.");
            RenderMemoryTrace.Mark($"ground-{channel}:array-uploaded");
            return texture;
        }
        catch
        {
            texture?.Dispose();
            throw;
        }
        finally
        {
            // GetImage returns owned copies. The uploaded texture has its own rendering resource;
            // release both the array's native references and our managed wrappers on every path.
            imageReferences.Clear();
            foreach (Image image in ownedImages) image.Dispose();
            RenderMemoryTrace.Mark($"ground-{channel}:readbacks-released");
        }
    }

    // A layer's file as an image (VRAM-compressed, with its mipmaps), the texture let go at once: the arrays hold the copy.
    private static Image Read(string file)
    {
        using Texture2D texture = GD.Load<Texture2D>(Folder + file) ?? throw new InvalidOperationException($"Ground layer {file} is missing (tools/blender/surface_layers.py).");
        return texture.GetImage() ?? throw new InvalidOperationException($"Ground layer {file} can't be read back.");
    }

    private static List<Entry> Table()
    {
        string json = FileAccess.GetFileAsString(Folder + "layers.json");
        using JsonDocument doc = JsonDocument.Parse(json);
        var table = new List<Entry>();
        foreach (JsonElement layer in doc.RootElement.GetProperty("layers").EnumerateArray())
        {
            JsonElement f = layer.GetProperty("flip");
            table.Add(new Entry(layer.GetProperty("albedo").GetString()!, layer.GetProperty("normal").GetString()!,
                layer.GetProperty("roughHeight").GetString()!, (float)layer.GetProperty("metres").GetDouble(),
                new Vector2((float)f[0].GetDouble(), (float)f[1].GetDouble())));
        }
        if (table.Count != Count)
        {
            throw new InvalidOperationException($"layers.json has {table.Count} layers, not {Count}.");
        }
        return table;
    }
}
