using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>
/// One kind of particle for the whole world (GROUND.md §9.3): a GPUParticles3D in world space that never emits by
/// itself; effects put particles into it with <see cref="Emit"/> (GpuParticles3D.EmitParticle), each with its own size,
/// velocity, colour, life and random number. So a firefight costs one simulation step and one draw call per kind of
/// particle, however many impacts there are. Its capacity caps how many live at once: what doesn't fit is dropped.
/// A batch with nothing alive goes idle after its longest life and costs nothing.
/// </summary>
public sealed class FxBatch
{
    private const GpuParticles3D.EmitFlags AllData = GpuParticles3D.EmitFlags.Position | GpuParticles3D.EmitFlags.RotationScale
        | GpuParticles3D.EmitFlags.Velocity | GpuParticles3D.EmitFlags.Color | GpuParticles3D.EmitFlags.Custom;

    public string Name { get; }

    public GpuParticles3D Node { get; }

    public int Capacity { get; }

    /// <summary>How particles of this batch are oriented at emission.</summary>
    public FxOrient Orient { get; }

    /// <summary>Particles emitted this frame (the director's budget) and in all.</summary>
    public int EmittedThisFrame { get; internal set; }

    public long EmittedTotal { get; private set; }

    public FxBatch(string name, int capacity, float lifetime, Mesh mesh, Material draw, ShaderMaterial process, FxOrient orient)
    {
        Name = name;
        Capacity = capacity;
        Orient = orient;
        Node = new GpuParticles3D
        {
            Name = "Batch_" + name,
            Amount = capacity,
            Lifetime = lifetime,
            Emitting = false,
            OneShot = false,
            LocalCoords = false,
            FixedFps = 0,
            Interpolate = false,
            Explosiveness = 0,
            DrawOrder = orient == FxOrient.Tumble ? GpuParticles3D.DrawOrderEnum.Index : GpuParticles3D.DrawOrderEnum.ViewDepth,
            VisibilityAabb = new Aabb(new Vector3(-4000, -4000, -4000), new Vector3(8000, 8000, 8000)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = FxDirector.RenderLayer,
            ProcessMaterial = process,
            DrawPass1 = mesh,
            MaterialOverride = draw,
            TransformAlign = GpuParticles3D.TransformAlignEnum.Disabled,
        };
    }

    /// <summary>
    /// One particle: where, its size (or, for <see cref="FxOrient.Streak"/>, its length and width), how it starts moving,
    /// its colour and opacity, how long it lives, a random number in 0..1 and the batch's own parameter (growth for
    /// billboards and rings, width for streaks). <paramref name="normal"/> orients rings and chunks.
    /// </summary>
    public void Emit(Vector3 position, float size, Vector3 velocity, Color color, float life, float random, float parameter,
        Vector3 normal, Basis turn)
    {
        if (EmittedThisFrame >= Capacity)
        {
            return;
        }
        Basis basis = Orient switch
        {
            // Rings: Y is the plane's normal, X a direction in it; the size rides on X and Y.
            FxOrient.Plane => PlaneBasis(normal) * Basis.FromScale(new Vector3(size, 1f, size)),
            // Streaks: the base length in X; the shader writes the streak along the velocity into Y.
            FxOrient.Streak => Basis.FromScale(new Vector3(size, size, size)),
            FxOrient.Tumble => turn * Basis.FromScale(new Vector3(size, size, size)),
            _ => Basis.FromScale(new Vector3(size, size, size)),
        };
        Node.EmitParticle(new Transform3D(basis, position), velocity, color, new Color(life, 0f, random, parameter), (uint)AllData);
        EmittedThisFrame++;
        EmittedTotal++;
    }

    /// <summary>Freezes or runs the batch (screenshots).</summary>
    public void SetPaused(bool paused) => Node.SpeedScale = paused ? 0 : 1;

    private static Basis PlaneBasis(Vector3 normal)
    {
        Vector3 n = normal.LengthSquared() > 1e-8f ? normal.Normalized() : Vector3.Up;
        Vector3 t = Mathf.Abs(n.Y) < 0.95f ? Vector3.Up.Cross(n).Normalized() : Vector3.Right.Cross(n).Normalized();
        Vector3 b = n.Cross(t);
        return new Basis(t, n, b);
    }
}

/// <summary>How a batch's particles are oriented at emission.</summary>
public enum FxOrient
{
    /// <summary>Camera-facing: only the size matters.</summary>
    Billboard,

    /// <summary>Along the velocity (sparks, droplets).</summary>
    Streak,

    /// <summary>Lying in the surface's plane (rings).</summary>
    Plane,

    /// <summary>A mesh at a random turn, tumbling (chunks).</summary>
    Tumble,
}

/// <summary>
/// The batches of the effects library and how each looks and moves (GROUND.md §9.3). Built once by the director; the
/// numbers here are the batches' physics and materials, the per-effect numbers are in <c>fx.json</c>.
/// </summary>
public static class FxBatches
{
    private const string Shaders = "res://assets/fx/shaders/";

