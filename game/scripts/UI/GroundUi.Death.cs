using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The death screen in Rust's layout: DEAD and how it happened across the top, the map with the body and the
// sleeping bags in the middle (the map control, GroundUi.Globe.cs, as on G: it opens on the body, north up, close in,
// and turns and zooms the same way), the bags and RESPAWN (a random beach) along the bottom. RustMVP's own death panel
// had the same choices (beach, bags with cooldowns) in a simpler card.
public partial class GroundUi
{
    private static readonly Color DeathBar = new(0.115f, 0.118f, 0.12f);
    private static readonly Color ChipGreen = new(0.42f, 0.53f, 0.21f);
    private static readonly Color ChipRed = new(0.72f, 0.27f, 0.21f);
    private static readonly Color ChipGrey = new(0.33f, 0.34f, 0.34f);
    private static readonly Color RespawnFill = new(0.26f, 0.32f, 0.17f);
    private static readonly Color RespawnInk = new(0.62f, 0.78f, 0.27f);

    private void PaintDeath()
    {
        DeathInfo? death = Model.Death;
        _p.DrawRect(new Rect2(Vector2.Zero, Size), DeathBar);
        // The map's place (its picture is the globe's layer, its markers the overlay's: PaintDeathOverlay).
        Rect2 map = DeathMapArea();
        _p.DrawRect(map, Model.Map == null ? MapBlank : MapBackdrop);
        _hits.Add(new Hit(map, HitKind.Map));

        _o = Vector2.Zero;
        Text("DEAD", 150, 28, 60, new Color(0.58f, 0.59f, 0.59f), bold: true, condensed: true);
        if (death != null)
        {
            PaintDeathStats(death);
        }

        // Bottom bar: the bags on the left, RESPAWN on the right.
        _o = LeftFrame;
        IReadOnlyList<RespawnBag> bags = Model.Bags;
        Text(bags.Count > 0 ? "SLEEPING BAGS" : "NO SLEEPING BAGS", 40, 806, 13, new Color(G.Ink, 0.85f), bold: true);
        for (int i = 0; i < bags.Count && i < 5; i++)
        {
            RespawnBag bag = bags[i];
            float x = 40 + i * 232;
            bool ready = bag.Cooldown <= 0;
            string id = bag.Id;
            Button("", x, 826, 222, 52, ready ? () => Model.Respawn(id) : null, fill: new Color(1, 1, 1, 0.07f));
            Glyph("bag", x + 10, 840, 24, ready ? G.Ink : G.Muted);
            Text(bag.Name, x + 44, 832, 18, ready ? G.Ink : G.Muted, bold: true);
            Text(ready ? "READY" : $"READY IN {Clock(bag.Cooldown)}", x + 44, 853, 14, ready ? G.Green : G.Muted, bold: true);
        }
        _o = RightFrame;
        Button("", 1315, 814, 203, 65, () => Model.Respawn(null), fill: RespawnFill);
        float label = Measure("RESPAWN", 44, true, true), left = 1315 + (203 - label - 36) / 2;
        Text("RESPAWN", left, 822, 44, RespawnInk, bold: true, condensed: true);
        for (int i = 0; i < 2; i++)
        {
            float x = left + label + 14 + i * 12;
            Line(RespawnInk, 4f, new Vector2(x, 837), new Vector2(x + 9, 846.5f), new Vector2(x, 856));
        }
    }

    // ALIVE FOR — KILLED BY — WITH A — AT A DISTANCE OF, centred on the top bar.
    private void PaintDeathStats(DeathInfo death)
    {
        string weapon = death.Weapon != null ? Model.Item(death.Weapon).Name : "";
        var chips = new List<(string Label, string Value, Color Fill, Color Ink, string? Icon, bool Person)>
        {
            ("ALIVE FOR", Duration(death.AliveSeconds), ChipGreen, new Color(0.88f, 0.96f, 0.68f), null, false),
            ("KILLED BY", death.KilledBy, ChipRed, G.Ink, null, true),
        };
        if (death.Weapon != null)
        {
            chips.Add(("WITH A", weapon, ChipRed, G.Ink, death.Weapon, false));
        }
        if (death.Distance > 0)
        {
            chips.Add(("AT A DISTANCE OF", Ui.Number(death.Distance, "0.0") + "m", ChipGrey, G.Muted, null, false));
        }
        const float gap = 66;
        var widths = new float[chips.Count];
        float total = gap * (chips.Count - 1);
        for (int i = 0; i < chips.Count; i++)
        {
            float lead = chips[i].Icon != null || chips[i].Person ? 44 : 0;
            widths[i] = Math.Max(lead + 28 + Measure(chips[i].Value, 20, bold: true), Measure(chips[i].Label, 12, bold: true));
            total += widths[i];
        }
        _o = new Vector2(MathF.Floor(Size.X / 2) - Ui.Px(800), 0);
        float x = 800 - total / 2;
        for (int i = 0; i < chips.Count; i++)
        {
            (string label, string value, Color fill, Color ink, string? icon, bool person) = chips[i];
            Text(label, x, 33, 12, new Color(G.Ink, 0.85f), bold: true);
            Rect(x, 45, widths[i], 37, fill);
            float lead = icon != null || person ? 44 : 0;
            if (person)
            {
                Rect(x, 45, 37, 37, new Color(0.55f, 0.15f, 0.12f));
                Glyph("person", x + 8, 52, 22, G.Ink);
            }
            if (icon != null)
            {
                Icon(icon, x - 4, 31, 48, 48);
            }
            Text(value, x + lead + 14, 51, 20, ink, bold: true);
            if (i < chips.Count - 1)
            {
                Rect(x + widths[i] + 20, 63, 26, 2, new Color(0.36f, 0.37f, 0.37f));
            }
            x += widths[i] + gap;
        }
    }

