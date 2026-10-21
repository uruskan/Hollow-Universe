using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Content;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>Staged lifecycle rules; integration tests for death/support/network follow at the coordinated checkpoint.</summary>
public static class CorpseLifecycleTests
{
    private static CorpseTuning Tuning => new()
    {
        BodySeconds = 150, HarvestHealth = 100,
        Yield = new[] { new ItemAmount(ContentFixture.Item("bone-fragments"), 20), new ItemAmount(ContentFixture.Item("animal-fat"), 10) },
    };

    [Test]
    public static void CorpseLifecycleConvertsOnceAtAuthoritativeTime()
    {
        var life = new CorpseLifecycle(Tuning, 100, 1, new ushort[] { 25, 26 });
        Assert.True(!life.Advance(4599) && life.IsBody, "body persists before server deadline");
        Assert.True(life.Advance(4600) && !life.IsBody, "body converts at the exact shared deadline");
        Assert.True(!life.Advance(4601) && !life.Advance(long.MaxValue), "conversion cannot run twice");
        Assert.Equal((byte)1, life.Body, "appearance retained through conversion");
        Assert.True(life.Wear.SequenceEqual(new ushort[] { 25, 26 }), "clothing appearance retained");
    }

    [Test]
    public static void CorpseLifecycleToolSpeedCannotDuplicateYieldOrHarvestABag()
    {
        foreach (float rate in new[] { 4f, 15f, 31f, 10000f })
        {
            CorpseTuning tuning = Tuning;
            var life = new CorpseLifecycle(tuning, 0, 0);
            var all = new List<ItemAmount>();
            for (int n = 0; n < 100 && life.HarvestLeft > 0; n++)
                Assert.True(life.Harvest(rate, all), "valid harvest hit");
            foreach (ItemAmount item in tuning.Yield)
                Assert.Equal(item.Count, all.Where(a => a.Item == item.Item).Sum(a => a.Count), "same total with every tool rate");
            Assert.True(life.Advance(1), "depletion converts immediately without waiting for timer");
            int count = all.Count;
            Assert.True(!life.Harvest(rate, all) && all.Count == count, "bag cannot be harvested again");
        }
        var untouched = new CorpseLifecycle(Tuning, 0, 0);
        var none = new List<ItemAmount>();
        Assert.True(!untouched.Harvest(float.NaN, none) && !untouched.Harvest(-1, none), "invalid rate cannot change progress");
        Assert.Near(100, untouched.HarvestLeft, 0, "invalid hit preserves harvest");
    }

    [Test]
    public static void CorpseLifecycleRestoreKeepsRemainingTimeAndPartialYield()
    {
        CorpseTuning tuning = Tuning;
        var before = new CorpseLifecycle(tuning, 100, 1);
        var all = new List<ItemAmount>();
        before.Harvest(33, all);
        long remaining = before.RemainingBodyTicks(1300);
        var after = new CorpseLifecycle(tuning, 0, 1);
        after.Restore(before.Stage, remaining, before.HarvestLeft, 20);
        Assert.True(!after.Advance(20 + remaining - 1), "restored body does not age while server is stopped");
        after.Harvest(1000, all);
        foreach (ItemAmount item in tuning.Yield)
            Assert.Equal(item.Count, all.Where(a => a.Item == item.Item).Sum(a => a.Count), "restart cannot renew harvested resources");
        Assert.True(after.Advance(21), "restored partial body can finish harvesting");
        var bag = new CorpseLifecycle(tuning, 0, 0);
        bag.Restore(CorpseStage.Bag, tuning.BodyTicks, tuning.HarvestHealth, 0);
        Assert.True(!bag.IsBody && bag.HarvestLeft == 0 && !bag.Harvest(20, all), "saved bag ignores a forged remaining harvest");
    }
}
