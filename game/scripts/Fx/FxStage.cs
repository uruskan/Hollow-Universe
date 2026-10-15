using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Items;

namespace HollowUniverse.Fx;

/// <summary>
/// A place in the effects viewer where effects play (GROUND.md §9.3): its props, where and which way an effect plays,
/// what flashes and loops follow, and where the camera looks from.
/// </summary>
public sealed class FxStation
{
    public string Kind = "";
    public Node3D Root = null!;
    public Vector3 Spot;
    public Vector3 Normal = Vector3.Up;
    public Vector3 Direction;
    public Vector3 Area;
    public Node3D? Follow;
    public Vector3 Look;
    public float Distance = 3f;
    public float Pitch = 18f;
    public float Yaw;
    public bool FullTurn = true;

    /// <summary>A fresh hit point and its normal on the station's surface (impacts land on different spots).</summary>
    public Func<FxRandom, (Vector3 Position, Vector3 Normal)>? Scatter;

    /// <summary>
    /// Plays an effect the way the station's own view will (the Iron Wake's batteries: a muzzle socket, a rocket flying
    /// out, a ripple of barrels; FxStage.Ship.cs); null: the viewer's usual rules. It gets the effect, the viewer's
    /// parameters (seed, scale), the viewer's random source and a way to do something a little later, and returns a
    /// loop's handle.
    /// </summary>
    public Func<FxDef, FxParams, FxRandom, Action<double, Action>, FxHandle>? Play;

    /// <summary>The station's named places (a battery's muzzles and breeches), for scenes that fire it.</summary>
    public Dictionary<string, Node3D> Sockets { get; } = new(StringComparer.Ordinal);
}

/// <summary>Builds the viewer's stations: ground patches of the planet's own textures, rock, walls from the building kit,
/// water, a target dummy, the guns, the campfire, furnace and torch, and the Iron Wake's (FxStage.Ship.cs).</summary>
public static partial class FxStage
{
    private static TextureLayered? _albedo, _normal;
    private static readonly Dictionary<string, Mesh?> Walls = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Material> WallMaterials = new(StringComparer.Ordinal);

