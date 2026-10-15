using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

/// <summary>
/// The grass of the surface view (GROUND.md §4.6, docs/WHOLE_WORLD.md §10.3): presentation only, not part of the scatter,
/// and never plant models: tufts of blade cards placed and shaped on the GPU (surface_grass.gdshader) around the camera.
/// Every tuft belongs to a cell of a cube face at the level nearest 0.46 m (level 13 on a 2.41 km world), so what grows
/// in it (place, size, lean, colour, kind) comes from a hash of the cell and never shifts as the camera moves; near a
/// face seam each face's cells are drawn by a grid of their own (up to three at a corner), each keeping the cells whose
/// middles lie on its face. A worker lays out a lattice of the cells' corners around the camera whenever it has moved
/// <see cref="Recentre"/> metres: each corner on the drawn ground (exactly the finest tiles' triangles), and the grass
/// density, moisture, flowers, litter and small plants from <see cref="ISurface.Cover"/>, as textures. A second, smaller
/// grid lays leaf litter, needles and twigs flat on the forest floor from the same lattice.
/// </summary>
public sealed partial class GrassField : Node3D
{
    /// <summary>The cells are the cube faces' at the level whose edge is nearest this (metres).</summary>
    public const double CellMetres = 0.46;

    public const float FarRadius = 42;
    private const float ShrinkStart = 22;
    private const float LitterRadius = 26;
    private const float LitterShrink = 14;

    // The reaches at the graphics settings' grass distance (GROUND.md §8.6), taken when the field is set up: the
    // surface view builds a new field when the setting changes.
    private float _far = FarRadius, _shrink = ShrinkStart, _litterFar = LitterRadius, _litterShrink = LitterShrink;

    /// <summary>The grass distance and density this field was built with.</summary>
    public (float Distance, float Density) BuiltWith { get; private set; }
    private const double Recentre = 14;
    private const int CoarseStep = 8; // lattice nodes between climate samples

    /// <summary>The finest drawn tiles have this many cells along an edge (TileMesh: 32, a power of two).</summary>
    private const int TileCellsLog2 = 5;

    // One face's lattice: its corner nodes' places on the ground (from the anchor, render axes, and the grass density)
    // and the cover (moisture, flowers, litter, small plants), around the camera.
    private sealed class FaceLattice
    {
        public int Face;
        public long CornerX, CornerY;   // the face's cell index of the lattice's first cell
        public double OriginU, OriginV; // metres on the face of the first node, less the texture anchor
        public float[] Nodes = Array.Empty<float>();
        public float[] Cover = Array.Empty<float>();
    }

    private sealed class FieldData
    {
        public Vector3D Anchor; // body-frame point the nodes are measured from
        public readonly List<FaceLattice> Faces = new();
    }

    // What draws one face's cells: the grass and the litter grids, and the face's lattice textures.
    private sealed class FaceGrid
    {
        public MultiMeshInstance3D Grass = null!;
        public ShaderMaterial GrassMaterial = null!;
        public MultiMeshInstance3D Litter = null!;
        public ShaderMaterial LitterMaterial = null!;
        public Image? Nodes, Cover;
        public ImageTexture? NodesTexture, CoverTexture;
        public FaceLattice? Lattice;
    }

    private ISurface _surface = null!;
    private RenderFrame _frame = null!;
    private int _level;        // the cells' level
    private long _faceCells;   // cells along a face's edge
    private double _cellMetres;
    private int _heightShift;  // the finest tiles' vertex lattice: level + 5
    private int _gridWidth, _litterWidth, _latticeSize;
    private readonly FaceGrid[] _grids = new FaceGrid[3];
    private Task<FieldData>? _job;
    private FieldData? _field;
    private Vector3D _centre = new(double.MaxValue, 0, 0);
    private bool _near;
    private bool _ready;

    /// <summary>Tufts on the grids (most of them shrink to nothing where there is no grass).</summary>
    public int Tufts => _ready ? _gridWidth * _gridWidth * (_field?.Faces.Count ?? 0) : 0;

    /// <summary>True when the field around the camera is in place (or the camera is too high for grass).</summary>
    public bool IsIdle => !_near || (_ready && _job == null);

    /// <summary>The cube-sphere level of the grass's cells.</summary>
    public int Level => _level;

