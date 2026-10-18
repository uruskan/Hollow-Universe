using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    internal enum SpatialFixtureFault { None, BeforeFaceUpload, AfterGrassBatch, BeforePublish, DuringRetirement }
    private SpatialFixtureFault _spatialFixtureFault;

    private void ThrowSpatialFixtureFault(SpatialFixtureFault point)
    {
        if (_spatialFixtureFault != point) return;
        _spatialFixtureFault = SpatialFixtureFault.None;
        throw new InvalidOperationException("Expected spatial grass fixture failure at " + point);
    }

    internal void InjectSpatialLifecycleFault(SpatialFixtureFault point) => _spatialFixtureFault = point;

    /// <summary>Tiny art-free resources, but real production worker, compaction, native upload and retirement paths.</summary>
    internal void SetupSpatialLifecycleFixture(ISurface surface, RenderFrame frame, int cells = 32)
    {
        BeginSetup();
        _surface = surface; _frame = frame;
        _level = TileKey.LevelFor(surface.Radius, CellMetres);
        _faceCells = 1L << _level;
        _cellMetres = TileKey.EdgeLength(_level, surface.Radius);
        _heightShift = TileKey.LevelFor(surface.Radius, TerrainLod.FinestTileMetres) + TileCellsLog2;
        BuiltWith = (1, 0.6f);
        _far = 8; _shrink = 6; _litterFar = 6; _litterShrink = 4;
        _gridWidth = 37; _litterWidth = 29; _latticeSize = 49;
        ConfigureSpatialDiagnostics(new[] { "--spatial-grass-cells", cells.ToString() });
        _buildContext = CaptureBuildContext(surface, frame);
        var shader = new Shader
        {
            Code = "shader_type spatial; uniform sampler2D lattice; uniform sampler2D lattice_cover; " +
                "uniform int face; uniform ivec2 lattice_cell; uniform vec2 face_origin; uniform ivec2 grid_offset; " +
                "uniform vec3 anchor; uniform vec4 trample; uniform int layer; uniform int grid_width; " +
                "uniform float far_radius; uniform float shrink_start; uniform bool compact_cells; uniform ivec2 tile_cell; " +
                "void fragment() { ALBEDO = vec3(0.2, 0.4, 0.1); }",
        };
        var material = new ShaderMaterial { Shader = shader };
        Mesh tuft = TuftMesh(false), litter = LitterMesh(false);
        SetupSpatialResources(material, tuft, litter);
        _coverRoot = new Node3D { Name = "AuthoredGroundCover" };
        AddChild(_coverRoot);
        var art = new CoverMesh(tuft, material, 30);
        for (int i = 0; i < _coverBatches.Length; i++)
        {
            var instance = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true, Mesh = tuft },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            _coverRoot.AddChild(instance);
            _coverBatches[i] = new CoverBatch { Instance = instance, Art = art };
        }
        _coverReported = true;
        _setupComplete = true;
    }

    private FieldData SpatialFixtureData(bool revised, bool seam)
    {
        int n = _latticeSize;
        var field = new FieldData { Anchor = new Vector3D(_surface.Radius, 0, 0) };
        var face = new FaceLattice
        {
            Face = CubeSphere.FromDirection(field.Anchor).Face,
            CornerX = seam ? -7 : _faceCells / 2 - n / 2,
            CornerY = seam ? _faceCells - 27 : _faceCells / 2 - n / 2,
            Nodes = new float[n * n * 4], Cover = new float[n * n * 4],
        };
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int at = (y * n + x) * 4;
            float u = (float)((x - n / 2) * _cellMetres), v = (float)((y - n / 2) * _cellMetres);
            face.Nodes[at] = u * 0.45f + v * 0.2f + (revised ? 0.125f : 0);
            face.Nodes[at + 1] = u; face.Nodes[at + 2] = v;
            face.Nodes[at + 3] = x < 9 ? 0 : 0.7f;
            face.Cover[at] = 0.6f;
            face.Cover[at + 2] = y < 11 ? 0 : 0.4f;
        }
        field.Faces.Add(face);
        return field;
    }

    internal void QueueSpatialLifecycleGeneration(bool revised = false)
    {
        FieldData field = SpatialFixtureData(revised, false);
        BuildSpatialTiles(_buildContext!, field, CancellationToken.None);
        _centre = field.Anchor;
        _near = true;
        BeginSpatialUpload(field);
    }

    internal void AdvanceSpatialLifecycleUpload()
    {
        ContinueSpatialUpload();
        _ready = _field != null;
        if (_field != null)
        {
            Vector3D camera = _field.Anchor + new Vector3D(1.7, 0, 0);
            UpdateSpatialVisibility(camera, _frame.ToRender(camera), true, true);
        }
    }

    internal (bool Stable, bool Owned, bool Compact, int Instances) InspectSpatialLifecycleCells()
    {
        var keys = new List<HashSet<(int Face, long X, long Y, int Layer)>>();
        bool owned = true;
        int allCandidates = 0;
        foreach (int cells in new[] { 16, 32 })
        {
            FieldData field = SpatialFixtureData(false, true);
            BuildContext context = _buildContext! with { SpatialCells = cells };
            BuildSpatialTiles(context, field, CancellationToken.None);
            var found = new HashSet<(int, long, long, int)>();
            foreach (SpatialTileData tile in field.SpatialTiles)
            {
                FaceLattice face = field.Faces[tile.FaceIndex];
                Check(tile.Grass, 0); Check(tile.Litter, 1);
                void Check(float[] buffer, int layer)
                {
                    for (int p = 0; p < buffer.Length; p += 16)
                    {
                        float lx = buffer[p + 12], ly = buffer[p + 13];
                        long gx = face.CornerX + tile.CellX + (int)lx, gy = face.CornerY + tile.CellY + (int)ly;
                        owned &= lx == (int)lx && ly == (int)ly && lx >= 0 && ly >= 0 && lx < cells && ly < cells
                            && gx >= 0 && gy >= 0 && gx < _faceCells && gy < _faceCells
                            && found.Add((face.Face, gx, gy, layer));
                    }
                }
            }
            keys.Add(found);
            allCandidates = (_latticeSize - 1) * (_latticeSize - 1) * 2;
        }
        return (keys[0].SetEquals(keys[1]), owned, keys[0].Count > 0 && keys[0].Count < allCandidates, keys[0].Count);
    }

    internal (bool Conservative, bool Nested, bool Endpoints, int Retained, int Candidates, long Probes) InspectGrassCoverageAdmission()
    {
        // Exercise the actual tile builder/buffers. The oracle evaluates bilinear habitat and the complete patch
        // equation in double precision over adversarial interpolants; production pruning uses only a corner bound.
        // This proves the CPU bound at these samples, not GPU hash/float parity or final visible coverage.
        bool conservative = true, nested = true, endpoints = true;
        int retained = 0, candidates = 0;
        long probes = 0;
        const int n = 9;
        float[][] habitats = { new[] { 0f, 0f, 0f, 0f }, new[] { 0.019f, 0.02f, 0.021f, 0.019f },
            new[] { 0.04f, 0.08f, 0.12f, 0.16f }, new[] { 0f, 0f, 0f, 1f },
            new[] { 0.4f, 0.55f, 0.7f, 0.9f }, new[] { 1f, 1f, 1f, 1f } };
        double[] jitter = { 0, 0.125, 0.5, 0.875, 1 };
        double[] noise = { 0, 0.3, 0.5, 0.7, 1 };
        foreach (float[] habitat in habitats)
        {
            HashSet<(long X, long Y)>? previous = null;
            foreach (float quality in new[] { 0.1f, 0.6f, 1f })
            {
                var field = new FieldData();
                var face = new FaceLattice { Face = 2, CornerX = _faceCells / 2 - 4, CornerY = _faceCells / 2 - 4,
                    Nodes = new float[n * n * 4], Cover = new float[n * n * 4] };
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int p = (y * n + x) * 4;
                    face.Nodes[p] = x; face.Nodes[p + 2] = y;
                    face.Nodes[p + 3] = habitat[(x & 1) + 2 * (y & 1)];
                }
                field.Faces.Add(face);
                BuildSpatialTiles(_buildContext! with { LatticeSize = n, Density = quality }, field, CancellationToken.None);
                var actual = new HashSet<(long X, long Y)>();
                foreach (SpatialTileData tile in field.SpatialTiles)
                for (int p = 0; p < tile.Grass.Length; p += 16)
                    actual.Add((face.CornerX + tile.CellX + (int)tile.Grass[p + 12],
                        face.CornerY + tile.CellY + (int)tile.Grass[p + 13]));
                if (previous != null) nested &= previous.IsSubsetOf(actual);
                previous = actual;
                retained += actual.Count; candidates += (n - 1) * (n - 1);
                if (habitat.All(value => value == 0)) endpoints &= actual.Count == 0;
                if (quality == 1 && habitat.All(value => value == 1)) endpoints &= actual.Count == (n - 1) * (n - 1);
                for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    long gx = face.CornerX + x, gy = face.CornerY + y;
                    double roll = (SpatialCoverageState(face.Face, gx, gy, 0) >> 8) / 16777216.0;
                    int p = (y * n + x) * 4;
                    foreach (double u in jitter)
                    foreach (double v in jitter)
                    foreach (double patch in noise)
                    {
                        double row0 = face.Nodes[p + 3] * (1 - u) + face.Nodes[p + 7] * u;
                        double row1 = face.Nodes[p + n * 4 + 3] * (1 - u) + face.Nodes[p + n * 4 + 7] * u;
                        double density = row0 * (1 - v) + row1 * v;
                        double blend = Smooth(0.55, 0.9, density);
                        double modulation = (0.25 + 1.5 * Smooth(0.3, 0.7, patch)) * (1 - blend) + blend;
                        double probability = density < 0.02 ? 0 : Math.Clamp(density * modulation, 0, 1) * quality;
                        if (roll < probability) conservative &= actual.Contains((gx, gy));
                        probes++;
                    }
                }
            }
        }
        return (conservative, nested, endpoints, retained, candidates, probes);

        static double Smooth(double low, double high, double value)
        {
            double t = Math.Clamp((value - low) / (high - low), 0, 1);
            return t * t * (3 - 2 * t);
        }
    }

    internal (bool Finite, bool Enclosed, bool SmallMeshes, bool TexturesBound, int Batches) InspectSpatialLifecycleBounds()
    {
        bool finite = true, enclosed = true, small = true, textures = true;
        if (_spatialResident == null) return (false, false, false, false, 0);
        foreach (SpatialBatch batch in _spatialResident.Batches)
        {
            Aabb bounds = batch.MultiMesh.CustomAabb;
            Vector3 low = bounds.Position + batch.Centre, high = bounds.End + batch.Centre;
            Vector3 expectedLow = V(batch.Data.Minimum) - Vector3.One * SpatialDeformationEnvelope;
            Vector3 expectedHigh = V(batch.Data.Maximum) + Vector3.One * SpatialDeformationEnvelope;
            foreach (float component in new[] { low.X, low.Y, low.Z, high.X, high.Y, high.Z }) finite &= float.IsFinite(component);
            enclosed &= low.X <= expectedLow.X + 0.0001f && low.Y <= expectedLow.Y + 0.0001f && low.Z <= expectedLow.Z + 0.0001f
                && high.X >= expectedHigh.X - 0.0001f && high.Y >= expectedHigh.Y - 0.0001f && high.Z >= expectedHigh.Z - 0.0001f;
            small &= batch.MultiMesh.Mesh.GetAabb().Size.Length() < 10 && bounds.Size.Length() < 80;
            SpatialFaceResources face = _spatialResident.Faces[batch.Data.FaceIndex];
            textures &= batch.Material.GetShaderParameter("lattice").AsGodotObject()?.GetInstanceId() == face.Nodes.GetInstanceId()
                && batch.Material.GetShaderParameter("lattice_cover").AsGodotObject()?.GetInstanceId() == face.Cover.GetInstanceId();
        }
        return (finite, enclosed, small, textures, _spatialResident.Batches.Count);
    }

    internal GodotObject[] SpatialLifecycleNativeResources(bool staging = false)
    {
        SpatialGeneration? generation = staging ? _spatialStaging : _spatialResident;
        if (generation == null) return Array.Empty<GodotObject>();
        return generation.Faces.SelectMany(face => new GodotObject[] { face.Nodes, face.Cover })
            .Concat(generation.Batches.SelectMany(batch => new GodotObject[] { batch.Instance, batch.MultiMesh, batch.Material })).ToArray();
    }

    internal int SpatialLifecycleDirectBatches => GetChildren().OfType<MultiMeshInstance3D>().Count();
}
