using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Movement;
using HollowUniverse.Common;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game's placeholder interface, built with <see cref="Ui"/> (AGENTS.md): who and where you are, the
/// network graph (F3), chat (Enter), key hints, a crosshair, and other players' names placed by <see cref="Labeller"/>
/// so they never overlap each other or the panels. The RustMVP HUD replaces it in G2 (GROUND.md §8).
/// </summary>
public partial class GroundHud : Control
{
    private const int ChatLines = 8;
    private const double ChatFadeSeconds = 12;

    private readonly List<(Label Line, double Shown)> _chat = new();
    private PanelContainer _status = null!;
    private Label _title = null!;
    private Label _who = null!;
    private Label _health = null!;
    private Label _state = null!;
    private PanelContainer _graphCard = null!;
    private readonly Dictionary<string, Label> _netRows = new();
    private NetGraph _graph = null!;
    private VBoxContainer _chatBox = null!;
    private LineEdit _chatInput = null!;
    private Control _hints = null!;
    private MapOverlay _names = null!;
    private double _time;
    private bool _showNetGraph;

    public GameClient Client { get; set; } = null!;

    public int? HostedPort { get; set; }

    public bool IsChatting => _chatInput.Visible;

    public event Action? ChatOpened;

    public event Action? ChatClosed;

    /// <summary>A chat line starting with '/' (a test command; not sent as chat).</summary>
    public event Action<string>? CommandEntered;

