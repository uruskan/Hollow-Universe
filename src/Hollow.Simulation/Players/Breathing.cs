using System;
using Hollow.Core;
using Hollow.Surface;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Players;

/// <summary>One breath reserve for absent air. Water supplies the first environment; equipment can supply air here later.</summary>
public static class Breathing
{
    public static bool Underwater(ISurface surface, Vector3D eye)
    {
        double radius = eye.Length;
        if (radius < 1) return false;
        double water = surface.WaterLevelAt(eye / radius);
        return double.IsFinite(water) && radius < surface.Radius + water - 0.04;
    }

    /// <returns>Damage for the time spent without air after the reserve runs out.</returns>
    public static float Step(ref Vitals vitals, SurvivalTuning tuning, bool hasAir, float seconds)
    {
        float used = Math.Clamp(vitals.BreathUsed, 0, tuning.BreathSeconds);
        if (hasAir)
        {
            vitals.BreathUsed = Math.Max(0, used - tuning.BreathRecoveryPerSecond * seconds);
            vitals.Suffocating = false;
            return 0;
        }
        float exhaustedTime = Math.Clamp(used + seconds - tuning.BreathSeconds, 0, seconds);
        vitals.BreathUsed = Math.Min(tuning.BreathSeconds, used + seconds);
        vitals.Suffocating = vitals.BreathUsed >= tuning.BreathSeconds;
        return exhaustedTime * tuning.SuffocationDamagePerSecond;
    }
}
