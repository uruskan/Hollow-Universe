using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The HUD (RustMVP's, from SBareRoot.cpp): the centre dot, hit marker, damage direction and prompt in the middle;
// the belt with its ammo counter at the bottom centre; vitals, statuses and pickups at the bottom right; chat and
// notices at the bottom left.
public partial class GroundUi
{
    private const double PickupSeconds = 4;
    private const double ChatSeconds = 12;
    private static readonly Color Cold = new(0.36f, 0.60f, 0.82f);
    private static readonly Color Wet = new(0.27f, 0.49f, 0.71f);
    private static readonly Color Comfort = new(0.64f, 0.58f, 0.27f);

    private void PaintCentre()
    {
        _o = CentreFrame;
        bool alive = Model.Death == null && Model.Vitals.Health > 0;
        if (alive)
        {
            Text("·", 789, 428, 35, new Color(1, 1, 0.94f, 0.8f));
            if (Model.TargetHealth is { Fraction: > 0 and < 1 } health)
            {
                float fraction = Math.Clamp(health.Fraction, 0, 1);
                Rect(680, 366, 240, 53, G.HudSlot);
                TextIn(HealthTitle(health.Name), 690, 370, 174, 15, G.Ink, HorizontalAlignment.Left, bold: true);
                TextIn($"{MathF.Round(fraction * 100):0}%", 868, 370, 42, 15, G.Ink, HorizontalAlignment.Right, bold: true);
                Rect(690, 401, 220, 5, G.Panel);
                Rect(690, 401, 220 * fraction, 5, fraction < .25f ? G.Red : G.Health);
            }
        }
        double hit = Age(_hitAt);
        if (hit < 0.3)
        {
            float a = 1 - (float)(hit / 0.3) * (HoldEffects ? 0 : 1);
            Color c = _headshot ? new Color(1f, 0.35f, 0.3f, a) : new Color(1f, 1f, 0.9f, a);
            Line(c, 2, new Vector2(788, 438), new Vector2(795, 445));
            Line(c, 2, new Vector2(805, 455), new Vector2(812, 462));
            Line(c, 2, new Vector2(788, 462), new Vector2(795, 455));
            Line(c, 2, new Vector2(805, 445), new Vector2(812, 438));
        }
        // Damage: a red arc around the centre, pointing to where it came from; it fades in a second and a half.
        Vector2 centre = Pt(800, 450);
        foreach ((float bearing, double at) in _damage)
        {
            double age = Age(at);
            if (age > 1.5)
            {
                continue;
            }
            float alpha = 0.85f * (1 - (float)(age / 1.5));
            float middle = Mathf.DegToRad(bearing - 90);
            float half = Mathf.DegToRad(24);
            _p.DrawArc(centre, Ui.Pxf(122), middle - half, middle + half, 32, new Color(G.Red, alpha), Ui.Pxf(9), true);
            _p.DrawArc(centre, Ui.Pxf(128), middle - half * 0.6f, middle + half * 0.6f, 24, new Color(G.Red, alpha * 0.6f), Ui.Pxf(3), true);
        }
        if (alive && Model.Prompt is InteractionPrompt prompt)
        {
            CentreText($"[{prompt.Key}]  {prompt.Action}", 522, 26, G.Ink);
            if (prompt.MoreOptions)
            {
                CentreText($"Hold [{prompt.Key}] for more options", 556, 23, G.Ink, bold: true, condensed: false);
            }
        }
    }

    private static string HealthTitle(string name)
    {
        string title = name.ToUpperInvariant();
        FontFile font = FontFor(15, true, false);
        bool Fits(string value) => font.GetStringSize(value, HorizontalAlignment.Left, -1, Ui.Px(15)).X <= Ui.Pxf(174);
        if (Fits(title)) return title;
        int[] starts = StringInfo.ParseCombiningCharacters(title);
        for (int i = starts.Length - 1; i > 0; i--)
        {
            string shortened = title[..starts[i]] + "…";
            if (Fits(shortened)) return shortened;
        }
        return "…";
    }

    private void PaintBelt(bool inventory)
    {
        _o = CanvasFrame;
        IReadOnlyList<ItemStack> belt = Model.Slots(ContainerId.Belt);
        for (int i = 0; i < 6 && i < belt.Count; i++)
        {
            Slot(new SlotRef(ContainerId.Belt, i), belt[i], 550 + i * 80, 802, 75, inventory, i == Model.ActiveBelt, Screen == GroundScreen.Inventory);
        }
        if (Screen == GroundScreen.Inventory)
        {
            PanelArea(550, 802, 475, 75);
        }
        if (!inventory && Radial == null && Model.Death == null && Model.Ammo is HeldAmmo ammo)
        {
            Text($"{ammo.Loaded:00}  /  {ammo.Reserve}", 885, 763, 27, G.Ink, bold: true);
            if (ammo.Reloading)
            {
                Text("RELOADING", 550, 771, 18, G.Green, bold: true);
            }
        }
    }

    private void PaintVitals(bool inventory)
    {
        _o = RightFrame;
        Vitals v = Model.Vitals;
        (float Value, float Max, Color Colour, string Glyph)[] bars =
        {
            (v.Health, Vitals.MaxHealth, G.Health, "health"),
            (v.Hydration, Vitals.MaxHydration, G.Water, "water"),
            (v.Calories, Vitals.MaxCalories, G.Food, "food"),
        };
        for (int i = 0; i < bars.Length; i++)
        {
            (float value, float max, Color colour, string glyph) = bars[i];
            float x = 1340, y = 778 + i * 35;
            Rect(x, y, 240, 32, inventory ? new Color(0.71f, 0.69f, 0.65f, 0.22f) : G.HudSlot);
            float fill = 202 * Math.Clamp(value / max, 0, 1);
            if (fill > 0)
            {
                Rect(x + 30, y + 4, fill, 25, new Color(colour, inventory ? 1 : 0.9f));
            }
            Glyph(glyph, x + 4, y + 5, 23, G.Ink);
            Text(MathF.Round(value).ToString(CultureInfo.InvariantCulture), x + 38, y + 1, 21, G.Ink, bold: true);
        }
        if (inventory)
        {
            PanelArea(1340, 778, 240, 102);
            return;
        }
        List<(string Glyph, string Label, string Value, Color Colour)> statuses = Statuses(v);
        for (int i = 0; i < statuses.Count; i++)
        {
            (string glyph, string label, string value, Color colour) = statuses[i];
            float y = 739 - i * 35;
            Rect(1340, y, 240, 32, colour);
            Glyph(glyph, 1345, y + 5, 22, G.Ink);
            Text(label, 1375, y + 5, 18, G.Ink, bold: true);
            TextIn(value, 1340, y + 5, 230, 18, G.Ink, HorizontalAlignment.Right, bold: true);
        }
        _statusRows = statuses.Count;
    }

    private int _statusRows;

    /// <summary>The status rows above the vitals, bottom first: building rights, bleeding, cold or heat, wet, comfort.</summary>
    private static List<(string, string, string, Color)> Statuses(Vitals v)
    {
        var rows = new List<(string, string, string, Color)>();
        if (v.BreathFraction < 0.999f || v.Suffocating)
        {
            rows.Add(("oxygen", "OXYGEN", Ui.Number(Math.Clamp(v.BreathFraction, 0, 1) * 100, "N0") + "%", v.Suffocating ? G.Red : Cold));
        }
        if (v.Building == Privilege.Allowed)
        {
            rows.Add(("privilege", "BUILDING PRIVILEGE", "", G.Green));
        }
        else if (v.Building == Privilege.Blocked)
        {
            rows.Add(("blocked", "BUILDING BLOCKED", "", G.Red));
        }
        if (v.Bleeding > 0)
        {
            rows.Add(("bleeding", "BLEEDING", Ui.Number(v.Bleeding, "0.#"), G.Red));
        }
        if (v.Cold)
        {
            rows.Add(("cold", "COLD", Ui.Number(v.Temperature, "N0") + "°C", Cold));
        }
        else if (v.Hot)
        {
            rows.Add(("hot", "HOT", Ui.Number(v.Temperature, "N0") + "°C", G.Food));
        }
        if (v.Wetness > 0.01f)
        {
            rows.Add(("wet", "WET", Ui.Number(v.Wetness * 100, "N0") + "%", Wet));
        }
        if (v.Comfort > 0.01f)
        {
            rows.Add(("comfort", "COMFORT", Ui.Number(v.Comfort * 100, "N0") + "%", Comfort));
        }
        return rows;
    }

    // Pickups and crafts ("+20 Wood"), above the statuses, newest at the bottom; each fades after four seconds.
    private void PaintFeed()
    {
        _o = RightFrame;
        _pickups.RemoveAll(p => Age(p.At) > PickupSeconds + 0.5);
        float bottom = 739 - _statusRows * 35;
        for (int i = _pickups.Count - 1, row = 0; i >= 0; i--, row++)
        {
            Pickup pickup = _pickups[i];
            float alpha = Math.Clamp((float)((PickupSeconds + 0.5 - Age(pickup.At)) / 0.5), 0, 1);
            float y = bottom - row * 35;
            Rect(1340, y, 240, 32, new Color(G.HudSlot, G.HudSlot.A * alpha));
            Icon(pickup.Key, 1343, y + 2, 28, 28, alpha);
            Text(Model.Item(pickup.Key).Name, 1377, y + 5, 18, new Color(G.Ink, alpha), bold: true);
            TextIn("+" + pickup.Amount.ToString(CultureInfo.InvariantCulture), 1340, y + 5, 230, 18, new Color(G.Green, alpha),
                HorizontalAlignment.Right, bold: true);
        }
    }

    // Chat at the bottom left: names in colour, lines fading after twelve seconds unless the chat is open.
    private void PaintChat()
    {
        _o = LeftFrame;
        float lineHeight = 24, bottom = IsChatting ? 776 : 808;
        if (IsChatting)
        {
            Rect(12, bottom - 8 * lineHeight - 6, 456, 8 * lineHeight + 8, new Color(0.06f, 0.07f, 0.07f, 0.5f));
        }
        FontFile font = G.Medium;
        int px = Ui.Px(18);
        for (int i = _chatLines.Count - 1, row = 0; i >= 0; i--, row++)
        {
            (ChatLine line, double at) = _chatLines[i];
            float alpha = IsChatting ? 1 : Math.Clamp((float)((ChatSeconds - Age(at)) / 2), 0, 1);
            if (alpha <= 0)
            {
                continue;
            }
            Vector2 top = At(20, bottom - (row + 1) * lineHeight);
            var baseline = new Vector2(top.X, MathF.Round(top.Y + font.GetAscent(px)));
            string name = line.FromServer ? "" : (line.Team ? "[TEAM] " : "") + line.From + ": ";
            Color shadow = new(0, 0, 0, 0.45f * alpha);
            int outline = Ui.Px(3);
            _p.DrawStringOutline(font, baseline, name + line.Text, HorizontalAlignment.Left, -1, px, outline, shadow);
            if (name.Length > 0)
            {
                _p.DrawString(font, baseline, name, HorizontalAlignment.Left, -1, px, line.Team ? new Color(G.Green, alpha) : new Color(0.56f, 0.77f, 0.95f, alpha));
                baseline.X += font.GetStringSize(name, HorizontalAlignment.Left, -1, px).X;
            }
            _p.DrawString(font, baseline, line.Text, HorizontalAlignment.Left, -1, px, new Color(line.FromServer ? G.Star : G.Ink, alpha));
        }
    }

    // RustMVP's toast: one line at the bottom left for four and a half seconds.
    private void PaintToast()
    {
        if (_toast.Length == 0 || Age(_toastAt) > 4.5 || IsChatting)
        {
            return;
        }
        _o = LeftFrame;
        Text(_toast, 105, 842, 16, G.Toast);
    }
}
