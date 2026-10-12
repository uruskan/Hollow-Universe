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
}

/// <summary>
/// The ground game's interface (GROUND.md §8): RustMVP's interface, matched exactly, on top of an
/// <see cref="IGroundUi"/> view model. It is painted in immediate mode, as RustMVP's was, on a 1600 × 900 design
/// canvas: every design length goes through <see cref="Ui.Px"/>, fonts are rasterised at their final size, and the
/// canvas sits on the window's bottom edge, centred, so the belt stays put when the inventory opens. The HUD's corners
/// follow the window's edges; the crosshair and wheels its centre.
/// <para>
/// Keys: Tab inventory, Q crafting, Esc back or menu, 1–6 belt, Enter chat, H (hold) hover-transfer. In the
/// inventory: drag to move, middle-drag half (Shift: a third), right-click to quick-move, drag outside the panels to
/// drop.
/// </para>
/// </summary>
public partial class GroundUi : Control
{
    /// <summary>The view model; set before the node enters the tree.</summary>
    public IGroundUi Model { get; set; } = null!;

    public GroundScreen Screen { get; private set; } = GroundScreen.Hud;

    /// <summary>The open radial wheel, if any.</summary>
    public WheelKind? Wheel { get; private set; }

    /// <summary>Hit markers, damage, pickups and chat stay on screen (for screenshots).</summary>
    public bool HoldEffects { get; set; }

    /// <summary>Whether the game should free the mouse pointer.</summary>
    public bool WantsCursor => Screen != GroundScreen.Hud || Wheel != null || IsChatting;

    public bool IsChatting => _chat != null && _chat.Visible;

    /// <summary>The menu's PLAY or HOST SOLO: the player name.</summary>
    public event Action<string>? HostRequested;

    /// <summary>The menu's CONNECT: player name, address and port.</summary>
    public event Action<string, string, int>? ConnectRequested;

    public event Action? QuitRequested;

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
        _character = new CharacterPreview { Name = "Character" };
        AddChild(_character);

