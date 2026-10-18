using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface.Flora;

public sealed partial class FloraLibrary
{
    // The catalog replaces art only: natural-object IDs, scatter and gathering
    // remain authoritative. Exported vertices are Y-up, with the actual stem at
    // (0,0,0), fitted to the existing species capsule in its chopping band.
    private static IReadOnlyList<Species> LoadAuthoredTrees(FloraAppearanceInputs? inputs, Action<Resource> own,
        Action<IDisposable> ownCleanup)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(ownCleanup);
        using JsonDocument catalog = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/surface/trees/catalog.json"));
        var result = new List<Species>();
        Shader shader = GD.Load<Shader>("res://shaders/surface_authored_tree.gdshader");
        foreach (JsonElement row in catalog.RootElement.GetProperty("species").EnumerateArray())
        {
            var species = new Species
            {
                Name = row.GetProperty("name").GetString()!,
                Kind = Enum.Parse<ScatterKind>(row.GetProperty("kind").GetString()!, true),
                Scale = row.GetProperty("scale").GetSingle(), Tree = true,
                Bands = new Bands(row.GetProperty("near").GetSingle(), row.GetProperty("far").GetSingle(), true),
            };
            float height = row.GetProperty("variants")[0].GetProperty("levels")[0].GetProperty("height").GetSingle();
            var meshes = new Dictionary<string, ArrayMesh>(StringComparer.Ordinal);
            var materials = new Dictionary<Material, ShaderMaterial>();
            string model = row.GetProperty("model").GetString()!;
            FloraAppearanceInputs.ImportedTreeScene? captured = null;
            var retirement = new ImportedSceneRetirement();
            ownCleanup(retirement);
            Exception? failure = null;
            void Visit(Node node, Transform3D parent)
            {
                Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
                if (node is MeshInstance3D instance && instance.Mesh is ArrayMesh source)
                {
                    // The CPU exporter bakes every source/world transform into
                    // vertices. Reject a changed import instead of silently
                    // placing rotated/off-centre MultiMeshes or false impostors.
                    if (!transform.IsEqualApprox(Transform3D.Identity))
                        throw new InvalidOperationException("Tree export contains an unbaked transform: " + instance.Name);
                    // A captured scene is private and already owns exact imported geometry. Duplicating an ArrayMesh
                    // serializes uploaded surface buffers (GPU readback), so use its authenticated mesh directly.
                    ArrayMesh mesh = captured == null ? (ArrayMesh)source.Duplicate() : captured.TakeMesh(instance);
                    own(mesh);
                    for (int i = 0; i < source.GetSurfaceCount(); i++)
                    {
                        if (source.SurfaceGetMaterial(i) is not StandardMaterial3D scan || scan.AlbedoTexture == null)
                            throw new InvalidOperationException("Tree is missing its native material: " + instance.Name);
                        if (!materials.TryGetValue(scan, out ShaderMaterial? material))
                        {
                            bool leaves = scan.Transparency != BaseMaterial3D.TransparencyEnum.Disabled;
                            material = new ShaderMaterial();
                            own(material);
                            material.Shader = shader;
                            material.SetShaderParameter("mode", leaves ? 0 : 1);
                            material.SetShaderParameter("albedo_texture", scan.AlbedoTexture);
                            material.SetShaderParameter("albedo_factor", scan.AlbedoColor);
                            material.SetShaderParameter("alpha_cutoff", leaves ? scan.AlphaScissorThreshold : 0f);
                            material.SetMeta("native_tree", true);
                            material.SetMeta("bake_albedo_factor", scan.AlbedoColor);
                            material.SetMeta("bake_alpha_cutoff", leaves ? scan.AlphaScissorThreshold : 0f);
                            material.SetShaderParameter("tree_height", height);
                            material.SetShaderParameter("has_normal", scan.NormalEnabled && scan.NormalTexture != null);
                            if (scan.NormalTexture != null) material.SetShaderParameter("normal_texture", scan.NormalTexture);
                            material.SetShaderParameter("normal_strength", scan.NormalScale);
                            material.SetShaderParameter("has_roughness", scan.RoughnessTexture != null);
                            if (scan.RoughnessTexture != null) material.SetShaderParameter("roughness_texture", scan.RoughnessTexture);
                            material.SetShaderParameter("roughness_factor", scan.Roughness);
                            material.SetShaderParameter("roughness_channel", Channel(scan.RoughnessTextureChannel));
                            material.SetShaderParameter("has_ao", scan.AOEnabled && scan.AOTexture != null);
                            if (scan.AOTexture != null) material.SetShaderParameter("ao_texture", scan.AOTexture);
                            material.SetShaderParameter("ao_channel", Channel(scan.AOTextureChannel));
                            materials.Add(scan, material);
                        }
                        mesh.SurfaceSetMaterial(i, material);
                    }
                    meshes.Add(instance.Name, mesh);
                }
                foreach (Node child in node.GetChildren()) Visit(child, transform);
            }
            try
            {
                captured = inputs?.LoadImportedTreeScene(model, own);
                Node3D root = captured?.Root ?? GD.Load<PackedScene>(model).Instantiate<Node3D>();
                retirement.Attach(root, captured);
                Visit(root, Transform3D.Identity);
                foreach (JsonElement variant in row.GetProperty("variants").EnumerateArray())
                {
                    JsonElement levels = variant.GetProperty("levels");
                    species.Variants.Add(new Variant
                    {
                        Lod0 = meshes[levels[0].GetProperty("node").GetString()!],
                        Lod1 = meshes[levels[1].GetProperty("node").GetString()!],
                        Height = levels[0].GetProperty("height").GetSingle(),
                        Extent = levels[0].GetProperty("extent").GetSingle(),
                    });
                }
                result.Add(species);
            }
            catch (Exception error) { failure = error; throw; }
            finally { RetireImportedScene(retirement, failure); }
        }
        return result;
    }

    // Registered with the creation owner before loading. A failed release retains its root for a later owner pass;
    // successful early release clears references, so retaining this handle until library retirement is harmless.
    private sealed class ImportedSceneRetirement : IDisposable
    {
        private Node3D? _root;
        private FloraAppearanceInputs.ImportedTreeScene? _captured;
        private bool _attached, _captureAttempted;

        public void Attach(Node3D root, FloraAppearanceInputs.ImportedTreeScene? captured = null)
        {
            if (_attached) throw new InvalidOperationException("Imported scene retirement is already attached.");
            _attached = true; _root = root; _captured = captured;
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (OS.GetThreadCallerId() != OS.GetMainThreadId())
                throw new InvalidOperationException("Imported scene retirement requires the main thread.");
            if (GodotObject.IsInstanceValid(_root))
            {
                if (_captured != null && !_captureAttempted)
                {
                    // ImportedTreeScene marks itself disposed before freeing its root. If that free fails, the
                    // next owner pass must retry the retained root directly instead of its now-idempotent wrapper.
                    _captureAttempted = true;
                    _captured.Dispose();
                }
                else _root.Free();
                if (GodotObject.IsInstanceValid(_root))
                    throw new InvalidOperationException("Imported scene root remained alive after retirement.");
            }
            _root = null; _captured = null;
        }
    }

    private static void RetireImportedScene(ImportedSceneRetirement retirement, Exception? failure)
    {
        try { retirement.Dispose(); }
        catch (Exception cleanup) when (failure != null)
        {
            throw new AggregateException("Imported scene creation and retirement both failed.", failure, cleanup);
        }
    }
}