    public void Setup(ISurface surface, RenderFrame frame, int finestLevel, FloraLibrary library, TextureLayered groundAlbedo, Texture noise, Vector3 bladeMean)
    {
        _surface = surface;
        _frame = frame;
        _level = TileKey.LevelFor(surface.Radius, CellMetres);
        _faceCells = 1L << _level;
        _cellMetres = TileKey.EdgeLength(_level, surface.Radius);
        _heightShift = finestLevel + TileCellsLog2;
        HollowUniverse.Settings.GraphicsValues graphics = HollowUniverse.Settings.GraphicsSettings.Current;
        BuiltWith = (graphics.GrassDistance, graphics.GrassDensity);
        float reach = Math.Clamp(graphics.GrassDistance, 0.5f, 2f);
        _far = FarRadius * reach;
        _shrink = ShrinkStart * reach;
        _litterFar = LitterRadius * reach;
        _litterShrink = LitterShrink * reach;
        _gridWidth = (int)Math.Ceiling(2 * _far / _cellMetres) + 2;
        _litterWidth = (int)Math.Ceiling(2 * _litterFar / _cellMetres) + 2;
        _latticeSize = (int)Math.Ceiling(2 * (_far + Recentre) / _cellMetres) + 4;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_grass.gdshader") };
        material.SetShaderParameter("grid_width", _gridWidth);
        material.SetShaderParameter("far_radius", _far);
        material.SetShaderParameter("shrink_start", _shrink);
        material.SetShaderParameter("density_scale", Math.Clamp(graphics.GrassDensity, 0.1f, 1f));
        material.SetShaderParameter("lattice_size", _latticeSize);
        material.SetShaderParameter("face_cells", (int)Math.Min(_faceCells, int.MaxValue));
        material.SetShaderParameter("cell_metres", (float)(_cellMetres));
        material.SetShaderParameter("atlas", library.AtlasTexture);
        material.SetShaderParameter("cell_grass", FloraTextures.CellUv(FloraTextures.Grass, 6));
        material.SetShaderParameter("cell_dry", FloraTextures.CellUv(FloraTextures.DryGrass, 6));
        material.SetShaderParameter("cell_meadow", FloraTextures.CellUv(FloraTextures.Meadow, 6));
        material.SetShaderParameter("cell_flower", FloraTextures.CellUv(FloraTextures.Flower, 6));
        material.SetShaderParameter("cell_herb", FloraTextures.CellUv(FloraTextures.Herb, 6));
        material.SetShaderParameter("cell_fern", FloraTextures.CellUv(FloraTextures.Fern, 6));
        material.SetShaderParameter("cell_bush", FloraTextures.CellUv(FloraTextures.Bush, 6));
        material.SetShaderParameter("cell_leaves_a", FloraTextures.CellUv(FloraTextures.Broad, 6));
        material.SetShaderParameter("cell_leaves_b", FloraTextures.CellUv(FloraTextures.Beech, 6));
        material.SetShaderParameter("cell_leaves_c", FloraTextures.CellUv(FloraTextures.OakDark, 6));
        material.SetShaderParameter("cell_leaves_d", FloraTextures.CellUv(FloraTextures.BirchLeaf, 6));
        material.SetShaderParameter("cell_twigs", FloraTextures.CellUv(FloraTextures.Twigs, 6));
        material.SetShaderParameter("cell_needles", FloraTextures.CellUv(FloraTextures.Spruce, 6));
        material.SetShaderParameter("ground_albedo", groundAlbedo);
        material.SetShaderParameter("macro_noise", noise);
        material.SetShaderParameter("blade_mean", bladeMean);
        Mesh tuft = TuftMesh(), litter = LitterMesh();
        for (int g = 0; g < _grids.Length; g++)
        {
            var grid = new FaceGrid { GrassMaterial = (ShaderMaterial)material.Duplicate() };
            grid.Grass = Instance(tuft, grid.GrassMaterial, _gridWidth);
            // Litter: the same lattice and hashing on a smaller grid, flat pieces instead of tufts.
            grid.LitterMaterial = (ShaderMaterial)material.Duplicate();
            grid.LitterMaterial.SetShaderParameter("layer", 1);
            grid.LitterMaterial.SetShaderParameter("grid_width", _litterWidth);
            grid.LitterMaterial.SetShaderParameter("far_radius", _litterFar);
            grid.LitterMaterial.SetShaderParameter("shrink_start", _litterShrink);
            grid.Litter = Instance(litter, grid.LitterMaterial, _litterWidth);
            _grids[g] = grid;
        }
    }

