using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Hollow.Simulation.Diagnostics;

/// <summary>The parts of a server tick that <see cref="TickProfile"/> times (GROUND.md §5.11).</summary>
public enum ProfileSection
{
    /// <summary>The whole tick.</summary>
    Tick,

    /// <summary>Receiving: the network's events, inputs and intents.</summary>
    Receive,

    /// <summary>Logins finished and characters spawned; respawns that waited for their ground.</summary>
    Logins,

    /// <summary>The zone's step.</summary>
    Step,

    /// <summary>In the step: the ground under every character on foot (built, taken in or prefetched).</summary>
    CharacterGround,

    /// <summary>In the step: Jolt's broadphase kept in shape.</summary>
    BroadPhase,

    /// <summary>In the step: the characters' movement.</summary>
    Movement,

    /// <summary>In the step: the ships (their ground included).</summary>
    Ships,

    /// <summary>In the step: ground and props nobody needs any more, removed.</summary>
    GroundRelease,

    /// <summary>In the step: the game's rules after movement (actions, projectiles, survival, devices, building).</summary>
    Game,

    /// <summary>After the step: events routed, own states and containers sent.</summary>
    AfterStep,

    /// <summary>Snapshots and the replication of things, nature, building and explosives.</summary>
    Replicate,

    /// <summary>In replication: every player's snapshot written and sent (characters and ships).</summary>
    ReplicateSnapshots,

    /// <summary>In replication: things (drops, corpses, deployables) for every player.</summary>
    ReplicateThings,

    /// <summary>In replication: the natural objects' changes for every player.</summary>
    ReplicateNature,

    /// <summary>In replication: building pieces and explosives for every player.</summary>
    ReplicateBuilding,

    /// <summary>Saving (queued to the persistence thread).</summary>
    Save,

    /// <summary>LiteNetLib's update: the datagrams sent.</summary>
    Send,

    /// <summary>Across the above: ground tiles added to Jolt (with their natural objects).</summary>
    TileAdd,

    /// <summary>Across the above: a tile built on the tick's thread, or waited for while a worker built it.</summary>
    TileWait,

    /// <summary>Across the above: ground tiles removed (with their natural objects).</summary>
    TileRemove,

    /// <summary>Across the above: natural objects' bodies added (part of <see cref="TileAdd"/> or a regrowth).</summary>
    PropAdd,

    /// <summary>Across the above: natural objects' bodies removed.</summary>
    PropRemove,

    /// <summary>Across the above: the natural objects of a tile looked up on the tick's thread (scatter generated if not cached).</summary>
    NatureQuery,

    /// <summary>Across the above: ground asked for ahead (tiles started on worker threads, or marked in use).</summary>
    Prefetch,

    /// <summary>Across the above: respawns (their ground included).</summary>
    Respawn,
}

/// <summary>
/// Where a server's ticks go (GROUND.md §5.11): time per <see cref="ProfileSection"/> on the tick's thread, totals since
/// <see cref="Reset"/>, each section's longest tick, and what the slow ticks spent theirs on, garbage collections
/// included. Sections nest (a tile added during movement counts in both), so the report lists them as a tree. One
/// thread; a null profile measures nothing.
/// </summary>
public sealed class TickProfile
{
    private static readonly int Count = Enum.GetValues<ProfileSection>().Length;

    private readonly long[] _now = new long[Count];
    private readonly int[] _nowCalls = new int[Count];
    private readonly long[] _total = new long[Count];
    private readonly long[] _calls = new long[Count];
    private readonly long[] _worst = new long[Count];
    private readonly long[] _slow = new long[Count];
    private readonly long[] _allocated = new long[Count];

    /// <summary>A tick at least this long counts as slow (the budget: a tick at 30 Hz).</summary>
    public double SlowMs { get; init; } = 1000.0 / 30;

    public long Ticks { get; private set; }

    public long SlowTicks { get; private set; }

    public double GcPauseMs { get; private set; }

    public double SlowGcPauseMs { get; private set; }

    public int SlowWithGc { get; private set; }

    /// <summary>Adds a measured stretch (in <see cref="Stopwatch"/> ticks, and bytes allocated) to a section.</summary>
    public void Add(ProfileSection section, long elapsed, long allocated = 0)
    {
        _now[(int)section] += elapsed;
        _nowCalls[(int)section]++;
        _allocated[(int)section] += allocated;
    }

