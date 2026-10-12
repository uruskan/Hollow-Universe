using Godot;

namespace HollowUniverse.UI;

/// <summary>
/// The death screen's map until the planet package draws the real one: a coast generated from fixed-seed noise in the
/// muted colours of a survey map (sea, shallows, beach, grass, forest, rock). It's a picture, drawn under the crisp
/// grid, labels and markers.
/// </summary>
public static class DeathMap
{
    private static ImageTexture? _texture;

    /// <summary>The map picture, 2.4 times as wide as tall.</summary>
    public static Texture2D Texture => _texture ??= Build(960, 400);

    private static ImageTexture Build(int width, int height)
    {
        var land = new FastNoiseLite { Seed = 7, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.008f, FractalOctaves = 5 };
        var woods = new FastNoiseLite { Seed = 11, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.02f, FractalOctaves = 3 };
        Color deep = new(0.22f, 0.36f, 0.43f), shallow = new(0.36f, 0.50f, 0.53f), sand = new(0.78f, 0.73f, 0.59f);
        Color grass = new(0.58f, 0.60f, 0.45f), forest = new(0.36f, 0.43f, 0.30f), rock = new(0.60f, 0.59f, 0.52f);
        Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // A landmass across the middle that falls into the sea towards the bottom right.
                float u = x / (float)width, v = y / (float)height;
                float h = land.GetNoise2D(x, y) * 0.9f + 0.32f - 0.55f * Mathf.Max(0, v - 0.55f) - 0.35f * Mathf.Max(0, u - 0.8f) - 0.2f * Mathf.Max(0, 0.15f - v);
                Color c;
                if (h < -0.08f)
                {
                    c = deep;
                }
                else if (h < 0f)
                {
                    c = shallow.Lerp(deep, -h / 0.08f);
                }
                else if (h < 0.05f)
                {
                    c = sand;
                }
                else if (h > 0.6f)
                {
                    c = rock;
                }
                else
                {
                    c = woods.GetNoise2D(x, y) > 0.22f ? forest : grass.Lerp(sand, Mathf.Clamp(0.12f - h, 0, 0.07f) / 0.07f * 0.5f);
                }
                image.SetPixel(x, y, c);
            }
        }
        return ImageTexture.CreateFromImage(image);
    }
}
