using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>
/// Changing a vehicle's build at the vehicle workbench (VEHICLES.md §9.11; the owner, 21 October 2026: the lift is the
/// workbench's). A module is fitted from the player's inventory into a socket, or taken out of one into it, engines and running
/// gear alike, for a vehicle standing in the bay the bench marks in front of it (deployables.json <c>bay</c>). The bay is the
/// only place a build changes, and the server decides: the player near the bench or its bay, the vehicle's centre of mass in
/// the bay, the vehicle still with nobody in its seats, the player on its crew, and the build after the change one
/// <see cref="ShipAssembler.Check"/> allows. The ship then takes its new layout (<see cref="Ship.Rebuild"/>), its parked body is
/// made again, and every client that knows it is sent its details (its build with them).
/// </summary>
public sealed partial class ShipSystem
{
    /// <summary>How far from its bay's clear space, or from the bench itself, a player may work on the vehicle in it (metres).</summary>
    public const float BayReach = 3f;

    /// <summary>Fits the module in an inventory slot into a socket and level of the vehicle in a station's bay.</summary>
    public Outcome FitModule(Entity entity, EntityId station, EntityId shipId, int socket, int level, ContainerRef from, int slot)
    {
        Outcome ready = AtBay(entity, station, shipId, out Player player, out Deployable bench, out Ship ship);
        if (!ready.Ok)
        {
            return ready;
        }
        if (from is not (ContainerRef.Main or ContainerRef.Belt) || slot < 0 || slot >= player.Inventory[from].Size)
        {
            return OutcomeCode.NotFound;
        }
        ItemStack stack = player.Inventory[from][slot];
        if (stack.IsEmpty || Content.Module(stack.Item) is not { } module || socket is < 0 or > byte.MaxValue || level is not (1 or 2))
        {
            return OutcomeCode.DoesNotFit;
        }
        ShipBuild build = ship.Def.Build.With(new Installed((byte)socket, (byte)level, module.Id));
        BuildProblem problem = ShipAssembler.Check(ship.Def.Kind, build, Content.Module, out ModuleDef? culprit, out ModuleDef? other);
        if (problem != BuildProblem.None)
        {
            return Refusal(problem, module, culprit, other);
        }
        stack.Count--;
        player.Inventory[from].Set(slot, stack.Count > 0 ? stack : default);
        Rebuilt(ship, Content.Compose(ship.Def.Kind, build), bench);
        return Outcome.Done;
    }

    /// <summary>Takes the module at a socket and level of the vehicle in a station's bay out into the player's inventory.</summary>
    public Outcome TakeModule(Entity entity, EntityId station, EntityId shipId, int socket, int level)
    {
        Outcome ready = AtBay(entity, station, shipId, out Player player, out Deployable bench, out Ship ship);
        if (!ready.Ok)
        {
            return ready;
        }
        if (ship.Def.Build.At(socket, level) is not { } entry || Content.Module(entry.Module) is not { } module)
        {
            return OutcomeCode.NotFound;
        }
        // What it holds stays with the vehicle: a locker emptied first (VEHICLES.md §3.3, rule 8).
        int placed = ship.Def.Placed.ToList().FindIndex(p => p.Entry == entry);
        for (int h = 0; h < ship.Def.Holds.Count; h++)
        {
            if (ship.Def.Holds[h].Module == placed && !ship.Holds[h].IsEmpty)
            {
                return OutcomeCode.NotEmpty;
            }
        }
        ShipBuild build = ship.Def.Build.Without(socket, level);
        BuildProblem problem = ShipAssembler.Check(ship.Def.Kind, build, Content.Module, out ModuleDef? culprit, out _);
        if (problem != BuildProblem.None)
        {
            // What stands on it, or needs it, comes off first.
            ModuleDef? inTheWay = problem == BuildProblem.NoRoof && Content.Module(ship.Def.Build.At(socket, 2)?.Module ?? 0) is { } above ? above : culprit;
            return OutcomeCode.TakeOffFirst.With(inTheWay?.Id ?? 0);
        }
        ShipDef def = Content.Compose(ship.Def.Kind, build);
        if (ship.Fuel.Units > def.FuelCapacity)
        {
            return OutcomeCode.NotEmpty;
        }
        if (player.Inventory.Give(Content, module.Item, 1) > 0)
        {
            return OutcomeCode.NoSpace;
        }
        Rebuilt(ship, def, bench);
        return Outcome.Done;
    }

