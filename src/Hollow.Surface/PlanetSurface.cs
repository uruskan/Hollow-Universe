using System;
using System.Collections.Generic;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>What the height function knows about a point, besides its height.</summary>
public readonly struct SurfaceSample
{
    public SurfaceSample(double height, double continent, double mountain, double beach, double roughness)
    {
        Height = height;
        Continent = continent;
        Mountain = mountain;
        Beach = beach;
        Roughness = roughness;
    }

    /// <summary>Height above <see cref="ISurface.Radius"/> (sea level), metres.</summary>
    public double Height { get; }

    /// <summary>How continental the crust is, relative to the coastline: below 0 sea floor, rising inland.</summary>
    public double Continent { get; }

    /// <summary>How mountainous, 0 (plains) to about 1 (the highest ranges).</summary>
    public double Mountain { get; }

    /// <summary>How sandy the coast is here, 0 (rocky) to 1 (wide beaches).</summary>
    public double Beach { get; }

    /// <summary>How rough the small-scale ground is, 0 (smooth) to 1 (stony).</summary>
    public double Roughness { get; }
}

/// <summary>Plant and rock densities at a point, per square metre (GROUND.md §4.6).</summary>
public readonly struct VegetationSample
{
    public VegetationSample(double trees, double bushes, double ferns, double rocks, double grass, double redBerries = 0, double blueBerries = 0)
    {
        Trees = trees;
        Bushes = bushes;
        Ferns = ferns;
        Rocks = rocks;
        Grass = grass;
        RedBerries = redBerries;
        BlueBerries = blueBerries;
    }

    public double Trees { get; }
    public double Bushes { get; }
    public double Ferns { get; }
    public double Rocks { get; }

    /// <summary>Red- and blue-berry bushes per square metre (food).</summary>
    public double RedBerries { get; }
    public double BlueBerries { get; }

    /// <summary>How thick the grass is, 0 (bare) to 1 (a full meadow).</summary>
    public double Grass { get; }
}

/// <summary>
/// The real surface of a rocky planet (GROUND.md §4.6): a pure function of (universe seed, system, body, planet
/// state, surface generator version) built with <see cref="DMath"/>, <see cref="Hash64"/> and noise only, so the
/// server's collision and every client's terrain agree to the bit (ARCHITECTURE.md §8).
/// </summary>
/// <remarks>
/// <para>Height is built in layers: continents and ocean basins from tectonic plates and a warped continental field,
/// calibrated so the sea covers <see cref="PlanetState.OceanCover"/>; boundary relief (collision ranges, coastal
/// ranges, trenches, arcs, ridges, rifts); mountains shaped by an eroded ridged noise inside the uplift; hills with
/// eroded valleys; metre-scale ground; and a coast curve that flattens sandy shores into beaches.</para>
/// <para>Climate comes from latitude, height and the planet's mean temperature, calibrated so ice covers
/// <see cref="PlanetState.IceCover"/>; moisture from latitude bands, distance inland and noise. A biome table turns
/// climate into ground materials and plant densities.</para>
/// </remarks>
public sealed class PlanetSurface : ISurface, IScatterField
{
    /// <summary>Quadtree level of the tiles collision is built from: the finest tiles the client draws.</summary>
    public const int CollisionLevel = 18;

    /// <summary>Cells per tile edge of every tile grid (render and collision).</summary>
    public const int TileCells = 32;

    private const double LapseRate = 0.006; // K per metre
    private const double EquatorToPole = 42; // K between the equator and the poles, annual mean
    private const int CalibrationSamples = 6144;

    private readonly ulong _seed;
    private readonly Plates _plates;
    private readonly double _tectonics; // 0 = no plates, 1 = plates
    private double _continentThreshold;
    private readonly double _meanTemperature; // K
    private readonly double _iceTemperature; // °C below which ice lies all year
    private readonly bool _hasSea;

