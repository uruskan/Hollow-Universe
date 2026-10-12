using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The building-plan and hammer wheels (RustMVP's, from SBareRoot.cpp): an ivory ring of radius 200–300 with the
// pointed sector in brick red (197–318), outline glyphs at radius 250 (257 when selected), and the choice's large
// glyph, title, description, cost and reason in the middle. Selection needs the pointer 95–430 px from the centre.
public partial class GroundUi
{
    // The plan's top ray separates its first piece (foundation) from its last (triangle roof); other wheels centre
    // their first sector at twelve o'clock.
    private static double StartAngle(WheelKind kind, int count) =>
        -Math.PI / 2 + (kind == WheelKind.Plan && count > 0 ? Math.PI / count : 0);

    private int WheelSelection(WheelKind kind)
    {
        int count = Model.Wheel(kind).Count;
        _o = CentreFrame;
        Vector2 delta = DesignPointer - new Vector2(800, 450);
        float distance = delta.Length();
        if (count == 0 || distance < 95 || distance > 430)
        {
            return -1;
        }
        double sector = Math.Tau / count;
        double angle = Math.Atan2(delta.Y, delta.X) - StartAngle(kind, count) + sector / 2 + 2 * Math.Tau;
        return (int)Math.Floor(angle % Math.Tau / sector) % count;
    }

    private void PaintWheel(WheelKind kind)
    {
        _p.DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.025f, 0.035f, 0.03f, 0.26f));
        _o = CentreFrame;
        IReadOnlyList<WheelChoice> choices = Model.Wheel(kind);
        int count = choices.Count;
        int selection = WheelSelection(kind);
        int shown = selection >= 0 ? selection : kind == WheelKind.Plan ? Model.WheelCurrent(kind) : -1;
        double sector = Math.Tau / Math.Max(1, count), start = StartAngle(kind, count);

        Annulus(0, Math.Tau, 200, 300, G.Ivory);
        if (shown >= 0 && shown < count)
        {
            double a = start + sector * shown;
            Annulus(a - sector / 2, a + sector / 2, 197, 318, G.Brick);
        }
        for (int i = 0; i < count; i++)
        {
            double a = start + sector * i;
            float radius = i == shown ? 257 : 250;
            Diagram(new Vector2(800 + (float)Math.Cos(a) * radius, 450 + (float)Math.Sin(a) * radius), choices[i].Glyph,
                i == shown ? G.Ivory : G.Brick, 29);
        }
        if (shown >= 0 && shown < count)
        {
            WheelChoice choice = choices[shown];
            Diagram(new Vector2(800, 345), choice.Glyph, G.Brick, 64);
            float title = 40;
            while (title > 22 && Measure(choice.Title, title, true, true) > 355)
            {
                title--;
            }
            CentreText(choice.Title, 431, title, G.Ivory);
            float description = 20;
            while (description > 15 && Measure(choice.Description, description, false, true) > 355)
            {
                description--;
            }
            CentreText(choice.Description, 480, description, G.Ivory, bold: false);
            if (choice.Cost != null)
            {
                CentreText(choice.Cost, 568, 20, G.Ivory, bold: false);
            }
            if (choice.Reason != null)
            {
                CentreText(choice.Reason, 601, 17, G.Warning, bold: false);
            }
        }
        CentreText("RELEASE RIGHT MOUSE TO SELECT   ·   ESC TO CANCEL", 792, 17, G.Ivory, bold: false);
    }

    // A filled ring sector, with antialiased edges.
    private void Annulus(double from, double to, float inner, float outer, Color colour)
    {
        Vector2 centre = Pt(800, 450);
        float r0 = Ui.Pxf(inner), r1 = Ui.Pxf(outer);
        int pieces = Math.Max(1, (int)Math.Ceiling((to - from) / (Math.PI / 2)));
        for (int p = 0; p < pieces; p++)
        {
            double a0 = from + (to - from) * p / pieces, a1 = from + (to - from) * (p + 1) / pieces;
            int steps = Math.Max(8, (int)Math.Ceiling((a1 - a0) / Math.Tau * 256));
            var points = new Vector2[2 * (steps + 1)];
            for (int i = 0; i <= steps; i++)
            {
                double a = a0 + (a1 - a0) * i / steps;
                var direction = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                points[i] = centre + direction * r1;
                points[2 * steps + 1 - i] = centre + direction * r0;
            }
            _p.DrawColoredPolygon(points, colour);
        }
        int arcPoints = Math.Max(16, (int)Math.Ceiling((to - from) / Math.Tau * 256));
        _p.DrawArc(centre, r1, (float)from, (float)to, arcPoints, colour, 1.2f, true);
        _p.DrawArc(centre, r0, (float)from, (float)to, arcPoints, colour, 1.2f, true);
        if (to - from < Math.Tau - 1e-3)
        {
            foreach (double a in new[] { from, to })
            {
                var direction = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                _p.DrawLine(centre + direction * r0, centre + direction * r1, colour, 1.2f, true);
            }
        }
    }

    // A piece's outline glyph, stroked about 7.5% of its size wide.
    private void Diagram(Vector2 at, int glyph, Color colour, float size)
    {
        WheelGlyphs.Draw(glyph, points =>
        {
            var design = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                design[i] = at + points[i] * size;
            }
            Line(colour, Math.Max(1f / Ui.Scale, size * 0.075f), design);
        });
    }
}
