using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Client.Streaming;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

/// <summary>
/// Trees, bushes, berry bushes, ferns and rocks of the surface view (GROUND.md §4.6), from a plant source
/// (<see cref="IPlantSource"/>): a surface's own scatter in the look-dev view, the zone's natural objects in the game, the
/// same objects the server knows by tile and index. Ranges follow the horizon (docs/WHOLE_WORLD.md §10.3): on a small
/// world nothing is laid out beyond where the ball hides it. Each species has seeded variants built in code
/// (<see cref="FloraLibrary"/>) with two meshes; near the camera every object is drawn with the first, farther with the
/// second, grouped per variant into MultiMeshes rebuilt as the camera moves. Trees then become impostors (eight baked
/// views): those of the near set with it, farther ones batched per block of scatter tiles, out to
/// <see cref="ImpostorEnd"/>; smaller things shrink into the ground at the end of their range. Each object keeps its
/// level between rebuilds (with some hysteresis); when it changes, the old and new levels cross-fade for a fraction of
/// a second, so no tree stands half-dithered at a boundary.
/// </summary>
public sealed partial class Vegetation : Node3D
{
    // Trees' second meshes end at 170 m (impostors begin) and their full meshes at 50 m, times the graphics settings'
    // plant distance (GROUND.md §8.6: 0.6–1.4, so the impostors' start stays inside the near set's 290 m).
    public static float ImpostorStart => 170 * Detail;
    public static float TreeNear => 50 * Detail;

    /// <summary>The graphics settings' trees-and-plants distance: every species' detail bands times this.</summary>
    public static float Detail => Math.Clamp(HollowUniverse.Settings.GraphicsSettings.Current.PlantDistance, 0.5f, 1.4f);
    public const float ImpostorEnd = 2200;
    private const float NearImpostorEnd = 290; // the near set's impostors hand over to the far blocks here
    private const double NearSetRadius = 300;
    private const double RebuildDistance = 8;

    /// <summary>The tallest plant, metres: what pokes up over the horizon.</summary>
    private const double TallestPlant = 25;

    private int _level;      // the plant source's tiles
    private int _blockLevel; // impostor blocks: eight tiles a side

    // One MultiMesh: a variant of a species at one level of detail.
    private sealed class LodSet
    {
        public FloraLibrary.Species Species = null!;
        public FloraLibrary.Variant Variant = null!;
        public int Lod;
        public MultiMeshInstance3D? Instance;
        public readonly List<float> Buffer = new();
    }

    private sealed class Block
    {
        public TileKey Key;
        public Vector3D Anchor;
        public MultiMeshInstance3D? Instance;
        public bool Built;
    }

    private ISurface _surface = null!;
    private IPlantSource _plants = null!;
    private (double Low, double High) _bounds;
    private RenderFrame _frame = null!;
    private TextureLayered _groundAlbedo = null!;
    private TextureLayered _groundNormal = null!;
    private LibraryBuild? _data;
    private FloraLibrary? _library;
    private readonly Dictionary<(FloraLibrary.Variant, int), LodSet> _sets = new();
    private readonly List<float> _nearImpostorBuffer = new();
    private Aabb _nearImpostorBounds;
    private MultiMeshInstance3D? _nearImpostors;

    // Each object's level (0, 1, 2 = impostor), the level it is leaving and since when (the LOD clock), and the rebuild
    // that last saw it.
    private struct LodState
    {
        public byte Lod;
        public byte From;
        public float Since;
        public int Seen;
    }

    private Dictionary<(ulong Tile, int Index), LodState> _lods = new();
    private int _rebuild;
    private ulong _startUsec;

    // Seconds since the view opened, plus one (zero means "no fade" in the instance data).
    private float Clock => (float)((Time.GetTicksUsec() - _startUsec) / 1e6) + 1;
    private ImpostorBaker? _baker;
    private ShaderMaterial? _impostors;
    private ArrayMesh _quad = null!;
    private readonly ConcurrentDictionary<ulong, IReadOnlyList<ScatterItem>> _full = new();
    private readonly ConcurrentDictionary<ulong, IReadOnlyList<ScatterItem>> _trees = new();
    private ScatterRequestSet _requested = new();
    private readonly WorkSession _jobs = new();
    private readonly Dictionary<ulong, Block> _blocks = new();
    private Vector3D _nearCentre = new(double.MaxValue, 0, 0);
    private Vector3D _nearAnchor;
    private int _nearMissing;
    private bool _nearDirty = true;
    private volatile bool _stopping;
    private int _busy;

