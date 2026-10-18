using System;
using System.Numerics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

/// <summary>Why a seat's occupant leaves it (IRON_WAKE.md §5.2): every way out goes through <c>ShipSystem.Release</c>.</summary>
public enum SeatRelease : byte
{
    /// <summary>They died (a kill, a wound bled out, a wreck): the seat is free, their body falls where the seat was.</summary>
    Died,

    /// <summary>Something put them elsewhere (an admin's teleport, a wreck, the ship gone): free, placed by whoever moved them.</summary>
    Moved,

    /// <summary>Their connection was lost (not a log-off): they stand up at the first clear exit, and sleep there.</summary>
    Disconnected,

    /// <summary>Their station was disabled (its health ran out, IRON_WAKE.md §5.5): they stand up at the first clear exit.</summary>
    Disabled,
}

// Seats (IRON_WAKE.md §5.2): who sits where, one rule for every seat of every ship. A seated character is aboard at its
// seat: in a ship people walk in, in the ship's frame at the seat's socket (a gunner's turns with the turret's yaw); in one
// they only sit in, at the seat carried in the zone's frame. Sitting is the seat capability's verb (gates: free, in reach,
// the seat's sitters, not disabled; through its door, open, the ship at rest); standing up is the occupant's key (Space),
// to the first clear exit. Death, a lost connection, a teleport, a wreck and a disabled station free a seat through
// Release; logging off keeps it, asleep.
public sealed partial class ShipSystem
{
    // Subscriptions: a death frees the seat at once (not on the ship's next step).
    private void InitSeats() => Events.On<Died>((in Died e) =>
    {
        if (Entities.TryFind(e.Player, out Entity dead) && Entities.Has<ShipSeat>(dead))
        {
            Release(dead, SeatRelease.Died);
        }
    });

    /// <summary>The ship a character sits in, or null.</summary>
    public Ship? SeatOf(Entity character) => Entities.Has<ShipSeat>(character) ? Entities.Get<ShipSeat>(character).Ship : null;

    /// <summary>Whether a character sits in an exposed seat (a gunner's, IRON_WAKE.md IW4): in the hit history and blasts' reach.</summary>
    public bool SeatExposed(Entity character) => TrySeat(character, out _, out ShipSeatDef seat) && seat.Exposed;

    /// <summary>The seat a character sits in (its index in its ship's seats), or −1.</summary>
    public int SeatIndexOf(Entity character) => Entities.Has<ShipSeat>(character) ? Entities.Get<ShipSeat>(character).Seat : -1;

    /// <summary>The seat of a character, with its ship, or false.</summary>
    public bool TrySeat(Entity character, out Ship ship, out ShipSeatDef seat)
    {
        ship = null!;
        seat = null!;
        if (!Entities.Has<ShipSeat>(character))
        {
            return false;
        }
        ShipSeat s = Entities.Get<ShipSeat>(character);
        ship = s.Ship;
        seat = s.Ship.Def.Seats[s.Seat];
        return true;
    }

    /// <summary>
    /// Whether a player may sit in a seat now, its sitters and its state (not who is in it, nor reach): a crew's seat wants
    /// them on the ship's list; an "unlocked" one the ship unlocked or them listed; a station's its part not disabled; one
    /// reached through a door that door open, and the ship at rest. The seat capability offers the same.
    /// </summary>
    public Outcome MaySit(Player p, Ship ship, int seat)
    {
        ShipSeatDef def = ship.Def.Seats[seat];
        if (!ship.Occupants[seat].IsNone)
        {
            return OutcomeCode.SeatTaken;
        }
        if (def.Sitters == SeatSitters.Crew && !ship.IsAuthorised(p.Id.Value))
        {
            return OutcomeCode.NotCrew;
        }
        if (def.Sitters == SeatSitters.Unlocked && ship.Locked && !ship.IsAuthorised(p.Id.Value))
        {
            return OutcomeCode.Locked;
        }
        if (SeatDisabled(ship, seat))
        {
            return OutcomeCode.Disabled;
        }
        if (def.Door >= 0)
        {
            if (!DoorOpenEnough(ship, def.Door))
            {
                return OutcomeCode.HatchShut;
            }
            if (!ShipStill(ship))
            {
                return OutcomeCode.ShipMoving;
            }
        }
        return Outcome.Done;
    }

