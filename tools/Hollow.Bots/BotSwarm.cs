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
    private readonly List<(GameClient Client, BotBrain Brain, SurvivalBrain? Survival, FightBrain? Fight, BuildBrain? Build, RaidBrain? Raid)> _bots =
        new();
    private readonly System.Collections.Generic.Dictionary<GameClient, FlyBrain> _pilots = new();
    private readonly Dictionary<GameClient, WorldBrain> _world = new();
    private readonly ConditionedRelay? _relay;
    private readonly GameContent _content = GameContent.LoadDefault();
    private PhysicsWorld? _ground;
    private NaturalState? _nature;
    private BuildingReplica? _building;
    private readonly Stopwatch _time = Stopwatch.StartNew();
    private double _last;

    public BotSwarm(string host, int port, int count, BotScript script, NetConditions conditions, string prefix, Action<string> log,
        int raidBases = 0, float raidRadius = 70, int fly = 0)
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
                SharedBuilding = SharedBuilding,
                Content = _content,
                Log = i == 0 ? log : _ => { },
            });
            if (i < 3 || Environment.GetEnvironmentVariable("HOLLOW_BOT_REFUSALS") != null)
            {
                client.Local.IntentRejected += a => log($"{client.Name}: intent {a.Seq} refused: {a.Result}");
                client.Local.BuildRejected += a => log($"{client.Name}: building intent {a.Seq} refused: {a.Reason} ({a.Text})");
            }
            client.Connect(host, port);
            _bots.Add((client, new BotBrain(i, script), script == BotScript.Survive ? new SurvivalBrain(i) : null,
                script == BotScript.Fight ? new FightBrain(i) : null,
                script == BotScript.Build ? new BuildBrain(i) { Log = i < 3 ? log : null } : null,
                script == BotScript.Raid ? new RaidBrain(i, raidBases, raidRadius) : null));
            if (script == BotScript.Fly || i < fly)
            {
                _pilots.Add(client, new FlyBrain(i));
            }
            if (script == BotScript.World)
            {
                _world[client] = new WorldBrain(i);
            }
        }
    }

    public IReadOnlyList<GameClient> Clients => _bots.Select(b => b.Client).ToList();

    /// <summary>What the survival bots did: crafts started, pickups, fights picked.</summary>
    public (int Crafts, int Picks, int Fights) Survival =>
        (_bots.Sum(b => b.Survival?.Crafts ?? 0), _bots.Sum(b => b.Survival?.Picks ?? 0), _bots.Sum(b => b.Survival?.Fights ?? 0));

    /// <summary>What the fighting bots did: bursts begun, reloads asked for, downed players helped up.</summary>
    public (int Bursts, int Reloads, int Assists) Fights =>
        (_bots.Sum(b => b.Fight?.Bursts ?? 0), _bots.Sum(b => b.Fight?.Reloads ?? 0), _bots.Sum(b => b.Fight?.Assists ?? 0));

    /// <summary>What the building bots did: intents done and refused, door toggles.</summary>
    public (int Done, int Refused, int Toggles) Building =>
        (_bots.Sum(b => b.Build?.Done ?? 0), _bots.Sum(b => b.Build?.Refused ?? 0), _bots.Sum(b => b.Build?.Toggles ?? 0));

    /// <summary>How far the building bots got, counted by stage.</summary>
    public string BuildProgress => string.Join("; ", _bots.Where(b => b.Build != null).GroupBy(b => b.Build!.Progress.Split(',')[0])
        .Select(g => $"{g.Count()} {g.Key}"));

    /// <summary>What the flying bots did: boardings, bots flying now, seconds flown in all, their ships' corrections (G6).</summary>
    public (int Boardings, int Flying, double Seconds, long Corrections) Flights =>
        (_pilots.Values.Sum(p => p.Boardings), _pilots.Keys.Count(c => c.Piloting), _pilots.Values.Sum(p => p.Airborne),
            _pilots.Keys.Sum(c => c.Stats.ShipCorrections));

    /// <summary>What the raiding bots did: charges placed, things thrown, rockets, bursts of explosive rounds; raid alerts heard.</summary>
    public (int Placed, int Thrown, int Rockets, int Bursts, int Alerts) Raids =>
        (_bots.Sum(b => b.Raid?.Placed ?? 0), _bots.Sum(b => b.Raid?.Thrown ?? 0), _bots.Sum(b => b.Raid?.Rockets ?? 0),
            _bots.Sum(b => b.Raid?.Bursts ?? 0), _bots.Sum(b => b.Client.Local.RaidAlerts.Count));

    /// <summary>What the world bots did (G7): barrels broken, crates emptied, things picked up, recycler runs started, research begun, blueprints learnt; teams they are in.</summary>
    public (int Broken, int Emptied, int Picked, int Recycled, int Researched, int Learnt, int Teamed) World =>
        (_world.Values.Sum(w => w.Broken), _world.Values.Sum(w => w.Emptied), _world.Values.Sum(w => w.Picked),
            _world.Values.Sum(w => w.Recycled), _world.Values.Sum(w => w.Researched), _world.Values.Sum(w => w.Learnt),
            _world.Keys.Count(c => c.Local.Team is { Members.Count: > 1 }));

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

    // One copy of the building for the shared ground: every bot applies what the server sends it (idempotently), so
    // the walls are solid in the shared prediction physics.
    private BuildingReplica SharedBuilding(ZoneSpec spec, ISurface surface, PhysicsWorld physics) =>
        _building ??= new BuildingReplica(_content.Building, physics, shared: true);

    private readonly Dictionary<GameClient, double> _respawnAsked = new();

    // A walking bot (wander, circle, jump): dead (the load test's respawn storms), it respawns after a second or two.
    private InputFrame Wander(BotBrain brain, double now, double delta, GameClient client)
    {
        if (!client.Local.IsDead)
        {
            return brain.Think(now, delta, client.Character.Position);
        }
        if (now >= _respawnAsked.GetValueOrDefault(client) + 2)
        {
            _respawnAsked[client] = now;
            client.Local.Respawn();
        }
        return default;
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
            foreach ((GameClient client, BotBrain brain, SurvivalBrain? survival, FightBrain? fight, BuildBrain? build, RaidBrain? raid) in _bots)
            {
                InputFrame input = !client.HasCharacter ? default
                    : _world.TryGetValue(client, out WorldBrain? world) ? world.Think(now, delta, client)
                    : raid != null ? raid.Think(now, delta, client)
                    : _pilots.TryGetValue(client, out FlyBrain? pilot) ? pilot.Think(now, delta, client)
                    : fight != null ? fight.Think(now, delta, client)
                    : build != null ? build.Think(now, delta, client)
                    : survival != null ? survival.Think(now, delta, client)
                    : Wander(brain, now, delta, client);
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
        foreach ((GameClient client, BotBrain _, SurvivalBrain? _, FightBrain? _, BuildBrain? _, RaidBrain? _) in _bots)
        {
            client.Dispose();
        }
        _building?.Dispose();
        _relay?.Dispose();
        _ground?.Dispose();
    }
}
