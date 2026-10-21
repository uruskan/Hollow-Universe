using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Protocol;
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
/// opens it beside the inventory, as loot opens, and so does a vehicle's own INSPECT VEHICLE (held E at it, anywhere). It
/// lists the vehicle by its kind's sockets, a slot for each socket and level with the module there and its condition, and
/// what the build makes. While the vehicle stands in a bay the player is near, a module dragged in from the inventory is
/// fitted (<see cref="LocalPlayer.FitModule"/>), one dragged out is taken out (<see cref="LocalPlayer.TakeModule"/>);
/// elsewhere the screen only shows it. Nothing is predicted: the server checks every rule and the vehicle's new build
/// arrives with its details; a refusal is shown as every refusal is. The screen closes when the player walks away.
/// </summary>
public sealed partial class ClientGroundUi
{
    /// <summary>The station whose bay's screen is open, or none.</summary>
    private EntityId _bay;

    /// <summary>The vehicle whose screen its INSPECT VEHICLE opened, or none.</summary>
    private EntityId _inspected;

    /// <summary>How far beyond its bounds a vehicle's screen stays open (metres).</summary>
    private const float InspectReach = 4;

    private readonly List<(int Socket, int Level)> _vehicleSlots = new();

    /// <summary>The panel EDIT VEHICLE and INSPECT VEHICLE open (interactions.json's <c>panel</c>).</summary>
    public const string VehiclePanel = "vehicle";

    /// <summary>Opens the vehicle screen of a station's bay, or of a vehicle (its verb's panel).</summary>
    public void OpenVehicle(TargetRef target)
    {
        CloseLoot();
        if (target.Family == TargetFamily.Ship)
        {
            _inspected = new EntityId(target.Id);
        }
        else
        {
            _bay = new EntityId(target.Id);
        }
    }

    /// <summary>The tech tree's VEHICLE button: the vehicle screen of the bench whose tree is open.</summary>
    public void OpenBayVehicle()
    {
        if (!_techBench.IsNone)
        {
            OpenVehicle(_techBench);
        }
    }

    // The screen open: the station whose bay the vehicle is worked on in (null: it is only looked at) and the vehicle (null:
    // none in the bay); closed once out of reach.
    private (RemoteThing? Station, RemoteShip? Ship)? OpenScreen()
    {
        if (!_inspected.IsNone)
        {
            if (_client.Ships().All.FirstOrDefault(s => s.Id == _inspected) is not { Def: { } def } looked || looked.Has(ShipNetFlags.Destroyed) ||
                Num.Vector3.Distance(looked.Position, _eye) > def.BoundingRadius + InspectReach)
            {
                _inspected = EntityId.None;
                return null;
            }
            RemoteThing? at = _client.Survival().Things.FirstOrDefault(t => BayInReach(t) is { } b && b.Holds(t.Position, t.Up, t.Yaw, looked.Position));
            return (at, looked);
        }
        if (_bay.IsNone)
        {
            return null;
        }
        if (_client.Survival().ThingOf(_bay) is not { } station || BayInReach(station) is not { } bay)
        {
            _bay = EntityId.None;
            return null;
        }
        RemoteShip? ship = _client.Ships().All.FirstOrDefault(s => s.Def is { Sockets.Count: > 0 } && bay.Holds(station.Position, station.Up, station.Yaw, s.Position));
        return (station, ship);
    }

    // A station's bay, when the player is near enough the station or its bay to work there (the server's rule, a metre kinder).
    private BayStats? BayInReach(RemoteThing station) =>
        station.Deployable(C) is { Bay: { } bay } def &&
        (Num.Vector3.Distance(Hollow.Simulation.Combat.Geometry.NearestOnBox(station.Position, station.Up, station.Yaw, def.HalfExtents, _eye), _eye) <= ShipSystem.BayReach + 1 ||
         bay.Holds(station.Position, station.Up, station.Yaw, _eye, ShipSystem.BayReach + 1))
            ? bay
            : null;