    /// <summary>
    /// A player sits in a seat (the seat capability's verb, after its gates, reach and sight): <see cref="MaySit"/>, then
    /// into it; a seat reached through a door shuts it behind them (the Tin Finch's hatch).
    /// </summary>
    internal Outcome Sit(Entity entity, Player p, Ship ship, int seat)
    {
        if (seat < 0 || seat >= ship.Occupants.Length || ship.Destroyed || ship.InOrbit)
        {
            return OutcomeCode.NotFound;
        }
        if (SeatOf(entity) != null)
        {
            return OutcomeCode.NotAllowed;
        }
        Outcome may = MaySit(p, ship, seat);
        if (!may.Ok)
        {
            return may;
        }
        Seat(entity, p, ship, seat);
        if (ship.Def.Seats[seat].Door is >= 0 and var door)
        {
            MoveDoor(ship, door, open: false);
        }
        return Outcome.Done;
    }

    // Puts a character in a seat (sitting, or a sleeper restored where it was): the seat its own, nothing looted, the
    // character carried there, the ship woken.
    private void Seat(Entity entity, Player p, Ship ship, int seat)
    {
        ship.Occupants[seat] = p.Id;
        if (Entities.Has<ShipSeat>(entity))
        {
            Entities.Get<ShipSeat>(entity) = new ShipSeat { Ship = ship, Seat = seat };
        }
        else
        {
            Entities.Add(entity, new ShipSeat { Ship = ship, Seat = seat });
        }
        p.Looting = EntityId.None;
        p.LootRevision++;
        p.StrikeTick = -1;
        p.DrawStartTick = -1;
        if (ship.Def.Seats[seat].Flies)
        {
            ship.Input = ship.Input.Unattended();
            // Restore seats sleepers before their reconnect; preserve their saved target until they actually wake.
            if (!p.Sleeping) ShipAttitudeHold.Clear(ref ship.State);
        }
        ship.DetailsRevision++;
        Carry(entity, ship, seat);
        WakeShip(ship);
    }

    /// <summary>
    /// Puts a character restored from storage back in the seat it was in (the server, at start; a version 1 record's pilot
    /// in its ship's helm).
    /// </summary>
    public bool Reseat(Entity entity, Ship ship, int seat)
    {
        if (PlayerOf(entity) is not { Dead: false } p || ship.Destroyed || seat < 0 || seat >= ship.Occupants.Length ||
            (!ship.Occupants[seat].IsNone && ship.Occupants[seat] != p.Id) || SeatOf(entity) is { } other && other != ship)
        {
            return false;
        }
        ship.Occupants[seat] = EntityId.None;
        Seat(entity, p, ship, seat);
        return true;
    }

    /// <summary>
    /// The occupant's own way out (Space): to the first clear exit of their seat, in the ship's frame aboard a ship people
    /// walk in (a gunner onto the walkway, in flight too), or on the ground beside one they only sit in, which must be at
    /// rest (its door opens to let them out). None clear: EXIT BLOCKED, and they stay.
    /// </summary>
    public Outcome LeaveSeat(Entity entity)
    {
        if (PlayerOf(entity) is not { Dead: false } || !TrySeat(entity, out Ship ship, out ShipSeatDef seat))
        {
            return OutcomeCode.Invalid;
        }
        if (ship.InOrbit)
        {
            return OutcomeCode.NotLanded;
        }
        if (!ExitPoint(ship, seat, out Vector3 at))
        {
            return OutcomeCode.ExitBlocked;
        }
        Stand(entity, ship, seat.Index, at);
        if (seat.Door >= 0)
        {
            MoveDoor(ship, seat.Door, open: true);
        }
        return Outcome.Done;
    }

