using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Simulation.Ships;
using HollowUniverse.Common;
using HollowUniverse.UI;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.Ships;

/// <summary>What the flight HUD shows this frame (the host fills it from the zone or the client's prediction).</summary>
public sealed class ShipHudData
{
    public ShipDef? Def;
    public ShipState State;

    /// <summary>Metres per second, the ship's speed; and its climb (+) or sink (−).</summary>
    public float Speed, VerticalSpeed;

    /// <summary>Metres over the ground (or the sea) below, and over the planet's datum (the ceiling's measure).</summary>
    public float OverGround, Height;

    /// <summary>The orbital-altitude marker over the datum, in metres; flight continues through it.</summary>
    public float Ceiling = float.PositiveInfinity;

    /// <summary>Degrees from north toward east, the nose's heading.</summary>
    public float Heading;

    /// <summary>The pilot's lever and stick, and whether flight assist is off.</summary>
    public float Throttle;

    public Vector2 Stick;
    public bool AssistOff;

    /// <summary>Seconds to hit the ground at this sink rate (infinite when climbing).</summary>
    public float TimeToGround = float.PositiveInfinity;

    /// <summary>The key line under the crosshair (e.g. stepping out), or null.</summary>
    public string? Prompt;

    /// <summary>A short message under the warnings (a refused action), or null.</summary>
    public string? Notice;

    /// <summary>A line of key hints for the top left, or null.</summary>
    public string? Hints;
}

/// <summary>
/// The flight HUD (G6, GROUND.md §6.7), in the ground interface's style (<see cref="Ui.Ground"/>, its 1600 × 900 design
/// canvas, every length through <see cref="Ui.Px"/>): speed, height over the ground and climb at the bottom; the
/// throttle lever on the left; hull and fuel where the vitals sit, with flight assist, boost, landed and orbit above
/// them; the stick and a crosshair in the middle; warnings at the top. Painted in immediate mode, like the ground HUD.
/// </summary>
public partial class ShipHud : Control
{
    private Vector2 _o;

    public ShipHudData Data { get; } = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Repaints from <see cref="Data"/>.</summary>
    public void Refresh() => QueueRedraw();

    // ---------------------------------------------------------------- frames and primitives (GroundUi's conventions)

    private Vector2 CanvasFrame => new(MathF.Floor((Size.X - Ui.Px(1600)) / 2), Size.Y - Ui.Px(900));

    private Vector2 CentreFrame => new(MathF.Floor(Size.X / 2) - Ui.Px(800), MathF.Floor(Size.Y / 2) - Ui.Px(450));

    private Vector2 RightFrame => new(Size.X - Ui.Px(1600), Size.Y - Ui.Px(900));

    private Vector2 LeftFrame => new(0, Size.Y - Ui.Px(900));

    private static int S(float v) => v < 0 ? -Ui.Px(-v) : Ui.Px(v);

    private Vector2 At(float x, float y) => _o + new Vector2(S(x), S(y));

    private Vector2 Pt(float x, float y) => _o + new Vector2(x, y) * Ui.Scale;

    private void Rect(float x, float y, float w, float h, Color colour) =>
        DrawRect(new Rect2(At(x, y), new Vector2(Ui.Px(w), Ui.Px(h))), colour);

    // Text drawn straight on the world (no slot behind it) gets a soft dark copy under it, to read on sky and snow alike.
    private static readonly Color ShadowColour = new(0, 0, 0, 0.55f);

    private float Text(string text, float x, float y, float size, Color colour, bool bold = false, bool condensed = false, bool shadow = false)
    {
        FontFile font = G.Font(size, bold, condensed);
        int px = Ui.Px(size);
        Vector2 top = At(x, y);
        var at = new Vector2(top.X, MathF.Round(top.Y + font.GetAscent(px)));
        if (shadow)
        {
            DrawString(font, at + Vector2.One * Math.Max(1, Ui.Px(1.5f)), text, HorizontalAlignment.Left, -1, px, ShadowColour);
        }
        DrawString(font, at, text, HorizontalAlignment.Left, -1, px, colour);
        return font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X / Ui.Scale;
    }

