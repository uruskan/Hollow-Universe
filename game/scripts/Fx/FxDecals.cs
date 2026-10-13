using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>
/// Decals (GROUND.md §9.3): bullet holes, blood, scuffs and scorch marks projected onto what was hit. Each texture has
/// its own small pool of Godot decals that keep that texture for good: swapping a decal's texture makes Godot rebuild
/// its decal atlas, which stalls frames, so a pool reuses its oldest decal instead. Decals fade out at the end of their
/// life and with distance, and skip the view model's render layer and the effects' own (<see cref="FxDirector.RenderLayer"/>).
/// </summary>
public sealed class FxDecals
{
    private sealed class Entry
    {
        public Decal Decal = null!;
        public double Start;
        public float Life;
        public float Fade;
        public float Glow;
        public float Alpha;
        public bool Active;
    }

    private sealed class Pool
    {
        public readonly List<Entry> Entries = new();
        public int Next;
    }

    private readonly Node _parent;
    private readonly Dictionary<string, Pool> _pools = new(StringComparer.Ordinal);

    /// <summary>Decals a texture keeps at most; the oldest makes way.</summary>
    public int PerTexture { get; set; } = 24;

    public int Active { get; private set; }

    public FxDecals(Node parent) => _parent = parent;

    /// <summary>Makes a texture's pool ahead of its first use.</summary>
    public void Prepare(string texture) => PoolFor(texture);

    private Pool PoolFor(string name)
    {
        if (_pools.TryGetValue(name, out Pool? pool))
        {
            return pool;
        }
        pool = new Pool();
        string normal = name + "_normal.png", glow = name + "_glow.png";
        Texture2D albedo = FxAssets.Texture(name + ".png");
        Texture2D? normalTex = ResourceLoader.Exists(FxAssets.Root + normal) ? FxAssets.Texture(normal) : null;
        Texture2D? glowTex = ResourceLoader.Exists(FxAssets.Root + glow) ? FxAssets.Texture(glow) : null;
        // Scorch marks are few and large; holes and stains many and small.
        int count = name.Contains("scorch", StringComparison.Ordinal) ? 10 : PerTexture;
        for (int i = 0; i < count; i++)
        {
            var decal = new Decal
            {
                Name = $"FxDecal_{name.Replace('/', '_')}_{i}",
                Visible = false,
                TextureAlbedo = albedo,
                TextureNormal = normalTex,
                TextureEmission = glowTex,
                CullMask = 0xFFFFF & ~(1u << 1) & ~FxDirector.RenderLayer,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 45f,
                DistanceFadeLength = 15f,
                UpperFade = 0.15f,
                LowerFade = 0.3f,
                NormalFade = 0.35f,
            };
            _parent.AddChild(decal);
            pool.Entries.Add(new Entry { Decal = decal });
        }
        _pools[name] = pool;
        return pool;
    }

    /// <summary>
    /// A decal of <paramref name="texture"/> (a path under assets/fx without ".png"; "_normal" and "_glow" beside it
    /// are used when present) on the surface at <paramref name="position"/> facing <paramref name="normal"/>.
    /// </summary>
    public void Place(double now, string texture, Vector3 position, Vector3 normal, float size, float depth, float life, float fade,
        float glow, float roll, Color tint)
    {
        Pool pool = PoolFor(texture);
        Entry? pick = null;
        foreach (Entry e in pool.Entries)
        {
            if (!e.Active)
            {
                pick = e;
                break;
            }
        }
        if (pick == null)
        {
            pick = pool.Entries[pool.Next];
            pool.Next = (pool.Next + 1) % pool.Entries.Count;
        }
        Decal d = pick.Decal;
        d.EmissionEnergy = glow;
        d.Size = new Vector3(size, depth, size);
        Vector3 n = normal.LengthSquared() > 1e-8f ? normal.Normalized() : Vector3.Up;
        (Vector3 t, Vector3 _) = FxRandom.Frame(n);
        // Right-handed, Y out of the surface: a decal projects along its -Y.
        var basis = new Basis(t, n, t.Cross(n)) * new Basis(Vector3.Up, roll);
        d.GlobalTransform = new Transform3D(basis, position);
        d.Modulate = tint;
        d.Visible = true;
        pick.Start = now;
        pick.Life = Math.Max(0.1f, life);
        pick.Fade = Math.Clamp(fade, 0.05f, pick.Life);
        pick.Glow = glow;
        pick.Alpha = tint.A;
        pick.Active = true;
    }

    public void Update(double now)
    {
        int active = 0;
        foreach (Pool pool in _pools.Values)
        {
            foreach (Entry e in pool.Entries)
            {
                if (!e.Active)
                {
                    continue;
                }
                float t = (float)(now - e.Start);
                if (t >= e.Life)
                {
                    e.Active = false;
                    e.Decal.Visible = false;
                    continue;
                }
                active++;
                float left = e.Life - t;
                if (left < e.Fade)
                {
                    Color c = e.Decal.Modulate;
                    c.A = e.Alpha * left / e.Fade;
                    e.Decal.Modulate = c;
                }
                if (e.Glow > 0)
                {
                    // Embers die down over the first seconds.
                    float k = Mathf.Max(0f, 1f - t / 6f);
                    e.Decal.EmissionEnergy = e.Glow * k * k;
                }
            }
        }
        Active = active;
    }

    /// <summary>Clears every decal (the viewer between scenes).</summary>
    public void Clear()
    {
        foreach (Pool pool in _pools.Values)
        {
            foreach (Entry e in pool.Entries)
            {
                e.Active = false;
                e.Decal.Visible = false;
            }
        }
    }
}
