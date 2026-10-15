using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Land biomes (GROUND.md §4.6), picked from the climate: annual mean temperature, moisture and height.</summary>
public enum Biome : byte
{
    Ocean = 0,
    Beach = 1,
    IceSheet = 2,
    Tundra = 3,
    BorealForest = 4,
    TemperateForest = 5,
    Grassland = 6,
    Shrubland = 7,
    Desert = 8,
    Savanna = 9,
    TropicalForest = 10,
    Alpine = 11,
}

/// <summary>
/// Ground materials the terrain is painted with; weights per vertex, sixteen in all, each a scanned 4K layer (GROUND.md
/// §9, docs/WALKING_GROUND.md §8). The last eight (S4c) each belong to one of the first eight's families
/// (<see cref="BiomeTable.Family"/>), which is what footsteps, effects and the grass read. Append only.
/// </summary>
public enum GroundMaterial : byte
{
    Grass = 0,
    ForestFloor = 1,
    Soil = 2,
    Rock = 3,
    Gravel = 4,
    Sand = 5,
    Snow = 6,
    Moss = 7,

    /// <summary>Wet sand: the swash, the waterline and below it (a sand).</summary>
    WetSand = 8,

    /// <summary>Shingle: pebbles and gravel of rocky coves and stream beds (a gravel).</summary>
    Shingle = 9,

    /// <summary>Wet grass: stream banks, wet valleys, rainy country (a grass).</summary>
    WetGrass = 10,

    /// <summary>Mossy forest floor: wet and cool woods (a forest floor).</summary>
    MossyFloor = 11,

    /// <summary>Tundra: mossy, stony ground of the cold (a moss).</summary>
    Tundra = 12,

    /// <summary>Sandstone: tablelands' escarpments, canyons and dry country's cliffs (a rock).</summary>
    Sandstone = 13,

    /// <summary>Dry earth: the coarse, stony dirt of deserts, savanna and shrubland (a soil).</summary>
    DryEarth = 14,

    /// <summary>Ice: frozen lakes and the ice sheets' bare ice (a snow).</summary>
    Ice = 15,
}

/// <summary>Kinds of natural objects the scatter places (GROUND.md §4.6).</summary>
public enum ScatterKind : byte
{
    /// <summary>Spruce and fir: dark, narrow conifers.</summary>
    Fir = 0,

    /// <summary>Pines: tall, open crowns.</summary>
    Pine = 1,

    /// <summary>Birches: light bark, small leaves.</summary>
    Birch = 2,

    /// <summary>Broad-leaved trees.</summary>
    Broadleaf = 3,

    /// <summary>Shrubs and saplings.</summary>
    Bush = 4,

    /// <summary>Ferns and low plants.</summary>
    Fern = 5,

    /// <summary>Stones and boulders lying on the ground.</summary>
    Rock = 6,

    /// <summary>Bushes with red berries (food).</summary>
    BerryRed = 7,

    /// <summary>Bushes with blue berries (food).</summary>
    BerryBlue = 8,

    // Gameplay kinds (GROUND.md §6.1): placed by the scatter like the plants, drawn by the ground game, not by the
    // surface view. New kinds only ever go at the end; existing numbers never change (they are part of every ID).

    /// <summary>A stone outcrop (broken for stone).</summary>
    StoneOutcrop = 9,

    /// <summary>A metal-ore outcrop.</summary>
    MetalOutcrop = 10,

    /// <summary>A sulphur-ore outcrop.</summary>
    SulphurOutcrop = 11,

    /// <summary>A rarer concentrated high-grade metal outcrop, in its own stable candidate stream.</summary>
    HighGradeOutcrop = 12,
}

/// <summary>The climate at a point: what the biome, materials and plants are picked from.</summary>
public readonly struct ClimateSample
{
    public ClimateSample(double temperature, double moisture, double treeLine)
    {
        Temperature = temperature;
        Moisture = moisture;
        TreeLine = treeLine;
    }

    /// <summary>Annual mean temperature at this height, °C.</summary>
    public double Temperature { get; }

    /// <summary>How wet the climate is, 0 (desert) to 1 (rainforest).</summary>
    public double Moisture { get; }

    /// <summary>Above 0 where trees can grow (0–1, falling off at the tree line).</summary>
    public double TreeLine { get; }
}

/// <summary>
/// The biome table: soft weights of the biomes from the climate (a Whittaker diagram with Gaussian cells), and per
/// biome the ground materials, the density of trees, bushes, ferns and rocks, and which trees grow there.
/// </summary>
public static class BiomeTable
{
    public const int Count = 12;
    public const int MaterialCount = 16;

