using System;
using System.Globalization;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

public partial class GroundUi
{
    /// <summary>The last painted crafting card, or null when hidden; also used by the UI regression viewer.</summary>
    public CraftingHudStatus? HudCrafting { get; private set; }

    private void PaintCraftingHud()
    {
        if (Model.Death != null || Model.Vitals.Health <= 0) return;
        var queue = Model.Queue;
        if (queue.Count == 0) return;
        CraftJob job = queue[0];
        HudCrafting = new CraftingHudStatus(job, Model.Have(job.OutputKey));
        Rect2 bounds = CraftingHudLayout(Size);
        _o = bounds.Position;
        float width = bounds.Size.X / Ui.Scale;
        Rect(0, 0, width, 102, G.HudSlot);
        Text("CRAFTING", 12, 9, 16, G.Green, bold: true);
        TextIn("×" + job.Count.ToString(CultureInfo.InvariantCulture), 12, 7, width - 24, 20,
            G.Ink, HorizontalAlignment.Right, bold: true);
        Icon(job.OutputKey, 10, 36, 44, 44);
        Text(CraftingHudTitle(Model.Item(job.OutputKey).Name, width - 76), 64, 36, 17, G.Ink, bold: true);
        string owned = "IN INVENTORY " + HudCrafting.Owned.ToString(CultureInfo.InvariantCulture);
        Text(owned, 64, 62, Measure(owned, 14) <= width - 76 ? 14 : 12, G.Ink);
        Rect(12, 90, width - 24, 3, G.Panel);
        float progress = Math.Clamp(job.UnitProgress, 0, 1);
        if (progress > 0) Rect(12, 90, (width - 24) * progress, 3, G.Green);
    }

    // Same right/bottom anchor as vitals. Keep the hotbar and its ammo/seat hints clear on narrow windows.
    internal static Rect2 CraftingHudLayout(Vector2 viewport)
    {
        float right = viewport.X - Ui.Px(276); // Vitals begin 260 design pixels from the right edge; gap 16.
        float beltRight = MathF.Floor((viewport.X - Ui.Px(1600)) / 2) + Ui.Px(1025);
        float available = (right - beltRight - Ui.Px(16)) / Ui.Scale;
        bool compact = available < 240;
        float width = compact ? 274 : Math.Min(274, available);
        return new Rect2(new Vector2(right - Ui.Px(width), viewport.Y - Ui.Px(compact ? 270 : 122)),
            new Vector2(Ui.Px(width), Ui.Px(102)));
    }

    private static string CraftingHudTitle(string name, float width)
    {
        if (Measure(name, 17, bold: true) <= width) return name;
        int[] starts = StringInfo.ParseCombiningCharacters(name);
        for (int i = starts.Length - 1; i > 0; i--)
        {
            string shortened = name[..starts[i]] + "…";
            if (Measure(shortened, 17, bold: true) <= width) return shortened;
        }
        return "…";
    }
}
