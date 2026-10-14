using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

/// <summary>
/// The building kit's art in the game (GROUND.md §6.3, §9): the meshes of every piece in every grade at three levels of
/// detail (<c>assets/building/building_&lt;grade&gt;.glb</c>), the doors, the garage door and the window inserts, and one
/// material per grade built from the kit's textures (<c>assets/building/textures</c>; the GLBs carry none). A piece
/// missing from the art falls back to its collision hulls, so the rules never wait on art.
/// </summary>
public sealed class BuildingAssets
{
    public const int Lods = 3;

    private static readonly string[] GradeKeys = { "twig", "wood", "stone", "metal", "armoured" };

    private readonly BuildingData _data;
    private readonly Dictionary<(string Key, int Grade, int Lod), Mesh> _pieces = new();
    private readonly Dictionary<(string Name, int Lod), Mesh> _parts = new();
    private readonly Dictionary<string, Material> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<PieceDef, Mesh> _hulls = new();
    private readonly Material[] _grades;

    private BuildingAssets(BuildingData data)
    {
        _data = data;
        _grades = new Material[GradeKeys.Length];
        for (int g = 0; g < GradeKeys.Length; g++)
        {
            _grades[g] = GradeMaterial(GradeKeys[g]);
            _materials["Building_" + GradeKeys[g]] = _grades[g];
        }
        _materials["Door_garage"] = TexturedMaterial("door_garage", "building_metal_NormalGL", 0.85f);
        _materials["Door_glass"] = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.70f, 0.72f, 0.28f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.12f,
            Metallic = 0.1f,
        };
        Overlay = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Colors.White,
            Roughness = 0.9f,
        };
    }

    /// <summary>Lit, coloured by each instance's colour: the stability overlay's material.</summary>
    public Material Overlay { get; }

    /// <summary>The garage door: frame, skinned curtain and its Open and Close clips.</summary>
    public PackedScene? Garage { get; private set; }

    private const string Folder = "res://assets/building/";
    private static readonly string[] PartFiles = { "doors_wood", "doors_metal", "doors_armored", "window_inserts" };
    private const string GarageFile = Folder + "garage_door.glb";

    private static readonly WeakReference<BuildingAssets?> Loaded = new(null);

    /// <summary>The files the kit's art is read from: the grades', the doors' and windows', the garage door's.</summary>
    public static IReadOnlyList<string> Files { get; } = GradeKeys.Select(g => $"{Folder}building_{g}.glb")
        .Concat(PartFiles.Select(f => $"{Folder}{f}.glb")).Append(GarageFile).ToArray();

    /// <summary>
    /// The art for these rules, shared by everyone drawing them at the same time (the game's building, the renderer's
    /// warm-up, the sandbox): its meshes and materials are only drawn, never changed. Read again once nobody holds it.
    /// </summary>
    public static BuildingAssets Load(BuildingData data)
    {
        if (Loaded.TryGetTarget(out BuildingAssets? shared) && shared?._data == data)
        {
            return shared;
        }
        var assets = new BuildingAssets(data);
        for (int g = 0; g < GradeKeys.Length; g++)
        {
            assets.Read($"{Folder}building_{GradeKeys[g]}.glb", (name, lod, mesh) =>
                assets._pieces[(name, g, lod)] = mesh);
        }
        foreach (string file in PartFiles)
        {
            assets.Read($"{Folder}{file}.glb", (name, lod, mesh) => assets._parts[(name, lod)] = mesh);
        }
        if (ResourceLoader.Exists(GarageFile))
        {
            assets.Garage = GD.Load<PackedScene>(GarageFile);
        }
        Loaded.SetTarget(assets);
        return assets;
    }

    /// <summary>Every grade (its index) with one of its pieces' meshes at each level of detail, for the warm-up.</summary>
    public IEnumerable<(int Grade, Mesh[] Lods)> GradeSamples()
    {
        for (int g = 0; g < GradeKeys.Length; g++)
        {
            string? key = _pieces.Keys.Where(k => k.Grade == g && k.Lod == 0 && _data.Piece(k.Key) != null).Select(k => k.Key)
                .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
            if (key != null)
            {
                yield return (g, Enumerable.Range(0, Lods).Select(l => _pieces.GetValueOrDefault((key, g, l)) ?? _pieces[(key, g, 0)]).ToArray());
            }
        }
    }

    /// <summary>Every door and window part's mesh at every level of detail.</summary>
    public IEnumerable<Mesh> AllParts => _parts.Values;

    // Every mesh of an imported GLB by node name; "_LOD1" and "_LOD2" mark the lower levels; collision is skipped.
    private void Read(string path, Action<string, int, Mesh> add)
    {
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"Building art missing: {path} (pieces fall back to their collision shapes).");
            return;
        }
        Node root = GD.Load<PackedScene>(path).Instantiate();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            foreach (Node child in node.GetChildren())
            {
                stack.Push(child);
            }
            if (node is not MeshInstance3D { Mesh: Mesh mesh } instance)
            {
                continue;
            }
            string name = instance.Name;
            int lod = 0;
            if (name.EndsWith("_LOD1", StringComparison.Ordinal))
            {
                lod = 1;
                name = name[..^5];
            }
            else if (name.EndsWith("_LOD2", StringComparison.Ordinal))
            {
                lod = 2;
                name = name[..^5];
            }
            add(name, lod, mesh);
        }
        root.Free();
    }

    private static Material GradeMaterial(string grade) =>
        TexturedMaterial("building_" + grade, "building_" + grade + "_NormalGL", grade == "twig" ? 1f : 0.85f);

    private static Material TexturedMaterial(string prefix, string normal, float normalScale)
    {
        string root = "res://assets/building/textures/";
        if (!ResourceLoader.Exists(root + prefix + "_BaseColor.jpg"))
        {
            return new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.5f, 0.42f), Roughness = 0.9f };
        }
        return new OrmMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>(root + prefix + "_BaseColor.jpg"),
            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>(root + normal + ".jpg"),
            NormalScale = normalScale,
            OrmTexture = GD.Load<Texture2D>(root + prefix + "_ORM.jpg"),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            ResourceName = prefix,
        };
    }

    public Material Grade(int grade) => _grades[Math.Clamp(grade, 0, _grades.Length - 1)];

    /// <summary>A material by the name the kit's GLBs give it (Building_wood, Door_garage, …), or the twig material.</summary>
    public Material Named(string name) => _materials.TryGetValue(name, out Material? m) ? m : _grades[0];

    /// <summary>A piece's mesh in a grade at a level of detail (the nearest level there is), or its collision hulls.</summary>
    public Mesh Piece(PieceDef def, int grade, int lod)
    {
        for (int l = lod; l >= 0; l--)
        {
            if (_pieces.TryGetValue((def.Key, grade, l), out Mesh? mesh))
            {
                return mesh;
            }
        }
        return Hulls(def);
    }

    public bool HasArt(PieceDef def, int grade) => _pieces.ContainsKey((def.Key, grade, 0));

    /// <summary>A corner post's mesh in a grade (<see cref="CornerPosts"/>; corner-&lt;angle&gt;-&lt;cm&gt;), or null when the
    /// grade has none.</summary>
    public Mesh? Corner(string name, int grade) => _pieces.TryGetValue((name, grade, 0), out Mesh? mesh) ? mesh : null;

    /// <summary>A door or window part's mesh (the kit's names: Wood_SingleDoor_Left, Window_MetalBars, …).</summary>
    public Mesh? Part(string name, int lod = 0)
    {
        for (int l = lod; l >= 0; l--)
        {
            if (_parts.TryGetValue((name, l), out Mesh? mesh))
            {
                return mesh;
            }
        }
        return null;
    }

    /// <summary>A part's own material, by its first surface's material name.</summary>
    public Material PartMaterial(Mesh mesh) =>
        mesh.GetSurfaceCount() > 0 && mesh.SurfaceGetMaterial(0) is Material m ? Named(m.ResourceName) : _grades[1];

    /// <summary>A mesh of a piece's collision hulls (the fallback, and the ghost's outline).</summary>
    public Mesh Hulls(PieceDef def)
    {
        if (_hulls.TryGetValue(def, out Mesh? mesh))
        {
            return mesh;
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (ConvexPart part in PieceShapes.For(_data, def))
        {
            foreach ((Num.Vector3 normal, float d) in part.Planes)
            {
                // The face's polygon: the hull's points on this plane, in order around it.
                var points = new List<Vector3>();
                foreach (Num.Vector3 p in part.Points)
                {
                    if (MathF.Abs(Num.Vector3.Dot(normal, p) - d) < 1e-3f)
                    {
                        points.Add(new Vector3(p.X, p.Y, p.Z));
                    }
                }
                if (points.Count < 3)
                {
                    continue;
                }
                var n = new Vector3(normal.X, normal.Y, normal.Z);
                Vector3 centre = Vector3.Zero;
                foreach (Vector3 p in points)
                {
                    centre += p;
                }
                centre /= points.Count;
                Vector3 axisU = (points[0] - centre).Normalized();
                Vector3 axisV = n.Cross(axisU);
                points.Sort((a, b) => MathF.Atan2((a - centre).Dot(axisV), (a - centre).Dot(axisU))
                    .CompareTo(MathF.Atan2((b - centre).Dot(axisV), (b - centre).Dot(axisU))));
                for (int i = 1; i + 1 < points.Count; i++)
                {
                    // Godot's front faces wind clockwise seen from outside.
                    st.SetNormal(n);
                    st.AddVertex(points[0]);
                    st.SetNormal(n);
                    st.AddVertex(points[i + 1]);
                    st.SetNormal(n);
                    st.AddVertex(points[i]);
                }
            }
        }
        mesh = st.Commit();
        _hulls[def] = mesh;
        return mesh;
    }
}
