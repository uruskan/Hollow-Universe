using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Trees, bushes, ferns and rocks of the surface view (GROUND.md §4.6), from the surface generator's scatter: the
/// same objects the server will know by tile and index. Near the camera every object is a mesh (two levels of detail),
/// grouped per model into MultiMeshes rebuilt as the camera moves; farther out trees are impostors, batched per block
/// of scatter tiles, out to a few kilometres. Shaders pick each instance's level by its own distance and cross-fade.
/// </summary>
public sealed partial class Vegetation : Node3D
{
    /// <summary>Meshes up to this distance (metres); impostors beyond.</summary>
    public const float MeshRange = 200;
    public const float NearRange = 48;
    private const float ImpostorRange = 3200;
    private const double NearSetRadius = 300;
    private const double RebuildDistance = 10;
    private const int BlockLevel = Scatter.Level - 3; // eight scatter tiles a side

    private sealed class Plant
    {
        public string Name = "";
        public string Kind = "";
        public Mesh Near = null!;
        public Mesh Mid = null!;
        public float Height;
        public float Extent;
        public int ImpostorRow = -1;
        public MultiMeshInstance3D? NearInstance;
        public MultiMeshInstance3D? MidInstance;
        public readonly List<float> NearBuffer = new();
        public readonly List<float> MidBuffer = new();
    }

    private sealed class Impostor
    {
        public ShaderMaterial Material = null!;
        public int Rows;
    }

    private sealed class Block
    {
        public TileKey Key;
        public Vector3D Anchor;
        public readonly Dictionary<string, MultiMeshInstance3D> Instances = new();
        public bool Built;
    }

    private PlanetSurface _surface = null!;
    private RenderFrame _frame = null!;
    private readonly Dictionary<string, List<Plant>> _byKind = new();
    private readonly Dictionary<string, Impostor> _impostors = new();
    private ArrayMesh _quad = null!;
    private readonly ConcurrentDictionary<ulong, List<ScatterItem>> _full = new();
    private readonly ConcurrentDictionary<ulong, List<ScatterItem>> _trees = new();
    private readonly ConcurrentDictionary<ulong, byte> _requested = new();
    private readonly BlockingQueue _jobs = new();
    private readonly List<Thread> _workers = new();
    private readonly Dictionary<ulong, Block> _blocks = new();
    private Vector3D _nearCentre = new(double.MaxValue, 0, 0);
    private Vector3D _nearAnchor;
    private int _nearMissing;
    private volatile bool _stopping;

    // Grass: drawn only near the camera, from the density the surface gives; positions are hashed per small tile, so
    // they stay put. Presentation only: grass is not part of the scatter.
    private const int GrassLevel = 20;
    private const float GrassNear = 16;
    private const float GrassFar = 46;
    private const double GrassPerSquareMetre = 2.2;

    private readonly struct GrassClump
    {
        public GrassClump(Vector3D position, float yaw, float scale, float tint, int variant)
        {
            Position = position;
            Yaw = yaw;
            Scale = scale;
            Tint = tint;
            Variant = variant;
        }

        public Vector3D Position { get; }
        public float Yaw { get; }
        public float Scale { get; }
        public float Tint { get; }
        public int Variant { get; }
    }

    private readonly ConcurrentDictionary<ulong, GrassClump[]> _grassCells = new();
    private readonly ConcurrentDictionary<ulong, byte> _grassRequested = new();
    private Vector3D _grassCentre = new(double.MaxValue, 0, 0);
    private Vector3D _grassAnchor;
    private int _grassMissing;
    private readonly Dictionary<Plant, (MultiMeshInstance3D? Near, MultiMeshInstance3D? Mid)> _grassInstances = new();

    /// <summary>Instances drawn as meshes near the camera, and as impostors.</summary>
    public int MeshInstances { get; private set; }
    public int ImpostorInstances { get; private set; }

    /// <summary>True when no scatter is pending around the camera.</summary>
    public bool IsIdle => _jobs.Count == 0 && _nearMissing == 0 && _grassMissing == 0 && Volatile.Read(ref _busy) == 0;

    /// <summary>Grass clumps drawn.</summary>
    public int GrassInstances { get; private set; }

    private int _busy;

