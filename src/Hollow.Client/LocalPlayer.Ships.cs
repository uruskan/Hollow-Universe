using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;

namespace Hollow.Client;

// The ship intents (GROUND.md §6.7): the hatch, boarding, stepping out, the lock, refuelling, descending from orbit.
// Each returns its sequence number (0 if it couldn't be sent) and is answered by IntentAnswered (IntentRejected when
// refused); what it changes arrives with the ship's state (the seat, the hatch) and the player's own (the character, the
// inventory's lamp oil). The cargo opens with Open on the ship's ID, like a box.
public sealed partial class LocalPlayer
{
    /// <summary>Opens or shuts a ship's hatch (beside it, or from the seat).</summary>
    public uint ShipHatch(EntityId ship) => ShipAct(ShipAction.Hatch, ship);

    /// <summary>Climbs into a ship's seat through its open hatch.</summary>
    public uint Board(EntityId ship) => ShipAct(ShipAction.Board, ship);

    /// <summary>Steps out of the seat (the ship still: landed, or nearly so near the ground).</summary>
    public uint ExitShip() => ShipAct(ShipAction.Exit, EntityId.None);

    /// <summary>Locks or unlocks one's own ship.</summary>
    public uint LockShip(EntityId ship) => ShipAct(ShipAction.Lock, ship);

    /// <summary>Fills a ship's tank with the inventory's lamp oil.</summary>
    public uint RefuelShip(EntityId ship) => ShipAct(ShipAction.Refuel, ship);

    /// <summary>From orbit, back down over where the ship left.</summary>
    public uint Descend() => ShipAct(ShipAction.Descend, EntityId.None);

    private uint ShipAct(ShipAction action, EntityId ship) =>
        Intend(null, w => new ShipIntent { Seq = _nextSeq, Ship = ship.Value, Action = action }.WriteMessage(w));
}
