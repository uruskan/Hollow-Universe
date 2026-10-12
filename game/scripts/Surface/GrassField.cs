using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

/// <summary>
/// The grass of the surface view (GROUND.md §4.6): presentation only, not part of the scatter. One MultiMesh of blade
/// tufts on a grid that follows the camera, placed and shaped entirely on the GPU (surface_grass.gdshader) from a hash
/// of each ground cell in stable coordinates, so nothing shifts as the grid moves. A field texture made on a worker
/// from the surface generator gives the shader the ground's height (exactly the rendered triangles of the finest
/// tiles), and the grass density, moisture and flowers; it is remade when the camera has moved a few metres.
/// </summary>
public sealed partial class GrassField : Node3D
{
    public const float Cell = 0.4f;
    public const float FarRadius = 42;
    private const float ShrinkStart = 22;
    private const int Texels = 256;
    private const double Spacing = 0.5;
    private const double Recentre = 14;
    private const int CoarseStep = 8; // texels between climate samples

    private sealed class FieldData
    {
        public float[] Pixels = Array.Empty<float>();
        public double OriginX, OriginZ, BaseY; // stable coordinates of texel (0, 0) and of height zero
    }

    private PlanetSurface _surface = null!;
    private RenderFrame _frame = null!;
    private Vector3D _initialOrigin;
    private Vector3D _east, _up, _south;
    private bool _near;
    private int _gridWidth;
    private MultiMeshInstance3D _instance = null!;
    private ShaderMaterial _material = null!;
    private Task<FieldData>? _job;
    private FieldData? _field;
    private Image? _image;
    private ImageTexture? _texture;
    private double _centreX = double.MaxValue, _centreZ;
    private bool _ready;

    /// <summary>Tufts on the grid (most of them shrink to nothing where there is no grass).</summary>
    public int Tufts => _ready ? _gridWidth * _gridWidth : 0;

    /// <summary>True when the field around the camera is in place (or the camera is too high for grass).</summary>
    public bool IsIdle => !_near || (_ready && _job == null);