    /// <summary>The vehicle standing in a station's bay (the first whose centre of mass is in it), or null.</summary>
    public Ship? InBay(Deployable station)
    {
        foreach (Ship ship in _ships)
        {
            if (!ship.Destroyed && ship.Def.Sockets.Count > 0 && IsInBay(station, ship.State.Position))
            {
                return ship;
            }
        }
        return null;
    }

    /// <summary>A point in a station's bay (the clear space in front of it, VEHICLES.md §9.11).</summary>
    public static bool IsInBay(Deployable station, Vector3 point) =>
        station.Def.Bay is { } bay && bay.Holds(station.Position, station.Up, station.Yaw, point);

    /// <summary>
    /// How far beyond a bay's clear space a vehicle's centre of mass may be for the bay to draw it in (metres), and the most
    /// it may be moving then (metres per second): Rust's car lift (VEHICLES.md §9.11).
    /// </summary>
    public const float BayPull = 1.5f, BayPullSpeed = 4f;

    /// <summary>The ticks a vehicle drawn into a bay glides for (half a second).</summary>
    public const int BayPullTicks = CharacterMotor.TickRate / 2;

    private readonly List<Deployable> _bays = new();
    private float _bayRadius = -1;

    /// <summary>
    /// A vehicle on wheels driven slowly onto a station's bay, or up to its edge (<see cref="BayPull"/>), is drawn into it as
    /// Rust's car lift draws a car: it glides there in <see cref="BayPullTicks"/> (<see cref="GlideIntoBay"/>), its centre of
    /// mass over the bay's middle, its nose along the bay whichever way it was nearer to, and stops. Once: it drives off as
    /// ever, and that bay leaves it alone until it has left it. Not when another vehicle stands in the bay, or anything but
    /// the ground, its own body and its own riders would be in the way where it ends.
    /// </summary>
    private void PullIntoBay(Ship ship)
    {
        // On its wheels: one without is held where it stands in its bay (HeldInBay).
        if (ship.Destroyed || ship.InOrbit || ship.Def.Ground == null || ship.Def.Sockets.Count == 0 || ship.Def.Wheels.Count == 0)
        {
            return;
        }
        Vector3 com = ship.State.Position;
        if (!ship.PulledInto.IsNone)
        {
            if (Things.StaticOf(ship.PulledInto) is Deployable { Def.Bay: { } held } at && held.Holds(at.Position, at.Up, at.Yaw, com, BayPull + 0.5f))
            {
                return;
            }
            ship.PulledInto = EntityId.None;
        }
        // Asleep it isn't arriving anywhere.
        if (ship.State.Asleep || ship.State.Velocity.LengthSquared() > BayPullSpeed * BayPullSpeed)
        {
            return;
        }
        if (_bayRadius < 0)
        {
            // The furthest a centre of mass drawn in can be from its station's base.
            _bayRadius = Content?.Deployables.Where(d => d.Bay != null).Select(d => d.Bay!.At.Length() + d.Bay!.Clear.Length() + BayPull).DefaultIfEmpty(0).Max() ?? 0;
        }
        _bays.Clear();
        Things.DeployablesNear(com, _bayRadius, _bays);
        foreach (Deployable station in _bays)
        {
            if (station.Def.Bay is not { } bay || !bay.Holds(station.Position, station.Up, station.Yaw, com, BayPull) ||
                _ships.Any(s => s != ship && !s.Destroyed && s.Def.Sockets.Count > 0 && IsInBay(station, s.State.Position)))
            {
                continue;
            }
            Vector3 up = Frame.UpAt(com);
            // Its nose along the bay, out of it or into it, whichever is nearer its heading.
            float heading = ShipMotor.YawOf(ship.State.Rotation, up), outward = BayStats.RollOutYaw(station.Yaw);
            float yaw = MathF.Abs(MathF.IEEERemainder(heading - outward, MathF.Tau)) <= MathF.PI / 2 ? outward : station.Yaw;
            // Its centre of mass over the bay's middle: the pivot set back by the centre of mass's reach along the ground.
            Vector3 middle = bay.Centre(station.Position, station.Up, station.Yaw);
            Vector3 reach = Vector3.Transform(ship.Def.CentreOfMass, ShipMotor.Orientation(up, yaw));
            Vector3 ground = Settle(middle - (reach - up * Vector3.Dot(reach, up)));
            ShipState standing = ShipMotor.Standing(ship.Def, ground + Frame.UpAt(ground) * 0.02f, Frame.UpAt(ground), yaw);
            Quaternion turn = Quaternion.Normalize(Facing.Turn(Frame.UpAt(ground), Frame.UpAt(standing.Position)) * standing.Rotation);
            if (!PullClear(ship, standing.Position, turn))
            {
                continue;
            }
            ship.PulledInto = station.Id;
            ship.PullPosition = standing.Position;
            ship.PullRotation = turn;
            ship.PullTicks = BayPullTicks;
            return;
        }
    }

