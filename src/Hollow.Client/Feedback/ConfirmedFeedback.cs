using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Client.Feedback;

public readonly record struct ConfirmedCue(string Sound, Vector3 Point);

/// <summary>Bounded intent-owned feedback: rejected, expired and duplicate answers never emit an accepted-action cue.</summary>
public sealed class ConfirmedFeedback
{
    private readonly Dictionary<uint, (ConfirmedCue Cue, double Expires)> _pending = new();
    private const int Capacity = 128;
    public void Track(uint sequence, string? sound, Vector3 point, double now)
    {
        if (sequence == 0 || sound == null) return;
        if (_pending.Count >= Capacity)
        {
            uint oldest = 0; double expiry = double.PositiveInfinity;
            foreach (var entry in _pending) if (entry.Value.Expires < expiry) { oldest = entry.Key; expiry = entry.Value.Expires; }
            _pending.Remove(oldest);
        }
        _pending[sequence] = (new ConfirmedCue(sound, point), now + 10);
    }
    public ConfirmedCue? Complete(uint sequence, bool accepted, double now) =>
        _pending.Remove(sequence, out var pending) && accepted && now <= pending.Expires ? pending.Cue : null;
    public void Clear() => _pending.Clear();
}
