using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Common;

/// <summary>Text styles of the interface. Each one is a Label theme type variation of the same name.</summary>
public enum TextStyle
{
    Title,
    Heading,
    Subtitle,
    Body,
    Secondary,
    Caption,
    Overline,
    Key,
    Value,
    Keycap,
}

/// <summary>
/// The interface's design system: palette, type, spacing, theme and icons.
/// <para>
/// Everything is drawn at the screen's real pixel size. The UI scale follows the window (1.0 up to 1080 p, set by
/// <c>UiScaler</c>); every metric is rounded to whole pixels at that scale, fonts are rasterised at their final
/// size, icons are rasterised from SVG at their final size, and the theme is rebuilt when the scale changes. Nothing
/// is ever stretched, so text and edges stay crisp at any resolution.
/// </para>
/// </summary>
public static class Ui
{
    // ---------------------------------------------------------------- palette

    public static readonly Color Panel = new(0.031f, 0.043f, 0.063f, 0.88f);
    public static readonly Color PanelBorder = new(1f, 1f, 1f, 0.085f);
    public static readonly Color Field = new(0.015f, 0.022f, 0.034f, 0.92f);
    public static readonly Color Raised = new(1f, 1f, 1f, 0.055f);
    public static readonly Color RaisedHover = new(1f, 1f, 1f, 0.095f);
    public static readonly Color RaisedPressed = new(1f, 1f, 1f, 0.14f);
    public static readonly Color Divider = new(1f, 1f, 1f, 0.08f);
    public static readonly Color Text = new(0.92f, 0.945f, 0.97f);
    public static readonly Color Secondary = new(0.63f, 0.69f, 0.76f);
    public static readonly Color Tertiary = new(0.50f, 0.56f, 0.63f);
    public static readonly Color Accent = new(0.42f, 0.80f, 1.00f);
    public static readonly Color Warm = new(1.00f, 0.73f, 0.42f);
    public static readonly Color Shadow = new(0f, 0f, 0f, 0.38f);

    // ---------------------------------------------------------------- type ramp (pixels at scale 1: 900 p to 1080 p)

    private static readonly Dictionary<TextStyle, (float Size, Color Colour)> Ramp = new()
    {
        [TextStyle.Title] = (24, Text),
        [TextStyle.Heading] = (18, Text),
        [TextStyle.Subtitle] = (16, Warm),
        [TextStyle.Body] = (16, Text),
        [TextStyle.Secondary] = (15, Secondary),
        [TextStyle.Caption] = (14, Tertiary),
        [TextStyle.Overline] = (13, Accent),
        [TextStyle.Key] = (15, Secondary),
        [TextStyle.Value] = (15, Text),
        [TextStyle.Keycap] = (13, Text),
    };

    // ---------------------------------------------------------------- scale

    /// <summary>Interface scale: 1.0 up to 1080 p, 1.33 at 1440 p. Set by <c>UiScaler</c>.</summary>
    public static float Scale { get; private set; } = 1f;

    /// <summary>Raised after the scale (and with it the theme and icons) changed.</summary>
    public static event Action? ScaleChanged;

    /// <summary>A length in whole pixels at the current scale (0 stays 0; anything positive is at least 1).</summary>
    public static int Px(float basePixels) =>
        basePixels <= 0 ? 0 : Math.Max(1, (int)MathF.Round(basePixels * Scale));

    /// <summary>A length in pixels at the current scale, unrounded (for drawing in screen space).</summary>
    public static float Pxf(float basePixels) => basePixels * Scale;

    public static void SetScale(float scale)
    {
        scale = Math.Clamp(scale, 0.5f, 6f);
        if (_theme != null && MathF.Abs(scale - Scale) < 1e-4f)
        {
            return;
        }
        Scale = scale;
        if (_theme != null)
        {
            ApplyTheme(_theme);
        }
        UiIcons.Refresh();
        ScaleChanged?.Invoke();
        RunTracked();
    }

    // ---------------------------------------------------------------- fonts

