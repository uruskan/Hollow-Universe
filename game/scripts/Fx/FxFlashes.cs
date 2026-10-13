using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Characters;

namespace HollowUniverse.Fx;

/// <summary>
/// Muzzle flashes (GROUND.md §9.3): a pool of small card meshes that sit on a gun's muzzle socket (+X along the barrel)
/// for two or three frames. Each shot picks one of four flame variants, turns the cards about the barrel and stretches
/// them a little, so no two flashes look alike. Styles: <c>rifle</c>, <c>pistol</c> and <c>brake</c> (a muzzle brake
/// throws the flame out of its side ports). First person draws with the view model's field of view, on its render layer,
/// without shadows; third person on the world's layer.
/// </summary>
public sealed class FxFlashes
{
    private sealed class Entry
    {
        public MeshInstance3D Mesh = null!;
        public double Start;
        public float Life;
        public float Stretch;
        public float Roll;
        public Node3D? Follow;
        public Transform3D At;
        public bool Active;
    }

    private readonly List<Entry> _pool = new();
    private readonly Dictionary<string, ArrayMesh> _meshes = new(StringComparer.Ordinal);
    private readonly ShaderMaterial _world;
    private readonly ShaderMaterial _viewmodel;

    public FxFlashes(Node parent, int count)
    {
        Texture2D atlas = FxAssets.Texture("textures/muzzle_flash.png");
        _world = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/fx/shaders/fx_muzzle.gdshader"), RenderPriority = 4 };
        _world.SetShaderParameter("atlas", atlas);
        _viewmodel = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/fx/shaders/fx_muzzle_viewmodel.gdshader"), RenderPriority = 4 };
        _viewmodel.SetShaderParameter("atlas", atlas);
        foreach (string style in new[] { "rifle", "pistol", "brake" })
        {
            _meshes[style] = Build(style);
        }
        for (int i = 0; i < count; i++)
        {
            var mesh = new MeshInstance3D
            {
                Name = $"FxFlash{i}",
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 1f,
            };
            parent.AddChild(mesh);
            _pool.Add(new Entry { Mesh = mesh });
        }
    }

    /// <summary>A flash on <paramref name="muzzle"/> (followed while it lasts) or, without one, at <paramref name="at"/>.</summary>
    public void Fire(double now, string style, Node3D? muzzle, Transform3D at, bool firstPerson, float life, float scale, FxRandom random)
    {
        Entry? pick = null;
        double oldest = double.MaxValue;
        foreach (Entry e in _pool)
        {
            if (!e.Active)
            {
                pick = e;
                break;
            }
            if (e.Start < oldest)
            {
                oldest = e.Start;
                pick = e;
            }
        }
        if (pick == null)
        {
            return;
        }
        MeshInstance3D m = pick.Mesh;
        m.Mesh = _meshes.TryGetValue(style, out ArrayMesh? mesh) ? mesh : _meshes["rifle"];
        m.MaterialOverride = firstPerson ? _viewmodel : _world;
        m.Layers = firstPerson ? ViewmodelView.RenderLayer : FxDirector.RenderLayer;
        if (firstPerson)
        {
            _viewmodel.SetShaderParameter("viewmodel_fov", CharacterMaterials.ViewmodelFov);
        }
        m.SetInstanceShaderParameter("fx_variant", (float)Math.Min(3, (int)(random.Next() * 4)));
        m.SetInstanceShaderParameter("fx_intensity", FxDirector.Brightness);
        pick.Start = now;
        pick.Life = Math.Max(0.016f, life);
        pick.Stretch = scale * random.Range(0.85f, 1.2f);
        pick.Roll = random.Range(0f, MathF.Tau);
        pick.Follow = muzzle;
        pick.At = at;
        pick.Active = true;
        Place(pick, 0f);
        m.Visible = true;
    }

