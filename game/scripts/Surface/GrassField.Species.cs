using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    private const string CatalogPath = "res://assets/surface/grass/grass.json";

    // Per species: the colours its accents take (petals, seed heads, bells), its dryness, how lush it stays, how it
    // sways and shines. The catalogue (grass.json, art/grass/build_grass.py) gives its meshes, sizes and mean colour.
    private sealed record Look(Color[] Accents, float AccentTint, float DryBias, float DryFloor, float Green, float Stiffness,
        float Sheen, float Fold, Vector3 Glow);

    private static readonly Color[] NoAccent = { Colors.White, Colors.White, Colors.White, Colors.White };

    private static readonly Dictionary<string, Look> Looks = new(StringComparer.Ordinal)
    {
        ["field"] = new(NoAccent, 0, 0, 0, 0, 1.0f, 0.3f, 0.6f, new Vector3(1.0f, 0.97f, 0.72f)),
        ["seeding"] = new(new[] { new Color(0.78f, 0.70f, 0.48f), new Color(0.55f, 0.42f, 0.36f), new Color(0.72f, 0.74f, 0.52f),
            new Color(0.86f, 0.80f, 0.58f) }, 1, 0.12f, 0, 0, 0.75f, 0.3f, 0.55f, new Vector3(1.0f, 0.93f, 0.66f)),
        ["dry"] = new(NoAccent, 0, 0.2f, 0.78f, 0, 1.0f, 0.2f, 0.45f, new Vector3(1.0f, 0.9f, 0.68f)),
        ["flower"] = new(new[] { new Color(0.88f, 0.88f, 0.84f), new Color(0.90f, 0.80f, 0.32f), new Color(0.84f, 0.84f, 0.80f),
            new Color(0.72f, 0.62f, 0.78f) }, 1, 0, 0, 0.15f, 0.9f, 0.3f, 0.55f, new Vector3(1.0f, 0.97f, 0.75f)),
        ["herb"] = new(new[] { new Color(0.70f, 0.52f, 0.68f), new Color(0.86f, 0.86f, 0.84f), new Color(0.64f, 0.54f, 0.72f),
            new Color(0.76f, 0.50f, 0.64f) }, 1, 0, 0, 0.4f, 1.3f, 0.25f, 0.6f, new Vector3(0.95f, 1.0f, 0.75f)),
        ["litter"] = new(NoAccent, 0, 0, 0, 0, 1.0f, 0.05f, 0.4f, Vector3.One),
    };

    // A species as drawn: its levels of detail (the far billboard last), a material each, and the distances its levels
    // switch at, worked out from its card counts so that a level only ever lacks cards already gone.
    private sealed class Species
    {
        public required string Key;
        public required bool Litter;
        public required Mesh[] Meshes;
        public required ShaderMaterial[] Materials;
        public required int[] Cards;
        public required float Height, Reach;
        public float[] Switch = Array.Empty<float>(); // distance from which each level after the first is used
        public Vector4 CardCurve, Thin;
        public Vector2 Fade;

        public float CardsKept(float d) => Mix(1, CardCurve.Z, SmoothStep(CardCurve.X, CardCurve.Y, d));

        /// <summary>The share of clumps kept at a distance (the shader's clumps_kept).</summary>
        public float Kept(float d) => Mix(1, Thin.Z, SmoothStep(Thin.X, Thin.Y, d)) * (1 - SmoothStep(Fade.X, Fade.Y, d));

        /// <summary>
        /// The level of detail for a tile whose nearest clump is <paramref name="d"/> away. A level with fewer cards is
        /// taken only once its missing cards have dropped from every clump (no hysteresis that way); going back up has
        /// half a metre of it.
        /// </summary>
        public int LodFor(double d, int current)
        {
            int lod = 0;
            for (int i = 0; i < Switch.Length; i++)
                if (d >= Switch[i] + (current > i ? 0 : 0.5)) lod = i + 1;
            return lod;
        }
    }

    private sealed class SpeciesCatalog
    {
        public required Species[] Species;
        public required SpeciesShape[] Shapes;
        public required int Litter;
    }

    // What the worker needs of a species: its height, the reach of its cards, and whether it lies flat.
    private sealed record SpeciesShape(string Key, float Height, float Reach, bool Litter);

    private Species[] _species = Array.Empty<Species>();

    private static float Mix(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / Math.Max(e1 - e0, 1e-6f), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private SpeciesCatalog LoadCatalog(Vector3 bladeMean, Texture2D noise)
    {
        using JsonDocument document = JsonDocument.Parse(FileAccess.GetFileAsString(CatalogPath));
        JsonElement root = document.RootElement;
        var albedo = GD.Load<Texture2D>(root.GetProperty("albedo").GetString()!);
        var detail = GD.Load<Texture2D>(root.GetProperty("detail").GetString()!);
        float offset = root.GetProperty("variantOffset").GetSingle();
        Node3D source = GD.Load<PackedScene>(root.GetProperty("model").GetString()!).Instantiate<Node3D>();
        var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        void Collect(Node node)
        {
            if (node is MeshInstance3D m && m.Mesh != null) meshes[m.Name] = m.Mesh;
            foreach (Node child in node.GetChildren()) Collect(child);
        }
        Collect(source);
        source.Free();
        var shader = GD.Load<Shader>("res://shaders/surface_grass.gdshader");
        var species = new List<Species>();
        var shapes = new List<SpeciesShape>();
        int litterIndex = -1;
        foreach (JsonElement e in root.GetProperty("species").EnumerateArray())
        {
            string key = e.GetProperty("key").GetString()!;
            Look look = Looks.TryGetValue(key, out Look? l) ? l : throw new InvalidOperationException($"No look for grass species '{key}'.");
            bool litter = key == "litter";
            var levels = new List<Mesh>();
            var cards = new List<int>();
            foreach (JsonElement level in e.GetProperty("lods").EnumerateArray())
            {
                levels.Add(meshes[level.GetProperty("node").GetString()!]);
                cards.Add(level.GetProperty("cards").GetInt32());
            }
            if (e.GetProperty("far").ValueKind == JsonValueKind.Object)
            {
                levels.Add(meshes[e.GetProperty("far").GetProperty("node").GetString()!]);
                cards.Add(0);
            }
            float height = e.GetProperty("height").GetSingle(), reach = e.GetProperty("reach").GetSingle();
            JsonElement mean = e.GetProperty("mean");
            var sp = new Species
            {
                Key = key, Litter = litter, Meshes = levels.ToArray(), Cards = cards.ToArray(), Height = height, Reach = reach,
                Materials = new ShaderMaterial[levels.Count],
            };
            ConfigureDistances(sp);
            for (int i = 0; i < levels.Count; i++)
            {
                var material = new ShaderMaterial { Shader = shader };
                material.SetShaderParameter("albedo_atlas", albedo);
                material.SetShaderParameter("detail_atlas", detail);
                material.SetShaderParameter("macro_noise", noise);
                material.SetShaderParameter("kind", litter ? 2 : i == levels.Count - 1 && cards[i] == 0 ? 1 : 0);
                material.SetShaderParameter("variant_offset", new Vector2(offset, litter ? offset : 0));
                material.SetShaderParameter("species_mean", new Vector3(mean[0].GetSingle(), mean[1].GetSingle(), mean[2].GetSingle()));
                material.SetShaderParameter("blade_mean", bladeMean);
                material.SetShaderParameter("mesh_height", height);
                material.SetShaderParameter("mesh_reach", reach);
                material.SetShaderParameter("dry_bias", look.DryBias);
                material.SetShaderParameter("dry_floor", look.DryFloor);
                material.SetShaderParameter("green", look.Green);
                material.SetShaderParameter("accent_a", look.Accents[0]);
                material.SetShaderParameter("accent_b", look.Accents[1]);
                material.SetShaderParameter("accent_c", look.Accents[2]);
                material.SetShaderParameter("accent_d", look.Accents[3]);
                material.SetShaderParameter("accent_tint", look.AccentTint);
                material.SetShaderParameter("translucency", e.GetProperty("translucency").GetSingle());
                material.SetShaderParameter("glow", look.Glow);
                material.SetShaderParameter("roughness", e.GetProperty("roughness").GetSingle());
                material.SetShaderParameter("sheen", look.Sheen);
                material.SetShaderParameter("fold", look.Fold);
                material.SetShaderParameter("stiffness", look.Stiffness);
                material.SetShaderParameter("card_curve", sp.CardCurve);
                material.SetShaderParameter("thin", sp.Thin);
                material.SetShaderParameter("fade", sp.Fade);
                material.SetShaderParameter("morph_range", _morphRange);
                sp.Materials[i] = material;
            }
            if (litter) litterIndex = species.Count;
            species.Add(sp);
            shapes.Add(new SpeciesShape(key, height, reach, litter));
        }
        // The habitat rule's species are the clumps, in the catalogue's order; litter comes after them.
        GroundCoverHabitats habitats = GroundCoverHabitats.Default;
        for (int i = 0; i < habitats.Species.Length; i++)
            if (i >= species.Count || species[i].Key != habitats.Species[i])
                throw new InvalidOperationException($"Grass species {i} is '{(i < species.Count ? species[i].Key : "missing")}', the habitats' '{habitats.Species[i]}'.");
        if (litterIndex != habitats.Species.Length) throw new InvalidOperationException("The grass catalogue's litter must follow its clumps.");
        _species = species.ToArray();
        return new SpeciesCatalog { Species = _species, Shapes = shapes.ToArray(), Litter = litterIndex };
    }

    // The distances of a species' card thinning, clump thinning and far edge, for this field's reach, and the
    // distances its levels switch at: LOD n+1 once the cards LOD n has beyond it are gone (cards_kept at most the first
    // missing card's rank, with the shader's jitter), the billboard at seven tenths of the reach.
    private void ConfigureDistances(Species sp)
    {
        float far = sp.Litter ? _litterFar : _far;
        int nearest = Math.Max(sp.Cards[0], 1);
        int lastCards = sp.Cards.Length > 1 ? sp.Cards[Math.Min(2, sp.Cards.Length - 1)] : nearest;
        if (lastCards == 0) lastCards = sp.Cards[Math.Max(0, sp.Cards.Length - 2)];
        sp.CardCurve = new Vector4(0.05f * far, 0.5f * far, sp.Litter ? 1 : (lastCards - 0.4f) / nearest, 0);
        sp.Thin = sp.Litter ? new Vector4(0.4f * far, 0.85f * far, 0.6f, 0) : new Vector4(0.3f * far, 0.75f * far, 0.45f, 0);
        sp.Fade = new Vector2(0.85f * far, far);
        var switches = new List<float>();
        for (int i = 1; i < sp.Cards.Length; i++)
        {
            if (sp.Cards[i] == 0)
            {
                switches.Add(Math.Max(0.7f * far, switches.Count > 0 ? switches[^1] + 2 : 0));
                continue;
            }
            float target = sp.Cards[i] / (float)nearest + 0.01f;
            switches.Add(Math.Max(Solve(sp, target), switches.Count > 0 ? switches[^1] : 0));
        }
        sp.Switch = switches.ToArray();
    }

    // The distance at which a species keeps a share of its cards (cards_kept falls with distance).
    private static float Solve(Species sp, float share)
    {
        if (sp.CardsKept(0) <= share) return 0;
        if (sp.CardsKept(sp.CardCurve.Y) > share) return sp.CardCurve.Y + 1;
        float lo = 0, hi = sp.CardCurve.Y;
        for (int i = 0; i < 40; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (sp.CardsKept(mid) > share) lo = mid;
            else hi = mid;
        }
        return hi;
    }

    private void UpdateMaterials(Vector4 trample, Vector3 east, Vector3 north)
    {
        foreach (Species species in _species)
            foreach (ShaderMaterial material in species.Materials)
            {
                if (!species.Litter) material.SetShaderParameter("trample", trample);
                material.SetShaderParameter("wind_east", east);
                material.SetShaderParameter("wind_north", north);
            }
    }

    // ---------------------------------------------------------------- the pool

    // One tile's instances of one species. Pooled by capacity (powers of two): a MultiMesh's buffer is reallocated only
    // when its instance count changes, so a batch keeps its capacity and draws the first Count instances.
    private sealed class Batch
    {
        public MultiMeshInstance3D Instance = null!;
        public MultiMesh MultiMesh = null!;
        public int Species, Capacity, Count, Lod = -1, Visible = -1, Shown = -1;
        public float[] Ranks = Array.Empty<float>();

        /// <summary>Its instances as uploaded (the tile's build keeps them), for a change of mesh (<see cref="SetLod"/>).</summary>
        public float[] Buffer = Array.Empty<float>();

        /// <summary>
        /// Draws its instances with another mesh. A MultiMesh given a new mesh while it holds instances reads them back from
        /// the GPU (to measure them again), a stall of milliseconds; emptied first, it takes the mesh at once and its
        /// instances again from <see cref="Buffer"/>, a plain upload. Its visible count is to be set again after.
        /// </summary>
        public void SetLod(Mesh mesh, Material material)
        {
            MultiMesh.InstanceCount = 0;
            MultiMesh.Mesh = mesh;
            MultiMesh.InstanceCount = Capacity;
            if (Buffer.Length > 0) MultiMesh.Buffer = Buffer;
            Instance.MaterialOverride = material;
            Visible = -1;
        }

        /// <summary>How many of the batch's clumps (sorted by rank) a share keeps.</summary>
        public int Prefix(float keep)
        {
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Ranks[mid] < keep) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }

    private readonly Dictionary<int, Stack<Batch>> _pool = new();
    private int _batchesCreated;

    /// <summary>The instance capacity a count is stored in.</summary>
    internal static int CapacityFor(int count) => Math.Max(32, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(count, 1)));

    private Batch AcquireBatch(int species, int capacity)
    {
        if (!_pool.TryGetValue(capacity, out Stack<Batch>? free) || free.Count == 0)
        {
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = _species[species].Meshes[0],
                InstanceCount = capacity,
            };
            var instance = new MultiMeshInstance3D
            {
                Multimesh = multimesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Visible = false,
            };
            AddChild(instance);
            _batchesCreated++;
            return new Batch { Instance = instance, MultiMesh = multimesh, Species = species, Capacity = capacity };
        }
        Batch batch = free.Pop();
        batch.Species = species;
        return batch;
    }

    private void ReleaseBatch(Batch batch)
    {
        batch.Instance.Visible = false;
        batch.Shown = 0;
        batch.Visible = -1;
        batch.Lod = -1;
        batch.Count = 0;
        batch.Ranks = Array.Empty<float>();
        batch.Buffer = Array.Empty<float>();
        if (_stopped)
        {
            FreeBatch(batch);
            return;
        }
        if (!_pool.TryGetValue(batch.Capacity, out Stack<Batch>? free)) _pool[batch.Capacity] = free = new Stack<Batch>();
        free.Push(batch);
    }

    private static void FreeBatch(Batch batch)
    {
        if (GodotObject.IsInstanceValid(batch.Instance)) batch.Instance.Free();
        batch.MultiMesh.Dispose();
    }
}
