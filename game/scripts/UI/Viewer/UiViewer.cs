using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.UI.Model;

namespace HollowUniverse.UI.Viewer;

/// <summary>
/// The interface viewer (<c>--view ui</c>, GROUND.md §8): the ground interface on <see cref="MockGroundUi"/> over a
/// plain backdrop, opened on one screen for a look or a screenshot. Options (after "--"):
/// <c>--screen hud|inventory|loot|corpse|furnace|campfire|crafting|death|wheel|hammer|menu|map|techtree|recycler|research</c>, and
/// <c>--pointer x,y</c> to hold the pointer at a design-canvas point (hover states, wheel sectors).
/// Screenshots hold the HUD's moments and the sample's clock still; without <c>--shot</c> the mock runs and every
/// screen is interactive. <c>--ui-check</c> drives the inventory and crafting with real input events, checks the
/// results and quits (exit code 0 when every check passes).
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
        else if (_shot && screen is not ("wheel" or "plan" or "hammer"))
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
                _ui.OpenWheel(WheelKind.Plan);
                // Pointing at the second sector, the triangle foundation, as in the reference.
                _ui.HoldPointer(new Vector2(913, 227));
                break;
            case "hammer":
                _ui.OpenWheel(WheelKind.Hammer);
                _ui.HoldPointer(new Vector2(800, 190));
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
        await Drag(Main(1), Point(700, 150));
        Expect(At(ContainerId.Main, 1).Amount == 1000, "released on a panel, a drag does nothing");
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
