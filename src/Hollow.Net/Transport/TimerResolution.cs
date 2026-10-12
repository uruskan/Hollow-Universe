using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Hollow.Net.Transport;

/// <summary>
/// Waiting precisely. Windows sleeps in 15.6 ms steps unless a process asks for 1 ms timers; a 30 Hz tick and a
/// latency simulator need better than that.
/// </summary>
public static partial class TimerResolution
{
    private static int _requests;

    /// <summary>Asks for 1 ms timer resolution (Windows; elsewhere it already is). Pair with <see cref="End"/>.</summary>
    public static void Begin()
    {
        if (OperatingSystem.IsWindows() && Interlocked.Increment(ref _requests) == 1)
        {
            _ = TimeBeginPeriod(1);
        }
    }

    public static void End()
    {
        if (OperatingSystem.IsWindows() && Interlocked.Decrement(ref _requests) == 0)
        {
            _ = TimeEndPeriod(1);
        }
    }

    /// <summary>Waits until a <see cref="Stopwatch"/> timestamp: sleeps while far, then yields the last stretch.</summary>
    public static void WaitUntil(long timestamp)
    {
        while (true)
        {
            double remaining = (timestamp - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            if (remaining <= 0)
            {
                return;
            }
            if (remaining > 2.0)
            {
                Thread.Sleep((int)(remaining - 1.5));
            }
            else
            {
                Thread.Yield();
            }
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint milliseconds);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint milliseconds);
}
