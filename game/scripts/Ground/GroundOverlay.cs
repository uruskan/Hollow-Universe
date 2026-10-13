using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Client;
using HollowUniverse.Common;

namespace HollowUniverse.Ground;

/// <summary>
/// What the ground game shows besides the RustMVP interface: other players' names over their heads (placed by
/// <see cref="Labeller"/>, nearest first, never overlapping), the network graph (F3), a line while connecting or after
/// the connection is lost, a gun's crosshair at the hip (its spread cone), and the wounded screen with its countdown
/// (G4). Built with <see cref="Ui"/>.
/// </summary>
public partial class GroundOverlay : Control
{
    private PanelContainer _graphCard = null!;
    private readonly Dictionary<string, Label> _netRows = new();
    private NetGraph _graph = null!;
    private MapOverlay _names = null!;
    private Label _status = null!;
    private Crosshair _cross = null!;
    private ColorRect _wounded = null!;
    private Label _woundedLine = null!;

    public GameClient Client { get; set; } = null!;

    /// <summary>A gun's spread cone at the hip (degrees) for the crosshair; null shows none.</summary>
    public float? Spread { get; set; }

    /// <summary>The camera's vertical field of view (degrees), to size the crosshair.</summary>
    public float Fov { get; set; } = 75;

    /// <summary>Seconds left to bleed out while down; null when not wounded.</summary>
    public double? WoundedSeconds { get; set; }

    /// <summary>A reload's progress (0–1) under the centre; null when not reloading.</summary>
    public float? ReloadProgress { get; set; }

    public bool ShowNetGraph
    {
        get => _graphCard.Visible;
        set => _graphCard.Visible = value;
    }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _names = new MapOverlay();
        AddChild(_names);

        (PanelContainer card, VBoxContainer content) = Ui.Card(6);
        _graphCard = card;
        content.AddChild(Ui.Label("Network", TextStyle.Overline));
        GridContainer rows = Ui.Rows();
        foreach (string key in new[] { "Round trip", "Snapshots", "Lost", "Down", "Up", "Prediction error", "Input margin", "In view" })
        {
            _netRows[key] = Ui.AddRow(rows, key);
        }
        content.AddChild(rows);
        _graph = new NetGraph { Stats = Client.Stats };
        Ui.Size(_graph, 300, 84);
        content.AddChild(_graph);
        content.AddChild(NetGraph.Legend());
        Ui.Size(card, 340);
        Ui.Place(card, LayoutPreset.TopRight);
        card.Visible = false;
        AddChild(card);

        _status = Ui.Label("", TextStyle.Title);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.AddThemeConstantOverride("outline_size", Ui.Px(5));
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        Ui.Place(_status, LayoutPreset.Center);
        AddChild(_status);