    private const string FontPath = "res://assets/fonts/Inter/InterVariable.ttf";
    private static FontFile? _file;
    private static FontVariation? _regular;
    private static FontVariation? _medium;
    private static FontVariation? _semiBold;
    private static FontVariation? _display;
    private static FontVariation? _tabular;
    private static FontVariation? _overline;

    // Light text on a dark panel reads thinner than the same weight dark on light, so every weight sits a step above
    // the usual: body text is 520 rather than 400, values and buttons 600.

    /// <summary>Inter, weight 520: body text, labels of rows, captions.</summary>
    public static FontVariation Regular => _regular ??= Variation(520);

    /// <summary>Weight 600: values, subtitles, buttons and names on the maps.</summary>
    public static FontVariation Medium => _medium ??= Variation(600);

    /// <summary>Weight 680: headings, keycaps and the selected object's name on a map.</summary>
    public static FontVariation SemiBold => _semiBold ??= Variation(680);

    /// <summary>Inter Display (optical size 32): titles.</summary>
    public static FontVariation Display => _display ??= Variation(650, opticalSize: 32);

    /// <summary>Weight 600 with tabular figures, so numbers line up and don't jitter as they change.</summary>
    public static FontVariation Tabular => _tabular ??= Variation(600, features: new[] { "tnum" });

    /// <summary>Small capitals-style labels: bold, letter-spaced (the spacing follows the scale).</summary>
    public static FontVariation Overline => _overline ??= Variation(700);

    private static FontFile File
    {
        get
        {
            if (_file != null)
            {
                return _file;
            }
            _file = GD.Load<FontFile>(FontPath);
            // Crisp at UI sizes: real rasterisation (no distance field), light vertical hinting, quarter-pixel
            // horizontal positioning at small sizes, greyscale antialiasing (safe on any panel and any monitor).
            _file.MultichannelSignedDistanceField = false;
            _file.Hinting = TextServer.Hinting.Light;
            _file.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
            _file.Antialiasing = TextServer.FontAntialiasing.Gray;
            _file.GenerateMipmaps = false;
            _file.AllowSystemFallback = true;
            return _file;
        }
    }

    private static FontVariation Variation(int weight, int opticalSize = 0, string[]? features = null)
    {
        TextServer ts = TextServerManager.GetPrimaryInterface();
        var axes = new Godot.Collections.Dictionary { [ts.NameToTag("wght")] = weight };
        if (opticalSize > 0)
        {
            axes[ts.NameToTag("opsz")] = opticalSize;
        }
        var variation = new FontVariation { BaseFont = File, VariationOpentype = axes };
        if (features != null)
        {
            var dictionary = new Godot.Collections.Dictionary();
            foreach (string feature in features)
            {
                dictionary[ts.NameToTag(feature)] = 1;
            }
            variation.OpentypeFeatures = dictionary;
        }
        return variation;
    }

    /// <summary>The font of a text style.</summary>
    public static Font FontOf(TextStyle style) => style switch
    {
        TextStyle.Title => Display,
        TextStyle.Heading => SemiBold,
        TextStyle.Subtitle => Medium,
        TextStyle.Overline => Overline,
        TextStyle.Value => Tabular,
        TextStyle.Keycap => SemiBold,
        _ => Regular,
    };

    /// <summary>The size of a text style in pixels at the current scale.</summary>
    public static int SizeOf(TextStyle style) => Px(Ramp[style].Size);

    public static Color ColourOf(TextStyle style) => Ramp[style].Colour;

    // ---------------------------------------------------------------- theme

    private static Theme? _theme;

    /// <summary>The theme every interface root uses; rebuilt in place when the scale changes.</summary>
    public static Theme Theme
    {
        get
        {
            if (_theme == null)
            {
                _theme = new Theme();
                ApplyTheme(_theme);
            }
            return _theme;
        }
    }

