using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// The plants and rocks of the surface view (GROUND.md §4.6), built in code after hollow-orbit's flora: species for a
/// temperate coast (spruce-like firs, pines, birches, oaks and beeches, bushes, red- and blue-berry bushes, ferns,
/// boulders), several seeded variants each, two levels of detail per variant, and the shared materials. Which object
/// stands where comes from the scatter; this only decides what it looks like.
/// </summary>
public sealed partial class FloraLibrary
{
    /// <summary>Distances (metres) at which a species' levels hand over: mesh 0 to mesh 1, mesh 1 to impostor or nothing.</summary>
    public readonly struct Bands
    {
        public Bands(float near, float far, bool impostor)
        {
            Near = near;
            Far = far;
            Impostor = impostor;
        }

        public float Near { get; }
        public float Far { get; }
        public bool Impostor { get; }
    }

    public sealed class Variant
    {
        public ArrayMesh Lod0 = null!;
        public ArrayMesh Lod1 = null!;
        public float Height;
        public float Extent;
        public int ImpostorRow = -1;
    }

    public sealed class Species
    {
        public string Name = "";
        public ScatterKind Kind;
        public float Scale = 1;
        public Bands Bands;
        public bool Tree;
        public readonly List<Variant> Variants = new();
    }

    // Geometry and images built on a worker; meshes and textures are made on the main thread.
    public sealed class Data
    {
        public Image Atlas = null!;
        public Image Bark = null!;
        public Image BarkNormal = null!;
        public Image Birch = null!;
        public Image BirchNormal = null!;
        public Vector3 BladeMean;
        public readonly List<(string Name, ScatterKind Kind, float Scale, Bands Bands, bool Tree, string Bark,
            List<(GeometryBuilder Lod0, GeometryBuilder Lod1, float Height, float Extent)> Variants)> Species = new();
    }

    private readonly Dictionary<ScatterKind, Species> _byKind = new();

    public IReadOnlyList<Species> All { get; private set; } = Array.Empty<Species>();

    public ShaderMaterial Leaves { get; private set; } = null!;
    public ShaderMaterial Bark { get; private set; } = null!;
    public ShaderMaterial BirchBark { get; private set; } = null!;
    public ShaderMaterial Berries { get; private set; } = null!;
    public ShaderMaterial Rock { get; private set; } = null!;
    public Texture2D AtlasTexture { get; private set; } = null!;

    /// <summary>Mean linear colour of the painted grass blades (the ground's grass layer is drawn in it).</summary>
    public Vector3 BladeMean { get; private set; }

    public Species? For(ScatterKind kind) => _byKind.TryGetValue(kind, out Species? s) ? s : null;

    /// <summary>Kinds the scatter never makes: the red and blue berry bushes picked bare (the ground game draws them).</summary>
    public const ScatterKind BareBerryRed = (ScatterKind)253, BareBerryBlue = (ScatterKind)254;

    // ---------------------------------------------------------------- data (worker thread)

