using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using HollowUniverse.Common;

namespace HollowUniverse.GalaxyMap;

/// <summary>Labels, rings and lines drawn over the 3D map in screen space.</summary>
public partial class MapOverlay : Control
{
    public readonly record struct Text(Vector2 Position, string Value, Color Colour, int Size, bool Centred);
    public readonly record struct Ring(Vector2 Position, float Radius, Color Colour, float Width);

    public readonly List<Text> Texts = new();
    public readonly List<Ring> Rings = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public void Clear()
    {
        Texts.Clear();
        Rings.Clear();
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        foreach (Ring ring in Rings)
        {
            DrawArc(ring.Position, ring.Radius, 0, Mathf.Tau, 48, ring.Colour, ring.Width, true);
        }
        foreach (Text text in Texts)
        {
            Vector2 size = font.GetStringSize(text.Value, HorizontalAlignment.Left, -1, text.Size);
            Vector2 at = text.Centred ? text.Position - new Vector2(size.X / 2, 0) : text.Position;
            DrawString(font, at + new Vector2(1, 1), text.Value, HorizontalAlignment.Left, -1, text.Size, new Color(0, 0, 0, 0.7f * text.Colour.A));
            DrawString(font, at, text.Value, HorizontalAlignment.Left, -1, text.Size, text.Colour);
        }
    }
}

/// <summary>The galaxy map's interface: focus readout, search, selected star, help and status.</summary>
public partial class GalaxyMapUi : Control
{
    private Label _sector = null!;
    private Label _position = null!;
    private Label _fromSol = null!;
    private Label _scale = null!;
    private LineEdit _search = null!;
    private VBoxContainer _results = null!;
    private PanelContainer _resultsPanel = null!;
    private PanelContainer _info = null!;
    private Label _infoName = null!;
    private Label _infoType = null!;
    private GridContainer _infoRows = null!;
    private Label _status = null!;

    public event Action<string>? SearchChanged;
    public event Action<SearchResult>? ResultChosen;
    public event Action? OpenSystemRequested;

    public MapOverlay Overlay { get; private set; } = null!;

    public bool IsTyping => _search.HasFocus();

    public override void _Ready()
    {
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Overlay = new MapOverlay();
        AddChild(Overlay);

        // Top left: where the camera is.
        var focusPanel = new PanelContainer();
        var focusBox = new VBoxContainer();
        focusBox.AddThemeConstantOverride("separation", 2);
        focusBox.AddChild(Ui.Heading("Hollow Universe · Galaxy map", 12));
        _sector = Ui.Label("", 20, Ui.Text);
        _position = Ui.Label("", 13, Ui.Dim);
        _fromSol = Ui.Label("", 13, Ui.Dim);
        _scale = Ui.Label("", 13, Ui.Dim);
        focusBox.AddChild(_sector);
        focusBox.AddChild(_position);
        focusBox.AddChild(_fromSol);
        focusBox.AddChild(_scale);
        focusPanel.AddChild(focusBox);
        AddChild(focusPanel);
        Ui.Place(focusPanel, LayoutPreset.TopLeft);

        // Top right: search.
        var searchBox = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        _search = new LineEdit { PlaceholderText = "Search stars, systems and sectors…", ClearButtonEnabled = true };
        _search.TextChanged += text => SearchChanged?.Invoke(text);
        _search.TextSubmitted += _ =>
        {
            if (_results.GetChildCount() > 0 && _results.GetChild(0) is Button first)
            {
                first.EmitSignal(BaseButton.SignalName.Pressed);
            }
        };
        searchBox.AddChild(_search);
        _resultsPanel = new PanelContainer { Visible = false };
        _results = new VBoxContainer();
        _results.AddThemeConstantOverride("separation", 2);
        _resultsPanel.AddChild(_results);
        searchBox.AddChild(_resultsPanel);
        AddChild(searchBox);
        Ui.Place(searchBox, LayoutPreset.TopRight);

        // Right: the selected star.
        _info = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(360, 0) };
        var infoBox = new VBoxContainer();
        infoBox.AddThemeConstantOverride("separation", 4);
        infoBox.AddChild(Ui.Heading("Selected system", 12));
        _infoName = Ui.Label("", 22, Ui.Text);
        _infoName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoType = Ui.Label("", 14, Ui.Warm);
        _infoRows = new GridContainer { Columns = 2 };
        _infoRows.AddThemeConstantOverride("h_separation", 16);
        _infoRows.AddThemeConstantOverride("v_separation", 3);
        var open = new Button { Text = "Open system map   (Enter)" };
        open.Pressed += () => OpenSystemRequested?.Invoke();
        infoBox.AddChild(_infoName);
        infoBox.AddChild(_infoType);
        infoBox.AddChild(new HSeparator());
        infoBox.AddChild(_infoRows);
        infoBox.AddChild(open);
        _info.AddChild(infoBox);
        AddChild(_info);
        Ui.Place(_info, LayoutPreset.TopRight);
        _info.OffsetTop += 56;
        _info.OffsetBottom += 56;

