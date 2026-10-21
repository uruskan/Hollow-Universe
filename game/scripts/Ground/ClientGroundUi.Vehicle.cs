using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using Hollow.Simulation.Targets;
using HollowUniverse.UI.Model;
using Num = System.Numerics;
using UiStack = HollowUniverse.UI.Model.ItemStack;
using UiContainerKind = HollowUniverse.UI.Model.ContainerKind;

namespace HollowUniverse.Ground;

/// <summary>
/// The vehicle screen (VEHICLES.md §9.11): the vehicle workbench's EDIT VEHICLE (held E, or the tech tree's VEHICLE button)
/// opens it beside the inventory, as loot opens. It lists the vehicle standing in the bench's bay by its kind's sockets, a slot
/// for each socket and level with the module there; a module dragged in from the inventory is fitted
/// (<see cref="LocalPlayer.FitModule"/>), one dragged out is taken out (<see cref="LocalPlayer.TakeModule"/>). Nothing is
/// predicted: the server checks every rule and the vehicle's new build arrives with its details; a refusal is shown as every
/// refusal is. The screen closes when the player walks away.
/// </summary>
public sealed partial class ClientGroundUi
{
    /// <summary>The station whose bay's screen is open, or none.</summary>
    private EntityId _bay;

    private readonly List<(int Socket, int Level)> _vehicleSlots = new();

    /// <summary>The panel the vehicle workbench's EDIT VEHICLE opens (interactions.json <c>edit-vehicle</c>'s <c>panel</c>).</summary>
    public const string VehiclePanel = "vehicle";

    /// <summary>Opens the vehicle screen of a station's bay (its verb's panel).</summary>
    public void OpenVehicle(TargetRef station)
    {
        CloseLoot();
        _bay = new EntityId(station.Id);
    }

    /// <summary>The tech tree's VEHICLE button: the vehicle screen of the bench whose tree is open.</summary>
    public void OpenBayVehicle()
    {
        if (!_techBench.IsNone)
        {
            OpenVehicle(_techBench);
        }
    }

    // The bay open, the station it is and the vehicle in it (null: none); closed once out of reach.
    private (RemoteThing Station, BayStats Bay, RemoteShip? Ship)? OpenBay()
    {
        if (_bay.IsNone)
        {
            return null;
        }
        if (_client.Survival().ThingOf(_bay) is not { } station || station.Deployable(C) is not { Bay: { } bay } def ||
            Num.Vector3.Distance(Hollow.Simulation.Combat.Geometry.NearestOnBox(station.Position, station.Up, station.Yaw, def.HalfExtents, _eye), _eye) > ShipSystem.BayReach + 1 &&
            !bay.Holds(station.Position, station.Up, station.Yaw, _eye, ShipSystem.BayReach + 1))
        {
            _bay = EntityId.None;
            return null;
        }
        RemoteShip? ship = _client.Ships().All.FirstOrDefault(s => s.Def is { Sockets.Count: > 0 } && bay.Holds(station.Position, station.Up, station.Yaw, s.Position));
        return (station, bay, ship);
    }

    // The vehicle screen as the loot panel shows it, or null when none is open.
    private LootView? VehicleLoot()
    {
        if (OpenBay() is not var (station, _, ship))
        {
            return null;
        }
        _vehicleSlots.Clear();
        var slots = new List<UiStack>();
        var labels = new List<string>();
        var lines = new List<string>();
        string? reason = null;
        if (ship?.Def is { } def)
        {
            foreach (ShipSocketDef socket in def.Kind.Sockets)
            {
                for (int level = 1; level <= socket.Levels; level++)
                {
                    _vehicleSlots.Add((socket.Index, level));
                    labels.Add((level == 2 ? socket.Name + " roof" : socket.Name).ToUpperInvariant());
                    slots.Add(def.Build.At(socket.Index, level) is { } entry && C.Item(entry.Module) is { } item ? new UiStack(item.Key, 1) : default);
                }
            }
            lines.AddRange(Facts(def));
            if (ship.Occupants.Any(o => !o.IsNone))
            {
                reason = C.Texts.Line(OutcomeCode.SomeoneAboard);
            }
            else if (ship.Velocity.Length() > 0.3f)
            {
                reason = C.Texts.Line(OutcomeCode.ShipMoving);
            }
        }
        else
        {
            reason = "Drive a vehicle into the bay";
        }
        string title = ship?.Def?.Name ?? station.Deployable(C)?.Item.Name ?? "Vehicle workbench";
        return new LootView(UiContainerKind.Vehicle, title.ToUpperInvariant(), slots) { Labels = labels, Lines = lines, Reason = reason };
    }

