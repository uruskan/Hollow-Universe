using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Client;
using HollowUniverse.Common;

namespace HollowUniverse.Ground;

/// <summary>
/// What the ground game shows besides the RustMVP interface: other players' names over their heads (placed by
/// <see cref="Labeller"/>, nearest first, never overlapping), the network graph (F3), and a line while connecting or
/// after the connection is lost. Built with <see cref="Ui"/>.
/// </summary>
public partial class GroundOverlay : Control
{
    private PanelContainer _graphCard = null!;
    private readonly Dictionary<string, Label> _netRows = new();
    private NetGraph _graph = null!;
    private MapOverlay _names = null!;
    private Label _status = null!;

    public GameClient Client { get; set; } = null!;

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
    }

    /// <summary>Every frame: the status line, the graph and the names (<paramref name="names"/>: head point, name, distance).</summary>
    public void Refresh(Camera3D? camera, IEnumerable<(Vector3 Head, string Name, float Distance)> names, string? status)
    {
        _status.Text = status ?? "";
        _status.Visible = status != null;
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

    // Names above other players, nearest first; each takes a free spot or is left out.
    private void PlaceNames(Camera3D? camera, IEnumerable<(Vector3 Head, string Name, float Distance)> people)
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
        var visible = new List<(float Distance, Vector2 Screen, string Name)>();
        foreach ((Vector3 head, string name, float distance) in people)
        {
            if (!camera.IsPositionBehind(head))
            {
                visible.Add((distance, camera.UnprojectPosition(head), name));
            }
        }
        visible.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach ((float distance, Vector2 screen, string name) in visible)
        {
            var colour = new Color(Ui.Text, distance < 20 ? 1f : 0.7f);
            var text = new MapOverlay.Text(screen, name, colour, 15, Centred: true, MapLabel.Name);
            if (labeller.TryPlace(text))
            {
                _names.Texts.Add(text);
            }
        }
        _names.QueueRedraw();
    }
}
