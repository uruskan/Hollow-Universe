using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using HollowUniverse.Ships;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// A TOGG made a Monster TOGG in the vehicle workbench's bay in the game (VEHICLES.md §9.11; <c>--tour-steps rebuild</c>):
/// <list type="number">
/// <item>a TOGG set down and a vehicle workbench placed ahead of it, facing it;</item>
/// <item>the TOGG driven into the bay the bench marks;</item>
/// <item>the bench's tech tree opened and its VEHICLE IN BAY button pressed;</item>
/// <item>the stock tyres and engine dragged out, and the lift kit, the monster tyres and the blown six dragged in, as a player
/// would;</item>
/// <item>the monster reversed out of the bay.</item>
/// </list>
/// Pictures: the TOGG in the bay, the tech tree, the vehicle screen before and after, the monster in the bay, and leaving it.
/// Prints what it did; throws when a step fails.
/// </summary>
public partial class GroundTour
{
    private async Task RebuildTour()
    {
        foreach (string key in new[] { "vehicle-workbench", "togg-lift-kit", "monster-tyres", "monster-togg-engine" })
        {
            Admin("give", key + " 1");
        }
        var old = Client.Ships().All.Select(s => s.Id).ToHashSet();
        Admin("ship", "togg");
        await Until(() => Client.Ships().All.Any(s => !old.Contains(s.Id) && s.Def != null) && Count("monster-togg-engine") > 0, 8);
        RemoteShip car = Client.Ships().All.FirstOrDefault(s => !old.Contains(s.Id) && s.Def != null)
            ?? throw new InvalidOperationException("rebuild: the TOGG was not set down");
        await Seconds(2);

        // The bench ahead of it along its nose, facing it, so that its bay lies 8 m ahead of the car's centre of mass.
        Num.Vector3 up = Client.Frame.UpAt(car.Position);
        float yaw = ShipMotor.YawOf(car.Rotation, up);
        (Num.Vector3 side, Num.Vector3 nose) = Hollow.Simulation.Combat.Geometry.Axes(up, yaw);
        DeployableDef benchDef = Client.Content.ItemByKey("vehicle-workbench").Deploys!;
        BayStats bay = benchDef.Bay!;
        var ground = Client.Placement().Ground!.Value;
        Num.Vector3 at = ground.Offset(car.Position + nose * (8f + bay.At.Z), 0, 0);
        await TeleportTo(at + side * 4f);
        Face(at);
        await Settle();
        var ghost = Client.Placement().PlanAt(Placeable("vehicle-workbench")!, at, yaw + MathF.PI)
            ?? throw new InvalidOperationException("rebuild: no ghost for the bench");
        GD.Print($"[tour] rebuild: the bench's ghost {(ghost.Valid ? "fits" : "doesn't fit: " + ghost.Outcome)}");
        var before = Client.Survival().Things.Select(t => t.Id).ToHashSet();
        Me.Place(ghost, ContainerRef.Main, SlotOf(Me.Inventory.Main, "vehicle-workbench")!.Value);
        await Until(() => Client.Survival().Things.Any(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == "vehicle-workbench"), 4);
        RemoteThing bench = Client.Survival().Things.FirstOrDefault(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == "vehicle-workbench")
            ?? throw new InvalidOperationException("rebuild: the bench was not placed");
        (Num.Vector3 benchRight, Num.Vector3 benchForward) = Hollow.Simulation.Combat.Geometry.Axes(bench.Up, bench.Yaw);
        Num.Vector3 centre = bench.Position + benchRight * bay.At.X + benchForward * bay.At.Z;
        GD.Print($"[tour] rebuild: the bench placed {Num.Vector3.Distance(bench.Position, at):0.00} m from where it was aimed; its bay's centre " +
            $"{Num.Vector3.Distance(centre, car.Position):0.0} m ahead of the car");

        // Into the bay: in at the cab's door, slowly forward until its centre of mass is at the bay's, the handbrake.
        ShipDef def = car.Def!;
        byte seat = (byte)def.PartOfSeat(def.Helm.Index);
        await TeleportTo(car.PointOf(def.PartPoint(seat) + new Num.Vector3(1.6f, 0, 0)));
        Face(car.PointOf(def.PartPoint(seat)));
        await Seconds(0.5);
        Me.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, seat), "hatch-open");
        await Seconds(1);
        Me.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, seat), def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        if (!Client.Ships().Piloting)
        {
            throw new InvalidOperationException("rebuild: the driver's seat wasn't taken");
        }
        View.ShipCamera.Mode = ShipCameraMode.Chase;
        View.ShipCamera.Reset();
        float Along() => Num.Vector3.Dot(car.Position - centre, nose);
        await Pilot(new ShipInput { Engines = true, Throttle = 0.45f }, 15, () => Along() > -1.6f);
        await Pilot(new ShipInput { Engines = true, Vertical = 1 }, 3, () => car.Velocity.Length() < 0.1f);
        View.Scripted = null;
        Me.LeaveSeat();
        await Until(() => !Client.Ships().Seated, 5);
        await Seconds(2.5);
        GD.Print($"[tour] rebuild: the TOGG stopped {Along():0.00} m along and {Num.Vector3.Dot(car.Position - centre, side):0.00} m across the bay's centre");
        await RebuildPicture(car, side, "rebuild-togg");

        // The bench's tech tree, then its VEHICLE IN BAY button: the vehicle screen beside the inventory.
        await TeleportTo(ground.Offset(bench.Position + benchRight * (benchDef.HalfExtents.X + 1.3f) + benchForward * 1.2f, 0, 0));
        Face(car.Position);
        await Settle();
        View.Interface.OpenTechTree();
        await Until(() => View.Model.TechTree is { Bay: true }, 4);
        await Seconds(0.8);
        await Shot("rebuild-techtree");
        View.Model.OpenBayVehicle();
        View.Interface.Open(GroundScreen.Inventory);
        await Until(() => View.Model.Loot is { Kind: UI.Model.ContainerKind.Vehicle, Slots.Count: > 0 }, 4);
        await Seconds(0.8);
        await Shot("rebuild-screen");
        // The stock tyres and engine out, the lift kit, the monster tyres and the blown six in: each a drag.
        await RebuildDrag(car, "WHEELS", null);
        await RebuildDrag(car, "NOSE", null);
        await RebuildDrag(car, "SUSPENSION", "togg-lift-kit");
        await RebuildDrag(car, "WHEELS", "monster-tyres");
        await RebuildDrag(car, "NOSE", "monster-togg-engine");
        await Seconds(1);
        await Shot("rebuild-screen-monster");
        View.Interface.Open(GroundScreen.Hud);
        await Until(() => car.Def?.Model == "monster-togg-frame", 3);
        GD.Print($"[tour] rebuild: now {car.Def?.Model}, {car.Def?.Mass:0} kg, {car.Def?.EnginePower / 1000:0} kW, tyres {car.Def?.Wheels.FirstOrDefault()?.Radius * 2:0.00} m; " +
            $"the stock tyres and engine in hand: {Count("togg-tyres")}, {Count("togg-engine")}");
        if (car.Def?.Model != "monster-togg-frame")
        {
            throw new InvalidOperationException("rebuild: the TOGG wasn't made a monster");
        }
        await Seconds(2.5);
        await RebuildPicture(car, side, "rebuild-monster");

        // Backed out of the bay on its new tyres.
        def = car.Def!;
        seat = (byte)def.PartOfSeat(def.Helm.Index);
        await TeleportTo(car.PointOf(def.PartPoint(seat) + new Num.Vector3(1.9f, 0, 0)));
        Face(car.PointOf(def.PartPoint(seat)));
        await Seconds(0.5);
        Me.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, seat), def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        View.ShipCamera.Mode = ShipCameraMode.Chase;
        View.ShipCamera.Reset();
        await Pilot(new ShipInput { Engines = true, Throttle = -0.6f }, 2.5);
        await Shot("rebuild-monster-out", frames: 0);
        await Pilot(new ShipInput { Engines = true, Vertical = 1 }, 2);
        View.Scripted = null;
        Me.LeaveSeat();
        await Until(() => !Client.Ships().Seated, 5);
        GD.Print($"[tour] rebuild: backed out, {Num.Vector3.Distance(car.Position, centre):0.0} m from the bay's centre");
    }

    // A picture of the car in the bay from beside its rear corner, the bench behind it.
    private async Task RebuildPicture(RemoteShip car, Num.Vector3 side, string name)
    {
        Num.Vector3 up = Client.Frame.UpAt(car.Position);
        Num.Vector3 nose = Num.Vector3.Transform(Num.Vector3.UnitZ, car.Rotation);
        await TeleportTo(car.Position - nose * 7f + side * 7f);
        await Settle();
        Face(car.Position + up * 0.6f);
        await Seconds(1);
        await Shot(name);
    }

    // One drag on the vehicle screen: the module at a socket out into the inventory (null), or one from the inventory in.
    private async Task RebuildDrag(RemoteShip car, string socket, string? item)
    {
        LootView screen = View.Model.Loot ?? throw new InvalidOperationException("rebuild: the vehicle screen is closed");
        int slot = screen.Labels.ToList().IndexOf(socket);
        Hollow.Simulation.Ships.Modules.ShipBuild was = car.Def!.Build;
        if (item == null)
        {
            View.Model.Move(new SlotRef(ContainerId.Loot, slot), new SlotRef(ContainerId.Main, 23), 1);
        }
        else
        {
            View.Model.Move(new SlotRef(ContainerId.Main, SlotOf(Me.Inventory.Main, item)!.Value), new SlotRef(ContainerId.Loot, slot), 1);
        }
        await Until(() => !car.Def!.Build.Equals(was), 3);
        GD.Print($"[tour] rebuild: {(item == null ? "took out" : "fitted " + item + " at")} {socket}: " +
            $"{(car.Def!.Build.Equals(was) ? "NO CHANGE" : "build " + car.Def.Build)}");
    }
}
