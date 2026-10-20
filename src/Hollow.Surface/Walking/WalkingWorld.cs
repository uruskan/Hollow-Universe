using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>Everything the walking-scale ground knows about a point (docs/WALKING_GROUND.md §8).</summary>
public readonly record struct WalkingSample(
    double Height,
    double Water,
    double Continent,
    double Plains,
    double Hills,
    double Tableland,
    double Mountains,
    double Rocky,
    double Roughness,
    double Wet,
    double Ice = 0)
{
    /// <summary>Whether the ground lies under water: the sea, a lake or a stream.</summary>
    public bool Wetted => Water > Height;

    /// <summary>The relief type with the largest weight here.</summary>
    public Relief Relief
    {
        get
        {
            Relief best = Relief.Plains;
            double w = Plains;
            if (Hills > w) (best, w) = (Relief.Hills, Hills);
            if (Tableland > w) (best, w) = (Relief.Tableland, Tableland);
            if (Mountains > w) best = Relief.Mountains;
            return best;
        }
    }
}

/// <summary>
/// The ground of a small world at walking scale (docs/WALKING_GROUND.md, package S4 of SCALE.md): a pure function of a
/// seed, the world's game radius and its real sea cover, ice cover and mean temperature, built for a world of a few
/// kilometres, where today's <see cref="PlanetSurface"/> is built for thousands.
/// <para>In layers: landmasses from a warped field calibrated so the sea covers the real share; relief provinces
/// (plains, hills, tablelands in terraces with cliffs and canyons, ridged mountains) blended across their borders;
/// coasts sandy or rocky; metre-scale ground; then the drainage (<see cref="Drainage"/>) cuts streams, rivers and
/// valleys from their sources to the sea or a lake, and fills hollows with lakes. The climate reads heights at their
/// real equivalent, so the highest peaks carry snow; the biome table and the scatter are the planet's, with woods in
/// stands, clearings and open plains.</para>
/// <para>Deterministic (ARCHITECTURE.md §8): <see cref="DMath"/>, <see cref="Hash64"/>, <see cref="Rng"/> and the
/// surface's gradient noise, so the server's collision and every client's ground agree to the bit.</para>
/// </summary>
public sealed partial class WalkingWorld : ISurface, IScatterField
{
    /// <summary>The base generator's seed version; body-specific changes use WalkingParameters.TerrainRevision.</summary>
    public const int Version = 1;

    private const int CalibrationSamples = 8192;

    /// <summary>How far inside the continents' coast (landmass units) a pocket of sea no longer flattens the relief round it.</summary>
    private const double PocketMargin = 0.03;

    // Landmass field (relative to the coast) → height: sea floor, shore, coastal lowland. Monotone, 0 at the coast.
    private static readonly MonotoneCurve CoastCurve = new(
        new[] { -1.2, -0.5, -0.22, -0.09, -0.03, 0, 0.03, 0.1, 0.25, 0.55, 1.2 },
        new[] { -160.0, -70, -28, -9, -2.2, 0, 1.4, 3.5, 6.5, 10, 14 });

    private readonly ulong _seed;
    private readonly WalkingParameters _p;
    private readonly ReliefProvinces _provinces;
    private readonly double _threshold;
    private readonly double _continentsCoast;
    private readonly Drainage? _drainage;
    // A baked world's shape and lakes (WalkingParameters.Baked), and which of its lakes lie frozen.
    private readonly TerrainBake? _bake;
    private readonly bool[] _bakeFrozen = Array.Empty<bool>();
    private readonly double _meanTemperature;
    private readonly bool _hasSea;
    private readonly ScatterPlan _plan;
    private readonly Lazy<Vector3D> _landing;
    private readonly RidgedNoise _ridges;
    private readonly (double Low, double High) _bounds;

    // The token has no back-reference: a worker's last sample cannot retain an otherwise unused world. It is only
    // a reference-identity cache stamp and never participates in seeds or generated data.
    private readonly object _sampleCacheIdentity = new();
    // The last point asked for on this thread: drawing asks Sample, Cover and WaterLevelAt at the same point in a row.
    [ThreadStatic] private static object? _lastWorldIdentity;
    [ThreadStatic] private static Vector3D _lastDirection;
    [ThreadStatic] private static WalkingSample _lastSample;

    /// <summary>
    /// A walking-scale world of a game radius, from its real sea cover (0 for none), ice cover and annual mean
    /// temperature (K).
    /// </summary>
    public WalkingWorld(ulong seed, double radius, double oceanCover, double iceCover, double meanTemperature,
        WalkingParameters? parameters = null)
        : this(seed, radius, oceanCover, iceCover, meanTemperature, parameters, null, default) { }