    // A grid of width² instances, every instance placed by the shader (the transforms are identity).
    private MultiMeshInstance3D Instance(Mesh mesh, ShaderMaterial material, int width)
    {
        int count = width * width;
        var buffer = new float[count * 12];
        for (int i = 0; i < count; i++)
        {
            buffer[i * 12] = 1;
            buffer[i * 12 + 5] = 1;
            buffer[i * 12 + 10] = 1;
        }
        var instance = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = mesh,
                InstanceCount = count,
                Buffer = buffer,
            },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Visible = false,
        };
        AddChild(instance);
        return instance;
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
        mesh.CustomAabb = new Aabb(new Vector3(-6000, -6000, -6000), new Vector3(12000, 12000, 12000));
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
        // Placed by the shader anywhere: never culled by the mesh's own small box.
        mesh.CustomAabb = new Aabb(new Vector3(-6000, -6000, -6000), new Vector3(12000, 12000, 12000));
        return mesh;
    }

    // ---------------------------------------------------------------- update

    /// <summary>
    /// Moves the grids with the camera (body-frame position, <paramref name="altitude"/> metres above the ground) and
    /// lays out the lattice again when it has moved far enough.
    /// </summary>
    public void UpdateGrass(Vector3D camera, double altitude, bool walking)
    {
        bool near = altitude < _far;
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
        Vector3D ground = camera.Normalized * (_surface.Radius + _surface.HeightAt(camera.Normalized));
        if (near && _job == null && Vector3D.Distance(ground, _centre) > Recentre)
        {
            _centre = ground;
            _job = Task.Run(() => Build(ground));
        }
        _ready = _field != null;
        bool show = near && _field != null;
        Vector3 cameraRender = _frame.ToRender(camera);
        for (int g = 0; g < _grids.Length; g++)
        {
            FaceGrid grid = _grids[g];
            FaceLattice? lattice = show && g < _field!.Faces.Count ? _field.Faces[g] : null;
            grid.Grass.Visible = grid.Litter.Visible = lattice != null;
            if (lattice == null)
            {
                continue;
            }
            Vector3 anchor = _frame.ToRender(_field!.Anchor);
            (double cu, double cv) = CellOn(lattice.Face, camera);
            foreach ((ShaderMaterial material, int width) in new[] { (grid.GrassMaterial, _gridWidth), (grid.LitterMaterial, _litterWidth) })
            {
                // The grid's first cell in the lattice: the camera's cell less half the grid, kept inside the lattice.
                int ox = (int)Math.Clamp(Math.Floor(cu) - width / 2 - lattice.CornerX, 0, _latticeSize - 1 - width);
                int oy = (int)Math.Clamp(Math.Floor(cv) - width / 2 - lattice.CornerY, 0, _latticeSize - 1 - width);
                material.SetShaderParameter("grid_offset", new Vector2I(ox, oy));
                material.SetShaderParameter("anchor", anchor);
            }
            // Grass bends away from the walker's feet, an eye's height under the camera.
            Vector3 feet = cameraRender - _frame.DirectionToRender(camera.Normalized) * 1.7f;
            grid.GrassMaterial.SetShaderParameter("trample", walking ? new Vector4(feet.X, feet.Y, feet.Z, 0.9f) : Vector4.Zero);
        }
    }

    // A body-frame point's place on a face, in cells (fractional; may lie beyond the face's edge).
    private (double U, double V) CellOn(int face, Vector3D point)
    {
        Vector3D n = CubeSphere.Normal(face);
        double dn = Vector3D.Dot(point, n);
        if (dn <= 1e-9)
        {
            return (double.NaN, double.NaN);
        }
        double u = CubeSphere.Unwarp(Vector3D.Dot(point, CubeSphere.AxisU(face)) / dn);
        double v = CubeSphere.Unwarp(Vector3D.Dot(point, CubeSphere.AxisV(face)) / dn);
        return ((u + 1) * 0.5 * _faceCells, (v + 1) * 0.5 * _faceCells);
    }

    private void Upload(FieldData field)
    {
        for (int g = 0; g < _grids.Length && g < field.Faces.Count; g++)
        {
            FaceGrid grid = _grids[g];
            FaceLattice lattice = field.Faces[g];
            grid.Lattice = lattice;
            byte[] nodes = new byte[lattice.Nodes.Length * 4];
            Buffer.BlockCopy(lattice.Nodes, 0, nodes, 0, nodes.Length);
            byte[] cover = new byte[lattice.Cover.Length * 4];
            Buffer.BlockCopy(lattice.Cover, 0, cover, 0, cover.Length);
            int n = _latticeSize;
            if (grid.Nodes == null || grid.NodesTexture == null || grid.Cover == null || grid.CoverTexture == null)
            {
                grid.Nodes = Image.CreateFromData(n, n, false, Image.Format.Rgbaf, nodes);
                grid.NodesTexture = ImageTexture.CreateFromImage(grid.Nodes);
                grid.Cover = Image.CreateFromData(n, n, false, Image.Format.Rgbaf, cover);
                grid.CoverTexture = ImageTexture.CreateFromImage(grid.Cover);
                foreach (ShaderMaterial material in new[] { grid.GrassMaterial, grid.LitterMaterial })
                {
                    material.SetShaderParameter("lattice", grid.NodesTexture);
                    material.SetShaderParameter("lattice_cover", grid.CoverTexture);
                }
            }
            else
            {
                grid.Nodes.SetData(n, n, false, Image.Format.Rgbaf, nodes);
                grid.NodesTexture.Update(grid.Nodes);
                grid.Cover.SetData(n, n, false, Image.Format.Rgbaf, cover);
                grid.CoverTexture.Update(grid.Cover);
            }
            foreach (ShaderMaterial material in new[] { grid.GrassMaterial, grid.LitterMaterial })
            {
                material.SetShaderParameter("face", lattice.Face);
                material.SetShaderParameter("lattice_cell", new Vector2I((int)lattice.CornerX, (int)lattice.CornerY));
                material.SetShaderParameter("face_origin", new Vector2((float)lattice.OriginU, (float)lattice.OriginV));
            }
        }
    }

    // ---------------------------------------------------------------- the lattice (worker thread)

    private FieldData Build(Vector3D anchor)
    {
        var field = new FieldData { Anchor = anchor };
        var heights = new Dictionary<(int, long, long), double>();
        foreach (int face in FacesNear(anchor))
        {
            if (field.Faces.Count < _grids.Length)
            {
                field.Faces.Add(BuildFace(face, anchor, heights));
            }
        }
        return field;
    }

    // The faces whose cells lie within the lattice's reach of a point: its own, and those across a seam or a corner near
    // it (found by sampling the square around it on its own face, whose coordinates run on past the edge).
    private List<int> FacesNear(Vector3D point)
    {
        (int face, double u, double v) = CubeSphere.FromDirection(point);
        var faces = new List<int> { face };
        double reach = (_latticeSize / 2 + 1) * 2.0 / _faceCells;
        for (int j = -4; j <= 4; j++)
        {
            for (int i = -4; i <= 4; i++)
            {
                Vector3D d = CubeSphere.Direction(face, u + reach * i / 4, v + reach * j / 4);
                int other = CubeSphere.FromDirection(d).Face;
                if (!faces.Contains(other))
                {
                    faces.Add(other);
                }
            }
        }
        return faces;
    }

    private FaceLattice BuildFace(int face, Vector3D anchor, Dictionary<(int, long, long), double> heights)
    {
        int n = _latticeSize;
        (double cu, double cv) = CellOn(face, anchor);
        var lattice = new FaceLattice
        {
            Face = face,
            CornerX = (long)Math.Floor(cu) - n / 2,
            CornerY = (long)Math.Floor(cv) - n / 2,
            Nodes = new float[n * n * 4],
            Cover = new float[n * n * 4],
        };
        double span = 2.0 / _faceCells;
        double U(long cell) => -1 + cell * span;
        (double ou, double ov) = TileMesh.FaceMetresOf(U(lattice.CornerX), U(lattice.CornerY), _surface.Radius);
        lattice.OriginU = ou - Math.Round(ou / TileMesh.TexturePeriod) * TileMesh.TexturePeriod;
        lattice.OriginV = ov - Math.Round(ov / TileMesh.TexturePeriod) * TileMesh.TexturePeriod;
        var points = new Vector3D[n * n];
        for (int b = 0; b < n; b++)
        {
            for (int a = 0; a < n; a++)
            {
                Vector3D d = CubeSphere.Direction(face, U(lattice.CornerX + a), U(lattice.CornerY + b));
                Vector3D p = d * (_surface.Radius + TriangleHeight(d, heights));
                points[b * n + a] = p;
                Vector3 local = _frame.DirectionToRender(p - anchor);
                int k = (b * n + a) * 4;
                lattice.Nodes[k] = local.X;
                lattice.Nodes[k + 1] = local.Y;
                lattice.Nodes[k + 2] = local.Z;
            }
        }
        // Grass density, moisture, flowers, litter and small plants on a coarser grid, then spread bilinearly.
        int coarse = (n - 1) / CoarseStep + 2;
        const int Channels = 5;
        var climate = new float[coarse * coarse * Channels];
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        double seaLevel = _surface.SeaRadius > 0 ? _surface.SeaRadius - _surface.Radius : 0;
        for (int b = 0; b < coarse; b++)
        {
            for (int a = 0; a < coarse; a++)
            {
                int ta = Math.Min(a * CoarseStep, n - 1), tb = Math.Min(b * CoarseStep, n - 1);
                Vector3D p = points[tb * n + ta];
                Vector3D d = p.Normalized;
                SurfaceSample s = _surface.Sample(d);
                // Slope from the lattice's own points, over a couple of metres.
                int a0 = Math.Max(ta - 2, 0), a1 = Math.Min(ta + 2, n - 1), b0 = Math.Max(tb - 2, 0), b1 = Math.Min(tb + 2, n - 1);
                Vector3D normal = Vector3D.Cross(points[tb * n + a1] - points[tb * n + a0], points[b1 * n + ta] - points[b0 * n + ta]).Normalized;
                double cos = Math.Abs(Vector3D.Dot(normal, d));
                double slope = Math.Sqrt(Math.Max(0, 1 - cos * cos));
                GroundCover c = _surface.Cover(d, s, slope, biomes, materials);
                BiomeTable.FoldFamilies(materials);
                double ground = 1 - materials[(int)GroundMaterial.Rock] - materials[(int)GroundMaterial.Sand] - materials[(int)GroundMaterial.Snow]
                    - 0.7 * materials[(int)GroundMaterial.Gravel] - 0.8 * materials[(int)GroundMaterial.ForestFloor];
                double shade = 1 - 0.75 * Math.Clamp(c.Trees / 0.018, 0, 1); // grass thins out under trees
                double density = Math.Clamp(c.Grass * 1.15 * Math.Clamp(ground, 0, 1) * shade * (1 - DMath.SmoothStep(0.3, 0.45, slope)), 0, 1);
                double flowers = Math.Clamp(biomes[(int)Biome.Grassland] + 0.5 * biomes[(int)Biome.Shrubland] + 0.3 * biomes[(int)Biome.TemperateForest], 0, 1)
                    * (1 - Math.Clamp(c.Trees / 0.02, 0, 1));
                // The forest floor: litter where trees stand (and their ground is litter), small plants in its shade.
                double cover = Math.Clamp(c.Trees / 0.018, 0, 1);
                double litter = Math.Pow(Math.Clamp(cover * 1.1, 0, 1), 1.5) * (0.45 + 0.55 * Math.Clamp(materials[(int)GroundMaterial.ForestFloor] * 2, 0, 1))
                    * (1 - DMath.SmoothStep(0.35, 0.55, slope)) * (s.Height - seaLevel > 0.5 ? 1 : 0);
                double plants = litter * Math.Clamp(0.45 + 0.8 * c.Moisture, 0, 1);
                int k = (b * coarse + a) * Channels;
                climate[k] = (float)density;
                climate[k + 1] = (float)c.Moisture;
                climate[k + 2] = (float)flowers;
                climate[k + 3] = (float)litter;
                climate[k + 4] = (float)plants;
            }
        }
        for (int b = 0; b < n; b++)
        {
            for (int a = 0; a < n; a++)
            {
                float fa = a / (float)CoarseStep, fb = b / (float)CoarseStep;
                int a0 = Math.Min((int)fa, coarse - 2), b0 = Math.Min((int)fb, coarse - 2);
                float u = fa - a0, w = fb - b0;
                int p = (b * n + a) * 4;
                for (int ch = 0; ch < Channels; ch++)
                {
                    float c00 = climate[(b0 * coarse + a0) * Channels + ch], c10 = climate[(b0 * coarse + a0 + 1) * Channels + ch];
                    float c01 = climate[((b0 + 1) * coarse + a0) * Channels + ch], c11 = climate[((b0 + 1) * coarse + a0 + 1) * Channels + ch];
                    float value = (c00 * (1 - u) + c10 * u) * (1 - w) + (c01 * (1 - u) + c11 * u) * w;
                    if (ch == 0)
                    {
                        lattice.Nodes[p + 3] = value;
                    }
                    else
                    {
                        lattice.Cover[p + ch - 1] = value;
                    }
                }
            }
        }
        return lattice;
    }

    // Height above the mean radius of the drawn ground along a direction: the finest tiles' triangles, which follow the
    // surface's heights at their vertex lattice (TileMesh: 32 cells a tile, the same diagonal everywhere), interpolated.
    private double TriangleHeight(Vector3D d, Dictionary<(int, long, long), double> cache)
    {
        double n = 1L << _heightShift;
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

    /// <summary>Nothing to re-anchor: the anchor's render position is set every frame.</summary>
    public void Reanchor()
    {
    }
}
