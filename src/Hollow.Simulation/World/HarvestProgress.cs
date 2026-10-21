using System;
using System.Collections.Generic;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.World;

/// <summary>Proportional corpse gathering: tool speed changes the number of hits, never the total yield.</summary>
public static class HarvestProgress
{
    public static bool Take(IReadOnlyList<ItemAmount> yield, float maximum, ref float left, float rate, List<ItemAmount> gained)
    {
        if (!float.IsFinite(rate) || rate <= 0 || !float.IsFinite(left) || left <= 0 || maximum <= 0) return false;
        float before = Math.Clamp(left, 0, maximum);
        float after = Math.Max(0, before - rate);
        foreach (ItemAmount item in yield)
        {
            int count = Gathered(item.Count, maximum, after) - Gathered(item.Count, maximum, before);
            if (count > 0) gained.Add(new ItemAmount(item.Item, count));
        }
        left = after;
        return true;
    }

    private static int Gathered(int total, float maximum, float left) =>
        left <= 0 ? total : (int)MathF.Floor(total * (1 - left / maximum) + 1e-4f);
}
