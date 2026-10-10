using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Common;

public enum UiIcon
{
    Search,
    Close,
    Back,
    Play,
    Pause,
    Slower,
    Faster,
    Star,
    Planet,
    Moon,
    Belt,
    Open,
}

/// <summary>
/// The interface's icons: original line icons drawn as 24 × 24 SVG in white (tinted by the theme), rasterised at
/// their final pixel size for the current UI scale and re-rasterised in place when the scale changes, so the same
/// texture objects stay valid and always crisp.
/// </summary>
public static class UiIcons
{
    private const string Head = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" " +
        "fill=\"none\" stroke=\"#ffffff\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">";

    private static readonly Dictionary<UiIcon, string> Shapes = new()
    {
        [UiIcon.Search] = "<circle cx=\"10.5\" cy=\"10.5\" r=\"6.5\"/><path d=\"M15.5 15.5 L20.5 20.5\"/>",
        [UiIcon.Close] = "<path d=\"M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5\"/>",
        [UiIcon.Back] = "<path d=\"M14.5 5.5 L8 12 L14.5 18.5\"/>",
        [UiIcon.Play] = "<path d=\"M8 5.5 L18.5 12 L8 18.5 Z\" fill=\"#ffffff\"/>",
        [UiIcon.Pause] = "<path d=\"M8.5 6 V18 M15.5 6 V18\" stroke-width=\"2.6\"/>",
        [UiIcon.Slower] = "<path d=\"M12 6.5 L6.5 12 L12 17.5 M18 6.5 L12.5 12 L18 17.5\"/>",
        [UiIcon.Faster] = "<path d=\"M12 6.5 L17.5 12 L12 17.5 M6 6.5 L11.5 12 L6 17.5\"/>",
        [UiIcon.Star] = "<path d=\"M12 3 C12.6 8.6 15.4 11.4 21 12 C15.4 12.6 12.6 15.4 12 21 C11.4 15.4 8.6 12.6 3 12 C8.6 11.4 11.4 8.6 12 3 Z\" " +
            "fill=\"#ffffff\" stroke-width=\"1\"/>",
        [UiIcon.Planet] = "<circle cx=\"12\" cy=\"12\" r=\"5\" fill=\"#ffffff\" stroke=\"none\"/>" +
            "<ellipse cx=\"12\" cy=\"12\" rx=\"10\" ry=\"3.6\" transform=\"rotate(-22 12 12)\" stroke-width=\"1.6\"/>",
        [UiIcon.Moon] = "<circle cx=\"12\" cy=\"12\" r=\"3.6\" fill=\"#ffffff\" stroke=\"none\"/>",
        [UiIcon.Belt] = "<g fill=\"#ffffff\" stroke=\"none\"><circle cx=\"4.5\" cy=\"14\" r=\"1.6\"/><circle cx=\"8.5\" cy=\"10.5\" r=\"1.3\"/>" +
            "<circle cx=\"12.5\" cy=\"9.2\" r=\"1.7\"/><circle cx=\"16.5\" cy=\"10\" r=\"1.2\"/><circle cx=\"19.8\" cy=\"12.8\" r=\"1.5\"/>" +
            "<circle cx=\"10.5\" cy=\"14.5\" r=\"1\"/><circle cx=\"15\" cy=\"14\" r=\"1.1\"/></g>",
        [UiIcon.Open] = "<path d=\"M9.5 5.5 L16 12 L9.5 18.5\"/>",
    };

    private static readonly Dictionary<(UiIcon Icon, float Size), ImageTexture> Cache = new();

    /// <summary>An icon at <paramref name="basePixels"/> (at scale 1), as a texture that tracks the UI scale.</summary>
    public static Texture2D Get(UiIcon icon, float basePixels = 16)
    {
        if (!Cache.TryGetValue((icon, basePixels), out ImageTexture? texture))
        {
            texture = ImageTexture.CreateFromImage(Rasterise(icon, basePixels));
            Cache[(icon, basePixels)] = texture;
        }
        return texture;
    }

    /// <summary>Re-rasterises every icon handed out so far at the current scale.</summary>
    public static void Refresh()
    {
        foreach (((UiIcon icon, float size), ImageTexture texture) in Cache)
        {
            texture.SetImage(Rasterise(icon, size));
        }
    }

    private static Image Rasterise(UiIcon icon, float basePixels)
    {
        var image = new Image();
        float pixels = Ui.Px(basePixels);
        image.LoadSvgFromString(Head + Shapes[icon] + "</svg>", pixels / 24f);
        return image;
    }
}
