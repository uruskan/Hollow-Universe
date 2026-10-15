using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

public sealed partial class ShipSystem
{
    /// <summary>The crew's explicit engine switch, reached through the shared ship interaction radial.</summary>
    public Outcome SetEngine(Player player, Ship ship, bool on)
    {
        if (!player.CanAct || ship.Destroyed) return OutcomeCode.NotAllowed;
        if (!ship.IsAuthorised(player.Id.Value)) return OutcomeCode.NotCrew;
        ship.SyncFuel();
        if (on && ship.State.Fuel <= 0) return OutcomeCode.NoFuel;
        if (on)
        {
            ship.State.Flags &= ~ShipFlags.EngineStopped;
            ship.State.Flags |= ShipFlags.EnginesOn;
        }
        else
        {
            ship.State.Flags |= ShipFlags.EngineStopped;
            ship.State.Flags &= ~(ShipFlags.EnginesOn | ShipFlags.Boosting);
            ship.State.BoostTicks = 0;
        }
        WakeShip(ship);
        ship.DetailsRevision++;
        return Outcome.Done;
    }
}
