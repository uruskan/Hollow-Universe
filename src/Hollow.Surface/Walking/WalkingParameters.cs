using System.Collections.Generic;

namespace Hollow.Surface;

/// <summary>
/// The walking-scale ground's tuning numbers in one table (docs/WALKING_GROUND.md §10): lengths in metres on the
/// world's surface, heights in metres above the sea. <see cref="Default"/> is the first world's; a test or a look-dev
/// run may change one with <c>with { … }</c>. Every number is read the same way on every machine (+ − × ÷ only), so a
/// changed table is a changed world, never a different one per machine.
/// </summary>
public sealed record WalkingParameters
{
    public static WalkingParameters Default { get; } = new();

    /// <summary>
    /// Earth revision 10: layout variant 5, with corrected ice/alpine biome allocation and generated populations (5),
    /// cliff formations traced along their faces (6–9), and since 10 its shape baked and eroded (docs/WORLD_TERRAIN_PLAN.md
    /// §4.1, iteration 1), with T2's cliff formations off until the cliff kit replaces them.
    /// </summary>
    public static WalkingParameters Earth { get; } = new()
    {
        TerrainRevision = 10,
        LayoutVariant = 5,
        OceanCover = 0.35,
        ContinentWavelength = 3200,
        IslandShare = 0.12,
        CoastDetail = 0.02,
        Baked = true,
        CliffFormations = false,
        KitCliffs = true,
    };

    private static readonly IReadOnlyDictionary<(ulong System, uint Body), WalkingParameters> Profiles =
        new Dictionary<(ulong, uint), WalkingParameters>
        {
            [(StartingWorld.System.Value, StartingWorld.BodyPath)] = Earth,
        };

    /// <summary>Shared body-specific tuning; addresses, never display names, select a profile.</summary>
    public static WalkingParameters ForBody(ulong systemAddress, uint bodyPath) =>
        Profiles.TryGetValue((systemAddress, bodyPath), out WalkingParameters? profile) ? profile : Default;

    /// <summary>
    /// Save compatibility includes heights, generated solids and populations. Revision 1 fences CliffPlacements
    /// generation 2; revision 2 corrects biome allocation; revisions 3, 4, 5 and 6 fence CliffPlacements generations 3,
    /// 4, 5 and 6 (faces traced, T2; 6, the walls' rock skins). Zero is legacy walking or stand-in ground.
    /// </summary>
    public int TerrainRevision { get; init; } = 6;

    /// <summary>Optional deterministic geography variation. Zero preserves the existing world exactly.
    /// Earth candidates set this explicitly; selecting one for production also requires a new terrain/save revision.</summary>
    public int LayoutVariant { get; init; }

    /// <summary>Optional playable ocean target, independent of the body's physical reference metadata.</summary>
    public double? OceanCover { get; init; }

    /// <summary>Small original monuments per candidate cell; tune world dressing without changing galaxy seeds.</summary>
    public double MonumentSpacing { get; init; } = 300;
    public double MonumentChance { get; init; } = 0.7;

    /// <summary>Harvestable outcrops relative to the inherited sparse planet scatter. Provisional gameplay balance.</summary>
    public double ResourceDensity { get; init; } = 2.2;

    // ------------------------------------------------------------ landmasses (§2)

    /// <summary>Wavelength of the landmass field's first octave: a few continents on a 2.4 km world.</summary>
    public double ContinentWavelength { get; init; } = 2600;

    /// <summary>Wavelength of the second landmass field, which breaks coasts into capes, bays and islands.</summary>
    public double IslandWavelength { get; init; } = 720;

    /// <summary>The second field's share of the landmass field.</summary>
    public double IslandShare { get; init; } = 0.32;

    /// <summary>A third landmass field, small: the coast's wiggles at walking scale (coves, points, skerries).</summary>
    public double CoastDetail { get; init; } = 0.035;

    public double CoastDetailWavelength { get; init; } = 210;

    /// <summary>The warp that bends coasts and province borders: its wavelength, and its reach in radians of the world.</summary>
    public double WarpWavelength { get; init; } = 1500;

    public double WarpReach { get; init; } = 0.09;

    /// <summary>Wavelength of the coast-kind field: sandy shores and beaches against rocky shores and sea cliffs.</summary>
    public double CoastKindWavelength { get; init; } = 950;

    /// <summary>How far inland the relief starts, in landmass units: on sandy coasts (a wide low shore) and rocky ones (cliffs).</summary>
    public double SandyShoreRamp { get; init; } = 0.1;

    public double RockyShoreRamp { get; init; } = 0.012;

    /// <summary>The highest a rocky coast rises straight from the sea (m); the rest of the land's height comes further inland.</summary>
    public double SeaCliffHeight { get; init; } = 26;

    // ------------------------------------------------------------ relief provinces (§3)

    /// <summary>Provinces over the whole sphere (about 0.8 km² each on the first world; about half lie under the sea).</summary>
    public int Provinces { get; init; } = 96;

    /// <summary>Metres across which one province's relief blends into its neighbour's.</summary>
    public double ProvinceBlend { get; init; } = 300;