        _search = Field("Search", 21, Ui.Ground.Panel, new Color(0.76f, 0.74f, 0.70f, 0.22f));
        _search.PlaceholderText = "Search...";
        _search.TextChanged += _ => QueueRedraw();
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
            Model.Say(text);
            CloseChat();
        };
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
        Open(Screen);
    }

    public override void _ExitTree()
    {
        Model.HitConfirmed -= OnHit;
        Model.Damaged -= OnDamaged;
        Model.Gathered -= OnGathered;
        Model.Crafted -= OnGathered;
        Model.Notice -= OnNotice;
        Model.ChatReceived -= OnChat;
        Ui.ScaleChanged -= Layout;
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
            _drag = null;
            _splitTracking = false;
            _hoverDone.Clear();
            _hoverStarted.Clear();
        }
        Screen = screen;
        Wheel = null;
        if (screen != GroundScreen.Hud)
        {
            CloseChat();
        }
        if (screen == GroundScreen.Menu)
        {
            _menuEntry = !_inGame;
        }
        GetViewport()?.GuiReleaseFocus();
        Layout();
        QueueRedraw();
    }

    /// <summary>Tab: opens or closes the inventory (closing crafting too).</summary>
    public void ToggleInventory() => Open(Screen is GroundScreen.Inventory or GroundScreen.Crafting ? GroundScreen.Hud : GroundScreen.Inventory);

    /// <summary>Q: opens crafting; pressed again it returns to the inventory.</summary>
    public void ToggleCrafting() => Open(Screen == GroundScreen.Crafting ? GroundScreen.Inventory : GroundScreen.Crafting);

    /// <summary>Opens a radial wheel over the HUD (the plan's or the hammer's; held right mouse button in the game).</summary>
    public void OpenWheel(WheelKind kind)
    {
        Open(GroundScreen.Hud);
        Wheel = kind;
        Layout();
    }

    /// <summary>Closes the wheel, applying the sector under the pointer when <paramref name="apply"/> is set.</summary>
    public void CloseWheel(bool apply)
    {
        if (Wheel is WheelKind kind && apply)
        {
            int choice = WheelSelection(kind);
            if (choice >= 0)
            {
                Model.ChooseWheel(kind, choice);
            }
        }
        Wheel = null;
        Layout();
    }

    /// <summary>The menu, as at startup (<paramref name="inGame"/> false: the connect panel is open) or as a pause menu.</summary>
    public void ShowMenu(bool inGame)
    {
        _inGame = inGame;
        Open(GroundScreen.Menu);
    }

    /// <summary>Selects a slot, as a click does (the item panel shows it).</summary>
    public void Select(SlotRef slot) => SelectSlot(slot);

    /// <summary>Selects a recipe on the crafting screen.</summary>
    public void SelectRecipe(string key) => _recipe = key;

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
        Place(_name, entry, LeftFrame, 916, 468, 534, 45);
        Place(_host, entry, LeftFrame, 916, 561, 400, 45);
        Place(_port, entry, LeftFrame, 1333, 561, 118, 45);
        Place(_chat, _chat.Visible, LeftFrame, 20, 806, 440, 32);
        bool wheel = Wheel != null && Screen == GroundScreen.Hud, menu = Screen == GroundScreen.Menu;
        _blur.Visible = Screen is GroundScreen.Inventory or GroundScreen.Crafting or GroundScreen.Menu || wheel;
        _blurMaterial.SetShaderParameter("blur", wheel ? 1.6f : menu ? 3.2f : 2.5f);
        _blurMaterial.SetShaderParameter("dim", wheel ? 0.74f : menu ? 0.42f : 0.86f);
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
        if (Model.Death != null && Screen is not (GroundScreen.Death or GroundScreen.Menu))
        {
            Open(GroundScreen.Death);
        }
        else if (Model.Death == null && Screen == GroundScreen.Death)
        {
            Open(GroundScreen.Hud);
        }
        if (Model.Loot != null && Screen == GroundScreen.Hud)
        {
            Open(GroundScreen.Inventory);
        }
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
        _canvas.QueueRedraw();
        _overlay.QueueRedraw();
    }

    // Tab works even while a text field has focus (RustMVP routes it first).
    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Tab } && Screen is not (GroundScreen.Menu or GroundScreen.Death))
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
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        bool handled = true;
        switch (key.PhysicalKeycode)
        {
            case Key.Escape when Wheel != null:
                CloseWheel(false);
                break;
            case Key.Escape when Screen is GroundScreen.Inventory or GroundScreen.Crafting:
                Open(GroundScreen.Hud);
                break;
            case Key.Escape when Screen == GroundScreen.Hud:
                ShowMenu(inGame: true);
                break;
            case Key.Escape when Screen == GroundScreen.Menu && _inGame:
                Open(Model.Death != null ? GroundScreen.Death : GroundScreen.Hud);
                break;
            case Key.Q when Screen is GroundScreen.Hud or GroundScreen.Inventory or GroundScreen.Crafting:
                ToggleCrafting();
                break;
            case Key.Enter or Key.KpEnter when Screen == GroundScreen.Hud && Wheel == null:
                OpenChat();
                break;
            case >= Key.Key1 and <= Key.Key6 when Screen is GroundScreen.Hud or GroundScreen.Inventory:
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

    /// <summary>The pointer in screen pixels (or the held design point, for screenshots).</summary>
    private Vector2 Pointer => _pointerDesign is Vector2 design ? CentreFrame + design * Ui.Scale : _pointer;

    private void Press(MouseButton button)
    {
        if (button is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            ScrollRecipes(button == MouseButton.WheelDown ? 1 : -1);
            return;
        }
        if (Wheel != null)
        {
            if (button == MouseButton.Left)
            {
                CloseWheel(true);
            }
            else if (button == MouseButton.Right)
            {
                CloseWheel(false);
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
        if (button == MouseButton.Left && h.Click != null)
        {
            h.Click();
        }
    }

    private void Release(MouseButton button)
    {
        if (Wheel != null && button == MouseButton.Right)
        {
            CloseWheel(true);
            return;
        }
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
    }

    /// <summary>Something under the pointer: a button with its action, a slot, or a panel (so drops don't fall through).</summary>
    private readonly record struct Hit(Rect2 Rect, HitKind Kind, Action? Click = null, SlotRef Slot = default, string? Key = null);

    private readonly List<Hit> _hits = new();

    private Hit? HitAt(Vector2 point)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Rect.HasPoint(point))
            {
                return _hits[i];
            }
        }
        return null;
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

    private void LoadPreferences()
    {
        var file = new ConfigFile();
        file.Load(PreferencesPath);
        _name.Text = (string)file.GetValue("connection", "name", "Survivor");
        _host.Text = (string)file.GetValue("connection", "host", "127.0.0.1");
        _port.Text = ((int)file.GetValue("connection", "port", 27015)).ToString(CultureInfo.InvariantCulture);
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