    /// <summary>Instances drawn as meshes near the camera, and as impostors.</summary>
    public int MeshInstances { get; private set; }
    public int ImpostorInstances { get; private set; }

    /// <summary>True when the library is built, the impostors baked and no scatter is pending around the camera.</summary>
    public bool IsIdle => !_stopping && !_requiredFailed && _jobs.Faults == 0 && _impostors != null && _jobs.Count == 0
        && _nearMissing == 0 && Volatile.Read(ref _busy) == 0 && _nearJob == null && _blockJobs.Count == 0;

    public FloraLibrary? Library => _library;

    /// <summary>Where the plants come from (the game names the objects it changed through it).</summary>
    public IPlantSource Source => _plants;

    /// <summary>
    /// Look-dev: one of each tree species at this distance (metres) in front of the camera, small plants and a rock
    /// nearer, all at one level of detail (0, 1, or 2 for impostors). Set before the first update.
    /// </summary>
    public (float Distance, int Lod, Vector3 Forward)? Lineup { get; set; }

    private bool _lineupBuilt;

    /// <summary>Starts drawing a source's plants on a surface; <paramref name="seed"/> paints the species' variants.</summary>
    public void Setup(ISurface surface, IPlantSource plants, ulong seed, RenderFrame frame, TextureLayered groundAlbedo, TextureLayered groundNormal)
    {
        if (_setup || _stopping) throw new InvalidOperationException("Vegetation.Setup is single-use.");
        _setup = true;
        _surface = surface;
        _plants = plants;
        _level = plants.Level;
        _blockLevel = Math.Max(0, _level - 3);
        _bounds = surface.HeightBounds;
        _frame = frame;
        _groundAlbedo = groundAlbedo;
        _groundNormal = groundNormal;
        _quad = BuildQuad();
        _startUsec = Time.GetTicksUsec();
        int librarySeed = (int)(seed & 0x7FFFFFFF);
        _data = new LibraryBuild(librarySeed);
        _jobs.Start(plants);
    }

    // ---------------------------------------------------------------- library

    private bool EnsureLibrary()
    {
        if (_library != null)
        {
            return true;
        }
        if (_data == null || !_data.TryTake(out FloraLibrary.Data? data, out string? error))
        {
            return false;
        }
        _data = null;
        if (data == null)
        {
            _requiredFailed = true;
            _assetFailures++;
            GD.PushError($"Flora failed: {error}");
            return false;
        }
        try { _library = FloraLibrary.Create(data, _groundAlbedo, _groundNormal); }
        catch (Exception failure)
        {
            _requiredFailed = true;
            _assetFailures++;
            GD.PushError($"Flora resources failed: {failure}");
            return false;
        }
        finally { DisposeData(data); }
        foreach (FloraLibrary.Species s in _library.All)
        {
            foreach (FloraLibrary.Variant v in s.Variants)
            {
                _variantIds[v] = _variants.Count;
                _variants.Add(v);
                for (int lod = 0; lod < 2; lod++)
                {
                    _sets[(v, lod)] = new LodSet { Species = s, Variant = v, Lod = lod };
                }
            }
        }
        _baker = new ImpostorBaker { Name = "Impostors" };
        AddChild(_baker);
        _bakeTask = _baker.Bake(_library);
        return true;
    }

