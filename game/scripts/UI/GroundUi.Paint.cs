using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// Immediate-mode painting on the design canvas, as RustMVP's SBareRoot painted: rectangles, text, icons and lines at
// design coordinates, each length through Ui.Px. Positions snap to whole pixels; glyph strokes stay antialiased.
public partial class GroundUi
{
    private Painter _p = null!;
    private Vector2 _o;

    private void PaintCanvas(Painter painter)
    {
        _p = painter;
        _hits.Clear();
        _hoverButton = null;
        PaintScreen();
        // The pointer came onto another button: a quiet tick (the sound has its own cooldown).
        if (_hoverButton is { } over && over != _hoverButtonLast)
        {
            Sound?.Invoke("ui.hover");
        }
        _hoverButtonLast = _hoverButton;
    }

    private void PaintScreen()
    {
        switch (Screen)
        {
            case GroundScreen.Menu:
                PaintMenu();
                return;
            case GroundScreen.Death:
                PaintDeath();
                return;
            case GroundScreen.Map:
                PaintMap();
                PaintToast();
                return;
            case GroundScreen.Console:
                // The F1 panel hangs from the top; the belt stays in sight below it (what is given lands there).
                if (Model.Death == null && !Model.Piloting)
                {
                    PaintBelt(false);
                }
                PaintConsoleScreen();
                PaintToast();
                return;
        }
        bool inventory = Screen is GroundScreen.Inventory or GroundScreen.Crafting or GroundScreen.TechTree;
        // Flying, the ship's HUD (drawn by the ground view) has the centre, the belt's and the vitals' places.
        bool flying = Screen == GroundScreen.Hud && Model.Piloting;
        if (Screen == GroundScreen.Hud && Radial == null && !flying)
        {
            PaintCentre();
        }
        if (!flying)
        {
            PaintBelt(inventory);
            PaintVitals(inventory);
        }
        if (Screen == GroundScreen.Hud)
        {
            PaintFeed();
            PaintChat();
        }
        if (Screen == GroundScreen.Inventory)
        {
            PaintInventory();
        }
        else if (Screen == GroundScreen.Crafting)
        {
            PaintCrafting();
        }
        else if (Screen == GroundScreen.TechTree)
        {
            PaintTechTree();
        }
        PaintRadial();
        PaintToast();
    }

    private void PaintOverlay(Painter painter)
    {
        _p = painter;
        if (Screen is GroundScreen.Inventory or GroundScreen.Crafting)
        {
            PaintDragAndTooltip();
        }
    }

    // ---------------------------------------------------------------- coordinates

    /// <summary>A signed design length in whole pixels.</summary>
    private static int S(float v) => v < 0 ? -Ui.Px(-v) : Ui.Px(v);

    private Vector2 At(float x, float y) => _o + new Vector2(S(x), S(y));

    private Rect2 Area(float x, float y, float w, float h) => new(At(x, y), new Vector2(Ui.Px(w), Ui.Px(h)));

    /// <summary>A design point, unrounded (for strokes).</summary>
    private Vector2 Pt(float x, float y) => _o + new Vector2(x, y) * Ui.Scale;

    /// <summary>The pointer in the current frame's design coordinates.</summary>
    private Vector2 DesignPointer => (Pointer - _o) / Ui.Scale;

    private bool Under(Rect2 area) => area.HasPoint(Pointer);

    // ---------------------------------------------------------------- primitives

    private void Rect(float x, float y, float w, float h, Color colour) => _p.DrawRect(Area(x, y, w, h), colour);

    private static FontFile FontFor(float size, bool bold, bool condensed) => G.Font(size, bold, condensed);

    /// <summary>A text's width in design pixels.</summary>
    private static float Measure(string text, float size, bool bold = false, bool condensed = false) =>
        FontFor(size, bold, condensed).GetStringSize(text, HorizontalAlignment.Left, -1, Ui.Px(size)).X / Ui.Scale;

    /// <summary>Text with its line's top at (x, y), as a Godot label (and RustMVP's) places it. Returns the width in design pixels.</summary>
    private float Text(string text, float x, float y, float size, Color colour, bool bold = false, bool condensed = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        FontFile font = FontFor(size, bold, condensed);
        int px = Ui.Px(size);
        Vector2 top = At(x, y);
        _p.DrawString(font, new Vector2(top.X, MathF.Round(top.Y + font.GetAscent(px))), text, HorizontalAlignment.Left, -1, px, colour);
        return font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X / Ui.Scale;
    }

