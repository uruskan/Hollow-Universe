using System;
using Godot;
using HollowUniverse.Common;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.Build;

/// <summary>
/// The build sandbox's lines over the ground HUD, in RustMVP's manner (GROUND.md §6.3): under the crosshair the target
/// ("STONE WALL · 500 / 500 · 100% STABLE") or the plan's piece and cost, the reason in amber when it can't be placed;
/// the held tool's keys above the belt; the stability legend while the overlay is on; and, top left, what the frame
/// costs. Drawn on the 1600 × 900 design canvas through <see cref="Ui.Px"/>, centred like the wheels.
/// </summary>
public partial class BuildHud : Control
{
    /// <summary>The first line under the crosshair (target or piece), or null.</summary>
    public string? Line { get; set; }

    /// <summary>The second line: a refusal (amber) or a note.</summary>
    public string? Reason { get; set; }

    public bool ReasonIsWarning { get; set; } = true;

    /// <summary>The held tool's keys.</summary>
    public string? Hints { get; set; }

    public bool Legend { get; set; }

    /// <summary>Top-left statistics (pieces, draw calls, frame time), or null.</summary>
    public string? Stats { get; set; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    private Vector2 Centre => new(MathF.Floor(Size.X / 2) - Ui.Px(800), MathF.Floor(Size.Y / 2) - Ui.Px(450));

    private Vector2 Bottom => new(MathF.Floor((Size.X - Ui.Px(1600)) / 2), Size.Y - Ui.Px(900));

    public override void _Draw()
    {
        if (Line != null)
        {
            CentreText(Centre, Line, 586, 24, G.Ink, bold: true);
        }
        if (Reason != null)
        {
            CentreText(Centre, Reason, 618, 20, ReasonIsWarning ? G.Warning : G.Muted, bold: false);
        }
        if (Hints != null)
        {
            CentreText(Bottom, Hints, 760, 18, new Color(G.Ink, 0.85f), bold: true);
        }
        if (Legend)
        {
            var origin = new Vector2(Size.X - Ui.Px(20), Ui.Px(20));
            string[] labels = { "100%", "50%", "0%" };
            Color[] colours = { new(0.16f, 0.62f, 0.18f), new(0.88f, 0.68f, 0.08f), new(0.80f, 0.12f, 0.08f) };
            Font font = G.Font(20, bold: true);
            float x = origin.X;
            for (int i = labels.Length - 1; i >= 0; i--)
            {
                float w = font.GetStringSize(labels[i], HorizontalAlignment.Left, -1, Ui.Px(20)).X;
                x -= w;
                DrawString(font, new Vector2(x, origin.Y + Ui.Px(20)), labels[i], HorizontalAlignment.Left, -1, Ui.Px(20), G.Ink);
                x -= Ui.Px(26);
                DrawRect(new Rect2(x, origin.Y + Ui.Px(4), Ui.Px(18), Ui.Px(18)), colours[i]);
                x -= Ui.Px(16);
            }
            string title = "STABILITY";
            float tw = font.GetStringSize(title, HorizontalAlignment.Left, -1, Ui.Px(20)).X;
            DrawString(font, new Vector2(x - tw, origin.Y + Ui.Px(20)), title, HorizontalAlignment.Left, -1, Ui.Px(20), G.Ink);
        }
        if (Stats != null)
        {
            Font font = G.Font(19, bold: true);
            float y = Ui.Px(20);
            foreach (string line in Stats.Split('\n'))
            {
                DrawString(font, new Vector2(Ui.Px(20) + 1, y + Ui.Px(19) + 1), line, HorizontalAlignment.Left, -1, Ui.Px(19), new Color(0, 0, 0, 0.6f));
                DrawString(font, new Vector2(Ui.Px(20), y + Ui.Px(19)), line, HorizontalAlignment.Left, -1, Ui.Px(19), G.Ink);
                y += Ui.Px(26);
            }
        }
    }

    // Centred on the design canvas's x = 800, its top at design y, with a soft shadow for light backgrounds.
    private void CentreText(Vector2 frame, string text, float y, float size, Color colour, bool bold)
    {
        int px = Ui.Px(size);
        Font font = G.Font(size, bold);
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X;
        var at = new Vector2(frame.X + Ui.Px(800) - MathF.Round(width / 2), MathF.Round(frame.Y + Ui.Px(y) + font.GetAscent(px)));
        // A soft dark edge as well as the shadow, so a line reads over a placement ghost's bright hologram.
        DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, px, Ui.Px(5), new Color(0, 0, 0, 0.75f));
        DrawString(font, at + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, px, new Color(0, 0, 0, 0.55f));
        DrawString(font, at, text, HorizontalAlignment.Left, -1, px, colour);
    }
}
