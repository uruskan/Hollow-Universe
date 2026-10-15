namespace Hollow.Client;

/// <summary>
/// The client's modules and their order (GROUND_SYSTEMS.md §12.5, K11), the one place that lists them: the host calls
/// every hook in this order (<see cref="ClientModule.Update"/> each frame, <see cref="ClientModule.Welcome"/> on
/// joining, the rest as they come), and <see cref="ClientModule.Dispose"/> in reverse. The order matters once: the
/// animals' states are read before the arrows are placed, since an arrow may ride an animal's body.
/// </summary>
public static class ClientModules
{
    public static ClientModule[] All() => new ClientModule[]
    {
        new SurvivalModule(),
        new TargetModule(),
        new WorldModule(),
        new BuildingModule(),
        new PlacementModule(),
        new InteractionModule(),
        new RaidModule(),
        new FireModule(),
        new ShipModule(),
        new AnimalModule(),
        new ArrowModule(),
        new ConsoleModule(),
        new DevModule(),
        new DamageModule(),
        new LoadingModule(),
    };
}
