namespace Hollow.Server;

/// <summary>
/// The server's modules and their order (GROUND_SYSTEMS.md §12.5, K10), the one place that lists them. The host calls
/// each hook on every module in this order, so the order is what a client receives in a tick: after the step, the
/// ships' notices, the raid alerts, the world's messages, then the player's own state and the answers; in a snapshot,
/// the things and natural objects, the building, the explosives and fires, then the arrows. The things come back from the
/// database before the building, the building before the ships, and the explosives after all they can be stuck to.
/// </summary>
public static class ServerModules
{
    public static ServerModule[] All() => new ServerModule[]
    {
        new AttackModule(),
        new ThingModule(),
        new BuildingModule(),
        new PlacementModule(),
        new WiringModule(),
        new ShipModule(),
        new RaidModule(),
        new FireModule(),
        new WorldModule(),
        new AnimalModule(),
        new ArrowModule(),
        new DamageModule(),
        new SurvivalModule(),
        new GunModule(),
        new ConsoleModule(),
        new DevModule(),
    };
}
