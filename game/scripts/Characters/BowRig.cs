using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Characters;

/// <summary>
/// The bow while it is drawn (GROUND.md §4.8): its model's limbs bend back as the string is drawn (a "draw" blend shape
/// made from its own mesh), the string runs from each tip to the nock as two thin cords (the model's straight string is
/// taken out of its mesh), and an arrow lies on the string while one is nocked. The same rig serves third and first
/// person; everything is a child of the bow's node, in its space (+X towards the target, +Y up along the limbs, the
/// string at −X).
/// </summary>
public sealed class BowRig
{
    /// <summary>How far the string comes back at full draw (metres).</summary>
    public const float FullDraw = 0.58f;

    /// <summary>How far each tip comes back at full draw (metres).</summary>
    private const float TipFlex = 0.11f;

    private readonly List<MeshInstance3D> _limbs = new();
    private readonly MeshInstance3D _upper, _lower;
    private readonly Node3D? _arrow;
    private readonly Vector3 _top, _bottom;
    private readonly float _stringX, _half;
    private readonly float _arrowHeight;

    private BowRig(Node3D bow, Vector3 top, Vector3 bottom, float stringX)
    {
        _top = top;
        _bottom = bottom;
        _stringX = stringX;
        _arrowHeight = HeldItems.Socket(bow, "arrow_rest").Origin.Y;
        _half = Math.Max(0.3f, (top.Y - bottom.Y) * 0.5f);
        var cord = new CylinderMesh { TopRadius = 0.0017f, BottomRadius = 0.0017f, Height = 1f, RadialSegments = 5, Rings = 1 };
        var material = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.82f, 0.72f), Roughness = 0.85f };
        _upper = new MeshInstance3D { Name = "StringUpper", Mesh = cord, MaterialOverride = material };
        _lower = new MeshInstance3D { Name = "StringLower", Mesh = cord, MaterialOverride = material };
        bow.AddChild(_upper);
        bow.AddChild(_lower);
        _arrow = ModelCatalog.Instantiate("arrow");
        if (_arrow != null)
        {
            _arrow.Name = "Arrow";
            ModelCatalog.ForceLod(_arrow, 0);
            _arrow.Visible = false;
            bow.AddChild(_arrow);
        }
    }

    /// <summary>The nock point (where the string is drawn from) at a draw (0–1), in the bow's space.</summary>
    public Vector3 Nock(float draw) => new(_stringX - FullDraw * Math.Clamp(draw, 0f, 1f), _arrowHeight, 0f);

    /// <summary>Rigs a bow model (an instance from <see cref="HeldItems"/>); null if it has no meshes.</summary>
    public static BowRig? Attach(Node3D bow)
    {
        var meshes = new List<MeshInstance3D>();
        Collect(bow, meshes);
        if (meshes.Count == 0)
        {
            return null;
        }
        // The tips and the string's line, from the most detailed mesh.
        MeshInstance3D finest = meshes.Find(m => m.Name.ToString().EndsWith("_lod0", StringComparison.Ordinal)) ?? meshes[0];
        Transform3D toBow = Relative(bow, finest);
        Vector3 top = new(0, float.MinValue, 0), bottom = new(0, float.MaxValue, 0);
        foreach (Vector3 v in Vertices(finest.Mesh))
        {
            Vector3 p = toBow * v;
            if (p.Y > top.Y)
            {
                top = p;
            }
            if (p.Y < bottom.Y)
            {
                bottom = p;
            }
        }
        // The string: the long thin triangles on the bow's middle plane (it is one tube from tip to tip).
        float sum = 0f;
        int count = 0;
        foreach ((Vector3 a, Vector3 b, Vector3 c) in Triangles(finest.Mesh, toBow))
        {
            if (StringLike(a, b, c, top.Y - bottom.Y))
            {
                sum += a.X + b.X + c.X;
                count += 3;
            }
        }
        float stringX = count > 0 ? sum / count : HeldItems.Socket(bow, "nock").Origin.X + 0.016f;
        var rig = new BowRig(bow, top, bottom, stringX);
        foreach (MeshInstance3D mesh in meshes)
        {
            rig.Rebuild(mesh, Relative(bow, mesh));
        }
        rig.Update(0f, false);
        return rig;
    }

    /// <summary>Bends the limbs to a draw (0–1) and lays the string from the tips to the nock; shows the arrow on it.</summary>
    public void Update(float draw, bool arrow)
    {
        draw = Math.Clamp(draw, 0f, 1f);
        foreach (MeshInstance3D limb in _limbs)
        {
            limb.SetBlendShapeValue(0, draw);
        }
        Vector3 nock = Nock(draw);
        Vector3 top = _top + Flex(_top, draw), bottom = _bottom + Flex(_bottom, draw);
        Place(_upper, top, nock);
        Place(_lower, bottom, nock);
        if (_arrow != null)
        {
            _arrow.Visible = arrow;
            // The arrow's nock on the string, pointing at the target over the grip.
            _arrow.Transform = new Transform3D(Basis.Identity, nock);
        }
    }

    public Node3D? Arrow => _arrow;

    // How far a point of the limbs moves at a draw: back towards the archer, more towards the tips.
    private Vector3 Flex(Vector3 p, float draw)
    {
        float k = Math.Clamp(Math.Abs(p.Y) / _half, 0f, 1f);
        k *= k;
        return new Vector3(-TipFlex * k, -Math.Sign(p.Y) * 0.012f * k, 0f) * draw;
    }

    private static void Place(MeshInstance3D cord, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float length = Math.Max(1e-4f, d.Length());
        Vector3 y = d / length;
        Vector3 x = y.Cross(Vector3.Forward).LengthSquared() > 1e-6f ? y.Cross(Vector3.Forward).Normalized() : y.Cross(Vector3.Right).Normalized();
        Vector3 z = x.Cross(y);
        cord.Transform = new Transform3D(new Basis(x, y * length, z), (a + b) * 0.5f);
    }

    /// <summary>A copy of a limb mesh without the model's string, with a "draw" blend shape.</summary>
    private void Rebuild(MeshInstance3D mesh, Transform3D toBow)
    {
        if (mesh.Mesh is not ArrayMesh source)
        {
            return;
        }
        Transform3D toMesh = toBow.AffineInverse();
        // Normalized: the shape holds the bent positions themselves (as glTF imports do), not offsets.
        var rebuilt = new ArrayMesh { BlendShapeMode = Mesh.BlendShapeMode.Normalized };
        rebuilt.AddBlendShape("draw");
        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = source.SurfaceGetArrays(s);
            Vector3[] verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length == 0)
            {
                indices = new int[verts.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = i;
                }
            }
            var kept = new List<int>(indices.Length);
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                // The string is a thin tube on the line x = stringX, z = 0 (the decimated levels make it a few long
                // triangles from tip to tip, so only that line is tested, not where along it).
                Vector3 a = toBow * verts[indices[t]], b = toBow * verts[indices[t + 1]], c = toBow * verts[indices[t + 2]];
                bool cord = StringLike(a, b, c, _half * 2f) && Math.Abs((a.X + b.X + c.X) / 3f - _stringX) < 0.012f;
                if (!cord)
                {
                    kept.Add(indices[t]);
                    kept.Add(indices[t + 1]);
                    kept.Add(indices[t + 2]);
                }
            }
            arrays[(int)Mesh.ArrayType.Index] = kept.ToArray();
            var shape = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = toBow * verts[i];
                shape[i] = toMesh * (p + Flex(p, 1f));
            }
            var shapeArrays = new Godot.Collections.Array();
            shapeArrays.Resize((int)Mesh.ArrayType.Max);
            shapeArrays[(int)Mesh.ArrayType.Vertex] = shape;
            if (arrays[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
            {
                shapeArrays[(int)Mesh.ArrayType.Normal] = arrays[(int)Mesh.ArrayType.Normal];
            }
            if (arrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil)
            {
                shapeArrays[(int)Mesh.ArrayType.Tangent] = arrays[(int)Mesh.ArrayType.Tangent];
            }
            rebuilt.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, new Godot.Collections.Array<Godot.Collections.Array> { shapeArrays });
            rebuilt.SurfaceSetMaterial(s, source.SurfaceGetMaterial(s));
        }
        Material?[] overrides = new Material?[mesh.GetSurfaceOverrideMaterialCount()];
        for (int i = 0; i < overrides.Length; i++)
        {
            overrides[i] = mesh.GetSurfaceOverrideMaterial(i);
        }
        mesh.Mesh = rebuilt;
        for (int i = 0; i < overrides.Length && i < rebuilt.GetSurfaceCount(); i++)
        {
            mesh.SetSurfaceOverrideMaterial(i, overrides[i]);
        }
        _limbs.Add(mesh);
    }

    /// <summary>A triangle of the string: long along the limbs and thin across, on the bow's middle plane.</summary>
    private static bool StringLike(Vector3 a, Vector3 b, Vector3 c, float height)
    {
        float span = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - Math.Min(a.Y, Math.Min(b.Y, c.Y));
        float width = Math.Max(a.X, Math.Max(b.X, c.X)) - Math.Min(a.X, Math.Min(b.X, c.X));
        return span > height * 0.3f && width < 0.012f && Math.Abs(a.Z) < 0.004f && Math.Abs(b.Z) < 0.004f && Math.Abs(c.Z) < 0.004f;
    }

    private static IEnumerable<(Vector3, Vector3, Vector3)> Triangles(Mesh mesh, Transform3D to)
    {
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            Vector3[] verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                yield return (to * verts[indices[t]], to * verts[indices[t + 1]], to * verts[indices[t + 2]]);
            }
        }
    }

    private static IEnumerable<Vector3> Vertices(Mesh mesh)
    {
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            foreach (Vector3 v in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                yield return v;
            }
        }
    }

    private static void Collect(Node node, List<MeshInstance3D> meshes)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null && !mi.Name.ToString().Contains("hull", StringComparison.Ordinal))
        {
            meshes.Add(mi);
        }
        foreach (Node child in node.GetChildren())
        {
            Collect(child, meshes);
        }
    }

    /// <summary>A node's transform in an ancestor's space.</summary>
    private static Transform3D Relative(Node3D ancestor, Node3D node)
    {
        Transform3D t = Transform3D.Identity;
        for (Node3D? n = node; n != null && n != ancestor; n = n.GetParent() as Node3D)
        {
            t = n.Transform * t;
        }
        return t;
    }
}
