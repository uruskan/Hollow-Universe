using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Net.Transport;
using Hollow.Server;
using HollowUniverse.App;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game in the client (GROUND.md §4.3): connects to a server (<c>--connect host:port</c>), or hosts one in
/// this process for solo play (<c>--host</c>, the default), and shows the first-person view. Options (after "--"):
/// <c>--view ground --name &lt;n&gt;</c>, <c>--connect host:port</c> or <c>--host [--port n]</c>, <c>--lag</c>,
/// <c>--jitter</c>, <c>--loss</c> (bad-network test), <c>--netgraph</c> (open the network graph), <c>--face &lt;name&gt;</c>
/// (turn toward a player once seen, for screenshots).
/// </summary>
public partial class GroundMode : Node
{
    private GameServer? _server;
    private GameClient? _client;
    private GroundView? _view;

    /// <summary>The command-line options; set before adding to the tree.</summary>
    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>For automated screenshots: playing, the ground drawn, and the player to face (if any) in view.</summary>
    public bool ReadyForShot => _view?.ReadyForShot ?? false;

    public override void _Ready()
    {
        string name = Option("name", "Player");
        int port = Protocol.DefaultPort;
        string host = "127.0.0.1";
        if (Options.TryGetValue("connect", out string? target) && target != "true")
        {
            string[] parts = target.Split(':');
            host = parts[0];
            if (parts.Length > 1)
            {
                port = int.Parse(parts[1], CultureInfo.InvariantCulture);
            }
        }
        else
        {
            // Solo play: the same server, on a background thread of this process (Host mode).
            port = (int)Number("port", Protocol.DefaultPort);
            _server = new GameServer(new ServerOptions
            {
                Port = port,
                Name = $"{name}'s game",
                DatabasePath = ProjectSettings.GlobalizePath("user://hollow-host.db"),
                Log = line => GD.Print("[server] " + line),
            }, GalaxyService.Galaxy);
            _server.StartThread();
            port = _server.Port;
        }
        NetConditions conditions = NetConditions.FromOptions(key => Options.TryGetValue(key, out string? v) ? v : null);
        _client = new GameClient(new ClientOptions
        {
            Name = name,
            Conditions = conditions,
            Log = line => GD.Print("[client] " + line),
        });
        _client.Connect(host, port);
        _view = new GroundView
        {
            Name = "GroundView",
            Client = _client,
            HostedPort = _server?.Port,
            FaceName = Options.TryGetValue("face", out string? face) ? face : null,
            ShowNetGraph = Options.ContainsKey("netgraph"),
        };
        AddChild(_view);
    }

    public override void _ExitTree()
    {
        _client?.Dispose();
        _client = null;
        if (_server != null)
        {
            _server.Stop();
            _server.Dispose();
            _server = null;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            _ExitTree();
        }
    }

    private string Option(string key, string fallback) => Options.TryGetValue(key, out string? v) && v != "true" ? v : fallback;

    private double Number(string key, double fallback) =>
        Options.TryGetValue(key, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
}
