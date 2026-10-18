using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface.Flora;

public sealed partial class FloraLibrary
{
    private static Species LoadAuthoredRocks(Action<Resource> own, Action<IDisposable> ownCleanup)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(ownCleanup);
        using JsonDocument catalog = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/surface/rocks/catalog.json"));
        var rocks = new Species { Name = "rock", Kind = ScatterKind.Rock, Scale = 1,
            Bands = new Bands(55, 230, false), Tree = false };
        var meshes = new Dictionary<string, ArrayMesh>(StringComparer.Ordinal);
        var materials = new Dictionary<Material, ShaderMaterial>();
        Shader shader = GD.Load<Shader>("res://shaders/surface_scanned_rock.gdshader");
        var retirement = new ImportedSceneRetirement();
        ownCleanup(retirement);
        Exception? failure = null;
        void Visit(Node node)
        {
            if (node is MeshInstance3D instance && instance.Mesh is ArrayMesh source)
            {
                // Export bakes node transforms into vertices so MultiMesh and bounds use identical local coordinates.
                if (!instance.Transform.IsEqualApprox(Transform3D.Identity))
                    throw new InvalidOperationException("Rock export contains an unbaked transform: " + instance.Name);
                var mesh = (ArrayMesh)source.Duplicate();
                own(mesh);
                for (int i = 0; i < mesh.GetSurfaceCount(); i++)
                {
                    if (source.SurfaceGetMaterial(i) is not StandardMaterial3D scan)
                        throw new InvalidOperationException("Rock is missing its scanned PBR material: " + instance.Name);
                    if (!materials.TryGetValue(scan, out ShaderMaterial? material))
                    {
                        material = new ShaderMaterial();
                        own(material);
                        material.Shader = shader;
                        material.SetShaderParameter("albedo_map", scan.AlbedoTexture);
                        material.SetShaderParameter("normal_map", scan.NormalTexture);
                        material.SetShaderParameter("roughness_map", scan.RoughnessTexture);
                        material.SetShaderParameter("albedo_factor", scan.AlbedoColor);
                        material.SetShaderParameter("roughness_factor", scan.Roughness);
                        material.SetShaderParameter("normal_strength", scan.NormalScale);
                        material.SetShaderParameter("roughness_channel", Channel(scan.RoughnessTextureChannel));
                        material.SetShaderParameter("has_ao", scan.AOEnabled && scan.AOTexture != null);
                        if (scan.AOTexture != null) material.SetShaderParameter("ao_map", scan.AOTexture);
                        material.SetShaderParameter("ao_channel", Channel(scan.AOTextureChannel));
                        materials.Add(scan, material);
                    }
                    mesh.SurfaceSetMaterial(i, material);
                }
                meshes.Add(instance.Name, mesh);
            }
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        try
        {
            Node3D root = GD.Load<PackedScene>(catalog.RootElement.GetProperty("model").GetString()!).Instantiate<Node3D>();
            retirement.Attach(root);
            Visit(root);
            foreach (JsonElement row in catalog.RootElement.GetProperty("variants").EnumerateArray())
            {
                JsonElement levels = row.GetProperty("levels");
                rocks.Variants.Add(new Variant { Lod0 = meshes[levels[0].GetProperty("node").GetString()!],
                    Lod1 = meshes[levels[1].GetProperty("node").GetString()!],
                    Height = levels[0].GetProperty("height").GetSingle(), Extent = levels[0].GetProperty("extent").GetSingle() });
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally { RetireImportedScene(retirement, failure); }
        return rocks;
    }

    private static Vector4 Channel(BaseMaterial3D.TextureChannel channel) => channel switch
    {
        BaseMaterial3D.TextureChannel.Green => new Vector4(0, 1, 0, 0),
        BaseMaterial3D.TextureChannel.Blue => new Vector4(0, 0, 1, 0),
        BaseMaterial3D.TextureChannel.Alpha => new Vector4(0, 0, 0, 1),
        BaseMaterial3D.TextureChannel.Grayscale => new Vector4(1f / 3, 1f / 3, 1f / 3, 0),
        _ => new Vector4(1, 0, 0, 0),
    };
}
