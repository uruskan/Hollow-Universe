using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Net.Transport;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Surface;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>The server's part of a networked entity: its net ID, view angles and owner.</summary>
public struct NetPresence
{
    public uint NetId;
    public ushort Yaw;
    public ushort Pitch;
    public PlayerSession? Owner;
}

/// <summary>
/// The dedicated server (GROUND.md §4.2): one zone on the first world, ticking at 30 Hz on one thread, with
/// development login by name, snapshot replication, chat, persistence and admin commands, and from G2 the survival
/// game (GameServer.Survival.cs). The console program runs it on its main thread; the game runs it on a background
/// thread for solo play (Host mode). Everything below happens on the tick thread; <see cref="Execute"/> and
/// <see cref="RequestStop"/> may be called from anywhere.
/// </summary>
public sealed partial class GameServer : INetEventListener, IDisposable
{
    public const int TickRate = CharacterMotor.TickRate;

    private readonly ServerOptions _options;
    private readonly NetManager _net;
    private readonly PersistenceWorker? _persistence;
    private readonly EntityIdAllocator _ids;
    private readonly Dictionary<NetPeer, PlayerSession> _sessions = new();
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly ConcurrentQueue<Action> _inspections = new();
    private readonly BitWriter _writer = new(2048);
    private readonly BitReader _reader = new();
    private readonly RelevanceGrid _grid;
    private readonly List<int> _relevantIndices = new();
    private readonly HashSet<uint> _nowRelevant = new();
    private readonly List<uint> _gone = new();
    private readonly List<SnapshotCandidate> _candidates = new();
    private readonly Dictionary<uint, (Entity Entity, CharacterNetState State)> _netStates = new();
    private readonly CancellationTokenSource _stop = new();
    private uint _nextNetId = 1;
    private double _snapshotAccumulator;
    private long _lastSave;
    private long _lastNetUpdate;
    private Thread? _thread;
    private bool _shutDown;
    private int _failedTicks;

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
        _thingGrid = new RelevanceGrid(options.RelevanceRadius);
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
        if (Zone.Surface is PlanetSurface planet)
        {
            if (Spec.CreateSky() is { } sky)
            {
                Zone.Climate = new PlanetClimate(Content.Survival, planet, Zone.Frame, sky);
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
        LoadWorld();
        StartBuilding();
        StartRaid();
        Zone.Profile = Profile;
        _lastSave = Stopwatch.GetTimestamp();
        _lastNetUpdate = Stopwatch.GetTimestamp();
        Log($"{options.Name}: zone on {Spec}, port {Port}, " +
            (options.DatabasePath != null ? $"database {options.DatabasePath}" : "no persistence") + $", data {Content.Hash:X16}");
    }

    public ZoneSpec Spec { get; }

    public Zone Zone { get; }

    /// <summary>The real beaches near the zone where players arrive and respawn (null on the stand-in ground).</summary>
    public Simulation.Beaches? Beaches { get; }

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

    // ---------------------------------------------------------------- running

    /// <summary>Runs ticks at 30 Hz on the calling thread until <see cref="RequestStop"/>, then shuts down.</summary>
    public void Run()
    {
        TimerResolution.Begin();
        // The tick can't wait for a blocking full collection (GROUND.md §5.10): full collections run in the background
        // while it ticks, as in the game's own process.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
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

    /// <summary>One tick: receive, log players in, step the zone, replicate, save, send.</summary>
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
            FinishRespawns();
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
            ApplyShipInput(session, input);
        }
        long stepStarted = Stopwatch.GetTimestamp();
        using (Profile.Time(ProfileSection.Step))
        {
            Zone.Step();
        }
        StepTime += Stopwatch.GetElapsedTime(stepStarted);
        ReportFalls();
        using (Profile.Time(ProfileSection.AfterStep))
        {
            AfterStep();
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

    /// <summary>Where the ticks go (GROUND.md §5.10): the load test prints it.</summary>
    public TickProfile Profile { get; } = new();

    private void ReportFalls()
    {
        foreach (PlayerSession session in _sessions.Values)
        {
            if (session.Playing && Zone.Entities.Get<Character>(session.Entity).Events.FallDamage > 0)
            {
                ref Character c = ref Zone.Entities.Get<Character>(session.Entity);
                Log($"{session.Name} fell: {c.Events.LandingSpeed:0.0} m/s, {c.Events.FallDamage} damage, {c.State.Health} health left");
            }
        }
    }

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
        if (record != null && Zone.TryFindPlayer(record.Id, out _, out _))
        {
            return null;
        }
        return record != null && Restorable(record, out Vector3 local) ? local : Arrival(Hash64.Of(session.Name.ToUpperInvariant()));
    }

    /// <summary>
    /// A player joins: their sleeper wakes where it lies (logged off earlier, or loaded at start); a stored character
    /// that isn't in the zone comes back from its record; anyone else starts a new life at the spawn.
    /// </summary>
    private void Spawn(PlayerSession session, CharacterRecord? record)
    {
        EntityId id;
        Entity entity;
        float yaw = 0, pitch = 0;
        string how;
        if (record != null && Zone.TryFindPlayer(record.Id, out entity, out Player _))
        {
            id = record.Id;
            Zone.Wake(entity);
            yaw = Zone.Entities.Get<Character>(entity).Input.Yaw;
            pitch = record.Pitch;
            how = "woke";
        }
        else
        {
            CharacterState state;
            Player? player = null;
            if (record != null && Restorable(record, out Vector3 local))
            {
                id = record.Id;
                state = Zone.NewCharacter(local + Zone.Frame.UpAt(local) * 0.02f);
                state.Stance = (Stance)Math.Min(record.Stance, (byte)Stance.Crouching);
                player = DecodePlayer(record);
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
            entity = Zone.AddPlayer(id, session.Name, state, player);
            Zone.Entities.Get<Character>(entity).Input.Yaw = yaw;
            if (how == "new" && (_options.ShipOnJoin || _options.ShipOnJoinFor?.Invoke(session.Name) == true))
            {
                _shipOnJoin.Add(session);
            }
            if (how == "new" && _options.JoinKit != null && Content.Loot(_options.JoinKit) is { } kit && Zone.PlayerOf(entity) is { } fresh)
            {
                GiveKit(fresh, kit);
            }
        }
        if (Zone.PlayerOf(entity) is { } chosen)
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
        Log($"{session.Name} joined from {session.Peer} as {id} ({how}) at {Format(Zone.Entities.Get<Character>(entity).State.Position)}");
        if (_shipOnJoin.Remove(session))
        {
            GiveShip(session);
        }
        QueueAlertDelivery(session);
    }

    private readonly HashSet<PlayerSession> _shipOnJoin = new();

    // A stored character comes back where it was if that is on this zone's body and within its ground.
    private bool Restorable(CharacterRecord record, out Vector3 local)
    {
        local = Zone.Frame.ToLocal(new Vector3D(record.BodyX, record.BodyY, record.BodyZ));
        float reach = Simulation.Physics.PhysicsWorld.Extent - 2 * Simulation.Physics.PhysicsWorld.TileSize;
        return record.SystemAddress == Spec.SystemAddress && record.BodyPath == Spec.BodyPath &&
            MathF.Abs(local.X) < reach && MathF.Abs(local.Z) < reach &&
            // A pilot may be high up, in orbit even (G6): their ship's seat brings them back.
            MathF.Abs(local.Y) < (record.Stance == (byte)Stance.Seated ? 25_000 : 5000);
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
            Zone.Sleep(session.Entity);
            ref NetPresence presence = ref Zone.Entities.Get<NetPresence>(session.Entity);
            presence.Owner = null;
            _sessionOf.Remove(session.EntityId);
            if (_persistence != null && Zone.PlayerOf(session.Entity) is { } p)
            {
                _persistence.Save(new[] { RecordOf(session.Entity, p) });
            }
            session.Entity = Entity.None;
        }
        Log($"{session.Name} left ({info.Reason})");
    }

    // ---------------------------------------------------------------- receiving

    public void OnNetworkReceive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
    {
        if (!_sessions.TryGetValue(peer, out PlayerSession? session))
        {
            return;
        }
        // Read in place: the transport recycles the packet after this returns.
        _reader.Reset(packet.RawData, packet.Position, packet.AvailableBytes);
        uint type = _reader.ReadBits(8);
        switch (type)
        {
            case InputPacket.TypeId:
                InputPacket input = InputPacket.Read(_reader);
                if (_reader.Failed)
                {
                    session.BadPackets++;
                    return;
                }
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
                break;
            case ChatSend.TypeId:
                ChatSend chat = ChatSend.Read(_reader);
                if (_reader.Failed || string.IsNullOrWhiteSpace(chat.Text))
                {
                    session.BadPackets++;
                    return;
                }
                Chat(session, chat.Text.Trim());
                break;
            default:
                if (!ReceiveIntent(session, type))
                {
                    session.BadPackets++;
                }
                break;
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

    private void Broadcast(ChatLine line)
    {
        _writer.Reset();
        line.WriteMessage(_writer);
        foreach (PlayerSession session in _sessions.Values)
        {
            session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
        }
    }

    // ---------------------------------------------------------------- replication

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
            CharacterNetState state = NetConvert.ToNet(character.State, values[i].Yaw, values[i].Pitch);
            if (Zone.PlayerOf(entity) is { } p)
            {
                state.Held = p.IsAwakeAndAlive ? p.HeldItem : (ushort)0;
                state.ActionCount = (byte)(p.ActionCount & 15);
                state.Action = p.LastAction;
                state.Sleeping = p.Sleeping && !p.Dead;
                // G4: a gun aimed down its sights, and what is on it, for others to draw.
                state.HeldMods = p.CanAct ? Zone.HeldGunLook(p) : (byte)0;
                state.Aiming = p.CanAct && p.Gun.Item != 0 && !p.Gun.Reloading && character.Actions.Has(ActionButtons.Secondary);
            }
            _netStates[values[i].NetId] = (entity, state);
        }
        PrepareThings();
        PrepareShips();
        PrepareExplosives();

        foreach (PlayerSession session in _sessions.Values)
        {
            if (!session.Playing)
            {
                continue;
            }
            ref Character own = ref Zone.Entities.Get<Character>(session.Entity);
            Vector3 centre = own.State.Position;

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
                session.LooksSent.Remove(netId);
                _writer.Reset();
                new Despawn { NetId = netId }.WriteMessage(_writer);
                session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
            }
            _candidates.Clear();
            foreach (int index in _relevantIndices)
            {
                (uint netId, Vector3 position) = _grid[index];
                if (netId == session.NetId)
                {
                    continue;
                }
                (Entity entity, CharacterNetState state) = _netStates[netId];
                if (session.Relevant.Add(netId))
                {
                    _writer.Reset();
                    new Spawn
                    {
                        NetId = netId,
                        EntityId = Zone.Entities.IdOf(entity).Value,
                        Kind = EntityKind.Character,
                        Name = Zone.Entities.Get<Named>(entity).Name,
                    }.WriteMessage(_writer);
                    session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
                }
                SendLook(session, netId, entity);
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
                OwnShip = OwnShip(session)!,
            };
            ReplicateShips(session, centre);
            using (Profile.Time(ProfileSection.ReplicateSnapshots))
            {
                int budget = Math.Min(Protocol.SnapshotByteBudget, session.Peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced));
                _writer.Reset();
                session.Snapshots.Write(_writer, header, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_candidates),
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_shipCandidates), budget);
                session.Peer.Send(_writer.Data, DeliveryMethod.Sequenced);
            }

            using (Profile.Time(ProfileSection.ReplicateThings))
            {
                ReplicateThings(session, centre);
            }
            using (Profile.Time(ProfileSection.ReplicateNature))
            {
                ReplicateNature(session, centre);
            }
            using (Profile.Time(ProfileSection.ReplicateBuilding))
            {
                ReplicateBuilding(session, centre);
                ReplicateExplosives(session, centre);
            }
        }
        FinishThings();
        FinishBuilding();
    }

    // ---------------------------------------------------------------- saving and shutting down

    /// <summary>Queues every character (players and sleepers), static thing and natural change to be written (one transaction).</summary>
    public Task SaveAll()
    {
        if (_persistence == null)
        {
            return Task.CompletedTask;
        }
        return _persistence.SaveZone(BuildSave());
    }

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

    private void Log(string line) => _options.Log(line);

    private static string Format(Vector3 p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})");
}
