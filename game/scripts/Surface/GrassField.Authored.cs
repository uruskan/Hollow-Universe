using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    // All families reuse the existing fine ground lattice and its water/pad exclusions. Rendering has no gameplay IDs.
    private readonly record struct CoverPlant(Vector3D Position, int Family, int Variant, float Yaw, float Scale, float Tint, float Keep);
    private sealed record CoverMesh(Mesh Mesh, ShaderMaterial Material, int Triangles);
    private sealed class CoverBatch
    {
        public MultiMeshInstance3D Instance = null!;
        public CoverMesh Art = null!;
        public readonly List<float> Buffer = new();
    }
    private const int CoverFamilies = 4, CoverVariants = 3, CoverLods = 3;
    private const float CoverNear = 8, CoverMiddle = 22;
    private const int CoverBudget = 18000;
    private readonly CoverBatch[] _coverBatches = new CoverBatch[CoverFamilies * CoverVariants * CoverLods];
    private Node3D _coverRoot = null!;
    private bool _coverDirty = true;
    private Vector3D _coverCamera = new(double.MaxValue, 0, 0);
    private int _coverCount;
    private bool _coverReported;
    private readonly List<(CoverPlant Plant, double DistanceSquared, double Priority)> _coverCandidates = new();

    private void SetupAuthoredCover()
    {
        _coverRoot = new Node3D { Name = "AuthoredGroundCover" };
        AddChild(_coverRoot);
        using JsonDocument catalog = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/surface/ground-cover/catalog.json"));
        int family = 0;
        Shader shader = GD.Load<Shader>("res://shaders/surface_authored_cover.gdshader");
        foreach (JsonElement group in catalog.RootElement.GetProperty("families").EnumerateArray())
        {
            Node3D source = GD.Load<PackedScene>(group.GetProperty("model").GetString()!).Instantiate<Node3D>();
            var meshes = new Dictionary<string, MeshInstance3D>(StringComparer.Ordinal);
            void Collect(Node node)
            {
                if (node is MeshInstance3D mesh) meshes[mesh.Name] = mesh;
                foreach (Node child in node.GetChildren()) Collect(child);
            }
            Collect(source);
            foreach (JsonElement variant in group.GetProperty("variants").EnumerateArray())
            {
                int v = variant.GetProperty("variant").GetInt32(), lod = 0;
                foreach (JsonElement level in variant.GetProperty("levels").EnumerateArray())
                {
                    Mesh mesh = meshes[level.GetProperty("node").GetString()!].Mesh;
                    if (mesh.SurfaceGetMaterial(0) is not BaseMaterial3D native)
                        throw new InvalidOperationException("Authored ground cover needs the supplied glTF PBR material.");
                    var material = new ShaderMaterial { Shader = shader };
                    material.SetShaderParameter("albedo_texture", native.AlbedoTexture);
                    material.SetShaderParameter("normal_texture", native.NormalTexture);
                    Texture2D? roughness = native.RoughnessTexture ?? native.OrmTexture;
                    material.SetShaderParameter("roughness_texture", roughness ?? native.AlbedoTexture);
                    material.SetShaderParameter("has_normal", native.NormalTexture != null);
                    material.SetShaderParameter("has_roughness", roughness != null);
                    material.SetShaderParameter("billboard", level.GetProperty("billboard").GetBoolean());
                    material.SetShaderParameter("plant_height", Math.Max(0.05f, level.GetProperty("height").GetSingle()));
                    material.SetShaderParameter("far_radius", _far);
                    var art = new CoverMesh(mesh, material, level.GetProperty("triangles").GetInt32());
                    var instance = new MultiMeshInstance3D
                    {
                        Name = $"{group.GetProperty("key").GetString()}_{v}_{lod}",
                        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                            UseCustomData = true, Mesh = mesh },
                        MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                        GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                    };
                    _coverRoot.AddChild(instance);
                    _coverBatches[(family * CoverVariants + v) * CoverLods + lod++] = new CoverBatch { Instance = instance, Art = art };
                }
            }
            source.Free();
            family++;
        }
        if (family != CoverFamilies || _coverBatches.Any(b => b == null)) throw new InvalidOperationException("Incomplete ground-cover LOD catalog.");
    }

    // A smooth 12–20 m species/density patch, stable on the same cube-face grid as the ground cover.
    private static double CoverPatch(int face, double x, double y)
    {
        long ix = (long)Math.Floor(x), iy = (long)Math.Floor(y);
        double u = DMath.SmoothStep(0, 1, x - ix), v = DMath.SmoothStep(0, 1, y - iy);
        double H(long a, long b) => new Rng(Hash64.Of(0x434F5652UL, (ulong)face, unchecked((ulong)a), unchecked((ulong)b))).NextDouble();
        double low = H(ix, iy) * (1 - u) + H(ix + 1, iy) * u;
        double high = H(ix, iy + 1) * (1 - u) + H(ix + 1, iy + 1) * u;
        return low * (1 - v) + high * v;
    }

    private void PopulateAuthoredCover(FaceLattice lattice, Vector3D[] points, bool[] bare)
    {
        int n = _latticeSize;
        for (int y = 0; y < n - 1; y++)
        for (int x = 0; x < n - 1; x++)
        {
            int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
            if (bare[a] || bare[b] || bare[c] || bare[d]) continue;
            long gx = lattice.CornerX + x, gy = lattice.CornerY + y;
            var r = new Rng(Hash64.Of(0x4752415353UL, (ulong)lattice.Face, unchecked((ulong)gx), unchecked((ulong)gy)));
            double u = r.Range(0.12, 0.88), v = r.Range(0.12, 0.88);
            Vector3D at = (points[a] * (1 - u) + points[b] * u) * (1 - v) + (points[c] * (1 - u) + points[d] * u) * v;
            if (CubeSphere.FromDirection(at.Normalized).Face != lattice.Face) continue;
            float Value(float[] values, int channel) =>
                (float)((values[a * 4 + channel] * (1 - u) + values[b * 4 + channel] * u) * (1 - v)
                    + (values[c * 4 + channel] * (1 - u) + values[d * 4 + channel] * u) * v);
            double grass = Value(lattice.Nodes, 3), moisture = Value(lattice.Cover, 0);
            double forest = Value(lattice.Cover, 2), plants = Value(lattice.Cover, 3);
            double patch = CoverPatch(lattice.Face, (gx + u) / 32, (gy + v) / 32);
            // The shorter blade layer keeps the meadow closed; authored crowns give it species and height diversity.
            double density = Math.Clamp(grass * (0.18 + patch * 0.36) + plants * 0.08, 0, 0.6) * BuiltWith.Density;
            if (!r.Chance(density)) continue;
            int family = forest > 0.22 && r.Chance(plants * 0.22) ? 3
                : moisture > 0.48 && patch > 0.55 && r.Chance(0.13) ? 1
                : patch < 0.38 || (moisture < 0.4 && r.Chance(0.55)) ? 2 : 0;
            // Fern crowns cover over a metre: sparse groups belong under trees, not one in every grass cell.
            if (family == 3 && !r.Chance(0.28)) continue;
            float scale = (float)r.Range(0.8, 1.25);
            if (family == 0) scale *= 1.25f;
            if (family == 2) scale *= 0.75f;
            lattice.Plants.Add(new CoverPlant(at - at.Normalized * 0.025, family, r.NextInt(CoverVariants),
                (float)r.Range(0, DMath.TwoPi), scale, (float)(0.84 + 0.18 * patch + r.Range(-0.06, 0.06)), (float)r.NextDouble()));
        }
    }

    private void UpdateAuthoredCover(Vector3D camera, bool show)
    {
        _coverRoot.Visible = show;
        if (!show || _field == null) return;
        _coverRoot.Transform = _frame.Anchor(_field.Anchor);
        if (!_coverDirty && Vector3D.Distance(camera, _coverCamera) < 2) return;
        _coverDirty = false; _coverCamera = camera;
        long start = Stopwatch.GetTimestamp();
        foreach (CoverBatch batch in _coverBatches) batch.Buffer.Clear();
        _coverCount = 0;
        int[] counts = new int[CoverLods];
        long triangles = 0;
        // Keep nearby crowns first, then use a stable random rank over the actual visible candidates. A nominal
        // cube-cell area estimate undercounted dense face regions and the ordered hard stop left spatial holes.
        _coverCandidates.Clear();
        foreach (FaceLattice face in _field.Faces)
        foreach (CoverPlant plant in face.Plants)
        {
            double d2 = (camera - plant.Position).LengthSquared;
            if (d2 > _far * _far) continue;
            double near2 = CoverMiddle * CoverMiddle;
            _coverCandidates.Add((plant, d2, d2 < near2 ? d2 / near2 : 1 + plant.Keep));
        }
        if (_coverCandidates.Count > CoverBudget)
            _coverCandidates.Sort(static (a, b) =>
            {
                int priority = a.Priority.CompareTo(b.Priority);
                return priority != 0 ? priority : a.DistanceSquared.CompareTo(b.DistanceSquared);
            });
        int selectedCount = Math.Min(CoverBudget, _coverCandidates.Count);
        for (int i = 0; i < selectedCount; i++)
        {
            var candidate = _coverCandidates[i];
            CoverPlant plant = candidate.Plant;
            double distance = Math.Sqrt(candidate.DistanceSquared);
            int lod = distance < CoverNear ? 0 : distance < CoverMiddle ? 1 : 2;
            CoverBatch batch = _coverBatches[(plant.Family * CoverVariants + plant.Variant) * CoverLods + lod];
            Vector3D up = plant.Position.Normalized, right = Vector3D.Cross(Vector3D.UnitY, up);
            right = right.LengthSquared > 1e-12 ? right.Normalized : Vector3D.UnitX;
            Vector3D forward = Vector3D.Cross(right, up);
            double cs = Math.Cos(plant.Yaw), sn = Math.Sin(plant.Yaw);
            Vector3D bx = (right * cs + forward * sn) * plant.Scale, by = up * plant.Scale,
                bz = (-right * sn + forward * cs) * plant.Scale, p = plant.Position - _field.Anchor;
            List<float> buffer = batch.Buffer;
            buffer.Add((float)bx.X); buffer.Add((float)by.X); buffer.Add((float)bz.X); buffer.Add((float)p.X);
            buffer.Add((float)bx.Y); buffer.Add((float)by.Y); buffer.Add((float)bz.Y); buffer.Add((float)p.Y);
            buffer.Add((float)bx.Z); buffer.Add((float)by.Z); buffer.Add((float)bz.Z); buffer.Add((float)p.Z);
            buffer.Add(plant.Tint); buffer.Add(plant.Tint * 0.99f); buffer.Add(plant.Tint * 0.95f); buffer.Add(plant.Yaw);
            counts[lod]++; _coverCount++; triangles += batch.Art.Triangles;
        }
        int draws = 0;
        foreach (CoverBatch batch in _coverBatches)
        {
            int count = batch.Buffer.Count / 16;
            batch.Instance.Visible = count > 0;
            batch.Instance.Multimesh.InstanceCount = count;
            if (count == 0) continue;
            batch.Instance.Multimesh.Buffer = batch.Buffer.ToArray();
            draws++;
        }
        if (!_coverReported)
        {
            _coverReported = true;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"[cover] {_coverCount}/{_coverCandidates.Count} authored plants; LOD {counts[0]}/{counts[1]}/{counts[2]}; {draws} draws; {triangles} submitted triangles; batch {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.0} ms"));
        }
    }
}
