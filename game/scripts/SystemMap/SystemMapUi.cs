using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using HollowUniverse.Common;

namespace HollowUniverse.SystemMap;

/// <summary>The system map's interface: system summary, body list, selected body, time and scale controls.</summary>
public partial class SystemMapUi : Control
{
    private Label _name = null!;
    private Label _star = null!;
    private Label _counts = null!;
    private Label _origin = null!;
    private VBoxContainer _column = null!;
    private PanelContainer _summary = null!;
    private PanelContainer _bodies = null!;
    private VBoxContainer _list = null!;
    private ScrollContainer _scroll = null!;
    private readonly Dictionary<int, Button> _rows = new();
    private ButtonGroup _rowGroup = new();
    private PanelContainer _info = null!;
    private Label _infoName = null!;
    private Label _infoType = null!;
    private GridContainer _infoRows = null!;
    private Label _time = null!;
    private Label _speed = null!;
    private Button _pause = null!;
    private readonly List<Control> _panels = new();

    public event Action<int>? BodyChosen;
    public event Action? BackRequested;
    public event Action<int>? SpeedStep;
    public event Action? PauseToggled;

    public MapOverlay Overlay { get; private set; } = null!;

    public override void _Ready()
    {
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Overlay = new MapOverlay();
        AddChild(Overlay);

        BuildLeftColumn();
        BuildInfoCard();
        BuildTimeBar();
        BuildHints();
    }

    // Left: the system and its bodies, filling the height above the key hints.
    private void BuildLeftColumn()
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _column = column;
        Ui.Track(column, c =>
        {
            c.AddThemeConstantOverride("separation", Ui.Px(10));
            c.AnchorLeft = 0;
            c.AnchorRight = 0;
            c.AnchorTop = 0;
            c.AnchorBottom = 1;
            c.OffsetLeft = Ui.Px(20);
            c.OffsetRight = Ui.Px(20 + 350);
            c.OffsetTop = Ui.Px(20);
            c.OffsetBottom = -Ui.Px(84);
        });

        (PanelContainer summary, VBoxContainer content) = Ui.Card(4);
        _summary = summary;
        content.AddChild(Ui.Label("System map", TextStyle.Overline));
        _name = Ui.Label("", TextStyle.Title);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _star = Ui.Label("", TextStyle.Subtitle);
        _counts = Ui.Label("", TextStyle.Secondary);
        _origin = Ui.Label("", TextStyle.Caption);
        content.AddChild(_name);
        content.AddChild(_star);
        content.AddChild(_counts);
        content.AddChild(_origin);
        var spacer = new Control();
        Ui.Size(spacer, 0, 6);
        content.AddChild(spacer);
        var back = new Button { Text = "Galaxy map", Icon = UiIcons.Get(UiIcon.Back, 16), Alignment = HorizontalAlignment.Left };
        back.Pressed += () => BackRequested?.Invoke();
        content.AddChild(back);
        column.AddChild(summary);

