using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Content;
using HollowUniverse.Fx;

namespace HollowUniverse.Ground;

/// <summary>
/// A station's bay shown in the world (deployables.json <c>bay</c>, G6; GROUND.md §6.7): the ground in front of a vehicle
/// workbench where a finished ship rolls out and which must be clear for it. A placed station marks it (<see cref="Mark"/>):
/// paint on the ground (<see cref="Paint"/>: a decal, worn hazard stripes round its edge and two chevrons the way the ship
/// faces) and a striped post at each corner, which stands above grass; a station's placement ghost draws its outline
/// (<see cref="Outline"/>), so the room it needs shows before it is put down. All in the station's own frame (x across,
/// y up, z out of its front), as the bay's numbers are.
/// </summary>
public static class BayMarking
{
    /// <summary>Paint's pixels per metre.</summary>
    private const float PixelsPerMetre = 80;

    /// <summary>The striped border's width and the stripes' period, metres.</summary>
    private const float Band = 0.2f, Stripe = 0.3f;

    private static readonly Dictionary<(int, int), ImageTexture> Textures = new();

    private static Mesh? _post;
    private static StandardMaterial3D? _postMaterial;

    /// <summary>
    /// The bay marked on the ground in front of a placed station: paint over its footprint (a decal, a metre and more up
    /// and down), and a striped post at each corner that stands above grass (presentation: nothing collides with them).
    /// </summary>
    public static Node3D Mark(BayStats bay)
    {
        var root = new Node3D { Name = "Bay" };
        root.AddChild(Paint(bay));
        _post ??= new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.045f, Height = 1.0f, RadialSegments = 8, Rings = 1 };
        _postMaterial ??= new StandardMaterial3D { AlbedoTexture = PostTexture(), Roughness = 0.7f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps };
        foreach ((float sx, float sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "BayPost",
                Mesh = _post,
                MaterialOverride = _postMaterial,
                // Sunk a third of its length, so it stands on a slope too.
                Position = new Vector3(bay.At.X + sx * bay.Clear.X, bay.At.Y + 0.25f, bay.At.Z + sz * bay.Clear.Z),
            });
        }
        return root;
    }

    // A post's paint: yellow and black bands round it (V along its length).
    private static ImageTexture PostTexture()
    {
        const int w = 8, h = 64;
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            bool yellow = (y / 8) % 2 == 0;
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                data[i] = (byte)(yellow ? 237 : 20);
                data[i + 1] = (byte)(yellow ? 184 : 20);
                data[i + 2] = (byte)(yellow ? 31 : 18);
                data[i + 3] = 255;
            }
        }
        Image image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The bay painted on the ground in front of a placed station: a decal over its footprint, a metre and more up and down.</summary>
    public static Decal Paint(BayStats bay)
    {
        float width = 2 * bay.Clear.X, length = 2 * bay.Clear.Z;
        return new Decal
        {
            Name = "Bay",
            Position = new Vector3(bay.At.X, bay.At.Y, bay.At.Z),
            Size = new Vector3(width, 2.6f, length),
            TextureAlbedo = Texture(width, length),
            // Not on the arms and tools in view, nor on the effects (as the nature markers and effect decals).
            CullMask = 0xFFFFF & ~(1u << 1) & ~FxDirector.RenderLayer,
            UpperFade = 0.25f,
            LowerFade = 0.25f,
            NormalFade = 0.45f,
            AlbedoMix = 0.92f,
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 90f,
            DistanceFadeLength = 30f,
        };
    }

    /// <summary>The bay's outline for a placement ghost: its edge and an arrow a few centimetres over its ground, both faces.</summary>
    public static MeshInstance3D Outline(BayStats bay)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        float hx = bay.Clear.X, hz = bay.Clear.Z, y = bay.At.Y + 0.04f, b = 0.1f;
        var c = new Vector3(bay.At.X, y, bay.At.Z);
        void Quad(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
        {
            foreach (Vector3 v in new[] { p, q, r, p, r, s, p, r, q, p, s, r })
            {
                tool.AddVertex(c + v);
            }
        }
        Quad(new(-hx, 0, -hz), new(hx, 0, -hz), new(hx, 0, -hz + b), new(-hx, 0, -hz + b));
        Quad(new(-hx, 0, hz - b), new(hx, 0, hz - b), new(hx, 0, hz), new(-hx, 0, hz));
        Quad(new(-hx, 0, -hz + b), new(-hx + b, 0, -hz + b), new(-hx + b, 0, hz - b), new(-hx, 0, hz - b));
        Quad(new(hx - b, 0, -hz + b), new(hx, 0, -hz + b), new(hx, 0, hz - b), new(hx - b, 0, hz - b));
        // The arrow: the way a ship built here faces as it rolls out (the station's front).
        float tip = hz * 0.55f, w = Math.Min(hx * 0.5f, 0.8f);
        foreach (float back in new[] { 0f, 0.7f })
        {
            Quad(new(-w, 0, tip - back - w), new(-w + 0.18f, 0, tip - back - w), new(0.09f, 0, tip - back), new(-0.09f, 0, tip - back));
            Quad(new(w - 0.18f, 0, tip - back - w), new(w, 0, tip - back - w), new(0.09f, 0, tip - back), new(-0.09f, 0, tip - back));
        }
        tool.GenerateNormals();
        return new MeshInstance3D { Name = "Bay outline", Mesh = tool.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    // The paint: hazard stripes round the edge, two chevrons toward the far end (+V, the station's front), worn in places;
    // clear inside. One texture per bay size.
    private static ImageTexture Texture(float width, float length)
    {
        int w = Math.Max(16, (int)(width * PixelsPerMetre)), h = Math.Max(16, (int)(length * PixelsPerMetre));
        if (Textures.TryGetValue((w, h), out ImageTexture? cached))
        {
            return cached;
        }
        var data = new byte[w * h * 4];
        var yellow = new Color(0.93f, 0.72f, 0.12f);
        var dark = new Color(0.07f, 0.07f, 0.065f);
        var white = new Color(0.86f, 0.85f, 0.8f);
        float chevronHalf = Math.Min(width * 0.25f, 0.8f), tip = length * 0.5f + length * 0.5f * 0.55f;
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float x = (px + 0.5f) / PixelsPerMetre, z = (py + 0.5f) / PixelsPerMetre;
                float edge = Math.Min(Math.Min(x, width - x), Math.Min(z, length - z));
                Color colour = default;
                float alpha = 0;
                if (edge < Band)
                {
                    bool stripe = ((x + z) / Stripe % 1 + 1) % 1 < 0.5f;
                    colour = stripe ? yellow : dark;
                    alpha = 0.9f;
                }
                else
                {
                    // Two chevrons pointing to +z, each a band 0.18 m wide.
                    float across = Math.Abs(x - width / 2);
                    foreach (float back in new[] { 0f, 0.7f })
                    {
                        float d = tip - back - z - across;
                        if (across < chevronHalf && d is >= 0 and < 0.25f)
                        {
                            colour = white;
                            alpha = 0.8f;
                        }
                    }
                }
                if (alpha > 0)
                {
                    // Worn: a hash of the metre cell and the pixel takes some paint away.
                    float wear = Hash(px / 9, py / 9) * 0.6f + Hash(px, py) * 0.4f;
                    alpha *= wear < 0.18f ? 0.25f : 1f;
                }
                int i = (py * w + px) * 4;
                data[i] = (byte)(colour.R * 255);
                data[i + 1] = (byte)(colour.G * 255);
                data[i + 2] = (byte)(colour.B * 255);
                data[i + 3] = (byte)(alpha * 255);
            }
        }
        Image image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        Textures[(w, h)] = texture;
        return texture;
    }

    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65535f;
    }
}
