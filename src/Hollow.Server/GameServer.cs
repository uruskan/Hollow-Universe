using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Net.Transport;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Surface;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// The dedicated server (GROUND.md §4.2): one zone on the first world, ticking at 30 Hz on one thread, with
/// development login by name, snapshot replication, chat, persistence and admin commands. It is a host
/// (GROUND_SYSTEMS.md §12.5): the connection, the logins and spawns, the tick loop, the inputs, the snapshots of the
/// dynamic entities with their relevance grid, chat, the intents' budget and answers, the saves and the console's
/// plumbing are its own; everything else the game does on the server is a <see cref="ServerModule"/>, listed in
/// <see cref="ServerModules"/>, called through its hooks in that one order. The console program runs it on its main
/// thread; the game runs it on a background thread for solo play (Host mode). Everything below happens on the tick
/// thread; <see cref="Execute"/> and <see cref="RequestStop"/> may be called from anywhere.
/// </summary>
public sealed partial class GameServer : INetEventListener, IDisposable
{
    public const int TickRate = CharacterMotor.TickRate;

    private readonly ServerOptions _options;
    private readonly NetManager _net;
    private readonly PersistenceWorker? _persistence;
    private readonly EntityIdAllocator _ids;
    private readonly Dictionary<NetPeer, PlayerSession> _sessions = new();
    private readonly Dictionary<EntityId, long> _accountOf = new();
    private readonly Dictionary<EntityId, PlayerSession> _sessionOf = new();
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly ConcurrentQueue<Action> _inspections = new();
    private readonly MessageHandlers<PlayerSession> _handlers = new();
    private readonly ServerModule[] _modules;
    private readonly Dictionary<Type, ServerModule> _byType = new();
    private readonly ServerModule[] _onInput;
    private readonly ServerModule[] _onSeen;
    private readonly BitWriter _writer = new(2048);
    private readonly BitReader _reader = new();
    private readonly RelevanceGrid _grid;
    private readonly List<int> _relevantIndices = new();
    private readonly HashSet<uint> _nowRelevant = new();
    private readonly List<uint> _gone = new();
    private readonly List<SnapshotCandidate> _candidates = new();
    private readonly List<ShipCandidate> _ships = new();
    private readonly Dictionary<uint, NetEntity> _netStates = new();
    private readonly HashSet<EntityId> _savedThings = new();
    private readonly CancellationTokenSource _stop = new();
    private uint _nextNetId = 1;
    private double _snapshotAccumulator;
    private long _lastSave;
    private long _lastNetUpdate;
    private bool _deathToSave;
    private long _lastDeathSave;
    private Thread? _thread;
    private bool _shutDown;
    private int _failedTicks;

    /// <summary>A dynamic entity in this snapshot: a character (its entity), or one a module presented (an animal: no zone entity, its presenter).</summary>
    private readonly record struct NetEntity(Entity Entity, CharacterNetState State, ServerModule? Presenter);