    /// <summary>The family each material belongs to (one of the first eight), in <see cref="GroundMaterial"/> order.</summary>
    private static readonly GroundMaterial[] Families =
    {
        GroundMaterial.Grass, GroundMaterial.ForestFloor, GroundMaterial.Soil, GroundMaterial.Rock,
        GroundMaterial.Gravel, GroundMaterial.Sand, GroundMaterial.Snow, GroundMaterial.Moss,
        GroundMaterial.Sand, GroundMaterial.Gravel, GroundMaterial.Grass, GroundMaterial.ForestFloor,
        GroundMaterial.Moss, GroundMaterial.Rock, GroundMaterial.Soil, GroundMaterial.Snow,
    };

    /// <summary>The material's family: itself for the first eight, the one it looks and sounds like for the others.</summary>
    public static GroundMaterial Family(GroundMaterial material) => Families[(int)material];

    /// <summary>Adds every material's weight into its family's (the first eight), leaving the others at 0.</summary>
    public static void FoldFamilies(Span<double> materials)
    {
        for (int i = 8; i < MaterialCount; i++)
        {
            materials[(int)Families[i]] += materials[i];
            materials[i] = 0;
        }
    }

    // Centre of each land biome in (temperature °C, moisture); Ocean, Beach, IceSheet and Alpine are placed by rules.
    private static readonly (double T, double M)[] Centres =
    {
        (0, 0), (0, 0), (0, 0),
        (-7, 0.40),  // tundra
        (2, 0.62),   // boreal forest
        (10, 0.68),  // temperate forest
        (12, 0.40),  // grassland
        (18, 0.27),  // shrubland
        (24, 0.06),  // desert
        (25, 0.38),  // savanna
        (26, 0.80),  // tropical forest
        (0, 0),
    };

    private const double SigmaT = 6.5;
    private const double SigmaM = 0.16;

