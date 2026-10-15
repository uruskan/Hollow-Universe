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
        var albedo = new Godot.Collections.Array<Image>();
        var normal = new Godot.Collections.Array<Image>();
        var roughHeight = new Godot.Collections.Array<Image>();
        var metres = new float[Count];
        var flip = new Vector2[Count];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < table.Count; i++)
        {
            albedo.Add(Read(table[i].Albedo));
            normal.Add(Read(table[i].Normal));
            roughHeight.Add(Read(table[i].RoughHeight));
            metres[i] = table[i].Metres;
            flip[i] = table[i].Flip;
        }
        var a = new Texture2DArray();
        a.CreateFromImages(albedo);
        var n = new Texture2DArray();
        n.CreateFromImages(normal);
        var r = new Texture2DArray();
        r.CreateFromImages(roughHeight);
        _loaded = new Loaded(a, n, r, metres, flip);
        GD.Print($"[ground] {Count} layers in arrays in {watch.ElapsedMilliseconds} ms");
        return _loaded;
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
