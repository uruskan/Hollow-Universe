using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>A camera shake that started somewhere (GROUND.md §9.3): what the camera hook and other listeners get.</summary>
public readonly record struct FxShakeEvent(Vector3 Position, float Strength, float Radius, float Duration);

/// <summary>
/// Camera shakes (GROUND.md §9.3). Each blast adds a shake at its position: strong close by, nothing past its radius,
/// dying away over its duration. <see cref="Sample"/> gives the camera's offset (pitch, yaw, roll in radians) for where
/// the camera is: the shakes' strengths are summed into a "trauma" whose square drives smooth noise, so a small blast
/// nudges and a big one jolts.
/// </summary>
public sealed class FxShake
{
    private readonly List<(FxShakeEvent Shake, double Start)> _live = new();

    /// <summary>Largest offsets at full trauma, radians (pitch, yaw, roll).</summary>
    public Vector3 MaxAngles { get; set; } = new(0.045f, 0.035f, 0.03f);

    public void Add(double now, FxShakeEvent shake) => _live.Add((shake, now));

    public Vector3 Sample(double now, Vector3 camera)
    {
        float trauma = 0;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            (FxShakeEvent s, double start) = _live[i];
            float t = (float)(now - start) / Math.Max(0.01f, s.Duration);
            if (t >= 1f)
            {
                _live.RemoveAt(i);
                continue;
            }
            float d = camera.DistanceTo(s.Position) / Math.Max(0.1f, s.Radius);
            if (d >= 1f)
            {
                continue;
            }
            float near = (1f - d) * (1f - d);
            trauma += s.Strength * near * (1f - t) * (1f - t);
        }
        trauma = Math.Min(1f, trauma);
        if (trauma <= 0)
        {
            return Vector3.Zero;
        }
        float k = trauma * trauma;
        float time = (float)now;
        return new Vector3(Wave(time, 1.3f) * MaxAngles.X, Wave(time, 7.1f) * MaxAngles.Y, Wave(time, 3.7f) * MaxAngles.Z) * k;
    }

    public void Clear() => _live.Clear();

    /// <summary>Smooth noise in -1..1: three sines at unrelated frequencies.</summary>
    private static float Wave(float t, float phase) =>
        (MathF.Sin(t * 23f + phase) * 0.5f + MathF.Sin(t * 37f + phase * 2.3f) * 0.3f + MathF.Sin(t * 61f + phase * 4.1f) * 0.2f);
}
