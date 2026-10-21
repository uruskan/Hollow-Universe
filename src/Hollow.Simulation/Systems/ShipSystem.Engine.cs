using System;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

public sealed partial class ShipSystem
{
    /// <summary>
    /// The crew's explicit engine switch, reached through the shared ship interaction radial. With nobody awake at the
    /// helm, ON starts it by hand (<see cref="Ship.HandStarted"/>): it idles, every control let go (a car's handbrake on, a
    /// rotor's collective down, no thrust asked), and only while the ship is at rest; OFF stops it, latched.
    /// </summary>
    public Outcome SetEngine(Player player, Ship ship, bool on)
    {
        if (!player.CanAct || ship.Destroyed) return OutcomeCode.NotAllowed;
        if (!ship.IsAuthorised(player.Id.Value)) return OutcomeCode.NotCrew;
        ship.SyncFuel();
        if (on && ship.State.Fuel <= 0) return OutcomeCode.NoFuel;
        bool attended = PilotAwake(ship) || ship.Autopilot;
        if (on && !attended && !ShipStill(ship) && ship.Def.NeedsPilot) return OutcomeCode.ShipMoving;
        if (on)
        {
            ship.State.Flags &= ~ShipFlags.EngineStopped;
            ship.State.Flags |= ShipFlags.EnginesOn;
            ship.HandStarted = !attended && ShipStill(ship);
        }
        else
        {
            ship.State.Flags |= ShipFlags.EngineStopped;
            ship.State.Flags &= ~(ShipFlags.EnginesOn | ShipFlags.Boosting);
            ship.State.BoostTicks = 0;
            ship.HandStarted = false;
            ShipAttitudeHold.Clear(ref ship.State);
        }
        WakeShip(ship);
        if (on) ReconcileAttitudeHold(ship);
        ship.DetailsRevision++;
        return Outcome.Done;
    }

    /// <summary>Someone awake sits at a ship's helm.</summary>
    private bool PilotAwake(Ship ship) =>
        ship.Piloted && Entities.TryFind(ship.Pilot, out Entity pilot) && PlayerOf(pilot) is { Sleeping: false };

    /// <summary>
    /// Whether a ship's engines run this tick, and the controls its step gets (callers pass the helm's, or the retained
    /// lever of a ship left in flight): the crew's switch (<see cref="ShipFlags.EngineStopped"/> latches OFF); started by
    /// hand, it idles hands off until switched off or dry; else one that needs a pilot runs while someone awake is at the
    /// helm or the zone flies it, and any other with a pilot aboard, or once its engines ran (they cruise on when the pilot
    /// leaves in flight).
    /// </summary>
    private bool EnginesRun(Ship ship, bool attended, ref ShipInput input)
    {
        if (ship.State.Has(ShipFlags.EngineStopped) || (ship.HandStarted && ship.State.Fuel <= 0))
        {
            ship.HandStarted = false;
        }
        if (ship.State.Has(ShipFlags.EngineStopped))
        {
            return false;
        }
        if (!attended && (ship.HandStarted || ship.Def.NeedsPilot))
        {
            // Hands off: nothing asked of the drive; on wheels, the handbrake (GroundMotor: Vertical over a half).
            input = new ShipInput { Vertical = ship.Def.Ground != null ? 1 : 0 };
        }
        if (ship.HandStarted)
        {
            return true;
        }
        return ship.Def.NeedsPilot ? attended : ship.Piloted || ship.Autopilot || ship.State.Has(ShipFlags.EnginesOn);
    }

    /// <summary>
    /// After its step: an engine started by hand runs on, idling, whatever the step found (a ship sitting on its skids idles
    /// without the flag a flight's thrust sets), and burns its idle when the step burned nothing.
    /// </summary>
    private void IdleByHand(Ship ship)
    {
        if (!ship.HandStarted || ship.Destroyed || ship.InOrbit)
        {
            return;
        }
        if (ship.Events.FuelUsed <= 0)
        {
            float idle = MathF.Min(ship.Fuel.Amount, ship.Def.FuelIdle * CharacterMotor.DeltaTime);
            ship.Fuel.Consume(idle);
            ship.SyncFuel();
        }
        if (ship.State.Fuel <= 0)
        {
            ship.HandStarted = false;
            ship.State.Flags &= ~ShipFlags.EnginesOn;
            return;
        }
        ship.State.Flags |= ShipFlags.EnginesOn;
    }
}
