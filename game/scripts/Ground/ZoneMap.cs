using System;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Ground;

/// <summary>
/// The map of the zone for the death screen (GROUND.md §8): the real ground around the landing site, 9.6 by 4 km (the
/// screen's 2.4 to 1), drawn in the survey colours of the interface's placeholder (sea, shallows, sand, meadow,
/// forest, rock, snow) from the planet's own heights, biomes and forest cover, on worker threads.
/// </summary>
public static class ZoneMap
{
    /// <summary>Half the map's width and height, in metres of the zone's tangent plane.</summary>
    public const float HalfWidth = 4800, HalfHeight = 2000;

    /// <summary>A zone point (x east, z south) on the map, 0–1 across and down.</summary>
    public static (float X, float Y) Point(float x, float z) =>
        (Math.Clamp(0.5f + x / (2 * HalfWidth), 0.01f, 0.99f), Math.Clamp(0.5f + z / (2 * HalfHeight), 0.01f, 0.99f));

    public static Task<Image> Build(PlanetSurface surface, ZoneFrame frame, int width = 960, int height = 400) => Task.Run(() =>
    {
        Color deep = new(0.22f, 0.36f, 0.43f), shallow = new(0.36f, 0.50f, 0.53f), sand = new(0.78f, 0.73f, 0.59f);
        Color grass = new(0.58f, 0.60f, 0.45f), forest = new(0.36f, 0.43f, 0.30f), rock = new(0.60f, 0.59f, 0.52f), snow = new(0.90f, 0.91f, 0.90f);
        var pixels = new byte[width * height * 3];
        Parallel.For(0, height, y =>
        {
            Span<double> biomes = stackalloc double[BiomeTable.Count];
            for (int x = 0; x < width; x++)
            {
                double px = ((x + 0.5) / width - 0.5) * 2 * HalfWidth, pz = ((y + 0.5) / height - 0.5) * 2 * HalfHeight;
                Vector3D d = frame.ToBody(px, 0, pz).Normalized;
                SurfaceSample s = surface.Sample(d);
                Color c;
                if (s.Height < 0)
                {
                    c = shallow.Lerp(deep, Mathf.Clamp((float)(-s.Height / 25), 0, 1));
                }
                else
                {
                    ClimateSample climate = surface.Climate(d, s);
                    surface.BiomeWeights(d, s, climate, biomes);
                    double trees = BiomeTable.Trees(biomes) * surface.ForestPatches(d) * climate.TreeLine;
                    c = climate.Temperature < -2 || biomes[(int)Biome.IceSheet] > 0.5 ? snow
                        : biomes[(int)Biome.Beach] > 0.45 ? sand
                        : s.Mountain > 0.45 || biomes[(int)Biome.Alpine] > 0.5 ? rock
                        : trees > 0.012 ? forest
                        : grass;
                    // Height shading: a little lighter up high.
                    c = c.Lightened(Mathf.Clamp((float)(s.Height / 2500), 0, 0.25f));
                }
                int k = (y * width + x) * 3;
                pixels[k] = (byte)(c.R * 255);
                pixels[k + 1] = (byte)(c.G * 255);
                pixels[k + 2] = (byte)(c.B * 255);
            }
        });
        return Image.CreateFromData(width, height, false, Image.Format.Rgb8, pixels);
    });
}
