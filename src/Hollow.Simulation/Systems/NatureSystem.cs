using Hollow.Simulation.Movement;

namespace Hollow.Simulation;

/// <summary>
/// The natural objects' own life (GROUND.md §6.1): once a second, what players cut, mined or picked comes back when its
/// time is up. The objects themselves and their state are the host's (<see cref="Zone.Nature"/>, <see cref="Zone.Natural"/>),
/// since every system that gathers, aims or places looks at them.
/// </summary>
public sealed class NatureSystem : ZoneSystem
{
    public override int Every => CharacterMotor.TickRate;

    protected internal override void Step() => Natural.Respawn(UnixMilliseconds);
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class NatureSystemAccess
{
    public static NatureSystem NatureSys(this Zone zone) => zone.Require<NatureSystem>();
}
