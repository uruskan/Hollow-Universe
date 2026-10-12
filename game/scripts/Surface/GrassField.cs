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
/// tiles), and the grass density, moisture and flowers; a second texture the forest cover and small plants; it is
/// remade when the camera has moved a few metres. A second, smaller grid lays leaf litter, needles and twigs flat on
/// the forest floor.
/// </summary>
public sealed partial class GrassField : Node3D
{
    public const float Cell = 0.4f;
    public const float FarRadius = 42;
    private const float ShrinkStart = 22;
    private const float LitterRadius = 26;
    private const float LitterShrink = 14;
    private const int Texels = 256;
    private const double Spacing = 0.5;
    private const double Recentre = 14;
    private const int CoarseStep = 8; // texels between climate samples

    private sealed class FieldData
    {
        public float[] Pixels = Array.Empty<float>();
        public float[] Pixels2 = Array.Empty<float>(); // forest cover, small plants
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
    private MultiMeshInstance3D _litter = null!;
    private ShaderMaterial _litterMaterial = null!;
    private int _litterWidth;
    private Image? _image2;
    private ImageTexture? _texture2;
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
        _material.SetShaderParameter("cell_fern", FloraTextures.CellUv(FloraTextures.Fern, 6));
        _material.SetShaderParameter("cell_bush", FloraTextures.CellUv(FloraTextures.Bush, 6));
        _material.SetShaderParameter("cell_leaves_a", FloraTextures.CellUv(FloraTextures.Broad, 6));
        _material.SetShaderParameter("cell_leaves_b", FloraTextures.CellUv(FloraTextures.Beech, 6));
        _material.SetShaderParameter("cell_leaves_c", FloraTextures.CellUv(FloraTextures.OakDark, 6));
        _material.SetShaderParameter("cell_leaves_d", FloraTextures.CellUv(FloraTextures.BirchLeaf, 6));
        _material.SetShaderParameter("cell_twigs", FloraTextures.CellUv(FloraTextures.Twigs, 6));
        _material.SetShaderParameter("cell_needles", FloraTextures.CellUv(FloraTextures.Spruce, 6));
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

