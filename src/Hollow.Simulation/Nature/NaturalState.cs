using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Nature;

/// <summary>A player change to a natural object (ARCHITECTURE.md P1: only changes are stored).</summary>
public struct NaturalOverride
{
    /// <summary>Health left (trees and outcrops).</summary>
    public float Health;

    /// <summary>
    /// The weak spot (Rust's): none until the first hit, then on the side facing whoever hit last. Which way it faces
    /// about the object's up, in 1/65536 of a turn, and how high on the object (0 … 255 of its range).
    /// </summary>
    public bool HasWeakSpot;
    public ushort WeakSpotYaw;
    public byte WeakSpotLift;

    /// <summary>Felled, broken or picked: gone (a bush: bare) until <see cref="RespawnAtUnixMs"/>.</summary>
    public bool Depleted;

    /// <summary>When it grows back, in Unix milliseconds (real time: nature carries on while the server is down).</summary>
    public long RespawnAtUnixMs;

    /// <summary>The weak spot in one number (0: none), for storage and the wire.</summary>
    public readonly uint PackedWeakSpot => HasWeakSpot ? 1u << 24 | (uint)WeakSpotLift << 16 | WeakSpotYaw : 0;

    public void UnpackWeakSpot(uint packed)
    {
        HasWeakSpot = (packed & 1u << 24) != 0;
        WeakSpotLift = (byte)(packed >> 16);
        WeakSpotYaw = (ushort)packed;
    }
}

/// <summary>What a gather hit or a pick gave.</summary>
public struct GatherResult
{
    public bool Hit;

    /// <summary>The hit was on the weak spot (it counted <see cref="WeakSpot.Bonus"/> times and moved).</summary>
    public bool WeakSpot;

    /// <summary>The object is gone now (felled, broken, picked).</summary>
    public bool Depleted;

    public List<ItemAmount> Yield;
}

/// <summary>
/// The changes to natural objects in a zone (GROUND.md §6): health, weak spots and depletion, with their respawn times.
/// The server owns one and applies the gathering rules to it; a client keeps a mirror from what the server sends, to
/// draw and to collide. <see cref="Changed"/> collects the IDs to send and save.
/// </summary>
public sealed class NaturalState
{
    /// <summary>A moved weak spot faces the hitter within this many degrees either way.</summary>
    public const double WeakSpotSpreadDegrees = 50;

    private readonly Dictionary<ulong, NaturalOverride> _overrides = new();
    private readonly List<ulong> _expired = new();

    public NaturalState(GameContent content, INaturalObjects objects)
    {
        Content = content;
        Objects = objects;
    }

    public GameContent Content { get; }

    public INaturalObjects Objects { get; }

    public int Count => _overrides.Count;

    public IReadOnlyDictionary<ulong, NaturalOverride> Overrides => _overrides;

    /// <summary>IDs whose state changed since the owner last took them (replication and saving).</summary>
    public HashSet<ulong> Changed { get; } = new();

    /// <summary>Raised on every change (views rebuild what they drew when it moves).</summary>
    public uint Revision { get; private set; }

    /// <summary>Raised on the owning thread when an object appears or disappears (the physics follows it).</summary>
    public event Action<ulong>? PresenceChanged;

    public bool TryGetOverride(ulong id, out NaturalOverride value) => _overrides.TryGetValue(id, out value);

    public bool IsDepleted(ulong id) => _overrides.TryGetValue(id, out NaturalOverride o) && o.Depleted;

    public float Health(in NaturalObject o, NaturalKind kind) =>
        _overrides.TryGetValue(o.Id, out NaturalOverride v) ? v.Health : NaturalShapes.MaxHealth(o, kind);

    /// <summary>Outcrops: 0 while above two thirds of their health, then 1, then 2 (for drawing).</summary>
    public int Stage(in NaturalObject o, NaturalKind kind)
    {
        if (kind.Stages <= 1 || !kind.IsHarvested)
        {
            return 0;
        }
        float fraction = Health(o, kind) / NaturalShapes.MaxHealth(o, kind);
        return Math.Clamp((int)((1 - fraction) * kind.Stages), 0, kind.Stages - 1);
    }

    /// <summary>Where an object's weak spot is now, or null before its first hit: on a tree's trunk at chest height, on an outcrop's upper surface.</summary>
    public Vector3? WeakSpot(in NaturalObject o, NaturalKind kind)
    {
        if (!_overrides.TryGetValue(o.Id, out NaturalOverride v) || !v.HasWeakSpot || v.Depleted)
        {
            return null;
        }
        return SpotAt(o, kind, Quantize16(v.WeakSpotYaw), v.WeakSpotLift / 255f);
    }

