using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Items;
using HollowUniverse.Characters;
using HollowUniverse.UI;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // Real keyboard/mouse coverage on an isolated hosted save: --tour-steps admire.
    // Uses the player's existing bindings without changing or saving any of them.
    private async Task AdmireTour()
    {
        await Until(() => Client.Console().IsAdmin, 10);
        RequireAdmire(Client.Console().IsAdmin, "the hosted fixture has admin rights for item setup");
        await RepairGod(true);
        var (spot, towards) = OpenSpot(Offset(35, 20));
        await TeleportTo(spot);
        Face(spot + towards * 12 + Client.RenderUp * Client.EyeHeight);
        await Settle();
        Admin("give", "rifle 1");
        Admin("give", "rifle-ammo 90");
        if (Count("rock") == 0) Admin("give", "rock 1");
        await Until(() => Count("rifle") > 0 && Count("rifle-ammo") > 0 && Count("rock") > 0, 5);
        await Hold("rifle", 5);
        await Until(() => Me.Gun.Ready, 5);
        Me.Gun.Reload();
        await Until(() => Me.Gun.Loaded > 0 && !Me.Gun.Reloading && Me.Gun.PendingCount == 0 && Me.PendingCount == 0, 8);
        RequireAdmire(Me.Gun.Loaded > 0 && !Me.Gun.Reloading, "the rifle is loaded and ready before inspecting");
        // Reload prediction finishes before the authoritative inventory snapshot arrives.
        // Settle that transaction before testing that a cosmetic action changes nothing.
        await Until(() => Count("rifle-ammo") == Me.Gun.Reserve &&
            Me.Inventory.Belt[Me.HeldSlot].Loaded == Me.Gun.Loaded, 5);
        RequireAdmire(Count("rifle-ammo") == Me.Gun.Reserve,
            "authoritative inventory has settled after the fixture reload");
        View.Interface.Open(GroundScreen.Hud);
        await RepairDeviceInput();

        FirstPersonView hands = View.GetNode<FirstPersonView>("FirstPerson");
        ViewmodelView Arms() => hands.HeldModel?.GetParent() as ViewmodelView
            ?? throw new InvalidOperationException("admire-check: no held viewmodel");
        ItemStack[] Inventory() => Me.Inventory.All.SelectMany(container => container.Slots.ToArray()).ToArray();
        async Task Start(string requirement)
        {
            await Until(() => Arms().ActiveAction == "idle" && !Me.Gun.Reloading, 5);
            await RepairDeviceInput();
            await AdmireKey(Key.N);
            RequireAdmire(View.Admiring, requirement);
        }

        try
        {
            ItemStack[] before = Inventory();
            int rounds = Me.Gun.Loaded, reserve = Me.Gun.Reserve;
            int slot = Me.HeldSlot;
            await Start("a real N press starts admiration of a ready rifle");
            await Seconds(0.5);
            float phase = Arms().ActivePhase;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.N, PhysicalKeycode = Key.N, Pressed = true, Echo = true });
            await Seconds(0.08);
            AdmireRelease(Key.N);
            RequireAdmire(View.Admiring && Arms().ActivePhase >= phase,
                "keyboard repeat cannot restart the admire timeline");
            await Until(() => Arms().ActivePhase >= 0.42f, 2);
            RequireAdmire(View.Admiring, "rifle admiration reaches its middle pose");
            await Shot("admire-rifle", frames: 0);
            await Until(() => !View.Admiring, 3);
            RequireAdmire(!View.Admiring && Arms().ActiveAction == "idle", "admiration finishes automatically and returns to idle");
            RequireAdmire(Me.HeldSlot == slot && Me.Gun.Loaded == rounds && Me.Gun.Reserve == reserve && before.SequenceEqual(Inventory()),
                "admiring changes no held slot, ammunition, item count, condition or attachment");

            await Start("admiration can be started again after completion");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            await Until(() => !View.Admiring && Me.Gun.Aimed, 1);
            RequireAdmire(!View.Admiring && Me.Gun.Aimed, "right mouse cancels admire and immediately restores ordinary aiming");
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            await Seconds(0.4);

            await Start("admiration starts before the attack cancellation check");
            rounds = Me.Gun.Loaded;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            await Until(() => !View.Admiring && Me.Gun.Loaded < rounds, 1);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            RequireAdmire(!View.Admiring && Me.Gun.Loaded < rounds, "left mouse cancels admire and fires through the normal weapon path");
            await Until(() => Me.Gun.PendingCount == 0, 4);
            await Seconds(0.3);

            await Start("admiration starts before the reload cancellation check");
            await AdmireKey(Key.R);
            await Until(() => Me.Gun.Reloading, 2);
            RequireAdmire(Me.Gun.Reloading && !View.Admiring, "R cancels admire and starts the ordinary reload");
            await AdmireKey(Key.N);
            RequireAdmire(Me.Gun.Reloading && !View.Admiring, "N cannot interrupt or replace an active reload");
            await Until(() => !Me.Gun.Reloading && Me.Gun.PendingCount == 0, 8);

            await Start("admiration starts before opening inventory");
            View.Interface.Open(GroundScreen.Inventory);
            await Seconds(0.15);
            RequireAdmire(!View.Admiring, "opening inventory cancels admiration");
            await AdmireKey(Key.N);
            RequireAdmire(!View.Admiring, "inventory consumes N without starting admiration");
            View.Interface.Open(GroundScreen.Hud);
            await RepairDeviceInput();
            View.Interface.Open(GroundScreen.Menu);
            await AdmireKey(Key.N);
            RequireAdmire(!View.Admiring, "the pause menu blocks admiration");
            View.Interface.Open(GroundScreen.Hud);
            await RepairDeviceInput();

            await Start("admiration starts before the free-look check");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Alt, PhysicalKeycode = Key.Alt, Pressed = true });
            await Seconds(0.15);
            RequireAdmire(!View.Admiring, "holding Alt cancels admiration for free look");
            await AdmireKey(Key.N);
            RequireAdmire(!View.Admiring, "N cannot start admiration during free look");
            AdmireRelease(Key.Alt);
            await Seconds(0.2);

            int rifleSlot = Me.HeldSlot;
            await Hold("rock", 0);
            int rockSlot = Me.HeldSlot;
            await Hold("rifle", rifleSlot);
            await Until(() => Me.Gun.Ready, 4);
            await Start("admiration starts before changing held items");
            await AdmireKey((Key)((long)Key.Key1 + rockSlot));
            RequireAdmire(Me.HeldSlot == rockSlot && !View.Admiring, "a belt number switches to the rock and cancels the rifle inspection");
            await Seconds(0.5);
            await Start("a real N press also admires the starting rock");
            await Until(() => Arms().ActivePhase >= 0.42f, 2);
            RequireAdmire(View.Admiring, "rock admiration reaches its middle pose");
            await Shot("admire-rock", frames: 0);
            await AdmireKey((Key)((long)Key.Key1 + rockSlot));
            RequireAdmire(Me.HeldSlot == -1 && !View.Admiring, "pressing the active belt number holsters and cancels admiration");
            await AdmireKey(Key.N);
            RequireAdmire(!View.Admiring && Me.HeldItem == null, "empty hands refuse admiration");
            GD.Print("[admire-check] PASS complete real-input held-item admiration tour");
        }
        finally
        {
            foreach (Key key in new[] { Key.N, Key.R, Key.Alt, Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6 }) AdmireRelease(key);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            View.Scripted = null;
            View.Interface.Open(GroundScreen.Hud);
        }
    }

    private async Task AdmireKey(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Seconds(0.08);
        AdmireRelease(key);
        await Seconds(0.08);
    }

    private static void AdmireRelease(Key key) => Input.ParseInputEvent(new InputEventKey
        { Keycode = key, PhysicalKeycode = key, Pressed = false });

    private static void RequireAdmire(bool condition, string requirement)
    {
        if (!condition) throw new InvalidOperationException("admire-check: " + requirement);
        GD.Print("[admire-check] PASS " + requirement);
    }
}
