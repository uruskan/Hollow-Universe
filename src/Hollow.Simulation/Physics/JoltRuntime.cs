using System;
using System.Threading;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Physics;

/// <summary>
/// The process-wide Jolt runtime (GROUND.md §4.5). Jolt is initialised once per process, by whichever world needs it
/// first.
/// <para>
/// joltc's character functions take no temporary allocator: they share one inside the native library. Two threads
/// moving characters at the same time (a hosted server and the client's prediction, both in the game process) would
/// corrupt it, so every call into Jolt from Hollow.Simulation holds <see cref="Gate"/>. A zone's tick holds it for the
/// whole movement pass; the cost is nothing while one thread uses Jolt at a time.
/// </para>
/// </summary>
public static class JoltRuntime
{
    private static readonly Lock InitLock = new();
    private static bool _initialised;

    /// <summary>Held around every call into Jolt (see the class remarks).</summary>
    public static readonly Lock Gate = new();

    private static JobSystem? _jobs;

    /// <summary>Initialises Jolt if it isn't yet. Throws if the native library can't be loaded.</summary>
    public static void EnsureInitialised()
    {
        lock (InitLock)
        {
            if (_initialised)
            {
                return;
            }
            if (!Foundation.Init(doublePrecision: false))
            {
                throw new InvalidOperationException("Jolt (joltc) failed to initialise.");
            }
            // Jolt's update asks for a job system. Ours only ever updates the broadphase (PhysicsWorld.OptimizeBroadPhase),
            // which runs on the calling thread, so the pool has no threads of its own.
            var config = new JobSystemThreadPoolConfig { maxJobs = 256, maxBarriers = 8, numThreads = 0 };
            _jobs = new JobSystemThreadPool(in config);
            _initialised = true;
        }
    }

    /// <summary>The job system for Jolt's update: no threads (see <see cref="EnsureInitialised"/>).</summary>
    internal static JobSystem Jobs => _jobs ?? throw new InvalidOperationException("Jolt isn't initialised.");
}
