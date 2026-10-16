using System;

namespace Hollow.Client.Rendering;

/// <summary>A presentation-only shadow candidate. Distance and influence radius are measured from the camera.</summary>
public readonly record struct LocalShadowCandidate(ulong Id, float Distance, float Range, int Priority = 0, bool Selected = false);

/// <summary>
/// Chooses at most eight local shadow lights without allocations or sorting the whole world. Priority wins, followed
/// by proximity; a ten-percent distance advantage for incumbents prevents nearly equal lights swapping every frame.
/// Lighting remains independent of this budget. The renderer supplies only visible, active lights as candidates.
/// </summary>
public static class LocalShadowSelector
{
    public const int Capacity = 8;

    public static int Select(ReadOnlySpan<LocalShadowCandidate> candidates, Span<ulong> selected, int maximum, float distance)
    {
        int limit = Math.Clamp(maximum, 0, Math.Min(Capacity, selected.Length));
        if (limit == 0 || !float.IsFinite(distance) || distance < 0) return 0;
        Span<int> choices = stackalloc int[Capacity];
        int count = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            LocalShadowCandidate candidate = candidates[i];
            if (!float.IsFinite(candidate.Distance) || candidate.Distance < 0 ||
                !float.IsFinite(candidate.Range) || candidate.Range <= 0 ||
                candidate.Distance > distance + candidate.Range) continue;
            int at = 0;
            while (at < count && !Before(candidate, candidates[choices[at]])) at++;
            if (at >= limit) continue;
            int end = Math.Min(count, limit - 1);
            for (int move = end; move > at; move--) choices[move] = choices[move - 1];
            choices[at] = i;
            count = Math.Min(count + 1, limit);
        }
        for (int i = 0; i < count; i++) selected[i] = candidates[choices[i]].Id;
        return count;
    }

    private static bool Before(LocalShadowCandidate a, LocalShadowCandidate b)
    {
        if (a.Priority != b.Priority) return a.Priority > b.Priority;
        float distanceA = a.Distance * (a.Selected ? 0.9f : 1), distanceB = b.Distance * (b.Selected ? 0.9f : 1);
        return distanceA < distanceB || distanceA == distanceB && a.Id < b.Id;
    }
}