    private WalkingWorld(ulong seed, double radius, double oceanCover, double iceCover, double meanTemperature,
        WalkingParameters? parameters, SelectedWalkingWorldPlan? selected, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        _p = parameters ?? WalkingParameters.Default;
        _seed = seed = LayoutSeed(seed, _p.LayoutVariant);
        oceanCover = _p.OceanCover ?? oceanCover;
        if (!double.IsFinite(oceanCover) || oceanCover < 0 || oceanCover > 1)
            throw new ArgumentOutOfRangeException(nameof(oceanCover));
        OceanCover = oceanCover;
        Radius = radius;
        _hasSea = oceanCover > 0.001;
        SeaRadius = _hasSea ? radius : 0;
        _meanTemperature = meanTemperature;
        _provinces = new ReliefProvinces(Hash64.Of(seed, 0x52454C46), radius, _p); // "RELF"
        _plan = ScatterPlan.BySize(radius, _p.ScatterTile);
        _landing = new Lazy<Vector3D>(FindLanding);
        _ridges = new RidgedNoise(_seed + 50, 7, 0.55, 0.25, _p.RidgeCrest);

        // The coast: the landmass field's level below which the real share of evenly spread directions lies. The
        // relief only rises inland of it, so the sea covers that share (a dry world keeps 2 % of basins).
        Vector3D[] directions = PlanetSurface.FibonacciSphere(CalibrationSamples);
        var values = new double[CalibrationSamples];
        for (int i = 0; i < CalibrationSamples; i++)
        {
            values[i] = Continents(Warp(directions[i]));
        }
        Array.Sort(values);
        _continentsCoast = Quantile(values, _hasSea ? Math.Clamp(oceanCover, 0, 0.98) : 0.02);
        for (int i = 0; i < CalibrationSamples; i++)
        {
            values[i] = Landmass(Warp(directions[i]));
        }
        Array.Sort(values);
        _threshold = Quantile(values, _hasSea ? Math.Clamp(oceanCover, 0, 0.98) : 0.02);

        if (_p.Baked)
        {
            // The relief, worn down by water and gravity once (docs/WORLD_TERRAIN_PLAN.md §4.1), or its cached bake.
            int level = TileKey.LevelFor(radius, _p.BakeCell, 12);
            _bake = TerrainBake.Get(TerrainBake.Key(_p, seed, radius, oceanCover), radius, level, () =>
                TerrainBake.Build(radius, level, _hasSea, _p, d =>
                {
                    WalkingSample s = Compute(d, carve: false, detail: false);
                    return new BakeInput(s.Height, s.Tableland, s.Mountains);
                }, Hash64.Of(seed, 0x42414B45))); // "BAKE"
            _bakeFrozen = new bool[_bake.Lakes.Count];
            for (int k = 0; k < _bakeFrozen.Length; k++)
            {
                WalkingLake lake = _bake.Lakes[k];
                _bakeFrozen[k] = MeanTemperature(lake.Centre, Math.Max(0, lake.Level)) < _p.FrozenLakeTemperature;
            }
        }
        else
        {
            _drainage = new Drainage(Hash64.Of(seed, 0x44524E47), radius, _p, d => // "DRNG"
            {
                WalkingSample s = Compute(d, carve: false, detail: false);
                return (s.Height, s.Mountains, s.Tableland);
            }, water: _hasSea);
            // Lakes whose water lies below freezing all year are ice to walk on.
            _drainage.Freeze(lake => MeanTemperature(lake.Centre, Math.Max(0, lake.Level)) < _p.FrozenLakeTemperature);
        }

        // The ice line: the temperature below which the real ice cover lies, at the ground's real-equivalent height.
        var temperatures = new double[CalibrationSamples];
        double low = 0, high = 0;
        for (int i = 0; i < CalibrationSamples; i++)
        {
            double h = HeightAt(directions[i]);
            low = Math.Min(low, h);
            high = Math.Max(high, h);
            temperatures[i] = MeanTemperature(directions[i], Math.Max(0, h));
        }
        // Drawn tiles' bounds before their heights are known: what the evenly spread directions found, with room for
        // the peaks and deeps between them.
        _bounds = (Math.Min(-200, low - 30), high + Math.Max(80, 0.3 * high));
        Array.Sort(temperatures);
        IceTemperature = iceCover > 0.001 ? Quantile(temperatures, iceCover) : -1000;
        cancellation.ThrowIfCancellationRequested();
        if (selected == null) _legacyCliffs = new CliffPlacements(this, seed, _p.CliffFormations);
        Monuments = new MonumentPlacements(this, seed);
        if (selected != null) _selected = BuildSelected(selected, cancellation);
        cancellation.ThrowIfCancellationRequested();
    }

    /// <summary>The seed of a body's walking-scale ground, from its zone's surface seed (<c>ZoneSpec.SurfaceSeed</c>).</summary>
    public static ulong SeedFor(ulong zoneSurfaceSeed) => Hash64.Of(zoneSurfaceSeed, 0x57414C4B, Version); // "WALK"

    /// <summary>Independent layout candidates; variant zero preserves the established generator seed.</summary>
    public static ulong LayoutSeed(ulong baseSeed, int variant)
    {
        if (variant < 0) throw new ArgumentOutOfRangeException(nameof(variant));
        return variant == 0 ? baseSeed : Hash64.Of(baseSeed, 0x4C41594F5554, (ulong)variant); // "LAYOUT"
    }