        // Bottom left: controls.
        var help = new PanelContainer();
        help.AddChild(Ui.Label(
            "Drag: rotate   ·   Right-drag / WASD: move   ·   R/F: up/down   ·   Wheel: zoom\n" +
            "Click: select   ·   Double-click: fly to   ·   Enter: system map   ·   H: Sol   ·   G: whole galaxy",
            13, Ui.Dim));
        AddChild(help);
        Ui.Place(help, LayoutPreset.BottomLeft);

        // Bottom right: status.
        _status = Ui.Label("", 12, Ui.Dim);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        _status.CustomMinimumSize = new Vector2(520, 0);
        AddChild(_status);
        Ui.Place(_status, LayoutPreset.BottomRight);
    }

    public void SetFocus(string sector, Vector3D focus, double cameraDistance)
    {
        _sector.Text = sector.Length > 0 ? sector + " sector" : "Intergalactic space";
        _position.Text = $"Position  {Ui.Number(focus.X, "N0")}, {Ui.Number(focus.Y, "N0")}, {Ui.Number(focus.Z, "N0")} ly";
        _fromSol.Text = $"From Sol  {Ui.Ly(focus.Length)}";
        _scale.Text = $"View  {Ui.Ly(cameraDistance * 1.15)} wide";
    }

    public void SetResults(List<SearchResult> results)
    {
        foreach (Node child in _results.GetChildren())
        {
            child.QueueFree();
        }
        foreach (SearchResult result in results)
        {
            SearchResult captured = result;
            var button = new Button
            {
                Text = $"{result.Name}   ·   {Ui.Ly(result.Position.Length)}",
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
            };
            button.Pressed += () =>
            {
                ResultChosen?.Invoke(captured);
                _resultsPanel.Visible = false;
                _search.ReleaseFocus();
            };
            _results.AddChild(button);
        }
        _resultsPanel.Visible = results.Count > 0;
    }

    public void ClearSearch()
    {
        _search.Text = "";
        SetResults(new List<SearchResult>());
    }

    public void ShowSelection(string name, GalaxyStar star, string sector, int companionCount)
    {
        _info.Visible = true;
        _infoName.Text = name;
        string kind = star.Class.KindName;
        _infoType.Text = star.Class.Kind is StarKind.NeutronStar or StarKind.BlackHole ? kind : $"{star.Class} · {kind}";
        foreach (Node child in _infoRows.GetChildren())
        {
            child.QueueFree();
        }
        Row("Distance from Sol", Ui.Ly(star.Position.Length));
        Row("Sector", sector);
        Row("Position", $"{Ui.Number(star.Position.X, "N1")}, {Ui.Number(star.Position.Y, "N1")}, {Ui.Number(star.Position.Z, "N1")}");
        if (star.Physics.Temperature > 0)
        {
            Row("Temperature", Ui.Temperature(star.Physics.Temperature));
        }
        Row("Mass", Ui.Number(star.Physics.Mass, "N2") + " M☉");
        Row("Radius", star.Physics.Radius < 0.01 ? Ui.Number(star.Physics.Radius * 695700, "N0") + " km" : Ui.Number(star.Physics.Radius, "N2") + " R☉");
        if (star.Physics.Luminosity > 0)
        {
            Row("Luminosity", Ui.Number(star.Physics.Luminosity, star.Physics.Luminosity < 0.01 ? "G3" : "N2") + " L☉");
        }
        if (star.Physics.AbsoluteMagnitude < 40)
        {
            Row("Abs. magnitude", Ui.Magnitude(star.Physics.AbsoluteMagnitude));
        }
        if (companionCount > 0)
        {
            Row("Companions", companionCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Row("Origin", star.IsCatalogue ? "Real star (HYG catalogue)" : "Generated");
        Row("Address", star.Address.ToString());
    }

    public void HideSelection() => _info.Visible = false;

    public void SetStatus(string text) => _status.Text = text;

    private void Row(string key, string value)
    {
        _infoRows.AddChild(Ui.Label(key, 13, Ui.Dim));
        _infoRows.AddChild(Ui.Label(value, 13, Ui.Text));
    }
}