    private void TextIn(string text, float x, float y, float w, float size, Color colour, HorizontalAlignment align, bool bold = false,
        bool condensed = false, bool shadow = false)
    {
        FontFile font = G.Font(size, bold, condensed);
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
        if (shadow)
        {
            DrawString(font, at + Vector2.One * Math.Max(1, Ui.Px(1.5f)), text, HorizontalAlignment.Left, -1, px, ShadowColour);
        }
        DrawString(font, at, text, HorizontalAlignment.Left, -1, px, colour);
    }

    private void Ring(float x, float y, float radius, Color colour, float width) =>
        DrawArc(Pt(x, y), radius * Ui.Scale, 0, MathF.Tau, 48, colour, Math.Max(1f, Ui.Pxf(width)), true);

    private void Icon(string key, float x, float y, float size)
    {
        int px = Ui.Px(size);
        if (ItemIcons.Get(key, px) is Texture2D texture)
        {
            DrawTextureRect(texture, new Rect2(At(x, y), new Vector2(px, px)), false);
        }
    }

    private static string N(float v, string format = "0") => v.ToString(format, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- painting

    public override void _Draw()
    {
        ShipHudData d = Data;
        if (d.Hints != null)
        {
            _o = Vector2.Zero;
            PaintHints(d.Hints);
        }
        if (d.Def == null)
        {
            // On foot: a dot for a crosshair, what the interact key would do, and notices.
            _o = CentreFrame;
            DrawCircle(Pt(800, 450), Ui.Pxf(2.5f), new Color(G.Ink, 0.9f));
            if (d.Prompt != null)
            {
                TextIn(d.Prompt, 0, 486, 1600, 22, G.Ink, HorizontalAlignment.Center, bold: true, condensed: true, shadow: true);
            }
            if (d.Notice != null)
            {
                TextIn(d.Notice, 0, 150, 1600, 22, G.Warning, HorizontalAlignment.Center, bold: true, condensed: true, shadow: true);
            }
            return;
        }
        PaintCentre(d);
        PaintReadouts(d);
        PaintThrottle(d);
        PaintHullAndFuel(d);
        PaintWarnings(d);
    }

    // Keep the flight help within the viewport at any UI scale, with at most two measured lines.
    private void PaintHints(string hints)
    {
        float width = Math.Max(1, Size.X - Ui.Px(48));
        List<string> Lines(float size)
        {
            FontFile font = G.Font(size, bold: true);
            int px = Ui.Px(size);
            var lines = new List<string>();
            foreach (string paragraph in hints.Split('\n'))
            {
                string line = "";
                foreach (string hint in paragraph.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    string next = line.Length == 0 ? hint : line + " · " + hint;
                    if (line.Length > 0 && font.GetStringSize(next, HorizontalAlignment.Left, -1, px).X > width)
                    {
                        lines.Add(line);
                        line = hint;
                    }
                    else line = next;
                }
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }
        float size = 16;
        List<string> lines = Lines(size);
        while (size > 13 && lines.Count > 2) lines = Lines(--size);
        for (int i = 0; i < Math.Min(2, lines.Count); i++)
        {
            string line = lines[i];
            if (i == 1 && lines.Count > 2) line += " …";
            FontFile font = G.Font(size, bold: true);
            while (line.Length > 1 && font.GetStringSize(line, HorizontalAlignment.Left, -1, Ui.Px(size)).X > width)
                line = line.TrimEnd(' ', '…')[..^1] + "…";
            Text(line, 24, 18 + i * (size + 5), size, G.Ink, bold: true, shadow: true);
        }
    }

    // The crosshair and the stick: a ring for the stick's reach, a dot where it is.
    private void PaintCentre(ShipHudData d)
    {
        _o = CentreFrame;
        var faint = new Color(G.Ink, 0.28f);
        Ring(800, 450, 6, new Color(G.Ink, 0.85f), 1.5f);
        Vector2 stick = d.Stick;
        if (stick.Length() > 0.02f)
        {
            Vector2 at = new(800 + stick.X * ShipControls.SteeringReach, 450 - stick.Y * ShipControls.SteeringReach);
            DrawLine(Pt(800, 450), Pt(at.X, at.Y), new Color(G.Ink, 0.35f), Math.Max(1f, Ui.Pxf(1.5f)), true);
            DrawCircle(Pt(at.X, at.Y), Ui.Pxf(4.5f), new Color(G.Ink, 0.9f));
        }
        if (d.Prompt != null)
        {
            TextIn(d.Prompt, 0, 540, 1600, 22, G.Ink, HorizontalAlignment.Center, bold: true, condensed: true, shadow: true);
        }
    }

    // Speed, height over the ground and climb, side by side at the bottom; the heading above them.
    private void PaintReadouts(ShipHudData d)
    {
        _o = CanvasFrame;
        const float w = 172, h = 66, gap = 6, y = 900 - 24 - h;
        float x0 = 800 - (3 * w + 2 * gap) / 2;
        string climb = (d.VerticalSpeed >= 0 ? "+" : "−") + N(MathF.Abs(d.VerticalSpeed), MathF.Abs(d.VerticalSpeed) < 10 ? "0.0" : "0");
        (string Label, string Value, string Unit)[] slots =
        {
            ("SPEED", N(d.Speed), "m/s"),
            ("ALTITUDE", d.OverGround >= 10_000 ? N(d.OverGround / 1000, "0.0") : N(MathF.Max(d.OverGround, 0)), d.OverGround >= 10_000 ? "km" : "m"),
            ("VERTICAL", climb, "m/s"),
        };
        for (int i = 0; i < slots.Length; i++)
        {
            float x = x0 + i * (w + gap);
            Rect(x, y, w, h, G.HudSlot);
            Text(slots[i].Label, x + 10, y + 5, 15, G.Muted, bold: true);
            float width = Text(slots[i].Value, x + 10, y + 21, 34, G.Ink, bold: true, condensed: true);
            Text(slots[i].Unit, x + 16 + width, y + 37, 17, G.Muted, bold: true);
        }
        string[] compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        float heading = ((d.Heading % 360) + 360) % 360;
        string point = compass[(int)MathF.Round(heading / 45) % 8];
        TextIn($"{N(heading)}°  {point}", 0, y - 30, 1600, 20, G.Ink, HorizontalAlignment.Center, bold: true, condensed: true, shadow: true);
    }

    // The throttle lever: ahead above the line, reverse below; with flight assist on, the speed it asks for.
    private void PaintThrottle(ShipHudData d)
    {
        _o = LeftFrame;
        const float x = 24, top = 618, height = 252, w = 44;
        ShipDef def = d.Def!;
        float reverseShare = def.ReverseSpeed / (def.ForwardSpeed + def.ReverseSpeed);
        float zero = top + height * (1 - reverseShare);
        Rect(x, top, w, height, G.HudSlot);
        float t = Math.Clamp(d.Throttle, -1, 1);
        if (t > 0)
        {
            float fill = (zero - top - 4) * t;
            Rect(x + 4, zero - fill, w - 8, fill, new Color(G.Selected, 0.95f));
        }
        else if (t < 0)
        {
            float fill = (top + height - zero - 4) * -t;
            Rect(x + 4, zero, w - 8, fill, new Color(G.Food, 0.95f));
        }
        Rect(x, zero - 1, w + 8, 2, G.Ink);
        Text("THROTTLE", x, top - 24, 16, G.Ink, bold: true, shadow: true);
        Text(N(MathF.Round(t * 100)) + "%", x + w + 12, zero - 13, 22, G.Ink, bold: true, condensed: true, shadow: true);
        if (!d.AssistOff)
        {
            float target = t >= 0 ? t * def.ForwardSpeed : t * def.ReverseSpeed;
            Text("→ " + N(target) + " m/s", x + w + 12, zero + 10, 16, G.Ink, bold: true, shadow: true);
        }
    }

    // Hull and fuel where the vitals sit, flight assist, boost, landed and orbit stacked above them.
    private void PaintHullAndFuel(ShipHudData d)
    {
        _o = RightFrame;
        ShipDef def = d.Def!;
        ShipState s = d.State;
        (string Icon, float Value, float Max, Color Colour)[] bars =
        {
            (def.Fuel.Key, s.Fuel, def.FuelCapacity, G.Food),
            (def.Key, s.Health, def.Health, G.Health),
        };
        for (int i = 0; i < bars.Length; i++)
        {
            (string icon, float value, float max, Color colour) = bars[i];
            float x = 1340, y = 813 + i * 35;
            Rect(x, y, 240, 32, G.HudSlot);
            float fill = 202 * Math.Clamp(value / max, 0, 1);
            if (fill > 0)
            {
                Rect(x + 30, y + 4, fill, 25, new Color(colour, 0.9f));
            }
            Icon(icon, x + 3, y + 3, 26);
            Text(N(MathF.Round(value)), x + 38, y + 1, 21, G.Ink, bold: true);
            TextIn(i == 0 ? "FUEL" : "HULL", x, y + 5, 232, 16, new Color(G.Ink, 0.8f), HorizontalAlignment.Right, bold: true);
        }
        int row = 0;
        void Status(string label, string value, Color colour)
        {
            float y = 778 - row++ * 35;
            Rect(1340, y, 240, 32, colour);
            Text(label, 1350, y + 5, 18, G.Ink, bold: true);
            TextIn(value, 1340, y + 5, 230, 18, G.Ink, HorizontalAlignment.Right, bold: true);
        }
        Status("FLIGHT ASSIST", d.AssistOff ? "OFF" : "ON", d.AssistOff ? G.Red : G.HudSlot);
        if (s.Has(ShipFlags.Boosting))
        {
            Status("BOOST", N(s.BoostTicks / 30f, "0.0") + " s", G.Selected);
        }
        else if (s.CooldownTicks > 0)
        {
            Status("BOOST", "COOLING " + N(MathF.Ceiling(s.CooldownTicks / 30f)) + " s", G.HudSlot);
        }
        if (s.Has(ShipFlags.InOrbit))
        {
            Status("IN ORBIT", "", G.Category);
        }
        else if (s.Has(ShipFlags.Landed))
        {
            Status("LANDED", "", G.Green);
        }
        else if (s.Has(ShipFlags.Touching))
        {
            Status("ON THE GROUND", "TOO STEEP", G.Short);
        }
        if (s.Fuel <= 0)
        {
            Status("OUT OF FUEL", "", G.Red);
        }
    }

    // Warnings at the top: the ground coming, fuel and hull running low, the ceiling near; a notice below them.
    private void PaintWarnings(ShipHudData d)
    {
        _o = CentreFrame;
        ShipDef def = d.Def!;
        ShipState s = d.State;
        float y = 150;
        void Line(string text, Color colour, float size = 30)
        {
            TextIn(text, 0, y, 1600, size, colour, HorizontalAlignment.Center, bold: true, condensed: true, shadow: true);
            y += size + 8;
        }
        if (!s.Has(ShipFlags.InOrbit) && d.TimeToGround < 4 && d.Speed > 15 && d.VerticalSpeed < -8)
        {
            Line("PULL UP", G.Red, 40);
        }
        if (s.Health < def.Health * 0.25f)
        {
            Line("HULL CRITICAL", G.Brick);
        }
        if (s.Fuel > 0 && s.Fuel < def.FuelCapacity * 0.15f)
        {
            Line("LOW FUEL", G.Warning);
        }
        if (s.Has(ShipFlags.AtEdge))
        {
            Line("LOCAL FLIGHT LIMIT  ·  TURN OR DESCEND", G.Warning, 24);
        }
        else if (!s.Has(ShipFlags.InOrbit) && d.Height >= d.Ceiling)
        {
            Line("ORBITAL ALTITUDE  ·  FLIGHT CONTROLS ACTIVE", G.Ink, 24);
        }
        else if (!s.Has(ShipFlags.InOrbit) && d.Height > d.Ceiling - 1000)
        {
            Line($"ORBITAL ALTITUDE IN {N(d.Ceiling - d.Height)} M", G.Ink, 24);
        }
        if (d.Notice != null)
        {
            Line(d.Notice, G.Warning, 22);
        }
    }
}
