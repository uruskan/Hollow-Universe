using System;
using Hollow.Client.Rendering;

namespace Hollow.Tests;

public static class LocalShadowTests
{
    [Test]
    public static void ShadowBudgetHonoursPriorityCapacityAndInfluenceDistance()
    {
        LocalShadowCandidate[] lights =
        {
            new(1, 3, 8), new(2, 9, 8), new(3, 30, 10, Priority: 1),
            new(4, 80, 8, Priority: 100), // Out of reach cannot steal a slot even at a higher priority.
            new(5, 45, 10), // The influence reaches within the camera's 40m shadow distance.
        };
        Span<ulong> selected = stackalloc ulong[8];
        int count = LocalShadowSelector.Select(lights, selected, 2, 40);
        Assert.Equal(2, count, "only two shadow lights are allowed");
        Assert.Equal(3UL, selected[0], "priority chooses the relevant light first");
        Assert.Equal(1UL, selected[1], "the nearest light takes the remaining slot");
        count = LocalShadowSelector.Select(lights, selected, 8, 40);
        Assert.Equal(4, count, "only lights whose influence is within range qualify");
        Assert.Equal(0, LocalShadowSelector.Select(lights, selected, 0, 40), "zero disables shadows without touching lights");
    }

    [Test]
    public static void NearbyShadowLightsStayStableUntilAnotherIsMeaningfullyCloser()
    {
        Span<ulong> selected = stackalloc ulong[1];
        LocalShadowCandidate[] lights = { new(1, 10, 8, Selected: true), new(2, 9.5f, 8) };
        LocalShadowSelector.Select(lights, selected, 1, 40);
        Assert.Equal(1UL, selected[0], "nearly equal distances keep the current shadow map");
        lights[1] = lights[1] with { Distance = 8.5f };
        LocalShadowSelector.Select(lights, selected, 1, 40);
        Assert.Equal(2UL, selected[0], "a meaningfully closer light takes the slot");
        lights[1] = lights[1] with { Distance = 12, Priority = 1 };
        LocalShadowSelector.Select(lights, selected, 1, 40);
        Assert.Equal(2UL, selected[0], "priority overrides the incumbent's small stability advantage");
    }

    [Test]
    public static void ShadowBudgetHasStableTiesAndRejectsInvalidCandidates()
    {
        LocalShadowCandidate[] lights =
        {
            new(4, 10, 4), new(2, 10, 4), new(3, float.NaN, 4), new(1, 10, -1), new(5, -1, 4),
        };
        Span<ulong> selected = stackalloc ulong[8];
        Assert.Equal(2, LocalShadowSelector.Select(lights, selected, 8, 40), "invalid positions and influence radii are excluded");
        Assert.Equal(2UL, selected[0], "equal lights use stable identity, independent of registration order");
        Array.Reverse(lights);
        LocalShadowSelector.Select(lights, selected, 8, 40);
        Assert.Equal(2UL, selected[0], "reverse registration order selects the same winner");
        Assert.Equal(0, LocalShadowSelector.Select(lights, selected, 8, float.NaN), "invalid distance disables selection");
    }

    [Test]
    public static void ShadowBudgetStaysBoundedWithManyLightsAndDropsDistantIncumbents()
    {
        var lights = new LocalShadowCandidate[1000];
        for (int i = 0; i < lights.Length; i++) lights[i] = new((ulong)(i + 1), i + 1, 8);
        Span<ulong> selected = stackalloc ulong[8];
        Assert.Equal(8, LocalShadowSelector.Select(lights, selected, 1000, 40), "even an invalid oversized setting is bounded to eight");
        Assert.Equal(8UL, selected[7], "the eight nearest lights are selected from the crowd");
        lights[0] = lights[0] with { Distance = 100, Selected = true };
        LocalShadowSelector.Select(lights, selected, 1, 40);
        Assert.Equal(2UL, selected[0], "hysteresis never keeps a shadow outside the permitted range");
    }
}