    public void Update(double now)
    {
        foreach (Entry e in _pool)
        {
            if (!e.Active)
            {
                continue;
            }
            float t = (float)(now - e.Start) / e.Life;
            if (t >= 1f || (e.Follow != null && !GodotObject.IsInstanceValid(e.Follow)))
            {
                e.Active = false;
                e.Mesh.Visible = false;
                continue;
            }
            // Full on the first frame, then gone fast; it grows a little as it goes.
            e.Mesh.SetInstanceShaderParameter("fx_intensity", (1f - t) * (1f - t * 0.5f) * FxDirector.Brightness);
            Place(e, t);
        }
    }

    private static void Place(Entry e, float t)
    {
        Transform3D frame = e.Follow != null && GodotObject.IsInstanceValid(e.Follow) ? e.Follow.GlobalTransform.Orthonormalized() : e.At;
        float grow = 1f + 0.3f * t;
        var local = new Transform3D(new Basis(Vector3.Right, e.Roll).Scaled(new Vector3(e.Stretch * grow, e.Stretch * (0.9f + 0.2f * t), e.Stretch * (0.9f + 0.2f * t))),
            Vector3.Zero);
        e.Mesh.GlobalTransform = frame * local;
    }

    // ---------------------------------------------------------------- meshes

    /// <summary>The cards of a style: flames along the barrel (or out of a brake's ports) and a star across it.</summary>
    private static ArrayMesh Build(string style)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        switch (style)
        {
            case "pistol":
                Flames(st, Vector3.Right, 0.18f, 0.12f, 3, 0f);
                Star(st, 0.012f, 0.17f);
                break;
            case "brake":
                // Out of the ports to both sides and a little up; a short flame ahead.
                foreach (Vector3 dir in new[] { new Vector3(0.25f, 0f, 1f), new Vector3(0.25f, 0f, -1f), new Vector3(0.3f, 0.75f, 0.45f), new Vector3(0.3f, 0.75f, -0.45f) })
                {
                    Flames(st, dir.Normalized(), dir.Y > 0 ? 0.11f : 0.17f, dir.Y > 0 ? 0.07f : 0.1f, 2, 0.02f);
                }
                Flames(st, Vector3.Right, 0.1f, 0.07f, 2, 0.04f);
                Star(st, 0.03f, 0.14f);
                break;
            default:
                Flames(st, Vector3.Right, 0.32f, 0.15f, 3, 0f);
                Star(st, 0.015f, 0.24f);
                break;
        }
        return st.Commit();
    }

    /// <summary><paramref name="cards"/> cards crossing along <paramref name="dir"/>, from <paramref name="start"/> along the barrel.</summary>
    private static void Flames(SurfaceTool st, Vector3 dir, float length, float width, int cards, float start)
    {
        (Vector3 t, Vector3 b) = FxRandom.Frame(dir);
        Vector3 origin = Vector3.Right * start;
        for (int i = 0; i < cards; i++)
        {
            float a = MathF.PI * i / cards;
            Vector3 across = t * MathF.Cos(a) + b * MathF.Sin(a);
            Quad(st, origin - across * width * 0.5f, origin + dir * length - across * width * 0.5f, origin + dir * length + across * width * 0.5f,
                origin + across * width * 0.5f, 0f);
        }
    }

    private static void Star(SurfaceTool st, float x, float size)
    {
        float h = size * 0.5f;
        Quad(st, new Vector3(x, -h, -h), new Vector3(x, -h, h), new Vector3(x, h, h), new Vector3(x, h, -h), 1f);
    }

    /// <summary>A card a-b-c-d with UV (0,0) at a, (1,0) at b, (1,1) at c; UV2.x marks side flames (0) or the star (1).</summary>
    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float kind)
    {
        Vector3 n = (b - a).Cross(d - a).Normalized();
        void V(Vector3 p, Vector2 uv)
        {
            st.SetNormal(n);
            st.SetUV(uv);
            st.SetUV2(new Vector2(kind, 0f));
            st.AddVertex(p);
        }
        V(a, new Vector2(0, 0));
        V(b, new Vector2(1, 0));
        V(c, new Vector2(1, 1));
        V(a, new Vector2(0, 0));
        V(c, new Vector2(1, 1));
        V(d, new Vector2(0, 1));
    }
}
