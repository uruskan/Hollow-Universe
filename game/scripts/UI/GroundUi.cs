using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;

namespace HollowUniverse.UI;

/// <summary>The ground interface's screens; the HUD shows under the inventory and crafting screens too.</summary>
public enum GroundScreen
{
    Hud,
    Inventory,
    Crafting,
    Death,
    Menu,

    /// <summary>G7: the map of the whole world with the markers and the team (G, as in Rust).</summary>
    Map,

    /// <summary>G7: the tech tree of the workbench in reach.</summary>
    TechTree,

    /// <summary>The F1 panel: the console and Rust's other F1 tabs (GROUND.md §8.2).</summary>
    Console,

    /// <summary>The F2 panel: the developer's window on the game (GROUND.md §8.5).</summary>
    Developer,
}

/// <summary>
/// The ground game's interface (GROUND.md §8): RustMVP's interface, matched exactly, on top of an
/// <see cref="IGroundUi"/> view model. It is painted in immediate mode, as RustMVP's was, on a 1600 × 900 design
/// canvas: every design length goes through <see cref="Ui.Px"/>, fonts are rasterised at their final size, and the
/// canvas sits on the window's bottom edge, centred, so the belt stays put when the inventory opens. The HUD's corners
/// follow the window's edges; the crosshair and wheels its centre.
/// <para>
/// Keys: Tab inventory, Q crafting, Esc back or menu, 1–6 belt, Enter chat, H (hold) hover-transfer. In the
/// inventory: drag to move, middle-drag half (Shift: a third), right-click to quick-move; a drag released anywhere but
/// a slot drops, and on its own slot is put back (Rust).
/// </para>
/// </summary>
public partial class GroundUi : Control
{
    /// <summary>The view model; set before the node enters the tree.</summary>
    public IGroundUi Model { get; set; } = null!;

    public GroundScreen Screen { get; private set; } = GroundScreen.Hud;

    /// <summary>Hit markers, damage, pickups and chat stay on screen (for screenshots).</summary>
    public bool HoldEffects { get; set; }

    /// <summary>Whether the game should free the mouse pointer.</summary>
    public bool WantsCursor => Screen != GroundScreen.Hud || Radial != null || IsChatting;

    /// <summary>
    /// Whether the interface takes the keys the game walks with: a screen over the HUD or chat does; a radial doesn't
    /// (RustMVP's: the player walks while choosing; its pointer only keeps the mouse).
    /// </summary>
    public bool TakesKeys => Screen != GroundScreen.Hud || IsChatting;

    public bool IsChatting => _chat != null && _chat.Visible;

    /// <summary>The menu's PLAY or HOST SOLO: the player name.</summary>
    public event Action<string>? HostRequested;

    /// <summary>The menu's CONNECT: player name, address and port.</summary>
    public event Action<string, string, int>? ConnectRequested;

    public event Action? QuitRequested;

    /// <summary>
    /// Set by the game to play the interface's sounds (GROUND.md §4.9): called with an event key ("ui.click",
    /// "ui.hover", "ui.drag", "ui.drop", "ui.inventory.open", "ui.inventory.close").
    /// </summary>
    public static Action<string>? Sound { get; set; }

    private Rect2? _hoverButton;
    private Rect2? _hoverButtonLast;

    private const double HoverSeconds = 0.35;

    private ColorRect _blur = null!;
    private ShaderMaterial _blurMaterial = null!;
    private Painter _canvas = null!;
    private Painter _overlay = null!;
    private CharacterPreview _character = null!;
    private LineEdit _search = null!;
    private LineEdit _quantity = null!;
    private LineEdit _name = null!;
    private LineEdit _host = null!;
    private LineEdit _port = null!;
    private LineEdit _chat = null!;

    private Vector2 _pointer = new(-100000, -100000);
    private Vector2? _pointerDesign;
    private bool _pointerHeld;
    private double _now;
    private bool _inGame = true;

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;

        _blurMaterial = new ShaderMaterial { Shader = new Shader { Code = BlurShader } };
        _blur = new ColorRect { Name = "Blur", Material = _blurMaterial, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _blur.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_blur);
        _canvas = new Painter { Name = "Canvas", Paint = PaintCanvas };
        AddChild(_canvas);
        CreateGlobe();
        _character = new CharacterPreview { Name = "Character" };
        AddChild(_character);