    public bool ShowNetGraph
    {
        get => _showNetGraph;
        set
        {
            _showNetGraph = value;
            if (_graphCard != null)
            {
                _graphCard.Visible = value;
            }
        }
    }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _names = new MapOverlay();
        AddChild(_names);
        AddChild(new Crosshair());
        BuildStatus();
        BuildNetGraph();
        BuildChat();
        BuildHints();
        Client.ChatReceived += OnChat;
    }

    public override void _ExitTree() => Client.ChatReceived -= OnChat;

    private void BuildStatus()
    {
        (PanelContainer card, VBoxContainer content) = Ui.Card(4);
        _status = card;
        content.AddChild(Ui.Label("On foot", TextStyle.Overline));
        _title = Ui.Label("Connecting", TextStyle.Title);
        content.AddChild(_title);
        _who = Ui.Label("", TextStyle.Secondary);
        content.AddChild(_who);
        GridContainer rows = Ui.Rows();
        _health = Ui.AddRow(rows, "Health");
        _state = Ui.AddRow(rows, "Moving", numeric: false);
        content.AddChild(rows);
        Ui.Size(card, 300);
        Ui.Place(card, LayoutPreset.TopLeft);
        AddChild(card);
    }

    private void BuildNetGraph()
    {
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
        card.Visible = _showNetGraph;
        AddChild(card);
    }

    private void BuildChat()
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        Ui.Track(column, c => c.AddThemeConstantOverride("separation", Ui.Px(4)));
        _chatBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        Ui.Track(_chatBox, c => c.AddThemeConstantOverride("separation", Ui.Px(2)));
        column.AddChild(_chatBox);
        _chatInput = new LineEdit { PlaceholderText = "Say something (Enter sends, Esc cancels)", Visible = false, MaxLength = 200 };
        Ui.Size(_chatInput, 420);
        _chatInput.TextSubmitted += text =>
        {
            if (text.StartsWith('/'))
            {
                CommandEntered?.Invoke(text);
            }
            else
            {
                Client.SendChat(text);
            }
            CloseChat();
        };
        column.AddChild(_chatInput);
        Ui.Size(column, 440);
        // Above the key hints, at the bottom left.
        Ui.Track(column, c =>
        {
            c.AnchorLeft = 0;
            c.AnchorRight = 0;
            c.AnchorTop = 1;
            c.AnchorBottom = 1;
            c.GrowVertical = GrowDirection.Begin;
            c.OffsetLeft = Ui.Px(20);
            c.OffsetBottom = -Ui.Px(64);
        });
        AddChild(column);
    }

    // A bar of key hints at the bottom left, as on the maps.
    private void BuildHints()
    {
        var hints = new PanelContainer();
        Ui.Track(hints, c =>
        {
            StyleBoxFlat style = Ui.Box(Ui.Panel, 10, Ui.PanelBorder, 10, 8);
            style.ShadowColor = Ui.Shadow;
            style.ShadowSize = Ui.Px(14);
            style.ShadowOffset = new Vector2(0, Ui.Px(3));
            c.AddThemeStyleboxOverride("panel", style);
        });
        var line = new HBoxContainer();
        Ui.Track(line, c => c.AddThemeConstantOverride("separation", Ui.Px(16)));
        foreach ((string keys, string action) in new[]
                 {
                     ("W|A|S|D", "Move"), ("Shift", "Sprint"), ("Space", "Jump"), ("1-6", "Belt"), ("LMB", "Use"), ("RMB", "Aim"),
                     ("E", "Interact"), ("F", "Place"), ("G", "Drop"), ("H", "Eat"), ("Enter", "Chat"), ("F3", "Network"),
                 })
        {
            line.AddChild(Ui.KeyHint(keys, action));
        }
        hints.AddChild(line);
        _hints = hints;
        AddChild(hints);
        Ui.Place(hints, LayoutPreset.BottomLeft);
    }

    public void OpenChat()
    {
        _chatInput.Visible = true;
        _chatInput.Text = "";
        _chatInput.GrabFocus();
        ChatOpened?.Invoke();
    }

    private void CloseChat()
    {
        _chatInput.Visible = false;
        _chatInput.ReleaseFocus();
        ChatClosed?.Invoke();
    }

    public override void _Input(InputEvent e)
    {
        if (IsChatting && e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
        {
            CloseChat();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnChat(ChatLine line)
    {
        string text = line.Kind == ChatKind.Server ? $"[server] {line.Text}" : $"{line.From}: {line.Text}";
        Label label = Ui.Label(text, TextStyle.Body, line.Kind == ChatKind.Server ? Ui.Warm : Ui.Text);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeConstantOverride("outline_size", Ui.Px(4));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
        _chatBox.AddChild(label);
        _chat.Add((label, _time));
        while (_chat.Count > ChatLines)
        {
            _chat[0].Line.QueueFree();
            _chat.RemoveAt(0);
        }
    }

    /// <summary>Updates every frame: the cards, the chat's fading and the name labels.</summary>
    public void Refresh(Camera3D camera, IEnumerable<Mannequin> people)
    {
        _time += GetProcessDeltaTime();
        UpdateStatus();
        if (_graphCard.Visible)
        {
            UpdateNetGraph();
        }
        foreach ((Label line, double shown) in _chat)
        {
            double age = _time - shown;
            line.Modulate = new Color(1, 1, 1, IsChatting ? 1f : (float)Math.Clamp((ChatFadeSeconds - age) / 2, 0, 1));
        }
        PlaceNames(camera, people);
    }

    private void UpdateStatus()
    {
        _title.Text = Client.State switch
        {
            ClientState.Playing => Client.ServerName,
            ClientState.Disconnected => "Disconnected",
            ClientState.Joining => "Joining",
            _ => "Connecting",
        };
        _who.Text = Client.State == ClientState.Disconnected
            ? Client.DisconnectReason ?? ""
            : HostedPort is int port ? $"{Client.Name}, hosting on port {port}" : Client.Name;
        CharacterState c = Client.Character;
        _health.Text = Client.HasCharacter ? Ui.Number(c.Health) : "—";
        _state.Text = !Client.HasCharacter ? "—"
            : (c.Flags & MoveFlags.Dead) != 0 ? "Dead (back in a moment)"
            : (c.Flags & MoveFlags.Swimming) != 0 ? "Swimming"
            : (c.Flags & MoveFlags.OnGround) == 0 ? "In the air"
            : c.Stance == Stance.Crouching ? "Crouching"
            : (c.Flags & MoveFlags.Sprinting) != 0 ? "Sprinting"
            : "On foot";
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
    private void PlaceNames(Camera3D camera, IEnumerable<Mannequin> people)
    {
        _names.Clear();
        var labeller = new Labeller(GetViewportRect().Size);
        foreach (Control panel in new Control[] { _status, _graphCard, _hints })
        {
            if (panel.Visible)
            {
                labeller.Block(panel.GetGlobalRect());
            }
        }
        var visible = new List<(float Distance, Vector2 Screen, string Name)>();
        foreach (Mannequin person in people)
        {
            if (!person.Visible || camera.IsPositionBehind(person.LabelPoint))
            {
                continue;
            }
            float distance = camera.GlobalPosition.DistanceTo(person.LabelPoint);
            if (distance < 250)
            {
                visible.Add((distance, camera.UnprojectPosition(person.LabelPoint), person.PlayerName));
            }
        }
        visible.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        foreach ((float distance, Vector2 screen, string name) in visible)
        {
            var colour = new Color(Ui.Text, distance < 60 ? 1f : 0.75f);
            var text = new MapOverlay.Text(screen, name, colour, 15, Centred: true, MapLabel.Name);
            if (labeller.TryPlace(text))
            {
                _names.Texts.Add(text);
            }
        }
        _names.QueueRedraw();
    }
}