        // The body list hugs its rows and scrolls once it would run into the key hints.
        (PanelContainer bodies, VBoxContainer bodiesContent) = Ui.Card(8);
        _bodies = bodies;
        bodiesContent.AddChild(Ui.Label("Bodies", TextStyle.Overline));
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };

        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Ui.Track(_list, c => c.AddThemeConstantOverride("separation", Ui.Px(1)));
        _scroll.AddChild(_list);
        bodiesContent.AddChild(_scroll);
        column.AddChild(bodies);

        AddChild(column);
        _panels.Add(summary);
        _panels.Add(bodies);
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

    // Refitting every frame is a handful of reads, and it settles by itself after anything moves: a new system, a
    // window resize, a new scale, or a title that wraps onto another line a frame late.
    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
        {
            FitBodies();
        }
    }

    private void FitBodies()
    {
        float rows = _list.GetCombinedMinimumSize().Y;
        float chrome = _bodies.GetCombinedMinimumSize().Y - _scroll.GetCombinedMinimumSize().Y;
        // The column's intended height comes from its anchors: once its content overflows, its actual size would
        // grow to fit and never shrink back.
        float column = Size.Y - _column.OffsetTop + _column.OffsetBottom;
        float available = column - _summary.GetCombinedMinimumSize().Y - Ui.Px(10) - chrome;
        float height = Math.Max(Ui.Px(64), Math.Min(rows, available));
        if (Math.Abs(_scroll.CustomMinimumSize.Y - height) > 0.5f)
        {
            _scroll.CustomMinimumSize = new Vector2(0, height);
        }
    }

    // Right: the selected body.
    private void BuildInfoCard()
    {
        VBoxContainer content;
        (_info, content) = Ui.Card(4);
        _info.Visible = false;
        Ui.Size(_info, 380);
        content.AddChild(Ui.Label("Selected body", TextStyle.Overline));
        _infoName = Ui.Label("", TextStyle.Title);
        _infoName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoType = Ui.Label("", TextStyle.Subtitle);
        content.AddChild(_infoName);
        content.AddChild(_infoType);
        content.AddChild(Ui.Separator());
        _infoRows = Ui.Rows();
        content.AddChild(_infoRows);
        AddChild(_info);
        Ui.Place(_info, LayoutPreset.TopRight);
        _panels.Add(_info);
    }

    // Top centre: time travel. (Distances are always compressed; the true-scale switch was removed on 14 October 2026.)
    private void BuildTimeBar()
    {
        var bar = new PanelContainer();
        Ui.Track(bar, c => c.AddThemeStyleboxOverride("panel", BarStyle()));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        Ui.Track(row, c => c.AddThemeConstantOverride("separation", Ui.Px(6)));

        Button slower = IconButton(UiIcon.Slower, "Slower  ( , )");
        slower.Pressed += () => SpeedStep?.Invoke(-1);
        _pause = IconButton(UiIcon.Pause, "Pause  (Space)");
        _pause.Pressed += () => PauseToggled?.Invoke();
        Button faster = IconButton(UiIcon.Faster, "Faster  ( . )");
        faster.Pressed += () => SpeedStep?.Invoke(1);
        row.AddChild(slower);
        row.AddChild(_pause);
        row.AddChild(faster);

        var clock = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        Ui.Track(clock, c =>
        {
            c.AddThemeConstantOverride("separation", 0);
            // As wide as the widest date, so the bar keeps its width while the clock runs.
            float widest = Ui.Tabular.GetStringSize("28 May 2026  00:00 UTC", HorizontalAlignment.Left, -1, Ui.SizeOf(TextStyle.Heading)).X;
            c.CustomMinimumSize = new Vector2(MathF.Ceiling(widest) + Ui.Px(16), 0);
        });
        _time = Ui.Label("", TextStyle.Heading);
        _time.AddThemeFontOverride("font", Ui.Tabular);
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        _speed = Ui.Label("", TextStyle.Caption);
        _speed.HorizontalAlignment = HorizontalAlignment.Center;
        clock.AddChild(_time);
        clock.AddChild(_speed);
        row.AddChild(clock);

        bar.AddChild(row);
        AddChild(bar);
        Ui.Place(bar, LayoutPreset.CenterTop);
        _panels.Add(bar);
    }

    private void BuildHints()
    {
        var hints = new PanelContainer();
        Ui.Track(hints, c => c.AddThemeStyleboxOverride("panel", BarStyle()));
        var line = new HBoxContainer();
        Ui.Track(line, c => c.AddThemeConstantOverride("separation", Ui.Px(16)));
        foreach ((string keys, string action) in new[]
                 {
                     ("Drag", "Rotate"), ("Wheel", "Zoom"), ("Click", "Select"), ("Double-click", "Focus"),
                     ("Space", "Pause"), (",|.", "Time speed"), ("Esc", "Galaxy map"),
                 })
        {
            line.AddChild(Ui.KeyHint(keys, action));
        }
        hints.AddChild(line);
        AddChild(hints);
        Ui.Place(hints, LayoutPreset.BottomLeft);
        _panels.Add(hints);
    }

    private static StyleBoxFlat BarStyle()
    {
        StyleBoxFlat style = Ui.Box(Ui.Panel, 10, Ui.PanelBorder, 10, 8);
        style.ShadowColor = Ui.Shadow;
        style.ShadowSize = Ui.Px(14);
        style.ShadowOffset = new Vector2(0, Ui.Px(3));
        return style;
    }

    private static Button IconButton(UiIcon icon, string tooltip) => new()
    {
        Icon = UiIcons.Get(icon, 18),
        ThemeTypeVariation = "IconButton",
        TooltipText = tooltip,
        IconAlignment = HorizontalAlignment.Center,
    };

    public void ShowSystem(StarSystem system, double distanceFromSol)
    {
        _name.Text = system.Name;
        GalaxyStar primary = system.Primary;
        _star.Text = $"{system.PrimaryStar.StarClass} · {system.PrimaryStar.StarClass.KindName}";
        int stars = system.Count(BodyKind.Star);
        int planets = system.Count(BodyKind.Planet);
        int moons = system.Count(BodyKind.Moon);
        int belts = system.Count(BodyKind.Belt);
        var parts = new List<string> { Plural(stars, "star"), Plural(planets, "planet"), Plural(moons, "moon") };
        if (belts > 0)
        {
            parts.Add(Plural(belts, "belt"));
        }
        _counts.Text = string.Join(" · ", parts);
        _origin.Text = $"{Ui.Ly(distanceFromSol)} from Sol · {(primary.IsCatalogue ? "real star" : "generated")}";

        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();
        _rowGroup = new ButtonGroup { AllowUnpress = true };
        foreach (Body body in TreeOrder(system))
        {
            Button row = BodyRow(system, body);
            _rows[body.Index] = row;
            _list.AddChild(row);
        }
    }

    // A row of the body list: indentation, an icon in the body's colour, its short name and its type.
    private Button BodyRow(StarSystem system, Body body)
    {
        int depth = Depth(system, body);
        bool moon = body.Kind == BodyKind.Moon;
        var row = new Button { ThemeTypeVariation = "ListButton", ToggleMode = true, ButtonGroup = _rowGroup };
        var content = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var icon = new TextureRect
        {
            Texture = UiIcons.Get(IconOf(body), moon ? 14 : 16),
            Modulate = ColourOf(body),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        Label name = Ui.Label(ShortName(system, body), moon ? TextStyle.Secondary : TextStyle.Body);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.MouseFilter = MouseFilterEnum.Ignore;
        Label kind = Ui.Label(body.Kind == BodyKind.Star ? body.StarClass.ToString() : body.TypeName, TextStyle.Caption);
        kind.VerticalAlignment = VerticalAlignment.Center;
        kind.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(icon);
        content.AddChild(name);
        content.AddChild(kind);
        row.AddChild(content);
        Ui.Track(row, r =>
        {
            r.CustomMinimumSize = new Vector2(0, Ui.Px(moon ? 30 : 34));
            content.OffsetLeft = Ui.Px(8 + 16 * depth);
            content.OffsetRight = -Ui.Px(10);
            content.AddThemeConstantOverride("separation", Ui.Px(8));
            icon.CustomMinimumSize = new Vector2(Ui.Px(16), 0);
        });
        int index = body.Index;
        row.Pressed += () => BodyChosen?.Invoke(index);
        return row;
    }

    /// <summary>Highlights a body in the list (also when it was picked on the map) and scrolls it into view.</summary>
    public void SelectBody(int index)
    {
        if (_rows.TryGetValue(index, out Button? row))
        {
            row.SetPressedNoSignal(true);
            _scroll.EnsureControlVisible(row);
        }
    }

    public void ShowBody(StarSystem system, Body body)
    {
        _info.Visible = true;
        _infoName.Text = body.Name;
        _infoType.Text = body.Kind == BodyKind.Star ? $"{body.StarClass} · {body.StarClass.KindName}" : body.TypeName;
        _infoType.AddThemeColorOverride("font_color", body.Kind == BodyKind.Star ? Ui.Warm : ColourOf(body));
        foreach (Node child in _infoRows.GetChildren())
        {
            _infoRows.RemoveChild(child);
            child.QueueFree();
        }
        if (body.Parent >= 0)
        {
            Row("Orbits", system.Bodies[body.Parent].Name, numeric: false);
            Row("Semi-major axis", Ui.Distance(body.Orbit.SemiMajorAxis));
            if (body.Kind != BodyKind.Belt)
            {
                Row("Orbital period", Ui.Duration(body.Orbit.Period));
                Row("Eccentricity", Ui.Number(body.Orbit.Eccentricity, "0.000"));
                Row("Inclination", Ui.Number(body.Orbit.Inclination * 180 / Math.PI, "0.0") + "°");
            }
            else
            {
                Row("Width", Ui.Distance(body.BeltWidth));
                Row("Material", BodyText.MaterialName(body.BeltMaterial), numeric: false);
                Row("Mass", Ui.Times(body.Mass / Units.EarthMass, "Earth", "G2"));
            }
        }
        if (body.Kind == BodyKind.Barycentre)
        {
            Row("Mass", Ui.Times(body.Mass / Units.SolarMass, "Sun"));
            var members = new List<string>();
            foreach (int child in body.Children)
            {
                Body member = system.Bodies[child];
                if (member.Kind is BodyKind.Star or BodyKind.Barycentre)
                {
                    members.Add(ShortName(system, member));
                }
            }
            Row("Members", string.Join(" and ", members), numeric: false);
            Row("Age", Ui.Age(system.Age));
        }
        else if (body.Kind == BodyKind.Star)
        {
            Row("Mass", Ui.Times(body.Mass / Units.SolarMass, "Sun"));
            Row("Radius", body.Radius < 0.01 * Units.SolarRadius
                ? Ui.Number(body.Radius / 1000, "N0") + " km"
                : Ui.Times(body.Radius / Units.SolarRadius, "Sun"));
            if (body.Temperature > 0)
            {
                Row("Temperature", Ui.Temperature(body.Temperature));
            }
            if (body.Luminosity > 0)
            {
                Row("Luminosity", Ui.Times(body.Luminosity, "Sun", body.Luminosity < 0.01 ? "G3" : "N2"));
            }
            Row("Age", Ui.Age(system.Age));
        }
        else if (body.Kind is BodyKind.Planet or BodyKind.Moon)
        {
            double playableRadius = GameScale.PlanetRadius(body);
            Row("Playable radius", Ui.Number(playableRadius / 1000, "N3") + " km");
            Row("Total surface area", Ui.Number(GameScale.SurfaceArea(playableRadius) / 1e6, "N2") + " km²");
            double earthMasses = body.Mass / Units.EarthMass;
            Row("Mass", earthMasses < 0.001
                ? body.Mass.ToString("0.00e+0", CultureInfo.InvariantCulture) + " kg"
                : Ui.Times(earthMasses, "Earth", earthMasses < 10 ? "N3" : "N1"));
            Row("Physical reference radius", Ui.Number(body.Radius / 1000, "N0") + " km");
            Row("Gravity", Ui.Number(body.SurfaceGravity, "N2") + " g");
            Row("Temperature", Ui.Temperature(body.Temperature));
            PlanetState? state = body.State;
            if (state != null && state.MaxTemperature - state.MinTemperature > 20)
            {
                Row("Day / night", Ui.Number(state.MaxTemperature, "N0") + " / " + Ui.Temperature(state.MinTemperature));
            }
            Row("Atmosphere", AtmosphereName(body), numeric: false);
            if (body.RotationPeriod != 0)
            {
                Row("Day length", body.TidallyLocked ? "Tidally locked" : Ui.Duration(body.RotationPeriod), !body.TidallyLocked);
            }
            Row("Axial tilt", Ui.Number(body.AxialTilt * 180 / Math.PI, "0.0") + "°");
            if (state != null)
            {
                Row("Surface", BodyText.Surface(state), numeric: false);
                Row("Made of", BodyText.Composition(state.Composition), numeric: false);
                if (state.Surface != SurfaceKind.None)
                {
                    Row("Geology", BodyText.Geology(state), numeric: false);
                    Row("Heat flow", Ui.Number(state.HeatFlow, state.HeatFlow < 0.1 ? "0.000" : "N2") + " W/m²"
                        + (state.TidalHeating > 0.5 * state.HeatFlow ? " (tides)" : ""));
                }
                if (state.MagneticField >= 0.01)
                {
                    Row("Magnetic field", Ui.Times(state.MagneticField, "Earth", state.MagneticField < 10 ? "N2" : "N0"));
                }
                if (state.SubsurfaceOcean)
                {
                    Row("Under the ice", "Liquid water ocean", numeric: false);
                }
                if (state.Biosphere != Biosphere.None)
                {
                    Row("Life", BodyText.Life(state.Biosphere), numeric: false);
                }
                Row("Hazards", BodyText.HazardList(state.Hazards), numeric: false);
            }
            if (body.HasRings)
            {
                Row("Rings", body.RingMaterial != Hollow.Galaxy.Systems.Material.None ? BodyText.MaterialName(body.RingMaterial) : "Yes", numeric: false);
            }
            Row("Landable", body.Landable ? "Yes" : "No", numeric: false);
        }
        int moons = 0;
        foreach (int child in body.Children)
        {
            moons += system.Bodies[child].Kind == BodyKind.Moon ? 1 : 0;
        }
        if (moons > 0)
        {
            Row("Moons", moons.ToString(CultureInfo.InvariantCulture));
        }
        SelectBody(body.Index);
    }

    public void HideBody()
    {
        _info.Visible = false;
        foreach (Button row in _rows.Values)
        {
            row.SetPressedNoSignal(false);
        }
    }

    public void SetTime(DateTime utc, string speed, bool paused)
    {
        _time.Text = utc.ToString("d MMM yyyy  HH:mm", CultureInfo.InvariantCulture) + " UTC";
        _speed.Text = paused ? "Paused" : speed;
        _pause.Icon = UiIcons.Get(paused ? UiIcon.Play : UiIcon.Pause, 18);
        _pause.TooltipText = paused ? "Play  (Space)" : "Pause  (Space)";
    }

    private static UiIcon IconOf(Body body) => body.Kind switch
    {
        BodyKind.Star => UiIcon.Star,
        BodyKind.Belt => UiIcon.Belt,
        BodyKind.Moon => UiIcon.Moon,
        BodyKind.Barycentre => UiIcon.Barycentre,
        _ => UiIcon.Planet,
    };

    /// <summary>The colour a body is shown in: stars by temperature, planets by type.</summary>
    public static Color ColourOf(Body body)
    {
        if (body.Kind == BodyKind.Star)
        {
            return StarColors.For(body.StarClass, (float)body.Temperature).Lerp(Ui.Warm, 0.25f);
        }
        if (body.Kind == BodyKind.Barycentre)
        {
            return Ui.Secondary;
        }
        return body.PlanetType switch
        {
            PlanetType.EarthLike => new Color(0.45f, 0.85f, 0.65f),
            PlanetType.WaterWorld => new Color(0.40f, 0.65f, 1.00f),
            PlanetType.GasGiant => new Color(0.95f, 0.78f, 0.55f),
            PlanetType.IceGiant => new Color(0.55f, 0.85f, 0.95f),
            PlanetType.AmmoniaWorld => new Color(0.85f, 0.75f, 0.45f),
            PlanetType.MetalRich => new Color(0.95f, 0.60f, 0.40f),
            PlanetType.LavaWorld => new Color(1.00f, 0.50f, 0.28f),
            PlanetType.GasDwarf => new Color(0.62f, 0.82f, 0.84f),
            PlanetType.Icy or PlanetType.RockyIce => new Color(0.78f, 0.86f, 0.94f),
            PlanetType.AsteroidBelt => Ui.Tertiary,
            _ => body.Kind == BodyKind.Moon ? Ui.Secondary : new Color(0.75f, 0.72f, 0.68f),
        };
    }

    /// <summary>
    /// A body's name without the system's name in front ("A 4" for "Nolcas Peris BF-BA 5-0 A 4", "B" for its star B);
    /// a single star keeps its name.
    /// </summary>
    public static string ShortName(StarSystem system, Body body) =>
        body.Name.StartsWith(system.Name + " ", StringComparison.Ordinal)
            ? body.Name[(system.Name.Length + 1)..]
            : body.Name;

    // The atmosphere by its gases and pressure, e.g. "Thick carbon dioxide, 92 atm"; a giant's has no surface.
    private static string AtmosphereName(Body body)
    {
        if (body.Atmosphere == AtmosphereType.None || body.State is { Surface: SurfaceKind.None })
        {
            return body.AtmosphereName;
        }
        double p = body.SurfacePressure;
        return body.AtmosphereName + ", " + Ui.Number(p, p < 0.01 ? "G2" : p < 10 ? "N2" : "N0") + " atm";
    }

    // Bodies in tree order: each star or planet followed by what orbits it, inner orbits first.
    private static IEnumerable<Body> TreeOrder(StarSystem system)
    {
        var stack = new Stack<Body>();
        stack.Push(system.Root);
        while (stack.Count > 0)
        {
            Body body = stack.Pop();
            yield return body;
            var children = new List<Body>();
            foreach (int child in body.Children)
            {
                children.Add(system.Bodies[child]);
            }
            children.Sort((a, b) => b.Orbit.SemiMajorAxis.CompareTo(a.Orbit.SemiMajorAxis));
            foreach (Body child in children)
            {
                stack.Push(child);
            }
        }
    }

    private static int Depth(StarSystem system, Body body)
    {
        int depth = 0;
        int parent = body.Parent;
        while (parent > 0)
        {
            depth++;
            parent = system.Bodies[parent].Parent;
        }
        return body.Index == 0 ? 0 : depth + 1;
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private void Row(string key, string value, bool numeric = true) => Ui.AddRow(_infoRows, key, value, numeric);
}