    /// <summary>
    /// A vehicle a bay draws in moves its share of the rest of the way this tick, still (nobody is run over), awake (its body
    /// follows), its step skipped and its controls waiting: true while it glides.
    /// </summary>
    private static bool GlideIntoBay(Ship ship)
    {
        if (ship.PullTicks <= 0)
        {
            return false;
        }
        if (ship.Destroyed)
        {
            ship.PullTicks = 0;
            return false;
        }
        float share = 1f / ship.PullTicks--;
        ship.State.Position += (ship.PullPosition - ship.State.Position) * share;
        ship.State.Rotation = Quaternion.Normalize(Quaternion.Slerp(ship.State.Rotation, ship.PullRotation, share));
        ship.State.Velocity = ship.State.AngularVelocity = Vector3.Zero;
        ship.State.RestTicks = 0;
        return true;
    }

    // Whether a vehicle would stand at a pose clear of all but the ground, its own body and its own riders.
    private bool PullClear(Ship ship, Vector3 position, Quaternion rotation)
    {
        if (Placement.World is { Ships: { } collider, Physics: not null })
        {
            Span<ShipContact> contacts = stackalloc ShipContact[4];
            int found;
            lock (JoltRuntime.Gate)
            {
                found = collider.Overlaps(ship.Def, position, rotation, ship.ParkedBody == 0 ? ShipCollider.NoBody : ship.ParkedBody, contacts);
            }
            for (int i = 0; i < found; i++)
            {
                if (!PhysicsWorld.IsGroundBody(contacts[i].Body) || contacts[i].Depth > HullFootprint.GroundDepth)
                {
                    return false;
                }
            }
        }
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        float reach = ship.Def.BoundingRadius + 1;
        for (int i = 0; i < players.Count; i++)
        {
            Entity entity = Entities.AtSlot(players.Slots[i]);
            if (players.Values[i].Player.Dead || SeatOf(entity) == ship)
            {
                continue;
            }
            ref Character c = ref Entities.Get<Character>(entity);
            Vector3 feet = WorldOf(c.State);
            if (Vector3.DistanceSquared(feet, position) <= reach * reach && HullFootprint.Inside(ship.Def, position, rotation, feet + UpOf(c.State) * 0.9f, 0.4f))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// A vehicle on wheels with none fitted, standing in the bay it was changed in, is held there as on a lift's arms (its tyres
    /// off, it doesn't drop onto its belly): still and at rest, its step skipped, until its running gear is back or it is out
    /// of the bay.
    /// </summary>
    private bool HeldInBay(Ship ship)
    {
        if (ship.Bay.IsNone)
        {
            return false;
        }
        if (ship.Def.Ground == null || ship.Def.Wheels.Count > 0 || Things.StaticOf(ship.Bay) is not Deployable station || !IsInBay(station, ship.State.Position))
        {
            ship.Bay = EntityId.None;
            return false;
        }
        ship.State.Velocity = ship.State.AngularVelocity = Vector3.Zero;
        ship.State.RestTicks = Math.Max(ship.State.RestTicks, (ushort)ShipMotor.SleepTicks);
        return true;
    }

    // Everything but the build's own rules: the player able, near the bench or its bay and out of every seat; the vehicle in
    // the bay, still and empty; the player on its crew.
    private Outcome AtBay(Entity entity, EntityId stationId, EntityId shipId, out Player player, out Deployable station, out Ship ship)
    {
        ship = null!;
        station = null!;
        if (PlayerOf(entity) is not { CanAct: true } p)
        {
            player = null!;
            return OutcomeCode.NotAllowed;
        }
        player = p;
        if (Things.StaticOf(stationId) is not Deployable { Def.Bay: { } bay } found)
        {
            return OutcomeCode.NotFound;
        }
        station = found;
        Vector3 at = WorldOf(Entities.Get<Character>(entity).State);
        if (Vector3.Distance(station.Nearest(at), at) > BayReach && !bay.Holds(station.Position, station.Up, station.Yaw, at, BayReach))
        {
            return OutcomeCode.OutOfReach;
        }
        if (SeatOf(entity) != null)
        {
            return OutcomeCode.Seated;
        }
        Ship? vehicle = _ships.FirstOrDefault(s => s.Id == shipId && !s.Destroyed);
        if (vehicle == null || vehicle.Def.Sockets.Count == 0 || !IsInBay(station, vehicle.State.Position))
        {
            return OutcomeCode.NeedsStation.With(station.Def.Item.Id);
        }
        ship = vehicle;
        if (!ship.IsAuthorised(player.Id.Value))
        {
            return OutcomeCode.NotCrew;
        }
        if (!ShipStill(ship) || ship.State.Velocity.Length() > 0.3f)
        {
            return OutcomeCode.ShipMoving;
        }
        if (ship.Occupants.Any(o => !o.IsNone))
        {
            return OutcomeCode.SomeoneAboard;
        }
        return Outcome.Done;
    }

    // A build the assembler refuses, said as the player is told: what is in the way comes off, what is needed goes on first.
    private Outcome Refusal(BuildProblem problem, ModuleDef fitting, ModuleDef? culprit, ModuleDef? other) => problem switch
    {
        BuildProblem.Taken => OutcomeCode.TakeOffFirst.With(culprit?.Id ?? 0),
        BuildProblem.Excludes => OutcomeCode.TakeOffFirst.With((culprit == fitting ? other : culprit)?.Id ?? 0),
        BuildProblem.Needs when culprit == fitting => OutcomeCode.FitFirst.With(Content.Modules.FirstOrDefault(m => m != fitting && m.FitsAny(fitting.Needs))?.Id ?? 0),
        BuildProblem.Needs => OutcomeCode.TakeOffFirst.With(culprit?.Id ?? 0),
        _ => OutcomeCode.DoesNotFit,
    };

    // A ship takes its new layout: the state carried over, its body made again where it was parked, everyone told.
    private void Rebuilt(Ship ship, ShipDef def, Deployable station)
    {
        ship.HandStarted = false;
        ship.Bay = station.Id;
        ship.Rebuild(def);
        if (ship.ParkedBody != 0)
        {
            lock (JoltRuntime.Gate)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
                Park(ship);
            }
        }
        WakeShip(ship);
        ship.DetailsRevision++;
        ship.PartsRevision++;
    }
}
