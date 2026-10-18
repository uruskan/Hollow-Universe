using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Surface;
using LiteNetLib;

namespace Hollow.Client;

public enum ClientState
{
    Idle,
    Connecting,
    Joining,
    Playing,
    Disconnected,
}

/// <summary>
/// The client without graphics (GROUND.md §4.3): the connection, clock synchronisation with the server's ticks, input
/// sent every tick, prediction and reconciliation of the player's own character, interpolation of everyone else 100 ms
/// in the past, a mirror of the entities it was told about, and statistics. The Godot game draws it; bots drive it with
/// scripted input. Call <see cref="Update"/> every frame from one thread. The rest of the game on the client is its
/// modules (GROUND_SYSTEMS.md §12.5, K11): one <see cref="ClientModule"/> per concern, listed in
/// <see cref="ClientModules"/>, their message handlers in one table (§12.4) and their hooks called here in a fixed
/// order; the presentation reads each through its accessor (<c>client.Raid()</c>, <c>client.Ships()</c>...).
/// </summary>
public sealed class GameClient : INetEventListener, IDisposable
{
    public const int TickRate = CharacterMotor.TickRate;
    public LocalMelee Melee { get; }
    public AttackLedger Attacks => Local.Attacks;

    /// <summary>The prediction clock aims to have inputs reach the server this many ticks early, at the latest.</summary>
    public const int TargetInputMargin = 2;

    /// <summary>A frame runs at most this many ticks; after a longer hitch the client skips ahead instead.</summary>
    private const int MaxTicksPerUpdate = 8;

    private readonly ClientOptions _options;
    private readonly NetManager _net = null!;
    private readonly BitWriter _writer = new(512);
    private readonly BitReader _reader = new();
    private SnapshotReceiver _snapshots = new();
    private readonly List<(uint NetId, CharacterNetState State)> _decoded = new();
    private readonly List<(uint NetId, ShipNetState State)> _decodedShips = new();
    private readonly Dictionary<uint, RemoteEntity> _remotes = new();
    private readonly InputCommand[] _sent = new InputCommand[Protocol.RedundantInputs + 1];
    private readonly MessageHandlers<GameClient> _handlers = new();
    private readonly ClientModule[] _modules = Array.Empty<ClientModule>();
    private readonly Dictionary<Type, ClientModule> _byType = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private ConditionedRelay? _ownRelay;
    private NetPeer? _peer;
    private readonly ClientWorldSession _worldSession;
    private readonly ClientWorldHandshake _worldHandshake = new();
    private Predictor? _predictor => _worldSession.Prediction;
    private ISurface? _surface => _worldSession.Surface;
    private uint _nextTick;
    private double _tickClock;
    private double _rate = 1;
    private double _marginCooldown;
    private double _remoteClock;
    private bool _remoteClockStarted;
    private bool _jumpLatched;
    private bool _primaryLatched;
    private bool _stationPressLatched, _stationFireWasFrame;
    private Vector3 _smoothing;
    private float _eyeHeight = MovementTuning.Default.StandingEyeHeight;
    private double _lastMarginLog;
    private double _roundTrip = -1;
    private double _connectStarted;
    private long _inputsSent;
    private uint _firstInputTick;
    private int _pruneCounter;
    private bool _disposed;

    public GameClient(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _worldSession = new ClientWorldSession(options, ClearWorldReplication);
        try
        {
            _net = ClientTransport.CreateNetwork(this);
            Content = options.Content ?? GameContent.LoadDefault();
            Local = new LocalPlayer(Content) { Send = SendReliable, SendGun = SendUnordered };
            Melee = new LocalMelee(Attacks);
            // The host's own messages; the snapshot is read apart (OnNetworkReceive); every other type is a module's.
            _handlers
                .On<WorldOffer>(static (GameClient client, in WorldOffer m) => client.OnWorldOffer(m))
                .On<Welcome>(static (GameClient client, in Welcome m) => client.OnWelcome(m))
                .On<Spawn>(static (GameClient client, in Spawn m) => client.OnSpawn(m))
                .On<Despawn>(static (GameClient client, in Despawn m) => client.OnDespawn(m.NetId))
                .On<ChatLine>(static (GameClient client, in ChatLine m) => client.ChatReceived?.Invoke(m));
            _modules = ClientModules.All();
            foreach (ClientModule module in _modules)
            {
                if (!_byType.TryAdd(module.GetType(), module))
                {
                    throw new InvalidOperationException($"{module.GetType().Name} is in the client's list twice (GROUND_SYSTEMS.md §12.5)");
                }
                module.Attach(this);
            }
            foreach (ClientModule module in _modules)
            {
                _worldSession.Initialize(module, _handlers);
            }
            bool started = options.BackgroundTransport ? _net.Start(0) : _net.StartInManualMode(0);
            if (!started) throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }
        catch (Exception opening)
        {
            _worldSession.Rollback(opening, () => _net?.Stop());
            throw;
        }
    }