    /// <summary>
    /// Frees a character's seat, whatever the cause (IRON_WAKE.md §5.2): the seat empties at once and the character is no
    /// longer seated. A death leaves the body where the seat carried it, in the zone's frame; a lost connection and a
    /// disabled station stand them up at the first clear exit (none clear: a lost connection's sleeper stays in the seat, a
    /// disabled station's gunner stays put with it EXIT BLOCKED); a move leaves them where the seat was, for whoever moves
    /// them. False when they weren't seated (or stay seated).
    /// </summary>
    public bool Release(Entity entity, SeatRelease why)
    {
        if (!TrySeat(entity, out Ship ship, out ShipSeatDef seat))
        {
            return false;
        }
        switch (why)
        {
            case SeatRelease.Disconnected:
            case SeatRelease.Disabled:
                if (ship.InOrbit || !ExitPoint(ship, seat, out Vector3 at))
                {
                    return false;
                }
                Stand(entity, ship, seat.Index, at);
                return true;
            default:
                Unseat(entity, ship, seat.Index);
                ref Character c = ref Entities.Get<Character>(entity);
                // In the zone's frame where the seat carried them (a body falls from there; a move places them).
                Zone.ToZoneFrame(ref c.State);
                c.State.Stance = Stance.Standing;
                return true;
        }
    }

    // Empties a seat of a character: the seat, the component, the helm's input.
    private void Unseat(Entity entity, Ship ship, int seat)
    {
        if (seat >= 0 && seat < ship.Occupants.Length && ship.Occupants[seat] == Entities.IdOf(entity))
        {
            ship.Occupants[seat] = EntityId.None;
            if (ship.Def.Seats[seat].Flies)
            {
                ship.Input = ship.Input.Unattended();
                ReconcileAttitudeHold(ship);
            }
        }
        if (Entities.Has<ShipSeat>(entity))
        {
            Entities.Remove<ShipSeat>(entity);
        }
        ship.DetailsRevision++;
        WakeShip(ship);
    }

    // Stands a character up from its seat at a point: of the ship's frame aboard one people walk in (facing the seat's way),
    // else of the zone's (facing the ship's heading).
    private void Stand(Entity entity, Ship ship, int seat, Vector3 at)
    {
        Unseat(entity, ship, seat);
        ref Character c = ref Entities.Get<Character>(entity);
        c.State.Position = at;
        c.State.Velocity = Vector3.Zero;
        c.State.Stance = Stance.Standing;
        c.State.Flags = MoveFlags.OnGround;
        if (ship.Frame != null)
        {
            c.State.Frame = ship.Frame.Key;
            Vector3 facing = ShipSeats.Forward(ship.Def, seat, ship.Parts);
            c.Input.View = facing;
            c.Input.Frame = ship.Frame.Key;
        }
        else
        {
            Vector3 up = Frame.UpAt(at);
            c.State.Frame = 0;
            if (!ShipStill(ship))
            {
                // Leaving an exposed aircraft seat inherits the velocity at its exit socket; gravity takes over.
                Vector3 spin = Vector3.Transform(ship.State.AngularVelocity, ship.State.Rotation);
                c.State.Velocity = ship.State.Velocity + Vector3.Cross(spin, at - ship.State.Position);
                c.State.Flags = MoveFlags.None;
            }
            c.Input.View = Facing.View(ShipMotor.YawOf(ship.State.Rotation, up), 0, up);
            c.Input.Frame = 0;
            Physics.EnsureGround(at);
        }
    }

    // Every tick: each seat's occupant still there, alive and seated in it, else the seat is free (a character gone from
    // the zone, or left some way that didn't release it).
    private void CheckSeats(Ship ship)
    {
        for (int seat = 0; seat < ship.Occupants.Length; seat++)
        {
            EntityId id = ship.Occupants[seat];
            if (id.IsNone)
            {
                continue;
            }
            if (!Entities.TryFind(id, out Entity occupant))
            {
                ship.Occupants[seat] = EntityId.None;
                ship.DetailsRevision++;
                if (ship.Def.Seats[seat].Flies)
                {
                    ship.Input = ship.Input.Unattended();
                    ReconcileAttitudeHold(ship);
                }
                continue;
            }
            bool here = Entities.Has<ShipSeat>(occupant) && Entities.Get<ShipSeat>(occupant) is { } s && s.Ship == ship && s.Seat == seat;
            if (!here || PlayerOf(occupant) is { Dead: true })
            {
                if (here)
                {
                    Release(occupant, SeatRelease.Died);
                }
                else
                {
                    ship.Occupants[seat] = EntityId.None;
                    ship.DetailsRevision++;
                    if (ship.Def.Seats[seat].Flies)
                    {
                        ship.Input = ship.Input.Unattended();
                        ReconcileAttitudeHold(ship);
                    }
                }
            }
        }
    }

