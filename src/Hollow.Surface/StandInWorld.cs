using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// The stand-in world S3 plays on until S4 makes the real ground at walking scale (docs/WHOLE_WORLD.md §4): a small
/// ball with continents about a kilometre across and ±20 m high under the plain hills of <see cref="SphereSurface"/>, a
/// sea over <see cref="SeaCover"/> of it (the first world's real cover), and climate bands by latitude, so arrivals on
/// beaches, coastal loot, swimming, the nav grid's water, biomes and the map all have something to work on. A test
/// fixture, not a world design. Deterministic: + − × ÷, floor, <see cref="Hash64"/> and <see cref="DMath"/>.
/// </summary>
public sealed class StandInWorld : ISurface
{
    /// <summary>The share of the world under the sea.</summary>
    public const double SeaCover = 0.45;

    /// <summary>The continents' heights: a broad layer and a narrower one, in metres.</summary>
    public const double ContinentHeight = 20, ShoulderHeight = 6;

    /// <summary>Directions sampled to put the sea at its cover.</summary>
    private const int SeaSamples = 4096;

    /// <summary>The landing site stands this high above the sea at least, on land that falls away over <see cref="IslandRadius"/>.</summary>
    public const double LandingHeight = 6, IslandRadius = 400;

    private readonly SphereSurface _hills;
    private readonly ulong _seed;
    private readonly Vector3D _landing;
    private readonly double _lift;

    /// <summary>A stand-in world of a radius and seed, with land under <paramref name="landing"/> (a unit direction) if given.</summary>
    public StandInWorld(double radius, ulong seed, Vector3D? landing = null)
    {
        Radius = radius;
        _seed = Hash64.Of(seed, 0x5EA);
        _hills = new SphereSurface(radius, seed);
        SeaLevel = LevelForCover(SeaCover);
        SeaRadius = radius + SeaLevel;
        if (landing is { } site && site.LengthSquared > 0)
        {
            // Raised where needed so arrivals and tests at the landing site stand on dry land (a small island at worst).
            _landing = site.Normalized;
            _lift = Math.Max(0, SeaLevel + LandingHeight - BaseHeight(_landing));
        }
    }

    public double Radius { get; }

    public double SeaRadius { get; }

    /// <summary>The sea's height above the mean radius, in metres.</summary>
    public double SeaLevel { get; }

    /// <summary>The amplitudes of the continents and the hills either way, and the lift of the landing site on top.</summary>
    public (double Low, double High) HeightBounds
    {
        get
        {
            (double low, double high) = _hills.HeightBounds;
            double continents = ContinentHeight + ShoulderHeight;
            return (low - continents, high + continents + _lift);
        }
    }

    public double HeightAt(Vector3D direction)
    {
        double h = BaseHeight(direction);
        if (_lift > 0)
        {
            // The arc to the landing site, from the chord (no trigonometry): smooth to nothing at IslandRadius.
            double chord = (direction - _landing).Length * Radius;
            double t = Math.Clamp(1 - chord / IslandRadius, 0, 1);
            h += _lift * t * t * (3 - 2 * t);
        }
        return h;
    }

    private double BaseHeight(Vector3D direction) => Continent(direction) + _hills.HeightAt(direction);

    /// <summary>
    /// The band a direction falls in: under the sea, the beach (within 3 m above it), then by latitude, ice beyond 70°,
    /// tundra from 55°, boreal forest from 40°, and temperate forest and grassland in patches below that.
    /// </summary>
    public Biome BiomeAt(Vector3D direction)
    {
        Vector3D d = direction.Normalized;
        double height = HeightAt(d) - SeaLevel;
        if (height < 0)
        {
            return Biome.Ocean;
        }
        if (height < 3)
        {
            return Biome.Beach;
        }
        double latitude = Math.Abs(DMath.Asin(Math.Clamp(d.Y, -1, 1))) * (180 / DMath.Pi);
        if (latitude >= 70)
        {
            return Biome.IceSheet;
        }
        if (latitude >= 55)
        {
            return Biome.Tundra;
        }
        if (latitude >= 40)
        {
            return Biome.BorealForest;
        }
        return SphereSurface.ValueNoise(d * (Radius / 600), Hash64.Of(_seed, 0xB10)) < 0.5 ? Biome.TemperateForest : Biome.Grassland;
    }

    private double Continent(Vector3D direction)
    {
        double broad = SphereSurface.ValueNoise(direction * (Radius / 1100), Hash64.Of(_seed, 1)) - 0.5;
        double shoulder = SphereSurface.ValueNoise(direction * (Radius / 450), Hash64.Of(_seed, 2)) - 0.5;
        return broad * 2 * ContinentHeight + shoulder * 2 * ShoulderHeight;
    }

    // The height below which a share of evenly spread directions lies (a Fibonacci sphere: the same on every machine).
    private double LevelForCover(double cover)
    {
        var heights = new List<double>(SeaSamples);
        double golden = DMath.Pi * (3 - Math.Sqrt(5));
        for (int k = 0; k < SeaSamples; k++)
        {
            double y = 1 - 2 * (k + 0.5) / SeaSamples;
            double r = Math.Sqrt(Math.Max(0, 1 - y * y));
            double theta = golden * k;
            heights.Add(BaseHeight(new Vector3D(r * DMath.Cos(theta), y, r * DMath.Sin(theta))));
        }
        heights.Sort();
        return heights[Math.Clamp((int)(cover * SeaSamples), 0, SeaSamples - 1)];
    }
}
