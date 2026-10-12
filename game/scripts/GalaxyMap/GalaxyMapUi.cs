using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using HollowUniverse.Common;

namespace HollowUniverse.GalaxyMap;

/// <summary>The galaxy map's interface: where the camera is, search, the selected system, key hints and status.</summary>
public partial class GalaxyMapUi : Control
{
    private Label _region = null!;
    private Label _regionKind = null!;
    private Label _position = null!;
    private Label _fromSol = null!;
    private Label _viewWidth = null!;
    private LineEdit _search = null!;
    private PanelContainer _resultsCard = null!;
    private VBoxContainer _results = null!;
    private PanelContainer _selectionCard = null!;
    private Label _selectionName = null!;
    private Label _selectionType = null!;
    private GridContainer _selectionRows = null!;
    private Label _status = null!;
    private readonly List<Control> _panels = new();
    private bool _hasSelection;

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

        BuildLocationCard();
        BuildRightColumn();
        BuildBottom();
    }

    // Top left: where the camera is looking.
    private void BuildLocationCard()
    {
        (PanelContainer card, VBoxContainer content) = Ui.Card(4);
        Ui.Size(card, 300);
        content.AddChild(Ui.Label("Galaxy map", TextStyle.Overline));
        _region = Ui.Label("", TextStyle.Title);
        _region.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _regionKind = Ui.Label("", TextStyle.Secondary);
        content.AddChild(_region);
        content.AddChild(_regionKind);
        content.AddChild(Ui.Separator());
        GridContainer rows = Ui.Rows();
        _position = Ui.AddRow(rows, "Position");
        _fromSol = Ui.AddRow(rows, "From Sol");
        _viewWidth = Ui.AddRow(rows, "View");
        content.AddChild(rows);
        AddChild(card);
        Ui.Place(card, LayoutPreset.TopLeft);
        _panels.Add(card);
    }

    // Top right: search, its results, and the selected system below them.
    private void BuildRightColumn()
    {
        var column = new VBoxContainer();
        Ui.Track(column, c => c.AddThemeConstantOverride("separation", Ui.Px(10)));
        Ui.Size(column, 380);
        column.MouseFilter = MouseFilterEnum.Ignore;

        _search = new LineEdit
        {
            PlaceholderText = "Search stars, systems and sectors",
            ClearButtonEnabled = true,
            RightIcon = UiIcons.Get(UiIcon.Search, 16),
            CaretBlink = true,
        };
        _search.TextChanged += text => SearchChanged?.Invoke(text);
        _search.TextSubmitted += _ =>
        {
            if (_results.GetChildCount() > 0 && _results.GetChild(0) is Button first)
            {
                first.EmitSignal(BaseButton.SignalName.Pressed);
            }
        };
        column.AddChild(_search);

        (_resultsCard, _results) = Ui.Card(2);
        Ui.Track(_resultsCard, c => c.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Panel, 10, Ui.PanelBorder, 6, 6)));
        _resultsCard.Visible = false;
        column.AddChild(_resultsCard);

        VBoxContainer content;
        (_selectionCard, content) = Ui.Card(4);
        _selectionCard.Visible = false;
        content.AddChild(Ui.Label("Selected system", TextStyle.Overline));
        _selectionName = Ui.Label("", TextStyle.Title);
        _selectionName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _selectionType = Ui.Label("", TextStyle.Subtitle);
        content.AddChild(_selectionName);
        content.AddChild(_selectionType);
        content.AddChild(Ui.Separator());
        _selectionRows = Ui.Rows();
        content.AddChild(_selectionRows);
        var spacer = new Control();
        Ui.Size(spacer, 0, 6);
        content.AddChild(spacer);
        var open = new Button
        {
            Text = "Open system map",
            ThemeTypeVariation = "PrimaryButton",
            Icon = UiIcons.Get(UiIcon.Open, 16),
            IconAlignment = HorizontalAlignment.Right,
        };
        open.Pressed += () => OpenSystemRequested?.Invoke();
        content.AddChild(open);
        column.AddChild(_selectionCard);

        AddChild(column);
        Ui.Place(column, LayoutPreset.TopRight);
        _panels.Add(_search);
        _panels.Add(_resultsCard);
        _panels.Add(_selectionCard);
    }

    // Bottom: controls on the left, streaming status on the right.
    private void BuildBottom()
    {
        var hints = new PanelContainer();
        Ui.Track(hints, c => c.AddThemeStyleboxOverride("panel", StripStyle()));
        var lines = new VBoxContainer();
        Ui.Track(lines, c => c.AddThemeConstantOverride("separation", Ui.Px(7)));
        lines.AddChild(HintLine(("Drag", "Rotate"), ("Right-drag", "Pan"), ("Wheel", "Zoom"), ("Click", "Select"), ("Double-click", "Fly to")));
        lines.AddChild(HintLine(("W|A|S|D", "Move"), ("R|F", "Up / down"), ("Enter", "System map"), ("H", "Sol"), ("G", "Whole galaxy")));
        hints.AddChild(lines);
        AddChild(hints);
        Ui.Place(hints, LayoutPreset.BottomLeft);

        var statusStrip = new PanelContainer();
        Ui.Track(statusStrip, c => c.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Ui.Panel, 0.7f), 8, null, 10, 6)));
        _status = Ui.Label("", TextStyle.Caption);
        _status.AddThemeFontOverride("font", Ui.Tabular);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        statusStrip.AddChild(_status);
        AddChild(statusStrip);
        Ui.Place(statusStrip, LayoutPreset.BottomRight);
        _panels.Add(hints);
        _panels.Add(statusStrip);
    }

    /// <summary>Where the visible panels are on screen, so labels on the map can keep clear of them.</summary>
    public IEnumerable<Rect2> PanelRects()
    {
        foreach (Control panel in _panels)
        {
            if (panel.IsVisibleInTree())
            {
                yield return panel.GetGlobalRect().Grow(Ui.Px(4));
            }
        }
    }

    private static HBoxContainer HintLine(params (string Keys, string Action)[] hints)
    {
        var line = new HBoxContainer();
        Ui.Track(line, c => c.AddThemeConstantOverride("separation", Ui.Px(16)));
        foreach ((string keys, string action) in hints)
        {
            line.AddChild(Ui.KeyHint(keys, action));
        }
        return line;
    }

    private static StyleBoxFlat StripStyle()
    {
        StyleBoxFlat strip = Ui.Box(Ui.Panel, 10, Ui.PanelBorder, 12, 10);
        strip.ShadowColor = Ui.Shadow;
        strip.ShadowSize = Ui.Px(14);
        strip.ShadowOffset = new Vector2(0, Ui.Px(3));
        return strip;
    }

    public void SetFocus(string sector, Region? region, Vector3D focus, double cameraDistance)
    {
        bool inside = sector.Length > 0;
        if (region != null)
        {
            _region.Text = region.Name;
            _regionKind.Text = char.ToUpperInvariant(region.KindName[0]) + region.KindName[1..] + (inside ? " in " + sector : "");
        }
        else
        {
            _region.Text = inside ? sector : "Intergalactic space";
            _regionKind.Text = inside ? "Sector" : "Outside the sector grid";
        }
        _position.Text = $"{Ui.Number(focus.X)}, {Ui.Number(focus.Y)}, {Ui.Number(focus.Z)} ly";
        _fromSol.Text = Ui.Ly(focus.Length);
        _viewWidth.Text = Ui.Ly(cameraDistance * 1.15) + " wide";
    }

    public void SetResults(List<SearchResult> results)
    {
        foreach (Node child in _results.GetChildren())
        {
            _results.RemoveChild(child);
            child.QueueFree();
        }
        foreach (SearchResult result in results)
        {
            SearchResult captured = result;
            var row = new Button { ThemeTypeVariation = "ListButton", ClipText = true };
            var content = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            Ui.Track(content, c =>
            {
                c.AddThemeConstantOverride("separation", Ui.Px(12));
                c.OffsetLeft = Ui.Px(10);
                c.OffsetRight = -Ui.Px(10);
            });
            Label name = Ui.Label(result.Name, TextStyle.Body);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.MouseFilter = MouseFilterEnum.Ignore;
            name.VerticalAlignment = VerticalAlignment.Center;
            Label distance = Ui.Label(result.IsSector ? result.PlaceKind : Ui.Ly(result.Position.Length), TextStyle.Caption);
            distance.AddThemeFontOverride("font", Ui.Tabular);
            distance.MouseFilter = MouseFilterEnum.Ignore;
            distance.VerticalAlignment = VerticalAlignment.Center;
            content.AddChild(name);
            content.AddChild(distance);
            row.AddChild(content);
            Ui.Track(row, r => r.CustomMinimumSize = new Vector2(0, Ui.Px(36)));
            row.Pressed += () =>
            {
                _resultsCard.Visible = false;
                _search.ReleaseFocus();
                ResultChosen?.Invoke(captured);
                UpdateCards();
            };
            _results.AddChild(row);
        }
        _resultsCard.Visible = results.Count > 0;
        UpdateCards();
    }

    // Search results take the selected system's place while they're open, so the column never runs off the screen.
    private void UpdateCards() => _selectionCard.Visible = _hasSelection && !_resultsCard.Visible;

    /// <summary>Types into the search box (used by automation; the view answers through SearchChanged).</summary>
    public void Search(string text)
    {
        _search.Text = text;
        _search.CaretColumn = text.Length;
        SearchChanged?.Invoke(text);
    }

    public void ClearSearch()
    {
        _search.Text = "";
        SetResults(new List<SearchResult>());
    }

    /// <summary>Shows the selected system (and closes any open search results; the search text stays).</summary>
    public void ShowSelection(string name, GalaxyStar star, string sector, int companionCount)
    {
        _hasSelection = true;
        _resultsCard.Visible = false;
        UpdateCards();
        _selectionName.Text = name;
        string kind = star.Class.KindName;
        _selectionType.Text = star.Class.Kind is StarKind.NeutronStar or StarKind.BlackHole ? kind : $"{star.Class} · {kind}";
        foreach (Node child in _selectionRows.GetChildren())
        {
            _selectionRows.RemoveChild(child);
            child.QueueFree();
        }
        Ui.AddRow(_selectionRows, "Distance from Sol", Ui.Ly(star.Position.Length));
        Ui.AddRow(_selectionRows, "Location", sector.Length > 0 ? sector : "—", numeric: false);
        Ui.AddRow(_selectionRows, "Position", $"{Ui.Number(star.Position.X, "N1")}, {Ui.Number(star.Position.Y, "N1")}, {Ui.Number(star.Position.Z, "N1")}");
        if (star.Physics.Temperature > 0)
        {
            Ui.AddRow(_selectionRows, "Temperature", Ui.Temperature(star.Physics.Temperature));
        }
        Ui.AddRow(_selectionRows, "Mass", Ui.Times(star.Physics.Mass, "Sun"));
        Ui.AddRow(_selectionRows, "Radius", star.Physics.Radius < 0.01
            ? Ui.Number(star.Physics.Radius * 695700, "N0") + " km"
            : Ui.Times(star.Physics.Radius, "Sun"));
        if (star.Physics.Luminosity > 0)
        {
            Ui.AddRow(_selectionRows, "Luminosity", Ui.Times(star.Physics.Luminosity, "Sun", star.Physics.Luminosity < 0.01 ? "G3" : "N2"));
        }
        if (star.Physics.AbsoluteMagnitude < 40)
        {
            Ui.AddRow(_selectionRows, "Absolute magnitude", Ui.Magnitude(star.Physics.AbsoluteMagnitude));
        }
        if (companionCount > 0)
        {
            Ui.AddRow(_selectionRows, "Companion stars", companionCount.ToString(CultureInfo.InvariantCulture));
        }
        Ui.AddRow(_selectionRows, "Origin", star.IsCatalogue ? "Real star (HYG)" : "Generated", numeric: false);
        Ui.AddRow(_selectionRows, "Address", star.Address.ToString());
    }

    public void HideSelection()
    {
        _hasSelection = false;
        UpdateCards();
    }

    public void SetStatus(string text) => _status.Text = text;
}
