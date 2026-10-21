using System;
using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;

namespace Hollow.Client;

// A seated player's keys (GROUND.md §6.7, IRON_WAKE.md §5.2): a door from a seat that works them, leaving the seat, the
// lock, descending from orbit. Each returns its sequence number (0 if it couldn't be sent) and is answered by
// IntentAnswered (IntentRejected when refused); what it changes arrives with the ship's state and details. On foot, a
// ship's doors, seats, tank, list and cargo are verbs (Interact).
public sealed partial class LocalPlayer
{
    /// <summary>Opens or shuts a door of the ship one sits in (its index in the kind's doors), from a seat that works them.</summary>
    public uint ShipDoor(EntityId ship, int door) => ShipAct(ShipAction.Door, ship, door);

    /// <summary>Leaves the seat, to its first clear exit (a ship one only sits in must be at rest).</summary>
    public uint LeaveSeat() => ShipAct(ShipAction.Leave, EntityId.None);

    /// <summary>Locks or unlocks the ship one sits in (its crew).</summary>
    public uint LockShip(EntityId ship) => ShipAct(ShipAction.Lock, ship);

    /// <summary>From orbit, back down over where the ship left (the helm).</summary>
    public uint Descend() => ShipAct(ShipAction.Descend, EntityId.None);

    /// <summary>A gunner's Tab (IW3): their station's magazine opens as loot beside the inventory.</summary>
    public uint OpenMagazine(EntityId ship) => ShipAct(ShipAction.Magazine, ship);

    /// <summary>
    /// At the vehicle workbench (VEHICLES.md §9.11): fits the module in an inventory slot into a socket and level of the vehicle
    /// in its bay. Not predicted: the vehicle's new build arrives with its details.
    /// </summary>
    public uint FitModule(EntityId station, EntityId ship, int socket, int level, ContainerRef from, int slot) =>
        Intend(null, w => new ModuleIntent
        {
            Seq = _nextSeq, Station = station.Value, Ship = ship.Value, Fit = true, Socket = (byte)Math.Clamp(socket, 0, 255), Level = (byte)Math.Clamp(level, 0, 3),
            From = from, Slot = (byte)Math.Clamp(slot, 0, 255),
        }.WriteMessage(w));

    /// <summary>At the vehicle workbench: takes the module at a socket and level of the vehicle in its bay out into the inventory.</summary>
    public uint TakeModule(EntityId station, EntityId ship, int socket, int level) =>
        Intend(null, w => new ModuleIntent
        {
            Seq = _nextSeq, Station = station.Value, Ship = ship.Value, Socket = (byte)Math.Clamp(socket, 0, 255), Level = (byte)Math.Clamp(level, 0, 3),
        }.WriteMessage(w));

    private uint ShipAct(ShipAction action, EntityId ship, int arg = 0) =>
        Intend(null, w => new ShipIntent { Seq = _nextSeq, Ship = ship.Value, Action = action, Arg = (byte)Math.Clamp(arg, 0, 15) }.WriteMessage(w));
}