        _search = Field("Search", 21, Ui.Ground.Panel, new Color(0.76f, 0.74f, 0.70f, 0.22f));
        _search.PlaceholderText = "Search...";
        _quantity = Field("Quantity", 21, new Color(0.10f, 0.10f, 0.09f, 0.22f), new Color(0.76f, 0.74f, 0.70f, 0.22f));
        _quantity.MaxLength = 3;
        _quantity.Text = "1";
        _quantity.TextChanged += text =>
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                _amount = Math.Clamp(value, 1, 999);
            }
        };
        _quantity.FocusExited += () => _quantity.Text = _amount.ToString(CultureInfo.InvariantCulture);
        _name = Field("Name", 21, Ui.Ground.Entry, Ui.Ground.EntryFocus);
        _name.MaxLength = 24;
        _host = Field("Address", 21, Ui.Ground.Entry, Ui.Ground.EntryFocus);
        _port = Field("Port", 21, Ui.Ground.Entry, Ui.Ground.EntryFocus);
        _port.MaxLength = 5;
        _chat = Field("Chat", 18, new Color(0.06f, 0.07f, 0.07f, 0.72f), new Color(0.10f, 0.12f, 0.12f, 0.6f));
        _chat.MaxLength = 200;
        _chat.PlaceholderText = "Say something";
        _chat.TextSubmitted += text =>
        {
            // G7: "/t " (or "/team ") says it to the team alone.
            if (text.StartsWith("/t ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("/team ", StringComparison.OrdinalIgnoreCase))
            {
                Model.SayTeam(text[(text.IndexOf(' ') + 1)..].Trim());
            }
            else
            {
                Model.Say(text);
            }
            CloseChat();
        };
        CreateConsoleFields();
        LoadPreferences();

        _overlay = new Painter { Name = "Overlay", Paint = PaintOverlay };
        AddChild(_overlay);

        Model.HitConfirmed += OnHit;
        Model.Damaged += OnDamaged;
        Model.Gathered += OnGathered;
        Model.Crafted += OnGathered;
        Model.Notice += OnNotice;
        Model.ChatReceived += OnChat;
        Resized += Layout;
        Ui.ScaleChanged += Layout;
        GetWindow().FocusExited += CancelInventoryGestures;
        Open(Screen);
    }

    /// <summary>RustMVP's greeting on entering the game (the game calls it when play is the player's).</summary>
    public void Greet() => OnNotice("TAB  Inventory     Q  Crafting     E  Interact");

    public override void _ExitTree()
    {
        Model.HitConfirmed -= OnHit;
        Model.Damaged -= OnDamaged;
        Model.Gathered -= OnGathered;
        Model.Crafted -= OnGathered;
        Model.Notice -= OnNotice;
        Model.ChatReceived -= OnChat;
        Ui.ScaleChanged -= Layout;
        GetWindow().FocusExited -= CancelInventoryGestures;
    }

    private LineEdit Field(string name, float fontSize, Color fill, Color focused)
    {
        var field = new LineEdit { Name = name, Visible = false, ContextMenuEnabled = false, CaretBlink = true };
        Ui.Ground.Field(field, fontSize, fill, focused);
        AddChild(field);
        return field;
    }

    // ---------------------------------------------------------------- screens

    /// <summary>Shows a screen. Leaving the inventory closes the open container; crafting is never tied to one.</summary>
    public void Open(GroundScreen screen)
    {
        if ((screen is not GroundScreen.Inventory) && Model.Loot != null)
        {
            Model.CloseLoot();
        }
        if (screen != Screen)
        {
            bool open = screen is GroundScreen.Inventory or GroundScreen.Crafting, was = Screen is GroundScreen.Inventory or GroundScreen.Crafting;
            if (open != was)
            {
                Sound?.Invoke(open ? "ui.inventory.open" : "ui.inventory.close");
            }
            CancelInventoryGestures();
        }
        // The F2 panel is watched by the server while it is open (GROUND.md §8.5).
        if (screen != Screen && (screen == GroundScreen.Developer || Screen == GroundScreen.Developer))
        {
            Model.Developer?.Watch(screen == GroundScreen.Developer);
        }
        Screen = screen;
        CancelRadial();
        if (screen != GroundScreen.Hud)
        {
            CloseChat();
        }
        if (screen == GroundScreen.Menu)
        {
            _menuEntry = !_inGame;
        }
        else
        {
            // Leaving the menu closes the settings panel, dropping what was not applied.
            CloseSettings();
        }
        GetViewport()?.GuiReleaseFocus();
        Layout();
    }

    /// <summary>Tab: opens or closes the inventory (closing crafting too).</summary>
    public void ToggleInventory() => Open(Screen is GroundScreen.Inventory or GroundScreen.Crafting ? GroundScreen.Hud : GroundScreen.Inventory);

    /// <summary>Q: opens crafting; pressed again it returns to the inventory.</summary>
    public void ToggleCrafting() => Open(Screen == GroundScreen.Crafting ? GroundScreen.Inventory : GroundScreen.Crafting);

    /// <summary>The menu, as at startup (<paramref name="inGame"/> false: the connect panel is open) or as a pause menu.</summary>
    public void ShowMenu(bool inGame)
    {
        _inGame = inGame;
        Open(GroundScreen.Menu);
    }

    /// <summary>The slot whose item the item panel shows, if any.</summary>
    public SlotRef? SelectedSlot => _selected;

    /// <summary>Selects a slot, as a click does (the item panel shows it).</summary>
    public void Select(SlotRef slot) => SelectSlot(slot);

    /// <summary>Selects a recipe on the crafting screen.</summary>
    public void SelectRecipe(string key) => _recipe = key;

    /// <summary>Where a point of the inventory's design canvas is on the window, in pixels.</summary>
    public Vector2 CanvasPoint(Vector2 design) => CanvasFrame + design * Ui.Scale;

    /// <summary>The centre of a visible slot, for input-driven UI regression checks.</summary>
    public Vector2 SlotPoint(SlotRef slot)
    {
        foreach (Hit hit in _hits)
        {
            if (hit.Kind == HitKind.Slot && hit.Slot == slot)
            {
                return hit.Rect.GetCenter();
            }
        }
        throw new InvalidOperationException($"Slot {slot} is not visible.");
    }

    /// <summary>Holds the pointer at a design-canvas point, for screenshots of hover states and wheels.</summary>
    public void HoldPointer(Vector2 design)
    {
        _pointerDesign = design;
        _pointerHeld = true;
    }

    public void OpenChat()
    {
        if (Screen != GroundScreen.Hud)
        {
            return;
        }
        _chat.Visible = true;
        _chat.Text = "";
        _chat.GrabFocus();
        Layout();
    }

    private void CloseChat()
    {
        if (_chat.Visible)
        {
            _chat.Visible = false;
            _chat.ReleaseFocus();
            Layout();
        }
    }

    // Places the text fields of the visible screen.
    private void Layout()
    {
        if (_canvas == null)
        {
            return;
        }
        bool crafting = Screen == GroundScreen.Crafting;
        bool entry = Screen == GroundScreen.Menu && _menuEntry;
        Place(_search, crafting, CanvasFrame, 266, 622, 521, 40);
        Place(_quantity, crafting && CurrentRecipe() != null, CanvasFrame, 853, 710, 80, 40);
        Place(_name, entry, LeftFrame, 916, 468, 374, 45);
        Place(_host, entry, LeftFrame, 916, 561, 400, 45);
        Place(_port, entry, LeftFrame, 1333, 561, 118, 45);
        Place(_chat, _chat.Visible, LeftFrame, 20, 806, 440, 32);
        bool console = Screen == GroundScreen.Console;
        Place(_consoleInput, console && _consoleTab == ConsoleTab.Console, TopFrame, 70, 698, 1484, 44);
        Place(_itemSearch, console && _consoleTab == ConsoleTab.Items, TopFrame, 300, 88, 440, 38);
        bool wheel = Radial != null && Screen == GroundScreen.Hud, menu = Screen == GroundScreen.Menu;
        _blur.Visible = Screen is GroundScreen.Inventory or GroundScreen.Crafting or GroundScreen.Menu or GroundScreen.TechTree || wheel;
        _blurMaterial.SetShaderParameter("blur", wheel ? 1.6f : menu ? 3.2f : 2.5f);
        // RustMVP: the inventory darkens the blurred world by 14 %; a wheel only blurs it (its own 26 % veil is painted).
        _blurMaterial.SetShaderParameter("dim", wheel ? 1f : menu ? 0.42f : 0.86f);
        MouseFilter = WantsCursor ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _character.Present(Screen == GroundScreen.Inventory, new Vector2I(Ui.Px(455), Ui.Px(628)));
    }

    private void Place(LineEdit field, bool visible, Vector2 frame, float x, float y, float w, float h)
    {
        if (field != _chat)
        {
            field.Visible = visible;
        }
        if (!visible)
        {
            return;
        }
        field.Position = frame + new Vector2(Ui.Px(x), Ui.Px(y));
        field.Size = new Vector2(Ui.Px(w), Ui.Px(h));
    }

    // ---------------------------------------------------------------- frames: where the design canvas sits

    /// <summary>The design canvas, centred and on the bottom edge (inventory, crafting, belt).</summary>
    private Vector2 CanvasFrame => new(MathF.Floor((Size.X - Ui.Px(1600)) / 2), Size.Y - Ui.Px(900));

    /// <summary>Centred both ways (crosshair, prompts, wheels).</summary>
    private Vector2 CentreFrame => new(MathF.Floor(Size.X / 2) - Ui.Px(800), MathF.Floor(Size.Y / 2) - Ui.Px(450));

    /// <summary>Its right edge on the window's (vitals, statuses, pickups).</summary>
    private Vector2 RightFrame => new(Size.X - Ui.Px(1600), Size.Y - Ui.Px(900));

    /// <summary>Its left edge on the window's (chat, notices, menu).</summary>
    private Vector2 LeftFrame => new(0, Size.Y - Ui.Px(900));

    /// <summary>The canvas's columns, hanging from the window's top edge (the inventory and crafting tabs).</summary>
    private Vector2 TopFrame => new(CanvasFrame.X, 0);

    // ---------------------------------------------------------------- time and input

    public override void _Process(double delta)
    {
        _now += delta;
        bool dead = Model.Death != null;
        // A death shows the death screen; the F1 panel may stay open over it, but a death (kill) closes it.
        if (dead && (Screen is not (GroundScreen.Death or GroundScreen.Menu or GroundScreen.Console) || (Screen == GroundScreen.Console && !_wasDeadForConsole)))
        {
            Open(GroundScreen.Death);
        }
        _wasDeadForConsole = dead;
        if (!dead && Screen == GroundScreen.Death)
        {
            Open(GroundScreen.Hud);
        }
        if (Model.Loot != null && Screen == GroundScreen.Hud)
        {
            Open(GroundScreen.Inventory);
        }
        UpdateRadial();
        if (Screen == GroundScreen.Inventory)
        {
            HoverTransfer();
            ItemStack held = Model.ActiveBelt >= 0 ? Model.Slots(ContainerId.Belt)[Model.ActiveBelt] : default;
            _character.Dress(held.IsEmpty ? null : held.Key);
        }
        if (_quantity.Visible != (Screen == GroundScreen.Crafting && CurrentRecipe() != null))
        {
            Layout();
        }
        UpdateGlobe(delta);
        DragSetting();
        _canvas.QueueRedraw();
        _globePicture.QueueRedraw();
        _overlay.QueueRedraw();
    }

    // Tab works even while a text field has focus (RustMVP routes it first).
    public override void _Input(InputEvent e)
    {
        InventoryInput(e);
        if (e is InputEventKey { Pressed: true } panelKey && (DeveloperKey(panelKey) || ConsoleKey(panelKey)))
        {
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Tab } && Screen is not (GroundScreen.Menu or GroundScreen.Death or GroundScreen.Console or GroundScreen.Developer))
        {
            ToggleInventory();
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape } && IsChatting)
        {
            CloseChat();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // A radial's held input (the right mouse with a building tool; E and R when a binding asks for them) and the keys
        // and buttons that end an open radial.
        if (RadialEvent(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey { Pressed: true } settingsKey && SettingsKey(settingsKey))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        bool handled = true;
        switch (key.PhysicalKeycode)
        {
            case Key.G when Screen is GroundScreen.Hud or GroundScreen.Map && Radial == null:
                ToggleMap();
                break;
            case Key.Escape when Screen is GroundScreen.Inventory or GroundScreen.Crafting or GroundScreen.Map or GroundScreen.TechTree:
                Open(GroundScreen.Hud);
                break;
            case Key.Escape when Screen is GroundScreen.Hud or GroundScreen.Death:
                ShowMenu(inGame: true);
                break;
            case Key.Escape when Screen == GroundScreen.Menu && _inGame:
                Open(Model.Death != null ? GroundScreen.Death : GroundScreen.Hud);
                break;
            case Key.Q when Screen is GroundScreen.Hud or GroundScreen.Inventory or GroundScreen.Crafting && KeyContexts.Hands(Model.Keys):
                ToggleCrafting();
                break;
            case Key.Enter or Key.KpEnter when Screen == GroundScreen.Hud && Radial == null:
                OpenChat();
                break;
            case >= Key.Key1 and <= Key.Key6 when Screen is GroundScreen.Hud or GroundScreen.Inventory && KeyContexts.Belt(Model.Keys):
                Model.SelectBelt((int)(key.PhysicalKeycode - Key.Key1));
                break;
            default:
                handled = false;
                break;
        }
        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_pointerHeld)
        {
            return;
        }
        switch (e)
        {
            case InputEventMouseMotion motion:
                _pointer = motion.Position;
                _pointerDesign = null;
                PointerMoved();
                break;
            case InputEventMouseButton button when RadialButton(button):
                break;
            case InputEventMouseButton { Pressed: true } press:
                _pointer = press.Position;
                _pointerDesign = null;
                GetViewport().GuiReleaseFocus();
                Press(press.ButtonIndex);
                break;
            case InputEventMouseButton { Pressed: false } release:
                _pointer = release.Position;
                Release(release.ButtonIndex);
                break;
            default:
                return;
        }
        AcceptEvent();
    }

    // A button while a radial is held or open (its trigger's release, a click that chooses or cancels), where it was.
    private bool RadialButton(InputEventMouseButton button)
    {
        _pointer = button.Position;
        return RadialEvent(button);
    }

    /// <summary>
    /// The pointer in screen pixels, or the held design point (for screenshots): on the centred frame over the HUD
    /// and radials, on the canvas elsewhere.
    /// </summary>
    private Vector2 Pointer => _pointerDesign is Vector2 design
        ? (Screen == GroundScreen.Hud ? CentreFrame : CanvasFrame) + design * Ui.Scale
        : _pointer;

    private void Press(MouseButton button)
    {
        if (button is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (!ZoomGlobe(button == MouseButton.WheelUp ? -1 : 1) && !ScrollConsole(button == MouseButton.WheelDown ? 1 : -1) &&
                !ScrollDeveloper(button == MouseButton.WheelDown ? 1 : -1))
            {
                ScrollRecipes(button == MouseButton.WheelDown ? 1 : -1);
            }
            return;
        }
        Hit? hit = HitAt(Pointer);
        if (hit is not Hit h)
        {
            return;
        }
        if (Screen == GroundScreen.Inventory && h.Kind is HitKind.Slot or HitKind.SplitIcon or HitKind.SplitTrack)
        {
            PressInventory(h, button);
            return;
        }
        if (Screen == GroundScreen.Inventory && h.Kind == HitKind.Character && button == MouseButton.Left)
        {
            _characterDragging = true;
            return;
        }
        if (h.Kind == HitKind.Map)
        {
            PressGlobe(button);
            return;
        }
        if (button == MouseButton.Left && h.Click != null)
        {
            Sound?.Invoke("ui.click");
            h.Click();
        }
    }

    private void Release(MouseButton button)
    {
        ReleaseConsole(button);
        ReleaseGlobe(button);
        ReleaseInventory(button);
    }

    // ---------------------------------------------------------------- hits

    private enum HitKind
    {
        Button,
        Slot,
        SplitIcon,
        SplitTrack,
        Panel,
        Character,
        Map,
    }

    /// <summary>Something under the pointer: a button with its action, a slot, or a panel (so a press on it goes nowhere).</summary>
    private readonly record struct Hit(Rect2 Rect, HitKind Kind, Action? Click = null, SlotRef Slot = default, string? Key = null, string? Tooltip = null);

    private readonly List<Hit> _hits = new();

    // The topmost button or slot under the point; a bare panel only when there is none.
    private Hit? HitAt(Vector2 point)
    {
        Hit? panel = null;
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Rect.HasPoint(point))
            {
                if (_hits[i].Kind is not (HitKind.Panel or HitKind.Map))
                {
                    return _hits[i];
                }
                panel ??= _hits[i];
            }
        }
        return panel;
    }

    // ---------------------------------------------------------------- HUD moments

    private double _hitAt = -100;
    private bool _headshot;
    private readonly List<(float Bearing, double At)> _damage = new();
    private readonly List<Pickup> _pickups = new();
    private readonly List<(ChatLine Line, double At)> _chatLines = new();
    private string _toast = "";
    private double _toastAt = -100;

    private sealed class Pickup
    {
        public string Key = "";
        public int Amount;
        public double At;
    }

    private void OnHit(bool headshot)
    {
        _hitAt = _now;
        _headshot = headshot;
    }

    private void OnDamaged(float bearing)
    {
        _damage.Add((bearing, _now));
        if (_damage.Count > 6)
        {
            _damage.RemoveAt(0);
        }
    }

    // Rust adds repeated pickups of the same item to its row while the row shows.
    private void OnGathered(string key, int amount)
    {
        foreach (Pickup row in _pickups)
        {
            if (row.Key == key && Age(row.At) < PickupSeconds)
            {
                row.Amount += amount;
                row.At = _now;
                return;
            }
        }
        _pickups.Add(new Pickup { Key = key, Amount = amount, At = _now });
        if (_pickups.Count > 6)
        {
            _pickups.RemoveAt(0);
        }
    }

    private void OnNotice(string text)
    {
        _toast = text;
        _toastAt = _now;
    }

    private void OnChat(ChatLine line)
    {
        _chatLines.Add((line, _now));
        if (_chatLines.Count > 8)
        {
            _chatLines.RemoveAt(0);
        }
    }

    /// <summary>Seconds since a moment (held at zero when effects are held for screenshots).</summary>
    private double Age(double at) => HoldEffects ? 0 : _now - at;

    // ---------------------------------------------------------------- preferences (name, address, favourites)

    private const string PreferencesPath = "user://ground-ui.cfg";

    /// <summary>The body chosen in the menu (GROUND.md §4.8): female or not (male).</summary>
    public bool FemaleBody { get; set; }

    /// <summary>A launch-specified port takes precedence over the last connection preference.</summary>
    public int? ConnectionPortOverride { get; set; }
    public int ConnectionPort => MenuPort();

    /// <summary>The body last chosen in the menu, as saved on this computer.</summary>
    public static bool SavedFemaleBody()
    {
        var file = new ConfigFile();
        file.Load(PreferencesPath);
        return (string)file.GetValue("connection", "body", "male") == "female";
    }

    private void LoadPreferences()
    {
        var file = new ConfigFile();
        file.Load(PreferencesPath);
        FemaleBody = (string)file.GetValue("connection", "body", "male") == "female";
        _name.Text = (string)file.GetValue("connection", "name", "Survivor");
        _host.Text = (string)file.GetValue("connection", "host", "127.0.0.1");
        _port.Text = (ConnectionPortOverride ?? (int)file.GetValue("connection", "port", 27015)).ToString(CultureInfo.InvariantCulture);
        foreach (string key in (string[])file.GetValue("crafting", "favourites", Array.Empty<string>()))
        {
            _favourites.Add(key);
        }
    }

    private void SavePreferences()
    {
        var file = new ConfigFile();
        file.Load(PreferencesPath);
        file.SetValue("connection", "name", _name.Text);
        file.SetValue("connection", "body", FemaleBody ? "female" : "male");
        file.SetValue("connection", "host", _host.Text);
        file.SetValue("connection", "port", MenuPort());
        file.SetValue("crafting", "favourites", new List<string>(_favourites).ToArray());
        file.Save(PreferencesPath);
    }

    private int MenuPort() => int.TryParse(_port.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) ? Math.Clamp(port, 1, 65535) : 27015;

    // The scene behind the inventory, crafting and wheels: blurred from the screen's mipmaps and dimmed, with a
    // trace of grain so the blur doesn't band (RustMVP's).
    private const string BlurShader = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float blur = 2.5;
uniform float dim = 0.86;
void fragment() {
    vec3 scene = textureLod(screen_tex, SCREEN_UV, blur).rgb;
    float grain = fract(sin(dot(FRAGCOORD.xy, vec2(12.9898, 78.233))) * 43758.5453);
    COLOR = vec4(scene * dim + (grain - 0.5) * 0.012, 1.0);
}";
}
