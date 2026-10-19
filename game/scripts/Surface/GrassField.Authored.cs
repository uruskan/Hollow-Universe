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
    // The scanned plants (wild grass, cotton grass, tundra grass, fern: game/assets/surface/ground-cover) are accents among
    // the clumps: the habitat rule puts one in a placement cell now and then (ferns under trees, cotton grass where it is
    // wet, tundra grass in the cold), rooted on the drawn ground as the clumps are. Their middle level is heavy (300 to
    // 2,100 triangles), so they use it only to CoverMiddle and their billboards beyond, within a budget.
    private readonly record struct CoverPlant(Vector3D Position, int Family, int Variant, float Yaw, float Scale, float Tint, float Keep);
    private sealed record CoverMesh(Mesh Mesh, ShaderMaterial Material, int Triangles);
    private sealed class CoverBatch
    {
        public MultiMeshInstance3D Instance = null!;
        public CoverMesh Art = null!;
        public readonly List<float> Buffer = new();
    }
    private const int CoverFamilies = 4, CoverVariants = 3, CoverLods = 3;
    private const float CoverNear = 6, CoverMiddle = 14;
    private const int CoverBudget = 3000;
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

    // Every accent of the resident tiles within the reach, the nearest first and then a stable random rank over the
    // rest, within the budget; each at its distance's level. Rebuilt when the tiles change or the camera has moved 2 m.
    private void UpdateAuthoredCover(Vector3D camera, bool show)
    {
        if (_coverRoot == null) return;
        _coverRoot.Visible = show && (_diagnosticLayers & GroundCoverLayers.Authored) != 0;
        if (!show) return;
        // Positions are kept relative to where the camera was at the last rebuild, so floats hold them on any planet.
        if (!_coverDirty && Vector3D.Distance(camera, _coverCamera) < 2)
        {
            _coverRoot.Transform = _frame.Anchor(_coverCamera);
            return;
        }
        _coverDirty = false;
        _coverCamera = camera;
        _coverRoot.Transform = _frame.Anchor(_coverCamera);
        long start = Stopwatch.GetTimestamp();
        foreach (CoverBatch batch in _coverBatches) batch.Buffer.Clear();
        _coverCount = 0;
        int[] counts = new int[CoverLods];
        long triangles = 0;
        _coverCandidates.Clear();
        double near2 = CoverMiddle * CoverMiddle;
        foreach (GrassTile tile in _tiles.Values)
        {
            if (tile.Built == null) continue;
            foreach (CoverPlant plant in tile.Built.Accents)
            {
                double d2 = (camera - plant.Position).LengthSquared;
                if (d2 > _far * _far) continue;
                _coverCandidates.Add((plant, d2, d2 < near2 ? d2 / near2 : 1 + plant.Keep));
            }
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
                bz = (-right * sn + forward * cs) * plant.Scale, p = plant.Position - _coverCamera;
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
        if (!_coverReported && _coverCount > 0)
        {
            _coverReported = true;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"[cover] {_coverCount}/{_coverCandidates.Count} authored accents; LOD {counts[0]}/{counts[1]}/{counts[2]}; {draws} draws; {triangles} submitted triangles; batch {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.0} ms"));
        }
    }

    private void StopAuthoredCover()
    {
        _coverCandidates.Clear();
        _coverCandidates.TrimExcess();
        _coverCount = 0;
        foreach (CoverBatch? batch in _coverBatches)
        {
            if (batch == null) continue;
            batch.Buffer.Clear();
            batch.Buffer.TrimExcess();
        }
    }
}
