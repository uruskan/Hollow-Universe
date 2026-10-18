using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Root adds explicit calls to these methods. Synthetic coverage needs no asset catalog;
    // the separate six-fern import qualification deliberately loads the real fern material set.
    private void CheckAuthoredCoverRoots()
    {
        using var sourceMesh = new ArrayMesh();
        using var material = new StandardMaterial3D();
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back };
        arrays[(int)Mesh.ArrayType.Tangent] = new float[] { 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1 };
        arrays[(int)Mesh.ArrayType.Color] = new[] { Colors.White, Colors.Red, Colors.Green };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { Vector2.Zero, Vector2.Right, Vector2.Up };
        arrays[(int)Mesh.ArrayType.TexUV2] = new[] { new Vector2(0.25f, 0), new Vector2(0.75f, 0), new Vector2(0.5f, 1) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 2, 0, 1 };
        sourceMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        sourceMesh.SurfaceSetMaterial(0, material);
        sourceMesh.SurfaceSetName(0, "preserve-name");
        var source = new MeshInstance3D { Name = "fern_v0_lod0", Mesh = sourceMesh };
        try
        {
            AuthoredCoverRootMesh.Description before = AuthoredCoverRootMesh.Describe(source);
            AuthoredCoverRootMesh.Description[] descriptions = Enumerable.Range(0, 6)
                .Select(i => before with { Node = $"fern_v{i / 2}_lod{i % 2}" }).ToArray();
            GroundCoverRootMetadata metadata = DiagnosticRoots(descriptions, new[] { Vector3D.Zero, Vector3D.Zero, Vector3D.Zero });
            ArrayMesh augmented = AuthoredCoverRootMesh.Create(source, metadata.Get(source.Name.ToString()));
            try
            {
                using var output = augmented.SurfaceGetArrays(0);
                float[] custom = output[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
                Expect("authored root stream keeps one logical anchor for every vertex", custom.Length == 12
                    && custom[0] == 0 && custom[1] == 0 && custom[2] == 0
                    && custom[4] == 0 && custom[5] == 0 && custom[6] == 0
                    && custom[8] == 0 && custom[9] == 0 && custom[10] == 0,
                    "whole-plant XYZ is constant; no disconnected card is independently lowered");
                Expect("authored root growth has explicit independent semantics", custom[3] == 0 && custom[7] == 0 && custom[11] == 1,
                    "weight is the reviewed axial height clamped to the reviewed metre range; no source wind inference");
                Expect("authored augmentation preserves source identity and material ownership", AuthoredCoverRootMesh.Describe(source) == before
                    && augmented.SurfaceGetMaterial(0) == material && augmented.SurfaceGetName(0) == "preserve-name",
                    "Create also checks exact position/normal/tangent/color/UV/UV2/index digest after native upload");
            }
            finally { augmented.Dispose(); }
            Expect("authored augmented mesh lifetime is independent of shared source", GodotObject.IsInstanceValid(sourceMesh)
                && GodotObject.IsInstanceValid(material) && AuthoredCoverRootMesh.Describe(source) == before,
                "disposing the owned augmentation does not dispose shared imported art");
            bool mismatch = false;
            source.Name = "fern_v0_lod1";
            try { using var refused = AuthoredCoverRootMesh.Create(source, metadata.Get("fern_v0_lod0")); }
            catch (ArgumentException) { mismatch = true; }
            Expect("authored root rejects another imported node identity", mismatch, "same arrays do not authorize a differently named node");
            source.Name = "fern_v0_lod0";
            source.Position = Vector3.Right;
            bool transformed = false;
            try { _ = AuthoredCoverRootMesh.Describe(source); } catch (ArgumentException) { transformed = true; }
            Expect("authored root rejects unqualified node transforms", transformed, "root coordinates are never guessed across node spaces");
        }
        finally { source.Free(); }
    }

    private void CheckAuthoredFernRootImports()
    {
        byte[] catalog = Godot.FileAccess.GetFileAsBytes("res://assets/surface/ground-cover/catalog.json");
        byte[] model = Godot.FileAccess.GetFileAsBytes("res://assets/surface/ground-cover/fern.glb");
        if (GroundCoverRootMetadata.Sha256(catalog) != "11f87351b1bf2486dd645c12d46edcaf2c0457c2f66db987dbf0dfbba6599239"
            || GroundCoverRootMetadata.Sha256(model) != "0fd809d2af27e5acbf6f881c8194fed0be7bd116d06edafc3cbaa4db9d3d8404")
            throw new InvalidOperationException("Fern root qualification requires the exact visually reviewed catalog/model receipts.");
        PackedScene scene = GD.Load<PackedScene>("res://assets/surface/ground-cover/fern.glb");
        Node source = scene.Instantiate();
        try
        {
            var meshes = new Dictionary<string, MeshInstance3D>(StringComparer.Ordinal);
            void Collect(Node node)
            {
                if (node is MeshInstance3D mesh && !meshes.TryAdd(mesh.Name.ToString(), mesh))
                    throw new InvalidOperationException("Duplicate fern mesh name.");
                foreach (Node child in node.GetChildren()) Collect(child);
            }
            Collect(source);
            AuthoredCoverRootMesh.Description[] descriptions = Enumerable.Range(0, 6)
                .Select(i => AuthoredCoverRootMesh.Describe(meshes[$"fern_v{i / 2}_lod{i % 2}"])).ToArray();
            var roots = new[]
            {
                new Vector3D(-0.0047980843902062364, -0.0001881867605107998, 0.00106728074549256),
                new Vector3D(0.0010148137581017513, -0.0011384226127321426, 0.00016790628057927748),
                new Vector3D(0.0000032782553893717647, -0.0026720985172965003, -0.025081902181761644)
            };
            // In-memory diagnostic authority only. This does NOT write a deployable sidecar or claim
            // contact, lighting, growth tuning, support volume or billboard approval.
            GroundCoverRootMetadata metadata = DiagnosticRoots(descriptions, roots);
            var report = new List<object>();
            foreach (AuthoredCoverRootMesh.Description description in descriptions)
            {
                try
                {
                MeshInstance3D node = meshes[description.Node];
                string packedBefore = RootPackedVertexDigest(node.Mesh);
                using ArrayMesh augmented = AuthoredCoverRootMesh.Create(node, metadata.Get(description.Node));
                using var originalArrays = node.Mesh.SurfaceGetArrays(0);
                using var actual = augmented.SurfaceGetArrays(0);
                Vector3[] positions = originalArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                float[] custom = actual[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
                GroundCoverRoot root = metadata.Get(description.Node);
                bool matches = custom.Length == positions.Length * 4;
                for (int i = 0; i < positions.Length && matches; i++)
                    matches = custom[i * 4] == (float)root.Root.X && custom[i * 4 + 1] == (float)root.Root.Y
                        && custom[i * 4 + 2] == (float)root.Root.Z
                        && BitConverter.SingleToInt32Bits(custom[i * 4 + 3]) == BitConverter.SingleToInt32Bits(
                            root.GrowthWeight(new Vector3D(positions[i].X, positions[i].Y, positions[i].Z)));
                bool sourceUntouched = AuthoredCoverRootMesh.Describe(node) == description
                    && RootPackedVertexDigest(node.Mesh) == packedBefore;
                bool packedIdentical = RootPackedVertexDigest(augmented) == packedBefore;
                matches &= sourceUntouched && packedIdentical;
                Expect("fern imported root roundtrip " + description.Node, matches,
                    $"{description.VertexCount} vertices, {description.TriangleCount} triangles; exact preserved-stream SHA {description.MeshSha256}; "
                    + $"source untouched={sourceUntouched}, packed vertex bytes identical={packedIdentical}; every CUSTOM0 weight checked bitwise");
                report.Add(new { description.Node, description.VertexCount, description.TriangleCount, description.MeshSha256,
                    root = new[] { root.Root.X, root.Root.Y, root.Root.Z }, nativeRoundtrip = matches,
                    sourceUntouched, packedVertexSha256 = packedBefore, packedIdentical });
                }
                catch (Exception error)
                {
                    Expect("fern imported root roundtrip " + description.Node, false, error.ToString());
                    report.Add(new { description.Node, description.VertexCount, description.TriangleCount, description.MeshSha256,
                        nativeRoundtrip = false, error = error.ToString() });
                }
            }
            if (!string.IsNullOrWhiteSpace(OutputPath))
                File.WriteAllText(OutputPath + ".fern-imports.json", JsonSerializer.Serialize(new
                {
                    schema = "hollow.cover-root-import-qualification.v1", runtimeAdmitted = false,
                    status = "native array qualification only; contact and growth tuning outstanding",
                    fingerprintVersion = GroundCoverMeshFingerprint.Version,
                    catalogSha256 = GroundCoverRootMetadata.Sha256(catalog), modelSha256 = GroundCoverRootMetadata.Sha256(model), meshes = report
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { source.Free(); }
    }

    private static string RootPackedVertexDigest(Mesh mesh)
    {
        using var surface = RenderingServer.MeshGetSurface(mesh.GetRid(), 0);
        return GroundCoverRootMetadata.Sha256(surface["vertex_data"].AsByteArray());
    }

    private static GroundCoverRootMetadata DiagnosticRoots(AuthoredCoverRootMesh.Description[] descriptions, Vector3D[] roots)
    {
        // Synthetic catalog keeps these native checks independent of deployable admission files.
        byte[] model = { 1, 2, 3, 4 };
        string modelHash = GroundCoverRootMetadata.Sha256(model), gltf = new('a', 64), buffer = new('b', 64);
        var variants = Enumerable.Range(0, 3).Select(v => new
        {
            variant = v, levels = Enumerable.Range(0, 3).Select(l => new
            { node = $"fern_v{v}_lod{l}", triangles = descriptions[v * 2 + Math.Min(l, 1)].TriangleCount, billboard = l == 2 }).ToArray()
        }).ToArray();
        byte[] catalog = JsonSerializer.SerializeToUtf8Bytes(new
        {
            families = new[] { new { key = "fern", model = "res://assets/surface/ground-cover/fern.glb", outputSha256 = modelHash,
                outputBytes = model.Length, inputs = new[] { new { sha256 = gltf }, new { sha256 = buffer } }, variants } }
        });
        object[] entries = descriptions.Select((d, i) => (object)new
        {
            node = d.Node, meshSha256 = d.MeshSha256, sourceGeometrySha256 = new string('f', 64), proposalSha256 = new string('a', 64),
            vertexCount = d.VertexCount, triangleCount = d.TriangleCount,
            root = new[] { roots[i / 2].X, roots[i / 2].Y, roots[i / 2].Z }, growthAxis = new[] { 0, 1, 0 }, growthLength = 1
        }).ToArray();
        byte[] sidecar = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = GroundCoverRootMetadata.Schema, status = "reviewed", runtimeAdmitted = true,
            catalogSha256 = GroundCoverRootMetadata.Sha256(catalog), modelSha256 = modelHash, reviewSha256 = new string('9', 64),
            sourceGltfSha256 = gltf, sourceBufferSha256 = buffer, meshes = entries
        });
        return GroundCoverRootMetadata.Read(sidecar, GroundCoverRootMetadata.Sha256(sidecar), catalog, model);
    }
}
