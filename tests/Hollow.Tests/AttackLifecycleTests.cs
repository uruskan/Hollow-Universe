using System;
using System.Diagnostics;
using Hollow.Client;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;

namespace Hollow.Tests;

public static class AttackLifecycleTests
{
    [Test]
    public static void ReconnectStartsANewIdentitySpaceWithoutAcceptingOldDecisions()
    {
        var ledger = new AttackLedger();
        ledger.BeginSession(1);
        AttackId old = ledger.Id(AttackDomain.Firearm, 1);
        ledger.Predict(old, 10);
        ledger.TryPresent(old);
        ledger.BeginSession(2);
        AttackId fresh = ledger.Id(AttackDomain.Firearm, 1);
        Assert.True(ledger.Predict(fresh, 100), "a new connection may restart its sequence");
        ledger.Resolve(new AttackResolution(old, OutcomeCode.Done, 10, 10));
        Assert.Equal(1, ledger.PendingCount, "an old rocket's decision cannot confirm a fresh shot");
        Assert.True(!ledger.TryGet(old, out _) && !ledger.CanPredict(old), "old connection history stays outside the active ledger");
        ledger.Resolve(new AttackResolution(fresh, OutcomeCode.Done, 100, 100));
        Assert.Equal(0, ledger.PendingCount, "new connection resolves its own attempt");
    }

    [Test]
    public static void OnePresentationPerAttemptAcrossReorderedDecisionsAndDomains()
    {
        var ledger = new AttackLedger();
        foreach (AttackDomain kind in new[] { AttackDomain.Melee, AttackDomain.Firearm, AttackDomain.Station })
        {
            var id = new AttackId(kind, 1);
            Assert.True(ledger.Predict(id, 100), "same sequence in a different domain remains distinct");
            Assert.True(ledger.TryPresent(id), "prediction plays immediately");
            Assert.True(!ledger.TryPresent(id), "repeat prediction never plays twice");
            var accepted = new AttackResolution(id, OutcomeCode.Done, 100, 102, 110, 126, 1, 0);
            ledger.Resolve(accepted);
            ledger.Resolve(accepted);
            ledger.Resolve(accepted with { Code = OutcomeCode.Stale });
            Assert.True(ledger.TryGet(id, out var entry) && entry.Resolution?.Code == OutcomeCode.Done,
                "late stale response cannot undo accepted authority");
            Assert.True(!ledger.TryPresent(id), "confirmation does not replay effects");
        }
        Assert.Equal(0, ledger.PendingCount, "all decisions settled");
    }

    [Test]
    public static void CancelledAttemptsCannotBeResurrectedAndOutstandingWorkIsBounded()
    {
        var ledger = new AttackLedger();
        var id = new AttackId(AttackDomain.Melee, 1);
        ledger.Predict(id, 1);
        ledger.Resolve(new AttackResolution(id, OutcomeCode.Stale, 1) { Cancelled = true });
        ledger.Resolve(new AttackResolution(id, OutcomeCode.Done, 1, 2, 10, 26));
        Assert.True(ledger.TryGet(id, out var cancelled) && cancelled.Resolution?.Cancelled == true,
            "reordered accepted result cannot revive cancelled attempt");
        for (uint seq = 2; seq <= AttackLedger.PendingLimit + 1; seq++)
            Assert.True(ledger.Predict(new AttackId(AttackDomain.Melee, seq), seq), "bounded pending slot available");
        Assert.True(!ledger.Predict(new AttackId(AttackDomain.Melee, 1000), 1000), "lost connection cannot grow pending state forever");
        ledger.Clear();
        Assert.Equal(0, ledger.Count, "new session releases history");
    }

    [Test]
    public static void EvictedIdentitiesRemainOldAndReorderingInsideHistoryRemainsValid()
    {
        var journal = new AttackJournal();
        var ledger = new AttackLedger();
        for (uint seq = 1; seq <= AttackLedger.HistoryLimit + 10; seq++)
        {
            var result = new AttackResolution(new AttackId(AttackDomain.Station, seq), OutcomeCode.Done, seq, seq);
            journal.Remember(result);
            ledger.Resolve(result);
        }
        var old = new AttackId(AttackDomain.Station, 1);
        Assert.True(journal.IsExpired(old), "an evicted accepted request cannot fire again");
        Assert.True(!ledger.Predict(old, 9999), "an evicted prediction cannot play again");
        ledger.Resolve(new AttackResolution(old, OutcomeCode.Done, 1, 1));
        Assert.True(!ledger.TryGet(old, out _), "late old confirmation stays forgotten");
        Assert.Equal(AttackJournal.Capacity, journal.Count, "bounded server state");
        Assert.Equal(AttackLedger.HistoryLimit, ledger.Count, "bounded client state");
        var fresh = new AttackJournal();
        fresh.Remember(new AttackResolution(new AttackId(AttackDomain.Melee, 3), OutcomeCode.Done, 3));
        Assert.True(!fresh.IsExpired(new AttackId(AttackDomain.Melee, 2)), "out-of-order receipt is not a high-water rejection");
    }

    [Test]
    public static void LifecycleCostAndWireSizeAreMeasured()
    {
        var ledger = new AttackLedger();
        var journal = new AttackJournal();
        for (uint i = 1; i <= 5000; i++)
        {
            var id = new AttackId(AttackDomain.Firearm, i);
            ledger.Predict(id, i); ledger.TryPresent(id);
            var result = new AttackResolution(id, OutcomeCode.Done, i, i);
            ledger.Resolve(result); journal.Remember(result);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        const uint samples = 100_000;
        for (uint i = 5001; i <= samples + 5000; i++)
        {
            var id = new AttackId(AttackDomain.Firearm, i);
            ledger.Predict(id, i); ledger.TryPresent(id);
            var result = new AttackResolution(id, OutcomeCode.Done, i, i);
            ledger.Resolve(result); journal.Remember(result);
        }
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var writer = new BitWriter(128);
        new AttackIntent { Kind = AttackDomain.Station, Seq = 10000, Tick = 100000, Ship = 42, Station = 1 }.WriteMessage(writer);
        int intentBytes = writer.ToArray().Length;
        writer.Reset();
        new AttackResult { Kind = AttackDomain.Station, Seq = 10000, RequestedTick = 100000, StartTick = 100001,
            StateSequence = 5000, Barrel = 1 }.WriteMessage(writer);
        int resultBytes = writer.ToArray().Length;
        Fixtures.Report($"attack lifecycle: {watch.Elapsed.TotalMilliseconds * 1000 / samples:0.000} us/attempt; " +
            $"{allocated / (double)samples:0.0} allocated bytes/attempt; request {intentBytes} B, result {resultBytes} B " +
            "before transport/acks/retransmission; client/server histories bounded");
        Assert.True(ledger.Count <= AttackLedger.HistoryLimit && journal.Count <= AttackJournal.Capacity && ledger.PendingCount == 0,
            "sustained fire has bounded storage and no accumulating pending work");
    }
}
