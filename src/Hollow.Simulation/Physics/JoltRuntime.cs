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
            _initialised = true;
        }
    }
}