    /// <summary>Bytes allocated on the tick's thread, in all (garbage the tick makes itself).</summary>
    public long AllocatedBytes { get; private set; }

    /// <summary>
    /// Closes a tick: its sections go into the totals; a slow tick's also into the slow ticks'. Takes the time the
    /// garbage collector paused everything during the tick and what the tick's thread allocated.
    /// </summary>
    public void EndTick(double gcPauseMs, long allocatedBytes = 0)
    {
        double tickMs = _now[(int)ProfileSection.Tick] * 1000.0 / Stopwatch.Frequency;
        bool slow = tickMs >= SlowMs;
        Ticks++;
        GcPauseMs += gcPauseMs;
        AllocatedBytes += allocatedBytes;
        if (gcPauseMs > 0)
        {
            // What kind of collection paused this tick (the latest one's: a tick seldom holds two).
            GCMemoryInfo info = GC.GetGCMemoryInfo(GCKind.Any);
            int kind = info.Generation < 2 ? info.Generation : info.Concurrent ? 3 : 2;
            _gcPauses[kind] += gcPauseMs;
            _gcCounts[kind]++;
            _gcWorst[kind] = Math.Max(_gcWorst[kind], gcPauseMs);
            MemoryLoadPercent = Math.Max(MemoryLoadPercent, info.TotalAvailableMemoryBytes == 0 ? 0 : 100.0 * info.MemoryLoadBytes / info.TotalAvailableMemoryBytes);
        }
        if (slow)
        {
            SlowTicks++;
            SlowGcPauseMs += gcPauseMs;
            SlowWithGc += gcPauseMs > 0 ? 1 : 0;
        }
        for (int i = 0; i < Count; i++)
        {
            _total[i] += _now[i];
            _calls[i] += _nowCalls[i];
            _worst[i] = Math.Max(_worst[i], _now[i]);
            if (slow)
            {
                _slow[i] += _now[i];
            }
            _now[i] = 0;
            _nowCalls[i] = 0;
        }
    }

    public void Reset()
    {
        Array.Clear(_total);
        Array.Clear(_calls);
        Array.Clear(_worst);
        Array.Clear(_slow);
        Array.Clear(_allocated);
        Ticks = SlowTicks = 0;
        GcPauseMs = SlowGcPauseMs = 0;
        SlowWithGc = 0;
        AllocatedBytes = 0;
        Array.Clear(_gcPauses);
        Array.Clear(_gcCounts);
        Array.Clear(_gcWorst);
        MemoryLoadPercent = 0;
    }

    // Collections that paused ticks, by kind: gen0, gen1, blocking gen2, background gen2.
    private readonly double[] _gcPauses = new double[4];
    private readonly int[] _gcCounts = new int[4];
    private readonly double[] _gcWorst = new double[4];

    /// <summary>The machine's memory in use at the collections, at most (the collector works harder above 90 %).</summary>
    public double MemoryLoadPercent { get; private set; }

    /// <summary>A section's average per tick in milliseconds.</summary>
    public double AverageMs(ProfileSection section) => Ticks == 0 ? 0 : Ms(_total[(int)section]) / Ticks;

    /// <summary>A section's longest tick in milliseconds.</summary>
    public double WorstMs(ProfileSection section) => Ms(_worst[(int)section]);

    public long Calls(ProfileSection section) => _calls[(int)section];

    /// <summary>The table of GROUND.md §5.11: per section, its average and longest tick, calls a tick, and its share of the slow ticks.</summary>
    public string Report()
    {
        var b = new StringBuilder();
        b.Append(CultureInfo.InvariantCulture,
            $"Profile ({Ticks} ticks; {SlowTicks} at {SlowMs:0.0} ms or more, {SlowWithGc} of them with a garbage collection):\n");
        b.Append("  section                   ms a tick   longest   calls a tick   in slow ticks (ms each)   KB a tick\n");
        foreach ((ProfileSection s, int depth) in Tree)
        {
            int i = (int)s;
            string name = new string(' ', 2 * depth) + Label(s);
            b.Append(CultureInfo.InvariantCulture,
                $"  {name,-24} {AverageMs(s),9:0.000} {WorstMs(s),9:0.0} {(Ticks == 0 ? 0 : (double)_calls[i] / Ticks),13:0.00}   " +
                $"{(SlowTicks == 0 ? 0 : Ms(_slow[i]) / SlowTicks),8:0.0}   {(Ticks == 0 ? 0 : _allocated[i] / 1024.0 / Ticks),20:0.0}\n");
        }
        b.Append(CultureInfo.InvariantCulture,
            $"  garbage collections: {(Ticks == 0 ? 0 : GcPauseMs / Ticks):0.000} ms a tick paused, {(SlowTicks == 0 ? 0 : SlowGcPauseMs / SlowTicks):0.0} ms in each slow tick; " +
            $"the tick's thread allocated {(Ticks == 0 ? 0 : AllocatedBytes / 1024.0 / Ticks):0} KB a tick\n");
        string[] kinds = { "gen0", "gen1", "blocking gen2", "background gen2" };
        b.Append("  collections that paused ticks:");
        for (int k = 0; k < kinds.Length; k++)
        {
            b.Append(CultureInfo.InvariantCulture, $" {kinds[k]} {_gcCounts[k]} ({_gcPauses[k]:0} ms, longest {_gcWorst[k]:0.0}),");
        }
        b.Append(CultureInfo.InvariantCulture, $" the machine's memory {MemoryLoadPercent:0} % used at most\n");
        return b.ToString();
    }

