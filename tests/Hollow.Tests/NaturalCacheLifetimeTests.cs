using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace Hollow.Tests;

public static class NaturalCacheLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Test]
    public static void EmptyTilesConsumeTheSameBoundedLruBudget()
    {
        var natural = Create(new FlatField());
        TileKey[] tiles = Tiles(natural.Level).Take(ScatterNaturalObjects.MaxTiles * 3).ToArray();
        for (int i = 0; i < ScatterNaturalObjects.MaxTiles; i++)
            Assert.Equal(0, natural.InTile(tiles[i]).Count, "empty scatter is cached explicitly");
        CheckBound(natural);
        Assert.Equal(ScatterNaturalObjects.MaxTiles, natural.CacheDiagnostics.EmptyEntries, "negative entries consume the budget");
        int builds = natural.Generated;
        _ = natural.InTile(tiles[0]);
        _ = natural.InTile(tiles[ScatterNaturalObjects.MaxTiles]);
        _ = natural.InTile(tiles[0]);
        Assert.Equal(builds + 1, natural.Generated, "recently used empty entry survives pressure");
        _ = natural.InTile(tiles[1]);
        Assert.Equal(builds + 2, natural.Generated, "least recently used entry was evicted");
        foreach (TileKey tile in tiles)
        {
            Assert.Equal(0, natural.InTile(tile).Count, "revisiting an empty tile remains empty");
            CheckBound(natural);
        }
        NaturalObjectCacheDiagnostics full = natural.CacheDiagnostics;
        Assert.Equal(ScatterNaturalObjects.MaxTiles, full.Entries, "long travel stays at capacity");
        Assert.True(full.Evictions > ScatterNaturalObjects.MaxTiles, "fixture crosses capacity repeatedly");
        Assert.Equal(0L, full.RetainedPayloadBytes, "empty entries have no object/index elements");
        Assert.Equal(0, natural.InTile(tiles[0].Parent).Count, "other scatter levels are refused");
        Assert.Equal(full, natural.CacheDiagnostics, "wrong level neither retains entries nor launches work");
    }

    [Test]
    public static void ConcurrentResidentReadersShareOneGeneration()
    {
        using var field = new ControlledField();
        var natural = Create(field);
        TileKey tile = Tiles(natural.Level).First();
        var readers = new Thread[8];
        var results = new IReadOnlyList<NaturalObject>?[readers.Length];
        var errors = new Exception?[readers.Length];
        for (int i = 0; i < readers.Length; i++)
        {
            int index = i;
            readers[i] = new Thread(() =>
            {
                try { results[index] = natural.InTile(tile); }
                catch (Exception error) { errors[index] = error; }
            }) { IsBackground = true, Name = "Natural cache shared reader" };
        }
        try
        {
            readers[0].Start();
            Assert.True(field.Entered.Wait(Bound), "first tile reaches the controlled source");
            for (int i = 1; i < readers.Length; i++) readers[i].Start();
            Assert.True(SpinWait.SpinUntil(() => natural.CacheDiagnostics.Hits == readers.Length - 1, Bound), "resident readers coalesce");
            NaturalObjectCacheDiagnostics pending = natural.CacheDiagnostics;
            Assert.Equal(1, pending.Entries, "one entry represents all readers");
            Assert.Equal(1, pending.PendingEntries, "unfinished entry consumes capacity");
            Assert.Equal(1, pending.ActiveBuilds, "only one source generation is executing");
            Assert.Equal(1, pending.Builds, "no duplicate generation");
            Assert.True(!natural.TryGetCached(ScatterNaturalObjects.IdOf(tile, (int)ScatterKind.Fir << 24), out _),
                "cached-only lookup never waits for an unfinished tile");
            field.Release.Set();
            foreach (Thread reader in readers) Assert.True(reader.Join(Bound), "each reader finishes");
            Assert.True(errors.All(error => error == null), "shared generation succeeds");
            Assert.True(results.All(result => ReferenceEquals(result, results[0])), "coalesced readers receive the same array");
            Assert.Equal(0, natural.CacheDiagnostics.ActiveBuilds, "no source work remains");
            CheckBound(natural);
        }
        finally
        {
            field.Release.Set();
            foreach (Thread reader in readers)
                if ((reader.ThreadState & ThreadState.Unstarted) == 0)
                    Assert.True(reader.Join(Bound), "fixture retires every reader");
        }
    }

    [Test]
    public static void EvictedInflightFailureCannotEraseItsReplacement()
    {
        using var field = new ControlledField { FailFirst = true };
        var natural = Create(field);
        TileKey tile = Tiles(natural.Level).First();
        Exception? failure = null;
        var first = new Thread(() =>
        {
            try { _ = natural.InTile(tile); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true, Name = "Natural cache evicted reader" };
        first.Start();
        try
        {
            Assert.True(field.Entered.Wait(Bound), "old tile is still executing");
            foreach (TileKey other in Tiles(natural.Level).Skip(1).Take(ScatterNaturalObjects.MaxTiles))
            {
                _ = natural.InTile(other);
                CheckBound(natural);
            }
            Assert.Equal(0, natural.CacheDiagnostics.PendingEntries, "evicted work is not retained in a second pending table");
            Assert.Equal(1, natural.CacheDiagnostics.ActiveBuilds, "caller-owned evicted execution remains visible in diagnostics");
            Assert.Equal(0, natural.InTile(tile).Count, "same key can produce a successful replacement");
            int builds = natural.Generated;
            field.Release.Set();
            Assert.True(first.Join(Bound), "old reader finishes after eviction");
            Assert.True(ReferenceEquals(field.Failure, failure), "original failure reaches its reader");
            Assert.Equal(0, natural.InTile(tile).Count, "replacement survives old failure cleanup");
            Assert.Equal(builds, natural.Generated, "replacement was not erased and rebuilt");
            Assert.Equal(0, natural.CacheDiagnostics.ActiveBuilds, "failed execution retires its count");
            CheckBound(natural);
        }
        finally
        {
            field.Release.Set();
            Assert.True(first.Join(Bound), "fixture releases its failed reader");
        }
    }

    [Test]
    public static void FailedResidentGenerationIsRemovedAndRetryable()
    {
        using var field = new ControlledField { FailFirst = true };
        field.Release.Set();
        var natural = Create(field);
        TileKey tile = Tiles(natural.Level).First();
        Exception? failure = null;
        try { _ = natural.InTile(tile); } catch (Exception error) { failure = error; }
        Assert.True(ReferenceEquals(field.Failure, failure), "source failure is preserved");
        Assert.Equal(0, natural.CachedTiles, "failed Lazy does not poison the tile");
        Assert.Equal(0, natural.CacheDiagnostics.ActiveBuilds, "failed work retires its active count");
        Assert.Equal(0, natural.InTile(tile).Count, "retry succeeds");
        Assert.Equal(2, natural.Generated, "retry performs new generation");
        Assert.Equal(1, natural.CacheDiagnostics.EmptyEntries, "successful negative result is now cached");
    }

    [Test]
    public static void EvictionPreservesNaturalIdsTransformsAndLookupOrder()
    {
        var field = new FlatField { Dense = true };
        var natural = Create(field);
        TileKey tile = Tiles(natural.Level).First();
        IReadOnlyList<NaturalObject> held = natural.InTile(tile);
        NaturalObject[] expected = held.ToArray();
        Assert.True(expected.Length > 0, "fixture contains actual harvestable objects");
        Assert.True(natural.TryGetCached(expected[0].Id, out NaturalObject cached) && cached == expected[0], "cached lookup agrees with scatter output");
        Assert.Equal((long)expected.Length * (Unsafe.SizeOf<NaturalObject>() + sizeof(ulong) + sizeof(int)),
            natural.CacheDiagnostics.RetainedPayloadBytes, "payload reports object and logical index elements");
        foreach (TileKey other in Tiles(natural.Level).Skip(1).Take(ScatterNaturalObjects.MaxTiles)) _ = natural.InTile(other);
        Assert.True(!natural.TryGetCached(expected[0].Id, out _), "cold lookup does not regenerate evicted tile");
        Assert.True(held.SequenceEqual(expected), "eviction leaves a caller's array intact");
        Assert.True(natural.InTile(tile).SequenceEqual(expected), "revisit preserves identities, transforms and order");
        Assert.True(Create(field).InTile(tile).SequenceEqual(expected), "independent cold cache agrees");
        Assert.True(natural.TryGet(expected[0].Id, out NaturalObject found) && found == expected[0], "ID lookup remains exact");
        var nearby = new List<NaturalObject>();
        natural.Query(expected[0].Position, 0.1f, nearby);
        Assert.True(nearby.Contains(expected[0]), "spatial query still finds the same object");
        CheckBound(natural);
    }

    private static ScatterNaturalObjects Create(IScatterField field) => new(field, ZoneFrame.World, ContentFixture.Content);
    private static void CheckBound(ScatterNaturalObjects natural)
    {
        NaturalObjectCacheDiagnostics cache = natural.CacheDiagnostics;
        Assert.True(cache.Entries <= ScatterNaturalObjects.MaxTiles, "all retained entries share a hard capacity");
        Assert.Equal(cache.Entries, cache.CompletedEntries + cache.PendingEntries, "pending entries are accounted for");
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
    private class FlatField : IScatterField
    {
        public double Radius => 1200;
        public ulong ScatterSeed => 107;
        public ScatterPlan ScatterPlan => new(4, 1, 1, 1, 1, 1, 1);
        public bool Dense { get; init; }
        public virtual SurfaceSample Sample(Vector3D direction) => default;
        public double HeightAt(Vector3D direction) => 0;
        public ScatterDensity Densities(Vector3D direction, in SurfaceSample sample, double slope) =>
            new(new VegetationSample(Dense ? 1 : 0, 0, 0, 0, 0), new double[] { 1, 0, 0, 0 }, 0, 0);
    }
    private sealed class ControlledField : FlatField, IDisposable
    {
        private int _entered;
        public bool FailFirst { get; init; }
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public InvalidOperationException Failure { get; } = new("Controlled natural scatter failure.");
        public override SurfaceSample Sample(Vector3D direction)
        {
            if (Interlocked.CompareExchange(ref _entered, 1, 0) == 0)
            {
                Entered.Set();
                if (!Release.Wait(Bound)) throw new TimeoutException("Natural-cache fixture did not release its source.");
                if (FailFirst) throw Failure;
            }
            return default;
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
