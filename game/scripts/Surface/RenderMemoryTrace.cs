using System;
using System.Diagnostics;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Surface;

/// <summary>
/// Opt-in main-thread allocation snapshots at resource phase boundaries. Renderer counters describe engine-accounted
/// allocations, not physical GPU residency; process working set/private memory also include unrelated concurrent work.
/// Private memory is split into the graphics driver's write-combined commit and the rest (<see cref="Common.ProcessCommit"/>).
/// No forced collection, resource readback, synchronization draw or additional generation is performed here.
/// </summary>
internal static class RenderMemoryTrace
{
    private static readonly bool Enabled = Array.IndexOf(OS.GetCmdlineUserArgs(), "--capture-trace") >= 0;
    private const double MiB = 1024.0 * 1024;

    public static void Mark(string phase)
    {
        if (!Enabled) return;
        try
        {
            using Process process = Process.GetCurrentProcess();
            Common.ProcessCommit.Split commit = Common.ProcessCommit.Measure();
            GD.Print("[render-memory] " + JsonSerializer.Serialize(new
            {
                phase,
                processSeconds = Time.GetTicksUsec() / 1_000_000.0,
                processFrame = Engine.GetProcessFrames(),
                workingSetMiB = process.WorkingSet64 / MiB,
                privateMiB = commit.PrivateMiB,
                graphicsCommitMiB = commit.GraphicsMiB,
                cpuCommitMiB = commit.CpuMiB,
                managedMiB = GC.GetTotalMemory(false) / MiB,
                managedCommittedMiB = GC.GetGCMemoryInfo().TotalCommittedBytes / MiB,
                engineStaticMiB = OS.GetStaticMemoryUsage() / MiB,
                rendererTextureMiB = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / MiB,
                rendererBufferMiB = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / MiB,
                rendererTotalMiB = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / MiB,
                rendererAccounting = "engine allocations; not measured GPU residency",
            }));
        }
        catch (Exception error)
        {
            GD.Print($"[render-memory] phase={phase}; diagnostics unavailable: {error.GetType().Name}");
        }
    }
}
