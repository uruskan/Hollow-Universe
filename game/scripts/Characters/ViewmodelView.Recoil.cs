using System.Collections.Generic;

namespace HollowUniverse.Characters;

public partial class ViewmodelView
{
    // A shot adds a complete authored impulse. Restarting the current clip at automatic-fire cadence
    // discards its unfinished recovery and teleports the weapon; overlapping impulses preserve it.
    private sealed class FirePulse(HeldPresentationClip clip, float duration)
    {
        public readonly HeldPresentationClip Clip = clip;
        public readonly float Duration = duration;
        public float Age;
        public bool PhaseSought;
    }

    private readonly List<FirePulse> _firePulses = new();

    private void AdvanceFirePulses(float elapsed)
    {
        for (int i = _firePulses.Count - 1; i >= 0; i--)
        {
            FirePulse pulse = _firePulses[i];
            if (!pulse.PhaseSought) pulse.Age += elapsed;
            pulse.PhaseSought = false;
            if (pulse.Age >= pulse.Duration) _firePulses.RemoveAt(i);
        }
    }

    private HeldPresentationKey SampleFirePulses()
    {
        var total = new HeldPresentationKey();
        foreach (FirePulse pulse in _firePulses)
        {
            HeldPresentationKey sample = pulse.Clip.Sample(pulse.Age / pulse.Duration,
                _model!.Presentation.RightHand.Fingers, _model.Presentation.LeftHand.Fingers);
            total.Position += sample.Position;
            total.Rotation += sample.Rotation;
            total.RightHandPosition += sample.RightHandPosition;
            total.RightHandRotation += sample.RightHandRotation;
            total.LeftHandPosition += sample.LeftHandPosition;
            total.LeftHandRotation += sample.LeftHandRotation;
            // Finger curls are absolute poses; the newest shot owns them, not their sum.
            total.RightFingers = sample.RightFingers;
            total.LeftFingers = sample.LeftFingers;
        }
        return total;
    }
}
