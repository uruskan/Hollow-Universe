using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using HollowUniverse.Common;
using HollowUniverse.GalaxyMap;

namespace HollowUniverse.SystemMap;

/// <summary>The system map's interface: system summary, body list, selected body, time controls.</summary>
public partial class SystemMapUi : Control
{
    private Label _name = null!;
    private Label _star = null!;
    private Label _counts = null!;
    private VBoxContainer _list = null!;
    private PanelContainer _info = null!;
    private Label _infoName = null!;
    private Label _infoType = null!;
    private GridContainer _rows = null!;
    private Label _time = null!;
    private Label _speed = null!;
    private Label _scale = null!;

    public event Action<int>? BodyChosen;
    public event Action? BackRequested;
    public event Action<int>? SpeedStep;
    public event Action? PauseToggled;
    public event Action? ScaleToggled;

    public MapOverlay Overlay { get; private set; } = null!;

    public override void _Ready()
    {
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Overlay = new MapOverlay();
        AddChild(Overlay);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        left.AddThemeConstantOverride("separation", 10);
        var summary = new PanelContainer();
        var summaryBox = new VBoxContainer();
        summaryBox.AddThemeConstantOverride("separation", 2);
        summaryBox.AddChild(Ui.Heading("System map", 12));
        _name = Ui.Label("", 22, Ui.Text);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _star = Ui.Label("", 14, Ui.Warm);
        _counts = Ui.Label("", 13, Ui.Dim);
        summaryBox.AddChild(_name);
        summaryBox.AddChild(_star);
        summaryBox.AddChild(_counts);
        var back = new Button { Text = "‹  Galaxy map   (Esc)" };
        back.Pressed += () => BackRequested?.Invoke();
        summaryBox.AddChild(back);
        summary.AddChild(summaryBox);
        left.AddChild(summary);

        var listPanel = new PanelContainer();
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 430), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 1);
        scroll.AddChild(_list);
        listPanel.AddChild(scroll);
        left.AddChild(listPanel);
        AddChild(left);
        Ui.Place(left, LayoutPreset.TopLeft);

        _info = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(340, 0) };
        var infoBox = new VBoxContainer();
        infoBox.AddThemeConstantOverride("separation", 4);
        infoBox.AddChild(Ui.Heading("Selected body", 12));
        _infoName = Ui.Label("", 22, Ui.Text);
        _infoName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoType = Ui.Label("", 14, Ui.Warm);
        _rows = new GridContainer { Columns = 2 };
        _rows.AddThemeConstantOverride("h_separation", 16);
        _rows.AddThemeConstantOverride("v_separation", 3);
        infoBox.AddChild(_infoName);
        infoBox.AddChild(_infoType);
        infoBox.AddChild(new HSeparator());
        infoBox.AddChild(_rows);
        _info.AddChild(infoBox);
        AddChild(_info);
        Ui.Place(_info, LayoutPreset.TopRight);

        var timePanel = new PanelContainer { CustomMinimumSize = new Vector2(440, 0) };
        var timeBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        timeBox.AddThemeConstantOverride("separation", 8);
        var slower = new Button { Text = "«" };
        slower.Pressed += () => SpeedStep?.Invoke(-1);
        var pause = new Button { Text = "❚❚" };
        pause.Pressed += () => PauseToggled?.Invoke();
        var faster = new Button { Text = "»" };
        faster.Pressed += () => SpeedStep?.Invoke(1);
        var labels = new VBoxContainer();
        labels.AddThemeConstantOverride("separation", 0);
        _time = Ui.Label("", 15, Ui.Text);
        _speed = Ui.Label("", 12, Ui.Dim);
        labels.AddChild(_time);
        labels.AddChild(_speed);
        var scale = new Button { Text = "Scale" };
        scale.Pressed += () => ScaleToggled?.Invoke();
        _scale = Ui.Label("", 12, Ui.Dim);
        timeBox.AddChild(slower);
        timeBox.AddChild(pause);
        timeBox.AddChild(faster);
        timeBox.AddChild(labels);
        timeBox.AddChild(scale);
        timeBox.AddChild(_scale);
        timePanel.AddChild(timeBox);
        AddChild(timePanel);
        Ui.Place(timePanel, LayoutPreset.CenterTop);

        var help = new PanelContainer();
        help.AddChild(Ui.Label(
            "Drag: rotate   ·   Wheel: zoom   ·   Click: select   ·   Double-click: focus   ·   Space: pause   ·   , / . : time speed   ·   L: scale   ·   Esc: galaxy map",
            13, Ui.Dim));
        AddChild(help);
        Ui.Place(help, LayoutPreset.BottomLeft);
    }

    public void ShowSystem(StarSystem system, double distanceFromSol)
    {
        _name.Text = system.Name;
        GalaxyStar primary = system.Primary;
        _star.Text = $"{system.Root.StarClass} · {system.Root.StarClass.KindName}";
        int stars = system.Count(BodyKind.Star);
        int planets = system.Count(BodyKind.Planet);
        int moons = system.Count(BodyKind.Moon);
        int belts = system.Count(BodyKind.Belt);
        var parts = new List<string> { Plural(stars, "star"), Plural(planets, "planet"), Plural(moons, "moon") };
        if (belts > 0)
        {
            parts.Add(Plural(belts, "belt"));
        }
        _counts.Text = string.Join(" · ", parts) + $"\n{Ui.Ly(distanceFromSol)} from Sol" + (primary.IsCatalogue ? " · real star" : " · generated");

        foreach (Node child in _list.GetChildren())
        {
            child.QueueFree();
        }
        foreach (Body body in TreeOrder(system))
        {
            int depth = Depth(system, body);
            string icon = body.Kind switch
            {
                BodyKind.Star => "✦",
                BodyKind.Belt => "⁘",
                BodyKind.Moon => "·",
                _ => "●",
            };
            string label = $"{new string(' ', depth * 4)}{icon}  {body.Name}";
            var button = new Button { Text = label, Alignment = HorizontalAlignment.Left, Flat = true, ClipText = true };
            button.AddThemeFontSizeOverride("font_size", body.Kind == BodyKind.Moon ? 12 : 14);
            button.AddThemeColorOverride("font_color", body.Kind == BodyKind.Moon ? Ui.Dim : Ui.Text);
            int index = body.Index;
            button.Pressed += () => BodyChosen?.Invoke(index);
            _list.AddChild(button);
        }
    }

    public void ShowBody(StarSystem system, Body body)
    {
        _info.Visible = true;
        _infoName.Text = body.Name;
        _infoType.Text = body.Kind == BodyKind.Star ? $"{body.StarClass} · {body.StarClass.KindName}" : body.TypeName;
        foreach (Node child in _rows.GetChildren())
        {
            child.QueueFree();
        }
        if (body.Parent >= 0)
        {
            Row("Orbits", system.Bodies[body.Parent].Name);
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
            }
        }
        if (body.Kind == BodyKind.Star)
        {
            Row("Mass", Ui.Number(body.Mass / Units.SolarMass, "N2") + " M☉");
            Row("Radius", body.Radius < 0.01 * Units.SolarRadius
                ? Ui.Number(body.Radius / 1000, "N0") + " km"
                : Ui.Number(body.Radius / Units.SolarRadius, "N2") + " R☉");
            if (body.Temperature > 0)
            {
                Row("Temperature", Ui.Temperature(body.Temperature));
            }
            if (body.Luminosity > 0)
            {
                Row("Luminosity", Ui.Number(body.Luminosity, body.Luminosity < 0.01 ? "G3" : "N2") + " L☉");
            }
        }
        else if (body.Kind != BodyKind.Belt)
        {
            double earthMasses = body.Mass / Units.EarthMass;
            Row("Mass", earthMasses < 0.001 ? body.Mass.ToString("0.00e+0", CultureInfo.InvariantCulture) + " kg" : Ui.Number(earthMasses, earthMasses < 10 ? "N3" : "N1") + " M⊕");
            Row("Radius", Ui.Number(body.Radius / 1000, "N0") + " km");
            Row("Gravity", Ui.Number(body.SurfaceGravity, "N2") + " g");
            Row("Temperature", Ui.Temperature(body.Temperature));
            Row("Atmosphere", AtmosphereName(body));
            if (body.RotationPeriod != 0)
            {
                Row("Day length", body.TidallyLocked ? "Tidally locked" : Ui.Duration(body.RotationPeriod));
            }
            Row("Axial tilt", Ui.Number(body.AxialTilt * 180 / Math.PI, "0.0") + "°");
            if (body.HasRings)
            {
                Row("Rings", "Yes");
            }
            Row("Landable", body.Landable ? "Yes" : "No");
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
    }

    public void HideBody() => _info.Visible = false;

    public void SetTime(DateTime utc, string speed, bool logScale)
    {
        _time.Text = utc.ToString("yyyy-MM-dd  HH:mm 'UTC'", CultureInfo.InvariantCulture);
        _speed.Text = speed;
        _scale.Text = logScale ? "compressed" : "true scale";
    }

    private static string AtmosphereName(Body body) => body.Atmosphere switch
    {
        AtmosphereType.None => "None",
        AtmosphereType.Thin => $"Thin ({Ui.Number(body.SurfacePressure, body.SurfacePressure < 0.01 ? "G2" : "N2")} atm)",
        AtmosphereType.Thick => $"Thick ({Ui.Number(body.SurfacePressure, "N1")} atm)",
        AtmosphereType.Breathable => $"Breathable ({Ui.Number(body.SurfacePressure, "N2")} atm)",
        AtmosphereType.HydrogenHelium => "Hydrogen–helium",
        _ => "Unknown",
    };

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
        return body.Kind == BodyKind.Star && body.Index == 0 ? 0 : depth + 1;
    }

    private static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private void Row(string key, string value)
    {
        _rows.AddChild(Ui.Label(key, 13, Ui.Dim));
        _rows.AddChild(Ui.Label(value, 13, Ui.Text));
    }
}