    /// <summary>Ground materials of each biome on gentle ground, in <see cref="GroundMaterial"/> order.</summary>
    private static readonly float[][] Materials =
    {
        //         grass forest soil  rock gravel sand  snow  moss | wet sand shingle wet grass mossy tundra sandstone dry earth ice
        new[] { 0f, 0f, 0f, 0.1f, 0.2f, 0.3f, 0f, 0f, 0.3f, 0.1f, 0f, 0f, 0f, 0f, 0f, 0f },              // ocean floor
        new[] { 0f, 0f, 0f, 0f, 0.05f, 0.9f, 0f, 0f, 0f, 0.05f, 0f, 0f, 0f, 0f, 0f, 0f },                 // beach
        new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0.8f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.2f },                     // ice sheet
        new[] { 0.1f, 0f, 0f, 0.05f, 0.15f, 0f, 0f, 0.15f, 0f, 0f, 0f, 0f, 0.55f, 0f, 0f, 0f },           // tundra
        new[] { 0.15f, 0.4f, 0f, 0f, 0.05f, 0f, 0f, 0.1f, 0f, 0f, 0f, 0.3f, 0f, 0f, 0f, 0f },             // boreal forest
        new[] { 0.45f, 0.37f, 0.08f, 0f, 0f, 0f, 0f, 0.05f, 0f, 0f, 0f, 0.05f, 0f, 0f, 0f, 0f },          // temperate forest
        new[] { 0.85f, 0f, 0.12f, 0f, 0.03f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },                // grassland
        new[] { 0.3f, 0f, 0.15f, 0f, 0.15f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.4f, 0f },               // shrubland
        new[] { 0f, 0f, 0.05f, 0.05f, 0.1f, 0.45f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.35f, 0f },           // desert
        new[] { 0.55f, 0f, 0.05f, 0f, 0.05f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.35f, 0f },             // savanna
        new[] { 0.25f, 0.45f, 0.1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.2f, 0f, 0f, 0f, 0f },               // tropical forest
        new[] { 0.1f, 0f, 0f, 0.35f, 0.3f, 0f, 0f, 0.1f, 0f, 0f, 0f, 0f, 0.15f, 0f, 0f, 0f },             // alpine
    };

    /// <summary>Trees per square metre at full cover, before clearings, slope and tree line.</summary>
    private static readonly double[] TreeDensity = { 0, 0, 0, 0.0004, 0.028, 0.03, 0.0015, 0.003, 0.0002, 0.002, 0.045, 0.0004 };

    /// <summary>Bushes per square metre.</summary>
    private static readonly double[] BushDensity = { 0, 0.002, 0, 0.01, 0.025, 0.04, 0.01, 0.05, 0.004, 0.02, 0.06, 0.006 };

    /// <summary>Ferns and low plants per square metre.</summary>
    private static readonly double[] FernDensity = { 0, 0, 0, 0.01, 0.06, 0.09, 0.02, 0.01, 0, 0.005, 0.12, 0.004 };

    /// <summary>Red-berry bushes per square metre: forest edges and clearings of the temperate belt.</summary>
    private static readonly double[] RedBerryDensity = { 0, 0, 0, 0.0004, 0.0012, 0.0035, 0.0018, 0.0012, 0, 0.0004, 0.0015, 0 };

    /// <summary>Blue-berry bushes per square metre: the floors of cool forests and the tundra.</summary>
    private static readonly double[] BlueBerryDensity = { 0, 0, 0, 0.0015, 0.0035, 0.0022, 0.0004, 0, 0, 0, 0.0004, 0.0004 };

    /// <summary>Loose rocks per square metre on gentle ground.</summary>
    private static readonly double[] RockDensity = { 0, 0.003, 0.001, 0.02, 0.004, 0.003, 0.002, 0.008, 0.01, 0.003, 0.002, 0.03 };

    /// <summary>Which trees grow in each biome: weights of Fir, Pine, Birch, Broadleaf.</summary>
    private static readonly double[][] TreeMix =
    {
        new double[] { 0, 0, 0, 0 },
        new double[] { 0, 0.6, 0, 0.4 },
        new double[] { 0, 0, 0, 0 },
        new double[] { 0.5, 0.1, 0.4, 0 },
        new double[] { 0.6, 0.25, 0.15, 0 },
        new double[] { 0.3, 0.2, 0.25, 0.25 },
        new double[] { 0.15, 0.25, 0.2, 0.4 },
        new double[] { 0.05, 0.45, 0, 0.5 },
        new double[] { 0, 0.4, 0, 0.6 },
        new double[] { 0, 0.1, 0, 0.9 },
        new double[] { 0, 0, 0, 1 },
        new double[] { 0.7, 0.3, 0, 0 },
    };

    /// <summary>
    /// Soft weights of the land biomes for a climate (they add up to 1), with ice and alpine ground by rule:
    /// ice sheet below <paramref name="iceTemperature"/>, alpine ground above the tree line in high mountains.
    /// </summary>
    public static void Weights(in ClimateSample climate, double height, double iceTemperature, Span<double> weights)
    {
        weights.Clear();
        double t = climate.Temperature, m = climate.Moisture;
        double ice = DMath.SmoothStep(iceTemperature + 1.5, iceTemperature - 1.5, t);
        double alpine = (1 - climate.TreeLine) * DMath.SmoothStep(600, 1400, height) * (1 - ice);
        double sum = 0;
        for (int b = (int)Biome.Tundra; b <= (int)Biome.TropicalForest; b++)
        {
            double dt = (t - Centres[b].T) / SigmaT, dm = (m - Centres[b].M) / SigmaM;
            double w = DMath.Exp(-(dt * dt + dm * dm));
            weights[b] = w;
            sum += w;
        }
        double land = (1 - ice) * (1 - alpine) / Math.Max(sum, 1e-12);
        for (int b = (int)Biome.Tundra; b <= (int)Biome.TropicalForest; b++)
        {
            weights[b] *= land;
        }
        weights[(int)Biome.IceSheet] = ice;
        weights[(int)Biome.Alpine] = alpine;
    }

    /// <summary>The biome with the largest weight.</summary>
    public static Biome Dominant(ReadOnlySpan<double> weights)
    {
        int best = 0;
        for (int b = 1; b < weights.Length; b++)
        {
            if (weights[b] > weights[best])
            {
                best = b;
            }
        }
        return (Biome)best;
    }

    /// <summary>Adds each biome's materials, times its weight, to <paramref name="materials"/>.</summary>
    public static void AddMaterials(ReadOnlySpan<double> weights, Span<double> materials)
    {
        for (int b = 0; b < Count; b++)
        {
            double w = weights[b];
            if (w <= 0)
            {
                continue;
            }
            float[] row = Materials[b];
            for (int i = 0; i < MaterialCount; i++)
            {
                materials[i] += w * row[i];
            }
        }
    }

    /// <summary>A typical annual mean temperature (°C) and moisture of each biome, for surfaces without a climate model.</summary>
    private static readonly (double T, double M)[] Typical =
    {
        (14, 0.70),  // ocean
        (16, 0.45),  // beach
        (-18, 0.35), // ice sheet
        (-7, 0.40), (2, 0.62), (10, 0.68), (12, 0.40), (18, 0.27), (24, 0.06), (25, 0.38), (26, 0.80),
        (-3, 0.45),  // alpine
    };

    /// <summary>How much of a meadow the biomes make: where grass grows thick (0–1 before slope and shade).</summary>
    public static double Meadow(ReadOnlySpan<double> weights) =>
        weights[(int)Biome.Grassland] + weights[(int)Biome.TemperateForest] + 0.8 * weights[(int)Biome.BorealForest]
        + 0.7 * weights[(int)Biome.Savanna] + 0.6 * weights[(int)Biome.Shrubland] + 0.5 * weights[(int)Biome.Tundra]
        + 0.8 * weights[(int)Biome.TropicalForest] + 0.3 * weights[(int)Biome.Alpine];

    /// <summary>
    /// Ground material weights (<see cref="MaterialCount"/>, adding up to 1) for biome weights on ground of a slope (the
    /// sine of its tilt), under a tree cover (0–1), at an annual mean temperature (°C), with a small-scale roughness
    /// (0–1): the biomes' materials, litter under trees and grass in the open, gravel on rough ground, snow where it is
    /// cold enough to lie, then scree and bare rock on slopes. One rule for every surface.
    /// </summary>
    public static void GroundMaterials(ReadOnlySpan<double> biomes, double slope, double treeCover, double temperature, double roughness,
        Span<double> materials, double sandstone = 0)
    {
        materials.Clear();
        AddMaterials(biomes, materials);
        // Under trees the ground is litter and moss; in the open it is grass.
        double litter = Math.Min(materials[(int)GroundMaterial.ForestFloor], 1);
        double open = 1 - treeCover;
        materials[(int)GroundMaterial.Grass] += litter * open * 0.8;
        materials[(int)GroundMaterial.ForestFloor] = litter * (0.2 + 0.8 * treeCover);
        // Stony ground in rough places.
        double stony = 0.25 * roughness * roughness;
        materials[(int)GroundMaterial.Gravel] += stony;
        // Snow where it is cold enough to lie (beyond the permanent ice).
        double snow = DMath.SmoothStep(-1.0, -5.0, temperature);
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
        // Cliffs are bare rock: sandstone in dry country and on tablelands (the caller's share), granite elsewhere.
        materials[(int)GroundMaterial.Rock] += cliff * (1 - sandstone);
        materials[(int)GroundMaterial.Sandstone] += cliff * sandstone;
    }

    /// <summary>
    /// The look of the ground (<see cref="ISurface.Cover"/>) for a surface that knows only its biome at a point: that
    /// biome alone, its typical climate, its trees and meadow thinned on steep ground (none under the sea, at
    /// <paramref name="heightAboveSea"/> below 0), and the materials by <see cref="GroundMaterials"/>.
    /// </summary>
    public static GroundCover Cover(Biome biome, double heightAboveSea, double slope, Span<double> biomes, Span<double> materials)
    {
        biomes.Clear();
        biomes[(int)biome] = 1;
        (double temperature, double moisture) = Typical[(int)biome];
        double gentle = 1 - DMath.SmoothStep(0.45, 0.62, slope);
        double land = heightAboveSea < 0.3 ? 0 : 1;
        double trees = Trees(biomes) * gentle * land;
        double grass = Math.Clamp(Meadow(biomes) * gentle * land, 0, 1);
        GroundMaterials(biomes, slope, Math.Clamp(trees / 0.022, 0, 1), temperature, 0, materials);
        return new GroundCover(temperature, moisture, grass, trees);
    }

    public static double Trees(ReadOnlySpan<double> weights) => Mix(weights, TreeDensity);
    public static double Bushes(ReadOnlySpan<double> weights) => Mix(weights, BushDensity);
    public static double Ferns(ReadOnlySpan<double> weights) => Mix(weights, FernDensity);
    public static double Rocks(ReadOnlySpan<double> weights) => Mix(weights, RockDensity);
    public static double RedBerries(ReadOnlySpan<double> weights) => Mix(weights, RedBerryDensity);
    public static double BlueBerries(ReadOnlySpan<double> weights) => Mix(weights, BlueBerryDensity);

    /// <summary>Weights of the four tree kinds (Fir, Pine, Birch, Broadleaf) for these biome weights.</summary>
    public static void TreeKinds(ReadOnlySpan<double> weights, Span<double> kinds)
    {
        kinds.Clear();
        for (int b = 0; b < Count; b++)
        {
            double w = weights[b] * TreeDensity[b];
            if (w <= 0)
            {
                continue;
            }
            for (int k = 0; k < 4; k++)
            {
                kinds[k] += w * TreeMix[b][k];
            }
        }
    }

    private static double Mix(ReadOnlySpan<double> weights, double[] table)
    {
        double s = 0;
        for (int b = 0; b < Count; b++)
        {
            s += weights[b] * table[b];
        }
        return s;
    }
}
