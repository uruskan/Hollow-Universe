using System;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Ships;

namespace Hollow.Client;

/// <summary>
/// A ship as this client sees it (GROUND.md §6.7), as <see cref="RemoteEntity"/> sees a character: the states snapshots
/// brought, each at its server tick, and the state drawn now, interpolated <see cref="Protocol.InterpolationDelaySeconds"/>
/// in the past (its turn by slerp). If snapshots stop it carries on briefly along its last motion, then holds.
/// </summary>
public sealed class RemoteShip
{
    private const int Samples = 32;
    private const double MaxExtrapolationTicks = 6;

    private readonly uint[] _ticks = new uint[Samples];
    private readonly ShipNetState[] _states = new ShipNetState[Samples];
    private int _count;
    private int _newest = -1;

    public RemoteShip(uint netId)
    {
        NetId = netId;
    }

    public uint NetId { get; }

    /// <summary>Known once its Spawn arrived.</summary>
    public bool Spawned { get; set; }

    public EntityId Id { get; set; }

    /// <summary>Its kind (null until a state names one this client's data knows).</summary>
    public ShipDef? Def { get; set; }

    public uint NewestTick => _newest < 0 ? 0 : _ticks[_newest];

    public bool HasState => _count > 0;

    /// <summary>The newest state received (the parked body and the flags follow it, not the interpolation).</summary>
    public ShipNetState Newest => _newest < 0 ? default : _states[_newest];

    // ---------------------------------------------------------------- what is drawn now

    /// <summary>The centre of mass and the turn, in the zone frame.</summary>
    public Vector3 Position { get; private set; }

    public Quaternion Rotation { get; private set; } = Quaternion.Identity;

    /// <summary>Metres per second, from the samples either side (for sound and the trail).</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>Each axis's thrust as a fraction (x + left, y + up, z + forward; boost above 1).</summary>
    public Vector3 Thrust { get; private set; }

    public ShipNetFlags Flags { get; private set; }

    /// <summary>The hatch's swing, 0 shut … 1 open.</summary>
    public float Hatch { get; private set; }

    public float Health { get; private set; } = 1;

    public bool Has(ShipNetFlags flag) => (Flags & flag) != 0;

    /// <summary>The model's pivot (on the ground between the skids when it stands).</summary>
    public Vector3 Origin => Def == null ? Position : Position - Vector3.Transform(Def.CentreOfMass, Rotation);

    /// <summary>A point of the ship's frame (relative to its pivot) in the zone frame.</summary>
    public Vector3 PointOf(Vector3 local) => Def == null ? Position : Position + Vector3.Transform(local - Def.CentreOfMass, Rotation);

    /// <summary>A state from a snapshot at a server tick (older or repeated ticks are ignored).</summary>
    public void Add(uint tick, in ShipNetState state)
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
            Blend(before, after, (float)t);
        }
        else if (before >= 0)
        {
            int previous = _count > 1 ? (before - 1 + Samples) % Samples : -1;
            if (previous >= 0 && _ticks[before] > _ticks[previous] && !Has(ShipNetFlags.Asleep))
            {
                double ahead = Math.Min(renderTick - _ticks[before], MaxExtrapolationTicks);
                double t = 1 + ahead / (_ticks[before] - _ticks[previous]);
                Blend(previous, before, (float)t);
            }
            else
            {
                Blend(before, before, 0);
            }
        }
        else
        {
            int oldest = (_newest - _count + 1 + Samples) % Samples;
            Blend(oldest, oldest, 0);
        }
    }

    private void Blend(int a, int b, float t)
    {
        ref readonly ShipNetState sa = ref _states[a];
        ref readonly ShipNetState sb = ref _states[b];
        Vector3 pa = ShipConvert.PositionOf(sa), pb = ShipConvert.PositionOf(sb);
        Position = Vector3.Lerp(pa, pb, t);
        Quaternion qa = ShipConvert.RotationOf(sa), qb = ShipConvert.RotationOf(sb);
        Rotation = t <= 1 ? Quaternion.Slerp(qa, qb, Math.Clamp(t, 0, 1)) : Extrapolate(qa, qb, t);
        float span = (_ticks[b] - _ticks[a]) / (float)Simulation.Movement.CharacterMotor.TickRate;
        Velocity = span > 0 ? (pb - pa) / span : Vector3.Zero;
        Thrust = Vector3.Lerp(ShipConvert.ThrustOf(sa), ShipConvert.ThrustOf(sb), Math.Clamp(t, 0, 1));
        ShipNetState discrete = t < 0.5f ? sa : sb;
        Flags = discrete.Flags;
        Hatch = (sa.Hatch + (sb.Hatch - sa.Hatch) * Math.Clamp(t, 0, 1)) / Ship.HatchTicks;
        Health = discrete.Health / 255f;
    }

    // Past the newest: keep turning the way it turned last, a little.
    private static Quaternion Extrapolate(Quaternion a, Quaternion b, float t)
    {
        Quaternion delta = b * Quaternion.Conjugate(a);
        if (delta.W < 0)
        {
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        }
        float extra = Math.Clamp(t - 1, 0, 1);
        return Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, delta, extra) * b);
    }
}
