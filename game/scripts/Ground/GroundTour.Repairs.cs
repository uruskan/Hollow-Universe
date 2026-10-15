using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Items;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using HollowUniverse.Ships;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

// Actual hosted-client coverage of the repaired fuel inventory and seated mouse gestures.
// Run on an isolated fresh save with --tour <folder> --tour-steps repair-ships.
public partial class GroundTour
{
    private async Task RepairCorpses()
    {
        await RepairGod(false);
        Num.Vector3 deathAt = Client.Character.Position;
        EntityId owner = Client.EntityId;
        Admin("kill", "");
        await Until(() => Me.IsDead, 5);
        RequireRepair(Me.IsDead, "ordinary death reaches the death screen");
        await Shot("repair-death-screen");
        Me.Respawn();
        await Until(() => !Me.IsDead && Client.HasCharacter, 8);
        RequireRepair(!Me.IsDead, "respawn completes without a crash");
        await RepairGod(true);
        await TeleportTo(Away(deathAt, 2.4f));
        await Settle();
        await Until(() => Client.Survival().Things.Any(t => t.Kind == StaticKind.Corpse && t.CorpseOwner == owner.Value && t.IsBody), 6);
        RemoteThing body = Client.Survival().Things.FirstOrDefault(t => t.Kind == StaticKind.Corpse && t.CorpseOwner == owner.Value && t.IsBody)
            ?? throw new InvalidOperationException("repair-corpses: authoritative body did not replicate after respawn");
        Face(body.Position + body.Up * 0.25f);
        await Seconds(0.7);
        await Shot("repair-corpse-body");
        Me.Interact(Thing(body.Id), "loot");
        await Until(() => Me.LootTarget == body.Id, 4);
        RequireRepair(Me.LootTarget == body.Id && Me.Loot is { IsEmpty: false }, "body exposes the preserved inventory");
        await Seconds(0.5);
        await Shot("repair-corpse-loot");
        Me.Close();
        View.Interface.Open(GroundScreen.Hud);
        // The original tools are deliberately still inside the corpse's preserved inventory.
        Admin("give", "stone-hatchet 1");
        await Until(() => Count("stone-hatchet") > 0, 4);
        await Hold("stone-hatchet", 2);
        RequireRepair(Me.Inventory.Belt[Me.HeldSlot].Item == Client.Content.ItemByKey("stone-hatchet").Id,
            "respawned harvester has equipped a new tool while the original loot remains intact");
        await TeleportTo(Away(body.Position, 1.2f));
        Face(body.Position + body.Up * 0.2f);
        await Seconds(0.5);
        View.Scripted = new InputFrame { Primary = true };
        await Until(() => body.Health < 0.99f || !body.IsBody, 6);
        View.Scripted = new InputFrame();
        RequireRepair(body.IsBody && body.Health is > 0 and < 0.99f, "a normal melee hit damages the body before its final harvest");
        await Seconds(0.25);
        RequireRepair(View.Interface.Model.TargetHealth is { Fraction: > 0 and < 1 }, "damaged corpse exposes the shared target health bar");
        await Shot("repair-corpse-damaged");
        View.Scripted = new InputFrame { Primary = true };
        await Until(() => !body.IsBody, 18);
        View.Scripted = null;
        RequireRepair(!body.IsBody && Client.Survival().ThingOf(body.Id) != null,
            "real melee harvesting converts the same loot entity to a backpack");
        Me.HeldSlot = -1;
        await TeleportTo(Away(body.Position, 2));
        Face(body.Position + body.Up * 0.35f);
        await Seconds(0.5);
        await Shot("repair-corpse-backpack");
        Me.Interact(Thing(body.Id), "loot");
        await Until(() => Me.LootTarget == body.Id, 4);
        RequireRepair(Me.LootTarget == body.Id && Me.Loot is { IsEmpty: false }, "backpack retains the body's loot");
        await Shot("repair-backpack-loot");
        GD.Print("[repair-check] PASS death, respawn, visible body, loot, harvest and same-container backpack");
    }

