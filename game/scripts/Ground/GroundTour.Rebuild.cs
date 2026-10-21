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
        // At the car's own yaw its front, and so its bay (BayStats.Axes), faces the car.
        var ghost = Client.Placement().PlanAt(Placeable("vehicle-workbench")!, at, yaw)
            ?? throw new InvalidOperationException("rebuild: no ghost for the bench");
        GD.Print($"[tour] rebuild: the bench's ghost {(ghost.Valid ? "fits" : "doesn't fit: " + ghost.Outcome)}");
        var before = Client.Survival().Things.Select(t => t.Id).ToHashSet();
        Me.Place(ghost, ContainerRef.Main, SlotOf(Me.Inventory.Main, "vehicle-workbench")!.Value);
        await Until(() => Client.Survival().Things.Any(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == "vehicle-workbench"), 4);
        RemoteThing bench = Client.Survival().Things.FirstOrDefault(t => !before.Contains(t.Id) && t.Deployable(Client.Content)?.Key == "vehicle-workbench")
            ?? throw new InvalidOperationException("rebuild: the bench was not placed");
        (Num.Vector3 benchRight, Num.Vector3 benchOut) = BayStats.Axes(bench.Up, bench.Yaw);
        Num.Vector3 centre = bay.Centre(bench.Position, bench.Up, bench.Yaw);
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
        await TeleportTo(ground.Offset(bench.Position + benchRight * (benchDef.HalfExtents.X + 1.3f) + benchOut * 1.2f, 0, 0));
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
        // The engine out first (the owner's way, 21 October 2026): the car is still worked with at its door; then the stock
        // tyres, which leave its hubs bare; then the lift kit, the monster tyres and the blown six in: each a drag.
        await RebuildDrag(car, "NOSE", null);
        await RebuildDoorCheck(car, "its engine out");
        await RebuildOpenScreen(car);
        await RebuildDrag(car, "WHEELS", null);
        await RebuildDoorCheck(car, "its tyres off too");
        await RebuildPicture(car, side, "rebuild-bare", 3.2f);
        await RebuildOpenScreen(car);
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

        // Out of the bay, its own INSPECT VEHICLE: the same screen, read-only, its modules' condition.
        await Seconds(1.5);
        await TeleportTo(car.PointOf(def.PartPoint(seat) + new Num.Vector3(2.2f, 0, 0)));
        Face(car.Position);
        await Settle();
        View.Model.OpenVehicle(new TargetRef(TargetFamily.Ship, car.Id.Value, 0));
        View.Interface.Open(GroundScreen.Inventory);
        await Until(() => View.Model.Loot is { Kind: UI.Model.ContainerKind.Vehicle, Heading: "VEHICLE" }, 4);
        await Seconds(0.8);
        GD.Print($"[tour] rebuild: inspected away from the bay: {View.Model.Loot?.Heading} {View.Model.Loot?.Title}, " +
            $"{string.Join(", ", View.Model.Loot?.Labels.Where(l => l.Contains('%')) ?? Array.Empty<string>())}; {View.Model.Loot?.Hint}");
        await Shot("rebuild-inspect");
        View.Interface.Open(GroundScreen.Hud);
    }

    // E at the car's door from beside it, as a player does: what the game's aim meets and its prompt; then its door opened
    // and shut through the verbs. Thrown when the door doesn't answer.
    private async Task RebuildDoorCheck(RemoteShip car, string when)
    {
        View.Interface.Open(GroundScreen.Hud);
        ShipDef def = car.Def!;
        ShipTargetPart door = def.Parts.First(p => p.Door >= 0 && p.Seat >= 0);
        await TeleportTo(car.PointOf(door.At.Position + new Num.Vector3(1.6f, -0.7f, 0)));
        Face(car.PointOf(door.At.Position));
        await Settle();
        await Seconds(0.6);
        InteractAim? aim = View.Model.Aimed;
        GD.Print($"[tour] rebuild: {when}, E at its door meets {(aim == null ? "nothing" : $"{aim.Target.Family} part {aim.Target.Part} of {car.Def!.Parts.Count}")}; " +
            $"the prompt \"{View.Model.Prompt?.Action ?? "none"}\"");
        var target = new TargetRef(TargetFamily.Ship, car.Id.Value, (byte)door.Index);
        bool wasOpen = car.Doors.Length > door.Door && car.Doors[door.Door].Opening;
        if (wasOpen)
        {
            Me.Interact(target, "hatch-close");
            await Until(() => !car.Doors[door.Door].Opening, 3);
        }
        Me.Interact(target, "hatch-open");
        await Until(() => car.Doors.Length > door.Door && car.Doors[door.Door].Opening, 3);
        bool opened = car.Doors.Length > door.Door && car.Doors[door.Door].Opening;
        Me.Interact(target, "hatch-close");
        await Until(() => car.Doors.Length > door.Door && !car.Doors[door.Door].Opening, 3);
        GD.Print($"[tour] rebuild: {when}, its door {(opened ? "opened and shut" : "did not open")}");
        if (aim == null || aim.Target.Id != car.Id.Value || !opened)
        {
            throw new InvalidOperationException($"rebuild: {when}, the car isn't worked with at its door");
        }
        if (wasOpen)
        {
            // Left as it was found: the drive out goes in through it.
            Me.Interact(target, "hatch-open");
            await Until(() => car.Doors[door.Door].Opening, 3);
        }
        await Seconds(1.5);
    }

    // Back beside the bench, its vehicle screen open again.
    private async Task RebuildOpenScreen(RemoteShip car)
    {
        RemoteThing bench = Client.Survival().Things.First(t => t.Deployable(Client.Content)?.Bay != null);
        (Num.Vector3 right, Num.Vector3 outward) = BayStats.Axes(bench.Up, bench.Yaw);
        await TeleportTo(Client.Placement().Ground!.Value.Offset(bench.Position + right * (bench.Deployable(Client.Content)!.HalfExtents.X + 1.3f) + outward * 1.2f, 0, 0));
        Face(car.Position);
        await Settle();
        View.Model.OpenVehicle(new TargetRef(TargetFamily.Thing, bench.Id.Value, 0));
        View.Interface.Open(GroundScreen.Inventory);
        await Until(() => View.Model.Loot is { Kind: UI.Model.ContainerKind.Vehicle, Heading: null, Slots.Count: > 0 }, 4);
        await Seconds(0.5);
    }

    // A picture of the car in the bay from beside its rear corner, the bench behind it.
    private async Task RebuildPicture(RemoteShip car, Num.Vector3 side, string name, float distance = 7f)
    {
        Num.Vector3 up = Client.Frame.UpAt(car.Position);
        Num.Vector3 nose = Num.Vector3.Transform(Num.Vector3.UnitZ, car.Rotation);
        await TeleportTo(car.Position - nose * distance + side * distance);
        await Settle();
        Face(car.Position + up * 0.6f);
        await Seconds(1);
        await Shot(name);
    }

    // One drag on the vehicle screen: the module at a socket out into the inventory (null), or one from the inventory in.
    private async Task RebuildDrag(RemoteShip car, string socket, string? item)
    {
        LootView screen = View.Model.Loot ?? throw new InvalidOperationException("rebuild: the vehicle screen is closed");
        // A fitted module's label carries its condition ("NOSE  100%").
        int slot = screen.Labels.ToList().FindIndex(l => l == socket || l.StartsWith(socket + "  ", StringComparison.Ordinal));
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
