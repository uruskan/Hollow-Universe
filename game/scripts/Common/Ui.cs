using System;
using System.Globalization;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Common;

/// <summary>Shared look of the interface: palette, theme, helpers.</summary>
public static class Ui
{
    public static readonly Color Panel = new(0.035f, 0.05f, 0.075f, 0.86f);
    public static readonly Color PanelBorder = new(0.40f, 0.72f, 0.90f, 0.35f);
    public static readonly Color Text = new(0.84f, 0.90f, 0.96f);
    public static readonly Color Dim = new(0.55f, 0.64f, 0.74f);
    public static readonly Color Accent = new(0.50f, 0.85f, 1.00f);
    public static readonly Color Warm = new(1.00f, 0.74f, 0.38f);

    private static Theme? _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 15 };
        var panel = new StyleBoxFlat
        {
            BgColor = Panel,
            BorderColor = PanelBorder,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetColor("font_color", "Label", Text);

        var edit = (StyleBoxFlat)panel.Duplicate();
        edit.BgColor = new Color(0.02f, 0.03f, 0.05f, 0.92f);
        edit.ContentMarginTop = 6;
        edit.ContentMarginBottom = 6;
        theme.SetStylebox("normal", "LineEdit", edit);
        var editFocus = (StyleBoxFlat)edit.Duplicate();
        editFocus.BorderColor = Accent;
        theme.SetStylebox("focus", "LineEdit", editFocus);
        theme.SetColor("font_color", "LineEdit", Text);
        theme.SetColor("font_placeholder_color", "LineEdit", Dim);
        theme.SetColor("caret_color", "LineEdit", Accent);

        var button = (StyleBoxFlat)panel.Duplicate();
        button.BgColor = new Color(0.06f, 0.10f, 0.15f, 0.95f);
        button.ContentMarginTop = 5;
        button.ContentMarginBottom = 5;
        button.ContentMarginLeft = 10;
        button.ContentMarginRight = 10;
        var hover = (StyleBoxFlat)button.Duplicate();
        hover.BgColor = new Color(0.10f, 0.18f, 0.26f, 0.98f);
        hover.BorderColor = Accent;
        var pressed = (StyleBoxFlat)hover.Duplicate();
        pressed.BgColor = new Color(0.14f, 0.26f, 0.36f, 1f);
        theme.SetStylebox("normal", "Button", button);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", pressed);
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Accent);
        return theme;
    }

    /// <summary>Anchors a control to a corner or edge of its parent with a margin, growing inward.</summary>
    public static void Place(Control control, Control.LayoutPreset preset, int margin = 18)
    {
        control.SetAnchorsAndOffsetsPreset(preset, Control.LayoutPresetMode.Minsize, margin);
        control.GrowHorizontal = preset switch
        {
            Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterRight => Control.GrowDirection.Begin,
            Control.LayoutPreset.CenterTop or Control.LayoutPreset.CenterBottom or Control.LayoutPreset.Center => Control.GrowDirection.Both,
            _ => Control.GrowDirection.End,
        };
        control.GrowVertical = preset switch
        {
            Control.LayoutPreset.BottomLeft or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterBottom => Control.GrowDirection.Begin,
            _ => Control.GrowDirection.End,
        };
    }

    public static Label Label(string text, int size = 15, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue)
        {
            label.AddThemeColorOverride("font_color", color.Value);
        }
        return label;
    }

    /// <summary>A small caps heading with letter spacing, e.g. "GALAXY MAP".</summary>
    public static Label Heading(string text, int size = 12)
    {
        Label label = Label(text.ToUpperInvariant(), size, Accent);
        return label;
    }

    public static string Number(double value, string format = "N0") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    public static string Ly(double ly)
    {
        if (ly < 1e-3)
        {
            return "0 ly";
        }
        if (ly < 0.01)
        {
            return Number(ly * Units.MetresPerLightYear / Units.MetresPerAu, "N0") + " AU";
        }
        return ly < 10 ? Number(ly, "N2") + " ly" : ly < 1000 ? Number(ly, "N1") + " ly" : Number(ly, "N0") + " ly";
    }

    public static string Temperature(double kelvin) => Number(kelvin, "N0") + " K";

    public static string Distance(double metres)
    {
        double au = metres / Units.MetresPerAu;
        if (au >= 0.01)
        {
            return Number(au, au < 10 ? "N3" : "N1") + " AU";
        }
        return Number(metres / 1000.0, "N0") + " km";
    }

    public static string Duration(double seconds)
    {
        double abs = Math.Abs(seconds);
        string sign = seconds < 0 ? "−" : "";
        if (abs < 2 * 86400)
        {
            return sign + Number(abs / 3600.0, "N1") + " h";
        }
        if (abs < 2 * Units.SecondsPerJulianYear)
        {
            return sign + Number(abs / 86400.0, "N1") + " d";
        }
        return sign + Number(abs / Units.SecondsPerJulianYear, "N1") + " yr";
    }

    public static string Magnitude(double value) => value.ToString("+0.00;−0.00", CultureInfo.InvariantCulture);
}