    public PlanetSurface(ulong seed, double radius, PlanetState state, double meanTemperature)
    {
        _seed = seed;
        Radius = radius;
        State = state;
        _hasSea = state.Liquid != LiquidKind.None && state.OceanCover > 0.001;
        SeaRadius = _hasSea ? radius : 0;
        _tectonics = state.Tectonics == Tectonics.Plates ? 1 : 0.25;
        var rng = new Rng(Hash64.Of(seed, 0x4C41594F)); // "LAYO"
        int plateCount = 14 + rng.NextInt(7);
        _plates = new Plates(seed, plateCount, Math.Clamp(1 - state.OceanCover + 0.1, 0.2, 0.9), radius);
        _meanTemperature = meanTemperature;

        // Calibrate the coastline and the ice line on a fixed set of directions, so the sea covers the planet state's
        // ocean cover and ice its ice cover. Sorting doubles is exact, so this is deterministic too. The coastline's
        // threshold starts from the continental field alone, then moves until the full height (hills and ranges
        // raise islands from the shelves) puts the right share under the sea.
        var continents = new double[CalibrationSamples];
        var directions = FibonacciSphere(CalibrationSamples);
        Span<double> dots = stackalloc double[Plates.MaxPlates];
        for (int i = 0; i < CalibrationSamples; i++)
        {
            continents[i] = ContinentField(directions[i], out _, out _, dots);
        }
        double[] sorted = (double[])continents.Clone();
        Array.Sort(sorted);
        double ocean = _hasSea ? Math.Clamp(state.OceanCover, 0, 0.98) : 0.02;
        double target = Quantile(sorted, ocean);
        _continentThreshold = target - CoastContinent;
        for (int pass = 0; pass < 3; pass++)
        {
            int under = 0;
            for (int i = 0; i < CalibrationSamples; i++)
            {
                if (Sample(directions[i]).Height < 0)
                {
                    under++;
                }
            }
            double actual = (double)under / CalibrationSamples;
            _continentThreshold += target - Quantile(sorted, actual);
        }
        var temperatures = new double[CalibrationSamples];
        for (int i = 0; i < CalibrationSamples; i++)
        {
            temperatures[i] = MeanTemperature(directions[i], Math.Max(0, Sample(directions[i]).Height));
        }
        Array.Sort(temperatures);
        _iceTemperature = state.IceCover > 0.001 ? Quantile(temperatures, state.IceCover) : -1000;
    }

