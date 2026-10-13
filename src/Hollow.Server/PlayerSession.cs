using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Persistence;
using Hollow.Simulation.Entities;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>One connected player: their connection, character, inputs and what they have been sent.</summary>
public sealed class PlayerSession
{
    /// <summary>Inputs are kept by tick in a ring this long (4 s).</summary>
    public const int InputRing = 128;

    private readonly InputCommand[] _inputs = new InputCommand[InputRing];
    private readonly uint[] _inputTicks = new uint[InputRing];
    private readonly Queue<(long Tick, int Margin)> _margins = new();

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

    /// <summary>The ships (net IDs) this player hears about (G6).</summary>
    public HashSet<uint> ShipsRelevant { get; } = new();

    // ---------------------------------------------------------------- statistics

    public long InputsApplied { get; private set; }

    public long InputsMissing { get; private set; }

    public long InputsLate { get; set; }

    public long BadPackets { get; set; }

    /// <summary>Chat budget (RustMVP's command budget, sized for chat): tokens refill at one a second, up to five.</summary>
    public double ChatTokens { get; set; } = 5;

    /// <summary>
    /// The F1 console's budget (GROUND.md §8.2): ten commands at once, five a second; a line over it is refused with a
    /// reply, not run.
    /// </summary>
    public bool SpendConsoleToken(long tick)
    {
        _consoleTokens = Math.Min(10, _consoleTokens + (tick - _consoleTick) * 5.0 / Simulation.Movement.CharacterMotor.TickRate);
        _consoleTick = tick;
        if (_consoleTokens < 1)
        {
            return false;
        }
        _consoleTokens -= 1;
        return true;
    }

    private double _consoleTokens = 10;
    private long _consoleTick;

    /// <summary>Console lines refused for coming too fast.</summary>
    public long ConsoleRateLimited { get; set; }

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

    /// <summary>
    /// The gun budget (RustMVP's weapon budget, survival.json <c>gunBurst</c>, <c>gunActionsPerSecond</c>): shots and
    /// reloads spend from their own bucket, so a firefight never starves the inventory's intents.
    /// </summary>
    public bool SpendGunToken(long tick, int burst, float perSecond)
    {
        if (!_gunBudgetStarted)
        {
            _gunBudgetStarted = true;
            _gunTokens = burst;
            _gunTick = tick;
        }
        _gunTokens = Math.Min(burst, _gunTokens + (tick - _gunTick) * (double)perSecond / Simulation.Movement.CharacterMotor.TickRate);
        _gunTick = tick;
        if (_gunTokens < 1)
        {
            return false;
        }
        _gunTokens -= 1;
        return true;
    }

    private bool _gunBudgetStarted;
    private double _gunTokens;
    private long _gunTick;

    /// <summary>Gun actions (shots and reloads) that arrived, those refused for the budget, and the gun state last sent.</summary>
    public long GunActions { get; set; }

    public long GunRateLimited { get; set; }

    public uint SentGun { get; set; } = uint.MaxValue;

    /// <summary>Whether the player was down (wounded) when their life was last sent.</summary>
    public bool? SentWounded { get; set; }

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

    /// <summary>What of the player's own state was sent: the revisions, and whether their containers went in full.</summary>
    public bool OwnContainersSent { get; set; }

    public uint SentVitals { get; set; } = uint.MaxValue;

    public uint SentCraft { get; set; } = uint.MaxValue;

    public uint SentLoot { get; set; } = uint.MaxValue;

    public bool? SentDead { get; set; }

    /// <summary>Static things this player was told about, by net ID, with the revision sent.</summary>
    public Dictionary<uint, uint> ThingsSent { get; } = new();

    /// <summary>What each relevant character was last told to wear (a hash of the items).</summary>
    public Dictionary<uint, ulong> LooksSent { get; } = new();

    /// <summary>The 64 m cells whose natural overrides this player has.</summary>
    public HashSet<(int X, int Z)> NatureCells { get; } = new();

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

    /// <summary>The input for a tick: the one received for it, or the last one again (without its jump press).</summary>
    public InputCommand InputFor(uint tick)
    {
        LastSimulatedTick = tick;
        if (_inputTicks[tick % InputRing] == tick)
        {
            LastInput = _inputs[tick % InputRing];
            InputsApplied++;
        }
        else
        {
            InputCommand repeated = LastInput;
            repeated.Buttons &= ~Simulation.Movement.MoveButtons.JumpPressed;
            repeated.Actions &= ~Simulation.Players.ActionButtons.PrimaryPressed;
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
