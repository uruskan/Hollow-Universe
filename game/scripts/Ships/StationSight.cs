using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.Ships;

/// <summary>One barrel on the sight: loaded with a round (its icon), loading (its progress) or empty.</summary>
public readonly record struct SightBarrel(bool Loaded, string? Icon, float Progress, bool Loading);

/// <summary>One of the battery's kinds on the sight: its icon, how many the magazine holds, chosen or not.</summary>
public readonly record struct SightKind(string Icon, string Name, int Count, bool Chosen);

/// <summary>What the gunner's sight shows this frame (the ground view fills it, IW3, IRON_WAKE.md §6.8).</summary>
public sealed class StationSightData
{
    public bool Show;

    /// <summary>Where the barrels point on the screen (pixels of the viewport; null behind the camera).</summary>
    public Vector2? Ring;

    /// <summary>The chosen rocket's path from the muzzle: a point on the screen every 100 m, and its metres.</summary>
    public readonly List<(Vector2 At, int Metres)> Marks = new();

    public readonly List<SightBarrel> Barrels = new();

    public readonly List<SightKind> Kinds = new();

    /// <summary>The battery's name, the refusal line (§6.7), or null.</summary>
    public string Name = "";

    public string? Refusal;

    /// <summary>The yaw arcs' dial (degrees from the bow, + to port): shown near or past a limit.</summary>
    public bool Dial;

    public float DialMin, DialMax, DialYaw, DialAsked;

    /// <summary>The ship's heading (degrees from north toward east) and speed (m/s).</summary>
    public float Heading, Speed;

    /// <summary>The zoom now (1: none).</summary>
    public float Zoom = 1;
}

/// <summary>
/// The gunner's sight (IW3, IRON_WAKE.md §6.8), in the ground interface's style (<see cref="Ui.Ground"/>, its 1600 × 900
/// design canvas, every length through <see cref="Ui.Px"/>): the cross where the gunner aims (the view's middle), a ring
/// where the barrels point (the two part while the mount slews or stands at a limit), the chosen rocket's range marks every
/// 100 m from its launch profile, each barrel's state, the chosen kind and the magazine's counts, the refusal line, a
/// small dial of the arcs near a limit, and the ship's heading and speed at the side. Painted in immediate mode.
/// </summary>
public partial class StationSight : Control
{
    private Vector2 _o;

    public StationSightData Data { get; } = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public void Refresh() => QueueRedraw();

    private Vector2 CentreFrame => new(MathF.Floor(Size.X / 2) - Ui.Px(800), MathF.Floor(Size.Y / 2) - Ui.Px(450));

    private Vector2 CanvasFrame => new(MathF.Floor((Size.X - Ui.Px(1600)) / 2), Size.Y - Ui.Px(900));

    private static int S(float v) => v < 0 ? -Ui.Px(-v) : Ui.Px(v);

    private Vector2 At(float x, float y) => _o + new Vector2(S(x), S(y));

    private Vector2 Pt(float x, float y) => _o + new Vector2(x, y) * Ui.Scale;

    private static readonly Color ShadowColour = new(0, 0, 0, 0.55f);

    private void TextIn(string text, float x, float y, float w, float size, Color colour, HorizontalAlignment align, bool bold = true)
    {
        FontFile font = G.Font(size, bold, true);
        int px = Ui.Px(size);
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X;
        Vector2 top = At(x, y);
        float left = align switch
        {
            HorizontalAlignment.Center => top.X + MathF.Floor((Ui.Px(w) - width) / 2),
            HorizontalAlignment.Right => top.X + Ui.Px(w) - MathF.Ceiling(width),
            _ => top.X,
        };
        var at = new Vector2(left, MathF.Round(top.Y + font.GetAscent(px)));
        DrawString(font, at + Vector2.One * Math.Max(1, Ui.Px(1.5f)), text, HorizontalAlignment.Left, -1, px, ShadowColour);
        DrawString(font, at, text, HorizontalAlignment.Left, -1, px, colour);
    }

    private void TextAt(string text, Vector2 at, float size, Color colour)
    {
        FontFile font = G.Font(size, true, true);
        int px = Ui.Px(size);
        var baseline = new Vector2(MathF.Round(at.X), MathF.Round(at.Y + font.GetAscent(px) / 2));
        DrawString(font, baseline + Vector2.One * Math.Max(1, Ui.Px(1.5f)), text, HorizontalAlignment.Left, -1, px, ShadowColour);
        DrawString(font, baseline, text, HorizontalAlignment.Left, -1, px, colour);
    }

    private void Icon(string key, float x, float y, float size, float alpha = 1)
    {
        int px = Ui.Px(size);
        if (ItemIcons.Get(key, px) is Texture2D texture)
        {
            DrawTextureRect(texture, new Rect2(At(x, y), new Vector2(px, px)), false, new Color(1, 1, 1, alpha));
        }
    }

    private float Line => Math.Max(1f, Ui.Pxf(1.5f));

    private static string N(float v, string format = "0") => v.ToString(format, CultureInfo.InvariantCulture);

    public override void _Draw()
    {
        StationSightData d = Data;
        if (!d.Show)
        {
            return;
        }
        PaintCross(d);
        PaintMarks(d);
        PaintBarrels(d);
        PaintDial(d);
        PaintSide(d);
    }

