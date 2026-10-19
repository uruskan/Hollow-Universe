using System;
using System.Reflection;
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

    private static readonly PropertyInfo? OwnsHandle =
        typeof(NativeObject).GetProperty("OwnsHandle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>
    /// Makes disposing a shape's settings release them natively. JoltPhysicsSharp 2.23.0 creates shape settings without
    /// ownership of their handles, so <c>Dispose</c> never released them; and a settings object keeps a reference to the
    /// shape it created. Every ground tile, prop, building piece, character and ship shape therefore stayed in memory
    /// with its settings (a tile's include a copy of all its vertices and triangles), even after its own shape was
    /// disposed (T0.3). Release is reference-counted, so children may be disposed before the compound that holds them.
    /// </summary>
    internal static T Owned<T>(this T settings) where T : NativeObject
    {
        if (OwnsHandle != null && OwnsHandle.GetValue(settings) is false) OwnsHandle.SetValue(settings, true);
        return settings;
    }

    /// <summary>
    /// Makes disposing <paramref name="system"/> free its native memory, exactly once. JoltPhysicsSharp 2.23.0 creates a
    /// <see cref="PhysicsSystem"/> without ownership of its handle, so <c>Dispose</c> never destroyed it: every world and
    /// ship space leaked its native system, its bodies and, through them, its ground and part shapes (about 5.6 MiB for an
    /// empty world, 16 MiB with resident ground). A long test run or a long-lived process then exhausted memory, and Jolt
    /// crashed on the failed native allocation (docs/TERRAIN_SCENERY_PLAN.md, T0.3). joltc's system destroy also deletes
    /// the layer pair filter, broadphase layer interface and object-vs-broadphase filter the system was created with, so
    /// those give up their own handles here. Call under <see cref="Gate"/>, right before disposing them all.
    /// </summary>
    internal static void HandOverToSystem(PhysicsSystem system, NativeObject layerPairs, NativeObject broadPhaseLayers,
        NativeObject objectVsBroadPhase)
    {
        // A binding that already owns the system is trusted to manage the filters consistently with it.
        if (OwnsHandle == null || OwnsHandle.GetValue(system) is true) return;
        OwnsHandle.SetValue(system, true);
        OwnsHandle.SetValue(layerPairs, false);
        OwnsHandle.SetValue(broadPhaseLayers, false);
        OwnsHandle.SetValue(objectVsBroadPhase, false);
    }
}
