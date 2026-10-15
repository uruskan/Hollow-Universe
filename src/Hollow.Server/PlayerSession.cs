using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Persistence;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Entities;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// One connected player: their connection, character, inputs, budgets and what the host has sent them. What a module
/// keeps of a player lives in a slot of its own (<see cref="State{T}"/>), not here (GROUND_SYSTEMS.md §12.5).
/// </summary>
public sealed class PlayerSession
{
    /// <summary>Inputs are kept by tick in a ring this long (4 s).</summary>
    public const int InputRing = 128;

    private readonly InputCommand[] _inputs = new InputCommand[InputRing];
    private readonly uint[] _inputTicks = new uint[InputRing];
    private readonly Queue<(long Tick, int Margin)> _margins = new();
    private readonly Dictionary<Type, object> _state = new();

    public PlayerSession(NetPeer peer, string name)
    {
        Peer = peer;
        Name = name;
    }

    public NetPeer Peer { get; }

    public string Name { get; }

    /// <summary>The body the player chose at connect (0 male, 1 female).</summary>
    public byte Body { get; set; }

    /// <summary>The account and stored character, while logging in.</summary>
    public Task<(long AccountId, CharacterRecord? Character)>? Login { get; set; }

    /// <summary>Ticks the login has waited for the ground where the player comes in (GameServer.FinishLogins).</summary>
    public int ArrivalWait { get; set; }

    /// <summary>
    /// A login or a respawn waits at most this long for its ground, built on worker threads, then the tick builds what is
    /// left: one arrival takes a few tens of milliseconds, a crowd arriving all round the world at once a few seconds.
    /// </summary>
    public const int ArrivalWaitTicks = 5 * GameServer.TickRate;

    public long AccountId { get; set; }

    public Entity Entity { get; set; } = Entity.None;

    public EntityId EntityId { get; set; }

    public uint NetId { get; set; }

    public bool Playing => Entity != Entity.None;

    /// <summary>The input applied last (repeated when the next one is missing) and the tick it was for.</summary>
    public InputCommand LastInput { get; private set; }

    public uint LastSimulatedTick { get; set; }

    /// <summary>The newest tick an input arrived for.</summary>
    public uint NewestInputTick { get; private set; }

    public SnapshotSender Snapshots { get; } = new();

    /// <summary>The client time of the newest input packet, and when it arrived (Stopwatch), echoed in snapshots.</summary>
    public ushort EchoTime { get; set; }

    public long EchoReceived { get; set; }

    /// <summary>Entities this player has been told about (Spawn sent, no Despawn since).</summary>
    public HashSet<uint> Relevant { get; } = new();

    /// <summary>
    /// A module's state for this player (GROUND_SYSTEMS.md §12.5, K10): one slot per type, made on first use and gone
    /// with the session, so a module keeps what it sent a player without the session naming it.
    /// </summary>
    public T State<T>() where T : class, new()
    {
        if (!_state.TryGetValue(typeof(T), out object? state))
        {
            state = new T();
            _state.Add(typeof(T), state);
        }
        return (T)state;
    }

    // ---------------------------------------------------------------- statistics

    public long InputsApplied { get; private set; }

    public long InputsMissing { get; private set; }

    public long InputsLate { get; set; }

    public long BadPackets { get; set; }

    /// <summary>Chat budget (RustMVP's command budget, sized for chat): tokens refill at one a second, up to five.</summary>
    public double ChatTokens { get; set; } = 5;

    public long ChatTick { get; set; }

    // ---------------------------------------------------------------- G2: actions and the player's own state

    /// <summary>
    /// The action budget (RustMVP's command budget, survival.json <c>rateLimits</c>): every intent that arrives spends a
    /// token, a bad one too; tokens refill at <paramref name="perSecond"/> up to <paramref name="burst"/>.
    /// </summary>
    public bool SpendIntentToken(long tick, int burst, float perSecond)
    {
        if (!_budgetStarted)
        {
            _budgetStarted = true;
            IntentTokens = burst;
            IntentTick = tick;
        }
        IntentTokens = Math.Min(burst, IntentTokens + (tick - IntentTick) * (double)perSecond / Simulation.Movement.CharacterMotor.TickRate);
        IntentTick = tick;
        if (IntentTokens < 1)
        {
            return false;
        }
        IntentTokens -= 1;
        return true;
    }