    // The map's overlay: the grid, where the body lies and the bags (click a ready bag to respawn there), names by the
    // Labeller, then the cells' names where they fit.
    private void PaintDeathOverlay()
    {
        if (Model.Map is not { } view || _deathGlobe.Camera == null)
        {
            return;
        }
        GlobeFrame f = BeginGlobe(_deathGlobe, view);
        DeathInfo? death = Model.Death;
        float r = Ui.Pxf(15);
        Vector2? body = death != null ? OnGlobe(f, death.At, Ui.Px(20)) : null;
        if (body is Vector2 b)
        {
            f.Labeller.Block(b - new Vector2(0, Ui.Pxf(4)), Ui.Pxf(20));
        }
        foreach (RespawnBag bag in Model.Bags)
        {
            if (OnGlobe(f, bag.At) is Vector2 at)
            {
                f.Labeller.Block(at, r + Ui.Pxf(2));
            }
        }
        ReadOnlySpan<LabelSide> sides = stackalloc[] { LabelSide.Below, LabelSide.Above, LabelSide.Right, LabelSide.Left };
        foreach (RespawnBag bag in Model.Bags)
        {
            if (OnGlobe(f, bag.At, Ui.Px(20)) is not Vector2 at)
            {
                continue;
            }
            bool ready = bag.Cooldown <= 0;
            _p.DrawCircle(at, r + Ui.Pxf(2), new Color(G.Ink, 0.9f), true, -1, true);
            _p.DrawCircle(at, r, ready ? new Color(0.20f, 0.30f, 0.14f) : new Color(0.18f, 0.18f, 0.18f), true, -1, true);
            _o = at;
            Glyph("bag", -10, -10, 20, ready ? G.Ink : G.Muted);
            string state = ready ? bag.Name : $"{bag.Name}  {Clock(bag.Cooldown)}";
            if (f.Labeller.TryPlaceAround(at, r + Ui.Pxf(2), state, G.Ink, 16, MapLabel.Strong, sides, out MapOverlay.Text placed))
            {
                // The Labeller measures with the maps' font; the name is drawn in Barlow on the spot it took.
                FontFile font = G.SemiBold;
                int px = Ui.Px(16);
                float w = font.GetStringSize(state, HorizontalAlignment.Left, -1, px).X;
                float left = MathF.Round(placed.Centred ? placed.Position.X - w / 2 : placed.Position.X);
                float baseline = MathF.Round(placed.Position.Y);
                float pad = Ui.Px(6);
                _p.DrawRect(new Rect2(left - pad, baseline - font.GetAscent(px) - Ui.Px(2), w + 2 * pad, font.GetHeight(px) + Ui.Px(4)),
                    new Color(0.08f, 0.08f, 0.08f, 0.78f));
                _p.DrawString(font, new Vector2(left, baseline), state, HorizontalAlignment.Left, -1, px, ready ? G.Ink : G.Muted);
            }
            if (ready)
            {
                string id = bag.Id;
                _hits.Add(new Hit(new Rect2(at - new Vector2(r, r), new Vector2(2 * r, 2 * r)), HitKind.Button, () => Model.Respawn(id)));
            }
        }
        if (body is Vector2 grave)
        {
            _p.DrawLine(grave + new Vector2(0, Ui.Pxf(10)), grave + new Vector2(0, Ui.Pxf(30)), G.Red, Ui.Pxf(3), true);
            _p.DrawCircle(grave, Ui.Pxf(16), G.Red, true, -1, true);
            _o = grave;
            Glyph("skull", -10, -11, 20, G.Ink);
        }
        EndGlobe(f, view);
    }

    private static string Duration(float seconds)
    {
        int s = (int)seconds;
        return s >= 3600 ? $"{s / 3600}h{s % 3600 / 60}m" : s >= 60 ? $"{s / 60}m{s % 60}s" : $"{s}s";
    }

    private static string Clock(float seconds)
    {
        int s = (int)MathF.Ceiling(seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
