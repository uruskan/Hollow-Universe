using System;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

// Doors (IRON_WAKE.md §5.3): a door is a part with a progress, 0 shut to 1 open, moved over its seconds by the ship's rules
// and drawn at it. Its motion is a record (where it was at a tick, which way it goes), so the server and every client know
// its progress at any tick alike, and its parts' states follow from it (the space and the view pose them). It opens as its
// rule allows (always; the ship at rest; the ship slow and low), to anyone the ship's lock lets through, on foot by its
// verb or from a seat that works the doors; it shuts any time. It never moves into someone: asked to, it refuses (RAMP
// BLOCKED); someone stepping into its way stops it until they are clear. Broken, it jams open until mended (§5.5).
public sealed partial class ShipSystem
{
    /// <summary>Whether a ship's door may open now, by its rule: always; the ship at rest; at rest, or slow and low.</summary>
    public Outcome MayOpen(Ship ship, int door) => ship.Def.Doors[door].Rule(ShipStill(ship), ShipSlow(ship));

    /// <summary>A door broken (its health gone): jammed open until the hammer mends it.</summary>
    public bool DoorJammed(Ship ship, int door) => ship.Def.Doors[door].Health != null && ship.Doors[door].Health <= 0;

    /// <summary>A door open at least half way, and opening or open: a seat reached through it can be got at (the Tin Finch's hatch).</summary>
    public bool DoorOpenEnough(Ship ship, int door) => ship.Doors[door].LetsIn(ship.DoorAt(door, Tick));

    /// <summary>
    /// Opens or shuts a ship's door (its verb on foot, or a seated key, IRON_WAKE.md §5.3): a jammed one stays open; it opens
    /// as its rule allows, to anyone the ship's lock lets through; its whole way must be clear of people (RAMP BLOCKED).
    /// From the seat a door that wants the ship at rest says "land first".
    /// </summary>
    internal Outcome SetDoor(Player p, Ship ship, int door, bool open, bool fromSeat = false)
    {
        if (door < 0 || door >= ship.Doors.Length || ship.Destroyed || ship.InOrbit)
        {
            return OutcomeCode.NotFound;
        }
        if (DoorJammed(ship, door))
        {
            return OutcomeCode.Jammed;
        }
        if (ship.Doors[door].Opening == open)
        {
            return Outcome.Done;
        }
        if (open)
        {
            if (ship.Locked && !ship.IsAuthorised(p.Id.Value))
            {
                return OutcomeCode.Locked;
            }
            Outcome may = MayOpen(ship, door);
            if (!may.Ok)
            {
                return fromSeat && may == OutcomeCode.ShipMoving ? OutcomeCode.NotLanded : may;
            }
        }
        if (DoorWouldMeet(ship, door, open ? 1f : 0f))
        {
            return OutcomeCode.RampBlocked;
        }
        MoveDoor(ship, door, open);
        return Outcome.Done;
    }

    /// <summary>The door verb's own: opens or shuts a door by the rules (<see cref="SetDoor"/>), the player on foot.</summary>
    internal Outcome SetDoorOnFoot(Player p, Ship ship, int door, bool open) => SetDoor(p, ship, door, open);

    /// <summary>
    /// Puts a door at a progress at once, still (a ship given open, IRON_WAKE.md §5.6; tests): its parts follow, and a parked
    /// ship's body is made again for them.
    /// </summary>
    public void SetDoorAtOnce(Ship ship, int door, float progress)
    {
        ship.Doors[door].From = Math.Clamp(progress, 0f, 1f);
        ship.Doors[door].Moving = 0;
        ship.Doors[door].Waiting = 0;
        ship.Doors[door].Since = Tick;
        ship.DetailsRevision++;
        DoorParts(ship, Tick, ship.Parts);
        PoseSpaceAndPark(ship);
    }

    // Sets a door going toward open or shut from where it is this tick (the rules', unchecked: a seat's door behind its
    // occupant, a broken door falling open).
    private void MoveDoor(Ship ship, int door, bool open)
    {
        ref ShipDoorState d = ref ship.Doors[door];
        float now = ship.DoorAt(door, Tick);
        d.From = now;
        d.Since = Tick;
        d.Waiting = 0;
        d.Moving = (sbyte)(now == (open ? 1f : 0f) ? 0 : open ? 1 : -1);
        if (d.Moving == 0 && open)
        {
            d.From = 1;
        }
        ship.DetailsRevision++;
    }