    /// <summary>Paints the textures and builds every variant's geometry. Deterministic per seed; a second or two.</summary>
    public static Data Build(int seed)
    {
        var data = new Data { Atlas = FloraTextures.Atlas(seed) };
        data.BladeMean = MeanColour(data.Atlas, FloraTextures.Grass) * 0.75f; // times the tufts' mean occlusion
        (data.Bark, data.BarkNormal) = FloraTextures.Bark(false, seed + 1);
        (data.Birch, data.BirchNormal) = FloraTextures.Bark(true, seed + 2);

        // The trees' bands at plant distance 1: the vegetation scales every band by the setting as it draws.
        var trees = new Bands(50, 190, impostor: true);
        Add(data, "fir", ScatterKind.Fir, 1.4f, trees, true, "bark", 3, (s, lod) =>
        {
            var r = new Rng(Hash64.Of((ulong)s, 0x46495220)); // "FIR "
            float R() => (float)r.NextDouble();
            return FloraGeometry.ConiferTree(new FloraGeometry.Conifer
            {
                Seed = s, H = 13 * (0.9f + 0.2f * R()), RBase = 0.3f, CrownStart = 0.07f + 0.06f * R(), Whorls = 13 + (int)(R() * 3),
                PerWhorl = 7, LenMax = 3.0f * (0.9f + 0.2f * R()), LenMin = 0.55f, Droop = 0.32f * (0.85f + 0.3f * R()),
                Pitch = -14 + (R() - 0.5f) * 6, Cell = FloraTextures.Spruce, Shape = 1.0f, WidthK = 0.5f,
                BarkLow = new Vector3(0.44f, 0.36f, 0.3f), BarkHigh = new Vector3(0.55f, 0.42f, 0.33f),
            }, lod);
        });
        Add(data, "pine", ScatterKind.Pine, 1.2f, trees, true, "bark", 3, (s, lod) =>
        {
            var r = new Rng(Hash64.Of((ulong)s, 0x50494E45)); // "PINE"
            float R() => (float)r.NextDouble();
            return FloraGeometry.ConiferTree(new FloraGeometry.Conifer
            {
                Seed = s, H = 16 * (0.9f + 0.2f * R()), RBase = 0.34f, CrownStart = 0.46f + 0.12f * R(), Whorls = 9 + (int)(R() * 3),
                PerWhorl = 6, LenMax = 3.3f * (0.9f + 0.2f * R()), LenMin = 0.7f, Droop = 0.22f, Pitch = -6 + (R() - 0.5f) * 6,
                Cell = FloraTextures.Pine, WidthK = 0.45f, Shape = 0.8f + 0.4f * R(),
                BarkLow = new Vector3(0.5f, 0.4f, 0.33f), BarkHigh = new Vector3(0.95f, 0.58f, 0.36f),
            }, lod);
        });
        Add(data, "birch", ScatterKind.Birch, 1.25f, trees, true, "birch", 3, (s, lod) =>
        {
            var r = new Rng(Hash64.Of((ulong)s, 0x42495243)); // "BIRC"
            float R() => (float)r.NextDouble();
            return FloraGeometry.BroadleafTree(new FloraGeometry.Broadleaf
            {
                Seed = s, H = 12 * (0.9f + 0.2f * R()), RBase = 0.2f, TrunkH = 0.5f + 0.1f * R(), Limbs = 4, Clusters = 32 + (int)(R() * 6),
                ClusterSize = 2.4f, CanopyR = 2.3f * (0.85f + 0.3f * R()), CanopyH = 3.6f, CanopyY = 0.72f,
                CellA = FloraTextures.BirchLeaf, CellB = FloraTextures.BirchLeaf, BarkColour = new Vector3(1, 1, 1), Birch = true,
            }, lod);
        });
        Add(data, "broadleaf", ScatterKind.Broadleaf, 1.3f, trees, true, "bark", 4, (s, lod) =>
        {
            var r = new Rng(Hash64.Of((ulong)s, 0x4F414B20)); // "OAK "
            float R() => (float)r.NextDouble();
            bool beech = (s & 1) == 1;
            return FloraGeometry.BroadleafTree(new FloraGeometry.Broadleaf
            {
                Seed = s, H = (beech ? 13 : 11) * (0.9f + 0.2f * R()), RBase = beech ? 0.36f : 0.42f, TrunkH = beech ? 0.45f : 0.4f,
                Limbs = 5, Clusters = 28 + (int)(R() * 6), ClusterSize = 3.0f, CanopyR = (beech ? 4.0f : 4.4f) * (0.9f + 0.2f * R()),
                CanopyH = beech ? 3.6f : 3.1f, CanopyY = 0.7f,
                CellA = beech ? FloraTextures.Beech : FloraTextures.Broad, CellB = beech ? FloraTextures.Broad : FloraTextures.OakDark,
                BarkColour = beech ? new Vector3(0.62f, 0.6f, 0.56f) : new Vector3(0.55f, 0.47f, 0.4f),
            }, lod);
        });
        var shrubs = new Bands(30, 120, impostor: false);
        Add(data, "bush", ScatterKind.Bush, 1.0f, shrubs, false, "bark", 3, (s, lod) =>
            FloraGeometry.Bush(new FloraGeometry.Shrub { Seed = s, R = 1.0f, H = 1.25f, Cards = 13, Size = 1.1f, Cell = FloraTextures.Bush }, lod));
        Add(data, "berry_red", ScatterKind.BerryRed, 1.0f, shrubs, false, "bark", 2, (s, lod) =>
            FloraGeometry.Bush(new FloraGeometry.Shrub
            {
                Seed = s, R = 0.85f, H = 1.05f, Cards = 12, Size = 0.95f, Cell = FloraTextures.Berry, Berries = new Vector3(0.55f, 0.03f, 0.04f),
            }, lod));
        Add(data, "berry_blue", ScatterKind.BerryBlue, 1.0f, shrubs, false, "bark", 2, (s, lod) =>
            FloraGeometry.Bush(new FloraGeometry.Shrub
            {
                Seed = s, R = 0.85f, H = 1.05f, Cards = 12, Size = 0.95f, Cell = FloraTextures.Berry, Berries = new Vector3(0.05f, 0.06f, 0.2f),
            }, lod));
        // The berry bushes picked bare (drawn by the ground game, never scattered): the same bushes, without berries.
        Add(data, "berry_red_bare", BareBerryRed, 1.0f, shrubs, false, "bark", 2, (s, lod) =>
            FloraGeometry.Bush(new FloraGeometry.Shrub { Seed = s, R = 0.85f, H = 1.05f, Cards = 12, Size = 0.95f, Cell = FloraTextures.Berry }, lod),
            seedKind: ScatterKind.BerryRed);
        Add(data, "berry_blue_bare", BareBerryBlue, 1.0f, shrubs, false, "bark", 2, (s, lod) =>
            FloraGeometry.Bush(new FloraGeometry.Shrub { Seed = s, R = 0.85f, H = 1.05f, Cards = 12, Size = 0.95f, Cell = FloraTextures.Berry }, lod),
            seedKind: ScatterKind.BerryBlue);
        Add(data, "fern", ScatterKind.Fern, 1.0f, new Bands(24, 75, impostor: false), false, "bark", 3, (s, lod) => FloraGeometry.Fern(s, lod));
        // Ordinary rocks are authored scans loaded on the main thread, not procedural ellipsoids.
        return data;
    }

