using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

public sealed partial class ShipSystem
{
    // Events need the effective engine switch too: raw network ShipInput never supplies Engines.
    private void ReconcileAttitudeHold(Ship ship)
    {
        ship.SyncFuel();
        ShipInput input = ship.Input.Unattended();
        input.Engines = ship.State.Has(ShipFlags.EnginesOn) && !ship.State.Has(ShipFlags.EngineStopped);
        Player? pilot = Entities.TryFind(ship.Pilot, out var entity) ? PlayerOf(entity) : null;
        ReconcileAttitudeHold(ship, input, pilot);
    }

    private static void ReconcileAttitudeHold(Ship ship, in ShipInput input, Player? pilot)
    {
        if (ship.Autopilot || pilot is { Sleeping: false } || !ShipAttitudeHold.CanHold(ship.State, input))
        {
            ShipAttitudeHold.Clear(ref ship.State);
            return;
        }
        if (ship.State.AttitudeHoldActive && !ShipAttitudeHold.Valid(ship.State.AttitudeHoldRotation))
        {
            ShipAttitudeHold.Clear(ref ship.State);
        }
        // A restored sleeper can retain an existing saved target, but an occupied helm never creates a new one.
        if (!ship.Piloted) ShipAttitudeHold.Capture(ref ship.State);
    }
}