    /// <summary>
    /// Every door's parts' states at a tick, written into <paramref name="parts"/> (each door's parts follow its progress by
    /// its ease); the other parts are left as they are. The server poses its space from them, a client its own.
    /// </summary>
    public static void DoorParts(Ship ship, long tick, float[] parts) => DoorParts(ship.Def, ship.Doors, tick, parts);

    /// <summary>The same from a ship's kind and its doors' records (a client's copy of them).</summary>
    public static void DoorParts(ShipDef def, ReadOnlySpan<ShipDoorState> doors, long tick, Span<float> parts)
    {
        for (int i = 0; i < def.Doors.Count && i < doors.Length; i++)
        {
            ShipDoorDef door = def.Doors[i];
            float state = door.PartState(doors[i].ProgressAt(tick, door.Rate));
            foreach (int part in door.Parts)
            {
                if (part < parts.Length)
                {
                    parts[part] = state;
                }
            }
        }
    }

    // Each tick, after the ship stepped: every moving door's parts go to its progress at this tick (the space follows),
    // unless someone is now in their way, which stops it there until they are clear; a door that reached its end stops,
    // and a parked ship's body is made again for its parts' new states.
    private void StepDoors(Ship ship)
    {
        bool moved = false, ended = false;
        for (int i = 0; i < ship.Doors.Length; i++)
        {
            ref ShipDoorState d = ref ship.Doors[i];
            if (d.Moving == 0 && d.Waiting == 0)
            {
                continue;
            }
            ShipDoorDef def = ship.Def.Doors[i];
            if (d.Waiting != 0)
            {
                // Stopped for someone: on again once the next step of its way is clear.
                float next = Math.Clamp(d.From + d.Waiting * def.Rate, 0f, 1f);
                if (StepWouldMeet(ship, def, next))
                {
                    continue;
                }
                d.Moving = d.Waiting;
                d.Waiting = 0;
                d.Since = Tick - 1;
                ship.DetailsRevision++;
            }
            float at = d.ProgressAt(Tick, def.Rate);
            if (StepWouldMeet(ship, def, at))
            {
                d.From = d.ProgressAt(Tick - 1, def.Rate);
                d.Waiting = d.Moving;
                d.Moving = 0;
                d.Since = Tick - 1;
                ship.DetailsRevision++;
                continue;
            }
            moved = true;
            if (at is <= 0f or >= 1f)
            {
                d.From = at;
                d.Moving = 0;
                d.Since = Tick;
                ended = true;
                ship.DetailsRevision++;
            }
        }
        if (!moved)
        {
            return;
        }
        if (ship.Space != null)
        {
            // (Clients work the door's parts out from its motion: no parts' message for them.)
            DoorParts(ship, Tick, ship.Parts);
            PoseSpaceAndPark(ship, park: ended);
        }
    }

    // Poses a ship's space at its parts' states, and (asked) makes a parked ship's body again for them.
    private void PoseSpaceAndPark(Ship ship, bool park = true)
    {
        if (ship.Space is not { } space)
        {
            return;
        }
        space.Pose(ship.Parts);
        if (park && ship.ParkedBody != 0)
        {
            lock (JoltRuntime.Gate)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
                Park(ship);
            }
        }
    }

    // Whether a door's parts, moved from where they are to its state at a progress, would pass through anyone (aboard or
    // ashore within reach): a ship without a space has none to meet.
    private bool StepWouldMeet(Ship ship, ShipDoorDef door, float progress)
    {
        if (ship.Space is not { } space || door.Parts.Count == 0)
        {
            return false;
        }
        Span<float> to = stackalloc float[ship.Parts.Length];
        ship.Parts.CopyTo(to);
        float state = door.PartState(progress);
        bool changes = false;
        foreach (int part in door.Parts)
        {
            changes |= to[part] != state;
            to[part] = state;
        }
        if (!changes)
        {
            return false;
        }
        lock (JoltRuntime.Gate)
        {
            return space.Sweeps(to, CapsulesNear(ship));
        }
    }

    // Whether a door's whole way from where it is to a progress is clear of people.
    private bool DoorWouldMeet(Ship ship, int door, float progress) => StepWouldMeet(ship, ship.Def.Doors[door], progress);
}