    /// <summary>Wavelength of the uplift field that groups mountains and tablelands into ranges and plains into lowlands.</summary>
    public double UpliftWavelength { get; init; } = 3000;

    /// <summary>The relief types' shares before the uplift field weighs in: plains, hills, tablelands, mountains.</summary>
    public double PlainsShare { get; init; } = 0.30;

    public double HillsShare { get; init; } = 0.34;

    public double TablelandShare { get; init; } = 0.16;

    public double MountainShare { get; init; } = 0.20;

    /// <summary>Plains: their height above the shore's, and the depth of their kettle hollows (which hold lakes).</summary>
    public double PlainsHeight { get; init; } = 7;

    public double KettleDepth { get; init; } = 4.5;

    /// <summary>Hills: their typical height and wavelength.</summary>
    public double HillsHeight { get; init; } = 44;

    public double HillsWavelength { get; init; } = 460;

    /// <summary>
    /// Tablelands: the plateau's height above the lowland around it and the mesas' on top of it. Each rise is an
    /// escarpment: a cliff over a talus slope. Their canyons are cut by the drainage (§4).
    /// </summary>
    public double PlateauHeight { get; init; } = 52;

    public double MesaHeight { get; init; } = 30;

    /// <summary>How much of an escarpment is cliff (the rest is the talus below it).</summary>
    public double CliffShare { get; init; } = 0.75;

    /// <summary>
    /// The width of an escarpment's cliff across its field's band, either side of its level (the talus spans four
    /// bands). At 0.3 a tableland's cliff rises tens of metres within a few, a wall the heightfield draws as a scalloped
    /// sheet; wider keeps it a steep slope the cliff scans can dress (LANDFORM_SURVEY.md, the proposed shape change).
    /// </summary>
    public double CliffBand { get; init; } = 0.3;

    /// <summary>Mountains: the highest peaks' height and the ridges' wavelength.</summary>
    public double MountainHeight { get; init; } = 400;

    public double RidgeWavelength { get; init; } = 1250;

    /// <summary>Eroded detail on the mountains' flanks, as a share of their height.</summary>
    public double MountainDetail { get; init; } = 0.12;

    /// <summary>How round the mountains' largest crests are (the width of a smooth |n|; each smaller octave's is half).</summary>
    public double RidgeCrest { get; init; } = 0.14;

    /// <summary>How far (m) the ridges' domain is bent, so no ridgeline runs straight.</summary>
    public double RidgeWarp { get; init; } = 110;

    /// <summary>Metre-scale ground: its height on smooth and on stony ground.</summary>
    public double GroundDetail { get; init; } = 0.3;

    public double StonyDetail { get; init; } = 1.2;

    // ------------------------------------------------------------ water (§4)

    /// <summary>The drainage grid's cell, in metres: rivers, valleys and lakes are found on it.</summary>
    public double DrainageCell { get; init; } = 30;

    /// <summary>Catchment in m² above which water carves a dry valley, and above which it runs as a stream.</summary>
    public double ValleyCatchment { get; init; } = 40_000;

    public double StreamCatchment { get; init; } = 260_000;

    /// <summary>A hollow holds a lake if it is at least this deep (m) and this large (m²).</summary>
    public double LakeMinDepth { get; init; } = 2.5;

    public double LakeMinArea { get; init; } = 15_000;

    /// <summary>A lake whose water's mean temperature is below this (°C) lies frozen all year: ice to walk on.</summary>
    public double FrozenLakeTemperature { get; init; } = -3;

    /// <summary>The stream's half-width (m) at the stream catchment and how it grows with the root of the catchment.</summary>
    public double StreamHalfWidth { get; init; } = 3;

    public double StreamWidthGrowth { get; init; } = 2.2;

    /// <summary>How far a stream's water lies below its banks, and its depth (m), at the stream catchment.</summary>
    public double StreamDrop { get; init; } = 0.8;

    public double StreamDepth { get; init; } = 0.9;

    /// <summary>Half-width of the valley a stream carves (m) and how it grows; dry valleys' half-width.</summary>
    public double StreamValley { get; init; } = 45;

    public double ValleyGrowth { get; init; } = 22;

    public double DryValley { get; init; } = 26;

    /// <summary>
    /// The network's paths wander off the grid's eight directions by up to this share of a cell, and dry valleys fade out
    /// on mountains (whose ridged relief has valleys of its own) by this share.
    /// </summary>
    public double Meander { get; init; } = 0.35;

    public double RuggedValleyFade { get; init; } = 0.3;

    /// <summary>
    /// Canyons: on tablelands water cuts this deep (m) once its catchment reaches the canyon catchment (m²), less above,
    /// between walls this steep (rise over run); on mountains, gorges this deep between walls this steep.
    /// </summary>
    public double CanyonDepth { get; init; } = 28;

    public double CanyonCatchment { get; init; } = 250_000;

    public double CanyonWall { get; init; } = 2.6;

    public double GorgeDepth { get; init; } = 8;

    public double GorgeWall { get; init; } = 1.1;

    // ------------------------------------------------------------ the bake (docs/WORLD_TERRAIN_PLAN.md §4.1)

