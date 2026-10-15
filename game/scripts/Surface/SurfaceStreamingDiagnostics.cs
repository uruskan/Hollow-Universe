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
            lock (_lock)
                return $"idle={IsIdle} pending={Pending} queued={_queue.Count} uploads={_done.Count} drawn={_drawn.Count} " +
                    $"finest={FinestLevel}/{MaxLevel} camera={_camera}";
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
    public string StreamingStatus => $"idle={IsIdle} near={_near} ready={_ready} " +
        $"job={_job?.Status.ToString() ?? "none"} centre={_centre}";
}

public sealed partial class CliffField
{
    public string StreamingStatus => $"idle={IsIdle} job={_job?.Status.ToString() ?? "none"} pending={_pending.Count} drawn={_instances.Count}";
}

public sealed partial class MonumentField
{
    public string StreamingStatus => $"idle={IsIdle} pending={_pending.Count} drawn={_instances.Count}";
}
