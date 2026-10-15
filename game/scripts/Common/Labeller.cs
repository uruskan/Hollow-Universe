using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Common;

/// <summary>Where a label goes relative to the object it names.</summary>
public enum LabelSide
{
    Right,
    Left,
    Below,
    Above,
}

/// <summary>
/// Places map labels so they never overlap each other, the panels or the selection rings. Labels are offered in
/// order of importance; each takes the first free spot among a few sides of its object, or is left out.
/// </summary>
public sealed class Labeller
{
    private readonly List<Rect2> _taken = new();
    private readonly Rect2 _screen;

    public Labeller(Vector2 screenSize) => _screen = new Rect2(Vector2.Zero, screenSize);

    /// <summary>Marks an area as taken (a panel).</summary>
    public void Block(Rect2 area) => _taken.Add(area);

    /// <summary>Marks a circle as taken (a ring, a star that has or needs a label).</summary>
    public void Block(Vector2 centre, float radius) =>
        _taken.Add(new Rect2(centre - new Vector2(radius, radius), new Vector2(2 * radius, 2 * radius)));

    /// <summary>Takes a label's spot whether or not it is free, for labels that always show.</summary>
    public void Force(in MapOverlay.Text text) => _taken.Add(MapOverlay.Bounds(text));

    /// <summary>Whether a point lies under something taken, such as a panel.</summary>
    public bool IsCovered(Vector2 point)
    {
        foreach (Rect2 area in _taken)
        {
            if (area.HasPoint(point))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Takes the label's spot if it is on screen and touches nothing taken.</summary>
    public bool TryPlace(in MapOverlay.Text text) => TryPlace(MapOverlay.Bounds(text));

    /// <summary>Takes a spot its owner measured (a label in another font) if it is on screen and touches nothing taken.</summary>
    public bool TryPlace(Rect2 bounds)
    {
        if (!_screen.Encloses(bounds))
        {
            return false;
        }
        Rect2 padded = bounds.Grow(Ui.Pxf(3));
        foreach (Rect2 area in _taken)
        {
            if (area.Intersects(padded))
            {
                return false;
            }
        }
        _taken.Add(bounds);
        return true;
    }

    /// <summary>
    /// Places a label next to a circle (a star's dot, a selection ring) on the first free side in
    /// <paramref name="sides"/>. Returns false, and places nothing, when none is free.
    /// </summary>
    public bool TryPlaceAround(Vector2 centre, float radius, string value, Color colour, float size, MapLabel style,
        ReadOnlySpan<LabelSide> sides, out MapOverlay.Text placed)
    {
        foreach (LabelSide side in sides)
        {
            placed = Beside(centre, radius, side, value, colour, size, style);
            if (TryPlace(placed))
            {
                return true;
            }
        }
        placed = default;
        return false;
    }

    /// <summary>A label on one side of a circle: beside it with its capitals centred on it, or centred above or below.</summary>
    public static MapOverlay.Text Beside(Vector2 centre, float radius, LabelSide side, string value, Color colour,
        float size, MapLabel style)
    {
        Font font = MapOverlay.FontOf(style);
        int pixels = Ui.Px(size);
        float gap = Ui.Pxf(8);
        float cap = 0.73f * pixels;
        Vector2 position = side switch
        {
            LabelSide.Right => centre + new Vector2(radius + gap, cap / 2),
            LabelSide.Left => centre + new Vector2(-radius - gap - MapOverlay.Measure(value, size, style).X, cap / 2),
            LabelSide.Below => centre + new Vector2(0, radius + gap + font.GetAscent(pixels)),
            _ => centre - new Vector2(0, radius + gap + font.GetDescent(pixels)),
        };
        return new MapOverlay.Text(position, value, colour, size, side is LabelSide.Below or LabelSide.Above, style);
    }
}
