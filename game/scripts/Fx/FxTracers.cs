using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>
/// Tracers and trails (GROUND.md §9.3): bright streaks that fly the projectile's own path, with the same gravity and
/// drag as <c>ClientProjectile.PositionAt</c>, so a tracer meets the impact where the bullet does. The head runs ahead,
/// the tail follows a fixed time behind; told where the projectile stopped, the head stays there and the tail catches
/// up. All of them are one MultiMesh, rewritten each frame.
/// </summary>
public sealed class FxTracers
{
    private sealed class Tracer
    {
        public int Id;
        public FxTracerDef Def = null!;
        public Vector3 Origin;
        public Vector3 Velocity;
        public Vector3 Gravity;
        public float Drag;
        public double Start;
        public Vector3? End;
        public float EndDistance;
        public Color Tint;
    }

    private readonly MultiMeshInstance3D _node;
    private readonly MultiMesh _multimesh;
    private readonly List<Tracer> _live = new();
    private readonly int _capacity;
    private int _nextId = 1;

    public int Active => _live.Count;

    public FxTracers(Node parent, int capacity)
    {
        _capacity = capacity;
        _multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
            Mesh = Strip(),
            CustomAabb = new Aabb(new Vector3(-4000, -4000, -4000), new Vector3(8000, 8000, 8000)),
        };
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/fx/shaders/fx_tracer.gdshader"), RenderPriority = 3 };
        _node = new MultiMeshInstance3D
        {
            Name = "Tracers",
            Multimesh = _multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = FxDirector.RenderLayer,
            CustomAabb = new Aabb(new Vector3(-4000, -4000, -4000), new Vector3(8000, 8000, 8000)),
        };
        parent.AddChild(_node);
    }

    /// <summary>A new tracer from <paramref name="origin"/>; returns its id (0 when the pool is full).</summary>
    public int Launch(double now, FxTracerDef def, Vector3 origin, Vector3 velocity, Vector3 gravity, float drag, Color tint)
    {
        if (_live.Count >= _capacity)
        {
            // The oldest makes way.
            _live.RemoveAt(0);
        }
        var t = new Tracer
        {
            Id = _nextId++,
            Def = def,
            Origin = origin,
            Velocity = velocity,
            Gravity = gravity,
            Drag = drag,
            Start = now,
            Tint = tint,
        };
        _live.Add(t);
        return t.Id;
    }

    /// <summary>The projectile stopped at <paramref name="at"/>: the head stays there, the tail catches up.</summary>
    public void End(int id, Vector3 at)
    {
        foreach (Tracer t in _live)
        {
            if (t.Id == id)
            {
                t.End = at;
                t.EndDistance = (at - t.Origin).Length();
                return;
            }
        }
    }

    public void Update(double now)
    {
        int count = 0;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Tracer t = _live[i];
            float age = (float)(now - t.Start);
            float tailAge = Math.Max(0f, age - t.Def.Length);
            Vector3 tail = At(t, tailAge);
            bool tailDone = t.End != null && (tail - t.Origin).Length() >= t.EndDistance;
            if (age > t.Def.MaxTime + t.Def.Length || tailDone)
            {
                _live.RemoveAt(i);
            }
        }
        foreach (Tracer t in _live)
        {
            float age = (float)(now - t.Start);
            Vector3 head = At(t, age);
            Vector3 tail = At(t, Math.Max(0f, age - t.Def.Length));
            float fade = t.Def.FadeIn > 0 ? Math.Clamp(age / t.Def.FadeIn, 0f, 1f) : 1f;
            if (age > t.Def.MaxTime)
            {
                fade *= Math.Max(0f, 1f - (age - t.Def.MaxTime) / Math.Max(1e-3f, t.Def.Length));
            }
            var basis = new Basis(tail - head, new Vector3(t.Def.Width, t.Def.Brightness * FxDirector.Brightness, t.Def.Opacity * fade), Vector3.Zero);
            _multimesh.SetInstanceTransform(count, new Transform3D(basis, head));
            _multimesh.SetInstanceColor(count, t.Tint * t.Def.Color);
            count++;
        }
        _multimesh.VisibleInstanceCount = count;
    }

    /// <summary>
    /// Where the projectile is <paramref name="time"/> seconds after launch, held at its end once stopped. Gravity is
    /// re-aimed toward the planet's centre as it flies, as the client's projectile does (Hollow.Client.Arc).
    /// </summary>
    private static Vector3 At(Tracer t, float time)
    {
        Vector3 p;
        Vector3 away = t.Origin + t.Velocity * (time / 3) - HollowUniverse.Surface.PlanetGlobals.Centre;
        Vector3 gravity = away.LengthSquared() > 1e-6f ? away.Normalized() * -t.Gravity.Length() : t.Gravity;
        if (t.Drag > 1e-6f)
        {
            Vector3 g = gravity / t.Drag;
            p = t.Origin + g * time + (t.Velocity - g) * ((1f - MathF.Exp(-t.Drag * time)) / t.Drag);
        }
        else
        {
            p = t.Origin + t.Velocity * time + gravity * (0.5f * time * time);
        }
        if (t.End is { } end && (p - t.Origin).Length() >= t.EndDistance)
        {
            return end;
        }
        return p;
    }

    public void Clear()
    {
        _live.Clear();
        _multimesh.VisibleInstanceCount = 0;
    }

    /// <summary>The card of one tracer: x 0 at the tail to 1 at the head, y -1..1 across.</summary>
    private static ArrayMesh Strip()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void V(float x, float y)
        {
            st.SetUV(new Vector2(x, (y + 1f) * 0.5f));
            st.SetNormal(Vector3.Back);
            st.AddVertex(new Vector3(x, y, 0f));
        }
        V(0, -1);
        V(1, -1);
        V(1, 1);
        V(0, -1);
        V(1, 1);
        V(0, 1);
        return st.Commit();
    }
}
