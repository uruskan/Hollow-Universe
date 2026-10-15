using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>One atomic drag between an attachment socket and an inventory/storage slot (or another socket).</summary>
public static class AttachmentTransfer
{
    public const int InventorySlot = -1;

    public static Outcome Move(GameContent content, Container from, int fromSlot, int fromMod,
        Container to, int toSlot, int toMod, in ItemStack expectedFrom, in ItemStack expectedTo,
        bool canDepositFrom = true, bool canDepositTo = true)
    {
        if (!from.InRange(fromSlot) || !to.InRange(toSlot) || fromMod is < -1 or > 2 || toMod is < -1 or > 2 ||
            fromMod < 0 && toMod < 0 || from == to && fromSlot == toSlot)
            return OutcomeCode.Invalid;
        ItemStack source = from[fromSlot], target = to[toSlot];
        if (source != expectedFrom || target != expectedTo) return OutcomeCode.Stale;
        ItemStack part = Part(content, source, fromMod), replaced = Part(content, target, toMod);
        if (part.IsEmpty) return OutcomeCode.Stale;
        if (content.Item(part.Item)?.Attachment == null || !replaced.IsEmpty && content.Item(replaced.Item)?.Attachment == null)
            return OutcomeCode.NotUsable;
        if (!canDepositTo || !replaced.IsEmpty && !canDepositFrom) return OutcomeCode.NotAllowed;
        // A socket holds exactly one part. A swap must have somewhere to put the old part before anything changes.
        if (toMod < 0 && !target.IsEmpty && target.Count != 1 || fromMod < 0 && source.Count > 1 && !replaced.IsEmpty)
            return OutcomeCode.NoSpace;
        if (!Takes(content, to, toSlot, target, toMod, part) ||
            !replaced.IsEmpty && !Takes(content, from, fromSlot, source, fromMod, replaced))
            return OutcomeCode.NotAllowed;

        ItemStack nextSource = source, nextTarget = target;
        if (fromMod >= 0) nextSource.SetMod((AttachmentSlot)fromMod, replaced.Item);
        else nextSource = replaced.IsEmpty ? source.WithCount(source.Count - 1) : replaced;
        if (toMod >= 0) nextTarget.SetMod((AttachmentSlot)toMod, part.Item);
        else nextTarget = part;
        from.Set(fromSlot, nextSource);
        to.Set(toSlot, nextTarget);
        return OutcomeCode.Done;
    }

    private static ItemStack Part(GameContent content, in ItemStack stack, int mod)
    {
        if (mod < 0) return stack.IsEmpty ? default : stack.WithCount(1);
        if (content.Item(stack.Item)?.Firearm is not { } gun || stack.Count != 1) return default;
        ushort id = stack.Mod((AttachmentSlot)mod);
        return content.Item(id) is { Attachment: { } attachment } part && (int)attachment.Slot == mod && gun.Takes(part)
            ? ItemStack.Of(part, 1) : default;
    }

    private static bool Takes(GameContent content, Container container, int index, in ItemStack holder, int mod, in ItemStack part)
    {
        if (mod < 0) return container.Accepts(content, index, part);
        return holder.Count == 1 && content.Item(holder.Item)?.Firearm is { } gun &&
            content.Item(part.Item) is { Attachment: { } attachment } item && (int)attachment.Slot == mod && gun.Takes(item);
    }
}
