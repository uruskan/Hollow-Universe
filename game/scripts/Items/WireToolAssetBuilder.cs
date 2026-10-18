using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace HollowUniverse.Items;

/// <summary>Reproducibly bakes the original editable probe into the same mesh/material/socket contract as other held items.</summary>
public static class WireToolAssetBuilder
{
    public const string SourcePath = "res://assets/items/wire-tool-source.tscn";
    public const string OutputPath = "res://assets/items/wire-tool.tscn";
    private const string MaterialDirectory = "res://assets/items/materials";

    public static void Build()
    {
        PackedScene source = ResourceLoader.Load<PackedScene>(SourcePath, cacheMode: ResourceLoader.CacheMode.Ignore)
            ?? throw new InvalidOperationException("Missing editable Wire Tool source: " + SourcePath);
        Node3D root = source.Instantiate<Node3D>();
        try
        {
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(MaterialDirectory));
            var materials = new Dictionary<ulong, Material>();
            bool anyBounds = false;
            Aabb bounds = default;
            int meshes = 0;
            Convert(root);
            if (!anyBounds || meshes == 0) throw new InvalidOperationException("Wire Tool source contains no primitive meshes");

            if (root.GetNodeOrNull<Marker3D>("grip") is { } grip) grip.Name = "socket_grip_r";
            if (root.GetNodeOrNull<Marker3D>("probe") is { } tip) tip.Name = "socket_wire_tip";
            if (root.GetNodeOrNull<Marker3D>("socket_grip_r") == null)
                throw new InvalidOperationException("Wire Tool source must define its right-hand grip marker");

            var body = new StaticBody3D { Name = "WireToolCollision" };
            root.AddChild(body); body.Owner = root;
            var collision = new CollisionShape3D
            {
                Name = "CollisionShape3D", Position = bounds.Position + bounds.Size * .5f,
                Shape = new BoxShape3D { Size = bounds.Size },
            };
            body.AddChild(collision); collision.Owner = root;
            using var packed = new PackedScene();
            Check(packed.Pack(root), "pack Wire Tool");
            Check(ResourceSaver.Save(packed, OutputPath), "save Wire Tool");
            // A review/icon run in the same process must observe the baked scene, even if a catalogue warmed it early.
            ResourceLoader.Load<PackedScene>(OutputPath, cacheMode: ResourceLoader.CacheMode.Replace);
            GD.Print($"[wire-tool-build] {meshes} ArrayMesh parts, {materials.Count} external PBR materials, grip/tip sockets and convex box -> {OutputPath}");

            void Convert(Node node)
            {
                if (node != root) node.Owner = root;
                if (node is MeshInstance3D instance && instance.Mesh is PrimitiveMesh primitive)
                {
                    string part = instance.Name.ToString();
                    var mesh = new ArrayMesh { ResourceName = part + "_mesh" };
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, primitive.GetMeshArrays());
                    if (primitive.Material is { } original)
                    {
                        ulong key = original.GetInstanceId();
                        if (!materials.TryGetValue(key, out Material? material))
                        {
                            material = (Material)original.Duplicate();
                            material.ResourceName = "WireTool_" + part;
                            string path = MaterialDirectory + "/wire-tool-" + part.ToLowerInvariant() + ".tres";
                            Check(ResourceSaver.Save(material, path, ResourceSaver.SaverFlags.ChangePath), "save material " + part);
                            // A duplicated source subresource can retain its embedded identity after Save(ChangePath).
                            // Bind the mesh to the reloaded external resource and explicitly claim its stable path.
                            material = ResourceLoader.Load<Material>(path, cacheMode: ResourceLoader.CacheMode.Ignore)
                                ?? throw new InvalidOperationException("Cannot reload saved Wire Tool material: " + path);
                            material.TakeOverPath(path);
                            materials.Add(key, material);
                        }
                        mesh.SurfaceSetMaterial(0, material);
                    }
                    instance.Mesh = mesh;
                    instance.Name = part + "_lod0";
                    Transform3D transform = Transform3D.Identity;
                    for (Node3D? at = instance; at != null && at != root; at = at.GetParent() as Node3D)
                        transform = at.Transform * transform;
                    Aabb box = transform * mesh.GetAabb();
                    bounds = anyBounds ? bounds.Merge(box) : box;
                    anyBounds = true; meshes++;
                }
                foreach (Node child in node.GetChildren()) Convert(child);
            }
        }
        finally { root.Free(); }
    }

    private static void Check(Error error, string operation)
    { if (error != Error.Ok) throw new InvalidOperationException($"Cannot {operation}: {error}"); }
}
