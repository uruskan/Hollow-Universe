using System;
using Hollow.Surface;

namespace Hollow.Client.Audio;

/// <summary>Where the listener is, for the ambience (GROUND.md §4.9). Distances in metres, angles in degrees.</summary>
public struct AmbienceInput
{
    /// <summary>Height above the sea.</summary>
    public double Altitude;

    /// <summary>How open the spot is to the wind: 0 sheltered (a hollow, a dense wood), 1 a bare ridge.</summary>
    public double Exposure;

    public Biome Biome;

    /// <summary>Trees around the listener, 0 (open) to 1 (dense forest).</summary>
    public double Forest;

    /// <summary>To the nearest sea shore (0 on the beach; large inland).</summary>
    public double SeaDistance;

    /// <summary>The sun's elevation above the horizon (local time of day).</summary>
    public double SunElevation;

    /// <summary>Wind strength, 0 calm to 1 a gale.</summary>
    public double Wind;

    /// <summary>Storminess, 0 to 1; thunder only above 0.3. No weather yet: 0.</summary>
    public double Storm;

    /// <summary>Inside a building, 0 to 1.</summary>
    public double Indoor;

    /// <summary>The sun's elevation for a local solar hour on an equinox at a latitude (for previews without an ephemeris).</summary>
    public static double SunAt(double hour, double latitude)
    {
        double h = (hour - 12) * 15 * Math.PI / 180;
        double phi = latitude * Math.PI / 180;
        return Math.Asin(Math.Cos(phi) * Math.Cos(h)) * 180 / Math.PI;
    }
}

/// <summary>How loud each ambience bed is (0–1, linear) and how often each accent comes (per minute).</summary>
public sealed class AmbienceLevels
{
    public double WindCalm;
    public double WindStrong;
    public double WindHigh;
    public double BirdsMeadow;
    public double BirdsForest;
    public double InsectsNight;
    public double SurfNear;
    public double SurfFar;
    public double Leaves;
    public double OwlRate;
    public double CrowRate;
    public double ThunderRate;

    /// <summary>The beds with their sounds.json keys, in a fixed order.</summary>
    public (string Key, double Level)[] Beds() => new[]
    {
        (AmbienceMix.WindCalmKey, WindCalm), (AmbienceMix.WindStrongKey, WindStrong), (AmbienceMix.WindHighKey, WindHigh),
        (AmbienceMix.BirdsMeadowKey, BirdsMeadow), (AmbienceMix.BirdsForestKey, BirdsForest),
        (AmbienceMix.InsectsKey, InsectsNight), (AmbienceMix.SurfNearKey, SurfNear), (AmbienceMix.SurfFarKey, SurfFar),
        (AmbienceMix.LeavesKey, Leaves),
    };
}

/// <summary>
/// The ambience rules (GROUND.md §4.9): a pure function from where the listener is to the level of each bed. Wind
/// is always there, stronger with wind, height and exposure; birds by day (meadow birds in the open, forest birds
/// among trees), insects at night, both quieter in wind, storm, cold biomes and high up; surf near the sea; leaves
/// rustling in forest, louder in wind. Rare accents: owls at night near trees, crows by day, thunder in a storm.
/// </summary>
public static class AmbienceMix
{
    public const string WindCalmKey = "amb.wind.calm";
    public const string WindStrongKey = "amb.wind.strong";
    public const string WindHighKey = "amb.wind.high";
    public const string BirdsMeadowKey = "amb.birds.meadow";
    public const string BirdsForestKey = "amb.birds.forest";
    public const string InsectsKey = "amb.insects.night";
    public const string SurfNearKey = "amb.surf.near";
    public const string SurfFarKey = "amb.surf.far";
    public const string LeavesKey = "amb.leaves";
    public const string OwlKey = "amb.owl";
    public const string CrowKey = "amb.crow";
    public const string ThunderKey = "amb.thunder";

