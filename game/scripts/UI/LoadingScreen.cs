using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client.Loading;
using HollowUniverse.Common;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

/// <summary>
/// The loading screen (GROUND.md §4.3.1, §8): a picture of the first world filling the window, the game's name, the
/// words of the stage the loading pipeline is in, one bar for the whole load and a tip, in the ground interface's style
/// (RustMVP's palette and Barlow, painted on the 1600 × 900 design canvas through <see cref="Ui.Px"/>, so it is crisp at
/// any size). It draws <see cref="LoadingPipeline.Progress"/> and <see cref="LoadingPipeline.Current"/> and nothing else
/// of the game: which parts load is the pipeline's. While a load runs it takes every input event, so nothing under it
/// acts; when play is handed over it fades out. An arrival (a respawn, a far teleport) shows it again, shorter: after
/// the plan's grace, without the title block and the tip.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    /// <summary>How fast the bar catches up with the pipeline (per second, exponential).</summary>
    private const float BarRate = 6f;

    private Painter _painter = null!;
    private Texture2D? _picture;
    private float _shown;
    private float _alpha;
    private double _clock;
    private int _tip;
    private double _tipAt;

    /// <summary>The load to show; set before the node enters the tree.</summary>
    public LoadingPipeline Pipeline { get; set; } = null!;

    /// <summary>Where the game is going (the host's game on this computer, an address), under the game's name.</summary>
    public string Destination { get; set; } = "";

    /// <summary>The screen is up (or still fading): the world under it isn't the player's to see yet.</summary>
    public bool Shown => Visible;

    public override void _Ready()
    {
        Layer = 100;
        _painter = new Painter
        {
            Name = "Screen",
            Paint = Paint,
            // The picture is drawn smaller than its pixels on most windows: its mipmaps keep the grass from shimmering.
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        AddChild(_painter);
        _painter.MouseFilter = Control.MouseFilterEnum.Stop;
        string path = Pipeline.Plan.Picture;
        _picture = path.Length > 0 && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        _tip = Pipeline.Plan.Tips.Count > 0 ? (int)(Time.GetTicksUsec() % (ulong)Pipeline.Plan.Tips.Count) : 0;
        _alpha = Pipeline.ShowScreen ? 1 : 0;
        Visible = _alpha > 0;
    }

    public override void _Process(double delta)
    {
        if (_alpha <= 0 && !Pipeline.ShowScreen)
        {
            // Nothing shows and nothing loads: nothing to do.
            Visible = false;
            return;
        }
        _clock += delta;
        float dt = (float)Math.Min(delta, 0.1);
        LoadingPlan plan = Pipeline.Plan;
        // In over a third of the fade (an arrival), out over the whole fade once play is the player's.
        float target = Pipeline.ShowScreen ? 1 : 0;
        float rate = 1f / (float)Math.Max(plan.FadeSeconds * (target > _alpha ? 0.35 : 1), 1e-3);
        _alpha = Mathf.MoveToward(_alpha, target, dt * rate);
        if (Pipeline.ShowScreen && _alpha <= 0.001f)
        {
            // A new load: the bar starts from where it is.
            _shown = 0;
        }
        _shown = Math.Max(_shown, Mathf.Lerp(_shown, (float)Pipeline.Progress, 1 - MathF.Exp(-BarRate * dt)));
        if (!Pipeline.Blocking)
        {
            // Play is handed over: the bar shows full while the screen fades.
            _shown = Mathf.MoveToward(_shown, 1, dt * 4);
        }
        if (_clock - _tipAt >= plan.TipSeconds && plan.Tips.Count > 0)
        {
            _tipAt = _clock;
            _tip = (_tip + 1) % plan.Tips.Count;
        }
        Visible = _alpha > 0;
        _painter.Modulate = new Color(1, 1, 1, _alpha);
        _painter.MouseFilter = Pipeline.Blocking ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _painter.QueueRedraw();
    }

    // While a load runs, nothing under the screen gets an input event (the game's own keys and the interface's).
    public override void _Input(InputEvent e)
    {
        if (Pipeline.Blocking)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------------------------------------------------------- painting

    private void Paint(Painter p)
    {
        Vector2 size = p.Size;
        PaintPicture(p, size);
        // A dark band rising from the bottom edge for the words, and a little weight on the left (RustMVP's menu).
        Color clear = new(0, 0, 0, 0), dark = new(0, 0, 0, 0.78f);
        float band = MathF.Round(size.Y * 0.55f);
        p.DrawPolygon(new[] { new Vector2(0, size.Y - band), new Vector2(size.X, size.Y - band), size, new Vector2(0, size.Y) },
            new[] { clear, clear, dark, dark });
        Color left = new(0, 0, 0, 0.32f);
        p.DrawPolygon(new[] { Vector2.Zero, new Vector2(size.X * 0.6f, 0), new Vector2(size.X * 0.6f, size.Y), new Vector2(0, size.Y) },
            new[] { left, clear, clear, left });

        // The design canvas sits on the window's bottom-left corner; the bar runs to the right edge's margin.
        var o = new Vector2(0, size.Y - Ui.Px(900));
        bool arrival = Pipeline.IsArrival;
        if (!arrival)
        {
            // The menu's title block (GroundUi.Menu), and where the game is going.
            Rect(p, o, 104, 586, 52, 5, G.Red);
            Text(p, o, "HOLLOW", 96, 595, 96, G.Ink, condensed: true);
            Text(p, o, Destination.ToUpperInvariant(), 106, 706, 20, G.Muted, condensed: true);
        }
        string stage = (Pipeline.Current?.Title ?? (Pipeline.Blocking ? "" : "Ready")).ToUpperInvariant();
        float right = size.X - Ui.Px(105);
        float barLeft = o.X + Ui.Px(105), barTop = o.Y + Ui.Px(802), barHeight = Ui.Px(6);
        Text(p, o, stage, 105, 756, 30, G.Ink, condensed: true);
        string percent = $"{Math.Round(_shown * 100):0}%";
        TextRight(p, percent, right, o.Y + Ui.Px(756), 30, G.Ink);
        // The bar: RustMVP's grey panel, the done part in ink, a red edge that breathes while the load runs.
        p.DrawRect(new Rect2(barLeft, barTop, right - barLeft, barHeight), G.Panel);
        float filled = MathF.Round((right - barLeft) * Math.Clamp(_shown, 0, 1));
        if (filled > 0)
        {
            p.DrawRect(new Rect2(barLeft, barTop, filled, barHeight), new Color(G.Ink, 0.92f));
        }
        if (Pipeline.Blocking)
        {
            float pulse = 0.55f + 0.45f * MathF.Sin((float)_clock * 5f);
            float edge = Ui.Px(3);
            p.DrawRect(new Rect2(barLeft + Math.Max(0, filled - edge), barTop - Ui.Px(2), edge, barHeight + Ui.Px(4)), new Color(G.Red, pulse));
        }
        IReadOnlyList<string> tips = Pipeline.Plan.Tips;
        if (!arrival && tips.Count > 0)
        {
            float width = Text(p, o, "TIP", 105, 830, 19, G.Red, condensed: true);
            Text(p, o, tips[_tip % tips.Count], 105 + width + 12, 830, 19, G.Muted);
        }
    }

    // The picture fills the window, cropped to its shape, centred.
    private void PaintPicture(Painter p, Vector2 size)
    {
        if (_picture == null)
        {
            p.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0.06f, 0.06f));
            return;
        }
        Vector2 source = _picture.GetSize();
        float scale = Math.Max(size.X / source.X, size.Y / source.Y);
        Vector2 seen = size / scale;
        var region = new Rect2((source - seen) / 2, seen);
        p.DrawTextureRectRegion(_picture, new Rect2(Vector2.Zero, size), region);
    }

    private static void Rect(Painter p, Vector2 o, float x, float y, float w, float h, Color colour) =>
        p.DrawRect(new Rect2(o + new Vector2(Ui.Px(x), Ui.Px(y)), new Vector2(Ui.Px(w), Ui.Px(h))), colour);

    /// <summary>Text with its line's top at a design point; returns its width in design pixels.</summary>
    private static float Text(Painter p, Vector2 o, string text, float x, float y, float size, Color colour, bool condensed = false)
    {
        if (text.Length == 0)
        {
            return 0;
        }
        FontFile font = G.Font(size, bold: condensed, condensed: condensed);
        int px = Ui.Px(size);
        Vector2 top = o + new Vector2(Ui.Px(x), Ui.Px(y));
        p.DrawString(font, new Vector2(top.X, MathF.Round(top.Y + font.GetAscent(px))), text, HorizontalAlignment.Left, -1, px, colour);
        return font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X / Ui.Scale;
    }

    /// <summary>Condensed text ending at <paramref name="right"/> (screen pixels), its line's top at <paramref name="top"/>.</summary>
    private static void TextRight(Painter p, string text, float right, float top, float size, Color colour)
    {
        FontFile font = G.Font(size, bold: true, condensed: true);
        int px = Ui.Px(size);
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X;
        p.DrawString(font, new Vector2(MathF.Round(right - width), MathF.Round(top + font.GetAscent(px))), text, HorizontalAlignment.Left, -1, px, colour);
    }
}
