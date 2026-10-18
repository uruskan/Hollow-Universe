using System;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // A real input-path check of the F1 self camera, including its handover back to first-person weapon controls.
    private async Task AdminCamera()
    {
        await Until(() => Client.Console().IsAdmin, 10);
        RequireAdminCamera(Client.Console().IsAdmin, "the hosted player has admin rights");
        RequireAdminCamera(!View.AdminThirdPerson, "a fresh session starts in first person");
        await RepairGod(true);
        var (spot, towards) = OpenSpot(Offset(35, 20));
        await TeleportTo(spot);
        Face(spot + towards * 12 + Client.RenderUp * Client.EyeHeight);
        await Settle();
        Admin("give", "rifle 1");
        await Until(() => Count("rifle") > 0, 4);
        await Hold("rifle", 5);
        await Until(() => Me.Gun.Ready, 4);
        RequireAdminCamera(Me.Gun.Ready, "the held rifle is ready, so aim leakage can be observed");
        float sensitivity = View.Console.Settings.Sensitivity;
        View.Console.Settings.Sensitivity = 1;
        try
        {
            View.Console.Run("thirdperson on");
            await Seconds(0.3);
            RequireAdminCamera(View.AdminThirdPerson, "the F1 thirdperson command enables the camera");
            View.Interface.ToggleConsole();
            View.Interface.ConsoleTab = ConsoleTab.Tools;
            View.Interface.HoldPointer(new Vector2(-10000, -10000));
            await Seconds(0.4);
            await Shot("admin-camera-tools");
            View.Interface.Open(GroundScreen.Hud);
            await RepairDeviceInput();
            await Seconds(0.3);
            RequireAdminCamera(View.People.OwnPlayerVisible && !View.FirstPersonVisible,
                "third person shows the full local character and hides first-person arms");
            await Shot("admin-camera-rear");

            InputCommand before = Client.Prediction!.CommandFor(Client.PredictedTick);
            var start = View.AdminOrbitState;
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(100, 20) });
            await Seconds(0.15);
            RequireAdminCamera(View.AdminOrbitState.Yaw == start.Yaw && View.AdminOrbitState.Pitch == start.Pitch,
                "moving the mouse without right click leaves the camera angle fixed");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(80, -20), ButtonMask = MouseButtonMask.Left });
            await Seconds(0.15);
            RequireAdminCamera(!View.AdminOrbitState.Dragging && View.AdminOrbitState.Yaw == start.Yaw && View.AdminOrbitState.Pitch == start.Pitch,
                "left click does not turn the inspection camera");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Seconds(0.1);
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(-MathF.PI / 0.002f, -20), ButtonMask = MouseButtonMask.Right });
            await Seconds(0.25);
            var turned = View.AdminOrbitState;
            RequireAdminCamera(turned.Dragging && MathF.Abs(turned.Yaw - start.Yaw) > 1 && MathF.Abs(turned.Pitch - start.Pitch) > 0.01f,
                "holding right click rotates both camera angles around the character");
            InputCommand after = Client.Prediction.CommandFor(Client.PredictedTick);
            RequireAdminCamera(Num.Vector3.Distance(NetConvert.View(before.ViewU, before.ViewV), NetConvert.View(after.ViewU, after.ViewV)) < 0.001f,
                "orbiting leaves the character's authoritative aim unchanged");
            RequireAdminCamera(!Me.Gun.Aimed && (after.Actions & (ActionButtons.Primary | ActionButtons.Secondary)) == 0,
                "inspection mouse buttons cannot fire or aim the weapon");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            await Seconds(0.2);
            RequireAdminCamera(!View.AdminOrbitState.Dragging, "releasing right mouse ends the drag");
            await Shot("admin-camera-front");

            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Seconds(0.15);
            RequireAdminCamera(View.AdminOrbitState.Dragging, "a fresh right press starts a new drag");
            View.Interface.Open(GroundScreen.Inventory);
            await Seconds(0.2);
            RequireAdminCamera(View.AdminThirdPerson && !View.AdminOrbitState.Dragging,
                "opening inventory cancels the drag while keeping the inspection camera active");
            View.Interface.Open(GroundScreen.Hud);
            await RepairDeviceInput();
            var menuAngle = View.AdminOrbitState;
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(60, 10), ButtonMask = MouseButtonMask.Right });
            await Seconds(0.2);
            RequireAdminCamera(!View.AdminOrbitState.Dragging && View.AdminOrbitState.Yaw == menuAngle.Yaw && View.AdminOrbitState.Pitch == menuAngle.Pitch,
                "closing inventory cannot resume a stale held-right-button drag");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            await Seconds(0.1);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Seconds(0.1);
            RequireAdminCamera(View.AdminOrbitState.Dragging, "releasing and pressing right mouse restores drag after the menu");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            await Seconds(0.1);

            int held = Me.HeldSlot;
            float speed = View.Console.Settings.NoclipSpeed;
            float distance = View.AdminOrbitState.Distance;
            View.Console.Run("noclip on");
            await Until(() => Client.Character.Has(MoveCheats.Noclip), 4);
            RequireAdminCamera(Client.Character.Has(MoveCheats.Noclip), "noclip is active for the wheel-routing check");
            CameraWheel(MouseButton.WheelDown);
            await Seconds(0.2);
            RequireAdminCamera(View.AdminOrbitState.Distance > distance + 0.1f, "wheel down zooms out");
            CameraWheel(MouseButton.WheelUp);
            await Seconds(0.2);
            RequireAdminCamera(MathF.Abs(View.AdminOrbitState.Distance - distance) < 0.001f, "wheel up zooms back in");
            RequireAdminCamera(Me.HeldSlot == held && View.Console.Settings.NoclipSpeed == speed,
                "camera zoom leaves the held slot and preferred noclip speed unchanged");
            View.Console.Run("noclip off");
            await Until(() => !Client.Character.Has(MoveCheats.Noclip), 4);
            await Seconds(0.5);

            Num.Vector3 beforeWalk = Client.RenderPosition;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = true });
            await Seconds(0.4);
            RequireAdminCamera(Num.Vector3.Distance(beforeWalk, Client.RenderPosition) > 0.25f && View.People.OwnPlayerVisible,
                "movement continues while the full local character is shown");
            await Shot("admin-camera-walk");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = false });
            await Seconds(0.4);

            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Ctrl, PhysicalKeycode = Key.Ctrl, Pressed = true });
            await Seconds(0.5);
            RequireAdminCamera(Client.Character.Stance == Stance.Crouching && View.People.OwnPlayerVisible,
                "the full character remains visible while crouching");
            await Shot("admin-camera-crouch");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Ctrl, PhysicalKeycode = Key.Ctrl, Pressed = false });
            await Seconds(0.4);

            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Seconds(0.15);
            View.Console.Run("tps off");
            await Seconds(0.25);
            RequireAdminCamera(!View.AdminThirdPerson && !View.People.OwnPlayerVisible && View.FirstPersonVisible,
                "the alias returns to first person and removes the local full body");
            InputCommand disabled = Client.Prediction.CommandFor(Client.PredictedTick);
            RequireAdminCamera(!Me.Gun.Aimed && (disabled.Actions & (ActionButtons.Primary | ActionButtons.PrimaryPressed | ActionButtons.Secondary)) == 0,
                "turning the camera off while holding both mouse buttons cannot start an attack or weapon aim");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            await Seconds(0.15);
            disabled = Client.Prediction.CommandFor(Client.PredictedTick);
            RequireAdminCamera((disabled.Actions & (ActionButtons.Primary | ActionButtons.PrimaryPressed | ActionButtons.Secondary)) == 0,
                "releasing right mouse leaves the held left button blocked from attacking");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            await Seconds(0.15);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Seconds(0.25);
            RequireAdminCamera(Me.Gun.Aimed, "a fresh right click restores first-person weapon aiming");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            await Seconds(0.25);
            await Shot("admin-camera-first-person");

            View.Console.Run("selfcamera on");
            await Seconds(0.15);
            RequireAdminCamera(View.AdminThirdPerson, "the selfcamera alias can enable inspection again");
            RequireAdminCamera(Command?.Invoke($"unadmin {Client.Name}") == true, "the server accepts revoking admin rights for the cleanup check");
            await Until(() => !Client.Console().IsAdmin, 5);
            await Seconds(0.25);
            RequireAdminCamera(!Client.Console().IsAdmin && !View.AdminThirdPerson && !View.People.OwnPlayerVisible,
                "losing admin rights closes the camera and removes the local full body");
            View.Console.Run("thirdperson on");
            await Seconds(0.15);
            RequireAdminCamera(!View.AdminThirdPerson, "a non-admin cannot enable the camera through its command");
            Command?.Invoke($"admin {Client.Name}");
            await Until(() => Client.Console().IsAdmin, 5);
            GD.Print("[admin-camera-check] PASS complete real-input self camera tour");
        }
        finally
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Ctrl, PhysicalKeycode = Key.Ctrl, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = false });
            View.Console.Settings.Sensitivity = sensitivity;
        }
    }

    private static void CameraWheel(MouseButton wheel)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wheel, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wheel, Pressed = false });
    }

    private static void RequireAdminCamera(bool condition, string requirement)
    {
        if (!condition) throw new InvalidOperationException("admin-camera-check: " + requirement);
        GD.Print("[admin-camera-check] PASS " + requirement);
    }
}
