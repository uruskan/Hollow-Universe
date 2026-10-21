using System;
using System.Runtime.InteropServices;

namespace HollowUniverse.Common;

/// <summary>
/// The client process's private commit split by what backs it, for the opt-in memory traces (docs/DEVELOPMENT.md,
/// 19 October 2026). On Windows the graphics driver commits write-combined private memory for the allocations it makes
/// for the process: the backing of device-local video memory (barely resident while the card has room for it) and
/// host-visible buffers (staging, readback). <see cref="Split.GraphicsMiB"/> sums those regions; <see cref="Split.CpuMiB"/>
/// is the rest of the private bytes: native heaps (the engine, the driver's compiled pipelines, physics), the managed
/// heap, stacks. One walk of the address space, a few milliseconds; never on a per-frame path.
/// </summary>
internal static class ProcessCommit
{
    /// <summary>Private bytes, and of them the write-combined (graphics driver) and the other commit; -1 where unknown.</summary>
    public readonly record struct Split(double PrivateMiB, double GraphicsMiB, double CpuMiB);

    private const uint MemCommit = 0x1000, MemPrivate = 0x20000, PageWriteCombine = 0x400;
    private const double MiB = 1024.0 * 1024.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nint RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll")]
    private static extern nint VirtualQuery(nint address, out MemoryBasicInformation info, nint length);

    public static Split Measure()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        double privateMiB = process.PrivateMemorySize64 / MiB;
        if (!OperatingSystem.IsWindows())
        {
            return new Split(privateMiB, -1, -1);
        }
        long writeCombined = 0;
        nint address = 0, length = Marshal.SizeOf<MemoryBasicInformation>();
        while (VirtualQuery(address, out MemoryBasicInformation info, length) != 0)
        {
            if (info.State == MemCommit && info.Type == MemPrivate && (info.Protect & PageWriteCombine) != 0)
            {
                writeCombined += info.RegionSize;
            }
            nint next = info.BaseAddress + info.RegionSize;
            if (next <= address)
            {
                break;
            }
            address = next;
        }
        double graphicsMiB = writeCombined / MiB;
        return new Split(privateMiB, graphicsMiB, Math.Max(0, privateMiB - graphicsMiB));
    }
}
