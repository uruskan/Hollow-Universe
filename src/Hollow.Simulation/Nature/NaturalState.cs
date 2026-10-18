using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// about the object's up, in 1/65536 of a turn, and a second number its surface reads (a tree: how high in its
    /// band; an outcrop: which ring of its measured top), 0 … 255 (<see cref="ISpotSurface"/>).
    /// </summary>
    public bool HasWeakSpot;
    public ushort WeakSpotYaw;
    public byte WeakSpotLift;

    /// <summary>Hits on the weak spot in a row (Rust's bonus level): 0 after a hit elsewhere. Not saved.</summary>
    public byte Streak;

    /// <summary>Where the last hit met the drawn surface, from the object's base (its effects play there). Not saved.</summary>
    public Vector3 LastHit;

    /// <summary>The server: the zone tick of the last hit (0: none since it started). Not saved.</summary>
    public long LastHitTick;

    /// <summary>A client's mirror: fresh hits seen so far (wraps); the views play one hit's effects per step.</summary>
    public byte SeenHits;

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

/// <summary>
/// A tool's swing landing on a tree or outcrop: the tool (for the last hit's bonus), its stats, where the swing met the
/// object's hit shape, the eye and the view it came along, the real clock and the zone's tick.
/// </summary>
public readonly record struct GatherSwing(ItemDef? Tool, MeleeStats Melee, Vector3 Point, Vector3 Eye, Vector3 Direction, long NowUnixMs, long Tick);

/// <summary>What a gather hit or a pick gave.</summary>
public struct GatherResult
{
    public bool Hit;

    /// <summary>The hit was on the weak spot (it paid <see cref="Scale"/> and moved the spot).</summary>
    public bool WeakSpot;

    /// <summary>Hits on the weak spot in a row, this one included (0: not on it).</summary>
    public int Streak;

    /// <summary>What the hit paid and took, against an ordinary hit.</summary>
    public float Scale;

    /// <summary>Where the swing met the drawn surface.</summary>
    public Vector3 Point;

    /// <summary>The object is gone now (felled, broken, picked).</summary>
    public bool Depleted;

    /// <summary>The last hit's bonus was paid (a proper tool).</summary>
    public bool Bonus;

    public List<ItemAmount> Yield;
}

/// <summary>
/// The changes to natural objects in a zone (GROUND.md §6): health, weak spots and depletion, with their respawn times.
/// The server owns one and applies the gathering rules to it; a client keeps a mirror from what the server sends, to
/// draw and to collide. <see cref="Changed"/> collects the IDs to send and save.
/// </summary>
public sealed class NaturalState
{
    private readonly Dictionary<ulong, NaturalOverride> _overrides = new();
    private readonly ReadOnlyDictionary<ulong, NaturalOverride> _readOnlyOverrides;
    private readonly List<ulong> _expired = new();
    internal object CollisionIdentity { get; } = new();

    /// <summary>
    /// Nonwrapping local presence version for collision snapshots. Conservatively includes pickups/bushes too;
    /// health-only changes do not invalidate collision. Independent of saved/network Revision semantics.
    /// </summary>
    public ulong CollisionPresenceVersion { get; private set; }

    public NaturalState(GameContent content, INaturalObjects objects)
    {
        Content = content;
        Objects = objects;
        _readOnlyOverrides = new ReadOnlyDictionary<ulong, NaturalOverride>(_overrides);
    }

    public GameContent Content { get; }

    public INaturalObjects Objects { get; }

    public int Count => _overrides.Count;

    public IReadOnlyDictionary<ulong, NaturalOverride> Overrides => _readOnlyOverrides;

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

    /// <summary>The depletion stage (0 whole … stages − 1): an outcrop breaks up as it loses health; for drawing and spots.</summary>
    public int Stage(in NaturalObject o, NaturalKind kind) => StageOf(Health(o, kind), NaturalShapes.MaxHealth(o, kind), kind);

    public static int StageOf(float health, float max, NaturalKind kind)
    {
        if (kind.Stages <= 1 || !kind.IsHarvested || max <= 0)
        {
            return 0;
        }
        return Math.Clamp((int)((1 - health / max) * kind.Stages), 0, kind.Stages - 1);
    }

    /// <summary>Where an object's weak spot is now, or null before its first hit.</summary>
    public Vector3? WeakSpot(in NaturalObject o, NaturalKind kind) => WeakSpotAt(o, kind)?.Point;

    /// <summary>An object's weak spot on its drawn surface and the surface's normal there, or null when it has none.</summary>
    public (Vector3 Point, Vector3 Normal)? WeakSpotAt(in NaturalObject o, NaturalKind kind)
    {
        if (kind.Gather is not { } rules || !_overrides.TryGetValue(o.Id, out NaturalOverride v) || !v.HasWeakSpot || v.Depleted)
        {
            return null;
        }
        return rules.Spot.Surface.At(o, kind, StageOf(v.Health, NaturalShapes.MaxHealth(o, kind), kind), v.WeakSpotYaw, v.WeakSpotLift);
    }

