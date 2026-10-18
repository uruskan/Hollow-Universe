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
public sealed partial class FloraLibrary : IDisposable
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
    // Construction owns every new resource, including on the legacy path. Successful legacy creation relinquishes
    // this ledger to preserve its existing reference lifetime; opt-in appearance creation retains it for retirement.
    private readonly CreationResources _appearanceResources = new();
    private bool _ownsAppearance, _retiring;
    private IReadOnlyList<Species> _all = Array.Empty<Species>();
    private ShaderMaterial _leaves = null!, _bark = null!, _birchBark = null!, _berries = null!, _rock = null!;
    private Texture2D _atlasTexture = null!;
    private Vector3 _bladeMean;

    public IReadOnlyList<Species> All { get { ThrowIfRetiring(); return _all; } private set => _all = value; }

    public ShaderMaterial Leaves { get { ThrowIfRetiring(); return _leaves; } private set => _leaves = value; }
    public ShaderMaterial Bark { get { ThrowIfRetiring(); return _bark; } private set => _bark = value; }
    public ShaderMaterial BirchBark { get { ThrowIfRetiring(); return _birchBark; } private set => _birchBark = value; }
    public ShaderMaterial Berries { get { ThrowIfRetiring(); return _berries; } private set => _berries = value; }
    public ShaderMaterial Rock { get { ThrowIfRetiring(); return _rock; } private set => _rock = value; }
    public Texture2D AtlasTexture { get { ThrowIfRetiring(); return _atlasTexture; } private set => _atlasTexture = value; }

    /// <summary>Mean linear colour of the painted grass blades (the ground's grass layer is drawn in it).</summary>
    public Vector3 BladeMean { get { ThrowIfRetiring(); return _bladeMean; } private set => _bladeMean = value; }

    public Species? For(ScatterKind kind)
    {
        ThrowIfRetiring();
        return _byKind.TryGetValue(kind, out Species? s) ? s : null;
    }

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
        PopulateGeometry(data);
        return data;
    }

    /// <summary>
    /// Builds the ordinary species geometry without painting or allocating Images. Geometry seeds are already fixed
    /// per kind/variant in Add, independently of the texture recipe seed. Use only with CreateFromAppearance.
    /// The returned Data has no images to dispose; its managed builders may be cleared after native creation.
    /// </summary>
    public static Data BuildGeometry()
    {
        var data = new Data();
        PopulateGeometry(data);
        return data;
    }

    private static void PopulateGeometry(Data data)
    {

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

    /// <summary>
    /// Creates the usual library. Supplying inputs records creation; the caller can then CaptureExport.
    /// Data Images remain caller-owned. An opt-in result owns its newly created native resources until Dispose;
    /// inputs remains caller-owned and must be disposed separately, including on failure.
    /// </summary>
    public static FloraLibrary Create(Data data, TextureLayered groundAlbedo, TextureLayered groundNormal,
        FloraAppearanceInputs? inputs = null)
        => CreateCore(data, groundAlbedo, groundNormal, inputs, null);

    /// <summary>
    /// Uses the five admitted near mip chains and preserved BladeMean with ordinary geometry. Does not paint or bake.
    /// Validates actual creation inputs before returning. The session owns this result and the package separately;
    /// dispose the result only after every view has released its meshes/materials. Shared shaders/ground arrays are borrowed.
    /// The two far images remain in the package for the session's separate impostor upload/retirement path.
    /// </summary>
    public static FloraLibrary CreateFromAppearance(Data geometry, TextureLayered groundAlbedo, TextureLayered groundNormal,
        FloraAppearancePackage approved, FloraAppearanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(approved); ArgumentNullException.ThrowIfNull(inputs);
        return CreateCore(geometry, groundAlbedo, groundNormal, inputs, approved);
    }

    private static FloraLibrary CreateCore(Data data, TextureLayered groundAlbedo, TextureLayered groundNormal,
        FloraAppearanceInputs? inputs, FloraAppearancePackage? approved)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (approved == null && (data.Atlas == null || data.Bark == null || data.BarkNormal == null ||
            data.Birch == null || data.BirchNormal == null))
            throw new ArgumentException("Geometry-only data requires CreateFromAppearance.", nameof(data));
        RenderMemoryTrace.Mark("flora-library:create-begin");
        var lib = new FloraLibrary { _ownsAppearance = inputs != null };
        try
        {
            Shader foliage = GD.Load<Shader>("res://shaders/surface_foliage.gdshader");
            ImageTexture Upload(string role, Image? image)
            {
                ImageTexture texture;
                if (approved != null)
                {
                    FloraAppearanceImage? payload = null;
                    foreach (FloraAppearanceImage candidate in approved.Images)
                        if (candidate.Layout.Role == role) { payload = candidate; break; }
                    texture = inputs!.UploadImage(payload ?? throw new FloraAppearanceValidationException("Missing near image: " + role));
                }
                else texture = inputs == null ? ImageTexture.CreateFromImage(image!) : inputs.UploadImage(role, image!);
                lib.OwnAppearanceResource(texture); return texture;
            }
            lib.AtlasTexture = Upload("near-atlas", data.Atlas);
            string dump = OS.GetEnvironment("HU_FLORA_DUMP");
            if (dump.Length > 0 && approved == null)
            {
                using Image copy = (Image)data.Atlas.Duplicate();
                copy.ClearMipmaps();
                copy.SavePng(dump + "/atlas.png");
                data.Bark.SavePng(dump + "/bark.png");
                data.Birch.SavePng(dump + "/birch.png");
            }
            lib.BladeMean = approved == null ? data.BladeMean
                : new Vector3(approved.BladeMean.R, approved.BladeMean.G, approved.BladeMean.B);
            lib.Leaves = new ShaderMaterial();
            lib.OwnAppearanceResource(lib.Leaves);
            lib.Leaves.Shader = foliage;
            lib.Leaves.SetShaderParameter("mode", 0);
            lib.Leaves.SetShaderParameter("albedo_texture", lib.AtlasTexture);
            lib.Leaves.SetShaderParameter("wind_params", new Vector3(0.22f, 1.15f, 0.035f));
            lib.Leaves.SetShaderParameter("translucency", 0.55f);
            lib.Leaves.SetShaderParameter("roughness", 0.82f);

            lib.Bark = lib.BarkMaterial(foliage, Upload("bark", data.Bark), Upload("bark-normal", data.BarkNormal));
            lib.BirchBark = lib.BarkMaterial(foliage, Upload("birch", data.Birch), Upload("birch-normal", data.BirchNormal));

            lib.Berries = new ShaderMaterial();
            lib.OwnAppearanceResource(lib.Berries);
            lib.Berries.Shader = foliage;
            lib.Berries.SetShaderParameter("mode", 2);
            lib.Berries.SetShaderParameter("wind_params", new Vector3(0.1f, 1.0f, 0.01f));
            lib.Berries.SetShaderParameter("roughness", 0.5f);
            lib.Berries.SetShaderParameter("specular_scale", 1.0f);

            lib.Rock = new ShaderMaterial();
            lib.OwnAppearanceResource(lib.Rock);
            lib.Rock.Shader = GD.Load<Shader>("res://shaders/surface_rock.gdshader");
            lib.Rock.SetShaderParameter("albedo_array", groundAlbedo);
            lib.Rock.SetShaderParameter("normal_array", groundNormal);

            var all = new List<Species>();
            RenderMemoryTrace.Mark("flora-library:procedural-textures-ready");
            IReadOnlyList<Species> authoredTrees = LoadAuthoredTrees(inputs, lib.OwnAppearanceResource, lib.OwnCreationCleanup);
            RenderMemoryTrace.Mark("flora-library:authored-trees-ready");
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
                    ArrayMesh near = lod0.Build(materials, s.Tree ? inputs : null);
                    lib.OwnAppearanceResource(near);
                    ArrayMesh far = lod1.Build(materials, s.Tree ? inputs : null);
                    lib.OwnAppearanceResource(far);
                    species.Variants.Add(new Variant { Lod0 = near, Lod1 = far, Height = height, Extent = extent });
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
            Species rocks = LoadAuthoredRocks(lib.OwnAppearanceResource, lib.OwnCreationCleanup);
            all.Add(rocks);
            lib._byKind[ScatterKind.Rock] = rocks;
            lib.All = all;
            if (approved != null) inputs!.ValidateRuntime(lib, approved.Inputs.RecipeSeed, approved);
            RenderMemoryTrace.Mark("flora-library:create-end");
            if (!lib._ownsAppearance) lib._appearanceResources.Relinquish();
            return lib;
        }
        catch (Exception creationError)
        {
            lib.BeginRetirement();
            try { lib._appearanceResources.Dispose(); }
            catch (Exception cleanupError)
            {
                // The partial library is never returned. Keep only its cleanup owner reachable for a bounded retry.
                throw new CreationCleanupException(creationError, cleanupError, lib._appearanceResources);
            }
            throw;
        }
    }

    private ShaderMaterial BarkMaterial(Shader shader, Texture2D albedo, Texture2D normal)
    {
        var m = new ShaderMaterial();
        OwnAppearanceResource(m);
        m.Shader = shader;
        m.SetShaderParameter("mode", 1);
        m.SetShaderParameter("albedo_texture", albedo);
        m.SetShaderParameter("normal_texture", normal);
        m.SetShaderParameter("wind_params", new Vector3(0.22f, 1.15f, 0.0f));
        m.SetShaderParameter("roughness", 0.92f);
        m.SetShaderParameter("specular_scale", 0.5f);
        return m;
    }

    private void OwnAppearanceResource(Resource resource) => _appearanceResources.Add(resource);
    private void OwnCreationCleanup(IDisposable cleanup) => _appearanceResources.AddCleanup(cleanup);

    private void ThrowIfRetiring() => ObjectDisposedException.ThrowIf(_retiring, this);

    private void BeginRetirement()
    {
        _retiring = true;
        _byKind.Clear(); _all = Array.Empty<Species>();
        _leaves = _bark = _birchBark = _berries = _rock = null!;
        _atlasTexture = null!;
    }

    /// <summary>
    /// Main-thread release after all consumers detach. Attempts every remaining owner once; failures remain owned for
    /// a later Dispose call, and the retiring library cannot be reused. Has no effect on successful legacy libraries.
    /// </summary>
    public void Dispose()
    {
        if (!_ownsAppearance) return;
        if (OS.GetThreadCallerId() != OS.GetMainThreadId()) throw new InvalidOperationException("Flora resources retire on the main thread.");
        BeginRetirement();
        _appearanceResources.Dispose();
    }

    /// <summary>
    /// Creation failed and some native owners could not retire. Retain PendingCleanup and retry Dispose on the main
    /// thread; both the creation error and cleanup errors are preserved. The handle cannot publish a partial library.
    /// </summary>
    public sealed class CreationCleanupException : AggregateException
    {
        internal CreationCleanupException(Exception creationError, Exception cleanupError, IDisposable pendingCleanup)
            : base("Flora creation failed and native cleanup is incomplete.", creationError, cleanupError)
            => PendingCleanup = pendingCleanup;

        public IDisposable PendingCleanup { get; }
    }

    // A finite construction's owners form the retry bound. Retirement rejects new owners and removes successes;
    // neither a retry nor a failed cleanup appends resources, tasks, exception history or a global retry queue.
    private sealed class CreationResources : IDisposable
    {
        private readonly record struct Entry(IDisposable Owner, Resource? Resource, ulong InstanceId);
        private readonly List<Entry> _owners = new();
        private readonly HashSet<ulong> _resourceIds = new();
        private readonly HashSet<IDisposable> _cleanupOwners = new(ReferenceEqualityComparer.Instance);
        private bool _retiring;

        public void Add(Resource resource)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            ulong id = resource.GetInstanceId();
            if (_resourceIds.Add(id)) _owners.Add(new Entry(resource, resource, id));
        }

        public void AddCleanup(IDisposable cleanup)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_cleanupOwners.Add(cleanup)) _owners.Add(new Entry(cleanup, null, 0));
        }

        public void Relinquish()
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _retiring = true;
            _owners.Clear(); _resourceIds.Clear(); _cleanupOwners.Clear();
        }

        public void Dispose()
        {
            if (OS.GetThreadCallerId() != OS.GetMainThreadId())
                throw new InvalidOperationException("Flora resources retire on the main thread.");
            _retiring = true;
            List<Exception>? errors = null;
            for (int i = _owners.Count - 1; i >= 0; i--)
            {
                Entry entry = _owners[i];
                try
                {
                    if (entry.Resource == null || GodotObject.IsInstanceValid(entry.Resource)) entry.Owner.Dispose();
                    // IDs were captured when registered. Never query an object after disposal.
                    if (entry.Resource == null) _cleanupOwners.Remove(entry.Owner);
                    else _resourceIds.Remove(entry.InstanceId);
                    _owners.RemoveAt(i);
                }
                catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            }
            if (errors != null) throw new AggregateException("Some flora resources could not retire; retry Dispose.", errors);
        }
    }
}
