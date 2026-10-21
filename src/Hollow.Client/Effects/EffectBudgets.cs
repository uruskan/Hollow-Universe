using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Hollow.Client.Effects;

/// <summary>Presentation resource ceilings. These do not change simulation or gameplay.</summary>
public sealed record EffectBudgetSettings
{
    public int ParticlesPerFrame { get; init; } = 1500;
    public int ActiveLoops { get; init; } = 64;
    public int LoopFlames { get; init; } = 48;
    public int LoopLights { get; init; } = 32;
    public float DistanceBand { get; init; } = 10;
    public double LeaseSeconds { get; init; } = 2;

    public static EffectBudgetSettings Read(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        int Count(string key, int fallback, int ceiling) => root.TryGetProperty(key, out JsonElement value)
            ? Math.Clamp(value.GetInt32(), 0, ceiling) : fallback;
        double Positive(string key, double fallback, double ceiling) => root.TryGetProperty(key, out JsonElement value)
            && double.IsFinite(value.GetDouble()) ? Math.Clamp(value.GetDouble(), 0.1, ceiling) : fallback;
        return new EffectBudgetSettings
        {
            ParticlesPerFrame = Count("particlesPerFrame", 1500, 100000),
            ActiveLoops = Count("activeLoops", 64, 512),
            LoopFlames = Count("loopFlames", 48, 512),
            LoopLights = Count("loopLights", 32, 128),
            DistanceBand = (float)Positive("distanceBand", 10, 1000),
            LeaseSeconds = Positive("leaseSeconds", 2, 60),
        };
    }
}

/// <summary>One counter for every particle path, reset once for each renderer frame.</summary>
public sealed class EffectParticleBudget
{
    private ulong _frame = ulong.MaxValue;
    public int Used { get; private set; }
    public int Limit { get; set; } = 1500;
    public int Remaining => Math.Max(0, Limit - Used);

    public bool BeginFrame(ulong frame)
    {
        if (_frame == frame) return false;
        _frame = frame;
        Used = 0;
        return true;
    }

    public int Take(int requested)
    {
        int admitted = Math.Min(Math.Max(0, requested), Remaining);
        Used += admitted;
        return admitted;
    }
}

/// <summary>A logical continuous effect requesting a render slot this frame.</summary>
public readonly record struct EffectLoopCandidate(int Id, string Key, int Cap, float Distance, int Priority,
    bool Flames, bool Light, bool Active, double ActivatedAt);

/// <summary>
/// Selects render slots without rejecting logical handles. Closest distance bands and first-person priority win;
/// equal-band contenders rotate after a short lease so a persistent crowd cannot starve a new effect forever.
/// Buffers are reused and no renderer resources are held here.
/// </summary>
public sealed class EffectLoopBudget
{
    private sealed class Order : IComparer<EffectLoopCandidate>
    {
        public EffectBudgetSettings Settings = new();
        public double Now;
        public int Compare(EffectLoopCandidate a, EffectLoopCandidate b)
        {
            int value = b.Priority.CompareTo(a.Priority);
            if (value != 0) return value;
            int bandA = (int)(a.Distance / Settings.DistanceBand), bandB = (int)(b.Distance / Settings.DistanceBand);
            value = bandA.CompareTo(bandB);
            if (value != 0) return value;
            bool heldA = a.Active && Now - a.ActivatedAt < Settings.LeaseSeconds;
            bool heldB = b.Active && Now - b.ActivatedAt < Settings.LeaseSeconds;
            value = heldB.CompareTo(heldA);
            if (value != 0) return value;
            value = a.ActivatedAt.CompareTo(b.ActivatedAt);
            return value != 0 ? value : a.Id.CompareTo(b.Id);
        }
    }

    private readonly List<EffectLoopCandidate> _ordered = new();
    private readonly Dictionary<string, int> _perEffect = new(StringComparer.Ordinal);
    private readonly HashSet<int> _selected = new();
    private readonly Order _order = new();
    public IReadOnlySet<int> Selected => _selected;

    public void Select(IReadOnlyList<EffectLoopCandidate> candidates, EffectBudgetSettings settings, double now)
    {
        _ordered.Clear();
        _selected.Clear();
        _perEffect.Clear();
        foreach (EffectLoopCandidate candidate in candidates)
            if (float.IsFinite(candidate.Distance) && candidate.Distance >= 0 && candidate.Cap > 0) _ordered.Add(candidate);
        _order.Settings = settings;
        _order.Now = now;
        _ordered.Sort(_order);
        int flames = 0, lights = 0;
        foreach (EffectLoopCandidate candidate in _ordered)
        {
            if (_selected.Count >= settings.ActiveLoops) break;
            int used = _perEffect.GetValueOrDefault(candidate.Key);
            if (used >= candidate.Cap || candidate.Flames && flames >= settings.LoopFlames || candidate.Light && lights >= settings.LoopLights) continue;
            _selected.Add(candidate.Id);
            _perEffect[candidate.Key] = used + 1;
            if (candidate.Flames) flames++;
            if (candidate.Light) lights++;
        }
    }
}
