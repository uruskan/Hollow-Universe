using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
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

/// <summary>One frame of the player's controls, as the game (or a bot) reads them.</summary>
public struct InputFrame
{
    /// <summary>−1 … 1: back to forward, left to right.</summary>
    public float Forward;
    public float Right;

    /// <summary>The view now, in radians (see <see cref="MoveInput.Yaw"/>).</summary>
    public float Yaw;
    public float Pitch;

    public bool Jump;
    public bool Crouch;
    public bool Sprint;

    /// <summary>Jump went down this frame; kept until the next tick uses it, so a quick tap is never lost.</summary>
    public bool JumpPressed;

    /// <summary>Attack (left mouse) and aim or draw (right mouse) are held (G2).</summary>
    public bool Primary;
    public bool Secondary;

    /// <summary>Attack went down this frame (kept until a tick uses it, as jump is).</summary>
    public bool PrimaryPressed;

    /// <summary>A pilot's controls (G6): sent with every tick's input while the player flies a ship.</summary>
    public Simulation.Ships.ShipInput Ship;
}

/// <summary>How a client runs.</summary>
public sealed record ClientOptions
{
    public string Name { get; init; } = "Player";

    /// <summary>The body the player chose (0 male, 1 female; GROUND.md §4.8), sent at connect.</summary>
    public byte Body { get; init; }

    /// <summary>Bad-network simulation (through a local relay), or none.</summary>
    public NetConditions Conditions { get; init; } = NetConditions.None;

    /// <summary>A relay shared by many clients in one process (bots), instead of one of its own.</summary>
    public ConditionedRelay? SharedRelay { get; init; }

    /// <summary>Ground shared by many clients in one process (bots); the owner releases unused tiles.</summary>
    public Func<ZoneSpec, ISurface, PhysicsWorld>? SharedPhysics { get; init; }

    /// <summary>
    /// The natural objects' state shared by many clients with <see cref="SharedPhysics"/> (bots): its owner keeps the
    /// shared ground's trees in step with it. Null: an own one on the client's own ground.
    /// </summary>
    public Func<ZoneSpec, ISurface, Simulation.Nature.NaturalState>? SharedNature { get; init; }

    /// <summary>
    /// The replicated building shared by many clients with <see cref="SharedPhysics"/> (bots), on that physics world.
    /// Null: an own one on the client's own ground.
    /// </summary>
    public Func<ZoneSpec, ISurface, PhysicsWorld, BuildingReplica>? SharedBuilding { get; init; }

    /// <summary>The game's data (the server must have the same); null finds and loads game/data/ground.</summary>
    public GameContent? Content { get; init; }

    public Action<string> Log { get; init; } = _ => { };
}

/// <summary>
/// The client without graphics (GROUND.md §4.3): the connection, clock synchronisation with the server's ticks, input
/// sent every tick, prediction and reconciliation of the player's own character, interpolation of everyone else 100 ms
/// in the past, a mirror of the entities it was told about, and statistics. The Godot game draws it; bots drive it with
/// scripted input. Call <see cref="Update"/> every frame from one thread.
/// </summary>
public sealed partial class GameClient : INetEventListener, IDisposable
{
    public const int TickRate = CharacterMotor.TickRate;

    /// <summary>The prediction clock aims to have inputs reach the server this many ticks early, at the latest.</summary>
    public const int TargetInputMargin = 2;

    /// <summary>A frame runs at most this many ticks; after a longer hitch the client skips ahead instead.</summary>
    private const int MaxTicksPerUpdate = 8;

    private readonly ClientOptions _options;
    private readonly NetManager _net;
    private readonly BitWriter _writer = new(512);
    private readonly BitReader _reader = new();
    private readonly SnapshotReceiver _snapshots = new();
    private readonly List<(uint NetId, CharacterNetState State)> _decoded = new();
    private readonly Dictionary<uint, RemoteEntity> _remotes = new();
    private readonly InputCommand[] _sent = new InputCommand[Protocol.RedundantInputs + 1];
    private ConditionedRelay? _ownRelay;
    private NetPeer? _peer;
    private Predictor? _predictor;
    private ISurface? _surface;
    private uint _nextTick;
    private double _tickClock;
    private double _rate = 1;
    private double _marginCooldown;
    private double _remoteClock;
    private bool _remoteClockStarted;
    private bool _jumpLatched;
    private Vector3 _smoothing;
    private float _eyeHeight = MovementTuning.Default.StandingEyeHeight;
    private double _lastMarginLog;
    private bool _disposed;

