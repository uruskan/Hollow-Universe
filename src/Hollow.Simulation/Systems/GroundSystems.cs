namespace Hollow.Simulation;

/// <summary>
/// The zone's systems and their order (GROUND_SYSTEMS.md §12.2, K3), the one place that lists them. The host runs them
/// in this order after the characters' movement (those that move bodies under the physics gate first, GROUND.md §4.2);
/// those without a step only answer. A later kind of zone (space) is the same host with another list.
/// </summary>
public static class GroundSystems
{
    /// <summary>The survival game's systems (G2 on), for a zone with game content.</summary>
    public static ZoneSystem[] Game() => new ZoneSystem[]
    {
        // The foundations (GROUND_SYSTEMS.md §3): they answer, they don't step.
        new TargetSystem(),
        new DamageSystem(),
        new HeldSystem(),
        new SupportSystem(),
        new InteractionSystem(),
        new PlacementSystem(),
        new SpillSystem(),
        new BlastSystem(),
        // Under the physics gate, after the characters moved.
        new ShipSystem(),
        // GROUND.md §4.2's order: actions, projectiles and damage, survival, devices, AI, decay and upkeep.
        new PlayerSystem(),
        new CombatSystem(),
        new GunSystem(),
        new ExplosiveSystem(),
        new FireSystem(),
        new WoundedSystem(),
        new AnimalSystem(),
        new VitalsSystem(),
        new NatureSystem(),
        new ThingSystem(),
        new ArrowSystem(),
        new WorldSystem(),
        new ActionSystem(),
        new BuildingSystem(),
        new HistorySystem(),
    };

    /// <summary>
    /// G1's movement-only zone (the physics checks, the network tests, the ships' flight checks): the dead come back by
    /// themselves, and ships fly, crash and break without the game's rules around them (the damage pipeline reports
    /// what breaks, GROUND.md §6.4.2).
    /// </summary>
    public static ZoneSystem[] MovementOnly() => new ZoneSystem[] { new DamageSystem(), new ShipSystem(), new AutoRespawnSystem() };
}
