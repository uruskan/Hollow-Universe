using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Ground;

/// <summary>
/// Light and glow in the planet's own units (GROUND.md §4.6): the sky meters the camera like an eye, a few thousand
/// times brighter by night than by day, so a fire's light is given its real strength (a tiny one next to the sun's)
/// and things that glow (flames, weak spots) are divided by the exposure, to look as bright by day as by night.
/// </summary>
public static class Glow
{
    private static readonly List<(WeakReference<StandardMaterial3D> Material, float Brightness)> Tracked = new();
    private static float _exposure = 1;

    /// <summary>A campfire's light: about 5 lux at two metres against the sun's 100,000 (the sun's light is 3 units).</summary>
    public const float FireLight = 0.0008f;

    /// <summary>The camera's exposure now (set every frame by the ground view).</summary>
    public static float Exposure
    {
        get => _exposure;
        set
        {
            if (Math.Abs(value - _exposure) < _exposure * 0.01f)
            {
                return;
            }
            _exposure = Math.Max(value, 1e-3f);
            for (int i = Tracked.Count - 1; i >= 0; i--)
            {
                if (Tracked[i].Material.TryGetTarget(out StandardMaterial3D? m))
                {
                    m.EmissionEnergyMultiplier = Tracked[i].Brightness / _exposure;
                }
                else
                {
                    Tracked.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Keeps a material's emission at <paramref name="brightness"/> as seen, whatever the exposure.</summary>
    public static void Track(StandardMaterial3D material, float brightness)
    {
        material.EmissionEnergyMultiplier = brightness / _exposure;
        Tracked.Add((new WeakReference<StandardMaterial3D>(material), brightness));
    }
}
