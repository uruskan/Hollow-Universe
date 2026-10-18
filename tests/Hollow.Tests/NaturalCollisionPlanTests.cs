using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class NaturalCollisionPlanTests
{
    [Test]
    public static void BoundedScatterPreservesEveryLegacyValueAndAcceptedOrderAcrossFacesAndRadii()
    {
        foreach (double radius in new[] { .4, 250, 1354.055000514614 })
        {
            var field = new Field { Radius = radius };
            foreach (TileKey tile in Tiles())
                foreach (bool treesOnly in new[] { false, true })
                {
                    List<ScatterItem> expected = Scatter.ForTile(field, tile, treesOnly);
                    List<ScatterItem> actual = Scatter.ForTile(field, tile, Generation(field.Plan, treesOnly), treesOnly: treesOnly);
                    Assert.True(expected.Count > 0, "dense fixture exercises accepted objects");
                    Assert.True(expected.SequenceEqual(actual), "bounded path preserves exact IDs, directions, height, kind, variant, yaw, scale and order");
                }
        }
    }

    [Test]
    public static void BoundedScatterRefusesBeforeDensityAndCancelsAtTheFinalAcceptedCandidate()
    {
        var field = new Field(); TileKey tile = Tiles()[0];
        ScatterGenerationLimits full = Generation(field.Plan);
        Reject<FormationCapacityException>(() => Scatter.ForTile(field, tile, full with { MaxCandidateVisits = full.MaxCandidateVisits - 1 }));
        Reject<FormationCapacityException>(() => Scatter.ForTile(field, tile, full with { MaxEstimatedBytes = full.MaxEstimatedBytes - 1 }));
        Assert.Equal(0, field.Samples, "both reservations refuse before density sampling");
        Reject<FormationCapacityException>(() => Scatter.ForTile(field, tile, full with { MaxItems = 0 }));
        using var cancellation = new CancellationTokenSource();
        field.Heights = 0; field.OnHeight = () => { if (field.Heights == full.MaxCandidateVisits) cancellation.Cancel(); };
        Reject<OperationCanceledException>(() => Scatter.ForTile(field, tile, full, cancellation.Token));
        Assert.Equal(full.MaxCandidateVisits, (long)field.Heights, "cancellation occurs during the final accepted candidate, before list publication");
    }

    [Test]
    public static void NaturalPlanSnapshotsExactLegacyShapesAndBypassesTheRetainedCache()
    {
        var field = new Field(); var provider = Create(field);
        TileKey[] tiles = Tiles();
        var expected = new List<NaturalObject>();
        foreach (TileKey tile in tiles.OrderBy(tile => tile.Packed))
            expected.AddRange(provider.InTile(tile).Where(item => ContentFixture.Content.Natural(item.Kind)!.IsSolid));
        NaturalObjectCacheDiagnostics cache = provider.CacheDiagnostics; int samples = field.Samples;
        NaturalCollisionPlan plan = provider.PrepareCollision(tiles.Reverse().ToArray(), Limits());
        Assert.True(plan.Count > 0, "fixture contains solid shapes");
        Assert.True(plan.Tiles.ToArray().SequenceEqual(tiles.OrderBy(tile => tile.Packed)), "complete root set is canonically ordered");
        Assert.True(plan.Entries.ToArray().Select(entry => entry.Object).SequenceEqual(expected), "semantic IDs and original accepted order are unchanged");
        Assert.Equal(cache, provider.CacheDiagnostics, "planning neither consults nor fills the retained LRU");
        Assert.True(field.Samples > samples, "warm cache is regenerated under explicit request allowances");
        bool capsule = false, sphere = false;
        foreach (NaturalCollisionEntry entry in plan.Entries)
        {
            NaturalKind kind = ContentFixture.Content.Natural(entry.Object.Kind)!;
            PropShape expectedShape;
            if (kind.Class == NaturalClass.Tree)
            {
                (Vector3 a, Vector3 b, float r) = NaturalShapes.Trunk(entry.Object, kind);
                expectedShape = PropShape.Capsule(a, b, r, entry.Object.Up); capsule = true;
            }
            else
            {
                (Vector3 centre, float r) = NaturalShapes.Rock(entry.Object, kind);
                expectedShape = PropShape.Sphere(centre, r); sphere = true;
            }
            Assert.Equal(expectedShape, entry.Shape.ToPropShape(), "value-only shape exactly matches the existing native input");
        }
        Assert.True(capsule && sphere, "fixture covers both supported native primitives");
        NaturalCollisionEntry first = plan.Entries[0];
        NaturalCollisionEntry[] copy = plan.Entries.ToArray(); copy[0] = default;
        tiles[0] = new TileKey(5, 2, 1, 1);
        Assert.Equal(first, plan.Entries[0], "public copies and caller arrays cannot mutate the immutable plan");
    }

    [Test]
    public static void NaturalPlanAdmitsTheWholeTileRequestAndEveryReservationBeforeSampling()
    {
        var field = new Field(); var provider = Create(field); TileKey[] tiles = Tiles();
        NaturalCollisionLimits full = Limits(); NaturalCollisionReservation expected = NaturalCollisionPlan.Estimate(field.Plan, tiles.Length, full);
        foreach (NaturalCollisionLimits refused in new[]
        {
            full with { MaxTiles = tiles.Length - 1 }, full with { MaxCandidateVisits = expected.CandidateVisits - 1 },
            full with { MaxEstimatedResidentBytes = expected.ResidentBytes - 1 },
            full with { MaxEstimatedScratchBytes = expected.ScratchBytes - 1 },
            full with { MaxEstimatedSnapshotBytes = expected.SnapshotBytes - 1 },
        }) Reject<FormationCapacityException>(() => provider.PrepareCollision(tiles, refused));
        Assert.Equal(0, field.Samples, "no complete-request admission failure starts DensityField");
        Assert.Equal(0, provider.CachedTiles, "capacity refusal retains no source cache entry");
        NaturalCollisionPlan plan = provider.PrepareCollision(tiles, full);
        Assert.Equal(expected, plan.Reservation, "published plan retains its complete preflight allowances");
        Assert.True(plan.Reservation.OutputCapacity >= plan.Count, "unused output slots remain covered by the retained estimate");
    }

    [Test]
    public static void NaturalPlanRejectsDuplicatesWrongLevelsAndAliasingIdDomainsBeforeSampling()
    {
        var field = new Field(); var provider = Create(field); TileKey tile = Tiles()[0];
        Reject<FormationValidationException>(() => provider.PrepareCollision(new[] { tile, tile }, Limits()));
        Reject<FormationValidationException>(() => provider.PrepareCollision(new[] { tile.Parent }, Limits()));
        Assert.Equal(0, field.Samples, "invalid complete root sets do not sample");
        foreach (ScatterPlan invalid in new[] { field.Plan with { Level = 17 }, field.Plan with { TreeCells = 363 },
            field.Plan with { RockCells = 0 }, field.Plan with { BerryCells = 363 } })
        {
            var source = new Field { Plan = invalid }; var other = Create(source);
            Reject<FormationValidationException>(() => other.PrepareCollision(new[] { new TileKey(0, invalid.Level, 0, 0) }, Limits()));
            Assert.Equal(0, source.Samples, "semantic ID domain is validated before any source generation");
        }
    }

    [Test]
    public static void NaturalPlanNeverPublishesPartialCapacityInvalidOrCancelledResults()
    {
        TileKey[] tiles = Tiles(); var field = new Field(); var provider = Create(field);
        Reject<FormationCapacityException>(() => provider.PrepareCollision(tiles, Limits() with { MaxSolids = 1 }));
        Assert.Equal(0, provider.CachedTiles, "partial generation never leaks into the source cache");
        field.InvalidHeight = true;
        Reject<FormationValidationException>(() => provider.PrepareCollision(tiles, Limits()));
        field.InvalidHeight = false;
        using var cancellation = new CancellationTokenSource();
        field.Samples = 0; field.OnSample = () => { if (field.Samples == 121) cancellation.Cancel(); };
        Reject<OperationCanceledException>(() => provider.PrepareCollision(tiles, Limits(), cancellation.Token));
        Assert.Equal(121, field.Samples, "cancellation after the final density sample cannot publish a prefix");
        field.OnSample = null;
        using var finalCancellation = new CancellationTokenSource();
        field.Heights = 0; long last = Scatter.CandidateCount(field.Plan) * tiles.Length;
        field.OnHeight = () => { if (field.Heights == last) finalCancellation.Cancel(); };
        Reject<OperationCanceledException>(() => provider.PrepareCollision(tiles, Limits(), finalCancellation.Token));
        Assert.Equal(last, (long)field.Heights, "final complete-request accepted candidate is cancellation checked");
        field.OnHeight = null;
        ScatterPlan original = field.Plan;
        field.OnSample = () => field.Plan = original with { BushCells = 3 };
        Reject<FormationValidationException>(() => provider.PrepareCollision(new[] { tiles[0] }, Limits()));
    }

    [Test]
    public static void NaturalDepletionSnapshotsBindProviderStateAndPresenceWithoutReadingLaterMutation()
    {
        var provider = Create(new Field()); var state = new NaturalState(ContentFixture.Content, provider);
        NaturalCollisionPlan plan = provider.PrepareCollision(Tiles(), Limits());
        NaturalCollisionSnapshot first = provider.CaptureDepletion(plan, state);
        Assert.True(provider.IsCurrent(first, state), "fresh capture matches exact provider/state and version");
        ulong id = plan.Entries[0].Object.Id;
        state.Apply(id, new NaturalOverride { Health = 5 });
        Assert.True(provider.IsCurrent(first, state), "health-only edits preserve solid collision");
        state.Apply(id, new NaturalOverride { Health = 0, Depleted = true, RespawnAtUnixMs = 20 });
        Assert.True(!provider.IsCurrent(first, state) && !first.Depleted[0], "old capture stays immutable and is explicitly stale");
        NaturalCollisionSnapshot depleted = provider.CaptureDepletion(plan, state);
        Assert.True(depleted.Depleted[0] && provider.IsCurrent(depleted, state), "new capture snapshots depletion");
        state.Respawn(20);
        Assert.True(!provider.IsCurrent(depleted, state) && depleted.Depleted[0], "regrowth invalidates but cannot rewrite a held snapshot");
        NaturalCollisionSnapshot restored = provider.CaptureDepletion(plan, state);
        var sameVersionOtherState = new NaturalState(ContentFixture.Content, provider);
        sameVersionOtherState.Apply(id, new NaturalOverride { Depleted = true }); sameVersionOtherState.Apply(id, null);
        Assert.Equal(restored.PresenceVersion, sameVersionOtherState.CollisionPresenceVersion, "fixture isolates token provenance from version");
        Assert.True(!provider.IsCurrent(restored, sameVersionOtherState), "equal version from a different state cannot authorize publication");
        var otherProvider = Create(new Field()); var otherState = new NaturalState(ContentFixture.Content, otherProvider);
        Assert.True(!otherProvider.IsCurrent(restored, otherState), "same generated world data does not forge provider ownership");
        Reject<FormationValidationException>(() => otherProvider.CaptureDepletion(plan, otherState));
        Reject<FormationValidationException>(() => provider.CaptureDepletion(plan, otherState));
        Reject<OperationCanceledException>(() => provider.CaptureDepletion(plan, state, new CancellationToken(true)));
    }

    [Test]
    public static void NaturalPresenceClockCannotWrapOrBeBypassedThroughMutableOverrideAccess()
    {
        var provider = Create(new Field()); var state = new NaturalState(ContentFixture.Content, provider);
        IReadOnlyDictionary<ulong, NaturalOverride> wrapper = state.Overrides;
        Assert.True(ReferenceEquals(wrapper, state.Overrides), "override wrapper is cached");
        Reject<NotSupportedException>(() => ((IDictionary<ulong, NaturalOverride>)wrapper).Add(1, new NaturalOverride { Depleted = true }));
        Assert.Equal(0, state.Count, "cast mutation cannot bypass presence tracking");
        state.Apply(1, new NaturalOverride { Depleted = true, RespawnAtUnixMs = 1 });
        state.Apply(2, new NaturalOverride { Depleted = true, RespawnAtUnixMs = 1 });
        typeof(NaturalState).GetProperty(nameof(NaturalState.CollisionPresenceVersion))!.SetValue(state, ulong.MaxValue - 1);
        uint revision = state.Revision;
        Reject<OverflowException>(() => state.Respawn(1));
        Assert.True(state.IsDepleted(1) && state.IsDepleted(2), "overflow refuses the entire respawn batch before presence mutation");
        Assert.Equal(revision, state.Revision, "failed overflow does not change existing protocol revision semantics");
        state.Apply(1, null);
        Assert.Equal(ulong.MaxValue, state.CollisionPresenceVersion, "last distinct presence version remains representable");
        Reject<OverflowException>(() => state.Apply(2, null));
        Assert.True(state.IsDepleted(2), "an exhausted presence clock cannot alias a prior snapshot");
        state.Apply(2, new NaturalOverride { Health = 5, Depleted = true });
        Assert.Equal(ulong.MaxValue, state.CollisionPresenceVersion, "health-only edits need no new collision version");
    }

    [Test]
    public static void EmptyNaturalPlansAndSnapshotsStillRetainTheirDeclaredOutputCapacity()
    {
        var field = new Field { Dense = false }; var provider = Create(field);
        NaturalCollisionPlan plan = provider.PrepareCollision(Tiles(), Limits());
        Assert.Equal(0, plan.Count, "empty density returns a complete empty shape set");
        Assert.True(plan.Reservation.OutputCapacity > 0 && plan.Reservation.ResidentBytes > 4096,
            "empty result does not silently credit its allocated output buffer");
        var state = new NaturalState(ContentFixture.Content, provider);
        Assert.True(provider.IsCurrent(provider.CaptureDepletion(plan, state), state), "empty capture retains exact provenance");
        int samples = field.Samples;
        NaturalCollisionPlan noTiles = provider.PrepareCollision(Array.Empty<TileKey>(), Limits());
        Assert.True(noTiles.Count == 0 && noTiles.Tiles.Length == 0, "empty requested set is complete");
        Assert.Equal(samples, field.Samples, "empty requested set performs no source work");
    }

    private static NaturalCollisionLimits Limits() => new()
    {
        MaxTiles = 16, MaxCandidateVisits = 10000, MaxSolids = 512,
        MaxEstimatedResidentBytes = 1024 * 1024, MaxEstimatedScratchBytes = 1024 * 1024, MaxEstimatedSnapshotBytes = 1024 * 1024,
    };
    private static ScatterGenerationLimits Generation(ScatterPlan plan, bool treesOnly = false)
    {
        int count = checked((int)Scatter.CandidateCount(plan, treesOnly));
        return new() { MaxCandidateVisits = count, MaxItems = count, MaxEstimatedBytes = Scatter.EstimatedBuildBytes(count) };
    }
    private static ScatterNaturalObjects Create(Field field) => new(field, ZoneFrame.World, ContentFixture.Content);
    private static TileKey[] Tiles() => new[] { new TileKey(0, 2, 0, 0), new TileKey(0, 2, 3, 3),
        new TileKey(1, 2, 0, 0), new TileKey(2, 2, 3, 0), new TileKey(3, 2, 0, 3), new TileKey(4, 2, 3, 3), new TileKey(5, 2, 0, 0) };
    private static void Reject<T>(Action action) where T : Exception => GroundCollisionPlanTests.Reject<T>(action);
    private sealed class Field : IScatterField
    {
        public double Radius { get; init; } = 1200;
        public ulong ScatterSeed => 107;
        public ScatterPlan Plan = new(2, 2, 2, 2, 2, 2, 2);
        public ScatterPlan ScatterPlan => Plan;
        public bool Dense { get; init; } = true;
        public bool InvalidHeight;
        public int Samples, Heights;
        public Action? OnSample, OnHeight;
        public SurfaceSample Sample(Vector3D direction) { Samples++; OnSample?.Invoke(); return default; }
        public double HeightAt(Vector3D direction) { Heights++; OnHeight?.Invoke(); return InvalidHeight ? double.NaN : 0; }
        public ScatterDensity Densities(Vector3D direction, in SurfaceSample sample, double slope) =>
            new(new VegetationSample(Dense ? 1e12 : 0, Dense ? 1e12 : 0, Dense ? 1e12 : 0, Dense ? 1e12 : 0, 0,
                Dense ? 1e12 : 0, Dense ? 1e12 : 0), new double[] { 1, 0, 0, 0 }, Dense ? 1e12 : 0, 0);
    }
}
