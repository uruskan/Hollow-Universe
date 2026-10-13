using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI;

namespace HollowUniverse.Items;

/// <summary>
/// Item icons rendered from the models (GROUND.md §9): the catalogue's PNG nearest above the asked size, resampled
/// to exactly that many pixels so it stays crisp at any interface scale. Keys without a rendered icon go to the
/// fallback (the placeholder drawings). Switch the interface to it with <see cref="Install"/>.
/// </summary>
public sealed class CatalogItemIcons : IItemIconProvider
{
    private readonly IItemIconProvider? _fallback;
    private readonly Dictionary<(string Key, int Pixels, bool Blueprint), ImageTexture?> _cache = new();

    public CatalogItemIcons(IItemIconProvider? fallback)
    {
        _fallback = fallback;
        Ui.ScaleChanged += _cache.Clear;
    }

    /// <summary>Makes the interface draw the rendered icons, falling back to the current provider.</summary>
    public static void Install()
    {
        if (ItemIcons.Provider is not CatalogItemIcons)
        {
            ItemIcons.Provider = new CatalogItemIcons(ItemIcons.Provider);
        }
    }

    public Texture2D? Icon(string key, int pixels) => Get(key, pixels, false) ?? _fallback?.Icon(key, pixels);

    /// <summary>The blueprint look of an icon: the same picture in blueprint blue (for the tech tree).</summary>
    public Texture2D? Blueprint(string key, int pixels) => Get(key, pixels, true);

    private ImageTexture? Get(string key, int pixels, bool blueprint)
    {
        if (pixels <= 0)
        {
            return null;
        }
        if (_cache.TryGetValue((key, pixels, blueprint), out ImageTexture? cached))
        {
            return cached;
        }
        ImageTexture? texture = null;
        ModelEntry? entry = ModelCatalog.Find(key);
        if (entry?.IconKey != null)
        {
            Image? image = Source(entry.IconKey, pixels);
            if (image != null)
            {
                if (image.GetWidth() != pixels)
                {
                    image.Resize(pixels, pixels, Image.Interpolation.Lanczos);
                }
                if (blueprint)
                {
                    Tint(image);
                }
                texture = ImageTexture.CreateFromImage(image);
            }
        }
        _cache[(key, pixels, blueprint)] = texture;
        return texture;
    }

    /// <summary>A copy of the smallest rendered size at or above <paramref name="pixels"/> (else the largest).</summary>
    private static Image? Source(string iconKey, int pixels)
    {
        IReadOnlyList<int> sizes = ModelCatalog.IconSizes;
        int size = sizes[^1];
        foreach (int s in sizes)
        {
            if (s >= pixels)
            {
                size = s;
                break;
            }
        }
        string path = ModelCatalog.IconPath(iconKey, size);
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }
        Image? image = ResourceLoader.Load<Texture2D>(path)?.GetImage();
        if (image == null)
        {
            return null;
        }
        image = (Image)image.Duplicate();
        if (image.IsCompressed())
        {
            image.Decompress();
        }
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>Blueprint blue: the picture's brightness on a blue ramp, alpha kept.</summary>
    private static void Tint(Image image)
    {
        var dark = new Color(0.08f, 0.24f, 0.44f);
        var light = new Color(0.66f, 0.84f, 1f);
        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color c = image.GetPixel(x, y);
                float l = Mathf.Clamp(c.R * 0.3f + c.G * 0.59f + c.B * 0.11f, 0, 1);
                Color t = dark.Lerp(light, Mathf.Pow(l, 0.8f));
                image.SetPixel(x, y, new Color(t.R, t.G, t.B, c.A));
            }
        }
    }
}
