using System;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.Settings;
using HollowUniverse.UI.Model;

namespace HollowUniverse.UI;

/// <summary>The ground HUD and settings preview share one geometrically centred crosshair.</summary>
public static class CrosshairPainter
{
    [Flags]
    private enum Parts { None = 0, Dot = 1, Bars = 2, Ring = 4 }

    private static readonly Parts[] Shapes =
    {
        Parts.None, Parts.Dot, Parts.Bars, Parts.Bars | Parts.Dot, Parts.Ring, Parts.Ring | Parts.Dot,
    };

    private static readonly Color[] Colours =
    {
        Colors.White, new(0.25f, 1, 0.3f), new(0.15f, 0.9f, 1),
        new(1, 0.9f, 0.15f), new(1, 0.2f, 0.2f), new(1, 0.3f, 0.9f),
    };

    private static readonly Vector2[] Directions = { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right };

    public static void DrawHud(CanvasItem canvas, Vector2 viewportSize, CrosshairValues values, CrosshairView state)
    {
        if (state.Hidden || (state.Aiming && !values.ShowWhileAiming)) return;
        float spreadRadius = 0;
        if (values.DynamicSpread && !state.Aiming && state.SpreadDegrees > 0 && float.IsFinite(state.SpreadDegrees))
        {
            float fov = float.IsFinite(state.FieldOfView) && state.FieldOfView > 0 ? state.FieldOfView : 75;
            // LocalGun exposes the cone's half-angle. Project it using the current camera's vertical FOV.
            spreadRadius = MathF.Tan(Mathf.DegToRad(Math.Clamp(state.SpreadDegrees, 0, 45))) /
                MathF.Tan(Mathf.DegToRad(Math.Clamp(fov, 1, 179)) / 2) * viewportSize.Y / 2;
        }
        // Do not round the centre or use the text/design-canvas origin: odd dimensions retain their half pixel.
        Draw(canvas, viewportSize / 2, values, spreadRadius);
    }

    public static void Draw(CanvasItem canvas, Vector2 centre, CrosshairValues values, float spreadRadius = 0)
    {
        Parts parts = Shapes[Math.Clamp((int)values.Style, 0, Shapes.Length - 1)];
        if (parts == Parts.None) return;
        Color colour = new(Colours[Math.Clamp(values.Color, 0, Colours.Length - 1)], values.Opacity);
        Color outline = new(0, 0, 0, values.Opacity);
        float width = Ui.Pxf(values.Thickness), border = Ui.Pxf(values.Outline);
        float dotRadius = Ui.Pxf(values.DotSize / 2), length = Ui.Pxf(values.Size);
        float gap = Math.Max(Ui.Pxf(values.Gap), spreadRadius);
        float ringRadius = Math.Max(length, spreadRadius);

        void Paint(Color ink, float extra)
        {
            if ((parts & Parts.Bars) != 0)
            {
                foreach (Vector2 direction in Directions)
                {
                    // Stroke endpoints include half the thickness, so Gap is the clear space around the centre.
                    Vector2 from = centre + direction * (gap + width / 2);
                    Vector2 to = from + direction * length;
                    canvas.DrawLine(from - direction * extra, to + direction * extra, ink, width + extra * 2, true);
                }
            }
            if ((parts & Parts.Ring) != 0)
                canvas.DrawArc(centre, ringRadius, 0, Mathf.Tau, 96, ink, width + extra * 2, true);
            if ((parts & Parts.Dot) != 0)
                canvas.DrawCircle(centre, dotRadius + extra, ink, true, -1, true);
        }

        if (border > 0) Paint(outline, border);
        Paint(colour, 0);
    }
}
