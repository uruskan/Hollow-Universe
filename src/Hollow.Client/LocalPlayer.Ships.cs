using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;

namespace Hollow.Client;

// The pilot's keys in the seat (GROUND.md §6.7): the hatch, stepping out, the lock, descending from orbit. Each returns its
// sequence number (0 if it couldn't be sent) and is answered by IntentAnswered (IntentRejected when refused); what it
// changes arrives with the ship's state. On foot, a ship's hatch, seat, tank, lock and cargo are verbs (Interact).
public sealed partial class LocalPlayer
{
    /// <summary>Opens or shuts the hatch of the ship flown, from its seat.</summary>
    public uint ShipHatch(EntityId ship) => ShipAct(ShipAction.Hatch, ship);

    /// <summary>Steps out of the seat (the ship still: landed, or nearly so near the ground).</summary>
    public uint ExitShip() => ShipAct(ShipAction.Exit, EntityId.None);

    /// <summary>Locks or unlocks one's own ship, from its seat.</summary>
    public uint LockShip(EntityId ship) => ShipAct(ShipAction.Lock, ship);

    /// <summary>From orbit, back down over where the ship left.</summary>
    public uint Descend() => ShipAct(ShipAction.Descend, EntityId.None);

    private uint ShipAct(ShipAction action, EntityId ship) =>
        Intend(null, w => new ShipIntent { Seq = _nextSeq, Ship = ship.Value, Action = action }.WriteMessage(w));
}
