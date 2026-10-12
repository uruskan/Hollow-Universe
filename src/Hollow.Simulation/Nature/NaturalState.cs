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

    /// <summary>How many times its weak spot moved: where the spot is follows from the ID and this.</summary>
    public ushort WeakSpotMoves;

    /// <summary>Felled, broken or picked: gone (a bush: bare) until <see cref="RespawnAtUnixMs"/>.</summary>
    public bool Depleted;

    /// <summary>When it grows back, in Unix milliseconds (real time: nature carries on while the server is down).</summary>
    public long RespawnAtUnixMs;
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

    /// <summary>Where an object's weak spot is now: on a tree's trunk at chest height, on an outcrop's upper surface.</summary>
    public Vector3 WeakSpot(in NaturalObject o, NaturalKind kind)
    {
        ushort moves = _overrides.TryGetValue(o.Id, out NaturalOverride v) ? v.WeakSpotMoves : (ushort)0;
        var rng = new Rng(Hash64.Of(o.Id, moves, 0x5B07));
        double angle = rng.Range(0, DMath.TwoPi);
        (Vector3 forward, _) = Movement.CharacterMotor.HorizontalBasis((float)angle, o.Up);
        if (kind.Class == NaturalClass.Tree)
        {
            (Vector3 a, _, float radius) = NaturalShapes.Trunk(o, kind);
            float height = (float)rng.Range(0.9, 1.6);
            return a - o.Up * radius + o.Up * height + forward * radius;
        }
        (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
        double elevation = rng.Range(0.15, 0.85);
        float up = (float)DMath.Sin(elevation), outward = (float)DMath.Cos(elevation);
        return centre + (forward * outward + o.Up * up) * r;
    }

    /// <summary>
    /// A tool's hit on a tree or outcrop at <paramref name="point"/>: takes the tool's rate of health (twice that on
    /// the weak spot, which then moves) and gives the share of the object's whole yield that health was. The last hit
    /// gives the remainder and starts the respawn timer.
    /// </summary>
    public GatherResult Hit(in NaturalObject o, MeleeStats tool, Vector3 point, long nowUnixMs)
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
        if (Vector3.Distance(point, WeakSpot(o, kind)) <= Content.WeakSpot.Radius)
        {
            rate *= Content.WeakSpot.Bonus;
            result.WeakSpot = true;
            state.WeakSpotMoves++;
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
            Changed.Add(id);
            PresenceChanged?.Invoke(id);
        }
        return _expired.Count;
    }

    /// <summary>Sets or clears (null) an override as the server says (a client's mirror, or a save loaded).</summary>
    public void Apply(ulong id, NaturalOverride? value)
    {
        bool wasDepleted = IsDepleted(id);
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