    public ClientState State { get; private set; }

    /// <summary>Why the connection ended (the server's words when it gave any).</summary>
    public string? DisconnectReason { get; private set; }

    public GoodbyeReason? Goodbye { get; private set; }

    public string Name => _options.Name;

    /// <summary>The body the player chose (0 male, 1 female).</summary>
    public byte Body => _options.Body;

    public string ServerName { get; private set; } = "";

    public ZoneSpec? Zone => _worldSession.Zone;

    public ISurface? Surface => _surface;

    /// <summary>Independent ownership for maps, rendering and shared physics. Release after their workers retire.</summary>
    public WorldLease ForkWorldLease() => _worldSession.ForkLease();

    /// <summary>Owner-thread notification before modules and physics retire; views stop work and keep their own pins.</summary>
    public event Action? WorldRetiring
    {
        add => _worldSession.WorldRetiring += value;
        remove => _worldSession.WorldRetiring -= value;
    }

    public System.Threading.Tasks.Task EnvironmentRetirement => _worldSession.Retirement;

    public ZoneFrame Frame => _predictor?.Frame ?? default;

    /// <summary>The ground world prediction runs in (the game also draws its tiles).</summary>
    public PhysicsWorld? Physics => _predictor?.World;

    public uint NetId { get; private set; }

    public EntityId EntityId { get; private set; }

    /// <summary>The view the server restored (to turn the camera to on joining).</summary>
    public float InitialYaw => Facing.YawOf(InitialView, OwnUp);

    public float InitialPitch => Facing.PitchOf(InitialView, OwnUp);

    /// <summary>The view the character was saved with, a direction in the zone's frame (the Welcome's).</summary>
    public Vector3 InitialView { get; private set; } = Vector3.UnitZ;

    /// <summary>Up where the player's predicted character stands, in its frame (aboard, the deck's +Y); the zone's +Y before the first state.</summary>
    public Vector3 OwnUp => _predictor is { HasState: true } p ? p.UpIn(p.Latest.Frame) : Vector3.UnitY;

    /// <summary>The frame the newest predicted state is in (IRON_WAKE.md §4.2): its ship's net ID aboard, 0 the zone's.</summary>
    public uint OwnFrame => (uint)Character.Frame;

    // The input's view in a frame: the camera's carried direction, or a heading and pitch read where the player stands.
    private Vector3 ViewOf(in InputFrame frame, uint viewFrame)
    {
        float length = frame.View.Length();
        return length > 1e-6f && float.IsFinite(length) ? frame.View / length
            : Facing.View(frame.Yaw, frame.Pitch, _predictor is { HasState: true } p ? p.UpIn(viewFrame) : Vector3.UnitY);
    }

    /// <summary>Where a ship's frame is drawn now (IRON_WAKE.md §4.8), by its net ID: the ships' module's; null for one not known.</summary>
    internal Func<uint, ShipPose?>? DrawnFrame { get; set; }

    /// <summary>A point of a frame where it is drawn now: aboard a ship, on the ship as drawn; the zone's frame's, itself.</summary>
    public Vector3 Drawn(uint frame, Vector3 local) => frame != 0 && DrawnFrame?.Invoke(frame) is { } ship ? ship.ToWorld(local) : local;

    public ClientStats Stats { get; } = new();

    /// <summary>Chat lines as they arrive.</summary>
    public event Action<ChatLine>? ChatReceived;

    /// <summary>The game's data (the same as the server's: checked at connect).</summary>
    public GameContent Content { get; }

    /// <summary>The player's own survival state and actions: the UI binds to this.</summary>
    public LocalPlayer Local { get; }