    // The cross where the gunner aims (the view's middle), and the ring where the barrels point.
    private void PaintCross(StationSightData d)
    {
        _o = CentreFrame;
        Color ink = new(G.Ink, 0.92f);
        const float gap = 6, arm = 16;
        DrawLine(Pt(800 - gap - arm, 450), Pt(800 - gap, 450), ink, Line, true);
        DrawLine(Pt(800 + gap, 450), Pt(800 + gap + arm, 450), ink, Line, true);
        DrawLine(Pt(800, 450 - gap - arm), Pt(800, 450 - gap), ink, Line, true);
        DrawLine(Pt(800, 450 + gap), Pt(800, 450 + gap + arm), ink, Line, true);
        if (d.Ring is { } ring)
        {
            Color colour = d.Refusal != null ? new Color(G.Warning, 0.9f) : new Color(G.Green, 0.95f);
            DrawArc(ring, Ui.Pxf(13), 0, MathF.Tau, 40, colour, Line, true);
            DrawCircle(ring, Ui.Pxf(1.6f), colour);
        }
        if (d.Refusal != null)
        {
            TextIn(d.Refusal, 0, 500, 1600, 22, G.Warning, HorizontalAlignment.Center);
        }
    }

    // The chosen rocket's fall: a short tick and the metres every 100 m along its path.
    private void PaintMarks(StationSightData d)
    {
        Color ink = new(G.Ink, 0.75f);
        foreach ((Vector2 at, int metres) in d.Marks)
        {
            float half = Ui.Pxf(metres % 500 == 0 ? 9 : 5);
            DrawLine(at - new Vector2(half, 0), at + new Vector2(half, 0), ink, Line, true);
            if (metres % 200 == 0)
            {
                TextAt(N(metres), at + new Vector2(half + Ui.Pxf(5), 0), 14, ink);
            }
        }
    }

    // Bottom middle: the battery's name and zoom, each barrel's state, the kinds and the magazine's counts.
    private void PaintBarrels(StationSightData d)
    {
        _o = CanvasFrame;
        const float cell = 58, gap = 6;
        float width = d.Barrels.Count * cell + Math.Max(0, d.Barrels.Count - 1) * gap;
        float x = 800 - width / 2, y = 900 - 150;
        TextIn(d.Name + (d.Zoom > 1.01f ? "   ×" + N(d.Zoom) : ""), 0, y - 30, 1600, 18, G.Ink, HorizontalAlignment.Center);
        foreach (SightBarrel b in d.Barrels)
        {
            DrawRect(new Rect2(At(x, y), new Vector2(Ui.Px(cell), Ui.Px(cell))), G.HudSlot);
            if (b.Loaded && b.Icon != null)
            {
                Icon(b.Icon, x + 5, y + 5, cell - 10);
            }
            else if (b.Loading)
            {
                if (b.Icon != null)
                {
                    Icon(b.Icon, x + 5, y + 5, cell - 10, 0.3f);
                }
                DrawRect(new Rect2(At(x, y + cell - 6), new Vector2(Ui.Px(cell * Math.Clamp(b.Progress, 0, 1)), Ui.Px(6))), G.Short);
            }
            else
            {
                TextIn("EMPTY", x, y + cell / 2 - 9, cell, 13, G.Muted, HorizontalAlignment.Center);
            }
            x += cell + gap;
        }
        // The kinds, the chosen one marked.
        const float kind = 40;
        float kx = 800 - (d.Kinds.Count * (kind + 46)) / 2, ky = y + cell + 10;
        foreach (SightKind k in d.Kinds)
        {
            if (k.Chosen)
            {
                DrawRect(new Rect2(At(kx - 3, ky - 3), new Vector2(Ui.Px(kind + 46), Ui.Px(kind + 6))), G.Selected);
            }
            Icon(k.Icon, kx, ky, kind, k.Count > 0 ? 1 : 0.35f);
            TextIn(N(k.Count), kx + kind + 4, ky + kind / 2 - 10, 36, 18, k.Count > 0 ? G.Ink : G.Muted, HorizontalAlignment.Left);
            kx += kind + 46;
        }
    }

    // The arcs' dial, near a limit: the arc the yaw turns through, where it points, and where the gunner asks.
    private void PaintDial(StationSightData d)
    {
        if (!d.Dial)
        {
            return;
        }
        _o = CanvasFrame;
        Vector2 centre = Pt(800 - 330, 900 - 100);
        float radius = Ui.Pxf(44);
        float Angle(float yaw) => -MathF.PI / 2 - yaw * MathF.PI / 180;
        DrawArc(centre, radius, Angle(d.DialMax), Angle(d.DialMin), 48, new Color(G.Ink, 0.55f), Ui.Pxf(3), true);
        Vector2 point = centre + new Vector2(MathF.Cos(Angle(d.DialYaw)), MathF.Sin(Angle(d.DialYaw))) * radius;
        DrawLine(centre, point, G.Ink, Line, true);
        Vector2 asked = centre + new Vector2(MathF.Cos(Angle(d.DialAsked)), MathF.Sin(Angle(d.DialAsked))) * radius * 1.12f;
        DrawCircle(asked, Ui.Pxf(3.5f), G.Warning);
        TextIn("ARC", 800 - 330 - 40, 900 - 100 + 50, 80, 13, G.Muted, HorizontalAlignment.Center);
    }

    // At the right: the ship's heading and speed, small.
    private void PaintSide(StationSightData d)
    {
        _o = CentreFrame;
        float heading = (d.Heading % 360 + 360) % 360;
        TextIn("HDG " + N(heading, "000") + "°", 1180, 430, 200, 16, G.Muted, HorizontalAlignment.Left);
        TextIn(N(d.Speed) + " M/S", 1180, 452, 200, 16, G.Muted, HorizontalAlignment.Left);
    }
}
