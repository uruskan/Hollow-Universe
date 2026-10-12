using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Ground;

/// <summary>
/// Where the ground game's world models come from (GROUND.md §9): deployables, dropped items and outcrops, by their
/// data keys. The assets package's model catalogue (<c>data/ground/models.json</c>, its <c>ModelCatalog</c>) will
/// implement this; until it lands, <see cref="PlaceholderModels"/> builds plain shapes in code. Swap
/// <see cref="GroundModels.Provider"/> to drop the catalogue in.
/// </summary>
public interface IGroundModels
{
    /// <summary>
    /// A new instance of a deployable's model (items.json key), standing on its base at the origin, facing −Z, about
    /// <paramref name="halfExtents"/> in size; null to draw nothing.
    /// </summary>
    Node3D? Deployable(string key, Vector3 halfExtents);

    /// <summary>A dropped stack of an item lying on the ground; null to draw nothing.</summary>
    Node3D? Dropped(string key);

    /// <summary>
    /// An outcrop (gather.json key) at a depletion stage (0 whole … 2 nearly gone), for an object of size 1: a rock about
    /// 2 m across standing 1.3 m out of the ground, its base at the origin. <paramref name="variant"/> picks a shape.
    /// </summary>
    Node3D? Outcrop(string key, int stage, int variant);
}

/// <summary>The ground game's model provider (placeholders until the assets catalogue lands).</summary>
public static class GroundModels
{
    public static IGroundModels Provider { get; set; } = new PlaceholderModels();
}

/// <summary>
/// Placeholder models made in code (no other game's files): rocks from the planet's own boulder geometry and rock
/// material (once the plants are built, <see cref="Library"/>), boxes, logs and stones for deployables.
/// </summary>
public sealed class PlaceholderModels : IGroundModels
{
    private readonly Dictionary<string, Mesh> _meshes = new();
    private readonly Dictionary<Color, StandardMaterial3D> _materials = new();

    /// <summary>The surface view's plants and rock material, when built; until then rocks are plain grey.</summary>
    public static FloraLibrary? Library { get; set; }

