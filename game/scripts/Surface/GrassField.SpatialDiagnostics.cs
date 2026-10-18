namespace HollowUniverse.Surface;

public readonly record struct SpatialGrassDiagnostics(bool Enabled, int TileCells, int ResidentTiles,
    int ResidentBatches, int StagedTiles, int PendingTileUploads, long ResidentBufferBytes, long StagedBufferBytes,
    long ReservedGenerationBytes, long AllocationLimitBytes, long UploadedBytes, double LastUploadMilliseconds,
    long ResidentGeneration, bool RequiredContentFailed, long UploadScratchBytes, string? RetirementError)
{
    public string Scope => "Diagnostic spatial submission only. Reservation bounds new instance/lattice payloads; excludes authored plants, source terrain and total process/GPU memory. Upload targets are soft; each texture pair is indivisible.";
}

public sealed partial class GrassField
{
    /// <summary>Main-thread snapshot; no per-frame traversal when diagnostics are not requested.</summary>
    public SpatialGrassDiagnostics CaptureSpatialDiagnostics()
    {
        long residentBytes = 0, stagedBytes = 0;
        if (_spatialResident != null)
            foreach (SpatialTileData tile in _spatialResident.Field.SpatialTiles) residentBytes += tile.BufferBytes;
        if (_spatialStaging != null)
            foreach (SpatialTileData tile in _spatialStaging.Field.SpatialTiles) stagedBytes += tile.BufferBytes;
        return new(_spatialCells != 0, _spatialCells, _spatialResident?.Field.SpatialTiles.Count ?? 0,
            _spatialResident?.Batches.Count ?? 0, _spatialStaging?.Field.SpatialTiles.Count ?? 0,
            (_spatialStaging?.Field.SpatialTiles.Count ?? 0) - (_spatialStaging?.NextTile ?? 0), residentBytes,
            stagedBytes, _spatialCells == 0 ? 0 : _spatialWorstGenerationBytes *
                ((_spatialResident != null ? 1 : 0) + (_job != null || _spatialStaging != null ? 1 : 0)),
            SpatialMemoryLimit, _spatialUploadedBytes, _spatialLastUploadMilliseconds,
            _spatialResident?.Serial ?? 0, _requiredJobFailed, _spatialCells == 0 ? 0 : _spatialScratchBytes,
            _spatialRetirementError);
    }
}
