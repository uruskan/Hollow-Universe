using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;

namespace Hollow.Simulation;

public sealed partial class GunSystem
{
    public Outcome MoveAttachment(Entity entity, ContainerRef from, int fromSlot, int fromMod,
        ContainerRef to, int toSlot, int toMod, in ItemStack expectedFrom, in ItemStack expectedTo)
    {
        if (!Actions.Acting(entity, out var player)) return OutcomeCode.Dead;
        Container? source = Actions.ResolveContainer(player, from), target = Actions.ResolveContainer(player, to);
        if (source == null || target == null) return OutcomeCode.NotOpen;
        bool deposits = LootRules.CanDeposit(Actions.LootKindOf(player));
        Outcome result = AttachmentTransfer.Move(Content, source, fromSlot, fromMod, target, toSlot, toMod,
            expectedFrom, expectedTo, !LootRules.IsLoot(from) || deposits, !LootRules.IsLoot(to) || deposits);
        if (result.Ok && (from == ContainerRef.Belt && fromSlot == player.HeldSlot || to == ContainerRef.Belt && toSlot == player.HeldSlot))
            player.GunRevision++;
        return result;
    }
}