    /// <summary>The ground far around the stations: soil.</summary>
    public static Node3D Ground(Node3D parent, float size = 4000f)
    {
        var mesh = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(size, size), SubdivideWidth = 1, SubdivideDepth = 1 },
            MaterialOverride = GroundMaterial(1, new Vector3(0.9f, 0.88f, 0.86f), false),
        };
        parent.AddChild(mesh);
        var body = new StaticBody3D { Name = "GroundBody" };
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        parent.AddChild(body);
        return mesh;
    }

    public static Material GroundMaterial(int layer, Vector3 tint, bool luminance)
    {
        // The ground's layers (HollowUniverse.Surface.GroundLayers), when they are built.
        if (_albedo == null && FileAccess.FileExists(HollowUniverse.Surface.GroundLayers.Folder + "layers.json"))
        {
            _albedo = HollowUniverse.Surface.GroundLayers.Albedo;
            _normal = HollowUniverse.Surface.GroundLayers.Normal;
        }
        if (_albedo == null)
        {
            return new StandardMaterial3D { AlbedoColor = new Color(tint.X * 0.35f, tint.Y * 0.3f, tint.Z * 0.25f), Roughness = 0.95f };
        }
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/fx/shaders/fx_stage_ground.gdshader") };
        m.SetShaderParameter("albedo_array", _albedo);
        if (_normal != null)
        {
            m.SetShaderParameter("normal_array", _normal);
        }
        m.SetShaderParameter("layer", (float)layer);
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("luminance_only", luminance);
        return m;
    }

    /// <summary>A station of <paramref name="kind"/> at <paramref name="at"/>, its front turned by <paramref name="yaw"/>
    /// (radians) from +Z.</summary>
    public static FxStation Build(Node3D parent, string kind, Vector3 at, float yaw)
    {
        var root = new Node3D { Name = "Station_" + kind, Position = at, Rotation = new Vector3(0, yaw, 0) };
        parent.AddChild(root);
        var s = new FxStation { Kind = kind, Root = root, Yaw = yaw };
        Basis basis = root.Basis;
        Vector3 front = basis.Z;
        switch (kind)
        {
            case "dirt":
            case "grass":
            case "sand":
            {
                (int layer, Vector3 tint, bool lum) = kind switch
                {
                    "grass" => (0, new Vector3(0.2f, 0.3f, 0.09f), true),
                    "sand" => (5, new Vector3(1.05f, 1f, 0.95f), false),
                    _ => (1, new Vector3(0.95f, 0.9f, 0.85f), false),
                };
                var patch = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(3.4f, 0.04f, 3.4f) },
                    MaterialOverride = GroundMaterial(layer, tint, lum),
                    Position = new Vector3(0, 0.02f, 0),
                };
                root.AddChild(patch);
                s.Spot = at + new Vector3(0, 0.04f, 0);
                s.Look = at + new Vector3(0, 0.35f, 0);
                s.Distance = 2.6f;
                s.Pitch = 24f;
                s.Direction = (s.Spot - (at + front * 6f + Vector3.Up * 1.6f)).Normalized();
                Vector3 centre = s.Spot;
                Vector3 dir = s.Direction;
                s.Scatter = r => (centre + basis * new Vector3(r.Range(-0.5f, 0.5f), 0, r.Range(-0.4f, 0.4f)), Vector3.Up);
                _ = dir;
                break;
            }
            case "rock":
            {
                Node3D? rock = ModelCatalog.Instantiate("stone-node", 0, ModelUse.Static);
                if (rock != null)
                {
                    root.AddChild(rock);
                }
                s.Look = at + new Vector3(0, 0.6f, 0);
                s.Distance = 3.2f;
                s.Pitch = 14f;
                s.FullTurn = false;
                Vector3 origin = at;
                s.Scatter = r => Probe(root, origin + basis * new Vector3(r.Range(-0.45f, 0.45f), r.Range(0.35f, 0.9f), 3f), -front);
                (s.Spot, s.Normal) = (at + front * 0.9f + Vector3.Up * 0.6f, front);
                s.Direction = -front;
                break;
            }
            case "wood":
            case "metal":
            case "wall":
            {
                string grade = kind == "wall" ? "stone" : kind;
                string piece = kind == "wall" ? "wall" : "half-wall";
                var wall = new MeshInstance3D { Mesh = WallMesh(grade, piece) };
                wall.MaterialOverride = WallMaterial(grade);
                root.AddChild(wall);
                var body = new StaticBody3D();
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3f, kind == "wall" ? 3f : 1.5f, 0.3f) },
                    Position = new Vector3(0, kind == "wall" ? 1.5f : 0.75f, 0) });
                root.AddChild(body);
                float height = kind == "wall" ? 3f : 1.5f;
                s.Spot = at + front * 0.152f + Vector3.Up * height * 0.55f;
                s.Normal = front;
                s.Direction = -front;
                s.Look = at + Vector3.Up * (height * 0.55f) + front * 0.2f;
                s.Distance = kind == "wall" ? 7f : 2.4f;
                s.Pitch = kind == "wall" ? 12f : 8f;
                s.FullTurn = false;
                s.Area = new Vector3(1.5f, 0.2f, height * 0.5f);
                Vector3 o = at;
                s.Scatter = r => (o + basis * new Vector3(r.Range(-0.7f, 0.7f), r.Range(0.45f, Math.Min(1.25f, height - 0.2f)), 0.152f), front);
                break;
            }
            case "water":
            {
                var basin = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(3.6f, 0.08f, 3.6f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.06f, 0.065f), Roughness = 0.04f, Metallic = 0.05f,
                        MetallicSpecular = 0.7f },
                    Position = new Vector3(0, 0.04f, 0),
                };
                root.AddChild(basin);
                foreach ((Vector3 p, Vector3 size) in new[] {
                    (new Vector3(0, 0.07f, 1.85f), new Vector3(3.8f, 0.14f, 0.2f)), (new Vector3(0, 0.07f, -1.85f), new Vector3(3.8f, 0.14f, 0.2f)),
                    (new Vector3(1.85f, 0.07f, 0), new Vector3(0.2f, 0.14f, 3.6f)), (new Vector3(-1.85f, 0.07f, 0), new Vector3(0.2f, 0.14f, 3.6f)) })
                {
                    root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = p, MaterialOverride = WallMaterial("stone") });
                }
                s.Spot = at + new Vector3(0, 0.081f, 0);
                s.Look = at + new Vector3(0, 0.4f, 0);
                s.Distance = 2.8f;
                s.Pitch = 22f;
                Vector3 c = s.Spot;
                s.Scatter = r => (c + basis * new Vector3(r.Range(-0.5f, 0.5f), 0, r.Range(-0.4f, 0.4f)), Vector3.Up);
                break;
            }
            case "flesh":
            {
                var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.31f, 0.22f), Roughness = 0.95f };
                var skin = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.47f, 0.38f), Roughness = 0.7f };
                var wood = WallMaterial("wood");
                root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 1.0f }, Position = new Vector3(0, 0.5f, 0),
                    MaterialOverride = wood });
                root.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.21f, Height = 0.8f }, Position = new Vector3(0, 1.25f, 0),
                    Scale = new Vector3(1.05f, 1f, 0.72f), MaterialOverride = cloth });
                root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.11f, Height = 0.24f }, Position = new Vector3(0, 1.77f, 0), MaterialOverride = skin });
                root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 0.12f }, Position = new Vector3(0, 1.64f, 0),
                    MaterialOverride = skin });
                s.Spot = at + front * 0.16f + Vector3.Up * 1.33f;
                s.Normal = front;
                s.Direction = (basis * new Vector3(-0.85f, -0.05f, -0.5f)).Normalized();
                s.Look = at + Vector3.Up * 1.25f;
                s.Distance = 2.5f;
                s.Pitch = 6f;
                s.FullTurn = false;
                Vector3 o = at;
                s.Scatter = r => (o + basis * new Vector3(r.Range(-0.08f, 0.08f), r.Range(1.15f, 1.5f), 0.16f), front);
                break;
            }
            case "rifle":
            case "pistol":
            case "launcher":
            {
                string key = kind == "launcher" ? "rocket-launcher" : kind;
                Node3D? gun = ModelCatalog.Instantiate(key);
                var holder = new Node3D { Position = new Vector3(0, 1.3f, 0) };
                root.AddChild(holder);
                if (gun != null)
                {
                    ModelCatalog.ForceLod(gun, 0);
                    holder.AddChild(gun);
                    // Its palm socket on the holder: the barrel along the station's +X.
                    Node3D? muzzle = gun.FindChild("socket_muzzle", true, false) as Node3D;
                    s.Follow = muzzle;
                    if (kind == "launcher")
                    {
                        var exhaust = gun.FindChild("socket_exhaust", true, false) as Node3D;
                        s.Root.SetMeta("exhaust", exhaust!);
                    }
                }
                Vector3 m = at + basis * new Vector3(kind == "pistol" ? 0.19f : kind == "launcher" ? 0.5f : 0.64f, 1.37f, 0);
                s.Spot = m;
                s.Normal = basis.X;
                s.Direction = basis.X;
                s.Look = m + basis.X * 0.25f;
                s.Distance = kind == "launcher" ? 2.6f : 1.5f;
                s.Pitch = 6f;
                s.FullTurn = false;
                break;
            }
            case "campfire":
            case "furnace":
            {
                Node3D? model = ModelCatalog.Instantiate(kind);
                if (model != null)
                {
                    root.AddChild(model);
                    s.Follow = model;
                }
                s.Spot = at;
                s.Look = at + Vector3.Up * (kind == "furnace" ? 1.0f : 0.45f);
                s.Distance = kind == "furnace" ? 4.2f : 2.6f;
                s.Pitch = kind == "furnace" ? 10f : 22f;
                break;
            }
            case "torch":
            {
                Node3D? torch = ModelCatalog.Instantiate("torch");
                root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 1.0f }, Position = new Vector3(0, 0.5f, 0),
                    MaterialOverride = WallMaterial("wood") });
                if (torch != null)
                {
                    ModelCatalog.ForceLod(torch, 0);
                    torch.Position = new Vector3(0, 1.05f, 0);
                    root.AddChild(torch);
                    s.Follow = torch.FindChild("socket_flame", true, false) as Node3D;
                }
                s.Spot = at + Vector3.Up * 1.53f;
                s.Look = at + Vector3.Up * 1.55f;
                s.Distance = 1.5f;
                s.Pitch = 8f;
                break;
            }
            case var ship when Array.IndexOf(ShipKinds, ship) >= 0:
                BuildShip(s, root, kind);
                break;
            default:
                s.Spot = at + Vector3.Up * 1.5f;
                s.Look = at + Vector3.Up * 1.5f;
                s.Distance = 8f;
                s.Pitch = 6f;
                break;
        }
        return s;
    }

    /// <summary>Where a ray from <paramref name="from"/> along <paramref name="dir"/> meets the station's collision.</summary>
    private static (Vector3, Vector3) Probe(Node3D root, Vector3 from, Vector3 dir)
    {
        PhysicsDirectSpaceState3D? space = root.GetWorld3D()?.DirectSpaceState;
        if (space != null)
        {
            var query = PhysicsRayQueryParameters3D.Create(from, from + dir * 6f);
            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            if (hit.Count > 0)
            {
                return ((Vector3)hit["position"], (Vector3)hit["normal"]);
            }
        }
        return (from + dir * 2.2f, -dir);
    }

    private static Mesh? WallMesh(string grade, string piece)
    {
        string key = grade + "/" + piece;
        if (Walls.TryGetValue(key, out Mesh? mesh))
        {
            return mesh;
        }
        string path = $"res://assets/building/building_{grade}.glb";
        mesh = null;
        if (ResourceLoader.Exists(path))
        {
            Node scene = GD.Load<PackedScene>(path).Instantiate();
            if (scene.FindChild(piece, true, false) is MeshInstance3D mi)
            {
                mesh = mi.Mesh;
            }
            scene.Free();
        }
        mesh ??= new BoxMesh { Size = new Vector3(3, 1.5f, 0.3f) };
        Walls[key] = mesh;
        return mesh;
    }

    /// <summary>The building kit's material of a grade (its 2K atlas), as the build sandbox makes it.</summary>
    public static Material WallMaterial(string grade)
    {
        if (WallMaterials.TryGetValue(grade, out Material? m))
        {
            return m;
        }
        string root = "res://assets/building/textures/building_" + grade;
        if (ResourceLoader.Exists(root + "_BaseColor.jpg"))
        {
            m = new OrmMaterial3D
            {
                AlbedoTexture = GD.Load<Texture2D>(root + "_BaseColor.jpg"),
                OrmTexture = GD.Load<Texture2D>(root + "_ORM.jpg"),
                NormalEnabled = true,
                NormalTexture = GD.Load<Texture2D>(root + "_NormalGL.jpg"),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            };
        }
        else
        {
            m = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.45f, 0.4f), Roughness = 0.9f };
        }
        WallMaterials[grade] = m;
        return m;
    }
}