        _cross = new Crosshair { MouseFilter = MouseFilterEnum.Ignore };
        _cross.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_cross);

        // Down: the world dims to a dark red, and the countdown (Rust's wounded screen).
        _wounded = new ColorRect { Color = new Color(0.22f, 0.02f, 0.02f, 0.45f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _wounded.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Label title = Ui.Label("WOUNDED", TextStyle.Title);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _woundedLine = Ui.Label("", TextStyle.Body);
        _woundedLine.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        column.AddChild(_woundedLine);
        _wounded.AddChild(column);
        AddChild(_wounded);
    }

    /// <summary>Every frame: the status line, the graph and the names (<paramref name="names"/>: head point, name, distance, teammate).</summary>
    public void Refresh(Camera3D? camera, IEnumerable<(Vector3 Head, string Name, float Distance, bool Team)> names, string? status)
    {
        _status.Text = status ?? "";
        _status.Visible = status != null;
        _cross.Spread = Spread;
        _cross.Fov = Fov;
        _cross.Reload = ReloadProgress;
        _cross.QueueRedraw();
        _wounded.Visible = WoundedSeconds != null;
        if (WoundedSeconds is { } left)
        {
            _woundedLine.Text = string.Create(CultureInfo.InvariantCulture,
                $"You will bleed out in {Math.Ceiling(left):0} s. Crawl to cover; another player can help you up.");
        }
        if (_graphCard.Visible)
        {
            UpdateNetGraph();
        }
        PlaceNames(camera, names);
    }

    private void UpdateNetGraph()
    {
        ClientStats s = Client.Stats;
        _netRows["Round trip"].Text = $"{Ui.Number(s.RoundTripMs)} ms";
        _netRows["Snapshots"].Text = $"{Ui.Number(s.SnapshotsPerSecond, "N1")} /s";
        _netRows["Lost"].Text = $"{Ui.Number(s.SnapshotLossPercent, "N1")} %";
        _netRows["Down"].Text = $"{Ui.Number(s.KilobitsIn, "N1")} kbit/s";
        _netRows["Up"].Text = $"{Ui.Number(s.KilobitsOut, "N1")} kbit/s";
        _netRows["Prediction error"].Text = string.Create(CultureInfo.InvariantCulture,
            $"{Ui.Number(s.PredictionErrorAverage * 1000, "N1")} mm avg, {Ui.Number(s.PredictionErrorMax * 1000, "N1")} max");
        _netRows["Input margin"].Text = $"{Ui.Number(s.InputMargin)} ticks";
        _netRows["In view"].Text = Ui.Number(Client.EntityCount);
        _graph.QueueRedraw();
    }

    // Names above other players, teammates' first (in the team's green, with how far beyond 40 m), then nearest first;
    // each takes a free spot or is left out.
    private void PlaceNames(Camera3D? camera, IEnumerable<(Vector3 Head, string Name, float Distance, bool Team)> people)
    {
        _names.Clear();
        if (camera == null)
        {
            _names.QueueRedraw();
            return;
        }
        var labeller = new Labeller(GetViewportRect().Size);
        if (_graphCard.Visible)
        {
            labeller.Block(_graphCard.GetGlobalRect());
        }
        var visible = new List<(float Distance, Vector2 Screen, string Name, bool Team)>();
        foreach ((Vector3 head, string name, float distance, bool team) in people)
        {
            if (!camera.IsPositionBehind(head))
            {
                visible.Add((distance, camera.UnprojectPosition(head), name, team));
            }
        }
        visible.Sort((a, b) => a.Team != b.Team ? (a.Team ? -1 : 1) : a.Distance.CompareTo(b.Distance));
        foreach ((float distance, Vector2 screen, string name, bool team) in visible)
        {
            var colour = team ? new Color(Ui.Ground.Green, distance < 120 ? 1f : 0.8f) : new Color(Ui.Text, distance < 20 ? 1f : 0.7f);
            string label = team && distance >= 40 ? string.Create(CultureInfo.InvariantCulture, $"{name}  {distance:0} m") : name;
            var text = new MapOverlay.Text(screen, label, colour, 15, Centred: true, MapLabel.Name);
            if (labeller.TryPlace(text))
            {
                _names.Texts.Add(text);
            }
        }
        _names.QueueRedraw();
    }

    // A gun's crosshair at the hip: four ticks around the centre, as far out as the spread cone reaches on screen.
    private sealed partial class Crosshair : Control
    {
        public float? Spread;
        public float Fov = 75;
        public float? Reload;

        public override void _Draw()
        {
            Vector2 centre = Size / 2;
            if (Reload is { } progress)
            {
                // A thin bar under the centre fills as the reload goes.
                var bar = new Rect2((centre + new Vector2(-Ui.Pxf(40), Ui.Pxf(28))).Round(), new Vector2(Ui.Px(80), Math.Max(2, Ui.Px(4))));
                DrawRect(bar, new Color(0, 0, 0, 0.45f));
                DrawRect(new Rect2(bar.Position, new Vector2(MathF.Round(bar.Size.X * Math.Clamp(progress, 0, 1)), bar.Size.Y)), Ui.Text);
            }
            if (Spread is not { } spread)
            {
                return;
            }
            float radius = MathF.Tan(Mathf.DegToRad(spread) / 2) / MathF.Tan(Mathf.DegToRad(Fov) / 2) * Size.Y / 2;
            float gap = Math.Max(radius, Ui.Pxf(5)), length = Ui.Pxf(8), width = Math.Max(1, Ui.Px(2));
            var shade = new Color(0, 0, 0, 0.45f);
            foreach (Vector2 d in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
            {
                Vector2 from = (centre + d * gap).Round(), to = (centre + d * (gap + length)).Round();
                DrawLine(from + Vector2.One, to + Vector2.One, shade, width);
                DrawLine(from, to, Ui.Text, width);
            }
        }
    }
}