    private static double Quantile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Floor(fraction * sorted.Length), 0, sorted.Length - 1)];

    /// <summary>The surface of a body of the generated universe: seeded from the universe, the body's address and the surface generator version.</summary>
    public static PlanetSurface For(Hollow.Galaxy.Galaxy galaxy, StarSystem system, Body body)
    {
        if (body.State == null)
        {
            throw new ArgumentException("Only planets and moons have a surface.", nameof(body));
        }
        ulong seed = Hash64.Of(GeneratorVersion.SurfaceSeed(galaxy.Seed), system.Address.Value, body.Path);
        return new PlanetSurface(seed, body.Radius, body.State, body.Temperature);
    }

    public double Radius { get; }

    public double SeaRadius { get; }

    public PlanetState State { get; }

    /// <summary>The seed every stream of this surface derives from.</summary>
    public ulong Seed => _seed;

    /// <summary>Annual mean temperature (°C) below which ice lies all year.</summary>
    public double IceTemperature => _iceTemperature;

    public double HeightAt(Vector3D direction) => Sample(direction).Height;

    // ---------------------------------------------------------------- height

    // The continental field where the coastline lies (the height curve crosses 0 there).
    private const double CoastContinent = 0.045;

    // Height of the crust against the continental field relative to the threshold: abyssal plains, continental slope,
    // shelf, coast, lowlands and the higher interiors of large continents. Monotone cubic through these knots.
    internal static readonly MonotoneCurve CrustCurve = new(
        new[] { -1.2, -0.45, -0.22, -0.08, -0.035, -0.012, 0.02, CoastContinent, 0.07, 0.15, 0.3, 0.6, 1.0, 1.6 },
        new[] { -5400.0, -4800, -4150, -2700, -420, -130, -28, 0, 10, 40, 100, 220, 420, 760 });

    /// <summary>The height and the fields it was built from, at a unit direction.</summary>
    public SurfaceSample Sample(Vector3D p)
    {
        Span<double> dots = stackalloc double[Plates.MaxPlates];
        double field = ContinentField(p, out Vector3D q, out double best, dots);
        double c = field - _continentThreshold;
        double land = DMath.SmoothStep(CoastContinent - 0.12, CoastContinent + 0.1, c);

        // Boundary relief: mountain belts and rifts on land, trenches, arcs and ridges at sea. Ranges come and go
        // along each boundary. Its positive part is where mountains grow.
        double strength = (0.2 + 0.8 * DMath.SmoothStep(-0.4, 0.3, Noise.Fbm(q, Radius, 800_000, 3, 0.5, _seed + 11))) * _tectonics;
        _plates.Relief(q, dots[.._plates.Count], best, strength, land, out double relief, out double uplift);
        double trench = relief - uplift;

        // Old, worn ranges inside some plates: long ridges of a low-frequency ridged noise, in a few regions only.
        double region = DMath.SmoothStep(0.15, 0.45, Noise.Fbm(q, Radius, 2_200_000, 2, 0.5, _seed + 19));
        double intraplate = 0;
        if (region > 0)
        {
            double ridge = 1 - Math.Abs(Noise.Fbm(q, Radius, 1_000_000, 2, 0.4, _seed + 12));
            intraplate = 1800 * DMath.SmoothStep(0.86, 0.985, ridge) * land * region;
        }

        double mountainHeight = uplift + intraplate;
        double mountain = Math.Clamp(mountainHeight / 4000, 0, 1.2);
        double h = CrustCurve.Evaluate(c) + trench;
        if (mountainHeight > 1)
        {
            // Eroded ridges: sharp crests, smooth valley floors, spurs that branch like drainage.
            double shape = Swiss(p, 24_000, 10, 0.5, 0.18, _seed + 13);
            h += mountainHeight * Math.Clamp(0.04 + 0.96 * shape, 0, 1.5);
        }

        // Hills: mostly gentle country, hilly in some regions and around the mountains, over an eroded noise.
        double hillField = DMath.SmoothStep(-0.3, 0.5, Noise.Fbm(q, Radius, 1_200_000, 3, 0.5, _seed + 14));
        double hillAmplitude = (14 + 260 * hillField * hillField + 0.22 * Math.Min(mountainHeight, 3000)) * (0.3 + 0.7 * land);
        h += hillAmplitude * Eroded(p, 7_000, 8, 0.5, _seed + 15);

        // Ground: metre-scale bumps, rougher in the mountains and on stony ground.
        double rough = Math.Clamp(0.35 + 0.5 * Noise.Fbm(p, Radius, 9_000, 2, 0.5, _seed + 16) + 0.6 * mountain, 0, 1);
        h += (0.4 + 2.2 * rough) * Noise.Fbm(p, Radius, 48, 3, 0.5, _seed + 17);

        // Coasts: sandy shores flatten into beaches, rocky ones keep their slope.
        double beach = DMath.SmoothStep(-0.1, 0.45, Noise.Fbm(q, Radius, 60_000, 3, 0.5, _seed + 18)) * (1 - Math.Min(1, mountain * 2));
        if (h > -20 && h < 20)
        {
            double t = h / 7;
            h *= 1 - 0.78 * beach * DMath.Exp(-t * t);
        }
        return new SurfaceSample(h, c, mountain, beach, rough);
    }

    // The warped continental field (before the coastline threshold); also the warped direction, and the plates'
    // dot products with it for the relief.
    private double ContinentField(Vector3D p, out Vector3D q, out double best, Span<double> dots)
    {
        Vector3D warp = Noise.Vector(p, Radius, 3_000_000, 3, 0.5, _seed + 1) * 0.2
            + Noise.Vector(p, Radius, 700_000, 2, 0.5, _seed + 2) * 0.05;
        q = (p + warp).Normalized;
        // The plates' bias is smooth across boundaries; the warped noise breaks their outlines.
        best = _plates.Dots(q, dots);
        double bias = _plates.Bias(dots[.._plates.Count], best);
        double noise = Noise.Fbm(q, Radius, 2_400_000, 4, 0.5, _seed + 3) + 0.3 * Noise.Fbm(p, Radius, 420_000, 5, 0.55, _seed + 4);
        return 0.7 * noise + 0.42 * bias * (0.4 + 0.6 * _tectonics);
    }

    // Eroded ridged noise and eroded-slope noise on this surface's sphere (Noise.Swiss and Noise.Eroded).
    private double Swiss(Vector3D p, double wavelength, int octaves, double gain, double warp, ulong seed) =>
        Noise.Swiss(p, Radius, wavelength, octaves, gain, warp, seed);

    private double Eroded(Vector3D p, double wavelength, int octaves, double gain, ulong seed) =>
        Noise.Eroded(p, Radius, wavelength, octaves, gain, seed);

    // ---------------------------------------------------------------- climate

    /// <summary>Annual mean temperature (°C) at a direction and height (metres; the sea surface for water).</summary>
    public double MeanTemperature(Vector3D p, double height)
    {
        double sinLat = p.Y;
        double t = _meanTemperature - 273.15 + EquatorToPole * (1.0 / 3 - sinLat * sinLat);
        t += 4 * Noise.Fbm(p, Radius, 1_500_000, 3, 0.5, _seed + 21);
        return t - LapseRate * height;
    }

    /// <summary>The climate at a point of the surface.</summary>
    public ClimateSample Climate(Vector3D p, in SurfaceSample s)
    {
        double height = Math.Max(0, s.Height);
        double t = MeanTemperature(p, height);
        double lat = DMath.Asin(Math.Clamp(p.Y, -1, 1)) * DMath.RadToDeg;
        double m = MoistureBand(Math.Abs(lat));
        m -= 0.28 * DMath.SmoothStep(0.1, 0.9, s.Continent);
        m += 0.34 * Noise.Fbm(p, Radius, 1_100_000, 4, 0.5, _seed + 22);
        m += 0.08 * DMath.SmoothStep(300, 1500, height); // rain on the mountains
        double treeLine = DMath.SmoothStep(-1.5, 1.5, t);
        return new ClimateSample(t, Math.Clamp(m, 0, 1), treeLine);
    }

    // Wet at the equator, dry in the subtropics, wet again in the mid-latitudes, dry and cold at the poles.
    internal static double MoistureBand(double absLatitude)
    {
        ReadOnlySpan<double> lat = stackalloc double[] { 0, 12, 26, 38, 52, 68, 90 };
        ReadOnlySpan<double> m = stackalloc double[] { 0.86, 0.66, 0.26, 0.56, 0.74, 0.56, 0.4 };
        for (int i = 1; i < lat.Length; i++)
        {
            if (absLatitude <= lat[i])
            {
                double t = (absLatitude - lat[i - 1]) / (lat[i] - lat[i - 1]);
                t = t * t * (3 - 2 * t);
                return m[i - 1] + (m[i] - m[i - 1]) * t;
            }
        }
        return m[^1];
    }

    /// <summary>Soft weights of the biomes at a point (<see cref="BiomeTable.Count"/> of them).</summary>
    public void BiomeWeights(Vector3D p, in SurfaceSample s, in ClimateSample climate, Span<double> weights)
    {
        if (_hasSea && s.Height < 0)
        {
            weights.Clear();
            weights[(int)Biome.Ocean] = 1;
            return;
        }
        BiomeTable.Weights(climate, s.Height, _iceTemperature, weights);
        // Sandy shores: a band of beach above the waterline.
        if (_hasSea)
        {
            double top = 1.2 + 4.0 * s.Beach;
            double beach = DMath.SmoothStep(top + 0.8, top - 0.6, s.Height) * DMath.SmoothStep(0.05, 0.3, s.Beach);
            beach *= 1 - weights[(int)Biome.IceSheet];
            if (beach > 0)
            {
                for (int b = 0; b < weights.Length; b++)
                {
                    weights[b] *= 1 - beach;
                }
                weights[(int)Biome.Beach] += beach;
            }
        }
    }

    /// <summary>The dominant biome at a direction.</summary>
    public Biome BiomeAt(Vector3D p)
    {
        SurfaceSample s = Sample(p);
        Span<double> w = stackalloc double[BiomeTable.Count];
        BiomeWeights(p, s, Climate(p, s), w);
        return BiomeTable.Dominant(w);
    }

    /// <summary>
    /// Ground material weights (<see cref="BiomeTable.MaterialCount"/>, adding up to 1) for a point with biome
    /// weights, slope (sine of the ground's tilt) and the local tree cover (0–1).
    /// </summary>
    public void Materials(in SurfaceSample s, in ClimateSample climate, ReadOnlySpan<double> biomes, double slope,
        double treeCover, Span<double> materials)
    {
        materials.Clear();
        BiomeTable.AddMaterials(biomes, materials);
        // Under trees the ground is litter and moss; in the open it is grass.
        double litter = Math.Min(materials[(int)GroundMaterial.ForestFloor], 1);
        double open = 1 - treeCover;
        materials[(int)GroundMaterial.Grass] += litter * open * 0.8;
        materials[(int)GroundMaterial.ForestFloor] = litter * (0.2 + 0.8 * treeCover);
        // Stony ground in rough places.
        double stony = 0.25 * s.Roughness * s.Roughness;
        materials[(int)GroundMaterial.Gravel] += stony;
        // Snow where it is cold enough to lie (beyond the permanent ice).
        double snow = DMath.SmoothStep(-1.0, -5.0, climate.Temperature);
        materials[(int)GroundMaterial.Snow] += 3 * snow;
        // Slopes: scree, then bare rock; snow slides off cliffs.
        double scree = DMath.SmoothStep(0.42, 0.6, slope);
        double cliff = DMath.SmoothStep(0.58, 0.74, slope);
        double sum = 0;
        for (int i = 0; i < materials.Length; i++)
        {
            sum += materials[i];
        }
        if (sum <= 0)
        {
            materials[(int)GroundMaterial.Soil] = 1;
            sum = 1;
        }
        double keep = (1 - cliff) * (1 - 0.6 * scree) / sum;
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] *= keep;
        }
        materials[(int)GroundMaterial.Gravel] += (1 - cliff) * 0.6 * scree;
        materials[(int)GroundMaterial.Rock] += cliff;
    }

    /// <summary>
    /// Plant and rock densities at a point: the biome's, thinned by clearings, steep ground, the tree line and the
    /// shore. <paramref name="slope"/> is the sine of the ground's tilt.
    /// </summary>
    public VegetationSample Vegetation(Vector3D p, in SurfaceSample s, in ClimateSample climate, ReadOnlySpan<double> biomes, double slope)
    {
        if (s.Height < 0.3 && _hasSea)
        {
            return new VegetationSample(0, 0, 0, 0, 0);
        }
        double clearing = ForestPatches(p);
        double shore = _hasSea ? DMath.SmoothStep(1.0, 6.0, s.Height) : 1;
        double gentle = 1 - DMath.SmoothStep(0.45, 0.62, slope);
        double trees = BiomeTable.Trees(biomes) * clearing * shore * gentle * climate.TreeLine;
        double bushes = BiomeTable.Bushes(biomes) * (0.35 + 0.65 * clearing) * shore * gentle;
        double ferns = BiomeTable.Ferns(biomes) * (0.2 + 0.8 * clearing) * shore * gentle;
        double rocks = BiomeTable.Rocks(biomes) * (0.5 + 2.5 * s.Roughness * s.Roughness) * (1 - 0.7 * DMath.SmoothStep(0.62, 0.8, slope));
        double meadow = biomes[(int)Biome.Grassland] + biomes[(int)Biome.TemperateForest] + 0.8 * biomes[(int)Biome.BorealForest]
            + 0.7 * biomes[(int)Biome.Savanna] + 0.6 * biomes[(int)Biome.Shrubland] + 0.5 * biomes[(int)Biome.Tundra]
            + 0.8 * biomes[(int)Biome.TropicalForest] + 0.3 * biomes[(int)Biome.Alpine];
        double grass = Math.Clamp(meadow * shore * gentle * (1 - 0.6 * clearing * BiomeTable.Trees(biomes) / 0.03), 0, 1);
        // Berries like the light at forest edges and in clearings, not deep shade or open grass.
        double edge = 0.35 + 2.6 * clearing * (1 - clearing);
        double redBerries = BiomeTable.RedBerries(biomes) * edge * shore * gentle;
        double blueBerries = BiomeTable.BlueBerries(biomes) * (0.5 + 0.5 * clearing) * shore * gentle;
        return new VegetationSample(trees, bushes, ferns, rocks, grass, redBerries, blueBerries);
    }

    /// <summary>Forest cover from patches and clearings: 0 (open meadow) to 1 (closed forest).</summary>
    public double ForestPatches(Vector3D p)
    {
        double n = Noise.Fbm(p, Radius, 1_100, 4, 0.55, _seed + 31) + 0.35 * Noise.Fbm(p, Radius, 9_000, 2, 0.5, _seed + 32);
        return DMath.SmoothStep(-0.32, 0.12, n);
    }

    // ---------------------------------------------------------------- collision

    // A zone asks for a cube-sphere tile's ground (ISurface's default grid, docs/WHOLE_WORLD.md §6.1). Until S4 makes this
    // surface at a game radius no zone stands on it (a centred frame can't hold 6,435 km in floats), so the patch of a
    // tangent plane built from the level-18 grids remains for the look-dev tools and their tests.

    /// <summary>The ground of a square of a tangent frame's plane, from the level-18 grids (look-dev, tests).</summary>
    public PooledTriangles PatchCollisionPooled(in ZoneFrame frame, double centreX, double centreZ, double size) =>
        TileMesh.CollisionPooled(this, frame, centreX, centreZ, size, CollisionLevel, TileCells, _collisionHeights);

    // The collision grids' heights, shared by neighbouring patches (GROUND.md §5.11): 4,096 grids, 36 MB at most.
    private readonly GridHeights _collisionHeights = new(4096);

    public TriangleMesh PatchCollision(in ZoneFrame frame, double centreX, double centreZ, double size) =>
        TileMesh.Collision(this, frame, centreX, centreZ, size, CollisionLevel, TileCells);

    public IReadOnlyList<ScatterItem> ScatterFor(TileKey tile) => Scatter.ForTile(this, tile);

    // ---------------------------------------------------------------- scatter

    ulong IScatterField.ScatterSeed => _seed;

    ScatterPlan IScatterField.ScatterPlan => ScatterPlan.Planet;

    /// <summary>What the scatter places at a point: the climate's biomes, their plants thinned by clearings and slope, outcrops.</summary>
    public ScatterDensity Densities(Vector3D direction, in SurfaceSample sample, double slope)
    {
        ClimateSample climate = Climate(direction, sample);
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        BiomeWeights(direction, sample, climate, biomes);
        VegetationSample v = Vegetation(direction, sample, climate, biomes, slope);
        Span<double> kinds = stackalloc double[4];
        BiomeTable.TreeKinds(biomes, kinds);
        return new ScatterDensity(v, kinds, Scatter.Outcrops(sample, biomes, slope), sample.Mountain);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Evenly spread unit directions (a Fibonacci lattice), deterministic.</summary>
    public static Vector3D[] FibonacciSphere(int count)
    {
        var result = new Vector3D[count];
        const double Golden = 2.399963229728653; // π (3 − √5)
        for (int i = 0; i < count; i++)
        {
            double y = 1 - (i + 0.5) * 2.0 / count;
            double r = Math.Sqrt(Math.Max(0, 1 - y * y));
            double a = Golden * i;
            result[i] = new Vector3D(r * DMath.Cos(a), y, r * DMath.Sin(a));
        }
        return result;
    }
}