    public Node3D? Deployable(string key, Vector3 halfExtents)
    {
        var root = new Node3D { Name = key };
        Vector3 size = halfExtents * 2;
        switch (key)
        {
            case "campfire":
                for (int i = 0; i < 9; i++)
                {
                    float a = i * Mathf.Tau / 9;
                    Add(root, Stone(i), new Vector3(MathF.Cos(a) * 0.42f, 0.03f, MathF.Sin(a) * 0.42f), new Vector3(0.13f, 0.10f, 0.12f), a);
                }
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.Tau / 3 + 0.4f;
                    var log = Add(root, Cylinder(0.055f, 0.6f), new Vector3(MathF.Cos(a) * 0.12f, 0.14f, MathF.Sin(a) * 0.12f), Vector3.One, 0, Bark);
                    log.Basis = new Basis(new Vector3(-MathF.Sin(a), 0, MathF.Cos(a)), 1.05f) * new Basis(Vector3.Up, -a);
                }
                Add(root, Cylinder(0.3f, 0.02f), new Vector3(0, 0.01f, 0), Vector3.One, 0, new Color(0.13f, 0.12f, 0.11f));
                break;
            case "sleeping-bag":
                Add(root, Capsule(0.36f, 1.9f), new Vector3(0, 0.09f, 0), new Vector3(1, 0.22f, 1), 0, new Color(0.22f, 0.32f, 0.24f)).RotationDegrees = new Vector3(90, 0, 0);
                Add(root, Box(new Vector3(0.62f, 0.12f, 0.36f)), new Vector3(0, 0.1f, -0.68f), Vector3.One, 0, new Color(0.62f, 0.58f, 0.48f));
                break;
            case "bed":
                Add(root, Box(new Vector3(size.X, 0.3f, size.Z)), new Vector3(0, 0.15f, 0), Vector3.One, 0, Wood);
                Add(root, Box(new Vector3(size.X * 0.92f, 0.14f, size.Z * 0.95f)), new Vector3(0, 0.37f, 0), Vector3.One, 0, new Color(0.55f, 0.52f, 0.44f));
                Add(root, Box(new Vector3(size.X * 0.7f, 0.1f, 0.35f)), new Vector3(0, 0.48f, -size.Z * 0.36f), Vector3.One, 0, new Color(0.72f, 0.70f, 0.62f));
                break;
            case "small-box":
            case "large-box":
                Add(root, Box(size), new Vector3(0, size.Y / 2, 0), Vector3.One, 0, Wood);
                foreach (float y in new[] { 0.06f, size.Y - 0.06f })
                {
                    Add(root, Box(new Vector3(size.X + 0.02f, 0.06f, size.Z + 0.02f)), new Vector3(0, y, 0), Vector3.One, 0, WoodDark);
                }
                Add(root, Box(new Vector3(0.1f, 0.12f, 0.03f)), new Vector3(0, size.Y * 0.7f, -size.Z / 2 - 0.01f), Vector3.One, 0, Iron);
                break;
            case "furnace":
                Add(root, Cylinder(0.62f, 1.3f, 0.48f), new Vector3(0, 0.65f, 0), Vector3.One, 0, new Color(0.40f, 0.38f, 0.35f));
                Add(root, Box(new Vector3(0.42f, 0.36f, 0.2f)), new Vector3(0, 0.38f, -0.5f), Vector3.One, 0, new Color(0.08f, 0.07f, 0.06f));
                Add(root, Cylinder(0.14f, 0.5f), new Vector3(0, 1.5f, 0), Vector3.One, 0, Iron);
                break;
            case "workbench-1":
            case "workbench-2":
            case "workbench-3":
            {
                Color top = key == "workbench-1" ? Wood : key == "workbench-2" ? new Color(0.45f, 0.47f, 0.48f) : new Color(0.32f, 0.36f, 0.40f);
                Add(root, Box(new Vector3(size.X, 0.08f, size.Z)), new Vector3(0, size.Y - 0.04f, 0), Vector3.One, 0, top);
                foreach (float x in new[] { -1f, 1f })
                {
                    foreach (float z in new[] { -1f, 1f })
                    {
                        Add(root, Box(new Vector3(0.08f, size.Y - 0.08f, 0.08f)), new Vector3(x * (size.X / 2 - 0.08f), (size.Y - 0.08f) / 2, z * (size.Z / 2 - 0.08f)), Vector3.One, 0, WoodDark);
                    }
                }
                Add(root, Box(new Vector3(0.22f, 0.16f, 0.18f)), new Vector3(size.X / 2 - 0.25f, size.Y + 0.08f, 0), Vector3.One, 0, Iron);
                break;
            }
            default:
                Add(root, Box(size), new Vector3(0, size.Y / 2, 0), Vector3.One, 0, Wood);
                break;
        }
        return root;
    }

    public Node3D? Dropped(string key)
    {
        var root = new Node3D { Name = "Dropped " + key };
        Add(root, Sphere(), new Vector3(0, 0.1f, 0), new Vector3(0.24f, 0.13f, 0.2f), 0, new Color(0.55f, 0.47f, 0.33f));
        Add(root, Cylinder(0.035f, 0.08f), new Vector3(0, 0.24f, 0), Vector3.One, 0, new Color(0.42f, 0.35f, 0.24f));
        return root;
    }

    public Node3D? Outcrop(string key, int stage, int variant)
    {
        var root = new Node3D { Name = key };
        float shrink = 1 - 0.2f * Math.Clamp(stage, 0, 2);
        // The main rock: the planet's boulder shape (its base at 0), as tall as the outcrop's collision sphere (1.35 of
        // its radius) and as wide.
        Add(root, Rock(variant, 3), new Vector3(0, -0.1f, 0), new Vector3(1.0f, 1.5f, 1.0f) * shrink, variant * 1.3f, RockColour, rock: true);
        Color? ore = key switch
        {
            "metal-node" => new Color(0.42f, 0.24f, 0.13f),
            "sulphur-node" => new Color(0.86f, 0.74f, 0.20f),
            _ => null,
        };
        // Veins of ore: chunks in the ore's colour set into the upper side, fewer as it is broken.
        int lumps = ore == null ? 0 : 9 - 2 * Math.Clamp(stage, 0, 2);
        var rng = new Rng(Hash64.Of((ulong)variant, 0x4F5245)); // "ORE"
        for (int i = 0; i < lumps; i++)
        {
            float a = (float)rng.Range(0, Mathf.Tau), r = (float)rng.Range(0.25, 0.7);
            float s = (float)rng.Range(0.13, 0.22) * shrink;
            // On the rock's surface (about 1.2 high in the middle, falling off to the sides), mostly sunk into it.
            float h = (1.2f * MathF.Sqrt(Math.Max(0, 1 - r * r)) - 0.1f) * shrink - s * 0.55f;
            var lump = Add(root, Rock(i + 7, 1), new Vector3(MathF.Cos(a) * r * shrink, h, MathF.Sin(a) * r * shrink), new Vector3(s, s * 1.2f, s), a, ore!.Value, metallic: key == "metal-node");
            lump.Basis = new Basis(new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)), r * 1.1f) * lump.Basis;
        }
        return root;
    }

    // ---------------------------------------------------------------- parts

    private static readonly Color Wood = new(0.52f, 0.38f, 0.23f);
    private static readonly Color WoodDark = new(0.33f, 0.24f, 0.15f);
    private static readonly Color Iron = new(0.30f, 0.31f, 0.32f);
    private static readonly Color Bark = new(0.30f, 0.22f, 0.15f);
    private static readonly Color RockColour = new(0.47f, 0.46f, 0.43f);

    private MeshInstance3D Add(Node3D parent, Mesh mesh, Vector3 at, Vector3 scale, float yaw, Color colour = default, bool rock = false, bool metallic = false)
    {
        var mi = new MeshInstance3D { Mesh = mesh };
        mi.Transform = new Transform3D(new Basis(Vector3.Up, yaw).Scaled(scale), at);
        if (rock && Library?.Rock != null)
        {
            mi.MaterialOverride = Library.Rock;
        }
        else
        {
            mi.MaterialOverride = Material(colour == default ? RockColour : colour, metallic);
        }
        parent.AddChild(mi);
        return mi;
    }

    private Mesh Stone(int seed) => Rock(seed, 1);

    private Mesh Rock(int seed, int detail)
    {
        string name = $"rock{seed % 6}/{detail}";
        if (!_meshes.TryGetValue(name, out Mesh? mesh))
        {
            var rng = new Rng(Hash64.Of((ulong)(seed % 6), 0x524F434B));
            GeometryBuilder g = FloraGeometry.Boulder(seed % 6 + 11, detail, 0.9f + 0.2f * (float)rng.NextDouble(), 0.6f + 0.2f * (float)rng.NextDouble(),
                0.9f + 0.2f * (float)rng.NextDouble(), 0.25f);
            mesh = g.Build(new Material?[] { null });
            _meshes[name] = mesh;
        }
        return mesh;
    }

    private Mesh Box(Vector3 size) => Cached($"box{size}", () => new BoxMesh { Size = size });

    private Mesh Cylinder(float radius, float height, float top = -1) =>
        Cached($"cyl{radius}/{height}/{top}", () => new CylinderMesh { BottomRadius = radius, TopRadius = top < 0 ? radius : top, Height = height, RadialSegments = 12, Rings = 1 });

    private Mesh Capsule(float radius, float height) => Cached($"cap{radius}/{height}", () => new CapsuleMesh { Radius = radius, Height = height, RadialSegments = 16, Rings = 4 });

    private Mesh Sphere() => Cached("sphere", () => new SphereMesh { Radius = 1, Height = 2, RadialSegments = 12, Rings = 6 });

    private Mesh Cached(string key, Func<Mesh> make)
    {
        if (!_meshes.TryGetValue(key, out Mesh? mesh))
        {
            mesh = make();
            _meshes[key] = mesh;
        }
        return mesh;
    }

    private StandardMaterial3D Material(Color colour, bool metallic)
    {
        Color k = metallic ? new Color(colour, 0.5f) : colour;
        if (!_materials.TryGetValue(k, out StandardMaterial3D? m))
        {
            m = new StandardMaterial3D { AlbedoColor = colour, Roughness = metallic ? 0.62f : 0.9f, Metallic = metallic ? 0.35f : 0 };
            _materials[k] = m;
        }
        return m;
    }
}
