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
        public string Signature = "";
        public SlotRef? SelectedWeapon;
        public string WeaponSignature = "";
        public bool SelectOnClick;
    }

    private DragState? _drag;
    private bool _splitTracking;
    private bool _characterDragging;
    private bool _hoverLootHeld;

    public float CharacterYaw => _character.Yaw;
    public bool HoverLootHeld => _hoverLootHeld;
    private SlotRef? _selected;
    private int _splitValue = 1;
    private string _splitSignature = "";
    private readonly Dictionary<SlotRef, double> _hoverStarted = new();
    private readonly HashSet<SlotRef> _hoverDone = new();
    private static readonly Color Transparent = new(0, 0, 0, 0);
    private static readonly Color Caption = new(0.89f, 0.875f, 0.84f, 0.8f);

    private ItemStack StackAt(SlotRef slot)
    {
        if (slot.Attachment >= 0)
            return Model.Attachments(slot with { Attachment = -1 }).FirstOrDefault(a => a.Index == slot.Attachment)?.Stack ?? default;
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
        _hits.Add(new Hit(Area(70, 40, 455, 628), HitKind.Character));
        Text(Model.PlayerName, 212, 84, 26, G.Ink, bold: true);
        for (int i = 0; i < Model.Protection.Count; i++)
        {
            ProtectionStat stat = Model.Protection[i];
            float y = 570 + i * 29;
            Rect(75, y, 195, 26, G.Panel);
            Text(stat.Name, 82, y + 3, 14, G.Ink, bold: true);
            TextIn(stat.Value, 204, y + 3, 58, 14, G.Ink, HorizontalAlignment.Right);
            _hits.Add(new Hit(Area(75, y, 195, 26), HitKind.Button, Tooltip: stat.Description));
        }

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
        IReadOnlyList<AttachmentSlotView> attachments = Model.Attachments(slot);
        bool split = stack.Amount > 1;
        float content = split ? 298 : 361;
        Text(info.Name.ToUpperInvariant(), 552, 84, 26, G.Ink, bold: true);
        Rect(552, 116, 475, 76, G.Panel);
        Paragraph(info.Description, 567, 127, 368, 17, G.Ink, 3);
        Icon(stack.Key, 950, 121, 70, 66);
        // INFORMATION (the describers' rows, GROUND_SYSTEMS.md §7) and ACTIONS side by side.
        float rowsBottom = attachments.Count > 0 ? 355 : 194 + content - 109;
        Rect(552, 194, InfoWidth, rowsBottom - 194, G.Panel);
        Rect(ActionsX, 194, ActionsWidth, content - 109, G.Panel);
        Rect(552, 194, InfoWidth, 25, G.Panel);
        Rect(ActionsX, 194, ActionsWidth, 25, G.Panel);
        Text("INFORMATION", 558, 194, 17, G.Ink, bold: true);
        Text("ACTIONS", ActionsX + 6, 194, 17, G.Ink, bold: true);
        PanelArea(545, 80, 492, 370);
        PaintRows(Model.Rows(slot), rowsBottom);
        for (int i = 0; i < attachments.Count; i++)
        {
            AttachmentSlotView socket = attachments[i];
            float x = 562 + i * 99;
            Text(socket.Label, x, 358, 13, G.Ink, bold: true);
            SlotRef socketRef = slot with { Attachment = socket.Index };
            Slot(socketRef, socket.Stack, x, 379, 62, true, false, true);
            Rect2 area = Area(x, 379, 62, 62);
            if (_drag is { Moving: true } dragging)
                _p.DrawRect(area, socket.Compatible.Contains(dragging.Key) ? G.Green : G.Short, false, Ui.Pxf(2));
            string compatible = string.Join(", ", socket.Compatible.Select(key => Model.Item(key).Name));
            _hits.Add(new Hit(area, HitKind.Slot, Slot: socketRef,
                Tooltip: (socket.Stack.IsEmpty ? socket.Label : Model.Item(socket.Stack.Key).Name) + ". Accepts: " + compatible + ". Drag a part here; drag it out to an inventory or storage slot."));
        }

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
        if (_splitTracking || _hoverLootHeld || _characterDragging || HitAt(Pointer) is not Hit hit)
        {
            return;
        }
        if (hit.Tooltip is { } tip)
        {
            const float width = 350, font = 16;
            List<string> lines = WrapLines(tip, width - 20, font);
            float height = lines.Count * 21 + 16;
            float left = Math.Clamp(p.X + 16, 10, 1590 - width), top = Math.Clamp(p.Y + 20, 10, 880 - height);
            Rect(left, top, width, height, G.Tooltip);
            for (int i = 0; i < lines.Count; i++)
            {
                Text(lines[i], left + 10, top + 8 + i * 21, font, G.Ink);
            }
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
        bool retainWeapon = hit.Slot.Attachment < 0 && Model.AttachmentKind(stack.Key ?? "") >= 0 &&
            _selected is { } selected && Model.Attachments(selected).Count > 0;
        if (hit.Kind == HitKind.Slot && button == MouseButton.Left && hit.Slot.Attachment < 0 && !retainWeapon)
        {
            SelectSlot(hit.Slot);
        }
        if (stack.IsEmpty || _hoverLootHeld || button is not (MouseButton.Left or MouseButton.Middle))
        {
            return;
        }
        int amount = hit.Kind == HitKind.SplitIcon ? Math.Clamp(_splitValue, 1, stack.Amount)
            : button == MouseButton.Middle ? (stack.Amount + (Input.IsKeyPressed(Key.Shift) ? 2 : 1)) / (Input.IsKeyPressed(Key.Shift) ? 3 : 2)
            : stack.Amount;
        SlotRef? weapon = _selected is { } selectedWeapon && Model.Attachments(selectedWeapon).Count > 0 ? selectedWeapon : null;
        _drag = new DragState { From = hit.Slot, Key = stack.Key!, Amount = Math.Max(1, amount), Origin = Pointer, Button = button,
            Signature = Model.MoveSignature(hit.Slot), SelectedWeapon = weapon,
            WeaponSignature = weapon is { } owner ? Model.MoveSignature(owner) : "", SelectOnClick = retainWeapon };
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
            if (!drag.Moving && drag.SelectOnClick && Screen == GroundScreen.Inventory) SelectSlot(drag.From);
            return;
        }
        if (Model.MoveSignature(drag.From) != drag.Signature)
        {
            OnNotice("That item changed while you were dragging it.");
            return;
        }
        if (drag.SelectedWeapon is { } selectedWeapon && Model.MoveSignature(selectedWeapon) != drag.WeaponSignature)
        {
            OnNotice("That weapon changed while you were dragging.");
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
        // A loose socket drag needs an explicit destination; releasing in the world never drops the whole weapon.
        if (drag.From.Attachment >= 0) return;
        Sound?.Invoke("ui.drop");
        Model.Drop(drag.From, drag.Amount);
    }

    // H held: each filled slot the pointer rests on for 350 ms moves across once (RustMVP's hover transfer).
    private void HoverTransfer()
    {
        if (!_hoverLootHeld)
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

    // Window-level input owns the held gesture. Crossing a panel or a child control never ends H or a figure drag.
    private void InventoryInput(InputEvent e)
    {
        if (e is InputEventKey { PhysicalKeycode: Key.H, Echo: false } key)
        {
            _hoverLootHeld = key.Pressed && Screen == GroundScreen.Inventory && GetViewport().GuiGetFocusOwner() is not LineEdit;
            if (!_hoverLootHeld)
            {
                _hoverStarted.Clear();
                _hoverDone.Clear();
            }
        }
        if (Screen == GroundScreen.Inventory && e is InputEventMouseMotion motion && !_pointerHeld)
        {
            if (_characterDragging)
            {
                _character.Turn((motion.Position.X - _pointer.X) / Ui.Scale);
            }
            _pointer = motion.Position;
            _pointerDesign = null;
        }
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _characterDragging = false;
        }
    }

    private void CancelInventoryGestures()
    {
        _characterDragging = _hoverLootHeld = _splitTracking = false;
        _drag = null;
        _hoverStarted.Clear();
        _hoverDone.Clear();
    }
}
