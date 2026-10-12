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

    /// <summary>The account and stored character, while logging in.</summary>
    public Task<(long AccountId, CharacterRecord? Character)>? Login { get; set; }

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

    // ---------------------------------------------------------------- statistics

    public long InputsApplied { get; private set; }

    public long InputsMissing { get; private set; }

    public long InputsLate { get; set; }

    public long BadPackets { get; set; }

    /// <summary>Chat budget (RustMVP's command budget, sized for chat): tokens refill at one a second, up to five.</summary>
    public double ChatTokens { get; set; } = 5;

    public long ChatTick { get; set; }

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