    public void Setup(PlanetSurface surface, RenderFrame frame, FloraLibrary library, TextureLayered groundAlbedo, Texture noise, Vector3 bladeMean)
    {
        _surface = surface;
        _frame = frame;
        _initialOrigin = frame.Origin - frame.Travelled;
        _east = frame.DirectionToBody(Vector3.Right);
        _up = frame.Up;
        _south = frame.DirectionToBody(Vector3.Back);
        _gridWidth = (int)Math.Ceiling(2 * FarRadius / Cell) + 2;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_grass.gdshader") };
        _material.SetShaderParameter("grid_width", _gridWidth);
        _material.SetShaderParameter("cell", Cell);
        _material.SetShaderParameter("far_radius", FarRadius);
        _material.SetShaderParameter("shrink_start", ShrinkStart);
        _material.SetShaderParameter("field_spacing", (float)Spacing);
        _material.SetShaderParameter("field_texels", (float)Texels);
        _material.SetShaderParameter("atlas", library.AtlasTexture);
        _material.SetShaderParameter("cell_grass", FloraTextures.CellUv(FloraTextures.Grass, 6));
        _material.SetShaderParameter("cell_dry", FloraTextures.CellUv(FloraTextures.DryGrass, 6));
        _material.SetShaderParameter("cell_meadow", FloraTextures.CellUv(FloraTextures.Meadow, 6));
        _material.SetShaderParameter("cell_flower", FloraTextures.CellUv(FloraTextures.Flower, 6));
        _material.SetShaderParameter("cell_herb", FloraTextures.CellUv(FloraTextures.Herb, 6));
        _material.SetShaderParameter("ground_albedo", groundAlbedo);
        _material.SetShaderParameter("macro_noise", noise);
        _material.SetShaderParameter("blade_mean", bladeMean);

        int count = _gridWidth * _gridWidth;
        var buffer = new float[count * 12];
        for (int i = 0; i < count; i++)
        {
            buffer[i * 12] = 1;
            buffer[i * 12 + 5] = 1;
            buffer[i * 12 + 10] = 1;
        }
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = TuftMesh(),
            InstanceCount = count,
            Buffer = buffer,
            CustomAabb = new Aabb(new Vector3(-6000, -6000, -6000), new Vector3(12000, 12000, 12000)),
        };
        _instance = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Visible = false,
        };
        AddChild(_instance);
    }

    // A tuft at unit height: hollow-orbit's grass clump, cards leaning out around the centre with a curved profile.
    // UV: x across the card, y from the foot (0) to the tip (1); colour alpha: the same height fraction.
    private static ArrayMesh TuftMesh()
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colours = new List<Color>();
        var indices = new List<int>();
        var r = new Rng(201);
        float R() => (float)r.NextDouble();
        const int Cards = 5, Segments = 3;
        const float W = 0.36f;
        for (int k = 0; k < Cards; k++)
        {
            float u0 = R() * 0.35f, u1 = Math.Min(1, u0 + 0.55f + 0.1f * R());
            float a = k / (float)Cards * MathF.Tau + R() * 0.6f;
            var outward = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            Vector3 side = new Vector3(-MathF.Sin(a), 0, MathF.Cos(a)) * (W * (0.42f + 0.2f * R()));
            float h = 0.8f + 0.4f * R();
            float lean = 0.12f + 0.14f * R();
            Vector3 up = new(outward.X * h * lean * 0.6f, h, outward.Z * h * lean * 0.6f);
            Vector3 offset = outward * (0.05f + 0.08f * R());
            Vector3 curl = outward * (h * lean * 0.35f);
            Vector3 normal = new Vector3(outward.X * 0.35f, 1, outward.Z * 0.35f).Normalized();
            int b = positions.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                Vector3 c = offset + up * t + curl * (t * t);
                for (int s = 0; s < 2; s++)
                {
                    positions.Add(c + side * (s == 0 ? -1 : 1) * (1 - 0.35f * t));
                    normals.Add(normal);
                    uvs.Add(new Vector2(s == 0 ? u0 : u1, t));
                    colours.Add(new Color(1, 1, 1, t));
                }
            }
            for (int i = 0; i < Segments; i++)
            {
                int p = b + i * 2;
                indices.AddRange(new[] { p, p + 3, p + 1, p, p + 2, p + 3 });
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // ---------------------------------------------------------------- update

    /// <summary>Moves the grid with the camera (render position) and remakes the field when it has moved far enough.</summary>
    public void UpdateGrass(Vector3 camera, double altitude, bool walking)
    {
        Vector3D travelled = _frame.DirectionToRenderD(_frame.Travelled);
        double sx = camera.X + travelled.X, sz = camera.Z + travelled.Z;
        bool near = altitude < FarRadius;
        _near = near;
        if (_job != null && _job.IsCompleted)
        {
            if (!_job.IsFaulted)
            {
                _field = _job.Result;
                Upload(_field);
            }
            else
            {
                GD.PushError($"Grass field failed: {_job.Exception}");
            }
            _job = null;
        }
        double dx = sx - _centreX, dz = sz - _centreZ;
        if (near && _job == null && dx * dx + dz * dz > Recentre * Recentre)
        {
            // Snap the field to its texels in stable coordinates, so successive fields sample alike.
            double cx = Math.Round(sx / Spacing) * Spacing, cz = Math.Round(sz / Spacing) * Spacing;
            _centreX = cx;
            _centreZ = cz;
            _job = Task.Run(() => Build(cx, cz));
        }
        _ready = _field != null;
        _instance.Visible = near && _field != null;
        if (!_instance.Visible)
        {
            return;
        }
        long cornerX = (long)Math.Floor(sx / Cell) - _gridWidth / 2, cornerZ = (long)Math.Floor(sz / Cell) - _gridWidth / 2;
        _material.SetShaderParameter("corner_cell", new Vector2I((int)cornerX, (int)cornerZ));
        _material.SetShaderParameter("corner_render", new Vector2((float)(cornerX * Cell - travelled.X), (float)(cornerZ * Cell - travelled.Z)));
        _material.SetShaderParameter("field_origin", new Vector2((float)(_field!.OriginX - travelled.X), (float)(_field.OriginZ - travelled.Z)));
        _material.SetShaderParameter("field_base", (float)(_field.BaseY - travelled.Y));
        _material.SetShaderParameter("trample", walking ? new Vector4(camera.X, camera.Y - 1.7f, camera.Z, 0.9f) : Vector4.Zero);
    }

    private void Upload(FieldData field)
    {
        byte[] bytes = new byte[field.Pixels.Length * 4];
        Buffer.BlockCopy(field.Pixels, 0, bytes, 0, bytes.Length);
        if (_image == null || _texture == null)
        {
            _image = Image.CreateFromData(Texels, Texels, false, Image.Format.Rgbaf, bytes);
            _texture = ImageTexture.CreateFromImage(_image);
            _material.SetShaderParameter("field", _texture);
        }
        else
        {
            _image.SetData(Texels, Texels, false, Image.Format.Rgbaf, bytes);
            _texture.Update(_image);
        }
    }

    // ---------------------------------------------------------------- the field (worker thread)

    private FieldData Build(double centreX, double centreZ)
    {
        double ox = centreX - Texels / 2 * Spacing, oz = centreZ - Texels / 2 * Spacing;
        var field = new FieldData { OriginX = ox, OriginZ = oz, Pixels = new float[Texels * Texels * 4] };
        var heights = new Dictionary<(int, long, long), double>();
        var ys = new double[Texels * Texels];
        double baseY = double.NaN;
        for (int b = 0; b < Texels; b++)
        {
            for (int a = 0; a < Texels; a++)
            {
                double y = GroundY(ox + a * Spacing, oz + b * Spacing, heights);
                ys[b * Texels + a] = y;
                if (double.IsNaN(baseY))
                {
                    baseY = Math.Round(y);
                }
            }
        }
        field.BaseY = baseY;
        // Density, moisture and flowers on a coarser grid, then spread bilinearly.
        int coarse = Texels / CoarseStep + 1;
        var climate = new float[coarse * coarse * 3];
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        for (int b = 0; b < coarse; b++)
        {
            for (int a = 0; a < coarse; a++)
            {
                int ta = Math.Min(a * CoarseStep, Texels - 1), tb = Math.Min(b * CoarseStep, Texels - 1);
                double x = ox + ta * Spacing, z = oz + tb * Spacing;
                Vector3D d = Body(x, ys[tb * Texels + ta], z).Normalized;
                SurfaceSample s = _surface.Sample(d);
                ClimateSample c = _surface.Climate(d, s);
                // Slope from the field's own heights, over a couple of metres.
                int a0 = Math.Max(ta - 2, 0), a1 = Math.Min(ta + 2, Texels - 1), b0 = Math.Max(tb - 2, 0), b1 = Math.Min(tb + 2, Texels - 1);
                double gx = (ys[tb * Texels + a1] - ys[tb * Texels + a0]) / ((a1 - a0) * Spacing);
                double gz = (ys[b1 * Texels + ta] - ys[b0 * Texels + ta]) / ((b1 - b0) * Spacing);
                double slope = Math.Sqrt(gx * gx + gz * gz) / Math.Sqrt(1 + gx * gx + gz * gz);
                _surface.BiomeWeights(d, s, c, biomes);
                VegetationSample v = _surface.Vegetation(d, s, c, biomes, slope);
                _surface.Materials(s, c, biomes, slope, Math.Clamp(v.Trees / 0.022, 0, 1), materials);
                double ground = 1 - materials[(int)GroundMaterial.Rock] - materials[(int)GroundMaterial.Sand] - materials[(int)GroundMaterial.Snow]
                    - 0.7 * materials[(int)GroundMaterial.Gravel] - 0.45 * materials[(int)GroundMaterial.ForestFloor];
                double density = Math.Clamp(v.Grass * 1.15 * Math.Clamp(ground, 0, 1) * (1 - DMath.SmoothStep(0.3, 0.45, slope)), 0, 1);
                double flowers = Math.Clamp(biomes[(int)Biome.Grassland] + 0.5 * biomes[(int)Biome.Shrubland] + 0.3 * biomes[(int)Biome.TemperateForest], 0, 1)
                    * (1 - Math.Clamp(v.Trees / 0.02, 0, 1));
                int k = (b * coarse + a) * 3;
                climate[k] = (float)density;
                climate[k + 1] = (float)c.Moisture;
                climate[k + 2] = (float)flowers;
            }
        }
        for (int b = 0; b < Texels; b++)
        {
            for (int a = 0; a < Texels; a++)
            {
                float fa = a / (float)CoarseStep, fb = b / (float)CoarseStep;
                int a0 = Math.Min((int)fa, coarse - 2), b0 = Math.Min((int)fb, coarse - 2);
                float u = fa - a0, w = fb - b0;
                int p = (b * Texels + a) * 4;
                field.Pixels[p] = (float)(ys[b * Texels + a] - baseY);
                for (int ch = 0; ch < 3; ch++)
                {
                    float c00 = climate[(b0 * coarse + a0) * 3 + ch], c10 = climate[(b0 * coarse + a0 + 1) * 3 + ch];
                    float c01 = climate[((b0 + 1) * coarse + a0) * 3 + ch], c11 = climate[((b0 + 1) * coarse + a0 + 1) * 3 + ch];
                    field.Pixels[p + 1 + ch] = (c00 * (1 - u) + c10 * u) * (1 - w) + (c01 * (1 - u) + c11 * u) * w;
                }
            }
        }
        return field;
    }

    // Body-frame point of stable render-axis coordinates (the frame's axes never turn; its origin only moves).
    private Vector3D Body(double x, double y, double z) => _initialOrigin + _east * x + _up * y + _south * z;

    // Stable render-axis height of the drawn ground at (x, z): the finest tiles' triangles, which follow the surface's
    // heights at the level-18 grid vertices (TileMesh, uniform diagonal), interpolated.
    private double GroundY(double x, double z, Dictionary<(int, long, long), double> cache)
    {
        Vector3D up = _up;
        Vector3D b0 = _initialOrigin + _east * x + _south * z;
        double y = 0;
        for (int it = 0; it < 2; it++)
        {
            Vector3D d = (b0 + up * y).Normalized;
            double r = _surface.Radius + TriangleHeight(d, cache);
            double bu = Vector3D.Dot(b0, up);
            double disc = bu * bu - b0.LengthSquared + r * r;
            y = -bu + Math.Sqrt(Math.Max(disc, 0));
        }
        return y;
    }

    private double TriangleHeight(Vector3D d, Dictionary<(int, long, long), double> cache)
    {
        const int Shift = PlanetSurface.CollisionLevel + 5; // 32 cells per tile
        double n = 1L << Shift;
        (int face, double u, double v) = CubeSphere.FromDirection(d);
        double gu = (u + 1) * 0.5 * n, gv = (v + 1) * 0.5 * n;
        long i = (long)Math.Floor(gu), j = (long)Math.Floor(gv);
        double fu = gu - i, fv = gv - j;
        double H(long a, long b)
        {
            if (!cache.TryGetValue((face, a, b), out double h))
            {
                h = _surface.HeightAt(CubeSphere.Direction(face, -1 + 2.0 * a / n, -1 + 2.0 * b / n));
                cache[(face, a, b)] = h;
            }
            return h;
        }
        double h00 = H(i, j), h11 = H(i + 1, j + 1);
        return fu >= fv
            ? h00 + fu * (H(i + 1, j) - h00) + fv * (h11 - H(i + 1, j))
            : h00 + fv * (H(i, j + 1) - h00) + fu * (h11 - H(i, j + 1));
    }

    /// <summary>Nothing to re-anchor: the grid follows the camera every frame.</summary>
    public void Reanchor()
    {
    }
}