    private void ApplyImpostors()
    {
        if (_bakeTask is not { IsCompleted: true } task) return;
        _bakeTask = null;
        if (!task.IsCompletedSuccessfully)
        {
            _requiredFailed = true;
            _assetFailures++;
            if (task.IsFaulted) GD.PushError($"Flora baking failed: {task.Exception}");
            _baker?.Stop();
            _baker?.QueueFree();
            _baker = null;
            return;
        }
        ImpostorBaker.Result result = _baker!.TakeResult();
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_impostor.gdshader") };
        material.SetShaderParameter("colour_atlas", result.Colour);
        material.SetShaderParameter("normal_atlas", result.Normal);
        material.SetShaderParameter("rows", (float)result.Rows);
        material.SetShaderParameter("native_rows", result.NativeRows);
        material.SetShaderParameter("views", (float)ImpostorBaker.Views);
        material.SetShaderParameter("fade_out", ImpostorEnd);
        _impostors = material;
        _baker.QueueFree();
        _baker = null;
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
        if (!_setup || _stopping) return;
        CollectScatter();
        if (!EnsureLibrary())
        {
            return;
        }
        ApplyImpostors();
        RenderingServer.GlobalShaderParameterSet("hu_lod_clock", Clock);
        double edge = TileKey.EdgeLength(_level, _surface.Radius);
        Vector3D up = camera.Normalized;
        Vector3D ground = up * _surface.Radius;
        var zone = new ZoneFrame(ground);
        _reach = Reach(camera);
        double nearRadius = Math.Min(NearSetRadius, _reach);
        // Sample at a third of a tile: tiles are turned against the zone's axes and vary in size, so sampling at a
        // whole tile's spacing would skip some.
        double step = edge / 3;
        int nearSteps = (int)Math.Ceiling((nearRadius + edge) / step);
        _nearMissing = 0;
        var nearTiles = new List<TileKey>();
        var seen = new HashSet<ulong>();
        for (int j = -nearSteps; j <= nearSteps; j++)
        {
            for (int i = -nearSteps; i <= nearSteps; i++)
            {
                TileKey key = TileKey.At(zone.ToBody(i * step, 0, j * step), _level);
                if (!seen.Add(key.Packed) || Vector3D.Distance(key.Centre * _surface.Radius, ground) > nearRadius + edge)
                {
                    continue;
                }
                nearTiles.Add(key);
                if (!_full.ContainsKey(key.Packed))
                {
                    _nearMissing++;
                    Request(key, full: true, priority: Vector3D.Distance(key.Centre * _surface.Radius, ground));
                }
            }
        }
        ApplyNear();
        if (_nearMissing == 0 && (Vector3D.Distance(camera, _nearCentre) > RebuildDistance || _nearDirty))
        {
            RebuildNear(nearTiles, camera);
        }
        if (_impostors != null)
        {
            UpdateBlocks(zone, camera);
        }
        if (Lineup is { } lineup && !_lineupBuilt && (lineup.Lod < 2 || _impostors != null))
        {
            BuildLineup(camera, lineup.Distance, lineup.Lod, lineup.Forward);
            _lineupBuilt = true;
        }
    }

    // How far plants can be seen from the camera (body frame): over the horizon of the lowest ground there can be to the
    // tallest plant on the highest ground there can be, at most the impostors' end. On the real first world that is the
    // impostors' end; on a 2.41 km world, about a kilometre from the ground.
    private double _reach = ImpostorEnd;

    private double Reach(Vector3D camera)
    {
        double r = _surface.Radius;
        double eye = Math.Max(camera.Length - r - _bounds.Low, 1);
        double plant = TallestPlant + _bounds.High - _bounds.Low;
        double reach = Math.Sqrt(2 * r * eye + eye * eye) + Math.Sqrt(2 * r * plant + plant * plant);
        return Math.Min(ImpostorEnd, reach);
    }

