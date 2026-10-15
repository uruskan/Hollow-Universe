using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using HollowUniverse.UI.Model;
using SimStack = Hollow.Simulation.Items.ItemStack;
using UiStack = HollowUniverse.UI.Model.ItemStack;

namespace HollowUniverse.Ground;

public sealed partial class ClientGroundUi
{
    public int AttachmentKind(string key) => C.Item(key)?.Attachment is { } part ? (int)part.Slot : -1;

    public IReadOnlyList<AttachmentSlotView> Attachments(SlotRef weapon)
    {
        if (Resolve(weapon) is not var (which, index) || Me.Resolve(which) is not { } container || !container.InRange(index) ||
            C.Item(container[index].Item)?.Firearm is not { } gun) return Array.Empty<AttachmentSlotView>();
        SimStack stack = container[index];
        return gun.Attachments.Where(a => a.Attachment != null).GroupBy(a => a.Attachment!.Slot).OrderBy(g => g.Key)
            .Select(g => new AttachmentSlotView((int)g.Key, g.Key.ToString().ToUpperInvariant(),
                C.Item(stack.Mod(g.Key)) is { } fitted ? new UiStack(fitted.Key, 1) : default, g.Select(a => a.Key).ToArray())).ToArray();
    }

    public string MoveSignature(SlotRef slot)
    {
        if (Resolve(slot) is not var (which, index) || Me.Resolve(which) is not { } container || !container.InRange(index)) return "";
        SimStack s = container[index];
        return $"{s.Item}:{s.Count}:{s.Condition}:{s.Loaded}:{s.LoadedAmmo}:{s.Sight}:{s.Muzzle}:{s.Rail}";
    }

    private bool MoveSocket(SlotRef from, SlotRef to)
    {
        if (Resolve(from) is not var (a, i) || Resolve(to) is not var (b, j)) return false;
        int toMod = to.Attachment;
        if (from.Attachment < 0 && toMod < 0 && Me.Resolve(a) is { } source && source.InRange(i) &&
            C.Item(source[i].Item)?.Attachment is { } part && Me.Resolve(b) is { } target && target.InRange(j) && C.Item(target[j].Item)?.Firearm != null)
            toMod = (int)part.Slot;
        if (from.Attachment < 0 && toMod < 0) return false;
        Me.MoveAttachment(a, i, from.Attachment, b, j, toMod);
        return true;
    }

    private void QuickMoveSocket(SlotRef slot)
    {
        foreach (ContainerId which in new[] { ContainerId.Main, ContainerId.Belt })
        {
            IReadOnlyList<UiStack> slots = Slots(which);
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].IsEmpty) { MoveSocket(slot, new SlotRef(which, i)); return; }
        }
        Notice?.Invoke("No free slot for the attachment.");
    }
}
