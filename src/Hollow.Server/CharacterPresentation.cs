using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Server;

internal static class CharacterPresentation
{
    /// <summary>Replicated held art describes accepted simulation actions, including an item consumed by that action.</summary>
    public static void Apply(Zone zone, Player player, Character character, ref CharacterNetState state)
    {
        state.Held = player.IsAwakeAndAlive ? player.HeldItem : (ushort)0;
        state.LookYaw = player.IsAwakeAndAlive ? state.LookYaw : (sbyte)0;
        state.LookPitch = player.IsAwakeAndAlive ? state.LookPitch : (sbyte)0;
        state.ActionCount = (byte)(player.ActionCount & 15);
        state.Action = player.LastAction;
        state.ActionItem = player.LastActionItem;
        state.ActionTick = player.LastActionTick;
        state.Sleeping = player.Sleeping && !player.Dead;
        state.HeldMods = player.CanAct ? zone.Guns().HeldGunLook(player) : (byte)0;
        state.Aiming = player.CanAct && (player.Gun.Item != 0 && !player.Gun.Reloading &&
            character.Actions.Has(ActionButtons.Secondary) || player.DrawStartTick >= 0);
    }
}