    private bool _budgetStarted;

    public double IntentTokens { get; private set; }

    public long IntentTick { get; private set; }

    /// <summary>Intents accepted, refused (any reason), and refused for being over budget.</summary>
    public long IntentsAccepted { get; set; }

    public long IntentsRefused { get; set; }

    public long IntentsRateLimited { get; set; }

    /// <summary>The newest intent applied: owner state carries it, so the client knows what its copy includes.</summary>
    public uint LastIntentSeq { get; set; }

    /// <summary>Answers to send after this tick's changes (so the changes arrive first).</summary>
    public List<IntentResult> PendingResults { get; } = new();

    /// <summary>
    /// The player as a viewer this snapshot (GROUND_SYSTEMS.md §3.3: their ID, team and keys), and whether that changed
    /// since the last (a team joined or left, a key picked up or dropped): then the viewer flags of everything they were
    /// sent are worked out again.
    /// </summary>
    public Viewer Viewer { get; set; }

    public ulong ViewerSignature { get; set; }

    public bool ViewerChanged { get; set; }

    /// <summary>Stores the inputs of a packet that are still useful: not yet simulated, and not absurdly far ahead.</summary>
    public void ReceiveInputs(in InputPacket packet, uint nextTick, long serverTick)
    {
        if (packet.Inputs == null)
        {
            return;
        }
        for (int i = 0; i < packet.Inputs.Length; i++)
        {
            uint tick = packet.NewestTick - (uint)i;
            if (tick < nextTick)
            {
                if (i == 0)
                {
                    InputsLate++;
                }
                break;
            }
            if (tick >= nextTick + InputRing - 8)
            {
                continue;
            }
            _inputs[tick % InputRing] = packet.Inputs[i];
            _inputTicks[tick % InputRing] = tick;
        }
        if (packet.NewestTick > NewestInputTick && packet.NewestTick < nextTick + InputRing)
        {
            NewestInputTick = packet.NewestTick;
        }
        // How early the newest input arrived: the client steers its clock by the least of these over half a second.
        int margin = (int)Math.Clamp((long)packet.NewestTick - nextTick, -100, 100);
        _margins.Enqueue((serverTick, margin));
    }

    /// <summary>Ticks of repeated input after which the head's free look turns back (RustMVP's 0.8 s of silence).</summary>
    private const int LookTimeoutTicks = (int)(Simulation.Movement.FreeLook.TimeoutSeconds * Simulation.Movement.CharacterMotor.TickRate);

    private int _repeated;

    /// <summary>
    /// The input for a tick: the one received for it, or the last one again (without its jump press, and after
    /// <see cref="Simulation.Movement.FreeLook.TimeoutSeconds"/> of that without its free look).
    /// </summary>
    public InputCommand InputFor(uint tick)
    {
        LastSimulatedTick = tick;
        if (_inputTicks[tick % InputRing] == tick)
        {
            LastInput = _inputs[tick % InputRing];
            InputsApplied++;
            _repeated = 0;
        }
        else
        {
            InputCommand repeated = LastInput;
            repeated.Buttons &= ~Simulation.Movement.MoveButtons.JumpPressed;
            repeated.Actions &= ~Simulation.Players.ActionButtons.PrimaryPressed;
            if (++_repeated > LookTimeoutTicks)
            {
                repeated.LookYaw = repeated.LookPitch = 0;
            }
            LastInput = repeated;
            InputsMissing++;
        }
        return LastInput;
    }

    /// <summary>Starts input history afresh at a tick (a new character has no inputs before it).</summary>
    public void ResetInputs(uint tick)
    {
        Array.Clear(_inputTicks);
        LastSimulatedTick = tick;
        NewestInputTick = tick;
    }

    /// <summary>The least arrival margin over the last half second (what the snapshot reports); −128 with none yet.</summary>
    public sbyte InputMargin(long serverTick)
    {
        while (_margins.Count > 0 && _margins.Peek().Tick < serverTick - Simulation.Movement.CharacterMotor.TickRate / 2)
        {
            _margins.Dequeue();
        }
        int least = int.MaxValue;
        foreach ((long _, int margin) in _margins)
        {
            least = Math.Min(least, margin);
        }
        return least == int.MaxValue ? sbyte.MinValue : (sbyte)Math.Clamp(least, -127, 127);
    }
}
