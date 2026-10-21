using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace Hollow.Tests;

public static class StandInNaturalCacheLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Test]
    public static void EmptyTilesUseTheSameBoundedLruBudget()
    {
        var nature = Create(new FlatSurface { Underwater = true });
        TileKey[] tiles = Tiles(nature.Level).Take(StandInNaturalObjects.MaxTiles * 3).ToArray();
        for (int i = 0; i < StandInNaturalObjects.MaxTiles; i++)
            Assert.Equal(0, nature.InTile(tiles[i]).Count, "underwater tile is empty");
        Assert.Equal(StandInNaturalObjects.MaxTiles, nature.CacheDiagnostics.EmptyEntries, "negative results consume capacity");
        int builds = nature.Generated;
        _ = nature.InTile(tiles[0]);
        _ = nature.InTile(tiles[StandInNaturalObjects.MaxTiles]);
        _ = nature.InTile(tiles[0]);
        Assert.Equal(builds + 1, nature.Generated, "recently used empty entry survives pressure");
        _ = nature.InTile(tiles[1]);
        Assert.Equal(builds + 2, nature.Generated, "least recently used entry is rebuilt");
        foreach (TileKey tile in tiles)
        {
            Assert.Equal(0, nature.InTile(tile).Count, "empty result stays deterministic after eviction");
            CheckBound(nature);
        }
        NaturalObjectCacheDiagnostics full = nature.CacheDiagnostics;
        Assert.Equal(StandInNaturalObjects.MaxTiles, full.Entries, "long travel retains a finite cache");
        Assert.True(full.Evictions > StandInNaturalObjects.MaxTiles, "fixture repeatedly crosses capacity");
        Assert.Equal(0L, full.RetainedPayloadBytes, "empty arrays have no object payload");
        Assert.Equal(0, nature.InTile(tiles[0].Parent).Count, "wrong scatter level is refused");
        Assert.Equal(full, nature.CacheDiagnostics, "wrong level does not retain or build entries");
    }

    [Test]
    public static void ResidentParallelRequestsCoalesce()
    {
        using var surface = new ControlledSurface();
        var nature = Create(surface);
        TileKey tile = Tiles(nature.Level).First();
        var readers = new Thread[8];
        var results = new IReadOnlyList<NaturalObject>?[readers.Length];
        var errors = new Exception?[readers.Length];
        for (int i = 0; i < readers.Length; i++)
        {
            int index = i;
            readers[i] = new Thread(() =>
            {
                try { results[index] = nature.InTile(tile); }
                catch (Exception error) { errors[index] = error; }
            }) { IsBackground = true, Name = "Stand-in cache shared reader" };
        }
        try
        {
            readers[0].Start();
            Assert.True(surface.Entered.Wait(Bound), "first reader starts real surface generation");
            for (int i = 1; i < readers.Length; i++) readers[i].Start();
            Assert.True(SpinWait.SpinUntil(() => nature.CacheDiagnostics.Hits == readers.Length - 1, Bound), "all readers reach the same resident entry");
            NaturalObjectCacheDiagnostics pending = nature.CacheDiagnostics;
            Assert.Equal(1, pending.Entries, "one pending entry serves all readers");
            Assert.Equal(1, pending.PendingEntries, "pending work is part of the entry budget");
            Assert.Equal(1, pending.Builds, "source factory executes once");
            Assert.Equal(1, pending.ActiveBuilds, "one executing build is visible");
            surface.Release.Set();
            foreach (Thread reader in readers) Assert.True(reader.Join(Bound), "every reader completes");
            Assert.True(errors.All(error => error == null), "all readers succeed");
            Assert.True(results[0]!.Count > 0 && results.All(result => ReferenceEquals(result, results[0])), "all readers share the nonempty array");
            Assert.Equal(0, nature.CacheDiagnostics.ActiveBuilds, "completed work retires its active count");
            CheckBound(nature);
        }
        finally
        {
            surface.Release.Set();
            foreach (Thread reader in readers)
                if ((reader.ThreadState & ThreadState.Unstarted) == 0)
                    Assert.True(reader.Join(Bound), "fixture never leaves a blocked reader");
        }
    }

    [Test]
    public static void EvictedInflightFailureDoesNotEraseItsReplacement()
    {
        using var surface = new ControlledSurface { FailFirst = true };
        var nature = Create(surface);
        TileKey tile = Tiles(nature.Level).First();
        Exception? failure = null;
        var first = new Thread(() =>
        {
            try { _ = nature.InTile(tile); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true, Name = "Stand-in cache evicted reader" };
        first.Start();
        try
        {
            Assert.True(surface.Entered.Wait(Bound), "old entry is still executing");
            foreach (TileKey other in Tiles(nature.Level).Skip(1).Take(StandInNaturalObjects.MaxTiles))
            {
                _ = nature.InTile(other);
                CheckBound(nature);
            }
            Assert.Equal(0, nature.CacheDiagnostics.PendingEntries, "evicted work has no second retained pending table");
            Assert.Equal(1, nature.CacheDiagnostics.ActiveBuilds, "diagnostics still report caller-owned execution");
            IReadOnlyList<NaturalObject> replacement = nature.InTile(tile);
            Assert.True(replacement.Count > 0, "replacement has actual objects");
            int builds = nature.Generated;
            surface.Release.Set();
            Assert.True(first.Join(Bound), "old reader completes with its failure");
            Assert.True(ReferenceEquals(surface.Failure, failure), "original failure reaches its reader");
            Assert.True(ReferenceEquals(replacement, nature.InTile(tile)), "late fault did not erase successful replacement");
            Assert.Equal(builds, nature.Generated, "replacement was not generated again");
            Assert.Equal(0, nature.CacheDiagnostics.ActiveBuilds, "failed work retires its active count");
            CheckBound(nature);
        }
        finally
        {
            surface.Release.Set();
            Assert.True(first.Join(Bound), "fixture releases the old reader");
        }
    }

    [Test]
    public static void FailedResidentGenerationCanBeRetried()
    {
        using var surface = new ControlledSurface { FailFirst = true };
        surface.Release.Set();
        var nature = Create(surface);
        TileKey tile = Tiles(nature.Level).First();
        Exception? failure = null;
        try { _ = nature.InTile(tile); } catch (Exception error) { failure = error; }
        Assert.True(ReferenceEquals(surface.Failure, failure), "source failure is preserved");
        Assert.Equal(0, nature.CachedTiles, "failed Lazy is removed");
        Assert.Equal(0, nature.CacheDiagnostics.ActiveBuilds, "failed work is not executing anymore");
        Assert.True(nature.InTile(tile).Count > 0, "fresh retry succeeds");
        Assert.Equal(2, nature.Generated, "retry is a new source generation");
        CheckBound(nature);
    }

    [Test]
    public static void EvictionPreservesIdsTransformsOrderingAndQueries()
    {
        var surface = new FlatSurface();
        var nature = Create(surface);
        TileKey tile = Tiles(nature.Level).First();
        IReadOnlyList<NaturalObject> held = nature.InTile(tile);
        NaturalObject[] expected = held.ToArray();
        Assert.True(expected.Length > 0, "fixture contains actual natural objects");
        Assert.Equal((long)expected.Length * Unsafe.SizeOf<NaturalObject>(), nature.CacheDiagnostics.RetainedPayloadBytes,
            "payload counts completed object elements");
        foreach (TileKey other in Tiles(nature.Level).Skip(1).Take(StandInNaturalObjects.MaxTiles)) _ = nature.InTile(other);
        Assert.True(held.SequenceEqual(expected), "eviction preserves arrays already held by readers");
        Assert.True(nature.InTile(tile).SequenceEqual(expected), "revisit preserves natural IDs, transforms and order");
        Assert.True(Create(surface).InTile(tile).SequenceEqual(expected), "independent cold generation agrees");
        Assert.True(nature.TryGet(expected[0].Id, out NaturalObject found) && found == expected[0], "ID lookup remains exact");
        var nearby = new List<NaturalObject>();
        nature.Query(expected[0].Position, 0.1f, nearby);
        Assert.True(nearby.Contains(expected[0]), "spatial query returns the original object");
        CheckBound(nature);
    }

    private static StandInNaturalObjects Create(ISurface surface) =>
        new(surface, ZoneFrame.World, ContentFixture.Content, 107, Vector3D.Zero);
    private static void CheckBound(StandInNaturalObjects nature)
    {
        NaturalObjectCacheDiagnostics cache = nature.CacheDiagnostics;
        Assert.True(cache.Entries <= StandInNaturalObjects.MaxTiles, "all retained entries share a hard capacity");
        Assert.Equal(cache.Entries, cache.CompletedEntries + cache.PendingEntries, "pending entries are counted");
        Assert.True(cache.EmptyEntries <= cache.CompletedEntries, "empty entries are completed entries");
    }
    private static IEnumerable<TileKey> Tiles(int level)
    {
        int count = 1 << level;
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < count; y++)
                for (int x = 0; x < count; x++)
                    yield return new TileKey(face, level, x, y);
    }
    private class FlatSurface : ISurface
    {
        public double Radius => 1200;
        public bool Underwater { get; init; }
        public double SeaRadius => Underwater ? 1300 : 0;
        public virtual double HeightAt(Vector3D direction) => 0;
    }
    private sealed class ControlledSurface : FlatSurface, IDisposable
    {
        private int _entered;
        public bool FailFirst { get; init; }
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public InvalidOperationException Failure { get; } = new("Controlled stand-in natural source failure.");
        public override double HeightAt(Vector3D direction)
        {
            if (Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                Entered.Set();
                if (!Release.Wait(Bound)) throw new TimeoutException("Stand-in cache fixture did not release its source.");
                if (FailFirst) throw Failure;
            }
            return 0;
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
