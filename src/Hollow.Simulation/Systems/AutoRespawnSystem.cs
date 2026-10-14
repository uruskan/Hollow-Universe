using System;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation;

/// <summary>
/// G1's respawn, for a zone without game content (the movement checks, the network tests): a dead character comes back
/// at the spawn point after <see cref="Zone.RespawnDelayTicks"/>. With content, PlayerSystem's respawn of the player's
/// choice replaces it (GROUND.md §6).
/// </summary>
public sealed class AutoRespawnSystem : ZoneSystem
{
    protected internal override void Step()
    {
        ComponentPool<Character> characters = Entities.Pool<Character>();
        Span<Character> values = characters.Values;
        ReadOnlySpan<int> slots = characters.Slots;
        for (int i = 0; i < values.Length; i++)
        {
            ref Character character = ref values[i];
            if (!character.State.IsDead)
            {
                continue;
            }
            if (character.RespawnTicks <= 0)
            {
                character.RespawnTicks = Zone.RespawnDelayTicks;
            }
            else if (--character.RespawnTicks == 0)
            {
                character.State = NewCharacter(SpawnPoint(Entities.IdOf(Entities.AtSlot(slots[i])).Value + (ulong)Tick));
            }
        }
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class AutoRespawnSystemAccess
{
    public static AutoRespawnSystem AutoRespawn(this Zone zone) => zone.Require<AutoRespawnSystem>();
}
