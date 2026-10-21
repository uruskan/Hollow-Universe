using System;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class GroundStreamingBudgetTests
{
    [Test]
    public static void GroundLedgerBackingReservationsFollowLimitsAndSurviveLastRecordRelease()
    {
        GroundStreamingLimits limits = Limits();
        long backing = GroundStreamingBudget.OwnerMetadataBytes(limits);
        var budget = new GroundStreamingBudget(limits with { MaxMetadataBytes = backing + GroundStreamingBudget.RecordBytes });
        Assert.True(budget.TryOpenOwner(out GroundStreamingOwner? owner), "one owner and one future record fit");
        Assert.Equal(backing, budget.Diagnostics.MetadataBytes, "limit-derived Work and area capacities reserved at opening");
        Assert.True(owner!.TryAdmit(Plans(1), out GroundBuildLease[] work), "one record admitted");
        work[0].Retire();
        Assert.Equal(backing, budget.Diagnostics.MetadataBytes, "last record retirement does not release retained set backing arrays");
        Assert.True(!budget.TryOpenOwner(out _), "a second owner cannot reuse retained backing storage");
        Close(owner); AssertEmpty(budget);
        Assert.True(budget.TryOpenOwner(out GroundStreamingOwner? next), "final retirement releases owner storage allowance");
        Close(next!); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerAdmitsFutureOutputAtomicallyAndUsesTheActualTriangleAbi()
    {
        SelectedGroundCollisionPlan[] plans = Plans(2);
        Assert.Equal(20, GroundStreamingEstimates.IndexedTriangleBytes, "pinned Jolt IndexedTriangle has five uint fields");
        var limits = Limits(); GroundBuildCost cost = GroundStreamingEstimates.For(plans[0], limits.Native);
        var budget = new GroundStreamingBudget(limits with { MaxNativeShapeBytes = cost.NativeShape * 2 - 1 });
        Assert.True(budget.TryOpenOwner(out GroundStreamingOwner? owner), "setup admitted");
        Assert.True(!owner!.TryAdmit(plans, out GroundBuildLease[] refused) && refused.Length == 0, "one-byte aggregate shortage refuses the entire batch");
        Assert.Equal(0, budget.Diagnostics.Tiles, "no tile prefix was admitted");
        Assert.Equal(0L, budget.Diagnostics.PlanBytes, "no plan prefix remains charged");
        Assert.True(owner.TryAdmit(plans.AsSpan(0, 1), out GroundBuildLease[] work), "one exact planned output fits");
        Assert.Equal(cost.NativeShape, budget.Diagnostics.NativeShapeBytes, "future native output is reserved before dispatch");
        Assert.Equal(1, budget.Diagnostics.Bodies, "future body slot is reserved before dispatch");
        Assert.True(!work[0].Ready, "queued work is not ready");
        Close(owner); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerCompletedQueueAndSynchronousTakeoverUseTheSameLimits()
    {
        var budget = new GroundStreamingBudget(Limits() with { MaxExecuting = 1, MaxCompletionSlots = 1 });
        budget.TryOpenOwner(out GroundStreamingOwner? owner); owner!.TryAdmit(Plans(2), out GroundBuildLease[] work);
        var first = new NativeProbe(); var second = new NativeProbe(); bool nested = true;
        Assert.True(work[0].TryRun(_ => { nested = work[1].TryRun(_ => second); return first; }), "first worker enters its admitted slot");
        Assert.True(!nested, "synchronous takeover cannot bypass a running slot");
        Assert.Equal(0, budget.Diagnostics.Executing, "returned factory releases build scratch slot");
        Assert.Equal(1, budget.Diagnostics.CompletionSlots, "completed output retains its preallocated completion slot");
        Assert.True(!work[0].Ready && !work[1].TryRun(_ => second), "unadopted output is not Ready and stops another dispatch at capacity");
        Assert.True(work[0].TryAdopt(_ => { }), "publication succeeds");
        Assert.True(work[0].Ready, "only published output is Ready");
        Assert.Equal(1, budget.Diagnostics.Outstanding, "adoption transfers the first queued record to residency");
        Assert.True(work[1].TryRun(_ => second), "adoption releases the completion slot");
        Assert.True(budget.Diagnostics.NativeShapeBytes > 0, "completed and resident outputs retain native shape charges");
        Close(owner); Assert.Equal(1, first.Disposals, "resident native owner released once");
        Assert.Equal(1, second.Disposals, "completed native owner released once"); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerRetiringRunningWorkKeepsReservationsUntilActualExit()
    {
        var budget = new GroundStreamingBudget(Limits()); budget.TryOpenOwner(out GroundStreamingOwner? owner);
        owner!.TryAdmit(Plans(1), out GroundBuildLease[] work);
        using var entered = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        var native = new NativeProbe();
        Task running = Task.Run(() => work[0].TryRun(_ =>
        { entered.Set(); Assert.True(finish.Wait(TimeSpan.FromSeconds(5)), "fixture releases blocked build"); return native; }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "worker entered build callback");
        try
        {
            owner.BeginRetirement();
            Assert.Equal(1, budget.Diagnostics.Executing, "retiring callback still owns its executing slot");
            Assert.True(budget.Diagnostics.ManagedBuildBytes > 0 && budget.Diagnostics.NativeBuildBytes > 0, "retiring callback keeps all work estimates");
            Assert.True(!owner.TryReleaseSetup(() => { }), "native system cannot retire before its worker");
            Assert.True(!work[0].Ready, "retiring epoch cannot report Ready");
        }
        finally { finish.Set(); }
        running.GetAwaiter().GetResult();
        Assert.Equal(1, native.Disposals, "late native output is disposed before releasing reservations");
        Assert.Equal(GroundWorkState.Released, work[0].State, "late work never publishes");
        Assert.True(owner.TryReleaseSetup(() => { }), "setup retires after actual worker exit"); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerNativeDisposalFailureStaysChargedForRetry()
    {
        var budget = new GroundStreamingBudget(Limits()); budget.TryOpenOwner(out GroundStreamingOwner? owner);
        owner!.TryAdmit(Plans(1), out GroundBuildLease[] work);
        var native = new NativeProbe { FailNextDispose = true };
        work[0].TryRun(_ => native); work[0].TryAdopt(_ => { });
        GroundCollisionPlanTests.Reject<AggregateException>(owner.BeginRetirement);
        Assert.True(!work[0].Ready && work[0].State == GroundWorkState.Retiring, "failed disposal never restores readiness");
        Assert.Equal(1, budget.Diagnostics.Bodies, "failed native body disposal retains its allowance");
        Assert.True(budget.Diagnostics.NativeShapeBytes > 0, "failed native shape disposal retains its allowance");
        Assert.True(!owner.TryReleaseSetup(() => { }), "failed owner still blocks setup retirement");
        owner.BeginRetirement(); Assert.Equal(2, native.Disposals, "retry reaches the retained owner");
        Assert.True(owner.TryReleaseSetup(() => { }), "setup retires after retry succeeds"); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerParkedBodiesRemainBoundedAndChargedAfterShapesLeave()
    {
        var budget = new GroundStreamingBudget(Limits() with { MaxParked = 1 }); budget.TryOpenOwner(out GroundStreamingOwner? owner);
        owner!.TryAdmit(Plans(2), out GroundBuildLease[] work);
        foreach (GroundBuildLease lease in work) { lease.TryRun(_ => new NativeProbe()); lease.TryAdopt(_ => { }); }
        long priorShape = budget.Diagnostics.NativeShapeBytes;
        Assert.True(work[0].TryPark(_ => { }), "first parked body admitted");
        Assert.Equal(1, budget.Diagnostics.Tiles, "parked body no longer retains a terrain shape");
        Assert.Equal(2, budget.Diagnostics.Bodies, "parked body remains charged alongside resident body");
        Assert.True(budget.Diagnostics.NativeShapeBytes < priorShape && !work[0].Ready, "shape reservation is released only after successful park callback");
        Assert.True(!work[1].TryPark(_ => { }), "parked body ceiling is enforced");
        work[0].Retire(); Assert.Equal(1, budget.Diagnostics.Bodies, "destroying parked owner releases actual body slot");
        Assert.True(work[1].TryPark(_ => { }), "freed parked slot can be reused");
        Close(owner); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerFailedBuildAndPartialPublicationDisposeWithoutReady()
    {
        var budget = new GroundStreamingBudget(Limits()); budget.TryOpenOwner(out GroundStreamingOwner? owner);
        owner!.TryAdmit(Plans(2), out GroundBuildLease[] work);
        work[0].TryRun(_ => throw new InvalidOperationException("injected build failure"));
        Assert.True(work[0].Failure != null && work[0].State == GroundWorkState.Released, "failed build retires its complete reservation");
        var native = new NativeProbe(); work[1].TryRun(_ => native);
        Assert.True(!work[1].TryAdopt(_ => { native.HasBody = true; throw new InvalidOperationException("injected body publication failure"); }),
            "failed partial publication is not ready");
        Assert.True(!native.HasBody && native.Disposals == 1, "the same native owner cleans its partially added body");
        Assert.Equal(0, budget.Diagnostics.Bodies, "both failed records release after cleanup"); Close(owner); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerOwnersShareCapacityAndSetupFailureKeepsItsReservation()
    {
        var budget = new GroundStreamingBudget(Limits() with
        { MaxTiles = 2, MaxOutstanding = 2, MaxCompletionSlots = 2, MaxBodies = 2 });
        Assert.True(budget.TryOpenOwner(out GroundStreamingOwner? a), "first owner epoch reserves setup");
        Assert.True(budget.TryOpenOwner(out GroundStreamingOwner? b), "second owner epoch shares setup capacity");
        Assert.True(a!.TryAdmit(Plans(1), out GroundBuildLease[] first), "first world reserves one shared tile");
        Assert.True(b!.TryAdmit(Plans(1), out GroundBuildLease[] second), "second world reserves the remaining shared tile");
        first[0].TryRun(_ => new NativeProbe()); first[0].TryAdopt(_ => { });
        second[0].TryRun(_ => new NativeProbe()); second[0].TryAdopt(_ => { });
        Assert.True(!b.TryAdmit(Plans(1), out _), "a different world cannot bypass shared output capacity");
        a.BeginRetirement(); Assert.True(!first[0].Ready && second[0].Ready, "retiring A invalidates only A readiness");
        GroundCollisionPlanTests.Reject<InvalidOperationException>(() => a.TryReleaseSetup(() => throw new InvalidOperationException("setup disposal failed")));
        Assert.Equal(2, budget.Diagnostics.Owners, "failed native setup remains charged");
        Assert.True(a.TryReleaseSetup(() => { }), "setup retry succeeds");
        Assert.True(b.TryAdmit(Plans(1), out _), "capacity returns only after A's resources actually retire");
        Close(b); AssertEmpty(budget);
    }

    [Test]
    public static void GroundLedgerPlanningAndAreaRetentionHaveIndependentFiniteAllowances()
    {
        var budget = new GroundStreamingBudget(Limits() with { MaxAreaTickets = 1, MaxPlanning = 1 });
        budget.TryOpenOwner(out GroundStreamingOwner? owner);
        var area = GroundAreaPlan.Around(2, Vector3D.Zero, 1, new GroundAreaLimits { MaxTiles = 6, MaxEstimatedBytes = 4096 });
        Assert.True(owner!.TryRetainArea(area, out GroundAreaLease? held) && !owner.TryRetainArea(area, out _), "area count is bounded independently");
        bool nestedCalled = false;
        Assert.True(owner.TryPrepareAndAdmit(32 * 1024 * 1024, 1, () =>
        {
            Assert.True(!owner.TryPrepareAndAdmit(32 * 1024 * 1024, 1, () => { nestedCalled = true; return Plans(1); }, out _),
                "a reentrant planner cannot bypass its global slot");
            return Plans(1);
        }, out GroundBuildLease[] work), "complete planned result transfers into the admitted queue");
        Assert.True(!nestedCalled && work.Length == 1, "refused planning callback never runs");
        Assert.Equal(0L, budget.Diagnostics.PlanningBytes, "preparation scratch releases only after publication finishes");
        held!.Dispose(); Assert.Equal(0L, budget.Diagnostics.AreaBytes, "area storage releases independently");
        Close(owner); AssertEmpty(budget);
    }

    private static SelectedGroundCollisionPlan[] Plans(int count)
    {
        var fixture = new SelectedGroundCollisionTests.Fixture(); fixture.Build(fixture.Piece(3, new Vector3D(0.4, 0.4, 9)));
        var plans = new SelectedGroundCollisionPlan[count];
        for (int i = 0; i < count; i++) plans[i] = GroundCollisionPlanTests.Prepare(fixture, new object(), new TileKey(4, 2, 2, 2));
        return plans;
    }
    private static GroundStreamingLimits Limits() => new()
    {
        Native = new GroundNativeEstimates { Contract = "fixture-estimates-only", ShapeFixed = 65536, ShapePerVertex = 16,
            ShapePerTriangle = 64, BuildFixed = 131072, BuildPerVertex = 64, BuildPerTriangle = 192, BodyBytes = 2048, SetupBytes = 1024 * 1024 },
        MaxOwners = 2, MaxTiles = 8, MaxOutstanding = 8, MaxExecuting = 2, MaxCompletionSlots = 4,
        MaxBodies = 10, MaxParked = 2, MaxAreaTickets = 4, MaxPlanning = 1,
        MaxPlanBytes = 8 * 1024 * 1024, MaxAreaBytes = 64 * 1024, MaxPlanningBytes = 64 * 1024 * 1024,
        MaxManagedBuildBytes = 128 * 1024 * 1024, MaxNativeBuildBytes = 64 * 1024 * 1024,
        MaxNativeShapeBytes = 64 * 1024 * 1024, MaxNativeBodyBytes = 1024 * 1024,
        MaxNativeSetupBytes = 4 * 1024 * 1024, MaxMetadataBytes = 1024 * 1024,
    };
    private static void Close(GroundStreamingOwner owner)
    { owner.BeginRetirement(); Assert.True(owner.TryReleaseSetup(() => { }), "all native dependencies retired before setup release"); }
    private static void AssertEmpty(GroundStreamingBudget budget) => Assert.Equal(default(GroundStreamingDiagnostics), budget.Diagnostics,
        "every count and byte reservation returns to zero after complete retirement");
    private sealed class NativeProbe : IDisposable
    {
        internal int Disposals;
        internal bool FailNextDispose, HasBody;
        public void Dispose()
        {
            Disposals++;
            if (FailNextDispose) { FailNextDispose = false; throw new InvalidOperationException("injected native dispose failure"); }
            HasBody = false;
        }
    }
}