        // Litter: the same field and hashing on a smaller grid, flat pieces instead of tufts.
        _litterWidth = (int)Math.Ceiling(2 * LitterRadius / Cell) + 2;
        _litterMaterial = (ShaderMaterial)_material.Duplicate();
        _litterMaterial.SetShaderParameter("layer", 1);
        _litterMaterial.SetShaderParameter("grid_width", _litterWidth);
        _litterMaterial.SetShaderParameter("far_radius", LitterRadius);
        _litterMaterial.SetShaderParameter("shrink_start", LitterShrink);
        int litterCount = _litterWidth * _litterWidth;
        var litterBuffer = new float[litterCount * 12];
        for (int i = 0; i < litterCount; i++)
        {
            litterBuffer[i * 12] = 1;
            litterBuffer[i * 12 + 5] = 1;
            litterBuffer[i * 12 + 10] = 1;
        }
        _litter = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = LitterMesh(),
                InstanceCount = litterCount,
                Buffer = litterBuffer,
                CustomAabb = new Aabb(new Vector3(-6000, -6000, -6000), new Vector3(12000, 12000, 12000)),
            },
            MaterialOverride = _litterMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Visible = false,
        };
        AddChild(_litter);
    }

    // Litter pieces lying on the ground: three cards of the atlas at random turns and places in the cell, their ends
    // curling up a little. UV as the tufts' (x across, y along); colour alpha 0 (no blade gradient).
    private static ArrayMesh LitterMesh()
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colours = new List<Color>();
        var indices = new List<int>();
        var r = new Rng(303);
        float R() => (float)r.NextDouble();
        const int Pieces = 3, Segments = 3;
        for (int k = 0; k < Pieces; k++)
        {
            float turn = R() * MathF.Tau;
            var along = new Vector3(MathF.Cos(turn), 0, MathF.Sin(turn));
            var across = new Vector3(-along.Z, 0, along.X);
            float length = 0.32f + 0.2f * R(), half = 0.13f + 0.08f * R();
            float ra = R() * MathF.Tau, rr = 0.2f * MathF.Sqrt(R());
            Vector3 centre = new(MathF.Cos(ra) * rr, 0, MathF.Sin(ra) * rr);
            float curl = 0.01f + 0.03f * R();
            int b = positions.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                float x = (t - 0.5f) * length;
                Vector3 c = centre + along * x + new Vector3(0, curl * (2 * t - 1) * (2 * t - 1) + 0.004f * k, 0);
                for (int sd = 0; sd < 2; sd++)
                {
                    positions.Add(c + across * (sd == 0 ? -half : half));
                    normals.Add(Vector3.Up);
                    uvs.Add(new Vector2(sd == 0 ? 0 : 1, t));
                    colours.Add(new Color(1, 1, 1, 0));
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
        // Cards spread over the tuft's cell at random turns, mostly upright: a field, not a fan of cards.
        const int Cards = 5, Segments = 3;
        const float W = 0.34f;
        for (int k = 0; k < Cards; k++)
        {
            float u0 = R() * 0.35f, u1 = Math.Min(1, u0 + 0.55f + 0.1f * R());
            float turn = R() * MathF.Tau;
            float lean = 0.04f + 0.12f * R();
            float leanTurn = R() * MathF.Tau;
            var leanDir = new Vector3(MathF.Cos(leanTurn), 0, MathF.Sin(leanTurn));
            Vector3 side = new Vector3(MathF.Cos(turn), 0, MathF.Sin(turn)) * (W * (0.4f + 0.25f * R()));
            float h = 0.75f + 0.45f * R();
            Vector3 up = new Vector3(0, h, 0) + leanDir * (h * lean);
            float ra = R() * MathF.Tau, rr = 0.22f * MathF.Sqrt(R());
            Vector3 offset = new Vector3(MathF.Cos(ra) * rr, 0, MathF.Sin(ra) * rr);
            Vector3 curl = leanDir * (h * lean * 0.6f);
            Vector3 normal = new Vector3(leanDir.X * 0.3f, 1, leanDir.Z * 0.3f).Normalized();
            int b = positions.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                Vector3 c = offset + up * t + curl * (t * t);
                for (int sd = 0; sd < 2; sd++)
                {
                    positions.Add(c + side * (sd == 0 ? -1 : 1) * (1 - 0.3f * t));
                    normals.Add(normal);
                    uvs.Add(new Vector2(sd == 0 ? u0 : u1, t));
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
        _litter.Visible = _instance.Visible;
        if (!_instance.Visible)
        {
            return;
        }
        foreach ((ShaderMaterial material, int width) in new[] { (_material, _gridWidth), (_litterMaterial, _litterWidth) })
        {
            long cornerX = (long)Math.Floor(sx / Cell) - width / 2, cornerZ = (long)Math.Floor(sz / Cell) - width / 2;
            material.SetShaderParameter("corner_cell", new Vector2I((int)cornerX, (int)cornerZ));
            material.SetShaderParameter("corner_render", new Vector2((float)(cornerX * Cell - travelled.X), (float)(cornerZ * Cell - travelled.Z)));
            material.SetShaderParameter("field_origin", new Vector2((float)(_field!.OriginX - travelled.X), (float)(_field.OriginZ - travelled.Z)));
            material.SetShaderParameter("field_base", (float)(_field.BaseY - travelled.Y));
        }
        _material.SetShaderParameter("trample", walking ? new Vector4(camera.X, camera.Y - 1.7f, camera.Z, 0.9f) : Vector4.Zero);
    }

    private void Upload(FieldData field)
    {
        byte[] bytes = new byte[field.Pixels.Length * 4];
        Buffer.BlockCopy(field.Pixels, 0, bytes, 0, bytes.Length);
        byte[] bytes2 = new byte[field.Pixels2.Length * 4];
        Buffer.BlockCopy(field.Pixels2, 0, bytes2, 0, bytes2.Length);
        if (_image == null || _texture == null || _image2 == null || _texture2 == null)
        {
            _image = Image.CreateFromData(Texels, Texels, false, Image.Format.Rgbaf, bytes);
            _texture = ImageTexture.CreateFromImage(_image);
            _image2 = Image.CreateFromData(Texels, Texels, false, Image.Format.Rgbaf, bytes2);
            _texture2 = ImageTexture.CreateFromImage(_image2);
            foreach (ShaderMaterial material in new[] { _material, _litterMaterial })
            {
                material.SetShaderParameter("field", _texture);
                material.SetShaderParameter("field2", _texture2);
            }
        }
        else
        {
            _image.SetData(Texels, Texels, false, Image.Format.Rgbaf, bytes);
            _texture.Update(_image);
            _image2.SetData(Texels, Texels, false, Image.Format.Rgbaf, bytes2);
            _texture2.Update(_image2);
        }
    }

    // ---------------------------------------------------------------- the field (worker thread)

    private FieldData Build(double centreX, double centreZ)
    {
        double ox = centreX - Texels / 2 * Spacing, oz = centreZ - Texels / 2 * Spacing;
        var field = new FieldData { OriginX = ox, OriginZ = oz, Pixels = new float[Texels * Texels * 4], Pixels2 = new float[Texels * Texels * 4] };
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
        const int Channels = 5;
        var climate = new float[coarse * coarse * Channels];
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
                    - 0.7 * materials[(int)GroundMaterial.Gravel] - 0.8 * materials[(int)GroundMaterial.ForestFloor];
                double shade = 1 - 0.75 * Math.Clamp(v.Trees / 0.018, 0, 1); // grass thins out under trees
                double density = Math.Clamp(v.Grass * 1.15 * Math.Clamp(ground, 0, 1) * shade * (1 - DMath.SmoothStep(0.3, 0.45, slope)), 0, 1);
                double flowers = Math.Clamp(biomes[(int)Biome.Grassland] + 0.5 * biomes[(int)Biome.Shrubland] + 0.3 * biomes[(int)Biome.TemperateForest], 0, 1)
                    * (1 - Math.Clamp(v.Trees / 0.02, 0, 1));
                // The forest floor: litter where trees stand (and their ground is litter), small plants in its shade.
                double cover = Math.Clamp(v.Trees / 0.018, 0, 1);
                double litter = Math.Pow(Math.Clamp(cover * 1.1, 0, 1), 1.5) * (0.45 + 0.55 * Math.Clamp(materials[(int)GroundMaterial.ForestFloor] * 2, 0, 1))
                    * (1 - DMath.SmoothStep(0.35, 0.55, slope)) * (s.Height > 0.5 ? 1 : 0);
                double plants = litter * Math.Clamp(0.45 + 0.8 * c.Moisture, 0, 1);
                int k = (b * coarse + a) * Channels;
                climate[k] = (float)density;
                climate[k + 1] = (float)c.Moisture;
                climate[k + 2] = (float)flowers;
                climate[k + 3] = (float)litter;
                climate[k + 4] = (float)plants;
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
                for (int ch = 0; ch < Channels; ch++)
                {
                    float c00 = climate[(b0 * coarse + a0) * Channels + ch], c10 = climate[(b0 * coarse + a0 + 1) * Channels + ch];
                    float c01 = climate[((b0 + 1) * coarse + a0) * Channels + ch], c11 = climate[((b0 + 1) * coarse + a0 + 1) * Channels + ch];
                    float value = (c00 * (1 - u) + c10 * u) * (1 - w) + (c01 * (1 - u) + c11 * u) * w;
                    if (ch < 3)
                    {
                        field.Pixels[p + 1 + ch] = value;
                    }
                    else
                    {
                        field.Pixels2[p + ch - 3] = value;
                    }
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