    private static Vector3 SpotAt(in NaturalObject o, NaturalKind kind, double yaw, float lift)
    {
        (Vector3 forward, _) = Movement.CharacterMotor.HorizontalBasis((float)yaw, o.Up);
        if (kind.Class == NaturalClass.Tree)
        {
            (Vector3 a, _, float radius) = NaturalShapes.Trunk(o, kind);
            return a - o.Up * radius + o.Up * (0.9f + 0.7f * lift) + forward * radius;
        }
        (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
        double elevation = 0.15 + 0.7 * lift;
        float up = (float)DMath.Sin(elevation), outward = (float)DMath.Cos(elevation);
        return centre + (forward * outward + o.Up * up) * r;
    }

    private static double Quantize16(ushort angle) => (short)angle * (Math.PI / 32768);

    private static ushort Angle16(double radians)
    {
        double turns = radians / DMath.TwoPi;
        turns -= Math.Floor(turns);
        return (ushort)((int)Math.Round(turns * 65536, MidpointRounding.ToEven) & 0xFFFF);
    }

    /// <summary>
    /// A tool's hit on a tree or outcrop at <paramref name="point"/>, by someone whose eye is at <paramref name="from"/>:
    /// takes the tool's rate of health (twice that on the weak spot) and gives the share of the object's whole yield that
    /// health was. The weak spot appears with the first hit, and after each hit on it moves, both times to the side
    /// facing the hitter. The last hit gives the remainder and starts the respawn timer.
    /// </summary>
    public GatherResult Hit(in NaturalObject o, MeleeStats tool, Vector3 point, Vector3 from, long nowUnixMs)
    {
        var result = new GatherResult { Yield = new List<ItemAmount>() };
        NaturalKind? kind = Content.Natural(o.Kind);
        if (kind == null || !kind.IsHarvested || IsDepleted(o.Id))
        {
            return result;
        }
        float rate = kind.Class == NaturalClass.Tree ? tool.Tree : tool.Ore;
        if (rate <= 0)
        {
            return result;
        }
        result.Hit = true;
        float max = NaturalShapes.MaxHealth(o, kind);
        NaturalOverride state = _overrides.TryGetValue(o.Id, out NaturalOverride v) ? v : new NaturalOverride { Health = max };
        bool onSpot = WeakSpot(o, kind) is { } spot && Vector3.Distance(point, spot) <= Content.WeakSpot.Radius;
        if (onSpot)
        {
            rate *= Content.WeakSpot.Bonus;
            result.WeakSpot = true;
        }
        if (onSpot || !state.HasWeakSpot)
        {
            // Face the hitter, give or take a little, at a new height.
            Vector3 d = from - o.Position;
            Vector3 flat = d - o.Up * Vector3.Dot(d, o.Up);
            var rng = new Rng(Hash64.Of(o.Id, (ulong)BitConverter.SingleToUInt32Bits(state.Health), 0x5B07));
            double facing = DMath.Atan2(-flat.X, -flat.Z) + (rng.NextDouble() * 2 - 1) * WeakSpotSpreadDegrees * Math.PI / 180;
            state.HasWeakSpot = true;
            state.WeakSpotYaw = Angle16(facing);
            state.WeakSpotLift = (byte)rng.NextInt(256);
        }
        float before = state.Health, after = Math.Max(0, before - rate);
        foreach (ItemAmount y in kind.Yield)
        {
            int total = NaturalShapes.Total(o, kind, y);
            int gained = Gathered(total, max, after) - Gathered(total, max, before);
            if (gained > 0)
            {
                result.Yield.Add(new ItemAmount(y.Item, gained));
            }
        }
        state.Health = after;
        if (after <= 0)
        {
            state.Depleted = true;
            state.HasWeakSpot = false;
            state.RespawnAtUnixMs = nowUnixMs + (long)(kind.RespawnSeconds * 1000);
            result.Depleted = true;
        }
        Set(o.Id, state, presence: result.Depleted);
        return result;
    }

    /// <summary>Picks a pickup or a bush: its yield, and it is gone (or bare) until it grows back.</summary>
    public GatherResult Pick(in NaturalObject o, long nowUnixMs)
    {
        var result = new GatherResult { Yield = new List<ItemAmount>() };
        NaturalKind? kind = Content.Natural(o.Kind);
        if (kind == null || !kind.IsPicked || IsDepleted(o.Id))
        {
            return result;
        }
        result.Hit = true;
        result.Depleted = true;
        foreach (ItemAmount y in kind.Yield)
        {
            result.Yield.Add(new ItemAmount(y.Item, NaturalShapes.Total(o, kind, y)));
        }
        Set(o.Id, new NaturalOverride { Depleted = true, RespawnAtUnixMs = nowUnixMs + (long)(kind.RespawnSeconds * 1000) }, presence: true);
        return result;
    }

    /// <summary>Brings back what is due (call about once a second). Returns how many came back.</summary>
    public int Respawn(long nowUnixMs)
    {
        _expired.Clear();
        foreach ((ulong id, NaturalOverride o) in _overrides)
        {
            if (o.Depleted && nowUnixMs >= o.RespawnAtUnixMs)
            {
                _expired.Add(id);
            }
        }
        foreach (ulong id in _expired)
        {
            _overrides.Remove(id);
            Revision++;
            Changed.Add(id);
            PresenceChanged?.Invoke(id);
        }
        return _expired.Count;
    }

    /// <summary>Sets or clears (null) an override as the server says (a client's mirror, or a save loaded).</summary>
    public void Apply(ulong id, NaturalOverride? value)
    {
        bool wasDepleted = IsDepleted(id);
        Revision++;
        if (value is { } v)
        {
            _overrides[id] = v;
        }
        else
        {
            _overrides.Remove(id);
        }
        if (wasDepleted != IsDepleted(id))
        {
            PresenceChanged?.Invoke(id);
        }
    }

    private void Set(ulong id, NaturalOverride value, bool presence)
    {
        _overrides[id] = value;
        Revision++;
        Changed.Add(id);
        if (presence)
        {
            PresenceChanged?.Invoke(id);
        }
    }

    // The part of the whole yield gathered once health is down to "health": whole units only, so the hits add up to
    // exactly the total.
    private static int Gathered(int total, float max, float health) =>
        health <= 0 ? total : (int)Math.Floor(total * (double)(max - health) / max + 1e-9);
}