    public GameClient(ClientOptions options)
    {
        _options = options;
        _net = new NetManager(this)
        {
            AutoRecycle = true,
            EnableStatistics = true,
            DisconnectTimeout = 10_000,
            IPv6Enabled = false,
            UnconnectedMessagesEnabled = false,
        };
        _net.StartInManualMode(0);
        Content = options.Content ?? GameContent.LoadDefault();
        Local = new LocalPlayer(Content) { Send = SendReliable, SendGun = SendUnordered };
        Local.Gun.ShotFired += OnOwnShot;
    }

    public ClientState State { get; private set; }

    /// <summary>Why the connection ended (the server's words when it gave any).</summary>
    public string? DisconnectReason { get; private set; }

    public GoodbyeReason? Goodbye { get; private set; }

    public string Name => _options.Name;

    /// <summary>The body the player chose (0 male, 1 female).</summary>
    public byte Body => _options.Body;

    public string ServerName { get; private set; } = "";

    public ZoneSpec? Zone { get; private set; }

    public ISurface? Surface => _surface;

    public ZoneFrame Frame => _predictor?.Frame ?? default;

    /// <summary>The ground world prediction runs in (the game also draws its tiles).</summary>
    public PhysicsWorld? Physics => _predictor?.World;

    public uint NetId { get; private set; }

    public EntityId EntityId { get; private set; }

    /// <summary>The view the server restored (to turn the camera to on joining).</summary>
    public float InitialYaw { get; private set; }

    public float InitialPitch { get; private set; }

    public ClientStats Stats { get; } = new();

    /// <summary>Chat lines as they arrive.</summary>
    public event Action<ChatLine>? ChatReceived;

    // ---------------------------------------------------------------- the player's character

    /// <summary>True once the server has placed the character and prediction runs.</summary>
    public bool HasCharacter => _predictor?.HasState == true;

    /// <summary>The newest predicted state.</summary>
    public CharacterState Character => _predictor?.Latest ?? default;

    /// <summary>The newest predicted tick.</summary>
    public uint PredictedTick => _predictor?.LatestTick ?? 0;

    /// <summary>The feet to draw now: between the last two predicted ticks, plus what is left of a smoothed correction.</summary>
    public Vector3 RenderPosition { get; private set; }

    /// <summary>The eye's height above the feet, easing between stances.</summary>
    public float EyeHeight => _eyeHeight;

    public Predictor? Prediction => _predictor;

    // ---------------------------------------------------------------- everyone else

    /// <summary>Entities this client was told about (spawned), interpolated for drawing.</summary>
    public IEnumerable<RemoteEntity> Entities => _remotes.Values.Where(r => r.Spawned && r.HasState);

    public int EntityCount => _remotes.Values.Count(r => r.Spawned);

    /// <summary>The server tick remote entities are drawn at.</summary>
    public double RenderTick => _remoteClock - Protocol.InterpolationDelaySeconds * TickRate;

    // ---------------------------------------------------------------- connecting

    /// <summary>Connects to a server (through a bad-network relay if the options ask for one).</summary>
    public void Connect(string host, int port)
    {
        IPAddress address = IPAddress.TryParse(host, out IPAddress? parsed)
            ? parsed
            : Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        var server = new IPEndPoint(address, port);
        IPEndPoint target = server;
        if (_options.SharedRelay != null)
        {
            target = _options.SharedRelay.LocalEndPoint;
        }
        else if (!_options.Conditions.IsNone)
        {
            _ownRelay = new ConditionedRelay(server, _options.Conditions, (ulong)Environment.ProcessId);
            target = _ownRelay.LocalEndPoint;
        }
        _writer.Reset();
        new Hello { ProtocolVersion = Protocol.Version, SchemaHash = Protocol.SchemaHash, PlayerName = _options.Name, ContentHash = Content.Hash, Body = _options.Body }.WriteMessage(_writer);
        _peer = _net.Connect(target, _writer.Data);
        _connectStarted = _clock.Elapsed.TotalMilliseconds;
        State = ClientState.Connecting;
        _options.Log($"connecting to {server}" + (target != server ? $" through a relay ({_ownRelay?.Conditions ?? _options.SharedRelay?.Conditions})" : ""));
    }

