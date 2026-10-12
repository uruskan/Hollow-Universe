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

/// <summary>Ground materials the terrain is painted with; weights per vertex, eight in all.</summary>
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
    public const int MaterialCount = 8;

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
        new[] { 0f, 0f, 0f, 0.2f, 0.3f, 0.5f, 0f, 0f },       // ocean floor
        new[] { 0f, 0f, 0f, 0f, 0.1f, 0.9f, 0f, 0f },         // beach
        new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f },             // ice sheet
        new[] { 0.15f, 0f, 0f, 0.05f, 0.25f, 0f, 0f, 0.55f }, // tundra
        new[] { 0.15f, 0.55f, 0f, 0f, 0.05f, 0f, 0f, 0.25f }, // boreal forest
        new[] { 0.45f, 0.42f, 0.08f, 0f, 0f, 0f, 0f, 0.05f }, // temperate forest
        new[] { 0.85f, 0f, 0.12f, 0f, 0.03f, 0f, 0f, 0f },    // grassland
        new[] { 0.35f, 0f, 0.45f, 0f, 0.2f, 0f, 0f, 0f },     // shrubland
        new[] { 0f, 0f, 0.1f, 0.05f, 0.15f, 0.7f, 0f, 0f },   // desert
        new[] { 0.55f, 0f, 0.4f, 0f, 0.05f, 0f, 0f, 0f },     // savanna
        new[] { 0.25f, 0.65f, 0.1f, 0f, 0f, 0f, 0f, 0f },     // tropical forest
        new[] { 0.1f, 0f, 0f, 0.4f, 0.4f, 0f, 0f, 0.1f },     // alpine
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