    /// <summary>
    /// A tool's hit on a tree or outcrop, by gather.json's rules for its class (GROUND.md §6.1): it takes the tool's rate
    /// of health, times the weak spot's multiplier when the swing met the drawn surface on the spot, and pays the share of
    /// the yield the hits pay that health was worth; the last hit pays the share kept for it, and the bonus with a proper
    /// tool. The spot appears with the first hit and moves after each hit on it, both times to the side facing the
    /// hitter; a hit elsewhere ends the run and leaves it. Gone at no health, until the respawn time.
    /// </summary>
    public GatherResult Hit(in NaturalObject o, in GatherSwing swing)
    {
        var result = new GatherResult { Yield = new List<ItemAmount>(), Scale = 1 };
        NaturalKind? kind = Content.Natural(o.Kind);
        if (kind?.Gather is not { } rules || IsDepleted(o.Id))
        {
            return result;
        }
        float rate = swing.Melee.Rate(rules.Rate);
        if (rate <= 0)
        {
            return result;
        }
        result.Hit = true;
        float max = NaturalShapes.MaxHealth(o, kind);
        NaturalOverride state = _overrides.TryGetValue(o.Id, out NaturalOverride v) ? v : new NaturalOverride { Health = max };
        ISpotSurface surface = rules.Spot.Surface;
        int stage = StageOf(state.Health, max, kind);
        Vector3 meet = surface.Meet(o, kind, stage, swing.Eye, swing.Direction, swing.Point);
        bool onSpot = state.HasWeakSpot &&
            Vector3.Distance(meet, surface.At(o, kind, stage, state.WeakSpotYaw, state.WeakSpotLift).Point) <= rules.Spot.Radius;
        state.Streak = onSpot ? (byte)Math.Min(byte.MaxValue, state.Streak + 1) : (byte)0;
        float scale = rules.Spot.Scale(state.Streak);
        float before = state.Health, after = Math.Max(0, before - rate * scale);
        if (onSpot || !state.HasWeakSpot)
        {
            // Face the hitter, give or take a little, on the surface as it will be after this hit.
            var rng = new Rng(Hash64.Of(o.Id, (ulong)BitConverter.SingleToUInt32Bits(state.Health), 0x5B07));
            double facing = SpotFrames.YawOf(o, swing.Eye - o.Position) + (rng.NextDouble() * 2 - 1) * rules.Spot.FacingDegrees * DMath.DegToRad;
            (state.WeakSpotYaw, state.WeakSpotLift) = surface.Place(o, kind, StageOf(after, max, kind), facing, meet, ref rng);
            state.HasWeakSpot = true;
        }
        foreach (ItemAmount y in kind.Yield)
        {
            int total = NaturalShapes.Total(o, kind, y);
            int last = (int)Math.Round(total * (double)rules.Last.Share);
            int gained = Gathered(total - last, max, after) - Gathered(total - last, max, before);
            if (after <= 0)
            {
                gained += last;
                if (rules.Last.PaysBonus(swing.Tool))
                {
                    gained += (int)Math.Round(total * (double)rules.Last.Bonus);
                    result.Bonus = true;
                }
            }
            if (gained > 0)
            {
                result.Yield.Add(new ItemAmount(y.Item, gained));
            }
        }
        state.Health = after;
        state.LastHit = meet - o.Position;
        state.LastHitTick = swing.Tick;
        result.WeakSpot = onSpot;
        result.Streak = state.Streak;
        result.Scale = scale;
        result.Point = meet;
        if (after <= 0)
        {
            state.Depleted = true;
            state.HasWeakSpot = false;
            state.Streak = 0;
            state.RespawnAtUnixMs = swing.NowUnixMs + (long)(kind.RespawnSeconds * 1000);
            result.Depleted = true;
        }
        Set(o.Id, state, presence: result.Depleted);
        return result;
    }

    /// <summary>A hit from <paramref name="eye"/> straight at <paramref name="point"/> with no tool named (tests, tools).</summary>
    public GatherResult Hit(in NaturalObject o, MeleeStats tool, Vector3 point, Vector3 eye, long nowUnixMs) =>
        Hit(o, new GatherSwing(null, tool, point, eye, Direction(eye, point), nowUnixMs, 0));

    private static Vector3 Direction(Vector3 from, Vector3 to) =>
        Vector3.DistanceSquared(from, to) > 1e-8f ? Vector3.Normalize(to - from) : -Vector3.UnitY;

    /// <summary>Picks a pickup or a bush: its yield, and it is gone (or bare) until it grows back.</summary>
    public GatherResult Pick(in NaturalObject o, long nowUnixMs)
    {
        var result = new GatherResult { Yield = new List<ItemAmount>(), Scale = 1 };
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
        // Refuse an exhausted presence clock before partially applying this respawn batch.
        _ = checked(CollisionPresenceVersion + (ulong)_expired.Count);
        foreach (ulong id in _expired)
        {
            CollisionPresenceVersion = checked(CollisionPresenceVersion + 1);
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
        bool depleted = value?.Depleted ?? false;
        if (wasDepleted != depleted) CollisionPresenceVersion = checked(CollisionPresenceVersion + 1);
        Revision++;
        Changed.Add(id);
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
        if (IsDepleted(id) != value.Depleted) CollisionPresenceVersion = checked(CollisionPresenceVersion + 1);
        _overrides[id] = value;
        Revision++;
        Changed.Add(id);
        if (presence)
        {
            PresenceChanged?.Invoke(id);
        }
    }

    // The part of a yield gathered once health is down to "health": whole units only, so the hits add up to exactly the
    // total.
    private static int Gathered(int total, float max, float health) =>
        health <= 0 ? total : (int)Math.Floor(total * (double)(max - health) / max + 1e-9);
}
