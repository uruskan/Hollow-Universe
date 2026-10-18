using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Hollow.Client;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Content;
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
    private readonly BotWorlds _worlds;
    private readonly BotGround _homes = new();
    private readonly Dictionary<GameClient, PlanetEnvironmentDescriptor> _clientWorlds = new();
    private bool _disposed;
    private bool _worldsChanged;
    private readonly Stopwatch _time = Stopwatch.StartNew();
    private double _last;

    public BotSwarm(string host, int port, int count, BotScript script, NetConditions conditions, string prefix, Action<string> log,
        int raidBases = 0, float raidRadius = 70, int fly = 0)
    {
        _worlds = new BotWorlds(_content);
        try
        {
            if (!conditions.IsNone)
            {
                IPAddress address = IPAddress.TryParse(host, out IPAddress? parsed) ? parsed : Dns.GetHostAddresses(host)[0];
                _relay = new ConditionedRelay(new IPEndPoint(address, port), conditions, 0xB075);
            }
            for (int i = 0; i < count; i++)
            {
                GameClient? client = null;
                client = new GameClient(new ClientOptions
                {
                    Name = $"{prefix}{i + 1:000}",
                    Body = (byte)(i % 3 == 1 ? 1 : 0), // a third of the crowd female
                    SharedRelay = _relay,
                    WorldStore = _worlds.Store,
                    SharedPhysics = (spec, surface) => SharedWorld(client!, spec, surface).Physics,
                    SharedNature = (spec, surface) => SharedWorld(client!, spec, surface).Nature,
                    SharedBuilding = (spec, surface, physics) => SharedBuilding(client!, spec, surface, physics),
                    Content = _content,
                    Log = i == 0 ? log : _ => { },
                });
                client.WorldRetiring += () =>
                {
                    _clientWorlds.Remove(client);
                    _homes.Forget(client);
                    _worldsChanged = true;
                };
                bool tell = i < 3 || Environment.GetEnvironmentVariable("HOLLOW_BOT_REFUSALS") != null;
                if (tell)
                {
                    client.Local.IntentRejected += a => log($"{client.Name}: intent {a.Seq} refused: {a.Code} ({_content.Texts.Line(a.Outcome)})");
                }
                _bots.Add((client, new BotBrain(i, script), script == BotScript.Survive ? new SurvivalBrain(i, _homes) : null,
                    script == BotScript.Fight ? new FightBrain(i, _homes) : null,
                    script == BotScript.Build ? new BuildBrain(i, _homes) { Log = tell ? log : null } : null,
                    script == BotScript.Raid ? new RaidBrain(i, raidBases, raidRadius, _homes) : null));
                client.Connect(host, port);
                if (script == BotScript.Fly || i < fly)
                {
                    _pilots.Add(client, new FlyBrain(i, _homes));
                }
                if (script == BotScript.World)
                {
                    _world[client] = new WorldBrain(i, _homes);
                }
            }
        }
        catch (Exception construction)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(construction, cleanup); }
            throw;
        }
    }

    public IReadOnlyList<GameClient> Clients => _bots.Select(b => b.Client).ToList();

    /// <summary>Completion and cleanup errors from the session store after this swarm has been disposed.</summary>
    public System.Threading.Tasks.Task EnvironmentRetirement => _worlds.Store.Retirement;

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
        (_pilots.Values.Sum(p => p.Boardings), _pilots.Keys.Count(c => c.Ships().Piloting), _pilots.Values.Sum(p => p.Airborne),
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

    private BotWorldContext SharedWorld(GameClient client, ZoneSpec spec, ISurface surface)
    {
        using WorldLease lease = client.ForkWorldLease();
        if (!ReferenceEquals(lease.Surface, surface))
            throw new InvalidOperationException("The bot callback does not match its active world lease.");
        BotWorldContext context = _worlds.Get(spec, lease);
        _homes.Bind(client, lease.Descriptor, spec);
        if (!_clientWorlds.TryGetValue(client, out PlanetEnvironmentDescriptor? previous) || !previous.Equals(lease.Descriptor))
            _worldsChanged = true;
        _clientWorlds[client] = lease.Descriptor;
        return context;
    }

    // One copy of the building for the shared ground: every bot applies what the server sends it (idempotently), so
    // the walls are solid in the shared prediction physics.
    private BuildingReplica SharedBuilding(GameClient client, ZoneSpec spec, ISurface surface, PhysicsWorld physics)
    {
        BotWorldContext context = SharedWorld(client, spec, surface);
        if (!ReferenceEquals(context.Physics, physics))
            throw new InvalidOperationException("The bot building and predictor must share the same world.");
        return context.Building;
    }

    private readonly Dictionary<GameClient, double> _respawnAsked = new();

    // A walking bot (wander, circle, jump): dead (the load test's respawn storms), it respawns after a second or two.
    private InputFrame Wander(BotBrain brain, double now, double delta, GameClient client)
    {
        if (!client.Local.IsDead)
        {
            return brain.Think(now, delta, client.Character.Position, _homes.Home(client));
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        double start = _time.Elapsed.TotalSeconds;
        while (_time.Elapsed.TotalSeconds - start < seconds && stop?.Invoke() != true)
        {
            double now = _time.Elapsed.TotalSeconds;
            double delta = now - _last;
            _last = now;
            foreach ((GameClient client, BotBrain brain, SurvivalBrain? survival, FightBrain? fight, BuildBrain? build, RaidBrain? raid) in _bots)
            {
                if (client.HasCharacter)
                {
                    _homes.Follow(client); // home: where it arrived (docs/WHOLE_WORLD.md §13)
                }
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
            if (_worldsChanged)
            {
                var retained = _clientWorlds.Values.ToHashSet();
                _worlds.RetireExcept(retained);
                _homes.RetireExcept(retained);
                _worldsChanged = false;
            }
            _worlds.ReleaseUnusedGround();
            TimerResolution.WaitUntil(Stopwatch.GetTimestamp() + Stopwatch.Frequency / 60);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Bot swarm retirement must run outside the Jolt gate.");
        _disposed = true;
        var errors = new List<Exception>();
        void Clean(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
        foreach ((GameClient client, BotBrain _, SurvivalBrain? _, FightBrain? _, BuildBrain? _, RaidBrain? _) in _bots)
        {
            Clean(client.Dispose);
        }
        _bots.Clear();
        _pilots.Clear();
        _world.Clear();
        _respawnAsked.Clear();
        _clientWorlds.Clear();
        Clean(_homes.Dispose);
        Clean(_worlds.Dispose);
        if (_relay != null) Clean(_relay.Dispose);
        if (errors.Count != 0) throw new AggregateException("Bot swarm retirement failed after cleanup.", errors);
    }
}