    public GameServer(ServerOptions options, Galaxy.Galaxy? galaxy = null)
    {
        _options = options;
        galaxy ??= new Galaxy.Galaxy(options.CataloguePath != null ? Galaxy.StarCatalogue.Load(options.CataloguePath) : null);
        Spec = (ZoneSpec.FirstWorld(galaxy, options.Latitude, options.Longitude)
            ?? throw new InvalidOperationException("The generator no longer makes the first world (GROUND.md §3).")) with
            {
                StandIn = options.StandIn,
                RotationTimeScale = options.RotationTimeScale,
            };
        Content = options.Content ?? GameContent.LoadDefault();
        _ids = new EntityIdAllocator(1, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Zone = new Zone(Spec, content: Content)
        {
            UnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NewEntityId = _ids.Next,
        };
        _grid = new RelevanceGrid(options.RelevanceRadius);
        if (options.DatabasePath != null)
        {
            _persistence = new PersistenceWorker(options.DatabasePath, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Log);
        }
        _net = new NetManager(this)
        {
            AutoRecycle = true,
            EnableStatistics = true,
            DisconnectTimeout = 10_000,
            UpdateTime = 1,
            IPv6Enabled = false,
            UnconnectedMessagesEnabled = false,
        };
        if (!_net.StartInManualMode(IPAddress.Any, IPAddress.IPv6Any, options.Port))
        {
            _persistence?.Dispose();
            Zone.Dispose();
            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }
        Port = _net.LocalPort;
        // The real planet (GROUND.md §6.1): its climate follows the sun of the zone's sky, and players arrive and
        // respawn on its real beaches near the zone.
        // The zone's sky: its spin is the zone's clock (GROUND.md §8.2: admins set it), which the climate follows.
        Sky = Spec.CreateSky();
        if (Zone.Surface is PlanetSurface planet)
        {
            if (Sky != null)
            {
                Zone.Climate = new PlanetClimate(Content.Survival, planet, Zone.Frame, Sky);
            }
            Beaches = Simulation.Beaches.Find(planet, Zone.Frame);
            if (Beaches.Points.Count > 0)
            {
                Zone.Beaches = salt => Beaches.Pick(salt)!.Value;
            }
            Log($"{Beaches.Points.Count} beach points within {Beaches.SearchedRadius / 1000:0} km" +
                (Beaches.Points.Count > 0 ? $", the nearest {Beaches.Points[0].Length():0} m from the landing site" : ": players arrive at the landing site"));
        }
        // Build the ground where players arrive now (and warm the code up), so the first arrival doesn't stall the
        // tick loop.
        Vector3 arrival = Arrival(0);
        Zone.Physics.Prefetch(arrival, radius: 2);
        Zone.Physics.EnsureGround(arrival, radius: 2);
        // The modules (GROUND_SYSTEMS.md §12.5): attached, their messages and subscriptions registered after the host's
        // own, the stored world restored into them, then started.
        _modules = ServerModules.All();
        foreach (ServerModule module in _modules)
        {
            module.Attach(this);
            _byType.Add(module.GetType(), module);
        }
        _onInput = Overriding(nameof(ServerModule.Input));
        _onSeen = Overriding(nameof(ServerModule.Seen));
        RegisterMessages();
        foreach (ServerModule module in _modules)
        {
            module.Init(_handlers);
        }
        LoadWorld();
        foreach (ServerModule module in _modules)
        {
            module.Start();
        }
        Zone.Profile = Profile;
        _lastSave = Stopwatch.GetTimestamp();
        _lastNetUpdate = Stopwatch.GetTimestamp();
        Log($"{options.Name}: zone on {Spec}, port {Port}, " +
            (options.DatabasePath != null ? $"database {options.DatabasePath}" : "no persistence") + $", data {Content.Hash:X16}");
    }

    public ZoneSpec Spec { get; }

    public Zone Zone { get; }

    /// <summary>The game's data, as every client must have it too.</summary>
    public GameContent Content { get; }

    /// <summary>The real beaches near the zone where players arrive and respawn (null on the stand-in ground).</summary>
    public Simulation.Beaches? Beaches { get; }

    /// <summary>The zone's sky: its spin is the zone's clock, which the climate follows and admins set; null without one.</summary>
    internal SkyEphemeris? Sky { get; }

    internal ServerOptions Options => _options;

    internal PersistenceWorker? Persistence => _persistence;

    internal BitWriter Writer => _writer;

    /// <summary>
    /// Where a new character arrives: a beach point picked by <paramref name="salt"/>, as in Rust; at the landing site
    /// with <see cref="ServerOptions.SpawnAtOrigin"/>, on the stand-in ground, or with no coast near.
    /// </summary>
    public Vector3 Arrival(ulong salt) =>
        !_options.SpawnAtOrigin && Beaches?.Pick(salt) is { } beach ? beach : Zone.SpawnPoint(salt);

    /// <summary>The UDP port the server listens on.</summary>
    public int Port { get; }

    public TickStats Ticks { get; } = new();

    /// <summary>Time spent stepping the zone (movement and the game's rules), the rest of a tick being the network's.</summary>
    public TimeSpan StepTime { get; private set; }

    public IReadOnlyCollection<PlayerSession> Players => _sessions.Values;

    public bool IsStopping => _stop.IsCancellationRequested;

    /// <summary>Total bytes and packets sent and received, for statistics.</summary>
    public NetStatistics Traffic => _net.Statistics;

    // ---------------------------------------------------------------- the modules

    /// <summary>The modules, in the order their hooks run (<see cref="ServerModules"/>).</summary>
    public IReadOnlyList<ServerModule> Modules => _modules;

    /// <summary>A module by type, or null when this server has none.</summary>
    public T? Get<T>() where T : ServerModule => _byType.TryGetValue(typeof(T), out ServerModule? module) ? (T)module : null;

    /// <summary>A module by type; fails naming what is missing.</summary>
    public T Require<T>() where T : ServerModule =>
        Get<T>() ?? throw new InvalidOperationException($"this server has no {typeof(T).Name} (its list is {string.Join(", ", _modules.Select(m => m.GetType().Name))})");

    // The modules that override a per-player or per-entity hook, so the hot loops call only those.
    private ServerModule[] Overriding(string hook) =>
        _modules.Where(m => m.GetType().GetMethod(hook, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.DeclaringType != typeof(ServerModule)).ToArray();

    // ---------------------------------------------------------------- running

    /// <summary>Runs ticks at 30 Hz on the calling thread until <see cref="RequestStop"/>, then shuts down.</summary>
    public void Run()
    {
        TimerResolution.Begin();
        // The tick can't wait for a blocking full collection (GROUND.md §5.11): full collections run in the background
        // while it ticks, as in the game's own process.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        // The tick before the work it starts on other threads (ground tiles built ahead) and before the rest of a busy
        // PC: with other work on it, its 99th percentile halved (GROUND.md §5.11).
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        try
        {
            long period = Stopwatch.Frequency / TickRate;
            long next = Stopwatch.GetTimestamp();
            while (!_stop.IsCancellationRequested)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    Tick();
                }
                catch (Exception e)
                {
                    // A bug in one tick must not take every player down: report it in full and carry on. Ten in a row
                    // is not a passing problem, so the server stops (saving what it can).
                    Log($"tick {Zone.Tick} failed: {e}");
                    if (++_failedTicks >= 10)
                    {
                        Log("ten ticks in a row failed: stopping");
                        break;
                    }
                    continue;
                }
                _failedTicks = 0;
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Ticks.Add(ms, 1000.0 / TickRate);
                next += period;
                long now = Stopwatch.GetTimestamp();
                if (now - next > 5 * period)
                {
                    // Far behind (a debugger, a stall): carry on from now rather than racing to catch up.
                    next = now;
                }
                TimerResolution.WaitUntil(next);
            }
        }
        finally
        {
            Shutdown();
            TimerResolution.End();
        }
    }