    public static Dictionary<string, FxBatch> Create(Node parent)
    {
        var quad = new QuadMesh { Size = Vector2.One };
        Texture2D noise = FxAssets.Texture("textures/noise.png");
        var batches = new Dictionary<string, FxBatch>(StringComparer.Ordinal);

        void Add(string name, int capacity, float lifetime, string shader, Dictionary<string, Variant> draw, Dictionary<string, Variant> process,
            FxOrient orient = FxOrient.Billboard, Mesh? mesh = null, int priority = 0)
        {
            var drawMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(Shaders + shader), RenderPriority = priority };
            foreach ((string k, Variant v) in draw)
            {
                drawMaterial.SetShaderParameter(k, v);
            }
            var processMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(Shaders + "fx_process.gdshader") };
            processMaterial.SetShaderParameter("noise_tex", noise);
            foreach ((string k, Variant v) in process)
            {
                processMaterial.SetShaderParameter(k, v);
            }
            var batch = new FxBatch(name, capacity, lifetime, mesh ?? quad, drawMaterial, processMaterial, orient);
            parent.AddChild(batch.Node);
            batches[name] = batch;
        }

        // Hot sparks: streaks along their flight, falling, cooling from white-yellow to red.
        Add("spark", 2048, 2.5f, "fx_spark.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/spark.png"), ["intensity"] = 7f, ["cool_color"] = new Color(1f, 0.32f, 0.08f), ["min_pixels"] = 1.3f,
                ["shrink"] = 0.5f, ["fade_out"] = 0.45f },
            new() { ["gravity"] = 9.0f, ["drag"] = 0.7f, ["align"] = 1, ["stretch"] = 0.011f }, FxOrient.Streak, priority: 3);
        // Embers: small glowing motes rising and wandering.
        Add("ember", 1024, 7f, "fx_add.gdshader",
            new() { ["tex"] = FxAssets.Texture("textures/soft_dot.png"), ["intensity"] = 6f, ["fade_in"] = 0.08f, ["fade_out"] = 0.55f,
                ["softness"] = 0.05f, ["near_fade"] = 0.15f },
            new() { ["gravity"] = -0.9f, ["drag"] = 1.1f, ["turbulence"] = 2.6f, ["turbulence_scale"] = 0.7f }, priority: 2);
        // Flashes: the C4 kit's star, a few frames long.
        Add("flash", 192, 1f, "fx_add.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/flash.png"), ["intensity"] = 10f, ["fade_in"] = 0.0f, ["fade_out"] = 0.9f, ["growth_power"] = 0.35f,
                ["softness"] = 0.15f, ["near_fade"] = 0.1f },
            new(), priority: 4);
        // Fire: the C4 kit's fireball flipbook, premultiplied (black is clear), played over the particle's life.
        Add("fire", 256, 3f, "fx_fire.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/fire_4x4.png"), ["frames"] = new Vector2(4, 4), ["animate"] = true, ["frame_range"] = new Vector2(0, 12),
                ["frame_jitter"] = 2.5f, ["intensity"] = 1.8f, ["soot"] = 0.6f, ["fade_in"] = 0.02f, ["fade_out"] = 0.5f, ["random_flip"] = true,
                ["random_roll"] = true, ["spin"] = 0.6f, ["softness"] = 0.6f, ["near_fade"] = 0.6f, ["growth_power"] = 0.4f },
            new() { ["gravity"] = -1.4f, ["drag"] = 2.2f }, priority: 2);
        // Dust: the C4 kit's dust puff, lit, rounded, turning slowly as it spreads.
        Add("dust", 2048, 12f, "fx_lit.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/dust_puff.png"), ["fade_in"] = 0.04f, ["fade_out"] = 0.7f, ["spin"] = 0.25f, ["softness"] = 0.12f,
                ["growth_power"] = 0.45f, ["near_fade"] = 0.7f, ["vignette"] = 1f },
            new() { ["gravity"] = 0.12f, ["drag"] = 2.6f, ["turbulence"] = 0.5f }, priority: 0);
        // Smoke puffs: single late frames of the C4 smoke flipbook, lit with its normals, rising and spreading.
        Add("smoke", 1024, 16f, "fx_smoke.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/smoke_8x8.png"), ["normal_tex"] = FxAssets.Texture("c4/smoke_normal_8x8.png"),
                ["frames"] = new Vector2(8, 8), ["animate"] = false, ["frame_range"] = new Vector2(30, 63), ["spin"] = 0.12f, ["fade_in"] = 0.03f,
                ["fade_out"] = 0.7f, ["softness"] = 0.6f, ["near_fade"] = 0.9f, ["growth_power"] = 0.45f, ["vignette"] = 1f },
            new() { ["gravity"] = -0.45f, ["drag"] = 1.0f, ["turbulence"] = 0.6f }, priority: 0);
        // The blast's own column: the whole C4 smoke flipbook played over its life, standing on the ground.
        Add("smoke_hero", 48, 12f, "fx_smoke.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/smoke_8x8.png"), ["normal_tex"] = FxAssets.Texture("c4/smoke_normal_8x8.png"),
                ["frames"] = new Vector2(8, 8), ["animate"] = true, ["frame_range"] = new Vector2(0, 63), ["anchor"] = 0.5f, ["random_roll"] = false,
                ["random_flip"] = true, ["fade_in"] = 0.03f, ["fade_out"] = 0.45f, ["softness"] = 1.2f, ["near_fade"] = 1.5f, ["growth_power"] = 0.6f,
                ["edge_fade"] = 0.1f,
                ["glow_color"] = new Color(1.6f, 0.55f, 0.15f), ["glow_time"] = 0.5f },
            new() { ["gravity"] = 0f, ["drag"] = 2f }, priority: 1);
        // Heat haze: the C4 kit's distortion ring for blasts, rising noise for fires.
        Add("haze", 64, 3f, "fx_haze.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/heat_distortion.png"), ["strength"] = 0.035f, ["fade_in"] = 0.0f, ["fade_out"] = 0.8f,
                ["random_roll"] = true, ["growth_power"] = 0.35f, ["softness"] = 0.5f },
            new(), priority: 5);
        Add("haze_rise", 192, 4f, "fx_haze.gdshader",
            new() { ["tex"] = FxAssets.Texture("textures/soft_dot.png"), ["noise_tex"] = noise, ["scroll"] = true, ["strength"] = 0.012f,
                ["fade_in"] = 0.25f, ["fade_out"] = 0.6f, ["softness"] = 0.3f },
            new() { ["gravity"] = -1.2f, ["drag"] = 1.5f }, priority: 5);
        // Rings in a plane: the blast's pressure ring over the ground, ripples on water.
        Add("ring", 128, 3f, "fx_ring.gdshader",
            new() { ["tex"] = FxAssets.Texture("c4/pressure_ring.png") }, new(), FxOrient.Plane, priority: 1);
        // Droplets: blood, water and sand, lit streaks falling.
        Add("droplet", 2048, 3f, "fx_droplet.gdshader",
            new() { ["tex"] = FxAssets.Texture("textures/droplet.png"), ["min_pixels"] = 1.2f, ["shrink"] = 0.3f, ["fade_out"] = 0.35f, ["roughness"] = 0.35f },
            new() { ["gravity"] = 9.0f, ["drag"] = 0.5f, ["align"] = 1, ["stretch"] = 0.018f }, FxOrient.Streak, priority: 1);
        // Debris: chips, clods, splinters, bark and shards that tumble, bounce on the floor and settle.
        Mesh stone = FxAssets.ChunkMesh("chunk_stone"), wood = FxAssets.ChunkMesh("chunk_wood"), metal = FxAssets.ChunkMesh("chunk_metal");
        Add("chunk_stone", 1024, 9f, "fx_chunk.gdshader",
            new() { ["albedo_tex"] = FxAssets.Texture("c4/concrete_albedo.png"), ["shapes"] = 10f, ["roughness"] = 0.9f },
            new() { ["gravity"] = 9.0f, ["drag"] = 0.25f, ["collide"] = true, ["bounce"] = 0.28f, ["friction"] = 0.45f, ["align"] = 2, ["tumble"] = 14f },
            FxOrient.Tumble, stone);
        Add("chunk_wood", 768, 9f, "fx_chunk.gdshader",
            new() { ["albedo_tex"] = FxAssets.Texture("c4/concrete_albedo.png"), ["texture_strength"] = 0.35f, ["shapes"] = 8f,
                ["first_color"] = new Color(0.8f, 0.64f, 0.44f), ["second_color"] = new Color(0.33f, 0.24f, 0.17f), ["roughness"] = 0.85f },
            new() { ["gravity"] = 9.0f, ["drag"] = 0.6f, ["collide"] = true, ["bounce"] = 0.22f, ["friction"] = 0.4f, ["align"] = 2, ["tumble"] = 18f },
            FxOrient.Tumble, wood);
        Add("chunk_metal", 512, 9f, "fx_chunk.gdshader",
            new() { ["albedo_tex"] = FxAssets.Texture("c4/concrete_albedo.png"), ["texture_strength"] = 0.25f, ["shapes"] = 4f,
                ["first_color"] = new Color(0.55f, 0.55f, 0.56f), ["second_color"] = new Color(0.62f, 0.6f, 0.55f), ["metallic"] = 0.85f, ["roughness"] = 0.42f },
            new() { ["gravity"] = 9.0f, ["drag"] = 0.15f, ["collide"] = true, ["bounce"] = 0.4f, ["friction"] = 0.55f, ["align"] = 2, ["tumble"] = 20f },
            FxOrient.Tumble, metal);
        return batches;
    }
}
