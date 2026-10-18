using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.UI.Model;
using HollowUniverse.Settings;

namespace HollowUniverse.UI.Viewer;

/// <summary>
/// The interface viewer (<c>--view ui</c>, GROUND.md §8): the ground interface on <see cref="MockGroundUi"/> over a
/// plain backdrop, opened on one screen for a look or a screenshot. Options (after "--"):
/// <c>--screen hud|hud-crafting|hud-crafting-next|inventory|loot|corpse|furnace|campfire|crafting|crafting-free|death|wheel|hammer|options|ammo|menu|map|techtree|recycler|research</c>,
/// the F1 panel's <c>console|console-info|console-net|console-items|console-tools|console-tools-stone</c>, the F2 panel's
/// <c>developer|developer-players|developer-look|developer-draw|developer-log</c>, settings tabs
/// <c>settings|settings-display|settings-shadows|settings-crosshair</c>, and
/// <c>--pointer x,y</c> to hold the pointer at a design-canvas point (hover states, radial sectors), and
/// <c>--across &lt;m&gt;</c> to open the map or death screen's map that many metres across (150 m to the whole world).
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
    private bool _checking;
    private int _frames;

    public override void _Ready()
    {
        _shot = Options.ContainsKey("shot");
        _checking = Options.ContainsKey("ui-check") || Options.ContainsKey("crafting-hud-check") || Options.ContainsKey("crosshair-check");
        AddChild(new UiBackdrop { Name = "Backdrop" });
        var layer = new CanvasLayer { Name = "Interface" };
        AddChild(layer);
        _model = new MockGroundUi();
        _ui = new GroundUi { Name = "GroundUi", Model = _model, HoldEffects = _shot };
        layer.AddChild(_ui);
        _ui.Greet();
        Show(Options.TryGetValue("screen", out string? screen) ? screen : "hud");
        // The map control's zoom (map and death screens): metres across the map's shorter side, 150 to the whole world.
        if (Options.TryGetValue("across", out string? across) && double.TryParse(across, NumberStyles.Float, CultureInfo.InvariantCulture, out double metres))
        {
            _ui.ZoomMapTo(metres);
        }
        if (Options.ContainsKey("crafting-hud-check"))
        {
            RunCraftingHudCheck();
            return;
        }
        if (Options.ContainsKey("crosshair-check"))
        {
            RunCrosshairCheck();
            return;
        }
        if (Options.ContainsKey("ui-check"))
        {
            RunCheck();
            return;
        }
        if (Options.ContainsKey("window-check"))
        {
            CheckWindowModes();
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

    // Native smoke test, explicitly requested by the viewer flag; never writes graphics.cfg.
    private async void CheckWindowModes()
    {
        if (DisplayServer.GetName() == "headless" || Engine.IsEmbeddedInEditor())
        {
            GD.PrintErr("[window-check] requires a standalone native window");
            GetTree().Quit(1);
            return;
        }
        GraphicsValues original = GraphicsSettings.Current;
        DisplayServer.WindowMode originalMode = DisplayServer.WindowGetMode();
        Vector2I size = DisplayServer.WindowGetSize(), position = DisplayServer.WindowGetPosition();
        int failures = 0;
        foreach ((GameWindowMode setting, DisplayServer.WindowMode expected) in new[]
        {
            (GameWindowMode.Borderless, DisplayServer.WindowMode.Fullscreen),
            (GameWindowMode.ExclusiveFullscreen, DisplayServer.WindowMode.ExclusiveFullscreen),
            (GameWindowMode.Windowed, DisplayServer.WindowMode.Windowed),
        })
        {
            GraphicsSettings.ApplyGlobal(GetViewport(), original with { WindowMode = setting });
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            DisplayServer.WindowMode actual = DisplayServer.WindowGetMode();
            bool ok = actual == expected || expected == DisplayServer.WindowMode.Windowed && actual == DisplayServer.WindowMode.Maximized;
            if (!ok) failures++;
            GD.Print($"[window-check] {setting}: {actual}, size={DisplayServer.WindowGetSize()}, {(ok ? "PASS" : "FAIL")}");
        }
        GraphicsSettings.ApplyGlobal(GetViewport(), original);
        DisplayServer.WindowSetMode(originalMode);
        if (originalMode is DisplayServer.WindowMode.Windowed or DisplayServer.WindowMode.Maximized)
        {
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(position);
        }
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private void Show(string screen)
    {
        switch (screen)
        {
            case "hud-crafting" or "hud-crafting-next":
                PrepareCraftingHud(_model);
                _model.Tick(screen == "hud-crafting-next" ? 7 : 2);
                break;
            case "inventory":
                _ui.Open(GroundScreen.Inventory);
                _ui.Select(new SlotRef(ContainerId.Belt, 5));
                break;
            case "attachments":
                _model.AttachmentSample(Hollow.Simulation.Content.GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground")));
                _model.Move(new SlotRef(ContainerId.Main, 1), new SlotRef(ContainerId.Main, 0, 0), 1);
                _ui.Open(GroundScreen.Inventory);
                _ui.Select(new SlotRef(ContainerId.Main, 0));
                break;
            case "name":
                var nameDialog = new HollowUniverse.Ground.Keypad();
                _ui.GetParent().AddChild(nameDialog);
                nameDialog.AskText("NAME", "East coast camp");
                break;
            case "target-health":
                _model.TargetHealth = new TargetHealthView("Large wooden storage box", .42f);
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
            case "crafting-free":
                _model.Console?.Run("freebuild on");
                _model.Workbench = 0;
                _ui.SelectRecipe("rifle");
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
            case "settings" or "settings-display" or "settings-shadows" or "settings-crosshair":
                _ui.ShowSettings(screen == "settings-crosshair" ? 4 : screen == "settings-shadows" ? 3 : screen == "settings-display" ? 2 : 0);
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
            case "console" or "console-info" or "console-net" or "console-items" or "console-tools" or "console-tools-stone":
                // The F1 panel (GROUND.md §8.2) on sample data.
                _model.HudSample();
                if (screen == "console-tools-stone")
                {
                    _model.Console?.Run("freebuild on");
                    _model.Console?.Run("bgrade stone");
                }
                _ui.ToggleConsole();
                _ui.ConsoleTab = screen switch
                {
                    "console-info" => ConsoleTab.Info,
                    "console-net" => ConsoleTab.Net,
                    "console-items" => ConsoleTab.Items,
                    "console-tools" or "console-tools-stone" => ConsoleTab.Tools,
                    _ => ConsoleTab.Console,
                };
                break;
            case "developer" or "developer-players" or "developer-look" or "developer-draw" or "developer-log":
                // The F2 panel (GROUND.md §8.5) on sample data.
                _model.HudSample();
                _ui.ToggleDeveloper();
                _ui.DeveloperTab = screen switch
                {
                    "developer-players" => DeveloperTab.Players,
                    "developer-look" => DeveloperTab.Look,
                    "developer-draw" => DeveloperTab.Draw,
                    "developer-log" => DeveloperTab.Log,
                    _ => DeveloperTab.Server,
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

        // Exercise the actual client-to-UI action mapping; the mock's fixed action list cannot catch duplicate
        // legacy REMOVE buttons beside the new draggable attachment sockets.
        var liveContent = Hollow.Simulation.Content.GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
        using (var actionClient = new Hollow.Client.GameClient(new Hollow.Client.ClientOptions { Name = "UiActions", Content = liveContent }))
        using (var liveUi = new HollowUniverse.Ground.ClientGroundUi(actionClient))
        {
            var rifle = Hollow.Simulation.Items.ItemStack.Of(liveContent.ItemByKey("rifle"), 1);
            rifle.Sight = liveContent.ItemByKey("scope").Id;
            rifle.Loaded = 9;
            rifle.LoadedAmmo = liveContent.ItemByKey("rifle-ammo").Id;
            actionClient.Local.Inventory.Main.Set(0, rifle);
            var slot = new SlotRef(ContainerId.Main, 0);
            var raw = new List<Hollow.Simulation.Items.ItemActionOption>();
            actionClient.Local.ItemActions(Hollow.Simulation.Items.ContainerRef.Main, 0, raw);
            Expect(raw.Any(o => o.Action.Key == "remove") && liveUi.Attachments(slot).Any(s => s.Stack.Key == "scope"),
                "the real client retains legacy detach compatibility and exposes its fitted socket");
            IReadOnlyList<ItemActionView> mapped = liveUi.Actions(slot);
            Expect(raw.Any(o => o.Action.Key == "unload") && raw.Where(o => o.Action.Key == "remove").All(o => mapped.All(v => v.Label != o.Label)) &&
                raw.Where(o => o.Action.Key != "remove").All(o => mapped.Any(v => v.Label == o.Label)),
                "real socket UI omits only redundant REMOVE actions and preserves UNLOAD and other actions");
        }

        _ui.Open(GroundScreen.Inventory);
        await Frames(6);
        float figureYaw = _ui.CharacterYaw;
        await Drag(Point(280, 350), Point(380, 350));
        Expect(System.Math.Abs(_ui.CharacterYaw - figureYaw) > 0.8f, "left drag rotates the inventory's 3D figure");
        float releasedYaw = _ui.CharacterYaw;
        await MoveTo(Point(450, 350));
        Expect(_ui.CharacterYaw == releasedYaw, "the inventory figure stops rotating on mouse release");
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
        // An admin can use the same crafting controls with no bench or resources; turning it off restores the gate.
        int bench = _model.Workbench;
        _model.Workbench = 0;
        _model.Console?.Run("freebuild on");
        _ui.SelectRecipe("rifle");
        await Frames(3);
        int metalBefore = _model.Have("metal.refined");
        int woodBefore = _model.Have("wood");
        await Click(Point(1016, 730));
        await Click(Point(1427, 730));
        CraftJob freeJob = _model.Queue.Last();
        Expect(freeJob.Key == "rifle" && freeJob.Count == 999 && _model.Queue.Count == queued + 2,
            "free crafting enables a workbench-three recipe and MAX selects 999");
        Expect(_model.Have("metal.refined") == metalBefore && _model.Have("wood") == woodBefore,
            "free crafting consumes no ingredients");
        _model.Console?.Run("freebuild off");
        _model.CancelCraft(freeJob.Ticket);
        Expect(_model.Have("metal.refined") == metalBefore && _model.Have("wood") == woodBefore,
            "cancelling a free craft after switching off does not refund unspent resources");
        await Frames(3);
        await Click(Point(1427, 730));
        Expect(_model.Queue.Count == queued + 1, "turning off free crafting restores recipe requirements");
        _model.Workbench = bench;
        await Key(Godot.Key.Tab);
        Expect(_ui.Screen == GroundScreen.Hud, "Tab closes the screen");

        // H belongs to the open inventory, not to the rectangle under the pointer.
        _model.OpenLoot(ContainerKind.Box);
        _ui.Open(GroundScreen.Inventory);
        await Frames(3);
        int firstLoot = System.Linq.Enumerable.Range(0, _model.Slots(ContainerId.Loot).Count).First(i => !At(ContainerId.Loot, i).IsEmpty);
        // Use the real slot hitboxes, so this also checks the active layout.
        await Hold(Godot.Key.H, true);
        await MoveTo(Point(25, 350));
        Expect(_ui.HoverLootHeld, "H stays active outside inventory and loot panels");
        await MoveTo(_ui.SlotPoint(new SlotRef(ContainerId.Loot, firstLoot)));
        await ToSignal(GetTree().CreateTimer(0.45), SceneTreeTimer.SignalName.Timeout);
        Expect(At(ContainerId.Loot, firstLoot).IsEmpty, "returning to a loot slot transfers while H is still held");
        await Hold(Godot.Key.H, false);
        Expect(!_ui.HoverLootHeld, "H release ends hover loot");
        await Hold(Godot.Key.H, true);
        _ui.Open(GroundScreen.Hud);
        Expect(!_ui.HoverLootHeld, "closing the inventory ends hover loot");
        await Hold(Godot.Key.H, false);

        // Real socket capabilities and the production atomic rule behind the UI model.
        _model.AttachmentSample(Hollow.Simulation.Content.GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground")));
        _ui.Open(GroundScreen.Inventory);
        _ui.Select(new SlotRef(ContainerId.Main, 0));
        await Frames(4);
        Vector2 Socket(int i) => Point(593 + i * 99, 410);
        Expect(_model.Attachments(new SlotRef(ContainerId.Main, 0)).Count == 3, "the rifle exposes its three supported attachment sockets");
        await Drag(Main(1), Socket(0));
        Expect(At(ContainerId.Main, 1).IsEmpty && At(ContainerId.Main, 0).Sight == "scope", "dragging a sight into its socket attaches it");
        await Drag(Socket(0), Main(6));
        Expect(At(ContainerId.Main, 6).Key == "scope" && At(ContainerId.Main, 0).Sight == null, "dragging out of a socket uses the exact inventory destination");
        await Drag(Main(2), Socket(0));
        await Drag(Main(6), Socket(0));
        Expect(At(ContainerId.Main, 6).Key == "holo-sight" && At(ContainerId.Main, 0).Sight == "scope", "a filled socket swaps its attachment back to the source slot");
        await Drag(Main(3), Socket(0));
        Expect(At(ContainerId.Main, 3).Key == "silencer" && At(ContainerId.Main, 0).Sight == "scope", "an incompatible socket leaves both items intact");
        await Drag(Main(3), Socket(1));
        await Drag(Socket(1), Point(1077, 295));
        Expect(At(ContainerId.Loot, 0).Key == "silencer" && At(ContainerId.Main, 0).Muzzle == null, "a socket can detach directly into open storage");
        await Drag(Socket(0), Point(1300, 150));
        Expect(At(ContainerId.Main, 0).Key == "rifle" && At(ContainerId.Main, 0).Sight == "scope", "releasing an attachment outside slots cancels without dropping its weapon");
        await MoveTo(Main(6));
        await Mouse(Main(6), MouseButton.Left, true);
        await MoveTo(Socket(0));
        _model.Move(new SlotRef(ContainerId.Main, 0), new SlotRef(ContainerId.Main, 10), 1);
        await Frames(3);
        await Mouse(Socket(0), MouseButton.Left, false);
        Expect(At(ContainerId.Main, 6).Key == "holo-sight" && At(ContainerId.Main, 10).Sight == "scope", "a changed selected weapon cancels the pending attachment drag");
        _ui.Open(GroundScreen.Hud);

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

        // The map (docs/WHOLE_WORLD.md §11): a click puts a marker down where it was clicked (the projection's inverse), a
        // right-click takes it off, a drag turns the globe away from the player.
        _model.WorldSample();
        _ui.Open(GroundScreen.Map);
        await Frames(4);
        int Markers() => _model.MapMarkers.Count(m => m.Kind == MapMarkerKind.Marker);
        int markers = Markers();
        Rect2 map = _ui.MapScreenArea;
        Vector2 spot = (map.Position + map.Size * new Vector2(0.3f, 0.62f)).Round();
        string? cell = _ui.MapCellAt(spot);
        await Click(spot);
        MapMarkerView? put = _model.MapMarkers.LastOrDefault(m => m.Kind == MapMarkerKind.Marker);
        Expect(Markers() == markers + 1 && cell != null && put != null &&
            Hollow.Surface.WorldGrid.Name(new Hollow.Core.Vector3D(put.At.X, put.At.Y, put.At.Z), _model.Map!.Cells) == cell,
            $"a click on the map puts a marker down where it was clicked ({cell})");
        await Click(spot, MouseButton.Right);
        Expect(Markers() == markers, "a right-click on it takes it off");
        Expect(_ui.MapFollowing, "the map follows the player");
        await Drag(spot, spot + new Vector2(140, 60));
        Expect(!_ui.MapFollowing && Markers() == markers, "a drag turns the globe (no marker put down)");
        await Key(Godot.Key.G);
        Expect(_ui.Screen == GroundScreen.Hud, "G closes the map");

        // The same interaction modal handles plain text without the code lock's four-digit auto-submit.
        _ui.Hide();
        var dialog = new HollowUniverse.Ground.Keypad();
        _ui.GetParent().AddChild(dialog);
        var submitted = new List<string>();
        dialog.Entered += submitted.Add;
        async Task TypeText(string value)
        {
            foreach (Rune rune in value.EnumerateRunes())
            {
                Input.ParseInputEvent(new InputEventKey { Unicode = (uint)rune.Value, Pressed = true });
                await Frames();
                Input.ParseInputEvent(new InputEventKey { Unicode = (uint)rune.Value, Pressed = false });
            }
            await Frames();
        }
        dialog.AskText("NAME");
        await Frames();
        await TypeText("1234");
        Expect(dialog.Visible && submitted.Count == 0 && dialog.GetNode<LineEdit>("TextEntry").Text == "1234", "four typed digits remain editable in a name");
        await Key(Godot.Key.Enter);
        Expect(!dialog.Visible && submitted.SequenceEqual(new[] { "1234" }), "Enter explicitly submits a name");
        dialog.AskText("NAME", "Old name");
        await Frames();
        await TypeText("Café 東");
        await Key(Godot.Key.Enter);
        Expect(submitted.LastOrDefault() == "Café 東", "Unicode input replaces selected text and survives submission");
        dialog.AskText("NAME");
        await Key(Godot.Key.Enter);
        Expect(dialog.Visible && submitted.Count == 2, "empty names keep the dialog open");
        await Key(Godot.Key.Escape);
        Expect(!dialog.Visible && submitted.Count == 2, "Escape cancels without sending an interaction");
        dialog.QueueFree();

        _ui.Show();
        _model.OpenLoot(ContainerKind.Box);
        _ui.Open(GroundScreen.Inventory);
        ItemStack[] keptBelt = _model.Slots(ContainerId.Belt).ToArray();
        ItemStack[] keptWear = _model.Slots(ContainerId.Wear).ToArray();
        ItemStack[] keptLoot = _model.Slots(ContainerId.Loot).ToArray();
        Expect(_model.Slots(ContainerId.Main).Any(s => !s.IsEmpty), "DROP ALL fixture has backpack contents");
        int selectedMain = Enumerable.Range(0, _model.Slots(ContainerId.Main).Count).First(i => !At(ContainerId.Main, i).IsEmpty);
        _ui.Select(new SlotRef(ContainerId.Main, selectedMain));
        await Frames(3);
        await Click(Point(967, 457), MouseButton.Right);
        Expect(_model.Slots(ContainerId.Main).Any(s => !s.IsEmpty), "right-click does not activate DROP ALL");
        await Click(Point(967, 457));
        Expect(_model.Slots(ContainerId.Main).All(s => s.IsEmpty) && _ui.SelectedSlot == null,
            "DROP ALL empties the backpack and clears its item panel");
        Expect(_model.Slots(ContainerId.Belt).SequenceEqual(keptBelt) && _model.Slots(ContainerId.Wear).SequenceEqual(keptWear),
            "DROP ALL keeps the hotbar and worn equipment");
        Expect(_model.Slots(ContainerId.Loot).SequenceEqual(keptLoot), "DROP ALL leaves open storage untouched");
        await Click(Point(967, 457));
        Expect(_model.Slots(ContainerId.Main).All(s => s.IsEmpty) && _model.Slots(ContainerId.Belt).SequenceEqual(keptBelt),
            "empty-backpack DROP ALL cannot drop hotbar items");
        _ui.Hide();

        await CheckCraftingHud(Expect);
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
        if (!_shot && !_checking)
        {
            _model.Tick(delta);
        }
    }
}