    /// <summary>Host mode: runs on a background thread.</summary>
    public void StartThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Hollow server" };
        _thread.Start();
    }

    /// <summary>Asks the loop to stop; it saves everyone and disconnects them first.</summary>
    public void RequestStop() => _stop.Cancel();

    /// <summary>Stops and waits for the loop (Host mode and tests).</summary>
    public void Stop()
    {
        RequestStop();
        _thread?.Join();
        _thread = null;
    }

    /// <summary>Queues an admin command (from any thread); it runs at the start of the next tick.</summary>
    public void Execute(string command) => _commands.Enqueue(command);

    /// <summary>
    /// Runs <paramref name="look"/> on the tick thread at the start of the next tick and waits for it: the safe way for
    /// another thread (a load test, the game's overlay in Host mode) to read the server's state.
    /// </summary>
    public T Inspect<T>(Func<GameServer, T> look)
    {
        if (_thread == null || Thread.CurrentThread == _thread || _shutDown)
        {
            return look(this);
        }
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inspections.Enqueue(() =>
        {
            try
            {
                done.SetResult(look(this));
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        return done.Task.Wait(TimeSpan.FromSeconds(5)) ? done.Task.Result : throw new TimeoutException("The server's tick didn't run.");
    }

    /// <summary>One tick: receive, log players in, step the zone, the modules' after-step work, replicate, save, send.</summary>
    public void Tick()
    {
        TimeSpan gcBefore = GC.GetTotalPauseDuration();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        TickProfile.Scope whole = Profile.Time(ProfileSection.Tick);
        Zone.UnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (Profile.Time(ProfileSection.Receive))
        {
            _net.PollEvents();
        }
        using (Profile.Time(ProfileSection.Logins))
        {
            FinishLogins();
            foreach (ServerModule module in _modules)
            {
                module.BeforeStep();
            }
        }
        while (_commands.TryDequeue(out string? command))
        {
            RunCommand(command);
        }
        while (_inspections.TryDequeue(out Action? inspection))
        {
            inspection();
        }

        uint tick = (uint)Zone.Tick + 1;
        foreach (PlayerSession session in _sessions.Values)
        {
            if (!session.Playing)
            {
                continue;
            }
            InputCommand input = session.InputFor(tick);
            ref Character character = ref Zone.Entities.Get<Character>(session.Entity);
            character.Input = NetConvert.ToMoveInput(input);
            character.Actions = NetConvert.ToActions(input);
            ref NetPresence presence = ref Zone.Entities.Get<NetPresence>(session.Entity);
            presence.Yaw = input.Yaw;
            presence.Pitch = input.Pitch;
            presence.LookYaw = input.LookYaw;
            presence.LookPitch = input.LookPitch;
            foreach (ServerModule module in _onInput)
            {
                module.Input(session, input);
            }
        }
        long stepStarted = Stopwatch.GetTimestamp();
        using (Profile.Time(ProfileSection.Step))
        {
            Zone.Step();
        }
        StepTime += Stopwatch.GetElapsedTime(stepStarted);
        using (Profile.Time(ProfileSection.AfterStep))
        {
            // The events went to their subscribers as they happened (GROUND_SYSTEMS.md §12.3); the lists are done with.
            Zone.Events.Clear();
            foreach (ServerModule module in _modules)
            {
                module.AfterStep();
            }
            AnswerIntents();
        }

        _snapshotAccumulator += (double)Protocol.SnapshotRate / TickRate;
        if (_snapshotAccumulator >= 1)
        {
            _snapshotAccumulator -= 1;
            using TickProfile.Scope _ = Profile.Time(ProfileSection.Replicate);
            Replicate();
        }
        if (_persistence != null && (Stopwatch.GetElapsedTime(_lastSave).TotalSeconds >= _options.SaveIntervalSeconds ||
            (_deathToSave && Stopwatch.GetElapsedTime(_lastDeathSave).TotalSeconds >= 1)))
        {
            using TickProfile.Scope _ = Profile.Time(ProfileSection.Save);
            _lastSave = _lastDeathSave = Stopwatch.GetTimestamp();
            _deathToSave = false;
            SaveAll();
        }
        using (Profile.Time(ProfileSection.Send))
        {
            long now = Stopwatch.GetTimestamp();
            _net.ManualUpdate((float)((now - _lastNetUpdate) * 1000.0 / Stopwatch.Frequency));
            _lastNetUpdate = now;
        }
        whole.Dispose();
        Profile.EndTick((GC.GetTotalPauseDuration() - gcBefore).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    /// <summary>Where the ticks go (GROUND.md §5.11): the load test prints it.</summary>
    public TickProfile Profile { get; } = new();

    // ---------------------------------------------------------------- connecting and logging in

    public void OnConnectionRequest(ConnectionRequest request)
    {
        byte[] data = request.Data.GetRemainingBytes();
        _reader.Reset(data, 0, data.Length);
        Hello hello = default;
        bool parsed = _reader.ReadBits(8) == Hello.TypeId;
        if (parsed)
        {
            hello = Hello.Read(_reader);
            parsed = !_reader.Failed;
        }
        if (!parsed || hello.ProtocolVersion != Protocol.Version || hello.SchemaHash != Protocol.SchemaHash)
        {
            Refuse(request, GoodbyeReason.ProtocolMismatch, $"This server speaks protocol {Protocol.Version} ({Protocol.SchemaHash:X16}); update the game.");
            return;
        }
        if (hello.ContentHash != Content.Hash)
        {
            Refuse(request, GoodbyeReason.ContentMismatch, $"This server runs other game data ({Content.Hash:X16}); update the game.");
            return;
        }
        string name = (hello.PlayerName ?? "").Trim();
        if (!IsValidName(name))
        {
            Refuse(request, GoodbyeReason.BadName, "Names are 1 to 32 letters, digits, spaces, '-', '_' or '.'.");
            return;
        }
        if (_sessions.Values.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Refuse(request, GoodbyeReason.NameInUse, $"{name} is already playing.");
            return;
        }
        if (_sessions.Count >= _options.MaxPlayers)
        {
            Refuse(request, GoodbyeReason.ServerFull, "The server is full.");
            return;
        }
        NetPeer peer = request.Accept();
        var session = new PlayerSession(peer, name) { Body = Math.Min(hello.Body, (byte)1) };
        session.Login = _persistence != null
            ? _persistence.Login(name)
            : Task.FromResult<(long, CharacterRecord?)>((0, null));
        _sessions.Add(peer, session);
    }

    private void Refuse(ConnectionRequest request, GoodbyeReason reason, string text)
    {
        Log($"refused {request.RemoteEndPoint}: {reason}");
        _writer.Reset();
        new Goodbye { Reason = reason, Text = text }.WriteMessage(_writer);
        request.Reject(_writer.ToArray());
    }

    public static bool IsValidName(string name) =>
        name.Length is >= 1 and <= Protocol.MaxPlayerNameBytes && name == name.Trim() &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' or '.');

    private void FinishLogins()
    {
        foreach (PlayerSession session in _sessions.Values)
        {
            if (session.Playing || session.Login is not { IsCompleted: true } login)
            {
                continue;
            }
            if (!login.IsCompletedSuccessfully)
            {
                session.Login = null;
                Kick(session, GoodbyeReason.Unknown, "The server couldn't load your character.");
                continue;
            }
            (long account, CharacterRecord? record) = login.Result;
            // The ground where the player comes in is built first, on worker threads (GROUND.md §4.6): the login
            // waits for it, a second at most, so a crowd joining at once doesn't stall the tick.
            if (session.ArrivalWait < TickRate && ArrivalPoint(session, record) is { } at && !Zone.Physics.GroundReady(at))
            {
                session.ArrivalWait++;
                continue;
            }
            session.Login = null;
            session.AccountId = account;
            Spawn(session, record);
        }
    }

    // Where Spawn brings a player in: null for a sleeper (its ground is kept), else the stored place or the beach.
    private Vector3? ArrivalPoint(PlayerSession session, CharacterRecord? record)
    {
        if (record != null && Zone.Players().TryFindPlayer(record.Id, out _, out _))
        {
            return null;
        }
        return record != null && CharacterRecords.Restorable(Zone, Spec, record, out Vector3 local) ? local : Arrival(Hash64.Of(session.Name.ToUpperInvariant()));
    }

    /// <summary>
    /// A player joins: their sleeper wakes where it lies (logged off earlier, or loaded at start); a stored character
    /// that isn't in the zone comes back from its record; anyone else starts a new life at the spawn. Then the modules
    /// hear of them (<see cref="ServerModule.Joined"/>: the join kit, the ship on join, the clock, the alerts that waited).
    /// </summary>
    private void Spawn(PlayerSession session, CharacterRecord? record)
    {
        EntityId id;
        Entity entity;
        float yaw = 0, pitch = 0;
        string how;
        if (record != null && Zone.Players().TryFindPlayer(record.Id, out entity, out Player _))
        {
            id = record.Id;
            Zone.Players().Wake(entity);
            yaw = Zone.Entities.Get<Character>(entity).Input.Yaw;
            pitch = record.Pitch;
            how = "woke";
        }
        else
        {
            CharacterState state;
            Player? player = null;
            if (record != null && CharacterRecords.Restorable(Zone, Spec, record, out Vector3 local))
            {
                id = record.Id;
                state = Zone.NewCharacter(local + Zone.Frame.UpAt(local) * 0.02f);
                state.Stance = (Stance)Math.Min(record.Stance, (byte)Stance.Crouching);
                player = CharacterRecords.Decode(record, Content, Log);
                yaw = record.Yaw;
                pitch = record.Pitch;
                how = "restored";
            }
            else
            {
                id = record?.Id ?? _ids.Next();
                state = Zone.NewCharacter(Arrival(Hash64.Of(session.Name.ToUpperInvariant())));
                how = "new";
            }
            if (Zone.Entities.TryFind(id, out _))
            {
                id = _ids.Next();
            }
            entity = Zone.Players().AddPlayer(id, session.Name, state, player);
            Zone.Entities.Get<Character>(entity).Input.Yaw = yaw;
        }
        if (Zone.Players().PlayerOf(entity) is { } chosen)
        {
            chosen.Body = session.Body; // the body chosen in the menu, kept with the character
        }
        if (!Zone.Entities.Has<NetPresence>(entity))
        {
            Zone.Entities.Add(entity, new NetPresence { NetId = _nextNetId++ });
        }
        ref NetPresence presence = ref Zone.Entities.Get<NetPresence>(entity);
        presence.Owner = session;
        presence.Yaw = Quantize.Angle16(yaw);
        presence.Pitch = Quantize.Angle16(pitch);
        session.Entity = entity;
        session.EntityId = id;
        session.NetId = presence.NetId;
        session.ResetInputs((uint)Zone.Tick);
        _accountOf[id] = session.AccountId;
        _sessionOf[id] = session;
        _writer.Reset();
        new Welcome
        {
            NetId = presence.NetId,
            EntityId = id.Value,
            ServerTick = (uint)Zone.Tick,
            TickRate = TickRate,
            SnapshotRate = Protocol.SnapshotRate,
            Zone = NetConvert.ToInfo(Spec),
            ServerName = _options.Name,
            Yaw = Quantize.Angle16(yaw),
            Pitch = Quantize.Angle16(pitch),
        }.WriteMessage(_writer);
        session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
        Log($"{session.Name} joined from {session.Peer} as {id} ({how}) at {Format(Zone.Entities.Get<Character>(entity).State.Position)}" +
            (this.Console().IsAdmin(session) ? " (admin)" : ""));
        foreach (ServerModule module in _modules)
        {
            module.Joined(session, how == "new");
        }
    }

    public void OnPeerConnected(NetPeer peer)
    {
    }

    /// <summary>A player left: their character stays in the world, asleep (GROUND.md §6), and is saved.</summary>
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (!_sessions.Remove(peer, out PlayerSession? session))
        {
            return;
        }
        if (session.Playing)
        {
            Zone.Players().Sleep(session.Entity);
            ref NetPresence presence = ref Zone.Entities.Get<NetPresence>(session.Entity);
            presence.Owner = null;
            _sessionOf.Remove(session.EntityId);
            if (_persistence != null && Zone.Players().PlayerOf(session.Entity) is { } p)
            {
                _persistence.Save(new[] { CharacterRecords.Encode(Zone, Spec, session.Entity, p, _accountOf.GetValueOrDefault(p.Id)) });
            }
            session.Entity = Entity.None;
        }
        foreach (ServerModule module in _modules)
        {
            module.Left(session);
        }
        Log($"{session.Name} left ({info.Reason})");
    }

    // ---------------------------------------------------------------- receiving

    // The host's own messages (GROUND_SYSTEMS.md §12.4): inputs and chat; every other type is a module's, or a bad packet.
    private void RegisterMessages()
    {
        _handlers.On<InputPacket>((PlayerSession session, in InputPacket input) =>
        {
            session.Snapshots.Acknowledge(input.AckSequence, input.AckBits);
            if (input.NewestTick >= session.NewestInputTick)
            {
                session.EchoTime = input.ClientTime;
                session.EchoReceived = Stopwatch.GetTimestamp();
            }
            if (session.Playing)
            {
                session.ReceiveInputs(input, (uint)Zone.Tick + 1, Zone.Tick);
            }
        });
        _handlers.On<ChatSend>((PlayerSession session, in ChatSend chat) =>
        {
            if (string.IsNullOrWhiteSpace(chat.Text))
            {
                session.BadPackets++;
                return;
            }
            Chat(session, chat.Text.Trim());
        });
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
    {
        if (!_sessions.TryGetValue(peer, out PlayerSession? session))
        {
            return;
        }
        // Read in place: the transport recycles the packet after this returns.
        _reader.Reset(packet.RawData, packet.Position, packet.AvailableBytes);
        byte type = (byte)_reader.ReadBits(8);
        if (!_handlers.Dispatch(type, _reader, session))
        {
            session.BadPackets++;
        }
    }

    private void Chat(PlayerSession session, string text)
    {
        // A budget per player: one line a second, five at once.
        session.ChatTokens = Math.Min(5, session.ChatTokens + (Zone.Tick - session.ChatTick) / (double)TickRate);
        session.ChatTick = Zone.Tick;
        if (session.ChatTokens < 1)
        {
            return;
        }
        session.ChatTokens -= 1;
        Log($"<{session.Name}> {text}");
        Broadcast(new ChatLine { Kind = ChatKind.Player, From = session.Name, Text = text });
    }

    internal void Broadcast(ChatLine line)
    {
        _writer.Reset();
        line.WriteMessage(_writer);
        foreach (PlayerSession session in _sessions.Values)
        {
            session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
        }
    }

    internal void Send(PlayerSession session) => session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);

    internal void Despawned(PlayerSession session, uint netId)
    {
        _writer.Reset();
        new Despawn { NetId = netId }.WriteMessage(_writer);
        Send(session);
    }

    internal PlayerSession? SessionOf(EntityId player) => _sessionOf.GetValueOrDefault(player);

    internal uint NextNetId() => _nextNetId++;

    // ---------------------------------------------------------------- intents

    // The answers (ServerModule.Intent) go after this tick's changes (every module's after-step messages), so the changes
    // arrive first.
    private void AnswerIntents()
    {
        foreach (PlayerSession session in _sessions.Values)
        {
            foreach (IntentResult result in session.PendingResults)
            {
                _writer.Reset();
                result.WriteMessage(_writer);
                Send(session);
            }
            session.PendingResults.Clear();
        }
    }

    // ---------------------------------------------------------------- replication

    /// <summary>A dynamic entity a module presents for this snapshot (<see cref="ServerModule.Present"/>).</summary>
    internal void Present(ServerModule presenter, uint netId, Vector3 position, in CharacterNetState state)
    {
        _grid.Add(netId, position);
        _netStates[netId] = new NetEntity(Entity.None, state, presenter);
    }

    /// <summary>How many dynamic entities the last snapshot had (characters, and what the modules presented).</summary>
    internal int NetEntityCount => _netStates.Count;

    private void Replicate()
    {
        uint tick = (uint)Zone.Tick;
        _grid.Clear();
        _netStates.Clear();
        ComponentPool<NetPresence> presences = Zone.Entities.Pool<NetPresence>();
        ReadOnlySpan<int> slots = presences.Slots;
        Span<NetPresence> values = presences.Values;
        for (int i = 0; i < values.Length; i++)
        {
            Entity entity = Zone.Entities.AtSlot(slots[i]);
            ref Character character = ref Zone.Entities.Get<Character>(entity);
            _grid.Add(values[i].NetId, character.State.Position);
            CharacterNetState state = NetConvert.ToNet(character.State, values[i].Yaw, values[i].Pitch, values[i].LookYaw, values[i].LookPitch);
            if (Zone.Players().PlayerOf(entity) is { } p)
            {
                state.Held = p.IsAwakeAndAlive ? p.HeldItem : (ushort)0;
                // Only an awake player's head looks around (a sleeper sends nothing more, a body nothing at all).
                state.LookYaw = p.IsAwakeAndAlive ? state.LookYaw : (sbyte)0;
                state.LookPitch = p.IsAwakeAndAlive ? state.LookPitch : (sbyte)0;
                state.ActionCount = (byte)(p.ActionCount & 15);
                state.Action = p.LastAction;
                state.Sleeping = p.Sleeping && !p.Dead;
                // G4: a gun aimed down its sights, and what is on it, for others to draw.
                state.HeldMods = p.CanAct ? Zone.Guns().HeldGunLook(p) : (byte)0;
                state.Aiming = p.CanAct && p.Gun.Item != 0 && !p.Gun.Reloading && character.Actions.Has(ActionButtons.Secondary);
            }
            _netStates[values[i].NetId] = new NetEntity(entity, state, null);
        }
        foreach (ServerModule module in _modules)
        {
            module.Prepare();
        }

        foreach (PlayerSession session in _sessions.Values)
        {
            if (!session.Playing)
            {
                continue;
            }
            ref Character own = ref Zone.Entities.Get<Character>(session.Entity);
            Vector3 centre = own.State.Position;
            ViewAs(session);

            // Relevance: who this player should hear about now, and the spawns and despawns that changes.
            _grid.Query(centre, session.Relevant, _relevantIndices);
            _nowRelevant.Clear();
            foreach (int index in _relevantIndices)
            {
                uint netId = _grid[index].NetId;
                if (netId != session.NetId)
                {
                    _nowRelevant.Add(netId);
                }
            }
            _gone.Clear();
            foreach (uint netId in session.Relevant)
            {
                if (!_nowRelevant.Contains(netId))
                {
                    _gone.Add(netId);
                }
            }
            foreach (uint netId in _gone)
            {
                session.Relevant.Remove(netId);
                session.Snapshots.Forget(netId);
                Despawned(session, netId);
            }
            _candidates.Clear();
            foreach (int index in _relevantIndices)
            {
                (uint netId, Vector3 position) = _grid[index];
                if (netId == session.NetId)
                {
                    continue;
                }
                (Entity entity, CharacterNetState state, ServerModule? presenter) = _netStates[netId];
                bool arrived = session.Relevant.Add(netId);
                if (arrived)
                {
                    // A character, or what a module presented (an animal: its species).
                    Spawn spawn = presenter == null
                        ? new Spawn { NetId = netId, EntityId = Zone.Entities.IdOf(entity).Value, Kind = EntityKind.Character, Name = Zone.Entities.Get<Named>(entity).Name }
                        : default;
                    presenter?.Describe(netId, ref spawn);
                    _writer.Reset();
                    spawn.WriteMessage(_writer);
                    session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
                }
                if (presenter == null)
                {
                    foreach (ServerModule module in _onSeen)
                    {
                        module.Seen(session, netId, entity, arrived);
                    }
                }
                else if (presenter.AtRest(session, netId, state))
                {
                    continue;
                }
                float distance = Vector3.Distance(position, centre);
                _candidates.Add(new SnapshotCandidate
                {
                    NetId = netId,
                    State = state,
                    Weight = 1f + 3f * MathF.Max(0f, 1f - distance / _options.RelevanceRadius),
                });
            }

            var header = new SnapshotHeader
            {
                ServerTick = tick,
                InputMargin = session.InputMargin(Zone.Tick),
                OwnTick = session.LastSimulatedTick,
                Own = NetConvert.ToOwn(own.State),
                EchoTime = session.EchoTime,
                EchoHold = (ushort)Math.Min(ushort.MaxValue, Stopwatch.GetElapsedTime(session.EchoReceived).TotalMilliseconds),
            };
            _ships.Clear();
            foreach (ServerModule module in _modules)
            {
                module.Snapshot(session, centre, ref header, _ships);
            }
            using (Profile.Time(ProfileSection.ReplicateSnapshots))
            {
                int budget = Math.Min(Protocol.SnapshotByteBudget, session.Peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced));
                _writer.Reset();
                session.Snapshots.Write(_writer, header, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_candidates),
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_ships), budget);
                session.Peer.Send(_writer.Data, DeliveryMethod.Sequenced);
            }
            foreach (ServerModule module in _modules)
            {
                module.Replicate(session, centre);
            }
        }
        foreach (ServerModule module in _modules)
        {
            module.Finish();
        }
    }

    // The player as a viewer for this snapshot (GROUND_SYSTEMS.md §3.3), and whether that changed since the last: their
    // team (and who is in it) and the keys they carry decide what they are told of doors, things and ships.
    private void ViewAs(PlayerSession session)
    {
        if (Zone.Content == null || Zone.Players().PlayerOf(session.Entity) is not { } p)
        {
            session.ViewerChanged = false;
            return;
        }
        Viewer viewer = Zone.Targets().ViewerOf(p);
        ulong signature = Hash64.Of(viewer.Id.Value, viewer.Team?.Id ?? 0, viewer.Team?.Revision ?? 0);
        foreach (int key in viewer.Keys ?? Array.Empty<int>())
        {
            signature = Hash64.Combine(signature, (ulong)key);
        }
        session.Viewer = viewer;
        session.ViewerChanged = signature != session.ViewerSignature;
        session.ViewerSignature = signature;
    }

    // ---------------------------------------------------------------- loading and saving the world

    // Everything stored about this body: characters come back asleep (or dead), then each module's records
    // (things where they were, natural objects as players left them, the building, ships, explosives, teams, corpses).
    private void LoadWorld()
    {
        if (_persistence == null)
        {
            return;
        }
        ZoneRecord stored = _persistence.LoadZone(Spec.SystemAddress, Spec.BodyPath).GetAwaiter().GetResult();
        int sleepers = 0;
        foreach (CharacterRecord r in stored.Characters)
        {
            if (!CharacterRecords.Restorable(Zone, Spec, r, out Vector3 local) || Zone.Entities.TryFind(r.Id, out _))
            {
                continue;
            }
            CharacterState state = Zone.NewCharacter(local + Zone.Frame.UpAt(local) * 0.02f);
            state.Stance = (Stance)Math.Min(r.Stance, (byte)Stance.Crouching);
            Entity entity = Zone.Players().AddPlayer(r.Id, r.Name, state, CharacterRecords.Decode(r, Content, Log));
            Zone.Entities.Get<Character>(entity).Input.Yaw = r.Yaw;
            Zone.Players().Sleep(entity);
            Zone.Entities.Add(entity, new NetPresence { NetId = _nextNetId++, Yaw = Quantize.Angle16(r.Yaw), Pitch = Quantize.Angle16(r.Pitch) });
            _accountOf[r.Id] = r.AccountId;
            sleepers++;
        }
        foreach (ServerModule module in _modules)
        {
            module.Restore(stored, _savedThings);
        }
        Log($"loaded {sleepers} sleepers");
    }

    // The whole zone in one transaction: every character (players and sleepers), then each module's records, and the
    // things gone since the last save (those the modules didn't write again).
    private ZoneSave BuildSave()
    {
        var save = new ZoneSave
        {
            SystemAddress = Spec.SystemAddress,
            BodyPath = Spec.BodyPath,
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        ComponentPool<PlayerLink> players = Zone.Entities.Pool<PlayerLink>();
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            if (_accountOf.GetValueOrDefault(p.Id) != 0)
            {
                save.Characters.Add(CharacterRecords.Encode(Zone, Spec, Zone.Entities.AtSlot(players.Slots[i]), p, _accountOf.GetValueOrDefault(p.Id)));
            }
        }
        var current = new HashSet<EntityId>();
        foreach (ServerModule module in _modules)
        {
            module.Save(save, current);
        }
        foreach (EntityId id in _savedThings)
        {
            if (!current.Contains(id))
            {
                save.RemovedThings.Add(id);
            }
        }
        _savedThings.Clear();
        _savedThings.UnionWith(current);
        return save;
    }

    /// <summary>Queues every character (players and sleepers), static thing and natural change to be written (one transaction).</summary>
    public Task SaveAll()
    {
        if (_persistence == null)
        {
            return Task.CompletedTask;
        }
        return _persistence.SaveZone(BuildSave());
    }

    /// <summary>A save within the second (deaths commit at once, ARCHITECTURE.md §13: within a second, several deaths in one save).</summary>
    internal void SaveSoon() => _deathToSave = true;

    // ---------------------------------------------------------------- shutting down

    private void Kick(PlayerSession session, GoodbyeReason reason, string text)
    {
        _writer.Reset();
        new Goodbye { Reason = reason, Text = text }.WriteMessage(_writer);
        session.Peer.Disconnect(_writer.ToArray());
    }

    private void Shutdown()
    {
        if (_shutDown)
        {
            return;
        }
        _shutDown = true;
        Log("shutting down: saving the world");
        try
        {
            SaveAll().Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException e)
        {
            Log($"saving failed: {e.InnerException?.Message}");
        }
        foreach (PlayerSession session in _sessions.Values.ToList())
        {
            Kick(session, GoodbyeReason.ServerShutdown, "The server is shutting down.");
        }
        // Let the goodbyes go out.
        for (int i = 0; i < 5; i++)
        {
            _net.ManualUpdate(10);
            Thread.Sleep(10);
        }
        _net.Stop();
        for (int i = _modules.Length - 1; i >= 0; i--)
        {
            _modules[i].Dispose();
        }
        _persistence?.Dispose();
        Zone.Dispose();
        Log("server stopped");
    }

    public void Dispose()
    {
        if (_thread != null)
        {
            Stop();
        }
        else
        {
            Shutdown();
        }
        _stop.Dispose();
    }

    // ---------------------------------------------------------------- the rest of the listener

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) => Log($"network error from {endPoint}: {socketError}");

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    public void OnMessageDelivered(NetPeer peer, object userData)
    {
    }

    public void OnNtpResponse(LiteNetLib.Utils.NtpPacket packet)
    {
    }

    public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress)
    {
    }

    internal void Log(string line) => _options.Log(line);

    internal static string Format(Vector3 p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})");
}
