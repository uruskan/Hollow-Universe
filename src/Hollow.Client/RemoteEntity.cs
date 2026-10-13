using System;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Client;

/// <summary>
/// Another entity as this client sees it (GROUND.md §4.3): the states snapshots brought, each at its server tick, and
/// the state drawn now, interpolated <see cref="Protocol.InterpolationDelaySeconds"/> in the past so there is a
/// snapshot on each side to blend. If snapshots stop, it carries on briefly along its last motion, then holds.
/// </summary>
public sealed class RemoteEntity
{
    private const int Samples = 32;

    /// <summary>At most this many ticks past the newest snapshot are extrapolated.</summary>
    private const double MaxExtrapolationTicks = 4;

    private readonly uint[] _ticks = new uint[Samples];
    private readonly CharacterNetState[] _states = new CharacterNetState[Samples];
    private int _count;
    private int _newest = -1;

    public RemoteEntity(uint netId)
    {
        NetId = netId;
    }

    public uint NetId { get; }

    /// <summary>Known once its Spawn arrived (it may appear in snapshots a moment before).</summary>
    public bool Spawned { get; set; }

    public EntityId Id { get; set; }

    public EntityKind Kind { get; set; }

    public string Name { get; set; } = "";

    /// <summary>The server tick of the newest state received.</summary>
    public uint NewestTick => _newest < 0 ? 0 : _ticks[_newest];

    // ---------------------------------------------------------------- what is drawn now

    public Vector3 Position { get; private set; }

    public float Yaw { get; private set; }

    public float Pitch { get; private set; }

    public MoveFlags Flags { get; private set; }

    public Stance Stance { get; private set; }

    public byte Health { get; private set; }

    /// <summary>The item in its hand (0: none), a count of its swings and shots and the last kind (animate on a change).</summary>
    public ushort Held { get; private set; }

    public byte ActionCount { get; private set; }

    public Simulation.Players.ActionKind Action { get; private set; }

    /// <summary>Logged off and asleep in the world (lying down, lootable).</summary>
    public bool Sleeping { get; private set; }

    /// <summary>Down (wounded, G4): crawling; the interact key helps them up.</summary>
    public bool Wounded => Stance == Stance.Crawling && (Flags & MoveFlags.Dead) == 0;

    /// <summary>Aiming a gun down its sights, and the held gun's attachments (bit i: weapons.json's attachment i), G4.</summary>
    public bool Aiming { get; private set; }

    public byte HeldMods { get; private set; }

    /// <summary>What it wears (item IDs), from the server's look messages.</summary>
    public ushort[] Wear { get; set; } = System.Array.Empty<ushort>();

    /// <summary>True once there is anything to draw.</summary>
    public bool HasState => _count > 0;

    /// <summary>A state from a snapshot at a server tick (older or repeated ticks are ignored).</summary>
    public void Add(uint tick, in CharacterNetState state)
    {
        if (_count > 0 && tick <= _ticks[_newest])
        {
            return;
        }
        _newest = (_newest + 1) % Samples;
        _ticks[_newest] = tick;
        _states[_newest] = state;
        _count = Math.Min(Samples, _count + 1);
    }

    /// <summary>Sets what is drawn for a (fractional) server tick.</summary>
    public void Sample(double renderTick)
    {
        if (_count == 0)
        {
            return;
        }
        // Newest state at or before the render tick, and the one after it.
        int before = -1, after = -1;
        for (int i = 0; i < _count; i++)
        {
            int index = (_newest - i + Samples) % Samples;
            if (_ticks[index] <= renderTick)
            {
                before = index;
                break;
            }
            after = index;
        }
        if (before >= 0 && after >= 0)
        {
            double t = (renderTick - _ticks[before]) / (_ticks[after] - _ticks[before]);
            Blend(_states[before], _states[after], (float)t, t < 0.5 ? _states[before] : _states[after]);
        }
        else if (before >= 0)
        {
            // Past the newest: extrapolate a little along the last motion, then hold.
            int previous = _count > 1 ? (before - 1 + Samples) % Samples : -1;
            if (previous >= 0 && _ticks[before] > _ticks[previous])
            {
                double ahead = Math.Min(renderTick - _ticks[before], MaxExtrapolationTicks);
                double t = 1 + ahead / (_ticks[before] - _ticks[previous]);
                Blend(_states[previous], _states[before], (float)t, _states[before]);
            }
            else
            {
                Blend(_states[before], _states[before], 0, _states[before]);
            }
        }
        else
        {
            // Older than anything kept: the oldest.
            int oldest = (_newest - _count + 1 + Samples) % Samples;
            Blend(_states[oldest], _states[oldest], 0, _states[oldest]);
        }
    }

    private void Blend(in CharacterNetState a, in CharacterNetState b, float t, in CharacterNetState discrete)
    {
        Position = Vector3.Lerp(NetConvert.PositionOf(a), NetConvert.PositionOf(b), t);
        Yaw = Quantize.FromAngle16(a.Yaw) + t * Quantize.FromAngle16((ushort)(b.Yaw - a.Yaw));
        Pitch = Quantize.FromAngle16(a.Pitch) + t * Quantize.FromAngle16((ushort)(b.Pitch - a.Pitch));
        Flags = discrete.Flags;
        Stance = discrete.Stance;
        Health = discrete.Health;
        Held = discrete.Held;
        ActionCount = discrete.ActionCount;
        Action = discrete.Action;
        Sleeping = discrete.Sleeping;
        Aiming = discrete.Aiming;
        HeldMods = discrete.HeldMods;
    }
}
