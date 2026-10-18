using System;

namespace Hollow.Client.Audio;

/// <summary>Gain, pitch and playhead of a loop, independent of whether it currently owns an audible voice.</summary>
public sealed class SoundLoopEnvelope
{
    public SoundLoopEnvelope(float gainDb, float pitch = 1)
    {
        GainDb = TargetDb = Gain(gainDb);
        SetPitch(pitch);
    }

    public float GainDb { get; private set; }
    public float TargetDb { get; private set; }
    public float Pitch { get; private set; }
    public double Cursor { get; private set; }
    public bool Stopping { get; private set; }
    public bool Finished => Stopping && GainDb <= -79.9f;
    private float _rate;

    public void SetGain(float db, float seconds)
    {
        if (Stopping) return;
        Fade(Gain(db), seconds);
    }

    public void Stop(float seconds)
    {
        if (Stopping) return;
        Stopping = true;
        Fade(-80, seconds);
    }

    private void Fade(float target, float seconds)
    {
        TargetDb = target;
        if (!float.IsFinite(seconds) || seconds <= 0) { GainDb = target; _rate = 0; }
        else _rate = Math.Abs(target - GainDb) / seconds;
    }

    public void SetPitch(float pitch) => Pitch = float.IsFinite(pitch) ? Math.Clamp(pitch, 0.05f, 8f) : 1f;

    public void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0 || Finished) return;
        Cursor += delta * Pitch;
        float step = (float)Math.Min(float.MaxValue, delta * _rate);
        GainDb = GainDb < TargetDb ? Math.Min(TargetDb, GainDb + step) : Math.Max(TargetDb, GainDb - step);
    }

    private static float Gain(float db) => float.IsFinite(db) ? Math.Clamp(db, -80, 24) : -80;
}
