using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>
/// Where players arrive and respawn "on the beach", as in Rust (GROUND.md §6.1): real coast points near the zone's
/// origin, found once by sampling the planet: sandy shore (mostly the beach biome) between half a metre and three and a
/// half metres above the sea, gentle, with the water within 30 m. Deterministic: the same zone always finds the same
/// points, nearest first, at least <see cref="Spacing"/> apart.
/// </summary>
public sealed class Beaches
{
    /// <summary>Beach points are at least this far apart.</summary>
    public const double Spacing = 45;

    /// <summary>At most this many are kept, the nearest to the origin.</summary>
    public const int MaxPoints = 24;

    private Beaches(IReadOnlyList<Vector3> points, double searched)
    {
        Points = points;
        SearchedRadius = searched;
    }

    /// <summary>The beach points in the zone frame (on the ground, 5 cm up), nearest to the origin first.</summary>
    public IReadOnlyList<Vector3> Points { get; }

    /// <summary>How far from the origin the search had to look, in metres.</summary>
    public double SearchedRadius { get; }

    /// <summary>A random point among them (by <paramref name="salt"/>), or null when the coast is too far.</summary>
    public Vector3? Pick(ulong salt) => Points.Count == 0 ? null : Points[(int)(Hash64.Of(salt, 0xBEAC4) % (ulong)Points.Count)];

    /// <summary>
    /// Searches out from the origin in rings of 2 km (to <paramref name="maxRadius"/>, inside the zone's ground) until
    /// beach points turn up.
    /// </summary>
    public static Beaches Find(PlanetSurface surface, ZoneFrame frame, double maxRadius = 8000)
    {
        var found = new List<(double Distance, Vector3 Point)>();
        double radius = 0;
        while (found.Count == 0 && radius < maxRadius)
        {
            double inner = radius;
            radius = Math.Min(maxRadius, radius + 2000);
            Search(surface, frame, inner, radius, found);
        }
        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var kept = new List<Vector3>();
        foreach ((_, Vector3 p) in found)
        {
            bool apart = true;
            foreach (Vector3 k in kept)
            {
                if (Vector3.DistanceSquared(k, p) < Spacing * Spacing)
                {
                    apart = false;
                    break;
                }
            }
            if (apart)
            {
                kept.Add(p);
                if (kept.Count >= MaxPoints)
                {
                    break;
                }
            }
        }
        return new Beaches(kept, radius);
    }

    // A grid of 20 m over the ring between two radii.
    private static void Search(PlanetSurface surface, ZoneFrame frame, double inner, double outer, List<(double, Vector3)> found)
    {
        const double Step = 20;
        int n = (int)Math.Ceiling(outer / Step);
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        for (int j = -n; j <= n; j++)
        {
            for (int i = -n; i <= n; i++)
            {
                double x = i * Step, z = j * Step, d = Math.Sqrt(x * x + z * z);
                if (d < inner || d >= outer)
                {
                    continue;
                }
                Vector3D direction = frame.ToBody(x, 0, z).Normalized;
                SurfaceSample s = surface.Sample(direction);
                if (s.Height < 0.5 || s.Height > 3.5)
                {
                    continue;
                }
                surface.BiomeWeights(direction, s, surface.Climate(direction, s), biomes);
                if (biomes[(int)Biome.Beach] < 0.5 || !Gentle(surface, frame, x, z, s.Height) || !WaterNear(surface, frame, x, z))
                {
                    continue;
                }
                Vector3 ground = frame.ToLocal(direction * (surface.Radius + s.Height));
                found.Add((d, ground + frame.UpAt(ground) * 0.05f));
            }
        }
    }

    // Under about 14° across 3 m both ways.
    private static bool Gentle(PlanetSurface surface, ZoneFrame frame, double x, double z, double height)
    {
        double hx = surface.HeightAt(frame.ToBody(x + 3, 0, z).Normalized), hz = surface.HeightAt(frame.ToBody(x, 0, z + 3).Normalized);
        return Math.Abs(hx - height) < 0.75 && Math.Abs(hz - height) < 0.75;
    }

    // The sea within 30 m in one of eight directions.
    private static bool WaterNear(PlanetSurface surface, ZoneFrame frame, double x, double z)
    {
        for (int k = 0; k < 8; k++)
        {
            double a = k * DMath.TwoPi / 8;
            if (surface.HeightAt(frame.ToBody(x + 30 * DMath.Cos(a), 0, z + 30 * DMath.Sin(a)).Normalized) < 0)
            {
                return true;
            }
        }
        return false;
    }
}
