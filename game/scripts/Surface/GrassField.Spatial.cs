using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    // Comparison-only until same-pose native captures approve the migration. A 32-cell tile is ~14.73 m on Earth;
    // 16 cells (~7.36 m) is exposed for measuring culling benefit against its higher native draw/node count.
    private int _spatialCells;
    private Mesh? _spatialGrassMesh, _spatialLitterMesh;
    private ShaderMaterial? _spatialTemplate;
    private SpatialGeneration? _spatialResident, _spatialStaging;
    private long _spatialSerial, _spatialUploadedBytes, _spatialWorstGenerationBytes, _spatialScratchBytes;
    private string? _spatialRetirementError;
    private double _spatialLastUploadMilliseconds;
    private Vector3D _spatialLastCamera;
    private bool _spatialHasCamera, _spatialWalking;
    private const long SpatialMemoryLimit = 256L * 1024 * 1024;
    private const long SpatialUploadByteTarget = 512 * 1024;
    private const int SpatialUploadTileLimit = 4;
    private const double SpatialUploadTimeTargetMilliseconds = 0.5;
    // Includes root slope compensation (denominator >= .15), 1.2 m source card, width/height variation,
    // lean, wind (SurfaceView publishes <=1.15), trample and litter curl. Sum of their conservative displacement
    // lengths is <6.3 m with the current shader defaults; 8 m also covers float/normalization roundoff.
    // Changing those ranges requires updating this envelope and the native bounds checks together.
    private const float SpatialDeformationEnvelope = 8;

    private sealed class SpatialFaceResources
    {
        public ImageTexture Nodes = null!, Cover = null!;
        public void Dispose()
        {
            List<Exception>? failures = null;
            try { Nodes?.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
            finally { Nodes = null!; }
            try { Cover?.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
            finally { Cover = null!; }
            if (failures != null) throw new AggregateException("Spatial grass textures failed to retire.", failures);
        }
    }

    private sealed class SpatialBatch
    {
        public MultiMeshInstance3D Instance = null!;
        public ShaderMaterial Material = null!;
        public MultiMesh MultiMesh = null!;
        public SpatialTileData Data = null!;
        public int Layer;
        public Vector3 Centre;
        public long ContactVersion = -1;
        public Aabb ContactBounds;
        public bool ContactBoundsValid;
    }

    private sealed class SpatialGeneration
    {
        public readonly FieldData Field;
        public readonly long Serial;
        public readonly int Epoch;
        public readonly List<SpatialFaceResources> Faces = new();
        public readonly List<SpatialBatch> Batches = new();
        public int NextTile;
        public SpatialGeneration(FieldData field, long serial, int epoch) { Field = field; Serial = serial; Epoch = epoch; }
    }

    private void ConfigureSpatialDiagnostics(string[] args)
    {
        int option = Array.IndexOf(args, "--spatial-grass-cells");
        _spatialCells = Array.IndexOf(args, "--spatial-grass") >= 0 || option >= 0 ? 32 : 0;
        if (option >= 0 && (option + 1 >= args.Length || !int.TryParse(args[option + 1], out _spatialCells) || _spatialCells is not (16 or 32)))
            throw new ArgumentException("--spatial-grass-cells requires 16 or 32.");
        if (_spatialCells == 0) return;
        // At most one resident plus one building/staged generation. Full-density admission never assumes a sparse
        // meadow: two layers of 16-float instances plus two RGBA32F lattices, with copy scratch and list capacity.
        // This bounds the new spatial allocations; it is not a claim about total process/native GPU memory.
        _spatialWorstGenerationBytes = checked((long)_grids.Length * _latticeSize * _latticeSize * (2 * 16 * 4 + 2 * 4 * 4));
        _spatialScratchBytes = checked((long)_latticeSize * _latticeSize * 4 * sizeof(float) * 3 + 1024 * 1024);
        if (_spatialWorstGenerationBytes * 2 + _spatialScratchBytes > SpatialMemoryLimit)
            throw new InvalidOperationException("The requested spatial grass window exceeds its 256 MiB two-generation allocation limit.");
    }

    private void SetupSpatialResources(ShaderMaterial template, Mesh grass, Mesh litter)
    {
        _spatialTemplate = template;
        _spatialGrassMesh = grass;
        _spatialLitterMesh = litter;
        template.SetShaderParameter("compact_cells", true);
    }

    private void BeginSpatialUpload(FieldData field)
    {
        if (_spatialStaging != null) throw new InvalidOperationException("Only one grass generation may await upload.");
        _spatialStaging = new(field, ++_spatialSerial, _epoch);
    }

    private void ContinueSpatialUpload()
    {
        SpatialGeneration? generation = _spatialStaging;
        if (generation == null) return;
        if (_stopped || generation.Epoch != _epoch)
        {
            _spatialStaging = null;
            _workers.Monitor.Discarded();
            try { ReleaseSpatialGeneration(generation); }
            catch (Exception error)
            {
                _spatialRetirementError = error.ToString();
                GD.PushError($"Stale spatial grass generation retirement failed: {error}");
            }
            return;
        }
        long start = Stopwatch.GetTimestamp(), bytes = 0;
        int tiles = 0;
        try
        {
            // Face textures are immutable for this generation. One texture pair is indivisible and may exceed
            // the soft byte/time target; then this frame performs no further uploads. Old batches keep old textures.
            if (generation.Faces.Count < generation.Field.Faces.Count)
            {
                ThrowSpatialFixtureFault(SpatialFixtureFault.BeforeFaceUpload);
                FaceLattice lattice = generation.Field.Faces[generation.Faces.Count];
                generation.Faces.Add(CreateSpatialFace(lattice));
                bytes += ((long)lattice.Nodes.Length + lattice.Cover.Length) * sizeof(float);
            }
            else
            {
                while (generation.NextTile < generation.Field.SpatialTiles.Count && tiles < SpatialUploadTileLimit)
                {
                    SpatialTileData tile = generation.Field.SpatialTiles[generation.NextTile];
                    if (tiles > 0 && (bytes + tile.BufferBytes > SpatialUploadByteTarget
                        || Stopwatch.GetElapsedTime(start).TotalMilliseconds >= SpatialUploadTimeTargetMilliseconds)) break;
                    CreateSpatialTile(generation, tile);
                    generation.NextTile++;
                    bytes += tile.BufferBytes;
                    tiles++;
                }
            }
            _spatialUploadedBytes += bytes;
            _spatialLastUploadMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (generation.Faces.Count != generation.Field.Faces.Count || generation.NextTile != generation.Field.SpatialTiles.Count) return;
            ThrowSpatialFixtureFault(SpatialFixtureFault.BeforePublish);
            WriteAppearanceDiagnostic(generation.Field);
        }
        catch (Exception error)
        {
            _requiredJobFailed = true;
            _spatialStaging = null;
            try { ReleaseSpatialGeneration(generation); }
            catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            GD.PushError($"Spatial grass upload failed: {error}");
            return;
        }
        // Publication is the commit point. Failure while retiring the previous generation must never roll back
        // by disposing this live generation. Each retired resource is attempted before the failure is reported.
        SpatialGeneration? previous = _spatialResident;
        _spatialResident = generation;
        _spatialStaging = null;
        _field = generation.Field;
        _coverDirty = true;
        _requiredJobFailed = false;
        try { ReleaseSpatialGeneration(previous); }
        catch (Exception error)
        {
            _spatialRetirementError = error.ToString();
            GD.PushError($"Previous spatial grass generation retirement failed: {error}");
        }
    }

    private SpatialFaceResources CreateSpatialFace(FaceLattice lattice)
    {
        var resources = new SpatialFaceResources();
        try
        {
            resources.Nodes = UploadTexture(lattice.Nodes);
            resources.Cover = UploadTexture(lattice.Cover);
            return resources;
        }
        catch (Exception error)
        {
            try { resources.Dispose(); } catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }

        ImageTexture UploadTexture(float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            using Image source = Image.CreateFromData(_latticeSize, _latticeSize, false, Image.Format.Rgbaf, bytes);
            return ImageTexture.CreateFromImage(source);
        }
    }

    private void CreateSpatialTile(SpatialGeneration generation, SpatialTileData tile)
    {
        if (tile.Grass.Length > 0)
        {
            CreateBatch(tile.Grass, 0);
            ThrowSpatialFixtureFault(SpatialFixtureFault.AfterGrassBatch);
        }
        if (tile.Litter.Length > 0) CreateBatch(tile.Litter, 1);

        void CreateBatch(float[] buffer, int layer)
        {
            FaceLattice face = generation.Field.Faces[tile.FaceIndex];
            SpatialFaceResources textures = generation.Faces[tile.FaceIndex];
            Vector3 min = V(tile.Minimum), max = V(tile.Maximum), centre = (min + max) * 0.5f;
            ShaderMaterial? material = null;
            MultiMesh? multimesh = null;
            MultiMeshInstance3D? instance = null;
            try
            {
                material = (ShaderMaterial)_spatialTemplate!.Duplicate();
                multimesh = new MultiMesh();
                material.SetShaderParameter("layer", layer);
                material.SetShaderParameter("grid_width", layer == 0 ? _gridWidth : _litterWidth);
                material.SetShaderParameter("far_radius", layer == 0 ? _far : _litterFar);
                material.SetShaderParameter("shrink_start", layer == 0 ? _shrink : _litterShrink);
                material.SetShaderParameter("lattice", textures.Nodes);
                material.SetShaderParameter("lattice_cover", textures.Cover);
                material.SetShaderParameter("face", face.Face);
                material.SetShaderParameter("lattice_cell", new Vector2I((int)face.CornerX, (int)face.CornerY));
                material.SetShaderParameter("face_origin", new Vector2((float)face.OriginU, (float)face.OriginV));
                material.SetShaderParameter("tile_cell", new Vector2I(tile.CellX, tile.CellY));
                multimesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
                multimesh.UseCustomData = true;
                multimesh.Mesh = layer == 0 ? _spatialGrassMesh : _spatialLitterMesh;
                multimesh.CustomAabb = new Aabb(min - centre, max - min).Grow(SpatialDeformationEnvelope);
                multimesh.InstanceCount = buffer.Length / 16;
                multimesh.Buffer = buffer;
                instance = new MultiMeshInstance3D
                {
                    Name = $"Cover_{generation.Serial}_{face.Face}_{tile.CellX}_{tile.CellY}_{layer}",
                    Multimesh = multimesh, MaterialOverride = material, Visible = false,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                };
                AddChild(instance);
                generation.Batches.Add(new() { Instance = instance, MultiMesh = multimesh, Material = material,
                    Data = tile, Layer = layer, Centre = centre });
            }
            catch (Exception error)
            {
                List<Exception>? failures = null;
                try { instance?.Free(); } catch (Exception cleanup) { (failures ??= new()).Add(cleanup); }
                try { multimesh?.Dispose(); } catch (Exception cleanup) { (failures ??= new()).Add(cleanup); }
                try { material?.Dispose(); } catch (Exception cleanup) { (failures ??= new()).Add(cleanup); }
                if (failures != null) { failures.Insert(0, error); throw new AggregateException(failures); }
                throw;
            }
        }
    }

    private void UpdateSpatialVisibility(Vector3D camera, Vector3 cameraRender, bool walking, bool show)
    {
        _spatialLastCamera = camera;
        _spatialHasCamera = true;
        _spatialWalking = walking;
        SpatialGeneration? generation = _spatialResident;
        if (generation == null) return;
        Vector3 anchor = _frame.ToRender(generation.Field.Anchor);
        Vector3 feet = cameraRender - _frame.DirectionToRender(camera.Normalized) * 1.7f;
        foreach (SpatialBatch batch in generation.Batches)
        {
            SpatialTileData tile = batch.Data;
            FaceLattice face = generation.Field.Faces[tile.FaceIndex];
            int width = batch.Layer == 0 ? _gridWidth : _litterWidth;
            (double cu, double cv) = CellOn(face.Face, camera);
            int ox = (int)Math.Clamp(Math.Floor(cu) - width / 2 - face.CornerX, 0, _latticeSize - 1 - width);
            int oy = (int)Math.Clamp(Math.Floor(cv) - width / 2 - face.CornerY, 0, _latticeSize - 1 - width);
            bool intersects = tile.MinX < ox + width && tile.MaxX > ox && tile.MinY < oy + width && tile.MaxY > oy;
            GroundCoverLayers layer = batch.Layer == 0 ? GroundCoverLayers.Grass : GroundCoverLayers.Litter;
            float far = batch.Layer == 0 ? _far : _litterFar;
            // Shader distance is measured at the root; all possible roots lie in this convex support bound.
            double conservativeFar = far + 0.05; // retain the CPU/GPU float-rounding boundary band
            bool inRange = DistanceSquaredToBounds(cameraRender - anchor, tile.Minimum, tile.Maximum) < conservativeFar * conservativeFar;
            if (_contact != null && batch.Layer == 0)
            {
                bool supported = TrySpatialContactBounds(generation, batch, out Aabb contactBounds);
                if (supported)
                {
                    Vector3 low = contactBounds.Position, high = contactBounds.End;
                    inRange = DistanceSquaredToBounds(cameraRender, new Vector3D(low.X, low.Y, low.Z),
                        new Vector3D(high.X, high.Y, high.Z)) < conservativeFar * conservativeFar;
                    batch.MultiMesh.CustomAabb = new Aabb(low - (anchor + batch.Centre), contactBounds.Size).Grow(SpatialDeformationEnvelope);
                }
                else inRange = false;
            }
            batch.Instance.Visible = show && intersects && inRange && (_diagnosticLayers & layer) != 0;
            batch.Instance.GlobalTransform = new Transform3D(Basis.Identity, anchor + batch.Centre);
            batch.Material.SetShaderParameter("grid_offset", new Vector2I(ox, oy));
            batch.Material.SetShaderParameter("anchor", anchor);
            batch.Material.SetShaderParameter("trample", walking && batch.Layer == 0
                ? new Vector4(feet.X, feet.Y, feet.Z, 0.9f) : Vector4.Zero);
        }
    }

    private static double DistanceSquaredToBounds(Vector3 point, Vector3D min, Vector3D max)
    {
        double x = Math.Max(Math.Max(min.X - point.X, point.X - max.X), 0);
        double y = Math.Max(Math.Max(min.Y - point.Y, point.Y - max.Y), 0);
        double z = Math.Max(Math.Max(min.Z - point.Z, point.Z - max.Z), 0);
        return x * x + y * y + z * z;
    }

    private static Vector3 V(Vector3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

    private void ReleaseSpatialGeneration(SpatialGeneration? generation)
    {
        if (generation == null) return;
        List<Exception>? failures = null;
        foreach (SpatialBatch batch in generation.Batches)
        {
            try { ThrowSpatialFixtureFault(SpatialFixtureFault.DuringRetirement); }
            catch (Exception error) { (failures ??= new()).Add(error); }
            try { batch.Instance.Free(); } catch (Exception error) { (failures ??= new()).Add(error); }
            try { batch.MultiMesh.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
            try { batch.Material.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
        }
        generation.Batches.Clear();
        foreach (SpatialFaceResources face in generation.Faces)
            try { face.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
        generation.Faces.Clear();
        if (failures != null) throw new AggregateException("Spatial grass generation failed to retire.", failures);
    }

    private void StopSpatialResources()
    {
        SpatialGeneration? staging = _spatialStaging, resident = _spatialResident;
        ShaderMaterial? template = _spatialTemplate;
        Mesh? grass = _spatialGrassMesh, litter = _spatialLitterMesh;
        _spatialStaging = null; _spatialResident = null; _spatialTemplate = null;
        _spatialGrassMesh = null; _spatialLitterMesh = null;
        _spatialHasCamera = false;
        List<Exception>? failures = null;
        try { ReleaseSpatialGeneration(staging); } catch (Exception error) { (failures ??= new()).Add(error); }
        try { ReleaseSpatialGeneration(resident); } catch (Exception error) { (failures ??= new()).Add(error); }
        try { template?.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
        try { grass?.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
        try { litter?.Dispose(); } catch (Exception error) { (failures ??= new()).Add(error); }
        if (failures != null) throw new AggregateException("Spatial grass resources failed to retire.", failures);
    }
}