    /// <summary>The walking-scale ground of a body of the generated universe at a game radius.</summary>
    public static WalkingWorld For(StarSystem system, Body body, double radius, WalkingParameters? parameters = null)
    {
        PlanetState state = body.State ?? throw new ArgumentException("Only planets and moons have a surface.", nameof(body));
        parameters ??= WalkingParameters.ForBody(system.Address.Value, body.Path);
        bool sea = state.Liquid != LiquidKind.None && state.OceanCover > 0.001;
        return new WalkingWorld(SeedFor(Hash64.Of(system.Address.Value, body.Path)), radius, sea ? state.OceanCover : 0,
            state.IceCover, body.Temperature, parameters);
    }

    public double Radius { get; }

    /// <summary>The playable ocean target; lakes and streams are additional inland water.</summary>
    public double OceanCover { get; }

    /// <summary>The sea lies at height 0 (none on a dry world).</summary>
    public double SeaRadius { get; }

    /// <summary>Annual mean temperature (°C) below which ice lies all year.</summary>
    public double IceTemperature { get; }

    public WalkingParameters Parameters => _p;

    /// <summary>Shared scan formations, separate from harvestable natural-object IDs and ground-height generation.</summary>
    private readonly CliffPlacements? _legacyCliffs;
    /// <summary>Selected worlds require the sealed-field consumers; they never silently fall back to legacy cliffs.</summary>
    public CliffPlacements Cliffs => _legacyCliffs ??
        throw new NotSupportedException("Selected walking worlds require the sealed formation render/collision consumers.");
    public MonumentPlacements Monuments { get; }

    public TriangleMesh GroundCollision(in ZoneFrame frame, TileKey tile, double margin) =>
        _selected != null ? SelectedCollision(frame, tile, margin) : Monuments.AddCollision(Cliffs.Collision(frame, tile, margin), frame, tile, margin);

    /// <summary>The lakes, largest share of the world first by their place in the grid (a fixed order).</summary>
    public IReadOnlyList<WalkingLake> Lakes => _bake?.Lakes ?? _drainage?.Lakes ?? Array.Empty<WalkingLake>();

    /// <summary>A baked world's shape (WalkingParameters.Baked), or null.</summary>
    public TerrainBake? Bake => _bake;

    /// <summary>The drainage network's pieces: streams and rivers with water, and dry valleys.</summary>
    public IReadOnlyList<DrainageSegment> Drainage => _drainage?.Segments ?? Array.Empty<DrainageSegment>();

    internal Drainage? DrainageGrid => _drainage;

    public double HeightAt(Vector3D direction)
    {
        double ground = Fields(direction).Height;
        return Monuments == null ? ground : Monuments.HeightAt(direction, ground);
    }

    /// <summary>The lowest and highest ground, for drawing tiles before their heights are known.</summary>
    public (double Low, double High) HeightBounds => _bounds;

    /// <summary>Everything about a point: its ground, water, landmass, relief, coast, roughness, streams and ice.</summary>
    public WalkingSample Fields(Vector3D direction)
    {
        if (ReferenceEquals(_lastWorldIdentity, _sampleCacheIdentity) && _lastDirection.Equals(direction))
        {
            return _lastSample;
        }
        WalkingSample s = Compute(direction, carve: true, detail: true);
        _lastWorldIdentity = _sampleCacheIdentity;
        _lastDirection = direction;
        _lastSample = s;
        return s;
    }

    /// <summary>
    /// The water's surface over a point, metres above the sea: the sea (0) where the ground is below it, a lake's level
    /// over its hollow, a stream's surface in its channel; −∞ on dry ground. Water stands where it is above the ground.
    /// </summary>
    public double WaterLevelAt(Vector3D direction) => Fields(direction).Water;

    /// <summary>
    /// Where players land on this world (docs/WALKING_GROUND.md §11): a meadow 5 to 25 m above the sea at a forest's
    /// edge, near the coast and a stream, in the temperate belt (nearest 45° of latitude), off the mountains. Found once
    /// on the drainage grid, the same on every machine (a fixed order; ties go to the lower cell).
    /// </summary>
    public Vector3D LandingSite => _landing.Value;

