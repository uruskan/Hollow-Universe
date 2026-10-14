using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>
/// Where players arrive and respawn "on the beach", as in Rust (GROUND.md §6.1, docs/WHOLE_WORLD.md decision 8): coast
/// points anywhere on the world, found once by sampling it on a cube-sphere grid of about <see cref="Step"/> metres:
/// beach (the surface's beach biome) between half a metre and three and a half metres above the sea, gentle, with the
/// water within 30 m. A spread of them is kept, at least <see cref="Spacing"/> apart, picked in an order shuffled by
/// their tiles' hashes so they lie all round the world. Deterministic: the same world always finds the same points. A
/// world without a sea has none.
/// </summary>
public sealed class Beaches
{
    /// <summary>Beach points are at least this far apart.</summary>
    public const double Spacing = 45;

    /// <summary>At most this many are kept.</summary>
    public const int MaxPoints = 256;

    /// <summary>About how far apart the search's samples are, in metres.</summary>
    public const double Step = 25;

    private Beaches(IReadOnlyList<Vector3> points)
    {
        Points = points;
    }

    /// <summary>The beach points in the zone frame (on the ground, 5 cm up).</summary>
    public IReadOnlyList<Vector3> Points { get; }

    /// <summary>A random point among them (by <paramref name="salt"/>), or null when the world has no beach.</summary>
    public Vector3? Pick(ulong salt) => Points.Count == 0 ? null : Points[(int)(Hash64.Of(salt, 0xBEAC4) % (ulong)Points.Count)];

    /// <summary>Searches the whole world for beach points.</summary>
    public static Beaches Find(ISurface surface, ZoneFrame frame)
    {
        if (surface.SeaRadius <= 0)
        {
            return new Beaches(Array.Empty<Vector3>());
        }
        double sea = surface.SeaRadius - surface.Radius;
        int level = TileKey.LevelFor(surface.Radius, Step, 12);
        int n = 1 << level;
        var found = new List<(ulong Order, Vector3 Point)>();
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var tile = new TileKey(face, level, x, y);
                    Vector3D direction = tile.Centre;
                    double h = surface.HeightAt(direction) - sea;
                    if (h < 0.5 || h > 3.5 || surface.BiomeAt(direction) != Biome.Beach ||
                        !Gentle(surface, direction, h + sea) || !WaterNear(surface, direction, sea))
                    {
                        continue;
                    }
                    Vector3 ground = frame.ToLocal(direction * (surface.Radius + surface.HeightAt(direction)));
                    found.Add((Hash64.Of(tile.Packed, 0xBEAC), ground + frame.UpAt(ground) * 0.05f));
                }
            }
        }
        found.Sort((a, b) => a.Order.CompareTo(b.Order));
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
        return new Beaches(kept);
    }

    // A direction a little way along the ground: east and north of another, in metres.
    private static Vector3D Beside(ISurface surface, Vector3D direction, double east, double north)
    {
        Vector3D e = Vector3D.Cross(Vector3D.UnitY, direction);
        e = e.LengthSquared > 1e-18 ? e.Normalized : new Vector3D(0, 0, -1);
        Vector3D n = Vector3D.Cross(direction, e);
        return (direction * surface.Radius + e * east + n * north).Normalized;
    }

    // Under about 14° across 3 m both ways.
    private static bool Gentle(ISurface surface, Vector3D direction, double height)
    {
        double he = surface.HeightAt(Beside(surface, direction, 3, 0)), hn = surface.HeightAt(Beside(surface, direction, 0, 3));
        return Math.Abs(he - height) < 0.75 && Math.Abs(hn - height) < 0.75;
    }

    // The sea within 30 m in one of eight directions.
    private static bool WaterNear(ISurface surface, Vector3D direction, double sea)
    {
        for (int k = 0; k < 8; k++)
        {
            double a = k * DMath.TwoPi / 8;
            if (surface.HeightAt(Beside(surface, direction, 30 * DMath.Cos(a), 30 * DMath.Sin(a))) < sea)
            {
                return true;
            }
        }
        return false;
    }
}
