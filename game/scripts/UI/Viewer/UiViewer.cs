using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.UI.Model;

namespace HollowUniverse.UI.Viewer;

/// <summary>
/// The interface viewer (<c>--view ui</c>, GROUND.md §8): the ground interface on <see cref="MockGroundUi"/> over a
/// plain backdrop, opened on one screen for a look or a screenshot. Options (after "--"):
/// <c>--screen hud|inventory|loot|corpse|furnace|campfire|crafting|death|wheel|hammer|options|ammo|menu|map|techtree|recycler|research</c>,
/// the F1 panel's <c>console|console-info|console-net|console-items|console-tools</c>, and
/// <c>--pointer x,y</c> to hold the pointer at a design-canvas point (hover states, radial sectors).
/// Screenshots hold the HUD's moments and the sample's clock still; without <c>--shot</c> the mock runs and every
/// screen is interactive (on the HUD the right mouse opens the held plan's or hammer's radial, E held the sample door's
/// options, R held with the pistol its rounds). <c>--ui-check</c> drives the inventory, crafting and the radial with
/// real input events, checks the results and quits (exit code 0 when every check passes).
/// </summary>
public partial class UiViewer : Node
{
    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>For automated screenshots: fonts, icons and the character have had a few frames to settle.</summary>
    public bool ReadyForShot => _frames > 12 && _ui.MapReady;

    private MockGroundUi _model = null!;
    private GroundUi _ui = null!;
    private bool _shot;
    private int _frames;

