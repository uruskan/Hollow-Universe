using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using SimStack = Hollow.Simulation.Items.ItemStack;
using SimKind = Hollow.Simulation.Items.ContainerKind;

namespace HollowUniverse.UI.Model;

public sealed partial class MockGroundUi
{
    private GameContent? _attachmentContent;

    /// <summary>The UI smoke test uses the real attachment catalogue and transaction rules.</summary>
    public void AttachmentSample(GameContent content)
    {
        _attachmentContent = content;
        Array.Clear(_main);
        string[] keys = { "rifle", "scope", "holo-sight", "silencer", "muzzle-brake", "weapon-light" };
        for (int i = 0; i < keys.Length; i++)
        {
            ItemDef item = content.ItemByKey(keys[i]);
            _items[item.Key] = new ItemInfo(item.Key, item.Name, item.Description, "WEAPONS", item.StackSize);
            _main[i] = new ItemStack(item.Key, 1, item.HasCondition ? 1 : -1, item.Firearm != null ? 9 : -1);
        }
        _loot = new ItemStack[12];
        Loot = new LootView(ContainerKind.Box, "STORAGE", _loot);
    }

    public int AttachmentKind(string key) => _attachmentContent?.Item(key)?.Attachment is { } part ? (int)part.Slot : -1;

    public IReadOnlyList<AttachmentSlotView> Attachments(SlotRef weapon)
    {
        if (!Valid(weapon) || _attachmentContent?.Item(Of(weapon.Container)[weapon.Index].Key ?? "")?.Firearm is not { } gun)
            return Array.Empty<AttachmentSlotView>();
        ItemStack stack = Of(weapon.Container)[weapon.Index];
        return gun.Attachments.GroupBy(a => a.Attachment!.Slot).OrderBy(g => g.Key).Select(g =>
        {
            string? key = g.Key switch { AttachmentSlot.Sight => stack.Sight, AttachmentSlot.Muzzle => stack.Muzzle, _ => stack.Rail };
            return new AttachmentSlotView((int)g.Key, g.Key.ToString().ToUpperInvariant(), key != null ? new ItemStack(key, 1) : default,
                g.Select(a => a.Key).ToArray());
        }).ToArray();
    }

    public string MoveSignature(SlotRef slot) => Valid(slot) ? Of(slot.Container)[slot.Index].ToString() : "";

    private bool MoveAttachment(SlotRef from, SlotRef to)
    {
        if (_attachmentContent is not { } content || !Valid(from) || !Valid(to)) return from.Attachment >= 0 || to.Attachment >= 0;
        ItemStack source = Of(from.Container)[from.Index], target = Of(to.Container)[to.Index];
        int toMod = to.Attachment;
        if (from.Attachment < 0 && toMod < 0 && content.Item(source.Key ?? "")?.Attachment is { } part && content.Item(target.Key ?? "")?.Firearm != null)
            toMod = (int)part.Slot;
        if (from.Attachment < 0 && toMod < 0) return false;
        if (from.Container == to.Container && from.Index == to.Index) return true;
        Container Make(SlotRef slot, ItemStack stack)
        {
            SimKind kind = slot.Container == ContainerId.Wear ? SimKind.Wear :
                slot.Container == ContainerId.Loot && Loot?.Kind is ContainerKind.Crate or ContainerKind.Corpse ? SimKind.Crate : SimKind.Box;
            var c = new Container(kind, 1);
            c.Set(0, ToSimulation(stack, content));
            return c;
        }
        Container a = Make(from, source), b = Make(to, target);
        var result = AttachmentTransfer.Move(content, a, 0, from.Attachment, b, 0, toMod, a[0], b[0]);
        if (result.Ok)
        {
            Of(from.Container)[from.Index] = FromSimulation(a[0], content);
            Of(to.Container)[to.Index] = FromSimulation(b[0], content);
        }
        else Notice?.Invoke(result.Code.ToString());
        return true;
    }

    private static SimStack ToSimulation(ItemStack ui, GameContent content)
    {
        if (ui.IsEmpty || content.Item(ui.Key) is not { } item) return default;
        SimStack stack = SimStack.Of(item, ui.Amount);
        stack.Loaded = (ushort)Math.Max(0, ui.Ammo);
        stack.LoadedAmmo = content.Item(ui.Round ?? "")?.Id ?? 0;
        stack.Sight = content.Item(ui.Sight ?? "")?.Id ?? 0;
        stack.Muzzle = content.Item(ui.Muzzle ?? "")?.Id ?? 0;
        stack.Rail = content.Item(ui.Rail ?? "")?.Id ?? 0;
        return stack;
    }

    private static ItemStack FromSimulation(SimStack stack, GameContent content)
    {
        if (stack.IsEmpty || content.Item(stack.Item) is not { } item) return default;
        return new ItemStack(item.Key, stack.Count, item.HasCondition ? (float)stack.Condition / item.MaxCondition : -1,
            item.Firearm != null ? stack.Loaded : -1)
        {
            Round = content.Item(stack.LoadedAmmo)?.Key, Sight = content.Item(stack.Sight)?.Key,
            Muzzle = content.Item(stack.Muzzle)?.Key, Rail = content.Item(stack.Rail)?.Key,
        };
    }

    private void QuickAttachment(SlotRef slot)
    {
        foreach (ContainerId kind in new[] { ContainerId.Main, ContainerId.Belt })
            for (int i = 0; i < Of(kind).Length; i++)
                if (Of(kind)[i].IsEmpty) { MoveAttachment(slot, new SlotRef(kind, i)); return; }
        Notice?.Invoke("No free slot for the attachment.");
    }
}