    // Every tick, after the ship stepped and its doors moved: each occupant goes where their seat is (IRON_WAKE.md §4.2).
    private void CarrySeated(Ship ship)
    {
        for (int seat = 0; seat < ship.Occupants.Length; seat++)
        {
            if (!ship.Occupants[seat].IsNone && Entities.TryFind(ship.Occupants[seat], out Entity occupant))
            {
                Carry(occupant, ship, seat);
            }
        }
    }

    // The seat rule (ShipSeats.Pose, the client's prediction runs it too): aboard a ship people walk in, the seat's socket
    // in its frame at its parts' states, still; in one they only sit in, the seat carried in the zone's frame.
    private void Carry(Entity entity, Ship ship, int seat)
    {
        ref Character c = ref Entities.Get<Character>(entity);
        (bool aboard, Vector3 position, Vector3 velocity) = ShipSeats.Pose(ship.Def, seat, ship.State, ship.Parts);
        c.State.Frame = aboard && ship.Frame != null ? ship.Frame.Key : 0;
        c.State.Position = position;
        c.State.Velocity = velocity;
        c.State.Stance = Stance.Seated;
        c.State.Flags = MoveFlags.None;
    }

    // The first exit of a seat whose spot is clear: aboard a ship people walk in, a standing capsule on a floor in its space
    // (in the ship's frame, at its parts' states); on one they only sit in, a spot on the ground below the exit, clear of
    // the hull, deployables, trees and walls, no drop and no bank.
    private bool ExitPoint(Ship ship, ShipSeatDef seat, out Vector3 at)
    {
        Vector3 up = Frame.UpAt(ship.State.Position);
        lock (JoltRuntime.Gate)
        {
            if (ship.Space is { } space)
            {
                foreach (ShipSocket exit in seat.Exits)
                {
                    Vector3 feet = space.Interior.SocketAt(exit, ship.Parts) + Vector3.UnitY * 0.05f;
                    if (space.Ray(feet + Vector3.UnitY * 0.3f, -Vector3.UnitY, 0.6f, out SpaceHit floor))
                    {
                        feet = floor.Point + Vector3.UnitY * 0.02f;
                        if (space.CapsuleClear(feet))
                        {
                            at = feet;
                            return true;
                        }
                    }
                }
                at = default;
                return false;
            }
            foreach (ShipSocket exit in seat.Exits)
            {
                Vector3 point = ship.PointOf(exit.Position);
                if (!ShipStill(ship))
                {
                    // In flight, leave beside the hull at the authored exit, never teleport down to the surface.
                    if (!InsideHull(ship, point, up) && CapsuleClear(point, up))
                    {
                        at = point;
                        return true;
                    }
                    continue;
                }
                Vector3 ground = Settle(point);
                if (Vector3.Dot(point - ground, up) > ExitHeight + 1 || Vector3.Dot(ground - point, up) > 1.5f)
                {
                    continue; // a drop or a bank there
                }
                Physics.EnsureGround(ground);
                Vector3 feet = ground + up * 0.05f;
                if (InsideHull(ship, feet, up) || !CapsuleClear(feet, up))
                {
                    continue;
                }
                at = feet;
                return true;
            }
        }
        at = default;
        return false;
    }

    private static bool InsideHull(Ship ship, Vector3 feet, Vector3 up) =>
        Hollow.Simulation.Placement.HullFootprint.Inside(ship.Def, ship.State.Position, ship.State.Rotation, feet + up * 0.9f, 0.35f);

    // Whether a standing character's capsule is clear of every static body but the ground (rays up and down its sides).
    private bool CapsuleClear(Vector3 feet, Vector3 up)
    {
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(0, up);
        Vector3 top = feet + up * 1.7f;
        Span<Vector3> sides = stackalloc Vector3[] { Vector3.Zero, forward * 0.3f, -forward * 0.3f, right * 0.3f, -right * 0.3f };
        foreach (Vector3 side in sides)
        {
            if (ShipCollider.Ray(feet + up * 0.4f + side, up, 1.3f, ShipCollider.NoBody, out ShipRayHit hit) && !PhysicsWorld.IsGroundBody(hit.Body))
            {
                return false;
            }
            if (ShipCollider.Ray(top + side, -up, 1.3f, ShipCollider.NoBody, out hit) && !PhysicsWorld.IsGroundBody(hit.Body))
            {
                return false;
            }
        }
        return true;
    }

