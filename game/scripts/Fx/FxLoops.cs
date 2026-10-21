using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Characters;

namespace HollowUniverse.Fx;

/// <summary>A running loop (a fire, a trail), as <see cref="FxDirector.Start"/> returns it.</summary>
public readonly record struct FxHandle(int Id)
{
    public bool IsValid => Id != 0;
}

/// <summary>
/// Continuous effects (GROUND.md §9.3): campfires, furnaces, torches, things burning, rocket trails and burning fuses.
/// A loop is a frame (a position and up, or a node it follows) holding flame cards (one mesh, one draw), emitters that
/// put particles into the batches at their rates, and a flickering light. Starting and stopping fade it in and out.
/// </summary>
public sealed class FxLoop
{
    public int Id;
    public FxDef Def = null!;
    public FxParams Params;
    public Vector3 Position;
    public Vector3 Normal = Vector3.Up;
    public double Started;
    public double StopAt = double.MaxValue;
    public float Strength;
    public bool Stopping;
    public bool Rendered;
    public double ActivatedAt = double.NegativeInfinity;
    public float RenderStrength;
    public MeshInstance3D? Flames;
    public OmniLight3D? Light;
    public float[] Accumulators = Array.Empty<float>();
    public Vector3 LastEmit;
    public bool HasLastEmit;

    /// <summary>What the loop gives, 0 to 1 (<see cref="FxDirector.SetIntensity"/>): its emitters' rates, its light and its
    /// flames, on top of its fade.</summary>
    public float Intensity = 1f;
    public float FlickerPhase;

    /// <summary>The loop's frame: the followed node's, or its position with the normal as up (unscaled).</summary>
    public Transform3D Frame()
    {
        if (Params.Follow != null && GodotObject.IsInstanceValid(Params.Follow) && Params.Follow.IsInsideTree())
        {
            return Params.Follow.GlobalTransform.Orthonormalized();
        }
        Vector3 up = Normal.LengthSquared() > 1e-8f ? Normal.Normalized() : Vector3.Up;
        (Vector3 t, Vector3 b) = FxRandom.Frame(up);
        return new Transform3D(new Basis(t, up, b), Position);
    }
}

/// <summary>Builds and caches the flame meshes of loops.</summary>
public static class FxFlameMeshes
{
    private static readonly Dictionary<string, ArrayMesh> Cache = new(StringComparer.Ordinal);
    private static ShaderMaterial? _world, _viewmodel;

    public static ShaderMaterial Material(bool firstPerson)
    {
        if (_world == null)
        {
            Texture2D noise = FxAssets.Texture("textures/noise.png");
            _world = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/fx/shaders/fx_flame.gdshader"), RenderPriority = 2 };
            _world.SetShaderParameter("noise_tex", noise);
            _viewmodel = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/fx/shaders/fx_flame_viewmodel.gdshader"), RenderPriority = 2 };
            _viewmodel.SetShaderParameter("noise_tex", noise);
        }
        if (firstPerson)
        {
            _viewmodel!.SetShaderParameter("viewmodel_fov", CharacterMaterials.ViewmodelFov);
        }
        return firstPerson ? _viewmodel! : _world;
    }

    /// <summary>One card per flame: four corners at its foot, the corner in UV, its size in UV2, its random number and
    /// strength in the colour's red and green.</summary>
    public static ArrayMesh For(FxDef def)
    {
        if (Cache.TryGetValue(def.Key, out ArrayMesh? mesh))
        {
            return mesh;
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var random = new FxRandom(FxRandom.Hash(def.Key));
        foreach (FxLoopDef.Flame f in def.Loop!.Flames)
        {
            float seed = random.Next();
            var colour = new Color(seed, Math.Clamp(f.Power, 0f, 1f), 0f, 1f);
            void V(float u, float v)
            {
                st.SetColor(colour);
                st.SetUV(new Vector2(u, v));
                st.SetUV2(f.Size);
                st.SetNormal(Vector3.Back);
                st.AddVertex(f.Offset);
            }
            V(0, 1);
            V(1, 1);
            V(1, 0);
            V(0, 1);
            V(1, 0);
            V(0, 0);
        }
        mesh = st.Commit();
        Cache[def.Key] = mesh;
        return mesh;
    }
}