    private static void ApplyTheme(Theme t)
    {
        Overline.SpacingGlyph = Math.Max(1, (int)MathF.Round(1.1f * Scale));
        t.DefaultFont = Regular;
        t.DefaultFontSize = SizeOf(TextStyle.Body);

        // Labels: the default is body text; each style is a type variation.
        t.SetFont("font", "Label", Regular);
        t.SetFontSize("font_size", "Label", SizeOf(TextStyle.Body));
        t.SetColor("font_color", "Label", Text);
        t.SetConstant("line_spacing", "Label", Px(2));
        foreach ((TextStyle style, (float _, Color colour)) in Ramp)
        {
            string name = StyleName(style);
            t.SetTypeVariation(name, "Label");
            t.SetFont("font", name, FontOf(style));
            t.SetFontSize("font_size", name, SizeOf(style));
            t.SetColor("font_color", name, colour);
        }

        // Cards.
        StyleBoxFlat card = Box(Panel, 10, PanelBorder, 16, 14);
        card.ShadowColor = Shadow;
        card.ShadowSize = Px(14);
        card.ShadowOffset = new Vector2(0, Px(3));
        t.SetStylebox("panel", "PanelContainer", card);
        t.SetTypeVariation("Keycap", "PanelContainer");
        t.SetStylebox("panel", "Keycap", Box(new Color(1f, 1f, 1f, 0.07f), 4, new Color(1f, 1f, 1f, 0.16f), 6, 1.5f));
        t.SetTypeVariation("Plain", "PanelContainer");
        t.SetStylebox("panel", "Plain", new StyleBoxEmpty());

        // Buttons: secondary (default), primary, list rows, icon buttons and segments.
        SetButton(t, "Button", Raised, RaisedHover, RaisedPressed, PanelBorder, Text, 7, 12, 7);
        t.SetFont("font", "Button", Medium);
        t.SetFontSize("font_size", "Button", SizeOf(TextStyle.Secondary));
        t.SetConstant("h_separation", "Button", Px(8));
        t.SetConstant("icon_max_width", "Button", 0);

        t.SetTypeVariation("PrimaryButton", "Button");
        SetButton(t, "PrimaryButton", new Color(Accent, 0.16f), new Color(Accent, 0.25f), new Color(Accent, 0.34f),
            new Color(Accent, 0.48f), new Color(0.80f, 0.93f, 1f), 7, 14, 9);

        t.SetTypeVariation("ListButton", "Button");
        SetButton(t, "ListButton", new Color(0, 0, 0, 0), RaisedHover, RaisedPressed, null, Text, 6, 10, 6);
        StyleBoxFlat selected = Box(new Color(Accent, 0.13f), 6, null, 10, 6);
        selected.BorderColor = Accent;
        selected.BorderWidthLeft = Math.Max(2, Px(2));
        t.SetStylebox("pressed", "ListButton", selected);
        t.SetStylebox("hover_pressed", "ListButton", selected);
        t.SetFont("font", "ListButton", Regular);
        t.SetFontSize("font_size", "ListButton", SizeOf(TextStyle.Secondary));

        t.SetTypeVariation("IconButton", "Button");
        SetButton(t, "IconButton", Raised, RaisedHover, RaisedPressed, PanelBorder, Text, 7, 8, 8);

        t.SetTypeVariation("SegmentButton", "Button");
        SetButton(t, "SegmentButton", new Color(0, 0, 0, 0), RaisedHover, new Color(Accent, 0.16f), null, Secondary, 6, 12, 7);
        t.SetStylebox("hover_pressed", "SegmentButton", Box(new Color(Accent, 0.22f), 6, null, 12, 7));
        t.SetColor("font_pressed_color", "SegmentButton", Text);
        t.SetColor("font_hover_pressed_color", "SegmentButton", Text);

        // Text fields.
        t.SetStylebox("normal", "LineEdit", Box(Field, 8, PanelBorder, 12, 10));
        t.SetStylebox("focus", "LineEdit", Box(new Color(0, 0, 0, 0), 8, new Color(Accent, 0.65f), 12, 10));
        t.SetStylebox("read_only", "LineEdit", Box(Field, 8, PanelBorder, 12, 10));
        t.SetFont("font", "LineEdit", Regular);
        t.SetFontSize("font_size", "LineEdit", SizeOf(TextStyle.Body));
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("font_placeholder_color", "LineEdit", Tertiary);
        t.SetColor("caret_color", "LineEdit", Accent);
        t.SetColor("selection_color", "LineEdit", new Color(Accent, 0.32f));
        t.SetColor("clear_button_color", "LineEdit", Secondary);
        t.SetColor("clear_button_color_pressed", "LineEdit", Text);
        t.SetConstant("caret_width", "LineEdit", Math.Max(1, Px(1.5f)));
        t.SetIcon("clear", "LineEdit", UiIcons.Get(UiIcon.Close, 14));

        // Separators and scrolling.
        t.SetConstant("separation", "HSeparator", Px(14));
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Divider, Thickness = Math.Max(1, (int)MathF.Round(Scale)) });
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        var track = new StyleBoxEmpty { ContentMarginLeft = Px(3), ContentMarginRight = Px(3) };
        t.SetStylebox("scroll", "VScrollBar", track);
        t.SetStylebox("scroll_focus", "VScrollBar", track);
        t.SetStylebox("grabber", "VScrollBar", Box(new Color(1f, 1f, 1f, 0.16f), 3, null, 0, 0));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(new Color(1f, 1f, 1f, 0.26f), 3, null, 0, 0));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(new Color(1f, 1f, 1f, 0.34f), 3, null, 0, 0));

        // Tooltips.
        t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.06f, 0.08f, 0.11f, 0.97f), 6, PanelBorder, 10, 6));
        t.SetFont("font", "TooltipLabel", Regular);
        t.SetFontSize("font_size", "TooltipLabel", SizeOf(TextStyle.Secondary));
        t.SetColor("font_color", "TooltipLabel", Text);
    }

    public static string StyleName(TextStyle style) => style + "Label";

    private static void SetButton(Theme t, string type, Color normal, Color hover, Color pressed, Color? border,
        Color font, float radius, float padH, float padV)
    {
        t.SetStylebox("normal", type, Box(normal, radius, border, padH, padV));
        t.SetStylebox("hover", type, Box(hover, radius, border, padH, padV));
        t.SetStylebox("pressed", type, Box(pressed, radius, border, padH, padV));
        t.SetStylebox("hover_pressed", type, Box(pressed, radius, border, padH, padV));
        t.SetStylebox("disabled", type, Box(new Color(normal, normal.A * 0.5f), radius, border, padH, padV));
        t.SetStylebox("focus", type, new StyleBoxEmpty());
        t.SetColor("font_color", type, font);
        t.SetColor("font_hover_color", type, Text);
        t.SetColor("font_pressed_color", type, Text);
        t.SetColor("font_hover_pressed_color", type, Text);
        t.SetColor("font_focus_color", type, font);
        t.SetColor("font_disabled_color", type, Tertiary);
        t.SetColor("icon_normal_color", type, font);
        t.SetColor("icon_hover_color", type, Text);
        t.SetColor("icon_pressed_color", type, Text);
        t.SetColor("icon_hover_pressed_color", type, Text);
        t.SetColor("icon_focus_color", type, font);
    }

    /// <summary>A rounded box with whole-pixel radius, border and padding at the current scale.</summary>
    public static StyleBoxFlat Box(Color background, float radius, Color? border, float padH, float padV)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            AntiAliasing = true,
            // A thin antialiasing band keeps rounded corners smooth without blurring the straight edges.
            AntiAliasingSize = 0.6f,
            CornerDetail = 8,
            ContentMarginLeft = Px(padH),
            ContentMarginRight = Px(padH),
            ContentMarginTop = Px(padV),
            ContentMarginBottom = Px(padV),
        };
        box.SetCornerRadiusAll(Px(radius));
        if (border.HasValue)
        {
            box.BorderColor = border.Value;
            box.SetBorderWidthAll(Math.Max(1, (int)MathF.Round(Scale)));
        }
        return box;
    }

    // ---------------------------------------------------------------- building blocks

    public static Label Label(string text, TextStyle style = TextStyle.Body)
    {
        return new Label
        {
            Text = style == TextStyle.Overline ? text.ToUpperInvariant() : text,
            ThemeTypeVariation = StyleName(style),
        };
    }

    /// <summary>A label in a style with its own colour.</summary>
    public static Label Label(string text, TextStyle style, Color colour)
    {
        Label label = Label(text, style);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    /// <summary>A card: a panel with a vertical stack inside.</summary>
    public static (PanelContainer Card, VBoxContainer Content) Card(float spacing = 6)
    {
        var card = new PanelContainer();
        var content = new VBoxContainer();
        Track(content, c => c.AddThemeConstantOverride("separation", Px(spacing)));
        card.AddChild(content);
        return (card, content);
    }

    public static HSeparator Separator() => new();

    /// <summary>A two-column grid of key/value rows: labels on the left, each value right beside its label.</summary>
    public static GridContainer Rows()
    {
        var grid = new GridContainer { Columns = 2 };
        Track(grid, g =>
        {
            g.AddThemeConstantOverride("h_separation", Px(16));
            g.AddThemeConstantOverride("v_separation", Px(6));
        });
        return grid;
    }

    /// <summary>
    /// Adds a key/value row and returns the value's label. Numeric values use tabular figures; text values use the
    /// same weight without them and wrap if they don't fit beside the label.
    /// </summary>
    public static Label AddRow(GridContainer rows, string key, string value = "", bool numeric = true)
    {
        Label k = Label(key, TextStyle.Key);
        k.VerticalAlignment = VerticalAlignment.Top;
        rows.AddChild(k);
        Label v = Label(value, TextStyle.Value);
        if (!numeric)
        {
            v.AddThemeFontOverride("font", Medium);
        }
        v.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        v.AutowrapMode = numeric ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart;
        rows.AddChild(v);
        return v;
    }

    /// <summary>A keyboard or mouse hint: each key as a keycap, then what it does.</summary>
    public static HBoxContainer KeyHint(string keys, string action)
    {
        var row = new HBoxContainer();
        Track(row, r => r.AddThemeConstantOverride("separation", Px(5)));
        foreach (string key in keys.Split('|'))
        {
            var cap = new PanelContainer { ThemeTypeVariation = "Keycap" };
            cap.AddChild(Label(key, TextStyle.Keycap));
            row.AddChild(cap);
        }
        Label what = Label(action, TextStyle.Caption, Secondary);
        what.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(what);
        return row;
    }

    /// <summary>A strip of key hints that wraps when the window is narrow.</summary>
    public static HFlowContainer KeyHints(params (string Keys, string Action)[] hints)
    {
        var flow = new HFlowContainer();
        Track(flow, f =>
        {
            f.AddThemeConstantOverride("h_separation", Px(16));
            f.AddThemeConstantOverride("v_separation", Px(6));
        });
        foreach ((string keys, string action) in hints)
        {
            flow.AddChild(KeyHint(keys, action));
        }
        return flow;
    }

    // ---------------------------------------------------------------- layout that follows the scale

    private static readonly List<(WeakReference<Control> Control, Action<Control> Apply)> Tracked = new();

    /// <summary>Runs <paramref name="apply"/> now and again whenever the scale changes (while the control lives).</summary>
    public static void Track<T>(T control, Action<T> apply) where T : Control
    {
        apply(control);
        Tracked.Add((new WeakReference<Control>(control), c => apply((T)c)));
        // Lists rebuilt again and again (search results, body rows) would otherwise pile up dead entries.
        if (Tracked.Count % 512 == 0)
        {
            ForEachTracked(apply: false);
        }
    }

    private static void RunTracked() => ForEachTracked(apply: true);

    // Drops entries whose control is gone; applies the rest when asked.
    private static void ForEachTracked(bool apply)
    {
        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            (WeakReference<Control> reference, Action<Control> action) = Tracked[i];
            if (reference.TryGetTarget(out Control? control) && GodotObject.IsInstanceValid(control))
            {
                if (apply)
                {
                    action(control);
                }
            }
            else
            {
                Tracked.RemoveAt(i);
            }
        }
    }

    /// <summary>A minimum size in base pixels that follows the scale (0 leaves that axis free).</summary>
    public static void Size(Control control, float width, float height = 0) =>
        Track(control, c => c.CustomMinimumSize = new Vector2(Px(width), Px(height)));

    /// <summary>Anchors a control to a corner or edge of its parent with a margin, growing inward.</summary>
    public static void Place(Control control, Control.LayoutPreset preset, float margin = 20)
    {
        control.GrowHorizontal = preset switch
        {
            Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterRight
                or Control.LayoutPreset.RightWide => Control.GrowDirection.Begin,
            Control.LayoutPreset.CenterTop or Control.LayoutPreset.CenterBottom or Control.LayoutPreset.Center => Control.GrowDirection.Both,
            _ => Control.GrowDirection.End,
        };
        control.GrowVertical = preset switch
        {
            Control.LayoutPreset.BottomLeft or Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterBottom
                or Control.LayoutPreset.BottomWide => Control.GrowDirection.Begin,
            _ => Control.GrowDirection.End,
        };
        Track(control, c => c.SetAnchorsAndOffsetsPreset(preset, Control.LayoutPresetMode.Minsize, Px(margin)));
    }

    // ---------------------------------------------------------------- formatting

    /// <summary>A number with a real minus sign (U+2212) rather than a hyphen.</summary>
    public static string Number(double value, string format = "N0") =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('-', '−');

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

    /// <summary>An age given in Gyr: "4.57 billion years", "120 million years".</summary>
    public static string Age(double gyr) => gyr >= 1
        ? Number(gyr, gyr < 10 ? "N2" : "N1") + " billion years"
        : Number(gyr * 1000, gyr < 0.01 ? "N1" : "N0") + " million years";

    /// <summary>A multiple of the Sun or the Earth, e.g. "1.02 × Sun".</summary>
    public static string Times(double value, string of, string format = "N2") => Number(value, format) + " × " + of;

    // ---------------------------------------------------------------- the ground game's style (GROUND.md §8)

    /// <summary>
    /// The ground game's interface style: RustMVP's interface, matched exactly (its palette, Barlow type and 1600 × 900
    /// design canvas). It sits beside the map styles above and changes none of them. Lengths still go through
    /// <see cref="Px"/>, so the ground screens are as crisp as the maps at any scale.
    /// </summary>
    public static class Ground
    {
        // Colours as authored in sRGB in RustMVP (unreal/Bare/Source/Bare/UI/SBareRoot.cpp).
        public static readonly Color Ink = new(0.89f, 0.875f, 0.84f);
        public static readonly Color Muted = new(0.635f, 0.667f, 0.663f);
        public static readonly Color Red = new(0.725f, 0.286f, 0.212f);
        public static readonly Color Green = new(0.596f, 0.725f, 0.337f);
        public static readonly Color Panel = new(0.71f, 0.69f, 0.65f, 0.17f);
        public static readonly Color PanelHover = new(0.75f, 0.73f, 0.69f, 0.26f);
        public static readonly Color HudSlot = new(0.16f, 0.17f, 0.16f, 0.38f);
        public static readonly Color Selected = new(0.16f, 0.48f, 0.67f, 0.76f);
        public static readonly Color Category = new(0.18f, 0.53f, 0.74f, 0.8f);
        public static readonly Color Health = new(0.553f, 0.722f, 0.247f);
        public static readonly Color Water = new(0.322f, 0.62f, 0.8f);
        public static readonly Color Food = new(0.737f, 0.455f, 0.224f);
        public static readonly Color Ivory = new(0.97f, 0.935f, 0.89f);
        public static readonly Color Brick = new(0.76f, 0.235f, 0.17f);
        public static readonly Color Short = new(0.84f, 0.66f, 0.30f);
        public static readonly Color Warning = new(1f, 0.60f, 0.43f);
        public static readonly Color Tooltip = new(0.125f, 0.153f, 0.157f, 0.98f);
        public static readonly Color Toast = new(0.733f, 0.769f, 0.765f);
        public static readonly Color Star = new(0.84f, 0.77f, 0.27f);
        public static readonly Color Dialog = new(0.10f, 0.12f, 0.13f, 0.96f);
        public static readonly Color Entry = new(0.13f, 0.16f, 0.14f, 0.8f);
        public static readonly Color EntryFocus = new(0.21f, 0.25f, 0.21f, 0.9f);

        private const string Folder = "res://assets/fonts/Barlow/";
        private static FontFile? _medium;
        private static FontFile? _semiBold;
        private static FontFile? _condensed;
        private static FontFile? _condensedMedium;

        /// <summary>Barlow Medium: regular text.</summary>
        public static FontFile Medium => _medium ??= Load("Barlow-Medium.ttf");

        /// <summary>Barlow SemiBold: bold text below 26 px.</summary>
        public static FontFile SemiBold => _semiBold ??= Load("Barlow-SemiBold.ttf");

        /// <summary>Barlow Condensed Bold: headings, buttons, and bold text from 26 px.</summary>
        public static FontFile Condensed => _condensed ??= Load("BarlowCondensed-Bold.ttf");

        /// <summary>Barlow Condensed Medium: small condensed hints.</summary>
        public static FontFile CondensedMedium => _condensedMedium ??= Load("BarlowCondensed-Medium.ttf");

        /// <summary>
        /// RustMVP's type rule: regular text in Barlow Medium; bold in SemiBold below 26 px and in Condensed Bold from
        /// 26 px; condensed text in Condensed Bold.
        /// </summary>
        public static FontFile Font(float size, bool bold = false, bool condensed = false) =>
            condensed ? Condensed : bold ? (size >= 26 ? Condensed : SemiBold) : Medium;

        // The same crisp rasterisation as the map fonts: no distance field, light hinting, greyscale antialiasing.
        private static FontFile Load(string file)
        {
            FontFile font = GD.Load<FontFile>(Folder + file);
            font.MultichannelSignedDistanceField = false;
            font.Hinting = TextServer.Hinting.Light;
            font.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
            font.Antialiasing = TextServer.FontAntialiasing.Gray;
            font.GenerateMipmaps = false;
            font.AllowSystemFallback = true;
            return font;
        }

        /// <summary>
        /// Styles a text field as RustMVP's: a flat fill (another when focused), Barlow Medium in ink, padding
        /// 14 × 3 px. Everything follows the scale.
        /// </summary>
        public static void Field(LineEdit field, float fontSize, Color fill, Color focused)
        {
            Track(field, f =>
            {
                StyleBoxFlat Fill(Color colour) => new()
                {
                    BgColor = colour,
                    ContentMarginLeft = Px(14),
                    ContentMarginRight = Px(14),
                    ContentMarginTop = Px(3),
                    ContentMarginBottom = Px(3),
                };
                f.AddThemeStyleboxOverride("normal", Fill(fill));
                f.AddThemeStyleboxOverride("read_only", Fill(fill));
                f.AddThemeStyleboxOverride("focus", Fill(focused));
                f.AddThemeFontOverride("font", Medium);
                f.AddThemeFontSizeOverride("font_size", Px(fontSize));
                f.AddThemeColorOverride("font_color", Ink);
                f.AddThemeColorOverride("font_placeholder_color", Muted);
                f.AddThemeColorOverride("caret_color", Ink);
                f.AddThemeColorOverride("selection_color", new Color(Selected, 0.55f));
                f.AddThemeConstantOverride("caret_width", Math.Max(1, Px(1.5f)));
            });
        }
    }
}