    // Mean linear colour of the opaque texels of an atlas cell.
    private static Vector3 MeanColour(Image atlas, (int X, int Y) cell)
    {
        byte[] px = atlas.GetData();
        int size = atlas.GetWidth(), cs = FloraTextures.Cell;
        double r = 0, g = 0, b = 0, n = 0;
        for (int y = cell.Y * cs; y < (cell.Y + 1) * cs; y++)
        {
            for (int x = cell.X * cs; x < (cell.X + 1) * cs; x++)
            {
                int k = (y * size + x) * 4;
                if (px[k + 3] < 128)
                {
                    continue;
                }
                r += Linear(px[k]);
                g += Linear(px[k + 1]);
                b += Linear(px[k + 2]);
                n++;
            }
        }
        return n > 0 ? new Vector3((float)(r / n), (float)(g / n), (float)(b / n)) : new Vector3(0.5f, 0.6f, 0.35f);
    }

    private static double Linear(byte v)
    {
        double c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static void Add(Data data, string name, ScatterKind kind, float scale, Bands bands, bool tree, string bark, int variants,
        Func<int, bool, GeometryBuilder> build, ScatterKind? seedKind = null)
    {
        var list = new List<(GeometryBuilder, GeometryBuilder, float, float)>();
        for (int v = 0; v < variants; v++)
        {
            int seed = (int)(Hash64.Of(0x464C4F52, (ulong)(seedKind ?? kind), (ulong)v) & 0x7FFFFFFF);
            GeometryBuilder lod0 = build(seed, false);
            GeometryBuilder lod1 = build(seed, true);
            (float height, float extent) = lod0.Size();
            list.Add((lod0, lod1, height, extent));
        }
        data.Species.Add((name, kind, scale, bands, tree, bark, list));
    }

    // ---------------------------------------------------------------- resources (main thread)

    public static FloraLibrary Create(Data data, TextureLayered groundAlbedo, TextureLayered groundNormal)
    {
        var lib = new FloraLibrary();
        Shader foliage = GD.Load<Shader>("res://shaders/surface_foliage.gdshader");
        lib.AtlasTexture = ImageTexture.CreateFromImage(data.Atlas);
        string dump = OS.GetEnvironment("HU_FLORA_DUMP");
        if (dump.Length > 0)
        {
            Image copy = (Image)data.Atlas.Duplicate();
            copy.ClearMipmaps();
            copy.SavePng(dump + "/atlas.png");
            data.Bark.SavePng(dump + "/bark.png");
            data.Birch.SavePng(dump + "/birch.png");
        }
        lib.BladeMean = data.BladeMean;
        lib.Leaves = new ShaderMaterial { Shader = foliage };
        lib.Leaves.SetShaderParameter("mode", 0);
        lib.Leaves.SetShaderParameter("albedo_texture", lib.AtlasTexture);
        lib.Leaves.SetShaderParameter("wind_params", new Vector3(0.22f, 1.15f, 0.035f));
        lib.Leaves.SetShaderParameter("translucency", 0.55f);
        lib.Leaves.SetShaderParameter("roughness", 0.82f);

        lib.Bark = BarkMaterial(foliage, data.Bark, data.BarkNormal);
        lib.BirchBark = BarkMaterial(foliage, data.Birch, data.BirchNormal);

        lib.Berries = new ShaderMaterial { Shader = foliage };
        lib.Berries.SetShaderParameter("mode", 2);
        lib.Berries.SetShaderParameter("wind_params", new Vector3(0.1f, 1.0f, 0.01f));
        lib.Berries.SetShaderParameter("roughness", 0.5f);
        lib.Berries.SetShaderParameter("specular_scale", 1.0f);

        lib.Rock = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_rock.gdshader") };
        lib.Rock.SetShaderParameter("albedo_array", groundAlbedo);
        lib.Rock.SetShaderParameter("normal_array", groundNormal);

        var all = new List<Species>();
        IReadOnlyList<Species> authoredTrees = LoadAuthoredTrees();
        var authoredKinds = new HashSet<ScatterKind>();
        foreach (Species tree in authoredTrees) authoredKinds.Add(tree.Kind);
        foreach (var s in data.Species)
        {
            if (authoredKinds.Contains(s.Kind)) continue;
            var species = new Species { Name = s.Name, Kind = s.Kind, Scale = s.Scale, Bands = s.Bands, Tree = s.Tree };
            Material?[] materials = s.Bark switch
            {
                "rock" => new Material?[] { lib.Rock },
                "birch" => new Material?[] { lib.BirchBark, lib.Leaves, lib.Berries },
                _ => new Material?[] { lib.Bark, lib.Leaves, lib.Berries },
            };
            foreach ((GeometryBuilder lod0, GeometryBuilder lod1, float height, float extent) in s.Variants)
            {
                species.Variants.Add(new Variant { Lod0 = lod0.Build(materials), Lod1 = lod1.Build(materials), Height = height, Extent = extent });
            }
            all.Add(species);
            lib._byKind[s.Kind] = species;
        }
        // Authored rows stay together after legacy species for the shared far
        // atlas. Existing scatter, fall and refresh paths consume this library.
        foreach (Species tree in authoredTrees)
        {
            all.Add(tree);
            lib._byKind[tree.Kind] = tree;
        }
        Species rocks = LoadAuthoredRocks();
        all.Add(rocks);
        lib._byKind[ScatterKind.Rock] = rocks;
        lib.All = all;
        return lib;
    }

    private static ShaderMaterial BarkMaterial(Shader shader, Image albedo, Image normal)
    {
        var m = new ShaderMaterial { Shader = shader };
        m.SetShaderParameter("mode", 1);
        m.SetShaderParameter("albedo_texture", ImageTexture.CreateFromImage(albedo));
        m.SetShaderParameter("normal_texture", ImageTexture.CreateFromImage(normal));
        m.SetShaderParameter("wind_params", new Vector3(0.22f, 1.15f, 0.0f));
        m.SetShaderParameter("roughness", 0.92f);
        m.SetShaderParameter("specular_scale", 0.5f);
        return m;
    }
}