    /// <summary>Seconds on the client's clock (what projectiles and countdowns are timed by).</summary>
    public double ClockSeconds => _clock.Elapsed.TotalSeconds;

    /// <summary>The server's tick as the client estimates it now, fractional (explosives' arcs and fuses count by it).</summary>
    public double ServerTickNow => _remoteClock;

    // ---------------------------------------------------------------- the modules (GROUND_SYSTEMS.md §12.5)

    /// <summary>The modules, in the order their hooks run.</summary>
    public IReadOnlyList<ClientModule> Modules => _modules;

    /// <summary>The module of a type, or null when this client has none.</summary>
    public T? Get<T>() where T : ClientModule => _byType.TryGetValue(typeof(T), out ClientModule? module) ? (T)module : null;

    /// <summary>The module of a type that this client must have.</summary>
    public T Require<T>() where T : ClientModule =>
        Get<T>() ?? throw new InvalidOperationException($"this client has no {typeof(T).Name} (its list is {string.Join(", ", _modules.Select(m => m.GetType().Name))})");

    // What the modules share of the host (through ClientModule's forwarders), beyond its public state.
    internal ClientOptions Options => _options;

    internal double TickClock => _tickClock;

    internal uint NextTick => _nextTick;

    internal Dictionary<uint, RemoteEntity> Remotes => _remotes;

    internal List<(uint NetId, ShipNetState State)> DecodedShips => _decodedShips;

    // ---------------------------------------------------------------- the player's character

    /// <summary>True once the server has placed the character and prediction runs.</summary>
    public bool HasCharacter => _predictor?.HasState == true;

    /// <summary>The newest predicted state.</summary>
    public CharacterState Character => _predictor?.Latest ?? default;

    /// <summary>The newest predicted tick.</summary>
    public uint PredictedTick => _predictor?.LatestTick ?? 0;

    /// <summary>
    /// The feet to draw now, in the world: between the last two predicted ticks plus what is left of a smoothed correction,
    /// in the character's frame (<see cref="RenderLocal"/>), placed where that frame is drawn (aboard, on the ship).
    /// </summary>
    public Vector3 RenderPosition => Drawn(RenderFrame, RenderLocal);

    public Vector3 RenderLocal { get; private set; }

    public uint RenderFrame { get; private set; }

    /// <summary>Up at the feet as drawn (aboard, the ship's), and the eye there.</summary>
    public Vector3 RenderUp => RenderFrame != 0 && DrawnFrame?.Invoke(RenderFrame) is { } ship ? ship.Up : Frame.UpAt(RenderPosition);

    public Vector3 RenderEye => SeatEye?.Invoke() ?? RenderPosition + RenderUp * EyeHeight;

    /// <summary>The eye of the seat the player sits in, as drawn (a station's camera socket, IRON_WAKE.md §5.2), or null.</summary>
    internal Func<Vector3?>? SeatEye { get; set; }

    /// <summary>The eye's height above the feet, easing between stances.</summary>
    public float EyeHeight => _eyeHeight;

    public Predictor? Prediction => _predictor;

    // ---------------------------------------------------------------- everyone else

    /// <summary>
    /// Characters this client was told about (spawned), interpolated for drawing. Animals (G7) are entities too, listed
    /// apart in <see cref="AnimalModule.All"/>; ships ride in the snapshots' ship section (<see cref="ShipModule"/>).
    /// </summary>
    public IEnumerable<RemoteEntity> Entities => _remotes.Values.Where(r => r.Spawned && r.HasState && r.Kind == EntityKind.Character);

    public int EntityCount => _remotes.Values.Count(r => r.Spawned && r.Kind == EntityKind.Character);

    /// <summary>The server tick remote entities are drawn at.</summary>
    public double RenderTick => _remoteClock - Protocol.InterpolationDelaySeconds * TickRate;

    // ---------------------------------------------------------------- connecting

