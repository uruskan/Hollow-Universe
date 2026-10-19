using System.Threading;

namespace HollowUniverse.Surface;

// Queried only by capture/profile diagnostics. No per-frame strings or extra streaming work.
public partial class SurfaceView
{
    public string StreamingStatus => $"idleFrames={_idleFrames} " +
        $"terrain=[{_terrain.StreamingStatus}] plants=[{_vegetation?.StreamingStatus ?? "absent"}] " +
        $"grass=[{_grass?.StreamingStatus ?? "absent"}] cliffs=[{_cliffs?.StreamingStatus ?? "absent"}] " +
        $"monuments=[{_monuments?.StreamingStatus ?? "absent"}]";
}

public sealed partial class TerrainLod
{
    public string StreamingStatus
    {
        get
        {
            var counts = _work?.QueueCounts ?? (Queued: 0, Completed: 0);
            return $"idle={IsIdle} pending={Pending} queued={counts.Queued} uploads={counts.Completed} drawn={_drawn.Count} " +
                $"failed={FailedBuilds} historicalFaults={_work?.Failed ?? 0} finest={FinestLevel}/{MaxLevel} camera={_camera}";
        }
    }
}

public sealed partial class Vegetation
{
    public string StreamingStatus => $"idle={IsIdle} library={LibraryReady} impostors={ImpostorsReady} " +
        $"queued={_jobs.Count} busy={Volatile.Read(ref _busy)} missing={_nearMissing} " +
        $"nearJob={_nearJob?.Status.ToString() ?? "none"} dirty={_nearDirty} nearBuilds={NearBuilds} blocks={_blocks.Count} refreshes={_blockJobs.Count}";
}

public sealed partial class GrassField
{
    public string StreamingStatus
    {
        get
        {
            var t = TileState();
            return $"idle={IsIdle} near={_near} tiles={t.Tiles} resident={t.Resident} building={t.Building} uploads={t.Uploads} " +
                $"batches={t.Batches} pooled={t.Pooled} instances={_instances} uploadedBytes={t.UploadedBytes} " +
                $"uploadMs={t.UploadMs:0.00} failed={_requiredJobFailed}";
        }
    }
}

public sealed partial class CliffField
{
    public string StreamingStatus => $"idle={IsIdle} job={_job?.Status.ToString() ?? "none"} pending={_pending.Count} drawn={_instances.Count} " +
        $"far={FarShownPieces} in {FarShownChunks}/{FarChunkCount} chunks swept={FarSweptCells}/{FarCellCount} farDirty={_farDirty.Count} " +
        $"farUs={LastFarMicroseconds} walls={WallCount} wallsPending={WallPendingCount} wallTriangles={WallTriangles}";
}

public sealed partial class MonumentField
{
    public string StreamingStatus => $"idle={IsIdle} pending={_pending.Count} drawn={_instances.Count}";
}
