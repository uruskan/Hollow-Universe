using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Presentation densities from the surface's already biome-, shore- and canopy-adjusted cover.</summary>
public readonly record struct GroundDetailCover(double Grass, double Flowers, double Litter, double Plants)
{
    public static GroundDetailCover At(in GroundCover cover, double slope, double aboveWater,
        ReadOnlySpan<double> biomes, ReadOnlySpan<double> foldedMaterials)
    {
        if (aboveWater < 0.08)
        {
            return default;
        }
        // Cover.Grass already thins under trees. Suppressing it again for both forest floor and canopy erased
        // woodland grass while the separate collectible fern meshes remained visible.
        double substrate = Math.Clamp(1 - foldedMaterials[(int)GroundMaterial.Rock]
            - foldedMaterials[(int)GroundMaterial.Sand] - foldedMaterials[(int)GroundMaterial.Snow]
            - 0.55 * foldedMaterials[(int)GroundMaterial.Gravel], 0, 1);
        double gentle = 1 - DMath.SmoothStep(0.40, 0.68, slope);
        double grass = Math.Clamp(cover.Grass * 1.25 * substrate * gentle, 0, 1);
        double canopy = Math.Clamp(cover.Trees / 0.018, 0, 1);
        double flowers = Math.Clamp(biomes[(int)Biome.Grassland] + 0.5 * biomes[(int)Biome.Shrubland]
            + 0.3 * biomes[(int)Biome.TemperateForest], 0, 1) * (1 - canopy * 0.8);
        double litter = canopy * canopy * (0.45 + 0.55 * Math.Clamp(foldedMaterials[(int)GroundMaterial.ForestFloor] * 2, 0, 1))
            * (1 - DMath.SmoothStep(0.45, 0.70, slope));
        double plants = litter * Math.Clamp(0.45 + 0.8 * cover.Moisture, 0, 1);
        return new GroundDetailCover(grass, flowers, litter, plants);
    }
}