    // What a build makes, read off its composed layout: the numbers the lift's panel shows (VEHICLES.md §8), and what it lacks.
    private static IEnumerable<string> Facts(ShipDef def)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        yield return string.Format(inv, "MASS {0:0} KG", def.Mass);
        yield return def.EnginePower > 0 ? string.Format(inv, "POWER {0:0} KW", def.EnginePower / 1000) : "NO ENGINE";
        if (def.Ground is { } g)
        {
            yield return string.Format(inv, "TOP SPEED {0:0} KM/H", g.TopSpeed * 3.6f);
        }
        if (def.Wheels.Count > 0)
        {
            yield return string.Format(inv, "{0} WHEELS, {1:0.00} M TYRES", def.Wheels.Count, def.Wheels.Max(w => w.Radius) * 2);
        }
        else if (def.Ground != null)
        {
            yield return "NO WHEELS";
        }
        if (def.Raise > 0)
        {
            yield return string.Format(inv, "LIFTED {0:0.00} M", def.Raise);
        }
        if (!def.Seats.Any(s => s.Flies))
        {
            yield return "NO DRIVER'S SEAT";
        }
    }

    // A drag on the vehicle screen: into a socket fits, out of one takes out. True when it was the vehicle's.
    private bool MoveModule(SlotRef from, SlotRef to)
    {
        if (OpenBay() is not var (_, _, ship) || ship == null)
        {
            return false;
        }
        if (to.Container == ContainerId.Loot && from.Container is ContainerId.Main or ContainerId.Belt && to.Index < _vehicleSlots.Count)
        {
            (int socket, int level) = _vehicleSlots[to.Index];
            Me.FitModule(_bay, ship.Id, socket, level, from.Container == ContainerId.Main ? ContainerRef.Main : ContainerRef.Belt, from.Index);
            return true;
        }
        if (from.Container == ContainerId.Loot && from.Index < _vehicleSlots.Count)
        {
            (int socket, int level) = _vehicleSlots[from.Index];
            Me.TakeModule(_bay, ship.Id, socket, level);
            return true;
        }
        return false;
    }

    // A right click on the vehicle screen: a module out, or a module from the inventory into the first socket that takes it.
    private bool QuickMoveModule(SlotRef slot)
    {
        if (OpenBay() is not var (_, _, ship) || ship?.Def is not { } def)
        {
            return false;
        }
        if (slot.Container == ContainerId.Loot)
        {
            return MoveModule(slot, new SlotRef(ContainerId.Main, 0));
        }
        if (slot.Container is not (ContainerId.Main or ContainerId.Belt) || Slots(slot.Container) is not { } stacks || slot.Index >= stacks.Count ||
            C.Item(stacks[slot.Index].Key)?.Module is not { } module)
        {
            return false;
        }
        for (int i = 0; i < _vehicleSlots.Count; i++)
        {
            (int socket, int level) = _vehicleSlots[i];
            if (ShipAssembler.Check(def.Kind, def.Build.With(new Installed((byte)socket, (byte)level, module.Id)), C.Module) == BuildProblem.None)
            {
                return MoveModule(slot, new SlotRef(ContainerId.Loot, i));
            }
        }
        Notice?.Invoke(C.Texts.Line(OutcomeCode.DoesNotFit));
        return true;
    }
}
