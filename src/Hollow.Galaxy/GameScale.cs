using System;
using Hollow.Core;
using Hollow.Galaxy.Systems;

namespace Hollow.Galaxy;

/// <summary>Playable lengths, kept separate from the generator's physical values (WORLD-01).</summary>
public static class GameScale
{
    public const double MaximumSurfaceArea = 23_040_000; // m²: the entire sphere, not one face.
    // Rounded down so the computed sphere area cannot exceed the requirement through roundoff.
    public const double MaximumPlanetRadius = 1354.055000514614;
    public const double DefaultEarthRadius = 1200;
    public const double MinimumPlanetRadius = 250;

    /// <summary>
    /// Provisional tuning: Earth is 1.2 km; cube-root compression preserves size ordering until the hard cap.
    /// This applies to planets and moons, including giants. Physical mass, radius, gravity and climate stay intact.
    /// </summary>
    public static double PlanetRadius(double realRadius, double earthRadius = DefaultEarthRadius)
    {
        if (!double.IsFinite(realRadius) || realRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(realRadius));
        if (!double.IsFinite(earthRadius) || earthRadius < MinimumPlanetRadius || earthRadius > MaximumPlanetRadius)
            throw new ArgumentOutOfRangeException(nameof(earthRadius));
        double relative = DMath.Pow(realRadius / Units.EarthRadius, 1.0 / 3.0);
        return Math.Clamp(earthRadius * relative, MinimumPlanetRadius, MaximumPlanetRadius);
    }

    public static double PlanetRadius(Body body) => PlanetRadius(body.Radius);

    public static double SurfaceArea(double radius) => 4 * DMath.Pi * radius * radius;
}
