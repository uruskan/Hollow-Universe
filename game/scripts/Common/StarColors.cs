using System;
using Godot;
using Hollow.Galaxy;

namespace HollowUniverse.Common;

/// <summary>Presentation colours for stars (blackbody-like, tuned to read well on a dark map).</summary>
public static class StarColors
{
    // Temperature (K) → colour.
    private static readonly (float T, Color C)[] Ramp =
    {
        (1000, new Color(0.80f, 0.28f, 0.34f)),
        (2000, new Color(1.00f, 0.38f, 0.16f)),
        (3000, new Color(1.00f, 0.56f, 0.30f)),
        (4000, new Color(1.00f, 0.72f, 0.47f)),
        (5000, new Color(1.00f, 0.86f, 0.70f)),
        (5800, new Color(1.00f, 0.95f, 0.87f)),
        (6600, new Color(0.97f, 0.97f, 1.00f)),
        (8000, new Color(0.83f, 0.88f, 1.00f)),
        (10000, new Color(0.73f, 0.80f, 1.00f)),
        (20000, new Color(0.63f, 0.72f, 1.00f)),
        (40000, new Color(0.58f, 0.68f, 1.00f)),
    };

    public static Color FromTemperature(float kelvin)
    {
        if (kelvin <= Ramp[0].T)
        {
            return Ramp[0].C;
        }
        for (int i = 1; i < Ramp.Length; i++)
        {
            if (kelvin <= Ramp[i].T)
            {
                float t = (kelvin - Ramp[i - 1].T) / (Ramp[i].T - Ramp[i - 1].T);
                return Ramp[i - 1].C.Lerp(Ramp[i].C, t);
            }
        }
        return Ramp[^1].C;
    }

    public static Color For(StarClass starClass, float temperature) => starClass.Kind switch
    {
        StarKind.BlackHole => new Color(0.62f, 0.45f, 1.0f),
        StarKind.NeutronStar => new Color(0.70f, 0.90f, 1.0f),
        StarKind.WolfRayet => new Color(0.65f, 0.80f, 1.0f),
        StarKind.CarbonStar => new Color(1.0f, 0.42f, 0.22f),
        _ => FromTemperature(temperature),
    };

    /// <summary>Magnitude used for drawing; exotic objects that emit no visible light still get a dot.</summary>
    public static float DisplayMagnitude(StarClass starClass, float absoluteMagnitude) => starClass.Kind switch
    {
        StarKind.BlackHole or StarKind.NeutronStar => 11.5f,
        _ => Math.Min(absoluteMagnitude, 17f),
    };
}
