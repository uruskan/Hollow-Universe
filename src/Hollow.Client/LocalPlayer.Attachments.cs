using Hollow.Net.Protocol;
using Hollow.Simulation.Items;

namespace Hollow.Client;

public sealed partial class LocalPlayer
{
    /// <summary>Moves one attachment between ordinary slots and explicit weapon sockets, including open storage.</summary>
    public uint MoveAttachment(ContainerRef from, int fromSlot, int fromMod, ContainerRef to, int toSlot, int toMod)
    {
        if (fromSlot is < 0 or > 255 || toSlot is < 0 or > 255 || fromMod is < -1 or > 2 || toMod is < -1 or > 2 ||
            Resolve(from) is not { } source || Resolve(to) is not { } target || !source.InRange(fromSlot) || !target.InRange(toSlot)) return 0;
        ItemStack expectedFrom = source[fromSlot], expectedTo = target[toSlot];
        return Intend(new Pending(p => p.Resolve(from) is { } a && p.Resolve(to) is { } b &&
                AttachmentTransfer.Move(p.Content, a, fromSlot, fromMod, b, toSlot, toMod, expectedFrom, expectedTo,
                    !LootRules.IsLoot(from) || LootRules.CanDeposit(p.LootKind), !LootRules.IsLoot(to) || LootRules.CanDeposit(p.LootKind)).Ok),
            w => new AttachmentMoveIntent { Seq = _nextSeq, From = from, FromSlot = (byte)fromSlot,
                FromMod = fromMod < 0 ? byte.MaxValue : (byte)fromMod, To = to, ToSlot = (byte)toSlot,
                ToMod = toMod < 0 ? byte.MaxValue : (byte)toMod,
                ExpectedFrom = SlotSnapshot.Of(expectedFrom), ExpectedTo = SlotSnapshot.Of(expectedTo) }.WriteMessage(w));
    }
}
