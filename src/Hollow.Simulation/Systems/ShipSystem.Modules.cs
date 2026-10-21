using System;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Physics;
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
