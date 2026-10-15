using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The inventory (RustMVP's, from SBareRoot.cpp): the character and clothing on the left, the item panel and the
// 6 × 4 grid in the middle, the loot panel (or quick craft) on the right; and RustMVP's gestures.
public partial class GroundUi
{
    private sealed class DragState
    {
        public SlotRef From;
        public string Key = "";
        public int Amount;
        public Vector2 Origin;
        public bool Moving;
        public MouseButton Button;
    }

    private DragState? _drag;
    private bool _splitTracking;
    private SlotRef? _selected;
    private int _splitValue = 1;
    private string _splitSignature = "";
    private readonly Dictionary<SlotRef, double> _hoverStarted = new();
    private readonly HashSet<SlotRef> _hoverDone = new();
    private static readonly Color Transparent = new(0, 0, 0, 0);
    private static readonly Color Caption = new(0.89f, 0.875f, 0.84f, 0.8f);

    private ItemStack StackAt(SlotRef slot)
    {
        IReadOnlyList<ItemStack> slots = Model.Slots(slot.Container);
        return slot.Index >= 0 && slot.Index < slots.Count ? slots[slot.Index] : default;
    }

    private void PaintInventory()
    {
        _o = TopFrame;
        Button("", 802, 0, 250, 60, () => Open(GroundScreen.Crafting), 30);
        Text("CRAFTING", 846, 13, 30, G.Ink, bold: true, condensed: true);
        Arrow(995, 33);

        _o = CanvasFrame;
        _p.DrawTextureRect(_character.GetTexture(), Area(70, 40, 455, 628), false);
        PanelArea(70, 40, 455, 628);
        Text(Model.PlayerName, 212, 84, 26, G.Ink, bold: true);

        Section("INVENTORY", 550, 449);
        IReadOnlyList<ItemStack> main = Model.Slots(ContainerId.Main);
        for (int i = 0; i < 24 && i < main.Count; i++)
        {
            Slot(new SlotRef(ContainerId.Main, i), main[i], 550 + i % 6 * 80, 476 + i / 6 * 80, 75, true, false, true);
        }
        PanelArea(545, 445, 492, 435);
        IReadOnlyList<ItemStack> wear = Model.Slots(ContainerId.Wear);
        for (int i = 0; i < 7 && i < wear.Count; i++)
        {
            Slot(new SlotRef(ContainerId.Wear, i), wear[i], 67 + i * 67, 692, 63, true, false, true);
        }
        PanelArea(67, 692, 469, 63);
        PaintItemPanel();
        if (Model.Loot is LootView loot)
        {
            PaintLoot(loot);
        }
        else
        {
            PaintQuickCraft();
        }
    }

    // ---------------------------------------------------------------- the selected item

    private void PaintItemPanel()
    {
        if (_selected is not SlotRef slot)
        {
            return;
        }
        ItemStack stack = StackAt(slot);
        if (stack.IsEmpty)
        {
            return;
        }
        string signature = $"{slot}:{stack.Key}:{stack.Amount}";
        if (signature != _splitSignature)
        {
            _splitSignature = signature;
            _splitValue = Math.Max(1, (stack.Amount + 1) / 2);
        }
        ItemInfo info = Model.Item(stack.Key);
        bool split = stack.Amount > 1;
        float content = split ? 298 : 361;
        Text(info.Name.ToUpperInvariant(), 552, 84, 26, G.Ink, bold: true);
        Rect(552, 116, 475, 76, G.Panel);
        Paragraph(info.Description, 567, 127, 368, 17, G.Ink, 3);
        Icon(stack.Key, 950, 121, 70, 66);
        // INFORMATION (the describers' rows, GROUND_SYSTEMS.md §7) and ACTIONS side by side.
        Rect(552, 194, InfoWidth, content - 109, G.Panel);
        Rect(ActionsX, 194, ActionsWidth, content - 109, G.Panel);
        Rect(552, 194, InfoWidth, 25, G.Panel);
        Rect(ActionsX, 194, ActionsWidth, 25, G.Panel);
        Text("INFORMATION", 558, 194, 17, G.Ink, bold: true);
        Text("ACTIONS", ActionsX + 6, 194, 17, G.Ink, bold: true);
        PanelArea(545, 80, 492, 370);
        PaintRows(Model.Rows(slot), 194 + content - 109);

        // The stack's own actions (its parts', GROUND_SYSTEMS.md §7), then DROP; a refused one shows muted and says why.
        IReadOnlyList<ItemActionView> actions = Model.Actions(slot);
        int count = actions.Count;
        for (int i = 0; i <= count; i++)
        {
            ItemActionView action = i < count ? actions[i] : new ItemActionView("DROP", () => Model.Drop(slot, StackAt(slot).Amount));
            string label = action.Label;
            Button(label, ActionsX, 222 + i * 40, ActionsWidth, 37, action.Reason is { } why ? () => OnNotice(why) : action.Act,
                label.Length > 15 ? 16 : label.Length > 11 ? 19 : 21, muted: action.Reason != null);
        }

        if (split)
        {
            Rect(552, 382, 475, 25, G.Panel);
            Text("SPLITTING", 558, 382, 16, G.Ink, bold: true);
            TextIn("SET AMOUNT & DRAG ICON", 691, 382, 271, 16, new Color(0.85f, 0.82f, 0.78f, 0.64f), HorizontalAlignment.Right);
            int amount = Math.Clamp(_splitValue, 1, stack.Amount);
            float fill = 410f * (amount - 1) / Math.Max(1, stack.Amount - 1);
            Rect(552, 409, 410, 36, new Color(0.75f, 0.72f, 0.68f, 0.12f));
            if (fill > 0)
            {
                Rect(552, 409, fill, 36, new Color(0.85f, 0.82f, 0.76f, 0.35f));
            }
            Text(amount.ToString(CultureInfo.InvariantCulture), 552 + Math.Clamp(fill - 5, 0, 386), 415, 16, G.Ink, bold: true, condensed: true);
            _hits.Add(new Hit(Area(552, 409, 410, 36), HitKind.SplitTrack, Slot: slot));
            Rect2 icon = Area(965, 382, 62, 63);
            _p.DrawRect(icon, Under(icon) ? G.PanelHover : new Color(0.71f, 0.69f, 0.65f, 0.07f));
            Icon(stack.Key, 972, 387, 48, 51);
            TextIn(Count(amount), 971, 420, 50, 16, G.Ink, HorizontalAlignment.Right, bold: true);
            _hits.Add(new Hit(icon, HitKind.SplitIcon, Slot: slot));
            PanelArea(552, 382, 475, 63);
        }
    }

    // The INFORMATION column's width and the ACTIONS column's place and width (design pixels).
    private const float InfoWidth = 312, ActionsX = 867, ActionsWidth = 160;

    // The rows of the INFORMATION column, as many as fit above `bottom`: the label on the left, the value on the right in
    // green (good news) or amber, its font narrowed until the two fit side by side.
    private void PaintRows(IReadOnlyList<ItemStat> rows, float bottom)
    {
        const float step = 17, size = 14, left = 560, right = 552 + InfoWidth - 8;
        for (int i = 0; i < rows.Count && 224 + (i + 1) * step <= bottom - 2; i++)
        {
            float y = 224 + i * step;
            float label = Text(rows[i].Label, left, y, size, G.Ink, bold: true);
            float room = right - left - label - 12, value = size;
            while (value > 10 && Measure(rows[i].Value, value, bold: true) > room)
            {
                value--;
            }
            TextIn(rows[i].Value, left + label + 12, y + (size - value) / 2, room, value, rows[i].Positive ? G.Green : G.Short,
                HorizontalAlignment.Right, bold: true);
        }
    }

    // ---------------------------------------------------------------- the right column

    private void PaintQuickCraft()
    {
        List<Recipe> craftable = Model.Recipes.Where(r => CanCraft(r, 1)).Take(18).ToList();
        float y = craftable.Count > 0 ? 763 - (craftable.Count + 5) / 6 * 80 - 32 : 630;
        Section("QUICK CRAFT", 1040, y);
        for (int i = 0; i < craftable.Count; i++)
        {
            string key = craftable[i].Key;
            float x = 1040 + i % 6 * 80, top = y + 32 + i / 6 * 80;
            Button("", x, top, 75, 75, () => Model.Craft(key, 1), fill: Transparent, tip: key);
            Icon(key, x + 14, top + 14, 47, 47);
            if (_favourites.Contains(key))
            {
                StarShape(x + 66, top + 10, 9, G.Star);
            }
        }
        if (craftable.Count == 0)
        {
            Rect(1040, y + 32, 475, 102, G.Panel);
            Paragraph("Items you have the resources for show here. You can't craft anything with what you carry right now.",
                1060, y + 49, 435, 18, G.Ink);
        }
        PanelArea(1037, y - 4, 483, 767 - y);
    }

    private void PaintLoot(LootView loot)
    {
        IReadOnlyList<ItemStack> slots = loot.Slots;
        // A take-only grid (a crate, a bag of dropped items) shows the rows its stacks fill: nothing goes into the rest.
        int cells = loot.Kind == ContainerKind.Crate ? FilledRows(slots) * 6 : slots.Count;
        float y = loot.Kind == ContainerKind.Corpse || cells > 36 ? 70 : 191;
        Section("LOOT", 1040, y);
        Rect(1040, y + 34, 475, 27, G.Panel);
        Text(loot.Title.ToUpperInvariant(), 1050, y + 34, 16, G.Ink, bold: true);
        void Cell(int index, float x, float top, float size = 75)
        {
            if (index < slots.Count)
            {
                Slot(new SlotRef(ContainerId.Loot, index), slots[index], x, top, size, true, false, true);
            }
        }
        float bottom;
        switch (loot.Kind)
        {
            case ContainerKind.Corpse:
                for (int i = 0; i < 24; i++)
                {
                    Cell(i, 1040 + i % 6 * 80, y + 67 + i / 6 * 80);
                }
                Text("CLOTHING", 1040, y + 388, 16, Caption, bold: true);
                for (int i = 0; i < 7; i++)
                {
                    Cell(30 + i, 1040 + i * 67, y + 412, 63);
                }
                Text("BELT", 1040, y + 482, 16, Caption, bold: true);
                for (int i = 0; i < 6; i++)
                {
                    Cell(24 + i, 1040 + i * 80, y + 506);
                }
                bottom = y + 581;
                break;
            case ContainerKind.Furnace or ContainerKind.Campfire:
            {
                bool furnace = loot.Kind == ContainerKind.Furnace;
                Text("FUEL", 1040, y + 67, 16, Caption, bold: true);
                Text(furnace ? "INPUT" : "COOKING", 1120, y + 67, 16, Caption, bold: true);
                if (furnace)
                {
                    Text("OUTPUT", 1280, y + 67, 16, Caption, bold: true);
                }
                for (int i = 0; i < slots.Count && i < 6; i++)
                {
                    Cell(i, 1040 + i * 80, y + 91);
                }
                Rect(1040, y + 174, 475, 6, G.Panel);
                if (loot.IsOn && loot.Progress > 0)
                {
                    Rect(1040, y + 174, 475 * Math.Clamp(loot.Progress, 0, 1), 6, G.Green);
                }
                string fuel = slots.Count > 0 && !slots[0].IsEmpty
                    ? $"{Model.Item(slots[0].Key).Name} lasts {Minutes(loot.FuelSeconds)}"
                    : "No fuel";
                Rect(1040, y + 184, 475, 27, G.Panel);
                Text(loot.IsOn ? (furnace ? "SMELTING  ·  " : "COOKING  ·  ") + fuel.ToUpperInvariant() : fuel.ToUpperInvariant(), 1050, y + 184, 16, G.Ink, bold: true);
                bool on = loot.IsOn;
                Button(on ? "TURN OFF" : "TURN ON", 1040, y + 218, 475, 48, () => Model.SetBurning(!on), 24,
                    on ? new Color(G.Red, 0.85f) : new Color(G.Green, 0.85f));
                bottom = y + 266;
                break;
            }
            case ContainerKind.Recycler or ContainerKind.ResearchTable:
                bottom = PaintMachine(loot, y, (i, x, top, size) => Cell(i, x, top, size));
                break;
            default:
                for (int i = 0; i < cells; i++)
                {
                    Cell(i, 1040 + i % 6 * 80, y + 67 + i / 6 * 80);
                }
                bottom = y + 67 + (cells + 5) / 6 * 80;
                break;
        }
        PanelArea(1037, y - 4, 483, bottom - y + 8);
    }

    // Rows of six down to the last filled slot (at least one).
    private static int FilledRows(IReadOnlyList<ItemStack> slots)
    {
        int last = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            last = slots[i].IsEmpty ? last : i;
        }
        return Math.Max(1, last / 6 + 1);
    }

    private static string Minutes(float seconds)
    {
        int s = (int)MathF.Ceiling(seconds);
        return s >= 60 ? $"{s / 60} min {s % 60:00} s" : $"{s} s";
    }

    // ---------------------------------------------------------------- the drag ghost and the tooltip

    private void PaintDragAndTooltip()
    {
        _o = CanvasFrame;
        Vector2 p = DesignPointer;
        if (_drag is { Moving: true } drag)
        {
            Icon(drag.Key, p.X - 37, p.Y - 37, 75, 75);
            Text("x" + drag.Amount.ToString(CultureInfo.InvariantCulture), p.X, p.Y + 15, 18, G.Ink, bold: true);
            return;
        }
        if (_splitTracking || Input.IsKeyPressed(Key.H) || HitAt(Pointer) is not Hit hit)
        {
            return;
        }
        string? key = hit.Kind == HitKind.Slot ? StackAt(hit.Slot).Key : hit.Key;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        string name = Model.Item(key).Name;
        float w = Measure(name, 18) + 24;
        float x = Math.Min(p.X + 16, 1590 - w), y = Math.Min(p.Y + 20, 864);
        Rect(x, y, w, 31, G.Tooltip);
        Text(name, x + 10, y + 3, 18, G.Ink);
    }

    // ---------------------------------------------------------------- gestures (RustMVP's rules)

    private void SelectSlot(SlotRef slot)
    {
        _selected = slot;
        _splitSignature = "";
    }

    private void PressInventory(Hit hit, MouseButton button)
    {
        if (hit.Kind == HitKind.SplitTrack)
        {
            if (button == MouseButton.Left)
            {
                _splitTracking = true;
                UpdateSplit();
            }
            return;
        }
        ItemStack stack = StackAt(hit.Slot);
        if (hit.Kind == HitKind.Slot && button == MouseButton.Right)
        {
            Model.QuickMove(hit.Slot);
            return;
        }
        if (hit.Kind == HitKind.Slot && button == MouseButton.Left)
        {
            SelectSlot(hit.Slot);
        }
        if (stack.IsEmpty || Input.IsKeyPressed(Key.H) || button is not (MouseButton.Left or MouseButton.Middle))
        {
            return;
        }
        int amount = hit.Kind == HitKind.SplitIcon ? Math.Clamp(_splitValue, 1, stack.Amount)
            : button == MouseButton.Middle ? (stack.Amount + (Input.IsKeyPressed(Key.Shift) ? 2 : 1)) / (Input.IsKeyPressed(Key.Shift) ? 3 : 2)
            : stack.Amount;
        _drag = new DragState { From = hit.Slot, Key = stack.Key, Amount = Math.Max(1, amount), Origin = Pointer, Button = button };
    }

    private void PointerMoved()
    {
        if (_drag is { Moving: false } drag && drag.Origin.DistanceTo(Pointer) > Ui.Pxf(5))
        {
            drag.Moving = true;
            Sound?.Invoke("ui.drag");
        }
        if (_splitTracking)
        {
            UpdateSplit();
        }
        DragGlobe();
        DragConsoleSlider();
    }

    private void UpdateSplit()
    {
        if (_selected is not SlotRef slot)
        {
            return;
        }
        int count = StackAt(slot).Amount;
        _o = CanvasFrame;
        float t = Math.Clamp((DesignPointer.X - 552) / 410, 0, 1);
        _splitValue = Math.Clamp((int)MathF.Round(1 + t * (count - 1)), 1, Math.Max(1, count));
    }

    // A drag released on another slot moves there; on its own slot it is put back; anywhere else, over a panel, a button or
    // the world behind, it drops (Rust; GROUND_SYSTEMS.md §7).
    private void ReleaseInventory(MouseButton button)
    {
        if (_splitTracking && button == MouseButton.Left)
        {
            _splitTracking = false;
            return;
        }
        if (_drag is not DragState drag || drag.Button != button)
        {
            return;
        }
        _drag = null;
        if (!drag.Moving || Screen != GroundScreen.Inventory)
        {
            return;
        }
        if (HitAt(Pointer) is { Kind: HitKind.Slot } target)
        {
            if (target.Slot != drag.From)
            {
                Sound?.Invoke("ui.drop");
                Model.Move(drag.From, target.Slot, drag.Amount);
            }
            return;
        }
        Sound?.Invoke("ui.drop");
        Model.Drop(drag.From, drag.Amount);
    }

    // H held: each filled slot the pointer rests on for 350 ms moves across once (RustMVP's hover transfer).
    private void HoverTransfer()
    {
        bool held = Input.IsKeyPressed(Key.H) && GetViewport().GuiGetFocusOwner() is not LineEdit;
        if (!held)
        {
            _hoverStarted.Clear();
            _hoverDone.Clear();
            return;
        }
        if (_drag != null)
        {
            return;
        }
        if (HitAt(Pointer) is { Kind: HitKind.Slot } hit && !StackAt(hit.Slot).IsEmpty && !_hoverDone.Contains(hit.Slot)
            && !_hoverStarted.ContainsKey(hit.Slot))
        {
            _hoverStarted[hit.Slot] = _now;
        }
        foreach ((SlotRef slot, double started) in _hoverStarted.ToArray())
        {
            if (_now - started >= HoverSeconds)
            {
                _hoverStarted.Remove(slot);
                _hoverDone.Add(slot);
                Model.QuickMove(slot);
            }
        }
    }
}
