using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationCacheLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Test]
    public static void EmptyFormationsUseABoundedBudgetAndRecentEntriesSurviveEviction()
    {
        var cliffs = new CliffPlacements(new FlatSurface(), 7);
        TileKey[] tiles = Tiles(cliffs.Level).Take(CliffPlacements.CacheCapacity * 3).ToArray();
        for (int i = 0; i < CliffPlacements.CacheCapacity; i++)
            Assert.Equal(0, cliffs.FormationAt(tiles[i]).Count, "flat ground remains an empty formation");
        CheckBound(cliffs);
        Assert.Equal(CliffPlacements.CacheCapacity, cliffs.CacheDiagnostics.EmptyEntries, "negative results consume the same entry budget");
        long builds = cliffs.CacheDiagnostics.Builds;
        _ = cliffs.FormationAt(tiles[0]); // Keep the oldest entry hot; the next oldest should be evicted instead.
        _ = cliffs.FormationAt(tiles[CliffPlacements.CacheCapacity]);
        _ = cliffs.FormationAt(tiles[0]);
        Assert.Equal(builds + 1, cliffs.CacheDiagnostics.Builds, "recently used negative entry survives capacity pressure");
        _ = cliffs.FormationAt(tiles[1]);
        Assert.Equal(builds + 2, cliffs.CacheDiagnostics.Builds, "the least recently used negative entry is recomputed on revisit");

        foreach (TileKey tile in tiles)
        {
            Assert.Equal(0, cliffs.FormationAt(tile).Count, "cold and warm revisits retain the same output");
            CheckBound(cliffs);
        }
        CliffFormationCacheDiagnostics full = cliffs.CacheDiagnostics;
        Assert.Equal(CliffPlacements.CacheCapacity, full.Entries, "traveling through many cells cannot grow retained entries");
        Assert.True(full.Evictions > CliffPlacements.CacheCapacity, "the scenario actually crosses the cache budget repeatedly");
        Assert.Equal(0L, full.RetainedPayloadBytes, "empty results retain no placement-array elements");
        Assert.Equal(0, cliffs.FormationAt(tiles[0].Parent).Count, "a different tile level is refused");
        Assert.Equal(full, cliffs.CacheDiagnostics, "wrong-level requests do not consume entries or launch work");
    }

    [Test]
    public static void ConcurrentReadersCoalesceWhileTheEntryIsResident()
    {
        using var surface = new ControlledFlatSurface();
        var cliffs = new CliffPlacements(surface, 11);
        TileKey tile = Candidate(11, cliffs.Level);
        var readers = new Thread[8];
        var results = new IReadOnlyList<CliffPlacement>?[readers.Length];
        var errors = new Exception?[readers.Length];
        for (int i = 0; i < readers.Length; i++)
        {
            int index = i;
            readers[i] = new Thread(() =>
            {
                try { results[index] = cliffs.FormationAt(tile); }
                catch (Exception error) { errors[index] = error; }
            }) { IsBackground = true, Name = "Formation cache shared reader" };
        }
        try
        {
            readers[0].Start();
            Assert.True(surface.Entered.Wait(Bound), "the first real formation build reaches its controlled source");
            for (int i = 1; i < readers.Length; i++) readers[i].Start();
            Assert.True(SpinWait.SpinUntil(() => cliffs.CacheDiagnostics.Hits == readers.Length - 1, Bound),
                "all overlapping readers find the same resident entry");
            CliffFormationCacheDiagnostics pending = cliffs.CacheDiagnostics;
            Assert.Equal(1, pending.Entries, "one pending entry is retained");
            Assert.Equal(0, pending.CompletedEntries, "the source is still blocked");
            Assert.Equal(1L, pending.Builds, "one actual build serves all resident readers");
            surface.Release.Set();
            foreach (Thread reader in readers) Assert.True(reader.Join(Bound), "every admitted reader completes");
            Assert.True(errors.All(error => error == null), "all shared readers succeed");
            Assert.True(results.All(result => result != null && result.Count == 0), "every reader gets the same negative formation");
            Assert.Equal(1L, cliffs.CacheDiagnostics.Builds, "coalescing does not launch a late duplicate");
            CheckBound(cliffs);
        }
        finally
        {
            surface.Release.Set();
            foreach (Thread reader in readers)
                if ((reader.ThreadState & ThreadState.Unstarted) == 0)
                    Assert.True(reader.Join(Bound), "the fixture never leaves a blocked cache reader");
        }
    }

    [Test]
    public static void EvictedInflightFailureCannotRemoveItsSuccessfulReplacement()
    {
        using var surface = new ControlledFlatSurface { FailBlocked = true };
        var cliffs = new CliffPlacements(surface, 13);
        TileKey tile = Candidate(13, cliffs.Level);
        Exception? failure = null;
        var first = new Thread(() =>
        {
            try { _ = cliffs.FormationAt(tile); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true, Name = "Formation cache evicted reader" };
        first.Start();
        try
        {
            Assert.True(surface.Entered.Wait(Bound), "the old entry is still building");
            foreach (TileKey other in Tiles(cliffs.Level).Where(other => other != tile).Take(CliffPlacements.CacheCapacity))
            {
                _ = cliffs.FormationAt(other);
                CheckBound(cliffs);
            }
            Assert.Equal(0, cliffs.FormationAt(tile).Count, "an evicted running key may be rebuilt without retaining an unbounded in-flight table");
            long builds = cliffs.CacheDiagnostics.Builds;
            Assert.Equal((long)CliffPlacements.CacheCapacity + 2, builds, "both old and replacement builds are visible in diagnostics");
            surface.Release.Set();
            Assert.True(first.Join(Bound), "the old reader is allowed to finish after eviction");
            Assert.True(ReferenceEquals(surface.Failure, failure), "the old reader receives its own source failure");
            Assert.Equal(0, cliffs.FormationAt(tile).Count, "the replacement entry remains usable after the old failure");
            Assert.Equal(builds, cliffs.CacheDiagnostics.Builds, "failure cleanup did not erase the newer resident entry");
            CheckBound(cliffs);
        }
        finally
        {
            surface.Release.Set();
            Assert.True(first.Join(Bound), "the fixture retires its evicted reader");
        }
    }

    [Test]
    public static void FailedResidentBuildIsRemovedAndRetryable()
    {
        using var surface = new ControlledFlatSurface { FailBlocked = true };
        surface.Release.Set();
        var cliffs = new CliffPlacements(surface, 17);
        TileKey tile = Candidate(17, cliffs.Level);
        Exception? failure = null;
        try { _ = cliffs.FormationAt(tile); } catch (Exception error) { failure = error; }
        Assert.True(ReferenceEquals(surface.Failure, failure), "the build's original failure is preserved");
        Assert.Equal(0, cliffs.CacheDiagnostics.Entries, "the failed Lazy does not permanently poison a cell");
        Assert.Equal(0, cliffs.FormationAt(tile).Count, "the same cell can be retried successfully");
        Assert.Equal(2L, cliffs.CacheDiagnostics.Builds, "retry is an actual new build");
        Assert.Equal(1, cliffs.CacheDiagnostics.EmptyEntries, "the successful retry is now cached normally");
    }

    [Test]
    public static void EvictionPreservesFormationArraysAndQueriesAcrossACubeFaceSeam()
    {
        var surface = new SeamSlope();
        var cliffs = new CliffPlacements(surface, 1);
        TileKey left = TileKey.At(surface.Up + Vector3D.UnitX * 1e-5, cliffs.Level);
        TileKey right = TileKey.At(surface.Up - Vector3D.UnitX * 1e-5, cliffs.Level);
        Assert.True(left.Face != right.Face, "the fixture straddles a real cube-face boundary");
        ulong seed = 1;
        IReadOnlyList<CliffPlacement> held = cliffs.FormationAt(left);
        for (; held.Count == 0 && seed < 40;)
        {
            cliffs = new CliffPlacements(surface, ++seed);
            held = cliffs.FormationAt(left);
        }
        Assert.True(held.Count > 0, "the seam contains a fitted formation, not only negative cache entries");
        CliffPlacement[] expected = held.ToArray();
        IReadOnlyList<CliffPlacement> across = cliffs.FormationAt(right);
        CliffPlacement[] expectedAcross = across.ToArray();
        Vector3D centre = surface.Up * (surface.Radius + SeamSlope.BaseHeight);
        CliffPlacement[] nearby = cliffs.Near(centre, 35).ToArray();
        Assert.True(nearby.Length > 0, "nearby seam discovery includes actual formation members");
        CheckBound(cliffs);
        Assert.True(cliffs.CacheDiagnostics.RetainedPayloadBytes > 0, "diagnostics account for completed placement elements");

        // The far hemisphere is flat and cheap. It evicts the seam without a heavy full-world geology scan.
        TileKey[] pressure = Tiles(cliffs.Level).Where(tile => Vector3D.Dot(tile.Centre, surface.Up) < -0.2)
            .Take(CliffPlacements.CacheCapacity + 1).ToArray();
        Assert.Equal(CliffPlacements.CacheCapacity + 1, pressure.Length, "enough unrelated cells force eviction");
        foreach (TileKey tile in pressure) _ = cliffs.FormationAt(tile);
        Assert.True(held.SequenceEqual(expected), "a reader's returned array remains valid after cache eviction");
        Assert.True(across.SequenceEqual(expectedAcross), "the neighboring reader's array also remains valid");
        // Deliberately reverse the original query order after eviction.
        Assert.True(cliffs.FormationAt(right).SequenceEqual(expectedAcross), "the neighboring cell regenerates the same transforms");
        Assert.True(cliffs.FormationAt(left).SequenceEqual(expected), "a revisited formation has the same identities and transforms");
        Assert.True(cliffs.Near(centre, 35).SequenceEqual(nearby), "seam discovery retains output and order after eviction");
        var independent = new CliffPlacements(surface, seed);
        Assert.True(independent.FormationAt(left).SequenceEqual(expected), "cold independent generation agrees with warm and evicted queries");
        CheckBound(cliffs);
    }

    private static void CheckBound(CliffPlacements cliffs)
    {
        CliffFormationCacheDiagnostics cache = cliffs.CacheDiagnostics;
        Assert.True(cache.Entries <= CliffPlacements.CacheCapacity, "retained entries include pending and negative results within one budget");
        Assert.True(cache.CompletedEntries <= cache.Entries && cache.EmptyEntries <= cache.CompletedEntries,
            "materialized and negative entries are subsets of retained entries");
        Assert.True(cache.RetainedParts <= CliffPlacements.CacheCapacity * CliffPlacements.MaximumParts,
            "completed arrays stay within the documented part budget");
        Assert.True(cache.RetainedPayloadBytes <= (long)CliffPlacements.CacheCapacity * CliffPlacements.MaximumParts * Unsafe.SizeOf<CliffPlacement>(),
            "retained placement payload stays within its declared bound");
    }

    private static TileKey Candidate(ulong seed, int level) => Tiles(level).First(tile => new Rng(Hash64.Of(seed, 0x434C4946, tile.Packed)).Chance(0.8));
    private static IEnumerable<TileKey> Tiles(int level)
    {
        int count = 1 << level;
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < count; y++)
                for (int x = 0; x < count; x++)
                    yield return new TileKey(face, level, x, y);
    }
    private sealed class FlatSurface : ISurface
    {
        public double Radius => 1200;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => 0;
    }
    private sealed class ControlledFlatSurface : ISurface, IDisposable
    {
        private int _entered;
        public double Radius => 1200;
        public double SeaRadius => 0;
        public bool FailBlocked { get; init; }
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public InvalidOperationException Failure { get; } = new("Controlled formation-source failure.");
        public double HeightAt(Vector3D direction)
        {
            if (Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                Entered.Set();
                if (!Release.Wait(Bound)) throw new TimeoutException("The formation fixture did not release its source.");
                if (FailBlocked) throw Failure;
            }
            return 0;
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
    private sealed class SeamSlope : ISurface
    {
        public const double BaseHeight = 300;
        public Vector3D Up { get; } = (Vector3D.UnitX + Vector3D.UnitZ).Normalized;
        public double Radius => 1200;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction)
        {
            double along = Vector3D.Dot(direction, Up);
            return along > 0.8 ? BaseHeight - 1.1 * (Radius + BaseHeight) * direction.Y / along : BaseHeight;
        }
    }
}
