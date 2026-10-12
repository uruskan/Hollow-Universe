using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Hollow.Client;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Bots;

/// <summary>
/// Many headless clients in one process, on one thread (GROUND.md §4.3): each a full <see cref="GameClient"/> with
/// prediction, sharing one ground world and, under bad-network settings, one relay.
/// </summary>
public sealed class BotSwarm : IDisposable
{
    private readonly List<(GameClient Client, BotBrain Brain)> _bots = new();
    private readonly ConditionedRelay? _relay;
    private PhysicsWorld? _ground;
    private readonly Stopwatch _time = Stopwatch.StartNew();
    private double _last;

    public BotSwarm(string host, int port, int count, BotScript script, NetConditions conditions, string prefix, Action<string> log)
    {
        if (!conditions.IsNone)
        {
            IPAddress address = IPAddress.TryParse(host, out IPAddress? parsed) ? parsed : Dns.GetHostAddresses(host)[0];
            _relay = new ConditionedRelay(new IPEndPoint(address, port), conditions, 0xB075);
        }
        for (int i = 0; i < count; i++)
        {
            var client = new GameClient(new ClientOptions
            {
                Name = $"{prefix}{i + 1:000}",
                SharedRelay = _relay,
                SharedPhysics = SharedGround,
                Log = i == 0 ? log : _ => { },
            });
            client.Connect(host, port);
            _bots.Add((client, new BotBrain(i, script)));
        }
    }

    public IReadOnlyList<GameClient> Clients => _bots.Select(b => b.Client).ToList();

    public int Playing => _bots.Count(b => b.Client.State == ClientState.Playing);

    public int Disconnected => _bots.Count(b => b.Client.State == ClientState.Disconnected);

    private PhysicsWorld SharedGround(ZoneSpec spec, ISurface surface) =>
        _ground ??= new PhysicsWorld(surface, spec.CreateFrame(surface));

    /// <summary>Runs every bot at about 60 frames a second for a while.</summary>
    public void Run(double seconds, Func<bool>? stop = null)
    {
        double start = _time.Elapsed.TotalSeconds;
        while (_time.Elapsed.TotalSeconds - start < seconds && stop?.Invoke() != true)
        {
            double now = _time.Elapsed.TotalSeconds;
            double delta = now - _last;
            _last = now;
            foreach ((GameClient client, BotBrain brain) in _bots)
            {
                InputFrame input = client.HasCharacter ? brain.Think(now, delta, client.Character.Position) : default;
                client.Update(delta, input);
            }
            if (_ground != null)
            {
                lock (JoltRuntime.Gate)
                {
                    _ground.ReleaseUnusedGround(20 * 60);
                }
            }
            TimerResolution.WaitUntil(Stopwatch.GetTimestamp() + Stopwatch.Frequency / 60);
        }
    }

    public void Dispose()
    {
        foreach ((GameClient client, BotBrain _) in _bots)
        {
            client.Dispose();
        }
        _relay?.Dispose();
        _ground?.Dispose();
    }
}