    // The report's order and nesting.
    private static readonly (ProfileSection, int)[] Tree =
    {
        (ProfileSection.Tick, 0),
        (ProfileSection.Receive, 1),
        (ProfileSection.Logins, 1),
        (ProfileSection.Step, 1),
        (ProfileSection.CharacterGround, 2),
        (ProfileSection.BroadPhase, 2),
        (ProfileSection.Movement, 2),
        (ProfileSection.Ships, 2),
        (ProfileSection.GroundRelease, 2),
        (ProfileSection.Game, 2),
        (ProfileSection.AfterStep, 1),
        (ProfileSection.Replicate, 1),
        (ProfileSection.ReplicateSnapshots, 2),
        (ProfileSection.ReplicateThings, 2),
        (ProfileSection.ReplicateNature, 2),
        (ProfileSection.ReplicateBuilding, 2),
        (ProfileSection.Save, 1),
        (ProfileSection.Send, 1),
        (ProfileSection.TileWait, 0),
        (ProfileSection.TileAdd, 0),
        (ProfileSection.PropAdd, 1),
        (ProfileSection.TileRemove, 0),
        (ProfileSection.PropRemove, 1),
        (ProfileSection.NatureQuery, 0),
        (ProfileSection.Prefetch, 0),
        (ProfileSection.Respawn, 0),
    };

    private static string Label(ProfileSection s) => s switch
    {
        ProfileSection.CharacterGround => "ground (characters)",
        ProfileSection.BroadPhase => "broadphase",
        ProfileSection.GroundRelease => "ground released",
        ProfileSection.Logins => "logins, respawns",
        ProfileSection.AfterStep => "after the step",
        ProfileSection.ReplicateSnapshots => "snapshots",
        ProfileSection.ReplicateThings => "things",
        ProfileSection.ReplicateNature => "nature",
        ProfileSection.ReplicateBuilding => "building",
        ProfileSection.TileWait => "[tile built/waited]",
        ProfileSection.TileAdd => "[tile added]",
        ProfileSection.PropAdd => "[props added]",
        ProfileSection.TileRemove => "[tile removed]",
        ProfileSection.PropRemove => "[props removed]",
        ProfileSection.NatureQuery => "[nature looked up]",
        ProfileSection.Prefetch => "[ground ahead]",
        ProfileSection.Respawn => "[respawns]",
        _ => s.ToString().ToLowerInvariant(),
    };

    private static double Ms(long stopwatchTicks) => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

    /// <summary>A section being timed (<c>using</c>).</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly TickProfile? _profile;
        private readonly ProfileSection _section;
        private readonly long _start;
        private readonly long _bytes;

        internal Scope(TickProfile? profile, ProfileSection section)
        {
            _profile = profile;
            _section = section;
            _bytes = profile == null ? 0 : GC.GetAllocatedBytesForCurrentThread();
            _start = profile == null ? 0 : Stopwatch.GetTimestamp();
        }

        public void Dispose() => _profile?.Add(_section, Stopwatch.GetTimestamp() - _start, GC.GetAllocatedBytesForCurrentThread() - _bytes);
    }
}

/// <summary>Timing with a profile that may be null (the client's prediction has none).</summary>
public static class TickProfileExtensions
{
    public static TickProfile.Scope Time(this TickProfile? profile, ProfileSection section) => new(profile, section);
}