    private Vector3D FindLanding()
    {
        // The drainage grid's cells, or on a baked world the tiles of about 30 m.
        const int BakedLevel = 6;
        int count;
        Func<int, Vector3D> centre;
        if (_drainage != null)
        {
            count = _drainage.CellCount;
            centre = _drainage.CellCentre;
        }
        else if (_bake != null)
        {
            count = (int)TileKey.CountAt(BakedLevel);
            centre = i => TileKey.FromIndex(BakedLevel, i)!.Value.Centre;
        }
        else
        {
            return Vector3D.UnitX;
        }
        int best = -1;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Vector3D d = centre(i);
            double latitude = Math.Abs(DMath.Asin(Math.Clamp(d.Y, -1, 1)) * DMath.RadToDeg);
            if (latitude < 30 || latitude > 60)
            {
                continue;
            }
            WalkingSample s = Fields(d);
            if (s.Wetted || s.Height < 5 || s.Height > 25 || s.Continent > 0.12 || s.Mountains > 0.2)
            {
                continue;
            }
            double cover = ForestCover(d, s);
            double score = (1 - Math.Abs(latitude - 45) / 15) + (1 - 2 * Math.Abs(cover - 0.35)) + s.Wet;
            if (score > bestScore)
            {
                (best, bestScore) = (i, score);
            }
        }
        return best >= 0 ? centre(best) : Vector3D.UnitX;
    }

    public SurfaceSample Sample(Vector3D direction)
    {
        WalkingSample s = Fields(direction);
        return new SurfaceSample(HeightAt(direction), s.Continent, s.Mountains, 1 - s.Rocky, s.Roughness);
    }

    // ---------------------------------------------------------------- height

    private Vector3D Warp(Vector3D p) =>
        (p + Noise.Vector(p, Radius, _p.WarpWavelength, 2, 0.5, _seed + 1, NoiseBasis.Corners) * _p.WarpReach).Normalized;

    // The continents' field, before the smaller ones (its first part).
    private double Continents(Vector3D q) =>
        (1 - _p.IslandShare) * Noise.Fbm(q, Radius, _p.ContinentWavelength, 4, 0.5, _seed + 2, NoiseBasis.Corners);

    // The landmass field: the continents', with the islands' and the coast's smaller fields over it. Those may cut into
    // the land only near the continents' coast: deep inland a hole of sea would be a crater at sea level among the hills.
    private double Landmass(Vector3D q)
    {
        double continents = Continents(q);
        double small = _p.IslandShare * Noise.Fbm(q, Radius, _p.IslandWavelength, 3, 0.5, _seed + 3, NoiseBasis.Corners)
            + _p.CoastDetail * Noise.Fbm(q, Radius, _p.CoastDetailWavelength, 3, 0.5, _seed + 7, NoiseBasis.Corners);
        return continents + (small < 0 ? small * DMath.SmoothStep(0.09, 0.0, continents - _continentsCoast) : small);
    }

    private WalkingSample Compute(Vector3D p, bool carve, bool detail)
    {
        Vector3D q = Warp(p);
        double c = Landmass(q) - _threshold;
        double h = CoastCurve.Evaluate(c);
        // The relief rises inland of the continents' coast, over any pocket of sea inside it (which it lifts out of the
        // sea where the land is high: no crater at sea level among the hills), and inland of the coast itself near it.
        double shore = Math.Max(c, Continents(q) - _continentsCoast - PocketMargin);
        double rocky = DMath.SmoothStep(-0.2, 0.25, Noise.Fbm(p, Radius, _p.CoastKindWavelength, 2, 0.5, _seed + 4, NoiseBasis.Corners));
        Span<double> w = stackalloc double[ReliefProvinces.TypeCount];
        // Offshore points skip Weights but still return all four relief fields.
        w.Clear();
        Span<double> a = stackalloc double[ReliefProvinces.TypeCount];
        Span<double> centre = stackalloc double[ReliefProvinces.TypeCount];
        double rough = 0;
        if (_bake != null && carve)
        {
            // A baked world's ground comes from its bake; the relief's fields still say what kind of ground it is.
            if (shore > 0)
            {
                _provinces.Weights(q, w, a, centre);
                rough = Roughness(p, w);
            }
            double baked = _bake.HeightAt(p);
            if (detail && shore > 0)
            {
                baked += (_p.GroundDetail + _p.StonyDetail * rough) * Noise.Fbm(p, Radius, 22, 3, 0.5, _seed + 6, NoiseBasis.Corners)
                    * DMath.SmoothStep(0, 0.02, shore);
            }
            return Baked(p, baked, c, rocky, rough, w);
        }
        if (shore > 0)
        {
            // Relief rises inland of the coast: within a few metres on rocky coasts (sea cliffs where the land is high),
            // over a hundred on sandy ones (a low shore and beaches).
            _provinces.Weights(q, w, a, centre);
            const double Faint = 1e-4;
            double relief = 0;
            if (w[(int)Relief.Plains] > Faint)
            {
                relief += w[(int)Relief.Plains] * a[(int)Relief.Plains] * Plains(p);
            }
            if (w[(int)Relief.Hills] > Faint)
            {
                relief += w[(int)Relief.Hills] * a[(int)Relief.Hills] * Hills(p);
            }
            if (w[(int)Relief.Tableland] > Faint)
            {
                relief += w[(int)Relief.Tableland] * a[(int)Relief.Tableland] * Tableland(p);
            }
            if (w[(int)Relief.Mountains] > Faint)
            {
                relief += w[(int)Relief.Mountains] * a[(int)Relief.Mountains] * Mountains(p, centre[(int)Relief.Mountains]);
            }
            // Sandy coasts rise slowly; rocky ones in a sea cliff (at most SeaCliffHeight), the rest of the land behind it.
            double slow = DMath.SmoothStep(0, _p.SandyShoreRamp, shore);
            // A baked world starts from a rocky coast's wider ramp; the bake wears it into its slope.
            double rockyRamp = _p.Baked ? _p.RockyShoreRamp * 4 : _p.RockyShoreRamp;
            double cliff = relief > 0 ? Math.Min(1, _p.SeaCliffHeight / relief) * DMath.SmoothStep(0, rockyRamp, shore) : 0;
            double inland = slow + (1 - slow) * rocky * cliff;
            h += inland * relief;
            rough = Roughness(p, w);
            if (detail)
            {
                // Metre-scale ground, rougher on stony ground; none at the waterline.
                h += (_p.GroundDetail + _p.StonyDetail * rough) * Noise.Fbm(p, Radius, 22, 3, 0.5, _seed + 6, NoiseBasis.Corners)
                    * DMath.SmoothStep(0, 0.02, shore);
            }
        }
        double water = _hasSea && h < 0 ? 0 : double.NegativeInfinity;
        double wet = 0;
        bool ice = false;
        if (carve && _drainage != null)
        {
            h = _drainage.Shape(p, h, out double inland, out wet, out ice);
            water = _hasSea && h < 0 ? Math.Max(0, inland) : inland;
        }
        return new WalkingSample(h, water, c, w[(int)Relief.Plains], w[(int)Relief.Hills], w[(int)Relief.Tableland],
            w[(int)Relief.Mountains], rocky, rough, wet, ice ? 1 : 0);
    }

    // How rough the ground is, 0 to 1: patches, more on mountains and tablelands.
    private double Roughness(Vector3D p, ReadOnlySpan<double> w) =>
        Math.Clamp(0.3 + 0.4 * Noise.Fbm(p, Radius, 1500, 2, 0.5, _seed + 5, NoiseBasis.Corners) + 0.7 * w[(int)Relief.Mountains]
            + 0.35 * w[(int)Relief.Tableland], 0, 1);

    // A baked world's sample: the bake's ground, the sea below 0, its lakes' water (ice on frozen ones), and wet ground
    // where much water drains.
    private WalkingSample Baked(Vector3D p, double h, double c, double rocky, double rough, ReadOnlySpan<double> w)
    {
        TerrainBake bake = _bake!;
        double water = _hasSea && h < 0 ? 0 : double.NegativeInfinity;
        bool ice = false;
        int lake = bake.LakeAt(p);
        if (lake >= 0)
        {
            double level = bake.Lakes[lake].Level;
            if (_bakeFrozen[lake])
            {
                if (h < level)
                {
                    h = level;
                    ice = true;
                }
            }
            else if (level > h)
            {
                water = Math.Max(water, level);
            }
        }
        double wet = DMath.SmoothStep(8_000, 120_000, bake.CatchmentAt(p));
        return new WalkingSample(h, water, c, w[(int)Relief.Plains], w[(int)Relief.Hills], w[(int)Relief.Tableland],
            w[(int)Relief.Mountains], rocky, rough, wet, ice ? 1 : 0);
    }

    /// <summary>The parts of a height, for finding faults in look-dev: base (uncarved), the mountain relief's ridge, massif and centrality.</summary>
    internal (double Base, double Ridge, double Massif, double Centrality, double Mountains) Parts(Vector3D p)
    {
        Vector3D q = Warp(p);
        Span<double> w = stackalloc double[ReliefProvinces.TypeCount];
        Span<double> a = stackalloc double[ReliefProvinces.TypeCount];
        Span<double> centre = stackalloc double[ReliefProvinces.TypeCount];
        _provinces.Weights(q, w, a, centre);
        Vector3D bent = (p + Noise.Vector(p, Radius, 650, 2, 0.5, _seed + 52, NoiseBasis.Corners) * (_p.RidgeWarp / Radius)).Normalized;
        double ridge = _ridges.At(bent, Radius, _p.RidgeWavelength, NoiseBasis.Corners);
        double massif = 0.75 + 0.35 * Noise.Fbm(p, Radius, 1900, 2, 0.5, _seed + 51, NoiseBasis.Corners);
        return (Compute(p, carve: false, detail: true).Height, ridge, massif, centre[(int)Relief.Mountains], Mountains(p, centre[(int)Relief.Mountains]));
    }

    // Low open country, with kettle hollows a few metres deep (lakes, once the drainage fills them).
    private double Plains(Vector3D p)
    {
        double swell = Noise.Fbm(p, Radius, 320, 3, 0.5, _seed + 20, NoiseBasis.Corners);
        double kettle = DMath.SmoothStep(0.2, 0.5, Noise.Fbm(p, Radius, 280, 2, 0.5, _seed + 21, NoiseBasis.Corners));
        return Math.Max(0.3, _p.PlainsHeight * (0.7 + 0.6 * swell) - _p.KettleDepth * kettle);
    }

    // Rolling hills: eroded noise, smooth slopes, detail on crests and flats.
    private double Hills(Vector3D p) =>
        _p.HillsHeight * Math.Max(0, 0.45 + 0.6 * Noise.Eroded(p, Radius, _p.HillsWavelength, 6, 0.5, _seed + 30, NoiseBasis.Corners));

    // Tablelands: a plateau whose edge is an escarpment (a cliff over its talus) with spurs and bays, and mesas and
    // buttes standing on it. The drainage cuts its canyons (Drainage: deeper downstream, steep walls).
    private double Tableland(Vector3D p)
    {
        // Few octaves: the outlines bend in spurs and bays without thin tongues, which would stand as walls.
        double plateau = Escarpment(Noise.Fbm(p, Radius, 1000, 3, 0.42, _seed + 40, NoiseBasis.Corners), -0.02, 0.018);
        double mesa = Escarpment(Noise.Fbm(p, Radius, 480, 2, 0.4, _seed + 43, NoiseBasis.Corners), 0.22, 0.014);
        double top = 4 + _p.PlateauHeight * plateau + _p.MesaHeight * mesa * plateau;
        top += 1.2 * Noise.Fbm(p, Radius, 90, 2, 0.5, _seed + 42, NoiseBasis.Corners);
        return Math.Max(0, top);
    }

    // An escarpment's profile across a field's level: the cliff (its share of the rise) over the field's narrow band,
    // the talus below it over a band four times as wide. 0 below, 1 on top.
    private double Escarpment(double field, double level, double band)
    {
        if (_p.Baked)
        {
            // A baked world starts from a wide ramp; its caprock and the bake's erosion make the escarpment.
            return DMath.SmoothStep(level - band * 4 * _p.BakeRamp, level + band * _p.BakeRamp, field);
        }
        double cliff = DMath.SmoothStep(level - band * _p.CliffBand, level + band * _p.CliffBand, field);
        double talus = DMath.SmoothStep(level - band * 4, level, field);
        return _p.CliffShare * cliff + (1 - _p.CliffShare) * talus;
    }

    // Mountains: eroded ridges (sharp crests, smooth valleys, branching spurs) rising toward the massif's middle. The
    // ridged noise folds along its lattice's planes, which at this scale would run straight for hundreds of metres, so
    // its domain is first warped by about a hundred metres.
    private double Mountains(Vector3D p, double centrality)
    {
        Vector3D bent = (p + Noise.Vector(p, Radius, 650, 2, 0.5, _seed + 52, NoiseBasis.Corners) * (_p.RidgeWarp / Radius)).Normalized;
        double ridge = Math.Clamp(_ridges.At(bent, Radius, _p.RidgeWavelength, NoiseBasis.Corners), 0, 1.15);
        double massif = 0.75 + 0.35 * Noise.Fbm(p, Radius, 1900, 2, 0.5, _seed + 51, NoiseBasis.Corners);
        double dome = 0.6 + 0.4 * centrality;
        double shape = ridge * (0.45 + 0.55 * ridge);
        // Eroded detail on the flanks, so they don't read as smooth facets between the crests.
        double flanks = _p.MountainDetail * Noise.Eroded(p, Radius, 330, 5, 0.5, _seed + 53, NoiseBasis.Corners);
        return _p.MountainHeight * massif * dome * Math.Max(0, 0.08 + 0.92 * shape + flanks);
    }

    // ---------------------------------------------------------------- climate

    /// <summary>Annual mean temperature (°C) at a direction and height (metres; the climate reads its real equivalent).</summary>
    public double MeanTemperature(Vector3D p, double height)
    {
        double sinLat = p.Y;
        double t = _meanTemperature - 273.15 + _p.EquatorToPole * (1.0 / 3 - sinLat * sinLat);
        t += _p.TemperaturePatches * Noise.Fbm(p, Radius, _p.ClimatePatchWavelength, 3, 0.55, _seed + 60, NoiseBasis.Corners);
        return t - _p.LapseRate * _p.RealHeightFactor * height;
    }

    /// <summary>The climate at a point: temperature, moisture (latitude bands, patches, dry interiors, wet valleys), tree line.</summary>
    public ClimateSample Climate(Vector3D p, in WalkingSample s)
    {
        double height = Math.Max(0, s.Height);
        double t = MeanTemperature(p, height);
        double latitude = DMath.Asin(Math.Clamp(p.Y, -1, 1)) * DMath.RadToDeg;
        double m = PlanetSurface.MoistureBand(Math.Abs(latitude));
        m += _p.MoisturePatches * Noise.Fbm(p, Radius, _p.ClimatePatchWavelength, 4, 0.6, _seed + 61, NoiseBasis.Corners);
        m -= 0.1 * DMath.SmoothStep(0.25, 0.8, s.Continent);
        m += 0.15 * s.Wet;
        m += 0.06 * DMath.SmoothStep(300, 1500, height * _p.RealHeightFactor);
        return new ClimateSample(t, Math.Clamp(m, 0, 1), DMath.SmoothStep(-1.5, 1.5, t));
    }

    /// <summary>Soft weights of the biomes at a point (<see cref="BiomeTable.Count"/>): the table's, at real-equivalent heights, and beaches on sandy coasts.</summary>
    public void BiomeWeights(in WalkingSample s, in ClimateSample climate, Span<double> weights)
    {
        if (_hasSea && s.Height < 0)
        {
            weights.Clear();
            weights[(int)Biome.Ocean] = 1;
            return;
        }
        if (s.Ice > 0)
        {
            weights.Clear();
            weights[(int)Biome.IceSheet] = 1; // a frozen lake
            return;
        }
        BiomeTable.Weights(climate, s.Height * _p.RealHeightFactor, IceTemperature, weights);
        if (_hasSea)
        {
            double sandy = 1 - s.Rocky;
            double top = 0.6 + 1.6 * sandy;
            double beach = DMath.SmoothStep(top + 0.8, top - 0.6, s.Height) * DMath.SmoothStep(0.15, 0.5, sandy)
                * DMath.SmoothStep(0.05, 0.015, s.Continent) * (1 - weights[(int)Biome.IceSheet]);
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

    public Biome BiomeAt(Vector3D direction)
    {
        WalkingSample s = Fields(direction);
        Span<double> w = stackalloc double[BiomeTable.Count];
        BiomeWeights(s, Climate(direction, s), w);
        return BiomeTable.Dominant(w);
    }

    // ---------------------------------------------------------------- plants

    /// <summary>
    /// How wooded a point is, 0 (open: meadow, plain, clearing) to 1 (closed stand): each relief type's share of woods,
    /// more along streams, in patches of stands and clearings.
    /// </summary>
    public double ForestCover(Vector3D p, in WalkingSample s)
    {
        double share = s.Plains * _p.PlainsWoods + s.Hills * _p.HillsWoods + s.Tableland * _p.TablelandWoods
            + s.Mountains * _p.MountainWoods;
        share += 0.3 * s.Wet * (1 - share);
        double n = Noise.Fbm(p, Radius, _p.WoodsWavelength, 4, 0.55, _seed + 70, NoiseBasis.Corners) + 0.35 * Noise.Fbm(p, Radius, 110, 2, 0.5, _seed + 71, NoiseBasis.Corners);
        double threshold = (0.5 - share) * 0.8;
        return DMath.SmoothStep(threshold - 0.08, threshold + 0.08, n);
    }

    /// <summary>Plant and rock densities at a point, per square metre: the biomes', in woods and open ground, thinned on slopes, at the tree line, on shores and in water.</summary>
    public VegetationSample Vegetation(Vector3D p, in WalkingSample s, in ClimateSample climate, ReadOnlySpan<double> biomes, double slope)
    {
        if (s.Water > s.Height - 0.2 || (_hasSea && s.Height < 0.3) || s.Ice > 0 || Monuments.VegetationAt(p) == 0)
        {
            return new VegetationSample(0, 0, 0, 0, 0);
        }
        double cover = ForestCover(p, s);
        double shore = _hasSea ? DMath.SmoothStep(0.5, 3, s.Height) : 1;
        double gentle = 1 - DMath.SmoothStep(0.45, 0.62, slope);
        double treeBiomes = BiomeTable.Trees(biomes);
        double trees = treeBiomes * _p.TreeDensityScale * cover * shore * gentle * climate.TreeLine;
        double bushes = BiomeTable.Bushes(biomes) * (0.3 + 0.7 * cover) * shore * gentle;
        double ferns = BiomeTable.Ferns(biomes) * (0.15 + 0.85 * cover) * shore * gentle;
        double rocks = BiomeTable.Rocks(biomes) * (0.5 + 2.5 * s.Roughness * s.Roughness) * (1 - 0.7 * DMath.SmoothStep(0.62, 0.8, slope))
            + 0.01 * DMath.SmoothStep(0.35, 0.6, slope);
        double meadow = biomes[(int)Biome.Grassland] + biomes[(int)Biome.TemperateForest] + 0.8 * biomes[(int)Biome.BorealForest]
            + 0.7 * biomes[(int)Biome.Savanna] + 0.6 * biomes[(int)Biome.Shrubland] + 0.5 * biomes[(int)Biome.Tundra]
            + 0.8 * biomes[(int)Biome.TropicalForest] + 0.3 * biomes[(int)Biome.Alpine];
        double grass = Math.Clamp(meadow * shore * gentle * (1 - 0.75 * cover), 0, 1);
        double edge = 0.35 + 2.6 * cover * (1 - cover);
        double redBerries = BiomeTable.RedBerries(biomes) * edge * shore * gentle;
        double blueBerries = BiomeTable.BlueBerries(biomes) * (0.5 + 0.5 * cover) * shore * gentle;
        return new VegetationSample(trees, bushes, ferns, rocks, grass, redBerries, blueBerries);
    }

    // ---------------------------------------------------------------- scatter

    ulong IScatterField.ScatterSeed => Hash64.Of(_seed, 0x53434154); // "SCAT"

    /// <summary>The scatter's tiles: the level nearest <see cref="WalkingParameters.ScatterTile"/> (5 on the first world), candidates spaced in metres.</summary>
    public ScatterPlan ScatterPlan => _plan;

    public bool AllowsScatter(Vector3D direction) => Monuments.VegetationAt(direction) > 0;

    public ScatterDensity Densities(Vector3D direction, in SurfaceSample sample, double slope)
    {
        WalkingSample s = Fields(direction);
        ClimateSample climate = Climate(direction, s);
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        BiomeWeights(s, climate, biomes);
        VegetationSample v = Vegetation(direction, s, climate, biomes, slope);
        Span<double> kinds = stackalloc double[4];
        BiomeTable.TreeKinds(biomes, kinds);
        double outcrops = s.Water > s.Height - 0.3 || Monuments.VegetationAt(direction) == 0 ? 0
            : Scatter.Outcrops(sample, biomes, slope) * Math.Max(0, _p.ResourceDensity);
        return new ScatterDensity(v, kinds, outcrops, s.Mountains);
    }

    public IReadOnlyList<ScatterItem> ScatterFor(TileKey tile) => Scatter.ForTile(this, tile);

    /// <summary>The trees alone of a scatter tile (far impostors), the same trees as the whole tile's.</summary>
    public IReadOnlyList<ScatterItem> ScatterFor(TileKey tile, bool treesOnly) => Scatter.ForTile(this, tile, treesOnly);

    /// <summary>The scatter's level for drawing (<see cref="ISurface.ScatterLevel"/>): the plan's, by size.</summary>
    public int ScatterLevel => _plan.Level;

    // ---------------------------------------------------------------- drawing

    /// <summary>
    /// What the ground looks like (docs/WALKING_GROUND.md §8): the biome table's materials through the shared rule, with
    /// sandstone cliffs on tablelands and in dry country; wet grass and mossy forest floor where it is wet or near a
    /// stream (mossy also where it is cold); wet sand along sandy shores and shingle at the foot of rocky ones; beds of
    /// shingle, wet sand and mud under water; ice on frozen lakes.
    /// </summary>
    public GroundCover Cover(Vector3D direction, in SurfaceSample sample, double slope, Span<double> biomes, Span<double> materials)
    {
        WalkingSample s = Fields(direction);
        ClimateSample climate = Climate(direction, s);
        BiomeWeights(s, climate, biomes);
        VegetationSample v = Vegetation(direction, s, climate, biomes, slope);
        if (s.Ice > 0)
        {
            materials.Clear();
            materials[(int)GroundMaterial.Ice] = 0.85;
            materials[(int)GroundMaterial.Snow] = 0.15;
            return new GroundCover(climate.Temperature, climate.Moisture, 0, 0);
        }
        if (s.Water > s.Height)
        {
            // A bed: the sea's sand and shingle by the coast's kind; lakes' and streams' shingle, wet sand and mud.
            materials.Clear();
            if (s.Height < 0 && s.Water <= 0)
            {
                materials[(int)GroundMaterial.WetSand] = 0.6 * (1 - s.Rocky) + 0.1;
                materials[(int)GroundMaterial.Shingle] = 0.5 * s.Rocky + 0.1;
                materials[(int)GroundMaterial.Sand] = 0.2 * (1 - s.Rocky);
                materials[(int)GroundMaterial.Rock] = 0.1 * s.Rocky;
            }
            else
            {
                materials[(int)GroundMaterial.Shingle] = 0.45;
                materials[(int)GroundMaterial.WetSand] = 0.25;
                materials[(int)GroundMaterial.Soil] = 0.3;
            }
            Normalise(materials);
            return new GroundCover(climate.Temperature, climate.Moisture, 0, 0);
        }
        double woods = ForestCover(direction, s) * climate.TreeLine * Math.Clamp(BiomeTable.Trees(biomes) / 0.02, 0, 1);
        double dry = biomes[(int)Biome.Desert] + biomes[(int)Biome.Savanna] + 0.7 * biomes[(int)Biome.Shrubland];
        double sandstone = Math.Clamp(1.4 * s.Tableland + dry, 0, 1);
        BiomeTable.GroundMaterials(biomes, slope, woods, climate.Temperature, s.Roughness, materials, sandstone);
        // Wet country, and the banks of streams: wet grass, mossy forest floor (mossy in the cold too).
        double wet = Math.Max(s.Wet, DMath.SmoothStep(0.6, 0.9, climate.Moisture));
        Move(materials, GroundMaterial.Grass, GroundMaterial.WetGrass, 0.85 * wet);
        Move(materials, GroundMaterial.ForestFloor, GroundMaterial.MossyFloor, 0.7 * Math.Max(wet, DMath.SmoothStep(4, -2, climate.Temperature)));
        if (_hasSea && s.Continent < 0.06)
        {
            // The sea's edge: the swash's wet sand on sandy shores, shingle at the foot of rocky ones.
            double edge = DMath.SmoothStep(0.06, 0.02, s.Continent);
            materials[(int)GroundMaterial.WetSand] += 1.2 * DMath.SmoothStep(1.4, 0.3, s.Height) * (1 - s.Rocky) * edge;
            materials[(int)GroundMaterial.Shingle] += DMath.SmoothStep(2.5, 0.5, s.Height) * s.Rocky * edge;
        }
        Normalise(materials);
        if (Monuments.At(direction) is { } site)
        {
            materials.Clear();
            materials[(int)(site.Definition.Ground == "gravel" ? GroundMaterial.Gravel : GroundMaterial.Soil)] = 1;
        }
        return new GroundCover(climate.Temperature, climate.Moisture, v.Grass, v.Trees);
    }

    private static void Move(Span<double> materials, GroundMaterial from, GroundMaterial to, double share)
    {
        double moved = materials[(int)from] * Math.Clamp(share, 0, 1);
        materials[(int)from] -= moved;
        materials[(int)to] += moved;
    }

    private static void Normalise(Span<double> materials)
    {
        double sum = 0;
        foreach (double m in materials)
        {
            sum += m;
        }
        if (sum > 0)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] /= sum;
            }
        }
    }

    private static double Quantile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Floor(fraction * sorted.Length), 0, sorted.Length - 1)];
}
