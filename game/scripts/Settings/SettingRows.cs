using System;
using System.Globalization;

namespace HollowUniverse.Settings;

/// <summary>How a setting is changed: on or off, one of a list, or a value on a slider.</summary>
public enum SettingKind
{
    Switch,
    Choice,
    Slider,
}

/// <summary>
/// One row of the settings panel (GROUND.md §8.6): its name, a one-line explanation, and how it reads and changes the
/// values. Every value is a number to the panel: a switch 0 or 1, a choice its index, a slider its value.
/// </summary>
public sealed record SettingRow(
    string Label,
    string Help,
    SettingKind Kind,
    Func<GraphicsValues, float> Get,
    Func<GraphicsValues, float, GraphicsValues> Set,
    float Min = 0,
    float Max = 1,
    float Step = 1,
    string[]? Options = null,
    Func<float, string>? Format = null,
    Func<GraphicsValues, bool>? Active = null)
{
    /// <summary>Whether the row does anything now (a strength with its effect off does not).</summary>
    public bool IsActive(GraphicsValues values) => Active?.Invoke(values) ?? true;

    /// <summary>The value as shown.</summary>
    public string Text(GraphicsValues values)
    {
        float v = Get(values);
        return Kind switch
        {
            SettingKind.Switch => v > 0.5f ? "ON" : "OFF",
            SettingKind.Choice => Options is { } o && (int)v >= 0 && (int)v < o.Length ? o[(int)v] : "CUSTOM",
            _ => Format?.Invoke(v) ?? v.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Where a slider's value is along its track, 0–1.</summary>
    public float Fraction(GraphicsValues values) => Max > Min ? Math.Clamp((Get(values) - Min) / (Max - Min), 0, 1) : 0;

    /// <summary>The values with this setting moved by <paramref name="direction"/> steps (a switch flips, a list wraps).</summary>
    public GraphicsValues Move(GraphicsValues values, int direction)
    {
        float v = Get(values);
        switch (Kind)
        {
            case SettingKind.Switch:
                return Set(values, v > 0.5f ? 0 : 1);
            case SettingKind.Choice:
            {
                int n = Options?.Length ?? 1;
                int at = (int)v;
                // Off the list (the preset's CUSTOM): forward goes to the first, back to the last.
                int next = at >= n ? (direction > 0 ? 0 : n - 1) : ((at + direction) % n + n) % n;
                return Set(values, next);
            }
            default:
                return SetValue(values, v + direction * Step);
        }
    }

    /// <summary>A slider at a point of its track, 0–1, snapped to its step.</summary>
    public GraphicsValues SetFraction(GraphicsValues values, float fraction) => SetValue(values, Min + Math.Clamp(fraction, 0, 1) * (Max - Min));

    private GraphicsValues SetValue(GraphicsValues values, float v)
    {
        float snapped = Step > 0 ? Min + MathF.Round((v - Min) / Step) * Step : v;
        return Set(values, Math.Clamp(snapped, Min, Max));
    }
}

/// <summary>The settings panel's rows, by tab (GROUND.md §8.6).</summary>
public static class SettingRows
{
    private static string Percent(float v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    private static string Metres(float v) => v.ToString("0", CultureInfo.InvariantCulture) + " m";

    private static readonly string[] Levels = { "LOW", "MEDIUM", "HIGH", "ULTRA" };

    /// <summary>The GRAPHICS tab: twelve rows a column, the picture's quality on the left, its reach and the screen on the right.</summary>
    public static readonly SettingRow[] Graphics =
    {
        new("QUALITY PRESET", "Sets everything at once; changing one setting makes it Custom.", SettingKind.Choice,
            v => (int)v.Preset, (v, x) => x < (int)QualityPreset.Custom ? GraphicsSettings.WithPreset(v, (QualityPreset)(int)x) : v,
            Options: new[] { "LOW", "MEDIUM", "HIGH", "ULTRA", "MAXIMUM" }),
        new("ANTI-ALIASING", "Smooths jagged edges: MSAA is crisp, TAA and FSR 2 smoother.", SettingKind.Choice,
            v => (int)v.AntiAliasing, (v, x) => v with { AntiAliasing = (AntiAliasingMode)(int)x },
            Options: new[] { "OFF", "FXAA", "SMAA", "MSAA 2×", "MSAA 4×", "TAA", "FSR 2" }),
        new("RENDER SCALE", "The 3D image's resolution; below 100 % FSR upscales it.", SettingKind.Slider,
            v => v.RenderScale, (v, x) => v with { RenderScale = x }, 0.5f, 1.5f, 0.05f, Format: Percent),
        new("SHARPENING", "Contrast-adaptive sharpening of the 3D image, never the text.", SettingKind.Switch,
            v => v.Sharpen ? 1 : 0, (v, x) => v with { Sharpen = x > 0.5f }),
        new("SHARPENING STRENGTH", "How strongly edges are sharpened.", SettingKind.Slider,
            v => v.SharpenStrength, (v, x) => v with { SharpenStrength = x }, 0, 1, 0.05f, Format: Percent, Active: v => v.Sharpen),
        new("BLOOM AND GLOW", "Bright light bleeding softly into what is around it.", SettingKind.Switch,
            v => v.Glow ? 1 : 0, (v, x) => v with { Glow = x > 0.5f }),
        new("BLOOM STRENGTH", "How strong the glow is; 100 % is the game's own look.", SettingKind.Slider,
            v => v.GlowStrength, (v, x) => v with { GlowStrength = x }, 0, 2, 0.1f, Format: Percent, Active: v => v.Glow),
        new("AMBIENT OCCLUSION", "SSAO: soft contact shadows in corners and under things.", SettingKind.Switch,
            v => v.Ssao ? 1 : 0, (v, x) => v with { Ssao = x > 0.5f }),
        new("INDIRECT LIGHT", "SSIL: light bounced off the surfaces near what you see.", SettingKind.Switch,
            v => v.Ssil ? 1 : 0, (v, x) => v with { Ssil = x > 0.5f }),
        new("GLOBAL ILLUMINATION", "SDFGI: sun and sky light bouncing through the world. Costly.", SettingKind.Switch,
            v => v.Sdfgi ? 1 : 0, (v, x) => v with { Sdfgi = x > 0.5f }),
        new("REFLECTIONS", "Screen-space reflections on water and shiny surfaces.", SettingKind.Switch,
            v => v.Reflections ? 1 : 0, (v, x) => v with { Reflections = x > 0.5f }),
        new("VOLUMETRIC FOG", "A faint haze in the air's colour; shafts of sunlight.", SettingKind.Switch,
            v => v.Fog ? 1 : 0, (v, x) => v with { Fog = x > 0.5f }),

        new("EFFECT QUALITY", "Detail of occlusion, indirect light, GI, reflections and fog.", SettingKind.Choice,
            v => v.EffectQuality, (v, x) => v with { EffectQuality = (int)x }, Options: Levels),
        new("SHADOW QUALITY", "The sun's shadows: resolution and softness. Off saves most.", SettingKind.Choice,
            v => v.Shadows, (v, x) => v with { Shadows = (int)x }, Options: new[] { "OFF", "LOW", "MEDIUM", "HIGH", "ULTRA" }),
        new("SHADOW DISTANCE", "How far from you the sun's shadows reach.", SettingKind.Slider,
            v => v.ShadowDistance, (v, x) => v with { ShadowDistance = x }, 50, 3000, 50, Format: Metres, Active: v => v.Shadows > 0),
        new("TERRAIN DETAIL", "How far the ground keeps its finest shape.", SettingKind.Slider,
            v => v.TerrainDetail, (v, x) => v with { TerrainDetail = x }, 0.6f, 1.6f, 0.05f, Format: Percent),
        new("TREES AND PLANTS", "How far trees and plants keep their full detail.", SettingKind.Slider,
            v => v.PlantDistance, (v, x) => v with { PlantDistance = x }, 0.6f, 1.4f, 0.05f, Format: Percent),
        new("GRASS DENSITY", "How many tufts of grass grow; fewer is faster.", SettingKind.Slider,
            v => v.GrassDensity, (v, x) => v with { GrassDensity = x }, 0.4f, 1, 0.05f, Format: Percent),
        new("GRASS DISTANCE", "How far from you grass is drawn.", SettingKind.Slider,
            v => v.GrassDistance, (v, x) => v with { GrassDistance = x }, 0.6f, 1.6f, 0.05f, Format: x => Metres(42 * x)),
        new("MODEL DETAIL", "How far buildings and things keep their finest models.", SettingKind.Slider,
            v => v.ModelDetail, (v, x) => v with { ModelDetail = x }, 0.5f, 2, 0.05f, Format: Percent),
        new("TEXTURE FILTERING", "Anisotropic filtering: sharp ground seen at a glancing angle.", SettingKind.Choice,
            v => v.Anisotropy, (v, x) => v with { Anisotropy = (int)x }, Options: new[] { "TRILINEAR", "2×", "4×", "8×", "16×" }),
        new("VSYNC", "Waits for the screen: no tearing, a frame more of delay.", SettingKind.Choice,
            v => v.VSync, (v, x) => v with { VSync = (int)x }, Options: new[] { "OFF", "ON", "ADAPTIVE", "MAILBOX" }),
        new("FRAME RATE CAP", "The most frames drawn a second; lower runs cooler.", SettingKind.Choice,
            v => v.FrameCap, (v, x) => v with { FrameCap = (int)x },
            Options: Array.ConvertAll(GraphicsSettings.FrameCaps, c => c == 0 ? "NONE" : c.ToString(CultureInfo.InvariantCulture))),
        new("FIELD OF VIEW", "How wide you see on foot, in vertical degrees.", SettingKind.Slider,
            v => v.FieldOfView, (v, x) => v with { FieldOfView = x }, 60, 100, 1, Format: x => x.ToString("0", CultureInfo.InvariantCulture) + "°"),
    };

    /// <summary>Shadow cost controls; quality, reach, cascades and local-light count remain independent.</summary>
    public static readonly SettingRow[] Shadows =
    {
        new("SHADOW QUALITY", "Shadow resolution and softness; Off disables shadows.", SettingKind.Choice,
            v => v.Shadows, (v, x) => v with { Shadows = (int)x }, Options: new[] { "OFF", "LOW", "MEDIUM", "HIGH", "ULTRA" }),
        new("SHADOW DISTANCE", "Sun and moon shadow reach; shorter keeps detail close.", SettingKind.Slider,
            v => v.ShadowDistance, (v, x) => v with { ShadowDistance = x }, 50, 3000, 50, Format: Metres, Active: v => v.Shadows > 0),
        new("SHADOW CASCADES", "More cascades improve detail at a greater rendering cost.", SettingKind.Choice,
            v => v.ShadowCascades <= 1 ? 0 : v.ShadowCascades <= 2 ? 1 : 2,
            (v, x) => v with { ShadowCascades = (int)x switch { 0 => 1, 1 => 2, _ => 4 } },
            Options: new[] { "1", "2", "4" }, Active: v => v.Shadows > 0),
        new("MAX SHADOW LIGHTS", "Local lights casting shadows at once; light stays visible.", SettingKind.Slider,
            v => v.MaxShadowLights, (v, x) => v with { MaxShadowLights = (int)x }, 0, 8, 1,
            Active: v => v.Shadows > 0),
        new("LOCAL SHADOW DISTANCE", "Shadow reach beyond nearby lamps and torch light.", SettingKind.Slider,
            v => v.LocalShadowDistance, (v, x) => v with { LocalShadowDistance = x }, 10, 100, 5, Format: Metres,
            Active: v => v.Shadows > 0 && v.MaxShadowLights > 0),
    };

    /// <summary>The INTERFACE tab.</summary>
    public static readonly SettingRow[] Interface =
    {
        new("INTERFACE SCALE", "The size of all text and panels (also Ctrl + plus and minus).", SettingKind.Slider,
            v => v.InterfaceScale, (v, x) => v with { InterfaceScale = x }, 0.6f, 2, 0.05f, Format: Percent),
    };

    /// <summary>The CROSSHAIR tab: one centred reticle, with the same preview/apply/revert flow as other settings.</summary>
    public static readonly SettingRow[] Crosshair =
    {
        new("STYLE", "Choose one reticle at the exact centre of the screen.", SettingKind.Choice,
            v => (int)v.Crosshair.Style, (v, x) => v with { Crosshair = v.Crosshair with { Style = (CrosshairStyle)(int)x } },
            Options: new[] { "OFF", "DOT", "CROSS", "CROSS + DOT", "RING", "RING + DOT" }),
        new("COLOUR", "The reticle colour; the outline stays dark for contrast.", SettingKind.Choice,
            v => v.Crosshair.Color, (v, x) => v with { Crosshair = v.Crosshair with { Color = (int)x } },
            Options: new[] { "WHITE", "GREEN", "CYAN", "YELLOW", "RED", "MAGENTA" }, Active: CrosshairEnabled),
        new("SIZE", "Cross arm length or ring radius.", SettingKind.Slider,
            v => v.Crosshair.Size, (v, x) => v with { Crosshair = v.Crosshair with { Size = x } }, 2, 24, 1,
            Active: v => v.Crosshair.HasArms || v.Crosshair.HasRing),
        new("DOT SIZE", "Diameter of the centre dot.", SettingKind.Slider,
            v => v.Crosshair.DotSize, (v, x) => v with { Crosshair = v.Crosshair with { DotSize = x } }, 1, 12, 1,
            Active: v => v.Crosshair.HasDot),
        new("THICKNESS", "Width of the cross arms or ring line.", SettingKind.Slider,
            v => v.Crosshair.Thickness, (v, x) => v with { Crosshair = v.Crosshair with { Thickness = x } }, 1, 6, 0.5f,
            Active: v => v.Crosshair.HasArms || v.Crosshair.HasRing),
        new("GAP", "Space between the centre and each cross arm.", SettingKind.Slider,
            v => v.Crosshair.Gap, (v, x) => v with { Crosshair = v.Crosshair with { Gap = x } }, 0, 20, 1,
            Active: v => v.Crosshair.HasArms),
        new("OPACITY", "How solid the reticle appears.", SettingKind.Slider,
            v => v.Crosshair.Opacity, (v, x) => v with { Crosshair = v.Crosshair with { Opacity = x } }, 0.1f, 1, 0.05f,
            Format: Percent, Active: CrosshairEnabled),
        new("OUTLINE", "Dark border width; zero removes the border.", SettingKind.Slider,
            v => v.Crosshair.Outline, (v, x) => v with { Crosshair = v.Crosshair with { Outline = x } }, 0, 3, 0.5f,
            Active: CrosshairEnabled),
        new("SHOW WHILE AIMING", "Keep the reticle visible when aiming down sights.", SettingKind.Switch,
            v => v.Crosshair.ShowWhileAiming ? 1 : 0,
            (v, x) => v with { Crosshair = v.Crosshair with { ShowWhileAiming = x > 0.5f } }, Active: CrosshairEnabled),
        new("DYNAMIC SPREAD", "Expand the cross or ring with weapon spread.", SettingKind.Switch,
            v => v.Crosshair.DynamicSpread ? 1 : 0,
            (v, x) => v with { Crosshair = v.Crosshair with { DynamicSpread = x > 0.5f } },
            Active: v => v.Crosshair.HasArms || v.Crosshair.HasRing),
    };

    private static bool CrosshairEnabled(GraphicsValues values) => values.Crosshair.Style != CrosshairStyle.Off;

    public static readonly SettingRow[] Display =
    {
        new("WINDOW MODE", "Windowed, borderless desktop, or exclusive fullscreen.", SettingKind.Choice,
            v => (int)v.WindowMode, (v, x) => v with { WindowMode = (GameWindowMode)(int)x },
            Options: new[] { "WINDOWED", "BORDERLESS", "EXCLUSIVE" }),
    };
}
