using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    private bool _appearanceDiagnosticWritten;

    // Opt-in evidence from the actual field/mesh being rendered, not a substitute CPU copy of the shader.
    // --grass-diagnostics <absolute-json-path> can accompany a normal hosted --shot or environment route.
    // This records only undeformed mesh and biome-derived inputs; screenshots establish final blade appearance.
    private void WriteAppearanceDiagnostic(FieldData field)
    {
        if (_appearanceDiagnosticWritten) return;
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--grass-diagnostics");
        if (at < 0) return;
        if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("--grass-diagnostics requires an output JSON path.");

        Mesh mesh = _spatialGrassMesh ?? _grids[0].Grass.Multimesh.Mesh;
        using var arrays = mesh.SurfaceGetArrays(0);
        Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Vector2[] growth = arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        float[] feet = arrays[(int)Mesh.ArrayType.Custom0].AsFloat32Array();
        int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int[] roots = Enumerable.Range(0, vertices.Length).Where(i => uv[i].Y == 0).ToArray();
        bool constantFeet = ValidGrassFeet(vertices, feet);
        bool valid = vertices.Length == 40 && growth.Length == vertices.Length && indices.Length == 90 && constantFeet
            && roots.Length == 10 && roots.All(i => Math.Abs(vertices[i].Y) < 1e-6f && growth[i].LengthSquared() < 1e-12f)
            && vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z))
            && indices.All(i => i >= 0 && i < vertices.Length);

        static object Range(float[] data, int channel)
        {
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            double sum = 0;
            for (int i = channel; i < data.Length; i += 4)
            {
                minimum = Math.Min(minimum, data[i]); maximum = Math.Max(maximum, data[i]); sum += data[i];
            }
            return new { minimum, maximum, mean = sum / (data.Length / 4) };
        }
        var faces = new List<object>();
        foreach (FaceLattice face in field.Faces)
        {
            long potential = 0, habitatPresent = 0, conservativeSurvivors = 0;
            int n = _latticeSize;
            for (int y = 0; y < n - 1; y++)
            for (int x = 0; x < n - 1; x++)
            {
                long gx = face.CornerX + x, gy = face.CornerY + y;
                if (gx < 0 || gy < 0 || gx >= _faceCells || gy >= _faceCells) continue;
                potential++;
                int p = (y * n + x) * 4;
                float maximum = Math.Max(Math.Max(face.Nodes[p + 3], face.Nodes[p + 7]),
                    Math.Max(face.Nodes[p + n * 4 + 3], face.Nodes[p + n * 4 + 7]));
                if (maximum > 0) habitatPresent++;
                if (SpatialGrassMayGrow(face.Face, gx, gy, BuiltWith.Density, maximum)) conservativeSurvivors++;
            }
            faces.Add(new { face.Face, face.CornerX, face.CornerY, latticeNodes = face.Nodes.Length / 4,
                grass = Range(face.Nodes, 3), moisture = Range(face.Cover, 0), woodlandLitter = Range(face.Cover, 2),
                potentialGrassCells = potential, habitatPresentCells = habitatPresent,
                conservativeGrassCells = conservativeSurvivors,
                conservativeGrassVertices = conservativeSurvivors * vertices.Length,
                authoredAccents = face.Plants.Count });
        }

        ShaderMaterial diagnosticMaterial = _spatialTemplate ?? _grids[0].GrassMaterial;
        string shader = diagnosticMaterial.Shader.Code;
        var report = new
        {
            kind = "actual-production-grass-inputs", valid,
            scope = "CPU mesh and uploaded lattice inputs only; final vertex deformation, silhouette, lighting and GPU cost require native captures.",
            shaderSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shader))),
            bladeUnderlayer = diagnosticMaterial.GetShaderParameter("blade_underlayer").AsBool(),
            spatial = CaptureSpatialDiagnostics(),
            distance = BuiltWith.Distance, density = BuiltWith.Density, cellMetres = _cellMetres,
            vertexCount = vertices.Length, triangleCount = indices.Length / 3, rootCount = roots.Length,
            meshHeight = vertices.Max(v => v.Y), rootMaximumError = roots.Max(i => Math.Abs(vertices[i].Y)),
            constantSideFeet = constantFeet, footMetadataBytes = feet.Length * sizeof(float),
            coverageRule = "One stable cell roll against patch-modulated habitat times quality. Five full-size cards per survivor; stable per-card distance rejection. Atlas alpha is the only silhouette.",
            shapeRule = "Independent stable cell/card shape hash; same configured height range, 0.08–0.32 resting lean per unit height, bounded growth curvature. Original cell RNG, root feet, width, topology and occupancy are unchanged.",
            shapeBoundsScope = "Current production defaults and wind <=1.15 fit the unchanged 8 m spatial deformation allowance. This is a conservative support/displacement bound, not a measured silhouette or GPU cost.",
            countScope = "Whole uploaded lattice including movement halo, before active window/distance/card rejection; conservative bound, not actual visible coverage or GPU work.",
            atlas = GrassAtlasDiagnostic(diagnosticMaterial),
            faces,
        };
        string output = System.IO.Path.GetFullPath(args[at + 1]);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        System.IO.File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        _appearanceDiagnosticWritten = true;
        GD.Print($"[grass-diagnostics] valid={valid}; {vertices.Length} vertices, {indices.Length / 3} triangles, {roots.Length} rooted endpoints; {output}");
        if (!valid) throw new InvalidOperationException("Production grass mesh/root diagnostic failed; see " + output);
    }

    private static bool ValidGrassFeet(Vector3[] vertices, float[] feet)
    {
        if (vertices.Length != 40 || feet.Length != vertices.Length * 4) return false;
        for (int card = 0; card < 5; card++)
        for (int side = 0; side < 2; side++)
        for (int row = 0; row < 4; row++)
        {
            int root = card * 8 + side, opposite = card * 8 + 1 - side, p = (root + row * 2) * 4;
            if (!float.IsFinite(feet[p]) || !float.IsFinite(feet[p + 1]) || !float.IsFinite(feet[p + 2]) || !float.IsFinite(feet[p + 3])
                || Math.Abs(feet[p] - vertices[root].X) > 1e-6f || Math.Abs(feet[p + 1] - vertices[root].Z) > 1e-6f
                || Math.Abs(feet[p + 2] - vertices[opposite].X) > 1e-6f || Math.Abs(feet[p + 3] - vertices[opposite].Z) > 1e-6f)
                return false;
        }
        return true;
    }

    internal static bool InspectGrassFootMetadata()
    {
        using ArrayMesh mesh = TuftMesh(false);
        using var arrays = mesh.SurfaceGetArrays(0);
        return ValidGrassFeet(arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(),
            arrays[(int)Mesh.ArrayType.Custom0].AsFloat32Array())
            && arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length == 90;
    }

    // The bounded render fixture owns the returned production mesh and must dispose it after its viewport.
    internal static ArrayMesh CreateGrassRenderFixtureMesh() => TuftMesh(false);

    private static object GrassAtlasDiagnostic(ShaderMaterial material)
    {
        // One opt-in native readback. Sample the actual material texture without retaining another byte copy.
        if (material.GetShaderParameter("atlas").AsGodotObject() is not Texture2D texture)
            return new { available = false };
        using Image image = texture.GetImage();
        if (image == null || image.IsEmpty()) return new { available = false };
        var cells = new List<object>();
        foreach (string name in new[] { "cell_grass", "cell_dry", "cell_meadow" })
        {
            Vector4 cell = material.GetShaderParameter(name).AsVector4();
            int covered = 0;
            int[] rows = new int[5];
            const int samples = 128;
            for (int y = 0; y < samples; y++)
            for (int x = 0; x < samples; x++)
                if (Alpha((x + 0.5f) / samples, (y + 0.5f) / samples) >= 0.5f) covered++;
            for (int row = 0; row < rows.Length; row++)
            for (int x = 0; x < samples; x++)
                if (Alpha((x + 0.5f) / samples, row * 0.2f) >= 0.5f) rows[row]++;
            cells.Add(new { name, samplesPerAxis = samples, alphaCoverage = covered / (double)(samples * samples),
                rootToTipRowCoverage = rows.Select(value => value / (double)samples).ToArray() });

            float Alpha(float u, float v)
            {
                int x = Math.Clamp((int)((cell.X + (cell.Z - cell.X) * u) * image.GetWidth()), 0, image.GetWidth() - 1);
                int y = Math.Clamp((int)((cell.Y + (cell.W - cell.Y) * v) * image.GetHeight()), 0, image.GetHeight() - 1);
                return image.GetPixel(x, y).A;
            }
        }
        return new { available = true, scope = "Actual base-level atlas alpha point samples; excludes projected area, mip selection and occlusion.", cells };
    }
}