    public void Disconnect()
    {
        _peer?.Disconnect();
        _net.ManualUpdate(0);
    }

    public void SendChat(string text)
    {
        if (_peer == null || State != ClientState.Playing || string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        _writer.Reset();
        new ChatSend { Text = text.Trim() }.WriteMessage(_writer);
        _peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
    }

    // ---------------------------------------------------------------- every frame

    /// <summary>Receives, runs the ticks that are due (each sending its input), and advances what is drawn.</summary>
    public void Update(double delta, in InputFrame input)
    {
        _jumpLatched |= input.JumpPressed;
        _primaryLatched |= input.PrimaryPressed;
        _net.PollEvents();
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
                SampleShips(renderTick);
                PruneStale();
            }
            UpdateSurvival();
        }
        if (_peer != null && _roundTrip < 0)
        {
            Stats.RoundTripMs = _peer.RoundTripTime;
        }
        NetStatistics traffic = _net.Statistics;
        Stats.Update(delta, traffic.BytesReceived, traffic.BytesSent, traffic.PacketsReceived, traffic.PacketsSent);
        _net.ManualUpdate((float)(delta * 1000));
    }

    private void RunTick(uint tick, in InputFrame frame)
    {
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
        InputCommand command = NetConvert.ToCommand(new MoveInput
        {
            Forward = frame.Forward,
            Right = frame.Right,
            Yaw = frame.Yaw,
            Pitch = frame.Pitch,
            Buttons = buttons,
        }, actions);
        if (Piloting)
        {
            // G6: the pilot's controls ride in the input; the ship is predicted first, as the server steps it, so the
            // seated character goes where its seat is.
            command.Ship = new[] { ShipConvert.ToControls(frame.Ship) };
        }
        _predictor!.RecordInput(tick, command);
        if (_predictor.HasState)
        {
            PredictShip(tick);
            _predictor.Predict(tick);
        }
        _predictor.EndTick();
        if (_predictor.HasState)
        {
            // G4: the gun in hand fires (or reloads) on this tick's predicted state, and sends its intent for this tick.
            Local.Gun.Tick(tick, command, _predictor.Latest, _predictor.Frame, _predictor.Tuning, EntityId, !Local.IsDead && !Local.IsWounded && !Piloting);
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

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _roundTrip = -1;
    private double _connectStarted;
    private long _inputsSent;

    private uint _firstInputTick;

    private void UpdateRender(double delta)
    {
        UpdateShipRender(delta);
        if (_predictor == null || !_predictor.HasState)
        {
            return;
        }
        uint latest = _predictor.LatestTick;
        CharacterState current = _predictor.Latest;
        Vector3 position = current.Position;
        if (_predictor.TryGetState(latest - 1, out CharacterState previous))
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
        RenderPosition = position + _smoothing;
        MovementTuning tuning = _predictor.Tuning;
        float eye = tuning.EyeHeight(current.Stance);
        _eyeHeight += (eye - _eyeHeight) * (float)(1 - Math.Exp(-delta * tuning.EyeHeightRate));
    }

    // ---------------------------------------------------------------- messages

    public void OnPeerConnected(NetPeer peer)
    {
        // The handshake took about a round trip: the first estimate, until snapshots echo inputs.
        _roundTrip = _clock.Elapsed.TotalMilliseconds - _connectStarted;
        State = ClientState.Joining;
        _options.Log("connected; joining");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        State = ClientState.Disconnected;
        DisconnectReason = info.Reason.ToString();
        if (info.AdditionalData.AvailableBytes > 0)
        {
            byte[] data = info.AdditionalData.GetRemainingBytes();
            _reader.Reset(data, 0, data.Length);
            if (_reader.ReadBits(8) == Net.Protocol.Goodbye.TypeId)
            {
                Goodbye bye = Net.Protocol.Goodbye.Read(_reader);
                if (!_reader.Failed)
                {
                    Goodbye = bye.Reason;
                    DisconnectReason = $"{bye.Reason}: {bye.Text}";
                }
            }
        }
        _options.Log($"disconnected ({DisconnectReason})");
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
    {
        // Read in place: the transport recycles the packet after this returns.
        _reader.Reset(packet.RawData, packet.Position, packet.AvailableBytes);
        uint type = _reader.ReadBits(8);
        switch (type)
        {
            case Protocol.SnapshotTypeId:
                OnSnapshot();
                break;
            case Welcome.TypeId:
                Welcome welcome = Welcome.Read(_reader);
                if (!_reader.Failed)
                {
                    OnWelcome(welcome);
                }
                break;
            case Spawn.TypeId:
                Spawn spawn = Spawn.Read(_reader);
                if (!_reader.Failed && spawn.Kind == EntityKind.Ship)
                {
                    SpawnShip(spawn.NetId, spawn.EntityId, spawn.Name ?? "");
                }
                else if (!_reader.Failed)
                {
                    RemoteEntity remote = Remote(spawn.NetId);
                    remote.Spawned = true;
                    remote.Id = new EntityId(spawn.EntityId);
                    remote.Kind = spawn.Kind;
                    remote.Name = spawn.Name;
                }
                break;
            case Despawn.TypeId:
                Despawn despawn = Despawn.Read(_reader);
                if (!_reader.Failed)
                {
                    _remotes.Remove(despawn.NetId);
                    RemoveThing(despawn.NetId);
                    DespawnShip(despawn.NetId);
                }
                break;
            case ChatLine.TypeId:
                ChatLine line = ChatLine.Read(_reader);
                if (!_reader.Failed)
                {
                    ChatReceived?.Invoke(line);
                }
                break;
            default:
                ReceiveSurvival(type);
                break;
        }
    }

    private void OnWelcome(Welcome welcome)
    {
        Zone = NetConvert.FromInfo(welcome.Zone);
        _surface = Zone.CreateSurface();
        DisposeShips();
        DisposeBuilding();
        _predictor?.Dispose();
        _predictor = new Predictor(Zone, _surface, _options.SharedPhysics?.Invoke(Zone, _surface));
        StartSurvival();
        StartShips();
        NetId = welcome.NetId;
        EntityId = new EntityId(welcome.EntityId);
        ServerName = welcome.ServerName;
        InitialYaw = Quantize.FromAngle16(welcome.Yaw);
        InitialPitch = Quantize.FromAngle16(welcome.Pitch);
        // Start ahead of the Welcome's tick by a round trip (the Welcome came one way, inputs go the other) and the
        // margin; the server's reports refine it.
        double trip = (_roundTrip >= 0 ? _roundTrip : _peer?.RoundTripTime ?? 100) / 1000.0 * TickRate;
        _nextTick = welcome.ServerTick + 1 + (uint)Math.Ceiling(trip) + TargetInputMargin;
        _firstInputTick = _nextTick;
        _tickClock = _nextTick;
        State = ClientState.Playing;
        _options.Log($"joined {ServerName} as {EntityId} on {Zone}");
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
        ReceiveShips(header.ServerTick);
        SteerClock(header.InputMargin);

        if (header.OwnTick == 0 || _predictor == null)
        {
            return;
        }
        ReconcileShip(header);
        CharacterState server = NetConvert.FromOwn(header.Own);
        if (!_predictor.HasState)
        {
            _predictor.Reset(header.OwnTick, server, _nextTick - 1);
            RenderPosition = _predictor.Latest.Position;
            return;
        }
        Vector3 before = _predictor.Latest.Position;
        float? error = _predictor.Reconcile(header.OwnTick, server);
        Stats.Prediction(error);
        Vector3 jump = before - _predictor.Latest.Position;
        // Small corrections are eased out over a few frames; big ones (a teleport, a respawn) happen at once.
        _smoothing = (_smoothing + jump).LengthSquared() < 4f ? _smoothing + jump : Vector3.Zero;
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

    private int _pruneCounter;

    private RemoteEntity Remote(uint netId)
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _peer?.Disconnect();
        _net.ManualUpdate(0);
        _net.Stop();
        DisposeShips();
        DisposeBuilding();
        _predictor?.Dispose();
        _ownRelay?.Dispose();
    }
}
