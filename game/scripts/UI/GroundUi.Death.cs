using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The death screen in Rust's layout: DEAD and how it happened across the top, the map with the body and the
// sleeping bags in the middle, the bags and RESPAWN (a random beach) along the bottom. RustMVP's own death panel
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
        float top = Ui.Px(124), bottom = Size.Y - Ui.Px(106);
        PaintDeathMap(new Rect2(0, top, Size.X, bottom - top), death);

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

    // The map: the picture, a lettered grid, where the body lies and the bags (click a ready bag to respawn there).
    private void PaintDeathMap(Rect2 area, DeathInfo? death)
    {
        _p.DrawTextureRect(DeathMap.Texture, area, false);
        float cell = Ui.Px(230);
        _o = area.Position;
        int column = 0;
        for (float x = 0; x < area.Size.X; x += cell, column++)
        {
            int row = 0;
            for (float y = 0; y < area.Size.Y; y += cell, row++)
            {
                if (x > 0)
                {
                    _p.DrawRect(new Rect2(area.Position.X + MathF.Round(x), area.Position.Y + MathF.Round(y), 1, cell), new Color(0.1f, 0.1f, 0.1f, 0.35f));
                }
                if (y > 0)
                {
                    _p.DrawRect(new Rect2(area.Position.X + MathF.Round(x), area.Position.Y + MathF.Round(y), cell, 1), new Color(0.1f, 0.1f, 0.1f, 0.35f));
                }
                string label = ((char)('K' + column)).ToString() + (14 + row).ToString(CultureInfo.InvariantCulture);
                Text(label, x / Ui.Scale + 14, y / Ui.Scale + 8, 18, new Color(0.12f, 0.12f, 0.12f, 0.7f));
            }
        }
        Vector2 MapPoint(float u, float v) => area.Position + new Vector2(u * area.Size.X, v * area.Size.Y).Round();
        foreach (RespawnBag bag in Model.Bags)
        {
            Vector2 at = MapPoint(bag.MapX, bag.MapY);
            bool ready = bag.Cooldown <= 0;
            float r = Ui.Pxf(15);
            _p.DrawCircle(at, r + Ui.Pxf(2), new Color(G.Ink, 0.9f), true, -1, true);
            _p.DrawCircle(at, r, ready ? new Color(0.20f, 0.30f, 0.14f) : new Color(0.18f, 0.18f, 0.18f), true, -1, true);
            _o = at;
            Glyph("bag", -10, -10, 20, ready ? G.Ink : G.Muted);
            string state = ready ? bag.Name : $"{bag.Name}  {Clock(bag.Cooldown)}";
            float w = Measure(state, 16, bold: true);
            Rect(-w / 2 - 6, 20, w + 12, 22, new Color(0.08f, 0.08f, 0.08f, 0.78f));
            Text(state, -w / 2, 22, 16, ready ? G.Ink : G.Muted, bold: true);
            if (ready)
            {
                string id = bag.Id;
                _hits.Add(new Hit(new Rect2(at - new Vector2(r, r), new Vector2(2 * r, 2 * r)), HitKind.Button, () => Model.Respawn(id)));
            }
        }
        if (death != null)
        {
            Vector2 at = MapPoint(death.MapX, death.MapY);
            _p.DrawLine(at + new Vector2(0, Ui.Pxf(10)), at + new Vector2(0, Ui.Pxf(30)), G.Red, Ui.Pxf(3), true);
            _p.DrawCircle(at, Ui.Pxf(16), G.Red, true, -1, true);
            _o = at;
            Glyph("skull", -10, -11, 20, G.Ink);
        }
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
