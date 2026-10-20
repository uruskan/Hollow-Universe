using System;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

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
    public ShipDef? Def
    {
        get => _def;
        set
        {
            if (value != _def)
            {
                _def = value;
                ApplyDetails();
            }
        }
    }

    private ShipDef? _def;

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

    /// <summary>The drive's engine speed (a rotor's spin), 0 … 1, interpolated like the thrust.</summary>
    public float Spool { get; private set; }

    public ShipNetFlags Flags { get; private set; }

    /// <summary>What the server tells this player of it (GROUND_SYSTEMS.md §3.3): theirs, a teammate's, locked.</summary>
    public ViewerFlags Viewer { get; private set; }

    /// <summary>This player's own (they built it).</summary>
    public bool Mine => (Viewer & ViewerFlags.Mine) != 0;

    public bool Locked => (Viewer & ViewerFlags.Locked) != 0;

    public float Health { get; private set; } = 1;

    public bool Has(ShipNetFlags flag) => (Flags & flag) != 0;

    /// <summary>The model's pivot (on the ground between the skids when it stands).</summary>
    public Vector3 Origin => Def == null ? Position : Position - Vector3.Transform(Def.CentreOfMass, Rotation);

    /// <summary>A point of the ship's frame (relative to its pivot) in the zone frame.</summary>
    public Vector3 PointOf(Vector3 local) => Def == null ? Position : Position + Vector3.Transform(local - Def.CentreOfMass, Rotation);

    // ---------------------------------------------------------------- its details (IRON_WAKE.md §4.7, §5)

    private ShipDetails _details;
    private bool _hasDetails;

    /// <summary>Raised whenever the server tells this client its details (its moving parts, doors, seats, health).</summary>
    public int DetailsVersion { get; private set; }

    /// <summary>Raised when the server moves a part no door drives (a turret): a parked body is made again for it.</summary>
    public int PartsVersion { get; private set; }

    /// <summary>Each door's motion (<see cref="ShipDef.Doors"/>' order), as the server last said; its progress at any tick follows.</summary>
    public ShipDoorState[] Doors { get; private set; } = Array.Empty<ShipDoorState>();

    /// <summary>Who sits in each seat (<see cref="ShipDef.Seats"/>' order).</summary>
    public EntityId[] Occupants { get; private set; } = Array.Empty<EntityId>();

    /// <summary>Each target part's health as a share of its most (1 whole; a door's part, its door's).</summary>
    public float[] PartHealth { get; private set; } = Array.Empty<float>();

    /// <summary>The server's word on its details (<see cref="ShipDetails"/>).</summary>
    public void SetDetails(in ShipDetails details)
    {
        bool partsMoved = !_hasDetails || !SameParts(_details.Parts, details.Parts, _def?.Interior);
        _details = details;
        _hasDetails = true;
        DetailsVersion++;
        if (partsMoved)
        {
            PartsVersion++;
        }
        ApplyDetails();
    }

    // Whether two lists of parts away from rest are the same but for the stations' mounts (a gunner's aim moves them every
    // tick; the parked body keeps them at rest, IRON_WAKE.md §6.2).
    private static bool SameParts(ShipPartNet[]? a, ShipPartNet[]? b, ShipInterior? interior)
    {
        a ??= Array.Empty<ShipPartNet>();
        b ??= Array.Empty<ShipPartNet>();
        int i = 0, j = 0;
        while (true)
        {
            while (i < a.Length && interior != null && interior.IsMount((int)a[i].Part))
            {
                i++;
            }
            while (j < b.Length && interior != null && interior.IsMount((int)b[j].Part))
            {
                j++;
            }
            if (i >= a.Length || j >= b.Length)
            {
                return i >= a.Length && j >= b.Length;
            }
            if (a[i].Part != b[j].Part || a[i].Bits != b[j].Bits)
            {
                return false;
            }
            i++;
            j++;
        }
    }

    // The details for its kind (once both are known): doors, seats and health sized to its kind's; before any word from
    // the server, its doors as a new ship's (the interior's rest), its seats empty, its parts whole.
    private void ApplyDetails()
    {
        if (_def == null)
        {
            return;
        }
        ShipDef def = _def;
        var doors = new ShipDoorState[def.Doors.Count];
        for (int i = 0; i < doors.Length; i++)
        {
            float max = def.Doors[i].Health?.Max ?? 0;
            if (_hasDetails && _details.Doors != null && i < _details.Doors.Length)
            {
                doors[i] = ShipConvert.DoorOf(_details.Doors[i], max);
            }
            else
            {
                int first = def.Doors[i].Parts.Count > 0 ? def.Doors[i].Parts[0] : -1;
                float rest = def.Interior != null && first >= 0 && first < def.Interior.Rest.Length ? def.Interior.Rest[first] : 0;
                doors[i] = new ShipDoorState { From = Math.Clamp(rest, 0f, 1f), Health = max };
            }
        }
        var occupants = new EntityId[def.Seats.Count];
        foreach (ShipSeatNet seat in _hasDetails ? _details.Seats ?? Array.Empty<ShipSeatNet>() : Array.Empty<ShipSeatNet>())
        {
            if (seat.Seat < occupants.Length)
            {
                occupants[seat.Seat] = new EntityId(seat.Occupant);
            }
        }
        var health = new float[def.Parts.Count];
        Array.Fill(health, 1f);
        foreach (ShipPartHealthNet part in _hasDetails ? _details.Health ?? Array.Empty<ShipPartHealthNet>() : Array.Empty<ShipPartHealthNet>())
        {
            if (part.Part < health.Length)
            {
                health[part.Part] = part.Health / 255f;
            }
        }
        for (int i = 0; i < health.Length; i++)
        {
            if (def.Parts[i].Door >= 0 && def.Parts[i].Door < doors.Length && def.Doors[def.Parts[i].Door].Health is { Max: > 0 } h)
            {
                health[i] = doors[def.Parts[i].Door].Health / h.Max;
            }
        }
        Doors = doors;
        Occupants = occupants;
        PartHealth = health;
    }

    /// <summary>A door's progress at a (fractional) server tick: 0 shut … 1 open.</summary>
    public float DoorAt(int door, double tick) =>
        _def != null && door >= 0 && door < Doors.Length ? Doors[door].ProgressAt(tick, _def.Doors[door].Rate) : 0;

    /// <summary>The seat a character sits in here, or −1.</summary>
    public int SeatOf(EntityId character)
    {
        for (int i = 0; i < Occupants.Length; i++)
        {
            if (!character.IsNone && Occupants[i] == character)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Its parts' states at a server tick into an array the size of its interior's parts: the interior's rest, the parts the
    /// server listed, then each door's parts at its progress then (the server's arithmetic, ShipSystem.DoorParts).
    /// </summary>
    public void PartsInto(ShipInterior interior, float[] into, long tick)
    {
        interior.Rest.AsSpan(0, Math.Min(interior.Rest.Length, into.Length)).CopyTo(into);
        if (_hasDetails)
        {
            foreach (ShipPartNet part in _details.Parts ?? Array.Empty<ShipPartNet>())
            {
                if (part.Part < into.Length)
                {
                    into[part.Part] = BitConverter.UInt32BitsToSingle(part.Bits);
                }
            }
        }
        if (_def != null)
        {
            Simulation.ShipSystem.DoorParts(_def, Doors, tick, into);
        }
    }

    // ---------------------------------------------------------------- where it was at a tick

    /// <summary>At most this many ticks past the newest state are extrapolated for a pose (then it holds).</summary>
    private const double MaxPoseExtrapolationTicks = 30;

    /// <summary>
    /// Where it was at a (fractional) server tick, for a client's frame of a ship it doesn't fly (IRON_WAKE.md §4.8): while
    /// the newest state says it is asleep, exactly that state (the server's parked pose, on the wire's grid, still); else
    /// between the states either side, or past the newest along its last motion for up to a second, with the velocity
    /// and turn rate between the two states used. False before any state or kind.
    /// </summary>
    public bool TryPoseAt(double tick, out ShipPose pose)
    {
        pose = default;
        if (_count == 0 || Def == null)
        {
            return false;
        }
        ref readonly ShipNetState newest = ref _states[_newest];
        if ((newest.Flags & ShipNetFlags.Asleep) != 0 || _count == 1)
        {
            pose = new ShipPose(ShipConvert.PositionOf(newest), ShipConvert.RotationOf(newest), Vector3.Zero, Vector3.Zero, Def.CentreOfMass);
            return true;
        }
        int before = -1, after = -1;
        for (int i = 0; i < _count; i++)
        {
            int index = (_newest - i + Samples) % Samples;
            if (_ticks[index] <= tick)
            {
                before = index;
                break;
            }
            after = index;
        }
        int a, b;
        double t;
        if (before >= 0 && after >= 0)
        {
            (a, b) = (before, after);
            t = (tick - _ticks[a]) / (_ticks[b] - _ticks[a]);
        }
        else if (before >= 0)
        {
            (a, b) = ((before - 1 + Samples) % Samples, before);
            t = 1 + Math.Min(tick - _ticks[b], MaxPoseExtrapolationTicks) / Math.Max(1u, _ticks[b] - _ticks[a]);
        }
        else
        {
            int oldest = (_newest - _count + 1 + Samples) % Samples;
            (a, b) = (oldest, (oldest + 1) % Samples);
            t = 0;
        }
        Vector3 pa = ShipConvert.PositionOf(_states[a]), pb = ShipConvert.PositionOf(_states[b]);
        Quaternion qa = ShipConvert.RotationOf(_states[a]), qb = ShipConvert.RotationOf(_states[b]);
        float span = Math.Max(1u, _ticks[b] - _ticks[a]) / (float)CharacterMotor.TickRate;
        Quaternion turn = qb * Quaternion.Conjugate(qa);
        if (turn.W < 0)
        {
            turn = new Quaternion(-turn.X, -turn.Y, -turn.Z, -turn.W);
        }
        float sin = new Vector3(turn.X, turn.Y, turn.Z).Length();
        Vector3 spin = sin > 1e-7f ? new Vector3(turn.X, turn.Y, turn.Z) / sin * (2 * MathF.Atan2(sin, turn.W) / span) : Vector3.Zero;
        Quaternion rotation = t <= 1 ? Quaternion.Slerp(qa, qb, (float)Math.Max(t, 0)) : Extrapolate(qa, qb, (float)t);
        pose = new ShipPose(pa + (pb - pa) * (float)t, rotation, (pb - pa) / span, spin, Def.CentreOfMass);
        return true;
    }

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
        Spool = float.Lerp(ShipConvert.SpoolOf(sa), ShipConvert.SpoolOf(sb), Math.Clamp(t, 0, 1));
        ShipNetState discrete = t < 0.5f ? sa : sb;
        Flags = discrete.Flags;
        Viewer = discrete.Viewer;
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
