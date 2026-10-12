using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// Renders every tree variant of the flora library from eight sides into two atlases (GROUND.md §4.6): colour with
/// alpha, and camera-space normals; one row per variant, one column per view. Done once when the surface view opens,
/// in an offscreen viewport of its own world, two frames per pass.
/// </summary>
public sealed partial class ImpostorBaker : Node
{
    public const int Views = 8;
    public const int CellPixels = 256;
    private const float Spacing = 100; // metres between cells in the bake world (trees are scaled to fit)

    public sealed class Result
    {
        public ImageTexture Colour = null!;
        public ImageTexture Normal = null!;
        public int Rows;
    }

    /// <summary>Bakes the atlases; assigns each tree variant its row.</summary>
    public async Task<Result> Bake(FloraLibrary library)
    {
        var variants = new List<(FloraLibrary.Species Species, FloraLibrary.Variant Variant)>();
        foreach (FloraLibrary.Species s in library.All)
        {
            if (!s.Tree)
            {
                continue;
            }
            foreach (FloraLibrary.Variant v in s.Variants)
            {
                v.ImpostorRow = variants.Count;
                variants.Add((s, v));
            }
        }
        int rows = variants.Count;
        var viewport = new SubViewport
        {
            Size = new Vector2I(Views * CellPixels, rows * CellPixels),
            TransparentBg = true,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            PositionalShadowAtlasSize = 0,
        };
        AddChild(viewport);
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = rows * Spacing,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = 1,
            Far = 4 * Spacing,
            Position = new Vector3(Views * Spacing / 2, rows * Spacing / 2, 2 * Spacing),
            Current = true,
        };
        viewport.AddChild(camera);

        Shader shader = GD.Load<Shader>("res://shaders/surface_impostor_bake.gdshader");
        var materials = new List<ShaderMaterial>();
        ShaderMaterial Bake(ShaderMaterial source, int mode)
        {
            var m = new ShaderMaterial { Shader = shader };
            m.SetShaderParameter("mode", mode);
            if (mode != 2)
            {
                m.SetShaderParameter("albedo_texture", source.GetShaderParameter("albedo_texture"));
            }
            materials.Add(m);
            return m;
        }
        ShaderMaterial leaves = Bake(library.Leaves, 0), bark = Bake(library.Bark, 1), birch = Bake(library.BirchBark, 1);
        ShaderMaterial berries = Bake(library.Berries, 2);

        for (int r = 0; r < rows; r++)
        {
            (FloraLibrary.Species species, FloraLibrary.Variant variant) = variants[r];
            float k = Spacing / variant.Extent;
            for (int c = 0; c < Views; c++)
            {
                // View c shows the tree as seen from azimuth c/8 of a turn (atan2(x, z) in its own frame): the camera
                // looks along -Z, so the tree turns the other way.
                float azimuth = c * Mathf.Tau / Views;
                var basis = new Basis(Vector3.Up, -azimuth).Scaled(new Vector3(k, k, k));
                var origin = new Vector3((c + 0.5f) * Spacing, (rows - r - 0.5f) * Spacing - k * variant.Height / 2, 0);
                var mi = new MeshInstance3D { Mesh = variant.Lod0, Transform = new Transform3D(basis, origin) };
                for (int s = 0; s < variant.Lod0.GetSurfaceCount(); s++)
                {
                    Material m = variant.Lod0.SurfaceGetMaterial(s);
                    mi.SetSurfaceOverrideMaterial(s, m == library.Leaves ? leaves : m == library.Berries ? berries
                        : m == library.BirchBark ? birch : bark);
                }
                viewport.AddChild(mi);
            }
        }

        var result = new Result { Rows = rows };
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (ShaderMaterial m in materials)
            {
                m.SetShaderParameter("pass", pass);
            }
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            for (int f = 0; f < 3; f++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            Image image = viewport.GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            string dump = OS.GetEnvironment("HU_FLORA_DUMP");
            if (dump.Length > 0)
            {
                image.SavePng(dump + $"/impostor_{pass}.png");
            }
            float[] px = ToFloats(image);
            int w = image.GetWidth(), h = image.GetHeight();
            if (pass == 0)
            {
                FloraTextures.Dilate(px, w, h, 4);
                result.Colour = ImageTexture.CreateFromImage(FloraTextures.WithCoverageMipmaps(px, w, h, Views, rows));
            }
            else
            {
                FloraTextures.Dilate(px, w, h, 4);
                Image normal = FromFloats(px, w, h);
                normal.GenerateMipmaps();
                result.Normal = ImageTexture.CreateFromImage(normal);
            }
        }
        viewport.QueueFree();
        return result;
    }

    private static float[] ToFloats(Image image)
    {
        byte[] data = image.GetData();
        int n = image.GetWidth() * image.GetHeight() * 4;
        var px = new float[n];
        for (int i = 0; i < n; i++)
        {
            px[i] = data[i] / 255f;
        }
        // Transparent texels carry no colour yet: let the dilation fill them.
        for (int i = 0; i < n; i += 4)
        {
            if (px[i + 3] < 0.01f)
            {
                px[i] = px[i + 1] = px[i + 2] = 0;
            }
        }
        return px;
    }

    private static Image FromFloats(float[] px, int w, int h)
    {
        var bytes = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            bytes[i] = (byte)Math.Clamp((int)(px[i] * 255 + 0.5f), 0, 255);
        }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, bytes);
    }
}
