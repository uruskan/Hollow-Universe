using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Hollow.Client;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Bots;

/// <summary>
/// Many headless clients in one process, on one thread (GROUND.md §4.3): each a full <see cref="GameClient"/> with
/// prediction, sharing one ground world and, under bad-network settings, one relay.
/// </summary>
public sealed class BotSwarm : IDisposable
{
    private readonly List<(GameClient Client, BotBrain Brain, SurvivalBrain? Survival, FightBrain? Fight)> _bots = new();
    private readonly ConditionedRelay? _relay;
    private readonly GameContent _content = GameContent.LoadDefault();
    private PhysicsWorld? _ground;
    private NaturalState? _nature;
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
                Body = (byte)(i % 3 == 1 ? 1 : 0), // a third of the crowd female
                SharedRelay = _relay,
                SharedPhysics = SharedGround,
                SharedNature = SharedNature,
                Content = _content,
                Log = i == 0 ? log : _ => { },
            });
            if (i == 0)
            {
                client.Local.IntentRejected += a => log($"{client.Name}: intent {a.Seq} refused: {a.Result}");
            }
            client.Connect(host, port);
            _bots.Add((client, new BotBrain(i, script), script == BotScript.Survive ? new SurvivalBrain(i) : null,
                script == BotScript.Fight ? new FightBrain(i) : null));
        }
    }

    public IReadOnlyList<GameClient> Clients => _bots.Select(b => b.Client).ToList();

    /// <summary>What the survival bots did: crafts started, pickups, fights picked.</summary>
    public (int Crafts, int Picks, int Fights) Survival =>
        (_bots.Sum(b => b.Survival?.Crafts ?? 0), _bots.Sum(b => b.Survival?.Picks ?? 0), _bots.Sum(b => b.Survival?.Fights ?? 0));

    /// <summary>What the fighting bots did: bursts begun, reloads asked for, downed players helped up.</summary>
    public (int Bursts, int Reloads, int Assists) Fights =>
        (_bots.Sum(b => b.Fight?.Bursts ?? 0), _bots.Sum(b => b.Fight?.Reloads ?? 0), _bots.Sum(b => b.Fight?.Assists ?? 0));

    public int Playing => _bots.Count(b => b.Client.State == ClientState.Playing);

    public int Disconnected => _bots.Count(b => b.Client.State == ClientState.Disconnected);

    private PhysicsWorld SharedGround(ZoneSpec spec, ISurface surface) =>
        _ground ??= new PhysicsWorld(surface, spec.CreateFrame(surface));

    // One copy of the natural objects' changes for the shared ground: every bot applies the server's (the same)
    // changes to it, and its trees and outcrops stay solid in the shared physics.
    private NaturalState SharedNature(ZoneSpec spec, ISurface surface)
    {
        if (_nature == null)
        {
            ZoneFrame frame = spec.CreateFrame(surface);
            _nature = new NaturalState(_content, spec.CreateNature(surface, frame, _content));
            _ = new NaturalBodies(SharedGround(spec, surface), _nature);
        }
        return _nature;
    }

    /// <summary>Runs every bot at about 60 frames a second for a while.</summary>
    public void Run(double seconds, Func<bool>? stop = null)
    {
        double start = _time.Elapsed.TotalSeconds;
        while (_time.Elapsed.TotalSeconds - start < seconds && stop?.Invoke() != true)
        {
            double now = _time.Elapsed.TotalSeconds;
            double delta = now - _last;
            _last = now;
            foreach ((GameClient client, BotBrain brain, SurvivalBrain? survival, FightBrain? fight) in _bots)
            {
                InputFrame input = !client.HasCharacter ? default
                    : fight != null ? fight.Think(now, delta, client)
                    : survival != null ? survival.Think(now, delta, client)
                    : brain.Think(now, delta, client.Character.Position);
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
        foreach ((GameClient client, BotBrain _, SurvivalBrain? _, FightBrain? _) in _bots)
        {
            client.Dispose();
        }
        _relay?.Dispose();
        _ground?.Dispose();
    }
}