    public static AmbienceLevels Compute(in AmbienceInput s)
    {
        double wind = Math.Clamp(Math.Max(s.Wind, s.Storm * 0.9), 0, 1);
        double exposure = Math.Clamp(s.Exposure, 0, 1);
        double forest = Math.Clamp(s.Forest, 0, 1);
        double day = Smooth(-6, 4, s.SunElevation);
        double night = 1 - Smooth(-10, -2, s.SunElevation);
        double alt = Math.Max(0, s.Altitude);
        double highUp = Smooth(400, 2000, alt);
        double wildlife = (1 - 0.7 * Smooth(1500, 2500, alt)) * (1 - 0.6 * wind) * (1 - 0.8 * Math.Clamp(s.Storm, 0, 1));
        double inside = 1 - 0.75 * Math.Clamp(s.Indoor, 0, 1);
        var l = new AmbienceLevels
        {
            WindCalm = (0.35 + 0.65 * wind) * (0.45 + 0.55 * exposure) * inside,
            WindStrong = Smooth(0.35, 0.85, wind * (0.6 + 0.4 * exposure) + 0.5 * highUp) * inside,
            WindHigh = Smooth(0.45, 1.0, wind * exposure + 0.6 * Smooth(800, 2300, alt)) * inside,
            BirdsMeadow = day * Open(s.Biome) * (1 - 0.8 * forest) * wildlife * inside,
            BirdsForest = day * Wooded(s.Biome) * Smooth(0.1, 0.6, forest) * wildlife * inside,
            InsectsNight = night * Warm(s.Biome) * (1 - 0.3 * forest) * wildlife * inside,
            SurfNear = (1 - Smooth(40, 220, s.SeaDistance)) * inside,
            SurfFar = Smooth(15, 120, s.SeaDistance) * (1 - Smooth(500, 1600, s.SeaDistance)) * inside,
            Leaves = Smooth(0.1, 0.7, forest) * (0.3 + 0.7 * wind) * inside,
        };
        l.OwlRate = night * Smooth(0.15, 0.6, forest) * wildlife * 0.5;
        l.CrowRate = day * Math.Max(Open(s.Biome), Wooded(s.Biome)) * wildlife * 0.35;
        l.ThunderRate = s.Storm > 0.3 ? 0.4 + 1.6 * Math.Clamp(s.Storm, 0, 1) : 0;
        if (s.Biome == Biome.Ocean || s.Biome == Biome.IceSheet)
        {
            l.CrowRate = 0;
            l.OwlRate = 0;
        }
        return l;
    }

    /// <summary>Meadow birds by biome: open, temperate land most.</summary>
    private static double Open(Biome b) => b switch
    {
        Biome.Grassland => 1.0,
        Biome.Shrubland or Biome.Savanna => 0.9,
        Biome.TemperateForest => 0.6,
        Biome.TropicalForest => 0.5,
        Biome.BorealForest or Biome.Beach => 0.4,
        Biome.Tundra => 0.3,
        Biome.Alpine => 0.2,
        Biome.Desert => 0.15,
        _ => 0.0,
    };

    /// <summary>Forest birds by biome.</summary>
    private static double Wooded(Biome b) => b switch
    {
        Biome.TemperateForest or Biome.TropicalForest => 1.0,
        Biome.BorealForest => 0.8,
        Biome.Grassland or Biome.Shrubland or Biome.Savanna => 0.6,
        Biome.Tundra or Biome.Alpine or Biome.Beach => 0.3,
        Biome.Desert => 0.1,
        _ => 0.0,
    };

    /// <summary>Night insects by biome: warm and green.</summary>
    private static double Warm(Biome b) => b switch
    {
        Biome.Grassland or Biome.Shrubland or Biome.Savanna or Biome.TropicalForest => 1.0,
        Biome.TemperateForest => 0.9,
        Biome.BorealForest or Biome.Beach => 0.5,
        Biome.Desert => 0.4,
        Biome.Tundra => 0.15,
        Biome.Alpine => 0.1,
        _ => 0.0,
    };

    private static double Smooth(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