    /// <summary>Connects to a server (through a bad-network relay if the options ask for one).</summary>
    public void Connect(string host, int port)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _worldHandshake.Reset(_clock.Elapsed.TotalSeconds);
        _peer = ClientTransport.Connect(_net, _options, Content.Hash, _writer, host, port, ref _ownRelay,
            out IPEndPoint server, out IPEndPoint target);
        _connectStarted = _clock.Elapsed.TotalMilliseconds;
        State = ClientState.Connecting;
        _options.Log($"connecting to {server}" + (target != server ? $" through a relay ({_ownRelay?.Conditions ?? _options.SharedRelay?.Conditions})" : ""));
    }

    public void Disconnect()
    {
        _peer?.Disconnect();
        UpdateTransport(0);
    }

    // Normal mode owns its update thread; never run ManualUpdate concurrently with it. Application events remain
    // queued for Update's PollEvents, so no simulation or presentation callback moves to a transport thread.
    private void UpdateTransport(float elapsedMilliseconds) =>
        ClientTransport.Update(_net, _options.BackgroundTransport, elapsedMilliseconds);

    public void SendChat(string text) => ClientTransport.SendChat(_peer, State, _writer, text);

    // ---------------------------------------------------------------- sending

    private bool SendReliable(Action<BitWriter> write) => SendWith(write, DeliveryMethod.ReliableOrdered);

    // Shots and reloads: reliable but unordered, so one lost and resent holds nothing else up (they carry their tick and
    // sequence; the server puts them in order).
    private bool SendUnordered(Action<BitWriter> write) => SendWith(write, DeliveryMethod.ReliableUnordered);

    private bool SendWith(Action<BitWriter> write, DeliveryMethod method)
    {
        if (_peer == null || State != ClientState.Playing)
        {
            return false;
        }
        _writer.Reset();
        write(_writer);
        _peer.Send(_writer.Data, method);
        return true;
    }

    /// <summary>A module's message, in order; false, and nothing sent, while not playing.</summary>
    internal bool SendReliable<T>(in T message) where T : struct, INetMessage<T> => Send(message, DeliveryMethod.ReliableOrdered);

    /// <summary>A module's message, reliable but unordered; false, and nothing sent, while not playing.</summary>
    internal bool SendUnordered<T>(in T message) where T : struct, INetMessage<T> => Send(message, DeliveryMethod.ReliableUnordered);

    private bool Send<T>(in T message, DeliveryMethod method) where T : struct, INetMessage<T> =>
        ClientTransport.Send(_peer, State, _writer, message, method);

    // ---------------------------------------------------------------- every frame

    /// <summary>Receives, runs the ticks that are due (each sending its input), and advances what is drawn.</summary>
    public void Update(double delta, in InputFrame input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _jumpLatched |= input.JumpPressed;
        _primaryLatched |= input.PrimaryPressed;
        _stationPressLatched |= input.Station.FirePressed || (input.Station.Fire && !_stationFireWasFrame);
        _stationFireWasFrame = input.Station.Fire;
        _net.PollEvents();
        CheckWorldHandshakeTimeout(_clock.Elapsed.TotalSeconds);
        Local.AfterMessages();
        if (State == ClientState.Playing)
        {
            _marginCooldown = Math.Max(0, _marginCooldown - delta);
            _tickClock += delta * TickRate * _rate;
            int ran = 0;
            while (_tickClock >= _nextTick && ran < MaxTicksPerUpdate)
            {
                RunTick(_nextTick, input);
                _nextTick++;
                ran++;
            }
            if (_tickClock - _nextTick > TickRate)
            {
                // A hitch of over a second (a debugger, a stall): drop the time; the margin reports set the clock again.
                _tickClock = _nextTick;
            }
            UpdateRender(delta);
            Local.Gun.Render(_tickClock);
            if (_remoteClockStarted)
            {
                _remoteClock += delta * TickRate;
                double renderTick = RenderTick;
                foreach (RemoteEntity remote in _remotes.Values)
                {
                    remote.Sample(renderTick);
                }
                foreach (ClientModule module in _modules)
                {
                    module.Sample(renderTick);
                }
                PruneStale();
            }
            foreach (ClientModule module in _modules)
            {
                module.Update(delta);
            }
        }
        if (_peer != null && _roundTrip < 0)
        {
            Stats.RoundTripMs = _peer.RoundTripTime;
        }
        NetStatistics traffic = _net.Statistics;
        Stats.Update(delta, traffic.BytesReceived, traffic.BytesSent, traffic.PacketsReceived, traffic.PacketsSent);
        UpdateTransport((float)(delta * 1000));
    }

    private void RunTick(uint tick, in InputFrame frame)
    {
        InputFrame tickFrame = frame;
        tickFrame.Station.FirePressed = _stationPressLatched;
        _stationPressLatched = false;
        var buttons = MoveButtons.None;
        if (frame.Jump)
        {
            buttons |= MoveButtons.Jump;
        }
        if (_jumpLatched)
        {
            buttons |= MoveButtons.JumpPressed;
            _jumpLatched = false;
        }
        if (frame.Crouch)
        {
            buttons |= MoveButtons.Crouch;
        }
        if (frame.Sprint)
        {
            buttons |= MoveButtons.Sprint;
        }
        var actions = new ActionInput
        {
            HeldSlot = Local.HeldSlot is >= 0 and < 6 ? (byte)Local.HeldSlot : ActionInput.NoSlot,
            Buttons = (frame.Primary ? ActionButtons.Primary : ActionButtons.None) |
                (frame.Secondary ? ActionButtons.Secondary : ActionButtons.None) |
                (_primaryLatched ? ActionButtons.PrimaryPressed : ActionButtons.None),
            ViewDelay = ViewDelay(tick),
        };
        _primaryLatched = false;
        // The view in the frame the camera holds it in: on the tick of a hand-over, the server and the prediction turn it.
        uint viewFrame = frame.ViewFrame ?? OwnFrame;
        InputCommand command = NetConvert.ToCommand(new MoveInput
        {
            Forward = frame.Forward,
            Right = frame.Right,
            View = ViewOf(frame, viewFrame),
            Buttons = buttons,
            Frame = viewFrame,
            NoclipSpeed = frame.NoclipSpeed,
        }, actions);
        // Free look rides with the view, for others to see the head turn (the prediction never uses it).
        command.LookYaw = NetConvert.ToLook(frame.LookYaw, FreeLook.MaxYaw);
        command.LookPitch = NetConvert.ToLook(frame.LookPitch, FreeLook.MaxPitch);
        bool flies = false;
        foreach (ClientModule module in _modules)
        {
            flies |= module.Flies;
        }
        if (flies)
        {
            // G6: the pilot's controls ride in the input; the ship is predicted first (BeforePredict), as the server steps
            // it, so the seated character goes where its seat is.
            command.Ship = new[] { ShipConvert.ToControls(frame.Ship) };
        }
        foreach (ClientModule module in _modules)
        {
            module.Command(tick, ref command, tickFrame);
        }
        _predictor!.RecordInput(tick, command);
        if (_predictor.HasState)
        {
            foreach (ClientModule module in _modules)
            {
                module.BeforePredict(tick);
            }
            _predictor.Predict(tick);
        }
        _predictor.EndTick();
        if (_predictor.HasState)
        {
            // G4: the gun in hand fires (or reloads) on this tick's predicted state, and sends its intent for this tick.
            Local.Gun.Tick(tick, command, _predictor.Latest, _predictor.Frames, DrawnFrame?.Invoke(OwnFrame), _predictor.Tuning, EntityId,
                !Local.IsDead && !Local.IsWounded && !this.Ships().Seated);
            Melee.Tick(tick, Local.HeldItem, Local.HeldSlot, (command.Actions & (ActionButtons.Primary | ActionButtons.PrimaryPressed)) != 0,
                !Local.IsDead && !Local.IsWounded && !this.Ships().Seated, Content.Survival.EquipSeconds);
        }

        // The packet: this tick's input and the three before it, and which snapshots arrived.
        for (int i = _sent.Length - 1; i > 0; i--)
        {
            _sent[i] = _sent[i - 1];
        }
        _sent[0] = command;
        int count = (int)Math.Min(_sent.Length, Math.Max(1, tick - _firstInputTick + 1));
        var packet = new InputPacket
        {
            AckSequence = _snapshots.LatestSequence,
            AckBits = _snapshots.AckBits,
            NewestTick = tick,
            Inputs = _sent[..count],
            ClientTime = (ushort)_clock.ElapsedMilliseconds,
        };
        _writer.Reset();
        packet.WriteMessage(_writer);
        _peer?.Send(_writer.Data, DeliveryMethod.Sequenced);
        _inputsSent++;
    }

    // How far behind this input's tick the player sees others: the input's tick against the remote render tick.
    private byte ViewDelay(uint tick)
    {
        if (!_remoteClockStarted)
        {
            return 0;
        }
        double behind = tick - RenderTick;
        return (byte)Math.Clamp(Math.Round(behind * 4), 0, 255);
    }

    private void UpdateRender(double delta)
    {
        foreach (ClientModule module in _modules)
        {
            module.Render(delta);
        }
        if (_predictor == null || !_predictor.HasState)
        {
            return;
        }
        uint latest = _predictor.LatestTick;
        CharacterState current = _predictor.Latest;
        Vector3 position = current.Position;
        if ((uint)current.Frame != RenderFrame)
        {
            _smoothing = Vector3.Zero; // handed over between frames: what was left to ease out was the other frame's
        }
        if (_predictor.TryGetState(latest - 1, out CharacterState previous) && previous.Frame == current.Frame)
        {
            // The newest tick is shown when the clock reaches the next one: blend by how far it has got.
            float alpha = (float)Math.Clamp(_tickClock - _nextTick + 1, 0, 1);
            position = Vector3.Lerp(previous.Position, current.Position, alpha);
        }
        _smoothing *= (float)Math.Exp(-delta * 12);
        if (_smoothing.LengthSquared() < 1e-8f)
        {
            _smoothing = Vector3.Zero;
        }
        RenderFrame = (uint)current.Frame;
        RenderLocal = position + _smoothing;
        MovementTuning tuning = _predictor.Tuning;
        float eye = tuning.EyeHeight(current.Stance);
        _eyeHeight += (eye - _eyeHeight) * (float)(1 - Math.Exp(-delta * tuning.EyeHeightRate));
    }

    // ---------------------------------------------------------------- messages

    public void OnPeerConnected(NetPeer peer)
    {
        if (peer != _peer || _disposed) return;
        // The handshake took about a round trip: the first estimate, until snapshots echo inputs.
        _roundTrip = _clock.Elapsed.TotalMilliseconds - _connectStarted;
        State = ClientState.Joining;
        _worldHandshake.Connected(_clock.Elapsed.TotalSeconds);
        _options.Log("connected; joining");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (peer != _peer) return;
        _worldHandshake.ClearOffer();
        State = ClientState.Disconnected;
        DisconnectReason = info.Reason.ToString();
        (Goodbye, DisconnectReason) = _worldHandshake.Disconnected(info, Goodbye);
        try { _options.Log($"disconnected ({DisconnectReason})"); }
        finally { RetireWorld(); }
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
    {
        if (_disposed || peer != _peer || State is ClientState.Disconnected or ClientState.Idle) return;
        // Read in place: the transport recycles the packet after this returns.
        _reader.Reset(packet.RawData, packet.Position, packet.AvailableBytes);
        uint type = _reader.ReadBits(8);
        if (!ClientWorldHandshake.AdmitPacket(type, packet.AvailableBytes, method, State, out string? rejection))
        {
            if (rejection != null) RejectWorldHandshake(rejection);
            else Stats.UnhandledMessages++;
            return;
        }
        if (type == Protocol.SnapshotTypeId)
        {
            OnSnapshot();
        }
        else if (!_handlers.Dispatch((byte)type, _reader, this))
        {
            if (ClientWorldHandshake.IsHandshake(type))
            {
                RejectWorldHandshake("Malformed world handshake packet.");
                return;
            }
            // No module handles the type, or the message didn't read: dropped, as a bad packet (GROUND_SYSTEMS.md §12.4).
            Stats.UnhandledMessages++;
        }
    }

    private void OnWorldOffer(in WorldOffer offer)
    {
        if (_disposed || !_worldSession.CanOpen) return;
        if (!_worldHandshake.TryOffer(offer, _clock.Elapsed.TotalSeconds, out string? rejection))
        {
            if (rejection != null) RejectWorldHandshake(rejection);
            return;
        }
        State = ClientState.Joining;
        _writer.Reset();
        new WorldAccept { OfferId = offer.OfferId, Environment = offer.Environment }.WriteMessage(_writer);
        _peer?.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
    }

    private void CheckWorldHandshakeTimeout(double now)
    {
        if (_worldHandshake.Timeout(now, State) is { } reason) RejectWorldHandshake(reason);
    }

    private void RejectWorldHandshake(string reason)
    {
        _worldHandshake.Reject(reason);
        Disconnect();
        Goodbye = GoodbyeReason.EnvironmentMismatch;
        DisconnectReason = reason;
        State = ClientState.Disconnected;
        RetireWorld();
    }

    private void OnWelcome(in Welcome welcome)
    {
        if (_disposed || !_worldSession.CanOpen) return;
        if (!_worldHandshake.ValidateWelcome(welcome, out string? refusal))
        {
            if (refusal != null) RejectWorldHandshake(refusal);
            return;
        }
        _jumpLatched = _primaryLatched = _stationPressLatched = _stationFireWasFrame = false;
        Welcome message = welcome;
        if (!_worldSession.TryOpen(message.Zone, message.Environment, () => Attacks.BeginSession(message.AttackSession), () =>
        {
            NetId = message.NetId;
            EntityId = new EntityId(message.EntityId);
            Local.EntityId = EntityId;
            ServerName = message.ServerName;
            InitialView = Quantize.FromOctahedral(message.ViewU, message.ViewV);
            Local.Up = () => OwnUp;
            Local.Frame = () => OwnFrame;
            foreach (ClientModule module in _modules)
            {
                if (_disposed) return;
                module.Welcome();
            }
        }, out string? rejection))
        {
            if (rejection != null)
            {
                DisconnectReason = rejection;
                _options.Log(DisconnectReason);
                Disconnect();
                State = ClientState.Disconnected;
                RetireWorld();
            }
            return;
        }
        _worldHandshake.ConsumeWelcome();
        // Start ahead of the Welcome's tick by a round trip (the Welcome came one way, inputs go the other) and the
        // margin; the server's reports refine it.
        double trip = (_roundTrip >= 0 ? _roundTrip : _peer?.RoundTripTime ?? 100) / 1000.0 * TickRate;
        _nextTick = welcome.ServerTick + 1 + (uint)Math.Ceiling(trip) + TargetInputMargin;
        _firstInputTick = _nextTick;
        _tickClock = _nextTick;
        State = ClientState.Playing;
        _options.Log($"joined {ServerName} as {EntityId} on {Zone}");
    }

    private void OnSpawn(in Spawn spawn)
    {
        if (spawn.Kind != EntityKind.Ship)
        {
            // The entities the host interpolates (the snapshots' character section); a ship's state rides in its ship section.
            RemoteEntity remote = Remote(spawn.NetId);
            remote.Spawned = true;
            remote.Id = new EntityId(spawn.EntityId);
            remote.Kind = spawn.Kind;
            remote.Name = spawn.Name;
        }
        foreach (ClientModule module in _modules)
        {
            module.Spawned(spawn);
        }
    }

    private void OnDespawn(uint netId)
    {
        _remotes.Remove(netId);
        foreach (ClientModule module in _modules)
        {
            module.Despawned(netId);
        }
    }

    private void OnSnapshot()
    {
        if (!_snapshots.TryRead(_reader, out SnapshotHeader header, _decoded, _decodedShips))
        {
            return;
        }
        Stats.Snapshot(header.Sequence);
        if (_inputsSent > _sent.Length)
        {
            // Round trip: from sending the echoed input to this snapshot, less the time the server held it.
            int trip = (ushort)((ushort)_clock.ElapsedMilliseconds - header.EchoTime) - header.EchoHold;
            if (trip is >= 0 and < 10_000)
            {
                _roundTrip = _roundTrip < 0 ? trip : _roundTrip + (trip - _roundTrip) * 0.1;
                Stats.RoundTripMs = (int)Math.Round(_roundTrip);
            }
        }

        // Remote clock: follows the newest snapshot tick, gently, so jitter doesn't shake what is drawn.
        if (!_remoteClockStarted || Math.Abs(header.ServerTick - _remoteClock) > 10)
        {
            _remoteClock = header.ServerTick;
            _remoteClockStarted = true;
        }
        else
        {
            _remoteClock += (header.ServerTick - _remoteClock) * 0.1;
        }
        foreach ((uint netId, CharacterNetState state) in _decoded)
        {
            Remote(netId).Add(header.ServerTick, state);
        }
        SteerClock(header.InputMargin);
        foreach (ClientModule module in _modules)
        {
            module.AfterSnapshot(header);
        }

        if (header.OwnTick == 0 || _predictor == null)
        {
            return;
        }
        CharacterState server = NetConvert.FromOwn(header.Own);
        if (!_predictor.HasState)
        {
            _predictor.Reset(header.OwnTick, server, _nextTick - 1);
            (RenderFrame, RenderLocal) = ((uint)_predictor.Latest.Frame, _predictor.Latest.Position);
            return;
        }
        CharacterState before = _predictor.Latest;
        float? error = _predictor.Reconcile(header.OwnTick, server);
        Stats.Prediction(error);
        Vector3 jump = before.Position - _predictor.Latest.Position;
        // Small corrections are eased out over a few frames, within one frame; big ones (a teleport, a respawn) and a
        // change of frame happen at once.
        _smoothing = before.Frame == _predictor.Latest.Frame && (_smoothing + jump).LengthSquared() < 4f ? _smoothing + jump : Vector3.Zero;
    }

    /// <summary>
    /// Keeps inputs arriving at the server <see cref="TargetInputMargin"/> ticks early at the least: a little faster
    /// or slower while close, a jump when far off.
    /// </summary>
    private void SteerClock(sbyte margin)
    {
        if (margin == sbyte.MinValue)
        {
            return;
        }
        Stats.InputMargin = margin;
        if (_marginCooldown > 0)
        {
            return;
        }
        int error = margin - TargetInputMargin;
        if (margin < 0 || error > 10)
        {
            // Inputs arrive late (each one the server lacks is a misprediction) or far too early: move the clock at
            // once, to a tick of slack beyond the target, then let the reports (the least margin over half a second)
            // catch up before steering again. Forward runs the extra ticks over the next frames, so their inputs are
            // still sent; more than a second behind, it skips them (the server repeats the last input for those, and
            // prediction does the same). Back pauses until the clock gets there.
            int move = error < 0 ? -error + 1 : -error;
            if (move > TickRate)
            {
                _nextTick += (uint)move;
            }
            _tickClock += move;
            _rate = 1;
            _marginCooldown = 0.6;
            Stats.ClockJumps++;
            if (_tickClock - _lastMarginLog > 30 * TickRate)
            {
                _lastMarginLog = _tickClock;
                _options.Log($"clock moved by {move} ticks (inputs reached the server {margin} ticks early)");
            }
        }
        else
        {
            // Close: a little faster while short of the target, a little slower while more than two past it.
            _rate = error < 0 ? 1.04 : error > 2 ? 0.97 : 1;
        }
        Stats.ClockRate = _rate;
    }

    // A snapshot can carry an entity just after its despawn arrived; such leftovers are dropped after ten seconds.
    private void PruneStale()
    {
        if (++_pruneCounter % 600 != 0)
        {
            return;
        }
        foreach (RemoteEntity remote in _remotes.Values.Where(r => !r.Spawned && r.NewestTick + 10 * TickRate < _remoteClock).ToList())
        {
            _remotes.Remove(remote.NetId);
        }
    }

    internal RemoteEntity Remote(uint netId)
    {
        if (!_remotes.TryGetValue(netId, out RemoteEntity? remote))
        {
            remote = new RemoteEntity(netId);
            _remotes.Add(netId, remote);
        }
        return remote;
    }

    // ---------------------------------------------------------------- the rest of the listener

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) => _options.Log($"network error: {socketError}");

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();

    public void OnMessageDelivered(NetPeer peer, object userData)
    {
    }

    public void OnNtpResponse(LiteNetLib.Utils.NtpPacket packet)
    {
    }

    public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress)
    {
    }

    private void RetireWorld() => _worldSession.Retire();

    private void ClearWorldReplication()
    {
        _remotes.Clear();
        _decoded.Clear();
        _decodedShips.Clear();
        _snapshots = new SnapshotReceiver();
        _remoteClockStarted = false;
        _smoothing = Vector3.Zero;
        Local?.RetireWorldIntents();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _worldSession.CloseAdmission();
        _worldHandshake.ClearOffer();
        _disposed = true;
        State = ClientState.Disconnected;
        List<Exception> failures = new();
        void Clean(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
        Clean(() => _peer?.Disconnect());
        Clean(() => UpdateTransport(0));
        Clean(() => _net.Stop());
        Clean(_worldSession.Dispose);
        if (_ownRelay != null) Clean(_ownRelay.Dispose);
        _ownRelay = null;
        _peer = null;
        if (failures.Count != 0) throw new AggregateException("Client disposal failed after retiring all owners.", failures).Flatten();
    }
}