/// <summary>A monotone cubic through knots (Fritsch–Carlson): smooth, and never overshoots between knots.</summary>
internal sealed class MonotoneCurve
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _m;

    public MonotoneCurve(double[] x, double[] y)
    {
        _x = x;
        _y = y;
        int n = x.Length;
        var delta = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            delta[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]);
        }
        _m = new double[n];
        _m[0] = delta[0];
        _m[n - 1] = delta[n - 2];
        for (int i = 1; i < n - 1; i++)
        {
            _m[i] = delta[i - 1] * delta[i] <= 0 ? 0 : (delta[i - 1] + delta[i]) / 2;
        }
        for (int i = 0; i < n - 1; i++)
        {
            if (delta[i] == 0)
            {
                _m[i] = 0;
                _m[i + 1] = 0;
                continue;
            }
            double a = _m[i] / delta[i], b = _m[i + 1] / delta[i];
            double s = a * a + b * b;
            if (s > 9)
            {
                double t = 3 / Math.Sqrt(s);
                _m[i] = t * a * delta[i];
                _m[i + 1] = t * b * delta[i];
            }
        }
    }

    public double Evaluate(double x)
    {
        int n = _x.Length;
        if (x <= _x[0])
        {
            return _y[0] + _m[0] * (x - _x[0]);
        }
        if (x >= _x[n - 1])
        {
            return _y[n - 1] + _m[n - 1] * (x - _x[n - 1]);
        }
        int i = 0;
        while (x > _x[i + 1])
        {
            i++;
        }
        double h = _x[i + 1] - _x[i];
        double t = (x - _x[i]) / h, t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * _y[i] + (t3 - 2 * t2 + t) * h * _m[i] + (-2 * t3 + 3 * t2) * _y[i + 1] + (t3 - t2) * h * _m[i + 1];
    }
}
