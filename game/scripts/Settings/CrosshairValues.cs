using System;

namespace HollowUniverse.Settings;

/// <summary>The shape of the player's single aiming reticle.</summary>
public enum CrosshairStyle
{
    Off,
    Dot,
    Cross,
    CrossDot,
    Ring,
    RingDot,
}

/// <summary>Player-owned crosshair preferences; dimensions use the interface's logical pixels.</summary>
public sealed record CrosshairValues
{
    public CrosshairStyle Style { get; init; } = CrosshairStyle.Dot;

    /// <summary>Palette index: white, green, cyan, yellow, red, magenta.</summary>
    public int Color { get; init; }

    /// <summary>Cross arm length or ring radius, 2–24 logical pixels.</summary>
    public float Size { get; init; } = 8;

    /// <summary>Centre dot diameter, 1–12 logical pixels.</summary>
    public float DotSize { get; init; } = 4;

    public float Thickness { get; init; } = 2;
    public float Gap { get; init; } = 4;
    public float Opacity { get; init; } = 0.9f;
    public float Outline { get; init; } = 1;
    public bool ShowWhileAiming { get; init; } = true;
    public bool DynamicSpread { get; init; }

    public bool HasArms => Style is CrosshairStyle.Cross or CrosshairStyle.CrossDot;
    public bool HasRing => Style is CrosshairStyle.Ring or CrosshairStyle.RingDot;
    public bool HasDot => Style is CrosshairStyle.Dot or CrosshairStyle.CrossDot or CrosshairStyle.RingDot;

    /// <summary>Bounds stored or previewed values, replacing non-finite numbers with their defaults.</summary>
    public CrosshairValues Normalize() => this with
    {
        Style = (CrosshairStyle)Math.Clamp((int)Style, (int)CrosshairStyle.Off, (int)CrosshairStyle.RingDot),
        Color = Math.Clamp(Color, 0, 5),
        Size = Bounded(Size, 8, 2, 24),
        DotSize = Bounded(DotSize, 4, 1, 12),
        Thickness = Bounded(Thickness, 2, 1, 6),
        Gap = Bounded(Gap, 4, 0, 20),
        Opacity = Bounded(Opacity, 0.9f, 0.1f, 1),
        Outline = Bounded(Outline, 1, 0, 3),
    };

    private static float Bounded(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