    private void BuildLineup(Vector3D camera, float distance, int lod, Vector3 forward)
    {
        Vector3D up = camera.Normalized;
        Vector3D f = _frame.DirectionToBody(forward);
        f = (f - up * Vector3D.Dot(f, up)).Normalized;
        Vector3D side = Vector3D.Cross(f, up).Normalized;
        var rows = new List<(ScatterKind Kind, float Along, float Across)>
        {
            (ScatterKind.Fir, distance, -15), (ScatterKind.Pine, distance, -5), (ScatterKind.Birch, distance, 5), (ScatterKind.Broadleaf, distance, 15),
            (ScatterKind.Bush, distance * 0.3f, -4.4f), (ScatterKind.BerryRed, distance * 0.3f, -2.2f), (ScatterKind.BerryBlue, distance * 0.3f, 0),
            (ScatterKind.Fern, distance * 0.3f, 2.2f), (ScatterKind.Rock, distance * 0.3f, 4.4f),
        };
        foreach ((ScatterKind kind, float along, float across) in rows)
        {
            FloraLibrary.Species? species = _library!.For(kind);
            if (species == null)
            {
                continue;
            }
            FloraLibrary.Variant variant = species.Variants[0];
            Vector3D d = (camera + f * along + side * across).Normalized;
            double h = _surface.HeightAt(d);
            var item = new ScatterItem(0, kind, 0, d, (float)h, 0.6f, 1.0f);
            Vector3D body = item.Position(_surface.Radius);
            Transform3D t = InstanceTransform(item, body, _frame.Origin, species.Scale, 0);
            var buffer = new List<float>();
            bool impostor = lod == 2 && species.Tree;
            if (impostor)
            {
                Append(buffer, t, 0.5f, variant.ImpostorRow, Pack(variant), 0);
            }
            else
            {
                Append(buffer, t, 0.5f, 0.5f, 0, 0);
            }
            var mmi = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true,
                    Mesh = impostor ? _quad : lod == 0 ? variant.Lod0 : variant.Lod1,
                    InstanceCount = 1,
                    Buffer = buffer.ToArray(),
                },
                MaterialOverride = impostor ? _impostors : null,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            if (impostor)
            {
                mmi.Multimesh.CustomAabb = new Aabb(new Vector3(-50, -50, -50), new Vector3(100, 100, 100));
            }
            AddChild(mmi);
        }
    }

    private void Request(TileKey key, bool full, double priority)
    {
        if (!_stopping && _requested.TryAdd(key, full))
        {
            _jobs.Add(priority + (full ? 0 : 1e6), key, full);
            Volatile.Write(ref _busy, _jobs.Busy);
        }
    }

    // ---------------------------------------------------------------- meshes near the camera

    private void BuildNear(List<TileKey> tiles, Vector3D camera)
    {
        _nearCentre = camera;
        _nearDirty = false;
        // Anchored at the camera, so the near set's bounds follow it (docs/WHOLE_WORLD.md §12).
        PlaceNear(ComputeNear(CaptureNear(tiles, camera, (key, item) => DrawnKind(key, item)), CancellationToken.None));
    }

    // What the near set draws: each variant's buffers per level, and the near impostors with their bounds.
    private sealed class NearData
    {
        public Vector3D Anchor;
        public readonly Dictionary<(int Variant, int Lod), List<float>> Sets = new();
        public readonly List<float> Impostors = new();
        public Dictionary<(ulong Tile, int Index), LodState> Lods = null!;
        public int Rebuild;

        // Every near impostor's quad however it turns, in render axes from the anchor. The shader places the corners, so
        // Godot can't bound them from the mesh, and the near set may lie anywhere from its anchor (in play the anchor is
        // the landing site).
        public Aabb ImpostorBounds;
        public int Count;

        // Grows the bounds by the impostor just added. Its quad turns about the tree's up through the foot, its corners
        // within half the extent of that axis and half the height ± half the extent up it (surface_impostor.gdshader):
        // all within half the height plus the extent of the foot.
        public void Bound(Transform3D t, VariantInput variant)
        {
            // Pack's extent and height back out of the one float.
            float whole = MathF.Floor(variant.Packed);
            float extent = whole / 100, height = (variant.Packed - whole) * 100;
            float reach = (height * 0.5f + extent) * t.Basis.Y.Length();
            var box = new Aabb(t.Origin - new Vector3(reach, reach, reach), new Vector3(2 * reach, 2 * reach, 2 * reach));
            ImpostorBounds = Impostors.Count > 16 ? ImpostorBounds.Merge(box) : box;
        }
    }

    // Lays out the near set from the scatter around the camera, with each object drawn as `kinds` says. It touches no
    // node, so the ground game runs it on a worker thread (Vegetation.Game.cs); one runs at a time.
    private static NearData ComputeNear(NearInput input, CancellationToken cancellation)
    {
        var data = new NearData { Anchor = input.Anchor, Lods = input.Lods, Rebuild = input.Rebuild };
        AppearanceInput appearance = input.Appearance;
        float clock = input.Clock;
        double margin = 2 * RebuildDistance;
        foreach (PlantInput plant in input.Plants)
        {
            cancellation.ThrowIfCancellationRequested();
            TileKey key = plant.Tile;
            ScatterItem item = plant.Item;
            if (!appearance.Species.TryGetValue(plant.Kind, out SpeciesInput? species) || species.Variants.Length == 0)
            {
                continue;
            }
            Vector3D body = item.Position(appearance.Radius);
            double d = Vector3D.Distance(body, input.Camera);
            bool tree = species.Tree && appearance.HasImpostors;
            if (d > (tree ? NearImpostorEnd : species.Bands.Far * appearance.Detail) + margin)
            {
                continue;
            }
            // The object's level, held between rebuilds with some hysteresis; a change starts a cross-fade.
            var id = (key.Packed, item.Index);
            bool known = data.Lods.TryGetValue(id, out LodState state);
            int lod = Level(species, tree, d, known ? state.Lod : -1, appearance.Detail);
            if (!known)
            {
                state = new LodState { Lod = (byte)lod, From = (byte)lod, Since = -1 };
            }
            else if (state.Lod != lod)
            {
                state.From = state.Lod;
                state.Lod = (byte)lod;
                state.Since = clock;
            }
            state.Seen = data.Rebuild;
            data.Lods[id] = state;
            bool fading = state.From != state.Lod && clock - state.Since < 0.5f;

            VariantInput variant = species.Variants[item.Variant % species.Variants.Length];
            float scale = item.Scale * species.Scale;
            float sink = item.Kind == ScatterKind.Rock ? -0.12f * scale : -0.05f;
            Transform3D t = InstanceTransform(appearance.Axes, item, body, data.Anchor, scale, sink);
            (float a, float b) = Shade(item);
            Emit(data, variant, state.Lod, t, a, b, fading ? state.Since : 0);
            if (fading)
            {
                Emit(data, variant, state.From, t, a, b, -state.Since);
            }
            data.Count++;
        }
        // Forget objects the near set no longer holds.
        var gone = new List<(ulong, int)>();
        foreach ((var id, LodState state) in data.Lods)
        {
            if (state.Seen != data.Rebuild)
            {
                gone.Add(id);
            }
        }
        foreach (var id in gone)
        {
            data.Lods.Remove(id);
        }
        return data;
    }

    // Puts a laid-out near set in place (main thread).
    private void PlaceNear(NearData data)
    {
        _nearAnchor = data.Anchor;
        _lods = data.Lods;
        _rebuild = data.Rebuild;
        foreach (((FloraLibrary.Variant, int) key, LodSet set) in _sets)
        {
            set.Buffer.Clear();
            if (data.Sets.TryGetValue((_variantIds[key.Item1], key.Item2), out List<float>? buffer))
            {
                set.Buffer.AddRange(buffer);
            }
            Fill(set);
        }
        _nearImpostorBuffer.Clear();
        _nearImpostorBuffer.AddRange(data.Impostors);
        _nearImpostorBounds = data.ImpostorBounds;
        FillNearImpostors();
        MeshInstances = data.Count;
    }

    // Level for an object at distance d, given the level it has (-1: none yet): meshes 0 and 1, impostor 2.
    private static int Level(SpeciesInput species, bool tree, double d, int previous, float detail)
    {
        if (tree)
        {
            double near = 50 * detail + (previous == 0 ? 3 : -3);
            if (d < near)
            {
                return 0;
            }
            double mid = 170 * detail + (previous is 0 or 1 ? 6 : -6);
            return d < mid ? 1 : 2;
        }
        double n = species.Bands.Near * detail + (previous == 0 ? 2 : -2);
        return d < n ? 0 : 1;
    }

    private static void Emit(NearData data, VariantInput variant, int lod, Transform3D t, float a, float b, float fade)
    {
        if (lod == 2)
        {
            Append(data.Impostors, t, a, variant.ImpostorRow, variant.Packed, fade);
            data.Bound(t, variant);
        }
        else
        {
            if (!data.Sets.TryGetValue((variant.Id, lod), out List<float>? buffer))
            {
                data.Sets[(variant.Id, lod)] = buffer = new List<float>();
            }
            Append(buffer, t, a, b, fade, 0);
        }
    }

    // An impostor's extent and height in one float: floor(extent * 100) + height / 100 (metres).
    private static float Pack(FloraLibrary.Variant v) => MathF.Floor(v.Extent * 100) + Math.Clamp(v.Height, 0, 99.9f) / 100;

    private void FillNearImpostors()
    {
        int n = _nearImpostorBuffer.Count / 16;
        if (_impostors == null || n == 0 || DebugSkip.Contains("impostor"))
        {
            if (_nearImpostors != null)
            {
                _nearImpostors.Visible = false;
            }
            return;
        }
        if (_nearImpostors == null)
        {
            _nearImpostors = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true,
                    Mesh = _quad,
                },
                MaterialOverride = _impostors,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            _nearImpostors.SetInstanceShaderParameter("cut", new Vector2(0, NearImpostorEnd));
            AddChild(_nearImpostors);
        }
        _nearImpostors.Multimesh.InstanceCount = n;
        _nearImpostors.Multimesh.Buffer = _nearImpostorBuffer.ToArray();
        _nearImpostors.Multimesh.CustomAabb = _nearImpostorBounds;
        _nearImpostors.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
        _nearImpostors.Visible = true;
    }

    // Two stable random numbers per object, for its colour.
    private static (float A, float B) Shade(in ScatterItem item)
    {
        ulong h = Hash64.Of((ulong)(uint)item.Index, item.Variant, 0x54494E54); // "TINT"
        return ((h & 0xFFFF) / 65535f, ((h >> 16) & 0xFFFF) / 65535f);
    }

    // Look-dev: HU_VEG_SKIP=lod0,lod1,impostor leaves those levels out.
    private static readonly string DebugSkip = OS.GetEnvironment("HU_VEG_SKIP");

    private void Fill(LodSet set)
    {
        int n = set.Buffer.Count / 16;
        if (DebugSkip.Contains("lod" + set.Lod))
        {
            n = 0;
        }
        if (n == 0)
        {
            if (set.Instance != null)
            {
                set.Instance.Visible = false;
            }
            return;
        }
        if (set.Instance == null)
        {
            FloraLibrary.Bands b = set.Species.Bands;
            bool shrinks = set.Lod == 1 && !b.Impostor;
            set.Instance = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true,
                    Mesh = set.Lod == 0 ? set.Variant.Lod0 : set.Variant.Lod1,
                },
                CastShadow = set.Lod == 0 || set.Species.Tree ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            // Levels change by cross-fades over time; only the last level of small things shrinks away with distance.
            float far = b.Far * Detail;
            set.Instance.SetInstanceShaderParameter("lod_band", shrinks ? new Vector4(0, 0, far * 0.82f, far) : new Vector4(0, 0, 1e6f, 2e6f));
            set.Instance.SetInstanceShaderParameter("shrink", shrinks ? 1.0f : 0.0f);
            AddChild(set.Instance);
        }
        set.Instance.Multimesh.InstanceCount = n;
        set.Instance.Multimesh.Buffer = set.Buffer.ToArray();
        set.Instance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
        set.Instance.Visible = true;
    }

    // Instance transform in render axes relative to an anchor: the object's up is the planet's up there.
    private Transform3D InstanceTransform(in ScatterItem item, Vector3D body, Vector3D anchor, float scale, float sink)
        => InstanceTransform(new(_frame.DirectionToBody(Vector3.Right), _frame.DirectionToBody(Vector3.Up),
            _frame.DirectionToBody(Vector3.Back)), item, body, anchor, scale, sink);

    private static Transform3D InstanceTransform(Axes axes, in ScatterItem item, Vector3D body, Vector3D anchor, float scale, float sink)
    {
        Vector3 up = axes.ToRender(item.Direction);
        Vector3 reference = Math.Abs(up.Z) < 0.9f ? Vector3.Back : Vector3.Right;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = x.Cross(up);
        var basis = new Basis(x, up, z).Rotated(up, item.Yaw).Scaled(new Vector3(scale, scale, scale));
        Vector3 origin = axes.ToRender(body - anchor) + up * sink;
        return new Transform3D(basis, origin);
    }

    private static void Append(List<float> buffer, Transform3D t, float c0, float c1, float c2, float c3)
    {
        Basis b = t.Basis;
        buffer.Add(b.X.X); buffer.Add(b.Y.X); buffer.Add(b.Z.X); buffer.Add(t.Origin.X);
        buffer.Add(b.X.Y); buffer.Add(b.Y.Y); buffer.Add(b.Z.Y); buffer.Add(t.Origin.Y);
        buffer.Add(b.X.Z); buffer.Add(b.Y.Z); buffer.Add(b.Z.Z); buffer.Add(t.Origin.Z);
        buffer.Add(c0);
        buffer.Add(c1);
        buffer.Add(c2);
        buffer.Add(c3);
    }

    // ---------------------------------------------------------------- impostor blocks

    private void UpdateBlocks(ZoneFrame zone, Vector3D camera)
    {
        double blockEdge = TileKey.EdgeLength(_blockLevel, _surface.Radius);
        double step = blockEdge / 3;
        // Blocks within reach of the camera (the horizon's), found over the tangent plane under it.
        int steps = (int)Math.Ceiling((_reach + blockEdge) / step);
        var wanted = new HashSet<ulong>();
        int impostors = 0;
        for (int j = -steps; j <= steps; j++)
        {
            for (int i = -steps; i <= steps; i++)
            {
                TileKey key = TileKey.At(zone.ToBody(i * step, 0, j * step), _blockLevel);
                double d = Vector3D.Distance(key.Centre * _surface.Radius, camera);
                if (d > _reach + blockEdge || !wanted.Add(key.Packed))
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
                impostors += block.Instance?.Multimesh.InstanceCount ?? 0;
            }
        }
        var stale = new List<ulong>();
        foreach ((ulong key, Block block) in _blocks)
        {
            if (!wanted.Contains(key))
            {
                stale.Add(key);
                block.Instance?.QueueFree();
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
        int span = 1 << (_level - _blockLevel);
        var tiles = new List<TileKey>(span * span);
        bool ready = true;
        for (int y = 0; y < span; y++)
        {
            for (int x = 0; x < span; x++)
            {
                var key = new TileKey(block.Key.Face, _level, block.Key.X * span + x, block.Key.Y * span + y);
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
        List<float> buffer = ImpostorBuffer(CaptureBlock(tiles, block.Anchor, (key, item) => DrawnKind(key, item)), CancellationToken.None);
        if (buffer.Count > 0 && !DebugSkip.Contains("impostor"))
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = _quad,
                InstanceCount = buffer.Count / 16,
                Buffer = buffer.ToArray(),
            };
            double edge = TileKey.EdgeLength(_blockLevel, _surface.Radius);
            float half = (float)(edge * 0.75 + 60);
            mm.CustomAabb = new Aabb(new Vector3(-half, -half, -half), new Vector3(2 * half, 2 * half, 2 * half));
            block.Instance = new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = _impostors,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Transform = new Transform3D(Basis.Identity, _frame.ToRender(block.Anchor)),
            };
            block.Instance.SetInstanceShaderParameter("cut", new Vector2(NearImpostorEnd, 1e9f));
            AddChild(block.Instance);
        }
        block.Built = true;
    }

    // A block's tree impostors from copied metadata and scatter; no Node, world or callback is retained.
    private static List<float> ImpostorBuffer(BlockInput input, CancellationToken cancellation)
    {
        var buffer = new List<float>();
        AppearanceInput appearance = input.Appearance;
        foreach (PlantInput plant in input.Plants)
        {
            cancellation.ThrowIfCancellationRequested();
            ScatterItem item = plant.Item;
            if (!appearance.Species.TryGetValue(plant.Kind, out SpeciesInput? species) || !species.Tree)
            {
                continue;
            }
            VariantInput variant = species.Variants[item.Variant % species.Variants.Length];
            if (variant.ImpostorRow < 0)
            {
                continue;
            }
            float scale = item.Scale * species.Scale;
            Transform3D tr = InstanceTransform(appearance.Axes, item, item.Position(appearance.Radius), input.Anchor, scale, -0.05f);
            (float a, _) = Shade(item);
            Append(buffer, tr, a, variant.ImpostorRow, variant.Packed, 0);
        }
        return buffer;
    }

    /// <summary>Re-anchors everything after the render origin moved.</summary>
    public void Reanchor()
    {
        if (!_setup || _stopping) return;
        foreach (LodSet set in _sets.Values)
        {
            if (set.Instance != null)
            {
                set.Instance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
            }
        }
        if (_nearImpostors != null)
        {
            _nearImpostors.Transform = new Transform3D(Basis.Identity, _frame.ToRender(_nearAnchor));
        }
        foreach (Block block in _blocks.Values)
        {
            if (block.Instance != null)
            {
                block.Instance.Transform = new Transform3D(Basis.Identity, _frame.ToRender(block.Anchor));
            }
        }
    }

}