    public void Setup(PlanetSurface surface, RenderFrame frame)
    {
        _surface = surface;
        _frame = frame;
        LoadLibrary();
        _quad = BuildQuad();
        for (int i = 0; i < 2; i++)
        {
            var t = new Thread(Work) { IsBackground = true, Name = $"Scatter {i}", Priority = ThreadPriority.BelowNormal };
            t.Start();
            _workers.Add(t);
        }
    }

    public override void _ExitTree()
    {
        _stopping = true;
        _jobs.Stop();
    }

    // ---------------------------------------------------------------- library

    private void LoadLibrary()
    {
        const string Folder = "res://assets/surface/flora/";
        using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(Folder + "plants.json"));
        Shader foliage = GD.Load<Shader>("res://shaders/surface_foliage.gdshader");
        var scenes = new Dictionary<string, Node>();
        foreach (JsonElement p in json.RootElement.GetProperty("plants").EnumerateArray())
        {
            string name = p.GetProperty("name").GetString()!;
            string kind = p.GetProperty("kind").GetString()!;
            string file = p.GetProperty("file").GetString()!;
            if (!scenes.TryGetValue(file, out Node? root))
            {
                scenes[file] = root = GD.Load<PackedScene>(Folder + file).Instantiate();
            }
            Mesh? near = FindMesh(root, p.GetProperty("near").GetString()!), mid = FindMesh(root, p.GetProperty("mid").GetString()!);
            if (near == null || mid == null)
            {
                GD.PushWarning($"Plant {name}: meshes 'near' and 'mid' not found.");
                continue;
            }
            float height = p.GetProperty("height").GetSingle();
            var plant = new Plant
            {
                Name = name,
                Kind = kind,
                Height = height,
                Extent = p.TryGetProperty("impostor_extent", out JsonElement e) ? e.GetSingle() : height,
                ImpostorRow = p.TryGetProperty("impostor_row", out JsonElement r) ? r.GetInt32() : -1,
                Near = Restyle(near, foliage, kind, height, nearLevel: true),
                Mid = Restyle(mid, foliage, kind, height, nearLevel: false),
            };
            if (!_byKind.TryGetValue(kind, out List<Plant>? list))
            {
                _byKind[kind] = list = new List<Plant>();
            }
            list.Add(plant);
        }
        foreach (Node root in scenes.Values)
        {
            root.Free();
        }
        if (json.RootElement.TryGetProperty("impostors", out JsonElement impostors))
        {
            Shader shader = GD.Load<Shader>("res://shaders/surface_impostor.gdshader");
            foreach (JsonProperty kind in impostors.EnumerateObject())
            {
                int rows = kind.Value.GetProperty("rows").GetArrayLength();
                var material = new ShaderMaterial { Shader = shader };
                material.SetShaderParameter("colour_atlas", GD.Load<Texture2D>(Folder + $"impostor_{kind.Name}_colour.png"));
                material.SetShaderParameter("normal_atlas", GD.Load<Texture2D>(Folder + $"impostor_{kind.Name}_normal.png"));
                material.SetShaderParameter("rows", (float)rows);
                material.SetShaderParameter("views", (float)kind.Value.GetProperty("views").GetInt32());
                material.SetShaderParameter("fade_in", new Vector2(MeshRange - 30, MeshRange));
                material.SetShaderParameter("fade_out", ImpostorRange);
                material.SetShaderParameter("tint", KindTint(kind.Name));
                _impostors[kind.Name] = new Impostor { Material = material, Rows = rows };
            }
        }
    }

    private static Mesh? FindMesh(Node node, string name)
    {
        if (node is MeshInstance3D mi && mi.Name.ToString() == name)
        {
            return mi.Mesh;
        }
        foreach (Node child in node.GetChildren())
        {
            Mesh? m = FindMesh(child, name);
            if (m != null)
            {
                return m;
            }
        }
        return null;
    }

    private static Vector3 KindTint(string kind) => kind switch
    {
        "fir" => new Vector3(0.78f, 0.86f, 0.8f),
        "pine" => new Vector3(0.9f, 0.92f, 0.85f),
        "birch" => new Vector3(0.95f, 1.0f, 0.82f),
        "grass" => new Vector3(0.9f, 1.0f, 0.8f),
        _ => Vector3.One,
    };

    // Our shader on every surface, with the textures the GLB brought.
    private static Mesh Restyle(Mesh source, Shader shader, string kind, float height, bool nearLevel)
    {
        var mesh = (Mesh)source.Duplicate();
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var standard = mesh.SurfaceGetMaterial(s) as BaseMaterial3D;
            bool leaves = standard != null && (standard.Transparency is BaseMaterial3D.TransparencyEnum.AlphaScissor
                or BaseMaterial3D.TransparencyEnum.Alpha or BaseMaterial3D.TransparencyEnum.AlphaHash or BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass);
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("albedo_texture", standard?.AlbedoTexture);
            material.SetShaderParameter("normal_texture", standard?.NormalTexture);
            material.SetShaderParameter("has_normal", standard?.NormalTexture != null && nearLevel);
            material.SetShaderParameter("foliage", leaves);
            material.SetShaderParameter("two_sided", leaves);
            material.SetShaderParameter("plant_height", height);
            material.SetShaderParameter("tint", KindTint(kind));
            material.SetShaderParameter("roughness", kind == "rock" ? 0.85f : leaves ? 0.7f : 0.9f);
            float wind = kind switch
            {
                "rock" => 0f,
                "grass" => 0.06f,
                "fern" => 0.05f,
                "bush" => 0.05f,
                _ => 0.18f,
            };
            material.SetShaderParameter("wind", wind);
            Vector4 band;
            if (kind == "grass")
            {
                band = nearLevel ? new Vector4(0, 0, GrassNear - 4, GrassNear) : new Vector4(GrassNear - 4, GrassNear, GrassFar - 10, GrassFar);
            }
            else
            {
                band = nearLevel ? new Vector4(0, 0, NearRange - 6, NearRange) : new Vector4(NearRange - 6, NearRange, MeshRange - 30, MeshRange);
            }
            material.SetShaderParameter("lod_band", band);
            mesh.SurfaceSetMaterial(s, material);
        }
        return mesh;
    }

    private static ArrayMesh BuildQuad()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 0, 3, 2 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // ---------------------------------------------------------------- update

    /// <summary>Streams scatter around the camera (body frame) and rebuilds what it draws.</summary>
    public void UpdateVegetation(Vector3D camera)
    {
        // Tiles for meshes (full scatter) near, and for impostors (trees only) out to the impostor range.
        double edge = TileKey.EdgeLength(Scatter.Level, _surface.Radius);
        Vector3D up = camera.Normalized;
        Vector3D ground = up * _surface.Radius;
        var zone = new ZoneFrame(ground);
        int nearSteps = (int)Math.Ceiling(NearSetRadius / edge) + 1;
        _nearMissing = 0;
        var nearTiles = new List<TileKey>();
        for (int j = -nearSteps; j <= nearSteps; j++)
        {
            for (int i = -nearSteps; i <= nearSteps; i++)
            {
                Vector3D p = zone.ToBody(i * edge, 0, j * edge);
                TileKey key = TileKey.At(p, Scatter.Level);
                if (Vector3D.Distance(key.Centre * _surface.Radius, ground) > NearSetRadius + edge)
                {
                    continue;
                }
                if (!nearTiles.Contains(key))
                {
                    nearTiles.Add(key);
                }
                if (!_full.ContainsKey(key.Packed))
                {
                    _nearMissing++;
                    Request(key, full: true, priority: Vector3D.Distance(key.Centre * _surface.Radius, ground));
                }
            }
        }
        if (_nearMissing == 0 && (Vector3D.Distance(camera, _nearCentre) > RebuildDistance || _nearDirty))
        {
            BuildNear(nearTiles, camera);
        }
        UpdateBlocks(zone, ground);
        UpdateGrass(camera, zone, ground);
    }

    // ---------------------------------------------------------------- grass

    private void UpdateGrass(Vector3D camera, ZoneFrame zone, Vector3D ground)
    {
        if (!_byKind.TryGetValue("grass", out List<Plant>? grasses) || grasses.Count == 0)
        {
            return;
        }
        double altitude = camera.Length - _surface.Radius - Math.Max(_surface.HeightAt(camera.Normalized), 0);
        if (altitude > GrassFar)
        {
            _grassMissing = 0;
            return;
        }
        double edge = TileKey.EdgeLength(GrassLevel, _surface.Radius);
        int steps = (int)Math.Ceiling(GrassFar / edge) + 1;
        var cells = new List<ulong>();
        _grassMissing = 0;
        for (int j = -steps; j <= steps; j++)
        {
            for (int i = -steps; i <= steps; i++)
            {
                TileKey key = TileKey.At(zone.ToBody(i * edge, 0, j * edge), GrassLevel);
                double d = Vector3D.Distance(key.Centre * _surface.Radius, ground);
                if (d > GrassFar + edge || cells.Contains(key.Packed))
                {
                    continue;
                }
                cells.Add(key.Packed);
                if (_grassCells.ContainsKey(key.Packed))
                {
                    continue;
                }
                _grassMissing++;
                if (_grassRequested.TryAdd(key.Packed, 0))
                {
                    Interlocked.Increment(ref _busy);
                    TileKey cell = key;
                    int variants = grasses.Count;
                    _jobs.Add(d * 0.5, () =>
                    {
                        try
                        {
                            _grassCells[cell.Packed] = GrassCell(cell, variants);
                        }
                        catch (Exception e)
                        {
                            GD.PushError($"Grass {cell} failed: {e}");
                            _grassCells[cell.Packed] = Array.Empty<GrassClump>();
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _busy);
                        }
                    });
                }
            }
        }
        if (_grassMissing > 0 || Vector3D.Distance(camera, _grassCentre) < 3)
        {
            return;
        }
        _grassCentre = camera;
        _grassAnchor = _frame.Origin;
        var near = new Dictionary<Plant, List<float>>();
        var mid = new Dictionary<Plant, List<float>>();
        foreach (Plant p in grasses)
        {
            near[p] = new List<float>();
            mid[p] = new List<float>();
        }
        int count = 0;
        foreach (ulong id in cells)
        {
            if (!_grassCells.TryGetValue(id, out GrassClump[]? clumps))
            {
                continue;
            }
            foreach (GrassClump c in clumps)
            {
                double dist = Vector3D.Distance(c.Position, camera);
                if (dist > GrassFar + 4)
                {
                    continue;
                }
                Plant plant = grasses[c.Variant % grasses.Count];
                Vector3 up = _frame.DirectionToRender(c.Position.Normalized);
                Vector3 reference = Math.Abs(up.Z) < 0.9f ? Vector3.Back : Vector3.Right;
                Vector3 x = reference.Cross(up).Normalized();
                Vector3 z = x.Cross(up);
                var basis = new Basis(x, up, z).Rotated(up, c.Yaw).Scaled(new Vector3(c.Scale, c.Scale, c.Scale));
                var t = new Transform3D(basis, _frame.DirectionToRender(c.Position - _grassAnchor) - up * 0.04f);
                if (dist < GrassNear + 6)
                {
                    Append(near[plant], t, c.Tint, 0, plant);
                }
                if (dist > GrassNear - 6)
                {
                    Append(mid[plant], t, c.Tint, 0, plant);
                }
                count++;
            }
        }
        var anchor = new Transform3D(Basis.Identity, _frame.ToRender(_grassAnchor));
        foreach (Plant p in grasses)
        {
            _grassInstances.TryGetValue(p, out var pair);
            pair.Near = Fill(pair.Near, p.Near, near[p], castShadow: false);
            pair.Mid = Fill(pair.Mid, p.Mid, mid[p], castShadow: false);
            if (pair.Near != null)
            {
                pair.Near.Transform = anchor;
            }
            if (pair.Mid != null)
            {
                pair.Mid.Transform = anchor;
            }
            _grassInstances[p] = pair;
        }
        GrassInstances = count;
    }

    // Grass clumps of one small tile: positions hashed from the tile, kept where the surface's grass density says.
    private GrassClump[] GrassCell(TileKey key, int variants)
    {
        double edge = TileKey.EdgeLength(GrassLevel, _surface.Radius);
        int n = (int)Math.Ceiling(edge * edge * GrassPerSquareMetre);
        // Density from the cell's centre: the climate changes slowly over ten metres.
        Vector3D centre = key.Centre;
        SurfaceSample s = _surface.Sample(centre);
        ClimateSample climate = _surface.Climate(centre, s);
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        _surface.BiomeWeights(centre, s, climate, biomes);
        VegetationSample v = _surface.Vegetation(centre, s, climate, biomes, 0);
        double density = v.Grass;
        if (density <= 0.02)
        {
            return Array.Empty<GrassClump>();
        }
        var list = new List<GrassClump>(n);
        var rng = new Rng(Hash64.Of(_surface.Seed, 0x47524153, key.Packed)); // "GRAS"
        double span = key.Span;
        for (int i = 0; i < n; i++)
        {
            double u = key.U0 + rng.NextDouble() * span, w = key.V0 + rng.NextDouble() * span;
            double keep = rng.NextDouble();
            float yaw = (float)rng.Range(0, Math.PI * 2);
            float scale = (float)rng.Range(0.7, 1.35);
            int variant = rng.NextInt(variants);
            float tint = (float)Math.Clamp(climate.Moisture + rng.Range(-0.2, 0.2), 0, 1);
            if (keep > density)
            {
                continue;
            }
            Vector3D d = CubeSphere.Direction(key.Face, u, w);
            double h = _surface.HeightAt(d);
            if (h < 0.4)
            {
                continue;
            }
            list.Add(new GrassClump(d * (_surface.Radius + h), yaw, scale, tint, variant));
        }
        return list.ToArray();
    }

    private bool _nearDirty = true;

    private void Request(TileKey key, bool full, double priority)
    {
        ulong id = key.Packed ^ (full ? 1UL << 63 : 0);
        if (_requested.TryAdd(id, 0))
        {
            Interlocked.Increment(ref _busy);
            _jobs.Add(priority + (full ? 0 : 1e6), () =>
            {
                try
                {
                    List<ScatterItem> items = Scatter.ForTile(_surface, key, treesOnly: !full);
                    (full ? _full : _trees)[key.Packed] = items;
                }
                catch (Exception e)
                {
                    GD.PushError($"Scatter {key} failed: {e}");
                }
                finally
                {
                    Interlocked.Decrement(ref _busy);
                }
            });
        }
    }

    private void Work()
    {
        while (!_stopping)
        {
            Action? job = _jobs.Take();
            job?.Invoke();
        }
    }

    // ---------------------------------------------------------------- near meshes

    private void BuildNear(List<TileKey> tiles, Vector3D camera)
    {
        _nearCentre = camera;
        _nearDirty = false;
        _nearAnchor = _frame.Origin;
        foreach (List<Plant> list in _byKind.Values)
        {
            foreach (Plant p in list)
            {
                p.NearBuffer.Clear();
                p.MidBuffer.Clear();
            }
        }
        int count = 0;
        foreach (TileKey key in tiles)
        {
            if (!_full.TryGetValue(key.Packed, out List<ScatterItem>? items))
            {
                continue;
            }
            foreach (ScatterItem item in items)
            {
                Vector3D body = item.Position(_surface.Radius);
                double d = Vector3D.Distance(body, camera);
                // Each level only holds what it may draw before the next rebuild: shaders still pick per instance.
                if (d > MeshRange + 2 * RebuildDistance)
                {
                    continue;
                }
                (Plant? plant, float scale, float tint) = Choose(item);
                if (plant == null)
                {
                    continue;
                }
                Transform3D t = InstanceTransform(item, body, _nearAnchor, scale, plant.Kind == "rock" ? -0.25f * scale : -0.15f);
                if (d < NearRange + 2 * RebuildDistance)
                {
                    Append(plant.NearBuffer, t, tint, item.Index, plant);
                }
                if (d > NearRange - 8 - 2 * RebuildDistance)
                {
                    Append(plant.MidBuffer, t, tint, item.Index, plant);
                }
                count++;
            }
        }
        foreach (List<Plant> list in _byKind.Values)
        {
            foreach (Plant p in list)
            {
                p.NearInstance = Fill(p.NearInstance, p.Near, p.NearBuffer, castShadow: true);
                p.MidInstance = Fill(p.MidInstance, p.Mid, p.MidBuffer, castShadow: p.Kind is "fir" or "pine" or "birch");
                if (p.NearInstance != null)
                {
                    p.NearInstance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
                }
                if (p.MidInstance != null)
                {
                    p.MidInstance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
                }
            }
        }
        MeshInstances = count;
    }

    private MultiMeshInstance3D? Fill(MultiMeshInstance3D? instance, Mesh mesh, List<float> buffer, bool castShadow)
    {
        int n = buffer.Count / 16;
        if (n == 0)
        {
            if (instance != null)
            {
                instance.Visible = false;
            }
            return instance;
        }
        if (instance == null)
        {
            instance = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh },
                CastShadow = castShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            AddChild(instance);
        }
        instance.Multimesh.InstanceCount = n;
        instance.Multimesh.Buffer = buffer.ToArray();
        instance.Visible = true;
        return instance;
    }

    // Which model a scatter object becomes, how big, and its colour variation.
    private (Plant? Plant, float Scale, float Tint) Choose(in ScatterItem item)
    {
        string kind = item.Kind switch
        {
            ScatterKind.Fir => "fir",
            ScatterKind.Pine => "pine",
            ScatterKind.Birch or ScatterKind.Broadleaf => "birch",
            ScatterKind.Bush => (item.Variant & 1) == 0 ? "bush" : "fern",
            ScatterKind.Fern => "fern",
            _ => "rock",
        };
        if (!_byKind.TryGetValue(kind, out List<Plant>? list) || list.Count == 0)
        {
            return (null, 0, 0);
        }
        Plant plant = list[item.Variant % list.Count];
        float scale = item.Scale * kind switch
        {
            "fir" => 1.35f,
            "pine" => 1.0f,
            "birch" => item.Kind == ScatterKind.Broadleaf ? 1.15f : 0.95f,
            "bush" => 1.1f,
            "fern" => item.Kind == ScatterKind.Bush ? 1.0f : 0.75f,
            _ => 1.0f,
        };
        float tint = ((item.Variant * 37) & 255) / 255f;
        if (item.Kind == ScatterKind.Broadleaf)
        {
            tint *= 0.5f;
        }
        return (plant, scale, tint);
    }

    // Instance transform in render axes relative to an anchor: the object's up is the planet's up there.
    private Transform3D InstanceTransform(in ScatterItem item, Vector3D body, Vector3D anchor, float scale, float sink)
    {
        Vector3 up = _frame.DirectionToRender(item.Direction);
        Vector3 reference = Math.Abs(up.Z) < 0.9f ? Vector3.Back : Vector3.Right;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = x.Cross(up);
        var basis = new Basis(x, up, z).Rotated(up, item.Yaw).Scaled(new Vector3(scale, scale, scale));
        Vector3 origin = _frame.DirectionToRender(body - anchor) + up * sink;
        return new Transform3D(basis, origin);
    }

    private static void Append(List<float> buffer, Transform3D t, float tint, int index, Plant plant)
    {
        Basis b = t.Basis;
        buffer.Add(b.X.X); buffer.Add(b.Y.X); buffer.Add(b.Z.X); buffer.Add(t.Origin.X);
        buffer.Add(b.X.Y); buffer.Add(b.Y.Y); buffer.Add(b.Z.Y); buffer.Add(t.Origin.Y);
        buffer.Add(b.X.Z); buffer.Add(b.Y.Z); buffer.Add(b.Z.Z); buffer.Add(t.Origin.Z);
        buffer.Add(tint);
        buffer.Add(Math.Max(plant.ImpostorRow, 0));
        buffer.Add(plant.Extent);
        buffer.Add(plant.Height);
        _ = index;
    }

    // ---------------------------------------------------------------- impostor blocks

    private void UpdateBlocks(ZoneFrame zone, Vector3D ground)
    {
        double blockEdge = TileKey.EdgeLength(BlockLevel, _surface.Radius);
        int steps = (int)Math.Ceiling(ImpostorRange / blockEdge) + 1;
        var wanted = new HashSet<ulong>();
        int impostors = 0;
        for (int j = -steps; j <= steps; j++)
        {
            for (int i = -steps; i <= steps; i++)
            {
                TileKey key = TileKey.At(zone.ToBody(i * blockEdge, 0, j * blockEdge), BlockLevel);
                double d = Vector3D.Distance(key.Centre * _surface.Radius, ground);
                if (d > ImpostorRange + blockEdge || !wanted.Add(key.Packed))
                {
                    continue;
                }
                if (!_blocks.TryGetValue(key.Packed, out Block? block))
                {
                    block = new Block { Key = key, Anchor = key.Centre * _surface.Radius };
                    _blocks[key.Packed] = block;
                }
                if (!block.Built)
                {
                    TryBuildBlock(block, d);
                }
                foreach (MultiMeshInstance3D mmi in block.Instances.Values)
                {
                    impostors += mmi.Multimesh.InstanceCount;
                }
            }
        }
        var stale = new List<ulong>();
        foreach ((ulong key, Block block) in _blocks)
        {
            if (!wanted.Contains(key))
            {
                stale.Add(key);
                foreach (MultiMeshInstance3D mmi in block.Instances.Values)
                {
                    mmi.QueueFree();
                }
            }
        }
        foreach (ulong key in stale)
        {
            _blocks.Remove(key);
        }
        ImpostorInstances = impostors;
    }

    private void TryBuildBlock(Block block, double distance)
    {
        int span = 1 << (Scatter.Level - BlockLevel);
        var tiles = new List<TileKey>(span * span);
        bool ready = true;
        for (int y = 0; y < span; y++)
        {
            for (int x = 0; x < span; x++)
            {
                var key = new TileKey(block.Key.Face, Scatter.Level, block.Key.X * span + x, block.Key.Y * span + y);
                tiles.Add(key);
                if (!_trees.ContainsKey(key.Packed) && !_full.ContainsKey(key.Packed))
                {
                    ready = false;
                    Request(key, full: false, priority: distance);
                }
            }
        }
        if (!ready)
        {
            return;
        }
        var buffers = new Dictionary<string, List<float>>();
        foreach (TileKey key in tiles)
        {
            List<ScatterItem>? items = _full.TryGetValue(key.Packed, out var f) ? f : _trees.TryGetValue(key.Packed, out var t) ? t : null;
            if (items == null)
            {
                continue;
            }
            foreach (ScatterItem item in items)
            {
                if (item.Kind is not (ScatterKind.Fir or ScatterKind.Pine or ScatterKind.Birch or ScatterKind.Broadleaf))
                {
                    continue;
                }
                (Plant? plant, float scale, float tint) = Choose(item);
                if (plant == null || plant.ImpostorRow < 0 || !_impostors.ContainsKey(plant.Kind))
                {
                    continue;
                }
                if (!buffers.TryGetValue(plant.Kind, out List<float>? buffer))
                {
                    buffers[plant.Kind] = buffer = new List<float>();
                }
                Transform3D tr = InstanceTransform(item, item.Position(_surface.Radius), block.Anchor, scale, -0.1f);
                Append(buffer, tr, tint, item.Index, plant);
            }
        }
        foreach ((string kind, List<float> buffer) in buffers)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = _quad,
                InstanceCount = buffer.Count / 16,
                Buffer = buffer.ToArray(),
            };
            double edge = TileKey.EdgeLength(BlockLevel, _surface.Radius);
            float half = (float)(edge * 0.75 + 60);
            mm.CustomAabb = new Aabb(new Vector3(-half, -half, -half), new Vector3(2 * half, 2 * half, 2 * half));
            var mmi = new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = _impostors[kind].Material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Transform = new Transform3D(Basis.Identity, _frame.ToRender(block.Anchor)),
            };
            AddChild(mmi);
            block.Instances[kind] = mmi;
        }
        block.Built = true;
    }

    /// <summary>Re-anchors everything after the render origin moved.</summary>
    public void Reanchor()
    {
        foreach (List<Plant> list in _byKind.Values)
        {
            foreach (Plant p in list)
            {
                if (p.NearInstance != null)
                {
                    p.NearInstance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
                }
                if (p.MidInstance != null)
                {
                    p.MidInstance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
                }
            }
        }
        foreach (Block block in _blocks.Values)
        {
            foreach (MultiMeshInstance3D mmi in block.Instances.Values)
            {
                mmi.Transform = new Transform3D(Basis.Identity, _frame.ToRender(block.Anchor));
            }
        }
        var grassAnchor = new Transform3D(Basis.Identity, _frame.ToRender(_grassAnchor));
        foreach ((MultiMeshInstance3D? near, MultiMeshInstance3D? mid) in _grassInstances.Values)
        {
            if (near != null)
            {
                near.Transform = grassAnchor;
            }
            if (mid != null)
            {
                mid.Transform = grassAnchor;
            }
        }
    }

    /// <summary>A queue of jobs taken lowest priority value first.</summary>
    private sealed class BlockingQueue
    {
        private readonly List<(double Priority, Action Job)> _items = new();
        private readonly object _lock = new();
        private bool _stopped;

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        public void Add(double priority, Action job)
        {
            lock (_lock)
            {
                _items.Add((priority, job));
                Monitor.Pulse(_lock);
            }
        }

        public Action? Take()
        {
            lock (_lock)
            {
                while (_items.Count == 0 && !_stopped)
                {
                    Monitor.Wait(_lock);
                }
                if (_stopped)
                {
                    return null;
                }
                int best = 0;
                for (int i = 1; i < _items.Count; i++)
                {
                    if (_items[i].Priority < _items[best].Priority)
                    {
                        best = i;
                    }
                }
                Action job = _items[best].Job;
                _items.RemoveAt(best);
                return job;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _stopped = true;
                Monitor.PulseAll(_lock);
            }
        }
    }
}
