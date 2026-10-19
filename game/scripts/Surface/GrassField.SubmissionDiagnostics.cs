using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Surface;

[Flags]
public enum GroundCoverLayers
{
    None = 0,
    Grass = 1,
    Litter = 2,
    Authored = 4,
    All = Grass | Litter | Authored,
}

public readonly record struct GroundCoverSubmission(int Batches, long Instances, long Vertices, long Triangles);

public readonly record struct GroundCoverDiagnostics(bool SetupComplete, bool Stopped, bool Near, bool Ready,
    bool RequiredContentFailed, GroundCoverLayers Layers, GroundCoverSubmission Grass, GroundCoverSubmission Litter,
    GroundCoverSubmission Authored, GrassWorkSnapshot Work)
{
    public string Scope => "Visible-node instance submissions before renderer frustum culling, shader rejection or additional passes; not GPU invocation counts or timings.";
}

public sealed partial class GrassField
{
    private GroundCoverLayers _diagnosticLayers = GroundCoverLayers.All;
    private readonly Dictionary<Mesh, (int Vertices, int Triangles)> _diagnosticGeometry = new();

    /// <summary>Main-thread diagnostic mask. Hiding layers retains generation, uploads and loading readiness.</summary>
    public GroundCoverLayers DiagnosticLayers
    {
        get => _diagnosticLayers;
        set
        {
            _diagnosticLayers = value & GroundCoverLayers.All;
            if (!_setupComplete || _stopped) return;
            bool grass = (_diagnosticLayers & GroundCoverLayers.Grass) != 0, litter = (_diagnosticLayers & GroundCoverLayers.Litter) != 0;
            foreach (GrassTile tile in _tiles.Values)
                foreach (Batch batch in tile.Batches)
                {
                    bool layer = _species[batch.Species].Litter ? litter : grass;
                    if (layer) continue;
                    batch.Instance.Visible = false;
                    batch.Shown = 0;
                }
            if (_coverRoot != null) _coverRoot.Visible = _show && (_diagnosticLayers & GroundCoverLayers.Authored) != 0;
        }
    }

    /// <summary>Opt-in main-thread snapshot; no per-frame traversal, allocation or logging when unused.</summary>
    public GroundCoverDiagnostics CaptureDiagnostics()
    {
        GroundCoverSubmission grass = default, litter = default, authored = default;
        if (_setupComplete && !_stopped)
        {
            foreach (GrassTile tile in _tiles.Values)
                foreach (Batch batch in tile.Batches)
                {
                    if (_species.Length > 0 && _species[batch.Species].Litter) Add(ref litter, batch.Instance);
                    else Add(ref grass, batch.Instance);
                }
            foreach (CoverBatch? batch in _coverBatches)
                if (batch != null) Add(ref authored, batch.Instance);
        }
        return new(_setupComplete, _stopped, _near, IsIdle, _requiredJobFailed, _diagnosticLayers,
            grass, litter, authored, _workers.Monitor.Snapshot());

        void Add(ref GroundCoverSubmission total, MultiMeshInstance3D instance)
        {
            if (!instance.IsVisibleInTree()) return;
            MultiMesh mesh = instance.Multimesh;
            int count = mesh.VisibleInstanceCount < 0 ? mesh.InstanceCount : Math.Min(mesh.InstanceCount, mesh.VisibleInstanceCount);
            if (count == 0 || mesh.Mesh == null) return;
            if (!_diagnosticGeometry.TryGetValue(mesh.Mesh, out var geometry))
            {
                geometry = GeometryCounts(mesh.Mesh);
                _diagnosticGeometry.Add(mesh.Mesh, geometry);
            }
            total = new(total.Batches + 1, total.Instances + count, total.Vertices + (long)count * geometry.Vertices,
                total.Triangles + (long)count * geometry.Triangles);
        }
    }

    // Called lazily for diagnostic snapshots on the main thread; never read meshes on workers or timed frames.
    private static (int Vertices, int Triangles) GeometryCounts(Mesh mesh)
    {
        int vertices = 0, triangles = 0;
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            using var arrays = mesh.SurfaceGetArrays(surface);
            int count = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
            int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            vertices += count;
            triangles += (indices.Length > 0 ? indices.Length : count) / 3;
        }
        return (vertices, triangles);
    }

    /// <summary>The field's tiles and their work, for the streaming status line.</summary>
    internal (int Tiles, int Resident, int Building, int Uploads, int Batches, int Pooled, long UploadedBytes, double UploadMs) TileState()
    {
        int resident = 0, building = 0, batches = 0, pooled = 0;
        foreach (GrassTile tile in _tiles.Values)
        {
            if (tile.Built != null) resident++;
            if (tile.Job != null) building++;
            batches += tile.Batches.Count;
        }
        foreach (Stack<Batch> free in _pool.Values) pooled += free.Count;
        return (_tiles.Count, resident, building, _uploadQueue.Count, batches, pooled, _uploadedBytes, _lastUploadMilliseconds);
    }
}
