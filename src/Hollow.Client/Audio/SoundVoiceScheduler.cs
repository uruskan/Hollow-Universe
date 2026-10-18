using System;
using System.Collections.Generic;

namespace Hollow.Client.Audio;

/// <summary>A physical one-shot or a logical loop competing for one voice. IDs are unique within one selection.</summary>
public readonly record struct SoundVoiceCandidate(int Id, string Event, int Limit, int Priority, double AudibleDb,
    bool RetainLoop, long Order);

/// <summary>
/// Bounded selection shared by loop reconciliation and one-shot admission. Inaudible requests never win a voice;
/// event limits and the pool cap apply together. Loudness and priority choose winners, with a small retention
/// margin for already voiced loops so a listener near a boundary does not repeatedly restart two equal sources.
/// The caller retains logical loops after losing; one-shots may be discarded. Scratch storage is reused.
/// </summary>
public sealed class SoundVoiceScheduler
{
    private readonly List<Ranked> _ranked = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly HashSet<int> _selected = new();
    private readonly record struct Ranked(SoundVoiceCandidate Candidate, double Score);

    public int SelectedCount => _selected.Count;
    public bool Selected(int id) => _selected.Contains(id);

    public void Select(IEnumerable<SoundVoiceCandidate> candidates, int capacity, AudioVoiceBudget budget)
    {
        _ranked.Clear(); _counts.Clear(); _selected.Clear();
        capacity = Math.Clamp(capacity, 0, 512);
        if (capacity == 0) return;
        foreach (SoundVoiceCandidate candidate in candidates)
        {
            if (!double.IsFinite(candidate.AudibleDb) || candidate.AudibleDb < budget.VirtualizeBelowDb || candidate.Limit <= 0) continue;
            double score = Math.Min(3, candidate.AudibleDb) + Math.Clamp(candidate.Priority, 0, 100) * budget.PriorityWeightDb
                + (candidate.RetainLoop ? budget.HysteresisDb : 0);
            var ranked = new Ranked(candidate, score);
            _counts.TryGetValue(candidate.Event, out int used);
            int victim = -1;
            if (used >= candidate.Limit)
            {
                for (int i = _ranked.Count - 1; i >= 0; i--)
                    if (_ranked[i].Candidate.Event == candidate.Event) { victim = i; break; }
            }
            else if (_ranked.Count == capacity) victim = _ranked.Count - 1;
            if (victim >= 0)
            {
                if (Compare(ranked, _ranked[victim]) >= 0) continue;
                string oldKey = _ranked[victim].Candidate.Event;
                if (--_counts[oldKey] == 0) _counts.Remove(oldKey);
                _ranked.RemoveAt(victim);
            }
            // Only the winning <=capacity candidates are retained, even for thousands of live emitters.
            int low = 0, high = _ranked.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (Compare(ranked, _ranked[mid]) < 0) high = mid; else low = mid + 1;
            }
            _ranked.Insert(low, ranked);
            _counts.TryGetValue(candidate.Event, out used);
            _counts[candidate.Event] = used + 1;
        }
        foreach (Ranked ranked in _ranked) _selected.Add(ranked.Candidate.Id);
    }

    private static int Compare(Ranked a, Ranked b)
    {
        int score = b.Score.CompareTo(a.Score);
        if (score != 0) return score;
        int order = b.Candidate.Order.CompareTo(a.Candidate.Order);
        return order != 0 ? order : a.Candidate.Id.CompareTo(b.Candidate.Id);
    }

    /// <summary>Conservative inverse-distance estimate for ranking; Godot still performs the actual attenuation.</summary>
    public static double AudibleDb(double gainDb, double distance, double unitSize, double maxDistance, bool positional)
    {
        if (!double.IsFinite(gainDb) || !double.IsFinite(distance) || distance < 0) return double.NegativeInfinity;
        if (!positional) return gainDb;
        if (maxDistance <= 0 || distance >= maxDistance) return double.NegativeInfinity;
        double unit = Math.Max(0.001, unitSize);
        return gainDb - 20 * Math.Log10(Math.Max(1, distance / unit));
    }
}
