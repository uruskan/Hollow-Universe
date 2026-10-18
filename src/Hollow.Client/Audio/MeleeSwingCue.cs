using System;

namespace Hollow.Client.Audio;

/// <summary>
/// One presentation-only swish during an attack's final anticipation phase. The caller supplies the same start
/// and contact ticks used by its animation. This gate never produces an impact or a gameplay result.
/// </summary>
public sealed class MeleeSwingCue<TAttempt> where TAttempt : struct, IEquatable<TAttempt>
{
    /// <summary>Provisional presentation choice, not measured audio matching: begin the swish in the final
    /// quarter of anticipation. One shared parameter can be tuned without item-specific branches.</summary>
    public const double DefaultAnticipationFraction = 0.75;

    private readonly double _anticipationFraction;
    private TAttempt _attempt;
    private bool _known, _pending;
    private double _cueTick, _contactTick;

    public MeleeSwingCue(double anticipationFraction = DefaultAnticipationFraction)
    {
        if (!double.IsFinite(anticipationFraction) || anticipationFraction <= 0 || anticipationFraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(anticipationFraction));
        _anticipationFraction = anticipationFraction;
    }

    public bool Pending => _pending;

    /// <summary>A new identity replaces any waiting cue. Repeating an identity never re-arms it.</summary>
    public void Start(TAttempt attempt, double startTick, double contactTick)
    {
        if (_known && _attempt.Equals(attempt)) return;
        _attempt = attempt;
        _known = true;
        _pending = true;
        SetTiming(startTick, contactTick);
    }

    /// <summary>Authority may move an unplayed cue without changing its stable attempt identity.</summary>
    public void Reconcile(TAttempt attempt, double startTick, double contactTick)
    {
        if (!_known || !_pending || !_attempt.Equals(attempt)) return;
        SetTiming(startTick, contactTick);
    }

    private void SetTiming(double startTick, double contactTick)
    {
        if (!double.IsFinite(startTick) || !double.IsFinite(contactTick) || contactTick <= startTick)
        {
            Cancel();
            return;
        }
        _cueTick = startTick + (contactTick - startTick) * _anticipationFraction;
        _contactTick = contactTick;
    }

    /// <summary>Consume once when the presentation clock reaches the cue. Activation at or after contact is
    /// silent; cancellation and clock rewinds cannot replay a consumed or abandoned cue.</summary>
    public bool Step(double tick, bool canPresent = true)
    {
        if (!canPresent || !double.IsFinite(tick)) Cancel();
        if (!_pending) return false;
        if (tick >= _contactTick)
        {
            Cancel();
            return false;
        }
        if (tick < _cueTick) return false;
        _pending = false;
        return true;
    }

    public void Cancel() => _pending = false;
}
