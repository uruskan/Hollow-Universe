using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// The walls' rock skins (docs/TERRAIN_SCENERY_PLAN.md T2, <see cref="CliffWall"/>): built by the far sweep with each
/// cell's formation, one mesh a cell and recipe, with the skin's own coarser triangle lists as Godot's levels of detail,
/// so one mesh serves every distance. Each draws its recipe's wall texture, one of the ground's own tileable rock scans
/// from its arrays (cliff_skin.gdshader), stands under its chunk (hidden with it behind the horizon) and casts shadows
/// within the near layer's reach. Meshes are made a few a frame, nearest first.
/// </summary>
public sealed partial class CliffField
{
    /// <summary>A wall texture repeats this many times further apart on a skin than on the ground.</summary>
    private const float SkinTextureScale = 1.0f;
    private const ulong WallBudgetMicroseconds = 1500;

    private readonly record struct WallInstance(MeshInstance3D Node, Vector3D Anchor, double Radius);

    private readonly Queue<(int Cell, CliffWall Wall)> _wallQueue = new();
    private readonly List<WallInstance> _wallInstances = new();
    private readonly Dictionary<int, Material> _wallMaterials = new();
    private Node3D? _wallRoot;
    private Vector3D _wallShadowsFor = new(double.MaxValue, 0, 0);
    /// <summary>`--no-cliff-skins`: the walls without their rock skins, for comparisons.</summary>
    private bool _wallsOff;

    public int WallCount => _wallInstances.Count;
    public int WallPendingCount => _wallQueue.Count;
    public long WallTriangles { get; private set; }

    private void SetupWalls()
    {
        _wallsOff = Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-cliff-skins") >= 0;
        _wallRoot = new Node3D { Name = "Walls" };
        AddChild(_wallRoot);
    }

    // Whether a skin still waits to be made in a cell the field must have built before it is idle.
    private bool WallsPending()
    {
        foreach ((int cell, CliffWall _) in _wallQueue)
            if (_farRequired[cell]) return true;
        return false;
    }

    private void AddWall(int cell, CliffWall wall)
    {
        FarChunk chunk = ChunkFor(wall.Cell);
        double distance = wall.Anchor.Length;
        double angle = Math.Acos(Math.Clamp(Vector3D.Dot(chunk.Anchor.Normalized, wall.Anchor / distance), -1, 1));
        chunk.Angle = Math.Max(chunk.Angle, angle + wall.Radius / Math.Max(distance - wall.Radius, 1));
        chunk.TopRadius = Math.Max(chunk.TopRadius, distance + wall.Radius);
        _wallQueue.Enqueue((cell, wall));
    }

    // Skins made while the frame's budget lasts, at least one; a skin whose recipe names no texture this run loaded is
    // left out.
    private void BuildWalls(Vector3D camera)
    {
        ulong began = Time.GetTicksUsec();
        while (_wallQueue.Count > 0)
        {
            (int cell, CliffWall wall) = _wallQueue.Dequeue();
            if (SkinMaterial(wall.Recipe) is not { } material) continue;
            FarChunk chunk = ChunkFor(wall.Cell);
            Node3D parent = WallNode(chunk);
            var node = new MeshInstance3D
            {
                Name = "Wall",
                Mesh = WallMesh(wall),
                MaterialOverride = material,
                Transform = new Transform3D(_frame.Basis, _frame.DirectionToRender(wall.Anchor - chunk.Anchor)),
                CastShadow = Near(wall.Anchor, wall.Radius, camera),
            };
            parent.AddChild(node);
            _wallInstances.Add(new WallInstance(node, wall.Anchor, wall.Radius));
            WallTriangles += wall.Indices.Length / 3;
            if (Time.GetTicksUsec() - began >= WallBudgetMicroseconds) break;
        }
        // Shadows within the near layer's reach, as its pieces cast them.
        if (Vector3D.Distance(camera, _wallShadowsFor) > RefreshDistance)
        {
            _wallShadowsFor = camera;
            foreach (WallInstance wall in _wallInstances)
                wall.Node.CastShadow = Near(wall.Anchor, wall.Radius, camera);
        }
    }

    private static GeometryInstance3D.ShadowCastingSetting Near(Vector3D anchor, double radius, Vector3D camera) =>
        Vector3D.Distance(anchor, camera) - radius < Reach
            ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;

    private Node3D WallNode(FarChunk chunk)
    {
        if (chunk.WallNode == null)
        {
            chunk.WallNode = new Node3D { Name = "WallChunk", Position = _frame.ToRender(chunk.Anchor), Visible = chunk.Shown };
            _wallRoot!.AddChild(chunk.WallNode);
        }
        return chunk.WallNode;
    }

    // The skin's mesh: its vertices and normals in the body's axes (the node's basis turns them into render axes), its
    // triangles, and its coarser triangle lists keyed by the error each admits.
    private static ArrayMesh WallMesh(CliffWall wall)
    {
        int count = wall.Positions.Length / 3;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(wall.Positions[i * 3], wall.Positions[i * 3 + 1], wall.Positions[i * 3 + 2]);
            normals[i] = new Vector3(wall.Normals[i * 3], wall.Normals[i * 3 + 1], wall.Normals[i * 3 + 2]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = wall.Indices;
        var lods = new Godot.Collections.Dictionary();
        foreach (CliffWallLod lod in wall.Lods) lods[(float)lod.Error] = lod.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, lods);
        return mesh;
    }

    // A recipe's wall material: the first of its wall textures this run loaded, from the ground's arrays.
    private Material? SkinMaterial(int recipe)
    {
        if (_wallMaterials.TryGetValue(recipe, out Material? made)) return made;
        CliffRecipe r = CliffRecipe.All[recipe];
        if (GroundLayers.Find(r.WallTextures) is not { } texture)
        {
            GD.PushWarning($"Cliff wall skins of {r.Key} skipped: none of {string.Join(", ", r.WallTextures)} is loaded.");
            return null;
        }
        var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/cliff_skin.gdshader"), ResourceName = r.Key + "_skin" };
        skin.SetShaderParameter("albedo_array", GroundLayers.Albedo);
        skin.SetShaderParameter("normal_array", GroundLayers.Normal);
        skin.SetShaderParameter("rough_height_array", GroundLayers.RoughHeight);
        skin.SetShaderParameter("layer", texture.Index);
        skin.SetShaderParameter("metres", texture.Metres * SkinTextureScale);
        skin.SetShaderParameter("flip", texture.Flip);
        skin.SetShaderParameter("planet_centre", _frame.Centre);
        skin.SetShaderParameter("planet_radius", (float)_source.Surface.Radius);
        skin.SetShaderParameter("strata", (float)r.WallStrata);
        _wallMaterials[recipe] = skin;
        return skin;
    }
}
