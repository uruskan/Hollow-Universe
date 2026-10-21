using System;
using Godot;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The loot panels of G7's machines (GROUND.md §6.8), in the furnace's manner: the recycler's six inputs and six outputs
// with its switch (no fuel), and the research table's item, salvage and blueprint slots with the price and BEGIN
// RESEARCH. A world crate is the box's grid (take only). A vehicle in the vehicle workbench's bay (VEHICLES.md §9.11) shows
// its sockets.
public partial class GroundUi
{
    // A vehicle in a workbench's bay (VEHICLES.md §9.11): a slot for each socket and level under its name, three to a row; then what its
    // build makes, and why it can't be changed now.
    private float PaintVehicle(LootView loot, float y, Action<int, float, float, float> cell)
    {
        float top = y + 67;
        for (int i = 0; i < loot.Slots.Count; i++)
        {
            float x = 1040 + i % 3 * 160, row = top + i / 3 * 104;
            Text(i < loot.Labels.Count ? loot.Labels[i] : "", x, row, 14, Caption, bold: true);
            cell(i, x, row + 22, 75);
        }
        float at = top + (loot.Slots.Count + 2) / 3 * 104 + 4;
        foreach (string line in loot.Lines)
        {
            Rect(1040, at, 475, 27, G.Panel);
            Text(line, 1050, at, 16, G.Ink, bold: true);
            at += 31;
        }
        if (loot.Reason is { } reason)
        {
            Rect(1040, at, 475, 27, new Color(G.Red, 0.85f));
            Text(reason.ToUpperInvariant(), 1050, at, 16, G.Ink, bold: true);
            at += 31;
        }
        else
        {
            Text(loot.Hint ?? "DRAG A MODULE IN TO FIT IT, OUT TO TAKE IT OFF", 1040, at + 4, 14, Caption, bold: true);
            at += 31;
        }
        return at;
    }

    /// <summary>Paints a machine's panel below its LOOT heading at <paramref name="y"/>; returns its bottom.</summary>
    private float PaintMachine(LootView loot, float y, Action<int, float, float, float> cell)
    {
        if (loot.Kind == ContainerKind.Recycler)
        {
            Text("INPUT", 1040, y + 67, 16, Caption, bold: true);
            for (int i = 0; i < 6; i++)
            {
                cell(i, 1040 + i * 80, y + 91, 75);
            }
            Text("OUTPUT", 1040, y + 174, 16, Caption, bold: true);
            for (int i = 0; i < 6; i++)
            {
                cell(6 + i, 1040 + i * 80, y + 198, 75);
            }
            Rect(1040, y + 281, 475, 6, G.Panel);
            if (loot.IsOn)
            {
                Rect(1040, y + 281, 475 * Math.Clamp(loot.Progress, 0, 1), 6, G.Green);
            }
            Rect(1040, y + 291, 475, 27, G.Panel);
            Text(loot.IsOn ? "RECYCLING  ·  HALF OF WHAT IT WAS MADE OF COMES BACK" : "PUT IN WHAT YOU WANT TAKEN APART", 1050, y + 291, 16, G.Ink, bold: true);
            bool on = loot.IsOn;
            Button(on ? "TURN OFF" : "TURN ON", 1040, y + 325, 475, 48, () => Model.SetBurning(!on), 24,
                on ? new Color(G.Red, 0.85f) : new Color(G.Green, 0.85f));
            return y + 373;
        }
        // The research table: the item, the salvage, the blueprint that comes out.
        (string Label, int Slot)[] slots = { ("ITEM", 0), ("SALVAGE", 1), ("BLUEPRINT", 2) };
        for (int i = 0; i < slots.Length; i++)
        {
            float x = 1040 + i * 160;
            Text(slots[i].Label, x, y + 67, 16, Caption, bold: true);
            cell(slots[i].Slot, x, y + 91, 75);
            if (i < 2)
            {
                Line(G.Muted, 2, new Vector2(x + 95, y + 128), new Vector2(x + 140, y + 128));
                Line(G.Muted, 2, new Vector2(x + 132, y + 122), new Vector2(x + 140, y + 128), new Vector2(x + 132, y + 134));
            }
        }
        Rect(1040, y + 174, 475, 6, G.Panel);
        if (loot.IsOn)
        {
            Rect(1040, y + 174, 475 * Math.Clamp(loot.Progress, 0, 1), 6, G.Green);
        }
        Rect(1040, y + 184, 475, 27, G.Panel);
        string line = loot.IsOn ? $"RESEARCHING {Model.Item(loot.Researching ?? "").Name.ToUpperInvariant()}"
            : loot.Reason != null ? loot.Reason.ToUpperInvariant()
            : $"{Model.Item(loot.Researching ?? "").Name.ToUpperInvariant()}: {loot.Cost} SALVAGE";
        Text(line, 1050, y + 184, 16, loot.Reason != null && !loot.IsOn ? G.Short : G.Ink, bold: true);
        bool can = !loot.IsOn && loot.Reason == null;
        Button(loot.IsOn ? "RESEARCHING…" : "BEGIN RESEARCH", 1040, y + 218, 475, 48, can ? () => Model.SetBurning(true) : null, 24,
            can ? new Color(G.Green, 0.85f) : G.Panel, enabled: can);
        return y + 266;
    }
}
