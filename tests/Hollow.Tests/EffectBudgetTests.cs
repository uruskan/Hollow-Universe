using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.Client.Effects;

namespace Hollow.Tests;

public static class EffectBudgetTests
{
    [Test]
    public static void MixedParticleSourcesCannotOvershootOrResetTheSameFrame()
    {
        var budget = new EffectParticleBudget { Limit = 17 };
        Assert.True(budget.BeginFrame(10), "first renderer frame starts");
        Assert.Equal(12, budget.Take(12), "immediate burst");
        Assert.True(!budget.BeginFrame(10), "a later director update cannot reset the same renderer frame");
        Assert.Equal(5, budget.Take(100), "delayed layer takes only the remainder");
        Assert.Equal(0, budget.Take(1), "continuous emission shares the exhausted ceiling");
        Assert.Equal(17, budget.Used, "exact global ceiling");
        Assert.True(budget.BeginFrame(11), "next frame recovers");
        Assert.Equal(0, budget.Take(-10), "negative requests do not create budget credit");
        Assert.Equal(17, budget.Take(int.MaxValue), "large catch-up is bounded");
        budget.Limit = 0;
        budget.BeginFrame(12);
        Assert.Equal(0, budget.Take(1), "zero budget suppresses emissions");
    }

    [Test]
    public static void LoopSelectionRespectsIndependentResourcesAndPerEffectCaps()
    {
        var settings = new EffectBudgetSettings { ActiveLoops = 4, LoopFlames = 2, LoopLights = 1 };
        var candidates = new List<EffectLoopCandidate>();
        for (int i = 1; i <= 30; i++) candidates.Add(new(i, "torch", 8, 1, 0, true, true, false, double.NegativeInfinity));
        for (int i = 31; i <= 60; i++) candidates.Add(new(i, "trail", 2, 2, 0, false, false, false, double.NegativeInfinity));
        candidates.Add(new(61, "flame", 8, 3, 0, true, false, false, double.NegativeInfinity));
        var selector = new EffectLoopBudget();
        selector.Select(candidates, settings, 0);
        Assert.Equal(4, selector.Selected.Count, "other kinds can fill unused loop slots");
        Assert.Equal(1, selector.Selected.Count(id => id <= 30), "light ceiling");
        Assert.Equal(2, selector.Selected.Count(id => id is >= 31 and <= 60), "per-effect trail cap");
        Assert.True(selector.Selected.Contains(61), "separate flame pool can still be used");
        selector.Select(candidates, settings with { ActiveLoops = 0 }, 1);
        Assert.Equal(0, selector.Selected.Count, "global zero disables all active resources");
    }

    [Test]
    public static void VirtualLoopsRecoverAndEqualDistanceContendersDoNotStarve()
    {
        var settings = new EffectBudgetSettings { ActiveLoops = 1, LeaseSeconds = 2 };
        var selector = new EffectLoopBudget();
        var first = new EffectLoopCandidate(1, "fire", 1, 5, 0, false, false, false, double.NegativeInfinity);
        var second = first with { Id = 2 };
        selector.Select(new[] { first, second }, settings, 0);
        Assert.True(selector.Selected.Contains(1), "stable initial tie");
        first = first with { Active = true, ActivatedAt = 0 };
        selector.Select(new[] { first, second }, settings, 1);
        Assert.True(selector.Selected.Contains(1), "lease prevents frame-by-frame resource churn");
        selector.Select(new[] { first, second }, settings, 2.1);
        Assert.True(selector.Selected.Contains(2), "waiting effect obtains the expired lease");
        second = second with { Active = true, ActivatedAt = 2.1 };
        first = first with { Active = false };
        selector.Select(new[] { first, second }, settings, 4.2);
        Assert.True(selector.Selected.Contains(1), "virtual former owner recovers without a new Start call");
        selector.Select(new[] { first with { Distance = 100 }, second with { Distance = 1 } }, settings, 5);
        Assert.True(selector.Selected.Contains(2), "near loop wins after camera/source movement");
        selector.Select(new[] { first with { Priority = 10, Distance = 0 }, second }, settings, 5);
        Assert.True(selector.Selected.Contains(1), "first-person loop precedes ordinary world effects");
        selector.Select(Array.Empty<EffectLoopCandidate>(), settings, 6);
        Assert.Equal(0, selector.Selected.Count, "retirement clears selection state");
    }

    [Test]
    public static void BudgetDataHasFiniteBoundedCeilings()
    {
        EffectBudgetSettings data = EffectBudgetSettings.Read(File.ReadAllText(Path.Combine(Fixtures.Root, "game", "data", "ground", "fx-budgets.json")));
        Assert.True(data.ParticlesPerFrame > 0 && data.ActiveLoops > 0 && data.LoopLights <= data.ActiveLoops && data.LoopFlames <= data.ActiveLoops,
            "shipped budget enables a bounded, consistent presentation");
        EffectBudgetSettings malformed = EffectBudgetSettings.Read("{\"particlesPerFrame\":-1,\"activeLoops\":2147483647,\"loopLights\":-2,\"distanceBand\":0,\"leaseSeconds\":-10}");
        Assert.Equal(0, malformed.ParticlesPerFrame, "negative ceiling clamps to zero");
        Assert.Equal(512, malformed.ActiveLoops, "allocation hard ceiling");
        Assert.Equal(0, malformed.LoopLights, "disabled lights");
        Assert.True(malformed.DistanceBand > 0 && malformed.LeaseSeconds > 0, "no zero division or unbounded rotation cadence");
    }
}
