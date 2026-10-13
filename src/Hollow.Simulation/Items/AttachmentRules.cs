using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>
/// Putting attachments on firearms and taking them off (GROUND.md §6.3): the server applies these to its containers and
/// the client to its copy for an optimistic update, so both agree. Each either does all it says or nothing, with why.
/// </summary>
public static class AttachmentRules
{
    /// <summary>
    /// An attachment from <paramref name="source"/>'s slot onto the firearm in <paramref name="holder"/>'s slot, into the
    /// attachment's own slot on the gun; what was there goes back where the attachment came from. The gun must take it.
    /// </summary>
    public static Outcome Attach(GameContent content, Container source, int slot, Container holder, int weaponSlot, ushort expected)
    {
        if (!source.InRange(slot) || !holder.InRange(weaponSlot) || (source == holder && slot == weaponSlot))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack part = source[slot];
        if (part.IsEmpty || (expected != 0 && part.Item != expected))
        {
            return OutcomeCode.Stale;
        }
        ItemStack weapon = holder[weaponSlot];
        if (content.Item(part.Item)?.Attachment is not { } attachment || content.Item(weapon.Item)?.Firearm is not { } gun)
        {
            return OutcomeCode.NotUsable;
        }
        if (!gun.Takes(attachment.Item))
        {
            return OutcomeCode.NotAllowed;
        }
        ushort old = weapon.Mod(attachment.Slot);
        weapon.SetMod(attachment.Slot, part.Item);
        part.Count--;
        source.Set(slot, part);
        holder.Set(weaponSlot, weapon);
        if (old != 0 && content.Item(old) is { } previous)
        {
            source.Set(slot, ItemStack.Of(previous, 1));
        }
        return OutcomeCode.Done;
    }

    /// <summary>The attachment in a slot of the firearm in <paramref name="holder"/>'s slot, off into the inventory (refused when full).</summary>
    public static Outcome Detach(GameContent content, PlayerInventory inventory, Container holder, int weaponSlot, AttachmentSlot mod)
    {
        if (!holder.InRange(weaponSlot) || mod > AttachmentSlot.Rail)
        {
            return OutcomeCode.Invalid;
        }
        ItemStack weapon = holder[weaponSlot];
        ushort id = weapon.Mod(mod);
        if (content.Item(weapon.Item)?.Firearm == null || id == 0 || content.Item(id) is not { } part)
        {
            return OutcomeCode.Stale;
        }
        if (!inventory.Fits(content, ItemStack.Of(part, 1)))
        {
            return OutcomeCode.NoSpace;
        }
        weapon.SetMod(mod, 0);
        holder.Set(weaponSlot, weapon);
        inventory.Give(content, ItemStack.Of(part, 1));
        return OutcomeCode.Done;
    }
}