    // ---------------------------------------------------------------- the seated occupant's keys (ShipIntent)

    // The ship a seated player acts on with their keys: theirs, and they in it.
    private Outcome Seated(Entity entity, EntityId target, out Player p, out Ship ship, out ShipSeatDef seat)
    {
        ship = null!;
        seat = null!;
        p = PlayerOf(entity)!;
        if (p is not { CanAct: true })
        {
            return OutcomeCode.Dead;
        }
        if (!TrySeat(entity, out ship, out seat) || ship.Destroyed || (!target.IsNone && ship.Id != target))
        {
            return OutcomeCode.NotAllowed;
        }
        return Outcome.Done;
    }

    /// <summary>
    /// A door worked from a seat (the occupant's key, IRON_WAKE.md §5.3: the helm, the copilot's seat): opens or shuts it, by
    /// the door's own rules; a seat that doesn't work the doors can't.
    /// </summary>
    public Outcome SeatedDoor(Entity entity, EntityId target, int door)
    {
        Outcome check = Seated(entity, target, out Player p, out Ship ship, out ShipSeatDef seat);
        if (!check.Ok)
        {
            return check;
        }
        if ((seat.Works & SeatWorks.Doors) == 0 || door < 0 || door >= ship.Doors.Length)
        {
            return OutcomeCode.NotAllowed;
        }
        return SetDoor(p, ship, door, !ship.Doors[door].Opening, fromSeat: true);
    }

    /// <summary>The seated occupant's lock key (the crew's): locks or unlocks their ship's doors.</summary>
    public Outcome SeatedLock(Entity entity, EntityId target)
    {
        Outcome check = Seated(entity, target, out Player p, out Ship ship, out _);
        return check.Ok ? SetShipLock(p, ship, !ship.Locked) : check;
    }
}

/// <summary>
/// The one seat rule (IRON_WAKE.md §5.2), the server's and every client's: where a seat's occupant is. Aboard a ship people
/// walk in, at the seat's socket in its frame, carried by its part at the parts' states (a gunner's seat turns with the
/// turret), still in that frame whatever the ship does; in one they only sit in, at the seat carried in the zone's frame
/// with the ship's velocity.
/// </summary>
public static class ShipSeats
{
    /// <summary>A seat's point in the ship's frame at the parts' states.</summary>
    public static Vector3 InFrame(ShipDef def, int seat, ReadOnlySpan<float> parts) =>
        def.Interior != null ? def.Interior.SocketAt(def.Seats[seat].At, parts) : def.Seats[seat].At.Position;

    /// <summary>A seat's eye in the ship's frame at the parts' states (its camera, IRON_WAKE.md §9.7).</summary>
    public static Vector3 EyeInFrame(ShipDef def, int seat, ReadOnlySpan<float> parts) =>
        def.Interior != null ? def.Interior.SocketAt(def.Seats[seat].Eye, parts) : def.Seats[seat].Eye.Position;

    /// <summary>Which way a seat faces in the ship's frame at the parts' states (its socket's forward, turned by its part).</summary>
    public static Vector3 Forward(ShipDef def, int seat, ReadOnlySpan<float> parts)
    {
        ShipSocket at = def.Seats[seat].At;
        Vector3 forward = at.Forward.LengthSquared() > 1e-6f ? Vector3.Normalize(at.Forward) : Vector3.UnitZ;
        if (def.Interior != null && at.Part > 0)
        {
            forward = Vector3.Transform(forward, def.Interior.PartPose(at.Part, parts).Rotation);
        }
        return forward;
    }

    /// <summary>
    /// Where a seated character's state is: aboard (true) in the ship's frame with no velocity, or in the zone's frame where
    /// the ship carries the seat, with the ship's velocity.
    /// </summary>
    public static (bool Aboard, Vector3 Position, Vector3 Velocity) Pose(ShipDef def, int seat, in ShipState state, ReadOnlySpan<float> parts)
    {
        Vector3 local = InFrame(def, seat, parts);
        return def.Interior != null ? (true, local, Vector3.Zero) : (false, state.PointOf(local, def.CentreOfMass), state.Velocity);
    }
}