    private async Task RepairShips()
    {
        await RepairGod(true);
        var old = Client.Ships().All.Select(s => s.Id).ToHashSet();
        Admin("ship", "tin-finch");
        await Until(() => Client.Ships().All.Any(s => !old.Contains(s.Id) && s.Def != null), 8);
        RemoteShip ship = Client.Ships().All.FirstOrDefault(s => !old.Contains(s.Id) && s.Def != null)
            ?? throw new InvalidOperationException("repair-ships: spawned craft did not replicate");
        ShipDef def = ship.Def!;
        await Seconds(0.5);

        byte cap = (byte)Math.Max(0, def.PartIndex("fuel"));
        await TeleportTo(ship.PointOf(def.PartPoint(cap) - Num.Vector3.UnitX * 1.4f));
        Face(ship.PointOf(def.PartPoint(cap)));
        await Settle();
        Me.Interact(new TargetRef(TargetFamily.Ship, ship.Id.Value, cap), "refuel");
        await Until(() => Me.LootTarget == ship.Id && Me.Loot is { Size: 1 }, 5);
        RequireRepair(Me.LootTarget == ship.Id && Me.Loot is { Size: 1 }, "fuel cap opens one inventory slot");
        RequireRepair(Me.Loot!.AcceptedItem == def.Fuel.Id, "fuel slot carries the shared item constraint");
        await Seconds(0.5);
        await Shot("repair-fuel-tank");
        Me.Close();
        View.Interface.Open(GroundScreen.Hud);

        byte hatch = (byte)def.PartOfSeat(def.Helm.Index);
        await TeleportTo(ship.PointOf(def.PartPoint(hatch) - Num.Vector3.UnitX * 1.4f));
        Face(ship.PointOf(def.PartPoint(hatch)));
        await Seconds(0.5);
        Me.Interact(new TargetRef(TargetFamily.Ship, ship.Id.Value, hatch), def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        RequireRepair(Client.Ships().Piloting, "pilot boards through the ordinary seat interaction");
        View.ShipCamera.Mode = ShipCameraMode.Cockpit;
        View.ShipCamera.Reset();
        await Pilot(new ShipInput { Engines = true, Vertical = 1 }, 2.5);
        await RepairDeviceInput();
        await Seconds(0.8);
        RequireRepair(View.ShipCamera.Mode == ShipCameraMode.Cockpit, "initial pilot view is the cockpit");
        await Shot("repair-pilot-cockpit");

        await RepairKey(Key.V);
        RequireRepair(View.ShipCamera.Mode == ShipCameraMode.Chase, "V selects the seated orbit camera");
        var beforeDrag = View.SeatOrbitState;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Seconds(0.1);
        RequireRepair(View.SeatOrbitState.Dragging, "left press starts orbit dragging");
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(220, -35), ButtonMask = MouseButtonMask.Left });
        await Seconds(0.15);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await Seconds(0.15);
        var afterDrag = View.SeatOrbitState;
        RequireRepair(MathF.Abs(afterDrag.Yaw - beforeDrag.Yaw) > 0.1f && MathF.Abs(afterDrag.Pitch - beforeDrag.Pitch) > 0.01f,
            "left drag changes orbit yaw and pitch independently of ship motion");
        RequireRepair(!afterDrag.Dragging, "left release ends orbit dragging");
        float beforeWheel = afterDrag.Distance;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false });
        await Seconds(0.2);
        RequireRepair(View.SeatOrbitState.Distance > beforeWheel + 0.1f, "wheel down increases orbit distance");
        GD.Print($"[repair-check] orbit yaw {beforeDrag.Yaw:0.000}->{afterDrag.Yaw:0.000}, pitch {beforeDrag.Pitch:0.000}->{afterDrag.Pitch:0.000}, distance {beforeWheel:0.00}->{View.SeatOrbitState.Distance:0.00}");
        await Shot("repair-pilot-orbit");

        // Use held devices through the same input path as leaving, so switching away from a scripted command cannot
        // insert a neutral control tick before the seat release reaches the server.
        await RepairKey(Key.W, 0.35);
        RequireRepair(View.ShipControls.Throttle > 0.2f, "W raises the persistent throttle before departure");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = true });
        await Seconds(0.4);
        await RepairKey(Key.Space);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = false });
        await Until(() => !Client.Ships().Seated, 3);
        RequireRepair(!Client.Ships().Seated, "Space leaves the seat in flight");
        await Seconds(0.8);
        RequireRepair(ship.Thrust.LengthSquared() > 0.001f && ship.Velocity.LengthSquared() > 1,
            "unattended craft retains powered motion");
        Face(ship.Position);
        await Shot("repair-unattended-flight");
        GD.Print("[repair-check] PASS fuel inventory, pilot camera drag/zoom, airborne exit and unattended flight");
    }

    private async Task RepairKey(Key key, double heldSeconds = 0.1)
    {
        await RepairDeviceInput();
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Seconds(heldSeconds);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Seconds(0.15);
    }

    private async Task RepairDeviceInput()
    {
        View.Scripted = null;
        GetWindow().GrabFocus();
        await Until(() => GetWindow().HasFocus() && !View.Interface.WantsCursor && !View.Interface.TakesKeys &&
            Input.MouseMode == Input.MouseModeEnum.Captured, 4);
        RequireRepair(GetWindow().HasFocus() && !View.Interface.WantsCursor && !View.Interface.TakesKeys &&
            Input.MouseMode == Input.MouseModeEnum.Captured, "device input has window focus and a captured mouse");
    }

    private async Task RepairGod(bool enabled)
    {
        // Admin() puts the player's name first; god's on/off syntax expects it last. A guarded toggle uses the
        // existing name-only form and verifies the authoritative state received by the client.
        if (Client.Character.Has(MoveCheats.God) != enabled) Admin("god", "");
        await Until(() => Client.Character.Has(MoveCheats.God) == enabled, 4);
        RequireRepair(Client.Character.Has(MoveCheats.God) == enabled, enabled ? "god mode is enabled" : "god mode is disabled");
    }

    private static void RequireRepair(bool condition, string requirement)
    {
        if (!condition) throw new InvalidOperationException("repair-check: " + requirement);
        GD.Print("[repair-check] PASS " + requirement);
    }
}