    public override void _Ready()
    {
        _shot = Options.ContainsKey("shot");
        AddChild(new UiBackdrop { Name = "Backdrop" });
        var layer = new CanvasLayer { Name = "Interface" };
        AddChild(layer);
        _model = new MockGroundUi();
        _ui = new GroundUi { Name = "GroundUi", Model = _model, HoldEffects = _shot };
        layer.AddChild(_ui);
        Show(Options.TryGetValue("screen", out string? screen) ? screen : "hud");
        if (Options.ContainsKey("ui-check"))
        {
            RunCheck();
            return;
        }
        if (Options.TryGetValue("pointer", out string? pointer))
        {
            string[] xy = pointer.Split(',');
            _ui.HoldPointer(new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture)));
        }
        else if (_shot && screen is not ("wheel" or "plan" or "hammer" or "options" or "ammo"))
        {
            // Wherever the real mouse happens to be, nothing in a screenshot is hovered.
            _ui.HoldPointer(new Vector2(-10000, -10000));
        }
    }

    private void Show(string screen)
    {
        switch (screen)
        {
            case "inventory":
                _ui.Open(GroundScreen.Inventory);
                _ui.Select(new SlotRef(ContainerId.Belt, 5));
                break;
            case "loot":
                Loot(ContainerKind.Box, new SlotRef(ContainerId.Belt, 5));
                break;
            case "corpse":
                Loot(ContainerKind.Corpse, new SlotRef(ContainerId.Main, 7));
                break;
            case "furnace":
                Loot(ContainerKind.Furnace, new SlotRef(ContainerId.Main, 4));
                break;
            case "campfire":
                Loot(ContainerKind.Campfire, new SlotRef(ContainerId.Main, 15));
                break;
            case "crafting":
                _ui.SelectRecipe("bandage");
                _ui.Open(GroundScreen.Crafting);
                break;
            case "death":
                _model.Die();
                break;
            case "wheel" or "plan":
                _ui.OpenRadial(_model.PlanRadial, RadialInput.Aim);
                // Pointing at the second sector, the triangle foundation, as in the reference.
                _ui.HoldPointer(new Vector2(913, 227));
                break;
            case "hammer":
                _ui.OpenRadial(_model.HammerRadial, RadialInput.Aim);
                _ui.HoldPointer(new Vector2(800, 190));
                break;
            case "options":
                // The same radial on another source (GROUND_SYSTEMS.md §4): the sample door's options, as E held opens
                // them; the pointer on LOCK, which says why it can't be chosen yet.
                _ui.OpenRadial(_model.DoorRadial, RadialInput.Interact);
                _ui.HoldPointer(new Vector2(800, 690));
                break;
            case "ammo":
                // R held with the pistol: the rounds carried, as item icons; the pointer on the high-velocity ones.
                _ui.OpenRadial(_model.AmmoRadial, RadialInput.Reload);
                _ui.HoldPointer(new Vector2(1010, 570));
                break;
            case "menu":
                _ui.ShowMenu(inGame: false);
                break;
            case "map":
                // G7: the map with markers, teammates and the team panel.
                _model.WorldSample();
                _ui.Open(GroundScreen.Map);
                break;
            case "techtree":
                _model.WorldSample();
                _ui.SelectTechNode("metal-pickaxe");
                _ui.OpenTechTree();
                break;
            case "recycler":
                _model.OpenMachine(ContainerKind.Recycler);
                _ui.Open(GroundScreen.Inventory);
                _ui.Select(new SlotRef(ContainerId.Loot, 2));
                break;
            case "research":
                _model.OpenMachine(ContainerKind.ResearchTable);
                _ui.Open(GroundScreen.Inventory);
                _ui.Select(new SlotRef(ContainerId.Loot, 2));
                break;
            case "console" or "console-info" or "console-net" or "console-items" or "console-tools":
                // The F1 panel (GROUND.md §8.2) on sample data.
                _model.HudSample();
                _ui.ToggleConsole();
                _ui.ConsoleTab = screen switch
                {
                    "console-info" => ConsoleTab.Info,
                    "console-net" => ConsoleTab.Net,
                    "console-items" => ConsoleTab.Items,
                    "console-tools" => ConsoleTab.Tools,
                    _ => ConsoleTab.Console,
                };
                break;
            default:
                _model.HudSample();
                _model.RaiseSamples();
                break;
        }
    }

    // ---------------------------------------------------------------- --ui-check

    private async void RunCheck()
    {
        var failures = new List<string>();
        int checks = 0;
        void Expect(bool ok, string what)
        {
            checks++;
            GD.Print($"ui-check: {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
            {
                failures.Add(what);
            }
        }
        ItemStack At(ContainerId container, int index) => _model.Slots(container)[index];
        Vector2 Point(float x, float y) => _ui.CanvasPoint(new Vector2(x, y));
        Vector2 Main(int i) => Point(550 + i % 6 * 80 + 37, 476 + i / 6 * 80 + 37);

        // GROUND_SYSTEMS.md §7: every held behaviour has its control (the registry won't seal otherwise).
        bool controls;
        try
        {
            controls = HollowUniverse.Ground.HeldControls.Create().Problems.Count == 0;
        }
        catch (System.InvalidOperationException)
        {
            controls = false;
        }
        Expect(controls, "every held behaviour has its control");

        _ui.Open(GroundScreen.Inventory);
        await Frames(6);
        await Click(Main(0));
        Expect(_ui.SelectedSlot == new SlotRef(ContainerId.Main, 0), "a click selects the slot");
        await Drag(Main(0), Main(20));
        Expect(At(ContainerId.Main, 0).IsEmpty && At(ContainerId.Main, 20) == new ItemStack("wood", 1000), "a drag moves the whole stack");
        await Drag(Main(2), Main(21), MouseButton.Middle);
        Expect(At(ContainerId.Main, 2).Amount == 500 && At(ContainerId.Main, 21).Amount == 500, "a middle drag moves half");
        await Click(Point(550 + 5 * 80 + 37, 802 + 37), MouseButton.Right);
        Expect(At(ContainerId.Belt, 5).IsEmpty && _model.Have("bandage") == 5, "a right-click moves a belt stack to the main slots");
        await Drag(Main(20), Point(1300, 150));
        Expect(At(ContainerId.Main, 20).IsEmpty && _model.Have("wood") == 1000, "released outside the panels, a drag drops");
        // GROUND_SYSTEMS.md §7: released anywhere but a slot (the item panel here), a drag drops; on its own slot it is put back.
        // (The sample's metal ore: the wood stays for the craft below.)
        ItemStack onPanel = At(ContainerId.Main, 4);
        int had = _model.Have(onPanel.Key);
        await Drag(Main(4), Point(700, 150));
        Expect(At(ContainerId.Main, 4).IsEmpty && _model.Have(onPanel.Key) == had - onPanel.Amount, "released on a panel, a drag drops");
        ItemStack own = At(ContainerId.Main, 2);
        await MoveTo(Main(2));
        await Mouse(Main(2), MouseButton.Left, true);
        await MoveTo(Point(1300, 150));
        await MoveTo(Main(2));
        await Mouse(Main(2), MouseButton.Left, false);
        Expect(At(ContainerId.Main, 2) == own, "released on its own slot, a drag is put back");
        await Click(Main(21));
        await Click(Point(552 + 205, 427));
        await Drag(Point(996, 413), Main(22));
        int left = At(ContainerId.Main, 21).Amount, split = At(ContainerId.Main, 22).Amount;
        Expect(left + split == 500 && split > 200 && split < 300, $"the split slider and icon move part of a stack ({left} + {split})");
        int queued = _model.Queue.Count;
        await Key(Godot.Key.Q);
        Expect(_ui.Screen == GroundScreen.Crafting, "Q opens crafting");
        await Click(Point(1368 + 59, 710 + 20));
        Expect(_model.Queue.Count == queued + 1, "CRAFT queues the recipe");
        await Key(Godot.Key.Tab);
        Expect(_ui.Screen == GroundScreen.Hud, "Tab closes the screen");

        // The radial (GROUND_SYSTEMS.md §4): the plan's on the right mouse; the sample door's on E held, tapped, chosen,
        // refused, cancelled by Esc and by the right mouse.
        await Key(Godot.Key.Key3);
        Vector2 middle = (_ui.Size / 2).Floor();
        await Mouse(middle, MouseButton.Right, true);
        Expect(_ui.Radial == _model.PlanRadial, "the right mouse with the plan in hand opens its radial");
        await MoveTo(_ui.RadialPoint(8));
        await Mouse(_ui.RadialPoint(8), MouseButton.Right, false);
        Expect(_ui.Radial == null && _model.PlanPiece == 8, "released on the wall, the plan takes the wall");
        await Key(Godot.Key.E);
        Expect(_ui.Radial == null && _model.Door.Open, "a tap of E does the door's first option (OPEN DOOR)");
        await HoldRadial(Godot.Key.E);
        Expect(_ui.Radial == _model.DoorRadial, "E held 0.35 s opens the door's options");
        await MoveTo(_ui.RadialPoint(2));
        await Hold(Godot.Key.E, false);
        Expect(_ui.Radial == null && !_model.Door.Locked, "released on LOCK before a code is set, nothing happens");
        await HoldRadial(Godot.Key.E);
        await MoveTo(_ui.RadialPoint(1));
        await Hold(Godot.Key.E, false);
        Expect(_model.Door.CodeSet, "released on SET CODE, the code is set");
        await HoldRadial(Godot.Key.E);
        await MoveTo(_ui.RadialPoint(2));
        await Key(Godot.Key.Escape);
        await Hold(Godot.Key.E, false);
        Expect(_ui.Radial == null && !_model.Door.Locked && _ui.Screen == GroundScreen.Hud, "Esc closes it, and the release chooses nothing");
        await HoldRadial(Godot.Key.E);
        await MoveTo(_ui.RadialPoint(2));
        await Mouse(_ui.RadialPoint(2), MouseButton.Right, true);
        await Mouse(_ui.RadialPoint(2), MouseButton.Right, false);
        await Hold(Godot.Key.E, false);
        Expect(_ui.Radial == null && !_model.Door.Locked, "the right mouse cancels it too");
        await HoldRadial(Godot.Key.E);
        await MoveTo(_ui.RadialPoint(2));
        await Hold(Godot.Key.E, false);
        Expect(_model.Door.Locked, "with a code, released on LOCK, the door locks");

        GD.Print($"ui-check: {checks - failures.Count}/{checks} passed");
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    private async Task Frames(int count = 2)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task MoveTo(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Frames();
    }

    private async Task Mouse(Vector2 at, MouseButton button, bool pressed)
    {
        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = button, Pressed = pressed });
        await Frames();
    }

    private async Task Click(Vector2 at, MouseButton button = MouseButton.Left)
    {
        await MoveTo(at);
        await Mouse(at, button, true);
        await Mouse(at, button, false);
    }

    private async Task Drag(Vector2 from, Vector2 to, MouseButton button = MouseButton.Left)
    {
        await MoveTo(from);
        await Mouse(from, button, true);
        await MoveTo(from.Lerp(to, 0.5f));
        await MoveTo(to);
        await Mouse(to, button, false);
    }

    private async Task Hold(Key key, bool down)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = down });
        await Frames();
    }

    // Holds a key down past the radial's hold time.
    private async Task HoldRadial(Key key)
    {
        await Hold(key, true);
        await ToSignal(GetTree().CreateTimer(MockGroundUi.HoldSeconds + 0.15), SceneTreeTimer.SignalName.Timeout);
        await Frames();
    }

    private async Task Key(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        await Frames();
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        await Frames();
    }

    private void Loot(ContainerKind kind, SlotRef selected)
    {
        _model.OpenLoot(kind);
        _ui.Open(GroundScreen.Inventory);
        _ui.Select(selected);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (!_shot)
        {
            _model.Tick(delta);
        }
    }
}
