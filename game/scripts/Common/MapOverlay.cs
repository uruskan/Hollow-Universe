using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Common;

/// <summary>How a label on a map looks.</summary>
public enum MapLabel
{
    /// <summary>Names of stars and bodies.</summary>
    Name,

    /// <summary>The hovered or selected object.</summary>
    Strong,

    /// <summary>Letter-spaced capitals, for regions such as sectors.</summary>
    Region,
}

/// <summary>
/// Labels and rings drawn over a 3D map in screen space, with the interface's fonts at their real pixel size and a
/// soft dark outline so they stay legible over stars. Positions are in screen pixels; text sizes are base pixels
/// (scaled here); baselines are snapped to whole pixels. <see cref="Labeller"/> places labels so they don't overlap.
/// </summary>
public partial class MapOverlay : Control
{
    public readonly record struct Text(Vector2 Position, string Value, Color Colour, float Size, bool Centred, MapLabel Style = MapLabel.Name);

    public readonly record struct Ring(Vector2 Position, float Radius, Color Colour, float Width);

    public readonly List<Text> Texts = new();
    public readonly List<Ring> Rings = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public void Clear()
    {
        Texts.Clear();
        Rings.Clear();
    }

    public override void _Draw()
    {
        foreach (Ring ring in Rings)
        {
            int segments = Math.Clamp((int)(ring.Radius * 1.5f), 24, 128);
            DrawArc(ring.Position, ring.Radius, 0, Mathf.Tau, segments, ring.Colour, ring.Width, true);
        }
        int outline = Ui.Px(4);
        foreach (Text text in Texts)
        {
            Font font = FontOf(text.Style);
            int size = Ui.Px(text.Size);
            Vector2 at = Origin(text);
            DrawStringOutline(font, at, text.Value, HorizontalAlignment.Left, -1, size, outline, new Color(0, 0, 0, 0.55f * text.Colour.A));
            DrawString(font, at, text.Value, HorizontalAlignment.Left, -1, size, text.Colour);
        }
    }

    public static Font FontOf(MapLabel style) => style switch
    {
        MapLabel.Strong => Ui.SemiBold,
        MapLabel.Region => Ui.Overline,
        _ => Ui.Medium,
    };

    /// <summary>Width and height of a label's text in screen pixels.</summary>
    public static Vector2 Measure(string value, float size, MapLabel style) =>
        FontOf(style).GetStringSize(value, HorizontalAlignment.Left, -1, Ui.Px(size));

    /// <summary>The screen rectangle a label covers, its outline included.</summary>
    public static Rect2 Bounds(in Text text)
    {
        int size = Ui.Px(text.Size);
        Vector2 extent = Measure(text.Value, text.Size, text.Style);
        Vector2 at = Origin(text);
        float outline = Ui.Px(4) / 2f;
        float top = at.Y - FontOf(text.Style).GetAscent(size);
        return new Rect2(at.X - outline, top - outline, extent.X + 2 * outline, extent.Y + 2 * outline);
    }

    // The baseline's left end, snapped to whole pixels.
    private static Vector2 Origin(in Text text)
    {
        Vector2 at = text.Position;
        if (text.Centred)
        {
            at.X -= Measure(text.Value, text.Size, text.Style).X / 2;
        }
        return new Vector2(MathF.Round(at.X), MathF.Round(at.Y));
    }
}