    /// <summary>
    /// Whether the ground's large shape is baked: the relief eroded by water and gravity on a grid (<see cref="TerrainBake"/>),
    /// with its lakes, instead of drawn straight from the relief with the drainage's channels cut into it.
    /// </summary>
    public bool Baked { get; init; }

    /// <summary>Whether T2's cliff scans and wall skins are placed; off on a baked world until the cliff kit replaces them.</summary>
    public bool CliffFormations { get; init; } = true;

    /// <summary>
    /// Whether the cliff kit is placed on a baked world (docs/WORLD_TERRAIN_PLAN.md §4.3, iteration 5): chains of pieces
    /// on steep ground, the ground fitted behind each.
    /// </summary>
    public bool KitCliffs { get; init; }

    /// <summary>The grade (rise over run) a seed needs (32°), and below which a chain ends in a slope end (24°).</summary>
    public double KitSeedSlope { get; init; } = 0.62;

    public double KitChainSlope { get; init; } = 0.45;

    /// <summary>Chains a 59 m cell may seed, and pieces in a chain at most.</summary>
    public int KitSeedsPerCell { get; init; } = 4;

    public int KitMaxChain { get; init; } = 8;

    /// <summary>How far a piece leans back into its slope (radians, about 7°).</summary>
    public double KitLeanBack { get; init; } = 0.12;

    /// <summary>The bake's cell, metres (Earth: level 8, about 7.4 m).</summary>
    public double BakeCell { get; init; } = 7;

    public int BakeSteps { get; init; } = 100;

    /// <summary>How much wider than its designed cliff an escarpment's ramp is made for the bake, which erodes its edge.</summary>
    public double BakeRamp { get; init; } = 2.5;

    /// <summary>Stream power per step on soft rock (K·dt), and the hardness of mountains and of caprock as shares of it.</summary>
    public double ErosionRate { get; init; } = 0.02;

    public double MountainHardness { get; init; } = 0.7;

    public double CaprockHardness { get; init; } = 0.12;

    /// <summary>On tablelands, rock above this height (m, varied by this much) is the hard caprock.</summary>
    public double CaprockBase { get; init; } = 36;

    public double CaprockVariation { get; init; } = 5;

    /// <summary>The steepest slope (rise over run) loose ground holds: soft ground (33°), mountains (40°), caprock (50°).</summary>
    public double TalusSlope { get; init; } = 0.65;

    public double MountainTalusSlope { get; init; } = 0.84;

    public double CaprockTalusSlope { get; init; } = 1.19;

    /// <summary>Sweeps of sliding per step.</summary>
    public int ThermalSweeps { get; init; } = 4;

    /// <summary>Roughness added to the land before the bake (m, at about 90 m), so water gathers into branching valleys.</summary>
    public double BakeRoughness { get; init; } = 2;

    /// <summary>Soil creep per step: how far each cell eases toward its neighbours (share of the difference).</summary>
    public double BakeCreep { get; init; } = 0.01;

    /// <summary>A baked hollow holds a lake if its water covers this much (m²) and stands this deep (m); smaller ones fill.</summary>
    public double BakeLakeMinArea { get; init; } = 2_500;

    public double BakeLakeMinDepth { get; init; } = 1;

    // ------------------------------------------------------------ climate (§5)

    /// <summary>
    /// Real-equivalent heights: the climate reads a game height times this factor, so the first world's few hundred
    /// metres carry the cold of the real planet's kilometres (SCALE.md §3.3).
    /// </summary>
    public double RealHeightFactor { get; init; } = 12;

    /// <summary>Temperature's fall with real-equivalent height (K per metre) and between the equator and the poles (K).</summary>
    public double LapseRate { get; init; } = 0.006;

    public double EquatorToPole { get; init; } = 42;

    /// <summary>Patches of warmer and colder (K) and of wetter and drier (moisture) ground, and their wavelength.</summary>
    public double TemperaturePatches { get; init; } = 3;

    public double MoisturePatches { get; init; } = 0.3;

    public double ClimatePatchWavelength { get; init; } = 1700;

    // ------------------------------------------------------------ plants (§6)

    /// <summary>
    /// The biome table's tree densities (BiomeTable: 300 a hectare in temperate forest) times this, in closed stands:
    /// about 135 a hectare in temperate forest.
    /// </summary>
    public double TreeDensityScale { get; init; } = 0.45;

    /// <summary>The share of each relief type covered by woods: plains, hills, tablelands, mountains.</summary>
    public double PlainsWoods { get; init; } = 0.18;

    public double HillsWoods { get; init; } = 0.55;

    public double TablelandWoods { get; init; } = 0.3;

    public double MountainWoods { get; init; } = 0.6;

    /// <summary>Wavelength of the woods' patches (stands, clearings and meadows).</summary>
    public double WoodsWavelength { get; init; } = 420;

    /// <summary>The scatter's tiles are about this size (docs/WHOLE_WORLD.md §3.2: level 5, 118 m on the first world).</summary>
    public double ScatterTile { get; init; } = 120;
}