    /// <summary>Text aligned in a box of width <paramref name="w"/> (left, centre or right), top at <paramref name="y"/>.</summary>
    private void TextIn(string text, float x, float y, float w, float size, Color colour, HorizontalAlignment align, bool bold = false, bool condensed = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        FontFile font = FontFor(size, bold, condensed);
        int px = Ui.Px(size);
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X;
        Vector2 top = At(x, y);
        float left = align switch
        {
            HorizontalAlignment.Center => top.X + MathF.Floor((Ui.Px(w) - width) / 2),
            HorizontalAlignment.Right => top.X + Ui.Px(w) - MathF.Ceiling(width),
            _ => top.X,
        };
        _p.DrawString(font, new Vector2(left, MathF.Round(top.Y + font.GetAscent(px))), text, HorizontalAlignment.Left, -1, px, colour);
    }

    /// <summary>Text centred on the canvas's middle column (wheels, prompts).</summary>
    private void CentreText(string text, float y, float size, Color colour, bool bold = true, bool condensed = true) =>
        TextIn(text, 0, y, 1600, size, colour, HorizontalAlignment.Center, bold, condensed);

    /// <summary>Wrapped text in a column of width <paramref name="w"/>, lines a font height apart.</summary>
    private void Paragraph(string text, float x, float y, float w, float size, Color colour, int maxLines = -1)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        FontFile font = FontFor(size, false, false);
        int px = Ui.Px(size);
        Vector2 top = At(x, y);
        _p.DrawMultilineString(font, new Vector2(top.X, MathF.Round(top.Y + font.GetAscent(px))), text, HorizontalAlignment.Left,
            Ui.Px(w), px, maxLines, colour);
    }

    /// <summary>An item's icon, fitted square and centred in the box.</summary>
    private void Icon(string? key, float x, float y, float w, float h, float alpha = 1f)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        int pw = Ui.Px(w), ph = Ui.Px(h), side = Math.Min(pw, ph);
        // By the item's key (a recipe's key names its item: G7's second ways to make one).
        Texture2D? texture = ItemIcons.Get(Model.Item(key).Key, side);
        if (texture == null)
        {
            return;
        }
        Vector2 at = At(x, y) + new Vector2((pw - side) / 2, (ph - side) / 2);
        _p.DrawTextureRect(texture, new Rect2(at, new Vector2(side, side)), false, new Color(1, 1, 1, alpha));
    }

    /// <summary>A white glyph tinted <paramref name="colour"/>, <paramref name="size"/> design pixels square.</summary>
    private void Glyph(string name, float x, float y, float size, Color colour)
    {
        int px = Ui.Px(size);
        Texture2D? texture = GroundGlyphs.Get(name, px);
        if (texture != null)
        {
            _p.DrawTextureRect(texture, new Rect2(At(x, y), new Vector2(px, px)), false, colour);
        }
    }

    /// <summary>A polyline through design points, antialiased, <paramref name="width"/> screen pixels wide at scale 1.</summary>
    private void Line(Color colour, float width, params Vector2[] points)
    {
        var screen = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            screen[i] = Pt(points[i].X, points[i].Y);
        }
        _p.DrawPolyline(screen, colour, Math.Max(1f, Ui.Pxf(width)), true);
    }

    /// <summary>RustMVP's tab arrow: a shaft, a head and a bar, pointing right (or left).</summary>
    private void Arrow(float x, float y, float direction = 1)
    {
        Line(G.Ink, 1.5f, new Vector2(x - 11 * direction, y), new Vector2(x + 7 * direction, y));
        Line(G.Ink, 1.5f, new Vector2(x + 2 * direction, y - 5), new Vector2(x + 7 * direction, y), new Vector2(x + 2 * direction, y + 5));
        Line(G.Ink, 1.5f, new Vector2(x + 11 * direction, y - 6), new Vector2(x + 11 * direction, y + 6));
    }

    /// <summary>A five-pointed star outline (favourites).</summary>
    private void StarShape(float x, float y, float radius, Color colour)
    {
        var points = new Vector2[11];
        for (int i = 0; i <= 10; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 5;
            float r = i % 2 == 1 ? radius * 0.42f : radius;
            points[i] = new Vector2(x + MathF.Cos(a) * r, y + MathF.Sin(a) * r);
        }
        Line(colour, 1.5f, points);
    }

    /// <summary>
    /// RustMVP's button: a flat fill that lightens under the pointer, and a condensed bold label, centred (or left,
    /// for menu items and categories). Disabled buttons show their label muted and take no clicks; muted ones take them
    /// (a refused action says why).
    /// </summary>
    private void Button(string label, float x, float y, float w, float h, Action? click, float size = 24, Color? fill = null,
        bool enabled = true, bool left = false, string? tip = null, bool muted = false)
    {
        Color f = fill ?? G.Panel;
        Rect2 area = Area(x, y, w, h);
        bool hover = enabled && click != null && Under(area);
        if (hover)
        {
            _hoverButton = area;
        }
        _p.DrawRect(area, hover ? new Color(f.R + 0.06f, f.G + 0.06f, f.B + 0.06f, Math.Max(0.22f, f.A)) : f);
        if (label.Length > 0)
        {
            FontFile font = FontFor(size, true, true);
            int px = Ui.Px(size);
            float top = MathF.Round(area.Position.Y + (area.Size.Y - (font.GetAscent(px) + font.GetDescent(px))) / 2);
            float width = font.GetStringSize(label, HorizontalAlignment.Left, -1, px).X;
            float textX = left ? area.Position.X + Ui.Px(14) : area.Position.X + MathF.Floor((area.Size.X - width) / 2);
            _p.DrawString(font, new Vector2(textX, MathF.Round(top + font.GetAscent(px))), label, HorizontalAlignment.Left, -1, px,
                enabled && !muted ? G.Ink : G.Muted);
        }
        if (enabled && click != null)
        {
            _hits.Add(new Hit(area, HitKind.Button, click, Key: tip));
        }
    }

    /// <summary>Marks a panel: a press on it goes nowhere (a drag released on it drops, as anywhere but a slot).</summary>
    private void PanelArea(float x, float y, float w, float h) => _hits.Add(new Hit(Area(x, y, w, h), HitKind.Panel));

    private static string Count(int amount) =>
        "x" + (amount >= 1000 ? $"{amount / 1000},{amount % 1000:000}" : amount.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// A slot as RustMVP draws it: a flat square (lighter under the pointer in the inventory, blue when selected or
    /// active), the icon, the stack count (or a weapon's loaded rounds) at the bottom right, the loaded round's icon at the
    /// bottom left, the condition bar on the left, and the hover-transfer ring.
    /// </summary>
    private void Slot(SlotRef slot, ItemStack stack, float x, float y, float size, bool inventory, bool active, bool interactive)
    {
        Rect2 area = Area(x, y, size, size);
        Color fill = inventory ? G.Panel : G.HudSlot;
        if (inventory && interactive && Under(area) && _drag is not { Moving: true })
        {
            fill = G.PanelHover;
        }
        if (active || (inventory && _selected == slot))
        {
            fill = G.Selected;
        }
        _p.DrawRect(area, fill);
        if (!stack.IsEmpty)
        {
            bool dragged = _drag is { Moving: true } d && d.From == slot;
            float alpha = dragged ? 0.35f : 1f;
            float inset = inventory ? 7 : 3;
            if (stack.Teaches != null)
            {
                // G7: a blueprint is the item it teaches, on blueprint blue.
                Rect(x + inset - 2, y + inset - 2, size - 2 * inset + 4, size - 2 * inset + 4, new Color(0.18f, 0.36f, 0.62f, 0.75f));
                Icon(stack.Teaches, x + inset, y + inset, size - 2 * inset, size - 2 * inset, alpha * 0.9f);
            }
            else if (inventory)
            {
                Icon(stack.Key, x + 7, y + 5, size - 14, size - 12, alpha);
            }
            else
            {
                Icon(stack.Key, x + 3, y + 3, size - 6, size - 6, alpha);
            }
            if (stack.Round != null)
            {
                // A gun's loaded round, small at the bottom left (its count stays at the bottom right).
                float round = MathF.Round(size * 0.3f);
                Icon(stack.Round, x + 4, y + size - round - 3, round, round, alpha);
            }
            string label = stack.Amount > 1 ? Count(stack.Amount) : stack.HasAmmo ? stack.Ammo.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            if (label.Length > 0)
            {
                TextIn(label, x + 6, y + size - 25, size - 12, 16, G.Ink, HorizontalAlignment.Right, bold: true);
            }
            if (stack.HasCondition)
            {
                float full = size - 6, filled = full * Math.Clamp(stack.Condition, 0, 1);
                if (filled > 0)
                {
                    Rect(x + 2, y + 3 + full - filled, 3, filled, G.Green);
                }
            }
        }
        if (interactive)
        {
            _hits.Add(new Hit(area, HitKind.Slot, Slot: slot));
        }
        if (_hoverStarted.TryGetValue(slot, out double started))
        {
            float progress = Math.Clamp((float)((_now - started) / HoverSeconds), 0, 1);
            Vector2 centre = area.GetCenter();
            float radius = area.Size.X * 0.18f;
            _p.DrawCircle(centre, radius, new Color(0.13f, 0.16f, 0.08f, 0.7f), true, -1, true);
            if (progress > 0)
            {
                var points = new List<Vector2> { centre };
                int slices = Math.Max(2, (int)MathF.Ceiling(progress * 48));
                for (int i = 0; i <= slices; i++)
                {
                    float a = -MathF.PI / 2 + MathF.Tau * progress * i / slices;
                    points.Add(centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
                }
                _p.DrawColoredPolygon(points.ToArray(), new Color("9bce3d"));
            }
        }
    }

    /// <summary>A section heading (28 px condensed bold), as INVENTORY, LOOT and QUICK CRAFT.</summary>
    private void Section(string text, float x, float y) => Text(text, x, y, 28, G.Ink, bold: true);
}
