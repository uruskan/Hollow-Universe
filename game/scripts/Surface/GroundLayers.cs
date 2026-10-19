using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Surface;

/// <summary>
/// The ground's texture layers (GROUND.md §9, docs/WALKING_GROUND.md §8): built from the owned scans by
/// <c>tools/blender/surface_layers.py</c> into one file per texture and map (4K colour and normals as the scans' own
/// files, 2K roughness and height), each imported VRAM-compressed with mipmaps, and put together here into three
/// texture arrays every ground material shares: colour, normals, and roughness (r) with height (g). The first
/// <see cref="Count"/> textures are the sixteen ground materials' own scans, in <c>Hollow.Surface.GroundMaterial</c>
/// order (so a material's index is its texture's); the textures after them are the alternatives a material switches
/// to by place (<c>layers.json</c>: each material's texture, colour gain and variants with their selector), which
/// <see cref="IncludeVariants"/> loads on the High preset and above. Built once, on the main thread (it reads the
/// imported textures back from the GPU), and kept.
/// </summary>
public static class GroundLayers
{
    public const string Folder = "res://assets/surface/ground/layers/";

    /// <summary>The ground materials (slots of the shader), <c>GroundMaterial</c>'s count.</summary>
    public const int Count = 16;

    /// <summary>The most textures the arrays hold (the shaders' array uniforms are this long).</summary>
    public const int MaxTextures = 40;

    /// <summary>Alternatives a material may have (the shader samples up to this many besides the material's own).</summary>
    public const int MaxVariants = 3;

    private static Loaded? _loaded;

    private sealed record Loaded(Texture2DArray Albedo, Texture2DArray Normal, Texture2DArray RoughHeight, Catalogue Catalogue, int TextureCount);

    private sealed record Texture(int Index, string Key, string Albedo, string Normal, string RoughHeight, float Metres, Vector2 Flip, float Luminance);

    private sealed record Variant(int Texture, int Selector, Vector3 Gain);

    private sealed record Layer(int Index, string Key, int Texture, Vector3 Gain, IReadOnlyList<Variant> Variants);

    private sealed record Catalogue(IReadOnlyList<Texture> Textures, IReadOnlyList<Layer> Layers);

    /// <summary>Base colour, one layer a texture.</summary>
    public static Texture2DArray Albedo => Load().Albedo;

    /// <summary>Normal maps (each in its scan's own convention; <see cref="Apply"/> gives the flips).</summary>
    public static Texture2DArray Normal => Load().Normal;

    /// <summary>Roughness (r) and height (g).</summary>
    public static Texture2DArray RoughHeight => Load().RoughHeight;

    /// <summary>How many textures the arrays hold: the sixteen materials', and the variants when loaded.</summary>
    public static int TextureCount => Load().TextureCount;

    /// <summary>
    /// Whether the materials' alternative scans are loaded besides their own (about 50 MB of GPU memory a 4K texture,
    /// 0.95 GB for the nineteen): on every preset but Low, and on custom settings whose terrain detail is at least
    /// Low's (0.75). Decided when the arrays are first built, for the run.
    /// </summary>
    public static bool IncludeVariants
    {
        get
        {
            HollowUniverse.Settings.GraphicsValues v = HollowUniverse.Settings.GraphicsSettings.Current;
            return v.Preset switch
            {
                HollowUniverse.Settings.QualityPreset.Low => false,
                HollowUniverse.Settings.QualityPreset.Custom => v.TerrainDetail >= 0.75f,
                _ => true,
            };
        }
    }