    // The vehicle screen as the loot panel shows it, or null when none is open.
    private LootView? VehicleLoot()
    {
        if (OpenScreen() is not var (station, ship))
        {
            return null;
        }
        _vehicleSlots.Clear();
        var slots = new List<UiStack>();
        var labels = new List<string>();
        var lines = new List<string>();
        string? reason = null, hint = null;
        if (ship?.Def is { } def)
        {
            foreach (ShipSocketDef socket in def.Kind.Sockets)
            {
                for (int level = 1; level <= socket.Levels; level++)
                {
                    _vehicleSlots.Add((socket.Index, level));
                    string label = (level == 2 ? socket.Name + " roof" : socket.Name).ToUpperInvariant();
                    if (def.Placed.FirstOrDefault(p => p.Entry.Socket == socket.Index && p.Entry.Level == level) is { } placed && C.Item(placed.Entry.Module) is { } item)
                    {
                        float condition = Condition(ship, placed);
                        slots.Add(new UiStack(item.Key, 1, condition));
                        labels.Add(condition >= 0 ? string.Format(CultureInfo.InvariantCulture, "{0}  {1:0}%", label, condition * 100) : label);
                    }
                    else
                    {
                        slots.Add(default);
                        labels.Add(label);
                    }
                }
            }
            lines.Add(string.Format(CultureInfo.InvariantCulture, "CHASSIS {0:0}%", Math.Clamp(ship.Health, 0, 1) * 100));
            lines.AddRange(Facts(def));
            if (station == null)
            {
                hint = "DRIVE IT INTO A VEHICLE WORKBENCH'S BAY TO CHANGE IT";
            }
            else if (ship.Occupants.Any(o => !o.IsNone))
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
        string title = ship?.Def?.Name ?? station?.Deployable(C)?.Item.Name ?? "Vehicle workbench";
        return new LootView(UiContainerKind.Vehicle, title.ToUpperInvariant(), slots)
        {
            Heading = station == null ? "VEHICLE" : null, Labels = labels, Lines = lines, Reason = reason, Hint = hint,
        };
    }

    // A module's condition: its parts' health at its weakest (0 … 1), or −1 when none of them can be hurt.
    private static float Condition(RemoteShip ship, PlacedModule placed)
    {
        float least = -1;
        foreach (ShipTargetPart part in placed.Module.Parts)
        {
            int index = placed.FirstPart + part.Index;
            if (part.Health != null && index < ship.PartHealth.Length)
            {
                least = least < 0 ? ship.PartHealth[index] : Math.Min(least, ship.PartHealth[index]);
            }
        }
        return least;
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

    // A drag on the vehicle screen: into a socket fits, out of one takes out (away from a bay, only says where it can be). True
    // when it was the vehicle's.
    private bool MoveModule(SlotRef from, SlotRef to)
    {
        if (OpenScreen() is not var (station, ship) || ship == null)
        {
            return false;
        }
        bool fit = to.Container == ContainerId.Loot && from.Container is ContainerId.Main or ContainerId.Belt && to.Index < _vehicleSlots.Count;
        bool take = from.Container == ContainerId.Loot && from.Index < _vehicleSlots.Count;
        if ((fit || take) && station == null)
        {
            // Named as the server names its own refusal: the first station with a bay.
            Notice?.Invoke(C.Texts.Line(OutcomeCode.NeedsStation.With(C.Deployables.FirstOrDefault(d => d.Bay != null)?.Item.Id ?? 0)));
            return true;
        }
        if (fit)
        {
            (int socket, int level) = _vehicleSlots[to.Index];
            Me.FitModule(station!.Id, ship.Id, socket, level, from.Container == ContainerId.Main ? ContainerRef.Main : ContainerRef.Belt, from.Index);
            return true;
        }
        if (take)
        {
            (int socket, int level) = _vehicleSlots[from.Index];
            Me.TakeModule(station!.Id, ship.Id, socket, level);
            return true;
        }
        return false;
    }

    // A right click on the vehicle screen: a module out, or a module from the inventory into the first socket that takes it.
    private bool QuickMoveModule(SlotRef slot)
    {
        if (OpenScreen() is not var (_, ship) || ship?.Def is not { } def)
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