    // Read-only borrowed data for the view-owned canonical sampler phase compiler: each material's own texture's size.
    internal static ReadOnlySpan<float> Metres
    {
        get
        {
            Catalogue c = Load().Catalogue;
            var metres = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                metres[i] = c.Textures[c.Layers[i].Texture].Metres;
            }
            return metres;
        }
    }

    /// <summary>Every texture's files that this run loads, for the loading screen to read ahead on a worker thread (GROUND.md §4.3.1).</summary>
    public static IReadOnlyList<string> Files()
    {
        Catalogue c = Read();
        int count = TexturesToLoad(c);
        var files = new List<string>();
        for (int i = 0; i < count; i++)
        {
            Texture t = c.Textures[i];
            files.Add(Folder + t.Albedo);
            files.Add(Folder + t.Normal);
            files.Add(Folder + t.RoughHeight);
        }
        return files;
    }

    /// <summary>Puts the textures into their arrays now (the loading screen's warm-up), if not done yet.</summary>
    public static void Warm() => Load();

    /// <summary>Gives a ground shader the arrays, every texture's size, normal flips and luminance, and the materials' variants.</summary>
    public static void Apply(ShaderMaterial material)
    {
        Loaded l = Load();
        Catalogue c = l.Catalogue;
        material.SetShaderParameter("albedo_array", l.Albedo);
        material.SetShaderParameter("normal_array", l.Normal);
        material.SetShaderParameter("rough_height_array", l.RoughHeight);
        material.SetShaderParameter("layer_metres", Metres.ToArray());
        var texMetres = new float[MaxTextures];
        var texFlip = new Vector2[MaxTextures];
        var texLuminance = new float[MaxTextures];
        for (int i = 0; i < MaxTextures; i++)
        {
            Texture t = c.Textures[Math.Min(i, c.Textures.Count - 1)];
            texMetres[i] = t.Metres;
            texFlip[i] = t.Flip;
            texLuminance[i] = t.Luminance;
        }
        material.SetShaderParameter("tex_metres", texMetres);
        material.SetShaderParameter("tex_flip", texFlip);
        material.SetShaderParameter("tex_luminance", texLuminance);
        var slotGain = new Vector3[Count];
        var variants = new int[Count * MaxVariants];
        var selectors = new int[Count * MaxVariants];
        var variantGain = new Vector3[Count * MaxVariants];
        for (int i = 0; i < Count; i++)
        {
            Layer layer = c.Layers[i];
            slotGain[i] = layer.Gain;
            for (int k = 0; k < MaxVariants; k++)
            {
                int j = i * MaxVariants + k;
                // A variant whose texture this run did not load is left out (Low and Medium).
                Variant? v = k < layer.Variants.Count && layer.Variants[k].Texture < l.TextureCount ? layer.Variants[k] : null;
                variants[j] = v?.Texture ?? -1;
                selectors[j] = v?.Selector ?? 0;
                variantGain[j] = v?.Gain ?? Vector3.One;
            }
        }
        material.SetShaderParameter("slot_gain", slotGain);
        material.SetShaderParameter("slot_variant", variants);
        material.SetShaderParameter("slot_select", selectors);
        material.SetShaderParameter("slot_variant_gain", variantGain);
    }

    /// <summary>
    /// Gives the grass's shader what it needs of the ground's grass layer: the texture under lush grass (the grass
    /// material's "lush" variant, or none) with its gain, and both textures' luminance, so the blades' feet take the
    /// ground's colour as the terrain draws it.
    /// </summary>
    public static void ApplyGrass(ShaderMaterial material)
    {
        Loaded l = Load();
        Catalogue c = l.Catalogue;
        Layer grass = c.Layers[0];
        int lush = -1;
        Vector3 gain = Vector3.One;
        foreach (Variant v in grass.Variants)
        {
            if (v.Selector == SelectorLush && v.Texture < l.TextureCount)
            {
                lush = v.Texture;
                gain = v.Gain;
                break;
            }
        }
        material.SetShaderParameter("ground_albedo", l.Albedo);
        material.SetShaderParameter("grass_layer_size", c.Textures[grass.Texture].Metres);
        material.SetShaderParameter("ground_layer_luminance", c.Textures[grass.Texture].Luminance);
        material.SetShaderParameter("ground_lush_layer", lush);
        material.SetShaderParameter("ground_lush_size", lush >= 0 ? c.Textures[lush].Metres : c.Textures[grass.Texture].Metres);
        material.SetShaderParameter("ground_lush_gain", gain);
        material.SetShaderParameter("ground_lush_luminance", lush >= 0 ? c.Textures[lush].Luminance : c.Textures[grass.Texture].Luminance);
    }

    private const int SelectorLush = 3;

    /// <summary>The shader's selector codes (surface_ground_look.gdshaderinc) by the catalogue's names.</summary>
    private static int SelectorCode(string name) => name switch
    {
        "noise" => 1,
        "dryness" => 2,
        "lush" => SelectorLush,
        "lush-patch" => 4,
        "heat" => 5,
        "cold" => 6,
        "steep" => 7,
        "snowline" => 8,
        "thaw" => 9,
        _ => throw new InvalidOperationException($"layers.json: unknown variant selector '{name}'."),
    };

    private static int TexturesToLoad(Catalogue c) => IncludeVariants ? c.Textures.Count : Count;

    private static Loaded Load()
    {
        if (_loaded != null)
        {
            return _loaded;
        }
        Catalogue catalogue = Read();
        int count = TexturesToLoad(catalogue);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Texture2DArray? albedo = null, normal = null, roughHeight = null;
        try
        {
            // Finish and release each channel's readback images before starting the next channel.
            albedo = Upload(catalogue.Textures, count, static e => e.Albedo, "albedo");
            normal = Upload(catalogue.Textures, count, static e => e.Normal, "normal");
            roughHeight = Upload(catalogue.Textures, count, static e => e.RoughHeight, "roughness/height");
            _loaded = new Loaded(albedo, normal, roughHeight, catalogue, count);
        }
        catch
        {
            roughHeight?.Dispose();
            normal?.Dispose();
            albedo?.Dispose();
            throw;
        }
        HollowUniverse.Settings.GraphicsValues graphics = HollowUniverse.Settings.GraphicsSettings.Current;
        GD.Print($"[ground] {count} of {catalogue.Textures.Count} textures in arrays in {watch.ElapsedMilliseconds} ms"
            + $" (preset {graphics.Preset}, terrain detail {graphics.TerrainDetail:0.00}"
            + (count < catalogue.Textures.Count ? ": the materials' alternatives left out)" : ")"));
        return _loaded;
    }

    private static Texture2DArray Upload(IReadOnlyList<Texture> textures, int count, Func<Texture, string> file, string channel)
    {
        RenderMemoryTrace.Mark($"ground-{channel}:begin");
        var ownedImages = new List<Image>(count);
        var images = new Godot.Collections.Array<Image>();
        // Array<T> itself is not IDisposable; this is its underlying native-reference container.
        using var imageReferences = (Godot.Collections.Array)images;
        Texture2DArray? texture = null;
        try
        {
            for (int i = 0; i < count; i++)
            {
                Image image = ReadImage(file(textures[i]));
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

    // A texture's file as an image (VRAM-compressed, with its mipmaps), the texture let go at once: the arrays hold the copy.
    private static Image ReadImage(string file)
    {
        using Texture2D texture = GD.Load<Texture2D>(Folder + file) ?? throw new InvalidOperationException($"Ground texture {file} is missing (tools/blender/surface_layers.py).");
        return texture.GetImage() ?? throw new InvalidOperationException($"Ground texture {file} can't be read back.");
    }

    private static Catalogue Read()
    {
        string json = FileAccess.GetFileAsString(Folder + "layers.json");
        using JsonDocument doc = JsonDocument.Parse(json);
        var textures = new List<Texture>();
        foreach (JsonElement t in doc.RootElement.GetProperty("textures").EnumerateArray())
        {
            JsonElement f = t.GetProperty("flip");
            JsonElement mean = t.GetProperty("meanLinear");
            float luminance = (float)(0.2126 * mean[0].GetDouble() + 0.7152 * mean[1].GetDouble() + 0.0722 * mean[2].GetDouble());
            textures.Add(new Texture(t.GetProperty("index").GetInt32(), t.GetProperty("key").GetString()!,
                t.GetProperty("albedo").GetString()!, t.GetProperty("normal").GetString()!, t.GetProperty("roughHeight").GetString()!,
                (float)t.GetProperty("metres").GetDouble(), new Vector2((float)f[0].GetDouble(), (float)f[1].GetDouble()),
                Math.Max(luminance, 0.002f)));
        }
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i].Index != i)
            {
                throw new InvalidOperationException($"layers.json: texture {i} is numbered {textures[i].Index}.");
            }
        }
        if (textures.Count < Count || textures.Count > MaxTextures)
        {
            throw new InvalidOperationException($"layers.json has {textures.Count} textures, not {Count} to {MaxTextures}.");
        }
        var layers = new List<Layer>();
        foreach (JsonElement layer in doc.RootElement.GetProperty("layers").EnumerateArray())
        {
            var variants = new List<Variant>();
            foreach (JsonElement v in layer.GetProperty("variants").EnumerateArray())
            {
                variants.Add(new Variant(TextureIndex(v, textures.Count), SelectorCode(v.GetProperty("select").GetString()!), Gain(v)));
            }
            if (variants.Count > MaxVariants)
            {
                throw new InvalidOperationException($"layers.json: layer {layers.Count} has {variants.Count} variants, more than {MaxVariants}.");
            }
            layers.Add(new Layer(layer.GetProperty("index").GetInt32(), layer.GetProperty("key").GetString()!, TextureIndex(layer, textures.Count), Gain(layer), variants));
        }
        if (layers.Count != Count)
        {
            throw new InvalidOperationException($"layers.json has {layers.Count} layers, not {Count}.");
        }
        for (int i = 0; i < Count; i++)
        {
            if (layers[i].Index != i || layers[i].Texture != i)
            {
                throw new InvalidOperationException($"layers.json: layer {i} must be numbered {i} and own texture {i} (the materials' own scans come first).");
            }
        }
        return new Catalogue(textures, layers);
    }

    private static int TextureIndex(JsonElement element, int count)
    {
        int index = element.GetProperty("texture").GetInt32();
        if (index < 0 || index >= count)
        {
            throw new InvalidOperationException($"layers.json: texture {index} does not exist.");
        }
        return index;
    }

    private static Vector3 Gain(JsonElement element)
    {
        if (!element.TryGetProperty("gain", out JsonElement g))
        {
            return Vector3.One;
        }
        return new Vector3((float)g[0].GetDouble(), (float)g[1].GetDouble(), (float)g[2].GetDouble());
    }
}
